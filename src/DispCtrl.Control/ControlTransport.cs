using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DispCtrl.Core.Settings;

namespace DispCtrl.Control;

/// <summary>Length-prefixed UTF-8 JSON, bounded to 1 MiB per request or response.</summary>
public static class ControlTransport
{
    // Computed once: the listeners recreate the pipe for every connection, and
    // neither the session nor the data folder changes for the life of a process.
    public static string PipeName { get; } = "DispCtrl.Control.v1." + CurrentSessionId() + "."
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SettingsStore.Directory)))[..16];

    private static int CurrentSessionId()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }

    public const int MaxBytes = 1024 * 1024;

    public static async Task WriteAsync(Stream stream, JsonObject message, CancellationToken cancellation)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Control message exceeds 1 MiB.");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), cancellation);
        await stream.WriteAsync(bytes, cancellation);
        await stream.FlushAsync(cancellation);
    }

    public static async Task<JsonObject> ReadAsync(Stream stream, CancellationToken cancellation)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellation);
        int length = BitConverter.ToInt32(header);
        if (length is < 2 or > MaxBytes) throw new InvalidDataException("Invalid control message length.");
        byte[] bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellation);
        return JsonNode.Parse(bytes)?.AsObject() ?? throw new InvalidDataException("Expected a JSON object.");
    }
}

public sealed class ControlClient
{
    /// <summary>Only a failed connection permits local fallback. A lost reply never replays a mutation.</summary>
    public async Task<JsonObject> ExecuteAsync(JsonObject request, bool local = false, int timeoutMs = 30000,
        CancellationToken cancellation = default)
    {
        if (local) return await Task.Run(() => new ControlService().Execute(request), cancellation);
        using var pipe = new NamedPipeClientStream(".", ControlTransport.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        bool connected;
        try { await pipe.ConnectAsync(150, cancellation); connected = true; }
        catch (TimeoutException) { connected = false; }
        if (!connected) return await Task.Run(() => new ControlService().Execute(request), cancellation);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(timeoutMs);
        try
        {
            await ControlTransport.WriteAsync(pipe, request, timeout.Token);
            return await ControlTransport.ReadAsync(pipe, timeout.Token);
        }
        catch (OperationCanceledException ex) when (!cancellation.IsCancellationRequested)
        {
            throw new TimeoutException("The engine did not return before the deadline. The operation may have run; check status before retrying.", ex);
        }
        catch (IOException ex)
        {
            throw new IOException("The engine connection ended before its result arrived. The operation may have run; check status before retrying.", ex);
        }
    }
}

/// <summary>The engine owns this listener; exceptions are exposed to its log callback.</summary>
public sealed class ControlServer : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _listener;
    private readonly Action<string> _log;
    private readonly ControlService _service = new();

    public ControlServer(Action<string> log) { _log = log; _listener = Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(ListenAsync))); }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(ControlTransport.PipeName, PipeDirection.InOut, 4,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(60));
                var request = await ControlTransport.ReadAsync(pipe, deadline.Token);
                var result = _service.Execute(request);
                _log($"control {result["id"]}: {result["command"]}, exit {result["exitCode"]}, {result["elapsedMs"]} ms");
                await ControlTransport.WriteAsync(pipe, result, deadline.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception ex) { _log("Control connection: " + ex.Message); }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Wait(TimeSpan.FromSeconds(2));
        // A blocking driver may still be finishing. Its cancellation source must
        // remain alive until the listener exits; never terminate native calls.
        if (_listener.IsCompleted) _stop.Dispose();
        else _ = _listener.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
