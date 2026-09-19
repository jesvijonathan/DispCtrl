namespace DisplCtrl.Core.Presets;

/// <summary>
/// A named snapshot of how the desk is set up.
/// </summary>
/// <remarks>
/// One preset is one file, which is what makes sharing one trivial: the file
/// <em>is</em> the shareable artifact, so export is a copy and import is a
/// paste. No bundle format, no registry, nothing to explain.
/// <para>
/// Monitors are keyed on the same replug-surviving token as settings, so a
/// preset saved today still finds the right panel after the cables have been
/// pulled and swapped.
/// </para>
/// <para>
/// Capture records supported display state. IncludeGlobal and IncludeLayout
/// declare whether shared settings and layout are restored. Monitor-only
/// presets restore values for their named displays without changing these groups.
/// </para>
/// </remarks>
public sealed class Preset
{
    /// <summary>Schema version, so a later format change can migrate.</summary>
    /// <remarks>
    /// Version 3 adds explicit global/layout scope and capture diagnostics.
    /// Versions 1 and 2 retain their existing whole-desk interpretation.
    /// </remarks>
    public int Version { get; set; } = 3;

    public Preset Copy()
    {
        var copy = (Preset)MemberwiseClone();
        copy.Global = Global.Copy();
        copy.CaptureNotes = [.. CaptureNotes];
        copy.Monitors = Monitors.ToDictionary(pair => pair.Key, pair => pair.Value.Copy());
        return copy;
    }

    /// <summary>Restore shared desktop settings. False for monitor-only snapshots.</summary>
    public bool IncludeGlobal { get; set; } = true;

    /// <summary>Restore topology, positions and primary display.</summary>
    public bool IncludeLayout { get; set; } = true;

    /// <summary>Read failures or values that could not be captured.</summary>
    public List<string> CaptureNotes { get; set; } = [];

    public string Name { get; set; } = "Untitled";

    /// <summary>Free text, for the user's own benefit.</summary>
    public string? Description { get; set; }

    public DateTimeOffset SavedUtc { get; set; } = DateTimeOffset.UtcNow;

    public PresetGlobal Global { get; set; } = new();

    /// <summary>Per-monitor state, keyed on <c>DisplayKey.ToToken()</c>.</summary>
    public Dictionary<string, PresetMonitor> Monitors { get; set; } = [];
}

/// <summary>Everything that belongs to the desk rather than to one monitor.</summary>
public sealed class PresetGlobal
{
    public PresetGlobal Copy()
    {
        var copy = (PresetGlobal)MemberwiseClone();
        copy.Taskbar = Taskbar?.Copy();
        return copy;
    }

    /// <summary>Extend, Duplicate, InternalOnly or ExternalOnly.</summary>
    public string Topology { get; set; } = "Extend";

    public bool UnisonBrightness { get; set; }
    public int UnisonLevel { get; set; } = 100;
    public bool UnisonCalibrated { get; set; }

    public bool NightLightEnabled { get; set; }
    public int NightLightStrength { get; set; } = 45;
    public bool NightLightUnison { get; set; } = true;
    public bool NightLightCalibrated { get; set; }
    public bool NightLightScheduled { get; set; }
    public int NightLightFrom { get; set; } = 20 * 60;
    public int NightLightTo { get; set; } = 7 * 60;

    /// <summary>Wallpaper fit, as the <c>WallpaperFit</c> ordinal.</summary>
    public int WallpaperFit { get; set; }

    /// <summary>
    /// Variable refresh rate, which Windows keeps as one machine-wide switch.
    /// </summary>
    /// <remarks>
    /// Null when the machine has no display that supports it, so applying a
    /// preset from a machine that does cannot turn something on here that does
    /// not exist.
    /// </remarks>
    public bool? VariableRefreshRate { get; set; }

    /// <summary>How the auto-hiding taskbars behave.</summary>
    /// <remarks>
    /// Part of the snapshot because it is part of how the desk is set up: a
    /// preset for presenting wants the bar to stay put, an everyday one wants
    /// it quick. Null when the preset predates these being captured, which is
    /// the difference between "this preset wants 350 ms" and "this preset has
    /// nothing to say about it".
    /// </remarks>
    public PresetTaskbar? Taskbar { get; set; }
}

/// <summary>
/// The taskbar's reveal behaviour, as a preset carries it.
/// </summary>
/// <remarks>
/// A class rather than loose fields on <see cref="PresetGlobal"/> so the whole
/// group can be absent. All-or-nothing is right here: these values only make
/// sense together, and a preset carrying a poll interval but not the delay it
/// was tuned against would be worse than one carrying neither.
/// </remarks>
public sealed class PresetTaskbar
{
    public PresetTaskbar Copy() => (PresetTaskbar)MemberwiseClone();

    public int HideDelayMs { get; set; } = 350;
    public int AnimMs { get; set; } = 180;
    public int RevealPx { get; set; } = 2;
    public int ArmDistancePx { get; set; } = 300;
    public int IdlePollMs { get; set; } = 100;
    public int FarPollMs { get; set; } = 500;
    public int ArmedPollMs { get; set; } = 16;
    public int ShownPollMs { get; set; } = 40;
}

/// <summary>One monitor's whole state, as a preset carries it.</summary>
public sealed class PresetMonitor
{
    public PresetMonitor Copy()
    {
        var copy = (PresetMonitor)MemberwiseClone();
        copy.MonitorControls = new(MonitorControls);
        return copy;
    }

    /// <summary>
    /// Last known friendly name.
    /// </summary>
    /// <remarks>
    /// For reading the file, and for telling the user which monitor a preset
    /// wants when that monitor is not currently attached. Never used to match.
    /// </remarks>
    public string? Label { get; set; }

    /// <summary>User-defined monitor name, distinct from hardware identity.</summary>
    public string? CustomLabel { get; set; }

    // ---------------------------------------------------------- identity --
    // Recorded, never applied. A preset file is something people send each
    // other and open in an editor, and these are what make it readable: which
    // monitor this actually was, on what cable, at what size. Matching is on
    // the token alone.

    /// <summary>EDID manufacturer and product code, e.g. DEL-A234.</summary>
    public string? Model { get; set; }

    /// <summary>EDID serial. Present so a shared preset can be traced back.</summary>
    public string? Serial { get; set; }

    /// <summary>Internal, Hdmi, DisplayPort and so on.</summary>
    public string? Connector { get; set; }

    public int PhysicalWidthMm { get; set; }
    public int PhysicalHeightMm { get; set; }

    /// <summary>Effective DPI at capture; 96 is 100%.</summary>
    public uint Dpi { get; set; }

    /// <summary>The colour profile Windows had assigned.</summary>
    public string? ColorProfile { get; set; }

    // -------------------------------------------------------- arrangement --

    public int X { get; set; }
    public int Y { get; set; }
    public bool Primary { get; set; }

    public uint Width { get; set; }
    public uint Height { get; set; }
    public uint RefreshHz { get; set; }

    public int ScalePercent { get; set; }

    /// <summary>0, 90, 180 or 270.</summary>
    public int OrientationDegrees { get; set; }

    public bool Hdr { get; set; }

    // ------------------------------------------------------------ light --

    /// <summary>-1 when this display reported no brightness control.</summary>
    public int Brightness { get; set; } = -1;

    /// <summary>Gamma-ramp dimming, 10 to 100. Composed with warmth by the engine.</summary>
    public int SoftwareBrightness { get; set; } = 100;

    public int NightLightStrength { get; set; } = -1;
    public int NightLightFloor { get; set; } = -1;
    public int NightLightCeiling { get; set; } = -1;
    public int BrightnessBaseline { get; set; } = -1;
    public int BrightnessFloor { get; set; } = -1;
    public int BrightnessCeiling { get; set; } = -1;

    /// <summary>
    /// Whether the owner has marked this panel as OLED.
    /// </summary>
    /// <remarks>
    /// Null when nobody has said. Carried so that moving a preset to a rebuilt
    /// machine does not lose a fact the user had to supply by hand — nothing
    /// on this panel reports it.
    /// </remarks>
    public bool? IsOled { get; set; }

    // ----------------------------------------------------------- desktop --

    public string? WallpaperPath { get; set; }

    public bool HideTaskbar { get; set; }
    public bool ReclaimWorkArea { get; set; } = true;

    /// <summary>
    /// The monitor's own VCP settings, keyed by code as "0x12".
    /// </summary>
    /// <remarks>
    /// Keyed by hex string rather than by number so the file reads the way the
    /// display report and every DDC/CI tool write these codes. Only controls
    /// DisplCtrl is willing to set are recorded; a manufacturer-specific code it
    /// will not write has no business being restored either.
    /// </remarks>
    public Dictionary<string, int> MonitorControls { get; set; } = [];
}
