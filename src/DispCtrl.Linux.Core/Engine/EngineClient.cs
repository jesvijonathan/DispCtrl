using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DispCtrl.Linux.Engine;

/// <summary>One command sent to the engine: its arguments, and the client's X
/// display so an engine started before the session exported one can still
/// reach it.</summary>
public sealed record EngineRequest(string[]? Args, Dictionary<string, string>? Env = null);

/// <summary>What a command printed and how it ended, exactly as the command
/// line would have.</summary>
public sealed record EngineReply(int ExitCode, string Stdout, string Stderr);

/// <summary>Talks to a running <c>dispctrl-linux engine</c>.</summary>
/// <remarks>
/// Like <c>dispctrl.exe</c> and the Windows engine's pipe: a client sends its
/// command to the engine when one is running, so there is one writer of the
/// ramps and one queue in front of the DDC/CI channel, and runs it itself when
/// none is. A socket file with nobody listening (an engine that was killed)
/// counts as none.
/// </remarks>
public static class EngineClient
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static bool IsRunning(string? socketPath = null)
    {
        using var socket = TryConnect(socketPath ?? Runtime.SocketPath, 300);
        return socket is not null;
    }

    /// <summary>Sends <paramref name="args"/> and returns the engine's reply, or
    /// null when no engine is listening. A reply slower than
    /// <paramref name="timeoutMs"/> (a ddcutil call on a slow monitor waits in
    /// the queue) is reported as refused rather than run a second time here.</summary>
    public static EngineReply? TrySend(string[] args, string? socketPath = null, int timeoutMs = 60_000)
    {
        using var socket = TryConnect(socketPath ?? Runtime.SocketPath, 300);
        if (socket is null) return null;

        var env = new Dictionary<string, string>();
        foreach (var name in new[] { "DISPLAY", "XAUTHORITY" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value)) env[name] = value;
        }

        try
        {
            socket.ReceiveTimeout = timeoutMs;
            socket.SendTimeout = 5000;
            using var stream = new NetworkStream(socket, ownsSocket: false);
            using var writer = new StreamWriter(stream, Utf8) { AutoFlush = true };
            using var reader = new StreamReader(stream, Utf8);
            writer.WriteLine(JsonSerializer.Serialize(new EngineRequest(args, env), Json));
            var line = reader.ReadLine();
            if (line is null) return new EngineReply(1, "", "dispctrl-linux: the engine closed the connection without answering\n");
            return JsonSerializer.Deserialize<EngineReply>(line, Json)
                ?? new EngineReply(1, "", "dispctrl-linux: the engine sent an empty answer\n");
        }
        catch (IOException)
        {
            return new EngineReply(1, "", $"dispctrl-linux: the engine did not answer within {timeoutMs / 1000} s\n");
        }
        catch (JsonException)
        {
            return new EngineReply(1, "", "dispctrl-linux: the engine's answer was not understood (a different version?)\n");
        }
    }

    private static Socket? TryConnect(string path, int timeoutMs)
    {
        if (path.Length > Runtime.MaxSocketPath || !File.Exists(path)) return null;
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            var connect = socket.ConnectAsync(new UnixDomainSocketEndPoint(path));
            if (connect.Wait(timeoutMs) && socket.Connected) return socket;
        }
        catch (AggregateException) { }
        catch (SocketException) { }
        socket.Dispose();
        return null;
    }
}
