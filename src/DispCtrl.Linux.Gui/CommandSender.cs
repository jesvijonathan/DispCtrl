using Avalonia.Threading;
using DispCtrl.Linux.Commands;
using DispCtrl.Linux.Engine;

namespace DispCtrl.Linux.Gui;

/// <summary>Runs commands for the window: through the engine when it is
/// running, here otherwise - exactly what <c>dispctrl-linux</c> does.</summary>
/// <remarks>
/// A ddcutil write takes a few hundred milliseconds, and a slider drag asks for
/// dozens a second. Sent from the UI thread, every step froze the window; sent
/// as they came, they queued behind each other and the monitor kept moving
/// long after the pointer stopped. So each target (one monitor, one output, the
/// night light) has one call in flight at a time and only its newest value
/// waits - the Windows brightness bridge's throttle, not a debounce, so the
/// first step moves at once.
/// </remarks>
public sealed class CommandSender
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, (string[] Args, Action<string?>? Done)> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _running = new(StringComparer.Ordinal);

    /// <summary>Queues <paramref name="args"/> for <paramref name="key"/>,
    /// replacing whatever was waiting for it. <paramref name="done"/> runs on
    /// the UI thread with null when it succeeded, or the reason it did not.</summary>
    public void Post(string key, string[] args, Action<string?>? done = null)
    {
        lock (_lock)
        {
            _pending[key] = (args, done);
            if (!_running.Add(key)) return;
        }
        _ = Task.Run(() => Drain(key));
    }

    /// <summary>Runs one command now, off the UI thread.</summary>
    public static Task<EngineReply> RunAsync(string[] args) => Task.Run(() => Run(args));

    private void Drain(string key)
    {
        while (true)
        {
            (string[] Args, Action<string?>? Done) next;
            lock (_lock)
            {
                if (!_pending.Remove(key, out next))
                {
                    _running.Remove(key);
                    return;
                }
            }
            var reply = Run(next.Args);
            string? error = reply.ExitCode == CommandRunner.Done
                ? null
                : FirstLine(reply.Stderr) ?? $"refused ({reply.ExitCode})";
            if (next.Done is { } done) Dispatcher.UIThread.Post(() => done(error));
        }
    }

    private static EngineReply Run(string[] args)
    {
        if (EngineClient.TrySend(args) is { } reply) return reply;
        var output = new StringWriter();
        var error = new StringWriter();
        int code = CommandRunner.Run(args, CommandContext.Local(output, error));
        return new EngineReply(code, output.ToString(), error.ToString());
    }

    private static string? FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()?.Replace("dispctrl-linux: ", "", StringComparison.Ordinal);
}
