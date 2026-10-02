using System.Text.Json.Nodes;
using DispCtrl.Core;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Presets;
using DispCtrl.Core.Settings;
using DispCtrl.Display.Presets;

namespace DispCtrl.Control;

public sealed partial class ControlService
{
    /// <summary>preset list|save|apply|delete|desk: presets and desk profiles (Beta).</summary>
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
                });
            return new JsonObject { ["presets"] = list };
        }

        if (args.Any(p => p.Key is not ("name" or "enabled" or "dryRun")) || args.ContainsKey("enabled") && action != "desk")
            throw new ArgumentException($"preset {action} takes a name{(action == "desk" ? " and on or off" : "")}.");
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
                PresetStore.Save(fresh);
                return new JsonObject { ["state"] = "saved", ["name"] = name, ["displays"] = fresh.Monitors.Count };
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
            case "delete":
                if (dryRun) return new JsonObject { ["state"] = "validated", ["name"] = name };
                PresetStore.Delete(name);
                return new JsonObject { ["state"] = "deleted", ["name"] = name };
            default:
                throw new ArgumentException("Preset action: list, save, apply, delete, desk; launch from the command line.");
        }
    }
}
