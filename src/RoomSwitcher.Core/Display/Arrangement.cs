using System.Text.Json.Serialization;

namespace RoomSwitcher.Core.Display;

/// <summary>A resolution and refresh rate a monitor supports (landscape: Width ≥ Height).</summary>
public sealed record DisplayMode(int Width, int Height, int RefreshMillihz)
{
    [JsonIgnore] public string SizeText => $"{Width} × {Height}";
    [JsonIgnore] public string RefreshText => $"{RefreshMillihz / 1000.0:0.##} Hz";

    /// <summary>The same mode as it sits on the desktop at a rotation: portrait swaps width and height.</summary>
    public (int Width, int Height) Rotated(int rotation) => rotation is 90 or 270 ? (Height, Width) : (Width, Height);
}

/// <summary>
/// Where screens sit on the desktop, the way Windows' own arranger works: screens touch edge to
/// edge and never overlap. Coordinates are desktop pixels.
/// </summary>
public static class Arrangement
{
    public readonly record struct Box(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;

        public bool Overlaps(Box o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

        /// <summary>True when the boxes share part of an edge (corners alone don't count).</summary>
        public bool Touches(Box o) =>
            ((Right == o.X || o.Right == X) && Y < o.Bottom && o.Y < Bottom) ||
            ((Bottom == o.Y || o.Bottom == Y) && X < o.Right && o.X < Right);
    }

    /// <summary>Screens side by side, left to right, tops aligned.</summary>
    public static List<Box> SideBySide(IEnumerable<(int Width, int Height)> sizes)
    {
        var x = 0;
        var result = new List<Box>();
        foreach (var (w, h) in sizes)
        {
            result.Add(new Box(x, 0, w, h));
            x += w;
        }
        return result;
    }

    /// <summary>
    /// Where a screen dropped at <paramref name="dropped"/> ends up: the nearest spot where it
    /// touches another screen without overlapping any. Edges that are nearly lined up snap into
    /// line. With no other screens it stays where it was dropped.
    /// </summary>
    public static Box Snap(IReadOnlyList<Box> screens, int moving, Box dropped, int alignWithin = 60)
    {
        var others = screens.Where((_, i) => i != moving).ToList();
        if (others.Count == 0) return dropped;

        var candidates = new List<Box>();
        foreach (var o in others)
        {
            // Beside it (left or right): keep the dropped height, slide vertically to still share an edge.
            foreach (var x in new[] { o.X - dropped.Width, o.Right })
            {
                var y = Math.Clamp(dropped.Y, o.Y - dropped.Height + 1, o.Bottom - 1);
                candidates.Add(Align(new Box(x, y, dropped.Width, dropped.Height), o, vertical: true, alignWithin));
            }
            // Above or below.
            foreach (var y in new[] { o.Y - dropped.Height, o.Bottom })
            {
                var x = Math.Clamp(dropped.X, o.X - dropped.Width + 1, o.Right - 1);
                candidates.Add(Align(new Box(x, y, dropped.Width, dropped.Height), o, vertical: false, alignWithin));
            }
        }

        return candidates
            .Where(c => others.All(o => !c.Overlaps(o)))
            .OrderBy(c => Distance(c, dropped))
            .FirstOrDefault(dropped);
    }

    /// <summary>Lines the box up with a neighbour's edge or centre when it's close.</summary>
    private static Box Align(Box box, Box neighbour, bool vertical, int within)
    {
        if (vertical)
        {
            foreach (var y in new[] { neighbour.Y, neighbour.Bottom - box.Height, neighbour.Y + (neighbour.Height - box.Height) / 2 })
                if (Math.Abs(box.Y - y) <= within) return box with { Y = y };
        }
        else
        {
            foreach (var x in new[] { neighbour.X, neighbour.Right - box.Width, neighbour.X + (neighbour.Width - box.Width) / 2 })
                if (Math.Abs(box.X - x) <= within) return box with { X = x };
        }
        return box;
    }

    private static double Distance(Box a, Box b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    /// <summary>
    /// True when every screen is joined to the others through shared edges (Windows needs this),
    /// and no two overlap.
    /// </summary>
    public static bool IsValid(IReadOnlyList<Box> screens)
    {
        for (var i = 0; i < screens.Count; i++)
            for (var j = i + 1; j < screens.Count; j++)
                if (screens[i].Overlaps(screens[j])) return false;
        if (screens.Count <= 1) return true;

        var reached = new HashSet<int> { 0 };
        var queue = new Queue<int>([0]);
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            for (var j = 0; j < screens.Count; j++)
                if (!reached.Contains(j) && screens[i].Touches(screens[j]))
                {
                    reached.Add(j);
                    queue.Enqueue(j);
                }
        }
        return reached.Count == screens.Count;
    }

    /// <summary>Resizes one screen (new resolution or rotation) and nudges the others so nothing overlaps or floats.</summary>
    public static List<Box> Resize(IReadOnlyList<Box> screens, int index, int width, int height)
    {
        var result = screens.ToList();
        result[index] = result[index] with { Width = width, Height = height };
        if (IsValid(result)) return result;

        // Keep the resized screen where it is and re-attach the others to it, nearest first.
        var placed = new List<Box> { result[index] };
        var order = Enumerable.Range(0, result.Count)
            .Where(i => i != index)
            .OrderBy(i => Distance(result[i], result[index]))
            .ToList();
        foreach (var i in order)
        {
            var snapped = Snap(placed.Append(result[i]).ToList(), placed.Count, result[i]);
            result[i] = snapped;
            placed.Add(snapped);
        }
        return IsValid(result) ? result : SideBySide(result.Select(b => (b.Width, b.Height)));
    }

    /// <summary>The modes both (all) monitors of a mirrored screen can show.</summary>
    public static List<DisplayMode> Common(IEnumerable<IReadOnlyList<DisplayMode>> perMonitor)
    {
        List<DisplayMode>? common = null;
        foreach (var modes in perMonitor)
            common = common == null ? modes.ToList() : common.Where(modes.Contains).ToList();
        return common ?? [];
    }

    /// <summary>Distinct sizes, biggest first.</summary>
    public static List<(int Width, int Height)> Sizes(IEnumerable<DisplayMode> modes) =>
        modes.Select(m => (m.Width, m.Height)).Distinct().OrderByDescending(s => s.Width * s.Height).ThenByDescending(s => s.Width).ToList();

    /// <summary>Refresh rates available at a size, fastest first.</summary>
    public static List<int> Rates(IEnumerable<DisplayMode> modes, int width, int height) =>
        modes.Where(m => m.Width == width && m.Height == height).Select(m => m.RefreshMillihz).Distinct().OrderByDescending(r => r).ToList();
}
