using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoomSwitcher.Core.Model;

/// <summary>layouts.json (schema 1): where each room's windows were when the room was last left.</summary>
public sealed class LayoutsFile
{
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;
    public Dictionary<string, RoomLayout> Rooms { get; set; } = new();
    /// <summary>The room Room Switcher last switched to. Breaks ties when two rooms use the same monitors.</summary>
    public string? LastRoomId { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class RoomLayout
{
    public DateTimeOffset SavedAt { get; set; }
    public List<SavedWindow> Windows { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class SavedWindow
{
    public long Hwnd { get; set; }
    public string Exe { get; set; } = "";
    public string Class { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>1 = normal, 2 = minimized, 3 = maximized (WINDOWPLACEMENT.showCmd).</summary>
    public int ShowCmd { get; set; }
    public RectData NormalRect { get; set; } = new();
    public PointData MinPos { get; set; } = new();
    public PointData MaxPos { get; set; } = new();
    /// <summary>Set when the window was minimized from a maximized state.</summary>
    public bool RestoreToMaximized { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class RectData
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }

    public RectData() { }
    public RectData(int left, int top, int right, int bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }
}

public sealed class PointData
{
    public int X { get; set; }
    public int Y { get; set; }

    public PointData() { }
    public PointData(int x, int y) { X = x; Y = y; }
}
