using DisplCtrl.Core.Displays;
using DisplCtrl.Core.Caching;
using DisplCtrl.Core.Presets;
using DisplCtrl.Core.Settings;

namespace DisplCtrl.Display.Presets;

/// <summary>What happened when a preset was applied.</summary>
/// <remarks>
/// A list of notes rather than a single failure, because applying a preset is
/// a dozen independent hardware writes and any of them can be refused on its
/// own. Collapsing that to one boolean would throw away the only information
/// the user can act on.
/// </remarks>
public sealed record PresetResult(bool Ok, List<string> Notes, bool Attempted = true)
{
    public static PresetResult Nothing(string why) => new(false, [why], false);
}

/// <summary>
/// Reads the desk into a preset, and writes a preset back onto the desk.
/// </summary>
/// <remarks>
/// Lives beside the control APIs rather than in Core because applying a preset
/// is exactly the set of calls DisplCtrl.Display exists for — mode changes, CCD
/// writes, DDC/CI, wallpaper COM. Core stays the shape of the data, and nothing
/// in it touches hardware.
/// </remarks>
public static class PresetService
{
    private readonly record struct HardwareKey(string Token, string Path, nint Handle, DisplayRect Bounds,
        uint Dpi, uint Rate, int Orientation, bool Primary);
    private static readonly BoundedCache<HardwareKey, PresetMonitor> Hardware = new(32);
    public static void InvalidateHardware() => Hardware.Clear();
    public static event Action? HardwareChanged;
    internal static void NotifyHardwareChanged()
    {
        Hardware.Clear();
        MonitorCapabilities.InvalidateAllValues();
        HardwareChanged?.Invoke();
    }

    /// <summary>Takes a snapshot of the desk as it is right now.</summary>
    /// <remarks>
    /// Everything, with no filter: what a preset holds is what applying it
    /// restores. A field is left at its "not recorded" default only when the
    /// hardware would not answer, and applying skips exactly those.
    /// <para>
    /// Blocks on DDC/CI, so not from the UI thread.
    /// </para>
    /// </remarks>
    public static Preset Capture(string name, IReadOnlyList<DisplayInfo> displays, DisplCtrlSettings settings, bool useCache = false)
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
                VariableRefreshRate = CaptureVrr(displays),
                Taskbar = new PresetTaskbar
                {
                    HideDelayMs = settings.Global.HideDelayMs,
                    AnimMs = settings.Global.AnimMs,
                    RevealPx = settings.Global.RevealPx,
                    ArmDistancePx = settings.Global.ArmDistancePx,
                    IdlePollMs = settings.Global.IdlePollMs,
                    FarPollMs = settings.Global.FarPollMs,
                    ArmedPollMs = settings.Global.ArmedPollMs,
                    ShownPollMs = settings.Global.ShownPollMs,
                },
            },
        };

        var states = new PresetMonitor[displays.Count];
        Parallel.For(0, displays.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            DisplayInfo d = displays[i];
            var key = new HardwareKey(d.Token, d.Key.DevicePath, d.Handle, d.Bounds, d.Dpi,
                d.RefreshHz, d.OrientationDegrees, d.IsPrimary);
            if (!useCache) Hardware.Remove(key);
            states[i] = Hardware.Get(key, TimeSpan.FromSeconds(5), () => ReadHardware(d, useCache)).Copy();
        });
        for (int i = 0; i < displays.Count; i++)
        {
            DisplayInfo d = displays[i];
            MonitorSettings ms = settings.For(d.Token);
            PresetMonitor state = states[i];
            state.CustomLabel = ms.Label;
            state.NightLightStrength = ms.NightLightStrength;
            state.NightLightFloor = ms.NightLightFloor;
            state.NightLightCeiling = ms.NightLightCeiling;
            state.BrightnessBaseline = ms.BrightnessBaseline;
            state.BrightnessFloor = ms.BrightnessFloor;
            state.BrightnessCeiling = ms.BrightnessCeiling;
            state.SoftwareBrightness = ms.SoftwareBrightness;
            state.IsOled = ms.IsOled;
            state.HideTaskbar = ms.HideTaskbar;
            state.ReclaimWorkArea = ms.ReclaimWorkArea;
            if (state.Brightness < 0) preset.CaptureNotes.Add($"{d.Label}: hardware brightness unavailable.");
            if (state.ScalePercent == 0) preset.CaptureNotes.Add($"{d.Label}: scaling unavailable.");
            preset.Monitors[d.Token] = state;
        }

        return preset;
    }

    private static PresetMonitor ReadHardware(DisplayInfo d, bool useCache)
    {
        BrightnessRange brightness = Brightness.Read(d);
        HdrState hdr = AdvancedDisplay.ReadHdr(d);
        ScalingState scaling = AdvancedDisplay.ReadScaling(d);
        string? profile = null;
        try { profile = ColorProfile.ReadName(d) ?? "System default"; }
        catch (Exception) { }
        return new PresetMonitor
            {
                Label = d.Label,
                Model = d.Key.Model,
                Serial = d.Key.Serial,
                Connector = d.Connector.ToString(),
                PhysicalWidthMm = d.PhysicalWidthMm,
                PhysicalHeightMm = d.PhysicalHeightMm,
                Dpi = d.Dpi,
                ColorProfile = profile,
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
                WallpaperPath = Wallpaper.Read(d),
                MonitorControls = CaptureMonitorControls(d, useCache),
            };
    }

    /// <summary>
    /// Whether variable refresh is on, or null when nothing here supports it.
    /// </summary>
    /// <remarks>
    /// Null rather than false on a machine with no capable display, so that
    /// applying this preset elsewhere cannot switch off something it was never
    /// in a position to observe.
    /// </remarks>
    private static bool? CaptureVrr(IReadOnlyList<DisplayInfo> displays)
    {
        try
        {
            foreach (DisplayInfo d in displays)
                if (VariableRefreshRate.Read(d).Capable) return VariableRefreshRate.IsEnabled();
        }
        catch (Exception)
        {
            // Treated as "nothing to say", which is what null means here.
        }

        return null;
    }

    /// <summary>
    /// The monitor's own settings, as far as it will report them.
    /// </summary>
    /// <remarks>
    /// Only the controls DisplCtrl is willing to write. Recording one it would
    /// refuse to set would put a value in the file that applying can never
    /// honour, which is worse than not recording it.
    /// </remarks>
    /// <summary>VCP 10h, which brightness already owns.</summary>
    private const byte BrightnessCode = 0x10;

    private static Dictionary<string, int> CaptureMonitorControls(DisplayInfo display, bool useCache)
    {
        var result = new Dictionary<string, int>();
        if (display.IsInternal) return result;

        try
        {
            foreach (VcpControl c in MonitorCapabilities.ReadSettable(display, useCache).Controls)
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
    public static PresetResult Apply(Preset preset, IReadOnlyList<DisplayInfo> displays, DisplCtrlSettings settings)
    {
        if (!DisplCtrl.Core.FeatureFlags.Presets)
            return PresetResult.Nothing("Presets are an unavailable beta feature in this build.");
        try { PresetValidation.Validate(preset); }
        catch (FormatException ex) { return PresetResult.Nothing(ex.Message); }
        using var gate = new Mutex(false, @"Local\DisplCtrl.PresetApply");
        bool acquired;
        try { acquired = gate.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return PresetResult.Nothing("Another preset is being applied. Try again when it finishes.");
        InvalidateHardware();
        try { return ApplyCore(preset, settings); }
        catch (Exception ex) { return new PresetResult(false, [$"Preset application stopped: {ex.Message}"]); }
        finally { InvalidateHardware(); gate.ReleaseMutex(); }
    }

    private static PresetResult ApplyCore(Preset preset, DisplCtrlSettings settings)
    {
        var notes = new List<string>();
        void Step(string label, Action work)
        {
            try { work(); }
            catch (Exception ex) { notes.Add($"{label}: {ex.Message}"); }
        }
        IReadOnlyList<DisplayInfo> displays = DisplayRegistry.Enumerate();
        if (preset.IncludeLayout)
            Step("Topology", () =>
            {
                if (Enum.TryParse(preset.Global.Topology, out DesktopArrangement topology)
                    && topology.ToString() != CurrentTopology(displays) && !DesktopLayout.Apply(topology))
                    notes.Add($"Windows refused the {topology} topology.");
            });
        // A topology change invalidates HMONITOR handles and can enable previously absent panels.
        displays = DisplayRegistry.Enumerate();
        List<(DisplayInfo Display, PresetMonitor State)> Match() => displays
            .Where(d => preset.Monitors.ContainsKey(d.Token)).Select(d => (d, preset.Monitors[d.Token])).ToList();
        var matched = Match();
        if (matched.Count == 0) return new PresetResult(false, ["None of this preset's displays are attached. Use Map displays to choose targets."]);
        foreach (string token in preset.Monitors.Keys.Where(token => !matched.Any(pair => pair.Display.Token == token)))
            notes.Add($"{preset.Monitors[token].Label ?? token}: display is missing; its values were not restored.");
        // Modes determine rectangle sizes. Positions are restored after those sizes are final.
        foreach (var pair in matched)
            Step(pair.Display.Label, () => ApplyModes([pair], notes));
        displays = DisplayRegistry.Enumerate();
        matched = Match();
        if (preset.IncludeLayout && preset.Global.Topology != "Duplicate") Step("Layout", () => ApplyArrangement(preset, displays, matched, notes));
        displays = DisplayRegistry.Enumerate();
        matched = Match();
        if (preset.IncludeGlobal) Step("Variable refresh", () => ApplyVrr(preset, notes));
        foreach (var pair in matched)
        {
            Step(pair.Display.Label + " HDR", () => ApplyHdr([pair], notes));
            Step(pair.Display.Label + " monitor controls", () => ApplyMonitorControls([pair], notes));
            Step(pair.Display.Label + " brightness", () => ApplyBrightness(preset, [pair], settings, notes));
            Step(pair.Display.Label + " warmth", () => ApplyNightLight(preset, [pair], settings));
            Step(pair.Display.Label + " wallpaper", () => ApplyWallpaper(preset, [pair], notes));
            ApplyTaskbar(preset, [pair], settings);
            Step(pair.Display.Label + " input and power", () => ApplyMonitorControls([pair], notes, disruptive: true));
        }
        if (preset.IncludeGlobal) ApplyTaskbarBehaviour(preset, settings);
        Step("Verification", () =>
        {
            Preset live = Capture("Verification", DisplayRegistry.Enumerate(), settings);
            foreach (PresetChange difference in PresetDiff.Describe(preset, live))
                notes.Add("Not restored: " + difference.Line);
        });
        return new PresetResult(notes.Count == 0, notes.Distinct().ToList());
    }

    private static void ApplyArrangement(Preset preset, IReadOnlyList<DisplayInfo> displays,
                                         List<(DisplayInfo Display, PresetMonitor State)> matched,
                                         List<string> notes)
    {
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

        displays = DisplayRegistry.Enumerate();
        if (matched.All(pair => positions[pair.Display.Token] == (pair.Display.Bounds.Left, pair.Display.Bounds.Top))
            && matched.All(pair => pair.State.Primary == pair.Display.IsPrimary)) return;
        if (!DisplayArrangement.SetPositions(positions, displays, out string? why))
            notes.Add($"Arrangement not applied — {why}.");
    }

    private static void ApplyModes(List<(DisplayInfo Display, PresetMonitor State)> matched, List<string> notes)
    {
        foreach ((DisplayInfo d, PresetMonitor m) in matched)
        {
            // Orientation was captured but never written, so a rotated display
            // silently came back the wrong way up. The scope text promised it.
            if (m.OrientationDegrees != d.OrientationDegrees)
            {
                var wanted = (ScreenOrientation)((m.OrientationDegrees / 90) % 4);
                if (!DisplayArrangement.SetOrientation(d, wanted))
                    notes.Add($"{d.Label}: could not rotate to {m.OrientationDegrees} degrees.");
            }

            DisplayMode? currentMode = DisplayModes.Current(d.GdiName);
            bool sizeDiffers = m.Width > 0 && (m.Width != currentMode?.Width || m.Height != currentMode?.Height);

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

            if (m.ScalePercent <= 0) continue;

            ScalingState scaling = AdvancedDisplay.ReadScaling(d);
            if (!scaling.Supported) { notes.Add($"{d.Label}: scaling is unavailable."); continue; }
            if (scaling.Current == m.ScalePercent) continue;

            if (!AdvancedDisplay.WriteScaling(d, m.ScalePercent))
                notes.Add($"{d.Label}: {m.ScalePercent}% scaling was refused.");
        }
    }

    private static void ApplyHdr(List<(DisplayInfo Display, PresetMonitor State)> matched, List<string> notes)
    {
        foreach ((DisplayInfo d, PresetMonitor m) in matched)
        {
            HdrState state = AdvancedDisplay.ReadHdr(d);
            if (!state.Supported) { if (m.Hdr) notes.Add($"{d.Label}: HDR is unavailable."); continue; }
            if (state.Enabled == m.Hdr) continue;

            if (!AdvancedDisplay.WriteHdr(d, m.Hdr))
                notes.Add($"{d.Label}: HDR could not be switched {(m.Hdr ? "on" : "off")}.");
        }
    }

    private static void ApplyBrightness(Preset preset, List<(DisplayInfo Display, PresetMonitor State)> matched,
                                        DisplCtrlSettings settings, List<string> notes)
    {
        if (preset.IncludeGlobal)
        {
        settings.Global.UnisonBrightness = preset.Global.UnisonBrightness;
        settings.Global.UnisonLevel = preset.Global.UnisonLevel;
        settings.Global.UnisonCalibrated = preset.Global.UnisonCalibrated;
        }

        foreach ((DisplayInfo d, PresetMonitor m) in matched)
        {
            MonitorSettings ms = settings.For(d.Token);
            ms.BrightnessBaseline = m.BrightnessBaseline;
            ms.BrightnessFloor = m.BrightnessFloor;
            ms.BrightnessCeiling = m.BrightnessCeiling;
            ms.SoftwareBrightness = m.SoftwareBrightness;

            if (preset.Version >= 3 || m.IsOled is not null) ms.IsOled = m.IsOled;

            if (m.Brightness < 0) continue;

            if (!Brightness.Write(d, (uint)m.Brightness))
                notes.Add($"{d.Label}: brightness could not be set.");
        }
    }

    /// <remarks>
    /// One machine-wide switch, not a per-display one, and skipped entirely
    /// when the preset has nothing to say — see <see cref="CaptureVrr"/>.
    /// </remarks>
    private static void ApplyVrr(Preset preset, List<string> notes)
    {
        if (preset.Global.VariableRefreshRate is not { } want) return;

        try
        {
            if (VariableRefreshRate.IsEnabled() != want && !VariableRefreshRate.SetEnabled(want))
                notes.Add($"Variable refresh rate could not be turned {(want ? "on" : "off")}.");
        }
        catch (Exception ex)
        {
            notes.Add($"Variable refresh rate: {ex.Message}");
        }
    }

    /// <remarks>
    /// Settings only; the engine picks these up from the file. Skipped when the
    /// preset predates them being captured, so an old preset does not quietly
    /// reset the taskbar timings to defaults.
    /// </remarks>
    private static void ApplyTaskbarBehaviour(Preset preset, DisplCtrlSettings settings)
    {
        if (preset.Global.Taskbar is not { } t) return;

        GlobalSettings g = settings.Global;
        g.HideDelayMs = t.HideDelayMs;
        g.AnimMs = t.AnimMs;
        g.RevealPx = t.RevealPx;
        g.ArmDistancePx = t.ArmDistancePx;
        g.IdlePollMs = t.IdlePollMs;
        g.FarPollMs = t.FarPollMs;
        g.ArmedPollMs = t.ArmedPollMs;
        g.ShownPollMs = t.ShownPollMs;
    }

    /// <remarks>
    /// Settings only. The engine owns the gamma ramp and will pick this up from
    /// the file within about a tenth of a second — writing the ramp here as well
    /// is the bug that made warmth compound until it could not be undone.
    /// </remarks>
    private static void ApplyNightLight(Preset preset, List<(DisplayInfo Display, PresetMonitor State)> matched,
                                        DisplCtrlSettings settings)
    {
        NightLightSettings n = settings.Global.NightLight;

        if (preset.IncludeGlobal)
        {
        n.Enabled = preset.Global.NightLightEnabled;
        n.Strength = preset.Global.NightLightStrength;
        n.Unison = preset.Global.NightLightUnison;
        n.Calibrated = preset.Global.NightLightCalibrated;
        n.Scheduled = preset.Global.NightLightScheduled;
        n.FromMinutes = preset.Global.NightLightFrom;
        n.ToMinutes = preset.Global.NightLightTo;
        }

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
        if (preset.IncludeGlobal && !Wallpaper.WriteFit((WallpaperFit)preset.Global.WallpaperFit))
            notes.Add("Wallpaper fit could not be restored.");

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
                                             List<string> notes, bool disruptive = false)
    {
        foreach ((DisplayInfo d, PresetMonitor m) in matched)
        {
            if (m.MonitorControls.Count == 0 || d.IsInternal) continue;
            if (!m.MonitorControls.Keys.Any(hex => TryParseCode(hex, out byte code)
                && code != BrightnessCode && disruptive == (code is 0x60 or 0xD6))) continue;

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

            foreach ((string hex, int want) in m.MonitorControls.OrderBy(pair =>
                TryParseCode(pair.Key, out byte c) ? c switch { 0xDC => -2, 0x14 => -1, 0x60 => 1000, 0xD6 => 1001, _ => c } : 0))
            {
                if (!TryParseCode(hex, out byte code)) continue;

                // Presets written before brightness was excluded still name it.
                if (code == BrightnessCode || disruptive != (code is 0x60 or 0xD6)) continue;

                // Only controls the monitor still offers. A preset from another
                // machine, or from before a firmware change, can name codes this
                // panel does not have.
                if (!live.TryGetValue(code, out int have))
                { notes.Add($"{d.Label}: control {hex} is unavailable; value {want} was not restored."); continue; }
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

    private static void ApplyTaskbar(Preset preset, List<(DisplayInfo Display, PresetMonitor State)> matched, DisplCtrlSettings settings)
    {
        foreach ((DisplayInfo d, PresetMonitor m) in matched)
        {
            MonitorSettings ms = settings.For(d.Token);
            if (preset.Version >= 3) ms.Label = m.CustomLabel;
            ms.HideTaskbar = m.HideTaskbar;
            ms.ReclaimWorkArea = m.ReclaimWorkArea;
        }
    }
}
