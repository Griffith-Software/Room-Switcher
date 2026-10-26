using RoomSwitcher.Core.Display;
using RoomSwitcher.Windows.Native;
using static RoomSwitcher.Windows.Native.DisplayConfigApi;

namespace RoomSwitcher.Windows.Display;

/// <summary>
/// Turns monitors on and off and arranges them with SetDisplayConfig. Monitors left out of the
/// path set are detached, so they show "No signal" and go to sleep.
/// </summary>
public sealed class WindowsDisplayBackend : IDisplayBackend
{
    private sealed record Target(LUID Adapter, uint Id, string DevicePath, string FriendlyName, uint OutputTechnology);

    private sealed record RestorePoint(CcdConfig Config) : IDisplayRestorePoint;

    /// <summary>One way of asking Windows for the plan. Several are tried, most exact first.</summary>
    private sealed record Attempt(string Name, DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[]? Modes, bool TopologyOnly,
        IReadOnlyList<(PlannedScreen Screen, LUID Adapter, uint SourceId)> Sources)
    {
        /// <summary>False: Windows must use the modes exactly as given. True: it may adjust them to make the set work.</summary>
        public bool AllowChanges { get; init; } = true;
    }

    /// <summary>Last attempt that worked or the last error, for the proof-of-concept tool and logs.</summary>
    public string LastDetail { get; private set; } = "";

    public DisplayState Query()
    {
        var all = CcdConfig.All();
        var active = CcdConfig.Active();
        var targets = Targets(all);
        var byKey = targets.ToDictionary(t => (t.Adapter, t.Id));

        var activeTargets = active.Paths
            .Where(p => (p.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0)
            .Select(p => (p.targetInfo.adapterId, p.targetInfo.id))
            .ToHashSet();

        var monitors = targets
            .Select(t => new ConnectedMonitor(t.DevicePath, t.FriendlyName, DeviceInfo.Connector(t.OutputTechnology),
                t.Adapter.ToString(), activeTargets.Contains((t.Adapter, t.Id))))
            .ToList();

        var adapters = all.Paths
            .GroupBy(p => p.sourceInfo.adapterId)
            .Select(g => new AdapterInfo(g.Key.ToString(), g.Select(p => p.sourceInfo.id).Distinct().Count()))
            .ToList();

        var screens = new List<ActiveScreen>();
        foreach (var group in active.Paths
                     .Where(p => (p.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0)
                     .GroupBy(p => (p.sourceInfo.adapterId, p.sourceInfo.id)))
        {
            var first = group.First();
            var source = active.SourceMode(first);
            if (source == null) continue;
            var refresh = DeviceInfo.ToMillihz(first.targetInfo.refreshRate);
            if (refresh == 0 && active.TargetMode(first) is { } tm) refresh = DeviceInfo.ToMillihz(tm.vSyncFreq);

            var paths = group
                .Select(p => byKey.TryGetValue((p.targetInfo.adapterId, p.targetInfo.id), out var t) ? t.DevicePath : null)
                .Where(p => p != null)
                .Select(p => p!)
                .ToList();
            if (paths.Count == 0) continue;

            var s = source.Value;
            screens.Add(new ActiveScreen(s.position.x, s.position.y, (int)s.width, (int)s.height, (int)refresh,
                RotationFromApi(first.targetInfo.rotation), DeviceInfo.ScalePercent(group.Key.adapterId, group.Key.id), paths));
        }

        return new DisplayState(monitors, adapters, screens);
    }

    public DisplayResult Validate(RoomPlan plan)
    {
        List<Attempt> attempts;
        try { attempts = Attempts(plan); }
        catch (PlanException e) { return DisplayResult.Fail(e.Message); }

        var lastError = 0;
        foreach (var attempt in attempts)
        {
            lastError = Run(attempt, SDC_VALIDATE);
            if (lastError == ERROR_SUCCESS)
            {
                LastDetail = $"validated: {attempt.Name}";
                return DisplayResult.Success;
            }
        }
        LastDetail = $"validate failed: error {lastError}";
        return DisplayResult.Fail(Explain(plan, lastError));
    }

    public DisplayResult Apply(RoomPlan plan)
    {
        List<Attempt> attempts;
        try { attempts = Attempts(plan); }
        catch (PlanException e) { return DisplayResult.Fail(e.Message); }

        var lastError = 0;
        // A rejected SetDisplayConfig changes nothing, so each attempt can go straight to apply.
        foreach (var attempt in attempts)
        {
            lastError = Run(attempt, SDC_APPLY | SDC_SAVE_TO_DATABASE);
            if (lastError == ERROR_SUCCESS)
            {
                LastDetail = $"applied: {attempt.Name}";
                ApplyScaling(attempt);
                return DisplayResult.Success;
            }
        }

        // Some drivers reject partial path sets outright. Fall back to the older per-monitor API.
        if (lastError != ERROR_ACCESS_DENIED && plan.Arranged && plan.Screens.All(s => s.DevicePaths.Count == 1) && ApplyWithChangeDisplaySettings(plan))
        {
            LastDetail = "applied: ChangeDisplaySettingsEx fallback";
            return DisplayResult.Success;
        }

        if (lastError == 0) lastError = ERROR_GEN_FAILURE;
        LastDetail = $"apply failed: error {lastError}";
        return DisplayResult.Fail(Explain(plan, lastError));
    }

    /// <summary>Diagnostics: validate the exact arrangement with no adjustments allowed. Returns the Win32 error.</summary>
    internal int ValidateExact(RoomPlan plan)
    {
        var attempt = Attempts(plan)[0];
        return Run(attempt with { AllowChanges = false }, SDC_VALIDATE);
    }

    /// <summary>
    /// Resolutions and refresh rates the monitor can show. Windows only lists them while the monitor
    /// is on; when it's off this returns just its preferred mode (or nothing).
    /// </summary>
    public IReadOnlyList<DisplayMode> Modes(string devicePath)
    {
        var all = CcdConfig.All();
        var target = Targets(all).FirstOrDefault(t => string.Equals(t.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));
        if (target == null) return [];

        var active = CcdConfig.Active().Paths.FirstOrDefault(p =>
            (p.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0 && p.targetInfo.adapterId.Equals(target.Adapter) && p.targetInfo.id == target.Id);
        var modes = new List<DisplayMode>();
        if (active.flags != 0 && DeviceInfo.SourceGdiName(active.sourceInfo.adapterId, active.sourceInfo.id) is { } gdi)
        {
            var dm = GdiDisplayApi.NewDevMode();
            for (var i = 0; GdiDisplayApi.EnumDisplaySettings(gdi, i, ref dm); i++)
            {
                if (dm.dmBitsPerPel < 24 || dm.dmPelsWidth < 640 || dm.dmDisplayFrequency < 23) continue;
                var (w, h) = dm.dmPelsWidth >= dm.dmPelsHeight ? (dm.dmPelsWidth, dm.dmPelsHeight) : (dm.dmPelsHeight, dm.dmPelsWidth);
                modes.Add(new DisplayMode((int)w, (int)h, (int)dm.dmDisplayFrequency * 1000));
            }
        }
        if (modes.Count == 0 && DeviceInfo.PreferredMode(target.Adapter, target.Id) is { } preferred)
        {
            var (w, h) = preferred.Width >= preferred.Height ? (preferred.Width, preferred.Height) : (preferred.Height, preferred.Width);
            var hz = preferred.RefreshMillihz > 0 ? (int)Math.Round(preferred.RefreshMillihz / 1000.0) * 1000 : 60000;
            modes.Add(new DisplayMode((int)w, (int)h, hz));
        }
        return modes.Distinct().OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.RefreshMillihz).ToList();
    }

    /// <summary>The monitor's own preferred (native) resolution, whether or not it's on.</summary>
    public DisplayMode? PreferredMode(string devicePath)
    {
        var target = Targets(CcdConfig.All()).FirstOrDefault(t => string.Equals(t.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));
        if (target == null || DeviceInfo.PreferredMode(target.Adapter, target.Id) is not { } p) return null;
        var (w, h) = p.Width >= p.Height ? (p.Width, p.Height) : (p.Height, p.Width);
        return new DisplayMode((int)w, (int)h, p.RefreshMillihz > 0 ? (int)Math.Round(p.RefreshMillihz / 1000.0) * 1000 : 60000);
    }

    public IDisplayRestorePoint CaptureRestorePoint() => new RestorePoint(CcdConfig.Active());

    public DisplayResult Restore(IDisplayRestorePoint point)
    {
        var config = ((RestorePoint)point).Config;
        var error = SetDisplayConfig((uint)config.Paths.Length, config.Paths, (uint)config.Modes.Length, config.Modes,
            SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_SAVE_TO_DATABASE);
        return error == ERROR_SUCCESS ? DisplayResult.Success : DisplayResult.Fail(ErrorText(error));
    }

    public DisplayResult ShowAll()
    {
        var state = Query();
        var screens = state.Monitors.Select(m => new PlannedScreen(0, 0, 0, 0, 0, 0, null, [m.DevicePath])).ToList();
        if (screens.Count > 0 && Apply(new RoomPlan("", "All displays", screens)).Ok) return DisplayResult.Success;

        // The graphics card can't drive them all at once: let Windows extend across as many as it can.
        var error = SetDisplayConfig(0, null, 0, null, SDC_APPLY | SDC_TOPOLOGY_EXTEND);
        return error == ERROR_SUCCESS ? DisplayResult.Success : DisplayResult.Fail(ErrorText(error));
    }

    // ---- building path sets ----

    private sealed class PlanException(string message) : Exception(message);

    private static List<Target> Targets(CcdConfig all)
    {
        var result = new List<Target>();
        var seen = new HashSet<(LUID, uint)>();
        foreach (var p in all.Paths)
        {
            if (p.targetInfo.targetAvailable == 0) continue;
            var key = (p.targetInfo.adapterId, p.targetInfo.id);
            if (!seen.Add(key)) continue;
            var name = DeviceInfo.TargetName(key.adapterId, key.id);
            if (name == null || string.IsNullOrEmpty(name.Value.monitorDevicePath)) continue;
            var friendly = string.IsNullOrWhiteSpace(name.Value.monitorFriendlyDeviceName)
                ? (name.Value.outputTechnology == 0x80000000 ? "Built-in display" : "Display")
                : name.Value.monitorFriendlyDeviceName;
            result.Add(new Target(key.adapterId, key.id, name.Value.monitorDevicePath, friendly, name.Value.outputTechnology));
        }
        return result;
    }

    private List<Attempt> Attempts(RoomPlan plan)
    {
        var all = CcdConfig.All();
        var active = CcdConfig.Active();
        var targets = Targets(all).ToDictionary(t => t.DevicePath, StringComparer.OrdinalIgnoreCase);
        var sources = AssignSources(plan, all, active, targets);
        var attempts = new List<Attempt>();

        if (plan.Arranged)
        {
            var saved = Build("saved arrangement", all, sources, targets, withModes: true, keepRefresh: true);
            attempts.Add(saved with { Name = "saved arrangement, exact", AllowChanges = false });
            attempts.Add(saved);
            if (plan.Screens.Any(s => s.RefreshMillihz > 0))
                attempts.Add(Build("saved arrangement, refresh rate chosen by Windows", all, sources, targets, withModes: true, keepRefresh: false));
            attempts.Add(Build("Windows' remembered arrangement", all, sources, targets, withModes: false, keepRefresh: false));
        }
        else
        {
            attempts.Add(Build("Windows' remembered arrangement", all, sources, targets, withModes: false, keepRefresh: false));
            var synthesized = Synthesize(plan, sources, targets);
            attempts.Add(Build("each monitor's preferred resolution, side by side", all, synthesized, targets, withModes: true, keepRefresh: false));
        }
        return attempts;
    }

    /// <summary>Gives each screen its own source on its graphics card, keeping the current one where possible.</summary>
    private static List<(PlannedScreen Screen, LUID Adapter, uint SourceId)> AssignSources(
        RoomPlan plan, CcdConfig all, CcdConfig active, Dictionary<string, Target> targets)
    {
        var activeSource = active.Paths
            .Where(p => (p.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0)
            .GroupBy(p => (p.targetInfo.adapterId, p.targetInfo.id))
            .ToDictionary(g => g.Key, g => g.First().sourceInfo.id);

        var used = new HashSet<(LUID, uint)>();
        var result = new List<(PlannedScreen, LUID, uint)>();
        foreach (var screen in plan.Screens)
        {
            var screenTargets = screen.DevicePaths.Select(path =>
                targets.TryGetValue(path, out var t) ? t : throw new PlanException("A display in this room isn't connected right now.")).ToList();
            var adapter = screenTargets[0].Adapter;
            if (screenTargets.Any(t => !t.Adapter.Equals(adapter)))
                throw new PlanException("These displays are on different graphics cards, so they can't be duplicated.");

            var candidates = all.Paths
                .Where(p => p.sourceInfo.adapterId.Equals(adapter))
                .Select(p => p.sourceInfo.id)
                .Distinct()
                .Where(id => !used.Contains((adapter, id)))
                .Where(id => screenTargets.All(t => all.Paths.Any(p => p.sourceInfo.adapterId.Equals(adapter) && p.sourceInfo.id == id
                                                                     && p.targetInfo.adapterId.Equals(t.Adapter) && p.targetInfo.id == t.Id)))
                .OrderBy(id => id)
                .ToList();
            if (candidates.Count == 0)
                throw new PlanException(screen.DevicePaths.Count > 1
                    ? "This graphics card can't show the same picture on all of these displays."
                    : "This graphics card can't show any more separate screens.");

            uint? preferred = null;
            foreach (var t in screenTargets)
                if (activeSource.TryGetValue((t.Adapter, t.Id), out var s) && candidates.Contains(s)) { preferred = s; break; }

            var sourceId = preferred ?? candidates[0];
            used.Add((adapter, sourceId));
            result.Add((screen, adapter, sourceId));
        }
        return result;
    }

    /// <summary>For rooms not arranged yet: each screen at its first monitor's preferred resolution, left to right.</summary>
    private static List<(PlannedScreen, LUID, uint)> Synthesize(
        RoomPlan plan, List<(PlannedScreen Screen, LUID Adapter, uint SourceId)> sources, Dictionary<string, Target> targets)
    {
        var x = 0;
        var result = new List<(PlannedScreen, LUID, uint)>();
        foreach (var (screen, adapter, sourceId) in sources)
        {
            var t = targets[screen.DevicePaths[0]];
            var mode = DeviceInfo.PreferredMode(t.Adapter, t.Id) ?? (1920u, 1080u, 0u);
            result.Add((screen with { X = x, Y = 0, Width = (int)mode.Width, Height = (int)mode.Height }, adapter, sourceId));
            x += (int)mode.Width;
        }
        return result;
    }

    private static Attempt Build(string name, CcdConfig all, IReadOnlyList<(PlannedScreen Screen, LUID Adapter, uint SourceId)> sources,
        Dictionary<string, Target> targets, bool withModes, bool keepRefresh)
    {
        var paths = new List<DISPLAYCONFIG_PATH_INFO>();
        var modes = new List<DISPLAYCONFIG_MODE_INFO>();

        foreach (var (screen, adapter, sourceId) in sources)
        {
            var sourceModeIndex = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            if (withModes)
            {
                modes.Add(new DISPLAYCONFIG_MODE_INFO
                {
                    infoType = DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE,
                    id = sourceId,
                    adapterId = adapter,
                    sourceMode = new DISPLAYCONFIG_SOURCE_MODE
                    {
                        width = (uint)screen.Width,
                        height = (uint)screen.Height,
                        pixelFormat = DISPLAYCONFIG_PIXELFORMAT_32BPP,
                        position = new POINTL { x = screen.X, y = screen.Y },
                    },
                });
                sourceModeIndex = (uint)(modes.Count - 1);
            }

            foreach (var devicePath in screen.DevicePaths)
            {
                var t = targets[devicePath];
                var path = all.Paths.First(p => p.sourceInfo.adapterId.Equals(adapter) && p.sourceInfo.id == sourceId
                                                && p.targetInfo.adapterId.Equals(t.Adapter) && p.targetInfo.id == t.Id);
                path.flags = DISPLAYCONFIG_PATH_ACTIVE;
                path.sourceInfo.modeInfoIdx = sourceModeIndex;
                path.sourceInfo.statusFlags = 0;
                path.targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                path.targetInfo.statusFlags = 0;
                path.targetInfo.scaling = DISPLAYCONFIG_SCALING_PREFERRED;
                path.targetInfo.rotation = withModes ? RotationToApi(screen.Rotation) : 1;
                // Refresh rate and scan-line ordering go together: both given, or both left to Windows.
                if (keepRefresh && screen.RefreshMillihz > 0)
                {
                    path.targetInfo.refreshRate = ToRational(screen.RefreshMillihz);
                    if (path.targetInfo.scanLineOrdering == 0) path.targetInfo.scanLineOrdering = DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE;
                }
                else
                {
                    path.targetInfo.refreshRate = default;
                    path.targetInfo.scanLineOrdering = 0;
                }
                paths.Add(path);
            }
        }

        return new Attempt(name, paths.ToArray(), withModes ? modes.ToArray() : null, !withModes, sources);
    }

    private static int Run(Attempt attempt, uint action)
    {
        if (attempt.TopologyOnly)
            return SetDisplayConfig((uint)attempt.Paths.Length, attempt.Paths, 0, null,
                action | SDC_TOPOLOGY_SUPPLIED | SDC_ALLOW_PATH_ORDER_CHANGES);
        return SetDisplayConfig((uint)attempt.Paths.Length, attempt.Paths, (uint)attempt.Modes!.Length, attempt.Modes,
            action | SDC_USE_SUPPLIED_DISPLAY_CONFIG | (attempt.AllowChanges ? SDC_ALLOW_CHANGES : 0));
    }

    /// <summary>Scaling isn't part of SetDisplayConfig; put back the room's saved scale per screen.</summary>
    private static void ApplyScaling(Attempt attempt)
    {
        foreach (var (screen, adapter, sourceId) in attempt.Sources)
            if (screen.ScalePercent is int percent)
                DeviceInfo.SetScalePercent(adapter, sourceId, percent);
    }

    private bool ApplyWithChangeDisplaySettings(RoomPlan plan)
    {
        try
        {
            var all = CcdConfig.All();
            var active = CcdConfig.Active();
            var targets = Targets(all).ToDictionary(t => t.DevicePath, StringComparer.OrdinalIgnoreCase);
            var wanted = plan.DevicePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sources = AssignSources(plan, all, active, targets);

            foreach (var p in active.Paths.Where(p => (p.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0))
            {
                var target = targets.Values.FirstOrDefault(t => t.Adapter.Equals(p.targetInfo.adapterId) && t.Id == p.targetInfo.id);
                if (target != null && wanted.Contains(target.DevicePath)) continue;
                if (sources.Any(s => s.Adapter.Equals(p.sourceInfo.adapterId) && s.SourceId == p.sourceInfo.id)) continue;
                var gdi = DeviceInfo.SourceGdiName(p.sourceInfo.adapterId, p.sourceInfo.id);
                if (gdi == null) continue;
                var detach = GdiDisplayApi.NewDevMode();
                detach.dmFields = GdiDisplayApi.DM_POSITION | GdiDisplayApi.DM_PELSWIDTH | GdiDisplayApi.DM_PELSHEIGHT;
                GdiDisplayApi.ChangeDisplaySettingsEx(gdi, ref detach, IntPtr.Zero, GdiDisplayApi.CDS_UPDATEREGISTRY | GdiDisplayApi.CDS_NORESET, IntPtr.Zero);
            }

            foreach (var (screen, adapter, sourceId) in sources)
            {
                var gdi = DeviceInfo.SourceGdiName(adapter, sourceId);
                if (gdi == null) return false;
                var mode = GdiDisplayApi.NewDevMode();
                mode.dmFields = GdiDisplayApi.DM_POSITION | GdiDisplayApi.DM_PELSWIDTH | GdiDisplayApi.DM_PELSHEIGHT;
                mode.dmPositionX = screen.X;
                mode.dmPositionY = screen.Y;
                mode.dmPelsWidth = (uint)screen.Width;
                mode.dmPelsHeight = (uint)screen.Height;
                if (screen.RefreshMillihz > 0)
                {
                    mode.dmFields |= GdiDisplayApi.DM_DISPLAYFREQUENCY;
                    mode.dmDisplayFrequency = (uint)Math.Round(screen.RefreshMillihz / 1000.0);
                }
                var flags = GdiDisplayApi.CDS_UPDATEREGISTRY | GdiDisplayApi.CDS_NORESET;
                if (screen.X == 0 && screen.Y == 0) flags |= GdiDisplayApi.CDS_SET_PRIMARY;
                var result = GdiDisplayApi.ChangeDisplaySettingsEx(gdi, ref mode, IntPtr.Zero, flags, IntPtr.Zero);
                if (result != GdiDisplayApi.DISP_CHANGE_SUCCESSFUL) return false;
            }

            return GdiDisplayApi.ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero) == GdiDisplayApi.DISP_CHANGE_SUCCESSFUL;
        }
        catch (Exception e) when (e is PlanException or DisplayApiException)
        {
            return false;
        }
    }

    // ---- helpers ----

    /// <summary>Turns a driver error into a sentence a person can act on.</summary>
    private static string Explain(RoomPlan plan, int error)
    {
        if (error == ERROR_ACCESS_DENIED)
            return "Windows blocked the change. This happens on the lock screen, during a remote desktop session, or while another program is changing the displays.";
        if (plan.Screens.Any(s => s.DevicePaths.Count > 1))
            return "The graphics card can't duplicate these displays in this combination. Duplicated displays must be on the same graphics card and use the same resolution.";
        var count = plan.DevicePaths.Count();
        if (count >= 2 && error is ERROR_BAD_CONFIGURATION or ERROR_GEN_FAILURE or ERROR_INVALID_PARAMETER)
            return $"This graphics card can't run these {count} displays at once.";
        return ErrorText(error);
    }

    private static string ErrorText(int error) => error switch
    {
        ERROR_NOT_SUPPORTED => "This PC's graphics driver doesn't support switching displays this way.",
        ERROR_BAD_CONFIGURATION => "The graphics card can't run this combination of displays.",
        ERROR_GEN_FAILURE => "The graphics driver couldn't set up this combination of displays.",
        ERROR_INVALID_PARAMETER => "Windows didn't accept this combination of displays and settings.",
        ERROR_ACCESS_DENIED => "Windows blocked the change.",
        _ => $"Windows didn't accept the change (error code {error}).",
    };

    private static DISPLAYCONFIG_RATIONAL ToRational(int millihz)
    {
        // NTSC-style rates (59.94, 29.97, 119.88 Hz) are exact fractions over 1001.
        var ntsc = millihz * 1001.0 / 1000.0;
        if (millihz % 1000 != 0 && Math.Abs(ntsc - Math.Round(ntsc / 1000) * 1000) < 2)
            return new DISPLAYCONFIG_RATIONAL { Numerator = (uint)(Math.Round(ntsc / 1000) * 1000), Denominator = 1001 };
        return new DISPLAYCONFIG_RATIONAL { Numerator = (uint)millihz, Denominator = 1000 };
    }

    private static int RotationFromApi(uint rotation) => rotation switch { 2 => 90, 3 => 180, 4 => 270, _ => 0 };

    private static uint RotationToApi(int degrees) => degrees switch { 90 => 2, 180 => 3, 270 => 4, _ => 1 };
}
