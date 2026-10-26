using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RoomSwitcher.Core.Layouts;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Windows.Native;
using static RoomSwitcher.Windows.Native.WindowApi;

namespace RoomSwitcher.Windows.Layouts;

/// <summary>Reads and sets where ordinary application windows sit, with Get/SetWindowPlacement.</summary>
public sealed class WindowsWindowBackend : IWindowBackend
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private readonly bool _selfElevated = IsElevated((uint)Environment.ProcessId);

    public IReadOnlyList<SavedWindow> Enumerate()
    {
        var result = new List<SavedWindow>();
        var shell = GetShellWindow();
        var exeCache = new Dictionary<uint, string>();

        EnumWindows((hwnd, _) =>
        {
            try
            {
                if (hwnd == shell || !IsOrdinaryWindow(hwnd)) return true;
                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == _ownProcessId) return true;

                var className = ClassName(hwnd);
                if (ShellClasses.Contains(className)) return true;
                var title = Title(hwnd);
                if (title.Length == 0) return true;

                var placement = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
                if (!GetWindowPlacement(hwnd, ref placement)) return true;

                if (!exeCache.TryGetValue(pid, out var exe)) exeCache[pid] = exe = ExeName(pid);

                result.Add(new SavedWindow
                {
                    Hwnd = hwnd.ToInt64(),
                    Exe = exe,
                    Class = className,
                    Title = title,
                    ShowCmd = (int)placement.showCmd,
                    RestoreToMaximized = (placement.flags & WPF_RESTORETOMAXIMIZED) != 0,
                    NormalRect = new RectData(placement.rcNormalPosition.Left, placement.rcNormalPosition.Top,
                        placement.rcNormalPosition.Right, placement.rcNormalPosition.Bottom),
                    MinPos = new PointData(placement.ptMinPosition.X, placement.ptMinPosition.Y),
                    MaxPos = new PointData(placement.ptMaxPosition.X, placement.ptMaxPosition.Y),
                });
            }
            catch (Exception e)
            {
                Debug.WriteLine($"Skipping window {hwnd}: {e.Message}");
            }
            return true;
        }, IntPtr.Zero);

        return result;
    }

    public bool Apply(SavedWindow live, SavedWindow placement)
    {
        var hwnd = new IntPtr(live.Hwnd);
        if (!IsWindow(hwnd)) return false;

        // A program running as administrator ignores placement from a normal-privilege program.
        GetWindowThreadProcessId(hwnd, out var pid);
        if (!_selfElevated && IsElevated(pid)) return false;

        var wp = new WINDOWPLACEMENT
        {
            length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>(),
            showCmd = placement.ShowCmd switch
            {
                (int)SW_SHOWMAXIMIZED => SW_SHOWMAXIMIZED,
                (int)SW_SHOWMINIMIZED => SW_SHOWMINNOACTIVE,
                _ => SW_SHOWNOACTIVATE,
            },
            ptMinPosition = new POINT { X = placement.MinPos.X, Y = placement.MinPos.Y },
            ptMaxPosition = new POINT { X = placement.MaxPos.X, Y = placement.MaxPos.Y },
            rcNormalPosition = new RECT
            {
                Left = placement.NormalRect.Left, Top = placement.NormalRect.Top,
                Right = placement.NormalRect.Right, Bottom = placement.NormalRect.Bottom,
            },
        };
        if (placement.ShowCmd == (int)SW_SHOWMINIMIZED && placement.RestoreToMaximized) wp.flags |= WPF_RESTORETOMAXIMIZED;
        // Don't wait on a program that has stopped responding.
        if (IsHungAppWindow(hwnd)) wp.flags |= WPF_ASYNCWINDOWPLACEMENT;

        return SetWindowPlacement(hwnd, ref wp);
    }

    private static bool IsOrdinaryWindow(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd)) return false;
        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        var appWindow = (exStyle & WS_EX_APPWINDOW) != 0;
        if (!appWindow && (exStyle & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0) return false;
        if (!appWindow && GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false;
        // Cloaked: on another virtual desktop, or a suspended Store app.
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        return true;
    }

    private static string ClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string Title(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0) return "";
        var sb = new StringBuilder(length + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string ExeName(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return "";
        try
        {
            var sb = new StringBuilder(1024);
            var size = (uint)sb.Capacity;
            return QueryFullProcessImageName(process, 0, sb, ref size) ? Path.GetFileName(sb.ToString()) : "";
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static bool IsElevated(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return false;
        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return false;
            try
            {
                return GetTokenInformation(token, TokenElevation, out var elevated, sizeof(int), out _) && elevated != 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }
}
