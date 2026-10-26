using RoomSwitcher.Core.Model;

namespace RoomSwitcher.Core.Hotkeys;

/// <summary>Modifier keys, with the same values Windows' RegisterHotKey uses.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A keyboard shortcut: modifiers plus one key (a Windows virtual-key code). Stored in config.json
/// as text such as "Ctrl+Alt+1", so it can be read and edited by hand.
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, int Key)
{
    private static readonly Dictionary<int, string> Names = BuildNames();
    private static readonly Dictionary<string, int> Codes =
        Names.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        // '+' only ever separates parts; the key that types '+' is stored as "=".
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var m = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Ctrl,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" or "windows" => HotkeyModifiers.Win,
                _ => (HotkeyModifiers?)null,
            };
            if (m == null) return false;
            modifiers |= m.Value;
        }

        if (!Codes.TryGetValue(parts[^1], out var key)) return false;
        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    public static bool IsKnownKey(int key) => Names.ContainsKey(key);

    /// <summary>The stored form: "Ctrl+Alt+1".</summary>
    public override string ToString() => string.Join("+", Parts());

    /// <summary>The shown form: "Ctrl + Alt + 1".</summary>
    public string Display => string.Join(" + ", Parts());

    private IEnumerable<string> Parts()
    {
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) yield return "Ctrl";
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) yield return "Alt";
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) yield return "Shift";
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) yield return "Win";
        yield return Names.TryGetValue(Key, out var name) ? name : $"Key{Key}";
    }

    /// <summary>Why this shortcut can't be used, or null if it's fine.</summary>
    public string? Problem()
    {
        if (!IsKnownKey(Key)) return "That key can't be used for a shortcut.";
        var isHighFunctionKey = Key is >= 0x7C and <= 0x87; // F13–F24 are free on almost every keyboard
        if (!isHighFunctionKey && (Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0)
            return "Add Ctrl, Alt or Win, so the shortcut doesn't get in the way of normal typing.";
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl) && Modifiers.HasFlag(HotkeyModifiers.Alt) && Key == 0x2E)
            return "Ctrl + Alt + Delete belongs to Windows.";
        return null;
    }

    private static Dictionary<int, string> BuildNames()
    {
        var names = new Dictionary<int, string>();
        for (var c = '0'; c <= '9'; c++) names[c] = c.ToString();
        for (var c = 'A'; c <= 'Z'; c++) names[c] = c.ToString();
        for (var i = 1; i <= 24; i++) names[0x6F + i] = $"F{i}";
        for (var i = 0; i <= 9; i++) names[0x60 + i] = $"Num{i}";
        names[0x20] = "Space";
        names[0x21] = "PageUp";
        names[0x22] = "PageDown";
        names[0x23] = "End";
        names[0x24] = "Home";
        names[0x25] = "Left";
        names[0x26] = "Up";
        names[0x27] = "Right";
        names[0x28] = "Down";
        names[0x2D] = "Insert";
        names[0x2E] = "Delete";
        names[0x13] = "Pause";
        names[0xBA] = ";";
        names[0xBB] = "=";
        names[0xBC] = ",";
        names[0xBD] = "-";
        names[0xBE] = ".";
        names[0xBF] = "/";
        names[0xC0] = "`";
        names[0xDB] = "[";
        names[0xDC] = "\\";
        names[0xDD] = "]";
        names[0xDE] = "'";
        return names;
    }
}

/// <summary>Default shortcuts, and the rules for shortcuts that clash.</summary>
public static class HotkeyRules
{
    public const string DefaultNextRoom = "Ctrl+Alt+0";
    public const string NextRoomOwner = "next-room";

    /// <summary>
    /// Gives rooms that have never had a shortcut the default Ctrl+Alt+1…9 by their place in the list,
    /// skipping any already taken. An empty string means the person turned the shortcut off; it stays off.
    /// Returns true if anything changed.
    /// </summary>
    public static bool FillDefaults(AppConfig config)
    {
        var changed = false;
        if (config.Settings.NextRoomHotkey == null)
        {
            config.Settings.NextRoomHotkey = DefaultNextRoom;
            changed = true;
        }
        foreach (var room in config.Rooms.Where(r => r.Hotkey == null))
        {
            room.Hotkey = NextFreeDefault(config, room) ?? "";
            changed = true;
        }
        return changed;
    }

    /// <summary>The first of Ctrl+Alt+1…9 nobody uses, preferring the room's own position.</summary>
    public static string? NextFreeDefault(AppConfig config, Room room)
    {
        var position = config.Rooms.IndexOf(room);
        var candidates = Enumerable.Range(1, 9).Select(n => $"Ctrl+Alt+{n}").ToList();
        if (position is >= 0 and < 9)
        {
            var own = candidates[position];
            candidates.Remove(own);
            candidates.Insert(0, own);
        }
        return candidates.FirstOrDefault(c => FindOwner(config, Parse(c)!.Value, room.Id) == null);
    }

    public static Hotkey? Parse(string? text) => Hotkey.TryParse(text, out var h) ? h : null;

    /// <summary>Who else uses this shortcut: a room's name, "the next room shortcut", or null.</summary>
    public static string? FindOwner(AppConfig config, Hotkey hotkey, string ownerId)
    {
        foreach (var room in config.Rooms)
            if (room.Id != ownerId && Parse(room.Hotkey) == hotkey) return room.Name;
        if (ownerId != NextRoomOwner && Parse(config.Settings.NextRoomHotkey) == hotkey) return "the next room shortcut";
        return null;
    }

    /// <summary>A plain-language problem with a room's (or the next-room) shortcut, or null.</summary>
    public static string? Check(AppConfig config, string? text, string ownerId)
    {
        if (string.IsNullOrEmpty(text)) return null;
        if (Parse(text) is not { } hotkey) return $"\"{text}\" isn't a shortcut Room Switcher understands.";
        if (hotkey.Problem() is { } problem) return problem;
        if (FindOwner(config, hotkey, ownerId) is { } owner) return $"{hotkey.Display} is also used by {owner}.";
        return null;
    }

    /// <summary>The room after the current one, wrapping around; the first room when none is current.</summary>
    public static Room? NextRoom(IReadOnlyList<Room> rooms, Room? current, Func<Room, bool> usable)
    {
        if (rooms.Count == 0) return null;
        var start = current == null ? -1 : rooms.ToList().FindIndex(r => r.Id == current.Id);
        for (var i = 1; i <= rooms.Count; i++)
        {
            var candidate = rooms[(start + i + rooms.Count) % rooms.Count];
            if (candidate.Id != current?.Id && usable(candidate)) return candidate;
        }
        return null;
    }
}
