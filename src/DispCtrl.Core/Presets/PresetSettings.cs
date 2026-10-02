using DispCtrl.Core.Settings;

namespace DispCtrl.Core.Presets;

/// <summary>Commit only preset-owned fields into the latest settings snapshot.</summary>
/// <remarks>
/// Field by field, never an object at a time: night light was assigned whole,
/// so a change made elsewhere while a preset applied - following Windows'
/// night light, the theme schedule's bookkeeping - was overwritten with the
/// copy loaded before, and the two settings objects then shared one night
/// light, so a later write to either changed both.
/// </remarks>
public static class PresetSettings
{
    public static DispCtrlSettings Merge(Preset preset, DispCtrlSettings applied, DispCtrlSettings latest)
    {
        if (preset.IncludeGlobal)
        {
            GlobalSettings from = applied.Global, to = latest.Global;
            to.UnisonBrightness = from.UnisonBrightness;
            to.UnisonLevel = from.UnisonLevel;
            to.UnisonCalibrated = from.UnisonCalibrated;
            NightLightSettings night = from.NightLight, into = to.NightLight;
            into.Enabled = night.Enabled;
            into.Strength = night.Strength;
            into.Unison = night.Unison;
            into.Calibrated = night.Calibrated;
            into.Scheduled = night.Scheduled;
            into.FromMinutes = night.FromMinutes;
            into.ToMinutes = night.ToMinutes;
            if (preset.Global.Taskbar is not null)
            {
                to.HideDelayMs = from.HideDelayMs;
                to.AnimMs = from.AnimMs;
                to.RevealPx = from.RevealPx;
                to.ArmDistancePx = from.ArmDistancePx;
                to.IdlePollMs = from.IdlePollMs;
                to.FarPollMs = from.FarPollMs;
                to.ArmedPollMs = from.ArmedPollMs;
                to.ShownPollMs = from.ShownPollMs;
            }
        }
        foreach (string token in preset.Monitors.Keys)
        {
            if (!applied.Monitors.TryGetValue(token, out MonitorSettings? from)) continue;
            MonitorSettings to = latest.For(token);
            to.Label = from.Label;
            to.HideTaskbar = from.HideTaskbar;
            to.ReclaimWorkArea = from.ReclaimWorkArea;
            to.BrightnessBaseline = from.BrightnessBaseline;
            to.BrightnessFloor = from.BrightnessFloor;
            to.BrightnessCeiling = from.BrightnessCeiling;
            to.NightLightStrength = from.NightLightStrength;
            to.NightLightFloor = from.NightLightFloor;
            to.NightLightCeiling = from.NightLightCeiling;
            to.SoftwareBrightness = from.SoftwareBrightness;
            to.IsOled = from.IsOled;
        }
        return latest;
    }
}
