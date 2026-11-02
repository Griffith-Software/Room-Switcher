using System.Diagnostics;
using RoomSwitcher.App.Ui;
using RoomSwitcher.Core.Support;

namespace RoomSwitcher.App;

/// <summary>
/// Griffith Software's support button: opens griffith.software's support address in the default
/// browser (never inside Room Switcher). Nothing is tracked or stored.
/// </summary>
internal static class SupportButton
{
    public static void Open()
    {
        try { Process.Start(new ProcessStartInfo(SupportLink.Url) { UseShellExecute = true }); }
        catch (Exception) { /* no default browser */ }
    }

    /// <summary>
    /// The support banner: megaphone, "Support" label, "Support Room Switcher", a line about paying
    /// what you want, and a red "Support the App →" button. Same wording as Griffith Software's other programs.
    /// </summary>
    public static Control Banner()
    {
        var card = new RoundedPanel
        {
            Radius = 12, FillColor = Theme.Window, BackColor = Theme.Card, Padding = new Padding(18, 14, 20, 14),
            Height = 112, Margin = new Padding(0, 18, 0, 0),
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, BackColor = Theme.Window };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var megaphone = new PictureBox
        {
            Image = LoadMegaphone(), SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, BackColor = Theme.Window,
            Margin = new Padding(0, 0, 14, 0), AccessibleRole = AccessibleRole.Graphic, AccessibleName = "Megaphone",
        };
        layout.Controls.Add(megaphone, 0, 0);
        layout.SetRowSpan(megaphone, 3);
        layout.Controls.Add(new Label
        {
            Text = "SUPPORT", AutoSize = true, Font = Theme.Small, ForeColor = Theme.Subtext, BackColor = Theme.Window,
            Margin = new Padding(0, 2, 0, 0), AccessibleName = "Support",
        }, 1, 0);
        layout.Controls.Add(new Label
        {
            Text = "Support Room Switcher", AutoSize = true, Font = Theme.PageTitle, ForeColor = Theme.Text, BackColor = Theme.Window,
            Margin = new Padding(0, 2, 0, 0),
        }, 1, 1);
        layout.Controls.Add(new Label
        {
            Text = "Pay what you want, one time or monthly.", AutoSize = true, Font = Theme.PageSubtitle, ForeColor = Theme.Subtext,
            BackColor = Theme.Window, Margin = new Padding(0, 2, 0, 0),
        }, 1, 2);
        var button = new ModernButton("Support the App →", ButtonKind.Red) { Height = 44, Anchor = AnchorStyles.Right, Margin = new Padding(12, 0, 0, 0) };
        button.Click += (_, _) => Open();
        layout.Controls.Add(button, 2, 0);
        layout.SetRowSpan(button, 3);
        card.Controls.Add(layout);
        return card;
    }

    private static Image? LoadMegaphone()
    {
        using var stream = typeof(SupportButton).Assembly.GetManifestResourceStream("RoomSwitcher.App.Assets.megaphone.png");
        return stream == null ? null : Image.FromStream(stream);
    }
}
