using System.Diagnostics;
using RoomSwitcher.Core.Audio;
using RoomSwitcher.Core.Devices;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Rooms;
using RoomSwitcher.Core.Storage;
using RoomSwitcher.Windows;

namespace RoomSwitcher.App;

/// <summary>
/// The Reset button's three choices: reset rooms (keep nicknames), reset everything, or uninstall.
/// Each first puts back what Room Switcher changed in Windows (see <see cref="RestoreWindowsAsync"/>).
/// Returns plain-language problems, or null when it all went well.
/// </summary>
internal static class ResetActions
{
    // The files a release zip puts next to RoomSwitcher.exe; uninstalling removes these and nothing else there.
    private static readonly string[] ShippedFiles = ["RoomSwitcher.exe", "README.md", "CHANGELOG.md", "LICENSE", "NOTICE", "THIRD-PARTY-NOTICES.md"];

    public static async Task<string?> ResetRoomsAsync(AppHost host)
    {
        var problems = await RestoreWindowsAsync(host);
        try
        {
            host.ReplaceConfig(ResetRules.WithoutRooms(host.Config));
            host.ReplaceLayouts(new LayoutsFile());
        }
        catch (Exception e)
        {
            problems.Add($"Couldn't reset the rooms. {e.Message}");
        }
        return Join(problems);
    }

    /// <summary>As on first use: also removes the device switching helper (one administrator prompt, if it was set up).</summary>
    public static async Task<string?> ResetEverythingAsync(AppHost host)
    {
        var problems = await RestoreWindowsAsync(host);
        if (await RemoveHelperAsync(host) is { } helperError)
            problems.Add($"The device switching helper is still set up. {helperError}");
        try
        {
            StartWithWindows.Set(false, AppHost.ExePath);
            DeleteSettingsFiles(host.Paths);
            host.ReplaceConfig(ResetRules.Fresh(), save: false); // first-run setup saves when it's finished
            host.ReplaceLayouts(new LayoutsFile(), save: false);
        }
        catch (Exception e)
        {
            problems.Add($"Couldn't reset everything. {e.Message}");
        }
        return Join(problems);
    }

    /// <summary>
    /// Removes Room Switcher: the device switching helper (one administrator prompt, if it was set up),
    /// the startup and Start menu shortcuts, all settings and logs, then the program's own files once it has closed.
    /// </summary>
    public static async Task<string?> UninstallAsync(AppHost host)
    {
        var problems = await RestoreWindowsAsync(host);
        if (await RemoveHelperAsync(host) is { } helperError)
            return Join([.. problems, $"Room Switcher wasn't uninstalled. {helperError}"]);

        try
        {
            StartWithWindows.Set(false, AppHost.ExePath);
            DeleteStartMenuShortcuts();
            DeleteSettingsFiles(host.Paths);
            var logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Room Switcher");
            if (Path.IsPathFullyQualified(logs) && Directory.Exists(logs)) Directory.Delete(logs, recursive: true);
        }
        catch (Exception e)
        {
            return Join([.. problems, $"Room Switcher was only partly uninstalled. {e.Message}"]);
        }
        DeleteProgramAfterExit();
        return Join(problems);
    }

    /// <summary>
    /// Puts back what Room Switcher changed in Windows: devices a room turned off are turned on, every display is
    /// turned on at its own resolution and the recommended scale, and the speakers and microphone Windows had
    /// before a room first changed them are the defaults again.
    /// </summary>
    private static async Task<List<string>> RestoreWindowsAsync(AppHost host)
    {
        var problems = new List<string>();
        await TurnDevicesBackOnAsync(host);

        try
        {
            var display = await Task.Run(host.Display.ResetToDefaults);
            if (!display.Ok) problems.Add($"Couldn't put the displays back to Windows' settings. {display.Message}");
        }
        catch (Exception e)
        {
            problems.Add($"Couldn't put the displays back to Windows' settings. {e.Message}");
        }

        // On this (STA) thread, as the sound API needs; it waits for a TV's sound to come back with its screen.
        if (host.Service.Layouts.SoundBefore is { } sound && (sound.Output != null || sound.Input != null))
        {
            try
            {
                var before = new Room { Name = "Windows", AudioOutput = sound.Output, AudioInput = sound.Input };
                problems.AddRange(await AudioRules.ApplyAsync(host.Audio, before, (ms, ct) => Task.Delay(ms, ct)));
            }
            catch (Exception e)
            {
                problems.Add($"Couldn't put the speakers and microphone back. {e.Message}");
            }
        }
        return problems;
    }

    /// <summary>Removes the device switching helper and its scheduled task, if set up. Returns why it couldn't, or null.</summary>
    private static async Task<string?> RemoveHelperAsync(AppHost host)
    {
        DeviceControlStatus helper;
        try { helper = await Task.Run(() => host.Devices.Status); }
        catch (Exception) { helper = DeviceControlStatus.NotSetUp; }
        if (helper == DeviceControlStatus.NotSetUp) return null;
        var (removed, error) = await Task.Run(() => (DeviceHelper.RunElevated(DeviceHelper.RemoveArg, out var e), e));
        return removed ? null : error;
    }

    private static string? Join(List<string> problems) => problems.Count == 0 ? null : string.Join(" ", problems);

    /// <summary>Devices a room turned off would stay off with nothing to switch them back on.</summary>
    private static async Task TurnDevicesBackOnAsync(AppHost host)
    {
        try
        {
            if (await Task.Run(() => host.Devices.Status) == DeviceControlStatus.NotSetUp) return;
            var off = (await Task.Run(() => host.Devices.List()))
                .Where(d => d.Connected && !d.Enabled && UsbRules.Find(host.Config, d.Id) is { Managed: true })
                .Select(d => d.Id).ToList();
            if (off.Count > 0) await host.Devices.ApplyAsync(off, []);
        }
        catch (Exception)
        {
            // A device that can't be turned on doesn't stop the reset; it can be turned on in Device Manager.
        }
    }

    /// <summary>config.json, its previous version and layouts.json. In the usual place, their whole folder.</summary>
    private static void DeleteSettingsFiles(AppPaths paths)
    {
        if (paths.Portable)
        {
            foreach (var file in new[] { paths.ConfigPath, JsonStore.PreviousPath(paths.ConfigPath), paths.LayoutsPath })
                if (File.Exists(file)) File.Delete(file);
        }
        else if (Directory.Exists(paths.DataDirectory))
        {
            Directory.Delete(paths.DataDirectory, recursive: true);
        }
    }

    /// <summary>Start menu shortcuts that open this copy of Room Switcher.</summary>
    private static void DeleteStartMenuShortcuts()
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (!Directory.Exists(programs)) return;
        foreach (var link in Directory.EnumerateFiles(programs, "*.lnk", SearchOption.AllDirectories))
        {
            if (StartWithWindows.ShortcutTarget(link) is { } target &&
                string.Equals(target, AppHost.ExePath, StringComparison.OrdinalIgnoreCase))
                File.Delete(link);
        }
    }

    /// <summary>
    /// A running program can't delete itself, so a hidden command waits a few seconds, deletes the files
    /// Room Switcher came with (nothing else in that folder), then the folder if that left it empty.
    /// </summary>
    private static void DeleteProgramAfterExit()
    {
        var folder = Path.GetDirectoryName(AppHost.ExePath);
        if (string.IsNullOrEmpty(folder)) return;
        var files = string.Join(" ", ShippedFiles.Select(f => $"\"{Path.Combine(folder, f)}\""));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 4 127.0.0.1 >nul & del /f /q {files} & rmdir \"{folder}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        });
    }
}
