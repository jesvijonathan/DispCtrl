using System.Text.Json.Nodes;
using DispCtrl.Core.Devices;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

namespace DispCtrl.Control;

public sealed partial class ControlService
{
    private sealed record Step(int Order, string Name, string? Monitor, Func<bool> Run);

    private static JsonNode DisplayCommand(string action, JsonObject args)
    {
        if (action == "set") return RunSteps(PlanDisplay(args), Flag(args, "dryRun"));
        if (action == "controls") return ControlsCommand(args);
        if (action == "reset") return ResetDisplay(args);
        if (action == "control") return ControlCommand(args);
        if (action == "identify")
        {
            if (args.Any(p => p.Key != "dryRun")) throw new ArgumentException("display identify takes no options; it numbers every display.");
            if (Flag(args, "dryRun")) return new JsonObject { ["state"] = "validated" };
            if (!QuickPanelSignal.Identify()) throw new InvalidOperationException("Open the desktop app once so DispCtrl can be located.");
            return new JsonObject { ["state"] = "requested" };
        }
        if (action == "factory-reset")
        {
            if (args.Any(p => p.Key is not ("monitor" or "confirm" or "dryRun"))) throw new ArgumentException("display factory-reset takes --monitor and --confirm.");
            DisplayInfo target = Resolve(Text(args, "monitor") ?? throw new ArgumentException("Choose the monitor: --monitor 2."), false).Single();
            if (target.IsInternal) throw new ArgumentException($"{target.Label} is a built-in panel; it has no factory settings to restore over DDC/CI.");
            if (!Flag(args, "confirm")) throw new ArgumentException($"This restores every setting on {target.Label} to how it left the factory. Add --confirm to go ahead.");
            if (Flag(args, "dryRun")) return new JsonObject { ["state"] = "validated", ["monitor"] = target.Token };
            if (!MonitorCapabilities.RestoreFactory(target)) throw new InvalidOperationException($"{target.Label} refused the factory reset.");
            return new JsonObject { ["state"] = "applied", ["monitor"] = target.Token };
        }
        List<DisplayInfo> targets = Resolve(Text(args, "monitor"));
        var displays = new JsonArray();
        foreach (DisplayInfo d in targets)
        {
            var entry = new JsonObject { ["token"] = d.Token, ["name"] = d.Label };
            entry["connection"] = new JsonObject { ["connector"] = d.Connector.ToString(),
                ["connectorInstance"] = d.ConnectorInstance, ["adapterId"] = d.AdapterId, ["targetId"] = d.TargetId,
                ["sharingConnector"] = d.SharingConnector, ["chainPosition"] = null, ["tunnelled"] = d.Tunnelled ? true : null,
                ["mstDescription"] = d.MstDescription, ["thunderboltDescription"] = d.ThunderboltDescription };
            if (action == "modes")
            {
                var modes = new JsonArray();
                foreach (var mode in DisplayModes.Available(d.GdiName)) modes.Add((JsonNode)new JsonObject
                    { ["width"] = mode.Width, ["height"] = mode.Height, ["refreshHz"] = mode.RefreshHz, ["bitsPerPixel"] = mode.Bpp });
                entry["modes"] = modes;
            }
            else if (action == "capabilities")
            {
                var capabilities = MonitorCapabilities.Read(d);
                var controls = new JsonArray();
                foreach (var c in capabilities.Controls)
                {
                    var values = new JsonArray();
                    foreach (var value in c.Values) values.Add((JsonNode)new JsonObject { ["value"] = value.Value, ["name"] = value.Name });
                    controls.Add((JsonNode)new JsonObject { ["code"] = c.Hex, ["name"] = c.Name, ["current"] = c.Current,
                        ["maximum"] = c.Maximum, ["settable"] = c.Settable, ["values"] = values });
                }
                entry["ddcSupported"] = capabilities.Supported; entry["controls"] = controls;
                entry["rawCapabilities"] = capabilities.Raw;
            }
            else if (action == "get")
            {
                entry["width"] = d.Bounds.Width; entry["height"] = d.Bounds.Height;
                entry["x"] = d.Bounds.Left; entry["y"] = d.Bounds.Top; entry["refreshHz"] = d.RefreshHz;
                entry["primary"] = d.IsPrimary; entry["scalePercent"] = d.Scale * 100;
                if (Flag(args, "hardware"))
                {
                    var brightness = Brightness.Read(d); var hdr = AdvancedDisplay.ReadHdr(d);
                    var scale = AdvancedDisplay.ReadScaling(d);
                    entry["brightness"] = new JsonObject { ["supported"] = brightness.Supported, ["percent"] = brightness.Percent,
                        ["current"] = brightness.Current, ["minimum"] = brightness.Min, ["maximum"] = brightness.Max };
                    entry["hdr"] = new JsonObject { ["supported"] = hdr.Supported, ["enabled"] = hdr.Enabled };
                    entry["scaling"] = new JsonObject { ["supported"] = scale.Supported, ["current"] = scale.Current };
                    entry["wallpaper"] = Wallpaper.Read(d);
                }
            }
            else throw new ArgumentException("Unknown display action.");
            displays.Add((JsonNode)entry);
        }
        return new JsonObject { ["displays"] = displays };
    }

    private static List<Step> PlanDisplay(JsonObject args)
    {
        string[] supported = ["monitor", "dryRun", "brightness", "resolution", "refresh", "orientation", "primary", "x", "y", "scale",
            "hdr", "contrast", "volume", "sharpness", "redGain", "greenGain", "blueGain", "colorPreset", "input", "power", "wallpaper", "vcpCode", "vcpValue",
            "controls"];
        foreach (var pair in args) if (!supported.Contains(pair.Key)) throw new ArgumentException("Unknown display option: " + pair.Key);
        var steps = new List<Step>();
        var targets = Resolve(Text(args, "monitor"), false);
        if (targets.Count == 0) throw new ArgumentException("No monitors are connected.");
        if (args.ContainsKey("primary") && targets.Count != 1) throw new ArgumentException("Choose exactly one primary monitor.");
        if (args.ContainsKey("x") != args.ContainsKey("y")) throw new ArgumentException("Specify both --x and --y.");
        if (args.ContainsKey("vcpCode") != args.ContainsKey("vcpValue")) throw new ArgumentException("Specify both --vcp-code and --vcp-value.");
        foreach (DisplayInfo initial in targets)
        {
            string token = initial.Token;
            DisplayInfo Live() => Resolve(token, false).Single();
            void Add(int order, string name, Func<bool> run) => steps.Add(new(order, name, token, run));
            if (args.ContainsKey("resolution") || args.ContainsKey("refresh"))
            {
                uint width = (uint)initial.Bounds.Width, height = (uint)initial.Bounds.Height;
                if (Text(args, "resolution") is { } resolution)
                {
                    string[] parts = resolution.ToLowerInvariant().Split('x');
                    if (parts.Length != 2 || !uint.TryParse(parts[0], out width) || !uint.TryParse(parts[1], out height))
                        throw new ArgumentException("Resolution must be WIDTHxHEIGHT.");
                }
                uint hz = args.ContainsKey("refresh") ? (uint)Integer(args, "refresh", 1, 1000) : initial.RefreshHz;
                var modes = DisplayModes.Available(initial.GdiName);
                DisplayMode? chosen = modes.Where(m => m.Width == width && m.Height == height && m.RefreshHz == hz)
                    .Select(m => (DisplayMode?)m).FirstOrDefault();
                if (chosen is null) throw new ArgumentException($"{initial.Label} does not offer {width}x{height} at {hz} Hz. Run display modes.");
                Add(20, "mode", () => DisplayModes.Apply(Live().GdiName, chosen.Value) is ModeChangeResult.Applied or ModeChangeResult.AppliedSeamlessly);
            }
            if (args.ContainsKey("orientation"))
            {
                int degrees = Integer(args, "orientation", 0, 270);
                if (degrees % 90 != 0) throw new ArgumentException("Orientation must be 0, 90, 180 or 270.");
                Add(25, "orientation", () => DisplayArrangement.SetOrientation(Live(), (ScreenOrientation)(degrees / 90)));
            }
            if (args.ContainsKey("primary"))
            {
                if (!Flag(args, "primary")) throw new ArgumentException("Set --primary true on the monitor that should become primary.");
                Add(30, "primary", () => DisplayArrangement.SetPrimary(Live(), Resolve(null)));
            }
            if (args.ContainsKey("x"))
            {
                int x = Integer(args, "x", -100000, 100000), y = Integer(args, "y", -100000, 100000);
                Add(35, "position", () =>
                {
                    var all = Resolve(null);
                    var positions = all.ToDictionary(d => d.Token, d => (d.Bounds.Left, d.Bounds.Top));
                    positions[token] = (x, y);
                    return DisplayArrangement.SetPositions(positions, all);
                });
            }
            if (args.ContainsKey("scale"))
            {
                int scale = Integer(args, "scale", 100, 500);
                var info = AdvancedDisplay.ReadScaling(initial);
                if (!info.Supported || !info.Available.Contains(scale)) throw new ArgumentException("This scale is not offered on " + initial.Label);
                Add(40, "scaling", () => AdvancedDisplay.WriteScaling(Live(), scale));
            }
            if (args.ContainsKey("hdr"))
            {
                bool on = Flag(args, "hdr");
                if (!AdvancedDisplay.ReadHdr(initial).Supported) throw new ArgumentException("HDR is not supported on " + initial.Label);
                Add(50, "HDR", () => AdvancedDisplay.WriteHdr(Live(), on));
            }
            if (args.ContainsKey("brightness"))
            {
                int level = Integer(args, "brightness", 0, 100);
                BrightnessRange planned = Brightness.Read(initial);
                if (!planned.Supported) throw new ArgumentException("Brightness is not supported on " + initial.Label);
                Add(60, "brightness", () =>
                {
                    var d = Live();
                    // The range read while planning: only its ends are used, and they
                    // belong to the monitor. Reading it again cost a DDC/CI round trip,
                    // ~55 ms of the ~250 ms a brightness set took (perfcheck writes).
                    if (!Brightness.Write(d, planned.FromPercent(level))) return false;
                    var observed = Brightness.Read(d);
                    return observed.Supported && Math.Abs(observed.Percent - level) <= 2;
                });
            }
            if (Text(args, "wallpaper") is { } image)
            {
                string path = Path.GetFullPath(image);
                if (!File.Exists(path)) throw new ArgumentException("Wallpaper file does not exist: " + path);
                Add(60, "wallpaper", () => Wallpaper.Write(Live(), path));
            }
            if (Text(args, "controls") is { } list)
                foreach ((string name, byte code, uint value) in PlanControls(initial, list))
                    Add(code == 0xD6 ? 100 : code == 0x60 ? 90 : 55, name, () => MonitorCapabilities.Write(Live(), code, value));
            Dictionary<string, byte> controls = new() { ["contrast"] = 0x12, ["volume"] = 0x62, ["sharpness"] = 0x87,
                ["redGain"] = 0x16, ["greenGain"] = 0x18, ["blueGain"] = 0x1A, ["colorPreset"] = 0x14, ["input"] = 0x60, ["power"] = 0xD6 };
            if (args.ContainsKey("vcpCode")) controls["vcpValue"] = (byte)Integer(args, "vcpCode", 0, 255);
            var requestedControls = controls.Where(pair => args.ContainsKey(pair.Key)).ToList();
            if (requestedControls.Count > 0)
            {
                var capabilities = MonitorCapabilities.Read(initial);
                foreach (var pair in requestedControls)
                {
                    uint value = (uint)Integer(args, pair.Key, 0, 65535);
                    var control = capabilities.Controls.FirstOrDefault(c => c.Code == pair.Value);
                    if (control is null || !control.Settable) throw new ArgumentException($"{initial.Label} does not expose writable {pair.Key}.");
                    if (control.Values.Count > 0 && !control.Values.Any(v => v.Value == value)
                        || control.Values.Count == 0 && control.Maximum >= 0 && value > control.Maximum)
                        throw new ArgumentException($"Value {value} is not supported for {pair.Key}.");
                    byte code = pair.Value;
                    Add(code == 0xD6 ? 100 : code == 0x60 ? 90 : 55, pair.Key, () => MonitorCapabilities.Write(Live(), code, value));
                }
            }
        }
        if (steps.Count == 0) throw new ArgumentException("No display changes requested.");
        return steps;
    }

    private static JsonObject RunSteps(List<Step> steps, bool dryRun)
    {
        var results = new JsonArray();
        bool failed = false;
        foreach (var step in OperationSequence.Execute(steps.Select(s => new PlannedOperation(s.Order, s.Name, s.Monitor, s.Run)), dryRun))
        {
            failed |= step.State is "failed" or "skipped";
            results.Add((JsonNode)new JsonObject { ["step"] = step.Name, ["monitor"] = step.Monitor, ["order"] = step.Order, ["state"] = step.State, ["error"] = step.Error });
        }
        return new JsonObject { ["state"] = dryRun ? "validated" : failed ? "partial" : "applied", ["steps"] = results };
    }

    private static JsonObject Apply(JsonObject args)
    {
        JsonObject document = args["document"]?.AsObject() ?? throw new ArgumentException("Apply requires a document.");
        foreach (var pair in document) if (pair.Key is not ("version" or "topology" or "missingMonitor" or "displays" or "settings")) throw new ArgumentException("Unknown apply property: " + pair.Key);
        if ((document["version"]?.GetValue<int>() ?? 1) != 1) throw new ArgumentException("Unsupported apply version.");
        string missing = document["missingMonitor"]?.GetValue<string>() ?? "fail";
        if (missing is not ("fail" or "skip")) throw new ArgumentException("missingMonitor must be fail or skip.");
        if (document.ContainsKey("displays") && document["displays"] is not JsonArray) throw new ArgumentException("displays must be an array.");
        if (document["settings"] is JsonNode settingsDocument) SettingsDocument.Validate(settingsDocument);
        bool dryRun = Flag(args, "dryRun");
        DesktopArrangement? topology = document["topology"] is { } requested
            ? requested.GetValue<string>().ToLowerInvariant() switch
            {
                "extend" => DesktopArrangement.Extend, "duplicate" => DesktopArrangement.Duplicate,
                "internal" => DesktopArrangement.InternalOnly, "external" => DesktopArrangement.ExternalOnly,
                _ => throw new ArgumentException("Topology must be extend, duplicate, internal or external."),
            }
            : null;

        // A topology change decides which monitors exist. Planning the rest
        // against the desk as it was would validate modes on displays about to
        // vanish and reject the ones about to appear, so the topology runs on its
        // own and everything after it is planned against the rediscovered desk.
        JsonArray? earlier = null;
        if (topology is { } mode)
        {
            var phase = new List<Step>
            {
                new(0, "topology", null, () => DesktopLayout.Apply(mode)),
                new(10, "rediscover", null, Settle),
            };
            if (dryRun) earlier = RunSteps(phase, true)["steps"]!.AsArray();
            else
            {
                JsonObject first = RunSteps(phase, false);
                if (first["state"]!.GetValue<string>() != "applied") { first["missingMonitors"] = new JsonArray(); return first; }
                earlier = first["steps"]!.AsArray();
            }
        }

        try { return PlanAndRun(document, missing, dryRun, topology is not null, earlier); }
        catch (ArgumentException ex) when (earlier is not null && !dryRun)
        {
            // The desk has already changed; a bare refusal would hide that.
            earlier.Add((JsonNode)new JsonObject { ["step"] = "plan", ["monitor"] = null, ["order"] = 15, ["state"] = "failed", ["error"] = ex.Message });
            return new JsonObject { ["state"] = "partial", ["steps"] = earlier, ["missingMonitors"] = new JsonArray() };
        }
    }

    private static JsonObject PlanAndRun(JsonObject document, string missing, bool dryRun, bool topology, JsonArray? earlier)
    {
        var steps = new List<Step>();
        var absent = new JsonArray();
        var positions = new Dictionary<string, (int, int)>();
        if (document["displays"] is JsonArray displays)
            foreach (JsonNode? display in displays)
            {
                var operation = display?.AsObject() ?? throw new ArgumentException("Invalid display operation.");
                List<DisplayInfo> targets;
                try { targets = Resolve(Text(operation, "monitor")); }
                catch (ArgumentException ex) when (ex.Message.Contains("not connected", StringComparison.Ordinal)
                    && (missing == "skip" || dryRun && topology))
                {
                    // In a dry run the topology has not happened, so a monitor it
                    // would switch on cannot be told apart from one unplugged.
                    bool deferred = dryRun && topology;
                    absent.Add((JsonNode)new JsonObject { ["monitor"] = Text(operation, "monitor"), ["state"] = deferred ? "deferred" : "skipped",
                        ["error"] = deferred ? "Not active now; it is resolved after the topology change." : ex.Message });
                    continue;
                }
                // Resolve transient display numbers before any mode operation.
                if (operation.ContainsKey("primary") && targets.Count != 1) throw new ArgumentException("Choose one primary monitor.");
                foreach (var target in targets)
                {
                    var stable = (JsonObject)operation.DeepClone(); stable["monitor"] = target.Token;
                    var planned = PlanDisplay(stable);
                    steps.AddRange(planned.Where(s => s.Name != "position"));
                    if (stable.ContainsKey("x")) positions[target.Token] = (Integer(stable, "x", -100000, 100000), Integer(stable, "y", -100000, 100000));
                }
            }
        if (positions.Count > 0)
            steps.Add(new(35, "layout", null, () =>
            {
                var live = Resolve(null);
                if (positions.Keys.Any(token => live.All(d => d.Token != token))) return false;
                var all = live.ToDictionary(d => d.Token, d => (d.Bounds.Left, d.Bounds.Top));
                foreach (var position in positions) all[position.Key] = position.Value;
                return DisplayArrangement.SetPositions(all, live);
            }));
        if (document["settings"] is JsonNode settings)
            steps.Add(new(70, "settings", null, () =>
            {
                SettingsCommand("import", new JsonObject { ["document"] = settings.DeepClone() });
                return true;
            }));
        if (steps.Count == 0 && absent.Count == 0 && earlier is null) throw new ArgumentException("Apply document contains no operations.");
        var result = RunSteps(steps, dryRun);
        if (earlier is not null)
        {
            var all = result["steps"]!.AsArray();
            for (int i = earlier.Count - 1; i >= 0; i--) { var item = earlier[i]!; earlier.RemoveAt(i); all.Insert(0, item); }
        }
        result["missingMonitors"] = absent;
        if (!dryRun && absent.Count > 0) result["state"] = "partial";
        return result;
    }

    /// <summary>Waits for Windows to finish rearranging after a topology change.</summary>
    /// <remarks>
    /// <c>SetDisplayConfig</c> returns before the monitors it switched on are
    /// enumerable; reading straight away sees the old desk. Two identical
    /// signatures 150 ms apart is the settle point, bounded at five seconds.
    /// </remarks>
    private static bool Settle()
    {
        string previous = "";
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 5000)
        {
            Thread.Sleep(150);
            string now = DisplayRegistry.CheapSignature();
            if (now.Length > 0 && now == previous) break;
            previous = now;
        }
        InventoryCache.Clear();
        return DisplayRegistry.Enumerate().Count > 0;
    }

    // ================================================================ monitor controls

    /// <summary>
    /// Every control the monitor itself lists, addressable by name.
    /// </summary>
    /// <remarks>
    /// Nothing here is a table of codes. Which controls exist, what they are
    /// called and which values they take come from the monitor's own
    /// capabilities string, overlaid with the device library: a code somebody
    /// has mapped for this model, its manufacturer, or every monitor, gets that
    /// name and those values, and becomes writable only if the mapping says it
    /// was seen to be safe. Standard codes stay behind the allow list the panel
    /// uses. See docs/DEVICE-LIBRARY.md.
    /// </remarks>
    private static JsonNode ControlsCommand(JsonObject args)
    {
        foreach (var pair in args)
            if (pair.Key is not ("monitor" or "all")) throw new ArgumentException("Unknown option for display controls: " + pair.Key);
        bool all = Flag(args, "all");
        var displays = new JsonArray();
        foreach (DisplayInfo d in Resolve(Text(args, "monitor")))
        {
            var entry = new JsonObject { ["token"] = d.Token, ["name"] = d.Label, ["model"] = d.Key.Model };
            if (d.IsInternal)
            {
                entry["ddcSupported"] = false;
                entry["note"] = "A built-in panel has no DDC/CI channel; its brightness is under display set --brightness.";
                displays.Add((JsonNode)entry);
                continue;
            }
            // A mapped code outside the allow list is not in ReadSettable's
            // subset, so with any mappings the whole list is read.
            bool mapped = DeviceLibrary.Resolve(d.Key.Model).Count > 0;
            MonitorCapability capabilities = all || mapped ? MonitorCapabilities.Read(d) : MonitorCapabilities.ReadSettable(d);
            var controls = new JsonArray();
            foreach (Effective c in Effectives(d, capabilities))
            {
                if (!all && !c.Settable) continue;
                controls.Add((JsonNode)Describe(c));
            }
            entry["ddcSupported"] = capabilities.Supported;
            entry["controls"] = controls;
            displays.Add((JsonNode)entry);
        }
        return new JsonObject { ["displays"] = displays };
    }

    /// <summary>A listed control as the library names it.</summary>
    private sealed record Effective(VcpControl Control, DefinedControl? Mapping, string? Origin)
    {
        public string Name => Mapping?.Name ?? Control.Name;
        public string Key => Mapping?.EffectiveKey ?? DeviceDefinitions.KeyFor(Control.Name);
        public string Kind => Mapping?.Kind ?? Control.Kind switch
        {
            VcpKind.Continuous => DefinedKinds.Range,
            VcpKind.Discrete => DefinedKinds.Choice,
            _ => DefinedKinds.Information,
        };

        /// <summary>Standard codes by the allow list; mapped ones only when the mapping says so.</summary>
        public bool Settable => Control.Settable
            || Mapping is { Writable: true } m && m.Kind != DefinedKinds.Information;

        /// <summary>The values offered: the mapping's names, within what the monitor itself lists.</summary>
        /// <remarks>
        /// A definition is shared across a model, a brand or every monitor, so it
        /// can name values this unit does not list. Those are left out: nothing is
        /// written that the monitor did not say it takes. Values the monitor lists
        /// that nobody has named are kept, by number, rather than hidden. Only
        /// when the monitor lists no values at all - a code it answers but does
        /// not enumerate - are the mapping's own, which someone wrote and watched,
        /// the whole list.
        /// </remarks>
        public IReadOnlyList<(uint Value, string Name)> Values
        {
            get
            {
                var listed = Control.Values.Select(v => ((uint)v.Value, v.Name)).ToList();
                if (Mapping is not { Values.Count: > 0 } m) return listed;
                var named = m.Values.Where(v => v.Number is not null).Select(v => (v.Number!.Value, v.Name)).ToList();
                if (listed.Count == 0) return named;
                var result = named.Where(n => listed.Any(l => (l.Item1 & 0xFF) == (n.Item1 & 0xFF))).ToList();
                result.AddRange(listed.Where(l => !named.Any(n => (n.Item1 & 0xFF) == (l.Item1 & 0xFF)))
                    .Select(l => (l.Item1, $"Value 0x{l.Item1 & 0xFF:X2}")));
                return result;
            }
        }

        /// <summary>The mapping's maximum, never above what the monitor itself reports.</summary>
        public int Maximum => Mapping?.Maximum is int mapped && Control.Maximum >= 0 ? Math.Min(mapped, Control.Maximum)
            : Mapping?.Maximum ?? Control.Maximum;

        /// <summary>Null when the monitor did not answer the read: DDC/CI drops the odd reply.</summary>
        public int? Current => Control.Current < 0 ? null : Kind == DefinedKinds.Choice ? Control.CurrentValue : Control.Current;

        /// <summary>The current value's name; a choice at a value nobody has named says so rather than nothing.</summary>
        public string? CurrentName => Current is not int now ? null
            : Values.FirstOrDefault(v => (v.Value & 0xFF) == (uint)(now & 0xFF)).Name
              ?? (Kind == DefinedKinds.Choice ? $"unnamed value 0x{now & 0xFF:X2}" : null);
    }

    private static List<Effective> Effectives(DisplayInfo display, MonitorCapability capabilities)
    {
        Dictionary<byte, ResolvedControl> library = DeviceLibrary.Resolve(display.Key.Model);
        return capabilities.Controls.Select(c => library.TryGetValue(c.Code, out ResolvedControl? r)
            ? new Effective(c, r.Definition, r.Origin) : new Effective(c, null, null)).ToList();
    }

    private static JsonObject Describe(Effective c)
    {
        var values = new JsonArray();
        foreach (var (value, name) in c.Values)
            values.Add((JsonNode)new JsonObject { ["key"] = DeviceDefinitions.KeyFor(name), ["value"] = value, ["name"] = name });
        var entry = new JsonObject
        {
            ["key"] = c.Key, ["code"] = c.Control.Hex, ["name"] = c.Name, ["kind"] = c.Kind,
            ["settable"] = c.Settable,
            ["current"] = c.Current,
            ["currentName"] = c.CurrentName,
            ["maximum"] = c.Kind == DefinedKinds.Range && c.Maximum >= 0 ? c.Maximum : null,
            ["values"] = values,
        };
        if (c.Mapping is not null)
        {
            entry["mapped"] = c.Origin;
            entry["confidence"] = c.Mapping.Confidence;
            if (c.Mapping.Notes is { } notes) entry["notes"] = notes;
        }
        return entry;
    }

    /// <summary>Reads one control, or sets it when a value is given.</summary>
    private static JsonNode ControlCommand(JsonObject args)
    {
        foreach (var pair in args)
            if (pair.Key is not ("monitor" or "name" or "value" or "dryRun")) throw new ArgumentException("Unknown option for display control: " + pair.Key);
        string selector = Text(args, "name") ?? throw new ArgumentException("Name the control: --name contrast, or its code: --name 0x12. List them with display controls.");
        List<DisplayInfo> targets = Resolve(Text(args, "monitor"), false);
        if (targets.Count == 0) throw new ArgumentException("No monitors are connected.");

        string? wanted = args["value"] is JsonNode node ? node.ToString() : null;
        bool dryRun = Flag(args, "dryRun");
        var results = new JsonArray();
        foreach (DisplayInfo d in targets)
        {
            if (d.IsInternal) throw new ArgumentException($"{d.Label} is a built-in panel and has no DDC/CI controls.");
            MonitorCapability capabilities = MonitorCapabilities.Read(d);
            if (!capabilities.Supported) throw new ArgumentException($"{d.Label} does not answer DDC/CI.");
            Effective control = FindControl(Effectives(d, capabilities), selector, d.Label);
            var entry = Describe(control);
            entry["monitor"] = d.Token;
            if (wanted is not null)
            {
                if (!control.Settable)
                    throw new ArgumentException(control.Mapping is null
                        ? $"{control.Name} on {d.Label} is read-only here: not a standard control DispCtrl writes, and nobody has mapped it as safe. See devices map."
                        : $"{control.Name} on {d.Label} is mapped but not as writable.");
                uint value = FindValue(control, wanted);
                entry["requested"] = value;
                if (dryRun) entry["state"] = "validated";
                else
                {
                    if (!MonitorCapabilities.Write(d, control.Control.Code, value)) throw new InvalidOperationException($"{d.Label} refused {control.Name} = {wanted}.");
                    entry["state"] = "applied";
                }
            }
            results.Add((JsonNode)entry);
        }
        return new JsonObject { ["controls"] = results };
    }

    /// <summary>A control by its key, its name, or its code.</summary>
    private static Effective FindControl(List<Effective> controls, string selector, string label)
    {
        string wanted = selector.Trim();
        byte? code = ParseCode(wanted);
        Effective? match = controls.FirstOrDefault(c =>
            code is { } k ? c.Control.Code == k
            : c.Key == DeviceDefinitions.KeyFor(wanted) || c.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)
              || DeviceDefinitions.KeyFor(c.Control.Name) == DeviceDefinitions.KeyFor(wanted));
        if (match is not null) return match;
        string known = string.Join(", ", controls.Where(c => c.Settable).Select(c => c.Key));
        throw new ArgumentException($"{label} has no control called '{selector}'. It offers: {known}.");
    }

    /// <summary>A value by its listed name or key, or a number the control accepts.</summary>
    private static uint FindValue(Effective control, string text)
    {
        string wanted = text.Trim();
        IReadOnlyList<(uint Value, string Name)> values = control.Values;
        if (values.Count > 0)
        {
            foreach (var (value, name) in values)
                if (DeviceDefinitions.KeyFor(name) == DeviceDefinitions.KeyFor(wanted) || name.Equals(wanted, StringComparison.OrdinalIgnoreCase)) return value;
            if (DeviceDefinitions.ParseNumber(wanted) is { } listed && values.Any(v => v.Value == listed)) return listed;
            throw new ArgumentException($"{control.Name} takes one of: {string.Join(", ", values.Select(v => DeviceDefinitions.KeyFor(v.Name)))}.");
        }
        if (control.Kind == DefinedKinds.Action) return DeviceDefinitions.ParseNumber(wanted) ?? 1;
        if (DeviceDefinitions.ParseNumber(wanted) is not { } number) throw new ArgumentException($"{control.Name} takes a number.");
        if (control.Maximum >= 0 && number > control.Maximum)
            throw new ArgumentException($"{control.Name} goes up to {control.Maximum}.");
        return number;
    }

    private static byte? ParseCode(string text)
    {
        string t = text.Trim();
        if (DeviceDefinitions.ParseCode(t) is { } hex) return hex;
        if (t.EndsWith('h') && t.Length <= 3 && byte.TryParse(t[..^1], System.Globalization.NumberStyles.HexNumber, null, out byte suffixed)) return suffixed;
        return null;
    }

    /// <summary>
    /// Parses <c>--controls "contrast=70,picture-mode=games"</c> into steps
    /// resolved against what the monitor lists.
    /// </summary>
    private static IEnumerable<(string Name, byte Code, uint Value)> PlanControls(DisplayInfo display, string list)
    {
        MonitorCapability capabilities = MonitorCapabilities.Read(display);
        if (!capabilities.Supported) throw new ArgumentException($"{display.Label} does not answer DDC/CI.");
        List<Effective> controls = Effectives(display, capabilities);
        foreach (string part in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equals = part.IndexOf('=');
            if (equals <= 0) throw new ArgumentException("Controls are name=value pairs, separated by commas.");
            Effective control = FindControl(controls, part[..equals], display.Label);
            if (!control.Settable) throw new ArgumentException($"{control.Name} on {display.Label} is read-only here.");
            yield return (control.Key, control.Control.Code, FindValue(control, part[(equals + 1)..]));
        }
    }
}
