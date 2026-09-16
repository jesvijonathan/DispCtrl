namespace Umbra.Core.Displays;

/// <summary>
/// Works out a monitor arrangement Windows will actually accept.
/// </summary>
/// <remarks>
/// Windows requires every display to sit flush against at least one other:
/// no gap, and no overlap. It enforces this at apply time and refuses the whole
/// arrangement without saying which display is at fault, so the only useful
/// place to enforce it is while the user is still dragging.
/// <para>
/// Pure geometry, deliberately free of any UI type, so the rules can be tested
/// directly instead of only through a pointer device.
/// </para>
/// </remarks>
public static class ArrangementSolver
{
    /// <summary>One display's rectangle in desktop coordinates.</summary>
    public readonly record struct Panel(string Token, int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;
        public int CentreX => X + (Width / 2);
        public int CentreY => Y + (Height / 2);
    }

    /// <summary>
    /// Iteration cap. Pushing a display clear of one neighbour can push it into
    /// another, so this settles rather than spinning on a pathological layout.
    /// </summary>
    private const int MaxPasses = 8;

    /// <summary>
    /// Returns the arrangement with <paramref name="movedToken"/> resolved to a
    /// flush position, and the whole layout normalised to the origin.
    /// </summary>
    public static List<Panel> Resolve(IReadOnlyList<Panel> panels, string movedToken)
    {
        var result = new List<Panel>(panels);
        if (result.Count < 2) return Normalise(result);

        int index = result.FindIndex(p => p.Token == movedToken);
        if (index < 0) return Normalise(result);

        for (int pass = 0; pass < MaxPasses; pass++)
        {
            Panel moved = result[index];

            if (FindOverlap(result, index) is { } overlapping)
            {
                result[index] = PushClear(moved, overlapping);
                continue;
            }

            if (SharesEdge(result, index)) break;

            result[index] = AttachToNearest(result, index);
        }

        return Normalise(result);
    }

    /// <summary>True when every display is flush against at least one other.</summary>
    public static bool IsValid(IReadOnlyList<Panel> panels)
    {
        if (panels.Count < 2) return true;

        for (int i = 0; i < panels.Count; i++)
        {
            if (FindOverlap(panels, i) is not null) return false;
            if (!SharesEdge(panels, i)) return false;
        }

        return true;
    }

    private static Panel? FindOverlap(IReadOnlyList<Panel> panels, int index)
    {
        Panel a = panels[index];

        for (int i = 0; i < panels.Count; i++)
        {
            if (i == index) continue;
            Panel b = panels[i];

            if (a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom)
                return b;
        }

        return null;
    }

    /// <remarks>
    /// A corner touch does not count: two displays meeting at a point leave no
    /// run of pixels for the cursor to cross, and Windows treats that as a gap.
    /// </remarks>
    private static bool SharesEdge(IReadOnlyList<Panel> panels, int index)
    {
        Panel a = panels[index];

        for (int i = 0; i < panels.Count; i++)
        {
            if (i == index) continue;
            Panel b = panels[i];

            bool xOverlap = a.X < b.Right && b.X < a.Right;
            bool yOverlap = a.Y < b.Bottom && b.Y < a.Bottom;

            bool edgeX = a.X == b.Right || b.X == a.Right;
            bool edgeY = a.Y == b.Bottom || b.Y == a.Bottom;

            if ((edgeX && yOverlap) || (edgeY && xOverlap)) return true;
        }

        return false;
    }

    /// <summary>
    /// Slides an overlapping display out to the nearest flush position.
    /// </summary>
    /// <remarks>
    /// Along the axis of least penetration, so it settles on the side it was
    /// already mostly on rather than jumping across its neighbour.
    /// </remarks>
    private static Panel PushClear(Panel moved, Panel other)
    {
        int toLeft = other.X - moved.Width - moved.X;
        int toRight = other.Right - moved.X;
        int toTop = other.Y - moved.Height - moved.Y;
        int toBottom = other.Bottom - moved.Y;

        int dx = Math.Abs(toLeft) <= Math.Abs(toRight) ? toLeft : toRight;
        int dy = Math.Abs(toTop) <= Math.Abs(toBottom) ? toTop : toBottom;

        return Math.Abs(dx) <= Math.Abs(dy)
            ? moved with { X = moved.X + dx }
            : moved with { Y = moved.Y + dy };
    }

    private static Panel AttachToNearest(IReadOnlyList<Panel> panels, int index)
    {
        Panel moved = panels[index];

        Panel? nearest = null;
        long best = long.MaxValue;

        for (int i = 0; i < panels.Count; i++)
        {
            if (i == index) continue;
            Panel b = panels[i];

            long dx = moved.CentreX - b.CentreX;
            long dy = moved.CentreY - b.CentreY;
            long distance = (dx * dx) + (dy * dy);

            if (distance >= best) continue;
            best = distance;
            nearest = b;
        }

        if (nearest is not { } target) return moved;

        if (Math.Abs(moved.CentreX - target.CentreX) >= Math.Abs(moved.CentreY - target.CentreY))
        {
            int x = moved.CentreX >= target.CentreX ? target.Right : target.X - moved.Width;
            // Leave a real run of shared edge rather than a single-pixel corner.
            int y = Math.Clamp(moved.Y, target.Y - moved.Height + 1, target.Bottom - 1);
            return moved with { X = x, Y = y };
        }
        else
        {
            int y = moved.CentreY >= target.CentreY ? target.Bottom : target.Y - moved.Height;
            int x = Math.Clamp(moved.X, target.X - moved.Width + 1, target.Right - 1);
            return moved with { X = x, Y = y };
        }
    }

    /// <summary>
    /// Shifts the arrangement so its leftmost and topmost edges sit at zero.
    /// </summary>
    /// <remarks>
    /// Windows anchors the desktop origin at the primary display and refuses a
    /// layout whose origin has drifted.
    /// </remarks>
    private static List<Panel> Normalise(List<Panel> panels)
    {
        if (panels.Count == 0) return panels;

        int minX = int.MaxValue, minY = int.MaxValue;
        foreach (Panel p in panels)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
        }

        if (minX == 0 && minY == 0) return panels;

        for (int i = 0; i < panels.Count; i++)
            panels[i] = panels[i] with { X = panels[i].X - minX, Y = panels[i].Y - minY };

        return panels;
    }
}
