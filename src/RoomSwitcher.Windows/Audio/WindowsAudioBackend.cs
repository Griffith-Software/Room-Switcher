using System.Runtime.InteropServices;
using RoomSwitcher.Core.Audio;

namespace RoomSwitcher.Windows.Audio;

/// <summary>
/// Lists sound devices with the Core Audio API and sets the default one with IPolicyConfig,
/// the interface the Windows Sound settings use (also used by EarTrumpet and SoundSwitch).
/// Must be called from an STA thread (the tray's UI thread, or the command line's main thread).
/// </summary>
public sealed class WindowsAudioBackend : IAudioBackend
{
    private const int DEVICE_STATE_ACTIVE = 0x1;
    private const int DEVICE_STATE_UNPLUGGED = 0x8;
    private const int STGM_READ = 0;

    private enum EDataFlow { eRender = 0, eCapture = 1 }
    private enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    private static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid PolicyConfigClsid = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
    private static PROPERTYKEY FriendlyName = new() { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };

    public IReadOnlyList<AudioDevice> List(AudioFlow flow)
    {
        var result = new List<AudioDevice>();
        var dataFlow = flow == AudioFlow.Output ? EDataFlow.eRender : EDataFlow.eCapture;
        var enumerator = CreateEnumerator();
        try
        {
            string? defaultId = null;
            if (enumerator.GetDefaultAudioEndpoint(dataFlow, ERole.eMultimedia, out var defaultDevice) == 0 && defaultDevice != null)
            {
                defaultDevice.GetId(out defaultId);
                Marshal.ReleaseComObject(defaultDevice);
            }

            Check(enumerator.EnumAudioEndpoints(dataFlow, DEVICE_STATE_ACTIVE | DEVICE_STATE_UNPLUGGED, out var collection));
            try
            {
                Check(collection.GetCount(out var count));
                for (uint i = 0; i < count; i++)
                {
                    if (collection.Item(i, out var device) != 0 || device == null) continue;
                    try
                    {
                        device.GetId(out var id);
                        device.GetState(out var state);
                        result.Add(new AudioDevice(id, ReadName(device) ?? id, flow, (state & DEVICE_STATE_ACTIVE) != 0,
                            string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(collection);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
        return result.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public bool SetDefault(string deviceId)
    {
        var type = Type.GetTypeFromCLSID(PolicyConfigClsid, throwOnError: true)!;
        var policy = (IPolicyConfig)Activator.CreateInstance(type)!;
        try
        {
            // All three roles, like choosing the device in Windows' Sound settings.
            return policy.SetDefaultEndpoint(deviceId, ERole.eConsole) == 0
                   & policy.SetDefaultEndpoint(deviceId, ERole.eMultimedia) == 0
                   & policy.SetDefaultEndpoint(deviceId, ERole.eCommunications) == 0;
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }

    private static IMMDeviceEnumerator CreateEnumerator()
    {
        var type = Type.GetTypeFromCLSID(MMDeviceEnumeratorClsid, throwOnError: true)!;
        return (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
    }

    private static string? ReadName(IMMDevice device)
    {
        if (device.OpenPropertyStore(STGM_READ, out var store) != 0 || store == null) return null;
        try
        {
            if (store.GetValue(ref FriendlyName, out var value) != 0) return null;
            try
            {
                return value.vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.pointer) : null;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static void Check(int hr)
    {
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT value);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        public ushort vt;
        public ushort reserved1, reserved2, reserved3;
        public IntPtr pointer;
        public IntPtr pointer2;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object itf);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    // Undocumented but stable since Windows 7. Only SetDefaultEndpoint is called; the methods
    // before it are placeholders that keep the vtable in the right order.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility();
    }
}
