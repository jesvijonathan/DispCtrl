using DispCtrl.Core.Settings;

namespace DispCtrl.Core.Displays;

/// <summary>From the room's light to a unison level.</summary>
/// <remarks>
/// Logarithmic, because eyes are: the step from a dark room to a lamp is as
/// large to them as the one from a lamp to a sunny window, though the second is
/// a hundred times more light. Pure, so it is checked in presetverify.
/// <para>
/// The two ends are settings, and between them runs whatever the person has
/// taught it: every level chosen by hand while following is a knot the curve
/// passes through. Knots are kept monotonic as they are learned - more light
/// never asks for a dimmer desk - so the curve is a plain interpolation.
/// </para>
/// </remarks>
public static class AmbientCurve
{
    /// <summary>Two corrections nearer than this in log lux (about 1.4x) are the same light.</summary>
    private const double SameLight = 0.15;

    /// <summary>Enough knots to bend the curve anywhere a room goes, few enough to read in the file.</summary>
    private const int MaxPoints = 16;

    /// <summary>The perceptual coordinate every comparison is made in.</summary>
    public static double Coordinate(double lux) => Math.Log10(1 + Math.Max(0, lux));

    public static int Level(double lux, AmbientSettings ambient)
    {
        List<(double X, double Level)> knots = Knots(ambient);
        double x = Coordinate(lux);
        if (x <= knots[0].X) return Round(knots[0].Level);
        if (x >= knots[^1].X) return Round(knots[^1].Level);
        for (int i = 1; i < knots.Count; i++)
        {
            if (x > knots[i].X) continue;
            (double x0, double l0) = knots[i - 1];
            (double x1, double l1) = knots[i];
            double t = x1 > x0 ? (x - x0) / (x1 - x0) : 1;
            return Round(l0 + (l1 - l0) * t);
        }
        return Round(knots[^1].Level);
    }

    /// <summary>
    /// Records that somebody wanted <paramref name="level"/> in <paramref name="lux"/>.
    /// </summary>
    /// <remarks>
    /// The newest answer wins. Knots in about the same light are replaced, and
    /// any that would now make the curve fall as the light rises are dropped:
    /// wanting 40% at 30 lux says the old 60% at 20 lux was wrong, not that the
    /// curve should wiggle. Returns false when the curve already gives that level.
    /// </remarks>
    public static bool Learn(AmbientSettings ambient, double lux, int level)
    {
        level = Math.Clamp(level, 0, 100);
        if (Level(lux, ambient) == level) return false;
        double x = Coordinate(lux);
        ambient.Points.RemoveAll(p =>
        {
            double px = Coordinate(p.Lux);
            return Math.Abs(px - x) < SameLight
                || px < x && p.Level > level
                || px > x && p.Level < level;
        });
        ambient.Points.Add(new AmbientPoint { Lux = Math.Round(Math.Max(0, lux), 1), Level = level });
        ambient.Points.Sort((a, b) => a.Lux.CompareTo(b.Lux));
        while (ambient.Points.Count > MaxPoints)
        {
            // The knot nearest the new one says least that the new one does not.
            AmbientPoint nearest = ambient.Points.Where(p => p.Lux != Math.Round(Math.Max(0, lux), 1))
                .MinBy(p => Math.Abs(Coordinate(p.Lux) - x))!;
            ambient.Points.Remove(nearest);
        }
        return true;
    }

    /// <summary>The curve's knots in log lux, low light first.</summary>
    /// <remarks>
    /// The two ends step aside for a learned knot that contradicts them: a
    /// correction is a newer, more particular answer than a default. A file
    /// edited by hand into a curve that falls is evened out by taking the
    /// running maximum, so it still never dims as the room brightens.
    /// </remarks>
    private static List<(double X, double Level)> Knots(AmbientSettings ambient)
    {
        var learned = ambient.Points
            .Select(p => (X: Coordinate(p.Lux), Level: (double)Math.Clamp(p.Level, 0, 100)))
            .OrderBy(k => k.X)
            .ToList();

        double darkX = Coordinate(Math.Max(0, ambient.DarkLux));
        double brightX = Math.Max(darkX + 0.1, Coordinate(Math.Max(1, ambient.BrightLux)));
        double dark = Math.Clamp(ambient.DarkLevel, 0, 100);
        double bright = Math.Clamp(ambient.BrightLevel, 0, 100);

        var knots = new List<(double X, double Level)>();
        if (!learned.Any(k => Conflicts(k, darkX, dark))) knots.Add((darkX, dark));
        if (!learned.Any(k => Conflicts(k, brightX, bright))) knots.Add((brightX, bright));
        foreach (var k in learned)
            if (!knots.Any(e => Math.Abs(e.X - k.X) < 1e-9)) knots.Add(k);
        if (knots.Count == 0) knots.Add((darkX, dark));
        knots.Sort((a, b) => a.X.CompareTo(b.X));

        // Only the learned knots are held to rising: two ends set the wrong way
        // round have always meant a desk that dims as the room brightens.
        if (learned.Count > 0)
            for (int i = 1; i < knots.Count; i++)
                if (knots[i].Level < knots[i - 1].Level) knots[i] = (knots[i].X, knots[i - 1].Level);
        return knots;
    }

    private static bool Conflicts((double X, double Level) learned, double x, double level) =>
        Math.Abs(learned.X - x) < SameLight && learned.Level != level
        || learned.X < x && learned.Level > level
        || learned.X > x && learned.Level < level;

    private static int Round(double level) => (int)Math.Round(Math.Clamp(level, 0, 100));
}
