using DispCtrl.Linux.Graphics;

namespace DispCtrl.Linux.X11;

/// <summary>A transparent window over one monitor that DispCtrl draws on: the
/// snap layouts, the zone preview, Snap Assist.</summary>
/// <remarks>
/// <para>Override-redirect, so the window manager neither decorates, places nor
/// focuses it, and it stays above managed windows; 32-bit ARGB, which the
/// compositor blends, so only what is drawn shows.</para>
/// <para>While a window is being dragged the overlay must not take the pointer
/// (the window manager holds it, and a drop must land where it would anyway),
/// so its input shape is empty: clicks fall through. Interactive overlays
/// (the shortcut's picker, Snap Assist) take input over the whole monitor, so a
/// click outside the panel dismisses rather than reaching the window under it.</para>
/// <para>Frames are drawn offscreen and copied in one paint, so a move never
/// shows half a frame.</para>
/// </remarks>
public sealed unsafe class Overlay : IDisposable
{
    private readonly XConnection _x;
    private readonly nint _visual;
    private readonly ulong _colormap;
    private bool _mapped;
    private bool _interactive;

    private Overlay(XConnection x, ulong window, nint visual, ulong colormap, Rect bounds)
    {
        _x = x;
        Window = window;
        _visual = visual;
        _colormap = colormap;
        Bounds = bounds;
    }

    public ulong Window { get; }
    public Rect Bounds { get; private set; }

    /// <summary>Null when the server has no 32-bit visual (no compositing).</summary>
    public static Overlay? Create(XConnection x, Rect bounds)
    {
        if (Xlib.XMatchVisualInfo(x.Display, x.Screen, 32, Xlib.TrueColor, out var info) == 0) return null;
        var colormap = Xlib.XCreateColormap(x.Display, x.Root, info.visual, Xlib.AllocNone);
        var attributes = new Xlib.XSetWindowAttributes
        {
            override_redirect = Xlib.True,
            background_pixel = 0,
            border_pixel = 0,
            colormap = colormap,
            event_mask = Xlib.ExposureMask | Xlib.ButtonPressMask | Xlib.ButtonReleaseMask | Xlib.PointerMotionMask | Xlib.KeyPressMask,
        };
        var window = Xlib.XCreateWindow(x.Display, x.Root, bounds.X, bounds.Y, (uint)bounds.Width, (uint)bounds.Height, 0,
            32, (uint)Xlib.InputOutput, info.visual,
            Xlib.CWOverrideRedirect | Xlib.CWBackPixel | Xlib.CWBorderPixel | Xlib.CWColormap | Xlib.CWEventMask, ref attributes);
        if (window == 0)
        {
            Xlib.XFreeColormap(x.Display, colormap);
            return null;
        }
        Xlib.XStoreName(x.Display, window, "DispCtrl snap");
        x.SetLongs(window, "_NET_WM_WINDOW_TYPE", "ATOM", (long)x.Atom("_NET_WM_WINDOW_TYPE_NOTIFICATION"));
        // Keeps the compositor from treating it as a fullscreen game and
        // unredirecting what is under it.
        x.SetLongs(window, "_NET_WM_BYPASS_COMPOSITOR", "CARDINAL", 2);
        var overlay = new Overlay(x, window, info.visual, colormap, bounds);
        overlay.SetInteractive(false);
        return overlay;
    }

    public void MoveTo(Rect bounds)
    {
        if (bounds == Bounds) return;
        Bounds = bounds;
        Xlib.XMoveResizeWindow(_x.Display, Window, bounds.X, bounds.Y, (uint)bounds.Width, (uint)bounds.Height);
    }

    public void SetInteractive(bool interactive)
    {
        _interactive = interactive;
        if (interactive)
        {
            // Region None: the input shape is the window's own.
            Xlib.XFixesSetWindowShapeRegion(_x.Display, Window, Xlib.ShapeInput, 0, 0, 0);
        }
        else
        {
            var empty = Xlib.XFixesCreateRegion(_x.Display, 0, 0);
            Xlib.XFixesSetWindowShapeRegion(_x.Display, Window, Xlib.ShapeInput, 0, 0, empty);
            Xlib.XFixesDestroyRegion(_x.Display, empty);
        }
    }

    /// <summary>Draws a frame: <paramref name="draw"/> paints onto a cleared
    /// offscreen canvas in this overlay's coordinates, which is then shown.</summary>
    public void Paint(Action<Canvas> draw)
    {
        using var buffer = Canvas.Image(Bounds.Width, Bounds.Height);
        buffer.Clear();
        draw(buffer);
        using var screen = Canvas.OnXlib(_x.Display, Window, _visual, Bounds.Width, Bounds.Height);
        screen.Blit(buffer);
        if (!_mapped)
        {
            Xlib.XMapRaised(_x.Display, Window);
            _mapped = true;
        }
        else
        {
            Xlib.XRaiseWindow(_x.Display, Window);
        }
        _x.Flush();
    }

    /// <summary>Takes the keyboard and pointer, so Esc and a click outside the
    /// panel reach the overlay. False when another client holds them.</summary>
    public bool Grab()
    {
        const uint mask = (uint)(Xlib.ButtonPressMask | Xlib.ButtonReleaseMask | Xlib.PointerMotionMask);
        for (int attempt = 0; attempt < 10; attempt++)
        {
            // A shortcut's own key may still be held a moment after it fired.
            int keyboard = Xlib.XGrabKeyboard(_x.Display, Window, Xlib.False, Xlib.GrabModeAsync, Xlib.GrabModeAsync, Xlib.CurrentTime);
            if (keyboard == 0)
            {
                Xlib.XGrabPointer(_x.Display, Window, Xlib.False, mask, Xlib.GrabModeAsync, Xlib.GrabModeAsync, 0, 0, Xlib.CurrentTime);
                return true;
            }
            Thread.Sleep(20);
        }
        return false;
    }

    public void Hide()
    {
        if (_interactive)
        {
            Xlib.XUngrabKeyboard(_x.Display, Xlib.CurrentTime);
            Xlib.XUngrabPointer(_x.Display, Xlib.CurrentTime);
        }
        if (_mapped)
        {
            Xlib.XUnmapWindow(_x.Display, Window);
            _mapped = false;
        }
        _x.Flush();
    }

    public bool Visible => _mapped;

    public void Dispose()
    {
        Hide();
        Xlib.XDestroyWindow(_x.Display, Window);
        Xlib.XFreeColormap(_x.Display, _colormap);
        _x.Flush();
    }
}
