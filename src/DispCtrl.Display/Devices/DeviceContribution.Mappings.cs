using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DispCtrl.Core.Devices;
using DispCtrl.Core.Displays;

namespace DispCtrl.Display.Devices;

/// <summary>A model's record and mappings, ready for the person to publish.</summary>
/// <param name="Body">The scrubbed issue body, exactly as it would be published.</param>
/// <param name="Path">Where the same text was saved locally.</param>
/// <param name="Url">The issue, prefilled with as much of the body as a link can carry.</param>
/// <param name="Prefilled">True when the whole body is in the link.</param>
/// <param name="Paste">What did not fit, for the clipboard; null when nothing is left to paste.</param>
public readonly record struct MappingShare(string Model, string Name, string Body, string Path, Uri Url, bool Prefilled, string? Paste);

public static partial class DeviceContribution
{
    /// <summary>The label the intake workflow turns into a pull request.</summary>
    public const string MappingLabel = "device-mapping";

    /// <summary>Where shares are written before anything is opened.</summary>
    public static string Outbox => System.IO.Path.Combine(Folder, "outbox");

    /// <summary>
    /// One share for one model: its record, what this machine has mapped for
    /// it, and what its unnamed codes have been seen to do.
    /// </summary>
    /// <remarks>
    /// Replaces collect, then view, then submit. Everything it needs is already
    /// local - the record is rebuilt when the monitor is attached, the history
    /// holds the rest - so there is nothing to collect first, and a monitor
    /// unplugged since can still be shared.
    /// <para>
    /// The body is Markdown for a person and one fenced JSON block for the
    /// intake workflow, which validates it and opens a pull request. Observed
    /// values are included for codes nobody has named yet, because they are
    /// what a mapping is worked out from; for the standard codes they would only
    /// describe somebody's evening, and are left out. The whole body goes through
    /// the same scrub and ASCII fold as every other record.
    /// </para>
    /// </remarks>
    public static MappingShare PrepareMapping(string model, DisplayInfo? attached = null)
    {
        if (!DeviceDefinitions.IsModel(model)) throw new ArgumentException($"'{model}' is not a model key such as DEL-A234.");
        DeviceHistory history = DeviceHistory.Load();
        history.Models.TryGetValue(model, out SeenModel? seen);
        string name = seen?.Name ?? attached?.Label ?? model;

        var sb = new StringBuilder();
        if (seen?.Record is { Length: > 0 } recordOnDisk)
        {
            sb.AppendLine(recordOnDisk);
        }
        else
        {
            sb.AppendLine($"### {name} ({model})");
            sb.AppendLine();
            if (seen is not null)
            {
                sb.AppendLine($"Connection: {seen.Connector}. {(seen.BuiltIn ? "Built-in panel." : "External monitor.")}");
                if (seen.WidthMm > 0 && seen.HeightMm > 0) sb.AppendLine($"Physical size: {seen.WidthMm} x {seen.HeightMm} mm.");
            }
            sb.AppendLine("Shared from saved discovery. A full model record has not been collected yet; the known codes and mappings follow.");
        }

        var payload = new JsonObject
        {
            ["schema"] = 1,
            ["kind"] = "dispctrl-device-mapping",
            ["model"] = model,
            ["name"] = name,
            ["definitions"] = new JsonArray(DeviceLibrary.LocalFor(model)
                .Select(d => JsonNode.Parse(JsonSerializer.Serialize(d, DeviceJsonContext.Default.DeviceDefinition)))
                .ToArray()),
            ["observed"] = Observed(model, seen),
        };

        string record = Redact.Ascii(Redact.Scrub(sb.ToString(), Identifiers(null))).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        string Mappings(JsonObject p) => Redact.Ascii(Redact.Scrub(
            "### Mappings\n\nRead by the intake workflow. Edit the names and notes if you like; keep it valid JSON.\n\n```json\n"
            + p.ToJsonString() + "\n```\n", Identifiers(null))).Replace("\r\n", "\n", StringComparison.Ordinal);
        string mappings = Mappings(payload);
        string body = record + "\n\n" + mappings;

        string path = "";
        try
        {
            Directory.CreateDirectory(Outbox);
            path = System.IO.Path.Combine(Outbox, $"{Safe(model)}.md");
            File.WriteAllText(path, body);
        }
        catch (Exception) { }

        string title = WebUtility.UrlEncode(Redact.Ascii(Redact.Scrub($"Device mapping: {name} ({model})", Identifiers(null))));
        string labels = WebUtility.UrlEncode($"device,{MappingLabel}");
        string Link(string text) => $"https://github.com/{Repository}/issues/new?title={title}&labels={labels}&body={WebUtility.UrlEncode(text)}";

        // The whole body rarely fits in a link: a monitor's record alone is
        // about 8,000 characters once percent-encoded. Sharing used to open an
        // empty form then, which looked exactly like the button doing nothing.
        // So the part the project needs - the model, the mappings and what each
        // code was seen to do - always goes in the link, compact, and only the
        // record, which describes the model rather than anyone's mapping, is
        // left for the clipboard. The intake takes a record only when it has
        // the "Device key" line, so this placeholder is never saved as one.
        if (Link(body).Length <= MaxUrlLength) return new MappingShare(model, name, body, path, new Uri(Link(body)), true, null);
        string placeholder = $"### {name} ({model})\n\n_DispCtrl copied this model's full record to the clipboard, because it is too long for a link. Paste it here, in place of this line._\n\n";
        if (Link(placeholder + mappings).Length <= MaxUrlLength)
            return new MappingShare(model, name, body, path, new Uri(Link(placeholder + mappings)), false, record);

        // Keep one JSON block per model. Prefilling a reduced block and asking
        // for the complete body to be pasted used to leave two conflicting ones.
        return new MappingShare(model, name, body, path,
            new Uri(Link("Paste the complete device share copied by DispCtrl here, replacing this line.")), false, body);
    }

    /// <summary>Combines every model in the device list into one reviewable issue.</summary>
    public static MappingShare PrepareMappings(IReadOnlyList<DisplayInfo> attached)
    {
        string[] models = DeviceHistory.Load().Models.Keys.Order(StringComparer.Ordinal).ToArray();
        if (models.Length == 0) throw new ArgumentException("No devices have been recorded yet.");
        var shares = models.Select(model => PrepareMapping(model,
            attached.FirstOrDefault(d => d.Key.Model.Equals(model, StringComparison.OrdinalIgnoreCase)))).ToArray();
        return CombineMappings(shares);
    }

    /// <summary>Fits a set of complete model shares into one link without discarding any model.</summary>
    public static MappingShare CombineMappings(IReadOnlyList<MappingShare> shares)
    {
        if (shares.Count == 0) throw new ArgumentException("No devices to share.");
        string body = string.Join("\n\n---\n\n", shares.Select(s => s.Body.Trim()));
        string title = $"Device library: {shares.Count} model(s)";
        string summary = "Devices: " + string.Join(", ", shares.Select(s => s.Model))
            + "\n\nPaste the complete device share copied by DispCtrl here, replacing this text.";
        string Link(string text) => $"https://github.com/{Repository}/issues/new?labels=device,{MappingLabel}"
            + $"&title={WebUtility.UrlEncode(title)}&body={WebUtility.UrlEncode(text)}";
        bool fits = Link(body).Length <= MaxUrlLength;
        if (Link(summary).Length > MaxUrlLength) summary = "Paste the complete device share copied by DispCtrl here.";
        string path = System.IO.Path.Combine(Outbox, "all-devices.md");
        Directory.CreateDirectory(Outbox);
        File.WriteAllText(path, body);
        return new MappingShare("all", title, body, path, new Uri(Link(fits ? body : summary)), fits, fits ? null : body);
    }

    /// <summary>What the history knows about a model's codes, without anybody's settings.</summary>
    private static JsonObject Observed(string model, SeenModel? seen)
    {
        var codes = new JsonObject();
        Dictionary<byte, ResolvedControl> known = DeviceLibrary.Resolve(model);
        foreach (var (code, c) in seen?.Codes ?? [])
        {
            byte? value = DeviceDefinitions.ParseCode(code);
            bool standard = value is byte v && VcpControl.IsAllowed(v);
            bool named = value is byte k && known.ContainsKey(k);
            var entry = new JsonObject
            {
                ["name"] = c.Name,
                ["kind"] = c.Kind,
                ["listed"] = new JsonArray(c.ListedValues.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            };
            // A standard control's values are a person's settings; an unnamed
            // code's values are the evidence a mapping is made from.
            if (!standard && !named)
                entry["observed"] = new JsonArray(c.Observed.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
            codes[code] = entry;
        }
        return new JsonObject { ["capabilities"] = seen?.Capabilities, ["codes"] = codes };
    }

    /// <summary>Puts text on the clipboard from a process that may have no window.</summary>
    public static bool CopyToClipboard(string text)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("clip.exe")
            {
                RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true,
            });
            if (p is null) return false;
            p.StandardInput.Write(text);
            p.StandardInput.Close();
            return p.WaitForExit(5000) && p.ExitCode == 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Opens a prepared share in the browser.</summary>
    public static bool Open(MappingShare share)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(share.Url.ToString()) { UseShellExecute = true });
            return true;
        }
        catch (Exception) { return false; }
    }
}
