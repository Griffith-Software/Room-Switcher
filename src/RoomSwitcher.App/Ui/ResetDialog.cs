using System.Runtime.InteropServices;
using RoomSwitcher.Core.Rooms;

namespace RoomSwitcher.App.Ui;

/// <summary>
/// Reset: three choices, from resetting the rooms (keeping nicknames) to uninstalling. The red button
/// says what it will do and stays off until a choice is made. Styled like <see cref="ThemedDialog"/>.
/// </summary>
internal sealed class ResetDialog : Form
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static readonly (ResetKind Kind, string Title, string Details, string Button)[] Choices =
    [
        (ResetKind.Rooms, "Reset Rooms and Assignments",
            "Deletes all rooms, the speakers, microphone and devices chosen for them, and their saved window positions. " +
            "Keeps your display, sound and device nicknames and your options. Setup then starts again at the beginning.",
            "Reset Rooms"),
        (ResetKind.Everything, "Reset Everything",
            "Deletes all rooms, all nicknames and all options, turns off Start with Windows, and removes the device switching helper " +
            "(Windows asks for permission if it was set up). Setup then starts again at the beginning, as on first use.",
            "Reset Everything"),
        (ResetKind.Uninstall, "Uninstall Room Switcher",
            "Removes Room Switcher from this PC: all settings and nicknames, the device switching helper " +
            "(Windows asks for permission if it was set up), the startup and Start menu shortcuts, and RoomSwitcher.exe itself.",
            "Uninstall"),
    ];

    private readonly ModernButton _go = new("Reset", ButtonKind.Red) { Enabled = false, Margin = new Padding(10, 0, 0, 0) };
    private ResetKind? _chosen;

    private ResetDialog()
    {
        Text = "Reset Room Switcher";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.Card;
        Font = Theme.Body;
        KeyPreview = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(28, 24, 28, 20);

        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, BackColor = Theme.Card, WrapContents = false };
        flow.Controls.Add(new Label { Text = "Reset Room Switcher", AutoSize = true, Font = Theme.PageTitle, ForeColor = Theme.Text });
        flow.Controls.Add(new Label
        {
            Text = "Each choice first puts Windows back the way Room Switcher found it: every display on at its own resolution and " +
                   "the recommended scale, the speakers and microphone Windows used before, and any devices your rooms turned off back on.",
            AutoSize = true, MaximumSize = new Size(500, 0),
            ForeColor = Theme.Subtext, Margin = new Padding(0, 6, 0, 10),
        });
        foreach (var (kind, title, details, button) in Choices)
        {
            var option = new RadioButton
            {
                Text = title, AutoSize = true, Font = Theme.BodySemibold, ForeColor = Theme.Text, BackColor = Theme.Card,
                Margin = new Padding(0, 10, 0, 0), AccessibleDescription = details,
            };
            option.CheckedChanged += (_, _) =>
            {
                if (!option.Checked) return;
                _chosen = kind;
                _go.Text = button;
                _go.FitWidth();
                _go.Width = Math.Max(130, _go.Width);
                _go.Enabled = true;
            };
            flow.Controls.Add(option);
            flow.Controls.Add(new Label
            {
                Text = details, AutoSize = true, MaximumSize = new Size(500, 0), ForeColor = Theme.Subtext, Margin = new Padding(20, 2, 0, 0),
            });
        }

        var cancel = new ModernButton("Cancel") { Margin = new Padding(10, 0, 0, 0), Width = 110 };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _go.Width = 130;
        _go.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, AutoSize = true, BackColor = Theme.Card, WrapContents = false,
            Margin = new Padding(0, 22, 0, 0), Anchor = AnchorStyles.Right,
        };
        row.Controls.Add(cancel);
        row.Controls.Add(_go);
        CancelButton = cancel;

        var outer = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, RowCount = 2, BackColor = Theme.Card, Dock = DockStyle.Fill };
        outer.Controls.Add(flow, 0, 0);
        outer.Controls.Add(row, 0, 1);
        Controls.Add(outer);
        Shown += (_, _) => cancel.Focus();
        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) NativeChrome.BeginDrag(this, false); };
    }

    /// <summary>Shows the choices. Returns the one picked, or null if cancelled.</summary>
    public static ResetKind? Ask(IWin32Window owner)
    {
        using var dialog = new ResetDialog();
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._chosen : null;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
}
