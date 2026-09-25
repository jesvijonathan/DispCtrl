using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using DispCtrl.Core.Settings;

namespace DispCtrl.Control;

/// <summary>Validated, source-generated access to the complete public settings tree.</summary>
public static class SettingsDocument
{
    private static readonly SettingsJsonContext Strict = new(new JsonSerializerOptions(SettingsJsonContext.Default.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    });

    public static JsonObject Read() => Encode(SettingsStore.Load());
    public static JsonObject Encode(DispCtrlSettings settings) =>
        JsonSerializer.SerializeToNode(settings, Strict.DispCtrlSettings)!.AsObject();
    public static JsonNode Schema() => Strict.DispCtrlSettings.GetJsonSchemaAsNode();
    public static string Revision(JsonNode node) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString())));

    public static DispCtrlSettings Validate(JsonNode document)
    {
        var settings = document.Deserialize(Strict.DispCtrlSettings) ?? throw new ArgumentException("Settings cannot be null.");
        if (settings.Version != 1) throw new ArgumentException("Unsupported settings version.");
        if (settings.Global is null || settings.Monitors is null || settings.Hotkeys is null || settings.AppRules is null)
            throw new ArgumentException("Settings sections cannot be null.");
        var g = settings.Global;
        if (g.Focus is null || g.OledCare is null || g.Awake is null || g.NightLight is null || g.QuickPanel is null
            || g.Pin is null || g.Placement is null || g.DdcGuard is null || g.Updates is null)
            throw new ArgumentException("Global settings sections cannot be null.");
        if (g.Updates.LatestVersion is null || g.Updates.LatestUrl is null || g.Updates.SkippedVersion is null)
            throw new ArgumentException("Update settings cannot be null.");
        if (g.Pin.BorderColour is null || g.Pin.ExcludedApps is null || g.Placement.ExcludedApps is null || g.OledCare.ExcludedApps is null)
            throw new ArgumentException("Pin and placement text settings cannot be null.");
        string colour = g.Pin.BorderColour.Trim();
        if (colour.Length > 0 && !(colour.Length == 7 && colour[0] == '#'
                && uint.TryParse(colour.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _)))
            throw new ArgumentException("The pin border colour is #RRGGBB, or empty for Windows' accent colour.");
        if (!Enum.IsDefined(g.Placement.Active)) throw new ArgumentException("Unknown active display: pointer or activeWindow.");
        if (g.DdcGuard.Blocked is null || g.DdcGuard.Blocked.Any(b => b is null || string.IsNullOrWhiteSpace(b.Token)
                || b.Model is null || b.Label is null || b.Reason is null))
            throw new ArgumentException("Invalid DDC/CI guard entry.");
        if (!Enum.IsDefined(g.Awake.Mode)) throw new ArgumentException("Unknown awake mode.");
        if (g.BeforeRestore is { } restored && (restored.Monitors is null || restored.Monitors.Any(p => p.Value is null || string.IsNullOrWhiteSpace(p.Key))))
            throw new ArgumentException("Invalid record of what the way back switched off.");
        if (settings.Hotkeys.Any(x => x is null) || settings.AppRules.Any(x => x is null))
            throw new ArgumentException("Rules and hotkeys cannot contain null entries.");
        var panel = g.QuickPanel;
        if (!Enum.IsDefined(panel.Density) || !Enum.IsDefined(panel.Icon)) throw new ArgumentException("Unknown quick-panel density or icon.");
        if (!Enum.IsDefined(panel.TrayWheel)) throw new ArgumentException("Tray wheel: off, main or all.");
        if (panel.Sections is null || panel.Tiles is null || panel.DisplayRows is null || panel.DisplayTiles is null
            || panel.CustomTiles is null || panel.Collapsed is null || panel.Expanded is null || panel.HiddenDisplays is null)
            throw new ArgumentException("Quick-panel lists cannot be null.");
        foreach (var list in new[] { panel.Sections, panel.Tiles, panel.DisplayRows, panel.DisplayTiles })
            if (list.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id)) || list.Select(item => item.Id).Distinct().Count() != list.Count)
                throw new ArgumentException("Quick-panel entries need unique non-empty IDs.");
        if (panel.CustomTiles.Any(t => t is null || !Enum.IsDefined(t.Kind) || string.IsNullOrWhiteSpace(t.Id)
                || t.Label is null || t.Target is null || t.Arguments is null || t.Glyph is null)
            || panel.Collapsed.Any(t => t is null) || panel.Expanded.Any(t => t is null) || panel.HiddenDisplays.Any(t => t is null))
            throw new ArgumentException("Invalid quick-panel entry.");
        if (panel.Width < QuickPanelSettings.MinWidth || panel.Width > QuickPanelSettings.MaxWidth
            || panel.Height < QuickPanelSettings.MinHeight || panel.Height > QuickPanelSettings.MaxHeight
            || panel.TileColumns < QuickPanelSettings.MinColumns || panel.TileColumns > QuickPanelSettings.MaxColumns)
            throw new ArgumentException("Quick-panel dimensions are outside the supported range.");
        if (settings.Hotkeys.Any(h => !Enum.IsDefined(h.Action) || h.Key > 255 || h.Modifiers > 15 || h.Display < 0 || h.Step is < 1 or > 100))
            throw new ArgumentException("Invalid hotkey action, key, modifiers, target or step.");
        if (settings.AppRules.Any(r => r.Process is null || r.Preset is null || !double.IsFinite(r.DwellSeconds) || r.DwellSeconds < 0))
            throw new ArgumentException("Invalid application rule.");
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in settings.Monitors)
        {
            if (pair.Value is null || string.IsNullOrWhiteSpace(pair.Key)) throw new ArgumentException("Invalid monitor entry.");
            if (pair.Value.ProbedCodes is { } codes && codes.Any(c => c is null || c.Length != 2
                    || !byte.TryParse(c, System.Globalization.NumberStyles.HexNumber, null, out _)))
                throw new ArgumentException("Probed codes are two hex digits each, as \"10\" or \"60\".");
            string alias = pair.Value.Alias;
            if (alias is null || alias.Length > 64 || alias.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_')))
                throw new ArgumentException("Monitor aliases use up to 64 letters, digits, dashes or underscores.");
            if (alias.Length > 0 && (!aliases.Add(alias) || int.TryParse(alias, out _) || alias.Equals("all", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Monitor aliases must be unique and cannot be display numbers or 'all'.");
        }
        ValidateRanges(document, "");
        return settings;
    }

    private static void ValidateRanges(JsonNode? node, string path)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                string childPath = path + "/" + pair.Key;
                if (pair.Value is JsonValue value && value.TryGetValue<int>(out int number))
                {
                    (int min, int max)? range = pair.Key switch
                    {
                        "dimPercent" or "secondStageDimPercent" or "taskbarOpacity" or "taskbarGlassRadius" or "taskbarGlassTint"
                            or "unisonLevel" or "strength" => (0, 100),
                        "brightnessBaseline" or "brightnessFloor" or "brightnessCeiling" or "nightLightFloor" or "nightLightCeiling" => (-1, 100),
                        "softwareBrightness" => (10, 100),
                        "nightLightStrength" => (-1, 100),
                        "idleMinutes" or "secondStageMinutes" => (1, 120),
                        "oledRestMinutes" => (1, 30),
                        "monitorSleepMinutes" => (1, 240),
                        "fadeMs" => (0, 2000),
                        "delayMs" => (0, 10000),
                        "idlePollMs" or "farPollMs" or "armedPollMs" or "shownPollMs" => (1, 60000),
                        "fromMinutes" or "toMinutes" => (0, 1439),
                        "darkLevel" or "brightLevel" => (0, 100),
                        "darkLux" => (0, 100000),
                        "brightLux" => (1, 100000),
                        "intervalHours" => (0, 168),
                        "intervalMinutes" => (0, 59),
                        "hideDelayMs" or "animMs" or "revealPx" or "armDistancePx" => (0, 60000),
                        "borderThickness" => (1, 16),
                        "borderOpacity" => (20, 100),
                        "wheelStep" => (QuickPanelSettings.MinWheelStep, QuickPanelSettings.MaxWheelStep),
                        _ => null,
                    };
                    if (range is { } r && (number < r.min || number > r.max))
                        throw new ArgumentException($"{childPath} must be between {r.min} and {r.max}.");
                }
                ValidateRanges(pair.Value, childPath);
            }
        }
        else if (node is JsonArray array)
            for (int i = 0; i < array.Count; i++) ValidateRanges(array[i], path + "/" + i);
    }

    public static string[] Segments(string path) => path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();

    public static JsonNode? Get(JsonNode document, string path)
    {
        JsonNode? node = document;
        foreach (string segment in Segments(path))
        {
            if (node is JsonObject obj && obj.TryGetPropertyValue(segment, out var next)) node = next;
            else if (node is JsonArray list && int.TryParse(segment, out int i) && i >= 0 && i < list.Count) node = list[i];
            else throw new ArgumentException($"Unknown settings path: {path}. Use settings get or settings schema.");
        }
        return node;
    }

    public static void Set(JsonObject document, string path, JsonNode? value)
    {
        string[] parts = Segments(path);
        if (parts.Length == 0 || parts[0] == "version") throw new ArgumentException("Choose a setting path; version is read-only.");
        JsonNode? parent = parts.Length == 1 ? document : Get(document, "/" + string.Join('/', parts[..^1].Select(p => p.Replace("~", "~0").Replace("/", "~1"))));
        if (parent is JsonObject obj && obj.ContainsKey(parts[^1])) obj[parts[^1]] = value?.DeepClone();
        else if (parent is JsonArray array && int.TryParse(parts[^1], out int index) && index >= 0 && index < array.Count)
            array[index] = value?.DeepClone();
        else throw new ArgumentException($"Unknown settings path: {path}.");
        Validate(document);
    }

    public static JsonObject Update(Action<JsonObject> change, bool dryRun, string? expectedRevision = null) => SettingsStore.WithWriteLock(() =>
    {
        JsonObject before = Read();
        if (expectedRevision is not null && !string.Equals(Revision(before), expectedRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Settings changed since they were read. Read them again before applying this edit.");
        JsonObject after = (JsonObject)before.DeepClone();
        change(after);
        DispCtrlSettings parsed = Validate(after);
        if (!dryRun) SettingsStore.Save(parsed);
        return new JsonObject { ["state"] = dryRun ? "validated" : "saved", ["revision"] = Revision(after), ["settings"] = after };
    });
}
