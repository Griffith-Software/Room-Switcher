using RoomSwitcher.Core.Model;
using RoomSwitcher.App.Ui;
using RoomSwitcher.Core.Rooms;

namespace RoomSwitcher.App;

// Step 1: the monitors Windows reports, with the person's own names for them.
internal sealed partial class SetupForm
{
    private readonly DataGridView _monitorGrid = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
        AllowUserToResizeColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, EditMode = DataGridViewEditMode.EditOnEnter,
    };
    private readonly ModernButton _forget = new("Remove display",ButtonKind.Secondary, Theme.Glyphs.Delete) { GlyphColor = Theme.Subtext };

    private void BuildMonitorsTab()
    {
        var page = Page();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Card };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(Paragraph(
            "Give each one a name you'll recognise, like \"Office left\" or \"Living room TV\". " +
            "Select Identify to show each display's number on its screen."), 0, 0);

        GridStyle.Apply(_monitorGrid);
        _monitorGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "number", HeaderText = "#", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 78,
            HeaderCell = { Style = { Alignment = DataGridViewContentAlignment.MiddleCenter, Padding = new Padding(0) } },
        });
        _monitorGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "name", HeaderText = "Display Nickname", FillWeight = 36, MaxInputLength = 60,
            DefaultCellStyle = { Padding = new Padding(28, 19, 32, 15) },
        });
        _monitorGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "model", HeaderText = "Model name", ReadOnly = true, FillWeight = 26 });
        _monitorGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "connector", HeaderText = "Connection", ReadOnly = true, FillWeight = 18 });
        _monitorGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", HeaderText = "Status", ReadOnly = true, FillWeight = 18 });
        foreach (DataGridViewColumn c in _monitorGrid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;

        _monitorGrid.CellPainting += PaintMonitorCell;
        GridStyle.ClickToEdit(_monitorGrid, "name");
        _monitorGrid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is TextBox box)
            {
                box.BorderStyle = BorderStyle.None;
                box.BackColor = Color.White;
                box.Font = Theme.Body;
            }
        };
        _monitorGrid.CellValueChanged += (_, e) =>
        {
            if (_loading || e.RowIndex < 0 || e.ColumnIndex != 1) return;
            var monitor = (MonitorInfo)_monitorGrid.Rows[e.RowIndex].Tag!;
            var name = (_monitorGrid.Rows[e.RowIndex].Cells[1].Value as string)?.Trim();
            monitor.Name = string.IsNullOrEmpty(name) ? monitor.FriendlyName ?? "Display" : name;
            MarkDirty();
        };
        _monitorGrid.SelectionChanged += (_, _) => UpdateForgetButton();
        var frame = new RoundedPanel { Dock = DockStyle.Fill, ClipChild = true, Radius = 10, BackColor = Theme.Card, Margin = new Padding(0) };
        frame.Controls.Add(_monitorGrid);
        layout.Controls.Add(frame, 0, 1);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 22, 0, 0), BackColor = Theme.Card, WrapContents = false };
        var identify = new ModernButton("Identify", ButtonKind.Primary, Theme.Glyphs.Monitor) { Height = 48, Margin = new Padding(0, 0, 14, 0) };
        var refresh = new ModernButton("Detect displays",ButtonKind.Secondary, Theme.Glyphs.Refresh) { Height = 48, Margin = new Padding(0, 0, 14, 0) };
        _forget.Height = 48;
        _identifyNumbers.Bind(identify);
        identify.Click += (_, _) => Identify();
        refresh.Click += (_, _) => { RefreshState(); LoadMonitorGrid(); };
        _forget.Click += (_, _) => ForgetSelectedMonitor();
        buttons.Controls.AddRange([identify, refresh, _forget]);
        layout.Controls.Add(buttons, 0, 2);

        page.Controls.Add(layout);
        AddStep(new StepInfo("Displays", "Detect connected displays", Theme.Glyphs.Monitor,
            "These are the displays connected to this PC, whether they're on or off right now."),page, LoadMonitorGrid);
    }

    private void PaintMonitorCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var row = _monitorGrid.Rows[e.RowIndex];
        var connected = row.Tag is MonitorInfo m && IsConnected(m);
        switch (_monitorGrid.Columns[e.ColumnIndex].Name)
        {
            case "number":
                GridStyle.PaintBackground(e);
                GridStyle.PaintBadge(e, Convert.ToString(e.Value) ?? "", connected ? Theme.Accent : Theme.Disabled);
                e.Handled = true;
                break;
            case "name":
                GridStyle.PaintBackground(e);
                if (!_monitorGrid.IsCurrentCellInEditMode || _monitorGrid.CurrentCell?.RowIndex != e.RowIndex)
                    GridStyle.PaintInput(e, Convert.ToString(e.Value) ?? "", true);
                else
                    GridStyle.PaintInput(e, "", true);
                e.Handled = true;
                break;
            case "status":
                GridStyle.PaintBackground(e);
                var status = Convert.ToString(e.Value) ?? "";
                if (status == "On") GridStyle.PaintPill(e, status, Theme.SuccessBack, Theme.Success, Theme.SuccessDot);
                else if (status == "Off") GridStyle.PaintPill(e, status, Theme.NeutralBack, Theme.Subtext, Theme.Disabled);
                else GridStyle.PaintPill(e, status, Theme.WarningBack, Theme.Warning, Color.FromArgb(214, 140, 0));
                e.Handled = true;
                break;
        }
    }

    private void LoadMonitorGrid()
    {
        _loading = true;
        try
        {
            _monitorGrid.Rows.Clear();
            var resolution = MonitorResolver.Resolve(_draft, _state);
            foreach (var m in _draft.Monitors)
            {
                var connected = resolution.Find(m.Id);
                var status = connected == null ? "Not connected" : connected.Active ? "On" : "Off";
                var row = _monitorGrid.Rows.Add(_draft.Monitors.IndexOf(m) + 1, m.Name, connected?.FriendlyName ?? m.FriendlyName ?? "",
                    connected?.Connector ?? "", status);
                _monitorGrid.Rows[row].Tag = m;
                if (connected == null) _monitorGrid.Rows[row].DefaultCellStyle.ForeColor = DisconnectedColor;
            }
            GridStyle.Size(_monitorGrid);
            var sc = Theme.Scale(_monitorGrid);
            _monitorGrid.Columns["number"]!.Width = (int)(78 * sc);
            _monitorGrid.Columns["name"]!.DefaultCellStyle.Padding = new Padding((int)(28 * sc), (int)(19 * sc), (int)(32 * sc), (int)(15 * sc));
        }
        finally
        {
            _loading = false;
        }
        UpdateForgetButton();
    }

    private MonitorInfo? SelectedMonitor =>
        _monitorGrid.SelectedRows.Count == 1 ? _monitorGrid.SelectedRows[0].Tag as MonitorInfo : null;

    private void UpdateForgetButton()
    {
        // Only monitors that are gone can be forgotten; connected ones come straight back.
        var m = SelectedMonitor;
        _forget.Enabled = m != null && !IsConnected(m);
    }

    private void ForgetSelectedMonitor()
    {
        var m = SelectedMonitor;
        if (m == null) return;
        var rooms = _draft.Rooms.Where(r => r.ActiveMonitorIds.Contains(m.Id)).Select(r => r.Name).ToList();
        var question = rooms.Count == 0
            ? $"Remove {m.Name} from the list?"
            : $"Remove {m.Name} from the list? It will also be taken out of these rooms: {string.Join(", ", rooms)}.";
        if (ThemedDialog.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        RoomEditor.ForgetMonitor(_draft, m);
        foreach (var room in _draft.Rooms) RoomEditor.RemoveEmptyScreens(room);
        MarkDirty();
        LoadMonitorGrid();
    }

    private async void Identify()
    {
        if (_identifyNumbers.Active)
        {
            _identifyNumbers.Hide();
            return;
        }
        _monitorGrid.EndEdit();
        RefreshState();
        var off = _state.Monitors.Count(m => !m.Active);
        if (off > 0)
        {
            var answer = ThemedDialog.Show(this,
                $"{off} display{(off == 1 ? " is" : "s are")} off right now, so {(off == 1 ? "it" : "they")} can't show a number.\n\n" +
                "Turn on all displays to identify them? They stay on until you switch rooms.",
                Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.Yes)
            {
                var result = _host.Display.ShowAll();
                if (!result.Ok)
                    ThemedDialog.Show(this, $"Couldn't turn on all displays. {result.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                // Give Windows a moment to light the screens before drawing on them.
                UseWaitCursor = true;
                await Task.Delay(2500);
                UseWaitCursor = false;
                RefreshState();
            }
        }
        LoadMonitorGrid();
        _identifyNumbers.Show(_draft, _state);
    }
}
