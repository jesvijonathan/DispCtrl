using System.Text.Json.Nodes;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

namespace DispCtrl.Control;

public sealed partial class ControlService
{
    private static JsonObject StartupCommand(string action, JsonObject args)
    {
        if (action == "get") return new JsonObject
        {
            ["packaged"] = StartupIntegration.IsPackaged,
            ["engine"] = StartupIntegration.ReadEngineStartupAsync().GetAwaiter().GetResult(),
            ["startMenu"] = StartupIntegration.IsPackaged || StartupIntegration.HasStartMenuShortcut,
            ["desktop"] = StartupIntegration.HasDesktopShortcut,
            ["engineTask"] = !StartupIntegration.IsPackaged && StartupIntegration.EngineTaskRegistered,
            ["preloadPanel"] = SettingsStore.Load().Global.PreloadQuickPanel,
            ["openWindow"] = SettingsStore.Load().Global.OpenWindowAtSignIn,
        };
        var steps = new List<Step>();
        foreach (var pair in args)
        {
            if (pair.Key == "dryRun") continue;
            bool enabled = Flag(args, pair.Key);
            Action run = pair.Key switch
            {
                "engine" => () => StartupIntegration.SetEngineStartupAsync(enabled,
                    Path.Combine(AppContext.BaseDirectory, "DispCtrl.Engine.exe")).GetAwaiter().GetResult(),
                "startMenu" when !StartupIntegration.IsPackaged => () => StartupIntegration.SetStartMenuShortcut(enabled),
                "desktop" when !StartupIntegration.IsPackaged => () => StartupIntegration.SetDesktopShortcut(enabled),
                "startMenu" or "desktop" => throw new ArgumentException("Windows manages packaged app shortcuts. Pin DispCtrl from Start."),
                "preloadPanel" => () => SaveGlobal(g => g.PreloadQuickPanel = enabled),
                "openWindow" => () => SaveGlobal(g => g.OpenWindowAtSignIn = enabled),
                _ => throw new ArgumentException("Startup options: engine, start-menu, desktop, preload-panel, open-window."),
            };
            steps.Add(new(70, pair.Key, null, () => { run(); return true; }));
        }
        if (steps.Count == 0) throw new ArgumentException("No startup settings requested.");
        return RunSteps(steps, Flag(args, "dryRun"));
    }

    private static void SaveGlobal(Action<GlobalSettings> change)
    {
        DispCtrlSettings settings = SettingsStore.Load();
        change(settings.Global);
        SettingsStore.Save(settings);
    }

    private static JsonObject UnisonCommand(string action, JsonObject args)
    {
        var settings = SettingsStore.Load();
        var global = settings.Global;
        if (action == "get") return new JsonObject { ["enabled"] = global.UnisonBrightness,
            ["level"] = global.UnisonLevel, ["calibrated"] = global.UnisonCalibrated, ["followWindows"] = global.UnisonFollowsWindows,
            ["calibrating"] = UnisonCalibration.IsActive };
        if (UnisonCalibration.IsActive) throw new InvalidOperationException("Finish or cancel brightness calibration before applying Unison.");
        foreach (var pair in args)
        {
            switch (pair.Key)
            {
                case "enabled": global.UnisonBrightness = Flag(args, pair.Key); break;
                case "level": global.UnisonLevel = Integer(args, pair.Key, 0, 100); break;
                case "calibrated": global.UnisonCalibrated = Flag(args, pair.Key); break;
                case "followWindows": global.UnisonFollowsWindows = Flag(args, pair.Key); break;
                case "monitor" or "floor" or "ceiling": break;
                case "dryRun": break;
                default: throw new ArgumentException("Unknown Unison option: " + pair.Key);
            }
        }
        if (!args.Any(p => p.Key != "dryRun")) throw new ArgumentException("Provide a Unison setting to change.");

        // A display's calibrated limits: what the walkthrough captures, set outright.
        if (args.ContainsKey("floor") || args.ContainsKey("ceiling"))
        {
            var target = Resolve(Text(args, "monitor") ?? throw new ArgumentException("--floor and --ceiling are per display: add --monitor."), false).Single();
            MonitorSettings limits = settings.For(target.Token);
            if (args.ContainsKey("floor")) limits.BrightnessFloor = Integer(args, "floor", 0, 99);
            if (args.ContainsKey("ceiling")) limits.BrightnessCeiling = Integer(args, "ceiling", 1, 100);
            if (!limits.HasBrightnessRange) throw new ArgumentException("The ceiling must be above the floor.");
        }
        else if (args.ContainsKey("monitor")) throw new ArgumentException("--monitor goes with --floor and --ceiling.");
        var steps = new List<Step>();
        if (global.UnisonBrightness)
        {
            foreach (var d in Resolve(null))
            {
                var read = Brightness.Read(d);
                if (!read.Supported) continue;
                var monitor = settings.For(d.Token);
                if (monitor.BrightnessBaseline <= 0)
                    monitor.BrightnessBaseline = UnisonResume.Enable(global.UnisonLevel, [read.Percent], [monitor.BrightnessBaseline], 0).Baselines[0];
                // The built-in panel too, within its own range, including while
                // Windows' slider drives unison - the engine reads that slider back
                // through the same range.
                int target = UnisonResume.Target(monitor, global.UnisonCalibrated, global.UnisonLevel);
                steps.AddRange(PlanDisplay(new JsonObject { ["monitor"] = d.Token, ["brightness"] = target }));
            }
        }
        // Persist the reference before its WMI echo arrives. Reports retain
        // hardware failures separately rather than claiming the desk is atomic.
        steps.Add(new(10, "unison settings", null, () => { SettingsStore.Save(settings); return true; }));
        return RunSteps(steps, Flag(args, "dryRun"));
    }
}
