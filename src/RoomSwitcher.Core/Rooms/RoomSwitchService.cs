using RoomSwitcher.Core.Audio;
using RoomSwitcher.Core.Devices;
using RoomSwitcher.Core.Display;
using RoomSwitcher.Core.Layouts;
using RoomSwitcher.Core.Model;

namespace RoomSwitcher.Core.Rooms;

/// <param name="Warning">The switch worked, but something (sound, an elevated window) needs the person's attention.</param>
public sealed record SwitchResult(bool Ok, string Message, int WindowsMoved = 0, int WindowsRefused = 0, bool Warning = false)
{
    public static SwitchResult Fail(string message) => new(false, message);
}

/// <summary>
/// Switching from one room to another:
/// save the windows of the room being left, apply the new room's displays (rolling back on
/// failure), wait for Windows to settle, then put the new room's windows back.
/// </summary>
public sealed class RoomSwitchService
{
    private readonly IDisplayBackend _display;
    private readonly IWindowBackend _windows;
    private readonly IDisplaySettle _settle;
    private readonly Action<LayoutsFile> _saveLayouts;
    private readonly IAudioBackend? _audio;
    private readonly IDeviceBackend? _devices;
    private readonly Func<int, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppConfig Config { get; set; }
    public LayoutsFile Layouts { get; set; }

    public RoomSwitchService(AppConfig config, LayoutsFile layouts, IDisplayBackend display,
        IWindowBackend windows, IDisplaySettle settle, Action<LayoutsFile> saveLayouts,
        IAudioBackend? audio = null, Func<int, CancellationToken, Task>? delay = null, IDeviceBackend? devices = null)
    {
        _audio = audio;
        _devices = devices;
        _delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
        Config = config;
        Layouts = layouts;
        _display = display;
        _windows = windows;
        _settle = settle;
        _saveLayouts = saveLayouts;
    }

    public bool Busy => _gate.CurrentCount == 0;

    public Room? DetectCurrentRoom() => RoomMatcher.DetectCurrent(Config, _display.Query(), Layouts.LastRoomId);

    /// <summary>Checks a room against the hardware without touching the screens.</summary>
    public PlanResult Check(Room room, bool askDriver = true) => Check(Config, room, _display.Query(), askDriver ? _display : null);

    /// <summary>
    /// The planner's checks, then the driver's. If the driver turns down the whole room, fewer displays are
    /// tried (no duplicates, then the main screen alone), so the room still switches while one display works.
    /// </summary>
    public static PlanResult Check(AppConfig config, Room room, DisplayState state, IDisplayBackend? driver)
    {
        var result = RoomPlanner.Build(config, room, state);
        if (!result.Ok || driver == null) return result;
        var plan = result.Plan!;
        var check = driver.Validate(plan);
        if (check.Ok) return result;

        foreach (var smaller in Smaller(plan))
        {
            if (!driver.Validate(smaller).Ok) continue;
            var resolution = MonitorResolver.Resolve(config, state);
            var off = plan.DevicePaths.Except(smaller.DevicePaths, StringComparer.OrdinalIgnoreCase).ToList();
            var names = off.Select(p => resolution.MonitorIdFor(p) is { } id ? config.FindMonitor(id)?.Name : null).Select(n => n ?? "a display").ToList();
            var problem = new RoomProblem(ProblemSeverity.Warning,
                $"Windows couldn't turn on all of this room's displays together, so {RoomPlanner.JoinNames(names)} {(names.Count == 1 ? "stays" : "stay")} off. {check.Message}",
                resolution.MonitorIdFor(off[0]));
            return new PlanResult(smaller, result.Problems.Append(problem).ToList());
        }
        return result with { Problems = result.Problems.Append(new RoomProblem(ProblemSeverity.Error, check.Message)).ToList() };
    }

    private static IEnumerable<RoomPlan> Smaller(RoomPlan plan)
    {
        if (plan.Screens.Any(s => s.DevicePaths.Count > 1))
            yield return plan with { Screens = plan.Screens.Select(s => s with { DevicePaths = [s.DevicePaths[0]] }).ToList() };
        if (plan.Screens.Count > 1)
            yield return plan with { Screens = [plan.Screens[0] with { DevicePaths = [plan.Screens[0].DevicePaths[0]] }] };
    }

    /// <summary>Displays the switch leaves off, in a sentence each, to go with the result.</summary>
    private static List<string> LeftOff(PlanResult result) =>
        result.Problems.Where(p => p.Severity == ProblemSeverity.Warning && p.MonitorId != null).Select(p => p.Message).ToList();

    public async Task<SwitchResult> SwitchToAsync(Room target, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = _display.Query();
            var planResult = Check(Config, target, state, _display);
            if (!planResult.Ok)
                return SwitchResult.Fail($"Can't switch to {target.Name}. {planResult.FirstError ?? "This room has no displays turned on."}");
            var plan = planResult.Plan!;
            var leftOff = LeftOff(planResult);

            var leaving = RoomMatcher.DetectCurrent(Config, state, Layouts.LastRoomId);
            if (leaving?.Id == target.Id && planResult.Problems.All(p => p.MonitorId != null) && SameActiveMonitors(state, plan))
            {
                // The screens are right; still put the room's sound back, in case it was changed.
                var extras = leftOff.Concat(await ApplyAudioAsync(target, cancellationToken)).Concat(await ApplyDevicesAsync(target, cancellationToken)).ToList();
                return new SwitchResult(true, WithProblems($"{target.Name} is already on.", extras), Warning: extras.Count > 0);
            }

            if (leaving != null && Config.Settings.RestoreWindows)
                SaveWindowsFor(leaving);

            var restorePoint = _display.CaptureRestorePoint();
            var applied = _display.Apply(plan);
            if (!applied.Ok)
                return SwitchResult.Fail($"Couldn't switch to {target.Name}. {applied.Message} {RollBack(restorePoint)}");

            await _settle.WaitAsync(Config.Settings.SettleDelayMs, cancellationToken);

            var after = _display.Query();
            if (after.Screens.Count == 0)
                return SwitchResult.Fail($"Switching to {target.Name} left no displays on. {RollBack(restorePoint)}");

            Layouts.LastRoomId = target.Id;
            SafeSaveLayouts();

            var message = SameActiveMonitors(after, plan)
                ? $"Switched to {target.Name}."
                : $"Switched to {target.Name}, but Windows didn't turn on exactly the displays this room uses.";

            int moved = 0, refused = 0;
            if (Config.Settings.RestoreWindows && Layouts.Rooms.TryGetValue(target.Id, out var layout))
            {
                (moved, refused) = RestoreWindows(layout);
                if (refused > 0)
                    message += $" {refused} window{(refused == 1 ? "" : "s")} couldn't be moved back (programs running as administrator need Room Switcher to run as administrator too).";
            }

            // Sound last: a TV's HDMI sound device only appears once its screen is on.
            var sound = await ApplyAudioAsync(target, cancellationToken);
            var devices = await ApplyDevicesAsync(target, cancellationToken);
            message = WithProblems(message, leftOff.Concat(sound).Concat(devices).ToList());
            return new SwitchResult(true, message, moved, refused, Warning: leftOff.Count > 0 || sound.Count > 0 || devices.Count > 0 || refused > 0);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Recovery: turn every connected monitor on, extended.</summary>
    public async Task<SwitchResult> ShowAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var leaving = RoomMatcher.DetectCurrent(Config, _display.Query(), Layouts.LastRoomId);
            if (leaving != null && Config.Settings.RestoreWindows) SaveWindowsFor(leaving);

            var result = _display.ShowAll();
            if (!result.Ok) return SwitchResult.Fail($"Couldn't turn on all displays. {result.Message}");
            await _settle.WaitAsync(Config.Settings.SettleDelayMs, cancellationToken);
            Layouts.LastRoomId = null;
            SafeSaveLayouts();
            return new SwitchResult(true, "All displays are on.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void SaveWindowsFor(Room room)
    {
        Layouts.Rooms[room.Id] = new RoomLayout
        {
            SavedAt = DateTimeOffset.Now,
            Windows = _windows.Enumerate().ToList(),
        };
        SafeSaveLayouts();
    }

    /// <summary>
    /// Puts saved windows back. Runs a second pass for windows that didn't land exactly, because a
    /// window crossing to a monitor with different scaling can resize itself after the first move.
    /// </summary>
    public (int Moved, int Refused) RestoreWindows(RoomLayout layout)
    {
        var moved = 0;
        var refused = new HashSet<long>();
        for (var pass = 0; pass < 2; pass++)
        {
            var matches = WindowMatcher.MatchAll(layout.Windows, _windows.Enumerate());
            var anyChange = false;
            foreach (var m in matches)
            {
                if (WindowMatcher.SamePlacement(m.Saved, m.Live)) continue;
                if (refused.Contains(m.Live.Hwnd)) continue;
                anyChange = true;
                if (_windows.Apply(m.Live, m.Saved))
                {
                    if (pass == 0) moved++;
                }
                else
                {
                    refused.Add(m.Live.Hwnd);
                }
            }
            if (!anyChange) break;
        }
        return (moved, refused.Count);
    }

    private async Task<IReadOnlyList<string>> ApplyAudioAsync(Room room, CancellationToken cancellationToken)
    {
        if (_audio == null || (room.AudioOutput == null && room.AudioInput == null)) return [];
        try
        {
            RememberWindowsSound(_audio);
            return await AudioRules.ApplyAsync(_audio, room, _delay, cancellationToken: cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return [$"Couldn't change the speakers or microphone. {e.Message}"];
        }
    }

    /// <summary>The first time a room changes the sound devices, notes the ones Windows had, so Reset can put them back.</summary>
    private void RememberWindowsSound(IAudioBackend audio)
    {
        if (Layouts.SoundBefore != null) return;
        AudioDeviceRef? Default(AudioFlow flow) => audio.List(flow).FirstOrDefault(d => d.IsDefault) is { } d ? AudioRules.ToRef(d) : null;
        Layouts.SoundBefore = new SoundDefaults { Output = Default(AudioFlow.Output), Input = Default(AudioFlow.Input) };
        SafeSaveLayouts();
    }

    /// <summary>Turns the room's USB devices on and off. Returns plain-language problems and safety notes.</summary>
    private async Task<IReadOnlyList<string>> ApplyDevicesAsync(Room room, CancellationToken cancellationToken)
    {
        if (_devices == null || !Config.UsbDevices.Any(d => d.Managed)) return [];
        try
        {
            var plan = UsbRules.Plan(Config, room, _devices.List());
            if (plan.Empty) return plan.Warnings;
            if (_devices.Status == DeviceControlStatus.NotSetUp)
                return plan.Warnings.Append("Devices weren't changed because device switching isn't set up yet. Set it up on the Devices step of Edit rooms…").ToList();
            var problems = await _devices.ApplyAsync(plan.Enable, plan.Disable, cancellationToken);
            return plan.Warnings.Concat(problems).ToList();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return [$"Couldn't turn devices on or off. {e.Message}"];
        }
    }

    private static string WithProblems(string message, IReadOnlyList<string> problems) =>
        problems.Count == 0 ? message : $"{message} {string.Join(" ", problems)}";

    private string RollBack(IDisplayRestorePoint restorePoint)
    {
        var restored = _display.Restore(restorePoint);
        if (restored.Ok && _display.Query().Screens.Count > 0)
            return "Your previous display settings have been restored.";
        var all = _display.ShowAll();
        return all.Ok
            ? "Your previous display settings couldn't be restored, so all displays have been turned on."
            : "Your previous display settings couldn't be restored. Press Windows logo key + P to choose a display mode.";
    }

    private static bool SameActiveMonitors(DisplayState state, RoomPlan plan)
    {
        var active = state.Screens.SelectMany(s => s.DevicePaths).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return active.SetEquals(plan.DevicePaths);
    }

    private void SafeSaveLayouts()
    {
        try { _saveLayouts(Layouts); }
        catch (IOException) { /* a locked file must not break switching; the next save retries */ }
        catch (UnauthorizedAccessException) { }
    }
}
