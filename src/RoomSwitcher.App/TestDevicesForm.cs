using System.Diagnostics;
using RoomSwitcher.App.Ui;
using RoomSwitcher.Core.Audio;
using RoomSwitcher.Core.Devices;
using RoomSwitcher.Core.Display;
using RoomSwitcher.Core.Hotkeys;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Rooms;
using RoomSwitcher.Windows.Audio;
using RoomSwitcher.Windows.Devices;

namespace RoomSwitcher.App;

/// <summary>
/// "Test devices": pick a room on the left and check everything it uses: its displays (identify,
/// switch to it), its speakers and microphone, and the devices it keeps on. Also fixes the
/// "Windows sounds play but apps are silent" problem by restarting Windows Audio.
/// Changes nothing about the rooms themselves.
/// </summary>
internal sealed class TestDevicesForm : Form
{
    private readonly AppHost _host;
    private readonly Func<Room, Task> _switchTo;
    private readonly StepNav _nav = new() { Dock = DockStyle.Left, Width = 284 };
    private readonly PageHeader _pageHeader = new();
    private readonly FlowLayoutPanel _page = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
        BackColor = Theme.Card, Padding = new Padding(0, 8, 0, 8),
    };

    private readonly ModernButton _switch = new("Switch to this room", ButtonKind.Primary, Theme.Glyphs.Monitor);
    private readonly ModernButton _identify = new("Identify", ButtonKind.Secondary, Theme.Glyphs.Monitor);
    private readonly DataGridView _displayGrid = Grid();
    private readonly DataGridView _soundGrid = Grid();
    private readonly DataGridView _deviceGrid = Grid();
    private readonly Label _noDevices = Note("No devices are in this room yet. Tick them under Devices in this room on the Rooms step of Edit rooms…");
    private readonly ModernButton _checkSound = new("Check app sound", ButtonKind.Secondary, Theme.Glyphs.Play);
    private readonly ModernButton _restartAudio = new("Restart Windows Audio", ButtonKind.Secondary, Theme.Glyphs.Refresh);
    private readonly CheckBox _restartAtSignIn = new()
    {
        Text = "Restart Windows Audio automatically after I sign in", AutoSize = true, Font = Theme.BodySemibold,
        ForeColor = Theme.Text, Margin = new Padding(0, 12, 0, 0),
    };
    private readonly Label _status = new()
    {
        AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Subtext,
        BackColor = Theme.Card, AutoEllipsis = true,
    };

    private readonly IdentifyToggle _identifyNumbers = new();
    private Room? _room;
    private DisplayState _state = DisplayState.Empty;
    private IReadOnlyList<AudioDevice> _outputs = [];
    private IReadOnlyList<AudioDevice> _inputs = [];
    private IReadOnlyList<UsbDevices.Physical> _physicals = [];
    private RawInputWatcher? _rawInput;
    private readonly Dictionary<string, long> _flash = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer _flashTimer = new() { Interval = 120 };
    private string? _playingId;
    private string? _listeningId;
    private float _micLevel;
    private CancellationTokenSource? _listenStop;
    private bool _busy;
    private bool _loading;

    public TestDevicesForm(AppHost host, Func<Room, Task> switchTo)
    {
        _host = host;
        _switchTo = switchTo;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Test devices";
        Icon = Icon.ExtractAssociatedIcon(AppHost.ExePath);
        Font = Theme.Body;
        ForeColor = Theme.Text;
        BackColor = Theme.Window;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1060, 760);
        Size = new Size(1240, 870);

        var header = new WindowHeader(Text, "Check that each room's displays, sound and devices work.");
        var card = new RoundedPanel { Dock = DockStyle.Fill, Radius = 14, Padding = new Padding(28, 24, 28, 14), BackColor = Theme.Window };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 74, BackColor = Theme.Card, Padding = new Padding(0, 18, 0, 0) };
        var close = new ModernButton("Close") { Dock = DockStyle.Right, Width = 138 };
        close.Click += (_, _) => Close();
        footer.Controls.Add(_status);
        footer.Controls.Add(close);
        footer.Controls.Add(new Separator { Dock = DockStyle.Top });
        card.Controls.Add(_page);
        card.Controls.Add(footer);
        card.Controls.Add(_pageHeader);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 18, 20, 20), BackColor = Theme.Window };
        content.Controls.Add(card);
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window };
        body.Controls.Add(content);
        body.Controls.Add(_nav);
        Controls.Add(body);
        Controls.Add(header);

        BuildPage();
        foreach (var room in _host.Config.Rooms)
        {
            var shortcut = HotkeyRules.Parse(room.Hotkey)?.Display;
            _nav.Add(new StepInfo(room.Name, shortcut ?? "No shortcut", Theme.Glyphs.Monitor, ""));
        }
        _nav.StepClicked += (_, index) => ShowRoom(index);
        _nav.Footer = $"Version {FileVersionInfo.GetVersionInfo(AppHost.ExePath).ProductVersion?.Split('+')[0]}";
        Resize += (_, _) => header.Invalidate();

        _flashTimer.Tick += (_, _) =>
        {
            var now = Environment.TickCount64;
            foreach (var expired in _flash.Where(f => f.Value < now).Select(f => f.Key).ToList()) _flash.Remove(expired);
            if (_flash.Count == 0) _flashTimer.Stop();
            _deviceGrid.Invalidate();
        };
    }

    // ---- custom title bar ----

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeChrome.WM_NCCALCSIZE && NativeChrome.CalcSize(this, ref m, msg => base.WndProc(ref msg)))
            return;
        base.WndProc(ref m);
        if (m.Msg == NativeChrome.WM_NCHITTEST) NativeChrome.HitTest(this, ref m);
    }

    // Grids filled before the window exists lose their rows, so load once it's shown.
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _rawInput = new RawInputWatcher();
        _rawInput.DeviceUsed += OnDeviceUsed;
        _rawInput.Start();
        var current = Current();
        ShowRoom(current != null ? Math.Max(0, _host.Config.Rooms.IndexOf(current)) : 0);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopListening();
        _identifyNumbers.Hide();
        _rawInput?.Dispose();
        _flashTimer.Dispose();
        base.OnFormClosed(e);
    }

    // ---- layout ----

    private static DataGridView Grid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            AllowUserToResizeColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ReadOnly = true, ScrollBars = ScrollBars.None,
        };
        GridStyle.Apply(grid);
        grid.SelectionChanged += (_, _) => grid.ClearSelection();
        return grid;
    }

    private static Label Note(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Theme.Subtext, BackColor = Theme.Card, Margin = new Padding(0, 6, 0, 0),
    };

    private static Label Section(string text, int top = 18) => new()
    {
        Text = text, AutoSize = true, Font = Theme.BodySemibold, ForeColor = Theme.Text, BackColor = Theme.Card,
        Margin = new Padding(0, top, 0, 6),
    };

    private static Control Frame(DataGridView grid)
    {
        var frame = new RoundedPanel { ClipChild = true, Radius = 10, BackColor = Theme.Card, Margin = new Padding(0) };
        frame.Controls.Add(grid);
        return frame;
    }

    private static void Columns(DataGridView grid, params (string Name, string Header, int Weight, bool Button)[] columns)
    {
        foreach (var (name, header, weight, button) in columns)
        {
            DataGridViewColumn column = button
                ? new DataGridViewButtonColumn { FlatStyle = FlatStyle.Flat }
                : new DataGridViewTextBoxColumn();
            column.Name = name;
            column.HeaderText = header;
            column.FillWeight = weight;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(column);
        }
    }

    private void BuildPage()
    {
        // Displays
        var displayBar = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Card, Margin = new Padding(0, 0, 0, 8) };
        _switch.Margin = new Padding(0, 0, 14, 0);
        _identify.Margin = new Padding(0);
        _switch.Height = _identify.Height = 44;
        displayBar.Controls.AddRange([_switch, _identify]);
        _switch.Click += async (_, _) => await SwitchHereAsync();
        _identifyNumbers.Bind(_identify);
        _identify.Click += (_, _) => Identify();
        Columns(_displayGrid, ("name", "Display Nickname", 34, false), ("model", "Model name", 36, false), ("status", "Status", 30, false));
        _displayGrid.CellPainting += PaintDisplayCell;

        // Sound
        Columns(_soundGrid, ("role", "", 15, false), ("name", "Nickname", 26, false), ("model", "Model name", 25, false),
            ("status", "Status", 14, false), ("level", "Level", 12, false), ("test", "", 14, true));
        _soundGrid.CellPainting += PaintSoundCell;
        _soundGrid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _soundGrid.Columns[e.ColumnIndex].Name != "test") return;
            if (_soundGrid.Rows[e.RowIndex].Tag is not AudioDevice { Connected: true } device) return;
            if (device.Flow == AudioFlow.Output) PlayChime(device);
            else ToggleListen(device);
        };

        // Devices
        Columns(_deviceGrid, ("name", "Device Nickname", 30, false), ("kind", "Type", 18, false), ("status", "Status", 20, false), ("test", "Test", 32, true));
        _deviceGrid.CellPainting += PaintDeviceCell;
        _deviceGrid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _deviceGrid.Columns[e.ColumnIndex].Name != "test") return;
            if (_deviceGrid.Rows[e.RowIndex].Tag is UsbDevices.Physical { Device: var d } &&
                (d.Kinds | UsbRules.Kinds(_host.Config, d)).HasFlag(UsbKind.Camera))
                OpenCameraApp();
        };

        // Sound for apps
        var fixBar = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Card, Margin = new Padding(0, 4, 0, 0) };
        _checkSound.Margin = new Padding(0, 0, 14, 0);
        _restartAudio.Margin = new Padding(0);
        _checkSound.Height = _restartAudio.Height = 44;
        fixBar.Controls.AddRange([_checkSound, _restartAudio]);
        _checkSound.Click += async (_, _) => await CheckAppSoundAsync();
        _restartAudio.Click += async (_, _) => await RestartAudioAsync();
        _restartAtSignIn.Checked = _host.Config.Settings.RestartAudioAtSignIn;
        _restartAtSignIn.CheckedChanged += async (_, _) => await SetRestartAtSignInAsync();

        _page.Controls.AddRange([
            Section("Displays", top: 0), displayBar, Frame(_displayGrid),
            Section("Sound"), Frame(_soundGrid),
            Section("Devices"), Frame(_deviceGrid), _noDevices,
            Section("Sound for apps"),
            Note("If Windows sounds play but apps, videos and calls are silent (it can happen after a restart), " +
                 "Check app sound plays a chime the way apps do and tells you whether it reached the speakers. " +
                 "Restart Windows Audio fixes it, the same as restarting it in Services."),
            fixBar, _restartAtSignIn,
            // A scrolling flow stops at the last control's edge; this keeps the check box clear of the bottom.
            new Panel { Height = 32, Width = 1, BackColor = Theme.Card, Margin = new Padding(0) },
        ]);
        _page.Resize += (_, _) => FitWidths();
        _page.ControlAdded += (_, _) => FitWidths();
    }

    /// <summary>A top-down flow doesn't stretch its children, so do it by hand.</summary>
    private void FitWidths()
    {
        var width = _page.ClientSize.Width - _page.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth;
        foreach (Control c in _page.Controls)
        {
            if (c is RoundedPanel) c.Width = width;
            if (c is Label { AutoSize: true } label) label.MaximumSize = new Size(Math.Max(100, width), 0);
        }
    }

    private void SizeGrid(DataGridView grid, int rows)
    {
        GridStyle.Size(grid, rowHeight: 50, headerHeight: 44);
        var s = Theme.Scale(grid);
        grid.Parent!.Height = (int)(44 * s) + Math.Max(rows, 1) * (int)(50 * s) + 2;
        grid.ClearSelection();
    }

    // ---- loading a room ----

    private Room? Current()
    {
        try { return _host.Service.DetectCurrentRoom(); }
        catch (Exception) { return null; }
    }

    private void ShowRoom(int index)
    {
        if (index < 0 || index >= _host.Config.Rooms.Count) return;
        StopListening();
        _room = _host.Config.Rooms[index];
        _nav.Selected = index;
        Reload();
    }

    private void Reload()
    {
        if (_room == null) return;
        try { _state = _host.Display.Query(); } catch (Exception) { _state = DisplayState.Empty; }
        try
        {
            _outputs = _host.Audio.List(AudioFlow.Output);
            _inputs = _host.Audio.List(AudioFlow.Input);
        }
        catch (Exception) { _outputs = _inputs = []; }
        try { _physicals = UsbDevices.ListPhysical(); } catch (Exception) { _physicals = []; }

        var isOn = Current()?.Id == _room.Id;
        var screens = _room.Screens.Count(s => s.MonitorIds.Count > 0);
        var devices = DevicesOn(_room).Count;
        _pageHeader.Set(Theme.Glyphs.Monitor, _room.Name,
            $"{(isOn ? "On now" : "Not on right now")} · {screens} screen{(screens == 1 ? "" : "s")} · {devices} device{(devices == 1 ? "" : "s")}");
        _switch.Visible = !isOn;
        _switch.Text = $"Switch to {_room.Name}";
        _switch.FitWidth();

        _loading = true;
        try
        {
            FillDisplays();
            FillSound();
            FillDevices();
        }
        finally { _loading = false; }
        FitWidths();
    }

    private void FillDisplays()
    {
        var resolution = MonitorResolver.Resolve(_host.Config, _state);
        _displayGrid.Rows.Clear();
        foreach (var id in _room!.ActiveMonitorIds.Distinct())
        {
            var monitor = _host.Config.Monitors.FirstOrDefault(m => m.Id == id);
            if (monitor == null) continue;
            var number = _host.Config.Monitors.IndexOf(monitor) + 1;
            var row = _displayGrid.Rows.Add($"{number}. {monitor.Name}", monitor.FriendlyName ?? "", "");
            _displayGrid.Rows[row].Tag = resolution.Find(monitor.Id);
        }
        SizeGrid(_displayGrid, _displayGrid.Rows.Count);
    }

    private void FillSound()
    {
        _soundGrid.Rows.Clear();
        foreach (var (flow, wanted, role) in new[] { (AudioFlow.Output, _room!.AudioOutput, "Speakers"), (AudioFlow.Input, _room.AudioInput, "Microphone") })
        {
            var list = flow == AudioFlow.Output ? _outputs : _inputs;
            // "No change" rooms use whatever Windows is set to, so that's what gets tested.
            var device = wanted is { } w && (w.Id.Length > 0 || w.Name.Length > 0) ? AudioRules.Find(list, w) : list.FirstOrDefault(d => d.IsDefault);
            var name = device != null ? Nickname(device) : wanted?.Name ?? "None";
            if (wanted == null) name += " (Windows default)";
            var row = _soundGrid.Rows.Add(role, name, device?.Name ?? "", "", "", "");
            _soundGrid.Rows[row].Tag = device;
        }
        SizeGrid(_soundGrid, 2);
    }

    private List<string> DevicesOn(Room room) => UsbRules.DevicesIn(_host.Config, room);

    private void FillDevices()
    {
        _deviceGrid.Rows.Clear();
        foreach (var id in DevicesOn(_room!))
        {
            var physical = _physicals.FirstOrDefault(p => string.Equals(p.Device.Id, id, StringComparison.OrdinalIgnoreCase));
            var info = UsbRules.Find(_host.Config, id);
            var kind = physical != null ? UsbRules.KindText(UsbRules.Kinds(_host.Config, physical.Device)) : info?.UserKind ?? info?.Kind ?? "";
            var name = info?.DisplayName ?? physical?.Device.Name ?? id;
            if (info?.Managed != true) name += " (not switched)";
            var row = _deviceGrid.Rows.Add(name, kind, "", "");
            _deviceGrid.Rows[row].Tag = physical;
        }
        _deviceGrid.Parent!.Visible = _deviceGrid.Rows.Count > 0;
        _noDevices.Visible = _deviceGrid.Rows.Count == 0;
        SizeGrid(_deviceGrid, _deviceGrid.Rows.Count);
    }

    private string Nickname(AudioDevice device) =>
        _host.Config.AudioDevices.FirstOrDefault(a => string.Equals(a.Id, device.Id, StringComparison.OrdinalIgnoreCase))?.Name is { Length: > 0 } name
            ? name : device.Name;

    // ---- painting ----

    private void PaintDisplayCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _displayGrid.Columns[e.ColumnIndex].Name != "status") return;
        GridStyle.PaintBackground(e);
        switch (_displayGrid.Rows[e.RowIndex].Tag)
        {
            case ConnectedMonitor { Active: true }:
                GridStyle.PaintPill(e, "On", Theme.SuccessBack, Theme.Success, Theme.SuccessDot);
                break;
            case ConnectedMonitor:
                GridStyle.PaintPill(e, "Off (connected)", Theme.NeutralBack, Theme.Subtext, Theme.Disabled);
                break;
            default:
                GridStyle.PaintPill(e, "Can't be detected", Theme.NeutralBack, Theme.Warning, Theme.Warning);
                break;
        }
        e.Handled = true;
    }

    private void PaintSoundCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var device = _soundGrid.Rows[e.RowIndex].Tag as AudioDevice;
        switch (_soundGrid.Columns[e.ColumnIndex].Name)
        {
            case "role":
                GridStyle.PaintBackground(e);
                TextRenderer.DrawText(e.Graphics!, e.Value as string, Theme.BodySemibold, Rectangle.Inflate(e.CellBounds, -14, 0), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                break;
            case "status":
                GridStyle.PaintBackground(e);
                if (device == null) GridStyle.PaintPill(e, "Not found", Theme.NeutralBack, Theme.Warning, Theme.Warning);
                else if (device.Connected) GridStyle.PaintPill(e, "Connected", Theme.SuccessBack, Theme.Success, Theme.SuccessDot);
                else GridStyle.PaintPill(e, "Unplugged", Theme.NeutralBack, Theme.Subtext, Theme.Disabled);
                break;
            case "level":
                if (device?.Flow == AudioFlow.Input) GridStyle.PaintMeter(e, _micLevel, _listeningId == device.Id);
                else GridStyle.PaintBackground(e);
                break;
            case "test":
                if (device?.Flow == AudioFlow.Input)
                    GridStyle.PaintCellButton(e, _listeningId == device.Id ? "Stop" : "Test",
                        _listeningId == device.Id ? Theme.Glyphs.Stop : Theme.Glyphs.Microphone, device.Connected, _listeningId == device.Id);
                else
                    GridStyle.PaintCellButton(e, _playingId != null && _playingId == device?.Id ? "Playing…" : "Test", Theme.Glyphs.Play,
                        device?.Connected == true, _playingId != null && _playingId == device?.Id);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void PaintDeviceCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var physical = _deviceGrid.Rows[e.RowIndex].Tag as UsbDevices.Physical;
        var device = physical?.Device;
        var flashing = device != null && _flash.ContainsKey(device.Id);
        var back = flashing ? Theme.SuccessBack : (Color?)null;
        switch (_deviceGrid.Columns[e.ColumnIndex].Name)
        {
            case "status":
                GridStyle.PaintBackground(e, back);
                if (device == null) GridStyle.PaintPill(e, "Unplugged", Theme.NeutralBack, Theme.Subtext, Theme.Disabled);
                else if (!device.Connected) GridStyle.PaintPill(e, "Not connected", Theme.NeutralBack, Theme.Subtext, Theme.Disabled);
                else if (device.Enabled) GridStyle.PaintPill(e, "On", Theme.SuccessBack, Theme.Success, Theme.SuccessDot);
                else GridStyle.PaintPill(e, "Off", Theme.NeutralBack, Theme.Warning, Theme.Warning);
                break;
            case "test":
                if (device != null && (device.Kinds | UsbRules.Kinds(_host.Config, device)).HasFlag(UsbKind.Camera))
                {
                    GridStyle.PaintCellButton(e, "Open Camera", Theme.Glyphs.Camera, device.Connected && device.Enabled);
                }
                else
                {
                    GridStyle.PaintBackground(e, back);
                    var hint = device == null ? "" :
                        flashing ? "✔ Working" :
                        UsbRules.Kinds(_host.Config, device) is var k && k.HasFlag(UsbKind.Keyboard) && k.HasFlag(UsbKind.Mouse) ? "Press a key or move it"
                        : k.HasFlag(UsbKind.Keyboard) ? "Press a key on it"
                        : k.HasFlag(UsbKind.Mouse) ? "Move it or click" : "";
                    TextRenderer.DrawText(e.Graphics!, hint, flashing ? Theme.BodySemibold : Theme.Body, Rectangle.Inflate(e.CellBounds, -14, 0),
                        flashing ? Theme.Success : Theme.Subtext, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                break;
            default:
                if (!flashing) return;
                GridStyle.PaintBackground(e, back);
                TextRenderer.DrawText(e.Graphics!, e.Value as string, Theme.Body, Rectangle.Inflate(e.CellBounds, -14, 0), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                break;
        }
        e.Handled = true;
    }

    private void OnDeviceUsed(string instanceId)
    {
        var physical = _physicals.FirstOrDefault(p => p.Members.Any(m => string.Equals(m.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase)));
        if (physical == null) return;
        _flash[physical.Device.Id] = Environment.TickCount64 + 900;
        _deviceGrid.Invalidate();
        _flashTimer.Start();
    }

    // ---- actions ----

    private void SetStatus(string text, Color color)
    {
        _status.ForeColor = color;
        _status.Text = text;
    }

    private async Task SwitchHereAsync()
    {
        if (_room == null || _busy) return;
        _busy = true;
        _switch.Enabled = false;
        SetStatus($"Switching to {_room.Name}…", Theme.Subtext);
        try { await _switchTo(_room); }
        finally
        {
            _busy = false;
            _switch.Enabled = true;
        }
        if (IsDisposed) return;
        Reload();
        // The screens changed, so numbers that were up are in the wrong places: show them again.
        if (_identifyNumbers.Active) _identifyNumbers.Show(_host.Config, _state);
        SetStatus(Current()?.Id == _room.Id ? $"✔ {_room.Name} is on. Select Identify to see which display is which." : $"{_room.Name} didn't turn on. See the message from the tray icon.",
            Current()?.Id == _room.Id ? Theme.Success : Theme.Danger);
    }

    private void Identify()
    {
        if (_identifyNumbers.Active)
        {
            _identifyNumbers.Hide();
            SetStatus("", Theme.Subtext);
            return;
        }
        try { _state = _host.Display.Query(); } catch (Exception) { }
        var shown = _identifyNumbers.Show(_host.Config, _state);
        SetStatus(shown == 0 ? "No display could show a number." : "Each display shows its number until you select Stop identifying or close this window.", Theme.Subtext);
    }

    private async void PlayChime(AudioDevice device)
    {
        if (_playingId != null) return;
        _playingId = device.Id;
        _soundGrid.Invalidate();
        SetStatus($"Playing a chime through {Nickname(device)}…", Theme.Subtext);
        var problem = await Task.Run(() => AudioTester.PlayChime(device.Id));
        _playingId = null;
        if (IsDisposed) return;
        _soundGrid.Invalidate();
        if (problem == null) SetStatus($"✔ Played a chime through {Nickname(device)}. Didn't hear it? Check the volume, or try Check app sound below.", Theme.Success);
        else SetStatus($"✖ {Nickname(device)}: {problem}", Theme.Danger);
    }

    private void ToggleListen(AudioDevice device)
    {
        var wasListening = _listeningId == device.Id;
        StopListening();
        if (wasListening) return;
        _listeningId = device.Id;
        _listenStop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var stop = _listenStop;
        SetStatus($"Listening to {Nickname(device)}. Say something and watch the level bar. It stops by itself after 20 seconds.", Theme.Subtext);
        _soundGrid.Invalidate();
        Task.Run(() =>
        {
            var problem = AudioTester.Listen(device.Id, level =>
            {
                if (IsDisposed) return;
                BeginInvoke(() => { _micLevel = level; _soundGrid.Invalidate(); });
            }, stop.Token);
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                if (ReferenceEquals(_listenStop, stop)) _listeningId = null;
                _micLevel = 0;
                _soundGrid.Invalidate();
                if (problem != null) SetStatus($"✖ {Nickname(device)}: {problem}", Theme.Danger);
            });
        });
    }

    private void StopListening()
    {
        _listenStop?.Cancel();
        _listenStop = null;
        _listeningId = null;
        _micLevel = 0;
        _soundGrid.Invalidate();
    }

    /// <summary>The speakers this room plays through, or the Windows default.</summary>
    private AudioDevice? RoomSpeakers() => _soundGrid.Rows.Count > 0 ? _soundGrid.Rows[0].Tag as AudioDevice : null;

    private async Task CheckAppSoundAsync()
    {
        if (RoomSpeakers() is not { Connected: true } speakers)
        {
            SetStatus("This room's speakers aren't connected, so there's nothing to check.", Theme.Warning);
            return;
        }
        _checkSound.Enabled = false;
        SetStatus($"Playing a chime through {Nickname(speakers)} the way apps do…", Theme.Subtext);
        var (problem, heard) = await Task.Run(() => AudioTester.CheckPlayback(speakers.Id));
        _checkSound.Enabled = true;
        if (IsDisposed) return;
        if (problem != null)
            SetStatus($"✖ Apps can't play sound through {Nickname(speakers)}: {problem} Select Restart Windows Audio.", Theme.Danger);
        else if (heard < 0.001f)
            SetStatus($"✖ The chime didn't reach {Nickname(speakers)}, so apps are probably silent. Select Restart Windows Audio.", Theme.Danger);
        else
            SetStatus($"✔ Apps can play sound: the chime reached {Nickname(speakers)}. Didn't hear it? Check the speaker's volume and power.", Theme.Success);
    }

    private async Task RestartAudioAsync()
    {
        if (!await EnsureHelperAsync("Restarting Windows Audio")) return;
        _restartAudio.Enabled = _checkSound.Enabled = false;
        StopListening();
        SetStatus("Restarting Windows Audio… Sound stops for a few seconds.", Theme.Subtext);
        var problems = await _host.Devices.RestartAudioAsync();
        if (IsDisposed) return;
        if (problems.Count > 0)
        {
            _restartAudio.Enabled = _checkSound.Enabled = true;
            SetStatus($"✖ {problems[0]}", Theme.Danger);
            return;
        }
        // Give the speakers a moment to come back, then prove it worked.
        await Task.Delay(2500);
        if (IsDisposed) return;
        _restartAudio.Enabled = true;
        Reload();
        await CheckAppSoundAsync();
        if (_status.ForeColor == Theme.Success) SetStatus("✔ Restarted Windows Audio. " + _status.Text.TrimStart('✔', ' '), Theme.Success);
    }

    private async Task SetRestartAtSignInAsync()
    {
        if (_loading) return;
        if (_restartAtSignIn.Checked && !await EnsureHelperAsync("Restarting Windows Audio after you sign in"))
        {
            _loading = true;
            _restartAtSignIn.Checked = false;
            _loading = false;
            return;
        }
        _host.Config.Settings.RestartAudioAtSignIn = _restartAtSignIn.Checked;
        try { _host.SaveConfig(); }
        catch (Exception e) { SetStatus($"Couldn't save that setting. {e.Message}", Theme.Danger); return; }
        SetStatus(_restartAtSignIn.Checked
            ? "✔ Windows Audio will be restarted about 15 seconds after you sign in."
            : "Windows Audio won't be restarted after you sign in.", Theme.Success);
    }

    /// <summary>The administrator helper is needed to restart a Windows service. Offers the one-time setup if it's missing or old.</summary>
    private async Task<bool> EnsureHelperAsync(string what)
    {
        var status = await Task.Run(() =>
        {
            try { return _host.Devices.Status; }
            catch (Exception) { return DeviceControlStatus.NotSetUp; }
        });
        if (status == DeviceControlStatus.Ready) return true;
        var message = status == DeviceControlStatus.NeedsUpdate
            ? $"{what} uses Room Switcher's administrator helper, which was set up by an older version.\n\nUpdate it now? Windows will ask for administrator permission once."
            : $"{what} needs administrator permission. Room Switcher can set up a helper for this (the same one that turns devices on and off), so you're only asked once.\n\nSet it up now?";
        if (ThemedDialog.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
        UseWaitCursor = true;
        var (ok, error) = await Task.Run(() => (DeviceHelper.RunElevated(DeviceHelper.InstallArg, out var e), e));
        UseWaitCursor = false;
        if (!ok) ThemedDialog.Show(this, error ?? "Couldn't set up the helper. Try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return ok;
    }

    private static void OpenCameraApp()
    {
        try { Process.Start(new ProcessStartInfo("microsoft.windows.camera:") { UseShellExecute = true }); }
        catch (Exception) { /* no Camera app */ }
    }
}
