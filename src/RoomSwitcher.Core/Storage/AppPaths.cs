namespace RoomSwitcher.Core.Storage;

/// <summary>
/// Where config.json and layouts.json live. If config.json sits next to the exe the app runs in
/// portable mode and keeps everything there; otherwise it uses %APPDATA%\Room Switcher.
/// </summary>
public sealed class AppPaths
{
    public const string ConfigFileName = "config.json";
    public const string LayoutsFileName = "layouts.json";

    public string DataDirectory { get; }
    public bool Portable { get; }
    public string ConfigPath => Path.Combine(DataDirectory, ConfigFileName);
    public string LayoutsPath => Path.Combine(DataDirectory, LayoutsFileName);

    public AppPaths(string dataDirectory, bool portable)
    {
        DataDirectory = dataDirectory;
        Portable = portable;
    }

    public static AppPaths Resolve(string exeDirectory, string appDataDirectory)
    {
        if (File.Exists(Path.Combine(exeDirectory, ConfigFileName)))
            return new AppPaths(exeDirectory, portable: true);
        return new AppPaths(Path.Combine(appDataDirectory, "Room Switcher"), portable: false);
    }
}
