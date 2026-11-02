using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Storage;

namespace RoomSwitcher.Core.Rooms;

/// <summary>What the Reset button can do, from least to most.</summary>
public enum ResetKind
{
    /// <summary>Rooms and what's assigned to them go; display, sound and device nicknames stay.</summary>
    Rooms,
    /// <summary>All settings and nicknames go, as if Room Switcher had never been set up.</summary>
    Everything,
    /// <summary>Everything goes, including the program itself.</summary>
    Uninstall,
}

public static class ResetRules
{
    /// <summary>
    /// A copy with no rooms and nothing switched by room, keeping everything learned about the
    /// hardware: display, speaker, microphone and device nicknames, chosen device types, and options.
    /// </summary>
    public static AppConfig WithoutRooms(AppConfig config)
    {
        var copy = JsonStore.Clone(config);
        copy.Rooms.Clear();
        foreach (var device in copy.UsbDevices) device.Managed = false;
        return copy;
    }

    /// <summary>A brand-new configuration, the same as on first run.</summary>
    public static AppConfig Fresh() => JsonStore.DeserializeConfig("{}");
}
