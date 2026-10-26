using System.Runtime.InteropServices;
using RoomSwitcher.Core.Hotkeys;

namespace RoomSwitcher.App;

/// <summary>Registers system-wide keyboard shortcuts with RegisterHotKey and runs their actions.</summary>
internal sealed class HotkeyListener : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int ProbeId = 0xBFFF;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private readonly Dictionary<int, Action> _actions = new();
    private readonly HashSet<Hotkey> _registered = new();

    /// <summary>True if this listener currently holds the shortcut.</summary>
    public bool Holds(Hotkey hotkey) => _registered.Contains(hotkey);
    private int _nextId = 1;

    public HotkeyListener()
    {
        CreateHandle(new CreateParams { Caption = "Room Switcher shortcuts" });
    }

    /// <summary>Registers one shortcut. False if another program already owns it.</summary>
    public bool Register(Hotkey hotkey, Action action)
    {
        var id = _nextId++;
        if (!RegisterHotKey(Handle, id, (uint)hotkey.Modifiers | MOD_NOREPEAT, (uint)hotkey.Key)) return false;
        _actions[id] = action;
        _registered.Add(hotkey);
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(Handle, id);
        _actions.Clear();
        _registered.Clear();
    }

    /// <summary>True if no other program has this shortcut. Only meaningful while our own are unregistered.</summary>
    public bool IsAvailable(Hotkey hotkey)
    {
        if (!RegisterHotKey(Handle, ProbeId, (uint)hotkey.Modifiers | MOD_NOREPEAT, (uint)hotkey.Key)) return false;
        UnregisterHotKey(Handle, ProbeId);
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && _actions.TryGetValue((int)m.WParam, out var action))
        {
            action();
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
