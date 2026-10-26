using RoomSwitcher.Core.Display;
using RoomSwitcher.Core.Model;

namespace RoomSwitcher.Core.Rooms;

/// <summary>
/// Pairs the monitors in the config with the monitors Windows reports. The device path is the
/// identity; if a device path has changed (driver reinstall, new port) and exactly one unclaimed
/// monitor has the same friendly name, that one is taken instead.
/// </summary>
public static class MonitorResolver
{
    public sealed record Resolution(
        IReadOnlyDictionary<string, ConnectedMonitor> ByMonitorId,
        IReadOnlyList<(MonitorInfo Monitor, ConnectedMonitor Connected)> Relinked)
    {
        public ConnectedMonitor? Find(string monitorId) => ByMonitorId.GetValueOrDefault(monitorId);

        public string? MonitorIdFor(string devicePath) =>
            ByMonitorId.FirstOrDefault(kv => string.Equals(kv.Value.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase)).Key;
    }

    public static Resolution Resolve(AppConfig config, DisplayState state)
    {
        var result = new Dictionary<string, ConnectedMonitor>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var relinked = new List<(MonitorInfo, ConnectedMonitor)>();

        foreach (var m in config.Monitors)
        {
            var c = state.FindMonitor(m.DevicePath);
            if (c != null && claimed.Add(c.DevicePath)) result[m.Id] = c;
        }

        foreach (var m in config.Monitors.Where(m => !result.ContainsKey(m.Id) && !string.IsNullOrEmpty(m.FriendlyName)))
        {
            var sameNameInConfig = config.Monitors.Count(o => string.Equals(o.FriendlyName, m.FriendlyName, StringComparison.OrdinalIgnoreCase));
            var candidates = state.Monitors
                .Where(c => !claimed.Contains(c.DevicePath) && !IsKnown(config, c.DevicePath)
                            && string.Equals(c.FriendlyName, m.FriendlyName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sameNameInConfig == 1 && candidates.Count == 1)
            {
                result[m.Id] = candidates[0];
                claimed.Add(candidates[0].DevicePath);
                relinked.Add((m, candidates[0]));
            }
        }

        return new Resolution(result, relinked);
    }

    private static bool IsKnown(AppConfig config, string devicePath) =>
        config.Monitors.Any(m => string.Equals(m.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Adds newly connected monitors to the config and records relinked device paths.
    /// Returns true if the config changed.
    /// </summary>
    public static bool SyncMonitors(AppConfig config, DisplayState state)
    {
        var changed = false;
        var resolution = Resolve(config, state);
        foreach (var (monitor, connected) in resolution.Relinked)
        {
            monitor.DevicePath = connected.DevicePath;
            changed = true;
        }

        foreach (var c in state.Monitors)
        {
            if (resolution.ByMonitorId.Values.Any(v => ReferenceEquals(v, c))) continue;
            config.Monitors.Add(new MonitorInfo
            {
                DevicePath = c.DevicePath,
                FriendlyName = c.FriendlyName,
                Name = DefaultName(config, c),
            });
            changed = true;
        }

        var after = Resolve(config, state);
        foreach (var m in config.Monitors)
        {
            var c = after.Find(m.Id);
            if (c != null && string.IsNullOrEmpty(m.FriendlyName) && !string.IsNullOrEmpty(c.FriendlyName))
            {
                m.FriendlyName = c.FriendlyName;
                changed = true;
            }
        }
        return changed;
    }

    private static string DefaultName(AppConfig config, ConnectedMonitor c)
    {
        var baseName = string.IsNullOrWhiteSpace(c.FriendlyName) ? "Display" : c.FriendlyName;
        var name = baseName;
        for (var i = 2; config.Monitors.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)); i++)
            name = $"{baseName} ({i})";
        return name;
    }
}
