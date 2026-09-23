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
        "map" => DevicesMap(args),
        "unmap" => DevicesUnmap(args),
        "link" => DevicesLink(args),
        "definitions" => DevicesDefinitions(args),
        "share" => DevicesShare(args),
        "validate" => DevicesValidate(args),
        _ => throw new ArgumentException("devices actions: list, show, scan, map, unmap, link, definitions, share, validate. probe runs in the terminal."),
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
        Only(args, "show", "monitor", "model");
        var (model, display) = ModelOf(args);
        Dictionary<byte, ResolvedControl> known = DeviceLibrary.Resolve(model);
        var codes = new JsonArray();
        string source;

        if (display is not null && !display.IsInternal)
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

        return new JsonObject { ["model"] = model, ["source"] = source, ["codes"] = codes };
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
        DeviceObserver.Attached(displays);
        foreach (DisplayInfo d in displays)
        {
            if (d.IsInternal) { scanned.Add((JsonNode)new JsonObject { ["model"] = d.Key.Model, ["ddc"] = false }); continue; }
            MonitorCapability c = MonitorCapabilities.Read(d);
            scanned.Add((JsonNode)new JsonObject { ["model"] = d.Key.Model, ["ddc"] = c.Supported, ["codes"] = c.Controls.Count });
        }
        return new JsonObject { ["scanned"] = scanned, ["history"] = DeviceHistory.PathOnDisk };
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

        JsonArray Folder(string path)
        {
            var problems = new List<string>();
            var found = new JsonArray();
            foreach (DeviceDefinition d in DeviceLibrary.LoadFolder(path, problems).Values)
                found.Add((JsonNode)new JsonObject { ["target"] = d.Target, ["name"] = d.Name, ["controls"] = d.Controls.Count,
                    ["extends"] = new JsonArray(d.Extends.Select(e => (JsonNode?)JsonValue.Create(e)).ToArray()) });
            foreach (string p in problems) found.Add((JsonNode)new JsonObject { ["problem"] = p });
            return found;
        }
        return new JsonObject
        {
            ["shipped"] = new JsonObject { ["folder"] = DeviceLibrary.ShippedFolder, ["definitions"] = Folder(DeviceLibrary.ShippedFolder) },
            ["local"] = new JsonObject { ["folder"] = DeviceLibrary.UserFolder, ["definitions"] = Folder(DeviceLibrary.UserFolder) },
        };
    }

    private static JsonNode DevicesShare(JsonObject args)
    {
        Only(args, "share", "monitor", "model", "open");
        var (model, display) = ModelOf(args);
        MappingShare share = DeviceContribution.PrepareMapping(model, display);
        bool opened = Flag(args, "open") && DeviceContribution.Open(share);
        return new JsonObject
        {
            ["model"] = model, ["path"] = share.Path, ["url"] = share.Url.ToString(), ["prefilled"] = share.Prefilled,
            ["opened"] = opened,
            ["note"] = share.Prefilled ? null : "Too long for a link: the form opens empty; paste the saved file into it.",
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
