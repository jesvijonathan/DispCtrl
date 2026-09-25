// perfcheck: DispCtrl's performance suite. docs/PERFORMANCE.md has the guide.
//
//   dotnet run --project tools/perfcheck -c Release                  core, displays, engine, cli
//   dotnet run --project tools/perfcheck -c Release -- --all         and ui, restart, writes
//   dotnet run --project tools/perfcheck -c Release -- --suites ui --quick
//   ... -- --baseline artifacts/perf/latest.json                      flag regressions
//
// Exit code: the number of measurements over budget or regressed.
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DispCtrl.PerfCheck;

Options options = Options.Parse(args);
if (options.Help) { Console.WriteLine(Options.Usage); return 0; }
Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
_ = Native.timeBeginPeriod(1); // 1 ms sleeps, or every poll below rounds up to 15.6 ms

var context = new Context(options, new Report { AsJsonLines = options.Child is not null });
if (options.Child == "core") { CoreSuite.Run(context); return 0; }

Console.WriteLine($"perfcheck: {string.Join(", ", options.Suites)}{(options.Quick ? " (quick)" : "")} on {Environment.MachineName}, {Environment.ProcessorCount} logical CPUs, {Native.CyclesPerMs / 1000:0} MHz reference clock");
if (options.Suites.Overlaps(["ui", "restart", "writes"]))
    Console.WriteLine("Hands off the desk: the ui suite opens and closes the quick panel, restart restarts the engine, writes writes brightness at its current value.");

var suites = new (string Name, Action<Context> Run)[]
{
    ("core", CoreSuite.RunIsolated), ("displays", DisplaySuite.Run), ("engine", EngineSuite.Run), ("cli", CliSuite.Run),
    ("ui", UiSuite.Run), ("writes", WritesSuite.Run), ("restart", RestartSuite.Run),
};
foreach (var (name, run) in suites)
{
    if (!options.Suites.Contains(name)) continue;
    try { run(context); }
    catch (Exception ex) { context.Report.Note(name, $"FAILED: {ex.GetType().Name}: {ex.Message}"); context.Failures++; }
}

List<Row> rows = context.Report.Rows;
int over = rows.Count(r => r.OverBudget);
int regressed = options.Baseline is null ? 0 : Compare(rows, options.Baseline);

string folder = Path.Combine(context.Repo, "artifacts", "perf");
if (!options.NoSave)
{
    Directory.CreateDirectory(folder);
    var document = new JsonObject
    {
        ["version"] = 1, ["taken"] = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture), ["machine"] = Environment.MachineName,
        ["commit"] = Git("rev-parse --short HEAD"), ["dirty"] = Git("status --porcelain").Length > 0,
        ["configuration"] = context.Configuration, ["suites"] = new JsonArray([.. options.Suites.Select(s => (JsonNode)s)]),
        ["quick"] = options.Quick, ["rows"] = new JsonArray([.. rows.Select(r => (JsonNode)r.ToJson())]),
        ["notes"] = new JsonArray([.. context.Report.Notes.Select(n => (JsonNode)n)]),
    };
    string text = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    string file = Path.Combine(folder, $"perf-{DateTime.Now:yyyyMMdd-HHmmss}.json");
    File.WriteAllText(file, text);
    File.WriteAllText(Path.Combine(folder, "latest.json"), text);
    Console.WriteLine($"\nReport: {file}");
}

Console.WriteLine($"{rows.Count} measurements; {over} over budget; {regressed} regressed; {context.Failures} suite failure(s).");
// Exit, not return: UI Automation's client leaves a foreground thread behind,
// and a returned Main then waits on it forever.
Environment.Exit(over + regressed + context.Failures);
return 0;

// A regression is slower than the baseline by 25% and by more than the unit's
// noise floor: a 3 us operation becoming 4 us is the timer, not the code.
static int Compare(List<Row> rows, string baselinePath)
{
    JsonNode? baseline = JsonNode.Parse(File.ReadAllText(baselinePath));
    var earlier = (baseline?["rows"]?.AsArray() ?? []).Select(n => Row.FromJson(n!)).ToDictionary(r => r.Suite + "/" + r.Name);
    Console.WriteLine($"\n== compared with {baselinePath} ({baseline?["commit"]}, {baseline?["taken"]})");
    int regressed = 0;
    foreach (Row row in rows)
    {
        if (!earlier.TryGetValue(row.Suite + "/" + row.Name, out Row? before) || before.Median <= 0) continue;
        double floor = row.Unit switch { "ms" => 1.0, "MB" => 4, "KB" => 16, "ms/min" => 20, "/s" => 5, _ => 1 };
        double change = (row.Median - before.Median) / before.Median;
        bool worse = change > 0.25 && row.Median - before.Median > floor;
        bool better = change < -0.25 && before.Median - row.Median > floor;
        if (!worse && !better) continue;
        if (worse) regressed++;
        Console.ForegroundColor = worse ? ConsoleColor.Red : ConsoleColor.Green;
        Console.WriteLine($"  {(worse ? "REGRESSED" : "improved "),-10} {row.Suite}/{row.Name}: {Report.Format(before.Median, row.Unit)} -> {Report.Format(row.Median, row.Unit)} ({change:+0%;-0%})");
        Console.ResetColor();
    }
    return regressed;
}

string Git(string arguments)
{
    try
    {
        using var git = Process.Start(new ProcessStartInfo("git", arguments) { RedirectStandardOutput = true, WorkingDirectory = context.Repo, UseShellExecute = false })!;
        string output = git.StandardOutput.ReadToEnd().Trim();
        git.WaitForExit();
        return output;
    }
    catch (Exception) { return ""; }
}

namespace DispCtrl.PerfCheck
{
    internal sealed class Options
    {
        public static readonly string[] Defaults = ["core", "displays", "engine", "cli"];
        public static readonly string[] All = ["core", "displays", "engine", "cli", "ui", "writes", "restart"];

        public HashSet<string> Suites { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Quick { get; private set; }
        public bool NoSave { get; private set; }
        public bool Help { get; private set; }
        public string? Baseline { get; private set; }
        public string? Child { get; private set; }

        /// <summary>Fewer samples and shorter idle windows: minutes become about one.</summary>
        public int N(int full) => Quick ? Math.Max(3, full / 3) : full;
        public int Seconds(int full) => Quick ? Math.Max(5, full / 3) : full;

        public const string Usage = """
            perfcheck [--suites a,b] [--all] [--ui] [--writes] [--restart] [--quick] [--baseline FILE] [--no-save]

              core      hardware-free: settings load/save/merge, control commands, presets (scratch folder)
              displays  reads only: enumeration, topology, brightness, capabilities, preset capture
              engine    the running engine: memory, handles, idle CPU and wake-ups, broker round trips,
                        settings sync (an unchanged re-save, timed to the engine's reload)
              cli       dispctrl.exe from start to exit
              ui        the quick panel: cold start, open/close, slide stalls, simple/full view, CPU,
                        idle cost, trim, leaks, self-closing, the main window (opens things on screen)
              writes    brightness written at its current value (external displays), slider to
                        hardware, dry-run apply plan. Leaves every value as it found it.
              restart   stops the engine gracefully and starts it through its task: stop, start, ready

            Default: core, displays, engine, cli. Reports go to artifacts/perf/ (latest.json too).
            Exit code: measurements over budget + regressions + failed suites.
            """;

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--suites": foreach (string s in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) o.Suites.Add(s); break;
                    case "--all": foreach (string s in All) o.Suites.Add(s); break;
                    case "--ui": o.Suites.Add("ui"); break;
                    case "--writes": o.Suites.Add("writes"); break;
                    case "--restart": o.Suites.Add("restart"); break;
                    case "--quick": o.Quick = true; break;
                    case "--no-save": o.NoSave = true; break;
                    case "--baseline": o.Baseline = args[++i]; break;
                    case "--child": o.Child = args[++i]; break;
                    case "-h" or "--help" or "help": o.Help = true; break;
                    default: throw new ArgumentException($"Unknown option {args[i]}.\n\n{Usage}");
                }
            }
            if (!args.Contains("--suites") && !args.Contains("--all")) foreach (string s in Defaults) o.Suites.Add(s);
            foreach (string s in o.Suites) if (!All.Contains(s, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException($"Unknown suite {s}.");
            return o;
        }
    }

    internal sealed class Context(Options options, Report report)
    {
        public Options Options { get; } = options;
        public Report Report { get; } = report;
        public int Failures { get; set; }

        public string Repo { get; } = FindRepo();

        /// <summary>The configuration perfcheck itself was built in, so it measures the matching binaries.</summary>
        public string Configuration { get; } = AppContext.BaseDirectory.Contains(@"\Debug\", StringComparison.OrdinalIgnoreCase) ? "Debug" : "Release";

        public string Output(string project, string exe)
        {
            foreach (string configuration in new[] { Configuration, Configuration == "Release" ? "Debug" : "Release" })
            {
                string path = Path.Combine(Repo, "src", project, "bin", configuration, "net10.0-windows10.0.26100.0", "win-x64", exe);
                if (File.Exists(path)) return path;
            }
            throw new FileNotFoundException($"{exe} is not built; run build.cmd build first.");
        }

        public Row Add(Row row) => Report.Add(row);

        private static string FindRepo()
        {
            for (string? dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
                if (File.Exists(Path.Combine(dir, "build", "dev.ps1"))) return dir;
            return Directory.GetCurrentDirectory();
        }
    }
}
