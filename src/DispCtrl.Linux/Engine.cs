using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DispCtrl.Linux;

/// <summary>
/// The resident half of `dispctrl-linux engine`: a Unix domain socket at
/// $XDG_RUNTIME_DIR/dispctrl-linux.sock (falling back to /tmp if that
/// variable is unset) that accepts one newline-delimited JSON request per
/// line - <c>{"args":["brightness","70","--ddc","1"]}</c> - and replies with
/// <c>{"exitCode":0,"stdout":"...","stderr":""}</c>, running the same
/// <see cref="Program.Dispatch"/> the CLI uses so there is exactly one
/// implementation of every command, not two. This is the "Unix socket
/// broker" from docs/design/LINUX-PORT.md's "Engine and IPC" section and the
/// step 4 skeleton mentioned there - a real, working broker for the four
/// commands the CLI already has, not a stub, but still missing the parts
/// that make it an actual always-on engine: no state reconciliation, no
/// scheduling, no presets, no client library other than "connect and write
/// a line of JSON" - see docs/LINUX.md for exactly what talks to it today
/// (nothing yet; it is dial-in only, verified with `socat` and `nc -U`).
/// </summary>
internal static class Engine
{
    internal static string SocketPath =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath(), "dispctrl-linux.sock");

    public static int Run()
    {
        string path = SocketPath;
        if (File.Exists(path)) File.Delete(path);

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(8);
        Console.WriteLine($"dispctrl-linux engine: listening on {path} (Ctrl+C to stop)");

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; listener.Close(); };

        try
        {
            while (true)
            {
                Socket client;
                try { client = listener.Accept(); }
                catch (SocketException) { break; } // listener closed by Ctrl+C
                catch (ObjectDisposedException) { break; }

                using (client)
                using (var stream = new NetworkStream(client, ownsSocket: false))
                using (var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true })
                {
                    string? line = reader.ReadLine();
                    if (line is null) continue;
                    writer.WriteLine(HandleRequest(line));
                }
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }

        return 0;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string HandleRequest(string requestJson)
    {
        string[] args;
        try
        {
            var request = JsonSerializer.Deserialize<EngineRequest>(requestJson, JsonOptions);
            args = request?.Args ?? [];
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new EngineResponse(2, "", "engine: malformed request (expected {\"args\":[...]})"), JsonOptions);
        }

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var (originalOut, originalErr) = (Console.Out, Console.Error);
        int exitCode;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            exitCode = Program.Dispatch(args);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }

        return JsonSerializer.Serialize(new EngineResponse(exitCode, stdout.ToString(), stderr.ToString()), JsonOptions);
    }

    private sealed record EngineRequest(string[]? Args);
    private sealed record EngineResponse(int ExitCode, string Stdout, string Stderr);
}
