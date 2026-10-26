using RoomSwitcher.Core.Display;

namespace RoomSwitcher.App;

/// <summary>
/// A hidden top-level window that hears WM_DISPLAYCHANGE (message-only windows don't get the
/// broadcast). Used to wait for a switch to settle, and to notice changes made outside the app.
/// </summary>
internal sealed class DisplayChangeListener : NativeWindow, IDisplaySettle, IDisposable
{
    private const int WM_DISPLAYCHANGE = 0x007E;
    private const int WM_SETTINGCHANGE = 0x001A;
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(4);

    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 800 };
    private TaskCompletionSource? _waiting;
    private DateTime _lastChangeUtc = DateTime.MinValue;

    /// <summary>Raised once things have been quiet for a moment after any display change.</summary>
    public event EventHandler? DisplayChanged;

    public DisplayChangeListener()
    {
        CreateHandle(new CreateParams { Caption = "Room Switcher display listener" });
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            DisplayChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DISPLAYCHANGE || (m.Msg == WM_SETTINGCHANGE && m.WParam == (IntPtr)0x2F /* SPI_SETWORKAREA */))
        {
            if (m.Msg == WM_DISPLAYCHANGE)
            {
                _lastChangeUtc = DateTime.UtcNow;
                _waiting?.TrySetResult();
            }
            _debounce.Stop();
            _debounce.Start();
        }
        base.WndProc(ref m);
    }

    public async Task WaitAsync(int settleDelayMs, CancellationToken cancellationToken = default)
    {
        // SetDisplayConfig can deliver WM_DISPLAYCHANGE to this thread before we start waiting.
        if (DateTime.UtcNow - _lastChangeUtc > TimeSpan.FromSeconds(2))
        {
            _waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await Task.WhenAny(_waiting.Task, Task.Delay(EventTimeout, cancellationToken));
            _waiting = null;
        }
        await Task.Delay(Math.Max(0, settleDelayMs), cancellationToken);
    }

    public void Dispose()
    {
        _debounce.Dispose();
        DestroyHandle();
    }
}
