using System.Text;
using RoomSwitcher.Core.Cli;

namespace RoomSwitcher.App;

/// <summary>Runs --room, --list and --all, either here or on behalf of another copy over the pipe.</summary>
internal static class CommandRunner
{
    public const int ExitOk = 0;
    public const int ExitFailed = 1;
    public const int ExitUsage = 2;

    public static async Task<(int ExitCode, string Message)> RunAsync(AppHost host, Command command)
    {
        switch (command.Kind)
        {
            case CommandKind.List:
                return (ExitOk, List(host));

            case CommandKind.All:
            {
                var result = await host.Service.ShowAllAsync();
                return (result.Ok ? ExitOk : ExitFailed, result.Message);
            }

            case CommandKind.Room:
            {
                var room = host.Config.FindRoomByName(command.RoomName ?? "");
                if (room == null)
                {
                    var names = host.Config.Rooms.Count == 0
                        ? "No rooms are set up yet. Run RoomSwitcher.exe to set them up."
                        : "Rooms: " + string.Join(", ", host.Config.Rooms.Select(r => $"\"{r.Name}\""));
                    return (ExitUsage, $"There's no room called \"{command.RoomName}\". {names}");
                }
                var result = await host.Service.SwitchToAsync(room);
                return (result.Ok ? ExitOk : ExitFailed, result.Message);
            }

            default:
                return (ExitUsage, CommandLine.Usage);
        }
    }

    private static string List(AppHost host)
    {
        if (host.Config.Rooms.Count == 0) return "No rooms are set up yet. Run RoomSwitcher.exe to set them up.";
        var current = host.Service.DetectCurrentRoom();
        var sb = new StringBuilder();
        foreach (var room in host.Config.Rooms)
        {
            var monitors = room.Screens
                .Where(s => s.MonitorIds.Count > 0)
                .Select(s => string.Join(" = ", s.MonitorIds.Select(id => host.Config.FindMonitor(id)?.Name ?? "?")));
            sb.AppendLine($"{(room.Id == current?.Id ? "*" : " ")} {room.Name}  ({string.Join(", ", monitors)})");
        }
        return sb.ToString();
    }
}
