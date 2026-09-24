using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

namespace DispCtrl.Control;

/// <summary>Public operations shared by terminal, desktop clients and the resident broker.</summary>
public sealed partial class ControlService
{
    private static readonly DispCtrl.Core.Caching.BoundedCache<string, DisplayInfo[]> InventoryCache = new(2);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> PendingBrightness = new(StringComparer.OrdinalIgnoreCase);
    private static long _nextBrightness;
    public const int ProtocolVersion = 1;
    public static readonly string[] Commands = ["status", "displays.list", "display.get", "display.modes", "display.capabilities",
        "display.set", "display.controls", "display.control", "display.identify", "display.factory-reset", "display.reset", "gamma.get", "gamma.set", "windows.open",
        "hotkeys.list", "hotkeys.add", "hotkeys.set", "hotkeys.remove", "hotkeys.reset",
        "devices.list", "devices.show", "devices.scan", "devices.forget", "devices.map", "devices.unmap", "devices.link", "devices.panel", "devices.definitions",
        "devices.share", "devices.validate", "settings.get", "settings.set", "settings.reset", "settings.schema", "settings.validate", "settings.import",
        "focus.get", "focus.set", "focus.reset", "oled.get", "oled.set", "oled.reset", "oled.preview", "oled.rest", "awake.displays-off",
        "restore.now", "restore.undo", "restore.get",
        "awake.get", "awake.set", "awake.reset", "nightlight.get", "nightlight.set", "nightlight.reset",
        "taskbar.get", "taskbar.set", "taskbar.reset", "tray.get", "tray.set", "tray.reset", "windows.get", "windows.set",
        "topology.get", "topology.set", "startup.get", "startup.set", "unison.get", "unison.set", "maintenance.repair", "maintenance.clear-cache", "tray.show", "apply", "commands", "diagnostics", "report"];

    public JsonObject Execute(JsonObject request)
    {
        var watch = Stopwatch.StartNew();
        string id = Guid.NewGuid().ToString("N");
        string command = "";
        try
        {
            if (request.Any(p => p.Key is not ("version" or "id" or "command" or "args"))) throw new ArgumentException("Unknown request envelope field.");
            id = Text(request, "id") ?? id;
            command = Text(request, "command") ?? "";
            if (request.ContainsKey("version")) _ = Integer(request, "version", ProtocolVersion, ProtocolVersion);
            JsonObject args = request["args"] is null ? new() : request["args"] as JsonObject ?? throw new ArgumentException("args must be an object.");
            ValidateArgs(command, args);
            string? brightnessKey = null;
            long ticket = 0;
            if (command == "display.set" && args.ContainsKey("coalesce"))
            {
                bool coalesce = Flag(args, "coalesce");
                if (args.Any(p => p.Key is not ("monitor" or "brightness" or "coalesce" or "dryRun")))
                    throw new ArgumentException("Coalescing is supported only for individual brightness requests.");
                _ = Integer(args, "brightness", 0, 100);
                brightnessKey = Text(args, "monitor") ?? throw new ArgumentException("Coalescing requires a monitor.");
                if (coalesce && !Flag(args, "dryRun")) { ticket = Interlocked.Increment(ref _nextBrightness); PendingBrightness[brightnessKey] = ticket; }
                else brightnessKey = null;
                args = (JsonObject)args.DeepClone();
                args.Remove("coalesce");
            }
            bool mutation = command.EndsWith(".set", StringComparison.Ordinal) || command.EndsWith(".reset", StringComparison.Ordinal)
                || command is "settings.import" or "oled.rest" or "restore.now" or "restore.undo" or "awake.displays-off" or "apply" or "display.factory-reset" or "display.reset"
                or "hotkeys.add" or "hotkeys.remove"
                || command == "display.control" && args.ContainsKey("value");
            using var gate = new Mutex(false, @"Local\DispCtrl.Control.Operations");
            bool held;
            try { held = !mutation || gate.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { held = true; }
            JsonNode? data;
            try
            {
                if (!held) throw new TimeoutException("Another operation is still using the display controls.");
                data = brightnessKey is not null && (!PendingBrightness.TryGetValue(brightnessKey, out long newest) || newest != ticket)
                    ? new JsonObject { ["state"] = "superseded" } : Dispatch(command, args);
            }
            finally
            {
                if (brightnessKey is not null) PendingBrightness.TryRemove(new KeyValuePair<string, long>(brightnessKey, ticket));
                if (mutation && held) { InventoryCache.Clear(); gate.ReleaseMutex(); }
            }
            return Result(id, command, data?["state"]?.GetValue<string>() == "partial" ? 1 : 0, data, null, watch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            int code = ex is ArgumentException or JsonException or FormatException or OverflowException ? 2
                : ex is TimeoutException ? 4 : 1;
            return Result(id, command, code, null, new JsonObject { ["type"] = ex.GetType().Name, ["message"] = ex.Message }, watch.ElapsedMilliseconds);
        }
    }

    private static JsonObject Result(string id, string command, int exit, JsonNode? data, JsonNode? error, long elapsed) => new()
    {
        ["version"] = ProtocolVersion, ["id"] = id, ["command"] = command, ["ok"] = exit == 0,
        ["exitCode"] = exit, ["elapsedMs"] = elapsed, ["data"] = data, ["error"] = error,
    };

    private JsonNode? Dispatch(string command, JsonObject args)
    {
        if (!Commands.Contains(command, StringComparer.Ordinal)) throw new ArgumentException("Unknown command: " + command);
        if (command == "commands") return new JsonObject { ["commands"] = new JsonArray(Commands.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) };
        if (command is "status" or "diagnostics") return Status(command == "diagnostics");
        if (command == "report") return ReportCommand(args);
        if (command == "settings.schema") return new JsonObject { ["schema"] = SettingsDocument.Schema() };
        if (command.StartsWith("settings.", StringComparison.Ordinal)) return SettingsCommand(command[9..], args);
        if (command == "displays.list") return new JsonObject { ["displays"] = Inventory(Flag(args, "refresh")) };
        if (command.StartsWith("display.", StringComparison.Ordinal)) return DisplayCommand(command[8..], args);
        if (command.StartsWith("windows.", StringComparison.Ordinal)) return WindowsCommand(command[8..], args);
        if (command.StartsWith("startup.", StringComparison.Ordinal)) return StartupCommand(command[8..], args);
        if (command.StartsWith("devices.", StringComparison.Ordinal)) return DevicesCommand(command[8..], args);
        if (command.StartsWith("hotkeys.", StringComparison.Ordinal)) return HotkeysCommand(command[8..], args);
        if (command.StartsWith("unison.", StringComparison.Ordinal)) return UnisonCommand(command[7..], args);
        if (command.StartsWith("maintenance.", StringComparison.Ordinal)) return MaintenanceCommand(command[12..], args);
        if (command == "gamma.get")
        {
            var state = GammaRange.Read();
            return new JsonObject { ["unlocked"] = state.Unlocked, ["value"] = state.Value,
                ["warmestKelvin"] = NightLight.WarmestAvailableKelvin };
        }
        if (command == "gamma.set")
        {
            if (args.Any(p => p.Key is not ("unlocked" or "dryRun"))) throw new ArgumentException("gamma set takes --unlocked on|off.");
            bool unlock = Flag(args, "unlocked");
            if (Flag(args, "dryRun")) return new JsonObject { ["state"] = "validated", ["unlocked"] = unlock };
            // Asks for elevation once: one machine-wide registry value.
            if (!GammaRange.Write(unlock)) throw new InvalidOperationException("The change needs administrator rights, and the prompt was declined or failed.");
            NightLight.Recheck();
            return new JsonObject { ["state"] = "applied", ["unlocked"] = GammaRange.Read().Unlocked };
        }
        if (command == "tray.show")
        {
            if (!Flag(args, "dryRun") && !QuickPanelSignal.Summon()) throw new InvalidOperationException("Open the desktop app once so the toolkit can be located.");
            return new JsonObject { ["state"] = Flag(args, "dryRun") ? "validated" : "requested" };
        }
        if (command.StartsWith("topology.", StringComparison.Ordinal))
        {
            if (command == "topology.get") return new JsonObject { ["topology"] = DisplayRegistry.Topology().ToString() };
            DesktopArrangement mode = Text(args, "mode")?.ToLowerInvariant() switch
            {
                "extend" => DesktopArrangement.Extend, "duplicate" => DesktopArrangement.Duplicate,
                "internal" => DesktopArrangement.InternalOnly, "external" => DesktopArrangement.ExternalOnly,
                _ => throw new ArgumentException("Topology mode: extend, duplicate, internal, external."),
            };
            if (!Flag(args, "dryRun") && !DesktopLayout.Apply(mode)) throw new InvalidOperationException("Windows refused the topology change.");
            return new JsonObject { ["state"] = Flag(args, "dryRun") ? "validated" : "applied", ["topology"] = mode.ToString() };
        }
        if (command == "apply") return Apply(args);
        return GroupCommand(command, args);
    }

    public static List<DisplayInfo> Resolve(string? selector, bool defaultAll = true)
    {
        List<DisplayInfo> all = DisplayRegistry.Enumerate().OrderByDescending(d => d.IsInternal).ThenBy(d => d.Bounds.Left).ThenBy(d => d.Bounds.Top).ToList();
        if (selector == "all" || selector is null && defaultAll) return all;
        if (selector is null) throw new ArgumentException("Specify --monitor <number, token or alias>.");
        if (int.TryParse(selector, out int n))
            return n > 0 && n <= all.Count ? [all[n - 1]] : throw new ArgumentException("Display number is not connected.");
        DispCtrlSettings settings = SettingsStore.Load();
        List<DisplayInfo> matches = all.Where(d => d.Token.Equals(selector, StringComparison.OrdinalIgnoreCase)
            || settings.Monitors.TryGetValue(d.Token, out var m) && m.Alias.Equals(selector, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches : throw new ArgumentException(matches.Count == 0 ? "Monitor is not connected: " + selector : "Ambiguous monitor selector: " + selector);
    }

    private static JsonArray Inventory(bool refresh = false)
    {
        if (refresh) InventoryCache.Clear();
        var snapshot = InventoryCache.Get(DisplayRegistry.CheapSignature(), TimeSpan.FromSeconds(2),
            () => DisplayRegistry.Enumerate().OrderByDescending(d => d.IsInternal).ThenBy(d => d.Bounds.Left).ThenBy(d => d.Bounds.Top).ToArray());
        var settings = SettingsStore.Load();
        var result = new JsonArray();
        int number = 0;
        foreach (var d in snapshot)
        {
            result.Add((JsonNode)new JsonObject { ["number"] = ++number, ["token"] = d.Token, ["name"] = d.Label,
                ["alias"] = settings.Monitors.GetValueOrDefault(d.Token)?.Alias ?? "", ["primary"] = d.IsPrimary,
                ["internal"] = d.IsInternal, ["connector"] = d.Connector.ToString(),
                ["x"] = d.Bounds.Left, ["y"] = d.Bounds.Top, ["width"] = d.Bounds.Width, ["height"] = d.Bounds.Height,
                ["refreshHz"] = d.RefreshHz, ["scalePercent"] = d.Scale * 100,
                ["physicalWidthMm"] = d.PhysicalWidthMm, ["physicalHeightMm"] = d.PhysicalHeightMm });
        }
        return result;
    }

    private static JsonObject Status(bool diagnostics)
    {
        var processes = Process.GetProcessesByName("DispCtrl.Engine");
        try
        {
            JsonObject settings = SettingsDocument.Read();
            return new JsonObject { ["engineRunning"] = processes.Length > 0,
                ["processId"] = processes.FirstOrDefault()?.Id, ["settingsRevision"] = SettingsDocument.Revision(settings),
                ["displays"] = Inventory(), ["dataDirectory"] = diagnostics ? SettingsStore.Directory : null,
                ["logPath"] = diagnostics ? SettingsStore.LogPath : null,
                ["runtime"] = diagnostics ? System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription : null,
                ["os"] = diagnostics ? Environment.OSVersion.VersionString : null };
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static JsonNode SettingsCommand(string action, JsonObject args)
    {
        string path = Text(args, "path") ?? "";
        string? monitor = Text(args, "monitor");
        string? token = monitor is null ? null : Resolve(monitor, false).Single().Token;
        if (token is not null) path = "/monitors/" + token + "/" + path.Trim('/');
        JsonObject document = SettingsDocument.Read();
        EnsureMonitor(document, token);
        if (action == "get") return new JsonObject { ["revision"] = SettingsDocument.Revision(document), ["value"] = SettingsDocument.Get(document, path)?.DeepClone() };
        if (action is "validate" or "import")
        {
            JsonNode input = args["document"] ?? throw new ArgumentException("A settings document is required.");
            SettingsDocument.Validate(input);
            return SettingsDocument.Update(target =>
            {
                target.Clear();
                foreach (var p in input.AsObject()) target[p.Key] = p.Value?.DeepClone();
            }, action == "validate" || Flag(args, "dryRun"), Text(args, "revision"));
        }
        if (action is not ("set" or "reset")) throw new ArgumentException("Unknown settings action.");
        if (action == "reset" && token is null && path.Length == 0)
            return SettingsDocument.Update(target =>
            {
                DispCtrlSettings settings = SettingsDocument.Validate(target);
                settings.ResetAll();
                JsonObject reset = SettingsDocument.Encode(settings);
                target.Clear();
                foreach (var pair in reset) target[pair.Key] = pair.Value?.DeepClone();
            }, Flag(args, "dryRun"), Text(args, "revision"));
        JsonNode? value;
        if (action == "reset")
        {
            JsonObject defaults = SettingsDocument.Encode(new DispCtrlSettings());
            EnsureMonitor(defaults, token);
            value = SettingsDocument.Get(defaults, path);
        }
        else
        {
            if (!args.ContainsKey("value")) throw new ArgumentException("--value is required (JSON value or a string).");
            value = args["value"];
        }
        return SettingsDocument.Update(target => { EnsureMonitor(target, token); SettingsDocument.Set(target, path, value); },
            Flag(args, "dryRun"), Text(args, "revision"));
    }

    private static void EnsureMonitor(JsonObject document, string? token)
    {
        if (token is null || document["monitors"]![token] is not null) return;
        var defaults = new DispCtrlSettings(); defaults.For(token);
        document["monitors"]![token] = SettingsDocument.Encode(defaults)["monitors"]![token]!.DeepClone();
    }

    private static JsonNode GroupCommand(string command, JsonObject args)
    {
        string[] pieces = command.Split('.');
        string group = pieces[0], action = pieces[1];
        if (command == "oled.preview")
        {
            int level = Integer(args, "percent", 0, 100);
            if (!Flag(args, "dryRun") && !OledPreview.TryShow(level)) throw new InvalidOperationException("Start the engine and enable OLED idle protection to preview.");
            return new JsonObject { ["state"] = Flag(args, "dryRun") ? "validated" : "requested" };
        }
        if (command == "awake.displays-off")
        {
            // A request the engine carries out after the delay and clears when
            // every display it turned off has woken; see AwakeSettings.DisplaysOffUtc.
            if (args["enabled"] is not JsonValue flag || !flag.TryGetValue(out bool on))
                throw new ArgumentException("--enabled on or off.");
            return SettingsDocument.Update(document =>
                document["global"]!["awake"]!["displaysOffUtc"] = on ? DateTimeOffset.UtcNow.ToString("O") : null,
                Flag(args, "dryRun"));
        }
        if (command == "restore.get")
        {
            JsonNode? recorded = SettingsDocument.Read()["global"]?["beforeRestore"];
            return new JsonObject { ["recorded"] = recorded is not null, ["beforeRestore"] = recorded?.DeepClone() };
        }
        if (command is "restore.now" or "restore.undo")
        {
            // The same two steps as Ctrl+Alt+Backspace and the Settings page, on
            // the typed settings rather than paths, so the three can never differ.
            bool undo = command == "restore.undo";
            return SettingsDocument.Update(document =>
            {
                DispCtrlSettings settings = SettingsDocument.Validate(document);
                if (undo && !settings.UndoRestoreVisibility())
                    throw new InvalidOperationException("Nothing to put back: the way back has not switched anything off since it was last undone.");
                if (!undo) settings.RestoreVisibility();
                JsonObject encoded = SettingsDocument.Encode(settings);
                foreach (var pair in encoded) document[pair.Key] = pair.Value?.DeepClone();
            }, Flag(args, "dryRun"));
        }
        if (command == "oled.rest")
        {
            var displays = Resolve(Text(args, "monitor"), false);
            int minutes = Integer(args, "minutes", 1, 30);
            return SettingsDocument.Update(document =>
            {
                foreach (var d in displays)
                {
                    EnsureMonitor(document, d.Token);
                    document["monitors"]![d.Token]!["oledRestUntilUtc"] = DateTimeOffset.UtcNow.AddMinutes(minutes).ToString("O");
                }
            }, Flag(args, "dryRun"));
        }
        string path = group switch { "focus" => "/global/focus", "oled" => "/global/oledCare", "awake" => "/global/awake",
            "nightlight" => "/global/nightLight", "tray" => "/global/quickPanel", "taskbar" => "/global", _ => throw new ArgumentException("Unknown settings group.") };
        string? selector = Text(args, "monitor");
        string? token = selector is null ? null : Resolve(selector, false).Single().Token;
        if (token is not null) path = "/monitors/" + token;
        if (action == "get" && group == "taskbar" && token is null)
        {
            // The taskbar's fields live loose in /global; handing back all of
            // /global answered "taskbar" with unison, night light and the rest.
            JsonObject all = SettingsCommand("get", new JsonObject { ["path"] = path })["value"]!.AsObject();
            var only = new JsonObject();
            foreach (string key in TaskbarKeys) only[key] = all[key]?.DeepClone();
            return new JsonObject { ["value"] = only };
        }
        if (action == "get") return SettingsCommand("get", token is null ? new JsonObject { ["path"] = path }
            : new JsonObject { ["monitor"] = selector });
        if (action == "reset")
        {
            if (token is null && group != "taskbar") return SettingsCommand("reset", new JsonObject { ["path"] = path, ["dryRun"] = Flag(args, "dryRun") });
            return SettingsDocument.Update(document =>
            {
                EnsureMonitor(document, token);
                JsonObject defaults = SettingsDocument.Encode(new DispCtrlSettings()); EnsureMonitor(defaults, token);
                JsonObject target = SettingsDocument.Get(document, path)!.AsObject();
                JsonObject fresh = SettingsDocument.Get(defaults, path)!.AsObject();
                string[] keys = group switch
                {
                    "taskbar" when token is not null => ["hideTaskbar", "reclaimWorkArea"],
                    "taskbar" => TaskbarKeys,
                    "oled" => ["oledProtection", "oledWakeOnPointerReturn", "oledRestMinutes", "oledRestUntilUtc"],
                    "focus" => ["focusDimming"],
                    "nightlight" => ["nightLightStrength", "nightLightFloor", "nightLightCeiling"],
                    _ => throw new ArgumentException("This group has no per-monitor reset."),
                };
                foreach (string key in keys) target[key] = fresh[key]?.DeepClone();
            }, Flag(args, "dryRun"));
        }
        return SettingsDocument.Update(document =>
        {
            EnsureMonitor(document, token);
            int count = 0;
            foreach (var pair in args)
            {
                if (pair.Key is "monitor" or "dryRun" or "revision") continue;
                string name = pair.Key;
                JsonNode? value = pair.Value;
                if (name == "wake" && group == "oled" && token is not null)
                {
                    string mode = value?.GetValue<string>() ?? "";
                    if (mode is not ("pointer-return" or "any-input")) throw new ArgumentException("--wake accepts pointer-return or any-input.");
                    name = "oledWakeOnPointerReturn"; value = JsonValue.Create(mode == "pointer-return");
                }
                name = group == "taskbar" ? name switch { "opacity" => "taskbarOpacity", "glass" => "taskbarGlassEnabled", "blur" => "taskbarGlassRadius",
                    "tint" => "taskbarGlassTint", "hide" => "hideTaskbar", "reclaimSpace" => "reclaimWorkArea", _ => name } : name;
                if (group == "oled" && token is not null && name == "enabled") name = "oledProtection";
                // Every taskbar field is loose in /global, so an unchecked name
                // here could set unison or night light by accident.
                if (group == "taskbar" && token is null && !TaskbarKeys.Contains(name))
                    throw new ArgumentException($"Not a taskbar setting: {pair.Key}. Taskbar settings: opacity, glass, blur, tint, {string.Join(", ", TaskbarKeys.Skip(4))}.");
                if (group == "nightlight" && name is "from" or "to")
                {
                    if (!TimeSpan.TryParseExact(value?.ToString(), @"h\:mm", null, out TimeSpan at) || at.TotalHours >= 24)
                        throw new ArgumentException($"--{name} is a time of day: 20:00.");
                    name += "Minutes"; value = JsonValue.Create((int)at.TotalMinutes);
                }
                SettingsDocument.Set(document, path + "/" + name, value);
                count++;
            }
            if (count == 0) throw new ArgumentException("Provide at least one setting; use get to inspect available fields.");
        }, Flag(args, "dryRun"), Text(args, "revision"));
    }

    private static readonly string[] TaskbarKeys = ["taskbarOpacity", "taskbarGlassEnabled", "taskbarGlassRadius", "taskbarGlassTint",
        "hideDelayMs", "animMs", "revealPx", "armDistancePx", "idlePollMs", "farPollMs", "armedPollMs", "shownPollMs"];

    internal static string? Text(JsonObject args, string key) => args[key] is null ? null
        : args[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : throw new ArgumentException(key + " must be a string.");
    private static void ValidateArgs(string command, JsonObject args)
    {
        string[]? allowed = command switch
        {
            "commands" or "status" or "diagnostics" or "settings.schema" or "topology.get" or "windows.get" => [],
            "displays.list" => ["refresh"],
            "report" => ["what", "steps"],
            "settings.get" => ["path", "monitor"],
            "settings.set" => ["path", "monitor", "value", "revision", "dryRun"],
            "settings.reset" => ["path", "monitor", "revision", "dryRun"],
            "settings.validate" or "settings.import" or "apply" => ["document", "revision", "dryRun"],
            "display.get" => ["monitor", "hardware"],
            "display.modes" or "display.capabilities" => ["monitor"],
            "topology.set" => ["mode", "dryRun"],
            "oled.preview" => ["percent", "dryRun"],
            "tray.show" => ["dryRun"],
            "startup.get" or "unison.get" => [],
            "oled.rest" => ["monitor", "minutes", "dryRun"],
            "awake.displays-off" => ["enabled", "dryRun"],
            "restore.now" or "restore.undo" => ["dryRun"],
            "restore.get" => [],
            "display.reset" => ["monitor", "factory", "confirm", "dryRun"],
            _ when command.EndsWith(".get", StringComparison.Ordinal) => ["monitor"],
            _ when command.EndsWith(".reset", StringComparison.Ordinal) => ["monitor", "dryRun", "revision"],
            _ => null,
        };
        if (allowed is not null)
            foreach (var pair in args) if (!allowed.Contains(pair.Key)) throw new ArgumentException("Unknown option: " + pair.Key);
    }
    internal static bool Flag(JsonObject args, string key) => args[key] is null ? false
        : args[key] is JsonValue value && value.TryGetValue<bool>(out bool flag) ? flag : throw new ArgumentException(key + " must be true or false.");
    internal static int Integer(JsonObject args, string key, int min, int max)
    {
        if (args[key] is not JsonValue node || !node.TryGetValue<int>(out int value)) throw new ArgumentException(key + " must be an integer.");
        if (value < min || value > max) throw new ArgumentException($"{key} must be between {min} and {max}.");
        return value;
    }
}
