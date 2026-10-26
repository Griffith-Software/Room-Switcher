using RoomSwitcher.Core.Cli;
using RoomSwitcher.Core.Layouts;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Rooms;
using static RoomSwitcher.Tests.TestData;

namespace RoomSwitcher.Tests;

public sealed class WindowMatcherTests
{
    [Fact]
    public void Handle_first_then_title_then_program()
    {
        var saved = new List<SavedWindow>
        {
            FakeWindows.Window(1, "code.exe", "Project A", 0, 0),
            FakeWindows.Window(2, "chrome.exe", "Inbox", 100, 0),
            FakeWindows.Window(3, "chrome.exe", "News", 200, 0),
        };
        var live = new List<SavedWindow>
        {
            FakeWindows.Window(10, "chrome.exe", "Weather", 0, 0),   // title changed
            FakeWindows.Window(11, "chrome.exe", "Inbox", 0, 0),     // new handle, same title
            FakeWindows.Window(1, "code.exe", "Project A - edited", 0, 0),
        };
        var matches = WindowMatcher.MatchAll(saved, live);
        Assert.Equal(3, matches.Count);
        Assert.Equal(1, matches.Single(m => m.Saved.Hwnd == 1).Live.Hwnd);
        Assert.Equal(11, matches.Single(m => m.Saved.Hwnd == 2).Live.Hwnd);
        Assert.Equal(10, matches.Single(m => m.Saved.Hwnd == 3).Live.Hwnd);
    }

    [Fact]
    public void Reused_handle_from_another_program_is_not_trusted()
    {
        var saved = new List<SavedWindow> { FakeWindows.Window(5, "notepad.exe", "a", 0, 0) };
        var live = new List<SavedWindow> { FakeWindows.Window(5, "paint.exe", "b", 0, 0) };
        Assert.Empty(WindowMatcher.MatchAll(saved, live));
    }

    [Fact]
    public void Each_live_window_is_used_once()
    {
        var saved = new List<SavedWindow> { FakeWindows.Window(1, "x.exe", "t", 0, 0), FakeWindows.Window(2, "x.exe", "t", 50, 0) };
        var live = new List<SavedWindow> { FakeWindows.Window(9, "x.exe", "t", 0, 0) };
        Assert.Single(WindowMatcher.MatchAll(saved, live));
    }
}

public sealed class SwitchServiceTests
{
    private readonly AppConfig _config = Config();
    private readonly FakeDisplay _display = new(OfficeState());
    private readonly FakeWindows _windows = new();
    private readonly NoWaitSettle _settle = new();
    private int _layoutSaves;
    private readonly RoomSwitchService _service;

    public SwitchServiceTests()
    {
        _service = new RoomSwitchService(_config, new LayoutsFile(), _display, _windows, _settle, _ => _layoutSaves++);
        // Whenever monitors change, Windows piles windows onto the main display.
        _display.OnApplied = _windows.PileOntoPrimary;
    }

    private Room Office => _config.Rooms[0];
    private Room Living => _config.Rooms[1];

    [Fact]
    public async Task Switch_saves_leaving_room_and_restores_target_room()
    {
        _windows.Open.Add(FakeWindows.Window(1, "code.exe", "Editor", 2000, 100));
        _windows.Open.Add(FakeWindows.Window(2, "chrome.exe", "Docs", 50, 50, showCmd: 3));

        var toLiving = await _service.SwitchToAsync(Living);
        Assert.True(toLiving.Ok, toLiving.Message);
        Assert.Equal("living", _service.DetectCurrentRoom()?.Id);
        Assert.Equal(2, _service.Layouts.Rooms["office"].Windows.Count);
        Assert.Equal(1, _settle.Waits);

        var back = await _service.SwitchToAsync(Office);
        Assert.True(back.Ok, back.Message);
        Assert.Equal(2000, _windows.Open[0].NormalRect.Left);
        Assert.Equal(3, _windows.Open[1].ShowCmd);
        Assert.Equal("office", _service.Layouts.LastRoomId);
        Assert.True(_layoutSaves > 0);
    }

    [Fact]
    public async Task Back_and_forth_does_not_drift()
    {
        _windows.Open.Add(FakeWindows.Window(1, "code.exe", "Editor", 2000, 100));
        _windows.Open.Add(FakeWindows.Window(2, "notepad.exe", "Notes", 300, 400));
        for (var i = 0; i < 10; i++)
        {
            Assert.True((await _service.SwitchToAsync(Living)).Ok);
            Assert.True((await _service.SwitchToAsync(Office)).Ok);
        }
        Assert.Equal((2000, 100, 2800, 700), (_windows.Open[0].NormalRect.Left, _windows.Open[0].NormalRect.Top, _windows.Open[0].NormalRect.Right, _windows.Open[0].NormalRect.Bottom));
        Assert.Equal((300, 400), (_windows.Open[1].NormalRect.Left, _windows.Open[1].NormalRect.Top));
    }

    [Fact]
    public async Task Windows_without_a_saved_entry_are_left_alone()
    {
        await _service.SwitchToAsync(Living);
        _windows.Open.Add(FakeWindows.Window(7, "new.exe", "Opened in living room", 10, 10));
        await _service.SwitchToAsync(Office);
        Assert.Equal(0, _windows.ApplyCount);
    }

    [Fact]
    public async Task Failed_apply_rolls_back_with_plain_message()
    {
        _display.ApplyError = "The graphics driver refused this combination.";
        var result = await _service.SwitchToAsync(Living);
        Assert.False(result.Ok);
        Assert.Contains("refused", result.Message);
        Assert.Contains("previous display settings have been restored", result.Message);
        Assert.Contains("restore", _display.Calls);
        Assert.Equal("office", _service.DetectCurrentRoom()?.Id);
    }

    [Fact]
    public async Task Nothing_left_on_rolls_back()
    {
        _display.ApplyLeavesNothingOn = true;
        var result = await _service.SwitchToAsync(Living);
        Assert.False(result.Ok);
        Assert.Contains("no displays on", result.Message);
        Assert.NotEmpty(_display.State.Screens);
    }

    [Fact]
    public async Task Failed_rollback_falls_back_to_all_monitors()
    {
        _display.ApplyError = "boom";
        _display.RestoreFails = true;
        var result = await _service.SwitchToAsync(Living);
        Assert.Contains("all displays have been turned on", result.Message);
        Assert.Contains("showall", _display.Calls);
    }

    [Fact]
    public async Task Driver_rejection_stops_before_touching_screens()
    {
        _display.ValidateError = "These two displays are on different graphics cards, so they can't be duplicated.";
        var result = await _service.SwitchToAsync(_config.Rooms[2]);
        Assert.False(result.Ok);
        Assert.DoesNotContain(_display.Calls, c => c.StartsWith("apply"));
        Assert.Contains("can't be duplicated", _service.Check(_config.Rooms[2]).FirstError);
    }

    [Fact]
    public async Task Switching_to_current_room_does_nothing()
    {
        var result = await _service.SwitchToAsync(Office);
        Assert.True(result.Ok);
        Assert.Contains("already on", result.Message);
        Assert.DoesNotContain(_display.Calls, c => c.StartsWith("apply"));
    }

    [Fact]
    public async Task Elevated_windows_are_reported()
    {
        _windows.Open.Add(FakeWindows.Window(1, "admin.exe", "Admin tool", 2000, 0));
        _windows.Elevated.Add(1);
        await _service.SwitchToAsync(Living);
        var back = await _service.SwitchToAsync(Office);
        Assert.True(back.Ok);
        Assert.Equal(1, back.WindowsRefused);
        Assert.Contains("administrator", back.Message);
    }

    [Fact]
    public async Task Restore_windows_setting_off_skips_layouts()
    {
        _config.Settings.RestoreWindows = false;
        _windows.Open.Add(FakeWindows.Window(1, "code.exe", "Editor", 2000, 100));
        await _service.SwitchToAsync(Living);
        await _service.SwitchToAsync(Office);
        Assert.Empty(_service.Layouts.Rooms);
        Assert.Equal(10, _windows.Open[0].NormalRect.Left);
    }

    [Fact]
    public async Task Show_all_turns_everything_on_and_clears_current_room()
    {
        var result = await _service.ShowAllAsync();
        Assert.True(result.Ok);
        Assert.Equal(3, _display.State.Screens.Count);
        Assert.Null(_service.DetectCurrentRoom());
        Assert.True(_service.Layouts.Rooms.ContainsKey("office"));
    }
}

public sealed class CommandLineTests
{
    [Theory]
    [InlineData(new string[0], CommandKind.Tray, null)]
    [InlineData(new[] { "--room", "Office" }, CommandKind.Room, "Office")]
    [InlineData(new[] { "--room", "Living", "Area" }, CommandKind.Room, "Living Area")]
    [InlineData(new[] { "--room=Living Area" }, CommandKind.Room, "Living Area")]
    [InlineData(new[] { "/room", "Office" }, CommandKind.Room, "Office")]
    [InlineData(new[] { "--list" }, CommandKind.List, null)]
    [InlineData(new[] { "--all" }, CommandKind.All, null)]
    [InlineData(new[] { "--setup" }, CommandKind.Setup, null)]
    [InlineData(new[] { "--test" }, CommandKind.Test, null)]
    [InlineData(new[] { "-?" }, CommandKind.Help, null)]
    public void Parses(string[] args, CommandKind kind, string? room)
    {
        var command = CommandLine.Parse(args);
        Assert.Equal(kind, command.Kind);
        Assert.Equal(room, command.RoomName);
        Assert.Null(command.Error);
    }

    [Fact]
    public void Bad_input_gives_help_with_error()
    {
        Assert.NotNull(CommandLine.Parse(["--room"]).Error);
        Assert.True(CommandLine.Parse(["--startup"]) is { Kind: CommandKind.Tray, AtSignIn: true });
        Assert.False(CommandLine.Parse([]).AtSignIn);
        Assert.NotNull(CommandLine.Parse(["--bogus"]).Error);
    }

    [Fact]
    public void Pipe_protocol_round_trips()
    {
        var request = PipeProtocol.DecodeRequest(PipeProtocol.EncodeRequest(new Command(CommandKind.Room, "Living Area")));
        Assert.Equal(CommandKind.Room, request.Kind);
        Assert.Equal("Living Area", request.RoomName);
        Assert.Equal(CommandKind.List, PipeProtocol.DecodeRequest("LIST\r\n").Kind);
        Assert.Equal(CommandKind.Help, PipeProtocol.DecodeRequest("ROOM\t").Kind);

        var (code, message) = PipeProtocol.DecodeReply(PipeProtocol.EncodeReply(2, "line one\r\nline two"));
        Assert.Equal(2, code);
        Assert.Equal("line one\nline two", message);
    }
}
