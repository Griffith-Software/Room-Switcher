using System.Diagnostics;
using RoomSwitcher.Core.Audio;
using RoomSwitcher.Core.Display;
using RoomSwitcher.Core.Hotkeys;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Rooms;
using RoomSwitcher.Core.Storage;
using RoomSwitcher.App.Ui;
using RoomSwitcher.Windows;

namespace RoomSwitcher.App;

/// <summary>
/// First-run setup and "Edit rooms…": name the monitors, build rooms in a routing grid,
/// arrange each room in Windows display settings, then finish. Works on a draft; nothing is
/// saved until Save or Finish.
/// </summary>
internal sealed partial class SetupForm : Form
{
    private static readonly Color DisconnectedColor = Theme.Disabled;

    private readonly AppHost _host;
    private readonly AppConfig _draft;
    private readonly bool _firstRun;
    private DisplayState _state = DisplayState.Empty;
    private bool _dirty;
    private bool _loading;

    private readonly StepNav _nav = new() { Dock = DockStyle.Left, Width = 284 };
    private readonly PageHeader _pageHeader = new();
    private readonly Panel _pageHost = new() { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(0, 8, 0, 8) };
    private readonly List<(StepInfo Info, Control Page, Action OnShow, Action? OnLeave)> _steps = new();
    private int _step = -1;

    private readonly ModernButton _back = new("Back", ButtonKind.Secondary, Theme.Glyphs.Back) { GlyphColor = Theme.Text };
    private readonly ModernButton _next = new("Next", ButtonKind.Primary, Theme.Glyphs.Forward) { GlyphAfter = true };
    private readonly ModernButton _save = new("Save and Close", ButtonKind.Secondary, Theme.Glyphs.Save);
    private TextCommit? _textCommit;
    private readonly IdentifyToggle _identifyNumbers = new();

    public SetupForm(AppHost host, bool firstRun)
    {
        _host = host;
        _firstRun = firstRun;
        _draft = JsonStore.Clone(host.Config);

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = firstRun ? "Room Switcher setup" : "Room Switcher settings";
        Icon = Icon.ExtractAssociatedIcon(AppHost.ExePath);
        Font = Theme.Body;
        ForeColor = Theme.Text;
        BackColor = Theme.Window;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1060, 760);
        Size = new Size(1240, 870);

        // Layout: header band; step list on the left; a white card with heading, step page and buttons.
        var header = new WindowHeader(Text, "Organize your displays by room.");
        var card = new RoundedPanel { Dock = DockStyle.Fill, Radius = 14, Padding = new Padding(28, 24, 28, 14), BackColor = Theme.Window };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 74, BackColor = Theme.Card };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Card,
            Padding = new Padding(0, 18, 0, 0), WrapContents = false,
        };
        // Right to left: Save and Close in the corner, then Save and Continue (Next/Finish in first-run setup), then Back.
        // Closing the window (×) asks whether to save, so there's no separate Cancel.
        _save.Margin = new Padding(0);
        _next.Margin = new Padding(0, 0, 14, 0);
        _back.Margin = new Padding(0, 0, 14, 0);
        _back.Width = 138;
        buttons.Controls.AddRange([_save, _next, _back]);
        footer.Controls.Add(buttons);
        footer.Controls.Add(new Separator { Dock = DockStyle.Top });
        card.Controls.Add(_pageHost);
        card.Controls.Add(footer);
        card.Controls.Add(_pageHeader);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 18, 20, 20), BackColor = Theme.Window };
        content.Controls.Add(card);
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window };
        body.Controls.Add(content);
        body.Controls.Add(_nav);
        Controls.Add(body);
        Controls.Add(header);

        BuildMonitorsTab();
        BuildAudioTab();
        BuildUsbTab();
        BuildRoomsTab();
        BuildArrangeTab();
        BuildFinishTab();
        PauseShortcutsWhileEditing(_roomHotkey);
        PauseShortcutsWhileEditing(_nextRoomHotkey);

        _nav.StepClicked += (_, index) => GoTo(index);
        _nav.Footer = $"Version {System.Diagnostics.FileVersionInfo.GetVersionInfo(AppHost.ExePath).ProductVersion?.Split('+')[0]}";
        _back.Click += (_, _) => GoTo(Math.Max(0, _step - 1));
        _next.Click += (_, _) =>
        {
            if (_step == _steps.Count - 1) SaveAndClose();
            else if (_firstRun || SaveAndContinue()) GoTo(_step + 1);
        };
        _save.Click += (_, _) => SaveAndClose();
        Resize += (_, _) => header.Invalidate();

        RefreshState();
        // Enter, or a click outside a text box, keeps what was typed and takes the cursor away.
        _textCommit = new TextCommit(this);
    }

    // ---- custom title bar ----

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeChrome.WM_NCCALCSIZE && NativeChrome.CalcSize(this, ref m, msg => base.WndProc(ref msg)))
            return;
        base.WndProc(ref m);
        if (m.Msg == NativeChrome.WM_NCHITTEST) NativeChrome.HitTest(this, ref m);
    }

    // ---- steps ----

    private void AddStep(StepInfo info, Control page, Action onShow, Action? onLeave = null)
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        page.BackColor = Theme.Card;
        _steps.Add((info, page, onShow, onLeave));
        _pageHost.Controls.Add(page);
        _nav.Add(info);
    }

    private static Panel Page() => new() { BackColor = Theme.Card };

    // Grids filled before the window exists lose their rows, so load the first step once it's shown.
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        GoTo(0);
    }

    private void GoTo(int index)
    {
        if (index == _step || index < 0 || index >= _steps.Count) return;
        Validate();
        if (_step >= 0) _steps[_step].OnLeave?.Invoke();
        _step = index;
        var (info, page, onShow, _) = _steps[index];
        _pageHost.SuspendLayout();
        foreach (var step in _steps) step.Page.Visible = step.Page == page;
        _pageHost.ResumeLayout();
        _nav.Selected = index;
        _pageHeader.Set(info.Glyph, info.Title, info.PageSubtitle);

        _back.Enabled = index > 0;
        var last = index == _steps.Count - 1;
        if (_firstRun)
        {
            // First-run setup saves once, at the end.
            _next.Text = last ? "Finish" : "Next";
            _next.Glyph = last ? Theme.Glyphs.Check : Theme.Glyphs.Forward;
            _next.GlyphAfter = !last;
            _save.Visible = false;
        }
        else
        {
            _next.Text = "Save and Continue";
            _next.Glyph = Theme.Glyphs.Forward;
            _next.GlyphAfter = true;
            // On the last step there's nothing to continue to: Save and Close is the main button.
            _next.Visible = !last;
            _save.Kind = last ? ButtonKind.Primary : ButtonKind.Secondary;
            _save.Visible = true;
            _save.FitWidth();
            _save.Invalidate();
        }
        _next.FitWidth();
        _next.Width = Math.Max(_next.Width, 138);
        _next.Invalidate();

        onShow();
    }

    private HotkeyListener? _probe;

    /// <summary>
    /// What's wrong with a shortcut, if anything: not allowed, used by another room, or owned by
    /// another program. The tray releases Room Switcher's own shortcuts while this window is open,
    /// so "owned by another program" is accurate here.
    /// </summary>
    private string? ShortcutProblem(string? text, string ownerId)
    {
        if (HotkeyRules.Check(_draft, text, ownerId) is { } problem) return problem;
        if (HotkeyRules.Parse(text) is { } key)
        {
            _probe ??= new HotkeyListener();
            if (_host.HoldsShortcut?.Invoke(key) != true && !_probe.IsAvailable(key))
                return $"Another program already uses {key.Display}. Pick a different shortcut.";
        }
        return null;
    }

    private static void ShowShortcutNote(Label note, string? problem)
    {
        note.Text = problem ?? "";
        note.Visible = problem != null;
    }

    /// <summary>
    /// Shortcuts keep working while this window is open; they're only released while a shortcut box
    /// has focus, so the keys you press go into the box instead of switching rooms.
    /// </summary>
    private void PauseShortcutsWhileEditing(Ui.HotkeyBox box)
    {
        box.Enter += (_, _) => _host.PauseShortcuts?.Invoke();
        box.Leave += (_, _) => _host.ResumeShortcuts?.Invoke();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_step >= 0) _steps[_step].OnLeave?.Invoke();
        _identifyNumbers.Hide();
        _probe?.Dispose();
        _rawInput?.Dispose();
        _usbFlashTimer.Dispose();
        _textCommit?.Dispose();
        base.OnFormClosed(e);
    }

    private void MarkDirty() { if (!_loading) _dirty = true; }

    /// <summary>Reads the hardware again and adds any newly connected monitors to the draft.</summary>
    private IReadOnlyList<AudioDevice> _outputs = [];
    private IReadOnlyList<AudioDevice> _inputs = [];

    private void RefreshState()
    {
        try
        {
            _outputs = _host.Audio.List(AudioFlow.Output);
            _inputs = _host.Audio.List(AudioFlow.Input);
        }
        catch (Exception)
        {
            // No sound devices (or the sound service is stopped): the sound choices just stay empty.
            _outputs = _inputs = [];
        }
        try
        {
            _state = _host.Display.Query();
            if (MonitorResolver.SyncMonitors(_draft, _state)) _dirty = true;
            // Remembered resolutions aren't an edit of yours, so they don't ask to be saved.
            _host.RememberModes(_draft, _state);
        }
        catch (Exception e)
        {
            ThemedDialog.Show(this, $"Couldn't get the list of displays from Windows.\n\n{e.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---- validation shared by the tabs ----

    private PlanResult CheckRoom(Room room)
    {
        var result = RoomPlanner.Build(_draft, room, _state);
        if (!result.Ok) return result;
        try
        {
            var driver = _host.Display.Validate(result.Plan!);
            if (driver.Ok) return result;
            return result with { Problems = result.Problems.Append(new RoomProblem(ProblemSeverity.Error, driver.Message)).ToList() };
        }
        catch (Exception e)
        {
            return result with { Problems = result.Problems.Append(new RoomProblem(ProblemSeverity.Error, e.Message)).ToList() };
        }
    }

    private string MonitorLabel(MonitorInfo m)
    {
        var number = _draft.Monitors.IndexOf(m) + 1;
        return $"{number}. {m.Name}";
    }

    private bool IsConnected(MonitorInfo m) => MonitorResolver.Resolve(_draft, _state).Find(m.Id) != null;

    // ---- saving ----

    /// <summary>Save and Continue: saves what's been changed so far and stays open. Returns false if saving failed.</summary>
    private bool SaveAndContinue()
    {
        Validate();
        if (!_dirty) return true;
        try
        {
            // A copy, so further edits stay a draft until they're saved too.
            _host.ReplaceConfig(JsonStore.Clone(_draft));
            StartWithWindows.Set(_draft.Settings.StartWithWindows, AppHost.ExePath);
        }
        catch (Exception e)
        {
            ThemedDialog.Show(this, $"Couldn't save.\n\n{e.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        _dirty = false;
        return true;
    }

    private void SaveAndClose()
    {
        var broken = _draft.Rooms.Where(r => !CheckRoom(r).Ok).Select(r => r.Name).ToList();
        if (broken.Count > 0)
        {
            var answer = ThemedDialog.Show(this,
                $"These rooms can't be used on this PC right now and will show as unavailable:\n\n  {string.Join("\n  ", broken)}\n\nSave anyway?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }

        try
        {
            _host.ReplaceConfig(_draft);
            StartWithWindows.Set(_draft.Settings.StartWithWindows, AppHost.ExePath);
        }
        catch (Exception e)
        {
            ThemedDialog.Show(this, $"Couldn't save.\n\n{e.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _dirty = false;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_dirty && e.CloseReason == CloseReason.UserClosing)
        {
            var answer = ThemedDialog.Show(this, "Save your changes to displays and rooms?", Text,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) { e.Cancel = true; return; }
            if (answer == DialogResult.Yes)
            {
                e.Cancel = true;
                BeginInvoke(SaveAndClose);
                return;
            }
        }
        base.OnFormClosing(e);
    }

    // ---- small layout helpers ----

    private static Label Paragraph(string text) =>
        Wrapping(new Label { Text = text, Margin = new Padding(0, 0, 0, 14), Font = Theme.PageSubtitle, ForeColor = Theme.Text });

    /// <summary>Makes an auto-sized label wrap to the width of whatever it is placed in.</summary>
    private static Label Wrapping(Label label)
    {
        label.AutoSize = true;
        void Fit()
        {
            if (label.Parent is { } parent)
                label.MaximumSize = new Size(Math.Max(100, parent.ClientSize.Width - parent.Padding.Horizontal - label.Margin.Horizontal - SystemInformation.VerticalScrollBarWidth), 0);
        }
        label.ParentChanged += (_, _) =>
        {
            if (label.Parent == null) return;
            label.Parent.SizeChanged += (_, _) => Fit();
            Fit();
        };
        return label;
    }

    private static void OpenDisplaySettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true }); }
        catch (Exception) { /* very old builds: nothing else to open */ }
    }
}
