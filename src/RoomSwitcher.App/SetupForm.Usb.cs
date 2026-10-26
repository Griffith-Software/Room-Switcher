using System.Diagnostics;
using RoomSwitcher.App.Ui;
using RoomSwitcher.Core.Devices;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Windows.Devices;

namespace RoomSwitcher.App;

// Step 3: USB devices. Name them, see which is which (use one and its row lights up), choose which
// ones rooms may turn off, and pick the rooms each of those is on in.
internal sealed partial class SetupForm
{
    private readonly DataGridView _usbGrid = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
        AllowUserToResizeColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, EditMode = DataGridViewEditMode.EditOnEnter,
    };
    private readonly DataGridView _usbRooms = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
        AllowUserToResizeColumns = false, ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.CellSelect,
    };
    private readonly Label _usbStatus = Wrapping(new Label { Margin = new Padding(0), Font = Theme.BodySemibold, BackColor = Theme.Window });
    private readonly ModernButton _usbSetUp = new("Set up device switching", ButtonKind.Primary, Theme.Glyphs.Shield);
    private readonly ModernButton _usbRemove = new("Remove", ButtonKind.Secondary, Theme.Glyphs.Delete) { GlyphColor = Theme.Subtext };
    private readonly Label _usbRoomsEmpty = Wrapping(new Label
    {
        Margin = new Padding(0, 6, 0, 0), ForeColor = Theme.Subtext,
        Text = "Tick \"Switch by room\" for a device to choose which rooms it's on in. Devices left unticked are never changed.",
    });
    private IReadOnlyList<UsbDevices.Physical> _physicals = [];
    private readonly Dictionary<string, long> _usbFlash = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer _usbFlashTimer = new() { Interval = 120 };
    private RawInputWatcher? _rawInput;
    private (int Row, int Column) _usbRoomHover = (-1, -1);
    private readonly TableLayoutPanel _usbLayout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = Theme.Card };

    private void BuildUsbTab()
    {
        var page = Page();
        var layout = _usbLayout;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        // Banner: is device switching set up?
        var banner = new RoundedPanel
        {
            Dock = DockStyle.Fill, Radius = 10, FillColor = Theme.Window, BackColor = Theme.Card, Padding = new Padding(16, 10, 12, 10),
            Margin = new Padding(0, 0, 0, 10), Height = 62,
        };
        var bannerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Theme.Window };
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _usbStatus.Anchor = AnchorStyles.Left;
        bannerLayout.Controls.Add(_usbStatus, 0, 0);
        _usbSetUp.Anchor = _usbRemove.Anchor = AnchorStyles.Right;
        _usbSetUp.Margin = new Padding(10, 0, 0, 0);
        _usbRemove.Margin = new Padding(10, 0, 0, 0);
        bannerLayout.Controls.Add(_usbSetUp, 1, 0);
        bannerLayout.Controls.Add(_usbRemove, 2, 0);
        banner.Controls.Add(bannerLayout);
        layout.Controls.Add(banner, 0, 0);

        layout.Controls.Add(Paragraph(
            "Give your devices nicknames, then press a key on a keyboard or move a mouse, and its row lights up so you can tell them apart. " +
            "Speakers and microphones are on the Audio step."), 0, 1);

        GridStyle.Apply(_usbGrid);
        _usbGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "name", HeaderText = "Device Nickname", FillWeight = 20, MaxInputLength = 60,
            DefaultCellStyle = { Padding = new Padding(28, 17, 32, 13) },
        });
        _usbGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "windows", HeaderText = "Model name", ReadOnly = true, FillWeight = 17 });
        _usbGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "type", HeaderText = "Type", FillWeight = 17, FlatStyle = FlatStyle.Flat,
            ToolTipText = "Select to change the type, if Windows reports it wrongly (for example a mouse receiver that says it's also a keyboard).",
        });
        _usbGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "connection", HeaderText = "Connection", ReadOnly = true, FillWeight = 14 });
        _usbGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", HeaderText = "Status", ReadOnly = true, FillWeight = 18 });
        _usbGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "managed", HeaderText = "Switch by room", FillWeight = 12,
            ToolTipText = "Let rooms turn this device on and off. Devices left unticked are never changed.",
        });
        _usbGrid.Columns.Add(new DataGridViewButtonColumn { Name = "test", HeaderText = "", FillWeight = 18, FlatStyle = FlatStyle.Flat });
        foreach (DataGridViewColumn c in _usbGrid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        // Narrow columns: less padding so their titles fit.
        foreach (var narrow in new[] { "connection", "status", "managed" })
            _usbGrid.Columns[narrow]!.HeaderCell.Style.Padding = new Padding(6, 0, 2, 0);
        _usbGrid.CellPainting += PaintUsbCell;
        GridStyle.ClickToEdit(_usbGrid, "name");
        _usbGrid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is TextBox box) { box.BorderStyle = BorderStyle.None; box.BackColor = Color.White; box.Font = Theme.Body; }
        };
        _usbGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_usbGrid.CurrentCell is DataGridViewCheckBoxCell) _usbGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _usbGrid.CellValueChanged += OnUsbCellChanged;
        _usbGrid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            var device = ((UsbDevices.Physical)_usbGrid.Rows[e.RowIndex].Tag!).Device;
            switch (_usbGrid.Columns[e.ColumnIndex].Name)
            {
                case "test" when IsCamera(device):
                    OpenCameraApp();
                    break;
                case "type":
                    ShowTypeMenu(e.RowIndex, device);
                    break;
            }
        };
        var gridFrame = new RoundedPanel { Dock = DockStyle.Fill, ClipChild = true, Radius = 10, BackColor = Theme.Card, Margin = new Padding(0) };
        gridFrame.Controls.Add(_usbGrid);
        layout.Controls.Add(gridFrame, 0, 2);

        var roomsLabel = SectionLabel("On or off in each room");
        roomsLabel.Margin = new Padding(0, 16, 0, 2);
        layout.Controls.Add(roomsLabel, 0, 3);
        layout.Controls.Add(_usbRoomsEmpty, 0, 4);

        GridStyle.Apply(_usbRooms);
        _usbRooms.DefaultCellStyle.SelectionBackColor = Theme.Card;
        _usbRooms.CellPainting += PaintUsbRoomCell;
        _usbRooms.CellClick += (_, e) => ToggleUsbRoom(e.RowIndex, e.ColumnIndex);
        _usbRooms.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Space or Keys.Enter && _usbRooms.CurrentCell is { } cell)
            {
                e.Handled = true;
                ToggleUsbRoom(cell.RowIndex, cell.ColumnIndex);
            }
        };
        _usbRooms.CellMouseEnter += (_, e) => { _usbRoomHover = (e.RowIndex, e.ColumnIndex); if (e.RowIndex >= 0 && e.ColumnIndex >= 0) _usbRooms.InvalidateCell(e.ColumnIndex, e.RowIndex); };
        _usbRooms.CellMouseLeave += (_, e) => { _usbRoomHover = (-1, -1); if (e.RowIndex >= 0 && e.ColumnIndex >= 0) _usbRooms.InvalidateCell(e.ColumnIndex, e.RowIndex); };
        var roomsFrame = new RoundedPanel { Dock = DockStyle.Fill, ClipChild = true, Radius = 10, BackColor = Theme.Card, Margin = new Padding(0, 6, 0, 0) };
        roomsFrame.Controls.Add(_usbRooms);
        layout.Controls.Add(roomsFrame, 0, 5);

        _usbSetUp.Click += async (_, _) => await RunDeviceSetupAsync(DeviceHelper.InstallArg);
        _usbRemove.Click += async (_, _) =>
        {
            if (ThemedDialog.Show(this, "Remove device switching? Rooms will stop turning devices on and off. Your devices stay as they are now.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                await RunDeviceSetupAsync(DeviceHelper.RemoveArg);
        };
        _usbFlashTimer.Tick += (_, _) =>
        {
            var now = Environment.TickCount64;
            if (_usbFlash.Count == 0) { _usbFlashTimer.Stop(); return; }
            foreach (var expired in _usbFlash.Where(f => f.Value < now).Select(f => f.Key).ToList()) _usbFlash.Remove(expired);
            _usbGrid.Invalidate();
        };

        page.Controls.Add(layout);
        AddStep(new StepInfo("Devices", "USB and Bluetooth", Theme.Glyphs.Usb,
            "Keyboards, mice, webcams, controllers and other USB and Bluetooth devices. Rooms can turn the ones you choose on and off."),
            page, LoadUsbTab, LeaveUsbTab);
    }

    private void LoadUsbTab()
    {
        try { _physicals = UsbDevices.ListPhysical(); }
        catch (Exception) { _physicals = []; }

        _loading = true;
        try
        {
            _usbGrid.Rows.Clear();
            foreach (var p in _physicals)
            {
                var info = UsbRules.Find(_draft, p.Device.Id);
                if (info != null && p.Device.Connected) info.Kind = UsbRules.KindText(p.Device.Kinds);
                var kind = info is { UserKind.Length: > 0 } ? info.UserKind
                    : !p.Device.Connected && info is { Kind.Length: > 0 } ? info.Kind : UsbRules.KindText(p.Device.Kinds);
                var row = _usbGrid.Rows.Add(info?.Name ?? "", p.Device.Name, kind, p.Device.Connection,
                    !p.Device.Connected ? "Not connected" : p.Device.Enabled ? "On" : "Off",
                    info?.Managed == true, "");
                _usbGrid.Rows[row].Tag = p;
            }
            GridStyle.Size(_usbGrid, rowHeight: 48, headerHeight: 56);
            var s = Theme.Scale(_usbGrid);
            _usbGrid.Columns["name"]!.DefaultCellStyle.Padding = new Padding((int)(26 * s), (int)(14 * s), (int)(30 * s), (int)(11 * s));
            _usbGrid.ClearSelection();
        }
        finally
        {
            _loading = false;
        }
        LoadUsbRooms();
        UpdateDeviceSwitchingStatus();

        _rawInput ??= new RawInputWatcher();
        _rawInput.DeviceUsed -= OnDeviceUsed;
        _rawInput.DeviceUsed += OnDeviceUsed;
        _rawInput.Start();
    }

    private void LeaveUsbTab()
    {
        _rawInput?.Stop();
        _usbFlashTimer.Stop();
        _usbFlash.Clear();
    }

    private void OnDeviceUsed(string instanceId)
    {
        var physical = _physicals.FirstOrDefault(p => p.Members.Any(m => string.Equals(m.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase)));
        if (physical == null) return;
        _usbFlash[physical.Device.Id] = Environment.TickCount64 + 900;
        _usbGrid.Invalidate();
        _usbFlashTimer.Start();
    }

    private void UpdateDeviceSwitchingStatus()
    {
        DeviceControlStatus status;
        try { status = _host.Devices.Status; }
        catch (Exception) { status = DeviceControlStatus.NotSetUp; }

        switch (status)
        {
            case DeviceControlStatus.Ready:
                _usbStatus.Text = "✔ Device switching is set up. Switching rooms turns the devices you've chosen on and off.";
                _usbStatus.ForeColor = Theme.Success;
                _usbSetUp.Visible = false;
                _usbRemove.Visible = true;
                break;
            case DeviceControlStatus.NeedsUpdate:
                _usbStatus.Text = "Device switching was set up by an older version of Room Switcher. Select Update to bring it up to date.";
                _usbStatus.ForeColor = Theme.Warning;
                _usbSetUp.Text = "Update";
                _usbSetUp.FitWidth();
                _usbSetUp.Visible = _usbRemove.Visible = true;
                break;
            default:
                _usbStatus.Text = "Device switching isn't set up. Windows needs administrator permission to turn devices on and off, so you'll be asked to approve this once.";
                _usbStatus.ForeColor = Theme.Text;
                _usbSetUp.Text = "Set up device switching";
                _usbSetUp.FitWidth();
                _usbSetUp.Visible = true;
                _usbRemove.Visible = false;
                break;
        }
    }

    private async Task RunDeviceSetupAsync(string argument)
    {
        _usbSetUp.Enabled = _usbRemove.Enabled = false;
        UseWaitCursor = true;
        try
        {
            var (ok, error) = await Task.Run(() => (DeviceHelper.RunElevated(argument, out var e), e));
            if (!ok) ThemedDialog.Show(this, error ?? "Couldn't make that change. Try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
            _usbSetUp.Enabled = _usbRemove.Enabled = true;
            UpdateDeviceSwitchingStatus();
        }
    }

    private void OnUsbCellChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || e.RowIndex < 0) return;
        var row = _usbGrid.Rows[e.RowIndex];
        var physical = (UsbDevices.Physical)row.Tag!;
        var column = _usbGrid.Columns[e.ColumnIndex].Name;

        if (column == "name")
        {
            var name = (row.Cells["name"].Value as string)?.Trim() ?? "";
            UsbRules.Entry(_draft, physical.Device).Name = name;
            MarkDirty();
            LoadUsbRooms();
            return;
        }

        if (column != "managed") return;
        var managed = row.Cells["managed"].Value is true;
        if (managed && UsbRules.Kinds(_draft, physical.Device).HasFlag(UsbKind.Keyboard))
        {
            var answer = ThemedDialog.Show(this,
                $"{UsbRules.DisplayName(_draft, physical.Device)} is a keyboard.\n\n" +
                "If a room turns it off, you can't type with it until you switch to a room where it's on " +
                "(the tray menu, another keyboard, or a shortcut on another keyboard still work).\n\n" +
                "Room Switcher always keeps at least one keyboard and one mouse working, but check your rooms before relying on it.\n\n" +
                "Let rooms turn this keyboard off?",
                "Turn off a keyboard?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                _loading = true;
                row.Cells["managed"].Value = false;
                _loading = false;
                return;
            }
        }

        // Which rooms the device belongs to is kept either way (see SetManaged).
        UsbRules.SetManaged(_draft, UsbRules.Entry(_draft, physical.Device), managed);
        MarkDirty();
        LoadUsbRooms();
    }

    private void PaintUsbCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var physical = (UsbDevices.Physical)_usbGrid.Rows[e.RowIndex].Tag!;
        var device = physical.Device;
        var flashing = _usbFlash.ContainsKey(device.Id);
        Color? back = flashing ? Color.FromArgb(214, 245, 224) : null;

        switch (_usbGrid.Columns[e.ColumnIndex].Name)
        {
            case "name":
                GridStyle.PaintBackground(e, back);
                var editing = _usbGrid.IsCurrentCellInEditMode && _usbGrid.CurrentCell?.RowIndex == e.RowIndex;
                GridStyle.PaintInput(e, editing ? "" : e.Value as string ?? "", true);
                if (!editing && string.IsNullOrEmpty(e.Value as string))
                {
                    var r = GridStyle.InputRect(e.CellBounds);
                    TextRenderer.DrawText(e.Graphics!, "Add a nickname", Theme.Body, Rectangle.Inflate(r, -(int)(12 * r.Height / 41f), 0), Theme.Disabled,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                e.Handled = true;
                break;
            case "status":
                GridStyle.PaintBackground(e, back);
                if (!device.Connected) GridStyle.PaintPill(e, "Not connected", Theme.NeutralBack, Theme.Subtext, Theme.Disabled);
                else if (device.Enabled) GridStyle.PaintPill(e, "On", Theme.SuccessBack, Theme.Success, Theme.SuccessDot);
                else GridStyle.PaintPill(e, "Off", Theme.NeutralBack, Theme.Subtext, Theme.Disabled);
                e.Handled = true;
                break;
            case "type":
                GridStyle.PaintBackground(e, back);
                var chosen = UsbRules.Find(_draft, device.Id) is { UserKind.Length: > 0 };
                var typeRect = Rectangle.Inflate(e.CellBounds, -12, 0);
                TextRenderer.DrawText(e.Graphics!, e.Value as string, chosen ? Theme.BodySemibold : Theme.Body,
                    new Rectangle(typeRect.X, typeRect.Y, typeRect.Width - 20, typeRect.Height), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                Theme.DrawGlyph(e.Graphics!, Theme.Glyphs.Down, 8f, Theme.Subtext, new RectangleF(typeRect.Right - 18, typeRect.Y, 18, typeRect.Height));
                e.Handled = true;
                break;
            case "test":
                if (IsCamera(device))
                {
                    GridStyle.PaintCellButton(e, "Test", Theme.Glyphs.Camera, true); // opens the Camera app
                }
                else
                {
                    GridStyle.PaintBackground(e, back);
                    var hint = flashing ? "In use ✔"
                        : !device.Connected ? "Connect first"
                        : (UsbRules.Kinds(_draft, device) & (UsbKind.Keyboard | UsbKind.Mouse)) != 0 ? "Use it to test" : "";
                    TextRenderer.DrawText(e.Graphics!, hint, flashing ? Theme.BodySemibold : Theme.Small, Rectangle.Inflate(e.CellBounds, -12, 0),
                        flashing ? Theme.Success : Theme.Subtext, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                e.Handled = true;
                break;
            default:
                if (back is { } flash)
                {
                    GridStyle.PaintBackground(e, flash);
                    e.Paint(e.CellBounds, DataGridViewPaintParts.ContentForeground);
                    e.Handled = true;
                }
                break;
        }
    }

    // ---- which rooms each managed device is on in ----

    private void LoadUsbRooms()
    {
        var managed = _draft.UsbDevices.Where(d => d.Managed).ToList();
        _usbRoomsEmpty.Visible = managed.Count == 0;
        _usbRooms.Parent!.Visible = managed.Count > 0;
        // With nothing to place in rooms yet, the device list gets the room.
        _usbLayout.RowStyles[2] = new RowStyle(SizeType.Percent, managed.Count == 0 ? 100 : 55);
        _usbLayout.RowStyles[5] = new RowStyle(SizeType.Percent, managed.Count == 0 ? 0 : 45);
        _usbRooms.Rows.Clear();
        _usbRooms.Columns.Clear();
        if (managed.Count == 0 || _draft.Rooms.Count == 0) return;

        _usbRooms.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Device", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 160, SortMode = DataGridViewColumnSortMode.NotSortable,
        });
        foreach (var room in _draft.Rooms)
        {
            var column = new DataGridViewButtonColumn
            {
                HeaderText = room.Name, ToolTipText = $"Select to turn this device on or off in {room.Name}.", Width = (int)(110 * Theme.Scale(_usbRooms)),
                SortMode = DataGridViewColumnSortMode.NotSortable, FlatStyle = FlatStyle.Flat, Tag = room,
            };
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            column.HeaderCell.Style.Padding = new Padding(0);
            _usbRooms.Columns.Add(column);
        }
        foreach (var info in managed)
        {
            var plugged = _physicals.Any(p => string.Equals(p.Device.Id, info.Id, StringComparison.OrdinalIgnoreCase));
            var cells = new List<object> { plugged ? info.DisplayName : $"{info.DisplayName} (not connected)" };
            cells.AddRange(_draft.Rooms.Select(r => (object)(r.UsbOff.Contains(info.Id, StringComparer.OrdinalIgnoreCase) ? "off" : "on")));
            var row = _usbRooms.Rows.Add(cells.ToArray());
            _usbRooms.Rows[row].Tag = info;
        }
        GridStyle.Size(_usbRooms, rowHeight: 50, headerHeight: 50);
        _usbRooms.ClearSelection();
    }

    private void PaintUsbRoomCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 1) return;
        GridStyle.PaintBackground(e, Theme.Card);
        GridStyle.PaintCheck(e, e.Value as string == "on", _usbRoomHover == (e.RowIndex, e.ColumnIndex), true);
        if (_usbRooms.Focused && _usbRooms.CurrentCell is { } cur && cur.RowIndex == e.RowIndex && cur.ColumnIndex == e.ColumnIndex)
            ControlPaint.DrawFocusRectangle(e.Graphics!, Rectangle.Inflate(e.CellBounds, -4, -4));
        e.Handled = true;
    }

    private void ToggleUsbRoom(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 1) return;
        var info = (UsbDeviceInfo)_usbRooms.Rows[rowIndex].Tag!;
        var room = (Room)_usbRooms.Columns[columnIndex].Tag!;
        var off = room.UsbOff.FindIndex(id => string.Equals(id, info.Id, StringComparison.OrdinalIgnoreCase));
        if (off >= 0) room.UsbOff.RemoveAt(off);
        else room.UsbOff.Add(info.Id);
        _usbRooms.Rows[rowIndex].Cells[columnIndex].Value = off >= 0 ? "on" : "off";
        MarkDirty();
    }

    private bool IsCamera(UsbDevice device) => (device.Kinds | UsbRules.Kinds(_draft, device)).HasFlag(UsbKind.Camera);

    /// <summary>Lets the person correct the type Windows reports, e.g. a mouse receiver that also claims to be a keyboard.</summary>
    private void ShowTypeMenu(int rowIndex, UsbDevice device)
    {
        var info = UsbRules.Find(_draft, device.Id);
        var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false, ShowCheckMargin = true };
        void Choose(string? kind)
        {
            var entry = UsbRules.Entry(_draft, device);
            entry.UserKind = kind;
            _usbGrid.Rows[rowIndex].Cells["type"].Value = kind ?? UsbRules.KindText(device.Kinds);
            _usbGrid.InvalidateRow(rowIndex);
            MarkDirty();
        }
        var detected = new ToolStripMenuItem($"As Windows reports it ({UsbRules.KindText(device.Kinds)})")
        {
            Checked = info is not { UserKind.Length: > 0 },
        };
        detected.Click += (_, _) => Choose(null);
        menu.Items.Add(detected);
        menu.Items.Add(new ToolStripSeparator());
        foreach (var kind in UsbRules.Choices)
        {
            var text = UsbRules.KindText(kind);
            var item = new ToolStripMenuItem(text) { Checked = info?.UserKind == text };
            item.Click += (_, _) => Choose(text);
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        var cell = _usbGrid.GetCellDisplayRectangle(_usbGrid.Columns["type"]!.Index, rowIndex, false);
        menu.Show(_usbGrid, new Point(cell.Left, cell.Bottom));
    }

    private static void OpenCameraApp()
    {
        try { Process.Start(new ProcessStartInfo("microsoft.windows.camera:") { UseShellExecute = true }); }
        catch (Exception) { /* Camera app not installed */ }
    }
}
