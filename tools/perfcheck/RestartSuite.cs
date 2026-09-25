using System.Diagnostics;
using System.IO.Pipes;
using DispCtrl.Control;
using DispCtrl.Core.Settings;

namespace DispCtrl.PerfCheck;

/// <summary>
/// The engine's life cycle: a graceful stop, then a start through its sign-in
/// task, timed to each service coming up from the engine's own log. Never
/// kills it - a killed engine strands a hidden taskbar off-screen.
/// </summary>
internal static class RestartSuite
{
    private const string S = "restart";

    public static void Run(Context context)
    {
        Process? running = EngineSuite.Find();
        if (running is null) { context.Report.Note(S, "no engine running; skipped"); return; }
        string path = running.MainModule!.FileName;

        // Stop: the stop verb signals the running engine and it unwinds.
        long t0 = Stopwatch.GetTimestamp();
        using (Process stop = Process.Start(new ProcessStartInfo(path, "stop") { UseShellExecute = false, CreateNoWindow = true })!) stop.WaitForExit();
        if (!running.WaitForExit(20000)) throw new InvalidOperationException("the engine did not stop within 20 s");
        double stopped = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        running.Dispose();
        context.Add(new Row(S, "graceful stop (signal -> process gone)", "ms", stopped, stopped, stopped, 1, 3000, "taskbars put back first"));

        // Start, the way sign-in starts it.
        var log = new EngineLog();
        DateTime started = DateTime.Now;
        t0 = Stopwatch.GetTimestamp();
        using (Process task = Process.Start(new ProcessStartInfo("schtasks.exe", "/run /tn DispCtrl.Engine") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!)
        {
            task.WaitForExit();
            if (task.ExitCode != 0)
            {
                context.Report.Note(S, "no DispCtrl.Engine task; started directly");
                using (Process.Start(new ProcessStartInfo(path, "run") { UseShellExecute = false, CreateNoWindow = true })) { }
            }
        }
        Process? engine = null;
        double? process = Bench.Until(() => (engine = EngineSuite.Find()) is not null, 15000, t0);
        if (engine is null || process is not double up) throw new InvalidOperationException("the engine did not start within 15 s");
        context.Add(new Row(S, "start: task -> process exists", "ms", up, up, up, 1, 1500));
        double? broker = Bench.Until(() => BrokerAnswers(), 15000, t0);
        if (broker is double b) context.Add(new Row(S, "start: task -> command broker answers", "ms", b, b, b, 1, 2500));

        // Services start in parallel and log in no fixed order.
        var expected = new List<(string Text, string Name)> { ("taskbar manager started", "taskbar manager running"), ("hotkeys:", "hotkeys registered") };
        if (SettingsStore.Load().Global.QuickPanel.Enabled) expected.Add(("tray: icon shown", "tray icon shown"));
        Dictionary<string, DateTime> seen = log.WaitForAll([.. expected.Select(e => e.Text)], 10000);
        foreach (var (text, name) in expected)
        {
            if (seen.TryGetValue(text, out DateTime at))
            {
                double ms = (at - started).TotalMilliseconds;
                context.Add(new Row(S, $"start: task -> {name}", "ms", ms, ms, ms, 1, 3000));
            }
            else context.Report.Note(S, $"'{text}' not logged within 10 s");
        }

        // What starting cost, and what it settles to.
        double sinceStart = Math.Max(0, 10000 - Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
        Thread.Sleep((int)sinceStart);
        ProcessSample ten = Native.Sample(engine);
        double cpu = ten.Cycles / Native.CyclesPerMs;
        context.Add(new Row(S, "first 10 s: CPU", "ms", cpu, cpu, cpu, 1, 2500, "whole process lifetime so far"));
        double peak = Native.PeakWorkingSet(engine.Handle) / 1048576.0;
        context.Add(new Row(S, "first 10 s: peak working set", "MB", peak, peak, peak, 1, 150));
        EngineSuite.Idle(context, S, engine, context.Options.Seconds(15), "after start, settled", 60, 10);
        EngineSuite.Resident(context, S, engine, "after start: ");
        engine.Dispose();
    }

    private static bool BrokerAnswers()
    {
        try
        {
            // A real request: a connection dropped unanswered is an error in the engine's log.
            using var pipe = new NamedPipeClientStream(".", ControlTransport.PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
            pipe.Connect(50);
            using var timeout = new CancellationTokenSource(3000);
            ControlTransport.WriteAsync(pipe, new() { ["version"] = 1, ["command"] = "focus.get", ["args"] = new System.Text.Json.Nodes.JsonObject() }, timeout.Token).GetAwaiter().GetResult();
            return ControlTransport.ReadAsync(pipe, timeout.Token).GetAwaiter().GetResult()["ok"]?.GetValue<bool>() == true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException) { return false; }
    }
}
