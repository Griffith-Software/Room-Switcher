namespace RoomSwitcher.Core.Cli;

public enum CommandKind { Tray, Room, List, All, Setup, Test, Help }

public sealed record Command(CommandKind Kind, string? RoomName = null, string? Error = null)
{
    /// <summary>Started by Windows at sign-in (the Run key passes --startup).</summary>
    public bool AtSignIn { get; init; }

    /// <summary>Commands that do one thing and exit (or hand off to the running copy).</summary>
    public bool IsOneShot => Kind is CommandKind.Room or CommandKind.List or CommandKind.All;
}

public static class CommandLine
{
    public const string Usage =
        """
        Room Switcher - switch this PC's displays between rooms.

          RoomSwitcher.exe                  Start in the system tray
          RoomSwitcher.exe --room "Office"  Switch to the room called Office
          RoomSwitcher.exe --list           List the rooms (* marks the current one)
          RoomSwitcher.exe --all            Turn on all displays
          RoomSwitcher.exe --setup          Open Room Switcher settings
          RoomSwitcher.exe --test           Open Test devices
          RoomSwitcher.exe --help           Show this help
        """;

    public static Command Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return new Command(CommandKind.Tray);

        var first = args[0].Trim();
        var key = first.TrimStart('-', '/').ToLowerInvariant();
        string? inlineValue = null;
        var eq = key.IndexOf('=');
        if (eq >= 0)
        {
            inlineValue = first[(first.IndexOf('=') + 1)..];
            key = key[..eq];
        }

        switch (key)
        {
            case "room":
            case "r":
                var name = inlineValue ?? (args.Count > 1 ? string.Join(' ', args.Skip(1)) : null);
                if (string.IsNullOrWhiteSpace(name))
                    return new Command(CommandKind.Help, Error: "--room needs a room name, for example --room \"Office\".");
                return new Command(CommandKind.Room, name.Trim().Trim('"'));
            case "list":
            case "l":
                return new Command(CommandKind.List);
            case "all":
                return new Command(CommandKind.All);
            case "setup":
            case "edit":
                return new Command(CommandKind.Setup);
            case "test":
                return new Command(CommandKind.Test);
            case "tray":
                return new Command(CommandKind.Tray);
            case "startup":
                return new Command(CommandKind.Tray) { AtSignIn = true };
            case "help":
            case "h":
            case "?":
                return new Command(CommandKind.Help);
            default:
                return new Command(CommandKind.Help, Error: $"Unknown option: {first}");
        }
    }
}

/// <summary>
/// The text protocol between a second copy of RoomSwitcher.exe and the running one, over a named pipe.
/// Request: one line, "ROOM\t&lt;name&gt;", "LIST", "ALL" or "SETUP".
/// Reply: first line is the exit code, the rest is the message to print.
/// </summary>
public static class PipeProtocol
{
    public static string EncodeRequest(Command command) => command.Kind switch
    {
        CommandKind.Room => $"ROOM\t{command.RoomName}",
        CommandKind.List => "LIST",
        CommandKind.All => "ALL",
        CommandKind.Setup => "SETUP",
        CommandKind.Test => "TEST",
        _ => "TRAY",
    };

    public static Command DecodeRequest(string line)
    {
        var parts = line.TrimEnd('\r', '\n').Split('\t', 2);
        return parts[0] switch
        {
            "ROOM" when parts.Length == 2 && parts[1].Length > 0 => new Command(CommandKind.Room, parts[1]),
            "LIST" => new Command(CommandKind.List),
            "ALL" => new Command(CommandKind.All),
            "SETUP" => new Command(CommandKind.Setup),
            "TEST" => new Command(CommandKind.Test),
            "TRAY" => new Command(CommandKind.Tray),
            _ => new Command(CommandKind.Help, Error: "Unrecognised request."),
        };
    }

    public static string EncodeReply(int exitCode, string message) =>
        $"{exitCode}\n{message.Replace("\r\n", "\n")}";

    public static (int ExitCode, string Message) DecodeReply(string text)
    {
        var newline = text.IndexOf('\n');
        var head = newline < 0 ? text : text[..newline];
        var body = newline < 0 ? "" : text[(newline + 1)..];
        return int.TryParse(head.Trim(), out var code) ? (code, body) : (1, text);
    }
}
