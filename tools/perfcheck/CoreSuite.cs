using System.Diagnostics;
using System.Text.Json.Nodes;
using DispCtrl.Control;
using DispCtrl.Core.Presets;
using DispCtrl.Core.Settings;

namespace DispCtrl.PerfCheck;

/// <summary>
/// The hardware-free paths every client runs constantly: settings, the command
/// API, presets. In a child process against a scratch folder, because
/// <see cref="SettingsStore.Directory"/> is fixed for a process's life and the
/// other suites need the real one.
/// </summary>
internal static class CoreSuite
{
    private const string S = "core";

    public static void RunIsolated(Context context)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "DispCtrl-perfcheck-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, UseShellExecute = false };
        if (Environment.ProcessPath!.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(CoreSuite).Assembly.Location);
        foreach (string a in (string[])["--child", "core", .. (context.Options.Quick ? new[] { "--quick" } : [])]) start.ArgumentList.Add(a);
        start.Environment["DISPCTRL_DATA_DIR"] = scratch;
        try
        {
            using Process child = Process.Start(start)!;
            string? line;
            while ((line = child.StandardOutput.ReadLine()) is not null)
            {
                if (line.StartsWith("ROW ", StringComparison.Ordinal)) context.Add(Row.FromJson(JsonNode.Parse(line[4..])!));
                else if (line.StartsWith("NOTE ", StringComparison.Ordinal)) context.Report.Note(S, line[(5 + S.Length + 2)..]);
            }
            child.WaitForExit();
            if (child.ExitCode != 0) throw new InvalidOperationException($"core child exited {child.ExitCode}");
        }
        finally { try { Directory.Delete(scratch, recursive: true); } catch (IOException) { } }
    }

    public static void Run(Context context)
    {
        Options o = context.Options;
        if (!SettingsStore.Directory.Contains("DispCtrl-perfcheck-", StringComparison.Ordinal))
            throw new InvalidOperationException("core must run against a scratch folder; refusing to touch real settings");

        // A settings file shaped like a lived-in desk, not the defaults: every
        // measured path below parses, merges or serialises it.
        DispCtrlSettings seeded = SettingsStore.Load();
        for (int i = 0; i < 4; i++) { var m = seeded.For($"TST-{i:X4}-{i:X8}"); m.Alias = "desk " + i; m.IsOled = i == 0; }
        SettingsStore.Save(seeded);
        context.Report.Note(S, $"settings.json {new FileInfo(SettingsStore.Path_).Length / 1024.0:0.0} KB");

        context.Add(Bench.Time(S, "settings load (read + parse)", o.N(300), 20, 3.0, () => SettingsStore.Load()));
        context.Add(Bench.Allocations(S, "settings load allocations", o.N(100), 400, () => SettingsStore.Load()));
        context.Add(Bench.Time(S, "settings save (merge + atomic replace)", o.N(100), 5, 15.0, () =>
        {
            DispCtrlSettings s = SettingsStore.Load();
            s.Global.Focus.DimPercent = s.Global.Focus.DimPercent == 40 ? 41 : 40;
            SettingsStore.Save(s);
        }));
        context.Add(Bench.Time(S, "settings save, stale client merge", o.N(100), 5, 20.0, () =>
        {
            DispCtrlSettings a = SettingsStore.Load(), b = SettingsStore.Load();
            a.Global.Focus.DimPercent = a.Global.Focus.DimPercent == 40 ? 41 : 40; SettingsStore.Save(a);
            b.Global.OledCare.DimPercent = b.Global.OledCare.DimPercent == 60 ? 61 : 60; SettingsStore.Save(b);
        }, "two saves"));

        // The floor under every save: the same bytes written and renamed with no
        // DispCtrl code at all. What a save costs above this is ours to cut.
        string bytes = File.ReadAllText(SettingsStore.Path_), bare = Path.Combine(SettingsStore.Directory, "floor.json");
        context.Add(Bench.Time(S, "filesystem floor: write temp + rename (same size)", o.N(100), 5, null, () =>
        {
            string tmp = bare + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, bytes);
            File.Move(tmp, bare, overwrite: true);
        }, "antivirus scans each new file"));
        context.Add(Bench.Time(S, "filesystem floor: overwrite in place (same size)", o.N(100), 5, null, () => File.WriteAllText(bare, bytes)));

        JsonNode baseline = JsonNode.Parse(File.ReadAllText(SettingsStore.Path_))!;
        JsonNode edited = baseline.DeepClone(); edited["global"]!["focus"]!["dimPercent"] = 12;
        JsonNode latest = baseline.DeepClone(); latest["global"]!["oledCare"]!["dimPercent"] = 34;
        context.Add(Bench.Time(S, "settings three-way merge (in memory)", o.N(500), 20, 2.0, () => SettingsStore.MergeEdits(baseline, edited, latest)));

        var service = new ControlService();
        JsonObject Request(string command, JsonObject? args = null) => new() { ["version"] = 1, ["command"] = command, ["args"] = args ?? [] };
        context.Add(Bench.Time(S, "control: focus.get", o.N(300), 10, 3.0, () => service.Execute(Request("focus.get"))));
        context.Add(Bench.Time(S, "control: settings.get (whole document)", o.N(200), 10, 5.0, () => service.Execute(Request("settings.get"))));
        context.Add(Bench.Time(S, "control: focus.set --dry-run", o.N(200), 10, 5.0, () => service.Execute(Request("focus.set", new() { ["dimPercent"] = 33, ["dryRun"] = true }))));
        int flip = 30;
        context.Add(Bench.Time(S, "control: focus.set (persisted)", o.N(60), 3, 20.0, () => service.Execute(Request("focus.set", new() { ["dimPercent"] = flip ^= 1 }))));
        context.Add(Bench.Time(S, "control: settings.validate", o.N(100), 5, 10.0, () => service.Execute(Request("settings.validate", new() { ["document"] = baseline.DeepClone() }))));
        context.Add(Bench.Time(S, "control: status (local, reads the desk)", o.N(20), 2, 150.0, () => service.Execute(Request("status"))));

        Preset saved = Desk(4), live = Desk(4);
        live.Monitors["TST-0001-00000001"].Brightness = 10; live.Global.NightLightEnabled = true;
        string json = PresetStore.ToJson(saved);
        context.Add(Bench.Time(S, "preset serialise (4 monitors)", o.N(2000), 50, 0.5, () => PresetStore.ToJson(saved)));
        context.Add(Bench.Time(S, "preset parse (4 monitors)", o.N(2000), 50, 0.5, () => PresetStore.Parse(json)));
        context.Add(Bench.Time(S, "preset diff (4 monitors, 2 changes)", o.N(2000), 50, 0.5, () => PresetDiff.Describe(saved, live)));
        context.Add(Bench.Allocations(S, "preset diff allocations", o.N(500), 64, () => PresetDiff.Describe(saved, live)));

        context.Add(Bench.Time(S, "settings schema", o.N(50), 3, 20.0, () => SettingsDocument.Schema().ToJsonString()));
    }

    private static Preset Desk(int monitors)
    {
        var preset = new Preset { Name = "Benchmark" };
        for (int i = 0; i < monitors; i++)
            preset.Monitors[$"TST-{i:X4}-{i:X8}"] = new PresetMonitor
            {
                Label = "Monitor " + i, Model = "TST" + i, X = i * 1920, Width = 1920, Height = 1080, RefreshHz = 60,
                ScalePercent = 100, Brightness = 50 + i, Dpi = 96, Primary = i == 0,
            };
        return preset;
    }
}
