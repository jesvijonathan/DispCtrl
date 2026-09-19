namespace DisplCtrl.Core.Displays;

/// <summary>
/// Lays an arrangement out at the displays' real physical sizes.
/// </summary>
/// <remarks>
/// Windows draws its arrangement diagram in raw pixels, which makes a 14-inch
/// 2880x1800 laptop panel look wider than the 24-inch monitor beside it. That
/// is the opposite of the truth, and showing where things physically are is the
/// diagram's entire job.
/// <para>
/// Desktop coordinates cannot be rescaled into millimetres directly, because a
/// pixel is a different real size on each panel — any single mapping of the
/// coordinate space gets one display right and stretches the other. The first
/// attempt here did exactly that and drew the laptop nearly twice its real
/// height.
/// </para>
/// <para>
/// So the sizes are taken from the EDID and nothing else, and only the
/// <em>relationships</em> come from the pixel arrangement: each display is hung
/// off a neighbour it touches, on the side it touches, offset along that edge
/// by the same fraction as in pixel space. Every panel is then its true size
/// and still meets its neighbour, which is what two real monitors of different
/// heights do on a real desk.
/// </para>
/// </remarks>
public static class PhysicalLayout
{
    /// <summary>One display's rectangle plus how big a pixel really is on it.</summary>
    /// <param name="MmPerPxX">Millimetres per pixel across. Zero when unknown.</param>
    /// <param name="MmPerPxY">Millimetres per pixel down. Zero when unknown.</param>
    public readonly record struct Panel(
        string Token, int X, int Y, int Width, int Height, double MmPerPxX, double MmPerPxY)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;

        public bool HasSize => MmPerPxX > 0 && MmPerPxY > 0;

        public double WidthMm => Width * MmPerPxX;
        public double HeightMm => Height * MmPerPxY;
    }

    /// <summary>A display's place in the diagram, in millimetres.</summary>
    public readonly record struct Placed(string Token, double X, double Y, double Width, double Height);

    /// <summary>
    /// Maps an arrangement into millimetre space.
    /// </summary>
    /// <remarks>
    /// Falls back to pixel space when any display does not report a physical
    /// size, rather than mixing the two. A diagram with one panel in
    /// millimetres and its neighbour in pixels would be wrong <em>and</em>
    /// silent about it.
    /// </remarks>
    public static List<Placed> Resolve(IReadOnlyList<Panel> panels)
    {
        if (panels.Count == 0) return [];

        foreach (Panel p in panels)
            if (!p.HasSize) return InPixels(panels);

        var placed = new Dictionary<string, Placed>(panels.Count);

        // Anchored on the widest display: the largest panel moves least when
        // its neighbours are hung off it, so the diagram stays put as displays
        // are dragged around it.
        int anchor = 0;
        for (int i = 1; i < panels.Count; i++)
            if (panels[i].WidthMm > panels[anchor].WidthMm) anchor = i;

        placed[panels[anchor].Token] = new Placed(
            panels[anchor].Token, 0, 0, panels[anchor].WidthMm, panels[anchor].HeightMm);

        // Breadth-first over "touches", so every display is positioned relative
        // to one already placed rather than to the coordinate space.
        var queue = new Queue<int>();
        queue.Enqueue(anchor);

        while (queue.Count > 0)
        {
            int from = queue.Dequeue();

            for (int to = 0; to < panels.Count; to++)
            {
                if (to == from || placed.ContainsKey(panels[to].Token)) continue;
                if (!Touches(panels[from], panels[to])) continue;

                placed[panels[to].Token] = Hang(panels[from], panels[to], placed[panels[from].Token]);
                queue.Enqueue(to);
            }
        }

        // A display touching nothing — possible mid-drag, before the solver has
        // pulled it back in. Positioned by its pixel offset from the anchor so
        // it at least moves with the cursor.
        foreach (Panel p in panels)
        {
            if (placed.ContainsKey(p.Token)) continue;

            Placed root = placed[panels[anchor].Token];
            placed[p.Token] = new Placed(
                p.Token,
                root.X + ((p.X - panels[anchor].X) * p.MmPerPxX),
                root.Y + ((p.Y - panels[anchor].Y) * p.MmPerPxY),
                p.WidthMm, p.HeightMm);
        }

        return Normalise(panels, placed);
    }

    private static List<Placed> InPixels(IReadOnlyList<Panel> panels)
    {
        var result = new List<Placed>(panels.Count);
        foreach (Panel p in panels) result.Add(new Placed(p.Token, p.X, p.Y, p.Width, p.Height));

        return result;
    }

    /// <summary>True when two displays share a run of edge, not merely a corner.</summary>
    private static bool Touches(Panel a, Panel b)
    {
        bool xOverlap = a.X < b.Right && b.X < a.Right;
        bool yOverlap = a.Y < b.Bottom && b.Y < a.Bottom;

        bool edgeX = a.X == b.Right || b.X == a.Right;
        bool edgeY = a.Y == b.Bottom || b.Y == a.Bottom;

        return (edgeX && yOverlap) || (edgeY && xOverlap);
    }

    /// <summary>
    /// Places <paramref name="b"/> against the side of <paramref name="a"/> it
    /// touches in pixel space.
    /// </summary>
    /// <remarks>
    /// The offset along the shared edge is kept as a fraction of the anchor's
    /// extent rather than as an absolute distance, because the two panels
    /// measure that distance in different numbers of pixels. A display centred
    /// on its neighbour stays centred; one aligned to the top stays at the top.
    /// <para>
    /// Public because the arrangement surface asks the same question of a
    /// position nothing is standing in yet: where would the dragged display be
    /// drawn if it landed in <em>that</em> slot? Answering it with this rather
    /// than a copy of it is what keeps the slot the pointer is nearest and the
    /// place the tile is drawn from disagreeing.
    /// </para>
    /// </remarks>
    public static Placed Hang(Panel a, Panel b, Placed anchor)
    {
        bool toTheRight = b.X == a.Right;
        bool toTheLeft = b.Right == a.X;

        if (toTheRight || toTheLeft)
        {
            double offset = AlignedOffset(b.Y - a.Y, a.Height, b.Height, anchor.Height, b.HeightMm);
            double x = toTheRight ? anchor.X + anchor.Width : anchor.X - b.WidthMm;

            return new Placed(b.Token, x, anchor.Y + offset, b.WidthMm, b.HeightMm);
        }

        bool below = b.Y == a.Bottom;
        double across = AlignedOffset(b.X - a.X, a.Width, b.Width, anchor.Width, b.WidthMm);
        double y = below ? anchor.Y + anchor.Height : anchor.Y - b.HeightMm;

        return new Placed(b.Token, anchor.X + across, y, b.WidthMm, b.HeightMm);
    }

    private static double AlignedOffset(int delta, int extent, int size, double physicalExtent, double physicalSize)
    {
        if (delta == 0) return 0;
        if (delta == extent - size) return physicalExtent - physicalSize;
        if (delta == (extent - size) / 2) return (physicalExtent - physicalSize) / 2;
        return delta / (double)extent * physicalExtent;
    }

    private static List<Placed> Normalise(IReadOnlyList<Panel> panels, Dictionary<string, Placed> placed)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        foreach (Placed p in placed.Values)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
        }

        // Returned in the caller's order, not the dictionary's, so the diagram
        // does not reshuffle between layouts.
        var result = new List<Placed>(panels.Count);
        foreach (Panel panel in panels)
        {
            Placed p = placed[panel.Token];
            result.Add(p with { X = p.X - minX, Y = p.Y - minY });
        }

        return result;
    }
}
