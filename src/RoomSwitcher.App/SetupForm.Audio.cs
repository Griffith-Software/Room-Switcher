using RoomSwitcher.App.Ui;
using RoomSwitcher.Core.Audio;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Windows.Audio;

namespace RoomSwitcher.App;

// Step 2: speakers and microphones. Name them, play a chime through a speaker, or watch a
// microphone's level. Names are used in the Rooms step and in messages.
internal sealed partial class SetupForm
{
    private readonly DataGridView _speakerGrid = AudioGrid();
    private readonly DataGridView _micGrid = AudioGrid();
    private readonly Label _audioNote = Wrapping(new Label { Margin = new Padding(0, 10, 0, 0), ForeColor = Theme.Subtext });
    private readonly Dictionary<string, float> _micLevels = new();
    private string? _playingId;
    private string? _listeningId;
    private CancellationTokenSource? _listenStop;

    private static DataGridView AudioGrid() => new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
        AllowUserToResizeColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, EditMode = DataGridViewEditMode.EditOnEnter,
    };

    private void BuildAudioTab()
    {
        var page = Page();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = Theme.Card };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(Paragraph(
            "Give each speaker and microphone a nickname you'll recognise, like \"Desk speakers\" or \"Living room TV\". " +
            "Test plays a chime through that speaker, or shows a microphone's level while you talk."), 0, 0);
        layout.Controls.Add(SectionLabel("Speakers"), 0, 1);
        layout.Controls.Add(AudioFrame(_speakerGrid, AudioFlow.Output, "Speakers Nickname"), 0, 2);
        var mics = SectionLabel("Microphones");
        mics.Margin = new Padding(0, 14, 0, 2);
        layout.Controls.Add(mics, 0, 3);
        layout.Controls.Add(AudioFrame(_micGrid, AudioFlow.Input, "Microphones Nickname"), 0, 4);
        layout.Controls.Add(_audioNote, 0, 5);

        page.Controls.Add(layout);
        AddStep(new StepInfo("Audio", "Name and test sound", Theme.Glyphs.Speaker,
            "These are the speakers and microphones Windows knows about."), page, LoadAudioTab, StopAudioTests);
    }

    private Control AudioFrame(DataGridView grid, AudioFlow flow, string nicknameHeader)
    {
        GridStyle.Apply(grid);
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "name", HeaderText = nicknameHeader, FillWeight = 30, MaxInputLength = 60,
            DefaultCellStyle = { Padding = new Padding(28, 19, 32, 15) },
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "windows", HeaderText = "Model name", ReadOnly = true, FillWeight = 32 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", HeaderText = "Status", ReadOnly = true, FillWeight = 20 });
        if (flow == AudioFlow.Input)
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "level", HeaderText = "Level", ReadOnly = true, FillWeight = 18 });
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "test", HeaderText = "", FillWeight = 16, FlatStyle = FlatStyle.Flat });
        foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;

        grid.CellPainting += (_, e) => PaintAudioCell(grid, flow, e);
        GridStyle.ClickToEdit(grid, "name");
        grid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is TextBox box) { box.BorderStyle = BorderStyle.None; box.BackColor = Color.White; box.Font = Theme.Body; }
        };
        grid.CellValueChanged += (_, e) =>
        {
            if (_loading || e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "name") return;
            var device = (AudioDevice)grid.Rows[e.RowIndex].Tag!;
            SetAudioName(device, (grid.Rows[e.RowIndex].Cells["name"].Value as string)?.Trim() ?? "");
            MarkDirty();
            CheckAudioNames();
        };
        grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "test") return;
            var device = (AudioDevice)grid.Rows[e.RowIndex].Tag!;
            if (!device.Connected) return;
            if (flow == AudioFlow.Output) PlayTest(device);
            else ToggleListen(device);
        };

        var frame = new RoundedPanel { Dock = DockStyle.Fill, ClipChild = true, Radius = 10, BackColor = Theme.Card, Margin = new Padding(0, 6, 0, 0) };
        frame.Controls.Add(grid);
        return frame;
    }

    private void LoadAudioTab()
    {
        FillAudioGrid(_speakerGrid, _outputs);
        FillAudioGrid(_micGrid, _inputs);
        CheckAudioNames();
    }

    private void FillAudioGrid(DataGridView grid, IReadOnlyList<AudioDevice> devices)
    {
        _loading = true;
        try
        {
            grid.Rows.Clear();
            foreach (var d in devices)
            {
                var values = new List<object> { AudioName(d.Id), d.Name, d.Connected ? "Connected" : "Unplugged" };
                if (grid.Columns.Contains("level")) values.Add("");
                values.Add("");
                var row = grid.Rows.Add(values.ToArray());
                grid.Rows[row].Tag = d;
            }
            GridStyle.Size(grid, rowHeight: 48, headerHeight: 44);
            var s = Theme.Scale(grid);
            grid.Columns["name"]!.DefaultCellStyle.Padding = new Padding((int)(26 * s), (int)(14 * s), (int)(30 * s), (int)(11 * s));
            grid.ClearSelection();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>The person's name for a device, or "" if they haven't named it (the grid shows Windows' name next to it).</summary>
    private string AudioName(string id) =>
        _draft.AudioDevices.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase))?.Name ?? "";

    /// <summary>What to call a device anywhere in setup: the person's name, else Windows' name.</summary>
    private string AudioLabel(AudioDevice device) => AudioName(device.Id) is { Length: > 0 } name ? name : device.Name;

    private void SetAudioName(AudioDevice device, string name)
    {
        var entry = _draft.AudioDevices.FirstOrDefault(a => string.Equals(a.Id, device.Id, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(name))
        {
            if (entry != null) _draft.AudioDevices.Remove(entry);
            return;
        }
        if (entry == null)
        {
            entry = new AudioDeviceName { Id = device.Id };
            _draft.AudioDevices.Add(entry);
        }
        entry.WindowsName = device.Name;
        entry.Name = name;
    }

    private void CheckAudioNames()
    {
        // Only devices listed on this page count: a nickname kept for a device Windows no longer has
        // (for example after it was plugged into another port and renamed) can't be seen or changed here.
        var listed = _outputs.Concat(_inputs).ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        var clash = _draft.AudioDevices
            .Where(a => a.Name.Length > 0 && listed.ContainsKey(a.Id))
            .GroupBy(a => a.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        _audioNote.ForeColor = clash != null ? Theme.Warning : Theme.Subtext;
        _audioNote.Text = clash != null
            ? $"⚠ {string.Join(" and ", clash.Select(a => listed[a.Id].Name))} are both called \"{clash.Key}\". " +
              "Give each a different name so you can tell them apart."
            : "Nicknames are optional. A device without one is shown by its model name.";
    }

    private void PaintAudioCell(DataGridView grid, AudioFlow flow, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var device = (AudioDevice)grid.Rows[e.RowIndex].Tag!;
        switch (grid.Columns[e.ColumnIndex].Name)
        {
            case "name":
                GridStyle.PaintBackground(e);
                var editing = grid.IsCurrentCellInEditMode && grid.CurrentCell?.RowIndex == e.RowIndex;
                GridStyle.PaintInput(e, editing ? "" : (e.Value as string) is { Length: > 0 } n ? n : "", true);
                if (!editing && string.IsNullOrEmpty(e.Value as string))
                {
                    var r = GridStyle.InputRect(e.CellBounds);
                    TextRenderer.DrawText(e.Graphics!, "Add a nickname", Theme.Body, Rectangle.Inflate(r, -(int)(12 * r.Height / 41f), 0), Theme.Disabled,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                e.Handled = true;
                break;
            case "status":
                GridStyle.PaintBackground(e);
                if (device.Connected) GridStyle.PaintPill(e, "Connected", Theme.SuccessBack, Theme.Success, Theme.SuccessDot);
                else GridStyle.PaintPill(e, "Unplugged", Theme.NeutralBack, Theme.Subtext, Theme.Disabled);
                e.Handled = true;
                break;
            case "level":
                GridStyle.PaintMeter(e, _micLevels.GetValueOrDefault(device.Id), _listeningId == device.Id);
                e.Handled = true;
                break;
            case "test":
                if (flow == AudioFlow.Output)
                    GridStyle.PaintCellButton(e, _playingId == device.Id ? "Playing…" : "Test", Theme.Glyphs.Play, device.Connected, _playingId == device.Id);
                else
                    GridStyle.PaintCellButton(e, _listeningId == device.Id ? "Stop" : "Test", _listeningId == device.Id ? Theme.Glyphs.Stop : Theme.Glyphs.Microphone,
                        device.Connected, _listeningId == device.Id);
                e.Handled = true;
                break;
        }
    }

    private async void PlayTest(AudioDevice device)
    {
        if (_playingId != null) return;
        _playingId = device.Id;
        _speakerGrid.Invalidate();
        _audioNote.ForeColor = Theme.Subtext;
        _audioNote.Text = $"Playing a chime through {AudioLabel(device)}…";
        var problem = await Task.Run(() => AudioTester.PlayChime(device.Id));
        _playingId = null;
        if (IsDisposed) return;
        _speakerGrid.Invalidate();
        _audioNote.ForeColor = problem == null ? Theme.Success : Theme.Danger;
        _audioNote.Text = problem == null
            ? $"✔ Played a chime through {AudioLabel(device)}. Didn't hear it? Check the volume and make sure the speaker is turned on."
            : $"✖ {AudioLabel(device)}: {problem}";
    }

    private void ToggleListen(AudioDevice device)
    {
        var wasListening = _listeningId == device.Id;
        StopAudioTests();
        if (wasListening) return;

        _listeningId = device.Id;
        _listenStop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var stop = _listenStop;
        _audioNote.ForeColor = Theme.Subtext;
        _audioNote.Text = $"Listening to {AudioLabel(device)}. Say something and watch the level bar move. Listening stops by itself after 20 seconds.";
        _micGrid.Invalidate();
        Task.Run(() =>
        {
            var problem = AudioTester.Listen(device.Id, level =>
            {
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    _micLevels[device.Id] = level;
                    _micGrid.Invalidate();
                });
            }, stop.Token);
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                if (_listeningId == device.Id && ReferenceEquals(_listenStop, stop)) _listeningId = null;
                _micLevels.Remove(device.Id);
                _micGrid.Invalidate();
                if (problem != null)
                {
                    _audioNote.ForeColor = Theme.Danger;
                    _audioNote.Text = $"✖ {AudioLabel(device)}: {problem}";
                }
            });
        });
    }

    private void StopAudioTests()
    {
        _listenStop?.Cancel();
        _listenStop = null;
        _listeningId = null;
        _micGrid.Invalidate();
    }
}
