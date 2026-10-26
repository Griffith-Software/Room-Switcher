using System.ComponentModel;
using System.Runtime.InteropServices;
using RoomSwitcher.Core.Hotkeys;

namespace RoomSwitcher.App.Ui;

/// <summary>
/// A field that records a keyboard shortcut: click it and press the keys. Backspace or the ×
/// clears it; Esc puts back what was there.
/// </summary>
internal sealed class HotkeyBox : Control
{
    [DllImport("user32.dll")] private static extern short GetKeyState(int vk);

    private string _value = "";
    private string _beforeEdit = "";
    private HotkeyModifiers _held;
    private bool _hoverClear;

    /// <summary>Raised when a new shortcut is recorded or cleared. The value is "" or e.g. "Ctrl+Alt+1".</summary>
    public event EventHandler? ValueChanged;

    public HotkeyBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint | ControlStyles.Selectable, true);
        TabStop = true;
        Height = 38;
        Font = Theme.Body;
        BackColor = Theme.Card;
        Cursor = Cursors.IBeam;
        AccessibleRole = AccessibleRole.Text;
        AccessibleDescription = "Press the keys for the shortcut. Backspace clears it.";
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get => _value;
        set
        {
            _value = value ?? "";
            AccessibleName = string.IsNullOrEmpty(_value) ? "Keyboard shortcut: none" : $"Keyboard shortcut: {DisplayText}";
            Invalidate();
        }
    }

    private string DisplayText => HotkeyRules.Parse(_value)?.Display ?? _value;

    private float S => Theme.Scale(this);
    private Rectangle ClearRect => new(Width - (int)(34 * S), 0, (int)(30 * S), Height);

    private void Set(string value)
    {
        if (value == _value) return;
        Value = value;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnGotFocus(EventArgs e) { _beforeEdit = _value; _held = HotkeyModifiers.None; Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { _held = HotkeyModifiers.None; Invalidate(); base.OnLostFocus(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!string.IsNullOrEmpty(_value) && ClearRect.Contains(e.Location)) Set("");
        Focus();
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hover = ClearRect.Contains(e.Location) && !string.IsNullOrEmpty(_value);
        if (hover != _hoverClear) { _hoverClear = hover; Cursor = hover ? Cursors.Hand : Cursors.IBeam; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hoverClear = false; Invalidate(); base.OnMouseLeave(e); }

    // Take every key while focused (Alt combinations included), except plain Tab so focus can move on.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!Focused) return base.ProcessCmdKey(ref msg, keyData);
        var key = keyData & Keys.KeyCode;
        if (key == Keys.Tab && (keyData & (Keys.Control | Keys.Alt)) == 0) return base.ProcessCmdKey(ref msg, keyData);
        const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
        if (msg.Msg is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            HandleKey(key, keyData & Keys.Modifiers);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _held = CurrentModifiers(e.Modifiers);
        Invalidate();
        base.OnKeyUp(e);
    }

    private static bool WinDown() => GetKeyState(0x5B) < 0 || GetKeyState(0x5C) < 0;

    private static HotkeyModifiers CurrentModifiers(Keys modifiers)
    {
        // The message's flags and the live key state can disagree for injected or fast key presses; trust either.
        var m = HotkeyModifiers.None;
        if (modifiers.HasFlag(Keys.Control) || GetKeyState(0x11) < 0) m |= HotkeyModifiers.Ctrl;
        if (modifiers.HasFlag(Keys.Alt) || GetKeyState(0x12) < 0) m |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(Keys.Shift) || GetKeyState(0x10) < 0) m |= HotkeyModifiers.Shift;
        if (WinDown()) m |= HotkeyModifiers.Win;
        return m;
    }

    private void HandleKey(Keys key, Keys modifiers)
    {
        var mods = CurrentModifiers(modifiers);
        switch (key)
        {
            case Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin:
                _held = mods;
                Invalidate();
                return;
            case Keys.Escape when mods == HotkeyModifiers.None:
                Value = _beforeEdit;
                _held = HotkeyModifiers.None;
                return;
            case Keys.Back or Keys.Delete when mods == HotkeyModifiers.None:
                _held = HotkeyModifiers.None;
                Set("");
                return;
        }
        _held = HotkeyModifiers.None;
        if (!Hotkey.IsKnownKey((int)key)) { System.Media.SystemSounds.Beep.Play(); Invalidate(); return; }
        Set(new Hotkey(mods, (int)key).ToString());
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        Theme.Smooth(g);
        var s = S;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.RoundedRect(rect, 6 * s);
        using (var b = new SolidBrush(Enabled ? Color.White : Theme.Window)) g.FillPath(b, path);
        using (var p = new Pen(Focused ? Theme.Accent : Color.FromArgb(205, 212, 222), Focused ? 1.6f : 1f)) g.DrawPath(p, path);

        string text;
        Color color;
        if (Focused && _held != HotkeyModifiers.None)
        {
            text = new Hotkey(_held, 0).Display.Replace("Key0", "…");
            color = Theme.Accent;
        }
        else if (Focused && string.IsNullOrEmpty(_value))
        {
            text = "Press the keys you want…";
            color = Theme.Subtext;
        }
        else if (string.IsNullOrEmpty(_value))
        {
            text = "None";
            color = Theme.Subtext;
        }
        else
        {
            text = DisplayText;
            color = Enabled ? Theme.Text : Theme.Disabled;
        }

        var textRect = new Rectangle((int)(12 * s), 0, Width - (int)(50 * s), Height);
        TextRenderer.DrawText(g, text, string.IsNullOrEmpty(_value) || _held != HotkeyModifiers.None ? Theme.Body : Theme.BodySemibold,
            textRect, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (!string.IsNullOrEmpty(_value) && Enabled)
        {
            var clear = ClearRect;
            if (_hoverClear)
            {
                using var hb = new SolidBrush(Theme.NavHover);
                var d = 24 * s;
                g.FillEllipse(hb, clear.X + (clear.Width - d) / 2, clear.Y + (clear.Height - d) / 2, d, d);
            }
            Theme.DrawGlyph(g, Theme.Glyphs.Close, 7.5f, Theme.Subtext, clear);
        }
    }
}
