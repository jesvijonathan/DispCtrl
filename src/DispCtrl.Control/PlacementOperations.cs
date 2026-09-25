using System.Text.Json.Nodes;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;
using DispCtrl.Display.Placement;

namespace DispCtrl.Control;

/// <summary>
/// Pinning windows on top, moving windows between displays, and the DDC/CI crash guard.
/// </summary>
/// <remarks>
/// Pinning and gathering act on other programs' windows directly, so they work
/// with the engine stopped; the engine only adds the border around a pinned
/// window, and it finds pins made here by their window property.
/// </remarks>
public sealed partial class ControlService
{
    // ------------------------------------------------------------------ pin --

    private static JsonNode PinCommand(string action, JsonObject args)
    {
        if (args.ContainsKey("monitor")) throw new ArgumentException("Pinning is per window, not per display: use --window.");
        bool dryRun = Flag(args, "dryRun");
        switch (action)
        {
            case "get":
                return new JsonObject
                {
                    ["value"] = SettingsDocument.Get(SettingsDocument.Read(), "/global/pin")?.DeepClone(),
                    ["pinned"] = Windows(WindowPins.List()),
                };
            case "list":
                return new JsonObject { ["windows"] = Windows(AppWindows.List()) };
            case "set":
            case "reset":
            {
                JsonObject result = (JsonObject)GroupCommand("pin." + action, args);
                // Switched off: every pin DispCtrl made is taken back, here as
                // well as in the engine, which may not be running.
                if (!dryRun && !SettingsStore.Load().Global.Pin.Enabled) result["unpinned"] = WindowPins.UnpinAll();
                return result;
            }
            case "on":
            case "off":
            case "toggle":
            {
                if (action == "off" && Flag(args, "all"))
                {
                    if (args.Any(p => p.Key is not ("all" or "dryRun"))) throw new ArgumentException("pin off --all takes nothing else.");
                    if (dryRun) return new JsonObject { ["state"] = "validated", ["pinned"] = WindowPins.List().Count };
                    return new JsonObject { ["state"] = "unpinned", ["unpinned"] = WindowPins.UnpinAll() };
                }
                if (args.Any(p => p.Key is not ("window" or "dryRun")))
                    throw new ArgumentException($"pin {action} takes --window (a handle from pin list, an app's name, or part of a title)"
                        + (action == "off" ? ", or --all" : "") + ".");
                AppWindow window = Text(args, "window") is { } named
                    ? WindowPins.Find(named) ?? throw new ArgumentException($"No open window matches '{named}'. See pin list.")
                    : AppWindows.Describe(WindowPins.Foreground()) ?? throw new ArgumentException("The window in front cannot be pinned. Name one with --window.");
                if (dryRun) return new JsonObject { ["state"] = "validated", ["window"] = Describe(window) };
                PinSettings pin = SettingsStore.Load().Global.Pin;
                PinOutcome outcome = action switch
                {
                    "on" => WindowPins.Pin(window.Handle, pin),
                    "off" => WindowPins.Unpin(window.Handle),
                    _ => WindowPins.Toggle(window.Handle, pin),
                };
                if (!outcome.Done) throw new InvalidOperationException(outcome.Message);
                // Described again: the one found before is from before the pin.
                return new JsonObject { ["state"] = outcome.Pinned ? "pinned" : "unpinned", ["message"] = outcome.Message,
                    ["window"] = Describe(AppWindows.Describe(window.Handle) ?? window) };
            }
            default:
                throw new ArgumentException("pin get|list|set|reset|on|off|toggle.");
        }
    }

    private static JsonArray Windows(IEnumerable<AppWindow> windows)
    {
        List<DisplayInfo> displays = Resolve(null);
        var list = new JsonArray();
        foreach (AppWindow w in windows)
        {
            JsonObject item = Describe(w);
            DisplayInfo? on = AppWindows.DisplayOf(w.Handle, displays);
            item["display"] = on is null ? null : displays.IndexOf(on) + 1;
            list.Add((JsonNode)item);
        }
        return list;
    }

    private static JsonObject Describe(AppWindow w) => new()
    {
        ["window"] = w.Id, ["title"] = w.Title, ["process"] = w.Process,
        ["show"] = w.Show.ToString().ToLowerInvariant(), ["pinned"] = w.Pinned,
        ["onTopByItself"] = w.Topmost && !w.Pinned,
    };

    // ------------------------------------------------------------ placement --

    private static JsonNode PlacementCommand(string action, JsonObject args)
    {
        bool dryRun = Flag(args, "dryRun");
        switch (action)
        {
            case "get":
            {
                if (args.ContainsKey("monitor")) throw new ArgumentException("Placement settings apply to the whole desk.");
                return new JsonObject
                {
                    ["value"] = SettingsDocument.Get(SettingsDocument.Read(), "/global/placement")?.DeepClone(),
                    ["windowsRemembersLocations"] = WindowsWindowMemory.Remember,
                    ["windowsMinimizesOnDisconnect"] = WindowsWindowMemory.MinimizeOnDisconnect,
                };
            }
            case "set":
            case "reset":
            {
                if (args.ContainsKey("monitor")) throw new ArgumentException("Placement settings apply to the whole desk.");
                // Bookkeeping survives a reset: it is how Windows' own window
                // memory is handed back once putting windows back is off.
                bool tookOver = SettingsStore.Load().Global.Placement.TookOverWindowsMemory;
                JsonObject result = (JsonObject)GroupCommand("placement." + action, args);
                if (dryRun) return result;
                if (action == "reset" && tookOver)
                    SettingsDocument.Update(d => d["global"]!["placement"]!["tookOverWindowsMemory"] = true, false);
                ReconcileWindowMemory();
                return result;
            }
            case "gather":
            {
                if (args.Any(p => p.Key is not ("to" or "from" or "dryRun")))
                    throw new ArgumentException("placement gather takes --to (a display, or active) and --from (a display).");
                List<DisplayInfo> displays = Resolve(null);
                PlacementSettings placement = SettingsStore.Load().Global.Placement;
                string to = args["to"]?.ToString() ?? "active";
                DisplayInfo target = to is "active" or "pointer"
                    ? WindowMover.Active(placement, displays) ?? throw new InvalidOperationException("No display is in use.")
                    : Resolve(to, false).Single();
                List<DisplayInfo>? from = args["from"] is { } source ? Resolve(source.ToString(), false) : null;
                if (from is not null && from.Any(d => d.Key == target.Key)) throw new ArgumentException("--from and --to are the same display.");
                if (dryRun) return new JsonObject { ["state"] = "validated", ["to"] = target.Token };
                GatherOutcome outcome = WindowMover.Gather(target, placement, from);
                return new JsonObject
                {
                    ["state"] = "gathered", ["to"] = displays.FindIndex(d => d.Key == target.Key) + 1, ["name"] = target.Label,
                    ["moved"] = outcome.Moved,
                    ["skipped"] = new JsonArray(outcome.Skipped.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
                };
            }
            case "move":
            {
                if (args.Any(p => p.Key is not ("window" or "to" or "dryRun")))
                    throw new ArgumentException("placement move takes --window and --to.");
                string named = args["window"]?.ToString() ?? throw new ArgumentException("Which window? --window (a handle from pin list, an app's name, or part of a title).");
                AppWindow window = WindowPins.Find(named) ?? throw new ArgumentException($"No open window matches '{named}'. See pin list.");
                List<DisplayInfo> displays = Resolve(null);
                PlacementSettings placement = SettingsStore.Load().Global.Placement;
                string to = args["to"]?.ToString() ?? throw new ArgumentException("Onto which display? --to 2, or --to active.");
                DisplayInfo target = to is "active" or "pointer"
                    ? WindowMover.Active(placement, displays) ?? throw new InvalidOperationException("No display is in use.")
                    : Resolve(to, false).Single();
                DisplayInfo from = AppWindows.DisplayOf(window.Handle, displays) ?? throw new InvalidOperationException("That window is on no display.");
                if (from.Key == target.Key) return new JsonObject { ["state"] = "unchanged", ["window"] = Describe(window) };
                if (dryRun) return new JsonObject { ["state"] = "validated", ["window"] = Describe(window), ["to"] = target.Token };
                if (!WindowMover.Move(window.Handle, from, target, placement.KeepSize))
                    throw new InvalidOperationException($"Windows refused to move {window.Title}: it may be running as administrator.");
                return new JsonObject { ["state"] = "moved", ["window"] = Describe(AppWindows.Describe(window.Handle) ?? window),
                    ["to"] = displays.FindIndex(d => d.Key == target.Key) + 1 };
            }
            default:
                throw new ArgumentException("placement get|set|reset|gather|move.");
        }
    }

    /// <summary>Windows' own window memory handed over or back, and the bookkeeping saved.</summary>
    private static void ReconcileWindowMemory()
    {
        SettingsStore.WithWriteLock(() =>
        {
            DispCtrlSettings settings = SettingsStore.Load();
            if (WindowsWindowMemory.Reconcile(settings.Global.Placement)) SettingsStore.Save(settings);
            return true;
        });
    }

    // ------------------------------------------------------------------ ddc --

    private static JsonNode DdcCommand(string action, JsonObject args)
    {
        bool dryRun = Flag(args, "dryRun");
        switch (action)
        {
            case "get":
            {
                if (args.ContainsKey("monitor")) throw new ArgumentException("ddc get covers every monitor.");
                DispCtrlSettings settings = SettingsStore.Load();
                var probed = new JsonObject();
                foreach (var (token, monitor) in settings.Monitors)
                    if (monitor.ProbedCodes is { Count: > 0 } codes)
                        probed[token] = new JsonArray(codes.Select(c => (JsonNode?)JsonValue.Create("0x" + c)).ToArray());
                return new JsonObject
                {
                    ["guard"] = settings.Global.DdcGuard.Enabled,
                    ["blocked"] = SettingsDocument.Get(SettingsDocument.Read(), "/global/ddcGuard/blocked")?.DeepClone(),
                    ["probed"] = probed,
                };
            }
            case "set":
            {
                if (args.Any(p => p.Key is not ("guard" or "dryRun"))) throw new ArgumentException("ddc set takes --guard on|off.");
                bool guard = Flag(args, "guard");
                JsonObject result = SettingsDocument.Update(d => d["global"]!["ddcGuard"]!["enabled"] = guard, dryRun);
                DdcGuard.Invalidate();
                return result;
            }
            case "reset":
            {
                // The guard back on; the monitors it blocked stay blocked, like
                // Reset all: which monitor took Windows down is a fact, not a preference.
                if (args.Any(p => p.Key is not ("dryRun" or "revision"))) throw new ArgumentException("ddc reset takes no options.");
                JsonObject result = SettingsDocument.Update(d => d["global"]!["ddcGuard"]!["enabled"] = true, dryRun, Text(args, "revision"));
                DdcGuard.Invalidate();
                return result;
            }
            case "allow":
            {
                if (args.Any(p => p.Key is not ("monitor" or "model" or "token" or "dryRun")))
                    throw new ArgumentException("ddc allow takes --monitor, --token or --model.");
                string key = Text(args, "token") ?? Text(args, "model")
                    ?? (Text(args, "monitor") is { } m ? Resolve(m, false).Single().Token : null)
                    ?? throw new ArgumentException("Which monitor? --monitor 2, --token TOKEN or --model DEL-A234 (see ddc get).");
                DispCtrlSettings settings = SettingsStore.Load();
                if (!settings.Global.DdcGuard.Blocked.Any(b => string.Equals(b.Token, key, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(b.Model, key, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException($"Nothing by '{key}' is blocked. See ddc get.");
                if (dryRun) return new JsonObject { ["state"] = "validated" };
                DdcGuard.Allow(key);
                return new JsonObject { ["state"] = "allowed", ["allowed"] = key };
            }
            case "probe":
            {
                if (args.Any(p => p.Key is not ("monitor" or "save" or "clear" or "dryRun")))
                    throw new ArgumentException("ddc probe takes --monitor, and --save or --clear.");
                DisplayInfo display = Resolve(Text(args, "monitor") ?? throw new ArgumentException("Choose the monitor: --monitor 2."), false).Single();
                if (display.IsInternal) throw new ArgumentException($"{display.Label} is a built-in panel; it has no DDC/CI to probe.");
                if (Flag(args, "clear"))
                {
                    if (Flag(args, "save")) throw new ArgumentException("--save or --clear, not both.");
                    return SettingsDocument.Update(d =>
                    {
                        EnsureMonitor(d, display.Token);
                        d["monitors"]![display.Token]!["probedCodes"] = null;
                    }, dryRun);
                }
                if (DdcGuard.IsBlocked(display))
                    throw new InvalidOperationException($"{display.Label} is blocked by the DDC/CI guard; allow it first (ddc allow).");
                if (dryRun) return new JsonObject { ["state"] = "validated", ["monitor"] = display.Token };
                IReadOnlyList<MonitorCapabilities.ProbeAnswer> answers = MonitorCapabilities.Probe(display);
                var found = new JsonArray(answers.Select(a => (JsonNode?)new JsonObject
                {
                    ["code"] = a.Hex, ["name"] = a.Name, ["kind"] = a.Kind.ToString().ToLowerInvariant(),
                    ["current"] = a.Current, ["maximum"] = a.Maximum,
                }).ToArray());
                var result = new JsonObject { ["state"] = "probed", ["monitor"] = display.Token, ["name"] = display.Label, ["answered"] = found };
                if (Flag(args, "save"))
                {
                    if (answers.Count == 0) throw new InvalidOperationException($"{display.Label} answered none of the codes; there is nothing to save.");
                    SettingsDocument.Update(d =>
                    {
                        EnsureMonitor(d, display.Token);
                        d["monitors"]![display.Token]!["probedCodes"] = new JsonArray(answers.Select(a => (JsonNode?)JsonValue.Create(a.Code.ToString("X2"))).ToArray());
                    }, false);
                    MonitorCapabilities.Forget(display);
                    result["state"] = "saved";
                }
                return result;
            }
            default:
                throw new ArgumentException("ddc get|set|reset|allow|probe.");
        }
    }
}
