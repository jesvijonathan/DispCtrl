using System.Text.Json.Nodes;
using DispCtrl.Core.Displays;
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

    /// <summary>Unison following the room's light: the settings, what the sensor reads now, and calibration.</summary>
    /// <remarks>
    /// <c>get</c> reads the sensor itself rather than asking the engine, so
    /// it answers with the engine stopped - which is when somebody is most
    /// likely to be asking why nothing follows. <c>capture</c> is calibration
    /// by holding the room there: cover the sensor and capture dark, shine a
    /// light at it and capture bright.
    /// </remarks>
    private static JsonNode AmbientCommand(string action, JsonObject args)
    {
        if (args.ContainsKey("monitor")) throw new ArgumentException("Following the room's light applies to unison, not to one display.");
        if (action is "set" or "reset") return GroupCommand("ambient." + action, args);
        bool dryRun = Flag(args, "dryRun");
        if (action == "get")
        {
            DispCtrlSettings settings = SettingsStore.Load();
            AmbientSettings ambient = settings.Global.Ambient;
            double? lux = AmbientSensors.ReadLux(ambient.SensorId);
            return new JsonObject
            {
                ["value"] = SettingsDocument.Get(SettingsDocument.Read(), "/global/ambient")?.DeepClone(),
                ["following"] = settings.Global.UnisonBrightness && ambient.Enabled,
                ["sensors"] = new JsonArray(AmbientSensors.List().Select(s => (JsonNode?)new JsonObject { ["id"] = s.Id, ["name"] = s.Name }).ToArray()),
                ["lux"] = lux is { } now ? Math.Round(now, 1) : null,
                ["levelForLux"] = lux is { } reading ? AmbientCurve.Level(reading, ambient) : null,
                ["unisonLevel"] = settings.Global.UnisonLevel,
            };
        }
        if (action == "forget")
            return SettingsDocument.Update(document => document["global"]!["ambient"]!["points"] = new JsonArray(), dryRun);
        if (action == "capture")
        {
            string end = Text(args, "as") ?? throw new ArgumentException("--as dark or --as bright.");
            if (end is not ("dark" or "bright")) throw new ArgumentException("--as dark or --as bright.");
            AmbientSettings current = SettingsStore.Load().Global.Ambient;
            double lux = AmbientSensors.ReadLux(current.SensorId) ?? throw new InvalidOperationException("No light sensor answered.");
            int captured = (int)Math.Round(Math.Clamp(lux, 0, 100000));
            // The ends need room between them, or the whole curve is one step.
            if (end == "dark" && AmbientCurve.Coordinate(current.BrightLux) - AmbientCurve.Coordinate(captured) < 0.3)
                throw new InvalidOperationException($"The sensor reads {captured} lx, too close to bright light at {current.BrightLux} lx. Cover it, or capture bright first.");
            if (end == "bright" && AmbientCurve.Coordinate(captured) - AmbientCurve.Coordinate(current.DarkLux) < 0.3)
                throw new InvalidOperationException($"The sensor reads {captured} lx, too close to the dark end at {current.DarkLux} lx. Light it, or capture dark first.");
            JsonObject result = SettingsDocument.Update(document =>
                document["global"]!["ambient"]![end == "dark" ? "darkLux" : "brightLux"] = Math.Max(end == "bright" ? 1 : 0, captured), dryRun);
            result["captured"] = captured;
            return result;
        }
        throw new ArgumentException("ambient get|set|reset|capture|forget.");
    }
}
