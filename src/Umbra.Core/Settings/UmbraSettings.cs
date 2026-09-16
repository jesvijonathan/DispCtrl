using System.Text.Json.Serialization;

namespace Umbra.Core.Settings;

/// <summary>Root of the on-disk configuration.</summary>
public sealed class UmbraSettings
{
    /// <summary>Schema version, so a future format change can migrate rather than reset.</summary>
    public int Version { get; set; } = 1;

    public GlobalSettings Global { get; set; } = new();

    /// <summary>
    /// Per-monitor settings keyed on <c>DisplayKey.ToToken()</c>.
    /// </summary>
    /// <remarks>
    /// Keyed on the panel's own identity rather than <c>\\.\DISPLAY1</c>, so
    /// settings follow the physical monitor across replugs and reorders
    /// instead of landing on whatever display happens to hold that slot.
    /// </remarks>
    public Dictionary<string, MonitorSettings> Monitors { get; set; } = [];

    /// <summary>Settings for a monitor, creating defaults on first sight.</summary>
    public MonitorSettings For(string token)
    {
        if (Monitors.TryGetValue(token, out MonitorSettings? s)) return s;
        s = new MonitorSettings();
        Monitors[token] = s;
        return s;
    }
}

public sealed class GlobalSettings
{
    /// <summary>How long the bar stays out after the cursor leaves.</summary>
    public int HideDelayMs { get; set; } = 350;

    /// <summary>Slide duration. 0 restores an instant snap.</summary>
    public int AnimMs { get; set; } = 180;

    /// <summary>How close to the screen edge the cursor must get to reveal.</summary>
    public int RevealPx { get; set; } = 2;

    /// <summary>
    /// Distance from a managed edge at which polling speeds up.
    /// </summary>
    /// <remarks>
    /// The whole point of the adaptive loop: far from every managed edge there
    /// is nothing to react to, so the engine idles. Too small a value and a
    /// fast cursor crosses the band between two idle polls, adding latency to
    /// the reveal; too large and it is always in the fast path.
    /// </remarks>
    public int ArmDistancePx { get; set; } = 300;

    /// <summary>Poll interval when no managed edge is near the cursor.</summary>
    public int IdlePollMs { get; set; } = 100;

    /// <summary>
    /// Longest the engine will ever wait between cursor checks.
    /// </summary>
    /// <remarks>
    /// Reached when the cursor is nowhere near a managed edge — on another
    /// monitor, typically — which is the overwhelmingly common case. Raising it
    /// cuts idle wake-ups proportionally, at the cost of up to this much extra
    /// latency on a very fast flick to the edge from far away.
    /// </remarks>
    public int FarPollMs { get; set; } = 500;

    /// <summary>Poll interval inside the armed band.</summary>
    public int ArmedPollMs { get; set; } = 16;

    /// <summary>Poll interval while a bar is revealed.</summary>
    public int ShownPollMs { get; set; } = 40;

    /// <summary>Write a rolling log next to the settings file.</summary>
    public bool Logging { get; set; } = true;

    /// <summary>Drive every display's brightness from one relative control.</summary>
    public bool UnisonBrightness { get; set; }

    /// <summary>
    /// The unison level, as a percentage of each display's own baseline.
    /// </summary>
    /// <remarks>
    /// A multiplier, not an absolute brightness. 100 means every display sits
    /// at the level its user chose; 50 means each sits at half of its own
    /// level. That is what keeps the control meaningful across panels with
    /// wildly different peak brightness — an OLED at 40% and an external at 80%
    /// stay in the same relation to each other as they dim together.
    /// </remarks>
    public int UnisonLevel { get; set; } = 100;

    /// <summary>
    /// Restores the shipped defaults, leaving per-monitor settings alone.
    /// </summary>
    public void ResetToDefaults()
    {
        var fresh = new GlobalSettings();

        HideDelayMs = fresh.HideDelayMs;
        AnimMs = fresh.AnimMs;
        RevealPx = fresh.RevealPx;
        ArmDistancePx = fresh.ArmDistancePx;
        IdlePollMs = fresh.IdlePollMs;
        FarPollMs = fresh.FarPollMs;
        ArmedPollMs = fresh.ArmedPollMs;
        ShownPollMs = fresh.ShownPollMs;
        Logging = fresh.Logging;
        UnisonBrightness = fresh.UnisonBrightness;
        UnisonLevel = fresh.UnisonLevel;
    }
}

/// <summary>Everything Umbra can do to one monitor.</summary>
public sealed class MonitorSettings
{
    /// <summary>
    /// Last known friendly name. Purely so the settings file is readable —
    /// never used for matching, since names are not unique.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>Hide this monitor's taskbar, revealing it on cursor approach.</summary>
    public bool HideTaskbar { get; set; }

    /// <summary>
    /// Expand this monitor's work area to the full panel once its bar is
    /// hidden, so maximized windows fill the screen.
    /// </summary>
    public bool ReclaimWorkArea { get; set; } = true;

    /// <summary>
    /// The brightness this display sits at when unison is at 100%.
    /// </summary>
    /// <remarks>
    /// Captured when unison is switched on, so the relative relationship
    /// between panels is the one the user had already dialled in. -1 means
    /// nothing has been captured yet.
    /// </remarks>
    public int BrightnessBaseline { get; set; } = -1;

    [JsonIgnore]
    public bool ManagesTaskbar => HideTaskbar;

    /// <summary>Restores this monitor's shipped defaults.</summary>
    public void ResetToDefaults()
    {
        var fresh = new MonitorSettings();

        HideTaskbar = fresh.HideTaskbar;
        ReclaimWorkArea = fresh.ReclaimWorkArea;
        BrightnessBaseline = fresh.BrightnessBaseline;
        // Label is descriptive, not a setting; keeping it leaves the file readable.
    }
}
