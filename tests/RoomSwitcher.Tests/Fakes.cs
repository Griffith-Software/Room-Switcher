using RoomSwitcher.Core.Display;
using RoomSwitcher.Core.Layouts;
using RoomSwitcher.Core.Model;

namespace RoomSwitcher.Tests;

internal sealed class FakeDisplay : IDisplayBackend
{
    private sealed record Point(DisplayState State) : IDisplayRestorePoint;

    public DisplayState State { get; set; }
    public string? ValidateError { get; set; }
    public string? ApplyError { get; set; }
    /// <summary>Simulates a driver that reports success but leaves nothing on.</summary>
    public bool ApplyLeavesNothingOn { get; set; }
    public bool RestoreFails { get; set; }
    public List<string> Calls { get; } = new();
    public Action? OnApplied { get; set; }

    public FakeDisplay(DisplayState state) => State = state;

    public DisplayState Query() => State;

    public DisplayResult Validate(RoomPlan plan)
    {
        Calls.Add("validate");
        return ValidateError == null ? DisplayResult.Success : DisplayResult.Fail(ValidateError);
    }

    public DisplayResult Apply(RoomPlan plan)
    {
        Calls.Add($"apply:{plan.RoomName}");
        if (ApplyError != null) return DisplayResult.Fail(ApplyError);
        State = ApplyLeavesNothingOn
            ? TestData.State()
            : TestData.State(plan.Screens.Select(s => TestData.Screen(s.X, s.Y, s.Width == 0 ? 1920 : s.Width, s.Height == 0 ? 1080 : s.Height, s.DevicePaths.ToArray())).ToArray());
        OnApplied?.Invoke();
        return DisplayResult.Success;
    }

    public IDisplayRestorePoint CaptureRestorePoint() => new Point(State);

    public DisplayResult Restore(IDisplayRestorePoint point)
    {
        Calls.Add("restore");
        if (RestoreFails) return DisplayResult.Fail("nope");
        State = ((Point)point).State;
        return DisplayResult.Success;
    }

    public DisplayResult ShowAll()
    {
        Calls.Add("showall");
        State = TestData.State(
            TestData.Screen(0, 0, 1920, 1080, TestData.PathLeft),
            TestData.Screen(1920, 0, 1920, 1080, TestData.PathRight),
            TestData.Screen(3840, 0, 3840, 2160, TestData.PathTv));
        return DisplayResult.Success;
    }
}

internal sealed class FakeWindows : IWindowBackend
{
    public List<SavedWindow> Open { get; } = new();
    public HashSet<long> Elevated { get; } = new();
    public int ApplyCount { get; private set; }

    public IReadOnlyList<SavedWindow> Enumerate() => Open.Select(Copy).ToList();

    public bool Apply(SavedWindow live, SavedWindow placement)
    {
        ApplyCount++;
        if (Elevated.Contains(live.Hwnd)) return false;
        var w = Open.First(o => o.Hwnd == live.Hwnd);
        w.ShowCmd = placement.ShowCmd;
        w.NormalRect = new RectData(placement.NormalRect.Left, placement.NormalRect.Top, placement.NormalRect.Right, placement.NormalRect.Bottom);
        return true;
    }

    /// <summary>What Windows does when monitors go away: everything lands on the main display.</summary>
    public void PileOntoPrimary()
    {
        foreach (var w in Open)
        {
            var width = w.NormalRect.Right - w.NormalRect.Left;
            var height = w.NormalRect.Bottom - w.NormalRect.Top;
            w.NormalRect = new RectData(10, 10, 10 + width, 10 + height);
            w.ShowCmd = 1;
        }
    }

    public static SavedWindow Window(long hwnd, string exe, string title, int left, int top, int showCmd = 1) => new()
    {
        Hwnd = hwnd, Exe = exe, Class = exe + "Class", Title = title, ShowCmd = showCmd,
        NormalRect = new RectData(left, top, left + 800, top + 600),
    };

    private static SavedWindow Copy(SavedWindow w) => new()
    {
        Hwnd = w.Hwnd, Exe = w.Exe, Class = w.Class, Title = w.Title, ShowCmd = w.ShowCmd,
        NormalRect = new RectData(w.NormalRect.Left, w.NormalRect.Top, w.NormalRect.Right, w.NormalRect.Bottom),
    };
}

internal sealed class NoWaitSettle : IDisplaySettle
{
    public int Waits { get; private set; }
    public Task WaitAsync(int settleDelayMs, CancellationToken cancellationToken = default)
    {
        Waits++;
        return Task.CompletedTask;
    }
}
