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
//   devicecheck guard    BEFORE AFTER     list every change AFTER makes to what BEFORE had reviewed
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
    "intake" => Intake(args.ElementAtOrDefault(1) ?? throw new ArgumentException("intake BODY-FILE [devices] [--issue N]"), Root(2), IssueArgument(args)),
    "guard" => Guard(args.ElementAtOrDefault(1) ?? throw new ArgumentException("guard BEFORE AFTER"), args.ElementAtOrDefault(2) ?? "devices"),
    "migrate" => Migrate(Root(1)),
    "selftest" => SelfTest(),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("devicecheck validate|index [--check]|intake BODY [devices] [--issue N]|guard BEFORE AFTER|migrate [devices]|selftest");
    return 2;
}

static int? IssueArgument(string[] args)
{
    int at = Array.IndexOf(args, "--issue");
    if (at < 0) return null;
    return int.TryParse(args.ElementAtOrDefault(at + 1), System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out int issue) && issue > 0
        ? issue : throw new ArgumentException("--issue takes an issue number.");
}

// reports.json: { "issues": [4, 5] } - ascending, no repeats, numbers only.
static List<int>? ReadReports(string path)
{
    if (!File.Exists(path)) return [];
    try
    {
        var issues = JsonNode.Parse(File.ReadAllText(path))?["issues"]?.AsArray().Select(n => n!.GetValue<int>()).ToList();
        return issues is not null && issues.All(i => i > 0) && issues.SequenceEqual(issues.Distinct().Order()) ? issues : null;
    }
    catch (Exception) { return null; }
}

static string ReportsText(IEnumerable<int> issues) =>
    new JsonObject { ["issues"] = new JsonArray(issues.Distinct().Order().Select(i => (JsonNode?)JsonValue.Create(i)).ToArray()) }
        .ToJsonString() + "\n";

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
    string Share(string model, string code = "0xE2") => $"### {model}\n\nDevice key: `{model}`\n\n---\n\nSubmitted from DispCtrl. Model capabilities are included; brightness, wallpaper and app settings are not.\n\n### Mappings\n\n```json\n"
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
        File.WriteAllText(input, Share("TST-0101", "0xE3").Replace("Test mode", "Something else").Replace("\"information\"", "\"range\",\"writable\":true"));
        Check(Intake(input, output) is 0 or 4 && JsonNode.Parse(File.ReadAllText(DeviceLayout.DefinitionPath(output, "TST-0101")))!["controls"]!.AsArray()
            .Single(c => c!["code"]!.GetValue<string>() == "0xE3")!["name"]!.GetValue<string>() == "Test mode",
            "a share never renames or re-kinds a code the library already has");
        File.WriteAllText(input, Share("TST-0808").Replace("\"information\"", "\"range\",\"writable\":true"));
        Check(Intake(input, output) == 0 && JsonNode.Parse(File.ReadAllText(DeviceLayout.DefinitionPath(output, "TST-0808")))!["controls"]![0]!["writable"]?.GetValue<bool>() != true,
            "a new code from a share arrives read-only, whatever the share asked");
        File.WriteAllText(input, Share("TST-0909").Replace("\"target\":\"TST-0909\"", "\"target\":\"TST\""));
        Check(Intake(input, output) is 0 or 4 && !File.Exists(DeviceLayout.DefinitionPath(output, "TST")),
            "a share cannot write a maker-wide definition");
        string sealedCopy = Path.Combine(temp, "sealed");
        foreach (string f in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(sealedCopy, Path.GetRelativePath(output, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to);
        }
        Check(Guard(output, sealedCopy) == 0, "an unchanged library passes the guard");
        File.WriteAllText(DeviceLayout.DefinitionPath(sealedCopy, "TST-0101"),
            new Regex("Test mode").Replace(File.ReadAllText(DeviceLayout.DefinitionPath(sealedCopy, "TST-0101")), "Factory reset", 1));
        File.Delete(DeviceLayout.RecordPath(sealedCopy, "TST-0202"));
        Check(Guard(output, sealedCopy) == 2, "renaming a reviewed code and deleting a record are both caught");
        File.WriteAllText(input, Share("TST-0A0A").Replace("Device key: `TST-0A0A`", "Device key: `TST-0A0A`\n\n<img src=x onerror=alert(1)> see https://spam.example"));
        Check(Intake(input, Path.Combine(temp, "spam")) == 1 && !Directory.Exists(Path.Combine(temp, "spam")),
            "a record with a link or HTML in it is refused outright");
        File.WriteAllText(input, "Paste the complete device share here.");
        Check(Intake(input, refused) == 3 && !Directory.Exists(refused), "a link placeholder is never ingested as a record");
        Check(DeviceDefinitions.Validate(new DeviceDefinition { Target = "TST-0303", Panel = new() { Technology = "OLED" } }).Count == 0,
            "a model's definition may say what its panel is, with no controls at all");
        Check(DeviceDefinitions.Validate(new DeviceDefinition { Target = "TST", Panel = new() { Technology = "OLED" } }).Count == 1,
            "a manufacturer's definition may not: a maker ships both kinds");
        Check(DeviceDefinitions.Validate(new DeviceDefinition { Target = "TST-0303", Panel = new() { Technology = " " } }).Count == 1,
            "a panel needs a technology");
        string library = Path.Combine(temp, "library");
        Directory.CreateDirectory(Path.GetDirectoryName(DeviceLayout.DefinitionPath(library, "TST-0404"))!);
        File.WriteAllText(DeviceLayout.DefinitionPath(library, "TST-0404"), "{\"schema\":1,\"target\":\"TST-0404\",\"panel\":{\"technology\":\"OLED\"},\"controls\":[]}");
        Directory.CreateDirectory(Path.GetDirectoryName(DeviceLayout.DefinitionPath(library, "TST-0505"))!);
        File.WriteAllText(DeviceLayout.DefinitionPath(library, "TST-0505"), "{\"schema\":1,\"target\":\"TST-0505\",\"extends\":[\"TST-0404\"],\"controls\":[]}");
        var saved = DeviceLibrary.FoldersOverride;
        DeviceLibrary.FoldersOverride = (library, Path.Combine(temp, "user"));
        try
        {
            Check(DeviceLibrary.Panel("TST-0404")?.IsOled == true && DeviceLibrary.Panel("TST-0505")?.IsOled == true
                && DeviceLibrary.Panel("TST-0606") is null, "the panel resolves from the model, then what it extends, and is absent otherwise");
        }
        finally { DeviceLibrary.FoldersOverride = saved; }
        string reported = Path.Combine(temp, "reported");
        File.WriteAllText(input, Share("TST-0707"));
        Check(Intake(input, reported, 4) == 0 && Intake(input, reported, 5) == 0 && Intake(input, reported, 5) == 4
            && ReadReports(DeviceLayout.ReportsPath(reported, "TST-0707")) is [4, 5],
            "each issue is counted once per model, by number, and a repeat of the same issue changes nothing");
        Check(!File.ReadAllText(DeviceLayout.ReportsPath(reported, "TST-0707")).Contains('@')
            && Validate(reported) == 0, "a report list holds numbers only and passes validation");
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
                if (f is not DeviceLayout.RecordFile and not DeviceLayout.DefinitionFile and not DeviceLayout.ReportsFile)
                    problems.Add($"{Rel(root, product)}/{f}: a model folder holds only {DeviceLayout.RecordFile}, {DeviceLayout.DefinitionFile} and {DeviceLayout.ReportsFile}");
            if (ReadReports(DeviceLayout.ReportsPath(root, key)) is null)
                problems.Add($"{Rel(root, product)}/{DeviceLayout.ReportsFile}: must be {{\"issues\": [ ... ]}}, issue numbers only, ascending and without repeats");
            if (files.Count == 0) problems.Add($"{Rel(root, product)}/: empty");
            if (Directory.EnumerateDirectories(product).Any()) problems.Add($"{Rel(root, product)}/: a model folder has no subfolders");
            string record = Path.Combine(product, DeviceLayout.RecordFile);
            if (File.Exists(record)) problems.AddRange(RecordProblems(File.ReadAllText(record), key).Select(p => $"{Rel(root, record)}: {p}"));
        }
    }

    foreach (var (target, d) in definitions.OrderBy(p => p.Key, StringComparer.Ordinal))
        Console.WriteLine($"ok   {target}: {d.Controls.Count} control(s){(d.Panel is { } panel ? ", panel " + panel.Technology : "")}");
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
    // What DispCtrl writes, and nothing else: a record is merged without a
    // person reading it, so anything a generated one never holds is refused.
    if (text.Length > 32 * 1024) yield return "is longer than 32 KB";
    if (text.Contains("://", StringComparison.Ordinal) || text.Contains("www.", StringComparison.OrdinalIgnoreCase)) yield return "carries a link";
    if (Regex.IsMatch(text, @"<(?!/?(details|summary)>)", RegexOptions.IgnoreCase)) yield return "carries HTML other than <details> and <summary>";
    if (text.Contains('@') && Regex.IsMatch(text, @"[\w.+-]+@[\w-]+\.[\w.]+")) yield return "carries an email address";
    if (text.Any(ch => char.IsControl(ch) && ch is not '\n' and not '\r' and not '\t')) yield return "carries control characters";
    if (!text.TrimEnd().EndsWith("brightness, wallpaper and app settings are not.", StringComparison.Ordinal)) yield return "does not end with DispCtrl's own footer";
}

// A row of a record's table, or null.
static string? RecordRow(string root, string model, string label)
{
    string record = DeviceLayout.RecordPath(root, model);
    if (!File.Exists(record)) return null;
    return Regex.Match(File.ReadAllText(record), $@"^\|\s*{Regex.Escape(label)}\s*\|\s*(?<v>[^|]+?)\s*\|", RegexOptions.Multiline) is { Success: true } m
        ? m.Groups["v"].Value : null;
}

// The name a model goes by: its definition's, else the record's table.
static string? NameOf(string root, string model, DeviceDefinition? d)
{
    if (!string.IsNullOrWhiteSpace(d?.Name)) return d!.Name;
    string? Row(string label) => RecordRow(root, model, label);
    string? maker = Row("Manufacturer") is { } mk ? Regex.Replace(mk, @"\s*\([A-Z]{3}\)$", "") : null;
    string? product = Row("Model");
    if (Row("Connector") == "Internal" && (product is null || product == model))
    {
        // Built-in panels rarely name themselves; say what the record does know.
        string? inches = Row("Physical size") is { } size && Regex.Match(size, @"\((?<in>[\d.]+) in\)") is { Success: true } s ? s.Groups["in"].Value + " inch" : null;
        string? machine = Row("Built into");
        return $"Built-in panel{(inches is null ? "" : ", " + inches)}{(machine is null ? "" : ", in " + machine)}";
    }
    if (product is null) return null;
    return maker is null || product.StartsWith(maker, StringComparison.OrdinalIgnoreCase) ? product : $"{maker} {product}";
}

// What the panel is: the definition's word, else the record's, without its hedge.
static string? PanelOf(string root, string model, DeviceDefinition? d)
{
    if (d?.Panel is { } panel) return panel.Technology;
    string? row = RecordRow(root, model, "Panel technology");
    if (row is null || row.StartsWith("not ", StringComparison.OrdinalIgnoreCase)) return null;
    return Regex.Replace(row, @"\s*\((reported by owner|device library)\)$", "");
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
            ["panel"] = PanelOf(root, m, d),
            ["writable"] = d?.Controls.Count(c => c.Writable) ?? 0,
            // How many shares it arrived in: a count to judge a mapping by,
            // with no name attached to any of them.
            ["reports"] = ReadReports(DeviceLayout.ReportsPath(root, m))?.Count ?? 0,
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
        string maker = DispCtrl.Core.Displays.PnpNames.For(b);
        md.Append(maker.Length > 0 ? $"\n## {maker} ({b})\n\n" : $"\n## {b}\n\n");
        if (definitions.TryGetValue(b, out DeviceDefinition? bd)) md.Append($"Every {b} model: {bd.Controls.Count} mapped code(s), [brand.json]({b}/{DeviceLayout.BrandFile}).\n\n");
        if (inBrand.Count == 0) continue;
        md.Append("| Model | Name | Panel | Record | Mapped codes | Reports |\n|---|---|---|---|---|---|\n");
        foreach (string m in inBrand)
        {
            definitions.TryGetValue(m, out DeviceDefinition? d);
            string folder = $"{b}/{m[4..]}";
            string record = File.Exists(DeviceLayout.RecordPath(root, m)) ? $"[record]({folder}/{DeviceLayout.RecordFile})" : "";
            // A definition that only says what the panel is still gets its link.
            string codes = d is null ? "" : $"[{(d.Controls.Count > 0 ? d.Controls.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "definition")}]({folder}/{DeviceLayout.DefinitionFile})";
            int reports = ReadReports(DeviceLayout.ReportsPath(root, m))?.Count ?? 0;
            md.Append($"| [`{m}`]({folder}/) | {(NameOf(root, m, d) ?? "").Replace("|", "\\|")} | {PanelOf(root, m, d) ?? ""} | {record} | {codes} | {(reports > 0 ? reports.ToString(System.Globalization.CultureInfo.InvariantCulture) : "")} |\n");
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
static int Intake(string bodyFile, string root, int? issue = null)
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

        // The issue it came in, by number: a second owner confirming a model
        // is worth a pull request even when it adds no new code.
        if (issue is { } number && payload["model"]?.GetValue<string>() is { } model && DeviceDefinitions.IsModel(model))
        {
            string reportsPath = DeviceLayout.ReportsPath(root, model);
            List<int> known = (pending.TryGetValue(reportsPath, out string? staged)
                ? JsonNode.Parse(staged)?["issues"]?.AsArray().Select(n => n!.GetValue<int>()).ToList()
                : ReadReports(reportsPath)) ?? [];
            if (!known.Contains(number))
            {
                pending[reportsPath] = ReportsText(known.Append(number));
                Console.WriteLine($"report #{number} -> {Rel(root, reportsPath)} ({known.Count + 1} in all)");
            }
        }
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
        // Only the model's own definition. One for a maker or for every monitor
        // changes what DispCtrl does to monitors the sharer never had.
        if (!string.Equals(incoming.Target, model, StringComparison.Ordinal))
        {
            Console.WriteLine($"review: left out the definition for {incoming.Target}; a share only adds to {model}'s own");
            continue;
        }

        // Added to what the repository has, never over it: what is there was
        // reviewed, and a share that disagrees is noted for the pull request,
        // not applied. Only a person edits a reviewed mapping.
        string path = DeviceLayout.DefinitionPath(root, incoming.Target);
        bool known = pending.TryGetValue(path, out string? staged) || File.Exists(path);
        DeviceDefinition current = known
            ? JsonSerializer.Deserialize(staged ?? File.ReadAllText(path), DeviceJsonContext.Default.DeviceDefinition)!
            : new DeviceDefinition { Target = incoming.Target, Name = incoming.Name, Extends = incoming.Extends };
        if (known && !incoming.Extends.All(current.Extends.Contains))
            Console.WriteLine($"review: {model} kept its links; this share would link it to {string.Join(", ", incoming.Extends)}");
        if (incoming.Panel is { } said)
        {
            if (current.Panel is null) current.Panel = said;
            else if (!string.Equals(current.Panel.Technology, said.Technology, StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"review: {model} kept panel {current.Panel.Technology}; this share says {said.Technology}");
        }
        int added = 0;
        foreach (DefinedControl c in incoming.Controls)
        {
            DefinedControl? before = current.Controls.FirstOrDefault(x => x.CodeValue == c.CodeValue);
            if (before is not null)
            {
                if (Essence(before) == Essence(c))
                    foreach (string s in c.Sources) { if (!before.Sources.Contains(s)) before.Sources.Add(s); }
                else Console.WriteLine($"review: {model} {before.Code} kept as \"{before.Name}\" ({before.Kind}{(before.Writable ? ", writable" : "")}); this share says \"{c.Name}\" ({c.Kind}{(c.Writable ? ", writable" : "")})");
                continue;
            }
            // Writing a manufacturer's code is the one risk in the library, so a
            // stranger's word is not enough: the reviewer switches it on.
            if (c.Writable)
            {
                c.Writable = false;
                Console.WriteLine($"review: {model} {c.Code} \"{c.Name}\" was shared as writable and arrives read-only; set \"writable\": true in the pull request to accept that");
            }
            current.Controls.Add(c);
            added++;
        }
        if (known && added == 0 && incoming.Panel is null) continue;
        current.Controls.Sort((a, b) => (a.CodeValue ?? 0).CompareTo(b.CodeValue ?? 0));
        var merged = DeviceDefinitions.Validate(current);
        if (merged.Count > 0) { Console.Error.WriteLine($"{current.Target} after merge: {string.Join("; ", merged)}"); return 1; }
        pending[path] = JsonSerializer.Serialize(current, DeviceJsonContext.Default.DeviceDefinition).Replace("\r\n", "\n") + "\n";
        Console.WriteLine($"definition {current.Target}: {incoming.Controls.Count} code(s){(incoming.Panel is { } p ? ", panel " + p.Technology : "")} -> {Rel(root, path)}");
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

// A mapping as the app acts on it: what sources and notes say does not change it.
static string Essence(DefinedControl c) => JsonSerializer.Serialize(new DefinedControl
{
    Code = c.CodeValue?.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) ?? c.Code,
    Key = c.Key, Name = c.Name, Kind = c.Kind, Writable = c.Writable, Maximum = c.Maximum, Values = c.Values,
}, DeviceJsonContext.Default.DefinedControl);

// What a change does to reviewed data: a record, a report, or any mapping the
// library already had. Adding is never listed. Run from the base branch's copy
// of this tool, so a pull request cannot change the rules it is judged by.
static int Guard(string before, string after)
{
    var found = new List<string>();
    if (Directory.Exists(before))
    {
        foreach (string model in DeviceLayout.Models(before))
        {
            string a = DeviceLayout.RecordPath(before, model), b = DeviceLayout.RecordPath(after, model);
            if (File.Exists(a) && (!File.Exists(b) || Norm(a) != Norm(b))) found.Add($"{model}: its record was changed or removed");
            var lost = (ReadReports(DeviceLayout.ReportsPath(before, model)) ?? []).Except(ReadReports(DeviceLayout.ReportsPath(after, model)) ?? []).ToList();
            if (lost.Count > 0) found.Add($"{model}: report(s) removed: {string.Join(", ", lost.Select(i => "#" + i))}");
        }
        var was = DeviceLibrary.LoadLibrary(before);
        var now = Directory.Exists(after) ? DeviceLibrary.LoadLibrary(after) : new Dictionary<string, DeviceDefinition>();
        foreach (var (target, old) in was)
        {
            string label = target == "*" ? "every monitor (common.json)" : target;
            if (!now.TryGetValue(target, out DeviceDefinition? d)) { found.Add($"{label}: definition removed"); continue; }
            if (old.Name != d.Name) found.Add($"{label}: renamed from \"{old.Name}\" to \"{d.Name}\"");
            if (!old.Extends.SequenceEqual(d.Extends)) found.Add($"{label}: links changed");
            if (old.Panel is not null && !string.Equals(old.Panel.Technology, d.Panel?.Technology, StringComparison.OrdinalIgnoreCase))
                found.Add($"{label}: panel changed from {old.Panel.Technology} to {d.Panel?.Technology ?? "nothing"}");
            foreach (DefinedControl c in old.Controls)
            {
                DefinedControl? n = d.Controls.FirstOrDefault(x => x.CodeValue == c.CodeValue);
                if (n is null) found.Add($"{label} {c.Code}: removed");
                else if (Essence(c) != Essence(n)) found.Add($"{label} {c.Code}: changed (\"{c.Name}\"{(c.Writable ? ", writable" : "")} -> \"{n.Name}\"{(n.Writable ? ", writable" : "")})");
            }
            // A maker's or every monitor's definition reaches monitors nobody tested.
            if (target.Length <= 3 && d.Controls.Count > old.Controls.Count) found.Add($"{label}: code(s) added to a definition every such monitor loads");
        }
        foreach (string target in now.Keys.Where(t => t.Length <= 3 && !was.ContainsKey(t)))
            found.Add($"{(target == "*" ? "every monitor (common.json)" : target)}: new definition every such monitor loads");
        foreach (var (target, d) in now)
            foreach (DefinedControl c in d.Controls.Where(c => c.Writable))
                if (!was.TryGetValue(target, out DeviceDefinition? o) || o.Controls.FirstOrDefault(x => x.CodeValue == c.CodeValue) is not { Writable: true })
                    found.Add($"{target} {c.Code}: newly writable - DispCtrl will write \"{c.Name}\" to every such monitor");
    }
    foreach (string f in found) Console.WriteLine("sealed: " + f);
    Console.WriteLine(found.Count == 0 ? "only additions" : $"{found.Count} change(s) to reviewed data");
    return found.Count;

    static string Norm(string path) => File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd();
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
