using System.Runtime.InteropServices;

namespace RoomSwitcher.App.Ui;

/// <summary>
/// Lets a normal resizable window draw its own title bar: the caption is removed from the
/// non-client area (so Windows keeps the shadow, rounded corners, snapping and resize edges),
/// the header acts as the caption, and the top edge still resizes.
/// </summary>
internal static class NativeChrome
{
    public const int WM_NCCALCSIZE = 0x0083;
    public const int WM_NCHITTEST = 0x0084;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int HTTRANSPARENT = -1;
    public const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    public const int HTTOP = 12;
    public const int HTTOPLEFT = 13;
    public const int HTTOPRIGHT = 14;
    private const int SM_CXSIZEFRAME = 32;
    private const int SM_CYSIZEFRAME = 33;
    private const int SM_CXPADDEDBORDER = 92;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NCCALCSIZE_PARAMS
    {
        public RECT rgrc0, rgrc1, rgrc2;
        public IntPtr lppos;
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);

    /// <summary>
    /// Asks Windows, not WinForms: while a window is being restored, Form.WindowState still says
    /// Maximized, which left a strip above the header after maximising and restoring.
    /// </summary>
    public static bool IsMaximized(Form form) => form.IsHandleCreated && IsZoomed(form.Handle);

    public static void BeginDrag(Form form, bool doubleClick)
    {
        if (doubleClick)
        {
            if (form.MaximizeBox)
                form.WindowState = form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            return;
        }
        ReleaseCapture();
        SendMessage(form.Handle, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
    }

    /// <summary>Handles WM_NCCALCSIZE: keeps the side and bottom frame, drops the caption. Returns true if handled.</summary>
    public static bool CalcSize(Form form, ref Message m, Action<Message> defaultProc)
    {
        if (m.WParam == IntPtr.Zero) return false;
        var before = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(m.LParam);
        var top = before.rgrc0.Top;
        defaultProc(m);
        var after = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(m.LParam);
        after.rgrc0.Top = top;
        if (IsMaximized(form))
        {
            // A maximised window hangs its frame off the screen edge; keep the header on screen.
            var dpi = (uint)form.DeviceDpi;
            after.rgrc0.Top += GetSystemMetricsForDpi(SM_CYSIZEFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi);
        }
        Marshal.StructureToPtr(after, m.LParam, false);
        m.Result = IntPtr.Zero;
        return true;
    }

    /// <summary>Thickness of the top resize strip in client pixels.</summary>
    public static int ResizeBorder(Control c) =>
        GetSystemMetricsForDpi(SM_CYSIZEFRAME, (uint)c.DeviceDpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, (uint)c.DeviceDpi);

    /// <summary>After the default hit test: turns the top strip of the client area into a resize edge.</summary>
    public static void HitTest(Form form, ref Message m)
    {
        if ((int)m.Result != HTCLIENT || IsMaximized(form)) return;
        var screen = new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16)));
        var p = form.PointToClient(screen);
        var border = ResizeBorder(form);
        if (p.Y >= border) return;
        m.Result = p.X < border * 2 ? HTTOPLEFT : p.X > form.ClientSize.Width - border * 2 ? HTTOPRIGHT : HTTOP;
    }
}
