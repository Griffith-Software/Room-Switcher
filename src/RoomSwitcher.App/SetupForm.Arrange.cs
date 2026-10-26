using System.Diagnostics;
using RoomSwitcher.App.Ui;
using RoomSwitcher.Core.Display;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Rooms;
using static RoomSwitcher.Core.Display.Arrangement;

namespace RoomSwitcher.App;

// Step 5: arrange a room's screens right here. Only that room's monitors are shown, with their
// nicknames; drag them into place, set each screen's resolution, refresh rate, orientation and scale,
// then Apply. Windows' own display settings are still there as a fallback.
internal sealed partial class SetupForm
{
    private sealed class ArrangeItem
    {
        public required ScreenSpec Spec;
        public required List<DisplayMode> Modes;
        public Box Box;
        /// <summary>The mode's own (landscape) size; the box is this turned by the rotation.</summary>
        public (int Width, int Height) Size;
        public int Refresh;
        public int Rotation;
        public int? Scale;
        public bool Primary;
    }

    private static readonly (string Label, int Degrees)[] Orientations =
        [("Landscape", 0), ("Portrait", 90), ("Landscape (flipped)", 180), ("Portrait (flipped)", 270)];
    private static readonly int[] ScaleSteps = [100, 125, 150, 175, 200, 225, 250, 300];

    private readonly ComboBox _arrangeRoom = ArrangeCombo(340);
    private readonly ArrangeCanvas _canvas = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _resolution = ArrangeCombo(0);
    private readonly ComboBox _refresh = ArrangeCombo(0);
    private readonly ComboBox _orientation = ArrangeCombo(0);
    private readonly ComboBox _scale = ArrangeCombo(0);
    private readonly CheckBox _mainDisplay = new() { Text = "Main display", AutoSize = true, Font = Theme.BodySemibold, Margin = new Padding(0, 30, 0, 0) };
    private readonly Label _arrangeStatus = Wrapping(new Label { Margin = new Padding(0, 10, 0, 0), Font = Theme.BodySemibold });
    private readonly ModernButton _applyLayout = new("Apply", ButtonKind.Primary, Theme.Glyphs.Check) { Width = 130 };
    private readonly ModernButton _identifyArrange = new("Identify", ButtonKind.Secondary, Theme.Glyphs.Monitor);
    private readonly ModernButton _copyFromWindows = new("Copy from Windows", ButtonKind.Secondary, Theme.Glyphs.Copy);
    private readonly ModernButton _openSettings = new("Windows display settings", ButtonKind.Secondary, Theme.Glyphs.Settings);
    private List<ArrangeItem> _arrangement = new();

    private Room? ArrangeRoom => _arrangeRoom.SelectedItem is RoomItem item ? item.Room : null;

    private static ComboBox ArrangeCombo(int width) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Body, FlatStyle = FlatStyle.System,
        Dock = width == 0 ? DockStyle.Fill : DockStyle.None, Width = width == 0 ? 120 : width, Margin = new Padding(0, 6, 14, 0),
    };

    private void BuildArrangeTab()
    {
        var page = Page();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = Theme.Card };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(Paragraph(
            "Only this room's displays are shown. Drag the screens into place, select one to set its resolution, " +
            "refresh rate, orientation and scale, then select Apply to try it."), 0, 0);

        var pick = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 10), BackColor = Theme.Card, WrapContents = false };
        pick.Controls.Add(new Label { Text = "Room", AutoSize = true, Font = Theme.BodySemibold, Margin = new Padding(0, 10, 12, 0) });
        _arrangeRoom.Margin = new Padding(0, 6, 0, 0);
        pick.Controls.Add(_arrangeRoom);
        layout.Controls.Add(pick, 0, 1);

        var canvasFrame = new RoundedPanel { Dock = DockStyle.Fill, Radius = 10, FillColor = Theme.Window, BackColor = Theme.Card, Padding = new Padding(2), MinimumSize = new Size(0, 200) };
        canvasFrame.Controls.Add(_canvas);
        layout.Controls.Add(canvasFrame, 0, 2);

        // The selected screen's settings.
        var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, AutoSize = true, BackColor = Theme.Card, Margin = new Padding(0, 12, 0, 0) };
        foreach (var w in new[] { 26f, 18f, 24f, 16f, 16f }) settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, w));
        settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        settings.Controls.Add(SectionLabel("Resolution"), 0, 0);
        settings.Controls.Add(SectionLabel("Refresh rate"), 1, 0);
        settings.Controls.Add(SectionLabel("Orientation"), 2, 0);
        settings.Controls.Add(SectionLabel("Scale"), 3, 0);
        settings.Controls.Add(_resolution, 0, 1);
        settings.Controls.Add(_refresh, 1, 1);
        settings.Controls.Add(_orientation, 2, 1);
        settings.Controls.Add(_scale, 3, 1);
        settings.Controls.Add(_mainDisplay, 4, 0);
        settings.SetRowSpan(_mainDisplay, 2);
        _resolution.AccessibleName = "Resolution";
        _refresh.AccessibleName = "Refresh rate";
        _orientation.AccessibleName = "Orientation";
        _scale.AccessibleName = "Scale";
        layout.Controls.Add(settings, 0, 3);
        layout.Controls.Add(_arrangeStatus, 0, 4);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, BackColor = Theme.Card, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
        foreach (var b in new[] { _applyLayout, _identifyArrange, _copyFromWindows, _openSettings }) b.Margin = new Padding(0, 0, 10, 0);
        buttons.Controls.AddRange([_applyLayout, _identifyArrange, _copyFromWindows, _openSettings]);
        layout.Controls.Add(buttons, 0, 5);

        // Look at the hardware again each time: right after a switch, Windows may still have been settling.
        _arrangeRoom.SelectedIndexChanged += (_, _) => { if (!_loading && ArrangeRoom is { } r) { RefreshState(); LoadArrangement(r); } };
        _canvas.SelectionChanged += _ => ShowSelectedScreen();
        _canvas.ScreenMoved += MoveScreen;
        _resolution.SelectedIndexChanged += (_, _) => { if (!_loading) ChangeResolution(); };
        _refresh.SelectedIndexChanged += (_, _) => { if (!_loading) ChangeRefresh(); };
        _orientation.SelectedIndexChanged += (_, _) => { if (!_loading) ChangeOrientation(); };
        _scale.SelectedIndexChanged += (_, _) => { if (!_loading) ChangeScale(); };
        _mainDisplay.CheckedChanged += (_, _) => { if (!_loading) ChangeMain(); };
        _applyLayout.Click += async (_, _) => await ApplyLayoutAsync();
        _identifyNumbers.Bind(_identifyArrange);
        _identifyArrange.Click += (_, _) =>
        {
            if (_identifyNumbers.Active) _identifyNumbers.Hide();
            else _identifyNumbers.Show(_draft, _host.Display.Query());
        };
        _copyFromWindows.Click += (_, _) => CopyLayoutFromWindows();
        _openSettings.Click += (_, _) => OpenDisplaySettings();

        page.Controls.Add(layout);
        AddStep(new StepInfo("Arrange", "Position your displays", Theme.Glyphs.Arrange,
            "Set where each room's screens sit. You only need to do this once per room."), page, LoadArrangeTab);
    }

    private void LoadArrangeTab()
    {
        RefreshState();
        var keep = ArrangeRoom ?? SelectedRoom;
        _loading = true;
        try
        {
            _arrangeRoom.Items.Clear();
            foreach (var room in _draft.Rooms) _arrangeRoom.Items.Add(new RoomItem(room, CheckRoom(room).Ok));
            var index = keep == null ? 0 : Math.Max(0, _draft.Rooms.IndexOf(keep));
            if (_arrangeRoom.Items.Count > 0) _arrangeRoom.SelectedIndex = Math.Min(index, _arrangeRoom.Items.Count - 1);
        }
        finally
        {
            _loading = false;
        }
        if (ArrangeRoom is { } r) LoadArrangement(r);
        else ShowArrangeMessage("Add a room on the Rooms step first.", Theme.Subtext);
    }

    // ---- building the picture ----

    private void LoadArrangement(Room room)
    {
        var screens = room.Screens.Where(s => s.MonitorIds.Count > 0).ToList();
        _arrangement = screens.Select(spec =>
        {
            var modes = ModesFor(spec);
            var item = new ArrangeItem { Spec = spec, Modes = modes };
            if (room.IsArranged)
            {
                item.Box = new Box(spec.X, spec.Y, spec.Width, spec.Height);
                item.Rotation = spec.Rotation;
                item.Size = spec.Rotation is 90 or 270 ? (spec.Height, spec.Width) : (spec.Width, spec.Height);
                item.Refresh = spec.RefreshMillihz > 0 ? spec.RefreshMillihz : Rates(modes, item.Size.Width, item.Size.Height).FirstOrDefault(60000);
                item.Scale = spec.ScalePercent;
                item.Primary = spec.Primary;
            }
            else
            {
                var sizes = Sizes(modes);
                (int Width, int Height) best = sizes.Count > 0 ? sizes[0] : (1920, 1080);
                item.Size = best;
                item.Refresh = Rates(modes, best.Width, best.Height).FirstOrDefault(60000);
            }
            return item;
        }).ToList();

        if (!room.IsArranged && _arrangement.Count > 0)
        {
            var boxes = SideBySide(_arrangement.Select(i => i.Size));
            for (var i = 0; i < boxes.Count; i++) _arrangement[i].Box = boxes[i];
            _arrangement[0].Primary = true;
        }
        if (_arrangement.Count > 0 && !_arrangement.Any(i => i.Primary)) _arrangement[0].Primary = true;

        RefreshCanvas();
        _canvas.Selected = 0;
        ShowSelectedScreen();
        _applyLayout.Enabled = _identifyArrange.Enabled = _copyFromWindows.Enabled = _arrangement.Count > 0;

        var check = CheckRoom(room);
        if (!check.Ok) ShowArrangeMessage($"✖ {check.FirstError}", Theme.Danger);
        else if (!room.IsArranged) ShowArrangeMessage($"{room.Name} isn't arranged yet. This is a suggested layout. Change it if you like, then select Apply.", Theme.Subtext);
        else ShowArrangeMessage($"This is {room.Name}'s saved layout. Change it and select Apply to try it.", Theme.Subtext);
    }

    /// <summary>The modes a screen can use: what each of its monitors supports (all of them, when mirrored).</summary>
    private List<DisplayMode> ModesFor(ScreenSpec spec)
    {
        var resolution = MonitorResolver.Resolve(_draft, _state);
        var perMonitor = new List<IReadOnlyList<DisplayMode>>();
        foreach (var id in spec.MonitorIds)
        {
            var modes = new List<DisplayMode>();
            if (_draft.FindMonitor(id)?.KnownModes is { } known) modes.AddRange(known);
            if (resolution.Find(id) is { } connected)
            {
                try { modes.AddRange(_host.Display.Modes(connected.DevicePath)); }
                catch (Exception) { /* leave it to the remembered list */ }
            }
            if (modes.Count > 0) perMonitor.Add(modes.Distinct().ToList());
        }
        var common = Common(perMonitor);
        if (common.Count == 0 && perMonitor.Count > 0) common = perMonitor[0].ToList();
        if (common.Count == 0) common = [new DisplayMode(1920, 1080, 60000)];
        return common.OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.RefreshMillihz).ToList();
    }

    private string ScreenLabel(ArrangeItem item) =>
        string.Join(" = ", item.Spec.MonitorIds.Select(id => _draft.FindMonitor(id)?.Name ?? "?"));

    private void RefreshCanvas()
    {
        _canvas.Screens = _arrangement.Select(i =>
            new CanvasScreen(ScreenLabel(i), $"{i.Box.Width} × {i.Box.Height} · {i.Refresh / 1000.0:0.##} Hz", i.Box, i.Primary)).ToList();
    }

    private ArrangeItem? SelectedItem => _canvas.Selected >= 0 && _canvas.Selected < _arrangement.Count ? _arrangement[_canvas.Selected] : null;

    private void ShowSelectedScreen()
    {
        var item = SelectedItem;
        foreach (var c in new Control[] { _resolution, _refresh, _orientation, _scale, _mainDisplay }) c.Enabled = item != null;
        if (item == null) return;
        _loading = true;
        try
        {
            _resolution.Items.Clear();
            foreach (var (w, h) in Sizes(item.Modes)) _resolution.Items.Add($"{w} × {h}");
            _resolution.SelectedItem = $"{item.Size.Width} × {item.Size.Height}";
            if (_resolution.SelectedIndex < 0)
            {
                _resolution.Items.Insert(0, $"{item.Size.Width} × {item.Size.Height}");
                _resolution.SelectedIndex = 0;
            }

            FillRefresh(item);

            _orientation.Items.Clear();
            foreach (var (label, _) in Orientations) _orientation.Items.Add(label);
            _orientation.SelectedIndex = Math.Max(0, Array.FindIndex(Orientations, o => o.Degrees == item.Rotation));

            _scale.Items.Clear();
            _scale.Items.Add("Default");
            foreach (var step in ScaleSteps) _scale.Items.Add($"{step}%");
            _scale.SelectedIndex = item.Scale is int sc && Array.IndexOf(ScaleSteps, sc) is >= 0 and var at ? at + 1 : 0;

            _mainDisplay.Checked = item.Primary;
        }
        finally
        {
            _loading = false;
        }
    }

    private void FillRefresh(ArrangeItem item)
    {
        _refresh.Items.Clear();
        var rates = Rates(item.Modes, item.Size.Width, item.Size.Height);
        if (!rates.Contains(item.Refresh)) item.Refresh = rates.FirstOrDefault(item.Refresh);
        foreach (var r in rates) _refresh.Items.Add($"{r / 1000.0:0.##} Hz");
        _refresh.SelectedItem = $"{item.Refresh / 1000.0:0.##} Hz";
        if (_refresh.SelectedIndex < 0 && _refresh.Items.Count > 0) _refresh.SelectedIndex = 0;
    }

    // ---- changes ----

    private void MoveScreen(int index, Box box)
    {
        var boxes = _arrangement.Select(i => i.Box).ToList();
        boxes[index] = box;
        if (!IsValid(boxes)) boxes = Arrangement.Resize(boxes, index, box.Width, box.Height); // re-attach screens left floating
        for (var i = 0; i < boxes.Count; i++) _arrangement[i].Box = boxes[i];
        CommitArrangement();
    }

    private void ChangeResolution()
    {
        if (SelectedItem is not { } item || _resolution.SelectedItem is not string text) return;
        var parts = text.Split('×', StringSplitOptions.TrimEntries);
        item.Size = (int.Parse(parts[0]), int.Parse(parts[1]));
        ResizeSelected(item);
        FillRefreshKeepingLoading(item);
    }

    private void FillRefreshKeepingLoading(ArrangeItem item)
    {
        _loading = true;
        try { FillRefresh(item); }
        finally { _loading = false; }
        CommitArrangement();
    }

    private void ChangeRefresh()
    {
        if (SelectedItem is not { } item || _refresh.SelectedItem is not string text) return;
        item.Refresh = (int)Math.Round(double.Parse(text.Replace(" Hz", ""), System.Globalization.CultureInfo.CurrentCulture) * 1000);
        CommitArrangement();
    }

    private void ChangeOrientation()
    {
        if (SelectedItem is not { } item || _orientation.SelectedIndex < 0) return;
        item.Rotation = Orientations[_orientation.SelectedIndex].Degrees;
        ResizeSelected(item);
        CommitArrangement();
    }

    private void ChangeScale()
    {
        if (SelectedItem is not { } item) return;
        item.Scale = _scale.SelectedIndex <= 0 ? null : ScaleSteps[_scale.SelectedIndex - 1];
        CommitArrangement();
    }

    private void ChangeMain()
    {
        if (SelectedItem is not { } item) return;
        if (!_mainDisplay.Checked)
        {
            // There's always one main display: to change it, tick it on another screen.
            _loading = true;
            _mainDisplay.Checked = true;
            _loading = false;
            return;
        }
        foreach (var other in _arrangement) other.Primary = other == item;
        CommitArrangement();
    }

    private void ResizeSelected(ArrangeItem item)
    {
        var (w, h) = item.Rotation is 90 or 270 ? (item.Size.Height, item.Size.Width) : item.Size;
        var boxes = Arrangement.Resize(_arrangement.Select(i => i.Box).ToList(), _arrangement.IndexOf(item), w, h);
        for (var i = 0; i < boxes.Count; i++) _arrangement[i].Box = boxes[i];
    }

    /// <summary>Writes the picture into the room (the draft: Save or Finish keeps it) and redraws.</summary>
    private void CommitArrangement()
    {
        foreach (var item in _arrangement)
        {
            var s = item.Spec;
            (s.X, s.Y, s.Width, s.Height) = (item.Box.X, item.Box.Y, item.Box.Width, item.Box.Height);
            s.RefreshMillihz = item.Refresh;
            s.Rotation = item.Rotation;
            s.ScalePercent = item.Scale;
            s.Primary = item.Primary;
        }
        MarkDirty();
        RefreshCanvas();
        if (ArrangeRoom is { } room)
            ShowArrangeMessage($"Changes made. Select Apply to try them, then Save or Finish to keep them.", Theme.Subtext);
    }

    // ---- applying ----

    private async Task ApplyLayoutAsync()
    {
        if (ArrangeRoom is not { } room || _arrangement.Count == 0) return;
        if (!IsValid(_arrangement.Select(i => i.Box).ToList()))
        {
            ShowArrangeMessage("✖ The screens need to touch edge to edge without overlapping. Drag them together and try again.", Theme.Danger);
            return;
        }
        CommitArrangement();
        RefreshState();
        var plan = RoomPlanner.Build(_draft, room, _state);
        if (!plan.Ok)
        {
            ShowArrangeMessage($"✖ {plan.FirstError}", Theme.Danger);
            return;
        }

        _applyLayout.Enabled = false;
        UseWaitCursor = true;
        try
        {
            var restore = _host.Display.CaptureRestorePoint();
            var result = _host.Display.Apply(plan.Plan!);
            if (!result.Ok)
            {
                _host.Display.Restore(restore);
                ShowArrangeMessage($"✖ {result.Message} Your previous display settings have been restored.", Theme.Danger);
                return;
            }
            await (_host.Listener?.WaitAsync(_draft.Settings.SettleDelayMs) ?? Task.Delay(1500));
            if (_host.Display.Query().Screens.Count == 0)
            {
                _host.Display.Restore(restore);
                ShowArrangeMessage("✖ No display came on, so your previous display settings have been restored.", Theme.Danger);
                return;
            }
            UseWaitCursor = false;

            if (!ThemedDialog.AskKeepDisplaySettings(this))
            {
                _host.Display.Restore(restore);
                await (_host.Listener?.WaitAsync(_draft.Settings.SettleDelayMs) ?? Task.Delay(1500));
                RefreshState();
                ShowArrangeMessage("Restored your previous display settings. The layout is still in this room, so you can change it and select Apply again.", Theme.Subtext);
                return;
            }

            _host.Service.Layouts.LastRoomId = room.Id;
            RefreshState();
            // Keep exactly what Windows settled on (it can nudge positions by a pixel or two).
            RoomEditor.CaptureArrangement(_draft, room, _state, acceptWindowsRouting: false);
            MarkDirty();
            LoadArrangement(room);
            ShowArrangeMessage($"✔ {room.Name} is on with this layout. Select Save or Finish to keep it.", Theme.Success);
        }
        finally
        {
            UseWaitCursor = false;
            _applyLayout.Enabled = true;
        }
    }

    /// <summary>The old way: take whatever Windows is showing now as this room's layout.</summary>
    private void CopyLayoutFromWindows()
    {
        if (ArrangeRoom is not { } room) return;
        RefreshState();
        var outcome = RoomEditor.CaptureArrangement(_draft, room, _state, acceptWindowsRouting: false);
        if (outcome == CaptureOutcome.RoutingDiffers)
        {
            var answer = ThemedDialog.Show(this,
                $"Windows is showing different displays than {room.Name} uses (or duplicates them differently).\n\n" +
                $"Copy what Windows shows now as {room.Name}'s displays and layout?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            outcome = RoomEditor.CaptureArrangement(_draft, room, _state, acceptWindowsRouting: true);
        }
        switch (outcome)
        {
            case CaptureOutcome.Saved:
            case CaptureOutcome.SavedWithWindowsRouting:
                MarkDirty();
                LoadArrangement(room);
                ShowArrangeMessage($"✔ Copied the layout Windows is showing into {room.Name}.", Theme.Success);
                break;
            case CaptureOutcome.UnknownMonitor:
                ShowArrangeMessage("✖ A display that's on isn't in the list of displays. Go to the Displays step and select Detect displays.", Theme.Danger);
                break;
            case CaptureOutcome.NothingActive:
                ShowArrangeMessage("✖ No displays are on.", Theme.Danger);
                break;
        }
    }

    private void ShowArrangeMessage(string text, Color color)
    {
        _arrangeStatus.ForeColor = color;
        _arrangeStatus.Text = text;
    }
}
