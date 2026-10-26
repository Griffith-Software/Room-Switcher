using RoomSwitcher.Core.Display;
using RoomSwitcher.Core.Model;

namespace RoomSwitcher.Tests;

/// <summary>A three-monitor PC like the one in the spec: two office monitors and a living-room TV.</summary>
internal static class TestData
{
    public const string PathLeft = @"\\?\DISPLAY#SAM0D2D#5&1&0&UID256#{e6f07b5f}";
    public const string PathRight = @"\\?\DISPLAY#SAM0D2D#5&1&0&UID257#{e6f07b5f}";
    public const string PathTv = @"\\?\DISPLAY#LGD0001#5&1&0&UID258#{e6f07b5f}";

    public static AppConfig Config()
    {
        var config = new AppConfig();
        config.Monitors.Add(new MonitorInfo { Id = "left", DevicePath = PathLeft, Name = "Office left", FriendlyName = "C24F390" });
        config.Monitors.Add(new MonitorInfo { Id = "right", DevicePath = PathRight, Name = "Office right", FriendlyName = "C24F390" });
        config.Monitors.Add(new MonitorInfo { Id = "tv", DevicePath = PathTv, Name = "Living room TV", FriendlyName = "LG TV" });

        config.Rooms.Add(new Room
        {
            Id = "office", Name = "Office",
            Screens =
            {
                new ScreenSpec { Id = "o1", X = 0, Y = 0, Width = 1920, Height = 1080, RefreshMillihz = 60000, Primary = true, MonitorIds = { "left" } },
                new ScreenSpec { Id = "o2", X = 1920, Y = 0, Width = 1920, Height = 1080, RefreshMillihz = 60000, MonitorIds = { "right" } },
            },
        });
        config.Rooms.Add(new Room
        {
            Id = "living", Name = "Living Area",
            Screens = { new ScreenSpec { Id = "l1", X = 0, Y = 0, Width = 3840, Height = 2160, RefreshMillihz = 60000, Primary = true, MonitorIds = { "tv" } } },
        });
        config.Rooms.Add(new Room
        {
            Id = "present", Name = "Presentation",
            Screens = { new ScreenSpec { Id = "p1", MonitorIds = { "left", "tv" } } },
        });
        return config;
    }

    public static DisplayState State(params ActiveScreen[] screens)
    {
        var active = screens.SelectMany(s => s.DevicePaths).ToHashSet();
        return new DisplayState(
            [
                new ConnectedMonitor(PathLeft, "C24F390", "DisplayPort", "gpu0", active.Contains(PathLeft)),
                new ConnectedMonitor(PathRight, "C24F390", "DisplayPort", "gpu0", active.Contains(PathRight)),
                new ConnectedMonitor(PathTv, "LG TV", "HDMI", "gpu0", active.Contains(PathTv)),
            ],
            [new AdapterInfo("gpu0", 4)],
            screens);
    }

    public static ActiveScreen Screen(int x, int y, int w, int h, params string[] paths) =>
        new(x, y, w, h, 60000, 0, null, paths);

    public static DisplayState OfficeState() => State(
        Screen(0, 0, 1920, 1080, PathLeft),
        Screen(1920, 0, 1920, 1080, PathRight));

    public static DisplayState LivingState() => State(Screen(0, 0, 3840, 2160, PathTv));
}
