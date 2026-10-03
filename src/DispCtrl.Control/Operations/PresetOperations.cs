using System.Text.Json.Nodes;
using DispCtrl.Core;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Presets;
using DispCtrl.Core.Settings;
using DispCtrl.Display.Presets;

namespace DispCtrl.Control;

public sealed partial class ControlService
{
    /// <summary>preset list|save|apply|delete|desk: presets and desk profiles.</summary>
    /// <remarks>
    /// <c>preset launch</c> is not here: it waits for a program to exit, for as
    /// long as a game runs, which no request with a timeout can do. The terminal
    /// runs it in its own process (<see cref="PresetLauncher"/>).
    /// </remarks>
    private static JsonNode PresetCommand(string action, JsonObject args)
    {
        if (!FeatureFlags.Presets) throw new InvalidOperationException("Presets are an unavailable beta feature in this build.");
        bool dryRun = Flag(args, "dryRun");
        if (action == "list")
        {
            if (args.Any(p => p.Key != "dryRun")) throw new ArgumentException("preset list takes no options.");
            var list = new JsonArray();
            foreach (Preset p in PresetStore.Load())
                list.Add((JsonNode)new JsonObject
                {
                    ["name"] = p.Name, ["displays"] = p.Monitors.Count, ["savedUtc"] = p.SavedUtc,
                    ["applyWhenConnected"] = p.ApplyWhenConnected, ["layout"] = p.IncludeLayout,
                    ["skips"] = new JsonArray(p.Skip.Select(s => (JsonNode?)JsonValue.Create(SkipWord(s))).ToArray()),
                });
            return new JsonObject { ["presets"] = list };
        }

        if (args.Any(p => p.Key is not ("name" or "enabled" or "skip" or "dryRun")) || args.ContainsKey("enabled") && action != "desk"
            || args.ContainsKey("skip") && action is not ("save" or "set"))
            throw new ArgumentException($"preset {action} takes a name{(action == "desk" ? " and on or off" : action is "save" or "set" ? " and --skip" : "")}.");
        List<PresetPart>? skip = args.ContainsKey("skip") ? Parts(Text(args, "skip") ?? "") : null;
        string name = Text(args, "name")?.Trim() is { Length: > 0 } n ? n : throw new ArgumentException($"preset {action} needs a name.");
        Preset? existing = PresetStore.Read(PresetStore.PathFor(name));
        if (existing is null && action != "save") throw new InvalidOperationException($"There is no preset called '{name}'.");

        switch (action)
        {
            case "apply":
            {
                if (dryRun) return new JsonObject { ["state"] = "validated", ["name"] = name };
                DispCtrlSettings settings = SettingsStore.Load();
                PresetResult result = PresetService.Apply(existing!, Resolve(null), settings);
                // Only what the preset owns: the apply takes seconds, and the
                // engine or the app may have saved meanwhile.
                if (result.Attempted) SettingsStore.Save(PresetSettings.Merge(existing!, settings, SettingsStore.Load()));
                return new JsonObject
                {
                    ["state"] = result.Ok ? "applied" : "partial", ["name"] = name,
                    ["notes"] = new JsonArray(result.Notes.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                };
            }
            case "save":
            {
                if (dryRun) return new JsonObject { ["state"] = "validated", ["name"] = name, ["replaces"] = existing is not null };
                DispCtrlSettings settings = SettingsStore.Load();
                Preset fresh = PresetService.Capture(name, Resolve(null), settings, windows: true);
                // Keep what an existing preset already covered; the defaults would
                // quietly widen what it controls.
                if (existing is not null) fresh = PresetValidation.RetainScope(fresh, existing, settings);
                if (skip is not null) fresh.Skip = skip;
                PresetStore.Save(fresh);
                return new JsonObject { ["state"] = "saved", ["name"] = name, ["displays"] = fresh.Monitors.Count, ["skips"] = Words(fresh.Skip) };
            }
            case "set":
            {
                if (skip is null) throw new ArgumentException("preset set needs --skip, as: preset set Evening --skip brightness,windows (or --skip none).");
                if (dryRun) return new JsonObject { ["state"] = "validated", ["name"] = name, ["skips"] = Words(skip) };
                existing!.Skip = skip;
                PresetStore.Save(existing);
                return new JsonObject { ["state"] = "saved", ["name"] = name, ["skips"] = Words(skip) };
            }
            case "desk":
            {
                if (!args.ContainsKey("enabled")) throw new ArgumentException("preset desk needs a name and on or off.");
                bool on = Flag(args, "enabled");
                if (on && !existing!.IncludeLayout)
                    throw new InvalidOperationException($"'{name}' does not hold a layout, so it describes no desk. Save it as a whole-desk preset.");
                if (dryRun) return new JsonObject { ["state"] = "validated", ["name"] = name, ["applyWhenConnected"] = on };
                existing!.ApplyWhenConnected = on;
                PresetStore.Save(existing);
                return new JsonObject { ["state"] = "applied", ["name"] = name, ["applyWhenConnected"] = on, ["displays"] = existing.Monitors.Count };
            }
            case "diff":
            {
                // What applying would change: the desk now against the preset,
                // as the app's drift sign counts it.
                DispCtrlSettings settings = SettingsStore.Load();
                Preset wanted = DeskProfiles.WithCurrentTokens(existing!, settings);
                List<DisplayInfo> attached = Resolve(null);
                Preset live = PresetService.Capture("Now", attached, settings, useCache: true);
                var changes = new JsonArray();
                foreach (PresetChange c in PresetDiff.Describe(wanted, live, settings.Global.BrightnessIsAutomatic))
                    changes.Add((JsonNode)new JsonObject { ["where"] = c.Where, ["what"] = c.What, ["now"] = c.Now, ["preset"] = c.Saved });
                var missing = new JsonArray(wanted.Monitors.Where(p => !attached.Any(d => d.Token == p.Key))
                    .Select(p => (JsonNode?)JsonValue.Create(p.Value.Label ?? p.Key)).ToArray());
                return new JsonObject { ["name"] = name, ["matches"] = changes.Count == 0, ["changes"] = changes, ["notAttached"] = missing };
            }
            case "delete":
                if (dryRun) return new JsonObject { ["state"] = "validated", ["name"] = name };
                PresetStore.Delete(name);
                return new JsonObject { ["state"] = "deleted", ["name"] = name };
            default:
                throw new ArgumentException("Preset action: list, diff, save, set, apply, delete, desk; launch from the command line.");
        }
    }

    private static string SkipWord(PresetPart part) => part switch
    {
        PresetPart.NightLight => "nightlight",
        _ => part.ToString().ToLowerInvariant(),
    };

    private static JsonArray Words(IEnumerable<PresetPart> parts) => new(parts.Select(p => (JsonNode?)JsonValue.Create(SkipWord(p))).ToArray());

    /// <summary>The parts a preset leaves alone, from "brightness,windows"; "none" or nothing is every part restored.</summary>
    private static List<PresetPart> Parts(string text)
    {
        var parts = new List<PresetPart>();
        foreach (string word in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (word.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
            parts.Add(PresetParts.Parse(word) ?? throw new ArgumentException(
                $"'{word}' is not a part. Parts: layout, brightness, nightlight, wallpaper, controls, taskbar, windows, or none."));
        }
        return parts.Distinct().Order().ToList();
    }
}
