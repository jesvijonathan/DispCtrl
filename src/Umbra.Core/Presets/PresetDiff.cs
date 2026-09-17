namespace Umbra.Core.Presets;

/// <summary>
/// What has drifted between a saved preset and the desk as it is now.
/// </summary>
/// <remarks>
/// Written as a list of readable differences rather than a boolean, because
/// "unsaved changes" on its own is a prompt the user cannot answer. Knowing it
/// is the brightness that moved, and not the resolution, is the difference
/// between confidently pressing Save and not daring to.
/// <para>
/// Only fields inside the preset's <see cref="PresetScope"/> are compared. A
/// preset that does not control wallpaper has not "changed" because the
/// wallpaper did.
/// </para>
/// </remarks>
public static class PresetDiff
{
    /// <summary>Differences between <paramref name="saved"/> and <paramref name="live"/>.</summary>
    public static List<string> Describe(Preset saved, Preset live)
    {
        var diffs = new List<string>();
        PresetScope scope = saved.Scope;

        if (scope.Arrangement && saved.Global.Topology != live.Global.Topology)
            diffs.Add($"topology is {live.Global.Topology}, preset has {saved.Global.Topology}");

        if (scope.NightLight) CompareNightLight(saved, live, diffs);
        if (scope.Brightness) CompareUnison(saved, live, diffs);
        if (scope.Wallpaper && saved.Global.WallpaperFit != live.Global.WallpaperFit)
            diffs.Add("wallpaper fit");

        foreach ((string token, PresetMonitor want) in saved.Monitors)
        {
            // A display the preset knows about but which is not attached is not
            // a difference. It is absent, which is a different problem and is
            // reported where the preset is applied.
            if (!live.Monitors.TryGetValue(token, out PresetMonitor? have)) continue;

            string name = want.Label ?? token;
            CompareMonitor(scope, name, want, have, diffs);
        }

        return diffs;
    }

    private static void CompareNightLight(Preset saved, Preset live, List<string> diffs)
    {
        PresetGlobal a = saved.Global, b = live.Global;

        if (a.NightLightEnabled != b.NightLightEnabled)
            diffs.Add($"night light is {(b.NightLightEnabled ? "on" : "off")}");
        else if (a.NightLightEnabled && a.NightLightStrength != b.NightLightStrength)
            diffs.Add($"night light strength is {b.NightLightStrength}%, preset has {a.NightLightStrength}%");

        if (a.NightLightUnison != b.NightLightUnison) diffs.Add("night light per-display mode");

        if (a.NightLightScheduled != b.NightLightScheduled
            || a.NightLightFrom != b.NightLightFrom
            || a.NightLightTo != b.NightLightTo)
        {
            diffs.Add("night light schedule");
        }
    }

    private static void CompareUnison(Preset saved, Preset live, List<string> diffs)
    {
        PresetGlobal a = saved.Global, b = live.Global;

        if (a.UnisonBrightness != b.UnisonBrightness)
            diffs.Add($"unison brightness is {(b.UnisonBrightness ? "on" : "off")}");
        else if (a.UnisonBrightness && a.UnisonLevel != b.UnisonLevel)
            diffs.Add($"unison level is {b.UnisonLevel}%, preset has {a.UnisonLevel}%");
    }

    private static void CompareMonitor(PresetScope scope, string name,
                                       PresetMonitor want, PresetMonitor have, List<string> diffs)
    {
        if (scope.Arrangement)
        {
            if (want.X != have.X || want.Y != have.Y)
                diffs.Add($"{name}: position is {have.X},{have.Y}, preset has {want.X},{want.Y}");

            if (want.Primary != have.Primary)
                diffs.Add($"{name}: {(have.Primary ? "is" : "is not")} the main display");
        }

        if (scope.Modes)
        {
            if (want.Width != have.Width || want.Height != have.Height)
                diffs.Add($"{name}: {have.Width} x {have.Height}, preset has {want.Width} x {want.Height}");

            if (want.RefreshHz != have.RefreshHz)
                diffs.Add($"{name}: {have.RefreshHz} Hz, preset has {want.RefreshHz} Hz");

            if (want.ScalePercent > 0 && have.ScalePercent > 0 && want.ScalePercent != have.ScalePercent)
                diffs.Add($"{name}: {have.ScalePercent}% scaling, preset has {want.ScalePercent}%");
        }

        if (scope.Hdr && want.Hdr != have.Hdr)
            diffs.Add($"{name}: HDR is {(have.Hdr ? "on" : "off")}");

        // Brightness drifts by a point or two on its own over DDC/CI, and a
        // monitor rounds what it was asked for. Flagging that as an unsaved
        // change would leave the preset permanently, uselessly dirty.
        if (scope.Brightness && want.Brightness >= 0 && have.Brightness >= 0
            && Math.Abs(want.Brightness - have.Brightness) > 2)
        {
            diffs.Add($"{name}: brightness is {have.Brightness}%, preset has {want.Brightness}%");
        }

        if (scope.NightLight && want.NightLightStrength != have.NightLightStrength)
            diffs.Add($"{name}: night light strength");

        if (scope.Wallpaper
            && !string.Equals(want.WallpaperPath, have.WallpaperPath, StringComparison.OrdinalIgnoreCase))
        {
            diffs.Add($"{name}: wallpaper");
        }

        if (scope.Taskbar && (want.HideTaskbar != have.HideTaskbar || want.ReclaimWorkArea != have.ReclaimWorkArea))
            diffs.Add($"{name}: taskbar hiding");
    }
}
