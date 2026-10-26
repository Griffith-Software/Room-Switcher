using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using RoomSwitcher.Core.Cli;

namespace RoomSwitcher.App;

internal static class PipeNames
{
    // Per user and per sign-in session, so two people signed in at once don't reach each other's copy.
    public static string Current =>
        $"GriffithSoftware.RoomSwitcher.{WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName}.{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
}

/// <summary>Sends a command to the copy of Room Switcher already running in the tray.</summary>
internal static class PipeClient
{
    public static (int ExitCode, string Message)? Send(Command command, int connectTimeoutMs = 3000)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeNames.Current, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(connectTimeoutMs);
            var request = Encoding.UTF8.GetBytes(PipeProtocol.EncodeRequest(command) + "\n");
            pipe.Write(request);
            pipe.Flush();
            using var reader = new StreamReader(pipe, Encoding.UTF8);
            return PipeProtocol.DecodeReply(reader.ReadToEnd());
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Listens for commands from later copies of RoomSwitcher.exe and runs them on the UI thread.</summary>
internal sealed class PipeServer : IDisposable
{
    private readonly Func<Command, Task<(int, string)>> _handler;
    private readonly SynchronizationContext _ui;
    private readonly CancellationTokenSource _stop = new();

    public PipeServer(Func<Command, Task<(int, string)>> handler, SynchronizationContext ui)
    {
        _handler = handler;
        _ui = ui;
        _ = Task.Run(ListenAsync);
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeNames.Current, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);

                string line;
                using (var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true))
                    line = await reader.ReadLineAsync(_stop.Token) ?? "";

                var command = PipeProtocol.DecodeRequest(line);
                var reply = command.Error != null
                    ? (2, command.Error)
                    : await RunOnUiAsync(command);

                var bytes = Encoding.UTF8.GetBytes(PipeProtocol.EncodeReply(reply.Item1, reply.Item2));
                await pipe.WriteAsync(bytes, _stop.Token);
                await pipe.FlushAsync(_stop.Token);
                pipe.WaitForPipeDrain();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // A client that hung up early must not stop the listener.
                await Task.Delay(200);
            }
        }
    }

    private Task<(int, string)> RunOnUiAsync(Command command)
    {
        var done = new TaskCompletionSource<(int, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(async _ =>
        {
            try { done.SetResult(await _handler(command)); }
            catch (Exception e) { done.SetResult((1, e.Message)); }
        }, null);
        return done.Task;
    }

    public void Dispose()
    {
        _stop.Cancel();
    }
}
