using RoomSwitcher.Core.Display;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Rooms;
using static RoomSwitcher.Tests.TestData;

namespace RoomSwitcher.Tests;

public sealed class PlannerTests
{
    [Fact]
    public void Office_plans_two_extended_screens()
    {
        var config = Config();
        var result = RoomPlanner.Build(config, config.Rooms[0], OfficeState());
        Assert.True(result.Ok);
        Assert.Empty(result.Problems);
        Assert.Equal(2, result.Plan!.Screens.Count);
        Assert.True(result.Plan.Arranged);
        Assert.Equal(new[] { PathLeft }, result.Plan.Screens[0].DevicePaths);
        Assert.Equal(1920, result.Plan.Screens[1].X);
    }

    [Fact]
    public void Mirror_routes_one_screen_to_two_monitors()
    {
        var config = Config();
        var result = RoomPlanner.Build(config, config.Rooms[2], OfficeState());
        Assert.True(result.Ok);
        var screen = Assert.Single(result.Plan!.Screens);
        Assert.Equal(new[] { PathLeft, PathTv }, screen.DevicePaths);
        Assert.False(result.Plan.Arranged);
    }

    [Fact]
    public void Mixed_extended_and_mirrored_is_allowed()
    {
        var config = Config();
        var room = new Room
        {
            Name = "Mixed",
            Screens =
            {
                new ScreenSpec { X = 0, Width = 1920, Height = 1080, Primary = true, MonitorIds = { "left", "tv" } },
                new ScreenSpec { X = 1920, Width = 1920, Height = 1080, MonitorIds = { "right" } },
            },
        };
        config.Rooms.Add(room);
        var result = RoomPlanner.Build(config, room, OfficeState());
        Assert.True(result.Ok);
        Assert.Equal(3, result.Plan!.DevicePaths.Count());
    }

    [Fact]
    public void Primary_is_moved_to_origin_and_listed_first()
    {
        var config = Config();
        var office = config.Rooms[0];
        office.Screens[0].Primary = false;
        office.Screens[1].Primary = true;
        office.Screens[0].X = -1920;
        office.Screens[1].X = 0;
        office.Screens[0].X = 100; office.Screens[1].X = 2020; // saved with an offset
        var plan = RoomPlanner.Build(config, office, OfficeState()).Plan!;
        Assert.Equal(new[] { PathRight }, plan.Screens[0].DevicePaths);
        Assert.Equal(0, plan.Screens[0].X);
        Assert.Equal(-1920, plan.Screens[1].X);
    }

    [Fact]
    public void Room_with_nothing_on_is_an_error()
    {
        var config = Config();
        var room = RoomEditor.AddRoom(config, "Empty");
        var result = RoomPlanner.Build(config, room, OfficeState());
        Assert.False(result.Ok);
        Assert.Contains("at least one display", result.FirstError);
    }

    [Fact]
    public void Disconnected_monitor_is_an_error()
    {
        var config = Config();
        var state = OfficeState() with { Monitors = OfficeState().Monitors.Where(m => m.DevicePath != PathTv).ToList() };
        var result = RoomPlanner.Build(config, config.Rooms[1], state);
        Assert.False(result.Ok);
        Assert.Equal("Windows can't detect Living room TV right now. Check that it's plugged in and powered on, then try again.", result.FirstError);
    }

    [Fact]
    public void Mirroring_across_graphics_cards_is_an_error()
    {
        var config = Config();
        var state = OfficeState() with
        {
            Monitors = OfficeState().Monitors.Select(m => m.DevicePath == PathTv ? m with { AdapterKey = "gpu1" } : m).ToList(),
            Adapters = [new AdapterInfo("gpu0", 4), new AdapterInfo("gpu1", 4)],
        };
        var result = RoomPlanner.Build(config, config.Rooms[2], state);
        Assert.False(result.Ok);
        Assert.Equal("Office left and Living room TV are on different graphics cards, so they can't be duplicated.", result.FirstError);
    }

    [Fact]
    public void Too_many_screens_for_the_card_is_an_error()
    {
        var config = Config();
        var room = new Room
        {
            Name = "Everything",
            Screens = { new() { MonitorIds = { "left" } }, new() { MonitorIds = { "right" } }, new() { MonitorIds = { "tv" } } },
        };
        config.Rooms.Add(room);
        var state = OfficeState() with { Adapters = [new AdapterInfo("gpu0", 2)] };
        var result = RoomPlanner.Build(config, room, state);
        Assert.False(result.Ok);
        Assert.Contains("can only show 2 separate screens at once", result.FirstError);
    }

    [Fact]
    public void Overlapping_screens_are_an_error()
    {
        var config = Config();
        config.Rooms[0].Screens[1].X = 1000;
        var result = RoomPlanner.Build(config, config.Rooms[0], OfficeState());
        Assert.False(result.Ok);
        Assert.Contains("overlap", result.FirstError);
    }

    [Fact]
    public void Duplicate_room_names_are_an_error()
    {
        var config = Config();
        config.Rooms[1].Name = " office ";
        Assert.Contains("already called", RoomPlanner.Build(config, config.Rooms[1], OfficeState()).FirstError);
    }

    [Fact]
    public void Monitor_in_two_screens_is_an_error()
    {
        var config = Config();
        config.Rooms[0].Screens[1].MonitorIds.Add("left");
        Assert.Contains("more than one screen", RoomPlanner.Build(config, config.Rooms[0], OfficeState()).FirstError);
    }

    [Fact]
    public void Partly_arranged_room_lets_windows_choose()
    {
        var config = Config();
        config.Rooms[0].Screens[1].ClearGeometry();
        var result = RoomPlanner.Build(config, config.Rooms[0], OfficeState());
        Assert.True(result.Ok);
        Assert.Single(result.Problems);
        Assert.False(result.Plan!.Arranged);
    }
}

public sealed class MatcherTests
{
    [Fact]
    public void Detects_office_and_living()
    {
        var config = Config();
        Assert.Equal("office", RoomMatcher.DetectCurrent(config, OfficeState())?.Id);
        Assert.Equal("living", RoomMatcher.DetectCurrent(config, LivingState())?.Id);
    }

    [Fact]
    public void Screen_order_does_not_matter()
    {
        var config = Config();
        var state = State(Screen(1920, 0, 1920, 1080, PathRight), Screen(0, 0, 1920, 1080, PathLeft));
        Assert.Equal("office", RoomMatcher.DetectCurrent(config, state)?.Id);
    }

    [Fact]
    public void Mirror_is_detected_as_presentation()
    {
        var config = Config();
        var state = State(Screen(0, 0, 1920, 1080, PathTv, PathLeft));
        Assert.Equal("present", RoomMatcher.DetectCurrent(config, state)?.Id);
    }

    [Fact]
    public void Win_P_style_layout_matches_nothing()
    {
        var config = Config();
        var everything = State(Screen(0, 0, 1920, 1080, PathLeft), Screen(1920, 0, 1920, 1080, PathRight), Screen(3840, 0, 3840, 2160, PathTv));
        Assert.Null(RoomMatcher.DetectCurrent(config, everything));
        // Same monitors on, but mirrored instead of extended:
        Assert.Null(RoomMatcher.DetectCurrent(config, State(Screen(0, 0, 1920, 1080, PathLeft, PathRight))));
    }

    [Fact]
    public void Unknown_active_monitor_matches_nothing()
    {
        var config = Config();
        var state = OfficeState();
        state = state with { Screens = state.Screens.Append(Screen(3840, 0, 1920, 1080, @"\\?\DISPLAY#NEW")).ToList() };
        Assert.Null(RoomMatcher.DetectCurrent(config, state));
    }

    [Fact]
    public void Same_routing_rooms_are_told_apart_by_position_then_last_used()
    {
        var config = Config();
        var stacked = RoomEditor.Duplicate(config, config.Rooms[0]);
        stacked.Name = "Office stacked";
        stacked.Screens[1].X = 0;
        stacked.Screens[1].Y = 1080;

        Assert.Equal("office", RoomMatcher.DetectCurrent(config, OfficeState())?.Id);
        var stackedState = State(Screen(0, 0, 1920, 1080, PathLeft), Screen(0, 1080, 1920, 1080, PathRight));
        Assert.Equal(stacked.Id, RoomMatcher.DetectCurrent(config, stackedState)?.Id);

        // Neither matches position: the last room switched to wins.
        var odd = State(Screen(0, 0, 1920, 1080, PathLeft), Screen(-1920, 0, 1920, 1080, PathRight));
        Assert.Equal(stacked.Id, RoomMatcher.DetectCurrent(config, odd, stacked.Id)?.Id);
        Assert.Equal("office", RoomMatcher.DetectCurrent(config, odd, null)?.Id);
    }

    [Fact]
    public void Last_room_wins_over_a_twin_when_it_is_not_arranged()
    {
        var config = Config();
        var twin = RoomEditor.Duplicate(config, config.Rooms[0]);
        foreach (var s in twin.Screens) s.ClearGeometry();
        Assert.Equal("office", RoomMatcher.DetectCurrent(config, OfficeState())?.Id);
        Assert.Equal(twin.Id, RoomMatcher.DetectCurrent(config, OfficeState(), twin.Id)?.Id);
        Assert.Equal("office", RoomMatcher.DetectCurrent(config, OfficeState(), "office")?.Id);
    }

    [Fact]
    public void Nothing_on_matches_nothing() => Assert.Null(RoomMatcher.DetectCurrent(Config(), State()));
}

public sealed class EditorTests
{
    [Fact]
    public void Route_moves_monitor_between_columns_and_off()
    {
        var config = Config();
        var office = config.Rooms[0];
        Assert.True(RoomEditor.Route(office, "tv", 0));
        Assert.Equal(new[] { "left", "tv" }, office.Screens[0].MonitorIds);

        Assert.True(RoomEditor.Route(office, "tv", 1));
        Assert.Equal(new[] { "left" }, office.Screens[0].MonitorIds);
        Assert.Equal(new[] { "right", "tv" }, office.Screens[1].MonitorIds);

        Assert.True(RoomEditor.Route(office, "tv", null));
        Assert.DoesNotContain("tv", office.ActiveMonitorIds);
        Assert.False(RoomEditor.Route(office, "tv", null));
    }

    [Fact]
    public void Changing_routing_clears_the_arrangement()
    {
        var config = Config();
        var office = config.Rooms[0];
        Assert.True(office.IsArranged);
        RoomEditor.Route(office, "tv", 0);
        Assert.False(office.IsArranged);
        Assert.All(office.Screens, s => Assert.False(s.Primary));
    }

    [Fact]
    public void Same_monitor_in_many_rooms_with_different_routing()
    {
        var config = Config();
        var everything = RoomEditor.AddRoom(config, "Everything");
        RoomEditor.AddScreen(everything);
        RoomEditor.AddScreen(everything);
        RoomEditor.Route(everything, "left", 0);
        RoomEditor.Route(everything, "right", 1);
        RoomEditor.Route(everything, "tv", 2);

        Assert.Equal(3, config.Rooms.Count(r => r.ActiveMonitorIds.Contains("tv")));
        Assert.Single(config.Rooms[2].Screens); // Presentation: TV still mirrors left
    }

    [Fact]
    public void Duplicate_gets_new_ids_and_unique_name()
    {
        var config = Config();
        var copy = RoomEditor.Duplicate(config, config.Rooms[0]);
        Assert.Equal("Office copy", copy.Name);
        Assert.NotEqual(config.Rooms[0].Id, copy.Id);
        Assert.NotEqual(config.Rooms[0].Screens[0].Id, copy.Screens[0].Id);
        Assert.Equal(1, config.Rooms.IndexOf(copy));
        copy.Screens[0].MonitorIds.Add("tv");
        Assert.Single(config.Rooms[0].Screens[0].MonitorIds);
        Assert.Equal("Office copy 2", RoomEditor.Duplicate(config, config.Rooms[0]).Name);
    }

    [Fact]
    public void Move_reorders_and_stops_at_ends()
    {
        var config = Config();
        var living = config.Rooms[1];
        Assert.True(RoomEditor.Move(config, living, -1));
        Assert.Equal(0, config.Rooms.IndexOf(living));
        Assert.False(RoomEditor.Move(config, living, -1));
        Assert.True(RoomEditor.Move(config, living, 2));
        Assert.Equal(2, config.Rooms.IndexOf(living));
    }

    [Fact]
    public void Forget_monitor_removes_it_everywhere()
    {
        var config = Config();
        RoomEditor.ForgetMonitor(config, config.FindMonitor("tv")!);
        Assert.Equal(2, config.Monitors.Count);
        Assert.DoesNotContain(config.Rooms, r => r.ActiveMonitorIds.Contains("tv"));
    }

    [Fact]
    public void Capture_copies_windows_arrangement_into_room()
    {
        var config = Config();
        var room = config.Rooms[2];
        var state = State(new ActiveScreen(0, 0, 1920, 1080, 59940, 0, 125, [PathLeft, PathTv]));
        Assert.Equal(CaptureOutcome.Saved, RoomEditor.CaptureArrangement(config, room, state, acceptWindowsRouting: false));
        var s = room.Screens[0];
        Assert.Equal((1920, 1080, 59940, 125, true), (s.Width, s.Height, s.RefreshMillihz, s.ScalePercent, s.Primary));
        Assert.True(room.IsArranged);
    }

    [Fact]
    public void Capture_reports_and_optionally_takes_different_routing()
    {
        var config = Config();
        var room = config.Rooms[2]; // left + tv mirrored
        var state = OfficeState(); // left and right extended
        Assert.Equal(CaptureOutcome.RoutingDiffers, RoomEditor.CaptureArrangement(config, room, state, false));
        Assert.Equal(new[] { "left", "tv" }, room.Screens[0].MonitorIds);

        Assert.Equal(CaptureOutcome.SavedWithWindowsRouting, RoomEditor.CaptureArrangement(config, room, state, true));
        Assert.Equal(2, room.Screens.Count);
        Assert.Equal(1920, room.Screens.Single(x => x.MonitorIds.Contains("right")).X);
    }

    [Fact]
    public void Sync_adds_new_monitors_and_relinks_moved_ones()
    {
        var config = Config();
        config.Monitors[2].DevicePath = @"\\?\DISPLAY#LGD0001#OLDPORT";
        var state = OfficeState() with
        {
            Monitors = OfficeState().Monitors.Append(new ConnectedMonitor(@"\\?\DISPLAY#DEL1234#X", "DELL U2720Q", "DisplayPort", "gpu0", false)).ToList(),
        };
        Assert.True(MonitorResolver.SyncMonitors(config, state));
        Assert.Equal(PathTv, config.Monitors[2].DevicePath);
        Assert.Equal(4, config.Monitors.Count);
        Assert.Equal("DELL U2720Q", config.Monitors[3].Name);
        Assert.False(MonitorResolver.SyncMonitors(config, state));
    }

    [Fact]
    public void Same_friendly_name_is_not_guessed()
    {
        var config = Config();
        config.Monitors[0].DevicePath = "gone";
        config.Monitors[1].DevicePath = "gone too";
        var resolution = MonitorResolver.Resolve(config, OfficeState());
        Assert.Null(resolution.Find("left"));
        Assert.Null(resolution.Find("right"));
    }
}
