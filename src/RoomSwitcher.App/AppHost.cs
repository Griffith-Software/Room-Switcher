using RoomSwitcher.Core.Display;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Rooms;
using RoomSwitcher.Core.Storage;
using RoomSwitcher.Windows.Audio;
using RoomSwitcher.Windows.Display;
using RoomSwitcher.Windows.Layouts;

namespace RoomSwitcher.App;

/// <summary>Everything the tray, the wizard and the command line share: files, backends and the switching service.</summary>
internal sealed class AppHost
{
    public AppPaths Paths { get; private set; }
    public WindowsDisplayBackend Display { get; } = new();
    public WindowsWindowBackend Windows { get; } = new();
    public WindowsAudioBackend Audio { get; } = new();
    public DeviceHelper Devices { get; } = new();
    public RoomSwitchService Service { get; }
    public DisplayChangeListener? Listener { get; }

    public AppConfig Config => Service.Config;

    /// <summary>Set by the tray: release the shortcuts while one is being typed into a shortcut box.</summary>
    public Action? PauseShortcuts { get; set; }

    /// <summary>Set by the tray: take the shortcuts back (from the saved settings).</summary>
    public Action? ResumeShortcuts { get; set; }

    /// <summary>Set by the tray: true if Room Switcher itself currently holds this shortcut.</summary>
    public Func<Core.Hotkeys.Hotkey, bool>? HoldsShortcut { get; set; }

    public static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    private AppHost(AppPaths paths, AppConfig config, LayoutsFile layouts, IDisplaySettle settle, DisplayChangeListener? listener)
    {
        Paths = paths;
        Listener = listener;
        Service = new RoomSwitchService(config, layouts, Display, Windows, settle,
            l => JsonStore.SaveLayouts(paths.LayoutsPath, l), Audio, devices: Devices);
    }

    /// <summary>
    /// Loads config and layouts. In the tray a hidden window listens for display changes; one-shot commands just wait.
    /// At sign-in Windows may still be loading the profile, so finding no rooms is retried for up to a minute
    /// before it's believed (otherwise setup would open empty and could save over the real rooms).
    /// </summary>
    public static AppHost Load(bool listen, bool atSignIn = false)
    {
        var started = DateTime.Now;
        var (paths, config) = LoadConfig();
        var attempts = 1;
        while (atSignIn && config is not { Rooms.Count: > 0 } && DateTime.Now - started < TimeSpan.FromMinutes(1))
        {
            Thread.Sleep(2000);
            (paths, config) = LoadConfig(rethrow: false);
            attempts++;
        }
        if (config == null) (paths, config) = LoadConfig(); // still failing: show why
        Log($"{(atSignIn ? "sign-in" : "start")}: {paths.ConfigPath} exists={File.Exists(paths.ConfigPath)} rooms={config!.Rooms.Count} attempts={attempts}");
        if (config.Rooms.Count == 0) Log(Diagnose(paths));
        var layouts = JsonStore.LoadLayouts(paths.LayoutsPath);

        var listener = listen ? new DisplayChangeListener() : null;
        IDisplaySettle settle = listener ?? (IDisplaySettle)new DelaySettle();
        var context = new AppHost(paths, config, layouts, settle, listener);
        context.SyncMonitors();
        return context;
    }

    /// <summary>
    /// If no rooms were found at start, looks again (the tray calls this until they turn up).
    /// Returns true when it found them and switched over to them.
    /// </summary>
    public bool ReloadIfEmpty()
    {
        if (Config.Rooms.Count > 0) return false;
        var (paths, config) = LoadConfig(rethrow: false);
        if (config is not { Rooms.Count: > 0 }) return false;
        Paths = paths;
        Service.Config = config;
        Service.Layouts = JsonStore.LoadLayouts(paths.LayoutsPath);
        Log($"found later: {paths.ConfigPath} rooms={config.Rooms.Count}");
        SyncMonitors();
        return true;
    }

    /// <summary>What Windows answered, for startup.log when the rooms couldn't be found.</summary>
    private static string Diagnose(AppPaths paths)
    {
        static string Try(Func<object?> f)
        {
            try { return f()?.ToString() ?? "(null)"; }
            catch (Exception e) { return $"{e.GetType().Name}: {e.Message}"; }
        }
        return string.Join(" | ",
            $"AppData known folder='{Try(() => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))}'",
            $"LocalAppData known folder='{Try(() => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))}'",
            $"APPDATA='{Environment.GetEnvironmentVariable("APPDATA")}'",
            $"folder exists={Try(() => Directory.Exists(paths.DataDirectory))}",
            $"parent lists={Try(() => Directory.GetDirectories(Path.GetDirectoryName(paths.DataDirectory)!).Length)}",
            $"read={Try(() => File.ReadAllText(paths.ConfigPath).Length)}");
    }

    private static (AppPaths Paths, AppConfig? Config) LoadConfig(bool rethrow = true)
    {
        var paths = AppPaths.Resolve(
            Path.GetDirectoryName(ExePath) ?? AppDomain.CurrentDomain.BaseDirectory,
            AppDataDirectory());
        try
        {
            return (paths, JsonStore.LoadConfig(paths.ConfigPath));
        }
        catch (Exception e) when (!rethrow)
        {
            Log($"couldn't read {paths.ConfigPath}: {e.Message}");
            return (paths, null);
        }
    }

    /// <summary>%APPDATA%, always as a full path (a blank answer would otherwise mean the current folder).</summary>
    private static string AppDataDirectory()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!Path.IsPathFullyQualified(dir)) dir = Environment.GetEnvironmentVariable("APPDATA") ?? "";
        if (!Path.IsPathFullyQualified(dir))
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming");
        if (!Path.IsPathFullyQualified(dir))
            throw new IOException("Windows didn't say where your AppData folder is. Try again in a minute.");
        return dir;
    }

    /// <summary>A short log of what each start found, in %LOCALAPPDATA%\Room Switcher\startup.log.</summary>
    internal static void Log(string line)
    {
        // Paths are written relative to the user's folder, so the log never contains the Windows user name.
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3) line = line.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!Path.IsPathFullyQualified(local)) local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
            if (!Path.IsPathFullyQualified(local)) throw new IOException("no LocalAppData folder");
            var dir = Path.Combine(local, "Room Switcher");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "startup.log");
            var lines = File.Exists(path) ? File.ReadAllLines(path).TakeLast(99).ToList() : new List<string>();
            lines.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}");
            File.WriteAllLines(path, lines);
        }
        catch (Exception e)
        {
            // Logging must never stop Room Switcher. Try once more next to the exe, saying why.
            try
            {
                File.AppendAllText(Path.Combine(Path.GetDirectoryName(ExePath)!, "startup.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line} (log: {e.Message}){Environment.NewLine}");
            }
            catch (Exception) { }
        }
    }

    /// <summary>Picks up newly connected monitors and moved device paths. Saves if anything changed.</summary>
    public bool SyncMonitors()
    {
        try
        {
            var state = Display.Query();
            var changed = MonitorResolver.SyncMonitors(Config, state);
            changed |= RememberModes(Config, state);
            if (!changed) return false;
            // Only write a file if the person has set something up; first run writes on Finish.
            if (Config.Rooms.Count > 0) SaveConfig();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Notes the resolutions each monitor that's on supports, so the arranger can offer them later
    /// while it's off (Windows only lists modes for monitors that are on). Returns true if anything changed.
    /// </summary>
    public bool RememberModes(AppConfig config, Core.Display.DisplayState state)
    {
        var changed = false;
        var resolution = MonitorResolver.Resolve(config, state);
        foreach (var monitor in config.Monitors)
        {
            if (resolution.Find(monitor.Id) is not { Active: true } connected) continue;
            IReadOnlyList<Core.Display.DisplayMode> modes;
            try { modes = Display.Modes(connected.DevicePath); }
            catch (Exception) { continue; }
            if (modes.Count < 2) continue; // only the preferred mode: not a real list
            if (monitor.KnownModes != null && monitor.KnownModes.SequenceEqual(modes)) continue;
            monitor.KnownModes = modes.ToList();
            changed = true;
        }
        return changed;
    }

    public void SaveConfig() => JsonStore.SaveConfig(Paths.ConfigPath, Config);

    public void ReplaceConfig(AppConfig config)
    {
        Service.Config = config;
        SaveConfig();
    }

    /// <summary>Used without a message loop: wait for the driver, then the configured delay.</summary>
    private sealed class DelaySettle : IDisplaySettle
    {
        public Task WaitAsync(int settleDelayMs, CancellationToken cancellationToken = default) =>
            Task.Delay(500 + Math.Max(0, settleDelayMs), cancellationToken);
    }
}
