using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DispCtrl.Control;
using DispCtrl.Core.Settings;

namespace DispCtrl.Control;

public static class ControlTerminal
{
    private static readonly HashSet<string> Roots = ["commands", "status", "diagnostics", "report", "display", "displays", "settings", "focus", "oled", "awake", "taskbar", "tray", "windows", "engine", "apply", "watch", "request", "scripts", "unison", "startup", "gamma", "devices", "hotkeys"];
    public static bool Handles(string[] args) => args.Length > 0 && (Roots.Contains(args[0])
        || args[0] == "nightlight" && args.Length > 1 && args[1] is "get" or "set" or "reset"
        || args[0] == "topology" && args.Length > 1 && args[1] is "get" or "set");

    public const string Help = """
    DispCtrl control API v1
      displays list                         Connected monitors and stable identities
      display get [--hardware]              Current state; hardware reads are opt-in
      display modes|capabilities            Supported modes and every DDC code the monitor lists
      display controls --monitor ID [--all] The monitor's own controls, by key, with their values
      display control --monitor ID --name KEY [--value V]
                                            Read or set one: --name picture-mode --value games
      display set --monitor ID [options]    Mode, layout, scaling, HDR, brightness, wallpaper,
                                            --controls "contrast=70,input-source=hdmi-1"
      display identify                      Show each display's number on it
      display factory-reset --monitor ID --confirm
      display reset --monitor ID [--factory --confirm]  DispCtrl's settings for it (and the monitor's own)
      gamma get|set --unlocked on|off       Windows' gamma clamp (night light and dimming range)

    Device library (docs/DEVICE-LIBRARY.md)
      devices list                          Every monitor model seen here, and what is known of it
      devices show --monitor ID|--model KEY Every code: standard, mapped, or not yet named
      devices scan                          Sync: read every attached monitor now (the engine does it by itself)
      devices forget --monitor ID|--model KEY   Remove a model from the list until the next scan
      devices probe --monitor ID [--codes unknown|all|0xE2,0xF0] [--seconds 120]
                                            Watch codes while you change the monitor's own menu
      devices map --monitor ID --code 0xE2 --name "Preset mode" [--values "0x0B=ComfortView"]
                  [--kind range|choice|action|information] [--writable] [--scope model|brand|all]
      devices unmap|link|definitions        Remove, cross-link (--to DEL-A233), inspect layers
      devices share --monitor ID [--open]   Record and mappings as one prefilled issue
      devices validate FILE                 Check a definition before sharing it
      settings get|schema                   Full settings tree or generated JSON schema
      settings set --path /global/... --value JSON
      settings set --monitor ID --path alias --value office
      settings reset --path /global/focus
      settings export [--output FILE]       Complete saved settings (includes identities)
      settings validate|import FILE         Validate or replace saved settings
      focus|oled|awake|nightlight|taskbar|tray get|set|reset
      windows get|set                       Taskbar preferences, VRR, dark mode, --wallpaper-fit fill
      windows open --page display|nightlight|colors|taskbar|startup|power|hdr|cast|colormanagement
      hotkeys list|add|set|remove|reset     --keys "Ctrl+Alt+PageUp" --action unison-up --step 5 --display 2
      unison get|set --level 50             Shared brightness and Windows-slider following
      unison set --monitor ID --floor 20 --ceiling 80   A display's calibrated range
      startup get|set                       --engine, --preload-panel, --open-window, --start-menu, --desktop
      tray show                            Open the taskbar toolkit
      topology get|set --mode extend|duplicate|internal|external
      oled preview --percent 50             Two-second preview (engine + care enabled)
      oled rest --monitor ID --minutes 5    Manual screen rest policy
      engine start|stop|status              Manage the resident engine
      apply FILE [--dry-run]                Ordered display + settings operations
      watch [--events displays,settings,engine] [--interval 500]
      watch --script PATH.ps1               Run an explicit script on changed state
      scripts list|run FILE                 Inspect or run local external scripts
      request FILE|-                       Send a versioned JSON API request
      commands|status|diagnostics           Discover commands and inspect local state
      report [--what TEXT] [--steps TEXT]   Review a scrubbed bug report and its issue link

    Common: --json --local --dry-run --monitor ID --timeout 30000
    Settings options use kebab-case names from 'get': --dim-percent 50, --enabled on.
    Output: JSON results on stdout, diagnostics on stderr. --json uses one line.
    Exit codes: 0 success, 1 failed/partial, 2 invalid request, 4 timeout, 130 cancelled.
    Use DISPCTRL_DATA_DIR for an isolated absolute configuration directory.
    Existing flat commands (brightness, contrast, vcp, etc.) remain supported.
    """;

    public static async Task<int> RunAsync(string[] words)
    {
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            var options = Parse(words, out List<string> positional);
            bool json = RemoveFlag(options, "json"), local = RemoveFlag(options, "local");
            int timeout = options["timeout"]?.GetValue<int>() ?? 30000;
            options.Remove("timeout");
            if (timeout is < 100 or > 600000) throw new ArgumentException("Timeout must be 100..600000 milliseconds.");
            if (positional.Count == 0) throw new ArgumentException("A command is required.");
            if (options.Remove("help")) { Console.WriteLine(Help); return 0; }
            string root = positional[0], action = positional.Count > 1 ? positional[1] : "get";
            int maximum = root switch
            {
                "commands" or "status" or "diagnostics" or "report" or "watch" => 1,
                "settings" when action is "import" or "validate" => 3,
                "scripts" when action == "run" => 3,
                "devices" when action == "validate" => 3,
                _ => 2,
            };
            if (positional.Count > maximum) throw new ArgumentException("Unexpected positional argument: " + positional[maximum]);
            if (root == "watch") return await Watch(options, cancel.Token);
            if (root == "devices" && action == "probe") return await Probe(options, cancel.Token);
            if (root == "scripts")
            {
                if (options.Count > 0) throw new ArgumentException("scripts accepts no additional options.");
                return await Scripts(action, positional.ElementAtOrDefault(2), json, cancel.Token);
            }
            if (root == "engine" && action != "status")
            {
                if (options.Any(p => p.Key != "dryRun")) throw new ArgumentException("Unknown engine option.");
                if (action is not ("start" or "stop")) throw new ArgumentException("Engine action: start, stop, status.");
                if (RemoveFlag(options, "dryRun")) { Print(new JsonObject { ["state"] = "planned", ["action"] = action }, json); return 0; }
                return await Engine(action, json, cancel.Token);
            }
            if (root == "request" && options.Count > 0) throw new ArgumentException("Place operation arguments, including dryRun, inside the request's args object.");
            string command = root switch
            {
                "commands" or "status" or "diagnostics" or "report" or "apply" => root,
                "displays" => action is "list" or "get" ? "displays.list" : throw new ArgumentException("Use displays list."),
                "engine" => "status",
                _ => root + "." + (action == "export" && root == "settings" ? "get" : action),
            };
            if (root == "apply") options["document"] = await ReadDocument(positional.ElementAtOrDefault(1), cancel.Token);
            if (root == "settings" && action is "import" or "validate") options["document"] = await ReadDocument(positional.ElementAtOrDefault(2), cancel.Token);
            if (root == "devices" && action == "validate") options["document"] = await ReadDocument(positional.ElementAtOrDefault(2), cancel.Token);
            string? output = options["output"]?.GetValue<string>(); options.Remove("output");
            bool overwrite = RemoveFlag(options, "overwrite");
            if (overwrite && !(root == "settings" && action == "export" && output is not null)) throw new ArgumentException("--overwrite requires settings export --output FILE.");
            if (output is not null && !(root == "settings" && action == "export")) throw new ArgumentException("--output is only supported for settings export.");
            var request = root == "request" ? (await ReadDocument(positional.ElementAtOrDefault(1), cancel.Token)).AsObject()
                : new JsonObject { ["version"] = 1, ["id"] = Guid.NewGuid().ToString("N"), ["command"] = command, ["args"] = options };
            var result = await new ControlClient().ExecuteAsync(request, local, timeout, cancel.Token);
            int exit = result["exitCode"]!.GetValue<int>();
            if (exit == 0 && root == "settings" && action == "export")
            {
                string contents = result["data"]!["value"]!.ToJsonString(SettingsJsonContext.Default.Options);
                if (output is null) Console.WriteLine(contents);
                else
                {
                    string path = Path.GetFullPath(output);
                    using var file = new FileStream(path, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write);
                    using var writer = new StreamWriter(file);
                    await writer.WriteAsync(contents.AsMemory(), cancel.Token);
                    Print(new JsonObject { ["ok"] = true, ["path"] = path }, json);
                }
            }
            else Print(result, json);
            return exit;
        }
        catch (OperationCanceledException) { return Fail(words, 130, "Cancelled", "Cancelled. Check status before retrying an interrupted hardware operation."); }
        catch (Exception ex)
        {
            return Fail(words, ex is ArgumentException or JsonException or FormatException ? 2 : ex is TimeoutException ? 4 : 1,
                ex.GetType().Name, ex.Message);
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    // A script that asked for JSON parses stdout, so a refusal before the request
    // reaches the service still has to arrive in the service's envelope.
    private static int Fail(string[] words, int exit, string type, string message)
    {
        Console.Error.WriteLine(message);
        if (words.Contains("--json"))
            Print(new JsonObject
            {
                ["version"] = ControlService.ProtocolVersion, ["id"] = null, ["command"] = string.Join('.', words.Where(w => !w.StartsWith("--", StringComparison.Ordinal)).Take(2)),
                ["ok"] = false, ["exitCode"] = exit, ["elapsedMs"] = 0, ["data"] = null,
                ["error"] = new JsonObject { ["type"] = type, ["message"] = message },
            }, true);
        return exit;
    }

    private static void Print(JsonNode result, bool compact) => Console.WriteLine(compact ? result.ToJsonString() : result.ToJsonString(SettingsJsonContext.Default.Options));

    private static JsonObject Parse(string[] words, out List<string> positional)
    {
        positional = [];
        var options = new JsonObject();
        string[] flags = ["json", "local", "dry-run", "hardware", "overwrite", "help", "confirm", "all", "writable", "remove", "open", "factory", "history"];
        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];
            if (!word.StartsWith("--", StringComparison.Ordinal)) { positional.Add(word); continue; }
            string[] parts = word[2..].Split('=', 2);
            string[] segments = parts[0].Split('-');
            string key = segments[0] + string.Concat(segments.Skip(1).Select(s => s.Length == 0 ? "" : char.ToUpperInvariant(s[0]) + s[1..]));
            if (options.ContainsKey(key)) throw new ArgumentException("Duplicate option --" + parts[0]);
            string value;
            if (parts.Length == 2) value = parts[1];
            else if (flags.Contains(parts[0]) || parts[0] == "refresh" && words[0] == "displays") value = "true";
            else if (++i < words.Length && !words[i].StartsWith("--", StringComparison.Ordinal)) value = words[i];
            else throw new ArgumentException("Missing value for --" + parts[0]);
            // Selectors are strings even when the user chooses display number 2.
            if (key is "monitor" or "path" or "output" or "resolution" or "wallpaper" or "script" or "events" or "revision" or "what" or "steps") options[key] = value;
            else if (value is "on" or "off") options[key] = value == "on";
            else
            {
                try { options[key] = JsonNode.Parse(value); }
                catch (JsonException) { options[key] = value; }
            }
        }
        return options;
    }

    private static bool RemoveFlag(JsonObject options, string key)
    {
        if (!options.TryGetPropertyValue(key, out var node)) return false;
        if (node is not JsonValue value || !value.TryGetValue<bool>(out bool enabled))
            throw new ArgumentException(key + " must be true or false.");
        options.Remove(key);
        return enabled;
    }

    private static async Task<JsonNode> ReadDocument(string? file, CancellationToken ct)
    {
        if (file is null) throw new ArgumentException("Specify a JSON file, or '-' for stdin.");
        string text = file == "-" ? await Console.In.ReadToEndAsync(ct) : await File.ReadAllTextAsync(file, ct);
        if (System.Text.Encoding.UTF8.GetByteCount(text) > ControlTransport.MaxBytes) throw new ArgumentException("Document exceeds 1 MiB.");
        return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })
            ?? throw new ArgumentException("Document cannot be null.");
    }

    private static async Task<int> Engine(string action, bool json, CancellationToken ct)
    {
        if (action == "stop")
        {
            if (EventWaitHandle.TryOpenExisting(@"Local\DispCtrl.Engine.Stop", out var signal))
                using (signal) signal.Set();
            Print(new JsonObject { ["state"] = "stop-requested" }, json);
            return 0;
        }
        if (action != "start") throw new ArgumentException("Engine action: start, stop, status.");
        string? engine = FindEngine();
        if (engine is null) throw new FileNotFoundException("DispCtrl.Engine.exe is missing. Install the CLI + engine bundle, or use --local for one-shot commands.");
        var running = Process.GetProcessesByName("DispCtrl.Engine");
        bool exists = running.Length > 0;
        foreach (var process in running) process.Dispose();
        if (!exists)
        {
            using var process = Process.Start(new ProcessStartInfo(engine, "run") { UseShellExecute = false, CreateNoWindow = true });
            await Task.Delay(400, ct);
            if (process is null || process.HasExited) throw new InvalidOperationException("Engine exited during startup. See diagnostics and engine.log.");
        }
        Print(new JsonObject { ["state"] = exists ? "already-running" : "started" }, json);
        return 0;
    }

    private static string? FindEngine()
    {
        string beside = Path.Combine(AppContext.BaseDirectory, "DispCtrl.Engine.exe");
        if (File.Exists(beside)) return beside;
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        for (int i = 0; i < 9 && dir is not null; i++, dir = dir.Parent)
        {
            string build = Path.Combine(dir.FullName, "src/DispCtrl.Engine/bin/Release/net10.0-windows10.0.26100.0/win-x64/DispCtrl.Engine.exe");
            if (File.Exists(build)) return build;
        }
        return null;
    }

    private static async Task<int> Watch(JsonObject args, CancellationToken ct)
    {
        if (args.Any(p => p.Key is not ("interval" or "events" or "script"))) throw new ArgumentException("Watch options: events, interval, script.");
        int interval = args["interval"]?.GetValue<int>() ?? 500;
        if (interval is < 100 or > 60000) throw new ArgumentException("Watch interval must be 100..60000 ms.");
        string[] events = (args["events"]?.GetValue<string>() ?? "displays,settings,engine").Split(',');
        if (events.Any(e => e is not ("displays" or "settings" or "engine"))) throw new ArgumentException("Events: displays,settings,engine.");
        string? script = args["script"]?.GetValue<string>();
        if (script is not null && !File.Exists(script)) throw new FileNotFoundException("Event script not found.", script);
        var previous = new Dictionary<string, string>();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
        do
        {
            // Metadata-only polling: no brightness, capabilities or DDC sweep.
            var state = new Dictionary<string, string>();
            if (events.Contains("displays")) state["displays"] = DispCtrl.Core.Displays.DisplayRegistry.CheapSignature();
            if (events.Contains("settings")) state["settings"] = File.Exists(SettingsStore.Path_) ? File.GetLastWriteTimeUtc(SettingsStore.Path_).Ticks.ToString() : "absent";
            if (events.Contains("engine"))
            {
                var processes = Process.GetProcessesByName("DispCtrl.Engine");
                state["engine"] = string.Join(',', processes.Select(p => p.Id).Order());
                foreach (var p in processes) p.Dispose();
            }
            foreach (var pair in state)
            {
                if (previous.TryGetValue(pair.Key, out string? old) && old == pair.Value) continue;
                var evt = new JsonObject { ["version"] = 1, ["event"] = pair.Key, ["initial"] = !previous.ContainsKey(pair.Key),
                    ["atUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["value"] = pair.Value };
                previous[pair.Key] = pair.Value;
                Console.WriteLine(evt.ToJsonString());
                if (script is not null) await RunScript(script, evt.ToJsonString(), ct);
            }
        } while (await timer.WaitForNextTickAsync(ct));
        return 0;
    }

    /// <summary>
    /// Watches a monitor's codes and prints each change as it happens.
    /// </summary>
    /// <remarks>
    /// How an unknown code is worked out: run this, change one thing in the
    /// monitor's own menu, and see which code moved and to what. Reads bypass
    /// the cache, and keep the channel's 40 ms gap, so a sweep of twenty codes
    /// takes a couple of seconds. NDJSON on stdout, guidance on stderr.
    /// </remarks>
    private static async Task<int> Probe(JsonObject args, CancellationToken ct)
    {
        foreach (var pair in args)
            if (pair.Key is not ("monitor" or "codes" or "seconds" or "interval")) throw new ArgumentException("devices probe takes --monitor, --codes, --seconds, --interval.");
        var display = ControlService.Resolve(args["monitor"]?.GetValue<string>() ?? throw new ArgumentException("Choose --monitor."), false).Single();
        if (display.IsInternal) throw new ArgumentException($"{display.Label} is a built-in panel; it has no DDC/CI codes.");
        int seconds = args["seconds"]?.GetValue<int>() ?? 120, interval = args["interval"]?.GetValue<int>() ?? 500;
        if (seconds is < 1 or > 3600 || interval is < 0 or > 60000) throw new ArgumentException("--seconds 1..3600, --interval 0..60000 ms.");

        var capabilities = DispCtrl.Display.MonitorCapabilities.Read(display, readValues: false);
        if (!capabilities.Supported) throw new InvalidOperationException($"{display.Label} does not answer DDC/CI.");
        var known = DispCtrl.Core.Devices.DeviceLibrary.Resolve(display.Key.Model);
        string codes = args["codes"]?.ToString() ?? "unknown";
        var wanted = capabilities.Controls.Where(c => codes switch
        {
            "all" => true,
            "unknown" => !DispCtrl.Display.MonitorCapabilities.IsNamed(c.Code) && !known.ContainsKey(c.Code),
            _ => codes.Split(',').Select(t => DispCtrl.Core.Devices.DeviceDefinitions.ParseCode(t)).Contains(c.Code),
        }).ToList();
        if (wanted.Count == 0) throw new ArgumentException("No codes to watch: try --codes all.");

        await Console.Error.WriteLineAsync($"Watching {wanted.Count} code(s) on {display.Label} for {seconds} s. Change one setting at a time in the monitor's own menu; each code that moves is printed. Ctrl+C stops.");
        var last = new Dictionary<byte, int>();
        var clock = Stopwatch.StartNew();
        bool first = true;
        while (clock.Elapsed.TotalSeconds < seconds && !ct.IsCancellationRequested)
        {
            await Task.Run(() => DispCtrl.Display.MonitorCapabilities.ReadValues(display, wanted), ct);
            foreach (var c in wanted)
            {
                if (c.Current < 0) continue;
                bool had = last.TryGetValue(c.Code, out int before);
                if (had && before == c.Current) continue;
                last[c.Code] = c.Current;
                var evt = new JsonObject
                {
                    ["event"] = first || !had ? "initial" : "changed",
                    ["code"] = c.Hex, ["name"] = known.TryGetValue(c.Code, out var r) ? r.Definition.Name : c.Name,
                    ["from"] = had ? before : null, ["to"] = c.Current, ["low"] = c.Current & 0xFF,
                    ["atMs"] = (long)clock.Elapsed.TotalMilliseconds,
                };
                Console.WriteLine(evt.ToJsonString());
            }
            first = false;
            try { await Task.Delay(interval, ct); } catch (OperationCanceledException) { break; }
        }
        return 0;
    }

    private static async Task<int> Scripts(string action, string? path, bool json, CancellationToken ct)
    {
        string folder = Path.Combine(SettingsStore.Directory, "scripts");
        if (action == "list")
        {
            var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.ps1") : [];
            Print(new JsonObject { ["folder"] = folder, ["scripts"] = new JsonArray(files.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()) }, json);
            return 0;
        }
        if (action != "run" || path is null) throw new ArgumentException("Use scripts list, or scripts run PATH.ps1.");
        return await RunScript(path, "{}", ct);
    }

    private static async Task<int> RunScript(string path, string eventJson, CancellationToken ct)
    {
        string absolute = Path.GetFullPath(path);
        if (!File.Exists(absolute) || !Path.GetExtension(absolute).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Specify an existing PowerShell .ps1 script.");
        // PowerShell 7 is not part of Windows; 5.1 is, and runs the same scripts.
        var info = new ProcessStartInfo(OnPath("pwsh.exe") ?? "powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive"); info.ArgumentList.Add("-File"); info.ArgumentList.Add(absolute);
        info.Environment["DISPCTRL_EVENT_JSON"] = eventJson;
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start PowerShell.");
        Task stdout = Pump(process.StandardOutput, ct), stderr = Pump(process.StandardError, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        await Task.WhenAll(stdout, stderr);
        return process.ExitCode;
    }

    private static string? OnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(d.Trim(), exe)).FirstOrDefault(File.Exists);

    private static async Task Pump(StreamReader reader, CancellationToken ct)
    {
        while (await reader.ReadLineAsync(ct) is { } line) await Console.Error.WriteLineAsync(line);
    }
}
