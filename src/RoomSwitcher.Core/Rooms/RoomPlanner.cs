using RoomSwitcher.Core.Display;
using RoomSwitcher.Core.Model;

namespace RoomSwitcher.Core.Rooms;

public enum ProblemSeverity { Warning, Error }

/// <summary>
/// A plain-language reason a room can't be used (Error) or may not come out as expected (Warning).
/// A warning with a MonitorId means that display stays off; the rest of the room still switches.
/// </summary>
public sealed record RoomProblem(ProblemSeverity Severity, string Message, string? MonitorId = null, string? ScreenId = null);

public sealed record PlanResult(RoomPlan? Plan, IReadOnlyList<RoomProblem> Problems)
{
    public bool Ok => Plan != null && Problems.All(p => p.Severity != ProblemSeverity.Error);
    public string? FirstError => Problems.FirstOrDefault(p => p.Severity == ProblemSeverity.Error)?.Message;
}

/// <summary>
/// Checks a room against the config and the connected hardware and turns it into a plan for the
/// display backend. These are the checks that can be answered without asking the driver; the
/// backend's Validate does the rest.
/// </summary>
public static class RoomPlanner
{
    public static PlanResult Build(AppConfig config, Room room, DisplayState state)
    {
        var problems = new List<RoomProblem>();
        var resolution = MonitorResolver.Resolve(config, state);

        if (string.IsNullOrWhiteSpace(room.Name))
            problems.Add(Error("Give this room a name."));
        else if (config.Rooms.Any(r => r.Id != room.Id && string.Equals(r.Name.Trim(), room.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            problems.Add(Error($"Another room is already called \"{room.Name.Trim()}\"."));

        var screens = room.Screens.Where(s => s.MonitorIds.Count > 0).ToList();
        if (screens.Count == 0)
        {
            problems.Add(Error("Turn on at least one display in this room."));
            return new PlanResult(null, problems);
        }

        var seen = new HashSet<string>();
        foreach (var id in screens.SelectMany(s => s.MonitorIds))
        {
            if (!seen.Add(id))
                problems.Add(Error($"{MonitorName(config, id)} is set to more than one screen. A display can show only one screen at a time.", id));
        }

        // A display that can't be used is left off with a warning, so the room still works on the rest.
        var planned = new List<PlannedScreen>();
        var kept = new List<ScreenSpec>();
        foreach (var screen in screens)
        {
            var found = new List<(string Id, ConnectedMonitor Monitor)>();
            foreach (var id in screen.MonitorIds.Distinct())
            {
                var info = config.FindMonitor(id);
                if (info == null)
                {
                    problems.Add(Warning("This room uses a display that's no longer in the list of displays, so it stays off.", id, screen.Id));
                    continue;
                }
                var connected = resolution.Find(id);
                if (connected == null)
                {
                    problems.Add(Warning($"Windows can't detect {info.Name} right now, so it stays off. Check that it's plugged in and powered on.", id, screen.Id));
                    continue;
                }
                found.Add((id, connected));
            }
            if (found.Count == 0) continue;

            // Duplicated displays must share a graphics card: keep the ones on the first display's card.
            var others = found.Where(f => f.Monitor.AdapterKey != found[0].Monitor.AdapterKey).ToList();
            if (others.Count > 0)
            {
                var names = found.Select(f => MonitorName(config, f.Id)).ToList();
                var off = others.Select(f => MonitorName(config, f.Id)).ToList();
                problems.Add(Warning(
                    $"{JoinNames(names)} are on different graphics cards, so they can't be duplicated. {JoinNames(off)} {(off.Count == 1 ? "stays" : "stay")} off.",
                    others[0].Id, screen.Id));
                found = found.Except(others).ToList();
            }

            kept.Add(screen);
            planned.Add(new PlannedScreen(screen.X, screen.Y, screen.Width, screen.Height,
                screen.RefreshMillihz, screen.Rotation, screen.ScalePercent, found.Select(f => f.Monitor.DevicePath).ToList()));
        }

        // Each screen needs its own source on its graphics card. Screens past what the card can show stay off, the main one last.
        var byAdapter = kept
            .Select((s, i) => (Screen: s, Adapter: resolution.Find(s.MonitorIds.First(id => resolution.Find(id) != null))!.AdapterKey))
            .GroupBy(x => x.Adapter)
            .ToList();
        foreach (var group in byAdapter)
        {
            var adapter = state.FindAdapter(group.Key);
            if (adapter == null || adapter.SourceCount <= 0 || group.Count() <= adapter.SourceCount) continue;
            var dropped = group.Select(x => x.Screen).OrderByDescending(s => s.Primary).Skip(adapter.SourceCount).ToList();
            var off = dropped.SelectMany(s => s.MonitorIds.Where(id => resolution.Find(id) != null)).Distinct().ToList();
            problems.Add(Warning(
                $"This graphics card can only show {adapter.SourceCount} separate screens at once, and this room needs {group.Count()}, so {JoinNames(off.Select(id => MonitorName(config, id)).ToList())} {(off.Count == 1 ? "stays" : "stay")} off.",
                off[0], dropped[0].Id));
            foreach (var screen in dropped)
            {
                planned.RemoveAt(kept.IndexOf(screen));
                kept.Remove(screen);
            }
        }

        if (kept.Count == 0)
        {
            problems.Add(Error("None of this room's displays can be turned on right now."));
            return new PlanResult(null, problems);
        }
        screens = kept;

        var arrangedScreens = screens.Where(s => s.HasGeometry).ToList();
        if (arrangedScreens.Count == screens.Count)
        {
            for (var i = 0; i < screens.Count; i++)
            for (var j = i + 1; j < screens.Count; j++)
            {
                if (Overlaps(screens[i], screens[j]))
                    problems.Add(Error($"{ScreenLabel(room, screens[i])} and {ScreenLabel(room, screens[j])} overlap. Arrange this room again on the Arrange step."));
            }
            if (screens.Count(s => s.Primary) > 1)
                problems.Add(Warning("More than one screen is set as the main display. The first one will be used."));
            planned = NormalizeToPrimary(screens, planned);
        }
        else if (arrangedScreens.Count > 0)
        {
            problems.Add(Warning("Some screens in this room haven't been arranged yet, so Windows will choose their position and resolution."));
            planned = planned.Select(p => p with { X = 0, Y = 0, Width = 0, Height = 0, RefreshMillihz = 0, Rotation = 0, ScalePercent = null }).ToList();
        }

        return new PlanResult(new RoomPlan(room.Id, room.Name, planned), problems);
    }

    /// <summary>Windows makes the screen at (0,0) the main display, so shift everything to put the primary there.</summary>
    private static List<PlannedScreen> NormalizeToPrimary(List<ScreenSpec> screens, List<PlannedScreen> planned)
    {
        var primaryIndex = screens.FindIndex(s => s.Primary);
        if (primaryIndex < 0) primaryIndex = screens.FindIndex(s => s.X == 0 && s.Y == 0);
        if (primaryIndex < 0) primaryIndex = 0;
        var dx = screens[primaryIndex].X;
        var dy = screens[primaryIndex].Y;
        var result = planned.Select(p => p with { X = p.X - dx, Y = p.Y - dy }).ToList();
        // The primary goes first; backends rely on that ordering.
        var primary = result[primaryIndex];
        result.RemoveAt(primaryIndex);
        result.Insert(0, primary);
        return result;
    }

    private static bool Overlaps(ScreenSpec a, ScreenSpec b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    public static string ScreenLabel(Room room, ScreenSpec screen) =>
        string.IsNullOrWhiteSpace(screen.Name) ? $"Screen {room.Screens.IndexOf(screen) + 1}" : screen.Name;

    private static string MonitorName(AppConfig config, string id) => config.FindMonitor(id)?.Name ?? "A removed display";

    internal static string JoinNames(IReadOnlyList<string> names) =>
        names.Count <= 2 ? string.Join(" and ", names) : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

    private static RoomProblem Error(string message, string? monitorId = null, string? screenId = null) =>
        new(ProblemSeverity.Error, message, monitorId, screenId);

    private static RoomProblem Warning(string message, string? monitorId = null, string? screenId = null) =>
        new(ProblemSeverity.Warning, message, monitorId, screenId);
}
