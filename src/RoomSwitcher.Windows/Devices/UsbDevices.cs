using System.Runtime.InteropServices;
using RoomSwitcher.Core.Devices;

namespace RoomSwitcher.Windows.Devices;

/// <summary>
/// Finds the extra devices plugged into or paired with the PC: keyboards, mice, webcams, game
/// controllers, pens, printers, phones, USB drives and other USB or Bluetooth devices. They're
/// grouped into physical devices by their Windows device container. Speakers and microphones are
/// left out (they're on the Audio step). Listing needs no special rights; turning devices on or
/// off needs administrator rights.
/// </summary>
public static class UsbDevices
{
    /// <summary>One Windows device node that belongs to a physical device (e.g. the keyboard part of a receiver).</summary>
    public sealed record Member(string InstanceId, string Class, UsbKind Kind, bool Enabled);

    /// <param name="Members">The parts that can be turned on and off. Empty for a paired Bluetooth device that's away.</param>
    public sealed record Physical(UsbDevice Device, IReadOnlyList<Member> Members);

    private static readonly Guid NoContainer = new("00000000-0000-0000-FFFF-FFFFFFFFFFFF");

    public static IReadOnlyList<UsbDevice> List() => ListPhysical().Select(p => p.Device).ToList();

    private sealed class Group
    {
        public readonly List<Member> Members = new();
        public UsbKind Kinds;
        public string? BluetoothName;
        public string? UsbName;
        public string? OtherName;
        public bool Bluetooth;
        public bool Usb;
        public bool HasAudio;
        public bool HasBluetoothNode;
    }

    public static IReadOnlyList<Physical> ListPhysical()
    {
        var groups = new Dictionary<Guid, Group>();
        Group GroupFor(Guid container) => groups.TryGetValue(container, out var g) ? g : groups[container] = new Group();

        foreach (var devInst in AllPresent())
        {
            var instanceId = Native.InstanceId(devInst);
            if (instanceId == null) continue;
            var container = Native.GetGuid(devInst, Native.DEVPKEY_Device_ContainerId);
            if (container == null || container == NoContainer) continue; // part of the PC itself
            var cls = Native.GetString(devInst, Native.DEVPKEY_Device_Class) ?? "";

            // A paired Bluetooth device's own node: there even when the device is away.
            if (instanceId.StartsWith("BTHENUM\\DEV_", StringComparison.OrdinalIgnoreCase) ||
                instanceId.StartsWith("BTHLE\\DEV_", StringComparison.OrdinalIgnoreCase))
            {
                var bt = GroupFor(container.Value);
                bt.HasBluetoothNode = bt.Bluetooth = true;
                bt.BluetoothName ??= Native.GetString(devInst, Native.DEVPKEY_Device_FriendlyName);
                var (kind, audio) = BluetoothKind(Native.GetUInt32(devInst, Native.DEVPKEY_Bluetooth_ClassOfDevice));
                bt.Kinds |= kind;
                bt.HasAudio |= audio;
                continue;
            }

            var (connection, nameNode) = Connection(devInst, instanceId);
            if (connection == null) continue; // built in (PS/2, I2C touchpad, internal camera): not an extra device

            if (cls.Equals("MEDIA", StringComparison.OrdinalIgnoreCase) || cls.Equals("AudioEndpoint", StringComparison.OrdinalIgnoreCase))
            {
                GroupFor(container.Value).HasAudio = true; // speakers and microphones are on the Audio step
                continue;
            }

            var description = Native.GetString(devInst, Native.DEVPKEY_Device_DeviceDesc) ?? "";
            var memberKind = KindOf(cls, description, instanceId);
            if (memberKind == null) continue;

            var group = GroupFor(container.Value);
            if (connection == "Bluetooth") group.Bluetooth = true; else group.Usb = true;
            if (connection == "USB")
                group.UsbName ??= Native.GetString(nameNode, Native.DEVPKEY_Device_BusReportedDeviceDesc)
                                  ?? Native.GetString(nameNode, Native.DEVPKEY_Device_FriendlyName);
            group.OtherName ??= Native.GetString(devInst, Native.DEVPKEY_Device_FriendlyName) ?? (description.Length > 0 ? description : null);
            group.Kinds |= memberKind.Value;
            group.Members.Add(new Member(instanceId, cls, memberKind.Value, !Native.IsDisabled(devInst)));
        }

        return groups
            // Nothing but sound (a headset, USB speakers): that's for the Audio step.
            .Where(g => !(g.Value.HasAudio && (g.Value.Kinds & ~UsbKind.Other) == UsbKind.Other))
            // Nothing we could show or switch.
            .Where(g => g.Value.Members.Count > 0 || g.Value.HasBluetoothNode)
            .Select(g =>
            {
                var v = g.Value;
                var name = v.BluetoothName ?? v.UsbName ?? v.OtherName ?? "Device";
                var connected = v.Members.Count > 0;
                var device = new UsbDevice(g.Key.ToString("B"), name, v.Kinds, v.Bluetooth ? "Bluetooth" : "USB",
                    Enabled: !connected || v.Members.Any(m => m.Enabled), Connected: connected);
                return new Physical(device, v.Members);
            })
            .OrderBy(p => KindOrder(p.Device.Kinds))
            .ThenBy(p => p.Device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Turns every part of the physical device on or off. Needs administrator rights.
    /// Returns a plain-language problem, or null when it worked.
    /// </summary>
    public static string? SetEnabled(Physical physical, bool enable)
    {
        var failures = new List<string>();
        var restart = false;
        foreach (var member in physical.Members)
        {
            if (member.Enabled == enable) continue;
            var error = Native.ChangeState(member.InstanceId, enable, out var needsRestart);
            restart |= needsRestart;
            if (error != 0) failures.Add(error == 5 ? "access denied" : $"error code {error}");
        }
        var verb = enable ? "turn on" : "turn off";
        if (failures.Count > 0) return $"Windows couldn't {verb} {physical.Device.Name} ({failures[0]}).";
        if (restart) return $"{physical.Device.Name} will finish turning {(enable ? "on" : "off")} after Windows restarts.";
        return null;
    }

    /// <summary>What kind of device a Windows device node is, or null for nodes that aren't shown or switched.</summary>
    public static UsbKind? KindOf(string deviceClass, string description, string instanceId)
    {
        switch (deviceClass.ToUpperInvariant())
        {
            case "KEYBOARD": return UsbKind.Keyboard;
            case "MOUSE": return UsbKind.Mouse;
            case "CAMERA":
            case "IMAGE": return UsbKind.Camera;
            case "DISKDRIVE":
                // USB drives only (not internal SATA/NVMe drives).
                return instanceId.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase) ? UsbKind.Storage : null;
            case "WPD": return UsbKind.Phone;
            case "PRINTER": return UsbKind.Printer;
            case "XNACOMPOSITE":
            case "XBOXCOMPOSITE": return UsbKind.Controller;
            case "SMARTCARDREADER":
            case "BIOMETRIC": return UsbKind.Other;
            case "HIDCLASS":
                // The USB and Bluetooth "input device" nodes are the transport for the HID devices below
                // them; skip them so a device is switched once, through its HID parts (turning a transport
                // node off and on again could leave the parts below it switched off).
                if (instanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) ||
                    instanceId.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase) ||
                    instanceId.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase)) return null;
                var d = description.ToLowerInvariant();
                if (d.Contains("game controller") || d.Contains("gamepad") || d.Contains("joystick")) return UsbKind.Controller;
                if (d.Contains("pen") || d.Contains("digitizer") || d.Contains("tablet") || d.Contains("stylus")) return UsbKind.Pen;
                return UsbKind.Other;
            default:
                return null;
        }
    }

    /// <summary>The kind of a paired Bluetooth device from its Bluetooth "class of device".</summary>
    private static (UsbKind Kind, bool Audio) BluetoothKind(uint? classOfDevice)
    {
        if (classOfDevice is not uint cod) return (UsbKind.Other, false);
        var major = (cod >> 8) & 0x1F;
        var minor = (cod >> 2) & 0x3F;
        return major switch
        {
            0x04 => (UsbKind.Other, true), // audio/video: headphones, speakers
            0x02 => (UsbKind.Phone, false),
            0x05 => ((minor >> 4) switch
            {
                1 => UsbKind.Keyboard,
                2 => UsbKind.Mouse,
                3 => UsbKind.Keyboard | UsbKind.Mouse,
                _ => (minor & 0xF) is 1 or 2 ? UsbKind.Controller : (minor & 0xF) == 5 ? UsbKind.Pen : UsbKind.Other,
            }, false),
            0x06 => (UsbKind.Printer, false),
            _ => (UsbKind.Other, false),
        };
    }

    private static int KindOrder(UsbKind kinds) =>
        kinds.HasFlag(UsbKind.Keyboard) ? 0
        : kinds.HasFlag(UsbKind.Mouse) ? 1
        : kinds.HasFlag(UsbKind.Controller) ? 2
        : kinds.HasFlag(UsbKind.Pen) ? 3
        : kinds.HasFlag(UsbKind.Camera) ? 4
        : kinds.HasFlag(UsbKind.Phone) ? 5
        : kinds.HasFlag(UsbKind.Printer) ? 6
        : kinds.HasFlag(UsbKind.Storage) ? 7 : 8;

    /// <summary>
    /// Walks up the device tree: on a USB bus → "USB", on Bluetooth → "Bluetooth", otherwise null.
    /// Also returns the node that best names the physical device (the top USB device, not one of its interfaces).
    /// </summary>
    private static (string? Connection, uint NameNode) Connection(uint devInst, string instanceId)
    {
        string? connection = null;
        var nameNode = devInst;
        var named = false;
        var node = devInst;
        var id = instanceId;
        for (var depth = 0; depth < 12 && id != null; depth++)
        {
            if (id.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase) || id.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase))
            {
                connection ??= "USB";
                // The device's own node: the first USB node above it that isn't one of its interfaces
                // (higher up are the hubs it's plugged into).
                if (!named && !id.Contains("&MI_", StringComparison.OrdinalIgnoreCase))
                {
                    nameNode = node;
                    named = true;
                }
            }
            else if (id.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase) || id.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase))
            {
                connection ??= "Bluetooth";
                nameNode = node;
            }
            else if (id.StartsWith("USB\\ROOT_HUB", StringComparison.OrdinalIgnoreCase) || id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            if (Native.Parent(node) is not uint parent) break;
            node = parent;
            id = Native.InstanceId(node);
        }
        return (connection, nameNode);
    }

    private static IEnumerable<uint> AllPresent()
    {
        var set = Native.SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, Native.DIGCF_PRESENT | Native.DIGCF_ALLCLASSES);
        if (set == new IntPtr(-1)) yield break;
        try
        {
            var data = new Native.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<Native.SP_DEVINFO_DATA>() };
            for (uint i = 0; Native.SetupDiEnumDeviceInfo(set, i, ref data); i++)
                yield return data.DevInst;
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static class Native
    {
        public const uint DIGCF_PRESENT = 0x2;
        public const uint DIGCF_ALLCLASSES = 0x4;
        private const uint DIF_PROPERTYCHANGE = 0x12;
        private const uint DICS_ENABLE = 1;
        private const uint DICS_DISABLE = 2;
        private const uint DICS_FLAG_GLOBAL = 1;
        private const uint DI_NEEDRESTART = 0x80;
        private const uint DI_NEEDREBOOT = 0x100;
        private const uint DN_HAS_PROBLEM = 0x400;
        private const uint CM_PROB_DISABLED = 22;
        private const int CR_SUCCESS = 0;
        private const int CR_BUFFER_SMALL = 0x1A;

        public static readonly DEVPROPKEY DEVPKEY_Device_DeviceDesc = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 2);
        public static readonly DEVPROPKEY DEVPKEY_Device_Class = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 9);
        public static readonly DEVPROPKEY DEVPKEY_Device_FriendlyName = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
        public static readonly DEVPROPKEY DEVPKEY_Device_ContainerId = new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
        public static readonly DEVPROPKEY DEVPKEY_Device_BusReportedDeviceDesc = new(new Guid("540B947E-8B40-45BC-A8A2-6A0B894CBDA2"), 4);
        public static readonly DEVPROPKEY DEVPKEY_Bluetooth_ClassOfDevice = new(new Guid("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A"), 10);

        [StructLayout(LayoutKind.Sequential)]
        public readonly struct DEVPROPKEY(Guid fmtid, uint pid)
        {
            public readonly Guid fmtid = fmtid;
            public readonly uint pid = pid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_PROPCHANGE_PARAMS
        {
            public uint cbSize; // SP_CLASSINSTALL_HEADER
            public uint InstallFunction;
            public uint StateChange;
            public uint Scope;
            public uint HwProfile;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SP_DEVINSTALL_PARAMS
        {
            public uint cbSize;
            public uint Flags;
            public uint FlagsEx;
            public IntPtr hwndParent;
            public IntPtr InstallMsgHandler;
            public IntPtr InstallMsgHandlerContext;
            public IntPtr FileQueue;
            public UIntPtr ClassInstallReserved;
            public uint Reserved;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DriverPath;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr classGuid, IntPtr hwndParent);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiOpenDeviceInfoW(IntPtr set, string instanceId, IntPtr hwndParent, uint flags, ref SP_DEVINFO_DATA data);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiSetClassInstallParamsW(IntPtr set, ref SP_DEVINFO_DATA data, ref SP_PROPCHANGE_PARAMS parameters, int size);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiCallClassInstaller(uint installFunction, IntPtr set, ref SP_DEVINFO_DATA data);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInstallParamsW(IntPtr set, ref SP_DEVINFO_DATA data, ref SP_DEVINSTALL_PARAMS parameters);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_DevNode_PropertyW(uint devInst, in DEVPROPKEY key, out uint type, byte[]? buffer, ref uint size, uint flags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_Device_IDW(uint devInst, char[] buffer, int length, int flags);

        [DllImport("cfgmgr32.dll")]
        private static extern int CM_Get_Parent(out uint parent, uint devInst, int flags);

        [DllImport("cfgmgr32.dll")]
        private static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, int flags);

        private static byte[]? GetBytes(uint devInst, DEVPROPKEY key)
        {
            uint size = 0;
            var cr = CM_Get_DevNode_PropertyW(devInst, in key, out _, null, ref size, 0);
            if (cr != CR_BUFFER_SMALL || size == 0) return null;
            var buffer = new byte[size];
            return CM_Get_DevNode_PropertyW(devInst, in key, out _, buffer, ref size, 0) == CR_SUCCESS ? buffer : null;
        }

        public static string? GetString(uint devInst, DEVPROPKEY key)
        {
            var bytes = GetBytes(devInst, key);
            if (bytes == null || bytes.Length < 2) return null;
            var text = System.Text.Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        public static uint? GetUInt32(uint devInst, DEVPROPKEY key)
        {
            var bytes = GetBytes(devInst, key);
            return bytes is { Length: 4 } ? BitConverter.ToUInt32(bytes) : null;
        }

        public static Guid? GetGuid(uint devInst, DEVPROPKEY key)
        {
            var bytes = GetBytes(devInst, key);
            return bytes is { Length: 16 } ? new Guid(bytes) : null;
        }

        public static string? InstanceId(uint devInst)
        {
            var buffer = new char[400];
            return CM_Get_Device_IDW(devInst, buffer, buffer.Length, 0) == CR_SUCCESS ? new string(buffer).TrimEnd('\0') : null;
        }

        public static uint? Parent(uint devInst) => CM_Get_Parent(out var parent, devInst, 0) == CR_SUCCESS ? parent : null;

        public static bool IsDisabled(uint devInst) =>
            CM_Get_DevNode_Status(out var status, out var problem, devInst, 0) == CR_SUCCESS
            && (status & DN_HAS_PROBLEM) != 0 && problem == CM_PROB_DISABLED;

        /// <summary>Enables or disables one device node like Device Manager. Returns 0 or a Win32 error.</summary>
        public static int ChangeState(string instanceId, bool enable, out bool needsRestart)
        {
            needsRestart = false;
            var set = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
            if (set == new IntPtr(-1)) return Marshal.GetLastWin32Error();
            try
            {
                var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                if (!SetupDiOpenDeviceInfoW(set, instanceId, IntPtr.Zero, 0, ref data)) return Marshal.GetLastWin32Error();
                var change = new SP_PROPCHANGE_PARAMS
                {
                    cbSize = 8,
                    InstallFunction = DIF_PROPERTYCHANGE,
                    StateChange = enable ? DICS_ENABLE : DICS_DISABLE,
                    Scope = DICS_FLAG_GLOBAL,
                    HwProfile = 0,
                };
                if (!SetupDiSetClassInstallParamsW(set, ref data, ref change, Marshal.SizeOf<SP_PROPCHANGE_PARAMS>()))
                    return Marshal.GetLastWin32Error();
                if (!SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, set, ref data)) return Marshal.GetLastWin32Error();
                var install = new SP_DEVINSTALL_PARAMS { cbSize = (uint)Marshal.SizeOf<SP_DEVINSTALL_PARAMS>(), DriverPath = "" };
                if (SetupDiGetDeviceInstallParamsW(set, ref data, ref install))
                    needsRestart = (install.Flags & (DI_NEEDRESTART | DI_NEEDREBOOT)) != 0;
                return 0;
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
        }
    }
}
