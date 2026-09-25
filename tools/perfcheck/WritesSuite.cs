using System.Text.Json.Nodes;
using DispCtrl.Control;
using DispCtrl.Core.Displays;
using DispCtrl.Display;

namespace DispCtrl.PerfCheck;

/// <summary>
/// Invocation to execution on real hardware, changing nothing: brightness is
/// written at the value the monitor already holds, and apply runs as a dry-run
/// plan. External monitors only - a write to the built-in panel raises the WMI
/// event the engine's brightness bridge follows, and would move the others.
/// </summary>
internal static class WritesSuite
{
    private const string S = "writes";

    public static void Run(Context context)
    {
        Options o = context.Options;
        List<DisplayInfo> displays = DisplayRegistry.Enumerate();
        var service = new ControlService();
        foreach (DisplayInfo d in displays.Where(d => !d.IsInternal))
        {
            uint current = Brightness.Read(d).Current;
            string label = "external " + d.Key.Model;
            context.Add(Bench.Time(S, $"{label}: DDC/CI brightness write (same value)", o.N(6), 1, 200.0, () => Brightness.Write(d, current)));
            context.Add(Bench.Time(S, $"{label}: write then read back", o.N(4), 0, 350.0, () =>
            {
                _ = Brightness.Write(d, current);
                if (Brightness.Read(d).Current != current) throw new InvalidOperationException($"{label} read back a different brightness");
            }));
            // Through the command API as the CLI and the app send it.
            JsonObject set = new() { ["version"] = 1, ["command"] = "display.set", ["args"] = new JsonObject { ["monitor"] = d.Token, ["brightness"] = (int)current } };
            context.Add(Bench.Time(S, $"{label}: display.set brightness (same value)", o.N(4), 1, 300.0, () => Check(service.Execute((JsonObject)set.DeepClone()))));
            uint after = Brightness.Read(d).Current;
            context.Add(new Row(S, $"{label}: brightness changed by the test", "count", Math.Abs((int)after - (int)current), 0, 0, 1, 1));
        }
        if (!displays.Any(d => !d.IsInternal)) context.Report.Note(S, "no external display: DDC/CI writes skipped");

        // The whole desk as a document, planned and validated but not applied.
        var entries = new JsonArray();
        foreach (DisplayInfo d in displays) entries.Add((JsonNode)new JsonObject { ["monitor"] = d.Token, ["brightness"] = (int)Brightness.Read(d).Current });
        JsonObject apply = new() { ["version"] = 1, ["command"] = "apply", ["args"] = new JsonObject { ["document"] = new JsonObject { ["version"] = 1, ["displays"] = entries }, ["dryRun"] = true } };
        context.Add(Bench.Time(S, "apply --dry-run (plan the whole desk)", o.N(5), 1, 1500.0, () => Check(service.Execute((JsonObject)apply.DeepClone()))));
    }

    private static void Check(JsonObject result)
    {
        if (result["ok"]?.GetValue<bool>() != true) throw new InvalidOperationException(result["error"]?["message"]?.GetValue<string>() ?? result.ToJsonString());
    }
}
