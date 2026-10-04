using DispCtrl.Linux.X11;

namespace DispCtrl.Linux.Snap;

/// <summary>Which zone of which layout, by index.</summary>
public readonly record struct ZoneRef(int Layout, int Zone);

/// <summary>Where the snap layouts panel and its pieces are on a monitor, in
/// root coordinates. Pure geometry: a drag is driven by the pointer alone, and
/// cannot be exercised by automation, so this is what the checks cover.</summary>
public sealed class SnapPanel
{
    public const int Padding = 12;
    public const int TileGap = 14;
    public const int TileHeight = 60;
    public const int TileInset = 4;
    public const int ZoneGap = 4;
    public const int TopOffset = 12;

    /// <summary>The bar shown at the top centre while a window is dragged:
    /// moving onto it opens the panel, as on Windows 11.</summary>
    public const int HandleWidth = 160, HandleHeight = 8;

    private SnapPanel(MonitorInfo monitor, IReadOnlyList<SnapLayout> layouts, Rect panel, IReadOnlyList<Rect> tiles, Rect handle)
    {
        Monitor = monitor;
        Layouts = layouts;
        Bounds = panel;
        Tiles = tiles;
        Handle = handle;
    }

    public MonitorInfo Monitor { get; }
    public IReadOnlyList<SnapLayout> Layouts { get; }
    public Rect Bounds { get; }
    public IReadOnlyList<Rect> Tiles { get; }
    public Rect Handle { get; }

    /// <summary>Where the pointer opens the panel: the bar with a generous margin,
    /// since the pointer is carrying a window and aim is rough.</summary>
    public Rect HandleHotspot => new(Handle.X - 60, Monitor.Bounds.Y, Handle.Width + 120, Handle.Bottom - Monitor.Bounds.Y + 48);

    /// <summary>Where the open panel stays open: the panel and a margin, so
    /// leaving it by a few pixels on the way to a zone does not close it.</summary>
    public Rect KeepOpen => new(Bounds.X - 32, Monitor.Bounds.Y, Bounds.Width + 64, Bounds.Bottom - Monitor.Bounds.Y + 32);

    public static SnapPanel For(MonitorInfo monitor)
    {
        var area = monitor.WorkArea;
        var layouts = SnapLayouts.For(area);
        double aspect = Math.Clamp((double)area.Width / area.Height, 0.5, 2.4);
        int tileWidth = (int)Math.Round(TileHeight * aspect);
        int width = Padding * 2 + layouts.Count * tileWidth + (layouts.Count - 1) * TileGap;
        int height = Padding * 2 + TileHeight;
        int x = area.X + (area.Width - width) / 2;
        int y = area.Y + TopOffset;
        var tiles = new List<Rect>();
        for (int i = 0; i < layouts.Count; i++)
            tiles.Add(new Rect(x + Padding + i * (tileWidth + TileGap), y + Padding, tileWidth, TileHeight));
        var handle = new Rect(area.X + (area.Width - HandleWidth) / 2, area.Y + TopOffset, HandleWidth, HandleHeight);
        return new SnapPanel(monitor, layouts, new Rect(x, y, width, height), tiles, handle);
    }

    /// <summary>A zone's rectangle inside its tile, as drawn.</summary>
    public Rect ZoneInTile(ZoneRef zone)
    {
        var tile = Tiles[zone.Layout];
        var z = Layouts[zone.Layout].Zones[zone.Zone];
        // Zones sit inside the tile's own frame, which is what keeps one
        // layout's zones from reading as part of the next.
        var inner = new Rect(tile.X + TileInset, tile.Y + TileInset, tile.Width - 2 * TileInset, tile.Height - 2 * TileInset);
        int x0 = inner.X + (int)Math.Round(z.Left * inner.Width), x1 = inner.X + (int)Math.Round(z.Right * inner.Width);
        int y0 = inner.Y + (int)Math.Round(z.Top * inner.Height), y1 = inner.Y + (int)Math.Round(z.Bottom * inner.Height);
        // Half a gap on inner edges, none on the tile's own, so zones read as one shape.
        int g = ZoneGap / 2;
        if (z.Left > 0) x0 += g;
        if (z.Right < 1) x1 -= g;
        if (z.Top > 0) y0 += g;
        if (z.Bottom < 1) y1 -= g;
        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    public IEnumerable<ZoneRef> AllZones()
    {
        for (int l = 0; l < Layouts.Count; l++)
            for (int z = 0; z < Layouts[l].Zones.Count; z++)
                yield return new ZoneRef(l, z);
    }

    /// <summary>The zone under the pointer, or within a few pixels of it: gaps
    /// between zones are thin, and a release in one should not lose the snap.</summary>
    public ZoneRef? HitTest(int x, int y)
    {
        ZoneRef? best = null;
        long bestDistance = long.MaxValue;
        foreach (var zone in AllZones())
        {
            var r = ZoneInTile(zone);
            if (r.Contains(x, y)) return zone;
            if (!r.Inflate(ZoneGap + 2).Contains(x, y)) continue;
            long d = (long)(r.CenterX - x) * (r.CenterX - x) + (long)(r.CenterY - y) * (r.CenterY - y);
            if (d < bestDistance) { best = zone; bestDistance = d; }
        }
        return best;
    }

    /// <summary>Where a window in <paramref name="zone"/> goes on the monitor.</summary>
    public Rect Target(ZoneRef zone, int gap) => Layouts[zone.Layout].Zones[zone.Zone].On(Monitor.WorkArea, gap);

    /// <summary>Zones in reading order (left to right, then top to bottom across
    /// the tiles), for the arrow keys.</summary>
    public ZoneRef Step(ZoneRef from, int delta)
    {
        var all = AllZones().ToList();
        int i = all.IndexOf(from);
        if (i < 0) return all[0];
        return all[((i + delta) % all.Count + all.Count) % all.Count];
    }
}

/// <summary>Where Snap Assist draws its window cards inside a free zone.</summary>
public static class AssistGrid
{
    public const int Margin = 28, CardGap = 20, TitleHeight = 30, HeaderHeight = 56, MaxCardWidth = 440;

    /// <summary>The cards' shape: the median of the windows' own, so most
    /// pictures fill their card; 16:10 when none has a size.</summary>
    public static double AspectOf(IReadOnlyList<AssistCandidate> candidates)
    {
        var aspects = candidates.Where(c => !c.Frame.IsEmpty).Select(c => (double)c.Frame.Width / c.Frame.Height).Order().ToList();
        return aspects.Count == 0 ? 16 / 10d : Math.Clamp(aspects[aspects.Count / 2], 0.75, 2.4);
    }

    /// <summary>Cards for <paramref name="count"/> windows in <paramref name="zone"/>,
    /// the largest cards that fit, at most <see cref="MaxCardWidth"/> wide; on a
    /// tie the squarer grid, which is what a tall zone of capped cards gave as
    /// one column of four.</summary>
    public static IReadOnlyList<Rect> Cards(Rect zone, int count, double windowAspect = 16 / 10d)
    {
        if (count <= 0) return [];
        var inner = new Rect(zone.X + Margin, zone.Y + Margin + HeaderHeight, zone.Width - 2 * Margin, zone.Height - 2 * Margin - HeaderHeight);
        if (inner.IsEmpty) return [];

        int bestColumns = 1;
        double bestScale = 0;
        for (int columns = 1; columns <= count; columns++)
        {
            int rows = (count + columns - 1) / columns;
            double cardW = (inner.Width - (columns - 1) * CardGap) / (double)columns;
            double cardH = (inner.Height - (rows - 1) * CardGap) / (double)rows - TitleHeight;
            if (cardW <= 0 || cardH <= 0) continue;
            double scale = Math.Min(Math.Min(cardW, MaxCardWidth), cardH * windowAspect);
            int bestRows = (count + bestColumns - 1) / bestColumns;
            bool squarer = Math.Abs(columns - rows) < Math.Abs(bestColumns - bestRows);
            if (scale > bestScale + 0.5 || (scale >= bestScale - 0.5 && squarer)) { bestScale = scale; bestColumns = columns; }
        }

        int cols = bestColumns, rowsUsed = (count + cols - 1) / cols;
        int w = (int)Math.Floor(bestScale);
        int h = (int)Math.Floor(bestScale / windowAspect) + TitleHeight;
        int gridW = cols * w + (cols - 1) * CardGap;
        int gridH = rowsUsed * h + (rowsUsed - 1) * CardGap;
        int x0 = inner.X + (inner.Width - gridW) / 2;
        int y0 = inner.Y + Math.Max(0, (inner.Height - gridH) / 2);
        var cards = new List<Rect>();
        for (int i = 0; i < count; i++)
        {
            int c = i % cols, r = i / cols;
            cards.Add(new Rect(x0 + c * (w + CardGap), y0 + r * (h + CardGap), w, h));
        }
        return cards;
    }
}
