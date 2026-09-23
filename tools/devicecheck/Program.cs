using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DispCtrl.Core.Devices;

// The device library's backend, run by CI and by maintainers:
//
//   devicecheck validate [devices]        every definition parses and passes the rules
//   devicecheck index    [devices]        rewrite devices/index.json (--check: fail if stale)
//   devicecheck intake   BODY [devices]   turn a shared issue body into files
//
// The rules are DeviceDefinitions.Validate - the same code DispCtrl loads a
// definition with - so nothing reaches the repository that the app would refuse.

string command = args.ElementAtOrDefault(0) ?? "validate";
return command switch
{
    "validate" => Validate(args.ElementAtOrDefault(1) ?? "devices"),
    "index" => Index(args.ElementAtOrDefault(1) is { } f && !f.StartsWith("--", StringComparison.Ordinal) ? f : "devices", args.Contains("--check")),
    "intake" => Intake(args.ElementAtOrDefault(1) ?? throw new ArgumentException("intake BODY-FILE [devices]"), args.ElementAtOrDefault(2) ?? "devices"),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("devicecheck validate|index [--check]|intake BODY");
    return 2;
}

static int Validate(string root)
{
    string folder = Path.Combine(root, "definitions");
    int failures = 0, count = 0;
    foreach (string file in Directory.EnumerateFiles(folder, "*.json"))
    {
        count++;
        string name = Path.GetFileNameWithoutExtension(file);
        try
        {
            DeviceDefinition d = JsonSerializer.Deserialize(File.ReadAllText(file), DeviceJsonContext.Default.DeviceDefinition)
                ?? throw new JsonException("empty");
            var problems = DeviceDefinitions.Validate(d);
            string expected = d.Target == "*" ? "common" : d.Target;
            if (!string.Equals(expected, name, StringComparison.Ordinal)) problems.Add($"file must be named {expected}.json");
            foreach (string p in problems) { Console.WriteLine($"FAIL {name}: {p}"); failures++; }
            if (problems.Count == 0) Console.WriteLine($"ok   {name}: {d.Controls.Count} control(s)");
        }
        catch (JsonException ex) { Console.WriteLine($"FAIL {name}: {ex.Message}"); failures++; }
    }
    Console.WriteLine($"{count} definition(s), {failures} problem(s)");
    return failures == 0 ? 0 : 1;
}

static int Index(string root, bool check)
{
    var entries = new JsonArray();
    var definitions = DeviceLibrary.LoadFolder(Path.Combine(root, "definitions"));
    var records = Directory.EnumerateFiles(root, "*.md").Select(f => Path.GetFileNameWithoutExtension(f))
        .Where(DeviceDefinitions.IsModel).ToHashSet(StringComparer.Ordinal);
    foreach (string target in definitions.Keys.Concat(records).Distinct().Order(StringComparer.Ordinal))
    {
        definitions.TryGetValue(target, out DeviceDefinition? d);
        entries.Add((JsonNode)new JsonObject
        {
            ["target"] = target,
            ["name"] = d?.Name,
            ["record"] = records.Contains(target),
            ["extends"] = new JsonArray((d?.Extends ?? []).Select(e => (JsonNode?)JsonValue.Create(e)).ToArray()),
            ["codes"] = new JsonArray((d?.Controls ?? []).Select(c => (JsonNode)new JsonObject
            {
                ["code"] = c.Code, ["key"] = c.EffectiveKey, ["kind"] = c.Kind, ["writable"] = c.Writable, ["confidence"] = c.Confidence,
            }).ToArray()),
        });
    }
    string text = new JsonObject { ["schema"] = 1, ["targets"] = entries }
        .ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n";
    string path = Path.Combine(root, "index.json");
    if (check)
    {
        bool current = File.Exists(path) && File.ReadAllText(path).Replace("\r\n", "\n") == text;
        Console.WriteLine(current ? "index is current" : "index.json is stale: run devicecheck index");
        return current ? 0 : 1;
    }
    File.WriteAllText(path, text);
    Console.WriteLine($"wrote {path}: {entries.Count} target(s)");
    return 0;
}

// An issue body from DispCtrl's share: a Markdown record, then one fenced JSON
// block. Everything in it is untrusted; only validated definitions and a record
// for a well-formed model key are written, and a person reviews the pull request.
static int Intake(string bodyFile, string root)
{
    string body = File.ReadAllText(bodyFile).Replace("\r\n", "\n");
    Match block = Regex.Match(body, "```json\\s*\\n(?<json>\\{.*?\\})\\s*\\n```", RegexOptions.Singleline);
    if (!block.Success) { Console.Error.WriteLine("No JSON block: not a DispCtrl device share."); return 3; }
    JsonNode? payload;
    try { payload = JsonNode.Parse(block.Groups["json"].Value); }
    catch (JsonException ex) { Console.Error.WriteLine($"The JSON block does not parse: {ex.Message}"); return 1; }
    if (payload?["kind"]?.GetValue<string>() != "dispctrl-device-mapping") { Console.Error.WriteLine("Not a device mapping."); return 3; }
    string model = payload["model"]?.GetValue<string>() ?? "";
    if (!DeviceDefinitions.IsModel(model)) { Console.Error.WriteLine($"'{model}' is not a model key."); return 1; }

    int written = 0;
    string folder = Path.Combine(root, "definitions");
    Directory.CreateDirectory(folder);
    foreach (JsonNode? node in payload["definitions"]?.AsArray() ?? [])
    {
        DeviceDefinition incoming = JsonSerializer.Deserialize(node, DeviceJsonContext.Default.DeviceDefinition)
            ?? throw new JsonException("empty definition");
        var problems = DeviceDefinitions.Validate(incoming);
        if (problems.Count > 0) { Console.Error.WriteLine($"{incoming.Target}: {string.Join("; ", problems)}"); return 1; }

        // Merged into what the repository has, code by code: a share replaces
        // the codes it defines and leaves the rest, and sources accumulate.
        string path = Path.Combine(folder, (incoming.Target == "*" ? "common" : incoming.Target) + ".json");
        DeviceDefinition current = File.Exists(path)
            ? JsonSerializer.Deserialize(File.ReadAllText(path), DeviceJsonContext.Default.DeviceDefinition)!
            : new DeviceDefinition { Target = incoming.Target };
        current.Name ??= incoming.Name;
        foreach (string link in incoming.Extends) if (!current.Extends.Contains(link)) current.Extends.Add(link);
        foreach (DefinedControl c in incoming.Controls)
        {
            DefinedControl? before = current.Controls.FirstOrDefault(x => x.CodeValue == c.CodeValue);
            if (before is not null)
            {
                foreach (string s in before.Sources) if (!c.Sources.Contains(s)) c.Sources.Add(s);
                current.Controls.Remove(before);
            }
            current.Controls.Add(c);
        }
        current.Controls.Sort((a, b) => (a.CodeValue ?? 0).CompareTo(b.CodeValue ?? 0));
        var merged = DeviceDefinitions.Validate(current);
        if (merged.Count > 0) { Console.Error.WriteLine($"{current.Target} after merge: {string.Join("; ", merged)}"); return 1; }
        File.WriteAllText(path, JsonSerializer.Serialize(current, DeviceJsonContext.Default.DeviceDefinition).Replace("\r\n", "\n") + "\n");
        Console.WriteLine($"definition {current.Target}: {incoming.Controls.Count} code(s)");
        written++;
    }

    // The record is the Markdown before the mappings, present when the monitor
    // was attached while sharing. New models get one; existing ones keep theirs
    // unless the maintainer takes the new text from the pull request.
    int split = body.IndexOf("\n### Mappings", StringComparison.Ordinal);
    string record = split > 0 ? body[..split].Trim() : "";
    string recordPath = Path.Combine(root, model + ".md");
    if (record.StartsWith("### ", StringComparison.Ordinal) && !record.Contains("the monitor was not attached", StringComparison.Ordinal)
        && !File.Exists(recordPath))
    {
        File.WriteAllText(recordPath, record + "\n");
        Console.WriteLine($"record {model}");
        written++;
    }

    Console.WriteLine(written == 0 ? "nothing to add" : $"{written} file(s) written");
    return written == 0 ? 4 : Index(root, check: false);
}
