using System.ComponentModel;
using RoomSwitcher.Core.Display;
using static RoomSwitcher.Core.Display.Arrangement;

namespace RoomSwitcher.App.Ui;

/// <summary>One screen on the arranger: its nickname(s), a short detail line and where it sits.</summary>
internal sealed record CanvasScreen(string Label, string Detail, Box Box, bool Primary);

/// <summary>
/// A to-scale picture of a room's screens, like Windows' display arranger: click to select, drag
/// to move (screens snap edge to edge), arrow keys nudge the selected screen to the next spot.
/// </summary>
internal sealed class ArrangeCanvas : Control
{
    private List<CanvasScreen> _screens = new();
    private int _selected;
    private int _dragging = -1;
    private Point _dragStart;
    private Box _dragOrigin;
    private Box _dragNow;

    /// <summary>A screen was dropped somewhere new (already snapped).</summary>
    public event Action<int, Box>? ScreenMoved;
    public event Action<int>? SelectionChanged;

    public ArrangeCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint | ControlStyles.Selectable, true);
        TabStop = true;
        BackColor = Theme.Window;
        AccessibleRole = AccessibleRole.Diagram;
        AccessibleName = "Screen arrangement";
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<CanvasScreen> Screens
    {
        get => _screens;
        set
        {
            _screens = value.ToList();
            if (_selected >= _screens.Count) _selected = 0;
            AccessibleDescription = string.Join("; ", _screens.Select(s => $"{s.Label}, {s.Detail}{(s.Primary ? ", main display" : "")}"));
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Selected
    {
        get => _selected;
        set
        {
            if (value == _selected || value < 0 || value >= _screens.Count) return;
            _selected = value;
            Invalidate();
            SelectionChanged?.Invoke(value);
        }
    }

    // ---- desktop ↔ canvas coordinates ----

    private (float Scale, float OffsetX, float OffsetY) Transform()
    {
        if (_screens.Count == 0) return (1, 0, 0);
        // While dragging keep the scale steady so the screen doesn't run away from the pointer.
        if (_dragging >= 0 && _frozen is { } frozen) return frozen;
        var boxes = _screens.Select((s, i) => i == _dragging ? _dragNow : s.Box).ToList();
        var minX = boxes.Min(b => b.X);
        var minY = boxes.Min(b => b.Y);
        var maxX = boxes.Max(b => b.Right);
        var maxY = boxes.Max(b => b.Bottom);
        var margin = 36 * Theme.Scale(this);
        var scale = Math.Min((Width - 2 * margin) / Math.Max(1, maxX - minX), (Height - 2 * margin) / Math.Max(1, maxY - minY));
        scale = Math.Min(scale, 0.18f * Theme.Scale(this));
        var offsetX = (Width - (maxX - minX) * scale) / 2 - minX * scale;
        var offsetY = (Height - (maxY - minY) * scale) / 2 - minY * scale;
        return (scale, offsetX, offsetY);
    }

    private (float, float, float)? _frozen;

    private RectangleF ToCanvas(Box b)
    {
        var (scale, ox, oy) = Transform();
        return new RectangleF(ox + b.X * scale, oy + b.Y * scale, b.Width * scale, b.Height * scale);
    }

    private int HitTest(Point p)
    {
        for (var i = _screens.Count - 1; i >= 0; i--)
            if (ToCanvas(_screens[i].Box).Contains(p)) return i;
        return -1;
    }

    // ---- mouse ----

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        var hit = HitTest(e.Location);
        if (hit >= 0)
        {
            Selected = hit;
            if (_screens.Count > 1 && e.Button == MouseButtons.Left)
            {
                _frozen = Transform();
                _dragging = hit;
                _dragStart = e.Location;
                _dragOrigin = _dragNow = _screens[hit].Box;
                Capture = true;
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging >= 0 && _frozen is { } frozen)
        {
            var scale = frozen.Item1;
            var dx = (int)Math.Round((e.X - _dragStart.X) / scale);
            var dy = (int)Math.Round((e.Y - _dragStart.Y) / scale);
            _dragNow = _dragOrigin with { X = _dragOrigin.X + dx, Y = _dragOrigin.Y + dy };
            Invalidate();
        }
        else
        {
            Cursor = HitTest(e.Location) >= 0 && _screens.Count > 1 ? Cursors.SizeAll : Cursors.Default;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging >= 0)
        {
            var index = _dragging;
            var moved = _dragNow != _dragOrigin;
            _dragging = -1;
            _frozen = null;
            Capture = false;
            if (moved)
            {
                var snapped = Snap(_screens.Select(s => s.Box).ToList(), index, _dragNow);
                ScreenMoved?.Invoke(index, snapped);
            }
            Invalidate();
        }
        base.OnMouseUp(e);
    }

    // ---- keyboard: Tab between screens, arrows move the selected one to the next spot ----

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_screens.Count > 1 && e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
        {
            var b = _screens[_selected].Box;
            var step = Math.Max(b.Width, b.Height);
            var nudged = e.KeyCode switch
            {
                Keys.Left => b with { X = b.X - step },
                Keys.Right => b with { X = b.X + step },
                Keys.Up => b with { Y = b.Y - step },
                _ => b with { Y = b.Y + step },
            };
            ScreenMoved?.Invoke(_selected, Snap(_screens.Select(s => s.Box).ToList(), _selected, nudged));
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    // ---- drawing ----

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.Smooth(g);
        var s = Theme.Scale(this);

        if (_screens.Count == 0)
        {
            TextRenderer.DrawText(g, "This room has no displays turned on.", Theme.Body, ClientRectangle, Theme.Subtext,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        for (var i = 0; i < _screens.Count; i++)
        {
            if (i == _dragging) continue;
            DrawScreen(g, _screens[i], ToCanvas(_screens[i].Box), i == _selected, ghost: false, s);
        }
        if (_dragging >= 0)
        {
            // Where it will land, then the screen under the pointer.
            var landing = Snap(_screens.Select(x => x.Box).ToList(), _dragging, _dragNow);
            using (var pen = new Pen(Theme.Accent, 2 * s) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            using (var path = Theme.RoundedRect(ToCanvas(landing), 6 * s))
                g.DrawPath(pen, path);
            DrawScreen(g, _screens[_dragging], ToCanvas(_dragNow), selected: true, ghost: true, s);
        }
    }

    private void DrawScreen(Graphics g, CanvasScreen screen, RectangleF r, bool selected, bool ghost, float s)
    {
        var inner = RectangleF.Inflate(r, -2 * s, -2 * s);
        using var path = Theme.RoundedRect(inner, 6 * s);
        var fill = selected ? Theme.Accent : Color.FromArgb(214, 221, 232);
        if (ghost) fill = Color.FromArgb(200, fill);
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        using (var p = new Pen(selected ? Theme.AccentPressed : Color.FromArgb(178, 188, 202), (selected ? 2f : 1f) * s)) g.DrawPath(p, path);
        if (selected && Focused && !ghost)
        {
            using var focus = Theme.RoundedRect(RectangleF.Inflate(inner, -5 * s, -5 * s), 4 * s);
            using var dotted = new Pen(Color.White, 1.5f * s) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            g.DrawPath(dotted, focus);
        }

        var text = selected ? Color.White : Theme.Text;
        var sub = selected ? Color.FromArgb(220, 232, 252) : Theme.Subtext;
        var label = new Rectangle((int)inner.X + 6, (int)(inner.Y + inner.Height / 2 - 22 * s), (int)inner.Width - 12, (int)(24 * s));
        var detail = new Rectangle((int)inner.X + 6, label.Bottom, (int)inner.Width - 12, (int)(20 * s));
        TextRenderer.DrawText(g, screen.Label, Theme.BodySemibold, label, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, screen.Detail, Theme.Small, detail, sub,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);

        if (screen.Primary)
        {
            var badge = new RectangleF(inner.X + 8 * s, inner.Y + 8 * s, 44 * s, 20 * s);
            using var bp = Theme.RoundedRect(badge, 10 * s);
            using (var bb = new SolidBrush(selected ? Color.White : Theme.Accent)) g.FillPath(bb, bp);
            TextRenderer.DrawText(g, "Main", Theme.Small, Rectangle.Round(badge), selected ? Theme.Accent : Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
