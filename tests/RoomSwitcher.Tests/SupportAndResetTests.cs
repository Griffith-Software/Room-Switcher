using System.Text.RegularExpressions;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Rooms;
using RoomSwitcher.Core.Support;
using static RoomSwitcher.Tests.TestData;

namespace RoomSwitcher.Tests;

public sealed class SupportAndResetTests
{
    [Fact]
    public void Support_goes_to_the_griffith_software_address_for_this_program()
    {
        Assert.Equal("https://griffith.software/support?app=room-switcher", SupportLink.Url);
        Assert.StartsWith("https://", SupportLink.Url);
        Assert.Matches(new Regex("^[a-z0-9]+(-[a-z0-9]+)*$"), SupportLink.Slug);
    }

    [Fact]
    public void Resetting_rooms_keeps_nicknames_and_options()
    {
        var config = Config();
        config.Monitors[0].Name = "Desk";
        config.AudioDevices.Add(new AudioDeviceName { Id = "spk", WindowsName = "Speakers (USB)", Name = "Desk speakers" });
        config.UsbDevices.Add(new UsbDeviceInfo { Id = "mouse", WindowsName = "USB Receiver", Name = "Desk mouse", UserKind = "Mouse", Managed = true });
        config.Settings.StartWithWindows = true;
        config.Rooms[0].UsbOff.Add("mouse");

        var reset = ResetRules.WithoutRooms(config);

        Assert.Empty(reset.Rooms);
        Assert.Equal("Desk", reset.Monitors[0].Name);
        Assert.Equal("Desk speakers", reset.AudioDevices.Single().Name);
        var mouse = reset.UsbDevices.Single();
        Assert.Equal(("Desk mouse", "Mouse", false), (mouse.Name, mouse.UserKind, mouse.Managed));
        Assert.True(reset.Settings.StartWithWindows);
        Assert.NotEmpty(config.Rooms); // the original is left alone
    }

    [Fact]
    public void Resetting_everything_starts_from_scratch()
    {
        var fresh = ResetRules.Fresh();
        Assert.Empty(fresh.Rooms);
        Assert.Empty(fresh.Monitors);
        Assert.Empty(fresh.AudioDevices);
        Assert.Empty(fresh.UsbDevices);
        Assert.False(fresh.Settings.StartWithWindows);
    }
}
