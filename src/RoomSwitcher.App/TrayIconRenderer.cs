using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using RoomSwitcher.App.Ui;

namespace RoomSwitcher.App;

/// <summary>Draws the tray icon: the app icon, with a small badge showing the current room's first letter.</summary>
internal static class TrayIconRenderer
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Render(string? roomName, bool switching)
    {
        var size = SystemInformation.SmallIconSize.Width;
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            // While switching, the icon is drawn faded.
            using var attributes = new ImageAttributes();
            if (switching)
                attributes.SetColorMatrix(new ColorMatrix { Matrix33 = 0.45f });
            g.DrawImage(Theme.AppImage, new Rectangle(0, 0, size, size), 0, 0, Theme.AppImage.Width, Theme.AppImage.Height,
                GraphicsUnit.Pixel, attributes);

            var letter = string.IsNullOrWhiteSpace(roomName) ? null : roomName.Trim()[..1].ToUpperInvariant();
            if (letter != null && !switching)
            {
                var d = size * 0.62f;
                var badge = new RectangleF(size - d, size - d, d - 0.5f, d - 0.5f);
                using (var white = new SolidBrush(Color.White)) g.FillEllipse(white, badge);
                using (var ring = new Pen(Color.FromArgb(12, 35, 76), Math.Max(1f, size / 24f))) g.DrawEllipse(ring, badge);
                using var font = new Font("Segoe UI", d * 0.66f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(Theme.Accent);
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(letter, font, brush, new RectangleF(badge.X, badge.Y + d * 0.04f, badge.Width, badge.Height), format);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
