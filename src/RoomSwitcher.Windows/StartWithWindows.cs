using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace RoomSwitcher.Windows;

/// <summary>
/// A "Room Switcher" shortcut in the Startup folder that starts Room Switcher at sign-in.
/// Older versions used the per-user Run key, which some Windows 11 builds skip without saying why,
/// so that entry is removed whenever the shortcut is written.
/// </summary>
public static class StartWithWindows
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Room Switcher";

    // --startup tells Room Switcher it was started at sign-in, when Windows may still be loading the profile.
    private const string Arguments = "--startup";

    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Room Switcher.lnk");

    public static bool IsEnabled(string exePath) =>
        ReadShortcut() is { } link && string.Equals(link.Target, exePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>True if the shortcut is exactly what this version writes and no old Run entry is left.</summary>
    public static bool IsCurrent(string exePath) =>
        ReadShortcut() is { } link &&
        string.Equals(link.Target, exePath, StringComparison.OrdinalIgnoreCase) &&
        link.Arguments == Arguments && !HasRunEntry();

    public static void Set(bool enabled, string exePath)
    {
        RemoveRunEntry();
        if (enabled) WriteShortcut(exePath);
        else if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
    }

    private static bool HasRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) != null;
    }

    private static void RemoveRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static void WriteShortcut(string exePath)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(exePath);
            link.SetArguments(Arguments);
            link.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? "");
            link.SetDescription("Starts Room Switcher in the system tray");
            link.SetIconLocation(exePath, 0);
            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);
            ((IPersistFile)link).Save(ShortcutPath, true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    private static (string Target, string Arguments)? ReadShortcut()
    {
        if (!File.Exists(ShortcutPath)) return null;
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(ShortcutPath, 0);
            var target = new StringBuilder(1024);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            var arguments = new StringBuilder(1024);
            link.GetArguments(arguments, arguments.Capacity);
            return (target.ToString(), arguments.ToString());
        }
        catch (Exception)
        {
            return null; // damaged shortcut: treated as missing, and rewritten
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
