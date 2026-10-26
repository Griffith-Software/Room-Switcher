using RoomSwitcher.Core.Display;
using RoomSwitcher.Core.Model;

namespace RoomSwitcher.Core.Rooms;

/// <summary>Works out which room, if any, the displays are showing right now.</summary>
public static class RoomMatcher
{
    /// <summary>
    /// A room matches when the same monitors are on and grouped into the same screens.
    /// If several rooms share that routing, the room last switched to wins if it fits what is on
    /// screen, then a room whose saved positions match exactly, then the first in the list.
    /// Returns null when nothing matches (for example after Win+P).
    /// </summary>
    public static Room? DetectCurrent(AppConfig config, DisplayState state, string? lastRoomId = null)
    {
        if (state.Screens.Count == 0) return null;
        var resolution = MonitorResolver.Resolve(config, state);

        var active = new List<HashSet<string>>();
        foreach (var screen in state.Screens)
        {
            var group = new HashSet<string>();
            foreach (var path in screen.DevicePaths)
            {
                var id = resolution.MonitorIdFor(path);
                if (id == null) return null; // a monitor no room knows about is on
                group.Add(id);
            }
            active.Add(group);
        }

        var candidates = config.Rooms.Where(r => SameRouting(Routing(r), active)).ToList();
        if (candidates.Count <= 1) return candidates.FirstOrDefault();

        // Rooms that fit what is on screen: saved positions match, or nothing saved to contradict it.
        var exact = candidates.Where(r => GeometryMatches(r, state, resolution)).ToList();
        var consistent = candidates.Where(r => exact.Contains(r) || !r.IsArranged).ToList();
        var pool = consistent.Count > 0 ? consistent : candidates;
        return pool.FirstOrDefault(r => r.Id == lastRoomId)
               ?? pool.FirstOrDefault(exact.Contains)
               ?? pool[0];
    }

    private static List<HashSet<string>> Routing(Room room) =>
        room.Screens.Where(s => s.MonitorIds.Count > 0).Select(s => s.MonitorIds.ToHashSet()).ToList();

    internal static bool SameRouting(List<HashSet<string>> a, List<HashSet<string>> b)
    {
        if (a.Count != b.Count) return false;
        var remaining = new List<HashSet<string>>(b);
        foreach (var group in a)
        {
            var index = remaining.FindIndex(g => g.SetEquals(group));
            if (index < 0) return false;
            remaining.RemoveAt(index);
        }
        return true;
    }

    private static bool GeometryMatches(Room room, DisplayState state, MonitorResolver.Resolution resolution)
    {
        var screens = room.Screens.Where(s => s.MonitorIds.Count > 0).ToList();
        if (!screens.All(s => s.HasGeometry)) return false;
        var primary = screens.FirstOrDefault(s => s.Primary) ?? screens[0];
        foreach (var screen in screens)
        {
            var path = resolution.Find(screen.MonitorIds[0])?.DevicePath;
            var live = state.Screens.FirstOrDefault(s => s.DevicePaths.Contains(path!, StringComparer.OrdinalIgnoreCase));
            if (live == null) return false;
            if (live.X != screen.X - primary.X || live.Y != screen.Y - primary.Y) return false;
            if (live.Width != screen.Width || live.Height != screen.Height) return false;
        }
        return true;
    }
}
