using DispCtrl.Core.Settings;

namespace DispCtrl.Core.Displays;

/// <summary>From the room's light to a unison level.</summary>
/// <remarks>
/// Logarithmic, because eyes are: the step from a dark room to a lamp is as
/// large to them as the one from a lamp to a sunny window, though the second is
/// a hundred times more light. Pure, so it is checked in presetverify.
/// </remarks>
public static class AmbientCurve
{
    public static int Level(double lux, AmbientSettings ambient)
    {
        int dark = Math.Clamp(ambient.DarkLevel, 0, 100);
        int bright = Math.Clamp(ambient.BrightLevel, 0, 100);
        double top = Math.Max(10, ambient.BrightLux);
        double t = Math.Clamp(Math.Log10(1 + Math.Max(0, lux)) / Math.Log10(1 + top), 0, 1);
        return (int)Math.Round(dark + (bright - dark) * t);
    }
}
