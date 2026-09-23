using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DispCtrl.Core.Devices;

// The device library's backend, run by CI and by maintainers:
//
//   devicecheck validate [devices]        every file is where its key says, and passes the rules
//   devicecheck index    [devices]        rewrite index.json and CATALOG.md (--check: fail if stale)
//   devicecheck intake   BODY [devices]   turn a shared issue body into files
//   devicecheck migrate  [devices]        move a flat library (definitions/*.json, *.md) into folders
//
// The layout is DeviceLayout's: devices/DEL/A234/{definition.json,record.md},
// devices/DEL/brand.json, devices/common.json. The rules are
// DeviceDefinitions.Validate - the same code DispCtrl loads a definition with -
// so nothing reaches the repository that the app would refuse.

string command = args.ElementAtOrDefault(0) ?? "validate";
string Root(int i) => args.ElementAtOrDefault(i) is { } f && !f.StartsWith("--", StringComparison.Ordinal) ? f : "devices";
return command switch
{
    "validate" => Validate(Root(1)),
    "index" => Index(Root(1), args.Contains("--check")),
    "intake" => Intake(args.ElementAtOrDefault(1) ?? throw new ArgumentException("intake BODY-FILE [devices]"), Root(2)),
    "migrate" => Migrate(Root(1)),
    "selftest" => SelfTest(),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("devicecheck validate|index [--check]|intake BODY|migrate [devices]|selftest");
    return 2;
}

static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

static int SelfTest()
{
    string temp = Path.Combine(Path.GetTempPath(), "DispCtrl-devicecheck-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    int checks = 0;
    void Check(bool ok, string description)
    {
        if (!ok) throw new Exception(description);
        Console.WriteLine("PASS " + description);
        checks++;
    }
    string Share(string model, string code = "0xE2") => $"### {model}\n\nDevice key: `{model}`\n\n### Mappings\n\n```json\n"
        + new JsonObject
        {
            ["schema"] = 1, ["kind"] = "dispctrl-device-mapping", ["model"] = model,
            ["definitions"] = new JsonArray(new JsonObject
            {
                ["schema"] = 1, ["target"] = model,
                ["controls"] = new JsonArray(new JsonObject { ["code"] = code, ["name"] = "Test mode", ["kind"] = "information" }),
            }),
        }.ToJsonString() + "\n```\n";
    try
    {
        string input = Path.Combine(temp, "issue.md"), output = Path.Combine(temp, "devices");
        File.WriteAllText(input, Share("TST-0101") + "\n\n---\n\n" + Share("TST-0202"));
        Check(Intake(input, output) == 0 && File.Exists(DeviceLayout.DefinitionPath(output, "TST-0101"))
            && File.Exists(DeviceLayout.DefinitionPath(output, "TST-0202"))
            && File.ReadAllText(DeviceLayout.RecordPath(output, "TST-0202")).StartsWith("### TST-0202"),
            "combined intake keeps both definitions and associates each record with its model");
        string refused = Path.Combine(temp, "refused");
        File.WriteAllText(input, Share("TST-0101") + "\n\n---\n\n" + Share("TST-0202", "bad-code"));
        Check(Intake(input, refused) == 1 && !Directory.Exists(refused),
            "an invalid later model leaves no partial combined intake on disk");
        File.WriteAllText(input, Share("TST-0101", "0xE3"));
        Check(Intake(input, output) == 0 && JsonNode.Parse(File.ReadAllText(DeviceLayout.DefinitionPath(output, "TST-0101")))!["controls"]!.AsArray().Count == 2,
            "single-model shares still merge without removing existing codes");
        File.WriteAllText(input, "Paste the complete device share here.");
        Check(Intake(input, refused) == 3 && !Directory.Exists(refused), "a link placeholder is never ingested as a record");
        Console.WriteLine($"{checks} intake checks passed.");
        return 0;
    }
    finally
    {
        if (Path.GetDirectoryName(temp) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
            && Path.GetFileName(temp).StartsWith("DispCtrl-devicecheck-", StringComparison.Ordinal)) Directory.Delete(temp, true);
    }
}

static int Validate(string root)
{
    var problems = new List<string>();
    var definitions = DeviceLibrary.LoadLibrary(root, problems);

    // Nothing but the layout: a stray file is usually a record or definition
    // put in the wrong place, which the app would then silently never read.
    string[] rootFiles = ["README.md", "CATALOG.md", DeviceLayout.IndexFile, DeviceLayout.CommonFile];
    foreach (string file in Directory.EnumerateFiles(root))
        if (!rootFiles.Contains(Path.GetFileName(file))) problems.Add($"{Rel(root, file)}: not part of the layout (models go in BRAND/PRODUCT/)");
    foreach (string dir in Directory.EnumerateDirectories(root))
    {
        string name = Path.GetFileName(dir);
        if (name == "schema") continue;
        if (name.Length != 3 || !DeviceDefinitions.IsTarget(name)) { problems.Add($"{Rel(root, dir)}/: not a manufacturer code such as DEL"); continue; }
        foreach (string file in Directory.EnumerateFiles(dir))
            if (Path.GetFileName(file) != DeviceLayout.BrandFile) problems.Add($"{Rel(root, file)}: a manufacturer folder holds only {DeviceLayout.BrandFile} and model folders");
        foreach (string product in Directory.EnumerateDirectories(dir))
        {
            string key = name + "-" + Path.GetFileName(product);
            if (!DeviceDefinitions.IsModel(key)) { problems.Add($"{Rel(root, product)}/: not a product code such as A234 (four hex digits, upper case)"); continue; }
            var files = Directory.EnumerateFiles(product).Select(Path.GetFileName).ToList();
            foreach (string? f in files)
                if (f is not DeviceLayout.RecordFile and not DeviceLayout.DefinitionFile) problems.Add($"{Rel(root, product)}/{f}: a model folder holds only {DeviceLayout.RecordFile} and {DeviceLayout.DefinitionFile}");
            if (files.Count == 0) problems.Add($"{Rel(root, product)}/: empty");
            if (Directory.EnumerateDirectories(product).Any()) problems.Add($"{Rel(root, product)}/: a model folder has no subfolders");
            string record = Path.Combine(product, DeviceLayout.RecordFile);
            if (File.Exists(record)) problems.AddRange(RecordProblems(File.ReadAllText(record), key).Select(p => $"{Rel(root, record)}: {p}"));
        }
    }

    foreach (var (target, d) in definitions.OrderBy(p => p.Key, StringComparer.Ordinal))
        Console.WriteLine($"ok   {target}: {d.Controls.Count} control(s)");
    foreach (string p in problems) Console.WriteLine($"FAIL {p}");
    Console.WriteLine($"{definitions.Count} definition(s), {DeviceLayout.Models(root).Count()} model(s), {problems.Count} problem(s)");
    return problems.Count == 0 ? 0 : 1;
}

// A record is published text: the same things DispCtrl strips must not be in it.
static IEnumerable<string> RecordProblems(string text, string key)
{
    if (!text.StartsWith("### ", StringComparison.Ordinal)) yield return "should start with a '### ' heading";
    if (!text.Contains($"`{key}`", StringComparison.Ordinal)) yield return $"does not name its key `{key}`";
    if (text.Contains("DISPLAY#", StringComparison.OrdinalIgnoreCase)) yield return "carries a device path";
    if (Regex.IsMatch(text, @"[A-Za-z]:\\Users\\", RegexOptions.IgnoreCase)) yield return "carries a path under a user folder";
    if (Regex.IsMatch(text, @"\b\d&[0-9a-f]{6,8}&\d&UID\d+", RegexOptions.IgnoreCase)) yield return "carries a device instance id";
}

// The name a model goes by: its definition's, else the record's table.
static string? NameOf(string root, string model, DeviceDefinition? d)
{
    if (!string.IsNullOrWhiteSpace(d?.Name)) return d!.Name;
    string record = DeviceLayout.RecordPath(root, model);
    if (!File.Exists(record)) return null;
    string text = File.ReadAllText(record);
    string? Row(string label) => Regex.Match(text, $@"^\|\s*{label}\s*\|\s*(?<v>[^|]+?)\s*\|", RegexOptions.Multiline) is { Success: true } m ? m.Groups["v"].Value : null;
    string? maker = Row("Manufacturer") is { } mk ? Regex.Replace(mk, @"\s*\([A-Z]{3}\)$", "") : null;
    string? product = Row("Model");
    if (product is null)
    {
        // Built-in panels rarely name themselves; say what the record does know.
        string? inches = Row("Physical size") is { } size && Regex.Match(size, @"\((?<in>[\d.]+) in\)") is { Success: true } s ? s.Groups["in"].Value + " inch" : null;
        return Row("Connector") == "Internal" ? $"Built-in panel{(inches is null ? "" : ", " + inches)}" : null;
    }
    return maker is null || product.StartsWith(maker, StringComparison.OrdinalIgnoreCase) ? product : $"{maker} {product}";
}

static int Index(string root, bool check)
{
    var definitions = DeviceLibrary.LoadLibrary(root);
    var models = DeviceLayout.Models(root).ToList();
    var brands = models.Select(m => m[..3]).Concat(definitions.Keys.Where(k => k.Length == 3)).Distinct().Order(StringComparer.Ordinal).ToList();
    var opts = new JsonSerializerOptions { WriteIndented = false };

    // One line per model, sorted: two merges that add different models touch
    // different lines, and the file is regenerated after every merge anyway.
    var json = new StringBuilder();
    json.Append("{\n  \"schema\": 2,\n  \"generated\": \"tools/devicecheck index - do not edit; regenerated after every merge\",\n");
    json.Append($"  \"counts\": {{ \"brands\": {brands.Count}, \"models\": {models.Count}, \"records\": {models.Count(m => File.Exists(DeviceLayout.RecordPath(root, m)))}, \"definitions\": {definitions.Count} }},\n");
    json.Append($"  \"common\": {(definitions.ContainsKey("*") ? definitions["*"].Controls.Count : 0)},\n");
    json.Append("  \"brands\": {\n");
    json.Append(string.Join(",\n", brands.Select(b =>
    {
        definitions.TryGetValue(b, out DeviceDefinition? bd);
        var entry = new JsonObject { ["brandCodes"] = bd?.Controls.Count ?? 0, ["models"] = models.Count(m => m.StartsWith(b + "-", StringComparison.Ordinal)) };
        return $"    \"{b}\": {entry.ToJsonString(opts)}";
    })));
    json.Append("\n  },\n  \"models\": {\n");
    json.Append(string.Join(",\n", models.Select(m =>
    {
        definitions.TryGetValue(m, out DeviceDefinition? d);
        var entry = new JsonObject
        {
            ["name"] = NameOf(root, m, d),
            ["record"] = File.Exists(DeviceLayout.RecordPath(root, m)),
            ["codes"] = d?.Controls.Count ?? 0,
            ["writable"] = d?.Controls.Count(c => c.Writable) ?? 0,
            ["extends"] = new JsonArray((d?.Extends ?? []).Select(e => (JsonNode?)JsonValue.Create(e)).ToArray()),
        };
        return $"    \"{m}\": {entry.ToJsonString(opts)}";
    })));
    json.Append("\n  }\n}\n");

    // The same, for people: is my monitor here, and what is known of it?
    var md = new StringBuilder();
    md.Append("# Monitor catalogue\n\n");
    md.Append("Generated by `tools/devicecheck index` after every merge; do not edit. ");
    md.Append($"{models.Count} model(s) from {brands.Count} manufacturer(s). A **record** is what the model reports about itself; ");
    md.Append("**mapped** codes are ones the standard does not name that somebody has identified. See [README.md](README.md) to add yours.\n");
    foreach (string b in brands)
    {
        var inBrand = models.Where(m => m.StartsWith(b + "-", StringComparison.Ordinal)).ToList();
        md.Append($"\n## {b}\n\n");
        if (definitions.TryGetValue(b, out DeviceDefinition? bd)) md.Append($"Every {b} model: {bd.Controls.Count} mapped code(s), [brand.json]({b}/{DeviceLayout.BrandFile}).\n\n");
        if (inBrand.Count == 0) continue;
        md.Append("| Model | Name | Record | Mapped codes |\n|---|---|---|---|\n");
        foreach (string m in inBrand)
        {
            definitions.TryGetValue(m, out DeviceDefinition? d);
            string folder = $"{b}/{m[4..]}";
            string record = File.Exists(DeviceLayout.RecordPath(root, m)) ? $"[record]({folder}/{DeviceLayout.RecordFile})" : "";
            string codes = d is null ? "" : $"[{d.Controls.Count}]({folder}/{DeviceLayout.DefinitionFile})";
            md.Append($"| `{m}` | {(NameOf(root, m, d) ?? "").Replace("|", "\\|")} | {record} | {codes} |\n");
        }
    }

    string indexPath = Path.Combine(root, DeviceLayout.IndexFile), catalogPath = Path.Combine(root, "CATALOG.md");
    string indexText = json.ToString(), catalogText = md.ToString();
    if (check)
    {
        bool current = Same(indexPath, indexText) && Same(catalogPath, catalogText);
        Console.WriteLine(current ? "index is current" : "index.json or CATALOG.md is stale: run devicecheck index");
        return current ? 0 : 1;
    }
    File.WriteAllText(indexPath, indexText);
    File.WriteAllText(catalogPath, catalogText);
    Console.WriteLine($"wrote {DeviceLayout.IndexFile} and CATALOG.md: {models.Count} model(s), {brands.Count} brand(s)");
    return 0;

    static bool Same(string path, string text) => File.Exists(path) && File.ReadAllText(path).Replace("\r\n", "\n") == text;
}

// An issue body from DispCtrl's share: a Markdown record, then one fenced JSON
// block. Everything in it is untrusted; only validated definitions and a record
// for a well-formed model key are written, and a person reviews the pull request.
// The index is not touched: it is regenerated after the merge, so two shares
// never conflict over it.
static int Intake(string bodyFile, string root)
{
    string body = File.ReadAllText(bodyFile).Replace("\r\n", "\n");
    MatchCollection blocks = Regex.Matches(body, "```json\\s*\\n(?<json>\\{.*?\\})\\s*\\n```", RegexOptions.Singleline);
    if (blocks.Count == 0) { Console.Error.WriteLine("No JSON block: not a DispCtrl device share."); return 3; }
    // Stage every model first. A malformed later model must not leave half a
    // combined submission written to disk.
    var pending = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    int start = 0, recognised = 0;
    foreach (Match block in blocks)
    {
        JsonNode? payload;
        try { payload = JsonNode.Parse(block.Groups["json"].Value); }
        catch (JsonException ex) { Console.Error.WriteLine($"The JSON block does not parse: {ex.Message}"); return 1; }
        string section = body[start..(block.Index + block.Length)].Trim();
        start = block.Index + block.Length;
        if (section.StartsWith("---\n", StringComparison.Ordinal)) section = section[4..].TrimStart();
        if (payload?["kind"]?.GetValue<string>() != "dispctrl-device-mapping") continue;
        recognised++;
        int code = IntakeModel(section, payload, root, pending);
        if (code != 0) return code;
    }
    if (recognised == 0) return 3;
    foreach (var (path, text) in pending)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
    Console.WriteLine($"{recognised} model(s), {pending.Count} file(s) written");
    return pending.Count == 0 ? 4 : 0;
}

static int IntakeModel(string body, JsonNode payload, string root, Dictionary<string, string> pending)
{
    string model = payload["model"]?.GetValue<string>() ?? "";
    if (!DeviceDefinitions.IsModel(model)) { Console.Error.WriteLine($"'{model}' is not a model key."); return 1; }

    int written = 0;
    foreach (JsonNode? node in payload["definitions"]?.AsArray() ?? [])
    {
        DeviceDefinition incoming = JsonSerializer.Deserialize(node, DeviceJsonContext.Default.DeviceDefinition)
            ?? throw new JsonException("empty definition");
        var problems = DeviceDefinitions.Validate(incoming);
        if (problems.Count > 0) { Console.Error.WriteLine($"{incoming.Target}: {string.Join("; ", problems)}"); return 1; }

        // Merged into what the repository has, code by code: a share replaces
        // the codes it defines and leaves the rest, and sources accumulate.
        string path = DeviceLayout.DefinitionPath(root, incoming.Target);
        DeviceDefinition current = pending.TryGetValue(path, out string? staged) || File.Exists(path)
            ? JsonSerializer.Deserialize(staged ?? File.ReadAllText(path), DeviceJsonContext.Default.DeviceDefinition)!
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
        pending[path] = JsonSerializer.Serialize(current, DeviceJsonContext.Default.DeviceDefinition).Replace("\r\n", "\n") + "\n";
        Console.WriteLine($"definition {current.Target}: {incoming.Controls.Count} code(s) -> {Rel(root, path)}");
        written++;
    }

    // The record is the Markdown before the mappings, present when the monitor
    // was attached while sharing. New models get one; existing ones keep theirs
    // unless the maintainer takes the new text from the pull request.
    int split = body.IndexOf("\n### Mappings", StringComparison.Ordinal);
    string record = split > 0 ? body[..split].Trim() : "";
    string recordPath = DeviceLayout.RecordPath(root, model);
    // A real record names its key; a share whose record was too long for the
    // link carries a placeholder heading instead, and one made while the
    // monitor was unplugged carries none. Neither is a record.
    if (record.StartsWith("### ", StringComparison.Ordinal) && record.Contains($"Device key: `{model}`", StringComparison.Ordinal)
        && !File.Exists(recordPath) && !pending.ContainsKey(recordPath))
    {
        var leaks = RecordProblems(record, model).Where(p => !p.StartsWith("does not name", StringComparison.Ordinal)).ToList();
        if (leaks.Count > 0) { Console.Error.WriteLine($"record refused: {string.Join("; ", leaks)}"); return 1; }
        pending[recordPath] = record + "\n";
        Console.WriteLine($"record {model} -> {Rel(root, recordPath)}");
        written++;
    }

    Console.WriteLine(written == 0 ? "nothing to add" : $"{written} file(s) written");
    return 0;
}

// From the first, flat layout: definitions/DEL-A234.json, definitions/DEL.json,
// definitions/common.json and DEL-A234.md at the top. Moves, never overwrites.
static int Migrate(string root)
{
    int moved = 0, refused = 0;
    void Move(string from, string to)
    {
        if (File.Exists(to)) { Console.WriteLine($"SKIP {Rel(root, from)}: {Rel(root, to)} already exists"); refused++; return; }
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to);
        Console.WriteLine($"move {Rel(root, from)} -> {Rel(root, to)}");
        moved++;
    }
    string flat = Path.Combine(root, "definitions");
    if (Directory.Exists(flat))
    {
        foreach (string file in Directory.EnumerateFiles(flat, "*.json"))
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            string target = stem == "common" ? "*" : stem;
            if (!DeviceDefinitions.IsTarget(target)) { Console.WriteLine($"SKIP {Rel(root, file)}: not a device key"); refused++; continue; }
            Move(file, DeviceLayout.DefinitionPath(root, target));
        }
        if (!Directory.EnumerateFileSystemEntries(flat).Any()) Directory.Delete(flat);
    }
    foreach (string file in Directory.EnumerateFiles(root, "*.md"))
    {
        string stem = Path.GetFileNameWithoutExtension(file);
        if (DeviceDefinitions.IsModel(stem)) Move(file, DeviceLayout.RecordPath(root, stem));
    }
    Console.WriteLine($"{moved} moved, {refused} left in place");
    return refused == 0 ? 0 : 1;
}
