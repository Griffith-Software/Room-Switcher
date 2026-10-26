using System.Reflection;
using RoomSwitcher.App.Ui;
using RoomSwitcher.Core.Cli;
using RoomSwitcher.Core.Hotkeys;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Rooms;
using RoomSwitcher.Windows;

namespace RoomSwitcher.App;

/// <summary>The tray icon and its menu: pick a room, edit rooms, turn on all displays, start with Windows, exit.</summary>
internal sealed class TrayApplication : ApplicationContext
{
    private readonly AppHost _host;
    private readonly NotifyIcon _icon;
    private readonly TrayMenu _menu = new();
    private readonly PipeServer _pipe;
    private readonly HotkeyListener _hotkeys = new();
    private SetupForm? _setup;
    private Icon? _currentIcon;
    private string? _switchingTo;

    public TrayApplication(AppHost host, bool openSetup, bool atSignIn = false, bool openTest = false)
    {
        _host = host;
        // Creating the menu installs the WinForms synchronization context the pipe needs.
        _menu.Opening += (_, e) => { TryReloadRooms(); BuildMenu(); e.Cancel = false; };

        _icon = new NotifyIcon { ContextMenuStrip = _menu, Visible = true };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowMenuAtCursor();
        };
        _icon.BalloonTipClicked += (_, _) => { };

        _pipe = new PipeServer(HandleRemoteAsync, SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
        if (_host.Listener != null) _host.Listener.DisplayChanged += (_, _) => OnDisplayChanged();

        SyncStartWithWindows();
        UpdateIcon();
        RegisterHotkeys();
        _host.PauseShortcuts = () => _hotkeys.UnregisterAll();
        _host.ResumeShortcuts = () => RegisterHotkeys(quiet: true);
        _host.HoldsShortcut = _hotkeys.Holds;

        if (atSignIn && _host.Config.Settings.RestartAudioAtSignIn) _ = RestartAudioAfterSignInAsync();

        if (atSignIn && _host.Config.Rooms.Count == 0 && !openSetup)
        {
            // Start with Windows is only turned on by finishing setup, so no rooms here means they couldn't be read.
            // Don't open an empty setup that could be saved over them; keep looking for a while instead.
            Notify("Room Switcher can't read your rooms yet. It will keep trying, and they'll be back in the menu as soon as Windows lets it.", ToolTipIcon.Warning);
            WatchForRooms();
        }
        else if (openSetup || _host.Config.Rooms.Count == 0)
            SynchronizationContext.Current!.Post(_ => OpenSetup(firstRun: _host.Config.Rooms.Count == 0), null);
        else if (openTest)
            SynchronizationContext.Current!.Post(_ => OpenTestDevices(), null);
    }

    /// <summary>After a sign-in that found no rooms: look again every 5 seconds for 15 minutes.</summary>
    private void WatchForRooms()
    {
        var until = DateTime.Now.AddMinutes(15);
        var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += (_, _) =>
        {
            if (TryReloadRooms() || DateTime.Now > until) timer.Dispose();
        };
        timer.Start();
    }

    /// <summary>True once rooms are loaded (picking them up now if they couldn't be read at start).</summary>
    private bool TryReloadRooms()
    {
        if (!_host.ReloadIfEmpty()) return _host.Config.Rooms.Count > 0;
        UpdateIcon();
        RegisterHotkeys(quiet: true);
        Notify("Your rooms are back.", ToolTipIcon.Info);
        return true;
    }

    // ---- menu ----

    private void BuildMenu()
    {
        _menu.SuspendLayout();
        _menu.ClearItems();
        var busy = _switchingTo != null;
        Room? current = null;
        try { current = _host.Service.DetectCurrentRoom(); } catch { /* displays mid-change */ }

        var status = busy ? $"Switching to {_switchingTo}…" : current != null ? $"{current.Name} is on" : "No room selected";
        _menu.Items.Add(new MenuHeader(status));
        _menu.Items.Add(new ToolStripSeparator());

        if (_host.Config.Rooms.Count == 0)
        {
            var empty = _menu.Item("No rooms yet", Theme.Glyphs.Info);
            empty.Enabled = false;
            _menu.Items.Add(empty);
        }

        foreach (var room in _host.Config.Rooms)
        {
            var isCurrent = room.Id == current?.Id;
            var item = _menu.Item(Escape(room.Name), Theme.Glyphs.Monitor);
            item.Enabled = !busy;
            item.ShortcutKeyDisplayString = HotkeyRules.Parse(room.Hotkey)?.Display;
            item.AccessibleName = isCurrent ? $"{room.Name}, current room" : room.Name;
            if (isCurrent)
            {
                item.Image?.Dispose();
                item.Image = _menu.GlyphImage(Theme.Glyphs.Check, Color.White, filledCircle: true);
                item.Font = Theme.BodySemibold;
            }
            PlanResult check;
            try { check = _host.Service.Check(room); }
            catch (Exception e) { check = new PlanResult(null, [new RoomProblem(ProblemSeverity.Error, e.Message)]); }
            if (!check.Ok)
            {
                item.Enabled = false;
                item.Text += "  (unavailable)";
                item.ToolTipText = check.FirstError;
            }
            var target = room;
            item.Click += async (_, _) => await SwitchAsync(target);
            _menu.Items.Add(item);
        }

        if (_host.Config.Rooms.Count > 1)
        {
            var next = _menu.Item("Next room", Theme.Glyphs.NextRoom, async (_, _) => await NextRoomAsync());
            next.Enabled = !busy;
            next.ShortcutKeyDisplayString = HotkeyRules.Parse(_host.Config.Settings.NextRoomHotkey)?.Display;
            _menu.Items.Add(next);
        }

        _menu.Items.Add(new ToolStripSeparator());
        var edit = _menu.Item("Edit rooms…", Theme.Glyphs.Edit, (_, _) => OpenSetup(firstRun: false));
        edit.Enabled = !busy;
        _menu.Items.Add(edit);
        var test = _menu.Item("Test devices…", Theme.Glyphs.Test, (_, _) => OpenTestDevices());
        test.Enabled = !busy && _host.Config.Rooms.Count > 0;
        test.ToolTipText = "Check each room's displays, speakers, microphone and devices, and fix silent apps.";
        _menu.Items.Add(test);

        _menu.Items.Add(new ToolStripSeparator());
        var startsWithWindows = StartWithWindows.IsEnabled(AppHost.ExePath);
        var start = _menu.Item("Start with Windows",
            startsWithWindows ? Theme.Glyphs.CheckboxChecked : Theme.Glyphs.Checkbox, (_, _) => ToggleStartWithWindows(), accent: startsWithWindows);
        start.AccessibleName = startsWithWindows ? "Start with Windows, on" : "Start with Windows, off";
        start.AccessibleRole = AccessibleRole.CheckButton;
        _menu.Items.Add(start);
        _menu.Items.Add(_menu.Item("Exit", Theme.Glyphs.Power, async (_, _) => await ExitAsync()));
        _menu.ResumeLayout();
    }

    private static string Escape(string text) => text.Replace("&", "&&");

    private void ShowMenuAtCursor()
    {
        // NotifyIcon only opens its menu on right-click; this is the same call it makes internally,
        // which also handles focus so the menu closes when you click elsewhere.
        var method = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method != null) method.Invoke(_icon, null);
        else _menu.Show(Cursor.Position);
    }

    // ---- actions ----

    private async Task SwitchAsync(Room room)
    {
        var (_, message, ok, warn) = await RunSwitch(room.Name, () => _host.Service.SwitchToAsync(room));
        if (!ok) Notify(message, ToolTipIcon.Error);
        else if (warn) Notify(message, ToolTipIcon.Warning);
    }

    // ---- keyboard shortcuts ----

    /// <summary>(Re)registers every room's shortcut and the next-room shortcut. Warns about any another program owns.</summary>
    private void RegisterHotkeys(bool quiet = false)
    {
        _hotkeys.UnregisterAll();
        var taken = new List<string>();
        foreach (var room in _host.Config.Rooms)
        {
            if (HotkeyRules.Parse(room.Hotkey) is not { } key || HotkeyRules.Check(_host.Config, room.Hotkey, room.Id) != null) continue;
            var id = room.Id;
            if (!_hotkeys.Register(key, () => _ = RoomHotkeyAsync(id))) taken.Add($"{key.Display} ({room.Name})");
        }
        var nextText = _host.Config.Settings.NextRoomHotkey;
        if (HotkeyRules.Parse(nextText) is { } next && HotkeyRules.Check(_host.Config, nextText, HotkeyRules.NextRoomOwner) == null)
        {
            if (!_hotkeys.Register(next, () => _ = NextRoomAsync())) taken.Add($"{next.Display} (next room)");
        }
        if (taken.Count > 0 && !quiet)
            Notify($"Another program already uses {string.Join(", ", taken)}. Choose a different shortcut in Edit rooms…", ToolTipIcon.Warning);
    }

    private async Task RoomHotkeyAsync(string roomId)
    {
        if (_switchingTo != null || _host.Config.FindRoom(roomId) is not { } room) return;
        var check = _host.Service.Check(room);
        if (!check.Ok)
        {
            Notify($"Can't switch to {room.Name}. {check.FirstError}", ToolTipIcon.Error);
            return;
        }
        await SwitchAsync(room);
    }

    private async Task NextRoomAsync()
    {
        if (_switchingTo != null) return;
        Room? current = null;
        try { current = _host.Service.DetectCurrentRoom(); } catch { /* displays mid-change */ }
        var next = HotkeyRules.NextRoom(_host.Config.Rooms, current, r => _host.Service.Check(r).Ok);
        if (next == null)
        {
            Notify("There's no other room that can be used right now.", ToolTipIcon.Info);
            return;
        }
        await SwitchAsync(next);
    }

    private async Task ShowAllAsync()
    {
        var (_, message, ok, _) = await RunSwitch("all displays", () => _host.Service.ShowAllAsync());
        if (!ok) Notify(message, ToolTipIcon.Error);
    }

    private async Task<(int Code, string Message, bool Ok, bool Warn)> RunSwitch(string label, Func<Task<SwitchResult>> action)
    {
        if (_switchingTo != null) return (1, $"Already switching to {_switchingTo}.", false, false);
        _switchingTo = label;
        UpdateIcon();
        try
        {
            var result = await action();
            var warn = result.Ok && (result.Warning || result.Message.Contains(", but ", StringComparison.Ordinal));
            return (result.Ok ? 0 : 1, result.Message, result.Ok, warn);
        }
        catch (Exception e)
        {
            return (1, $"Couldn't finish switching. {e.Message}", false, false);
        }
        finally
        {
            _switchingTo = null;
            UpdateIcon();
        }
    }

    private async Task<(int, string)> HandleRemoteAsync(Command command)
    {
        TryReloadRooms();
        switch (command.Kind)
        {
            case CommandKind.Setup:
                OpenSetup(firstRun: false);
                return (0, "Opened Room Switcher settings.");
            case CommandKind.Test:
                if (_host.Config.Rooms.Count == 0) return (1, "No rooms are set up yet, so there's nothing to test.");
                OpenTestDevices();
                return (0, "Opened Test devices.");
            case CommandKind.Tray:
                Notify("Room Switcher is already running. Select this icon to choose a room.", ToolTipIcon.Info);
                return (0, "Room Switcher is already running in the system tray.");
            case CommandKind.List:
                return await CommandRunner.RunAsync(_host, command);
        }

        if (_switchingTo != null) return (1, $"Room Switcher is busy switching to {_switchingTo}. Try again in a moment.");
        if (_setup != null) return (1, "Room Switcher settings are open. Close them first, then switch rooms.");

        _switchingTo = command.Kind == CommandKind.All ? "all displays" : command.RoomName;
        UpdateIcon();
        try
        {
            var (code, message) = await CommandRunner.RunAsync(_host, command);
            if (code == CommandRunner.ExitFailed) Notify(message, ToolTipIcon.Error);
            return (code, message);
        }
        finally
        {
            _switchingTo = null;
            UpdateIcon();
        }
    }

    /// <summary>
    /// Exit. Devices a room turned off stay off once Room Switcher has closed, so offer to turn them back on first.
    /// </summary>
    private async Task ExitAsync()
    {
        try
        {
            var present = await Task.Run(() => _host.Devices.List());
            var off = present
                .Where(d => d.Connected && !d.Enabled && Core.Devices.UsbRules.Find(_host.Config, d.Id) is { Managed: true })
                .ToList();
            if (off.Count > 0)
            {
                var names = string.Join("\n  ", off.Select(d => Core.Devices.UsbRules.DisplayName(_host.Config, d)));
                var answer = ThemedDialog.Show(null!,
                    $"These devices are turned off by your rooms and will stay off after Room Switcher closes:\n\n  {names}\n\nTurn them back on before closing?",
                    "Room Switcher", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.Yes) await _host.Devices.ApplyAsync(off.Select(d => d.Id).ToList(), []);
            }
        }
        catch (Exception)
        {
            // Never let a device problem stop Room Switcher from closing.
        }
        ExitThread();
    }

    private TestDevicesForm? _test;

    private void OpenTestDevices()
    {
        if (_test != null)
        {
            _test.Activate();
            return;
        }
        _test = new TestDevicesForm(_host, SwitchAsync);
        _test.FormClosed += (_, _) => _test = null;
        _test.Show();
        _test.Activate();
    }

    /// <summary>
    /// For PCs where apps go silent after a restart until Windows Audio is restarted: do that
    /// shortly after sign-in, once Windows has finished starting its own sound programs.
    /// </summary>
    private async Task RestartAudioAfterSignInAsync()
    {
        await Task.Delay(15000);
        IReadOnlyList<string> problems;
        try
        {
            if (await Task.Run(() => _host.Devices.Status) != Core.Devices.DeviceControlStatus.Ready)
                problems = ["Room Switcher's administrator helper isn't set up. Open Test devices… and select Restart Windows Audio to set it up."];
            else
                problems = await _host.Devices.RestartAudioAsync();
        }
        catch (Exception e)
        {
            problems = [e.Message];
        }
        AppHost.Log(problems.Count == 0 ? "restarted Windows Audio after sign-in" : $"couldn't restart Windows Audio after sign-in: {problems[0]}");
        if (problems.Count > 0) Notify($"Couldn't restart Windows Audio. {problems[0]}", ToolTipIcon.Warning);
    }

    private void OpenSetup(bool firstRun)
    {
        if (_setup != null)
        {
            _setup.Activate();
            return;
        }
        _setup = new SetupForm(_host, firstRun);
        _setup.FormClosed += (_, _) =>
        {
            _setup = null;
            SyncStartWithWindows();
            UpdateIcon();
            RegisterHotkeys();
        };
        _setup.Show();
        _setup.Activate();
    }

    private void ToggleStartWithWindows()
    {
        var enable = !StartWithWindows.IsEnabled(AppHost.ExePath);
        StartWithWindows.Set(enable, AppHost.ExePath);
        _host.Config.Settings.StartWithWindows = enable;
        TrySaveConfig();
    }

    /// <summary>The registry is the truth for the checkbox; keep the config's copy and the exe path in step.</summary>
    private void SyncStartWithWindows()
    {
        try
        {
            // The exe moved (e.g. a portable copy), or the entry was written by an older version.
            if (_host.Config.Settings.StartWithWindows && !StartWithWindows.IsCurrent(AppHost.ExePath))
                StartWithWindows.Set(true, AppHost.ExePath);
        }
        catch (Exception)
        {
            // Registry locked down by policy; the menu item still reflects the real state.
        }
    }

    private void TrySaveConfig()
    {
        try { _host.SaveConfig(); }
        catch (Exception e) { Notify($"Couldn't save your settings. {e.Message}", ToolTipIcon.Error); }
    }

    private void OnDisplayChanged()
    {
        if (_switchingTo == null && _setup == null) _host.SyncMonitors();
        UpdateIcon();
    }

    // ---- icon ----

    private void UpdateIcon()
    {
        Room? current = null;
        try { current = _host.Service.DetectCurrentRoom(); } catch { /* displays mid-change */ }

        var text = _switchingTo != null
            ? $"Room Switcher: switching to {_switchingTo}…"
            : current != null ? $"Room Switcher: {current.Name}" : "Room Switcher: no room selected";
        _icon.Text = text.Length > 127 ? text[..124] + "…" : text;

        var previous = _currentIcon;
        _currentIcon = TrayIconRenderer.Render(current?.Name, _switchingTo != null);
        _icon.Icon = _currentIcon;
        previous?.Dispose();
    }

    private void Notify(string message, ToolTipIcon icon)
    {
        _icon.BalloonTipTitle = "Room Switcher";
        _icon.BalloonTipText = message.Length > 255 ? message[..252] + "…" : message;
        _icon.BalloonTipIcon = icon;
        _icon.ShowBalloonTip(5000);
    }

    protected override void ExitThreadCore()
    {
        _setup?.Close();
        _icon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pipe.Dispose();
            _hotkeys.Dispose();
            _icon.Dispose();
            _menu.Dispose();
            _currentIcon?.Dispose();
            _host.Listener?.Dispose();
        }
        base.Dispose(disposing);
    }
}
