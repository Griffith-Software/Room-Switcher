using RoomSwitcher.Core.Model;

namespace RoomSwitcher.Core.Layouts;

/// <summary>
/// Pairs saved windows with the windows open now. Order of preference:
/// 1. same handle, still the same program and class (handles get reused after a window closes);
/// 2. same program, class and title;
/// 3. same program and class (titles change, e.g. a browser tab).
/// Each open window is used at most once.
/// </summary>
public static class WindowMatcher
{
    public sealed record Match(SavedWindow Saved, SavedWindow Live);

    public static IReadOnlyList<Match> MatchAll(IReadOnlyList<SavedWindow> saved, IReadOnlyList<SavedWindow> live)
    {
        var matches = new List<Match>();
        var usedLive = new HashSet<SavedWindow>(ReferenceEqualityComparer.Instance);
        var pending = new List<SavedWindow>(saved);

        void Pass(Func<SavedWindow, SavedWindow, bool> rule)
        {
            foreach (var s in pending.ToList())
            {
                var l = live.FirstOrDefault(l => !usedLive.Contains(l) && rule(s, l));
                if (l == null) continue;
                usedLive.Add(l);
                pending.Remove(s);
                matches.Add(new Match(s, l));
            }
        }

        Pass((s, l) => s.Hwnd != 0 && s.Hwnd == l.Hwnd && SameProgram(s, l));
        Pass((s, l) => SameProgram(s, l) && string.Equals(s.Title, l.Title, StringComparison.Ordinal));
        Pass(SameProgram);
        return matches;
    }

    private static bool SameProgram(SavedWindow a, SavedWindow b) =>
        string.Equals(a.Exe, b.Exe, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Class, b.Class, StringComparison.Ordinal);

    /// <summary>True when applying the saved placement would change nothing.</summary>
    public static bool SamePlacement(SavedWindow a, SavedWindow b) =>
        a.ShowCmd == b.ShowCmd &&
        a.NormalRect.Left == b.NormalRect.Left && a.NormalRect.Top == b.NormalRect.Top &&
        a.NormalRect.Right == b.NormalRect.Right && a.NormalRect.Bottom == b.NormalRect.Bottom;
}

/// <summary>Open top-level windows, seen from the core. Implemented in RoomSwitcher.Windows.</summary>
public interface IWindowBackend
{
    /// <summary>The visible, ordinary application windows open now, with their placement.</summary>
    IReadOnlyList<SavedWindow> Enumerate();

    /// <summary>Moves the live window to the saved placement. False if Windows refused (e.g. an elevated window).</summary>
    bool Apply(SavedWindow live, SavedWindow placement);
}
