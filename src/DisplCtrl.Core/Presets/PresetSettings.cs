using DisplCtrl.Core.Settings;

namespace DisplCtrl.Core.Presets;

/// <summary>Commit only preset-owned fields into the latest settings snapshot.</summary>
public static class PresetSettings
{
    public static DisplCtrlSettings Merge(Preset preset, DisplCtrlSettings applied, DisplCtrlSettings latest)
    {
        if (preset.IncludeGlobal)
        {
            GlobalSettings from = applied.Global, to = latest.Global;
            to.UnisonBrightness = from.UnisonBrightness;
            to.UnisonLevel = from.UnisonLevel;
            to.UnisonCalibrated = from.UnisonCalibrated;
            to.NightLight = from.NightLight;
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
