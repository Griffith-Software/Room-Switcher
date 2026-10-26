using RoomSwitcher.Core.Audio;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Rooms;
using RoomSwitcher.Core.Storage;
using static RoomSwitcher.Tests.TestData;

namespace RoomSwitcher.Tests;

internal sealed class FakeAudio : IAudioBackend
{
    public List<AudioDevice> Devices { get; } = new();
    public List<string> Set { get; } = new();
    public bool Refuse { get; set; }
    /// <summary>Called on each List; lets a test plug a device in part-way through waiting.</summary>
    public Action<int>? OnList { get; set; }
    private int _lists;

    public IReadOnlyList<AudioDevice> List(AudioFlow flow)
    {
        OnList?.Invoke(++_lists);
        return Devices.Where(d => d.Flow == flow).ToList();
    }

    public bool SetDefault(string deviceId)
    {
        if (Refuse) return false;
        Set.Add(deviceId);
        var flow = Devices.First(d => d.Id == deviceId).Flow;
        for (var i = 0; i < Devices.Count; i++)
            if (Devices[i].Flow == flow) Devices[i] = Devices[i] with { IsDefault = Devices[i].Id == deviceId };
        return true;
    }

    public static AudioDevice Speaker(string id, string name, bool connected = true, bool isDefault = false) =>
        new(id, name, AudioFlow.Output, connected, isDefault);

    public static AudioDevice Mic(string id, string name, bool connected = true, bool isDefault = false) =>
        new(id, name, AudioFlow.Input, connected, isDefault);
}

public sealed class AudioTests
{
    private static readonly Func<int, CancellationToken, Task> NoDelay = (_, _) => Task.CompletedTask;

    private static Room RoomWith(AudioDeviceRef? output, AudioDeviceRef? input = null) =>
        new() { Name = "Den", AudioOutput = output, AudioInput = input };

    [Fact]
    public async Task Sets_output_and_input()
    {
        var audio = new FakeAudio();
        audio.Devices.Add(FakeAudio.Speaker("desk", "Desk speakers", isDefault: true));
        audio.Devices.Add(FakeAudio.Speaker("tv", "LG TV"));
        audio.Devices.Add(FakeAudio.Mic("cam", "Webcam mic"));
        var problems = await AudioRules.ApplyAsync(audio, RoomWith(new() { Id = "tv", Name = "LG TV" }, new() { Id = "cam", Name = "Webcam mic" }), NoDelay);
        Assert.Empty(problems);
        Assert.Equal(new[] { "tv", "cam" }, audio.Set);
    }

    [Fact]
    public async Task Leaves_sound_alone_when_room_has_none_or_device_already_default()
    {
        var audio = new FakeAudio();
        audio.Devices.Add(FakeAudio.Speaker("desk", "Desk speakers", isDefault: true));
        Assert.Empty(await AudioRules.ApplyAsync(audio, RoomWith(null), NoDelay));
        Assert.Empty(await AudioRules.ApplyAsync(audio, RoomWith(new() { Id = "desk", Name = "Desk speakers" }), NoDelay));
        Assert.Empty(audio.Set);
    }

    [Fact]
    public async Task Waits_for_a_tv_that_comes_on_with_its_screen()
    {
        var audio = new FakeAudio();
        audio.Devices.Add(FakeAudio.Speaker("tv", "LG TV", connected: false));
        audio.OnList = n => { if (n == 3) audio.Devices[0] = audio.Devices[0] with { Connected = true }; };
        var waits = 0;
        var problems = await AudioRules.ApplyAsync(audio, RoomWith(new() { Id = "tv", Name = "LG TV" }), (_, _) => { waits++; return Task.CompletedTask; });
        Assert.Empty(problems);
        Assert.Equal(2, waits);
        Assert.Equal(new[] { "tv" }, audio.Set);
    }

    [Fact]
    public async Task Gives_up_on_a_device_that_never_connects()
    {
        var audio = new FakeAudio();
        audio.Devices.Add(FakeAudio.Speaker("tv", "LG TV", connected: false));
        var problems = await AudioRules.ApplyAsync(audio, RoomWith(new() { Id = "tv", Name = "LG TV" }), NoDelay, waitMs: 1000);
        Assert.Equal("Couldn't play sound through LG TV. It isn't connected.", Assert.Single(problems));
        Assert.Empty(audio.Set);
    }

    [Fact]
    public async Task Reports_missing_and_refused_devices()
    {
        var audio = new FakeAudio { Refuse = true };
        audio.Devices.Add(FakeAudio.Mic("cam", "Webcam mic"));
        var problems = await AudioRules.ApplyAsync(audio,
            RoomWith(new() { Id = "gone", Name = "Old headset" }, new() { Id = "cam", Name = "Webcam mic" }), NoDelay, waitMs: 0);
        Assert.Equal(2, problems.Count);
        Assert.Contains("Windows can't find it", problems[0]);
        Assert.Contains("didn't accept", problems[1]);
    }

    [Fact]
    public void Finds_by_id_then_by_unique_name()
    {
        var devices = new List<AudioDevice> { FakeAudio.Speaker("new-id", "LG TV"), FakeAudio.Speaker("a", "Speakers"), FakeAudio.Speaker("b", "Speakers") };
        Assert.Equal("new-id", AudioRules.Find(devices, new() { Id = "old-id", Name = "LG TV" })?.Id);
        Assert.Null(AudioRules.Find(devices, new() { Id = "old", Name = "Speakers" })); // two with that name: don't guess
        Assert.Equal("b", AudioRules.Find(devices, new() { Id = "b", Name = "Speakers" })?.Id);
    }

    [Fact]
    public async Task Switching_rooms_sets_sound_after_the_screens()
    {
        var config = Config();
        config.Rooms[1].AudioOutput = new() { Id = "tv", Name = "LG TV" };
        var display = new FakeDisplay(OfficeState());
        var audio = new FakeAudio();
        audio.Devices.Add(FakeAudio.Speaker("desk", "Desk speakers", isDefault: true));
        audio.Devices.Add(FakeAudio.Speaker("tv", "LG TV", connected: false));
        display.OnApplied = () => audio.Devices[1] = audio.Devices[1] with { Connected = true }; // TV sound appears with its screen
        var service = new RoomSwitchService(config, new LayoutsFile(), display, new FakeWindows(), new NoWaitSettle(), _ => { }, audio, NoDelay);

        var result = await service.SwitchToAsync(config.Rooms[1]);
        Assert.True(result.Ok);
        Assert.False(result.Warning);
        Assert.Equal(new[] { "tv" }, audio.Set);

        // Back in the Office (no sound set): sound is left alone.
        await service.SwitchToAsync(config.Rooms[0]);
        Assert.Single(audio.Set);
    }

    [Fact]
    public async Task Already_on_room_still_fixes_its_sound_and_warns_on_problems()
    {
        var config = Config();
        config.Rooms[0].AudioOutput = new() { Id = "desk", Name = "Desk speakers" };
        config.Rooms[0].AudioInput = new() { Id = "gone", Name = "Old headset" };
        var audio = new FakeAudio();
        audio.Devices.Add(FakeAudio.Speaker("desk", "Desk speakers"));
        audio.Devices.Add(FakeAudio.Speaker("tv", "LG TV", isDefault: true));
        var service = new RoomSwitchService(config, new LayoutsFile(), new FakeDisplay(OfficeState()), new FakeWindows(), new NoWaitSettle(), _ => { }, audio, NoDelay);

        var result = await service.SwitchToAsync(config.Rooms[0]);
        Assert.True(result.Ok);
        Assert.True(result.Warning);
        Assert.Contains("already on", result.Message);
        Assert.Contains("Old headset", result.Message);
        Assert.Equal(new[] { "desk" }, audio.Set);
    }

    [Fact]
    public void Sound_settings_survive_save_and_duplicate()
    {
        var config = Config();
        config.Rooms[1].AudioOutput = new() { Id = "{0.0.0.00000000}.{abc}", Name = "LG TV (NVIDIA High Definition Audio)" };
        var loaded = JsonStore.DeserializeConfig(JsonStore.Serialize(config));
        Assert.Equal("LG TV (NVIDIA High Definition Audio)", loaded.Rooms[1].AudioOutput!.Name);
        Assert.Null(loaded.Rooms[1].AudioInput);
        Assert.Null(loaded.Rooms[0].AudioOutput);

        var copy = RoomEditor.Duplicate(config, config.Rooms[1]);
        Assert.Equal("{0.0.0.00000000}.{abc}", copy.AudioOutput!.Id);
        Assert.NotSame(config.Rooms[1].AudioOutput, copy.AudioOutput);
    }
}
