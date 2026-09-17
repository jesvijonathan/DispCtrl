using Umbra.Core.Displays;
using Umbra.Core.Presets;
using Umbra.Core.Settings;

namespace Umbra.Display.Presets;

/// <summary>What happened when a preset was applied.</summary>
/// <remarks>
/// A list of notes rather than a single failure, because applying a preset is
/// a dozen independent hardware writes and any of them can be refused on its
/// own. Collapsing that to one boolean would throw away the only information
/// the user can act on.
/// </remarks>
public sealed record PresetResult(bool Ok, List<string> Notes)
{
    public static PresetResult Nothing(string why) => new(false, [why]);
}

/// <summary>
/// Reads the desk into a preset, and writes a preset back onto the desk.
/// </summary>
/// <remarks>
/// Lives beside the control APIs rather than in Core because applying a preset
/// is exactly the set of calls Umbra.Display exists for — mode changes, CCD
/// writes, DDC/CI, wallpaper COM. Core stays the shape of the data, and nothing
/// in it touches hardware.
/// </remarks>
public static class PresetService
{
    /// <summary>Takes a snapshot of the desk as it is right now.</summary>
    /// <remarks>
    /// Captures everything regardless of scope. Scope decides what is
    /// <em>applied</em>, not what is recorded, so narrowing or widening a preset
    /// later does not mean re-capturing what was already there.
    /// </remarks>
    public static Preset Capture(string name, IReadOnlyList<DisplayInfo> displays, UmbraSettings settings)
    {
        NightLightSettings night = settings.Global.NightLight;

        var preset = new Preset
        {
            Name = name,
            Global = new PresetGlobal
            {
                Topology = CurrentTopology(displays),
                UnisonBrightness = settings.Global.UnisonBrightness,
                UnisonLevel = settings.Global.UnisonLevel,
                UnisonCalibrated = settings.Global.UnisonCalibrated,
                NightLightEnabled = night.Enabled,
                NightLightStrength = night.Strength,
                NightLightUnison = night.Unison,
                NightLightCalibrated = night.Calibrated,
                NightLightScheduled = night.Scheduled,
                NightLightFrom = night.FromMinutes,
                NightLightTo = night.ToMinutes,
                WallpaperFit = (int)Wallpaper.ReadFit(),
            },
        };

        foreach (DisplayInfo d in displays)
        {
            MonitorSettings ms = settings.For(d.Token);
            BrightnessRange brightness = Brightness.Read(d);
            HdrState hdr = AdvancedDisplay.ReadHdr(d);
            ScalingState scaling = AdvancedDisplay.ReadScaling(d);

            preset.Monitors[d.Token] = new PresetMonitor
            {
                Label = d.Label,
                X = d.Bounds.Left,
                Y = d.Bounds.Top,
                Primary = d.IsPrimary,
                Width = (uint)d.Bounds.Width,
                Height = (uint)d.Bounds.Height,
                RefreshHz = d.RefreshHz,
                ScalePercent = scaling.Supported ? scaling.Current : 0,
                OrientationDegrees = d.OrientationDegrees,
                Hdr = hdr.Enabled,
                Brightness = brightness.Supported ? (int)brightness.Current : -1,
                NightLightStrength = ms.NightLightStrength,
                NightLightFloor = ms.NightLightFloor,
                NightLightCeiling = ms.NightLightCeiling,
                BrightnessBaseline = ms.BrightnessBaseline,
                BrightnessFloor = ms.BrightnessFloor,
                BrightnessCeiling = ms.BrightnessCeiling,
                WallpaperPath = Wallpaper.Read(d),
                HideTaskbar = ms.HideTaskbar,
                ReclaimWorkArea = ms.ReclaimWorkArea,
                MonitorControls = CaptureMonitorControls(d),
            };
        }

        return preset;
    }

    /// <summary>
    /// The monitor's own settings, as far as it will report them.
    /// </summary>
    /// <remarks>
    /// Only the controls Umbra is willing to write. Recording one it would
    /// refuse to set would put a value in the file that applying can never
    /// honour, which is worse than not recording it.
    /// </remarks>
    /// <summary>VCP 10h, which brightness already owns.</summary>
    private const byte BrightnessCode = 0x10;

    private static Dictionary<string, int> CaptureMonitorControls(DisplayInfo display)
    {
        var result = new Dictionary<string, int>();
        if (display.IsInternal) return result;

        try
        {
            foreach (VcpControl c in MonitorCapabilities.ReadSettable(display).Controls)
            {
                if (!c.Settable || c.CurrentValue < 0) continue;

                // Brightness has its own field, captured through the same path
                // the brightness slider uses. Recording it here as well would
                // give the preset two values for one setting, free to disagree
                // — and on this Dell the VCP read came back 24 while the panel
                // was plainly at 62.
                if (c.Code == BrightnessCode) continue;

                result[c.Hex] = c.CurrentValue;
            }
        }
        catch (Exception)
        {
            // A monitor that will not talk simply contributes nothing.
        }

        return result;
    }

    /// <summary>
    /// The topology as a <see cref="DesktopArrangement"/> name.
    /// </summary>
    /// <remarks>
    /// Derived from what is on screen rather than asked of Windows, because
    /// there is no API that answers "which of the four Win+P choices is
    /// current" — the topology flags are write-only.
    /// </remarks>
    private static string CurrentTopology(IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count > 1)
        {
            return DisplayRegistry.Topology() == DisplayRegistry.DisplayTopology.Duplicated
                ? nameof(DesktopArrangement.Duplicate)
                : nameof(DesktopArrangement.Extend);
        }

        // One display: which one it is decides which single-display choice this
        // was, and an internal-only desk is by far the common case of the two.
        return displays.Count == 1 && !displays[0].IsInternal
            ? nameof(DesktopArrangement.ExternalOnly)
            : nameof(DesktopArrangement.InternalOnly);
    }

    /// <summary>
    /// Writes a preset onto the desk and into settings.
    /// </summary>
    /// <remarks>
    /// Ordering is not arbitrary. Topology first, because it decides which
    /// displays exist at all; then positions and primary, because a mode change
    /// on a display that is about to move would be applied twice; then modes,
    /// scaling and colour, which are per display and independent; then the cheap
    /// settings-driven parts.
    /// <para>
    /// Every step is attempted even if an earlier one failed. A preset that gets
    /// six of its eight changes on is more useful than one that stops at the
    /// first refusal, and the notes say exactly what did not take.
    /// </para>
    /// <para>
    /// Blocks, sometimes for seconds, and must not be called on the UI thread.
    /// </para>
    /// </remarks>
    public static PresetResult Apply(Preset preset, IReadOnlyList<DisplayInfo> displays, UmbraSettings settings)
    {
        if (preset.Scope.IsEmpty) return PresetResult.Nothing("This preset controls nothing.");

        var notes = new List<string>();

        // Matched by token, so a preset follows the physical panel across
        // replugs rather than landing on whatever holds that slot today.
        var matched = new List<(DisplayInfo Display, PresetMonitor State)>();
        foreach (DisplayInfo d in displays)
            if (preset.Monitors.TryGetValue(d.Token, out PresetMonitor? m)) matched.Add((d, m));

        if (matched.Count == 0)
            return new PresetResult(false, ["None of this preset's displays are attached."]);

        int missing = preset.Monitors.Count - matched.Count;
        if (missing > 0) notes.Add($"{missing} display(s) in this preset are not attached, and were skipped.");

        if (preset.Scope.Arrangement) ApplyArrangement(preset, displays, matched, notes);
        if (preset.Scope.Modes) ApplyModes(matched, notes);
        if (preset.Scope.Hdr) ApplyHdr(matched, notes);
        if (preset.Scope.Brightness) ApplyBrightness(preset, matched, settings, notes);
        if (preset.Scope.NightLight) ApplyNightLight(preset, matched, settings);
        if (preset.Scope.Wallpaper) ApplyWallpaper(preset, matched, notes);
        if (preset.Scope.Taskbar) ApplyTaskbar(matched, settings);
        if (preset.Scope.MonitorControls) ApplyMonitorControls(matched, notes);

        return new PresetResult(true, notes);
    }

    private static void ApplyArrangement(Preset preset, IReadOnlyList<DisplayInfo> displays,
                                         List<(DisplayInfo Display, PresetMonitor State)> matched,
                                         List<string> notes)
    {
        if (Enum.TryParse(preset.Global.Topology, out DesktopArrangement topology)
            && topology.ToString() != CurrentTopology(displays)
            && !DesktopLayout.Apply(topology))
        {
            notes.Add($"Windows refused the {topology} topology.");
        }

        // Primary first, then positions. The desktop origin *is* the primary
        // display's top-left, so promoting a display re-bases every other
        // display's coordinates — positions written before it came out shifted
        // by exactly the offset between the old primary and the new one.
        foreach ((DisplayInfo d, PresetMonitor m) in matched)
        {
            if (!m.Primary || d.IsPrimary) continue;

            if (!DisplayArrangement.SetPrimary(d, displays))
                notes.Add($"Could not make {d.Label} the main display.");
        }

        var positions = new Dictionary<string, (int X, int Y)>(matched.Count);
        foreach ((DisplayInfo d, PresetMonitor m) in matched) positions[d.Token] = (m.X, m.Y);

        if (!DisplayArrangement.SetPositions(positions, displays, out string? why))
            notes.Add($"Arrangement not applied — {why}.");
    }

    private static void ApplyModes(List<(DisplayInfo Display, PresetMonitor State)> matched, List<string> notes)
    {
        foreach ((DisplayInfo d, PresetMonitor m) in matched)
        {
            bool sizeDiffers = m.Width > 0 && (m.Width != d.Bounds.Width || m.Height != d.Bounds.Height);

            if (sizeDiffers)
            {
                // A full mode switch, which blanks the display. Only done when
                // the resolution actually differs; a rate-only change goes
                // through the CCD path below, which most drivers apply without
                // blanking at all.
                var target = new DisplayMode(m.Width, m.Height, m.RefreshHz, d.BitsPerPixel);
                ModeChangeResult result = DisplayModes.Apply(d.GdiName, target);

                if (result is ModeChangeResult.NotSupported or ModeChangeResult.Failed)
                    notes.Add($"{d.Label}: {m.Width} x {m.Height} was refused.");
                else if (result == ModeChangeResult.NeedsRestart)
                    notes.Add($"{d.Label}: {m.Width} x {m.Height} needs a restart to take effect.");
            }
            else if (m.RefreshHz > 0 && m.RefreshHz != d.RefreshHz)
            {
                if (!DisplayArrangement.SetRefreshRate(d, m.RefreshHz))
                    notes.Add($"{d.Label}: {m.RefreshHz} Hz was refused.");
            }

            // Orientation was captured but never written, so a rotated display
            // silently came back the wrong way up. The scope text promised it.
            if (m.OrientationDegrees != d.OrientationDegrees)
            {
                var wanted = (ScreenOrientation)((m.OrientationDegrees / 90) % 4);
                if (!DisplayArrangement.SetOrientation(d, wanted))
                    notes.Add($"{d.Label}: could not rotate to {m.OrientationDegrees} degrees.");
            }

            if (m.ScalePercent <= 0) continue;

            ScalingState scaling = AdvancedDisplay.ReadScaling(d);
            if (!scaling.Supported || scaling.Current == m.ScalePercent) continue;

            if (!AdvancedDisplay.WriteScaling(d, m.ScalePercent))
                notes.Add($"{d.Label}: {m.ScalePercent}% scaling was refused.");
        }
    }

    private static void ApplyHdr(List<(DisplayInfo Display, PresetMonitor State)> matched, List<string> notes)
    {
        foreach ((DisplayInfo d, PresetMonitor m) in matched)
        {
            HdrState state = AdvancedDisplay.ReadHdr(d);
            if (!state.Supported || state.Enabled == m.Hdr) continue;

            if (!AdvancedDisplay.WriteHdr(d, m.Hdr))
                notes.Add($"{d.Label}: HDR could not be switched {(m.Hdr ? "on" : "off")}.");
        }
    }

    private static void ApplyBrightness(Preset preset, List<(DisplayInfo Display, PresetMonitor State)> matched,
                                        UmbraSettings settings, List<string> notes)
    {
        settings.Global.UnisonBrightness = preset.Global.UnisonBrightness;
        settings.Global.UnisonLevel = preset.Global.UnisonLevel;
        settings.Global.UnisonCalibrated = preset.Global.UnisonCalibrated;

        foreach ((DisplayInfo d, PresetMonitor m) in matched)
        {
            MonitorSettings ms = settings.For(d.Token);
            ms.BrightnessBaseline = m.BrightnessBaseline;
            ms.BrightnessFloor = m.BrightnessFloor;
            ms.BrightnessCeiling = m.BrightnessCeiling;

            if (m.Brightness < 0) continue;

            if (!Brightness.Write(d, (uint)m.Brightness))
                notes.Add($"{d.Label}: brightness could not be set.");
        }
    }

    /// <remarks>
    /// Settings only. The engine owns the gamma ramp and will pick this up from
    /// the file within about a tenth of a second — writing the ramp here as well
    /// is the bug that made warmth compound until it could not be undone.
    /// </remarks>
    private static void ApplyNightLight(Preset preset, List<(DisplayInfo Display, PresetMonitor State)> matched,
                                        UmbraSettings settings)
    {
        NightLightSettings n = settings.Global.NightLight;

        n.Enabled = preset.Global.NightLightEnabled;
        n.Strength = preset.Global.NightLightStrength;
        n.Unison = preset.Global.NightLightUnison;
        n.Calibrated = preset.Global.NightLightCalibrated;
        n.Scheduled = preset.Global.NightLightScheduled;
        n.FromMinutes = preset.Global.NightLightFrom;
        n.ToMinutes = preset.Global.NightLightTo;

        foreach ((DisplayInfo d, PresetMonitor m) in matched)
        {
            MonitorSettings ms = settings.For(d.Token);
            ms.NightLightStrength = m.NightLightStrength;
            ms.NightLightFloor = m.NightLightFloor;
            ms.NightLightCeiling = m.NightLightCeiling;
        }
    }

    private static void ApplyWallpaper(Preset preset, List<(DisplayInfo Display, PresetMonitor State)> matched,
                                       List<string> notes)
    {
        _ = Wallpaper.WriteFit((WallpaperFit)preset.Global.WallpaperFit);

        foreach ((DisplayInfo d, PresetMonitor m) in matched)
        {
            if (string.IsNullOrWhiteSpace(m.WallpaperPath)) continue;

            // Named rather than silently skipped: a preset shared from another
            // machine will routinely point at paths that do not exist here, and
            // which image to go and find is the only thing the user can act on.
            if (!File.Exists(m.WallpaperPath))
            {
                notes.Add($"{d.Label}: wallpaper not found at {m.WallpaperPath}.");
                continue;
            }

            if (!Wallpaper.Write(d, m.WallpaperPath))
                notes.Add($"{d.Label}: wallpaper could not be set.");
        }
    }

    /// <remarks>
    /// Each write is a DDC/CI round trip, so a monitor with a dozen recorded
    /// settings takes a noticeable moment. Values already at their target are
    /// skipped, which in the common case of re-applying a preset means no
    /// traffic at all.
    /// </remarks>
    private static void ApplyMonitorControls(List<(DisplayInfo Display, PresetMonitor State)> matched,
                                             List<string> notes)
    {
        foreach ((DisplayInfo d, PresetMonitor m) in matched)
        {
            if (m.MonitorControls.Count == 0 || d.IsInternal) continue;

            Dictionary<byte, int> live = [];
            try
            {
                foreach (VcpControl c in MonitorCapabilities.ReadSettable(d).Controls)
                    if (c.Settable && c.CurrentValue >= 0) live[c.Code] = c.CurrentValue;
            }
            catch (Exception)
            {
                notes.Add($"{d.Label}: could not read its own settings.");
                continue;
            }

            foreach ((string hex, int want) in m.MonitorControls)
            {
                if (!TryParseCode(hex, out byte code)) continue;

                // Presets written before brightness was excluded still name it.
                if (code == BrightnessCode) continue;

                // Only controls the monitor still offers. A preset from another
                // machine, or from before a firmware change, can name codes this
                // panel does not have.
                if (!live.TryGetValue(code, out int have)) continue;
                if (have == want) continue;

                if (!MonitorCapabilities.Write(d, code, (uint)want))
                    notes.Add($"{d.Label}: {hex} would not take {want}.");
            }
        }
    }

    private static bool TryParseCode(string hex, out byte code)
    {
        ReadOnlySpan<char> text = hex.AsSpan();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];

        return byte.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out code);
    }

    private static void ApplyTaskbar(List<(DisplayInfo Display, PresetMonitor State)> matched, UmbraSettings settings)
    {
        foreach ((DisplayInfo d, PresetMonitor m) in matched)
        {
            MonitorSettings ms = settings.For(d.Token);
            ms.HideTaskbar = m.HideTaskbar;
            ms.ReclaimWorkArea = m.ReclaimWorkArea;
        }
    }
}
