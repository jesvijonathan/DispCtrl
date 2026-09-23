using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DispCtrl.Core.Devices;

/// <summary>
/// What is known about the VCP codes of one model, one manufacturer, or every
/// monitor - the unit people share and the project collects.
/// </summary>
/// <remarks>
/// MCCS names the standard codes; everything from <c>0xE0</c> up, and much in
/// between, is left to the manufacturer. A capabilities string says only that a
/// code exists. A definition says what it is: a name, whether it is a range or a
/// choice, what each value means, and whether it has been seen to be safe to
/// write. Definitions are layered, so a mapping made on one Dell can serve every
/// Dell that lists the same code:
/// <list type="number">
/// <item><c>*</c> - every monitor.</item>
/// <item><c>DEL</c> - one manufacturer, by its EDID code.</item>
/// <item><c>DEL-A234</c> - one model, by manufacturer and EDID product code.</item>
/// </list>
/// A definition may also <see cref="Extends">extend</see> others: a model shares
/// a sibling's mappings without copying them, and a correction made once reaches
/// every model that links to it. Later layers win, code by code.
/// <para>
/// Nothing in a definition identifies a person or a unit: targets are model
/// keys, never tokens, and <see cref="DeviceDefinitions.Validate"/> refuses
/// anything shaped like a path or a serial.
/// </para>
/// </remarks>
public sealed class DeviceDefinition
{
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;

    /// <summary><c>*</c>, a manufacturer (<c>DEL</c>) or a model (<c>DEL-A234</c>).</summary>
    public string Target { get; set; } = "";

    /// <summary>What the target is called, for people reading the file.</summary>
    public string? Name { get; set; }

    /// <summary>Other targets whose mappings this one takes, before its own.</summary>
    public List<string> Extends { get; set; } = [];

    /// <summary>What the panel is, for a model; null when the definition says nothing about it.</summary>
    /// <remarks>
    /// The one part of a definition that is not about VCP codes, and the only
    /// part a built-in panel can use: it has no DDC/CI channel, so nothing on
    /// the machine will ever say it is OLED. Somebody who knows says it here,
    /// once, for every laptop with that panel.
    /// </remarks>
    public DefinedPanel? Panel { get; set; }

    public List<DefinedControl> Controls { get; set; } = [];
}

/// <summary>Facts about a model's panel that neither EDID nor Windows reports.</summary>
public sealed class DefinedPanel
{
    /// <summary><c>LCD</c>, <c>OLED</c>, <c>QD-OLED</c>, <c>Mini-LED</c> and so on.</summary>
    public string Technology { get; set; } = "";

    /// <summary>How it is known: a data sheet, the machine's specification, the owner.</summary>
    public string? Notes { get; set; }

    [JsonIgnore]
    public bool IsOled => Technology.Contains("OLED", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One VCP code, as a definition describes it.</summary>
public sealed class DefinedControl
{
    /// <summary>The VCP code, written <c>0xE2</c>.</summary>
    public string Code { get; set; } = "";

    /// <summary>The name scripts use; derived from <see cref="Name"/> when absent.</summary>
    public string? Key { get; set; }

    public string Name { get; set; } = "";

    /// <summary><c>range</c>, <c>choice</c>, <c>action</c> or <c>information</c>.</summary>
    public string Kind { get; set; } = DefinedKinds.Information;

    /// <summary>
    /// Seen to be safe to write. Only ever true because somebody wrote it and
    /// watched what the monitor did; a code is never writable by default.
    /// </summary>
    public bool Writable { get; set; }

    /// <summary>For a range, the highest value, when the monitor's own reply is wrong.</summary>
    public int? Maximum { get; set; }

    /// <summary>For a choice, what each value means.</summary>
    public List<DefinedValue> Values { get; set; } = [];

    /// <summary><c>observed</c>, <c>verified</c> or <c>documented</c>.</summary>
    public string Confidence { get; set; } = DefinedConfidence.Observed;

    /// <summary>The models this was worked out on.</summary>
    public List<string> Sources { get; set; } = [];

    public string? Notes { get; set; }

    /// <summary>The code as a number, or null when <see cref="Code"/> does not parse.</summary>
    [JsonIgnore]
    public byte? CodeValue => DeviceDefinitions.ParseCode(Code);

    [JsonIgnore]
    public string EffectiveKey => string.IsNullOrWhiteSpace(Key) ? DeviceDefinitions.KeyFor(Name) : Key!;
}

public sealed class DefinedValue
{
    /// <summary>The value, written <c>0x0B</c> or as a decimal number.</summary>
    public string Value { get; set; } = "";

    public string Name { get; set; } = "";

    [JsonIgnore]
    public uint? Number => DeviceDefinitions.ParseNumber(Value);
}

public static class DefinedKinds
{
    public const string Range = "range", Choice = "choice", Action = "action", Information = "information";
    public static readonly string[] All = [Range, Choice, Action, Information];
}

public static class DefinedConfidence
{
    /// <summary>Seen to change with the monitor's own menu.</summary>
    public const string Observed = "observed";

    /// <summary>Written and seen to do what the name says, on the source models.</summary>
    public const string Verified = "verified";

    /// <summary>From the manufacturer's own documentation.</summary>
    public const string Documented = "documented";

    public static readonly string[] All = [Observed, Verified, Documented];
}

/// <summary>A code as it resolves for one model, with where each part came from.</summary>
public sealed record ResolvedControl(byte Code, DefinedControl Definition, string Origin);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DeviceDefinition))]
[JsonSerializable(typeof(DeviceHistory))]
public sealed partial class DeviceJsonContext : JsonSerializerContext;

/// <summary>Parsing, keys and validation shared by the library, the CLI and the checks.</summary>
public static partial class DeviceDefinitions
{
    /// <summary><c>*</c>, a three-letter manufacturer, or manufacturer and four hex digits.</summary>
    public static bool IsTarget(string target) => target == "*" || TargetPattern().IsMatch(target);

    public static bool IsModel(string target) => target.Length == 8 && TargetPattern().IsMatch(target);

    /// <summary>The manufacturer part of a model key, "DEL" for "DEL-A234".</summary>
    public static string Brand(string model) => model.Length >= 3 ? model[..3] : model;

    public static byte? ParseCode(string text)
    {
        string t = text.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && byte.TryParse(t.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte hex)) return hex;
        return null;
    }

    public static uint? ParseNumber(string text)
    {
        string t = text.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(t.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hex) ? hex : null;
        return uint.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out uint number) ? number : null;
    }

    public static string FormatCode(byte code) => $"0x{code:X2}";

    /// <summary>"Preset mode" to "preset-mode", "6500 K" to "6500-k".</summary>
    public static string KeyFor(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char ch in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    /// <summary>Everything wrong with a definition, empty when it is fit to load or publish.</summary>
    public static List<string> Validate(DeviceDefinition definition)
    {
        var problems = new List<string>();
        if (definition.Schema != DeviceDefinition.CurrentSchema) problems.Add($"schema must be {DeviceDefinition.CurrentSchema}");
        if (!IsTarget(definition.Target)) problems.Add($"target '{definition.Target}' is not *, a manufacturer (DEL) or a model (DEL-A234)");
        Text(definition.Name, "name", 120, problems);

        foreach (string link in definition.Extends)
        {
            if (!IsTarget(link) || link == "*") problems.Add($"extends '{link}' is not a manufacturer or a model");
            if (link == definition.Target) problems.Add("a definition cannot extend itself");
        }

        if (definition.Panel is { } panel)
        {
            if (!IsModel(definition.Target)) problems.Add("panel: only a model's definition can say what its panel is");
            if (string.IsNullOrWhiteSpace(panel.Technology)) problems.Add("panel: needs a technology, such as LCD or OLED");
            Text(panel.Technology, "panel technology", 40, problems);
            Text(panel.Notes, "panel notes", 600, problems);
        }

        var codes = new HashSet<byte>();
        foreach (DefinedControl c in definition.Controls)
        {
            string at = $"control {c.Code}";
            if (c.CodeValue is not byte code) { problems.Add($"{at}: code must be written 0x00 to 0xFF"); continue; }
            if (!codes.Add(code)) problems.Add($"{at}: defined twice");
            if (string.IsNullOrWhiteSpace(c.Name)) problems.Add($"{at}: needs a name");
            Text(c.Name, $"{at} name", 80, problems);
            Text(c.Notes, $"{at} notes", 600, problems);
            if (!DefinedKinds.All.Contains(c.Kind)) problems.Add($"{at}: kind must be one of {string.Join(", ", DefinedKinds.All)}");
            if (!DefinedConfidence.All.Contains(c.Confidence)) problems.Add($"{at}: confidence must be one of {string.Join(", ", DefinedConfidence.All)}");
            if (c.Key is { } key && (key.Length == 0 || key != KeyFor(key))) problems.Add($"{at}: key '{key}' must be lower-case words joined by dashes");
            if (c.Writable && c.Kind == DefinedKinds.Information) problems.Add($"{at}: information cannot be writable");
            if (c.Writable && c.Kind == DefinedKinds.Choice && c.Values.Count == 0) problems.Add($"{at}: a writable choice must list its values");
            if (c.Maximum is < 0 or > 65535) problems.Add($"{at}: maximum must be 0 to 65535");
            var seen = new HashSet<uint>();
            foreach (DefinedValue v in c.Values)
            {
                if (v.Number is not uint n || n > 0xFFFF) problems.Add($"{at}: value '{v.Value}' must be a number, 0x0B or 11");
                else if (!seen.Add(n)) problems.Add($"{at}: value {v.Value} listed twice");
                if (string.IsNullOrWhiteSpace(v.Name)) problems.Add($"{at}: value {v.Value} needs a name");
                Text(v.Name, $"{at} value name", 60, problems);
            }
            foreach (string source in c.Sources)
                if (!IsModel(source)) problems.Add($"{at}: source '{source}' is not a model key such as DEL-A234");
        }
        return problems;
    }

    /// <summary>
    /// Free text is checked for what must never be published: paths carry an
    /// account name, and a long run of letters and digits is how a serial looks.
    /// </summary>
    private static void Text(string? text, string field, int limit, List<string> problems)
    {
        if (text is null) return;
        if (text.Length > limit) problems.Add($"{field} is longer than {limit} characters");
        if (text.Contains('\\') || text.Contains(":/") || PathPattern().IsMatch(text)) problems.Add($"{field} looks like a path");
        if (SerialPattern().IsMatch(text)) problems.Add($"{field} contains something shaped like a serial number");
    }

    [GeneratedRegex("^[A-Z]{3}(-[0-9A-F]{4})?$")]
    private static partial Regex TargetPattern();

    [GeneratedRegex(@"[A-Za-z]:[\\/]|/Users/|/home/", RegexOptions.IgnoreCase)]
    private static partial Regex PathPattern();

    // Mixed letters and digits, eight or more long: serials, tokens, instance ids.
    [GeneratedRegex(@"\b(?=[A-Z0-9]*\d)(?=[A-Z0-9]*[A-Z])[A-Z0-9]{8,}\b")]
    private static partial Regex SerialPattern();
}
