using RoomSwitcher.App.Ui;
using RoomSwitcher.Windows;

namespace RoomSwitcher.App;

// Step 4: start with Windows, window restore, timing, and where the files live.
internal sealed partial class SetupForm
{
    private readonly CheckBox _startWithWindows = new()
    {
        Text = "Start Room Switcher when I sign in to Windows", AutoSize = true, Font = Theme.BodySemibold, ForeColor = Theme.Text,
    };
    private readonly CheckBox _restoreWindows = new()
    {
        Text = "Put each room's open windows back where they were when switching to it", AutoSize = true,
        Font = Theme.BodySemibold, ForeColor = Theme.Text,
    };
    private readonly NumericUpDown _settleDelay = new() { Minimum = 0, Maximum = 10, DecimalPlaces = 1, Increment = 0.5m, Width = 96, Font = Theme.Body, AccessibleName = "Seconds to wait after switching" };
    private readonly HotkeyBox _nextRoomHotkey = new() { Width = 260, Margin = new Padding(0), AccessibleName = "Next room shortcut" };
    private readonly Label _nextRoomNote = new()
    {
        AutoSize = true, Font = Theme.Small, ForeColor = Theme.Warning, Visible = false, Margin = new Padding(0, 6, 0, 0),
        BackColor = Theme.Window,
    };
    private readonly Label _whereFiles = Wrapping(new Label { Margin = new Padding(0, 18, 0, 0), ForeColor = Theme.Subtext });

    private void BuildFinishTab()
    {
        var page = Page();
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Card,
        };
        flow.Controls.Add(Paragraph(
            "That's it. Room Switcher lives in the system tray. Select its icon to choose a room. " +
            "If a display is ever left blank, select Identify on the Displays step: it offers to turn on all displays."));

        var options = new RoundedPanel
        {
            Radius = 10, FillColor = Theme.Window, BackColor = Theme.Card, Padding = new Padding(20, 16, 20, 16),
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 4, 0, 0),
        };
        var inner = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Theme.Window, Dock = DockStyle.Fill,
        };
        _startWithWindows.BackColor = _restoreWindows.BackColor = Theme.Window;
        _startWithWindows.Margin = new Padding(0, 0, 0, 12);
        _restoreWindows.Margin = new Padding(0, 0, 0, 12);
        inner.Controls.Add(_startWithWindows);
        inner.Controls.Add(_restoreWindows);
        var delay = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Window, WrapContents = false, Margin = new Padding(0) };
        delay.Controls.Add(new Label { Text = "After switching, wait", AutoSize = true, Margin = new Padding(0, 4, 8, 0) });
        delay.Controls.Add(_settleDelay);
        delay.Controls.Add(new Label
        {
            Text = "seconds for displays to wake up before moving windows back.", AutoSize = true, Margin = new Padding(8, 4, 0, 0),
        });
        inner.Controls.Add(delay);
        var next = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Window, WrapContents = false, Margin = new Padding(0, 14, 0, 0) };
        next.Controls.Add(new Label { Text = "Next room shortcut", AutoSize = true, Font = Theme.BodySemibold, Margin = new Padding(0, 10, 12, 0) });
        next.Controls.Add(_nextRoomHotkey);
        next.Controls.Add(new Label
        {
            Text = "Steps through the rooms in order.", AutoSize = true, ForeColor = Theme.Subtext, Margin = new Padding(12, 10, 0, 0),
        });
        inner.Controls.Add(next);
        inner.Controls.Add(_nextRoomNote);
        options.Controls.Add(inner);
        flow.Controls.Add(options);
        flow.SizeChanged += (_, _) =>
        {
            var w = Math.Max(400, flow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            options.MinimumSize = new Size(w, 0);
            options.MaximumSize = new Size(w, 0);
        };
        flow.Controls.Add(_whereFiles);
        var cli = Paragraph("From a shortcut or script: RoomSwitcher.exe --room \"Office\"   ·   --list   ·   --all");
        cli.Margin = new Padding(0, 10, 0, 0);
        cli.Font = Theme.Body;
        cli.ForeColor = Theme.Subtext;
        flow.Controls.Add(cli);

        _startWithWindows.CheckedChanged += (_, _) => { if (!_loading) { _draft.Settings.StartWithWindows = _startWithWindows.Checked; MarkDirty(); } };
        _restoreWindows.CheckedChanged += (_, _) => { if (!_loading) { _draft.Settings.RestoreWindows = _restoreWindows.Checked; MarkDirty(); } };
        _nextRoomHotkey.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _draft.Settings.NextRoomHotkey = _nextRoomHotkey.Value;
            MarkDirty();
            ShowShortcutNote(_nextRoomNote, ShortcutProblem(_nextRoomHotkey.Value, Core.Hotkeys.HotkeyRules.NextRoomOwner));
        };
        _settleDelay.ValueChanged += (_, _) => { if (!_loading) { _draft.Settings.SettleDelayMs = (int)Math.Round(_settleDelay.Value * 1000); MarkDirty(); } };

        page.Controls.Add(flow);
        AddStep(new StepInfo("Finish", "Set options and complete", Theme.Glyphs.Settings,
            "Choose how Room Switcher starts and behaves, then finish."), page, LoadFinishTab);
    }

    private void LoadFinishTab()
    {
        _loading = true;
        try
        {
            // First run defaults to starting with Windows; a tray app nobody can find isn't much use.
            if (_firstRun && !_dirtyFinishSeen)
            {
                _draft.Settings.StartWithWindows = true;
                _dirtyFinishSeen = true;
                _dirty = true;
            }
            else if (!_firstRun && !_dirtyFinishSeen)
            {
                _draft.Settings.StartWithWindows = StartWithWindows.IsEnabled(AppHost.ExePath);
                _dirtyFinishSeen = true;
            }
            _startWithWindows.Checked = _draft.Settings.StartWithWindows;
            _restoreWindows.Checked = _draft.Settings.RestoreWindows;
            _settleDelay.Value = Math.Clamp(Math.Round(_draft.Settings.SettleDelayMs / 1000m, 1), 0m, 10m);
            _nextRoomHotkey.Value = _draft.Settings.NextRoomHotkey ?? "";
            ShowShortcutNote(_nextRoomNote, ShortcutProblem(_draft.Settings.NextRoomHotkey, Core.Hotkeys.HotkeyRules.NextRoomOwner));
            _whereFiles.Text = _host.Paths.Portable
                ? $"Portable mode: settings are kept next to RoomSwitcher.exe in {_host.Paths.DataDirectory}."
                : $"Settings are kept in {_host.Paths.DataDirectory}. To run from a USB drive instead, copy config.json next to RoomSwitcher.exe.";
        }
        finally
        {
            _loading = false;
        }
    }

    private bool _dirtyFinishSeen;
}
