using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using DispCtrl.Control;
using DispCtrl.Core.Settings;

namespace DispCtrl.PerfCheck;

/// <summary>
/// The resident engine as it runs now: what it holds, what it costs idle, how
/// fast the broker answers and how soon a saved setting reaches every service.
/// </summary>
internal static class EngineSuite
{
    private const string S = "engine";

    public static Process? Find()
    {
        Process[] all = Process.GetProcessesByName("DispCtrl.Engine");
        foreach (Process extra in all.Skip(1)) extra.Dispose();
        return all.FirstOrDefault();
    }

    public static void Run(Context context)
    {
        Options o = context.Options;
        using Process? engine = Find();
        if (engine is null) { context.Report.Note(S, "no engine running; skipped (start it: build.cmd run engine)"); return; }
        context.Report.Note(S, $"pid {engine.Id}, {engine.MainModule?.FileName}");

        Resident(context, S, engine, "");
        Idle(context, S, engine, o.Seconds(30), "idle", 60, 10);

        // The broker: a named pipe per session, four listeners.
        var client = new ControlClient();
        JsonObject Request(string command) => new() { ["version"] = 1, ["command"] = command, ["args"] = new JsonObject() };
        JsonObject probe = client.ExecuteAsync(Request("focus.get")).GetAwaiter().GetResult();
        context.Add(Bench.Time(S, "broker: focus.get round trip", o.N(40), 3, 10.0, () => client.ExecuteAsync(Request("focus.get")).GetAwaiter().GetResult()));
        context.Add(Bench.Time(S, "broker: status round trip", o.N(20), 2, 150.0, () => client.ExecuteAsync(Request("status")).GetAwaiter().GetResult()));
        context.Add(Bench.Time(S, "broker: displays.list round trip", o.N(10), 1, 250.0, () => client.ExecuteAsync(Request("displays.list")).GetAwaiter().GetResult()));
        context.Add(Bench.Time(S, "in-process: status (no broker)", o.N(10), 1, 150.0, () => new ControlService().Execute(Request("status"))));
        if (probe["ok"]?.GetValue<bool>() != true) context.Report.Note(S, "broker answered not-ok: " + probe.ToJsonString());

        Sync(context, engine);
        Invocation(context);
    }

    /// <summary>
    /// A setting changed the way a person or script changes it, timed to the
    /// engine having applied it (its "every service reloaded" line). The value
    /// written is the one already there - focus dimming's level - so the save,
    /// the watcher and every service's reload run and nothing on screen moves.
    /// </summary>
    private static void Invocation(Context context)
    {
        string cli = context.Output("DispCtrl.Cli", "dispctrl.exe");
        int dim = SettingsStore.Load().Global.Focus.DimPercent;
        var viaCli = new List<double>(); var viaBroker = new List<double>();
        var client = new ControlClient();
        for (int i = 0; i < context.Options.N(6); i++)
        {
            Thread.Sleep(600);
            var log = new EngineLog();
            DateTime wall = DateTime.Now;
            var start = new ProcessStartInfo(cli) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            foreach (string a in new[] { "focus", "set", "--dim-percent", dim.ToString(CultureInfo.InvariantCulture), "--json" }) start.ArgumentList.Add(a);
            using (Process p = Process.Start(start)!) { _ = p.StandardOutput.ReadToEnd(); p.WaitForExit(); }
            if (log.WaitFor("hotkeys:", 3000) is DateTime a1) viaCli.Add((a1 - wall).TotalMilliseconds);

            Thread.Sleep(600);
            log = new EngineLog();
            wall = DateTime.Now;
            _ = client.ExecuteAsync(new JsonObject { ["version"] = 1, ["command"] = "focus.set", ["args"] = new JsonObject { ["dimPercent"] = dim } }).GetAwaiter().GetResult();
            if (log.WaitFor("hotkeys:", 3000) is DateTime a2) viaBroker.Add((a2 - wall).TotalMilliseconds);
        }
        if (viaCli.Count > 0) context.Add(Stats.Row(S, "invoke: dispctrl focus set -> engine applied", "ms", viaCli, 300, "process start + command + save + reload"));
        if (viaBroker.Count > 0) context.Add(Stats.Row(S, "invoke: app/broker focus.set -> engine applied", "ms", viaBroker, 60, "the app's path"));
        if (viaCli.Count + viaBroker.Count == 0) context.Report.Note(S, "invoke: the engine logged no reload for an unchanged value");
    }

    /// <summary>What the process holds right now.</summary>
    public static void Resident(Context context, string suite, Process process, string prefix, double wsBudget = 80, double privateBudget = 120)
    {
        ProcessSample s = Native.Sample(process);
        context.Add(new Row(suite, prefix + "working set", "MB", s.WorkingSet / 1048576.0, s.WorkingSet / 1048576.0, s.WorkingSet / 1048576.0, 1, wsBudget));
        context.Add(new Row(suite, prefix + "private bytes", "MB", s.Private / 1048576.0, s.Private / 1048576.0, s.Private / 1048576.0, 1, privateBudget));
        context.Add(new Row(suite, prefix + "handles", "count", s.Handles, s.Handles, s.Handles, 1, 2000));
        context.Add(new Row(suite, prefix + "threads", "count", s.Threads, s.Threads, s.Threads, 1, 60));
        context.Add(new Row(suite, prefix + "GDI objects", "count", s.Gdi, s.Gdi, s.Gdi, 1, 300));
        context.Add(new Row(suite, prefix + "USER objects", "count", s.User, s.User, s.User, 1, 300));
    }

    /// <summary>CPU per minute and wake-ups per second over a quiet window, in one-second slices.</summary>
    public static void Idle(Context context, string suite, Process process, int seconds, string label, double cpuBudget, double wakeBudget)
    {
        var cpu = new List<double>(); var wakes = new List<double>();
        ProcessSample previous = Native.Sample(process);
        for (int i = 0; i < seconds; i++)
        {
            Thread.Sleep(1000);
            ProcessSample now = Native.Sample(process);
            double span = now.SecondsSince(previous);
            cpu.Add(now.CpuMsSince(previous) / span * 60);
            if (now.SwitchesSince(previous) is long switches) wakes.Add(switches / span);
            previous = now;
        }
        context.Add(Stats.Row(suite, $"{label}: CPU", "ms/min", cpu, cpuBudget, "per 1 s slice"));
        context.Add(Stats.Row(suite, $"{label}: wake-ups (context switches)", "/s", wakes, wakeBudget));
    }

    /// <summary>
    /// Saves the settings unchanged and times the engine's reload from its own
    /// log. The debounce (120 ms) is in the number by design: it is what a
    /// person waits for between a toggle and the desk following it.
    /// </summary>
    private static void Sync(Context context, Process engine)
    {
        var seen = new List<double>(); var done = new List<double>(); var cost = new List<double>();
        for (int i = 0; i < context.Options.N(8); i++)
        {
            Thread.Sleep(600);
            var log = new EngineLog();
            ProcessSample before = Native.Sample(engine);
            DateTime saved = DateTime.Now;
            SettingsStore.Save(SettingsStore.Load());
            // "settings reloaded" is the taskbar manager, first; "hotkeys:" the last service to log.
            if (log.WaitFor("settings reloaded", 3000) is not DateTime first) { context.Report.Note(S, "sync: no reload logged within 3 s"); return; }
            seen.Add((first - saved).TotalMilliseconds);
            if (log.WaitFor("hotkeys:", 1000) is DateTime last) done.Add((last - saved).TotalMilliseconds);
            Thread.Sleep(300);
            cost.Add(Native.Sample(engine).CpuMsSince(before));
        }
        context.Add(Stats.Row(S, "sync: save -> engine reload starts", "ms", seen, 60.0, "on the rename; in-place edits wait 120 ms"));
        if (done.Count > 0) context.Add(Stats.Row(S, "sync: save -> every service reloaded", "ms", done, 80.0));
        context.Add(Stats.Row(S, "sync: engine CPU per settings reload", "ms", cost, 60.0));
    }
}

/// <summary>Follows engine.log from where it ended when constructed.</summary>
internal sealed class EngineLog
{
    private long _offset;
    private readonly StringBuilder _pending = new();
    // Lines read but not yet matched: a batch can hold the next line waited for.
    private readonly Queue<string> _unread = new();

    public EngineLog() => _offset = File.Exists(SettingsStore.LogPath) ? new FileInfo(SettingsStore.LogPath).Length : 0;

    /// <summary>The engine's timestamp of the first new line containing each text, in whatever order they come.</summary>
    public Dictionary<string, DateTime> WaitForAll(IReadOnlyCollection<string> texts, int timeoutMs)
    {
        var found = new Dictionary<string, DateTime>();
        long start = Stopwatch.GetTimestamp();
        while (found.Count < texts.Count && Stopwatch.GetElapsedTime(start).TotalMilliseconds < timeoutMs)
        {
            foreach (string line in ReadNew())
                foreach (string text in texts)
                    if (!found.ContainsKey(text) && line.Contains(text, StringComparison.Ordinal) && Stamp(line) is DateTime when)
                        found[text] = when;
            Thread.Sleep(5);
        }
        return found;
    }

    private static DateTime? Stamp(string line) =>
        line.Length > 23 && DateTime.TryParseExact(line[..23], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when) ? when : null;

    /// <summary>The engine's own timestamp of the next line containing <paramref name="text"/>.</summary>
    public DateTime? WaitFor(string text, int timeoutMs)
    {
        long start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < timeoutMs)
        {
            foreach (string line in ReadNew()) _unread.Enqueue(line);
            while (_unread.TryDequeue(out string? line))
                if (line.Contains(text, StringComparison.Ordinal) && line.Length > 23
                    && DateTime.TryParseExact(line[..23], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when))
                    return when;
            Thread.Sleep(5);
        }
        return null;
    }

    private List<string> ReadNew()
    {
        var lines = new List<string>();
        if (!File.Exists(SettingsStore.LogPath)) return lines;
        using var file = new FileStream(SettingsStore.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length < _offset) _offset = 0; // rolled over
        file.Position = _offset;
        using var reader = new StreamReader(file, Encoding.UTF8);
        _pending.Append(reader.ReadToEnd());
        _offset = file.Length;
        string text = _pending.ToString();
        int end = text.LastIndexOf('\n');
        if (end < 0) return lines;
        lines.AddRange(text[..end].Split('\n').Select(l => l.TrimEnd('\r')));
        _pending.Clear().Append(text[(end + 1)..]);
        return lines;
    }
}
