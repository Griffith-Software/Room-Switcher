namespace RoomSwitcher.Core.Display;

/// <summary>A monitor Windows reports as connected, whether or not it is showing anything.</summary>
public sealed record ConnectedMonitor(
    string DevicePath,
    string FriendlyName,
    string Connector,
    string AdapterKey,
    bool Active);

/// <summary>A graphics adapter and how many independent screens (sources) it can drive at once.</summary>
public sealed record AdapterInfo(string Key, int SourceCount);

/// <summary>One screen Windows is currently drawing, and the monitors showing it.</summary>
public sealed record ActiveScreen(
    int X, int Y, int Width, int Height,
    int RefreshMillihz,
    int Rotation,
    int? ScalePercent,
    IReadOnlyList<string> DevicePaths)
{
    public bool Primary => X == 0 && Y == 0;
}

/// <summary>What the display hardware looks like right now.</summary>
public sealed record DisplayState(
    IReadOnlyList<ConnectedMonitor> Monitors,
    IReadOnlyList<AdapterInfo> Adapters,
    IReadOnlyList<ActiveScreen> Screens)
{
    public static readonly DisplayState Empty = new([], [], []);

    public ConnectedMonitor? FindMonitor(string devicePath) =>
        Monitors.FirstOrDefault(m => string.Equals(m.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));

    public AdapterInfo? FindAdapter(string key) => Adapters.FirstOrDefault(a => a.Key == key);
}

/// <summary>A room resolved to device paths, ready for the display backend.</summary>
public sealed record RoomPlan(string RoomId, string RoomName, IReadOnlyList<PlannedScreen> Screens)
{
    /// <summary>True when every screen has a position and size; otherwise Windows chooses them.</summary>
    public bool Arranged => Screens.All(s => s.Width > 0 && s.Height > 0);

    public IEnumerable<string> DevicePaths => Screens.SelectMany(s => s.DevicePaths);
}

public sealed record PlannedScreen(
    int X, int Y, int Width, int Height,
    int RefreshMillihz,
    int Rotation,
    int? ScalePercent,
    IReadOnlyList<string> DevicePaths);

/// <summary>Outcome of asking the display backend to check or apply something.</summary>
public sealed record DisplayResult(bool Ok, string Message)
{
    public static readonly DisplayResult Success = new(true, "");
    public static DisplayResult Fail(string message) => new(false, message);
}

/// <summary>Opaque saved display configuration, used to roll back a failed switch.</summary>
public interface IDisplayRestorePoint;

/// <summary>The Windows display API, seen from the core. Implemented in RoomSwitcher.Windows.</summary>
public interface IDisplayBackend
{
    DisplayState Query();
    /// <summary>Asks Windows and the driver whether the plan can work, without changing anything on screen.</summary>
    DisplayResult Validate(RoomPlan plan);
    DisplayResult Apply(RoomPlan plan);
    IDisplayRestorePoint CaptureRestorePoint();
    DisplayResult Restore(IDisplayRestorePoint point);
    /// <summary>Recovery: every connected monitor on, extended.</summary>
    DisplayResult ShowAll();
}

/// <summary>Waits for Windows to finish a display change.</summary>
public interface IDisplaySettle
{
    Task WaitAsync(int settleDelayMs, CancellationToken cancellationToken = default);
}
