using System.Text.Json.Serialization;

namespace DisplCtrl.Core.Settings;

/// <summary>Root of the on-disk configuration.</summary>
public sealed class DisplCtrlSettings
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

    /// <summary>
    /// Rules that switch presets when an app takes the foreground.
    /// </summary>
    /// <remarks>
    /// Kept in settings rather than in a preset, because a rule is about
    /// <em>when</em> to use a preset, not part of what the preset is. Putting
    /// them inside presets would mean exporting a preset also exported the
    /// user's app list, which is both surprising and a small privacy leak.
    /// </remarks>
    public List<AppRule> AppRules { get; set; } = [];

    /// <summary>
    /// Global keyboard shortcuts, registered by the engine.
    /// </summary>
    /// <remarks>
    /// Global rather than per-preset: a shortcut is how you reach a thing, not
    /// part of what the thing is, and one that changed meaning depending on the
    /// active preset would be worse than no shortcut.
    /// </remarks>
    public List<Hotkey> Hotkeys { get; set; } = [];

    /// <summary>Settings for a monitor, creating defaults on first sight.</summary>
    public MonitorSettings For(string token)
    {
        if (Monitors.TryGetValue(token, out MonitorSettings? s)) return s;
        s = new MonitorSettings();
        Monitors[token] = s;
        return s;
    }

    /// <summary>
    /// The warmth one display should actually be at, resolving unison,
    /// calibration and per-monitor overrides against each other.
    /// </summary>
    /// <remarks>
    /// Lives here rather than in either consumer because the engine and the
    /// panel both need the answer and must not disagree about it. Two copies of
    /// this rule drifting apart would show up as the screen not matching the
    /// slider, which is close to impossible to attribute.
    /// </remarks>
    /// <summary>
    /// Software brightness for one display, or 100 when it is not dimmed.
    /// </summary>
    /// <remarks>
    /// Lives beside <see cref="NightLightStrengthFor"/> because the two end up
    /// in the same gamma ramp and the engine has to resolve both together.
    /// </remarks>
    public int SoftwareBrightnessFor(string token) =>
        Math.Clamp(For(token).SoftwareBrightness, 10, 100);

    public int NightLightStrengthFor(string token)
    {
        NightLightSettings n = Global.NightLight;
        if (!n.Enabled) return 0;

        MonitorSettings m = For(token);

        // Per-display mode: the display's own number, falling back to the
        // shared one until it has been given a value of its own.
        if (!n.Unison)
            return Math.Clamp(m.NightLightStrength >= 0 ? m.NightLightStrength : n.Strength, 0, 100);

        // Unison, calibrated: the shared slider runs between this panel's own
        // captured limits, so the same position looks alike on panels that
        // render warmth very differently.
        if (n.Calibrated && m.HasNightLightRange)
        {
            double t = Math.Clamp(n.Strength, 0, 100) / 100.0;
            return Math.Clamp(
                m.NightLightFloor + (int)Math.Round((m.NightLightCeiling - m.NightLightFloor) * t),
                0, 100);
        }

        return Math.Clamp(n.Strength, 0, 100);
    }
}

public sealed class GlobalSettings
{
    public FocusSettings Focus { get; set; } = new();
    public OledCareSettings OledCare { get; set; } = new();

    /// <summary>The tray icon and what its panel shows.</summary>
    public QuickPanelSettings QuickPanel { get; set; } = new();
    /// <summary>Whole taskbar opacity, including icons. 100 leaves Explorer untouched.</summary>
    public int TaskbarOpacity { get; set; } = 100;
    /// <summary>Use Explorer's compositor-backed XAML taskbar blur.</summary>
    public bool TaskbarGlassEnabled { get; set; }
    /// <summary>Gaussian blur radius in XAML/compositor pixels.</summary>
    public int TaskbarGlassRadius { get; set; } = 36;
    /// <summary>Dark acrylic tint opacity applied after blur.</summary>
    public int TaskbarGlassTint { get; set; } = 8;
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
    /// Drive unison from a calibrated low/high limit per display instead of a
    /// multiplier on one captured level.
    /// </summary>
    /// <remarks>
    /// A multiplier is only honest near the level it was captured at: halving
    /// an OLED already at 30% takes it somewhere unusable, while halving an
    /// external at 90% is still bright. Calibration asks for each panel's own
    /// dimmest and brightest acceptable level once, and then the slider runs
    /// between them — so 0% and 100% mean the same thing on every panel even
    /// though the numbers behind them differ.
    /// </remarks>
    public bool UnisonCalibrated { get; set; }

    /// <summary>Warmth applied to every display together.</summary>
    public NightLightSettings NightLight { get; set; } = new();

    /// <summary>
    /// Restores the shipped defaults, leaving per-monitor settings alone.
    /// </summary>
    public void ResetToDefaults()
    {
        var fresh = new GlobalSettings();

        Focus = new();
        OledCare = new();
        TaskbarOpacity = fresh.TaskbarOpacity;
        TaskbarGlassEnabled = fresh.TaskbarGlassEnabled;
        TaskbarGlassRadius = fresh.TaskbarGlassRadius;
        TaskbarGlassTint = fresh.TaskbarGlassTint;

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
        UnisonCalibrated = fresh.UnisonCalibrated;
        NightLight = new NightLightSettings();
    }
}

/// <summary>
/// Warmth applied across every display at once.
/// </summary>
/// <remarks>
/// Global rather than per-monitor on purpose. The complaint night light
/// answers is that the desk is too blue at night, and warming one screen while
/// the other stays cold is worse than warming neither — the mismatch is more
/// noticeable than the blue was.
/// </remarks>
public sealed class NightLightSettings
{
    /// <summary>Warm the displays, subject to <see cref="Scheduled"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>How warm, 0-100. See <c>NightLight.KelvinFor</c> for the range.</summary>
    public int Strength { get; set; } = 45;

    /// <summary>
    /// Drive every display from <see cref="Strength"/> rather than each from
    /// its own.
    /// </summary>
    /// <remarks>
    /// On by default, because a desk with one screen warm and the other cold is
    /// worse than one with neither warmed — the mismatch is more distracting
    /// than the blue was. Per-display exists for the case where the panels
    /// genuinely need different numbers to look the same.
    /// </remarks>
    public bool Unison { get; set; } = true;

    /// <summary>
    /// Run the unison slider between each display's captured warmth limits.
    /// </summary>
    /// <remarks>
    /// The same idea as calibrated brightness, for the same reason: an OLED and
    /// an IPS panel do not look equally warm at equal numbers, so one slider
    /// position has to mean different numbers on each to look like one desk.
    /// </remarks>
    public bool Calibrated { get; set; }

    /// <summary>
    /// Keep this toggle and Windows' own night light as one setting.
    /// </summary>
    /// <remarks>
    /// With this on, the two are the same switch: turning night light on here
    /// turns Windows' on, and using Windows' own toggle — in Settings or the
    /// Quick Settings flyout — moves this one. The strength travels with it.
    /// <para>
    /// Windows then does the warming, and DisplCtrl stops writing a warm ramp of
    /// its own. That is not a detail: a display has one gamma ramp, and two
    /// things warming it at once is both twice as orange as either asked for and
    /// the exact arrangement that once left the screens permanently tinted.
    /// Delegating means there is only ever one writer.
    /// </para>
    /// <para>
    /// The cost is the warmth DisplCtrl can reach that Windows cannot: per-display
    /// warmth, calibrated warmth, and anything below Windows' own floor of
    /// 1200K. Switch this off to get those back and have DisplCtrl own the ramp
    /// again.
    /// </para>
    /// </remarks>
    public bool FollowWindows { get; set; } = true;

    /// <summary>Only warm between <see cref="FromMinutes"/> and <see cref="ToMinutes"/>.</summary>
    /// <remarks>
    /// Deliberately global, and stays global even in per-display mode: a
    /// schedule is about the time of day, which is the one thing every display
    /// on the desk genuinely shares.
    /// </remarks>
    public bool Scheduled { get; set; }

    /// <summary>Start of the warm period, in minutes past local midnight.</summary>
    public int FromMinutes { get; set; } = 20 * 60;

    /// <summary>End of the warm period, in minutes past local midnight.</summary>
    public int ToMinutes { get; set; } = 7 * 60;

    /// <summary>
    /// True when the warm period is in force at <paramref name="now"/>.
    /// </summary>
    /// <remarks>
    /// Handles the normal case of a window that crosses midnight, which is what
    /// nearly every night-light schedule does. Equal endpoints mean a window of
    /// no length, not one of a full day.
    /// </remarks>
    public bool ActiveAt(DateTime now)
    {
        if (!Enabled) return false;
        if (!Scheduled) return true;

        int from = Normalise(FromMinutes);
        int to = Normalise(ToMinutes);
        if (from == to) return false;

        int minute = (now.Hour * 60) + now.Minute;

        return from < to
            ? minute >= from && minute < to
            : minute >= from || minute < to;
    }

    private static int Normalise(int minutes) => ((minutes % 1440) + 1440) % 1440;
}

/// <summary>
/// "When this app is in front, use this preset."
/// </summary>
/// <remarks>
/// Matched on the executable name rather than the window title, because titles
/// change with whatever document is open and are localised, while the image
/// name is stable and is what a user can actually find in Task Manager.
/// </remarks>
public sealed class AppRule
{
    /// <summary>Executable name, with or without the extension. Case-insensitive.</summary>
    public string Process { get; set; } = "";

    /// <summary>The preset to apply while that app is in front.</summary>
    public string Preset { get; set; } = "";

    /// <summary>Whether this rule is live.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Preset to return to once the app is no longer in front. Blank leaves
    /// whatever the app's preset set in place.
    /// </summary>
    public string? RevertTo { get; set; }

    /// <summary>Restore an in-memory snapshot when leaving this app.</summary>
    public bool RestorePrevious { get; set; }

    /// <summary>Seconds the app must remain foreground before switching.</summary>
    public double DwellSeconds { get; set; } = 2;


    /// <summary>True when the rule has both halves filled in.</summary>
    [JsonIgnore]
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Process) && !string.IsNullOrWhiteSpace(Preset);

    /// <summary>True when <paramref name="imageName"/> is the app this rule is about.</summary>
    public bool Matches(string imageName)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(Process)) return false;

        string want = Process.Trim();
        if (want.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            want = want[..^4];

        return string.Equals(want, imageName, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Everything DisplCtrl can do to one monitor.</summary>
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
    /// Expand this monitor's work area to the full panel while its taskbar
    /// is managed. Revealing the bar overlays maximized windows without resizing them.
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

    /// <summary>
    /// The dimmest level this display should reach when unison is calibrated.
    /// -1 means it has not been captured.
    /// </summary>
    public int BrightnessFloor { get; set; } = -1;

    /// <summary>
    /// The brightest level this display should reach when unison is calibrated.
    /// -1 means it has not been captured.
    /// </summary>
    public int BrightnessCeiling { get; set; } = -1;

    /// <summary>
    /// True once both limits are captured and there is room between them.
    /// </summary>
    /// <remarks>
    /// An equal pair is rejected rather than accepted as a fixed level: it
    /// means the display was not touched between the two capture steps, which
    /// is a mistake far more often than an intention, and it would pin the
    /// panel at one brightness for every slider position.
    /// </remarks>
    [JsonIgnore]
    public bool HasBrightnessRange =>
        BrightnessFloor >= 0 && BrightnessCeiling > BrightnessFloor;

    /// <summary>
    /// This display's own warmth, 0-100. -1 means it has not been set and the
    /// shared value applies.
    /// </summary>
    public int NightLightStrength { get; set; } = -1;

    /// <summary>Least warmth this display should reach when unison is calibrated.</summary>
    public int NightLightFloor { get; set; } = -1;

    /// <summary>Most warmth this display should reach when unison is calibrated.</summary>
    public int NightLightCeiling { get; set; } = -1;

    [JsonIgnore]
    public bool HasNightLightRange =>
        NightLightFloor >= 0 && NightLightCeiling > NightLightFloor;

    /// <summary>
    /// Brightness for panels with no hardware control, 10-100.
    /// </summary>
    /// <remarks>
    /// Done in the gamma ramp, which is not the same thing as turning the
    /// backlight down: the panel still emits as much light, the signal is just
    /// scaled, so contrast and colour depth suffer as it goes down. It is the
    /// only option on a display that reports no brightness control at all, and
    /// saying so plainly is better than pretending it is the same control.
    /// <para>
    /// 100 means untouched. Never below <c>NightLight.MinimumDim</c>, because a
    /// black screen is not recoverable by looking at it.
    /// </para>
    /// </remarks>
    public int SoftwareBrightness { get; set; } = 100;

    /// <summary>
    /// Whether this panel is OLED, and where that was decided.
    /// </summary>
    /// <remarks>
    /// A setting rather than a fact, because nothing reliable reports it. EDID
    /// has no field for panel technology; an external monitor may say so over
    /// DDC/CI, but a built-in panel has no DDC/CI channel at all — which is
    /// exactly the case that matters, since built-in OLEDs are what burn in.
    /// <para>
    /// Null means "not decided": DisplCtrl will use whatever the monitor said, and
    /// the user can override it. Everything OLED-specific hangs off this, so it
    /// is better to ask than to guess wrong in either direction — burn-in
    /// protection on an LCD is a pointless annoyance, and its absence on an
    /// OLED is permanent damage.
    /// </para>
    /// </remarks>
    public bool? IsOled { get; set; }
    /// <summary>Last reported panel technology, so the engine never polls DDC for protection.</summary>
    public bool OledDetected { get; set; }
    public bool OledProtection { get; set; } = true;

    /// <summary>Whether focus dimming touches this display at all.</summary>
    /// <remarks>
    /// Separate from the shared "dim other monitors" switch, which is about
    /// where the active window is rather than about the panel. A display that
    /// should never be dimmed - a second screen holding a video, a reference
    /// monitor - is a property of that display, so it is set on the display.
    /// </remarks>
    public bool FocusDimming { get; set; } = true;
    /// <summary>Temporary screen-rest request consumed by the engine.</summary>
    public DateTimeOffset? OledRestUntilUtc { get; set; }
    [JsonIgnore]
    public bool TreatAsOled => IsOled ?? OledDetected;

    [JsonIgnore]
    public bool ManagesTaskbar => HideTaskbar;

    /// <summary>Restores this monitor's shipped defaults.</summary>
    public void ResetToDefaults()
    {
        var fresh = new MonitorSettings();

        HideTaskbar = fresh.HideTaskbar;
        ReclaimWorkArea = fresh.ReclaimWorkArea;
        BrightnessBaseline = fresh.BrightnessBaseline;
        BrightnessFloor = fresh.BrightnessFloor;
        BrightnessCeiling = fresh.BrightnessCeiling;
        NightLightStrength = fresh.NightLightStrength;
        NightLightFloor = fresh.NightLightFloor;
        NightLightCeiling = fresh.NightLightCeiling;
        IsOled = fresh.IsOled;
        OledProtection = fresh.OledProtection;
        FocusDimming = fresh.FocusDimming;
        OledRestUntilUtc = null;
        SoftwareBrightness = fresh.SoftwareBrightness;
        // Label is descriptive, not a setting; keeping it leaves the file readable.
    }
}
