using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DispCtrl.Linux.Commands;
using DispCtrl.Linux.Engine;
using DispCtrl.Linux.Hardware;
using DispCtrl.Linux.Ramps;
using DispCtrl.Linux.Settings;

namespace DispCtrl.Linux;

/// <summary>
/// <c>dispctrl-linux engine</c>: the resident process. It serves every command
/// over a Unix socket (one newline-delimited JSON request, one reply) and owns
/// the gamma ramps: night light follows its schedule, a ramp that a mode change
/// or hot-plug reset is put back, and stopping it puts every ramp it warmed or
/// dimmed back to normal.
/// </summary>
/// <remarks>
/// Commands run one at a time behind <see cref="_gate"/>: ddcutil serialises a
/// bus itself, but a ramp pass and a command changing the same setting must
/// not interleave. Output goes to per-request writers, never through
/// <see cref="Console"/>, which is process-wide.
/// </remarks>
internal sealed class EngineHost
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<string> _owned = Runtime.LoadOwnedRamps();
    private DateTime _lastPass;

    public static int Run()
    {
        string path = Runtime.SocketPath;
        if (path.Length > Runtime.MaxSocketPath)
        {
            Console.Error.WriteLine($"dispctrl-linux engine: the socket path is too long for a Unix socket ({path.Length} > {Runtime.MaxSocketPath}): {path}");
            return CommandRunner.Refused;
        }
        if (EngineClient.IsRunning(path))
        {
            Console.Error.WriteLine($"dispctrl-linux engine: already running ({path})");
            return CommandRunner.Refused;
        }
        return new EngineHost().Serve(path);
    }

    private int Serve(string path)
    {
        // A socket file nobody answers on was left by an engine that was killed.
        if (File.Exists(path)) File.Delete(path);

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        // bind() honours the umask, which left it group-writable (0775).
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        listener.Listen(16);

        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);
        using var hangup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, Stop);
        using var watcher = WatchSettings();

        Log($"dispctrl-linux engine {CommandRunner.Version}: listening on {path}");
        if (!GammaRamp.IsAvailable(out var reason))
            Log($"no ramps yet: {reason}. They follow once a client brings an X display, or $DISPLAY is set.");

        var reconcile = Task.Run(ReconcileLoop);
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                Socket client;
                try { client = listener.AcceptAsync(_stop.Token).AsTask().GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { break; }
                catch (SocketException ex) { Log($"accept failed: {ex.Message}"); continue; }
                _ = Task.Run(() => Serve(client));
            }
        }
        finally
        {
            _stop.Cancel();
            try { reconcile.Wait(5000); } catch (AggregateException) { }
            _gate.Wait(5000);
            RampApplier.Release(_owned);
            _owned.Clear();
            Runtime.SaveOwnedRamps(_owned);
            if (File.Exists(path)) File.Delete(path);
            Log("stopped; ramps put back");
        }
        return CommandRunner.Done;
    }

    private void Stop(PosixSignalContext context)
    {
        context.Cancel = true;
        _stop.Cancel();
    }

    private void Serve(Socket client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                using var stream = new NetworkStream(client, ownsSocket: false);
                using var reader = new StreamReader(stream, utf8);
                using var writer = new StreamWriter(stream, utf8) { AutoFlush = true };
                var line = reader.ReadLine();
                if (line is null) return;
                writer.WriteLine(JsonSerializer.Serialize(Handle(line), EngineClient.Json));
            }
            catch (IOException) { }
            catch (SocketException) { }
        }
    }

    private EngineReply Handle(string line)
    {
        EngineRequest? request;
        try { request = JsonSerializer.Deserialize<EngineRequest>(line, EngineClient.Json); }
        catch (JsonException) { request = null; }
        if (request?.Args is not { } args)
            return new EngineReply(CommandRunner.AskedWrongly, "", "engine: malformed request (expected {\"args\":[...]})\n");

        AdoptDisplay(request.Env);

        if (args is ["engine", "stop"])
        {
            _stop.Cancel();
            return new EngineReply(CommandRunner.Done, "Stopping the engine; ramps go back to normal.\n", "");
        }
        if (args is ["engine", "status"])
            return new EngineReply(CommandRunner.Done, Describe(), "");

        var output = new StringWriter();
        var error = new StringWriter();
        _gate.Wait();
        try
        {
            var context = new CommandContext(output, error, InEngine: true, ApplyRampsLocked);
            int code = CommandRunner.Run(args, context);
            return new EngineReply(code, output.ToString(), error.ToString());
        }
        catch (Exception ex)
        {
            Log($"command '{string.Join(' ', args)}' failed: {ex}");
            return new EngineReply(CommandRunner.Refused, output.ToString(), error + $"engine: {ex.Message}\n");
        }
        finally { _gate.Release(); }
    }

    private string Describe()
    {
        var since = _lastPass == default ? "not yet" : $"{_lastPass:HH:mm:ss}";
        return $"engine       running, {CommandRunner.Version}, pid {Environment.ProcessId}\n"
            + $"socket       {Runtime.SocketPath}\n"
            + $"last pass    {since}\n"
            + $"ramps held   {(_owned.Count == 0 ? "none" : string.Join(", ", _owned))}\n";
    }

    /// <summary>An engine started by systemd before the session exported
    /// <c>DISPLAY</c> has no X server to talk to; the first client that has one
    /// lends it. libX11 reads <c>XAUTHORITY</c> from the C environment, which
    /// <see cref="Environment.SetEnvironmentVariable(string, string)"/> does not
    /// reach, hence setenv.</summary>
    private void AdoptDisplay(Dictionary<string, string>? env)
    {
        if (env is null || GammaRamp.DisplayName is not null) return;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return;
        if (!env.TryGetValue("DISPLAY", out var display) || string.IsNullOrEmpty(display)) return;

        if (env.TryGetValue("XAUTHORITY", out var auth) && !string.IsNullOrEmpty(auth)) setenv("XAUTHORITY", auth, 1);
        setenv("DISPLAY", display, 1);
        Environment.SetEnvironmentVariable("DISPLAY", display);
        GammaRamp.DisplayName = display;
        Log($"using X display {display}, from a client");
        _wake.Release();
    }

    private IReadOnlyList<RampChange> ApplyRampsLocked()
    {
        var changes = RampApplier.Apply(SettingsStore.Load(), TimeOnly.FromDateTime(DateTime.Now), _owned);
        Runtime.SaveOwnedRamps(_owned);
        _lastPass = DateTime.Now;
        foreach (var c in changes) LogChange(c);
        return changes;
    }

    /// <summary>A pass every ten seconds, at each schedule boundary, and whenever
    /// settings change. Ten seconds bounds how long a hot-plugged monitor or a
    /// mode change shows an unwarmed ramp; a pass that finds every ramp in place
    /// writes nothing.</summary>
    private void ReconcileLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            LinuxSettings settings;
            _gate.Wait();
            try
            {
                settings = SettingsStore.Load();
                ApplyRampsLocked();
            }
            catch (Exception ex)
            {
                Log($"ramp pass failed: {ex.Message}");
                settings = new LinuxSettings();
            }
            finally { _gate.Release(); }

            var wait = TimeSpan.FromSeconds(10);
            var n = settings.NightLight;
            if (n.Enabled && n.Scheduled && Schedule.TryParse(n.From, out var from) && Schedule.TryParse(n.To, out var to))
            {
                var untilBoundary = Schedule.NextBoundary(DateTime.Now, from, to) - DateTime.Now + TimeSpan.FromMilliseconds(200);
                if (untilBoundary < wait) wait = untilBoundary;
            }
            try { _wake.Wait(wait, _stop.Token); }
            catch (OperationCanceledException) { break; }
            while (_wake.CurrentCount > 0) _wake.Wait(0);
        }
    }

    /// <summary>A DispCtrl save is a rename onto settings.json and wakes the pass
    /// at once; an editor writing in place raises several changes mid-write, so
    /// those wait 120 ms for the file to be whole - the Windows engine's rule.</summary>
    private FileSystemWatcher? WatchSettings()
    {
        try
        {
            System.IO.Directory.CreateDirectory(SettingsStore.Directory);
            var watcher = new FileSystemWatcher(SettingsStore.Directory, "settings.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            Timer? debounce = null;
            watcher.Renamed += (_, _) => _wake.Release();
            watcher.Created += (_, _) => _wake.Release();
            watcher.Changed += (_, _) =>
            {
                debounce?.Dispose();
                debounce = new Timer(_ => _wake.Release(), null, 120, Timeout.Infinite);
            };
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
        {
            Log($"not watching settings ({ex.Message}); changes apply on the next pass");
            return null;
        }
    }

    private static void LogChange(RampChange change)
    {
        var t = change.Target;
        if (!change.Written) Log($"{change.Output}: ramp refused: {change.Error}");
        else if (t.IsIdentity) Log($"{change.Output}: ramp back to normal");
        else Log($"{change.Output}: ramp {t.Red:0.000} {t.Green:0.000} {t.Blue:0.000} x {t.Dim:0.00}");
    }

    // journald stamps each line itself; a terminal run reads the same.
    private static void Log(string message) => Console.Error.WriteLine(message);

    [DllImport("libc", SetLastError = true)]
    private static extern int setenv([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int overwrite);
}
