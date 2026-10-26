namespace RoomSwitcher.App.Ui;

internal sealed record StepInfo(string Title, string Subtitle, string Glyph, string PageSubtitle);

/// <summary>The list of setup steps down the left side: number, icon, title and a short description.</summary>
internal sealed class StepNav : Control
{
    private readonly List<StepInfo> _steps = new();
    private int _selected;
    private int _hover = -1;

    public event EventHandler<int>? StepClicked;

    /// <summary>Shown in the bottom-left corner, e.g. "Version 0.5.0".</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Footer { get; set; } = "";

    public StepNav()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        BackColor = Theme.Window;
        Font = Theme.Body;
        TabStop = true;
        AccessibleRole = AccessibleRole.PageTabList;
        AccessibleName = "Setup steps";
    }

    public void Add(StepInfo step)
    {
        _steps.Add(step);
        Invalidate();
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    private float S => Theme.Scale(this);
    private int ItemHeight => (int)(80 * S);
    private int ItemGap => (int)(4 * S);

    private Rectangle ItemRect(int index) =>
        new((int)(14 * S), (int)(16 * S) + index * (ItemHeight + ItemGap), Width - (int)(26 * S), ItemHeight);

    private int HitTest(Point p)
    {
        for (var i = 0; i < _steps.Count; i++) if (ItemRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = HitTest(e.Location);
        if (hit != _hover) { _hover = hit; Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        var hit = HitTest(e.Location);
        if (hit >= 0) StepClicked?.Invoke(this, hit);
        base.OnMouseClick(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Up && _selected > 0) StepClicked?.Invoke(this, _selected - 1);
        if (e.KeyCode == Keys.Down && _selected < _steps.Count - 1) StepClicked?.Invoke(this, _selected + 1);
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.Smooth(g);
        var s = S;

        if (Footer.Length > 0)
        {
            var footer = new Rectangle((int)(28 * s), Height - (int)(40 * s), Width - (int)(40 * s), (int)(24 * s));
            TextRenderer.DrawText(g, Footer, Theme.Small, footer, Theme.Subtext,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        for (var i = 0; i < _steps.Count; i++)
        {
            var step = _steps[i];
            var r = ItemRect(i);
            var selected = i == _selected;

            if (selected || i == _hover)
            {
                using var path = Theme.RoundedRect(r, 12 * s);
                using var fill = new SolidBrush(selected ? Theme.NavSelected : Theme.NavHover);
                g.FillPath(fill, path);
                if (selected)
                {
                    using var pen = new Pen(Color.FromArgb(206, 222, 246));
                    g.DrawPath(pen, path);
                }
                if (selected && Focused)
                {
                    using var focus = Theme.RoundedRect(RectangleF.Inflate(r, -3 * s, -3 * s), 10 * s);
                    using var pen = new Pen(Theme.Accent, 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
                    g.DrawPath(pen, focus);
                }
            }

            // Number circle
            var d = 34 * s;
            var circle = new RectangleF(r.X + 12 * s, r.Y + (r.Height - d) / 2, d, d);
            using (var b = new SolidBrush(selected ? Theme.Accent : Theme.NumberIdle)) g.FillEllipse(b, circle);
            TextRenderer.DrawText(g, (i + 1).ToString(), Theme.NavTitle, Rectangle.Round(circle),
                selected ? Color.White : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // Icon
            var icon = new RectangleF(circle.Right + 12 * s, r.Y, 36 * s, r.Height);
            Theme.DrawGlyph(g, step.Glyph, 17f, Theme.Text, icon);

            // Title and description
            var textX = (int)(icon.Right + 12 * s);
            var titleRect = new Rectangle(textX, r.Y + (int)(17 * s), r.Right - textX - (int)(6 * s), (int)(24 * s));
            var subRect = new Rectangle(textX, titleRect.Bottom + (int)(2 * s), titleRect.Width, (int)(22 * s));
            TextRenderer.DrawText(g, step.Title, Theme.NavTitle, titleRect, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, step.Subtitle, Theme.Small, subRect, Theme.Subtext,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>The dark header band: app icon, title, subtitle, and the window's own minimise / maximise / close buttons.</summary>
internal sealed class WindowHeader : Control
{
    private readonly string _title;
    private readonly string _subtitle;
    private int _hoverButton = -1;
    private int _pressedButton = -1;

    public WindowHeader(string title, string subtitle)
    {
        _title = title;
        _subtitle = subtitle;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Dock = DockStyle.Top;
        Height = 84;
        AccessibleRole = AccessibleRole.TitleBar;
        AccessibleName = title;
    }

    private float S => Theme.Scale(this);

    protected override void WndProc(ref Message m)
    {
        // Let the top few pixels fall through to the window, so it can be resized from the top edge.
        if (m.Msg == NativeChrome.WM_NCHITTEST && FindForm() is { WindowState: not FormWindowState.Maximized })
        {
            var screen = new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16)));
            if (PointToClient(screen).Y < NativeChrome.ResizeBorder(this) && ButtonAt(PointToClient(screen)) < 0)
            {
                m.Result = NativeChrome.HTTRANSPARENT;
                return;
            }
        }
        base.WndProc(ref m);
    }

    /// <summary>Caption buttons from the right: 0 close, 1 maximise/restore, 2 minimise.</summary>
    public Rectangle ButtonRect(int index)
    {
        var w = (int)(46 * S);
        var h = (int)(36 * S);
        return new Rectangle(Width - w * (index + 1), 0, w, h);
    }

    public int ButtonAt(Point p)
    {
        for (var i = 0; i < 3; i++) if (ButtonRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = ButtonAt(e.Location);
        if (hit != _hoverButton) { _hoverButton = hit; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hoverButton = -1; _pressedButton = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressedButton = ButtonAt(e.Location);
        if (_pressedButton < 0 && e.Button == MouseButtons.Left && FindForm() is { } form)
        {
            // Drag the window by the header (double-click maximises, like a title bar).
            NativeChrome.BeginDrag(form, e.Clicks > 1);
            return;
        }
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        var hit = ButtonAt(e.Location);
        if (hit >= 0 && hit == _pressedButton && FindForm() is { } form)
        {
            switch (hit)
            {
                case 0: form.Close(); break;
                case 1: form.WindowState = form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; break;
                case 2: form.WindowState = FormWindowState.Minimized; break;
            }
        }
        _pressedButton = -1;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var s = S;
        using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(ClientRectangle, Theme.HeaderTop, Theme.HeaderBottom, 20f))
            g.FillRectangle(brush, ClientRectangle);
        // A soft light glow on the right, like the mockup's header.
        using (var glow = new System.Drawing.Drawing2D.GraphicsPath())
        {
            var glowRect = new RectangleF(Width * 0.45f, -Height * 1.2f, Width * 0.8f, Height * 3.2f);
            glow.AddEllipse(glowRect);
            using var pgb = new System.Drawing.Drawing2D.PathGradientBrush(glow)
            {
                CenterColor = Color.FromArgb(55, 120, 170, 230),
                SurroundColors = [Color.FromArgb(0, 120, 170, 230)],
            };
            g.FillRectangle(pgb, ClientRectangle);
        }
        Theme.Smooth(g);

        var iconSize = 54 * s;
        var icon = new RectangleF(26 * s, (Height - iconSize) / 2, iconSize, iconSize);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(Theme.AppImage, icon);

        var textX = (int)(icon.Right + 22 * s);
        var titleRect = new Rectangle(textX, (int)(Height / 2f - 30 * s), Width - textX - (int)(150 * s), (int)(34 * s));
        TextRenderer.DrawText(g, _title, Theme.HeaderTitle, titleRect, Theme.HeaderText,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        var subRect = new Rectangle(textX, titleRect.Bottom + (int)(3 * s), titleRect.Width, (int)(24 * s));
        TextRenderer.DrawText(g, _subtitle, Theme.HeaderSubtitle, subRect, Theme.HeaderSubtext,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        var maximized = FindForm()?.WindowState == FormWindowState.Maximized;
        string[] glyphs = [Theme.Glyphs.Close, maximized ? Theme.Glyphs.Restore : Theme.Glyphs.Maximize, Theme.Glyphs.Minimize];
        for (var i = 0; i < 3; i++)
        {
            var r = ButtonRect(i);
            if (i == _hoverButton)
            {
                var back = i == 0 ? Color.FromArgb(_pressedButton == 0 ? 200 : 232, 196, 43, 28) : Color.FromArgb(_pressedButton == i ? 60 : 36, 255, 255, 255);
                using var b = new SolidBrush(back);
                g.FillRectangle(b, r);
            }
            Theme.DrawGlyph(g, glyphs[i], 8.5f, Color.White, r);
        }
    }
}
