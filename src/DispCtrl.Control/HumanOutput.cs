using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace DispCtrl.Control;

/// <summary>
/// The command line's answers for a person at a terminal.
/// </summary>
/// <remarks>
/// Every command answers in the control API's JSON envelope, which is the
/// contract scripts rely on and stays exactly as it was whenever output is
/// piped or redirected, or <c>--json</c> is given. Printed to a terminal, the
/// same envelope read like a debugger's dump: <c>"ok": true</c> and a request
/// id around three lines of information. This renders the <c>data</c> part
/// instead - tables for lists, aligned lines for objects, a word for a done
/// action - and an error as one line on stderr. It is generic on purpose, so a
/// new command reads well without a formatter of its own.
/// </remarks>
internal static class HumanOutput
{
    private const int MaxColumns = 8, MaxCell = 42;

    public static void Write(JsonNode? result)
    {
        if (result is JsonObject envelope && envelope.ContainsKey("ok") && envelope.ContainsKey("exitCode"))
        {
            if (envelope["ok"]?.GetValue<bool>() != true)
            {
                Console.Error.WriteLine("error: " + (envelope["error"]?["message"]?.GetValue<string>() ?? "the command failed"));
                return;
            }
            result = envelope["data"];
        }

        var text = new StringBuilder();
        Render(result, text, 0);
        Console.Write(text.Length == 0 ? "Done." + Environment.NewLine : text.ToString());
    }

    private static void Render(JsonNode? node, StringBuilder text, int indent)
    {
        switch (node)
        {
            case null:
                break;
            case JsonArray array:
                RenderArray(array, text, indent);
                break;
            case JsonObject obj when obj.Count == 1 && obj["state"] is JsonValue state:
                text.Append(' ', indent).AppendLine(Sentence(Scalar(state)));
                break;
            case JsonObject obj:
                RenderObject(obj, text, indent);
                break;
            default:
                text.Append(' ', indent).AppendLine(Scalar(node));
                break;
        }
    }

    private static void RenderObject(JsonObject obj, StringBuilder text, int indent)
    {
        // Scalars first, aligned; then each list or nested object under a heading.
        // Empty fields say nothing to a reader; the JSON keeps them for scripts.
        // So is a revision hash, which only matters to a script comparing saves.
        var scalars = obj.Where(p => p.Value is JsonValue v && Scalar(v) != "-" && !Hidden.Contains(p.Key)).ToList();
        int width = scalars.Count == 0 ? 0 : scalars.Max(p => Words(p.Key).Length);
        foreach (var (key, value) in scalars)
            text.Append(' ', indent).Append(Words(key).PadRight(width)).Append("  ").AppendLine(Scalar(value));

        foreach (var (key, value) in obj.Where(p => p.Value is JsonArray or JsonObject))
        {
            if (value is JsonArray { Count: 0 }) continue;
            if (text.Length > 0) text.AppendLine();
            text.Append(' ', indent).AppendLine(Heading(key));
            Render(value, text, indent + 2);
        }
    }

    private static void RenderArray(JsonArray array, StringBuilder text, int indent)
    {
        if (array.Count == 0) { text.Append(' ', indent).AppendLine("(none)"); return; }
        if (!array.All(item => item is JsonObject))
        {
            foreach (JsonNode? item in array) text.Append(' ', indent).Append("- ").AppendLine(Scalar(item));
            return;
        }

        // Width and height read as one resolution column.
        var items = array.Cast<JsonObject>().Select(Merge).ToList();

        // Columns: the scalar fields in the order the rows give them, less the
        // ones empty in every row, with coordinates and the like last so the
        // column limit drops them first.
        var columns = new List<string>();
        foreach (JsonObject row in items.Take(20))
            foreach (var (key, value) in row)
                if (value is null or JsonValue && !columns.Contains(key)) columns.Add(key);
        columns = columns.Where(c => items.Any(r => Scalar(r[c]) != "-"))
            .OrderBy(c => Later.Contains(c) ? 1 : 0).Take(MaxColumns).ToList();

        var rows = items.Select(r => columns.Select(c => Cell(r[c])).ToArray()).ToList();
        int[] widths = columns.Select((c, i) => Math.Max(Words(c).Length, rows.Max(r => r[i].Length))).ToArray();

        text.Append(' ', indent).AppendLine(string.Join("  ", columns.Select((c, i) => Words(c).ToUpperInvariant().PadRight(widths[i]))).TrimEnd());
        foreach (string[] row in rows)
            text.Append(' ', indent).AppendLine(string.Join("  ", row.Select((v, i) => v.PadRight(widths[i]))).TrimEnd());
    }

    private static readonly HashSet<string> Hidden = new(StringComparer.Ordinal) { "revision", "settingsRevision" };

    private static readonly HashSet<string> Later = new(StringComparer.Ordinal)
        { "x", "y", "physicalWidthMm", "physicalHeightMm", "firstSeen", "id", "alias", "history" };

    private static JsonObject Merge(JsonObject row)
    {
        if (row["width"] is not JsonValue w || row["height"] is not JsonValue h) return row;
        var merged = new JsonObject();
        foreach (var (key, value) in row)
        {
            if (key == "height") continue;
            if (key == "width") { merged["resolution"] = Scalar(w) + "x" + Scalar(h); continue; }
            merged[key] = value?.DeepClone();
        }
        return merged;
    }

    private static string Cell(JsonNode? node)
    {
        string s = Scalar(node);
        return s.Length > MaxCell ? s[..(MaxCell - 1)] + "…" : s;
    }

    private static string Scalar(JsonNode? node)
    {
        if (node is null) return "-";
        if (node is not JsonValue value) return node.ToJsonString();
        if (value.TryGetValue(out bool b)) return b ? "yes" : "no";
        if (value.TryGetValue(out string? s))
        {
            // ISO timestamps, as the API writes them, read better local and short.
            if (s.Length >= 19 && s[4] == '-' && s[10] == 'T'
                && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset when))
                return when.Year < 1900 ? "-" : when.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            return s.Length == 0 ? "-" : s;
        }
        return value.ToJsonString();
    }

    /// <summary>"refreshHz" to "refresh hz".</summary>
    private static string Words(string key)
    {
        var sb = new StringBuilder(key.Length + 4);
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            if (char.IsUpper(c) && i > 0 && !char.IsUpper(key[i - 1])) sb.Append(' ');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static string Heading(string key)
    {
        string w = Words(key);
        return char.ToUpperInvariant(w[0]) + w[1..];
    }

    private static string Sentence(string state) => state switch
    {
        "requested" => "Requested.",
        "saved" => "Saved.",
        "validated" => "Valid. Nothing was changed.",
        "planned" => "Planned. Nothing was changed.",
        _ => char.ToUpperInvariant(state[0]) + state[1..] + ".",
    };
}
