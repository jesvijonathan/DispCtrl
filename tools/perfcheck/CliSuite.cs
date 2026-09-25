using System.Diagnostics;

namespace DispCtrl.PerfCheck;

/// <summary>
/// dispctrl.exe from process start to exit: runtime start-up, the broker
/// connection (or the local fallback), the command, and the JSON out. A script
/// calling it in a loop pays all of it every time.
/// </summary>
internal static class CliSuite
{
    private const string S = "cli";

    public static void Run(Context context)
    {
        string exe = context.Output("DispCtrl.Cli", "dispctrl.exe");
        Options o = context.Options;
        Measure(context, exe, "help", ["help"], o.N(10), 400.0);
        Measure(context, exe, "focus get", ["focus", "get"], o.N(10), 400.0);
        Measure(context, exe, "status", ["status"], o.N(8), 600.0);
        Measure(context, exe, "displays", ["displays"], o.N(5), 800.0);
    }

    private static void Measure(Context context, string exe, string label, string[] args, int samples, double budget)
    {
        var wall = new List<double>(); var cpu = new List<double>(); var memory = new List<double>();
        for (int i = 0; i <= samples; i++)
        {
            var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (string a in args) start.ArgumentList.Add(a);
            long t = Stopwatch.GetTimestamp();
            using Process p = Process.Start(start)!;
            _ = p.StandardOutput.ReadToEnd(); _ = p.StandardError.ReadToEnd();
            p.WaitForExit();
            double ms = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
            if (p.ExitCode != 0) { context.Report.Note(S, $"dispctrl {label} exited {p.ExitCode}"); return; }
            if (i == 0) continue; // the first run pays the disk cache
            wall.Add(ms);
            cpu.Add(Native.CpuMs(p.Handle));
            memory.Add(Native.PeakWorkingSet(p.Handle) / 1048576.0);
        }
        context.Add(Stats.Row(S, $"dispctrl {label}: start to exit", "ms", wall, budget));
        context.Add(Stats.Row(S, $"dispctrl {label}: CPU", "ms", cpu, budget / 2, "from cycle count"));
        context.Add(Stats.Row(S, $"dispctrl {label}: peak working set", "MB", memory, 80));
    }
}
