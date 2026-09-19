namespace DispCtrl.Core.Displays;

/// <summary>
/// Every position a display may be dragged to, as a countable set.
/// </summary>
/// <remarks>
/// Windows accepts an arrangement only when each display sits flush against at
/// least one other — no gap, no overlap, and a corner touch does not count. That
/// makes the legal positions for one display a small, finite set rather than a
/// continuous field: each side of each neighbour, at each of three alignments
/// along that side.
/// <para>
/// Enumerating them is what lets the drag be discrete, and discrete is the point.
/// Dragging freely and correcting on drop meant the user spent the whole drag
/// aiming at positions that were going to be rejected. Correcting continuously
/// — pushing the display clear and re-attaching it on every pointer move — was
/// worse: it clung to whatever it had last been pushed against, and moving it
/// anywhere else was a fight with a solver that kept answering the previous
/// question. Snapping between slots is neither. The display is in a legal place
/// at every moment of the drag, there are only ever a handful to choose from,
/// and the one it is going to is the one nearest the pointer.
/// </para>
/// <para>
/// Pure geometry, deliberately free of any UI type, because synthetic pointer
/// input does not reach a WinUI canvas — a drag cannot be tested through
/// automation, so what runs during one has to be testable on its own.
/// </para>
/// </remarks>
public static class ArrangementSlots
{
    /// <summary>Which side of the neighbour the display sits on.</summary>
    public enum Side { Left, Right, Above, Below }

    /// <summary>How it lines up along that side.</summary>
    /// <remarks>
    /// <see cref="Centre"/> is the one that makes unequal panels look deliberate:
    /// a 14-inch laptop beside a 24-inch monitor is normally set at eye level,
    /// which is neither of the two edges.
    /// </remarks>
    public enum Align { Start, Centre, End }

    /// <summary>One legal position, and the neighbour that makes it legal.</summary>
    public readonly record struct Slot(int X, int Y, string Against, Side Side, Align Align);

    private static readonly Side[] Sides = [Side.Right, Side.Left, Side.Below, Side.Above];
    private static readonly Align[] Alignments = [Align.Start, Align.Centre, Align.End];

    /// <summary>
    /// Where <paramref name="movingToken"/> may legally sit, given where every
    /// other display is.
    /// </summary>
    /// <remarks>
    /// Positions that would overlap a <em>third</em> display are dropped, not
    /// merely the neighbour being hung off: a slot to the right of one monitor
    /// can sit on top of another, and offering it would produce an arrangement
    /// Windows refuses without saying why.
    /// <para>
    /// Duplicates are dropped too. Two alignments coincide whenever the panels
    /// match on that axis, and a pair of identical monitors side by side would
    /// otherwise offer the same position three times over.
    /// </para>
    /// </remarks>
    public static List<Slot> For(IReadOnlyList<ArrangementSolver.Panel> panels, string movingToken)
    {
        var slots = new List<Slot>();

        int index = -1;
        for (int i = 0; i < panels.Count; i++)
            if (panels[i].Token == movingToken) { index = i; break; }

        // One display has nowhere to be: there is nothing for it to be flush
        // against, and its position is the desktop origin by definition.
        if (index < 0 || panels.Count < 2) return slots;

        ArrangementSolver.Panel moving = panels[index];
        var seen = new HashSet<(int X, int Y)>();

        for (int i = 0; i < panels.Count; i++)
        {
            if (i == index) continue;
            ArrangementSolver.Panel other = panels[i];

            foreach (Side side in Sides)
            {
                foreach (Align align in Alignments)
                {
                    (int x, int y) = Place(moving, other, side, align);

                    if (!seen.Add((x, y))) continue;
                    if (Overlaps(panels, index, x, y)) continue;

                    slots.Add(new Slot(x, y, other.Token, side, align));
                }
            }
        }

        return slots;
    }

    /// <summary>The slot nearest a point, in desktop pixels.</summary>
    /// <remarks>
    /// Offered for the command line and for checks. The panel ranks them in the
    /// millimetre space the diagram is drawn in instead, because that is the
    /// space the pointer is moving through — a pixel is a different real size on
    /// each display, so nearest in pixels is not what the eye is aiming at.
    /// </remarks>
    public static Slot? Nearest(IReadOnlyList<Slot> slots, int x, int y)
    {
        Slot? best = null;
        long nearest = long.MaxValue;

        foreach (Slot s in slots)
        {
            long dx = s.X - x;
            long dy = s.Y - y;
            long distance = (dx * dx) + (dy * dy);

            if (distance >= nearest) continue;
            nearest = distance;
            best = s;
        }

        return best;
    }

    private static (int X, int Y) Place(
        ArrangementSolver.Panel moving, ArrangementSolver.Panel other, Side side, Align align) => side switch
    {
        Side.Right => (other.Right, Along(other.Y, other.Height, moving.Height, align)),
        Side.Left => (other.X - moving.Width, Along(other.Y, other.Height, moving.Height, align)),
        Side.Below => (Along(other.X, other.Width, moving.Width, align), other.Bottom),
        _ => (Along(other.X, other.Width, moving.Width, align), other.Y - moving.Height),
    };

    private static int Along(int start, int extent, int size, Align align) => align switch
    {
        Align.Start => start,
        Align.Centre => start + ((extent - size) / 2),
        _ => start + extent - size,
    };

    private static bool Overlaps(IReadOnlyList<ArrangementSolver.Panel> panels, int moving, int x, int y)
    {
        ArrangementSolver.Panel m = panels[moving] with { X = x, Y = y };

        for (int i = 0; i < panels.Count; i++)
        {
            if (i == moving) continue;
            ArrangementSolver.Panel o = panels[i];

            if (m.X < o.Right && o.X < m.Right && m.Y < o.Bottom && o.Y < m.Bottom) return true;
        }

        return false;
    }
}
