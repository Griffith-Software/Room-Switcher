using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoomSwitcher.Core.Model;

/// <summary>config.json (schema 1). Unknown fields at every level are kept and written back.</summary>
public sealed class AppConfig
{
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;
    public List<MonitorInfo> Monitors { get; set; } = new();
    /// <summary>Speakers and microphones the person has named.</summary>
    public List<AudioDeviceName> AudioDevices { get; set; } = new();
    /// <summary>USB devices the person has named or lets rooms turn on and off.</summary>
    public List<UsbDeviceInfo> UsbDevices { get; set; } = new();
    public List<Room> Rooms { get; set; } = new();
    public AppSettings Settings { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public MonitorInfo? FindMonitor(string id) => Monitors.FirstOrDefault(m => m.Id == id);

    public Room? FindRoom(string id) => Rooms.FirstOrDefault(r => r.Id == id);

    /// <summary>Case-insensitive name lookup, used by the command line.</summary>
    public Room? FindRoomByName(string name) =>
        Rooms.FirstOrDefault(r => string.Equals(r.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>A physical monitor output, identified by its stable Windows device path.</summary>
public sealed class MonitorInfo
{
    public string Id { get; set; } = Ids.New();
    public string DevicePath { get; set; } = "";
    /// <summary>The person's own name for it, e.g. "Office left".</summary>
    public string Name { get; set; } = "";
    /// <summary>What Windows calls the monitor (EDID name). Used as a fallback match and for display.</summary>
    public string? FriendlyName { get; set; }
    /// <summary>Resolutions and refresh rates seen while the monitor was on, so the arranger can offer them while it's off.</summary>
    public List<Display.DisplayMode>? KnownModes { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>A named group of screens, each routed to one or more monitors.</summary>
public sealed class Room
{
    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = "";
    public List<ScreenSpec> Screens { get; set; } = new();
    /// <summary>Keyboard shortcut that switches to this room, e.g. "Ctrl+Alt+1". "" = none; null = not set yet (gets a default).</summary>
    public string? Hotkey { get; set; }
    /// <summary>Sound device Windows plays through in this room. Null = leave the Windows default alone.</summary>
    public AudioDeviceRef? AudioOutput { get; set; }
    /// <summary>Microphone Windows records from in this room. Null = leave the Windows default alone.</summary>
    public AudioDeviceRef? AudioInput { get; set; }
    /// <summary>USB devices (by id) that are off in this room. Only devices Room Switcher manages are ever turned off.</summary>
    public List<string> UsbOff { get; set; } = new();
    /// <summary>
    /// Devices (by id) used in this room that rooms don't switch: shown and tested in Test devices.
    /// Devices rooms do switch belong to the rooms they're on in (see UsbOff) and aren't listed here.
    /// </summary>
    public List<string> Devices { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Monitors that are on in this room.</summary>
    [JsonIgnore]
    public IEnumerable<string> ActiveMonitorIds => Screens.SelectMany(s => s.MonitorIds);

    /// <summary>The screen this monitor is routed to, or null when the monitor is off in this room.</summary>
    public ScreenSpec? ScreenFor(string monitorId) => Screens.FirstOrDefault(s => s.MonitorIds.Contains(monitorId));

    /// <summary>True once every screen that shows something has a saved position and size.</summary>
    [JsonIgnore]
    public bool IsArranged => Screens.Where(s => s.MonitorIds.Count > 0).All(s => s.HasGeometry);
}

/// <summary>
/// One independent desktop area (a Windows "source"). Several monitorIds means the monitors
/// duplicate this screen. Width/height of 0 means "not arranged yet; let Windows choose".
/// </summary>
public sealed class ScreenSpec
{
    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Refresh rate in millihertz (60 Hz = 60000). 0 = let Windows choose.</summary>
    public int RefreshMillihz { get; set; }
    /// <summary>0, 90, 180 or 270 degrees.</summary>
    public int Rotation { get; set; }
    public bool Primary { get; set; }
    /// <summary>Display scaling in percent (100, 125, ...). Null = leave whatever Windows remembers.</summary>
    public int? ScalePercent { get; set; }
    public List<string> MonitorIds { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public bool HasGeometry => Width > 0 && Height > 0;

    public void ClearGeometry()
    {
        X = Y = Width = Height = RefreshMillihz = Rotation = 0;
        ScalePercent = null;
    }
}

/// <summary>A Windows sound device, remembered by its endpoint id with the name as a fallback.</summary>
/// <summary>The person's own name for a speaker or microphone.</summary>
public sealed class AudioDeviceName
{
    public string Id { get; set; } = "";
    /// <summary>What Windows calls it, kept so the entry still makes sense if the device is gone.</summary>
    public string WindowsName { get; set; } = "";
    public string Name { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>A physical USB (or Bluetooth) device: a keyboard, mouse, webcam and so on.</summary>
public sealed class UsbDeviceInfo
{
    /// <summary>The Windows device container id: one per physical device, stable across replugging.</summary>
    public string Id { get; set; } = "";
    public string WindowsName { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Keyboard, Mouse, Camera, Audio, Storage or Other (several joined with "+").</summary>
    public string Kind { get; set; } = "";
    /// <summary>True: rooms may turn this device off. False: Room Switcher never touches it.</summary>
    public bool Managed { get; set; }
    /// <summary>
    /// The type the person chose (e.g. "Mouse") when Windows reports it differently, such as a mouse
    /// receiver that also says it's a keyboard. Null = use what Windows reports.
    /// </summary>
    public string? UserKind { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? WindowsName : Name;
}

public sealed class AudioDeviceRef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }
    public bool RestoreWindows { get; set; } = true;
    /// <summary>Extra wait after Windows reports the display change, before windows are put back.</summary>
    public int SettleDelayMs { get; set; } = 1500;
    /// <summary>Shortcut that steps to the next room. "" = none; null = not set yet (gets the default).</summary>
    public string? NextRoomHotkey { get; set; }
    /// <summary>Restart Windows Audio shortly after sign-in (for PCs where apps are silent after a restart).</summary>
    public bool RestartAudioAtSignIn { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N")[..12];
}
