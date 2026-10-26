using RoomSwitcher.Core.Cli;

namespace RoomSwitcher.App;

internal static class Program
{
    private const string MutexName = @"Local\GriffithSoftware.RoomSwitcher";

    [STAThread]
    private static int Main(string[] args)
    {
        // Device switching helper modes (see DeviceHelper). They run alongside the tray copy.
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case DeviceHelper.ApplyArg: return DeviceHelper.Apply();
                case DeviceHelper.InstallArg: return DeviceHelper.Install(args.Length > 1 ? args[1] : "");
                case DeviceHelper.RemoveArg: return DeviceHelper.Remove();
            }
        }

        var command = CommandLine.Parse(args);

        if (command.Kind == CommandKind.Help || command.IsOneShot)
            ConsoleOutput.Attach();

        if (command.Kind == CommandKind.Help)
        {
            if (command.Error != null) ConsoleOutput.WriteLine(command.Error);
            ConsoleOutput.WriteLine(CommandLine.Usage);
            return command.Error == null ? 0 : 2;
        }

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var firstInstance);

        if (!firstInstance)
        {
            // Another copy is in the tray: hand the command over and print its answer.
            var reply = PipeClient.Send(command);
            if (reply is { } r)
            {
                if (command.IsOneShot) ConsoleOutput.WriteLine(r.Message);
                return r.ExitCode;
            }
            if (!command.IsOneShot) return 0;
            // The other copy didn't answer (still starting, or shutting down); do it here instead.
        }

        ApplicationConfiguration.Initialize();

        AppHost context;
        try
        {
            context = AppHost.Load(listen: !command.IsOneShot, atSignIn: command.AtSignIn);
        }
        catch (Exception e)
        {
            var text = $"Room Switcher couldn't read its settings.\n\n{e.Message}";
            if (command.IsOneShot) ConsoleOutput.WriteLine(text);
            else MessageBox.Show(text, "Room Switcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        if (command.IsOneShot)
        {
            var (code, message) = CommandRunner.RunAsync(context, command).GetAwaiter().GetResult();
            ConsoleOutput.WriteLine(message);
            return code;
        }

        using var tray = new TrayApplication(context, openSetup: command.Kind == CommandKind.Setup, atSignIn: command.AtSignIn,
            openTest: command.Kind == CommandKind.Test);
        Application.Run(tray);
        GC.KeepAlive(mutex);
        return 0;
    }
}
