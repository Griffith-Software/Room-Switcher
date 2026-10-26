using System.Runtime.InteropServices;

namespace RoomSwitcher.App;

/// <summary>
/// RoomSwitcher.exe is a Windows app, not a console app, so it borrows the console of whatever
/// started it (cmd, PowerShell) to print command-line results.
/// </summary>
internal static class ConsoleOutput
{
    private const int AttachParentProcess = -1;
    private static bool _attached;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    public static void Attach()
    {
        if (_attached) return;
        _attached = true;
        // When output is piped or redirected the handles are already usable; only attach otherwise.
        if (!Console.IsOutputRedirected) AttachConsole(AttachParentProcess);
    }

    public static void WriteLine(string text)
    {
        try
        {
            if (!Console.IsOutputRedirected) Console.WriteLine();
            Console.WriteLine(text.TrimEnd());
            Console.Out.Flush();
        }
        catch (IOException)
        {
            // No console to write to (started from a shortcut); nothing to do.
        }
    }
}
