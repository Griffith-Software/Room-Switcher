using RoomSwitcher.Core.Devices;
using RoomSwitcher.Core.Display;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Storage;
using static RoomSwitcher.Core.Display.Arrangement;

namespace RoomSwitcher.Tests;

public sealed class ArrangementTests
{
    private static readonly Box A = new(0, 0, 1920, 1080);

    [Fact]
    public void Side_by_side_lines_screens_up()
    {
        var boxes = SideBySide([(1920, 1080), (2560, 1440)]);
        Assert.Equal(new Box(1920, 0, 2560, 1440), boxes[1]);
        Assert.True(IsValid(boxes));
    }

    [Fact]
    public void Dropped_near_the_right_edge_snaps_to_it_and_lines_up_tops()
    {
        var screens = new List<Box> { A, new(5000, 0, 1920, 1080) };
        var snapped = Snap(screens, 1, new Box(1950, 40, 1920, 1080));
        Assert.Equal(new Box(1920, 0, 1920, 1080), snapped);
    }

    [Fact]
    public void Dropped_below_snaps_underneath()
    {
        var screens = new List<Box> { A, new(0, 0, 1920, 1080) };
        var snapped = Snap(screens, 1, new Box(30, 1200, 1920, 1080));
        Assert.Equal(new Box(0, 1080, 1920, 1080), snapped);
    }

    [Fact]
    public void Dropped_on_top_of_another_moves_to_the_nearest_free_edge()
    {
        var screens = new List<Box> { A, new(0, 0, 1920, 1080) };
        var snapped = Snap(screens, 1, new Box(1500, 100, 1920, 1080));
        Assert.False(snapped.Overlaps(A));
        Assert.True(snapped.Touches(A));
        Assert.Equal(1920, snapped.X);
    }

    [Fact]
    public void A_smaller_screen_keeps_its_offset_when_not_near_an_edge()
    {
        var screens = new List<Box> { new(0, 0, 3840, 2160), new(0, 0, 1920, 1080) };
        var snapped = Snap(screens, 1, new Box(3900, 300, 1920, 1080));
        Assert.Equal(new Box(3840, 300, 1920, 1080), snapped);
        // Within reach of the centre line it centres.
        Assert.Equal(540, Snap(screens, 1, new Box(3900, 500, 1920, 1080)).Y);
    }

    [Fact]
    public void Lone_screen_stays_put()
    {
        Assert.Equal(new Box(10, 10, 1920, 1080), Snap([A], 0, new Box(10, 10, 1920, 1080)));
    }

    [Fact]
    public void Valid_means_joined_and_not_overlapping()
    {
        Assert.True(IsValid([A, new(1920, 0, 1920, 1080), new(0, 1080, 1920, 1080)]));
        Assert.False(IsValid([A, new(1000, 0, 1920, 1080)]));          // overlap
        Assert.False(IsValid([A, new(2000, 0, 1920, 1080)]));          // gap
        Assert.False(IsValid([A, new(1920, 1080, 1920, 1080)]));       // corners only
    }

    [Fact]
    public void Resizing_keeps_neighbours_attached()
    {
        var screens = new List<Box> { A, new(1920, 0, 1920, 1080) };
        var bigger = Resize(screens, 0, 2560, 1440);
        Assert.True(IsValid(bigger));
        Assert.Equal(new Box(0, 0, 2560, 1440), bigger[0]);
        Assert.Equal(2560, bigger[1].X);

        var portrait = Resize(screens, 1, 1080, 1920);
        Assert.True(IsValid(portrait));
        Assert.Equal((1080, 1920), (portrait[1].Width, portrait[1].Height));
    }

    [Fact]
    public void Modes_sizes_and_rates()
    {
        var lg = new List<DisplayMode> { new(1920, 1080, 60000), new(1920, 1080, 75000), new(1280, 720, 60000) };
        var tv = new List<DisplayMode> { new(3840, 2160, 60000), new(1920, 1080, 60000), new(1280, 720, 60000) };
        Assert.Equal(new[] { new DisplayMode(1920, 1080, 60000), new DisplayMode(1280, 720, 60000) }, Common([lg, tv]));
        Assert.Equal(new[] { (1920, 1080), (1280, 720) }, Sizes(lg));
        Assert.Equal(new[] { 75000, 60000 }, Rates(lg, 1920, 1080));
        Assert.Equal((1080, 1920), new DisplayMode(1920, 1080, 60000).Rotated(90));
    }

    [Fact]
    public void Known_modes_are_saved_with_the_monitor()
    {
        var config = new AppConfig();
        config.Monitors.Add(new MonitorInfo { Id = "m", Name = "Office Main", KnownModes = [new(1920, 1080, 60000)] });
        var json = JsonStore.Serialize(config);
        Assert.DoesNotContain("sizeText", json);
        Assert.Equal(1920, JsonStore.DeserializeConfig(json).Monitors[0].KnownModes![0].Width);
    }

    [Fact]
    public void Away_bluetooth_devices_are_never_switched_or_counted()
    {
        var config = TestData.Config();
        config.UsbDevices.Add(new UsbDeviceInfo { Id = "bt-kb", Managed = true });
        config.UsbDevices.Add(new UsbDeviceInfo { Id = "usb-kb", Managed = true });
        config.Rooms[0].UsbOff.Add("bt-kb");
        config.Rooms[0].UsbOff.Add("usb-kb");
        var present = new List<UsbDevice>
        {
            new("bt-kb", "MX Keys", UsbKind.Keyboard, "Bluetooth", Enabled: true, Connected: false),
            new("usb-kb", "USB Keyboard", UsbKind.Keyboard, "USB", Enabled: true),
        };
        var plan = UsbRules.Plan(config, config.Rooms[0], present);
        // The away Bluetooth keyboard isn't a working keyboard, so the USB one must stay on.
        Assert.Empty(plan.Disable);
        Assert.Contains("only working keyboard", Assert.Single(plan.Warnings));
    }
}
