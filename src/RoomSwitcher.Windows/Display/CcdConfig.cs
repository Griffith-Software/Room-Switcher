using RoomSwitcher.Windows.Native;
using static RoomSwitcher.Windows.Native.DisplayConfigApi;

namespace RoomSwitcher.Windows.Display;

/// <summary>A display configuration as the CCD API sees it: paths (source to target) and modes.</summary>
internal sealed class CcdConfig
{
    public DISPLAYCONFIG_PATH_INFO[] Paths { get; }
    public DISPLAYCONFIG_MODE_INFO[] Modes { get; }

    public CcdConfig(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        Paths = paths;
        Modes = modes;
    }

    public static CcdConfig Query(uint flags)
    {
        for (var attempt = 0; ; attempt++)
        {
            var error = GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
            if (error != ERROR_SUCCESS) throw new DisplayApiException("GetDisplayConfigBufferSizes", error);

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            error = QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            // The configuration can change between the two calls; ask again.
            if (error == ERROR_INSUFFICIENT_BUFFER && attempt < 5) continue;
            if (error != ERROR_SUCCESS) throw new DisplayApiException("QueryDisplayConfig", error);

            return new CcdConfig(paths[..(int)pathCount], modes[..(int)modeCount]);
        }
    }

    public static CcdConfig Active() => Query(QDC_ONLY_ACTIVE_PATHS);
    public static CcdConfig All() => Query(QDC_ALL_PATHS);

    public DISPLAYCONFIG_SOURCE_MODE? SourceMode(in DISPLAYCONFIG_PATH_INFO path)
    {
        var index = path.sourceInfo.modeInfoIdx;
        if (index == DISPLAYCONFIG_PATH_MODE_IDX_INVALID || index >= Modes.Length) return null;
        var mode = Modes[index];
        return mode.infoType == DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE ? mode.sourceMode : null;
    }

    public DISPLAYCONFIG_VIDEO_SIGNAL_INFO? TargetMode(in DISPLAYCONFIG_PATH_INFO path)
    {
        var index = path.targetInfo.modeInfoIdx;
        if (index == DISPLAYCONFIG_PATH_MODE_IDX_INVALID || index >= Modes.Length) return null;
        var mode = Modes[index];
        return mode.infoType == DISPLAYCONFIG_MODE_INFO_TYPE_TARGET ? mode.targetMode : null;
    }
}

internal sealed class DisplayApiException : Exception
{
    public int ErrorCode { get; }

    public DisplayApiException(string function, int errorCode)
        : base($"{function} failed with error {errorCode}.")
    {
        ErrorCode = errorCode;
    }
}

/// <summary>Lookups that need DisplayConfigGetDeviceInfo.</summary>
internal static class DeviceInfo
{
    public static DISPLAYCONFIG_TARGET_DEVICE_NAME? TargetName(LUID adapter, uint targetId)
    {
        var request = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(), adapter, targetId),
            monitorFriendlyDeviceName = "",
            monitorDevicePath = "",
        };
        return DisplayConfigGetDeviceInfo(ref request) == ERROR_SUCCESS ? request : null;
    }

    public static string? SourceGdiName(LUID adapter, uint sourceId)
    {
        var request = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(), adapter, sourceId),
            viewGdiDeviceName = "",
        };
        return DisplayConfigGetDeviceInfo(ref request) == ERROR_SUCCESS ? request.viewGdiDeviceName : null;
    }

    public static (uint Width, uint Height, uint RefreshMillihz)? PreferredMode(LUID adapter, uint targetId)
    {
        var request = new DISPLAYCONFIG_TARGET_PREFERRED_MODE
        {
            header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_PREFERRED_MODE, System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_TARGET_PREFERRED_MODE>(), adapter, targetId),
        };
        if (DisplayConfigGetDeviceInfo(ref request) != ERROR_SUCCESS || request.width == 0 || request.height == 0) return null;
        return (request.width, request.height, ToMillihz(request.targetMode.targetVideoSignalInfo.vSyncFreq));
    }

    // Steps the Settings app offers, in percent. The API works in steps relative to the recommended one.
    private static readonly int[] DpiSteps = [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];

    public static int? ScalePercent(LUID adapter, uint sourceId)
    {
        var request = new DISPLAYCONFIG_SOURCE_DPI_SCALE_GET
        {
            header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_DPI_SCALE, System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DPI_SCALE_GET>(), adapter, sourceId),
        };
        if (DisplayConfigGetDeviceInfo(ref request) != ERROR_SUCCESS) return null;
        var recommended = Math.Abs(request.minScaleRel);
        var current = Math.Clamp(recommended + request.curScaleRel, 0, DpiSteps.Length - 1);
        return DpiSteps[current];
    }

    public static bool SetScalePercent(LUID adapter, uint sourceId, int percent)
    {
        var get = new DISPLAYCONFIG_SOURCE_DPI_SCALE_GET
        {
            header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_DPI_SCALE, System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DPI_SCALE_GET>(), adapter, sourceId),
        };
        if (DisplayConfigGetDeviceInfo(ref get) != ERROR_SUCCESS) return false;
        var step = Array.IndexOf(DpiSteps, percent);
        if (step < 0) return false;
        var recommended = Math.Abs(get.minScaleRel);
        var relative = Math.Clamp(step - recommended, get.minScaleRel, get.maxScaleRel);
        if (relative == get.curScaleRel) return true;
        var set = new DISPLAYCONFIG_SOURCE_DPI_SCALE_SET
        {
            header = Header(DISPLAYCONFIG_DEVICE_INFO_SET_DPI_SCALE, System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DPI_SCALE_SET>(), adapter, sourceId),
            scaleRel = relative,
        };
        return DisplayConfigSetDeviceInfo(ref set) == ERROR_SUCCESS;
    }

    public static uint ToMillihz(DISPLAYCONFIG_RATIONAL r) =>
        r.Denominator == 0 ? 0 : (uint)Math.Round(r.Numerator * 1000.0 / r.Denominator);

    public static string Connector(uint outputTechnology) => outputTechnology switch
    {
        0 => "VGA",
        1 => "S-Video",
        2 or 3 => "Composite",
        4 => "DVI",
        5 => "HDMI",
        6 => "LVDS",
        8 => "D-Jpn",
        9 => "SDI",
        10 => "DisplayPort",
        11 => "Built-in DisplayPort",
        12 => "UDI",
        13 => "Built-in UDI",
        14 => "SDTV dongle",
        15 => "Miracast",
        16 => "Indirect (wired)",
        17 => "Indirect (virtual)",
        0x80000000 => "Built-in",
        _ => "Other",
    };

    private static DISPLAYCONFIG_DEVICE_INFO_HEADER Header(int type, int size, LUID adapter, uint id) =>
        new() { type = type, size = (uint)size, adapterId = adapter, id = id };
}
