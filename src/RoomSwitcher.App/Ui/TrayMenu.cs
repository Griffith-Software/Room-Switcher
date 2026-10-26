using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace RoomSwitcher.App.Ui;

/// <summary>Draws the tray menu in the setup window's style: white rounded panel, icons, rounded blue hover.</summary>
internal sealed class TrayMenuRenderer : ToolStripRenderer
{
    protected override void InitializeItem(ToolStripItem item)
    {
        base.InitializeItem(item);
        item.ForeColor = Theme.Text;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(Theme.Card);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // On Windows 11 the system draws the rounded border and shadow (see TrayMenu.RoundCorners).
        if (e.ToolStrip is TrayMenu { SystemRounded: true }) return;
        using var p = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is MenuHeader) return;
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        Theme.Smooth(g);
        var s = e.ToolStrip!.DeviceDpi / 96f;
        var r = new RectangleF(4 * s, 1 * s, e.Item.Width - 8 * s, e.Item.Height - 2 * s);
        using var path = Theme.RoundedRect(r, 6 * s);
        using var b = new SolidBrush(e.Item.Pressed ? Color.FromArgb(206, 222, 246) : Theme.NavSelected);
        g.FillPath(b, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var item = e.Item;
        var isShortcut = item is ToolStripMenuItem mi && !string.IsNullOrEmpty(mi.ShortcutKeyDisplayString) && e.Text == mi.ShortcutKeyDisplayString;
        var color = !item.Enabled ? Theme.Disabled : isShortcut ? Theme.Subtext : Theme.Text;
        var font = isShortcut ? Theme.Small : item.Font;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                    (isShortcut ? TextFormatFlags.Right : TextFormatFlags.Left);
        // Centre on the whole row so text and icon line up.
        var rect = new Rectangle(e.TextRectangle.X, 0, e.TextRectangle.Width, item.Height);
        if (isShortcut) rect.X -= 8;
        TextRenderer.DrawText(e.Graphics, e.Text, font, rect, color, flags);
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image == null) return;
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var r = e.ImageRectangle;
        var dest = new Rectangle(r.X, (e.Item.Height - r.Height) / 2, r.Width, r.Height);
        if (e.Item.Enabled)
        {
            g.DrawImage(e.Image, dest);
            return;
        }
        using var faded = new System.Drawing.Imaging.ImageAttributes();
        faded.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.35f });
        g.DrawImage(e.Image, dest, 0, 0, e.Image.Width, e.Image.Height, GraphicsUnit.Pixel, faded);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var s = e.ToolStrip!.DeviceDpi / 96f;
        var y = e.Item.Height / 2;
        using var p = new Pen(Theme.Border);
        e.Graphics.DrawLine(p, 12 * s, y, e.Item.Width - 12 * s, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { }
}

/// <summary>The tray's context menu, with system rounded corners on Windows 11.</summary>
internal sealed class TrayMenu : ContextMenuStrip
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;

    public bool SystemRounded { get; private set; }

    public TrayMenu()
    {
        Renderer = new TrayMenuRenderer();
        Font = Theme.Body;
        BackColor = Theme.Card;
        ShowImageMargin = true;
        ShowCheckMargin = false;
        Padding = new Padding(4, 6, 4, 6);
        DropShadowEnabled = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var corner = DWMWCP_ROUND;
        SystemRounded = DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int)) == 0;
        if (SystemRounded)
        {
            var border = ColorTranslator.ToWin32(Theme.Border);
            DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
    }

    private float S => DeviceDpi / 96f;

    /// <summary>A Fluent icon glyph drawn into a bitmap for a menu item.</summary>
    public Bitmap GlyphImage(string glyph, Color color, bool filledCircle = false)
    {
        var size = (int)Math.Round(20 * S);
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        Theme.Smooth(g);
        if (filledCircle)
        {
            using var b = new SolidBrush(Theme.Accent);
            g.FillEllipse(b, 0.5f, 0.5f, size - 1.5f, size - 1.5f);
            color = Color.White;
        }
        // Antialiased (not ClearType) text, because the bitmap is transparent.
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var glyphFont = Theme.Glyph(1f);
        using var font = new Font(glyphFont.FontFamily, (filledCircle ? 9f : 13f) * S, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyph, font, brush, new RectangleF(0, 0.5f * S, size, size), format);
        return bmp;
    }

    public ToolStripMenuItem Item(string text, string glyph, EventHandler? click = null, bool accent = false)
    {
        var size = (int)Math.Round(20 * S);
        ImageScalingSize = new Size(size, size);
        var item = new ToolStripMenuItem(text, GlyphImage(glyph, accent ? Theme.Accent : Theme.Text), click)
        {
            Padding = new Padding((int)(4 * S), (int)(7 * S), 0, (int)(7 * S)),
            ImageScaling = ToolStripItemImageScaling.SizeToFit,
            ImageAlign = ContentAlignment.MiddleCenter,
            Font = Theme.Body,
        };
        return item;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ClearItems();
        base.Dispose(disposing);
    }

    /// <summary>Removes the items and frees their icon bitmaps.</summary>
    public void ClearItems()
    {
        var images = Items.OfType<ToolStripItem>().Select(i => i.Image).Where(i => i != null).ToList();
        Items.Clear();
        foreach (var image in images) image!.Dispose();
    }
}

/// <summary>The top of the tray menu: app icon, "Room Switcher", and which room is on.</summary>
internal sealed class MenuHeader : ToolStripItem
{
    private readonly string _subtitle;

    public MenuHeader(string subtitle)
    {
        _subtitle = subtitle;
        Enabled = false;
        AutoSize = false;
        AccessibleName = $"Room Switcher: {subtitle}";
    }

    protected override void OnOwnerChanged(EventArgs e)
    {
        base.OnOwnerChanged(e);
        if (Owner != null) Size = GetPreferredSize(Size.Empty);
    }

    private float S => (Owner?.DeviceDpi ?? 96) / 96f;

    public override Size GetPreferredSize(Size constrainingSize)
    {
        var title = TextRenderer.MeasureText("Room Switcher", Theme.BodySemibold);
        var sub = TextRenderer.MeasureText(_subtitle, Theme.Small);
        return new Size((int)(56 * S) + Math.Max(title.Width, sub.Width) + (int)(24 * S), (int)(56 * S));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var s = S;
        var icon = new RectangleF(14 * s, (Height - 32 * s) / 2, 32 * s, 32 * s);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(Theme.AppImage, icon);
        var x = (int)(icon.Right + 12 * s);
        TextRenderer.DrawText(g, "Room Switcher", Theme.BodySemibold, new Rectangle(x, (int)(9 * s), Width - x, (int)(20 * s)), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, _subtitle, Theme.Small, new Rectangle(x, (int)(29 * s), Width - x, (int)(18 * s)), Theme.Subtext,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}
