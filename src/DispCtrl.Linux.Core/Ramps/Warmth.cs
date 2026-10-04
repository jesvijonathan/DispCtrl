namespace DispCtrl.Linux.Ramps;

/// <summary>Night light strength to colour temperature to channel multipliers.</summary>
/// <remarks>
/// The same fit and normalisation as the Windows side's <c>NightLight.Raw</c> and
/// <c>Multipliers</c> (DispCtrl.Core cannot be referenced: it targets Windows), so
/// a strength means the same colour on both. X has no clamp on how far a ramp may
/// leave identity, so the full range is Windows' unlocked one, 6500 K to 1900 K.
/// </remarks>
public static class Warmth
{
    public const double NeutralKelvin = 6500;
    public const double WarmestKelvin = 1900;

    public static double KelvinFor(int strength) =>
        NeutralKelvin - ((NeutralKelvin - WarmestKelvin) * Math.Clamp(strength, 0, 100) / 100.0);

    /// <summary>Channel multipliers relative to 6500 K, so 6500 K is exactly
    /// identity and nothing changes until there is something to warm.</summary>
    public static (double R, double G, double B) Multipliers(double kelvin)
    {
        (double nr, double ng, double nb) = Raw(NeutralKelvin);
        (double r, double g, double b) = Raw(kelvin);
        return (Math.Min(1, r / nr), Math.Min(1, g / ng), Math.Min(1, b / nb));
    }

    private static (double R, double G, double B) Raw(double kelvin)
    {
        double t = Math.Clamp(kelvin, 1000, 40000) / 100.0;

        double r = t <= 66
            ? 255
            : 329.698727446 * Math.Pow(t - 60, -0.1332047592);

        double g = t <= 66
            ? (99.4708025861 * Math.Log(t)) - 161.1195681661
            : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);

        double b = t >= 66
            ? 255
            : t <= 19
                ? 0
                : (138.5177312231 * Math.Log(t - 10)) - 305.0447927307;

        return (Math.Clamp(r, 1, 255) / 255.0,
                Math.Clamp(g, 1, 255) / 255.0,
                Math.Clamp(b, 1, 255) / 255.0);
    }
}
