namespace DispCtrl.Core.Settings;

/// <summary>
/// What unison brightness does to each display when it is switched back on.
/// </summary>
/// <remarks>
/// Unison drives each display at <c>baseline × level</c>. Switching it off
/// leaves every display wherever it is, free to be set by hand. Switching it on
/// again brings them all back to where unison puts them: the slider stays where
/// it was left and each display returns to its own baseline at that level.
/// <para>
/// What it must not do is what it first did: record every display's current
/// brightness as its new baseline and put the slider back to 100. Nothing moved
/// on screen, but the full scale shrank - off at 60%, on again, and 60% was the
/// new 100%, a little less every cycle.
/// </para>
/// <para>
/// A display with no baseline yet - one plugged in since unison was set up -
/// gets one worked back from where it is now, so it joins without a jump.
/// </para>
/// </remarks>
public static class UnisonResume
{
    /// <param name="level">The unison slider as it was left.</param>
    /// <param name="current">Each display's brightness now, 0-100.</param>
    /// <param name="baselines">Each display's recorded baseline, 0 when it has none.</param>
    /// <param name="minimum">The lowest the slider goes.</param>
    /// <returns>The level to apply, and the baseline each display is driven from.</returns>
    public static (int Level, int[] Baselines) Enable(
        int level, IReadOnlyList<int> current, IReadOnlyList<int> baselines, int minimum = 1)
    {
        level = Math.Clamp(level, Math.Clamp(minimum, 0, 100), 100);

        var result = new int[current.Count];
        for (int i = 0; i < current.Count; i++)
        {
            int old = i < baselines.Count ? baselines[i] : 0;
            if (old > 0)
            {
                result[i] = Math.Clamp(old, 1, 100);
                continue;
            }

            // Never zero: every later level multiplies it, and a display
            // captured at zero could not be brought back up.
            int now = Math.Clamp(current[i], 0, 100);
            result[i] = level == 0 ? Math.Max(now, 1) : Math.Clamp((int)Math.Round(now * 100.0 / level), 1, 100);
        }

        return (level, result);
    }

    /// <summary>Where unison puts one display, on the baseline scale.</summary>
    public static int Target(int baseline, int level) =>
        Math.Clamp((int)Math.Round(Math.Clamp(baseline, 1, 100) * Math.Clamp(level, 0, 100) / 100.0), 0, 100);

    /// <summary>Shared calibrated mapping used by the desktop, CLI and Windows brightness bridge.</summary>
    public static int Target(MonitorSettings monitor, bool calibrated, int level) =>
        calibrated && monitor.HasBrightnessRange
            ? Math.Clamp(monitor.BrightnessFloor + (int)Math.Round(
                (monitor.BrightnessCeiling - monitor.BrightnessFloor) * Math.Clamp(level, 0, 100) / 100.0), 0, 100)
            : Target(monitor.BrightnessBaseline, level);

    /// <summary>
    /// The unison level a display's brightness stands for: the inverse of
    /// <see cref="Target(MonitorSettings, bool, int)"/>.
    /// </summary>
    /// <remarks>
    /// How Windows' own brightness slider and keys drive unison. They move the
    /// built-in panel, and the panel is read back as a level through its own
    /// calibrated range, so the level and every other display agree with where
    /// the panel sits. Reading the panel's value as the level itself - what this
    /// replaced - let the built-in panel run 0-100 while the others moved within
    /// their calibrated ranges. A value outside the range reads as the nearest
    /// end; the caller puts the panel back inside it.
    /// </remarks>
    public static int LevelFor(MonitorSettings monitor, bool calibrated, int brightness)
    {
        brightness = Math.Clamp(brightness, 0, 100);
        if (calibrated && monitor.HasBrightnessRange)
        {
            int span = monitor.BrightnessCeiling - monitor.BrightnessFloor;
            if (span <= 0) return brightness >= monitor.BrightnessCeiling ? 100 : 0;
            return Math.Clamp((int)Math.Round((brightness - monitor.BrightnessFloor) * 100.0 / span), 0, 100);
        }

        int baseline = Math.Clamp(monitor.BrightnessBaseline, 1, 100);
        return Math.Clamp((int)Math.Round(brightness * 100.0 / baseline), 0, 100);
    }
}
