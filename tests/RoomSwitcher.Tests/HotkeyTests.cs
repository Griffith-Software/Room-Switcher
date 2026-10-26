using RoomSwitcher.Core.Hotkeys;
using RoomSwitcher.Core.Rooms;
using RoomSwitcher.Core.Storage;
using static RoomSwitcher.Tests.TestData;

namespace RoomSwitcher.Tests;

public sealed class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+1", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x31)]
    [InlineData("ctrl + alt + o", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x4F)]
    [InlineData("Win+Shift+F5", HotkeyModifiers.Win | HotkeyModifiers.Shift, 0x74)]
    [InlineData("Control+Windows+Num7", HotkeyModifiers.Ctrl | HotkeyModifiers.Win, 0x67)]
    [InlineData("Ctrl+Alt+=", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0xBB)]
    [InlineData("F13", HotkeyModifiers.None, 0x7C)]
    public void Parses(string text, HotkeyModifiers modifiers, int key)
    {
        Assert.True(Hotkey.TryParse(text, out var h));
        Assert.Equal(new Hotkey(modifiers, key), h);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+1")]
    [InlineData("Ctrl+Alt+Banana")]
    public void Rejects_nonsense(string text) => Assert.False(Hotkey.TryParse(text, out _));

    [Fact]
    public void Round_trips_in_a_fixed_order()
    {
        Assert.True(Hotkey.TryParse("Win+Alt+Shift+Ctrl+Left", out var h));
        Assert.Equal("Ctrl+Alt+Shift+Win+Left", h.ToString());
        Assert.Equal("Ctrl + Alt + Shift + Win + Left", h.Display);
    }

    [Theory]
    [InlineData("Ctrl+Alt+1", null)]
    [InlineData("F13", null)]
    [InlineData("A", "Add Ctrl, Alt or Win")]
    [InlineData("Shift+A", "Add Ctrl, Alt or Win")]
    [InlineData("F5", "Add Ctrl, Alt or Win")]
    [InlineData("Ctrl+Alt+Delete", "belongs to Windows")]
    public void Problems(string text, string? expected)
    {
        Hotkey.TryParse(text, out var h);
        var problem = h.Problem();
        if (expected == null) Assert.Null(problem);
        else Assert.Contains(expected, problem);
    }

    [Fact]
    public void Defaults_follow_room_order()
    {
        var config = Config();
        Assert.True(HotkeyRules.FillDefaults(config));
        Assert.Equal(new[] { "Ctrl+Alt+1", "Ctrl+Alt+2", "Ctrl+Alt+3" }, config.Rooms.Select(r => r.Hotkey));
        Assert.Equal("Ctrl+Alt+0", config.Settings.NextRoomHotkey);
        Assert.False(HotkeyRules.FillDefaults(config));
    }

    [Fact]
    public void Turned_off_shortcuts_stay_off()
    {
        var config = Config();
        config.Rooms[0].Hotkey = "";
        config.Settings.NextRoomHotkey = "";
        HotkeyRules.FillDefaults(config);
        Assert.Equal("", config.Rooms[0].Hotkey);
        Assert.Equal("", config.Settings.NextRoomHotkey);
        Assert.Equal("Ctrl+Alt+2", config.Rooms[1].Hotkey);
    }

    [Fact]
    public void Defaults_skip_taken_keys()
    {
        var config = Config();
        config.Rooms[0].Hotkey = "Ctrl+Alt+2";
        HotkeyRules.FillDefaults(config);
        Assert.Equal("Ctrl+Alt+1", config.Rooms[1].Hotkey); // its own Ctrl+Alt+2 is taken
        Assert.Equal("Ctrl+Alt+3", config.Rooms[2].Hotkey);
    }

    [Fact]
    public void Added_and_duplicated_rooms_get_a_free_shortcut()
    {
        var config = Config();
        HotkeyRules.FillDefaults(config);
        Assert.Equal("Ctrl+Alt+4", RoomEditor.AddRoom(config, "Everything").Hotkey);
        var copy = RoomEditor.Duplicate(config, config.Rooms[0]);
        Assert.Equal("Ctrl+Alt+5", copy.Hotkey);
        Assert.Equal("Ctrl+Alt+1", config.Rooms[0].Hotkey);
    }

    [Fact]
    public void Old_config_without_shortcuts_gets_defaults_on_load()
    {
        var config = JsonStore.DeserializeConfig("""{ "schema": 1, "rooms": [ { "id": "a", "name": "A" }, { "id": "b", "name": "B", "hotkey": "" } ] }""");
        Assert.Equal("Ctrl+Alt+1", config.Rooms[0].Hotkey);
        Assert.Equal("", config.Rooms[1].Hotkey);
        Assert.Equal("Ctrl+Alt+0", config.Settings.NextRoomHotkey);
    }

    [Fact]
    public void Clashes_are_reported_with_names()
    {
        var config = Config();
        HotkeyRules.FillDefaults(config);
        Assert.Null(HotkeyRules.Check(config, "Ctrl+Alt+1", "office"));
        Assert.Equal("Ctrl + Alt + 1 is also used by Office.", HotkeyRules.Check(config, "Ctrl+Alt+1", "living"));
        Assert.Equal("Ctrl + Alt + 0 is also used by the next room shortcut.", HotkeyRules.Check(config, "Ctrl+Alt+0", "living"));
        Assert.Null(HotkeyRules.Check(config, "Ctrl+Alt+0", HotkeyRules.NextRoomOwner));
        Assert.Null(HotkeyRules.Check(config, "", "living"));
        Assert.Contains("isn't a shortcut", HotkeyRules.Check(config, "Ctrl+Nope", "living"));
    }

    [Fact]
    public void Next_room_wraps_and_skips_unusable_rooms()
    {
        var rooms = Config().Rooms;
        Assert.Equal("office", HotkeyRules.NextRoom(rooms, null, _ => true)?.Id);
        Assert.Equal("living", HotkeyRules.NextRoom(rooms, rooms[0], _ => true)?.Id);
        Assert.Equal("office", HotkeyRules.NextRoom(rooms, rooms[2], _ => true)?.Id);
        Assert.Equal("present", HotkeyRules.NextRoom(rooms, rooms[0], r => r.Id != "living")?.Id);
        Assert.Null(HotkeyRules.NextRoom(rooms, rooms[0], r => r.Id == "office"));
    }
}
