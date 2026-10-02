namespace DispCtrl.Core.Displays;

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
    public readonly record struct Panel(string Token, int X, int Y, int Width, int Height, bool IsPrimary = false)
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

    /// <summary>True when no displays overlap and they form one desktop, every one reachable from every other.</summary>
    /// <remarks>
    /// "Each flush against at least one other" was the old test, and with three
    /// or more displays it passed layouts Windows refuses: two pairs, each
    /// touching only its partner, satisfy it while the desktop is in two pieces.
    /// </remarks>
    public static bool IsValid(IReadOnlyList<Panel> panels)
    {
        if (panels.Count < 2) return true;

        for (int i = 0; i < panels.Count; i++)
            if (FindOverlap(panels, i) is not null) return false;

        return Components(panels).Count == 1;
    }

    /// <summary>
    /// The arrangement with every piece joined back to the primary's, each
    /// slid the shortest way until it is flush.
    /// </summary>
    /// <remarks>
    /// Dragging the middle display of three out of the row left the outer two
    /// touching nothing: every slot the dragged display could take was legal
    /// for it, and the layout as a whole was not. Windows' own page closes such
    /// gaps; this is the same, applied when the display is dropped, so the
    /// drag itself still only ever offers real positions. Pieces join the one
    /// holding the primary (the desktop origin, which must not move), cheapest
    /// move first, and a piece moves whole so displays already arranged
    /// together stay together.
    /// </remarks>
    public static List<Panel> Close(IReadOnlyList<Panel> panels)
    {
        var result = new List<Panel>(panels);
        for (int pass = 0; pass < result.Count; pass++)
        {
            List<List<int>> pieces = Components(result);
            if (pieces.Count <= 1) break;
            List<int> main = pieces.FirstOrDefault(c => c.Any(i => result[i].IsPrimary))
                ?? pieces.OrderByDescending(c => c.Count).First();

            (int Dx, int Dy, long Cost, List<int> Piece)? best = null;
            foreach (List<int> piece in pieces)
            {
                if (ReferenceEquals(piece, main)) continue;
                foreach ((int dx, int dy) in Joins(result, piece, main))
                {
                    if (!FitsAfter(result, piece, dx, dy)) continue;
                    long cost = ((long)dx * dx) + ((long)dy * dy);
                    if (best is null || cost < best.Value.Cost) best = (dx, dy, cost, piece);
                }
            }
            if (best is not { } move) break;
            foreach (int i in move.Piece)
                result[i] = result[i] with { X = result[i].X + move.Dx, Y = result[i].Y + move.Dy };
        }
        return Normalise(result);
    }

    /// <summary>Translations that put some display of <paramref name="piece"/> flush against one of <paramref name="main"/>.</summary>
    private static IEnumerable<(int Dx, int Dy)> Joins(List<Panel> panels, List<int> piece, List<int> main)
    {
        foreach (int i in piece)
        foreach (int j in main)
        {
            Panel a = panels[i], b = panels[j];
            // Straight across when they already share a span: the shortest
            // move, and it keeps the alignment somebody chose.
            if (a.Y < b.Bottom && b.Y < a.Bottom)
            {
                yield return (b.Right - a.X, 0);
                yield return (b.X - a.Right, 0);
            }
            if (a.X < b.Right && b.X < a.Right)
            {
                yield return (0, b.Bottom - a.Y);
                yield return (0, b.Y - a.Bottom);
            }
            // Otherwise to each side of it, at its edges and centre.
            foreach (int y in new[] { b.Y, b.Y + ((b.Height - a.Height) / 2), b.Bottom - a.Height })
            {
                yield return (b.Right - a.X, y - a.Y);
                yield return (b.X - a.Width - a.X, y - a.Y);
            }
            foreach (int x in new[] { b.X, b.X + ((b.Width - a.Width) / 2), b.Right - a.Width })
            {
                yield return (x - a.X, b.Bottom - a.Y);
                yield return (x - a.X, b.Y - a.Height - a.Y);
            }
        }
    }

    private static bool FitsAfter(List<Panel> panels, List<int> piece, int dx, int dy)
    {
        var moving = piece.ToHashSet();
        foreach (int i in piece)
        {
            Panel a = panels[i] with { X = panels[i].X + dx, Y = panels[i].Y + dy };
            for (int j = 0; j < panels.Count; j++)
            {
                if (moving.Contains(j)) continue;
                Panel b = panels[j];
                if (a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom) return false;
            }
        }
        return true;
    }

    /// <summary>The displays in connected pieces: a piece is everything reachable across shared edges.</summary>
    public static List<List<int>> Components(IReadOnlyList<Panel> panels)
    {
        var pieces = new List<List<int>>();
        var seen = new bool[panels.Count];
        for (int start = 0; start < panels.Count; start++)
        {
            if (seen[start]) continue;
            var piece = new List<int>();
            var stack = new Stack<int>();
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                piece.Add(i);
                for (int j = 0; j < panels.Count; j++)
                {
                    if (seen[j] || !Touch(panels[i], panels[j])) continue;
                    seen[j] = true;
                    stack.Push(j);
                }
            }
            pieces.Add(piece);
        }
        return pieces;
    }

    private static bool Touch(Panel a, Panel b)
    {
        bool xOverlap = a.X < b.Right && b.X < a.Right;
        bool yOverlap = a.Y < b.Bottom && b.Y < a.Bottom;
        bool edgeX = a.X == b.Right || b.X == a.Right;
        bool edgeY = a.Y == b.Bottom || b.Y == a.Bottom;
        return (edgeX && yOverlap) || (edgeY && xOverlap);
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
    /// Shifts the arrangement so the primary display's top-left sits at (0,0).
    /// </summary>
    /// <remarks>
    /// The desktop origin <em>is</em> the primary display's top-left corner, so
    /// every other display's position is expressed relative to it — negative
    /// coordinates are normal and correct for anything above or to the left.
    /// <para>
    /// Anchoring on the bounding box instead is the bug this replaced: dragging
    /// a secondary display up or left pushed the primary off (0,0), and Windows
    /// rejected the whole arrangement without saying why. It read as Apply
    /// silently doing nothing.
    /// </para>
    /// </remarks>
    private static List<Panel> Normalise(List<Panel> panels)
    {
        if (panels.Count == 0) return panels;

        int index = panels.FindIndex(p => p.IsPrimary);

        // No panel claims to be primary — fall back to the bounding box, which
        // at least keeps the layout in positive space.
        int anchorX, anchorY;
        if (index >= 0)
        {
            anchorX = panels[index].X;
            anchorY = panels[index].Y;
        }
        else
        {
            anchorX = int.MaxValue;
            anchorY = int.MaxValue;
            foreach (Panel p in panels)
            {
                anchorX = Math.Min(anchorX, p.X);
                anchorY = Math.Min(anchorY, p.Y);
            }
        }

        if (anchorX == 0 && anchorY == 0) return panels;

        for (int i = 0; i < panels.Count; i++)
            panels[i] = panels[i] with { X = panels[i].X - anchorX, Y = panels[i].Y - anchorY };

        return panels;
    }
}
