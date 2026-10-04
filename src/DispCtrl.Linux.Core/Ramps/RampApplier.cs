using DispCtrl.Linux.Hardware;
using DispCtrl.Linux.Settings;

namespace DispCtrl.Linux.Ramps;

/// <summary>One output's ramp as a pass left it.</summary>
public sealed record RampChange(string Output, RampTarget Target, bool Written, string? Error);

/// <summary>Puts every output's ramp where the settings say it should be.</summary>
/// <remarks>
/// A ramp is compared with what is really on the CRTC, not with what was last
/// written, so a mode change or hot-plug that resets it is put right on the
/// next pass. An output is only written when its target is not identity, or
/// when DispCtrl warmed or dimmed it earlier (<paramref name="owned"/>): with
/// night light and dimming off, a ramp another tool set (Redshift, a
/// calibration loader) is left alone rather than fought every few seconds.
/// </remarks>
public static class RampApplier
{
    public static IReadOnlyList<RampChange> Apply(LinuxSettings settings, TimeOnly now, ISet<string>? owned = null)
    {
        var changes = new List<RampChange>();
        foreach (var output in GammaRamp.Outputs())
        {
            var target = RampTarget.For(settings, output.Name, now);
            bool ours = owned?.Contains(output.Name) ?? true;
            if (target.IsIdentity && !ours) continue;

            var actual = GammaRamp.Read(output.Name);
            if (actual is { } peaks && target.Matches(peaks))
            {
                if (target.IsIdentity) owned?.Remove(output.Name);
                continue;
            }

            bool ok = GammaRamp.Write(output.Name, target.Red, target.Green, target.Blue, target.Dim, out var error);
            if (ok)
            {
                if (target.IsIdentity) owned?.Remove(output.Name);
                else owned?.Add(output.Name);
            }
            changes.Add(new RampChange(output.Name, target, ok, error));
        }
        return changes;
    }

    /// <summary>Back to identity on every output DispCtrl touched: the engine's
    /// last act, so stopping it never strands a warm or dim screen.</summary>
    public static void Release(IEnumerable<string> owned)
    {
        foreach (var output in owned.ToList()) GammaRamp.Reset(output, out _);
    }
}
