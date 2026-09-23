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
/// <param name="Url">The prefilled issue, or the plain form when the body is too long for a link.</param>
/// <param name="Prefilled">False when the body must be pasted by hand.</param>
public readonly record struct MappingShare(string Model, string Name, string Body, string Path, Uri Url, bool Prefilled);

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
        if (attached is not null)
        {
            bool? isOled = null;
            try { isOled = Core.Settings.SettingsStore.Load().For(attached.Token).IsOled; } catch (Exception) { }
            sb.AppendLine(DeviceSubmission.Build(attached, isOled).ToRepositoryMarkdown());
        }
        else
        {
            sb.AppendLine($"### {name} ({model})");
            sb.AppendLine();
            sb.AppendLine("Shared from this machine's history; the monitor was not attached.");
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

        sb.AppendLine();
        sb.AppendLine("### Mappings");
        sb.AppendLine();
        sb.AppendLine("Read by the intake workflow. Edit the names and notes if you like; keep it valid JSON.");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine("```");

        string body = Redact.Ascii(Redact.Scrub(sb.ToString(), Identifiers(null))).Replace("\r\n", "\n", StringComparison.Ordinal);

        string path = "";
        try
        {
            Directory.CreateDirectory(Outbox);
            path = System.IO.Path.Combine(Outbox, $"{Safe(model)}.md");
            File.WriteAllText(path, body);
        }
        catch (Exception) { }

        string title = WebUtility.UrlEncode($"Device mapping: {name} ({model})");
        string labels = WebUtility.UrlEncode($"device,{MappingLabel}");
        string prefilled = $"https://github.com/{Repository}/issues/new?title={title}&labels={labels}&body={WebUtility.UrlEncode(body)}";
        return prefilled.Length <= MaxUrlLength
            ? new MappingShare(model, name, body, path, new Uri(prefilled), true)
            : new MappingShare(model, name, body, path,
                new Uri($"https://github.com/{Repository}/issues/new?title={title}&labels={labels}"), false);
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
