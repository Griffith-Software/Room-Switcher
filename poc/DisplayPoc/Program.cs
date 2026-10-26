using RoomSwitcher.Core.Display;
using RoomSwitcher.Core.Model;
using RoomSwitcher.Core.Rooms;
using RoomSwitcher.Core.Storage;
using RoomSwitcher.Windows.Display;

// Proof of concept (build step 1): capture display arrangements and toggle between them with
// SetDisplayConfig, test mirroring, and check SDC_VALIDATE rejects an impossible combination.
// Every change that touches the screens reverts on its own unless you press K to keep it.

var backend = new WindowsDisplayBackend();
var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

try
{
    switch (command)
    {
        case "list": List(); break;
        case "save": Save(Arg(1, "file")); break;
        case "validate": Validate(Load(Arg(1, "file"))); break;
        case "apply": ApplyWithRevert(Load(Arg(1, "file")), RevertSeconds()); break;
        case "toggle": Toggle(Arg(1, "fileA"), Arg(2, "fileB"), args.Length > 3 ? int.Parse(args[3]) : 3); break;
        case "clone": Clone(int.Parse(Arg(1, "monitor#")), int.Parse(Arg(2, "monitor#")), args.Contains("--apply")); break;
        case "impossible": Impossible(); break;
        case "showall": Report(backend.ShowAll()); break;
        case "refreshtest": RefreshTest(); break;
        case "loadconfig":
        {
            var path = args[1];
            try
            {
                var c = JsonStore.LoadConfig(path);
                Console.WriteLine($"loaded: {c.Rooms.Count} rooms, {c.Monitors.Count} displays");
                var paths = AppPaths.Resolve(args.Length > 2 ? args[2] : AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
                Console.WriteLine($"app would use: {paths.ConfigPath} (portable {paths.Portable}), exists {File.Exists(paths.ConfigPath)}");
            }
            catch (Exception e) { Console.WriteLine("LOAD FAILED: " + e); }
            break;
        }
        case "modes":
            foreach (var m in backend.Query().Monitors)
            {
                var modes = backend.Modes(m.DevicePath);
                Console.WriteLine($"{m.FriendlyName} ({(m.Active ? "on" : "off")}), preferred {backend.PreferredMode(m.DevicePath)?.SizeText}: {modes.Count} modes");
                foreach (var size in RoomSwitcher.Core.Display.Arrangement.Sizes(modes).Take(6))
                    Console.WriteLine($"   {size.Width} x {size.Height}: {string.Join(", ", RoomSwitcher.Core.Display.Arrangement.Rates(modes, size.Width, size.Height).Select(r => r / 1000))} Hz");
            }
            break;
        case "chime":
            Console.WriteLine(RoomSwitcher.Windows.Audio.AudioTester.PlayChime(args[1]) ?? "Played.");
            break;
        case "listen":
        {
            using var cts = new CancellationTokenSource(2500);
            var levels = new List<float>();
            var problem = RoomSwitcher.Windows.Audio.AudioTester.Listen(args[1], levels.Add, cts.Token);
            Console.WriteLine(problem ?? $"Heard {levels.Count} readings, loudest {(levels.Count == 0 ? 0 : levels.Max()):0.000}");
            break;
        }
        case "rawinput":
        {
            // Raw Input device paths, converted the way the setup window converts them, matched to physical USB devices.
            var physicals = RoomSwitcher.Windows.Devices.UsbDevices.ListPhysical();
            foreach (var path in RawInputList.Names())
            {
                var id = path.StartsWith(@"\\?\") ? path[4..] : path;
                var cut = id.LastIndexOf("#{", StringComparison.Ordinal);
                if (cut > 0) id = id[..cut];
                id = id.Replace('#', '\\');
                var match = physicals.FirstOrDefault(p => p.Members.Any(m => string.Equals(m.InstanceId, id, StringComparison.OrdinalIgnoreCase)));
                Console.WriteLine($"{(match == null ? "  (none)          " : match.Device.Name.PadRight(18))} {id}");
            }
            break;
        }
        case "usb":
            foreach (var phys in RoomSwitcher.Windows.Devices.UsbDevices.ListPhysical())
            {
                var d = phys.Device;
                Console.WriteLine($"{d.Name,-40} {RoomSwitcher.Core.Devices.UsbRules.KindText(d.Kinds),-20} {d.Connection,-10} {(d.Enabled ? "on" : "OFF")}  {d.Id}");
                foreach (var m in phys.Members) Console.WriteLine($"      {m.Class,-10} {(m.Enabled ? "on " : "OFF")} {m.InstanceId}");
            }
            break;
        case "audio":
        {
            var audio = new RoomSwitcher.Windows.Audio.WindowsAudioBackend();
            foreach (var flow in new[] { RoomSwitcher.Core.Audio.AudioFlow.Output, RoomSwitcher.Core.Audio.AudioFlow.Input })
            {
                Console.WriteLine(flow == RoomSwitcher.Core.Audio.AudioFlow.Output ? "Playback:" : "Recording:");
                foreach (var d in audio.List(flow))
                    Console.WriteLine($"  {(d.IsDefault ? "*" : " ")} {d.Name,-50} {(d.Connected ? "connected" : "not connected")}  {d.Id}");
            }
            // audio set <id>: make it the default
            if (args.Length > 2 && args[1] == "set") Console.WriteLine(audio.SetDefault(args[2]) ? "Set." : "Refused.");
            break;
        }
        case "windows":
            foreach (var w in new RoomSwitcher.Windows.Layouts.WindowsWindowBackend().Enumerate())
                Console.WriteLine($"{w.Hwnd,10} {w.Exe,-22} {w.Class,-28} show {w.ShowCmd} ({w.NormalRect.Left},{w.NormalRect.Top})-({w.NormalRect.Right},{w.NormalRect.Bottom}) {w.Title}");
            break;
        default: Help(); break;
    }
}
catch (Exception e)
{
    Console.WriteLine($"Error: {e.Message}");
    return 1;
}
return 0;

string Arg(int index, string name) =>
    args.Length > index ? args[index] : throw new ArgumentException($"Missing <{name}>. Run DisplayPoc help.");

int RevertSeconds()
{
    var i = Array.IndexOf(args, "--revert");
    return i >= 0 && i + 1 < args.Length ? int.Parse(args[i + 1]) : 15;
}

void Help() => Console.WriteLine(
    """
    DisplayPoc - Room Switcher display proof of concept

      DisplayPoc list                       Monitors, graphics cards, and what is on now
      DisplayPoc save <file.json>           Save the current arrangement (set it up in Windows Settings first)
      DisplayPoc validate <file.json>       Ask the driver if a saved arrangement can work (no flicker)
      DisplayPoc apply <file.json>          Apply it; reverts after 15 s unless you press K (--revert N)
      DisplayPoc toggle <a.json> <b.json> [n]  Switch a->b->a n times, checking each result
      DisplayPoc clone <m1> <m2> [--apply]  Mirror monitor #m1 onto #m2 (numbers from 'list')
      DisplayPoc impossible                 Check SDC_VALIDATE rejects an impossible mode
      DisplayPoc showall                    Turn every monitor on (recovery)
    """);

List<ConnectedMonitor> Monitors() => backend.Query().Monitors.ToList();

void List()
{
    var state = backend.Query();
    Console.WriteLine("Connected monitors:");
    for (var i = 0; i < state.Monitors.Count; i++)
    {
        var m = state.Monitors[i];
        Console.WriteLine($"  #{i + 1}  {m.FriendlyName,-20} {m.Connector,-12} card {m.AdapterKey}  {(m.Active ? "ON" : "off")}");
        Console.WriteLine($"       {m.DevicePath}");
    }
    Console.WriteLine("Graphics cards:");
    foreach (var a in state.Adapters) Console.WriteLine($"  {a.Key}: {a.SourceCount} screen sources");
    Console.WriteLine("Screens on now:");
    foreach (var s in state.Screens)
    {
        var names = s.DevicePaths.Select(p => state.FindMonitor(p)?.FriendlyName ?? p);
        Console.WriteLine($"  {s.Width}x{s.Height} at ({s.X},{s.Y}) {s.RefreshMillihz / 1000.0:0.###} Hz rot {s.Rotation} scale {s.ScalePercent?.ToString() ?? "?"}% {(s.Primary ? "PRIMARY " : "")}-> {string.Join(" + ", names)}");
    }
}

void Save(string file)
{
    var state = backend.Query();
    var config = new AppConfig();
    MonitorResolver.SyncMonitors(config, state);
    var room = new Room { Name = Path.GetFileNameWithoutExtension(file) };
    room.Screens.Add(new ScreenSpec { MonitorIds = { config.Monitors[0].Id } });
    config.Rooms.Add(room);
    var outcome = RoomEditor.CaptureArrangement(config, room, state, acceptWindowsRouting: true);
    JsonStore.SaveConfig(file, config);
    Console.WriteLine($"Saved {room.Screens.Count} screen(s) to {file} ({outcome}).");
    List();
}

(AppConfig Config, Room Room) Load(string file)
{
    var config = JsonStore.LoadConfig(file);
    if (config.Rooms.Count == 0) throw new InvalidDataException($"{file} has no room in it.");
    return (config, config.Rooms[0]);
}

RoomPlan Plan((AppConfig Config, Room Room) loaded)
{
    var result = RoomPlanner.Build(loaded.Config, loaded.Room, backend.Query());
    foreach (var p in result.Problems) Console.WriteLine($"  {p.Severity}: {p.Message}");
    return result.Ok ? result.Plan! : throw new InvalidOperationException(result.FirstError);
}

void Validate((AppConfig, Room) loaded)
{
    Report(backend.Validate(Plan(loaded)));
}

void Report(DisplayResult result)
{
    Console.WriteLine(result.Ok ? "OK" : $"Refused: {result.Message}");
    if (backend.LastDetail.Length > 0) Console.WriteLine($"  ({backend.LastDetail})");
}

bool ApplyWithRevert((AppConfig Config, Room Room) loaded, int seconds)
{
    var plan = Plan(loaded);
    var restore = backend.CaptureRestorePoint();
    var result = backend.Apply(plan);
    Report(result);
    if (!result.Ok) return false;
    Thread.Sleep(2000);
    List();
    CheckMatches(loaded);
    if (seconds <= 0) return true;

    Console.Write($"Press K within {seconds} s to keep this, anything else to go back... ");
    var deadline = DateTime.Now.AddSeconds(seconds);
    while (DateTime.Now < deadline)
    {
        if (!Console.IsInputRedirected && Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true).Key;
            if (key == ConsoleKey.K) { Console.WriteLine("kept."); return true; }
            break;
        }
        Thread.Sleep(100);
    }
    Console.WriteLine("reverting.");
    Report(backend.Restore(restore));
    return true;
}

void CheckMatches((AppConfig Config, Room Room) loaded)
{
    var current = RoomMatcher.DetectCurrent(loaded.Config, backend.Query());
    Console.WriteLine(current?.Id == loaded.Room.Id
        ? "Check: the screens now match the saved arrangement."
        : "Check: the screens do NOT match the saved arrangement.");
    var state = backend.Query();
    foreach (var spec in loaded.Room.Screens.Where(s => s.HasGeometry))
    {
        var path = loaded.Config.FindMonitor(spec.MonitorIds[0])?.DevicePath;
        var live = state.Screens.FirstOrDefault(s => s.DevicePaths.Contains(path!, StringComparer.OrdinalIgnoreCase));
        if (live == null) { Console.WriteLine($"  missing screen for {path}"); continue; }
        var diffs = new List<string>();
        if (live.Width != spec.Width || live.Height != spec.Height) diffs.Add($"size {live.Width}x{live.Height} vs {spec.Width}x{spec.Height}");
        if (spec.RefreshMillihz > 0 && Math.Abs(live.RefreshMillihz - spec.RefreshMillihz) > 50) diffs.Add($"refresh {live.RefreshMillihz} vs {spec.RefreshMillihz}");
        if (live.Rotation != spec.Rotation) diffs.Add($"rotation {live.Rotation} vs {spec.Rotation}");
        if (spec.ScalePercent != null && live.ScalePercent != spec.ScalePercent) diffs.Add($"scale {live.ScalePercent} vs {spec.ScalePercent}");
        if (live.Primary != spec.Primary) diffs.Add($"primary {live.Primary} vs {spec.Primary}");
        Console.WriteLine(diffs.Count == 0 ? $"  {spec.Width}x{spec.Height}: all match" : $"  {string.Join(", ", diffs)}");
    }
}

void Toggle(string a, string b, int times)
{
    var restore = backend.CaptureRestorePoint();
    for (var i = 0; i < times; i++)
    {
        foreach (var file in new[] { a, b })
        {
            Console.WriteLine($"--- {i + 1}: {file}");
            if (!ApplyWithRevert(Load(file), 0)) { backend.Restore(restore); return; }
            Thread.Sleep(3000);
        }
    }
    Console.WriteLine("--- back to how it was");
    Report(backend.Restore(restore));
}

void Clone(int first, int second, bool apply)
{
    var monitors = Monitors();
    var paths = new[] { monitors[first - 1].DevicePath, monitors[second - 1].DevicePath };
    var plan = new RoomPlan("clone", "Clone test", [new PlannedScreen(0, 0, 0, 0, 0, 0, null, paths)]);
    Console.Write("Validate mirror: ");
    Report(backend.Validate(plan));
    if (!apply) return;
    var restore = backend.CaptureRestorePoint();
    Report(backend.Apply(plan));
    Thread.Sleep(2000);
    List();
    Console.WriteLine("Look at both monitors: they should show the same picture. Reverting in 15 s.");
    Thread.Sleep(15000);
    Report(backend.Restore(restore));
}

void Impossible()
{
    var monitors = Monitors();
    var plan = new RoomPlan("x", "Impossible", [new PlannedScreen(0, 0, 30000, 30000, 0, 0, null, [monitors[0].DevicePath])]);
    Console.Write("A 30000x30000 screen should be refused: ");
    var result = backend.Validate(plan);
    Report(result);
    Console.WriteLine(result.Ok ? "Accepted with SDC_ALLOW_CHANGES (Windows adjusted the mode)." : "As expected.");
    var exact = backend.ValidateExact(plan);
    Console.WriteLine($"Without SDC_ALLOW_CHANGES: {(exact == 0 ? "accepted (UNEXPECTED)" : $"refused with error {exact} (as expected)")}");
    if (monitors.Count >= 2)
    {
        var all = monitors.Select(m => new PlannedScreen(0, 0, 0, 0, 0, 0, null, [m.DevicePath])).ToList();
        Console.Write($"All {monitors.Count} monitors as separate screens: ");
        Report(backend.Validate(new RoomPlan("all", "All", all)));
        Console.Write($"All {monitors.Count} monitors mirrored: ");
        Report(backend.Validate(new RoomPlan("mirror", "Mirror", [new PlannedScreen(0, 0, 0, 0, 0, 0, null, monitors.Select(m => m.DevicePath).ToList())])));
    }
}

// How strict is SetDisplayConfig about the refresh rate on a path with no target mode?
void RefreshTest()
{
    var active = RoomSwitcher.Windows.Display.CcdConfig.Active();
    foreach (var (label, change) in new (string, Func<RoomSwitcher.Windows.Native.DISPLAYCONFIG_RATIONAL, RoomSwitcher.Windows.Native.DISPLAYCONFIG_RATIONAL>)[]
             {
                 ("driver's exact rational", r => r),
                 ("rounded to millihertz /1000", r => new() { Numerator = RoomSwitcher.Windows.Display.DeviceInfo.ToMillihz(r), Denominator = 1000 }),
                 ("zero (let Windows choose)", r => default),
             })
    {
        var paths = active.Paths.ToArray();
        for (var i = 0; i < paths.Length; i++)
        {
            Console.WriteLine($"  path {i}: refresh {paths[i].targetInfo.refreshRate.Numerator}/{paths[i].targetInfo.refreshRate.Denominator}, target mode idx {paths[i].targetInfo.modeInfoIdx}");
            paths[i].targetInfo.refreshRate = change(paths[i].targetInfo.refreshRate);
            paths[i].targetInfo.modeInfoIdx = RoomSwitcher.Windows.Native.DisplayConfigApi.DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        }
        var modes = active.Modes.Where(m => m.infoType == RoomSwitcher.Windows.Native.DisplayConfigApi.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE).ToArray();
        // re-point source mode indices into the filtered array
        for (var i = 0; i < paths.Length; i++)
        {
            var src = active.Modes[active.Paths[i].sourceInfo.modeInfoIdx];
            paths[i].sourceInfo.modeInfoIdx = (uint)Array.FindIndex(modes, m => m.id == src.id && m.adapterId.Equals(src.adapterId));
        }
        foreach (var allow in new[] { false, true })
        {
            var flags = RoomSwitcher.Windows.Native.DisplayConfigApi.SDC_VALIDATE | RoomSwitcher.Windows.Native.DisplayConfigApi.SDC_USE_SUPPLIED_DISPLAY_CONFIG
                        | (allow ? RoomSwitcher.Windows.Native.DisplayConfigApi.SDC_ALLOW_CHANGES : 0);
            var err = RoomSwitcher.Windows.Native.DisplayConfigApi.SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, flags);
            Console.WriteLine($"{label}, allow changes {allow}: {(err == 0 ? "OK" : $"error {err}")}");
        }
    }
}

static class RawInputList
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct RAWINPUTDEVICELIST { public IntPtr Device; public uint Type; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetRawInputDeviceList([System.Runtime.InteropServices.Out] RAWINPUTDEVICELIST[]? list, ref uint count, uint size);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern uint GetRawInputDeviceInfoW(IntPtr device, uint command, char[]? data, ref uint size);

    public static IEnumerable<string> Names()
    {
        uint count = 0;
        var size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<RAWINPUTDEVICELIST>();
        GetRawInputDeviceList(null, ref count, size);
        var list = new RAWINPUTDEVICELIST[count];
        GetRawInputDeviceList(list, ref count, size);
        foreach (var d in list.Where(d => d.Type <= 1)) // mouse = 0, keyboard = 1
        {
            uint chars = 0;
            GetRawInputDeviceInfoW(d.Device, 0x20000007, null, ref chars);
            var buffer = new char[chars];
            GetRawInputDeviceInfoW(d.Device, 0x20000007, buffer, ref chars);
            yield return new string(buffer).TrimEnd('\0');
        }
    }
}
