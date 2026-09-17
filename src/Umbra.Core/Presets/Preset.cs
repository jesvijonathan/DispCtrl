using System.Text.Json.Serialization;

namespace Umbra.Core.Presets;

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
/// </remarks>
public sealed class Preset
{
    /// <summary>Schema version, so a later format change can migrate.</summary>
    public int Version { get; set; } = 1;

    public string Name { get; set; } = "Untitled";

    /// <summary>Free text, for the user's own benefit.</summary>
    public string? Description { get; set; }

    public DateTimeOffset SavedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>What this preset is allowed to change.</summary>
    public PresetScope Scope { get; set; } = new();

    public PresetGlobal Global { get; set; } = new();

    /// <summary>Per-monitor state, keyed on <c>DisplayKey.ToToken()</c>.</summary>
    public Dictionary<string, PresetMonitor> Monitors { get; set; } = [];
}

/// <summary>
/// Which aspects of the desk a preset controls.
/// </summary>
/// <remarks>
/// Scoped rather than all-or-nothing because the useful presets are usually
/// narrow: "warm and dim for the evening" should not also drag the resolution
/// and wallpaper along with it. Anything switched off here is left exactly as
/// it is when the preset is applied.
/// <para>
/// The two that cost a mode switch — arrangement and modes — are the ones that
/// blank the screen, so they are the ones worth being able to leave out.
/// </para>
/// </remarks>
public sealed class PresetScope
{
    /// <summary>Positions, which display is primary, and the topology.</summary>
    public bool Arrangement { get; set; } = true;

    /// <summary>Resolution, refresh rate, scaling and orientation.</summary>
    public bool Modes { get; set; } = true;

    public bool Hdr { get; set; }

    public bool Brightness { get; set; } = true;

    public bool NightLight { get; set; } = true;

    public bool Wallpaper { get; set; }

    /// <summary>Which monitors hide their taskbar.</summary>
    public bool Taskbar { get; set; }

    /// <summary>
    /// The monitor's own settings: contrast, colour preset, picture mode,
    /// input source, sharpness, RGB gains.
    /// </summary>
    /// <remarks>
    /// On by default. These are the settings a monitor forgets when it is
    /// switched between machines or inputs, and the ones Windows cannot restore
    /// at all — which makes them among the most worthwhile things a preset can
    /// carry.
    /// </remarks>
    public bool MonitorControls { get; set; } = true;

    /// <summary>True when the preset would change nothing at all.</summary>
    [JsonIgnore]
    public bool IsEmpty =>
        !Arrangement && !Modes && !Hdr && !Brightness && !NightLight && !Wallpaper && !Taskbar
        && !MonitorControls;
}

public sealed class PresetGlobal
{
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
}

public sealed class PresetMonitor
{
    /// <summary>
    /// Last known friendly name.
    /// </summary>
    /// <remarks>
    /// For reading the file, and for telling the user which monitor a preset
    /// wants when that monitor is not currently attached. Never used to match.
    /// </remarks>
    public string? Label { get; set; }

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

    /// <summary>-1 when this display reported no brightness control.</summary>
    public int Brightness { get; set; } = -1;

    public int NightLightStrength { get; set; } = -1;
    public int NightLightFloor { get; set; } = -1;
    public int NightLightCeiling { get; set; } = -1;
    public int BrightnessBaseline { get; set; } = -1;
    public int BrightnessFloor { get; set; } = -1;
    public int BrightnessCeiling { get; set; } = -1;

    public string? WallpaperPath { get; set; }

    public bool HideTaskbar { get; set; }
    public bool ReclaimWorkArea { get; set; } = true;

    /// <summary>
    /// The monitor's own VCP settings, keyed by code as "0x12".
    /// </summary>
    /// <remarks>
    /// Keyed by hex string rather than by number so the file reads the way the
    /// display report and every DDC/CI tool write these codes. Only controls
    /// Umbra is willing to set are recorded; a manufacturer-specific code it
    /// will not write has no business being restored either.
    /// </remarks>
    public Dictionary<string, int> MonitorControls { get; set; } = [];
}
