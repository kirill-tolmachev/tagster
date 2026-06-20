using System.IO;
using System.IO.Pipes;

namespace Tagster.App;

/// <summary>
/// Ensures one Tagster window: later launches (e.g. context-menu clicks) hand their arguments to
/// the running instance over a named pipe instead of opening a second window.
/// </summary>
internal sealed class SingleInstanceManager : IDisposable
{
    private const string MutexName = @"Local\Tagster.SingleInstance.v1";
    private const string PipeName = "Tagster.Activation.v1";

    private Mutex? _mutex;

    /// <summary>True if this is the first instance (and now owns the window).</summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        return createdNew;
    }

    public void SignalFirstInstance(IReadOnlyList<string> args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.Write(string.Join("\n", args));
        }
        catch
        {
            // if we can't reach the first instance, the caller simply exits
        }
    }

    public void StartServer(Action<string[]> onActivated)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    var payload = reader.ReadToEnd();
                    onActivated(payload.Split('\n', StringSplitOptions.RemoveEmptyEntries));
                }
                catch
                {
                    // keep listening across malformed connections
                }
            }
        })
        {
            IsBackground = true,
            Name = "Tagster.Activation",
        };
        thread.Start();
    }

    public void Dispose() => _mutex?.Dispose();
}
