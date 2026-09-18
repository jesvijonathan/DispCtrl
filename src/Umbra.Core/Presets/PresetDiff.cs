namespace Umbra.Core.Presets;

/// <summary>
/// One thing that is not the way the preset saved it.
/// </summary>
/// <param name="Where">The display it is on, or empty when it is desk-wide.</param>
/// <param name="What">The setting, named as the UI names it.</param>
/// <param name="Now">What it is at the moment.</param>
/// <param name="Saved">What the preset holds.</param>
/// <remarks>
/// Four fields rather than a sentence, because the panel lays these out as a
/// table — setting, now, saved — and a sentence would have to be taken apart
/// again to do that. <see cref="Line"/> puts it back together for the places
/// that want prose, which is the command line.
/// </remarks>
public readonly record struct PresetChange(string Where, string What, string Now, string Saved)
{
    public string Setting => Where.Length > 0 ? $"{Where} — {What}" : What;

    public string Line => Saved.Length > 0
        ? $"{Setting}: {Now}, preset has {Saved}"
        : $"{Setting}: {Now}";
}

/// <summary>
/// What has drifted between a saved preset and the desk as it is now.
/// </summary>
/// <remarks>
/// A list of differences rather than a boolean, because "unsaved changes" on
/// its own is a prompt the user cannot answer. Knowing it is the brightness
/// that moved, and not the resolution, is the difference between confidently
/// pressing Save and not daring to.
/// <para>
/// Everything a preset holds is compared, because everything a preset holds is
/// applied. The only fields skipped are the ones neither side could read, and
/// the ones that are recorded for the reader rather than restored — a monitor's
/// serial has not "changed" because the preset came from another machine.
/// </para>
/// </remarks>
public static class PresetDiff
{
    /// <summary>Differences between <paramref name="saved"/> and <paramref name="live"/>.</summary>
    public static List<PresetChange> Describe(Preset saved, Preset live)
    {
        var diffs = new List<PresetChange>();

        if (saved.Global.Topology != live.Global.Topology)
            diffs.Add(new("", "Topology", live.Global.Topology, saved.Global.Topology));

        CompareNightLight(saved, live, diffs);
        CompareUnison(saved, live, diffs);

        if (saved.Global.WallpaperFit != live.Global.WallpaperFit)
            diffs.Add(new("", "Wallpaper fit", Fit(live.Global.WallpaperFit), Fit(saved.Global.WallpaperFit)));

        if (saved.Global.VariableRefreshRate is { } wantVrr
            && live.Global.VariableRefreshRate is { } haveVrr
            && wantVrr != haveVrr)
        {
            diffs.Add(new("", "Variable refresh rate", OnOff(haveVrr), OnOff(wantVrr)));
        }

        CompareTaskbar(saved.Global.Taskbar, live.Global.Taskbar, diffs);

        foreach ((string token, PresetMonitor want) in saved.Monitors)
        {
            // A display the preset knows about but which is not attached is not
            // a difference. It is absent, which is a different problem and is
            // reported where the preset is applied.
            if (!live.Monitors.TryGetValue(token, out PresetMonitor? have)) continue;

            CompareMonitor(want.Label ?? token, want, have, diffs);
        }

        return diffs;
    }

    /// <summary>The same differences as prose, for the command line.</summary>
    public static List<string> Lines(Preset saved, Preset live)
    {
        var lines = new List<string>();
        foreach (PresetChange c in Describe(saved, live)) lines.Add(c.Line);

        return lines;
    }

    private static void CompareNightLight(Preset saved, Preset live, List<PresetChange> diffs)
    {
        PresetGlobal a = saved.Global, b = live.Global;

        if (a.NightLightEnabled != b.NightLightEnabled)
            diffs.Add(new("", "Night light", OnOff(b.NightLightEnabled), OnOff(a.NightLightEnabled)));
        else if (a.NightLightEnabled && a.NightLightStrength != b.NightLightStrength)
            diffs.Add(new("", "Night light strength", $"{b.NightLightStrength}%", $"{a.NightLightStrength}%"));

        if (a.NightLightUnison != b.NightLightUnison)
        {
            diffs.Add(new("", "Night light mode",
                b.NightLightUnison ? "all displays together" : "per display",
                a.NightLightUnison ? "all displays together" : "per display"));
        }

        if (a.NightLightScheduled != b.NightLightScheduled)
            diffs.Add(new("", "Night light schedule", OnOff(b.NightLightScheduled), OnOff(a.NightLightScheduled)));
        else if (a.NightLightScheduled && (a.NightLightFrom != b.NightLightFrom || a.NightLightTo != b.NightLightTo))
            diffs.Add(new("", "Night light hours", Span(b.NightLightFrom, b.NightLightTo), Span(a.NightLightFrom, a.NightLightTo)));
    }

    private static void CompareUnison(Preset saved, Preset live, List<PresetChange> diffs)
    {
        PresetGlobal a = saved.Global, b = live.Global;

        if (a.UnisonBrightness != b.UnisonBrightness)
            diffs.Add(new("", "Unison brightness", OnOff(b.UnisonBrightness), OnOff(a.UnisonBrightness)));
        else if (a.UnisonBrightness && a.UnisonLevel != b.UnisonLevel)
            diffs.Add(new("", "Unison level", $"{b.UnisonLevel}%", $"{a.UnisonLevel}%"));
    }

    /// <remarks>
    /// Skipped entirely when either side has no taskbar block. A preset saved
    /// before these were captured has nothing to say about them, which is not
    /// the same as wanting the defaults.
    /// </remarks>
    private static void CompareTaskbar(PresetTaskbar? want, PresetTaskbar? have, List<PresetChange> diffs)
    {
        if (want is null || have is null) return;

        Compare("Reveal delay", want.HideDelayMs, have.HideDelayMs, "ms");
        Compare("Slide duration", want.AnimMs, have.AnimMs, "ms");
        Compare("Reveal band", want.RevealPx, have.RevealPx, " px");
        Compare("Arm distance", want.ArmDistancePx, have.ArmDistancePx, " px");
        Compare("Idle poll", want.IdlePollMs, have.IdlePollMs, "ms");
        Compare("Far poll", want.FarPollMs, have.FarPollMs, "ms");
        Compare("Armed poll", want.ArmedPollMs, have.ArmedPollMs, "ms");
        Compare("Shown poll", want.ShownPollMs, have.ShownPollMs, "ms");

        void Compare(string what, int w, int h, string unit)
        {
            if (w != h) diffs.Add(new("Taskbar", what, $"{h}{unit}", $"{w}{unit}"));
        }
    }

    private static void CompareMonitor(string name, PresetMonitor want, PresetMonitor have, List<PresetChange> diffs)
    {
        if (want.X != have.X || want.Y != have.Y)
            diffs.Add(new(name, "Position", $"{have.X}, {have.Y}", $"{want.X}, {want.Y}"));

        if (want.Primary != have.Primary)
        {
            diffs.Add(new(name, "Main display",
                have.Primary ? "yes" : "no", want.Primary ? "yes" : "no"));
        }

        // Guarded per field rather than for the whole display. A capture that
        // could not read the mode leaves these at zero, and reporting
        // "resolution is 0 × 0" would be worse than saying nothing — but it must
        // not also silence the brightness, which was read perfectly well.
        if (want.Width > 0 && have.Width > 0 && (want.Width != have.Width || want.Height != have.Height))
            diffs.Add(new(name, "Resolution", $"{have.Width} × {have.Height}", $"{want.Width} × {want.Height}"));

        if (want.RefreshHz > 0 && have.RefreshHz > 0 && want.RefreshHz != have.RefreshHz)
            diffs.Add(new(name, "Refresh rate", $"{have.RefreshHz} Hz", $"{want.RefreshHz} Hz"));

        if (want.ScalePercent > 0 && have.ScalePercent > 0 && want.ScalePercent != have.ScalePercent)
            diffs.Add(new(name, "Scaling", $"{have.ScalePercent}%", $"{want.ScalePercent}%"));

        if (want.OrientationDegrees != have.OrientationDegrees)
            diffs.Add(new(name, "Orientation", $"{have.OrientationDegrees}°", $"{want.OrientationDegrees}°"));

        if (want.Hdr != have.Hdr)
            diffs.Add(new(name, "HDR", OnOff(have.Hdr), OnOff(want.Hdr)));

        // Brightness drifts by a point or two on its own over DDC/CI, and a
        // monitor rounds what it was asked for. Flagging that as an unsaved
        // change would leave the preset permanently, uselessly dirty.
        if (want.Brightness >= 0 && have.Brightness >= 0 && Math.Abs(want.Brightness - have.Brightness) > 2)
            diffs.Add(new(name, "Brightness", $"{have.Brightness}%", $"{want.Brightness}%"));

        if (want.SoftwareBrightness != have.SoftwareBrightness)
            diffs.Add(new(name, "Software dimming", $"{have.SoftwareBrightness}%", $"{want.SoftwareBrightness}%"));

        if (want.NightLightStrength != have.NightLightStrength)
        {
            diffs.Add(new(name, "Night light strength",
                Level(have.NightLightStrength), Level(want.NightLightStrength)));
        }

        if (want.BrightnessFloor != have.BrightnessFloor || want.BrightnessCeiling != have.BrightnessCeiling)
        {
            diffs.Add(new(name, "Brightness range",
                Range(have.BrightnessFloor, have.BrightnessCeiling),
                Range(want.BrightnessFloor, want.BrightnessCeiling)));
        }

        if (!string.Equals(want.WallpaperPath, have.WallpaperPath, StringComparison.OrdinalIgnoreCase))
            diffs.Add(new(name, "Wallpaper", FileName(have.WallpaperPath), FileName(want.WallpaperPath)));

        if (want.HideTaskbar != have.HideTaskbar)
            diffs.Add(new(name, "Hide the taskbar", OnOff(have.HideTaskbar), OnOff(want.HideTaskbar)));

        if (want.ReclaimWorkArea != have.ReclaimWorkArea)
            diffs.Add(new(name, "Reclaim the work area", OnOff(have.ReclaimWorkArea), OnOff(want.ReclaimWorkArea)));

        if (want.IsOled is { } wantOled && have.IsOled is { } haveOled && wantOled != haveOled)
            diffs.Add(new(name, "OLED panel", haveOled ? "yes" : "no", wantOled ? "yes" : "no"));

        // Only codes both sides know about. A preset naming a control this
        // monitor no longer reports is not drift — it is a preset from
        // somewhere else, and applying already says so.
        foreach ((string code, int wanted) in want.MonitorControls)
        {
            if (!have.MonitorControls.TryGetValue(code, out int actual) || actual == wanted) continue;
            diffs.Add(new(name, $"Monitor control {code}", actual.ToString(), wanted.ToString()));
        }
    }

    private static string OnOff(bool on) => on ? "on" : "off";

    private static string Level(int value) => value < 0 ? "follows the shared setting" : $"{value}%";

    private static string Range(int floor, int ceiling) =>
        floor < 0 || ceiling < 0 ? "not calibrated" : $"{floor}–{ceiling}%";

    private static string Span(int from, int to) => $"{Clock(from)} to {Clock(to)}";

    private static string Clock(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    private static string FileName(string? path) =>
        string.IsNullOrEmpty(path) ? "none" : Path.GetFileName(path);

    private static string Fit(int ordinal) => ordinal switch
    {
        0 => "Centre",
        1 => "Tile",
        2 => "Stretch",
        3 => "Fit",
        4 => "Fill",
        5 => "Span",
        _ => ordinal.ToString(),
    };
}
