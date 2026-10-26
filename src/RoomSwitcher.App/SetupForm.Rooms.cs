using RoomSwitcher.App.Ui;
using RoomSwitcher.Core.Audio;
using RoomSwitcher.Core.Devices;
using RoomSwitcher.Core.Hotkeys;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Rooms;
using RoomSwitcher.Windows.Devices;

namespace RoomSwitcher.App;

// Step 2: rooms, each edited in a routing grid like a matrix switcher panel.
// One row per monitor; columns Off, Screen 1, Screen 2, ... and a spare "New screen" column.
internal sealed partial class SetupForm
{
    private const string RoutedMark = "●";
    private const string UnroutedMark = "○";

    private readonly ListBox _roomList = new()
    {
        Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed,
        BackColor = Theme.Card, Font = Theme.Body, ItemHeight = 44,
    };
    private readonly TextField _roomNameField = new() { Dock = DockStyle.Fill, BackColor = Theme.Card, Margin = new Padding(0, 6, 0, 12) };
    private TextBox _roomName => _roomNameField.Box;
    private readonly HotkeyBox _roomHotkey = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0), AccessibleName = "Keyboard shortcut" };
    private readonly Label _roomHotkeyNote = new()
    {
        AutoSize = true, Font = Theme.Small, ForeColor = Theme.Warning, Visible = false, Margin = new Padding(0, 4, 0, 0),
    };
    private readonly DataGridView _routing = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
        AllowUserToResizeColumns = false, ReadOnly = true, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect, ShowCellToolTips = true,
    };
    private readonly Label _roomStatus = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(14, 0, 0, 0) };
    private readonly TableLayoutPanel _roomEditor = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty, BackColor = Theme.Card };
    private readonly ModernButton _deleteRoom = new("Delete", ButtonKind.Secondary, Theme.Glyphs.Delete) { GlyphColor = Theme.Subtext };
    private readonly ModernButton _moveUp = new("", ButtonKind.Secondary, Theme.Glyphs.Up) { AccessibleName = "Move room up", Width = 44 };
    private readonly ModernButton _moveDown = new("", ButtonKind.Secondary, Theme.Glyphs.Down) { AccessibleName = "Move room down", Width = 44 };
    private readonly ModernButton _duplicateRoom = new("Duplicate", ButtonKind.Secondary, Theme.Glyphs.Copy);
    private (int Row, int Column) _routeHover = (-1, -1);
    private readonly ComboBox _addDevice = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Font = Theme.Body, FlatStyle = FlatStyle.System,
        AccessibleName = "Add a device to this room", Margin = new Padding(0, 6, 14, 0), IntegralHeight = false, MaxDropDownItems = 12,
    };
    private readonly DataGridView _roomDevices = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
        AllowUserToResizeColumns = false, ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AccessibleName = "Devices in this room",
    };

    /// <summary>One entry in the Add a device list. A null id is the "Add a device…" prompt itself.</summary>
    private sealed record DeviceChoice(string? Id, string Label)
    {
        public override string ToString() => Label;
    }
    private readonly ComboBox _roomOutput = SoundBox("Speakers");
    private readonly ComboBox _roomInput = SoundBox("Microphone");

    /// <summary>One entry in a sound list. A null device means "No change".</summary>
    private sealed record SoundChoice(AudioDeviceRef? Device, string Label)
    {
        public override string ToString() => Label;
    }

    private static ComboBox SoundBox(string name) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Font = Theme.Body, FlatStyle = FlatStyle.System,
        AccessibleName = name, Margin = new Padding(0, 6, 14, 0), IntegralHeight = false, MaxDropDownItems = 12,
    };

    private Room? SelectedRoom => _roomList.SelectedItem is RoomItem item ? item.Room : null;

    private sealed record RoomItem(Room Room, bool Usable)
    {
        public override string ToString() => Usable ? Room.Name : $"{Room.Name}  (needs attention)";
    }

    private void BuildRoomsTab()
    {
        var page = Page();
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Card };
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Left: the room list and its buttons.
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 0, 22, 0), BackColor = Theme.Card };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(SectionLabel("Rooms"), 0, 0);
        var listFrame = new RoundedPanel { Dock = DockStyle.Fill, Radius = 10, BackColor = Theme.Card, Padding = new Padding(6), Margin = new Padding(0, 6, 0, 0) };
        listFrame.Controls.Add(_roomList);
        left.Controls.Add(listFrame, 0, 1);
        var roomButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, BackColor = Theme.Card, Margin = new Padding(0, 12, 0, 0) };
        var add = new ModernButton("Add", ButtonKind.Primary, Theme.Glyphs.Add);
        foreach (var b in new[] { add, _duplicateRoom, _deleteRoom, _moveUp, _moveDown }) b.Margin = new Padding(0, 0, 8, 8);
        add.Click += (_, _) => AddRoom();
        _duplicateRoom.Click += (_, _) => { if (SelectedRoom is { } r) SelectRoom(RoomEditor.Duplicate(_draft, r), focusName: true); };
        _deleteRoom.Click += (_, _) => DeleteSelectedRoom();
        _moveUp.Click += (_, _) => MoveSelectedRoom(-1);
        _moveDown.Click += (_, _) => MoveSelectedRoom(+1);
        roomButtons.Controls.AddRange([add, _duplicateRoom, _deleteRoom, _moveUp, _moveDown]);
        left.Controls.Add(roomButtons, 0, 2);
        split.Controls.Add(left, 0, 0);

        // Right: name, routing grid, and what's wrong (if anything).
        var right = _roomEditor;
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowCount = 8;
        // Name and keyboard shortcut side by side.
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, AutoSize = true, BackColor = Theme.Card, Margin = new Padding(0, 0, 0, 12),
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.Controls.Add(SectionLabel("Room name"), 0, 0);
        top.Controls.Add(SectionLabel("Keyboard shortcut"), 1, 0);
        _roomNameField.Margin = new Padding(0, 6, 14, 0);
        _roomName.AccessibleName = "Room name";
        top.Controls.Add(_roomNameField, 0, 1);
        top.Controls.Add(_roomHotkey, 1, 1);
        top.Controls.Add(_roomHotkeyNote, 1, 2);
        right.Controls.Add(top, 0, 0);
        right.SetRowSpan(top, 2);
        var help = Paragraph(
            "Select a circle to choose what each display shows. Displays in the same Screen column show the same " +
            "picture (duplicated). Off means the display gets no signal.");
        help.Font = Theme.Body;
        help.ForeColor = Theme.Subtext;
        right.Controls.Add(help, 0, 2);

        GridStyle.Apply(_routing);
        _routing.DefaultCellStyle.SelectionBackColor = Theme.Card;
        var frame = new RoundedPanel { Dock = DockStyle.Fill, ClipChild = true, Radius = 10, BackColor = Theme.Card, Margin = new Padding(0) };
        frame.Controls.Add(_routing);
        right.Controls.Add(frame, 0, 3);
        // Check again and the result share a line, to leave the displays grid as much room as possible.
        var gridButtons = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Card, Margin = new Padding(0, 12, 0, 0) };
        gridButtons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        gridButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var check = new ModernButton("Check again", ButtonKind.Secondary, Theme.Glyphs.Refresh) { Margin = new Padding(0) };
        check.Click += (_, _) => { RefreshState(); LoadRouting(); };
        gridButtons.Controls.Add(check, 0, 0);
        gridButtons.Controls.Add(_roomStatus, 1, 0);
        gridButtons.SizeChanged += (_, _) =>
            _roomStatus.MaximumSize = new Size(Math.Max(100, gridButtons.ClientSize.Width - check.Width - _roomStatus.Margin.Horizontal), 0);
        // Sound: which speakers and microphone Windows uses in this room.
        // Sound and devices: the three choices down the left, the room's devices in a fixed, scrolling list on the right.
        var extras = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, AutoSize = true, BackColor = Theme.Card, Margin = new Padding(0, 14, 0, 0),
        };
        extras.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        extras.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 3; i++)
        {
            extras.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            extras.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        }
        _roomInput.Margin = _addDevice.Margin = _roomOutput.Margin;
        extras.Controls.Add(SectionLabel("Speakers"), 0, 0);
        extras.Controls.Add(_roomOutput, 0, 1);
        extras.Controls.Add(SectionLabel("Microphone"), 0, 2);
        extras.Controls.Add(_roomInput, 0, 3);
        extras.Controls.Add(SectionLabel("Devices"), 0, 4);
        extras.Controls.Add(_addDevice, 0, 5);

        GridStyle.Apply(_roomDevices);
        _roomDevices.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Devices in this room", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _roomDevices.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "remove", HeaderText = "", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 64, FlatStyle = FlatStyle.Flat,
            ToolTipText = "Remove from this room",
        });
        _roomDevices.CellMouseEnter += (_, e) => { _removeHover = e.ColumnIndex >= 0 && _roomDevices.Columns[e.ColumnIndex].Name == "remove" ? e.RowIndex : -1; _roomDevices.Invalidate(); };
        _roomDevices.CellMouseLeave += (_, _) => { _removeHover = -1; _roomDevices.Invalidate(); };
        foreach (DataGridViewColumn c in _roomDevices.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        _roomDevices.CellPainting += PaintRoomDeviceCell;
        _roomDevices.Paint += (_, e) =>
        {
            if (_roomDevices.Rows.Count > 0) return;
            var below = _roomDevices.ColumnHeadersHeight;
            TextRenderer.DrawText(e.Graphics, "No devices yet. Add them with the Devices list.", Theme.Body,
                new Rectangle(14, below, _roomDevices.Width - 28, _roomDevices.Height - below), Theme.Subtext,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        };
        _roomDevices.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _roomDevices.Columns[e.ColumnIndex].Name == "remove") RemoveRoomDevice(e.RowIndex);
        };
        _roomDevices.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && _roomDevices.CurrentRow is { } row) { e.Handled = true; RemoveRoomDevice(row.Index); }
        };
        var devicesFrame = new RoundedPanel { Dock = DockStyle.Fill, ClipChild = true, Radius = 10, BackColor = Theme.Card, Margin = new Padding(0, 4, 0, 0) };
        devicesFrame.Controls.Add(_roomDevices);
        extras.Controls.Add(devicesFrame, 1, 0);
        extras.SetRowSpan(devicesFrame, 6);
        _addDevice.SelectedIndexChanged += (_, _) => OnDeviceAdded();
        right.Controls.Add(extras, 0, 4);
        right.Controls.Add(gridButtons, 0, 6);
        _roomOutput.SelectedIndexChanged += (_, _) => OnSoundChosen(_roomOutput, AudioFlow.Output);
        _roomInput.SelectedIndexChanged += (_, _) => OnSoundChosen(_roomInput, AudioFlow.Input);
        split.Controls.Add(_roomEditor, 1, 0);

        _roomList.DrawItem += DrawRoomItem;
        _roomList.SelectedIndexChanged += (_, _) => { _roomList.Invalidate(); if (!_loading) LoadRouting(); };
        _roomName.TextChanged += (_, _) =>
        {
            if (_loading || SelectedRoom is not { } room) return;
            room.Name = _roomName.Text.Trim();
            MarkDirty();
            UpdateRoomStatus(room, refreshListItem: true);
        };
        _roomHotkey.ValueChanged += (_, _) =>
        {
            if (_loading || SelectedRoom is not { } room) return;
            room.Hotkey = _roomHotkey.Value;
            MarkDirty();
            ShowShortcutNote(_roomHotkeyNote, ShortcutProblem(room.Hotkey, room.Id));
        };
        _routing.CellClick += (_, e) => OnRoutingCellClick(e.RowIndex, e.ColumnIndex);
        _routing.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Space or Keys.Enter && _routing.CurrentCell is { } cell)
            {
                e.Handled = true;
                OnRoutingCellClick(cell.RowIndex, cell.ColumnIndex);
            }
        };
        _routing.CellMouseEnter += (_, e) => { _routeHover = (e.RowIndex, e.ColumnIndex); if (e.RowIndex >= 0 && e.ColumnIndex >= 0) _routing.InvalidateCell(e.ColumnIndex, e.RowIndex); };
        _routing.CellMouseLeave += (_, e) => { _routeHover = (-1, -1); if (e.RowIndex >= 0 && e.ColumnIndex >= 0) _routing.InvalidateCell(e.ColumnIndex, e.RowIndex); };
        _routing.CellPainting += PaintRoutingCell;

        page.Controls.Add(split);
        AddStep(new StepInfo("Rooms", "Create and manage rooms", Theme.Glyphs.Rooms,
            "Choose which displays each room turns on, and which ones show the same picture."), page, () => LoadRoomList());
    }

    /// <summary>"No change", then every device Windows knows; a saved device that's missing is kept and marked.</summary>
    private void FillSoundBox(ComboBox box, IReadOnlyList<AudioDevice> devices, AudioDeviceRef? chosen)
    {
        box.BeginUpdate();
        box.Items.Clear();
        box.Items.Add(new SoundChoice(null, "No change"));
        var selected = 0;
        foreach (var d in devices)
        {
            box.Items.Add(new SoundChoice(AudioRules.ToRef(d), d.Connected ? AudioLabel(d) : $"{AudioLabel(d)} (not connected)"));
            if (chosen != null && AudioRules.Find([d], chosen) != null && selected == 0) selected = box.Items.Count - 1;
        }
        if (chosen != null && selected == 0)
        {
            box.Items.Add(new SoundChoice(chosen, $"{chosen.Name} (not found)"));
            selected = box.Items.Count - 1;
        }
        box.SelectedIndex = selected;
        box.EndUpdate();
    }

    private void OnSoundChosen(ComboBox box, AudioFlow flow)
    {
        if (_loading || SelectedRoom is not { } room || box.SelectedItem is not SoundChoice choice) return;
        var device = choice.Device == null ? null : new AudioDeviceRef { Id = choice.Device.Id, Name = choice.Device.Name };
        if (flow == AudioFlow.Output) room.AudioOutput = device;
        else room.AudioInput = device;
        MarkDirty();
    }

    /// <summary>
    /// The devices this room uses, as tags, and the Add a device list of the rest. For devices rooms
    /// switch, being in the room means on in it; any other device is just listed for the room (and
    /// tested in Test devices).
    /// </summary>
    private void LoadRoomDevices(Room? room)
    {
        if (_physicals.Count == 0)
        {
            try { _physicals = UsbDevices.ListPhysical(); }
            catch (Exception) { _physicals = []; }
        }
        var wasLoading = _loading;
        _loading = true;
        _roomDevices.Rows.Clear();
        _addDevice.BeginUpdate();
        _addDevice.Items.Clear();
        try
        {
            _addDevice.Enabled = room != null;
            if (room == null) return;
            var inRoom = UsbRules.DevicesIn(_draft, room);
            var all = _physicals.Select(p => p.Device.Id)
                .Concat(_draft.UsbDevices.Select(d => d.Id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var id in inRoom)
            {
                // "Remove" isn't drawn (the cell shows a trash can) but names the button for screen readers.
                var row = _roomDevices.Rows.Add(DeviceLabel(id, forList: false), "Remove");
                _roomDevices.Rows[row].Tag = id;
            }
            GridStyle.Size(_roomDevices, rowHeight: 44, headerHeight: 40);
            _roomDevices.ClearSelection();

            var rest = all.Where(id => !inRoom.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
            _addDevice.Items.Add(new DeviceChoice(null, rest.Count > 0 ? "Add a device…" : all.Count > 0 ? "All devices added" : "No devices found"));
            foreach (var id in rest) _addDevice.Items.Add(new DeviceChoice(id, DeviceLabel(id, forList: true)));
            _addDevice.SelectedIndex = 0;
            _addDevice.Enabled = rest.Count > 0;
        }
        finally
        {
            _addDevice.EndUpdate();
            _loading = wasLoading;
        }
    }

    private void PaintRoomDeviceCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _roomDevices.Columns[e.ColumnIndex].Name != "remove") return;
        GridStyle.PaintIconButton(e, Theme.Glyphs.Delete, _removeHover == e.RowIndex);
        e.Handled = true;
    }

    private int _removeHover = -1;

    private void RemoveRoomDevice(int rowIndex)
    {
        if (SelectedRoom is not { } room || _roomDevices.Rows[rowIndex].Tag is not string id) return;
        UsbRules.SetInRoom(_draft, room, id, false);
        MarkDirty();
        BeginInvoke(() => LoadRoomDevices(SelectedRoom));
    }

    private string DeviceLabel(string id, bool forList)
    {
        var physical = _physicals.FirstOrDefault(p => string.Equals(p.Device.Id, id, StringComparison.OrdinalIgnoreCase));
        var info = UsbRules.Find(_draft, id);
        var name = info?.DisplayName ?? physical?.Device.Name ?? id;
        if (physical == null) name += " (not connected)";
        if (forList && info?.Managed == true) name += " · switched by room";
        return name;
    }

    private void OnDeviceAdded()
    {
        if (_loading || SelectedRoom is not { } room || _addDevice.SelectedItem is not DeviceChoice { Id: { } id }) return;
        // Keep its name for when it's unplugged.
        if (_physicals.FirstOrDefault(p => string.Equals(p.Device.Id, id, StringComparison.OrdinalIgnoreCase)) is { } physical)
            UsbRules.Entry(_draft, physical.Device);
        UsbRules.SetInRoom(_draft, room, id, true);
        MarkDirty();
        BeginInvoke(() => LoadRoomDevices(SelectedRoom));
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text, AutoSize = true, Font = Theme.BodySemibold, ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 2),
    };

    private void DrawRoomItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var g = e.Graphics;
        using (var bg = new SolidBrush(Theme.Card)) g.FillRectangle(bg, e.Bounds);
        Theme.Smooth(g);
        var s = Theme.Scale(_roomList);
        var item = (RoomItem)_roomList.Items[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;
        var r = new RectangleF(e.Bounds.X + 1, e.Bounds.Y + 2 * s, e.Bounds.Width - 2, e.Bounds.Height - 4 * s);
        if (selected)
        {
            using var path = Theme.RoundedRect(r, 8 * s);
            using var fill = new SolidBrush(Theme.NavSelected);
            g.FillPath(fill, path);
        }
        var dotSize = 8 * s;
        var dot = new RectangleF(r.X + 12 * s, r.Y + (r.Height - dotSize) / 2, dotSize, dotSize);
        using (var b = new SolidBrush(item.Usable ? Theme.SuccessDot : Color.FromArgb(220, 60, 50))) g.FillEllipse(b, dot);
        var textRect = new Rectangle((int)(dot.Right + 10 * s), (int)r.Y, (int)(r.Right - dot.Right - 16 * s), (int)r.Height);
        TextRenderer.DrawText(g, item.ToString(), selected ? Theme.BodySemibold : Theme.Body, textRect, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private void PaintRoutingCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 1) return;
        var row = _routing.Rows[e.RowIndex];
        var connected = row.Tag is MonitorInfo m && IsConnected(m);
        GridStyle.PaintBackground(e, row.DefaultCellStyle.BackColor.IsEmpty ? Theme.Card : row.DefaultCellStyle.BackColor);
        GridStyle.PaintRoute(e, e.Value as string == RoutedMark, _routeHover == (e.RowIndex, e.ColumnIndex), connected);
        if (_routing.Focused && _routing.CurrentCell is { } cur && cur.RowIndex == e.RowIndex && cur.ColumnIndex == e.ColumnIndex)
            ControlPaint.DrawFocusRectangle(e.Graphics!, Rectangle.Inflate(e.CellBounds, -4, -4));
        e.Handled = true;
    }

    private void LoadRoomList(Room? select = null)
    {
        select ??= SelectedRoom ?? _draft.Rooms.FirstOrDefault();
        _loading = true;
        try
        {
            _roomList.BeginUpdate();
            _roomList.Items.Clear();
            foreach (var room in _draft.Rooms)
                _roomList.Items.Add(new RoomItem(room, CheckRoom(room).Ok));
            var index = select == null ? -1 : _draft.Rooms.IndexOf(select);
            _roomList.SelectedIndex = index >= 0 ? index : (_draft.Rooms.Count > 0 ? 0 : -1);
            _roomList.EndUpdate();
        }
        finally
        {
            _loading = false;
        }
        LoadRouting();
    }

    private void SelectRoom(Room room, bool focusName)
    {
        MarkDirty();
        LoadRoomList(room);
        if (focusName)
        {
            _roomName.Focus();
            _roomName.SelectAll();
        }
    }

    private void AddRoom()
    {
        var room = RoomEditor.AddRoom(_draft);
        room.Screens.Clear();
        // A new room starts with every connected monitor as its own screen; click to change.
        var resolution = MonitorResolver.Resolve(_draft, _state);
        foreach (var m in _draft.Monitors.Where(m => resolution.Find(m.Id) != null))
            room.Screens.Add(new ScreenSpec { MonitorIds = { m.Id } });
        SelectRoom(room, focusName: true);
    }

    private void DeleteSelectedRoom()
    {
        if (SelectedRoom is not { } room) return;
        if (ThemedDialog.Show(this, $"Delete the room \"{room.Name}\"?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        var index = _draft.Rooms.IndexOf(room);
        RoomEditor.Delete(_draft, room);
        MarkDirty();
        LoadRoomList(_draft.Rooms.ElementAtOrDefault(Math.Min(index, _draft.Rooms.Count - 1)));
    }

    private void MoveSelectedRoom(int delta)
    {
        if (SelectedRoom is { } room && RoomEditor.Move(_draft, room, delta))
        {
            MarkDirty();
            LoadRoomList(room);
        }
    }

    private void LoadRouting()
    {
        var room = SelectedRoom;
        _roomEditor.Enabled = room != null;
        _duplicateRoom.Enabled = _deleteRoom.Enabled = room != null;
        _moveUp.Enabled = room != null && _draft.Rooms.IndexOf(room) > 0;
        _moveDown.Enabled = room != null && _draft.Rooms.IndexOf(room) < _draft.Rooms.Count - 1;

        _loading = true;
        try
        {
            _roomName.Text = room?.Name ?? "";
            _roomHotkey.Value = room?.Hotkey ?? "";
            FillSoundBox(_roomOutput, _outputs, room?.AudioOutput);
            FillSoundBox(_roomInput, _inputs, room?.AudioInput);
            LoadRoomDevices(room);
            ShowShortcutNote(_roomHotkeyNote, room == null ? null : ShortcutProblem(room.Hotkey, room.Id));
            _routing.Rows.Clear();
            _routing.Columns.Clear();
            if (room == null)
            {
                _roomStatus.Text = _draft.Rooms.Count == 0 ? "Select Add to create your first room." : "";
                return;
            }

            _routing.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Display", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 140, SortMode = DataGridViewColumnSortMode.NotSortable });
            AddRoutingColumn("Off", "Off in this room. The display gets no signal.");
            for (var i = 0; i < room.Screens.Count; i++)
            {
                var mirrored = room.Screens[i].MonitorIds.Count > 1;
                AddRoutingColumn(mirrored ? $"Screen {i + 1}\n(duplicated)" : $"Screen {i + 1}",
                    "Every display in this column shows the same picture (duplicated).");
            }
            AddRoutingColumn("New\nscreen", "Select to give this display its own screen.");

            foreach (var m in _draft.Monitors)
            {
                var connected = IsConnected(m);
                var cells = new List<object> { connected ? MonitorLabel(m) : $"{MonitorLabel(m)} (not connected)" };
                var screenIndex = room.Screens.FindIndex(s => s.MonitorIds.Contains(m.Id));
                cells.Add(screenIndex < 0 ? RoutedMark : UnroutedMark);
                for (var i = 0; i < room.Screens.Count; i++) cells.Add(i == screenIndex ? RoutedMark : UnroutedMark);
                cells.Add(UnroutedMark);
                var row = _routing.Rows.Add(cells.ToArray());
                _routing.Rows[row].Tag = m;
                if (!connected) _routing.Rows[row].DefaultCellStyle.ForeColor = DisconnectedColor;
            }
            GridStyle.Size(_routing, rowHeight: 54, headerHeight: 60);
            _routing.ClearSelection();
        }
        finally
        {
            _loading = false;
        }
        UpdateRoomStatus(room);
    }

    private void AddRoutingColumn(string header, string tip)
    {
        // Button cells: a click, Space, or an accessibility tool's "invoke" all route the monitor.
        var column = new DataGridViewButtonColumn
        {
            HeaderText = header, ToolTipText = tip, Width = (int)(88 * Theme.Scale(_routing)), SortMode = DataGridViewColumnSortMode.NotSortable,
            FlatStyle = FlatStyle.Flat,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter, Font = new Font(Font.FontFamily, Font.Size * 1.3f) },
        };
        column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        column.HeaderCell.Style.Padding = new Padding(0);
        _routing.Columns.Add(column);
    }

    private void OnRoutingCellClick(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 1 || SelectedRoom is not { } room) return;
        var monitor = (MonitorInfo)_routing.Rows[rowIndex].Tag!;
        var wasArranged = room.IsArranged && room.Screens.Any(s => s.HasGeometry);

        int? target;
        if (columnIndex == 1) target = null;                                  // Off
        else if (columnIndex - 2 < room.Screens.Count) target = columnIndex - 2; // existing screen
        else target = room.Screens.IndexOf(RoomEditor.AddScreen(room));         // New screen

        if (!RoomEditor.Route(room, monitor.Id, target))
        {
            RoomEditor.RemoveEmptyScreens(room);
            return;
        }
        RoomEditor.RemoveEmptyScreens(room);
        MarkDirty();
        LoadRouting();
        if (wasArranged)
            _roomStatus.Text += "\n\nYou changed which displays are on, so arrange this room again on the Arrange step.";
    }

    private void UpdateRoomStatus(Room room, bool refreshListItem = false)
    {
        var result = CheckRoom(room);
        var problemMonitors = result.Problems.Where(p => p.Severity == ProblemSeverity.Error && p.MonitorId != null)
            .Select(p => p.MonitorId).ToHashSet();
        foreach (DataGridViewRow row in _routing.Rows)
        {
            var m = (MonitorInfo)row.Tag!;
            row.DefaultCellStyle.BackColor = problemMonitors.Contains(m.Id) ? Theme.ErrorRow : Theme.Card;
        }

        var lines = new List<string>();
        foreach (var p in result.Problems)
            lines.Add((p.Severity == ProblemSeverity.Error ? "✖ " : "⚠ ") + p.Message);
        if (result.Ok)
        {
            var on = room.ActiveMonitorIds.Count();
            lines.Insert(0, $"✔ This room works on this PC: {on} display{(on == 1 ? "" : "s")} on, {room.Screens.Count} screen{(room.Screens.Count == 1 ? "" : "s")}.");
            if (!room.IsArranged)
                lines.Add("Not arranged yet. Windows will choose where each display goes until you arrange this room on the Arrange step.");
        }
        _roomStatus.ForeColor = result.Ok ? Theme.Success : Theme.Danger;
        _roomStatus.Text = string.Join("\n", lines);

        var index = _draft.Rooms.IndexOf(room);
        if (index >= 0 && index < _roomList.Items.Count && _roomList.Items[index] is RoomItem item && (refreshListItem || item.Usable != result.Ok))
        {
            _loading = true;
            var wasSelected = _roomList.SelectedIndex == index;
            _roomList.Items[index] = new RoomItem(room, result.Ok);
            if (wasSelected) _roomList.SelectedIndex = index;
            _loading = false;
        }
    }
}
