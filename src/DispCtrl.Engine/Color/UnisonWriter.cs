using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

namespace DispCtrl.Engine.Color;

/// <summary>Puts every display at the unison level, each inside its own range.</summary>
/// <remarks>
/// One copy for the hotkeys and for ambient light, so the two cannot disagree
/// about where a level puts a display. Blocks for a DDC/CI round trip per
/// monitor: never on a thread that draws.
/// </remarks>
internal static class UnisonWriter
{
    /// <returns>True when a display joined unison and its baseline wants saving.</returns>
    public static bool Apply(DispCtrlSettings settings, IEnumerable<DisplayInfo> displays)
    {
        bool changed = false;
        foreach (DisplayInfo d in displays)
        {
            // Left out of unison: its brightness is its own.
            if (!settings.For(d.Token).InUnison) continue;
            BrightnessRange range = Brightness.Read(d);
            if (!range.Supported) continue;
            MonitorSettings m = settings.For(d.Token);
            if (!m.HasBrightnessRange && m.BrightnessBaseline <= 0)
            {
                // Joins without a jump: a baseline worked back from where it is.
                m.BrightnessBaseline = UnisonResume.Enable(settings.Global.UnisonLevel, [range.Percent], [0], 0).Baselines[0];
                changed = true;
            }
            int target = UnisonResume.Target(m, settings.Global.UnisonCalibrated, settings.Global.UnisonLevel);
            _ = Brightness.Write(d, range.FromPercent(target));
        }
        return changed;
    }
}
