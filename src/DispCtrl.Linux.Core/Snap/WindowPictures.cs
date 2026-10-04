using DispCtrl.Linux.Graphics;
using DispCtrl.Linux.X11;

namespace DispCtrl.Linux.Snap;

/// <summary>Live pictures of windows for Snap Assist's cards.</summary>
/// <remarks>
/// A compositing manager keeps every mapped window's contents in an offscreen
/// pixmap; <c>XCompositeNameWindowPixmap</c> names it, so a card shows the
/// window even where others cover it. A minimized window is unmapped and has
/// none: its card shows the app's initial. The pixmap is the top-level frame,
/// a GTK window's invisible shadow included, so the visible frame is cut out
/// of it.
/// </remarks>
internal sealed unsafe class WindowPictures(XConnection x) : IDisposable
{
    private readonly Dictionary<ulong, (ulong Pixmap, nint Surface, Rect Source)> _pictures = [];
    private readonly DesktopTheme _theme = DesktopTheme.Fallback;

    public void Capture(ulong id)
    {
        var top = Desktop.TopLevel(x, id);
        if (Xlib.XGetWindowAttributes(x.Display, top, out var attributes) == 0 || attributes.map_state != Xlib.IsViewable) return;
        if (Xlib.XTranslateCoordinates(x.Display, top, x.Root, 0, 0, out int tx, out int ty, out _) == 0) return;
        if (Desktop.VisibleFrame(x, id) is not { } frame) return;

        Xlib.LastErrorCode = 0;
        var pixmap = Xlib.XCompositeNameWindowPixmap(x.Display, top);
        Xlib.XSync(x.Display, Xlib.False);
        if (pixmap == 0 || Xlib.LastErrorCode != 0) return;

        var surface = Cairo.cairo_xlib_surface_create(x.Display, pixmap, attributes.visual, attributes.width, attributes.height);
        if (Cairo.cairo_surface_status(surface) != 0)
        {
            Cairo.cairo_surface_destroy(surface);
            Xlib.XFreePixmap(x.Display, pixmap);
            return;
        }
        var source = new Rect(frame.X - tx, frame.Y - ty, frame.Width, frame.Height)
            .Intersect(new Rect(0, 0, attributes.width, attributes.height));
        _pictures[id] = (pixmap, surface, source);
    }

    /// <summary>For <see cref="SnapPainter.Assist"/>: the window's picture, or its initial.</summary>
    public void Draw(Canvas canvas, AssistCandidate candidate, Rect card)
    {
        if (_pictures.TryGetValue(candidate.Id, out var p) && !p.Source.IsEmpty)
        {
            var inner = card.Inflate(-6);
            canvas.Picture(p.Surface, p.Source.X, p.Source.Y, p.Source.Width, p.Source.Height,
                inner.X, inner.Y, inner.Width, inner.Height, 6);
        }
        else
        {
            SnapPainter.Initial(canvas, candidate, card, _theme);
        }
    }

    public void Dispose()
    {
        foreach (var (pixmap, surface, _) in _pictures.Values)
        {
            Cairo.cairo_surface_destroy(surface);
            Xlib.XFreePixmap(x.Display, pixmap);
        }
        _pictures.Clear();
        x.Flush();
    }
}
