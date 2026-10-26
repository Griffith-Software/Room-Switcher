using RoomSwitcher.Core.Display;
using RoomSwitcher.Core.Model;

namespace RoomSwitcher.Core.Rooms;

/// <summary>Edits rooms the way the routing grid needs: route a monitor to a screen or off, add, copy, move, delete.</summary>
public static class RoomEditor
{
    public static Room AddRoom(AppConfig config, string? name = null)
    {
        var room = new Room { Name = UniqueName(config, name ?? "New room") };
        room.Screens.Add(new ScreenSpec());
        config.Rooms.Add(room);
        Hotkeys.HotkeyRules.FillDefaults(config);
        return room;
    }

    public static Room Duplicate(AppConfig config, Room source)
    {
        var copy = new Room
        {
            Name = UniqueName(config, $"{source.Name} copy"),
            Extra = source.Extra == null ? null : new(source.Extra),
            AudioOutput = source.AudioOutput == null ? null : new() { Id = source.AudioOutput.Id, Name = source.AudioOutput.Name },
            AudioInput = source.AudioInput == null ? null : new() { Id = source.AudioInput.Id, Name = source.AudioInput.Name },
            UsbOff = new(source.UsbOff),
            Devices = new(source.Devices),
            Screens = source.Screens.Select(s => new ScreenSpec
            {
                Name = s.Name, X = s.X, Y = s.Y, Width = s.Width, Height = s.Height,
                RefreshMillihz = s.RefreshMillihz, Rotation = s.Rotation, Primary = s.Primary,
                ScalePercent = s.ScalePercent, MonitorIds = new(s.MonitorIds),
                Extra = s.Extra == null ? null : new(s.Extra),
            }).ToList(),
        };
        config.Rooms.Insert(config.Rooms.IndexOf(source) + 1, copy);
        // The copy can't share the original's shortcut; it gets the next free default.
        Hotkeys.HotkeyRules.FillDefaults(config);
        return copy;
    }

    public static void Rename(AppConfig config, Room room, string name) => room.Name = name.Trim();

    public static void Delete(AppConfig config, Room room) => config.Rooms.Remove(room);

    /// <summary>Moves a room up (-1) or down (+1) in the list. Returns false at the ends.</summary>
    public static bool Move(AppConfig config, Room room, int delta)
    {
        var index = config.Rooms.IndexOf(room);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= config.Rooms.Count) return false;
        config.Rooms.RemoveAt(index);
        config.Rooms.Insert(target, room);
        return true;
    }

    public static ScreenSpec AddScreen(Room room)
    {
        var screen = new ScreenSpec();
        room.Screens.Add(screen);
        return screen;
    }

    /// <summary>
    /// Routes a monitor to a screen (by column index) or turns it off (null). A monitor sits in one
    /// column per room, so it is taken out of any other screen first. Changing the routing clears
    /// the room's saved arrangement, because positions and modes no longer fit.
    /// Returns true if anything changed.
    /// </summary>
    public static bool Route(Room room, string monitorId, int? screenIndex)
    {
        var index = room.Screens.FindIndex(s => s.MonitorIds.Contains(monitorId));
        int? current = index < 0 ? null : index;
        if (current == screenIndex) return false;
        if (screenIndex is int i && (i < 0 || i >= room.Screens.Count))
            throw new ArgumentOutOfRangeException(nameof(screenIndex));

        foreach (var s in room.Screens) s.MonitorIds.Remove(monitorId);
        if (screenIndex is int target) room.Screens[target].MonitorIds.Add(monitorId);
        foreach (var s in room.Screens) s.ClearGeometry();
        foreach (var s in room.Screens) s.Primary = false;
        return true;
    }

    /// <summary>Drops screens that no monitor shows, so columns stay numbered 1, 2, 3...</summary>
    public static bool RemoveEmptyScreens(Room room) => room.Screens.RemoveAll(s => s.MonitorIds.Count == 0) > 0;

    /// <summary>Drops empty screen columns at the end (keeps at least one column).</summary>
    public static void TrimEmptyScreens(Room room)
    {
        while (room.Screens.Count > 1 && room.Screens[^1].MonitorIds.Count == 0)
            room.Screens.RemoveAt(room.Screens.Count - 1);
    }

    /// <summary>Removes a monitor from the list and from every room.</summary>
    public static void ForgetMonitor(AppConfig config, MonitorInfo monitor)
    {
        foreach (var room in config.Rooms)
            Route(room, monitor.Id, null);
        config.Monitors.Remove(monitor);
    }

    public static string UniqueName(AppConfig config, string baseName, Room? except = null)
    {
        baseName = string.IsNullOrWhiteSpace(baseName) ? "Room" : baseName.Trim();
        var name = baseName;
        for (var i = 2; config.Rooms.Any(r => r != except && string.Equals(r.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)); i++)
            name = $"{baseName} {i}";
        return name;
    }

    /// <summary>
    /// Copies the arrangement Windows is showing into the room. If the person changed which monitors
    /// are on or mirrored while in Windows settings, the routing doesn't match; with
    /// <paramref name="acceptWindowsRouting"/> the room takes Windows' routing as well.
    /// </summary>
    public static CaptureOutcome CaptureArrangement(AppConfig config, Room room, DisplayState state, bool acceptWindowsRouting)
    {
        var resolution = MonitorResolver.Resolve(config, state);
        var live = new List<(ActiveScreen Screen, List<string> MonitorIds)>();
        foreach (var screen in state.Screens)
        {
            var ids = new List<string>();
            foreach (var path in screen.DevicePaths)
            {
                var id = resolution.MonitorIdFor(path);
                if (id == null) return CaptureOutcome.UnknownMonitor;
                ids.Add(id);
            }
            live.Add((screen, ids));
        }
        if (live.Count == 0) return CaptureOutcome.NothingActive;

        var roomScreens = room.Screens.Where(s => s.MonitorIds.Count > 0).ToList();
        var sameRouting = RoomMatcher.SameRouting(
            roomScreens.Select(s => s.MonitorIds.ToHashSet()).ToList(),
            live.Select(l => l.MonitorIds.ToHashSet()).ToList());

        if (!sameRouting && !acceptWindowsRouting) return CaptureOutcome.RoutingDiffers;

        if (!sameRouting)
        {
            room.Screens = live.Select(l => new ScreenSpec { MonitorIds = new(l.MonitorIds) }).ToList();
            roomScreens = room.Screens.ToList();
        }

        foreach (var spec in room.Screens)
        {
            if (spec.MonitorIds.Count == 0) { spec.ClearGeometry(); spec.Primary = false; continue; }
            var match = live.First(l => l.MonitorIds.ToHashSet().SetEquals(spec.MonitorIds)).Screen;
            spec.X = match.X;
            spec.Y = match.Y;
            spec.Width = match.Width;
            spec.Height = match.Height;
            spec.RefreshMillihz = match.RefreshMillihz;
            spec.Rotation = match.Rotation;
            spec.ScalePercent = match.ScalePercent;
            spec.Primary = match.Primary;
        }
        return sameRouting ? CaptureOutcome.Saved : CaptureOutcome.SavedWithWindowsRouting;
    }
}

public enum CaptureOutcome
{
    Saved,
    SavedWithWindowsRouting,
    RoutingDiffers,
    UnknownMonitor,
    NothingActive,
}
