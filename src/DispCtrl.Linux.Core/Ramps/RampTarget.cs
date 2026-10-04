using DispCtrl.Linux.Hardware;
using DispCtrl.Linux.Settings;

namespace DispCtrl.Linux.Ramps;

/// <summary>What one output's ramp should be: channel multipliers from warmth,
/// and a dim factor, composed into a single write.</summary>
public readonly record struct RampTarget(double Red, double Green, double Blue, double Dim)
{
    /// <summary>Software dimming never goes below this. A ramp at zero is a black
    /// screen with no way to see the control that would undo it.</summary>
    public const double LowestDim = 0.1;

    public static readonly RampTarget Identity = new(1, 1, 1, 1);

    public bool IsIdentity => Peaks.IsIdentity;

    public RampPeaks Peaks => new(Red * Dim, Green * Dim, Blue * Dim);

    /// <summary>The target for <paramref name="output"/> at <paramref name="now"/>.</summary>
    public static RampTarget For(LinuxSettings settings, string output, TimeOnly now)
    {
        double dim = settings.Dim.TryGetValue(output, out var d) ? Math.Clamp(d, LowestDim, 1) : 1;
        if (!settings.NightLight.ActiveAt(now)) return new RampTarget(1, 1, 1, dim);
        var (r, g, b) = Warmth.Multipliers(Warmth.KelvinFor(settings.NightLight.Strength));
        return new RampTarget(r, g, b, dim);
    }

    /// <summary>Whether a ramp read back is already this target, within what a
    /// 16-bit ramp can hold.</summary>
    public bool Matches(RampPeaks actual) =>
        Math.Abs(actual.Red - Peaks.Red) < 0.002
        && Math.Abs(actual.Green - Peaks.Green) < 0.002
        && Math.Abs(actual.Blue - Peaks.Blue) < 0.002;
}
