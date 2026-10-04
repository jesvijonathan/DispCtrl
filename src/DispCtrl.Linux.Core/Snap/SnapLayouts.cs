using DispCtrl.Linux.X11;

namespace DispCtrl.Linux.Snap;

/// <summary>One zone of a layout, as fractions of the work area.</summary>
public readonly record struct Zone(double Left, double Top, double Right, double Bottom)
{
    /// <summary>The zone on <paramref name="area"/>, with half of <paramref name="gap"/>
    /// on each inner edge and all of it on edges against the work area's own, so
    /// gaps between windows and at the edges are equal. Edges are rounded to
    /// whole pixels from the fractions, so neighbouring zones always meet.</summary>
    public Rect On(Rect area, int gap = 0)
    {
        int Px(double fraction, int origin, int length) => origin + (int)Math.Round(fraction * length);
        int x0 = Px(Left, area.X, area.Width), x1 = Px(Right, area.X, area.Width);
        int y0 = Px(Top, area.Y, area.Height), y1 = Px(Bottom, area.Y, area.Height);
        int half = gap / 2;
        x0 += Left <= 0 ? gap : half;
        y0 += Top <= 0 ? gap : half;
        x1 -= Right >= 1 ? gap : gap - half;
        y1 -= Bottom >= 1 ? gap : gap - half;
        return new Rect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }
}

/// <summary>A named arrangement of zones, as Windows 11's snap layouts.</summary>
public sealed record SnapLayout(string Id, string Name, IReadOnlyList<Zone> Zones);

/// <summary>The layouts offered for a monitor, chosen by its shape the way
/// Windows 11 chooses them: thirds once there is room for three readable
/// windows, a centre column on an ultrawide, stacks on a portrait monitor.</summary>
public static class SnapLayouts
{
    public static readonly SnapLayout Halves = new("halves", "Halves", [new(0, 0, .5, 1), new(.5, 0, 1, 1)]);
    public static readonly SnapLayout TwoThirds = new("two-thirds", "Two thirds and one third", [new(0, 0, 2 / 3d, 1), new(2 / 3d, 0, 1, 1)]);
    public static readonly SnapLayout OneThird = new("one-third", "One third and two thirds", [new(0, 0, 1 / 3d, 1), new(1 / 3d, 0, 1, 1)]);
    public static readonly SnapLayout Thirds = new("thirds", "Thirds", [new(0, 0, 1 / 3d, 1), new(1 / 3d, 0, 2 / 3d, 1), new(2 / 3d, 0, 1, 1)]);
    public static readonly SnapLayout HalfAndQuarters = new("half-quarters", "Half and two quarters",
        [new(0, 0, .5, 1), new(.5, 0, 1, .5), new(.5, .5, 1, 1)]);
    public static readonly SnapLayout Quarters = new("quarters", "Quarters",
        [new(0, 0, .5, .5), new(.5, 0, 1, .5), new(0, .5, .5, 1), new(.5, .5, 1, 1)]);
    public static readonly SnapLayout Centre = new("centre", "Quarter, half, quarter",
        [new(0, 0, .25, 1), new(.25, 0, .75, 1), new(.75, 0, 1, 1)]);
    public static readonly SnapLayout Stacked = new("stacked", "Top and bottom", [new(0, 0, 1, .5), new(0, .5, 1, 1)]);
    public static readonly SnapLayout StackedThirds = new("stacked-thirds", "Three rows",
        [new(0, 0, 1, 1 / 3d), new(0, 1 / 3d, 1, 2 / 3d), new(0, 2 / 3d, 1, 1)]);

    public static IReadOnlyList<SnapLayout> All { get; } =
        [Halves, TwoThirds, OneThird, Thirds, HalfAndQuarters, Quarters, Centre, Stacked, StackedThirds];

    public static SnapLayout? Find(string id) => All.FirstOrDefault(l => l.Id == id);

    /// <summary>Thirds need about 640 px each to hold a readable window.</summary>
    public const int MinThirdWidth = 640;

    public static IReadOnlyList<SnapLayout> For(Rect workArea)
    {
        if (workArea.Height > workArea.Width)
            return workArea.Height >= 3 * MinThirdWidth ? [Stacked, StackedThirds, Quarters] : [Stacked, Quarters];

        double ratio = (double)workArea.Width / workArea.Height;
        var layouts = new List<SnapLayout> { Halves, TwoThirds };
        if (workArea.Width >= 3 * MinThirdWidth) layouts.Add(Thirds);
        layouts.Add(HalfAndQuarters);
        layouts.Add(Quarters);
        if (ratio >= 2.1) layouts.Add(Centre);
        return layouts;
    }
}
