using System.Runtime.InteropServices;

namespace RoomSwitcher.App.Ui;

/// <summary>
/// The app's own message box, in the setup window's style: a white rounded card centred on its
/// window, an icon, a short heading, the details, and buttons on the right. Same calling shape as
/// MessageBox.Show. Can count down and answer "no" by itself (used for "Keep these display settings?").
/// </summary>
internal sealed class ThemedDialog : Form
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private readonly MessageBoxIcon _icon;
    private readonly Label _countdown = new() { AutoSize = true, Font = Theme.Body, ForeColor = Theme.Subtext, Margin = new Padding(0, 10, 0, 0), Visible = false };
    private int _seconds;
    private Func<int, string>? _countdownText;
    private DialogResult _timeoutResult = DialogResult.Cancel;

    private ThemedDialog(string message, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton,
        string? yesText, string? noText)
    {
        _icon = icon;
        Text = "Room Switcher";
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
        Padding = new Padding(84, 24, 24, 20);
        MinimumSize = new Size(440, 0);

        // First paragraph is the heading; the rest are details.
        var parts = message.Split("\n\n", 2);
        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, BackColor = Theme.Card, WrapContents = false };
        flow.Controls.Add(new Label
        {
            Text = parts[0], AutoSize = true, MaximumSize = new Size(380, 0), Font = Theme.PageSubtitle, ForeColor = Theme.Text,
            UseMnemonic = false,
        });
        if (parts.Length > 1)
        {
            flow.Controls.Add(new Label
            {
                Text = parts[1], AutoSize = true, MaximumSize = new Size(380, 0), Font = Theme.Body, ForeColor = Theme.Subtext,
                Margin = new Padding(0, 8, 0, 0), UseMnemonic = false,
            });
        }
        flow.Controls.Add(_countdown);

        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, AutoSize = true, BackColor = Theme.Card, WrapContents = false,
            Margin = new Padding(0, 22, 0, 0), Anchor = AnchorStyles.Right,
        };
        var made = new List<ModernButton>();
        ModernButton Add(string text, DialogResult result, bool primary)
        {
            var b = new ModernButton(text, primary ? ButtonKind.Primary : ButtonKind.Secondary) { Margin = new Padding(10, 0, 0, 0) };
            b.Width = Math.Max(110, b.Width);
            b.Click += (_, _) => { DialogResult = result; Close(); };
            made.Add(b);
            return b;
        }
        // Built right to left, so the last added sits leftmost.
        switch (buttons)
        {
            case MessageBoxButtons.YesNo:
                row.Controls.Add(Add(noText ?? "No", DialogResult.No, false));
                row.Controls.Add(Add(yesText ?? "Yes", DialogResult.Yes, true));
                CancelButton = made[0];
                break;
            case MessageBoxButtons.YesNoCancel:
                row.Controls.Add(Add("Cancel", DialogResult.Cancel, false));
                row.Controls.Add(Add(noText ?? "No", DialogResult.No, false));
                row.Controls.Add(Add(yesText ?? "Yes", DialogResult.Yes, true));
                CancelButton = made[0];
                break;
            case MessageBoxButtons.OKCancel:
                row.Controls.Add(Add("Cancel", DialogResult.Cancel, false));
                row.Controls.Add(Add(yesText ?? "OK", DialogResult.OK, true));
                CancelButton = made[0];
                break;
            default:
                row.Controls.Add(Add(yesText ?? "OK", DialogResult.OK, true));
                CancelButton = made[0];
                break;
        }
        // The default button is counted left to right, like MessageBox.
        var leftToRight = Enumerable.Reverse(made).ToList();
        var index = defaultButton switch { MessageBoxDefaultButton.Button2 => 1, MessageBoxDefaultButton.Button3 => 2, _ => 0 };
        var chosen = leftToRight[Math.Min(index, leftToRight.Count - 1)];
        AcceptButton = chosen;
        Shown += (_, _) => chosen.Focus();

        var outer = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, RowCount = 2, BackColor = Theme.Card, Dock = DockStyle.Fill };
        outer.Controls.Add(flow, 0, 0);
        outer.Controls.Add(row, 0, 1);
        Controls.Add(outer);

        // Drag the box by any empty part of it.
        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) NativeChrome.BeginDrag(this, false); };
        flow.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) NativeChrome.BeginDrag(this, false); };
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
        var g = e.Graphics;
        Theme.Smooth(g);
        using (var border = new Pen(Theme.Border)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        var s = Theme.Scale(this);
        var (glyph, color) = _icon switch
        {
            MessageBoxIcon.Warning => ("", Theme.Warning),
            MessageBoxIcon.Error => ("", Theme.Danger),
            MessageBoxIcon.Question => ("", Theme.Accent),
            _ => ("", Theme.Accent),
        };
        Theme.DrawGlyph(g, glyph, 22f, color, new RectangleF(24 * s, 22 * s, 40 * s, 40 * s));
    }

    private void StartCountdown(int seconds, Func<int, string> text, DialogResult whenDone)
    {
        _seconds = seconds;
        _countdownText = text;
        _timeoutResult = whenDone;
        _countdown.Visible = true;
        _countdown.Text = text(seconds);
        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) =>
        {
            _seconds--;
            _countdown.Text = _countdownText!(_seconds);
            if (_seconds > 0) return;
            timer.Stop();
            DialogResult = _timeoutResult;
            Close();
        };
        Shown += (_, _) => timer.Start();
        FormClosed += (_, _) => timer.Dispose();
    }

    public static DialogResult Show(IWin32Window owner, string message, string caption, MessageBoxButtons buttons,
        MessageBoxIcon icon, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1, string? yesText = null, string? noText = null)
    {
        using var dialog = new ThemedDialog(message, buttons, icon, defaultButton, yesText, noText);
        return dialog.ShowDialog(owner);
    }

    /// <summary>"Keep these display settings?" Reverts by itself if nobody answers, like Windows.</summary>
    public static bool AskKeepDisplaySettings(IWin32Window owner, int seconds = 15)
    {
        using var dialog = new ThemedDialog("Keep these display settings?", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1, "Keep changes", "Revert");
        dialog.TopMost = true;
        dialog.StartCountdown(seconds, s => $"Reverting to the previous display settings in {s} second{(s == 1 ? "" : "s")}.", DialogResult.No);
        return dialog.ShowDialog(owner) == DialogResult.Yes;
    }
}
