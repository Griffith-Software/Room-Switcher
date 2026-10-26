using RoomSwitcher.Core.Model;

namespace RoomSwitcher.Core.Audio;

public enum AudioFlow { Output, Input }

/// <summary>A sound device Windows knows about. Not connected = unplugged, or a TV whose screen is off.</summary>
public sealed record AudioDevice(string Id, string Name, AudioFlow Flow, bool Connected, bool IsDefault);

/// <summary>The Windows sound API, seen from the core. Implemented in RoomSwitcher.Windows.</summary>
public interface IAudioBackend
{
    /// <summary>Playback or recording devices, connected or not (disabled devices are left out).</summary>
    IReadOnlyList<AudioDevice> List(AudioFlow flow);

    /// <summary>Makes the device the Windows default for everything (apps, calls and system sounds).</summary>
    bool SetDefault(string deviceId);
}

public static class AudioRules
{
    /// <summary>
    /// The device a room asks for: same id first; if the id changed (driver reinstall), the only
    /// device with the same name.
    /// </summary>
    public static AudioDevice? Find(IReadOnlyList<AudioDevice> devices, AudioDeviceRef wanted)
    {
        var byId = devices.FirstOrDefault(d => string.Equals(d.Id, wanted.Id, StringComparison.OrdinalIgnoreCase));
        if (byId != null) return byId;
        var byName = devices.Where(d => string.Equals(d.Name, wanted.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        return byName.Count == 1 ? byName[0] : null;
    }

    public static AudioDeviceRef ToRef(AudioDevice device) => new() { Id = device.Id, Name = device.Name };

    /// <summary>
    /// Sets the room's sound output and microphone as the Windows defaults. A TV's HDMI sound only
    /// appears once its screen is on, so a device that isn't connected yet is waited for.
    /// Returns plain-language problems (empty when all went well).
    /// </summary>
    public static async Task<IReadOnlyList<string>> ApplyAsync(IAudioBackend audio, Room room,
        Func<int, CancellationToken, Task> delay, int waitMs = 6000, CancellationToken cancellationToken = default)
    {
        var problems = new List<string>();
        foreach (var (flow, wanted) in new[] { (AudioFlow.Output, room.AudioOutput), (AudioFlow.Input, room.AudioInput) })
        {
            if (wanted == null || string.IsNullOrEmpty(wanted.Id) && string.IsNullOrEmpty(wanted.Name)) continue;
            const int step = 500;
            AudioDevice? device = null;
            for (var waited = 0; ; waited += step)
            {
                device = Find(audio.List(flow), wanted);
                if (device is { Connected: true } || waited >= waitMs) break;
                await delay(step, cancellationToken);
            }

            var what = flow == AudioFlow.Output ? "play sound through" : "use the microphone";
            if (device == null)
                problems.Add($"Couldn't {what} {wanted.Name}. Windows can't find it any more.");
            else if (!device.Connected)
                problems.Add($"Couldn't {what} {wanted.Name}. It isn't connected.");
            else if (!device.IsDefault && !audio.SetDefault(device.Id))
                problems.Add($"Couldn't {what} {wanted.Name}. Windows didn't accept the change.");
        }
        return problems;
    }
}
