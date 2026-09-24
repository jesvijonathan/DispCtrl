using System.Text.Json;
using System.Text.Json.Nodes;
using DispCtrl.Core.Devices;
using DispCtrl.Core.Displays;
using DispCtrl.Display;
using DispCtrl.Display.Devices;

namespace DispCtrl.Control;

/// <summary>
/// The device library from the command line: what has been seen, what each code
/// is, naming the ones nobody has named, and sharing the result.
/// </summary>
/// <remarks>
/// The CLI is the first client of the library, as it is for everything else;
/// the app's Devices page calls these same operations. See docs/DEVICE-LIBRARY.md.
/// </remarks>
public sealed partial class ControlService
{
    private static JsonNode DevicesCommand(string action, JsonObject args) => action switch
    {
        "list" => DevicesList(args),
        "show" => DevicesShow(args),
        "scan" => DevicesScan(args),
        "forget" => DevicesForget(args),
        "map" => DevicesMap(args),
        "unmap" => DevicesUnmap(args),
        "link" => DevicesLink(args),
        "panel" => DevicesPanel(args),
        "definitions" => DevicesDefinitions(args),
        // "share" was the first name; scripts that use it keep working.
        "contribute" or "share" => DevicesShare(args),
        "validate" => DevicesValidate(args),
        _ => throw new ArgumentException("devices actions: list, show, scan, forget, map, unmap, link, panel, definitions, contribute, validate. probe runs in the terminal."),
    };

    private static void Only(JsonObject args, string command, params string[] allowed)
    {
        foreach (var pair in args)
            if (!allowed.Contains(pair.Key)) throw new ArgumentException($"Unknown option for devices {command}: {pair.Key}");
    }

    /// <summary>The model a request is about: an attached monitor's, or one named outright.</summary>
    private static (string Model, DisplayInfo? Display) ModelOf(JsonObject args)
    {
        if (Text(args, "model") is { } model)
        {
            model = model.ToUpperInvariant();
            if (!DeviceDefinitions.IsModel(model)) throw new ArgumentException($"'{model}' is not a model key such as DEL-A234.");
            DisplayInfo? attached = DisplayRegistry.Enumerate().FirstOrDefault(d => d.Key.Model.Equals(model, StringComparison.OrdinalIgnoreCase));
            return (model, attached);
        }
        List<DisplayInfo> targets = Resolve(Text(args, "monitor") ?? throw new ArgumentException("Choose --monitor or --model."), false);
        if (targets.Count != 1) throw new ArgumentException("Choose one monitor.");
        return (targets[0].Key.Model, targets[0]);
    }

    private static JsonNode DevicesList(JsonObject args)
    {
        Only(args, "list");
        // Opening the page also records arrivals when the resident engine is
        // stopped; enumeration itself does not touch a DDC/CI control.
        DeviceObserver.Attached(DisplayRegistry.Enumerate());
        DeviceHistory history = DeviceHistory.Load();
        var attached = DisplayRegistry.Enumerate().Select(d => d.Key.Model).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var models = new JsonArray();
        foreach (SeenModel m in history.Models.Values.OrderByDescending(m => m.LastSeen))
        {
            Dictionary<byte, ResolvedControl> known = DeviceLibrary.Resolve(m.Key);
            int unnamed = m.Codes.Keys.Count(code => DeviceDefinitions.ParseCode(code) is byte b && !MonitorCapabilities.IsNamed(b) && !known.ContainsKey(b));
            models.Add((JsonNode)new JsonObject
            {
                ["model"] = m.Key, ["name"] = m.Name, ["connector"] = m.Connector, ["builtIn"] = m.BuiltIn,
                ["attached"] = attached.Contains(m.Key), ["firstSeen"] = m.FirstSeen.ToString("O"), ["lastSeen"] = m.LastSeen.ToString("O"),
                ["codes"] = m.Codes.Count, ["mapped"] = known.Count, ["unnamed"] = unnamed,
                ["capabilitiesRead"] = m.Capabilities is not null,
            });
        }
        return new JsonObject { ["history"] = DeviceHistory.PathOnDisk, ["models"] = models };
    }

    /// <summary>Every code, what the standard or a definition calls it, and whether anyone has.</summary>
    private static JsonNode DevicesShow(JsonObject args)
    {
        Only(args, "show", "monitor", "model", "history");
        var (model, display) = ModelOf(args);
        Dictionary<byte, ResolvedControl> known = DeviceLibrary.Resolve(model);
        var codes = new JsonArray();
        string source;

        // --history answers from what has been recorded, in milliseconds: what
        // the Devices page shows first, before anybody asks for a live read.
        if (display is not null && !display.IsInternal && !Flag(args, "history"))
        {
            MonitorCapability capabilities = MonitorCapabilities.Read(display);
            if (!capabilities.Supported) throw new InvalidOperationException($"{display.Label} does not answer DDC/CI.");
            source = "live";
            foreach (VcpControl c in capabilities.Controls)
            {
                known.TryGetValue(c.Code, out ResolvedControl? mapped);
                codes.Add((JsonNode)CodeEntry(c.Code, c.Name, c.Kind.ToString().ToLowerInvariant(),
                    c.Values.Select(v => (int)v.Value), c.Current < 0 ? null : c.Kind == VcpKind.Discrete ? c.CurrentValue : c.Current, mapped));
            }
        }
        else
        {
            DeviceHistory history = DeviceHistory.Load();
            if (!history.Models.TryGetValue(model, out SeenModel? seen))
                throw new ArgumentException($"{model} has not been seen here. Attach it, or run devices scan with it attached.");
            source = "history";
            foreach (var (code, c) in seen.Codes)
            {
                byte b = DeviceDefinitions.ParseCode(code) ?? 0;
                known.TryGetValue(b, out ResolvedControl? mapped);
                codes.Add((JsonNode)CodeEntry(b, c.Name, c.Kind.ToLowerInvariant(), c.ListedValues, null, mapped, c.Observed));
            }
        }

        return new JsonObject { ["model"] = model, ["source"] = source, ["panel"] = DeviceLibrary.Panel(model)?.Technology, ["codes"] = codes };
    }

    private static JsonObject CodeEntry(byte code, string name, string kind, IEnumerable<int> listed, int? current,
        ResolvedControl? mapped, IEnumerable<int>? observed = null)
    {
        // standard: DispCtrl writes it. named: the MCCS standard says what it
        // is, read-only here. unnamed: nobody has said - what mapping is for.
        string status = mapped is not null ? "mapped" : VcpControl.IsAllowed(code) ? "standard"
            : MonitorCapabilities.IsNamed(code) ? "named" : "unnamed";
        var entry = new JsonObject
        {
            ["code"] = DeviceDefinitions.FormatCode(code),
            ["status"] = status,
            ["reported"] = name,
            ["kind"] = kind,
            ["listed"] = new JsonArray(listed.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()),
            ["current"] = current,
        };
        if (observed is not null) entry["observed"] = new JsonArray(observed.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
        if (mapped is not null)
        {
            entry["name"] = mapped.Definition.Name;
            entry["key"] = mapped.Definition.EffectiveKey;
            entry["mappedKind"] = mapped.Definition.Kind;
            entry["writable"] = mapped.Definition.Writable;
            entry["confidence"] = mapped.Definition.Confidence;
            entry["origin"] = mapped.Origin;
            entry["maximum"] = mapped.Definition.Maximum;
            entry["notes"] = mapped.Definition.Notes;
            entry["values"] = new JsonArray(mapped.Definition.Values
                .Select(v => (JsonNode)new JsonObject { ["value"] = v.Value, ["name"] = v.Name }).ToArray());
        }
        return entry;
    }

    private static JsonNode DevicesScan(JsonObject args)
    {
        Only(args, "scan");
        var scanned = new JsonArray();
        List<DisplayInfo> displays = DisplayRegistry.Enumerate();
        // An explicit sync brings back a monitor the person removed earlier;
        // only the automatic learning respects the removal.
        DeviceHistoryEdits.Remember(displays.Select(d => d.Key.Model));
        DeviceObserver.Attached(displays);
        foreach (DisplayInfo d in displays)
        {
            if (d.IsInternal)
            {
                DeviceDiscovery.CacheRecord(d);
                scanned.Add((JsonNode)new JsonObject { ["model"] = d.Key.Model, ["ddc"] = false });
                continue;
            }
            MonitorCapability c = MonitorCapabilities.Read(d);
            DeviceDiscovery.CacheRecord(d);
            scanned.Add((JsonNode)new JsonObject { ["model"] = d.Key.Model, ["ddc"] = c.Supported, ["codes"] = c.Controls.Count });
        }
        return new JsonObject { ["scanned"] = scanned, ["history"] = DeviceHistory.PathOnDisk };
    }

    /// <summary>Removes a model from this PC's list; it is not recorded again by itself until a scan.</summary>
    private static JsonNode DevicesForget(JsonObject args)
    {
        Only(args, "forget", "monitor", "model");
        var (model, _) = ModelOf(args);
        bool removed = DeviceHistoryEdits.Forget(model);
        return new JsonObject
        {
            ["model"] = model, ["removed"] = removed,
            ["note"] = "Kept off the list until devices scan runs with it attached. Local mappings are kept; devices unmap removes those.",
        };
    }

    /// <summary>Names one code for a model, its manufacturer, or every monitor.</summary>
    private static JsonNode DevicesMap(JsonObject args)
    {
        Only(args, "map", "monitor", "model", "code", "name", "key", "kind", "values", "maximum", "writable", "scope",
            "confidence", "notes", "dryRun");
        var (model, _) = ModelOf(args);
        byte code = DeviceDefinitions.ParseCode(Text(args, "code") ?? "")
            ?? throw new ArgumentException("--code is the VCP code, written 0xE2.");
        string scope = Text(args, "scope") ?? "model";
        string target = scope switch
        {
            "model" => model,
            "brand" => DeviceDefinitions.Brand(model),
            "all" => "*",
            _ => throw new ArgumentException("--scope is model, brand or all."),
        };

        var control = new DefinedControl
        {
            Code = DeviceDefinitions.FormatCode(code),
            Key = Text(args, "key"),
            Name = Text(args, "name") ?? throw new ArgumentException("--name says what the code does, in words."),
            Kind = Text(args, "kind") ?? DefinedKinds.Information,
            Writable = Flag(args, "writable"),
            Maximum = args.ContainsKey("maximum") ? Integer(args, "maximum", 0, 65535) : null,
            Confidence = Text(args, "confidence") ?? DefinedConfidence.Observed,
            Notes = Text(args, "notes"),
            Sources = [model],
        };
        foreach (string pair in (Text(args, "values") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equals = pair.IndexOf('=');
            if (equals <= 0) throw new ArgumentException("--values is value=name pairs: \"0x0B=ComfortView,0x00=Standard\".");
            control.Values.Add(new DefinedValue { Value = pair[..equals].Trim(), Name = pair[(equals + 1)..].Trim() });
        }
        if (control.Values.Count > 0 && Text(args, "kind") is null) control.Kind = DefinedKinds.Choice;

        // Keep what was there: another model's source, earlier notes.
        DefinedControl? before = DeviceLibrary.LoadLocal(target).Controls.FirstOrDefault(c => c.CodeValue == code);
        if (before is not null)
        {
            foreach (string s in before.Sources) if (!control.Sources.Contains(s)) control.Sources.Add(s);
            control.Notes ??= before.Notes;
        }

        var probe = new DeviceDefinition { Target = target, Controls = [control] };
        List<string> problems = DeviceDefinitions.Validate(probe);
        if (problems.Count > 0) throw new ArgumentException(string.Join("; ", problems));
        if (Flag(args, "dryRun")) return new JsonObject { ["state"] = "validated", ["target"] = target };

        string path = DeviceLibrary.Map(target, control, target == model ? DisplayRegistry.Enumerate()
            .FirstOrDefault(d => d.Key.Model == model)?.Label : null);
        return new JsonObject { ["state"] = "saved", ["target"] = target, ["code"] = control.Code, ["path"] = path };
    }

    private static JsonNode DevicesUnmap(JsonObject args)
    {
        Only(args, "unmap", "monitor", "model", "code", "scope", "dryRun");
        var (model, _) = ModelOf(args);
        byte code = DeviceDefinitions.ParseCode(Text(args, "code") ?? "") ?? throw new ArgumentException("--code is written 0xE2.");
        string target = (Text(args, "scope") ?? "model") switch
        {
            "model" => model, "brand" => DeviceDefinitions.Brand(model), "all" => "*",
            _ => throw new ArgumentException("--scope is model, brand or all."),
        };
        if (Flag(args, "dryRun")) return new JsonObject { ["state"] = "validated", ["target"] = target };
        bool removed = DeviceLibrary.Unmap(target, code);
        return new JsonObject { ["state"] = removed ? "removed" : "absent", ["target"] = target };
    }

    private static JsonNode DevicesLink(JsonObject args)
    {
        Only(args, "link", "monitor", "model", "to", "remove", "dryRun");
        var (model, _) = ModelOf(args);
        string to = (Text(args, "to") ?? throw new ArgumentException("--to is a model (DEL-A233) or a manufacturer (DEL).")).ToUpperInvariant();
        var probe = new DeviceDefinition { Target = model, Extends = [to] };
        List<string> problems = DeviceDefinitions.Validate(probe);
        if (problems.Count > 0) throw new ArgumentException(string.Join("; ", problems));
        if (Flag(args, "dryRun")) return new JsonObject { ["state"] = "validated" };
        string path = DeviceLibrary.Link(model, to, Flag(args, "remove"));
        return new JsonObject { ["state"] = "saved", ["model"] = model, ["extends"] = to, ["path"] = path };
    }

    /// <summary>Says what a model's panel is, which nothing on the machine may report.</summary>
    /// <remarks>
    /// The way to describe a built-in panel: it has no DDC/CI channel, so it has
    /// no codes to map, but whether it is OLED is what burn-in protection keys
    /// off, and every owner of the same laptop gets the answer once it is shared.
    /// </remarks>
    private static JsonNode DevicesPanel(JsonObject args)
    {
        Only(args, "panel", "monitor", "model", "technology", "notes", "dryRun");
        var (model, _) = ModelOf(args);
        string technology = Text(args, "technology")
            ?? throw new ArgumentException("--technology is what the panel is: LCD, OLED, QD-OLED, Mini-LED, or none to clear it.");
        DeviceDefinition d = DeviceLibrary.LoadLocal(model);
        d.Panel = technology.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? null
            : new DefinedPanel { Technology = technology.Trim(), Notes = Text(args, "notes") ?? d.Panel?.Notes };
        d.Name ??= DisplayRegistry.Enumerate().FirstOrDefault(x => x.Key.Model == model)?.Label;
        List<string> problems = DeviceDefinitions.Validate(d);
        if (problems.Count > 0) throw new ArgumentException(string.Join("; ", problems));
        if (Flag(args, "dryRun")) return new JsonObject { ["state"] = "validated", ["model"] = model };
        string path = DeviceLibrary.SaveLocal(d);
        return new JsonObject { ["state"] = "saved", ["model"] = model, ["panel"] = d.Panel?.Technology, ["path"] = path };
    }

    private static JsonNode DevicesDefinitions(JsonObject args)
    {
        Only(args, "definitions", "monitor", "model");
        if (args.ContainsKey("monitor") || args.ContainsKey("model"))
        {
            var (model, _) = ModelOf(args);
            var resolved = new JsonArray();
            foreach (ResolvedControl r in DeviceLibrary.Resolve(model).Values.OrderBy(r => r.Code))
            {
                JsonNode wrapped = JsonNode.Parse(JsonSerializer.Serialize(
                    new DeviceDefinition { Target = model, Controls = [r.Definition] }, DeviceJsonContext.Default.DeviceDefinition))!;
                resolved.Add((JsonNode)new JsonObject { ["origin"] = r.Origin, ["control"] = wrapped["controls"]![0]!.DeepClone() });
            }
            return new JsonObject { ["model"] = model, ["resolved"] = resolved };
        }

        var problems = new List<string>();
        var localFound = new JsonArray();
        foreach (DeviceDefinition d in DeviceLibrary.LoadFolder(DeviceLibrary.UserFolder, problems).Values)
            localFound.Add((JsonNode)new JsonObject { ["target"] = d.Target, ["name"] = d.Name, ["controls"] = d.Controls.Count,
                ["panel"] = d.Panel?.Technology,
                ["extends"] = new JsonArray(d.Extends.Select(e => (JsonNode?)JsonValue.Create(e)).ToArray()) });
        foreach (string p in problems) localFound.Add((JsonNode)new JsonObject { ["problem"] = p });

        // The shipped library is meant to hold thousands of models: counted by
        // brand here, and shown one model at a time with --model.
        var brands = new JsonObject();
        int models = 0;
        foreach (var group in DeviceLayout.Definitions(DeviceLibrary.ShippedFolder).GroupBy(d => d.Target == "*" ? "*" : d.Target[..3]))
        {
            int count = group.Count(d => DeviceDefinitions.IsModel(d.Target));
            models += count;
            brands[group.Key] = new JsonObject { ["models"] = count, ["brandFile"] = group.Any(d => d.Target == group.Key) };
        }
        return new JsonObject
        {
            ["shipped"] = new JsonObject { ["folder"] = DeviceLibrary.ShippedFolder, ["models"] = models, ["brands"] = brands,
                ["note"] = "devices definitions --model DEL-A234 shows one model with every layer that applies." },
            ["local"] = new JsonObject { ["folder"] = DeviceLibrary.UserFolder, ["definitions"] = localFound },
        };
    }

    private static JsonNode DevicesShare(JsonObject args)
    {
        Only(args, "share", "monitor", "model", "all", "open");
        bool all = Flag(args, "all");
        if (all && (args.ContainsKey("model") || args.ContainsKey("monitor")))
            throw new ArgumentException("Choose --all or a single --model/--monitor.");
        MappingShare share;
        if (all) share = DeviceContribution.PrepareMappings(DisplayRegistry.Enumerate());
        else
        {
            var (model, display) = ModelOf(args);
            share = DeviceContribution.PrepareMapping(model, display);
        }
        bool open = Flag(args, "open");
        // What did not fit in the link goes to the clipboard before the browser
        // opens, as the app does. clip.exe, because a console process has no
        // clipboard API of its own; the text is ASCII, so no code page matters.
        bool copied = open && share.Paste is not null && DeviceContribution.CopyToClipboard(share.Paste);
        bool opened = open && (share.Paste is null || copied) && DeviceContribution.Open(share);
        return new JsonObject
        {
            ["model"] = share.Model, ["path"] = share.Path, ["url"] = share.Url.ToString(), ["prefilled"] = share.Prefilled,
            ["opened"] = opened, ["copied"] = copied,
            ["note"] = share.Paste is null ? null
                : copied ? "Paste the copied text where the issue asks for it."
                : $"The share needs pasted text; copy it from the paste field or {share.Path}.",
            ["paste"] = share.Paste,
            ["body"] = share.Body,
        };
    }

    private static JsonNode DevicesValidate(JsonObject args)
    {
        Only(args, "validate", "document");
        JsonNode document = args["document"] ?? throw new ArgumentException("devices validate FILE");
        DeviceDefinition definition = JsonSerializer.Deserialize(document, DeviceJsonContext.Default.DeviceDefinition)
            ?? throw new ArgumentException("The file holds no definition.");
        List<string> problems = DeviceDefinitions.Validate(definition);
        return new JsonObject
        {
            ["target"] = definition.Target, ["valid"] = problems.Count == 0,
            ["problems"] = new JsonArray(problems.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()),
        };
    }
}
