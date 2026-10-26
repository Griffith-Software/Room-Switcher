using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using RoomSwitcher.Core.Display;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Rooms;

namespace RoomSwitcher.App;

/// <summary>
/// Shows each monitor's number in the bottom-left corner of its screen, the way Windows' own
/// Identify does: a large white number in a dark rounded box that fades in and out.
/// Mirrored monitors share a screen, so it shows both numbers ("1 | 3").
/// </summary>
internal sealed class IdentifyOverlay : Form
{
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int WM_DPICHANGED = 0x02E0;
    private const int WM_NCHITTEST = 0x0084;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const double ShownOpacity = 0.92;

    private static readonly Color Back = Color.FromArgb(32, 32, 32);
    private static readonly string NumberFamily =
        new InstalledFontCollection().Families.Any(f => f.Name == "Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";

    private readonly string _number;
    private readonly float _scale;
    private readonly Rectangle _target;
    private readonly System.Windows.Forms.Timer _fade = new() { Interval = 15 };
    private bool _closing;

    private IdentifyOverlay(Rectangle screenBounds, string number)
    {
        _number = number;
        _scale = MonitorScale(screenBounds);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Back;
        DoubleBuffered = true;
        Opacity = 0;
        AutoScaleMode = AutoScaleMode.None;
        AccessibleName = $"Display {number}";

        // Same size and place as Windows' Identify: a square-ish box near the bottom-left corner,
        // clear of the taskbar if this monitor has one.
        var work = Screen.FromRectangle(screenBounds).WorkingArea;
        var area = Rectangle.Intersect(work, screenBounds);
        if (area.IsEmpty) area = screenBounds;
        using var font = NumberFont();
        var textWidth = TextRenderer.MeasureText(_number, font, Size.Empty, TextFormatFlags.NoPadding).Width;
        var height = (int)(150 * _scale);
        var width = Math.Max(height, textWidth + (int)(64 * _scale));
        var margin = (int)(28 * _scale);
        _target = new Rectangle(area.Left + margin, area.Bottom - margin - height, width, height);
        Bounds = _target;

        Click += (_, _) => FadeOut();
        _fade.Tick += (_, _) => StepFade();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x80 /* WS_EX_TOOLWINDOW: no taskbar or Alt+Tab entry */;
            return cp;
        }
    }

    private Font NumberFont() => new(NumberFamily, 92 * _scale, FontStyle.Regular, GraphicsUnit.Pixel);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var round = 2; // DWMWCP_ROUND: Windows 11 rounded corners, like its own Identify box
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    protected override void WndProc(ref Message m)
    {
        // The box is placed in real pixels for its own monitor; don't let Windows resize it.
        if (m.Msg == WM_DPICHANGED)
        {
            Bounds = _target;
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = _target;
        _fade.Start();
    }

    private void StepFade()
    {
        if (_closing)
        {
            Opacity = Math.Max(0, Opacity - 0.08);
            if (Opacity <= 0) { _fade.Stop(); Close(); }
        }
        else
        {
            Opacity = Math.Min(ShownOpacity, Opacity + 0.12);
            if (Opacity >= ShownOpacity) _fade.Stop();
        }
    }

    public void FadeOut()
    {
        if (_closing || IsDisposed) return;
        _closing = true;
        _fade.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = NumberFont();
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        // Optical centring: digits sit slightly high in their line box.
        var rect = new RectangleF(0, 4 * _scale, ClientSize.Width, ClientSize.Height);
        g.DrawString(_number, font, Brushes.White, rect, format);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _fade.Dispose();
        base.Dispose(disposing);
    }

    private static float MonitorScale(Rectangle bounds)
    {
        var r = new RECT { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
        var monitor = MonitorFromRect(ref r, 2 /* MONITOR_DEFAULTTONEAREST */);
        return GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out var dpi, out _) == 0 ? dpi / 96f : 1f;
    }

    /// <summary>One number box per screen that's on (not shown yet).</summary>
    public static List<IdentifyOverlay> Create(AppConfig config, DisplayState state)
    {
        var resolution = MonitorResolver.Resolve(config, state);
        var forms = new List<IdentifyOverlay>();
        foreach (var screen in state.Screens)
        {
            var numbers = screen.DevicePaths
                .Select(resolution.MonitorIdFor)
                .Where(id => id != null)
                .Select(id => config.Monitors.FindIndex(m => m.Id == id) + 1)
                .OrderBy(n => n)
                .Select(n => n.ToString())
                .ToList();
            if (numbers.Count == 0) continue;
            forms.Add(new IdentifyOverlay(new Rectangle(screen.X, screen.Y, screen.Width, screen.Height), string.Join(" | ", numbers)));
        }
        return forms;
    }
}

/// <summary>
/// The Identify numbers, kept on screen until they're turned off: by the button again, by clicking
/// each number, or by closing the window that showed them (call Hide from its FormClosed).
/// </summary>
internal sealed class IdentifyToggle
{
    private List<IdentifyOverlay> _shown = new();

    public bool Active => _shown.Count > 0;

    /// <summary>Raised when the numbers appear or disappear, so buttons can change their text.</summary>
    public event Action? Changed;

    /// <summary>Shows the numbers (replacing any already up). Returns how many screens showed one.</summary>
    public int Show(AppConfig config, DisplayState state)
    {
        Hide();
        var forms = IdentifyOverlay.Create(config, state);
        _shown = forms;
        foreach (var form in forms)
        {
            form.FormClosed += (_, _) =>
            {
                if (!forms.Remove(form) || forms.Count > 0 || !ReferenceEquals(forms, _shown)) return;
                Changed?.Invoke();
            };
            form.Show();
        }
        Changed?.Invoke();
        return forms.Count;
    }

    public void Hide()
    {
        if (_shown.Count == 0) return;
        var old = _shown;
        _shown = new();
        foreach (var form in old.ToList()) form.FadeOut();
        Changed?.Invoke();
    }

    /// <summary>Keeps a button's text in step: "Identify" or "Stop identifying".</summary>
    public void Bind(Ui.ModernButton button)
    {
        void Update()
        {
            if (button.IsDisposed) return;
            button.Text = Active ? "Stop identifying" : "Identify";
            button.FitWidth();
            button.Invalidate();
        }
        Changed += Update;
        Update();
    }
}
