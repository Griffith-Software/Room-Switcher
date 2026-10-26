using System.Text.Json;
using System.Text.Json.Serialization;
using RoomSwitcher.Core.Model;

namespace RoomSwitcher.Core.Devices;

[Flags]
public enum UsbKind
{
    Other = 0,
    Keyboard = 1,
    Mouse = 2,
    Camera = 4,
    Audio = 8,
    Storage = 16,
    Controller = 32,
    Pen = 64,
    Printer = 128,
    Phone = 256,
}

/// <summary>
/// One physical device (a Windows device container): a keyboard, a mouse, a receiver that is both,
/// a webcam, a game controller, a Bluetooth device and so on. Enabled = its parts are switched on in
/// Windows. Connected = false for a paired Bluetooth device that isn't connected right now.
/// </summary>
public sealed record UsbDevice(string Id, string Name, UsbKind Kinds, string Connection, bool Enabled, bool Connected = true);

public enum DeviceControlStatus
{
    /// <summary>The one-time administrator setup hasn't been done.</summary>
    NotSetUp,
    Ready,
    /// <summary>Set up, but the helper in Program Files is an older version than this app.</summary>
    NeedsUpdate,
}

/// <summary>USB devices, seen from the core. Implemented in RoomSwitcher.Windows / the app.</summary>
public interface IDeviceBackend
{
    IReadOnlyList<UsbDevice> List();
    DeviceControlStatus Status { get; }

    /// <summary>Turns devices (by id) on and off through the administrator helper. Returns plain-language problems.</summary>
    Task<IReadOnlyList<string>> ApplyAsync(IReadOnlyList<string> enable, IReadOnlyList<string> disable, CancellationToken cancellationToken = default);
}

public sealed record UsbPlan(IReadOnlyList<string> Enable, IReadOnlyList<string> Disable, IReadOnlyList<string> Warnings)
{
    public bool Empty => Enable.Count == 0 && Disable.Count == 0;
}

public static class UsbRules
{
    public static string KindText(UsbKind kinds)
    {
        if (kinds == UsbKind.Other) return "Other";
        var parts = new List<string>();
        if (kinds.HasFlag(UsbKind.Keyboard)) parts.Add("Keyboard");
        if (kinds.HasFlag(UsbKind.Mouse)) parts.Add("Mouse");
        if (kinds.HasFlag(UsbKind.Camera)) parts.Add("Camera");
        if (kinds.HasFlag(UsbKind.Audio)) parts.Add("Audio");
        if (kinds.HasFlag(UsbKind.Storage)) parts.Add("Storage");
        if (kinds.HasFlag(UsbKind.Controller)) parts.Add("Controller");
        if (kinds.HasFlag(UsbKind.Pen)) parts.Add("Pen");
        if (kinds.HasFlag(UsbKind.Printer)) parts.Add("Printer");
        if (kinds.HasFlag(UsbKind.Phone)) parts.Add("Phone");
        return string.Join(" + ", parts);
    }

    /// <summary>The types a person can choose for a device, in menu order.</summary>
    public static readonly UsbKind[] Choices =
    [
        UsbKind.Keyboard, UsbKind.Mouse, UsbKind.Keyboard | UsbKind.Mouse, UsbKind.Camera, UsbKind.Controller,
        UsbKind.Pen, UsbKind.Printer, UsbKind.Phone, UsbKind.Storage, UsbKind.Other,
    ];

    /// <summary>Reads text written by KindText ("Keyboard + Mouse") back into flags. Unknown words are ignored.</summary>
    public static UsbKind ParseKind(string? text)
    {
        var kinds = UsbKind.Other;
        foreach (var part in (text ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            if (Enum.TryParse<UsbKind>(part, ignoreCase: true, out var kind)) kinds |= kind;
        return kinds;
    }

    /// <summary>What the device is, as far as the person is concerned: their chosen type, else what Windows reports.</summary>
    public static UsbKind Kinds(AppConfig config, UsbDevice device) =>
        Find(config, device.Id)?.UserKind is { Length: > 0 } chosen ? ParseKind(chosen) : device.Kinds;

    /// <summary>
    /// The devices a room uses: the switched devices that are on in it, then the others ticked for it.
    /// </summary>
    public static List<string> DevicesIn(AppConfig config, Room room)
    {
        var ids = config.UsbDevices
            .Where(d => d.Managed && !room.UsbOff.Contains(d.Id, StringComparer.OrdinalIgnoreCase))
            .Select(d => d.Id).ToList();
        foreach (var id in room.Devices)
        {
            if (Find(config, id) is { Managed: true }) continue; // switched devices go by UsbOff
            if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase)) ids.Add(id);
        }
        return ids;
    }

    /// <summary>Ticks or unticks a device for a room: on/off for a switched device, membership for any other.</summary>
    public static void SetInRoom(AppConfig config, Room room, string id, bool inRoom)
    {
        if (Find(config, id) is { Managed: true })
        {
            room.UsbOff.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            if (!inRoom) room.UsbOff.Add(id);
            return;
        }
        room.Devices.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        if (inRoom) room.Devices.Add(id);
    }

    /// <summary>
    /// Turns room switching on or off for a device, keeping which rooms it belongs to: a device that
    /// starts being switched is on in the rooms it was ticked for (on everywhere if it had none); one that
    /// stops being switched stays ticked for the rooms it was on in.
    /// </summary>
    public static void SetManaged(AppConfig config, UsbDeviceInfo info, bool managed)
    {
        if (info.Managed == managed) return;
        var inRooms = config.Rooms.Where(r => DevicesIn(config, r).Contains(info.Id, StringComparer.OrdinalIgnoreCase)).ToList();
        var anyTicked = inRooms.Count > 0;
        info.Managed = managed;
        foreach (var room in config.Rooms)
        {
            room.UsbOff.RemoveAll(x => string.Equals(x, info.Id, StringComparison.OrdinalIgnoreCase));
            room.Devices.RemoveAll(x => string.Equals(x, info.Id, StringComparison.OrdinalIgnoreCase));
            var wasIn = inRooms.Contains(room);
            if (managed && anyTicked && !wasIn) room.UsbOff.Add(info.Id);
            if (!managed && wasIn) room.Devices.Add(info.Id);
        }
    }

    public static UsbDeviceInfo? Find(AppConfig config, string id) =>
        config.UsbDevices.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The settings entry for a device, created (unmanaged, unnamed) if it has none yet.</summary>
    public static UsbDeviceInfo Entry(AppConfig config, UsbDevice device)
    {
        var info = Find(config, device.Id);
        if (info == null)
        {
            info = new UsbDeviceInfo { Id = device.Id };
            config.UsbDevices.Add(info);
        }
        info.WindowsName = device.Name;
        info.Kind = KindText(device.Kinds);
        return info;
    }

    public static string DisplayName(AppConfig config, UsbDevice device) => Find(config, device.Id)?.DisplayName ?? device.Name;

    /// <summary>
    /// What switching to the room changes. Only devices Room Switcher manages are touched; a device
    /// that's off in the room is turned off, every other managed device is turned on. As a safety
    /// net the last working keyboard and the last working mouse are never turned off.
    /// </summary>
    public static UsbPlan Plan(AppConfig config, Room room, IReadOnlyList<UsbDevice> present)
    {
        var enable = new List<UsbDevice>();
        var disable = new List<UsbDevice>();
        foreach (var info in config.UsbDevices.Where(d => d.Managed))
        {
            var device = present.FirstOrDefault(d => string.Equals(d.Id, info.Id, StringComparison.OrdinalIgnoreCase));
            if (device == null || !device.Connected) continue; // not plugged in, or a Bluetooth device that's away
            var wantOn = !room.UsbOff.Contains(info.Id, StringComparer.OrdinalIgnoreCase);
            if (wantOn && !device.Enabled) enable.Add(device);
            if (!wantOn && device.Enabled) disable.Add(device);
        }

        var warnings = new List<string>();
        foreach (var (kind, label) in new[] { (UsbKind.Keyboard, "keyboard"), (UsbKind.Mouse, "mouse") })
        {
            bool OnAfter(UsbDevice d) => d.Connected && (enable.Contains(d) || (d.Enabled && !disable.Contains(d)));
            if (!disable.Any(d => d.Kinds.HasFlag(kind))) continue;
            if (present.Any(d => d.Kinds.HasFlag(kind) && OnAfter(d))) continue;
            foreach (var kept in disable.Where(d => d.Kinds.HasFlag(kind)).ToList())
            {
                disable.Remove(kept);
                warnings.Add($"Kept {DisplayName(config, kept)} on because it's the only working {label}.");
            }
        }

        return new UsbPlan(enable.Select(d => d.Id).ToList(), disable.Select(d => d.Id).ToList(), warnings);
    }
}

/// <summary>What the app asks the administrator helper to do. Written to a file the helper reads.</summary>
public sealed class DeviceRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public List<string> Enable { get; set; } = new();
    public List<string> Disable { get; set; } = new();
    /// <summary>Restart the Windows Audio service (fixes apps going silent on some PCs).</summary>
    public bool RestartAudio { get; set; }
}

/// <summary>What the helper did.</summary>
public sealed class DeviceResponse
{
    public string RequestId { get; set; } = "";
    public List<string> Problems { get; set; } = new();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(DeviceRequest))]
[JsonSerializable(typeof(DeviceResponse))]
internal sealed partial class DeviceJsonContext : JsonSerializerContext;

public static class DeviceMessages
{
    public static string Write(DeviceRequest request) => JsonSerializer.Serialize(request, DeviceJsonContext.Default.DeviceRequest);
    public static string Write(DeviceResponse response) => JsonSerializer.Serialize(response, DeviceJsonContext.Default.DeviceResponse);
    public static DeviceRequest? ReadRequest(string json) => JsonSerializer.Deserialize(json, DeviceJsonContext.Default.DeviceRequest);
    public static DeviceResponse? ReadResponse(string json) => JsonSerializer.Deserialize(json, DeviceJsonContext.Default.DeviceResponse);
}
