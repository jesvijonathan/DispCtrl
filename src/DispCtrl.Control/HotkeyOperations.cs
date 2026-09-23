using System.Text.Json.Nodes;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

namespace DispCtrl.Control;

/// <summary>The Hotkeys page from the command line.</summary>
/// <remarks>
/// The engine registers whatever the settings hold and re-registers on every
/// save, so a shortcut added here works as soon as the command returns.
/// Shortcuts are written as the page shows them - <c>Ctrl + Alt + Up</c> - and
/// actions as the settings file names them, in kebab case on the command line.
/// </remarks>
public sealed partial class ControlService
{
    private static JsonNode HotkeysCommand(string action, JsonObject args)
    {
        DispCtrlSettings settings = SettingsStore.Load();
        switch (action)
        {
            case "list":
            case "get":
            {
                var list = new JsonArray();
                for (int i = 0; i < settings.Hotkeys.Count; i++)
                {
                    Hotkey h = settings.Hotkeys[i];
                    list.Add((JsonNode)new JsonObject
                    {
                        ["index"] = i + 1, ["keys"] = h.Describe(), ["action"] = KebabAction(h.Action),
                        ["does"] = h.DescribeAction(), ["display"] = h.Display, ["step"] = h.Step,
                        ["preset"] = h.Preset, ["enabled"] = h.Enabled,
                    });
                }
                return new JsonObject { ["hotkeys"] = list, ["actions"] = new JsonArray(Enum.GetValues<HotkeyAction>().Select(a => (JsonNode?)JsonValue.Create(KebabAction(a))).ToArray()) };
            }
            case "add":
            case "set":
            {
                foreach (var pair in args)
                    if (pair.Key is not ("index" or "keys" or "action" or "step" or "display" or "preset" or "enabled" or "dryRun"))
                        throw new ArgumentException("hotkeys options: --index, --keys, --action, --step, --display, --preset, --enabled.");
                Hotkey h;
                if (action == "add") h = new Hotkey();
                else h = settings.Hotkeys.ElementAtOrDefault(Integer(args, "index", 1, 999) - 1)
                    ?? throw new ArgumentException("No hotkey with that index; see hotkeys list.");

                if (Text(args, "keys") is { } keys)
                {
                    if (!Hotkey.TryParse(keys, out uint key, out uint modifiers))
                        throw new ArgumentException("Write the shortcut as modifiers and a key: \"Ctrl+Alt+Up\", \"Win+Shift+F9\".");
                    h.Key = key;
                    h.Modifiers = modifiers;
                }
                if (Text(args, "action") is { } named) h.Action = ParseAction(named);
                if (args.ContainsKey("step")) h.Step = Integer(args, "step", 1, 100);
                if (args.ContainsKey("display")) h.Display = Integer(args, "display", 0, 16);
                if (Text(args, "preset") is { } preset) h.Preset = preset;
                if (args.ContainsKey("enabled")) h.Enabled = Flag(args, "enabled");

                if (!h.IsComplete) throw new ArgumentException("A hotkey needs --keys, and a preset name for apply-preset.");
                if (settings.Hotkeys.Any(o => !ReferenceEquals(o, h) && o.Key == h.Key && o.Modifiers == h.Modifiers))
                    throw new ArgumentException($"{h.Describe()} is already bound; set or remove that one instead.");
                if (Flag(args, "dryRun")) return new JsonObject { ["state"] = "validated", ["keys"] = h.Describe(), ["does"] = h.DescribeAction() };
                if (action == "add") settings.Hotkeys.Add(h);
                SettingsStore.Save(settings);
                return new JsonObject { ["state"] = "saved", ["keys"] = h.Describe(), ["does"] = h.DescribeAction() };
            }
            case "remove":
            {
                int index = Integer(args, "index", 1, 999) - 1;
                if (index >= settings.Hotkeys.Count) throw new ArgumentException("No hotkey with that index; see hotkeys list.");
                if (Flag(args, "dryRun")) return new JsonObject { ["state"] = "validated" };
                string removed = settings.Hotkeys[index].Describe();
                settings.Hotkeys.RemoveAt(index);
                SettingsStore.Save(settings);
                return new JsonObject { ["state"] = "removed", ["keys"] = removed };
            }
            case "reset":
            {
                if (args.Any(p => p.Key != "dryRun")) throw new ArgumentException("hotkeys reset takes no options.");
                List<Hotkey> defaults = Hotkey.Defaults();
                if (Flag(args, "dryRun")) return new JsonObject { ["state"] = "validated", ["hotkeys"] = defaults.Count };
                settings.Hotkeys.Clear();
                settings.Hotkeys.AddRange(defaults);
                settings.Global.HotkeyDefaultsOffered = true;
                SettingsStore.Save(settings);
                return new JsonObject { ["state"] = "reset", ["hotkeys"] = new JsonArray(defaults.Select(h => (JsonNode?)JsonValue.Create($"{h.Describe()}: {h.DescribeAction()}")).ToArray()) };
            }
            default:
                throw new ArgumentException("hotkeys list|add|set|remove|reset");
        }
    }

    private static string KebabAction(HotkeyAction a) => Core.Devices.DeviceDefinitions.KeyFor(
        string.Concat(a.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString())));

    private static HotkeyAction ParseAction(string text)
    {
        foreach (HotkeyAction a in Enum.GetValues<HotkeyAction>())
            if (KebabAction(a) == Core.Devices.DeviceDefinitions.KeyFor(text) || a.ToString().Equals(text, StringComparison.OrdinalIgnoreCase)) return a;
        throw new ArgumentException("Actions: " + string.Join(", ", Enum.GetValues<HotkeyAction>().Select(KebabAction)));
    }

    /// <summary>
    /// The Displays page's per-display reset: DispCtrl's own settings for it,
    /// and with <c>--factory --confirm</c> the monitor's own defaults as well.
    /// </summary>
    private static JsonNode ResetDisplay(JsonObject args)
    {
        foreach (var pair in args)
            if (pair.Key is not ("monitor" or "factory" or "confirm" or "dryRun")) throw new ArgumentException("display reset takes --monitor, --factory, --confirm.");
        var target = Resolve(Text(args, "monitor") ?? throw new ArgumentException("Choose the monitor: --monitor 2."), false).Single();
        bool factory = Flag(args, "factory");
        if (factory && target.IsInternal) throw new ArgumentException($"{target.Label} is a built-in panel; it has no factory settings to restore over DDC/CI.");
        if (factory && !Flag(args, "confirm")) throw new ArgumentException($"--factory restores every setting on {target.Label} to how it left the factory. Add --confirm to go ahead.");
        if (Flag(args, "dryRun")) return new JsonObject { ["state"] = "validated", ["monitor"] = target.Token, ["factory"] = factory };
        DispCtrlSettings settings = SettingsStore.Load();
        settings.For(target.Token).ResetToDefaults();
        SettingsStore.Save(settings);
        bool restored = factory && MonitorCapabilities.RestoreFactory(target);
        if (factory && !restored) throw new InvalidOperationException($"DispCtrl's settings for {target.Label} were reset, but the monitor refused the factory reset.");
        return new JsonObject { ["state"] = "applied", ["monitor"] = target.Token, ["factory"] = restored };
    }
}
