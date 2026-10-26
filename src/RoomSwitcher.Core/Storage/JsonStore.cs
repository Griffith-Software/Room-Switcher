using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoomSwitcher.Core.Model;

namespace RoomSwitcher.Core.Storage;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(LayoutsFile))]
internal sealed partial class StoreJsonContext : JsonSerializerContext;

/// <summary>Reads and writes config.json and layouts.json. Writes are atomic: temp file, then rename.</summary>
public static class JsonStore
{
    // Relaxed escaping keeps hand-editable files readable: "Living & Kitchen" stays as typed.
    private static readonly StoreJsonContext Context = new(new JsonSerializerOptions(StoreJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public static AppConfig LoadConfig(string path)
    {
        if (!File.Exists(path)) return new AppConfig();
        var config = JsonSerializer.Deserialize(File.ReadAllText(path), Context.AppConfig) ?? new AppConfig();
        if (config.Schema > AppConfig.CurrentSchema)
            throw new InvalidDataException($"{Path.GetFileName(path)} was written by a newer version of Room Switcher (schema {config.Schema}).");
        Normalize(config);
        return config;
    }

    public static LayoutsFile LoadLayouts(string path)
    {
        if (!File.Exists(path)) return new LayoutsFile();
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), Context.LayoutsFile) ?? new LayoutsFile();
        }
        catch (JsonException)
        {
            // Saved window positions are a convenience; a damaged file must never stop room switching.
            return new LayoutsFile();
        }
    }

    /// <summary>Saves config.json, keeping the version it replaces as config.previous.json.</summary>
    public static void SaveConfig(string path, AppConfig config) =>
        WriteAtomic(path, JsonSerializer.Serialize(config, Context.AppConfig), PreviousPath(path));

    public static string PreviousPath(string path) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, Path.GetFileNameWithoutExtension(path) + ".previous.json");

    public static void SaveLayouts(string path, LayoutsFile layouts) =>
        WriteAtomic(path, JsonSerializer.Serialize(layouts, Context.LayoutsFile));

    public static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, Context.AppConfig);

    public static AppConfig DeserializeConfig(string json)
    {
        var config = JsonSerializer.Deserialize(json, Context.AppConfig) ?? new AppConfig();
        Normalize(config);
        return config;
    }

    /// <summary>Deep copy, so editors can work on a draft and cancel.</summary>
    public static AppConfig Clone(AppConfig config) => DeserializeConfig(Serialize(config));

    public static void WriteAtomic(string path, string contents, string? backupPath = null)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Environment.ProcessId}.tmp");
        File.WriteAllText(temp, contents);
        if (File.Exists(path))
            File.Replace(temp, path, backupPath, ignoreMetadataErrors: true);
        else
            File.Move(temp, path);
    }

    // Older or hand-edited files may have nulls where lists are expected.
    private static void Normalize(AppConfig config)
    {
        config.Monitors ??= new();
        config.Rooms ??= new();
        config.Settings ??= new();
        config.AudioDevices ??= new();
        config.UsbDevices ??= new();
        foreach (var room in config.Rooms)
        {
            room.Screens ??= new();
            room.UsbOff ??= new();
            room.Devices ??= new();
            foreach (var screen in room.Screens) screen.MonitorIds ??= new();
        }
        Hotkeys.HotkeyRules.FillDefaults(config);
    }
}
