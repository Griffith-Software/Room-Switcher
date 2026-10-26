using RoomSwitcher.Core.Devices;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Rooms;
using RoomSwitcher.Core.Storage;
using static RoomSwitcher.Tests.TestData;

namespace RoomSwitcher.Tests;

internal sealed class FakeDevices : IDeviceBackend
{
    public List<UsbDevice> Present { get; } = new();
    public DeviceControlStatus Status { get; set; } = DeviceControlStatus.Ready;
    public List<(IReadOnlyList<string> Enable, IReadOnlyList<string> Disable)> Calls { get; } = new();
    public List<string> Problems { get; } = new();

    public IReadOnlyList<UsbDevice> List() => Present.ToList();

    public Task<IReadOnlyList<string>> ApplyAsync(IReadOnlyList<string> enable, IReadOnlyList<string> disable, CancellationToken cancellationToken = default)
    {
        Calls.Add((enable, disable));
        for (var i = 0; i < Present.Count; i++)
        {
            if (enable.Contains(Present[i].Id)) Present[i] = Present[i] with { Enabled = true };
            if (disable.Contains(Present[i].Id)) Present[i] = Present[i] with { Enabled = false };
        }
        return Task.FromResult<IReadOnlyList<string>>(Problems.ToList());
    }
}

public sealed class UsbTests
{
    private static UsbDevice Dev(string id, UsbKind kind, bool enabled = true) => new(id, id + " (Windows)", kind, "USB", enabled);

    private static AppConfig ConfigWith(params (string Id, bool Managed)[] devices)
    {
        var config = Config();
        foreach (var (id, managed) in devices)
            config.UsbDevices.Add(new UsbDeviceInfo { Id = id, WindowsName = id + " (Windows)", Managed = managed });
        return config;
    }

    [Fact]
    public void Off_in_room_turns_managed_devices_off_and_others_on()
    {
        var config = ConfigWith(("cam", true), ("desk-kb", true), ("tv-kb", true));
        var room = config.Rooms[0];
        room.UsbOff.Add("cam");
        room.UsbOff.Add("tv-kb");
        var present = new List<UsbDevice> { Dev("cam", UsbKind.Camera), Dev("desk-kb", UsbKind.Keyboard, enabled: false), Dev("tv-kb", UsbKind.Keyboard) };

        var plan = UsbRules.Plan(config, room, present);
        Assert.Equal(new[] { "desk-kb" }, plan.Enable);
        Assert.Equal(new[] { "cam", "tv-kb" }, plan.Disable);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Unmanaged_devices_are_never_touched_even_if_off_in_room()
    {
        var config = ConfigWith(("cam", false));
        config.Rooms[0].UsbOff.Add("cam");
        var plan = UsbRules.Plan(config, config.Rooms[0], [Dev("cam", UsbKind.Camera)]);
        Assert.True(plan.Empty);
    }

    [Fact]
    public void Devices_not_plugged_in_are_skipped()
    {
        var config = ConfigWith(("cam", true));
        config.Rooms[0].UsbOff.Add("cam");
        Assert.True(UsbRules.Plan(config, config.Rooms[0], []).Empty);
    }

    [Fact]
    public void Last_working_keyboard_and_mouse_stay_on()
    {
        var config = ConfigWith(("receiver", true), ("mouse2", true));
        config.UsbDevices[0].Name = "Desk receiver";
        var room = config.Rooms[0];
        room.UsbOff.Add("receiver");
        room.UsbOff.Add("mouse2");
        var present = new List<UsbDevice> { Dev("receiver", UsbKind.Keyboard | UsbKind.Mouse), Dev("mouse2", UsbKind.Mouse) };

        var plan = UsbRules.Plan(config, room, present);
        // The receiver stays on for the keyboard, and it's also a mouse, so the second mouse can still go off.
        Assert.Equal(new[] { "mouse2" }, plan.Disable);
        Assert.Equal("Kept Desk receiver on because it's the only working keyboard.", Assert.Single(plan.Warnings));

        // A lone mouse is kept on too.
        var mouseOnly = UsbRules.Plan(config, room, [Dev("mouse2", UsbKind.Mouse), Dev("kb", UsbKind.Keyboard)]);
        Assert.Empty(mouseOnly.Disable);
        Assert.Contains("only working mouse", Assert.Single(mouseOnly.Warnings));
    }

    [Fact]
    public void A_keyboard_can_go_off_when_another_one_works()
    {
        var config = ConfigWith(("tv-kb", true));
        config.Rooms[0].UsbOff.Add("tv-kb");
        var present = new List<UsbDevice> { Dev("tv-kb", UsbKind.Keyboard), Dev("laptop-kb", UsbKind.Keyboard) }; // second one isn't managed
        var plan = UsbRules.Plan(config, config.Rooms[0], present);
        Assert.Equal(new[] { "tv-kb" }, plan.Disable);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void A_keyboard_turning_on_counts_as_working()
    {
        var config = ConfigWith(("desk-kb", true), ("tv-kb", true));
        config.Rooms[0].UsbOff.Add("tv-kb");
        var present = new List<UsbDevice> { Dev("desk-kb", UsbKind.Keyboard, enabled: false), Dev("tv-kb", UsbKind.Keyboard) };
        var plan = UsbRules.Plan(config, config.Rooms[0], present);
        Assert.Equal(new[] { "desk-kb" }, plan.Enable);
        Assert.Equal(new[] { "tv-kb" }, plan.Disable);
    }

    [Fact]
    public async Task Switching_applies_the_plan_and_reports_setup_needed()
    {
        var config = ConfigWith(("cam", true));
        config.Rooms[1].UsbOff.Add("cam");
        var devices = new FakeDevices { Status = DeviceControlStatus.NotSetUp };
        devices.Present.Add(Dev("cam", UsbKind.Camera));
        var service = new RoomSwitchService(config, new LayoutsFile(), new FakeDisplay(OfficeState()), new FakeWindows(), new NoWaitSettle(), _ => { },
            devices: devices);

        var result = await service.SwitchToAsync(config.Rooms[1]);
        Assert.True(result.Warning);
        Assert.Contains("isn't set up", result.Message);
        Assert.Empty(devices.Calls);

        devices.Status = DeviceControlStatus.Ready;
        result = await service.SwitchToAsync(config.Rooms[0]); // cam on in Office: nothing to do (still on)
        Assert.Empty(devices.Calls);
        result = await service.SwitchToAsync(config.Rooms[1]);
        Assert.False(result.Warning, result.Message);
        Assert.Equal(new[] { "cam" }, Assert.Single(devices.Calls).Disable);
        Assert.False(devices.Present[0].Enabled);

        await service.SwitchToAsync(config.Rooms[0]);
        Assert.Equal(new[] { "cam" }, devices.Calls[1].Enable);
    }

    [Fact]
    public void Entry_records_names_and_settings_round_trip()
    {
        var config = Config();
        var info = UsbRules.Entry(config, new UsbDevice("{abc}", "USB Receiver", UsbKind.Keyboard | UsbKind.Mouse, "USB", true));
        info.Name = "Desk receiver";
        info.Managed = true;
        config.Rooms[1].UsbOff.Add("{abc}");
        config.AudioDevices.Add(new AudioDeviceName { Id = "tv", WindowsName = "LG TV", Name = "Living room TV" });

        var loaded = JsonStore.DeserializeConfig(JsonStore.Serialize(config));
        Assert.Equal("Keyboard + Mouse", loaded.UsbDevices[0].Kind);
        Assert.Equal("Desk receiver", loaded.UsbDevices[0].DisplayName);
        Assert.True(loaded.UsbDevices[0].Managed);
        Assert.Equal(new[] { "{abc}" }, loaded.Rooms[1].UsbOff);
        Assert.Equal("Living room TV", loaded.AudioDevices[0].Name);
        Assert.DoesNotContain("displayName", JsonStore.Serialize(config));
        Assert.Same(info, UsbRules.Entry(config, new UsbDevice("{ABC}", "USB Receiver", UsbKind.Keyboard, "USB", true)));
    }

    [Fact]
    public void Helper_messages_round_trip()
    {
        var request = new DeviceRequest { Enable = { "a" }, Disable = { "b", "c" } };
        var back = DeviceMessages.ReadRequest(DeviceMessages.Write(request))!;
        Assert.Equal(request.RequestId, back.RequestId);
        Assert.Equal(new[] { "b", "c" }, back.Disable);
        var response = DeviceMessages.ReadResponse(DeviceMessages.Write(new DeviceResponse { RequestId = "x", Problems = { "p" } }))!;
        Assert.Equal("p", Assert.Single(response.Problems));
    }

    [Fact]
    public void Duplicate_copies_the_usb_off_list()
    {
        var config = Config();
        config.Rooms[0].UsbOff.Add("cam");
        var copy = RoomEditor.Duplicate(config, config.Rooms[0]);
        Assert.Equal(new[] { "cam" }, copy.UsbOff);
        Assert.NotSame(config.Rooms[0].UsbOff, copy.UsbOff);
    }

    [Fact]
    public void Chosen_type_overrides_what_Windows_reports()
    {
        var config = ConfigWith(("rx", false));
        var receiver = Dev("rx", UsbKind.Keyboard | UsbKind.Mouse);
        Assert.Equal(UsbKind.Keyboard | UsbKind.Mouse, UsbRules.Kinds(config, receiver));
        config.UsbDevices[0].UserKind = "Mouse";
        Assert.Equal(UsbKind.Mouse, UsbRules.Kinds(config, receiver));
        Assert.Equal(UsbKind.Keyboard | UsbKind.Mouse, UsbRules.ParseKind(UsbRules.KindText(UsbKind.Keyboard | UsbKind.Mouse)));
        Assert.Equal(UsbKind.Other, UsbRules.ParseKind("Other"));
    }

    [Fact]
    public void Rooms_list_switched_devices_that_are_on_and_ticked_ones()
    {
        var config = ConfigWith(("kbd", true), ("cam", false));
        var room = config.Rooms[0];
        Assert.Equal(new[] { "kbd" }, UsbRules.DevicesIn(config, room));

        UsbRules.SetInRoom(config, room, "cam", true);
        UsbRules.SetInRoom(config, room, "kbd", false);
        Assert.Equal(new[] { "cam" }, UsbRules.DevicesIn(config, room));
        Assert.Contains("kbd", room.UsbOff);
        Assert.DoesNotContain("kbd", room.Devices);
    }

    [Fact]
    public void Switching_on_or_off_keeps_which_rooms_a_device_is_in()
    {
        var config = ConfigWith(("mouse", false));
        var (first, second) = (config.Rooms[0], config.Rooms[1]);
        UsbRules.SetInRoom(config, first, "mouse", true);

        UsbRules.SetManaged(config, config.UsbDevices[0], true);
        Assert.Empty(first.UsbOff);
        Assert.Contains("mouse", second.UsbOff);
        Assert.Empty(first.Devices);

        UsbRules.SetManaged(config, config.UsbDevices[0], false);
        Assert.Equal(new[] { "mouse" }, first.Devices);
        Assert.Empty(second.Devices);
        Assert.Empty(second.UsbOff);
    }
}
