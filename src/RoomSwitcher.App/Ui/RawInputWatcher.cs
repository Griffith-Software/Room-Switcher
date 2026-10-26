using System.Runtime.InteropServices;

namespace RoomSwitcher.App.Ui;

/// <summary>
/// Tells which physical keyboard or mouse is being used, with Windows Raw Input. Reports the
/// device's instance id (e.g. HID\VID_046D&amp;PID_C534&amp;MI_00\9&amp;58877EE&amp;0&amp;0000). Nothing typed is read
/// or kept: only which device the input came from.
/// </summary>
internal sealed class RawInputWatcher : NativeWindow, IDisposable
{
    private const int WM_INPUT = 0x00FF;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_REMOVE = 0x00000001;
    private const uint RID_HEADER = 0x10000005;
    private const uint RIDI_DEVICENAME = 0x20000007;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, out RAWINPUTHEADER data, ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfoW(IntPtr device, uint command, char[]? data, ref uint size);

    private readonly Dictionary<IntPtr, string?> _names = new();
    private readonly Dictionary<IntPtr, long> _lastReport = new();
    private bool _registered;

    /// <summary>Raised (on the UI thread, at most a few times a second per device) with the device's instance id.</summary>
    public event Action<string>? DeviceUsed;

    public RawInputWatcher()
    {
        CreateHandle(new CreateParams { Caption = "Room Switcher input test" });
    }

    public void Start()
    {
        if (_registered) return;
        _registered = Register(RIDEV_INPUTSINK, Handle);
    }

    public void Stop()
    {
        if (!_registered) return;
        Register(RIDEV_REMOVE, IntPtr.Zero);
        _registered = false;
    }

    private static bool Register(uint flags, IntPtr target)
    {
        var devices = new[]
        {
            new RAWINPUTDEVICE { UsagePage = 0x01, Usage = 0x06, Flags = flags, Target = target }, // keyboards
            new RAWINPUTDEVICE { UsagePage = 0x01, Usage = 0x02, Flags = flags, Target = target }, // mice
        };
        return RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT)
        {
            var size = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
            if (GetRawInputData(m.LParam, RID_HEADER, out var header, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) != uint.MaxValue
                && header.Device != IntPtr.Zero)
            {
                var now = Environment.TickCount64;
                if (!_lastReport.TryGetValue(header.Device, out var last) || now - last > 250)
                {
                    _lastReport[header.Device] = now;
                    if (InstanceId(header.Device) is { } id) DeviceUsed?.Invoke(id);
                }
            }
        }
        base.WndProc(ref m);
    }

    /// <summary>"\\?\HID#VID_046D&amp;PID_C534&amp;MI_00#9&amp;58877ee&amp;0&amp;0000#{guid}" → "HID\VID_046D&amp;PID_C534&amp;MI_00\9&amp;58877ee&amp;0&amp;0000".</summary>
    private string? InstanceId(IntPtr device)
    {
        if (_names.TryGetValue(device, out var cached)) return cached;
        uint size = 0;
        GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, null, ref size);
        string? id = null;
        if (size > 0)
        {
            var buffer = new char[size];
            if (GetRawInputDeviceInfoW(device, RIDI_DEVICENAME, buffer, ref size) != uint.MaxValue)
            {
                var path = new string(buffer).TrimEnd('\0');
                if (path.StartsWith(@"\\?\")) path = path[4..];
                var guid = path.LastIndexOf("#{", StringComparison.Ordinal);
                if (guid > 0) path = path[..guid];
                id = path.Replace('#', '\\');
            }
        }
        _names[device] = id;
        return id;
    }

    public void Dispose()
    {
        Stop();
        DestroyHandle();
    }
}
