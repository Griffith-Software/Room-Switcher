using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Storage;

namespace RoomSwitcher.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rs-tests-" + Guid.NewGuid().ToString("N"));

    public StorageTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Config_round_trips()
    {
        var path = Path.Combine(_dir, "config.json");
        var config = TestData.Config();
        config.Settings.SettleDelayMs = 2500;
        JsonStore.SaveConfig(path, config);

        var loaded = JsonStore.LoadConfig(path);
        Assert.Equal(3, loaded.Monitors.Count);
        Assert.Equal("Office", loaded.Rooms[0].Name);
        Assert.Equal(new[] { "left", "tv" }, loaded.Rooms[2].Screens[0].MonitorIds);
        Assert.Equal(2500, loaded.Settings.SettleDelayMs);
        Assert.Contains("\"refreshMillihz\": 60000", File.ReadAllText(path));
    }

    [Fact]
    public void Unknown_fields_are_kept_on_save()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, """
            {
              "schema": 1,
              "futureTopLevel": { "a": 1 },
              "monitors": [ { "id": "m1", "devicePath": "p", "name": "TV", "ddcInput": 17 } ],
              "rooms": [ { "id": "r1", "name": "Den", "hotkey": "Ctrl+1",
                           "screens": [ { "id": "s1", "monitorIds": ["m1"], "audio": "Speakers" } ] } ],
              "settings": { "restoreWindows": false, "theme": "dark" }
            }
            """);

        var config = JsonStore.LoadConfig(path);
        config.Rooms[0].Name = "Den 2";
        JsonStore.SaveConfig(path, config);
        var text = File.ReadAllText(path);

        Assert.Contains("futureTopLevel", text);
        Assert.Contains("\"ddcInput\": 17", text);
        Assert.Contains("\"hotkey\": \"Ctrl+1\"", text);
        Assert.Contains("\"audio\": \"Speakers\"", text);
        Assert.Contains("\"theme\": \"dark\"", text);
        Assert.Contains("Den 2", text);
        Assert.False(JsonStore.LoadConfig(path).Settings.RestoreWindows);
    }

    [Fact]
    public void Saving_keeps_the_previous_config()
    {
        var path = Path.Combine(_dir, "config.json");
        var config = TestData.Config();
        JsonStore.SaveConfig(path, config);
        var rooms = config.Rooms.Count;
        config.Rooms.Clear();
        JsonStore.SaveConfig(path, config);

        Assert.Empty(JsonStore.LoadConfig(path).Rooms);
        Assert.Equal(rooms, JsonStore.LoadConfig(JsonStore.PreviousPath(path)).Rooms.Count);
        Assert.Equal(Path.Combine(_dir, "config.previous.json"), JsonStore.PreviousPath(path));
    }

    [Fact]
    public void Newer_schema_is_refused()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, """{ "schema": 2 }""");
        Assert.Throws<InvalidDataException>(() => JsonStore.LoadConfig(path));
    }

    [Fact]
    public void Missing_files_give_empty_defaults()
    {
        var config = JsonStore.LoadConfig(Path.Combine(_dir, "nope.json"));
        Assert.Empty(config.Rooms);
        Assert.True(config.Settings.RestoreWindows);
        Assert.Empty(JsonStore.LoadLayouts(Path.Combine(_dir, "nope2.json")).Rooms);
    }

    [Fact]
    public void Damaged_layouts_file_does_not_throw()
    {
        var path = Path.Combine(_dir, "layouts.json");
        File.WriteAllText(path, "{ not json");
        Assert.Empty(JsonStore.LoadLayouts(path).Rooms);
    }

    [Fact]
    public void Atomic_write_replaces_and_leaves_no_temp_files()
    {
        var path = Path.Combine(_dir, "layouts.json");
        var layouts = new LayoutsFile();
        layouts.Rooms["office"] = new RoomLayout { SavedAt = DateTimeOffset.Now, Windows = { FakeWindows.Window(42, "notepad.exe", "Notes", 100, 100) } };
        JsonStore.SaveLayouts(path, layouts);
        layouts.LastRoomId = "office";
        JsonStore.SaveLayouts(path, layouts);

        Assert.Single(Directory.GetFiles(_dir));
        var loaded = JsonStore.LoadLayouts(path);
        Assert.Equal("office", loaded.LastRoomId);
        Assert.Equal(42, loaded.Rooms["office"].Windows[0].Hwnd);
        Assert.Equal(900, loaded.Rooms["office"].Windows[0].NormalRect.Right);
    }

    [Fact]
    public void Portable_mode_when_config_sits_next_to_exe()
    {
        var exeDir = Path.Combine(_dir, "usb");
        var appData = Path.Combine(_dir, "appdata");
        Directory.CreateDirectory(exeDir);

        var installed = AppPaths.Resolve(exeDir, appData);
        Assert.False(installed.Portable);
        Assert.Equal(Path.Combine(appData, "Room Switcher", "config.json"), installed.ConfigPath);

        File.WriteAllText(Path.Combine(exeDir, "config.json"), "{}");
        var portable = AppPaths.Resolve(exeDir, appData);
        Assert.True(portable.Portable);
        Assert.Equal(Path.Combine(exeDir, "layouts.json"), portable.LayoutsPath);
    }

    [Fact]
    public void Clone_is_independent()
    {
        var config = TestData.Config();
        var copy = JsonStore.Clone(config);
        copy.Rooms[0].Name = "Changed";
        copy.Rooms[0].Screens[0].MonitorIds.Add("tv");
        Assert.Equal("Office", config.Rooms[0].Name);
        Assert.Single(config.Rooms[0].Screens[0].MonitorIds);
    }
}
