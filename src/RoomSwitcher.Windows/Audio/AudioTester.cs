using System.Runtime.InteropServices;

namespace RoomSwitcher.Windows.Audio;

/// <summary>
/// Tests one specific sound device without changing the Windows default: plays a short chime
/// through a speaker, or reports a microphone's live level. Uses WASAPI in shared mode.
/// Run on a background thread.
/// </summary>
public static class AudioTester
{
    private const int CLSCTX_ALL = 23;
    private const int AUDCLNT_SHAREMODE_SHARED = 0;
    private const long ReftimesPerSecond = 10_000_000;
    private const ushort WAVE_FORMAT_PCM = 1;
    private const ushort WAVE_FORMAT_IEEE_FLOAT = 3;
    private const ushort WAVE_FORMAT_EXTENSIBLE = 0xFFFE;
    private const int AUDCLNT_BUFFERFLAGS_SILENT = 0x2;

    private static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid IID_IAudioRenderClient = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
    private static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    private static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid SubtypeFloat = new("00000003-0000-0010-8000-00AA00389B71");

    private static readonly Guid IID_IAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");

    /// <summary>Plays a two-note chime (about a second) through the device. Returns a problem, or null.</summary>
    public static string? PlayChime(string deviceId, CancellationToken cancellationToken = default) =>
        PlayChime(deviceId, null, cancellationToken);

    /// <summary>
    /// Plays the chime the way any app plays sound, and watches the speaker's own level meter
    /// (what Windows actually sends to it after mixing). Heard = the loudest level seen, 0 to 1:
    /// close to 0 means the chime never reached the speaker.
    /// </summary>
    public static (string? Problem, float Heard) CheckPlayback(string deviceId, CancellationToken cancellationToken = default)
    {
        var heard = 0f;
        var problem = PlayChime(deviceId, level => heard = Math.Max(heard, level), cancellationToken);
        return (problem, heard);
    }

    private static string? PlayChime(string deviceId, Action<float>? meter, CancellationToken cancellationToken)
    {
        return WithClient(deviceId, (client, format, device) =>
        {
            IAudioMeterInformation? levels = null;
            if (meter != null)
            {
                var meterIid = IID_IAudioMeterInformation;
                if (device.Activate(ref meterIid, CLSCTX_ALL, IntPtr.Zero, out var meterObject) == 0)
                    levels = (IAudioMeterInformation)meterObject;
            }
            void Measure()
            {
                if (levels != null && levels.GetPeakValue(out var peak) == 0) meter!(peak);
            }
            Check(client.Initialize(AUDCLNT_SHAREMODE_SHARED, 0, ReftimesPerSecond / 5, 0, format.Pointer, IntPtr.Zero));
            Check(client.GetBufferSize(out var bufferFrames));
            var iid = IID_IAudioRenderClient;
            Check(client.GetService(ref iid, out var renderObject));
            var render = (IAudioRenderClient)renderObject;
            try
            {
                var samples = Chime(format.SampleRate);
                var written = 0;
                Check(client.Start());
                while (written < samples.Length && !cancellationToken.IsCancellationRequested)
                {
                    Check(client.GetCurrentPadding(out var padding));
                    var frames = (int)Math.Min(bufferFrames - padding, samples.Length - written);
                    if (frames > 0)
                    {
                        Check(render.GetBuffer((uint)frames, out var data));
                        WriteFrames(data, samples, written, frames, format);
                        Check(render.ReleaseBuffer((uint)frames, 0));
                        written += frames;
                    }
                    Thread.Sleep(10);
                    Measure();
                }
                // Let what's queued finish playing.
                for (var i = 0; i < 40 && client.GetCurrentPadding(out var left) == 0 && left > 0; i++) { Thread.Sleep(10); Measure(); }
                client.Stop();
                return null;
            }
            finally
            {
                Marshal.ReleaseComObject(render);
                if (levels != null) Marshal.ReleaseComObject(levels);
            }
        });
    }

    /// <summary>
    /// Listens to the microphone and reports its level (0 to 1) about 20 times a second until cancelled.
    /// Returns a problem, or null.
    /// </summary>
    public static string? Listen(string deviceId, Action<float> level, CancellationToken cancellationToken)
    {
        return WithClient(deviceId, (client, format, _device) =>
        {
            Check(client.Initialize(AUDCLNT_SHAREMODE_SHARED, 0, ReftimesPerSecond / 5, 0, format.Pointer, IntPtr.Zero));
            var iid = IID_IAudioCaptureClient;
            Check(client.GetService(ref iid, out var captureObject));
            var capture = (IAudioCaptureClient)captureObject;
            try
            {
                Check(client.Start());
                var peak = 0f;
                var lastReport = Environment.TickCount64;
                while (!cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(10);
                    while (capture.GetNextPacketSize(out var packet) == 0 && packet > 0)
                    {
                        Check(capture.GetBuffer(out var data, out var frames, out var flags, out _, out _));
                        if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) == 0)
                            peak = Math.Max(peak, Peak(data, (int)frames, format));
                        capture.ReleaseBuffer(frames);
                    }
                    if (Environment.TickCount64 - lastReport >= 50)
                    {
                        level(Math.Clamp(peak, 0f, 1f));
                        peak = 0;
                        lastReport = Environment.TickCount64;
                    }
                }
                client.Stop();
                return null;
            }
            finally
            {
                Marshal.ReleaseComObject(capture);
            }
        });
    }

    // ---- helpers ----

    private sealed record Format(IntPtr Pointer, int SampleRate, int Channels, int BitsPerSample, bool IsFloat);

    private static string? WithClient(string deviceId, Func<IAudioClient, Format, IMMDevice, string?> body)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? client = null;
        var formatPointer = IntPtr.Zero;
        try
        {
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(MMDeviceEnumeratorClsid, true)!)!;
            if (enumerator.GetDevice(deviceId, out device) != 0 || device == null) return "Windows can't find this device.";
            var iid = IID_IAudioClient;
            Check(device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var clientObject));
            client = (IAudioClient)clientObject;
            Check(client.GetMixFormat(out formatPointer));
            return body(client, ReadFormat(formatPointer), device);
        }
        catch (COMException e) when ((uint)e.HResult == 0x88890004) // AUDCLNT_E_DEVICE_INVALIDATED
        {
            return "The device isn't connected, or was disconnected during the test.";
        }
        catch (COMException e) when ((uint)e.HResult == 0x8889000A) // AUDCLNT_E_DEVICE_IN_USE
        {
            return "Another app is using this device right now. Close it and try again.";
        }
        catch (Exception e)
        {
            return $"Couldn't test this device. {e.Message}";
        }
        finally
        {
            if (formatPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(formatPointer);
            if (client != null) Marshal.ReleaseComObject(client);
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    private static Format ReadFormat(IntPtr pointer)
    {
        var tag = (ushort)Marshal.ReadInt16(pointer, 0);
        var channels = Marshal.ReadInt16(pointer, 2);
        var rate = Marshal.ReadInt32(pointer, 4);
        var bits = Marshal.ReadInt16(pointer, 14);
        var isFloat = tag == WAVE_FORMAT_IEEE_FLOAT;
        if (tag == WAVE_FORMAT_EXTENSIBLE)
        {
            var bytes = new byte[16];
            Marshal.Copy(pointer + 24, bytes, 0, 16); // WAVEFORMATEXTENSIBLE.SubFormat
            isFloat = new Guid(bytes) == SubtypeFloat;
        }
        else if (tag != WAVE_FORMAT_PCM && tag != WAVE_FORMAT_IEEE_FLOAT)
        {
            throw new NotSupportedException("This device uses a sound format the test can't play.");
        }
        return new Format(pointer, rate, channels, bits, isFloat);
    }

    /// <summary>A soft two-note chime (E5 then A5) with a smooth fade, at the device's sample rate.</summary>
    private static float[] Chime(int rate)
    {
        var length = (int)(rate * 1.1);
        var samples = new float[length];
        foreach (var (start, freq) in new[] { (0.0, 659.25), (0.35, 880.0) })
        {
            var first = (int)(start * rate);
            for (var i = first; i < length; i++)
            {
                var t = (i - first) / (double)rate;
                var envelope = Math.Min(1, t * 80) * Math.Exp(-t * 4.5);
                samples[i] += (float)(0.25 * envelope * Math.Sin(2 * Math.PI * freq * t));
            }
        }
        return samples;
    }

    private static void WriteFrames(IntPtr data, float[] samples, int offset, int frames, Format format)
    {
        var bytesPerSample = format.BitsPerSample / 8;
        for (var f = 0; f < frames; f++)
        {
            var value = samples[offset + f];
            for (var c = 0; c < format.Channels; c++)
            {
                var at = data + (f * format.Channels + c) * bytesPerSample;
                if (format.IsFloat && bytesPerSample == 4) Marshal.WriteInt32(at, BitConverter.SingleToInt32Bits(value));
                else if (bytesPerSample == 2) Marshal.WriteInt16(at, (short)(value * short.MaxValue));
                else if (bytesPerSample == 4) Marshal.WriteInt32(at, (int)(value * int.MaxValue));
                else if (bytesPerSample == 3)
                {
                    var v = (int)(value * 8388607);
                    Marshal.WriteByte(at, (byte)v);
                    Marshal.WriteByte(at + 1, (byte)(v >> 8));
                    Marshal.WriteByte(at + 2, (byte)(v >> 16));
                }
            }
        }
    }

    private static float Peak(IntPtr data, int frames, Format format)
    {
        var bytesPerSample = format.BitsPerSample / 8;
        var count = frames * format.Channels;
        var peak = 0f;
        for (var i = 0; i < count; i++)
        {
            var at = data + i * bytesPerSample;
            float v = format.IsFloat && bytesPerSample == 4
                ? BitConverter.Int32BitsToSingle(Marshal.ReadInt32(at))
                : bytesPerSample == 2 ? Marshal.ReadInt16(at) / 32768f
                : bytesPerSample == 4 ? Marshal.ReadInt32(at) / 2147483648f
                : 0f;
            peak = Math.Max(peak, Math.Abs(v));
        }
        return peak;
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object itf);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint bufferFrames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioRenderClient
    {
        [PreserveSig] int GetBuffer(uint frames, out IntPtr data);
        [PreserveSig] int ReleaseBuffer(uint frames, uint flags);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
