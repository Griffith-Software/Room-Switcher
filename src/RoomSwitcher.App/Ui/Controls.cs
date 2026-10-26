using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace RoomSwitcher.App.Ui;

internal enum ButtonKind { Primary, Secondary }

/// <summary>A rounded button with an optional icon glyph, in the primary (blue) or secondary (outlined) style.</summary>
internal sealed class ModernButton : Button
{
    private bool _hover;
    private bool _pressed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind { get; set; }

    /// <summary>Glyph drawn before the text (or after it, with <see cref="GlyphAfter"/>).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Glyph { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool GlyphAfter { get; set; }

    /// <summary>Glyph colour for secondary buttons; blue by default.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? GlyphColor { get; set; }

    public ModernButton(string text, ButtonKind kind = ButtonKind.Secondary, string? glyph = null)
    {
        Text = text;
        Kind = kind;
        Glyph = glyph;
        Font = Theme.BodySemibold;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 40;
        Margin = new Padding(0, 0, 10, 0);
        FitWidth();
    }

    /// <summary>Sizes the button to its text and glyph (in 96-dpi units; the form scales them).</summary>
    public void FitWidth()
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        Width = text + (Glyph != null ? 32 : 0) + 44;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        Theme.Smooth(g);
        var s = Theme.Scale(this);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.RoundedRect(rect, 7 * s);

        Color fill, border, text, glyph;
        if (Kind == ButtonKind.Primary)
        {
            fill = !Enabled ? Color.FromArgb(160, 190, 235) : _pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent;
            border = fill;
            text = glyph = Color.White;
        }
        else
        {
            fill = !Enabled ? Theme.Card : _pressed ? Theme.NavSelected : _hover ? Theme.Window : Theme.Card;
            border = Enabled ? Color.FromArgb(208, 215, 225) : Theme.Border;
            text = Enabled ? Theme.Text : Theme.Disabled;
            glyph = Enabled ? GlyphColor ?? Theme.Accent : Theme.Disabled;
        }

        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        using (var p = new Pen(border, 1f)) g.DrawPath(p, path);

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.HidePrefix;
        var textSize = TextRenderer.MeasureText(g, Text, Font, Size.Empty, flags);
        var glyphWidth = Glyph != null ? (int)(22 * s) : 0;
        var gap = Glyph != null ? (int)(10 * s) : 0;
        var total = textSize.Width + glyphWidth + gap;
        var x = (Width - total) / 2;
        var area = new Rectangle(0, 0, Width, Height);

        if (Glyph != null && !GlyphAfter)
        {
            Theme.DrawGlyph(g, Glyph, 13f, glyph, new RectangleF(x, 0, glyphWidth, Height));
            x += glyphWidth + gap;
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(x, area.Y, textSize.Width + 2, area.Height), text, flags);
        x += textSize.Width + gap;
        if (Glyph != null && GlyphAfter)
            Theme.DrawGlyph(g, Glyph, 11f, glyph, new RectangleF(x, 0, glyphWidth, Height));

        if (Focused && ShowFocusCues)
        {
            var focus = RectangleF.Inflate(rect, -3 * s, -3 * s);
            using var fp = Theme.RoundedRect(focus, 5 * s);
            using var pen = new Pen(Kind == ButtonKind.Primary ? Color.White : Theme.Accent, 1.5f) { DashStyle = DashStyle.Dot };
            g.DrawPath(pen, fp);
        }
    }
}

/// <summary>A panel with a rounded border and background, like a card. Children are padded inside the border.</summary>
internal class RoundedPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 10;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Theme.Border;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = Theme.Card;

    /// <summary>Clips the single docked child to the rounded shape (used around grids).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ClipChild { get; set; }

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Window;
        Padding = new Padding(1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? BackColor);
        Theme.Smooth(g);
        var s = Theme.Scale(this);
        using var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius * s);
        using (var b = new SolidBrush(FillColor)) g.FillPath(b, path);
        using (var p = new Pen(BorderColor)) g.DrawPath(p, path);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (!ClipChild) return;
        var s = Theme.Scale(this);
        foreach (Control child in Controls)
        {
            if (child.Width <= 0 || child.Height <= 0) continue;
            using var path = Theme.RoundedRect(new RectangleF(0, 0, child.Width, child.Height), Math.Max(0, Radius - 1) * s);
            child.Region = new Region(path);
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        PerformLayout();
        Invalidate();
    }
}

/// <summary>A borderless TextBox inside a rounded, padded outline that turns blue when focused.</summary>
internal sealed class TextField : RoundedPanel
{
    public TextBox Box { get; } = new() { BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, Font = Theme.Body };

    public TextField()
    {
        Radius = 6;
        BorderColor = Color.FromArgb(205, 212, 222);
        Padding = new Padding(10, 9, 10, 6);
        Height = 38;
        Box.BackColor = FillColor;
        Controls.Add(Box);
        Box.Enter += (_, _) => { BorderColor = Theme.Accent; Invalidate(); };
        Box.Leave += (_, _) => { BorderColor = Color.FromArgb(205, 212, 222); Invalidate(); };
        Click += (_, _) => Box.Focus();
    }
}

/// <summary>A thin horizontal line.</summary>
internal sealed class Separator : Control
{
    public Separator()
    {
        Height = 1;
        Dock = DockStyle.Bottom;
        BackColor = Theme.Border;
    }
}

/// <summary>The top of the content card: a large blue icon, the step's heading and a one-line description.</summary>
internal sealed class PageHeader : Control
{
    private string _glyph = "";
    private string _title = "";
    private string _subtitle = "";

    public PageHeader()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Card;
        Dock = DockStyle.Top;
        Height = 78;
        AccessibleRole = AccessibleRole.StaticText;
    }

    public void Set(string glyph, string title, string subtitle)
    {
        _glyph = glyph;
        _title = title;
        _subtitle = subtitle;
        AccessibleName = title;
        AccessibleDescription = subtitle;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.Smooth(g);
        var s = Theme.Scale(this);
        var icon = new RectangleF(0, 4 * s, 64 * s, 56 * s);
        Theme.DrawGlyph(g, _glyph, 27f, Theme.Accent, icon);
        var x = (int)(icon.Right + 16 * s);
        var title = new Rectangle(x, (int)(2 * s), Width - x, (int)(38 * s));
        TextRenderer.DrawText(g, _title, Theme.PageTitle, title, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        var sub = new Rectangle(x, title.Bottom + (int)(4 * s), Width - x, Height - title.Bottom - (int)(4 * s));
        TextRenderer.DrawText(g, _subtitle, Theme.PageSubtitle, sub, Theme.Subtext,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A blue circle with a step number in it.</summary>
internal sealed class NumberBadge : Control
{
    private int _number;

    public NumberBadge()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Card;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Number { get => _number; set { _number = value; Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.Smooth(g);
        var d = 34 * Theme.Scale(this);
        var r = new RectangleF(0, (Height - d) / 2, d, d);
        using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, r);
        TextRenderer.DrawText(g, _number.ToString(), Theme.NavTitle, Rectangle.Round(r), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
