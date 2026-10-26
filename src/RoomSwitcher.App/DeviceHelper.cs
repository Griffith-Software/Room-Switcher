using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using RoomSwitcher.Core.Devices;
using RoomSwitcher.Windows.Devices;

namespace RoomSwitcher.App;

/// <summary>
/// Turning devices on and off needs administrator rights. After a one-time approval, a copy of
/// RoomSwitcher.exe lives in Program Files (only administrators can change it) and a scheduled task
/// runs it with full rights on request. The tray writes what it wants to a request file; the helper
/// re-lists the devices itself and only ever switches USB/Bluetooth keyboards, mice, cameras, USB
/// audio and USB storage.
/// </summary>
internal sealed class DeviceHelper : IDeviceBackend
{
    public const string ApplyArg = "--apply-devices";
    public const string InstallArg = "--install-device-helper";
    public const string RemoveArg = "--remove-device-helper";

    private const string TaskName = @"\Room Switcher\Device switching";

    private static string HelperDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Room Switcher");
    private static string HelperExe => Path.Combine(HelperDirectory, "RoomSwitcher.exe");
    private static string ExchangeDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Room Switcher");
    private static string RequestPath => Path.Combine(ExchangeDirectory, "device-request.json");
    private static string ResponsePath => Path.Combine(ExchangeDirectory, "device-response.json");

    public IReadOnlyList<UsbDevice> List() => UsbDevices.List();

    public DeviceControlStatus Status
    {
        get
        {
            if (!File.Exists(HelperExe) || RunSchtasks($"/query /tn \"{TaskName}\"") != 0) return DeviceControlStatus.NotSetUp;
            var mine = FileVersionInfo.GetVersionInfo(AppHost.ExePath).ProductVersion;
            var theirs = FileVersionInfo.GetVersionInfo(HelperExe).ProductVersion;
            return string.Equals(mine, theirs, StringComparison.Ordinal) ? DeviceControlStatus.Ready : DeviceControlStatus.NeedsUpdate;
        }
    }

    public async Task<IReadOnlyList<string>> ApplyAsync(IReadOnlyList<string> enable, IReadOnlyList<string> disable, CancellationToken cancellationToken = default)
    {
        if (enable.Count == 0 && disable.Count == 0) return [];
        return await SendAsync(new DeviceRequest { Enable = enable.ToList(), Disable = disable.ToList() }, "Devices", cancellationToken);
    }

    /// <summary>Restarts the Windows Audio service through the helper. Returns plain-language problems.</summary>
    public Task<IReadOnlyList<string>> RestartAudioAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new DeviceRequest { RestartAudio = true }, "Windows Audio", cancellationToken, timeoutMs: 45000);

    private static async Task<IReadOnlyList<string>> SendAsync(DeviceRequest request, string what, CancellationToken cancellationToken, int timeoutMs = 20000)
    {
        Directory.CreateDirectory(ExchangeDirectory);
        File.Delete(ResponsePath);
        File.WriteAllText(RequestPath, DeviceMessages.Write(request));

        if (RunSchtasks($"/run /tn \"{TaskName}\"") != 0)
            return [$"{what} wasn't changed because the administrator helper couldn't start. Set it up again on the Devices step of Edit rooms…"];

        // The helper usually answers within a second or two; device drivers and services can take longer.
        for (var waited = 0; waited < timeoutMs; waited += 250)
        {
            await Task.Delay(250, cancellationToken);
            try
            {
                if (!File.Exists(ResponsePath)) continue;
                var response = DeviceMessages.ReadResponse(File.ReadAllText(ResponsePath));
                if (response?.RequestId == request.RequestId) return response.Problems;
            }
            catch (IOException) { /* still being written */ }
        }
        return [$"{what} may not have changed because the administrator helper didn't respond in time."];
    }

    // ---- one-time setup, run elevated ----

    /// <summary>Asks Windows for administrator approval (one prompt) and sets up or removes the helper.</summary>
    public static bool RunElevated(string argument, out string? error)
    {
        error = null;
        try
        {
            var user = WindowsIdentity.GetCurrent().User?.Value ?? "";
            using var process = Process.Start(new ProcessStartInfo(AppHost.ExePath, $"{argument} {user}")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            process!.WaitForExit();
            if (process.ExitCode == 0) return true;
            error = process.ExitCode == 2 ? "Windows couldn't set up device switching. Try again." : $"Couldn't change device switching (error code {process.ExitCode}). Try again.";
            return false;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            error = "Nothing was changed because the administrator prompt was cancelled.";
            return false;
        }
    }

    /// <summary>Elevated: copy this exe into Program Files and register the task for the given user.</summary>
    public static int Install(string userSid)
    {
        if (!IsElevated() || string.IsNullOrWhiteSpace(userSid)) return 1;
        Directory.CreateDirectory(HelperDirectory);
        File.Copy(AppHost.ExePath, HelperExe, overwrite: true);

        var xml = Path.Combine(Path.GetTempPath(), $"RoomSwitcherTask-{Guid.NewGuid():N}.xml");
        File.WriteAllText(xml, TaskXml(userSid), System.Text.Encoding.Unicode);
        try
        {
            return RunSchtasks($"/create /tn \"{TaskName}\" /xml \"{xml}\" /f") == 0 ? 0 : 2;
        }
        finally
        {
            File.Delete(xml);
        }
    }

    /// <summary>Elevated: remove the task and the Program Files copy.</summary>
    public static int Remove()
    {
        if (!IsElevated()) return 1;
        RunSchtasks($"/delete /tn \"{TaskName}\" /f");
        try { if (Directory.Exists(HelperDirectory)) Directory.Delete(HelperDirectory, recursive: true); }
        catch (IOException) { return 3; }
        return 0;
    }

    /// <summary>The helper itself (started by the task): do what the request file asks, if it's allowed.</summary>
    public static int Apply()
    {
        var response = new DeviceResponse();
        try
        {
            var request = DeviceMessages.ReadRequest(File.ReadAllText(RequestPath)) ?? new DeviceRequest();
            response.RequestId = request.RequestId;
            if (request.RestartAudio && RestartWindowsAudio() is { } audioProblem) response.Problems.Add(audioProblem);
            // Only the devices Room Switcher itself lists can be switched; anything else in the file is ignored.
            var devices = UsbDevices.ListPhysical().ToDictionary(p => p.Device.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var (ids, enable) in new[] { (request.Enable, true), (request.Disable, false) })
                foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!devices.TryGetValue(id, out var physical)) continue; // unplugged since, or not allowed
                    if (UsbDevices.SetEnabled(physical, enable) is { } problem) response.Problems.Add(problem);
                }
        }
        catch (Exception e)
        {
            response.Problems.Add($"Device switching ran into a problem. {e.Message}");
        }
        File.WriteAllText(ResponsePath, DeviceMessages.Write(response));
        return 0;
    }

    /// <summary>Elevated: stop and start the Windows Audio service, like Restart in services.msc.</summary>
    private static string? RestartWindowsAudio()
    {
        RunTool("net.exe", "stop Audiosrv /y"); // fails harmlessly if it's already stopped
        RunTool("net.exe", "start Audiosrv");
        // Checked rather than trusting net's exit code: something else may have started it first.
        return AudioServiceRunning() ? null : "Windows Audio didn't start again. Restart your PC to bring sound back.";
    }

    private static bool AudioServiceRunning()
    {
        using var process = Process.Start(new ProcessStartInfo("sc.exe", "query Audiosrv")
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true,
        })!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsElevated() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private static int RunSchtasks(string arguments) => RunTool("schtasks.exe", arguments);

    private static int RunTool(string tool, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(tool, arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string TaskXml(string userSid) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>Lets Room Switcher turn USB and Bluetooth devices on and off when you switch rooms, and restart Windows Audio when you ask it to.</Description>
            <Author>Griffith Software</Author>
          </RegistrationInfo>
          <Principals>
            <Principal id="Author">
              <UserId>{SecurityElement.Escape(userSid)}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>Queue</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>false</StartWhenAvailable>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>true</Hidden>
            <ExecutionTimeLimit>PT2M</ExecutionTimeLimit>
            <Priority>4</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>"{SecurityElement.Escape(HelperExe)}"</Command>
              <Arguments>{ApplyArg}</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;
}
