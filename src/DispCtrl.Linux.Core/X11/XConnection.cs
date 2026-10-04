using System.Runtime.InteropServices;
using System.Text;

namespace DispCtrl.Linux.X11;

/// <summary>One connection to the X server, with the property and message
/// helpers EWMH needs. Not thread-safe: one per thread.</summary>
public sealed unsafe class XConnection : IDisposable
{
    private readonly Dictionary<string, ulong> _atoms = new(StringComparer.Ordinal);

    private XConnection(nint display)
    {
        Display = display;
        Root = Xlib.XDefaultRootWindow(display);
        Screen = Xlib.XDefaultScreen(display);
    }

    public nint Display { get; }
    public ulong Root { get; }
    public int Screen { get; }

    /// <summary>Opens <c>$DISPLAY</c> (or <paramref name="name"/>), or null when
    /// there is no X server, libX11 is missing, or this is a Wayland session -
    /// XWayland answers there, but its windows are not the desktop's.</summary>
    public static XConnection? Open(string? name = null)
    {
        if (Hardware.GammaRamp.OnWayland) return null;
        try
        {
            Xlib.InstallErrorHandler();
            var display = Xlib.XOpenDisplay(name ?? Hardware.GammaRamp.DisplayName);
            return display == 0 ? null : new XConnection(display);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    public ulong Atom(string name)
    {
        if (!_atoms.TryGetValue(name, out var atom))
        {
            atom = Xlib.XInternAtom(Display, name, Xlib.False);
            _atoms[name] = atom;
        }
        return atom;
    }

    /// <summary>A 32-bit property as longs (Xlib hands format-32 data back as C longs).</summary>
    public long[]? Longs(ulong window, string property, ulong type = (ulong)Xlib.AnyPropertyType)
    {
        if (Xlib.XGetWindowProperty(Display, window, Atom(property), 0, 4096, Xlib.False, type,
                out _, out int format, out ulong count, out _, out nint data) != 0 || data == 0)
        {
            return null;
        }
        try
        {
            if (format != 32) return null;
            var result = new long[count];
            for (ulong i = 0; i < count; i++) result[i] = ((long*)data)[i];
            return result;
        }
        finally { Xlib.XFree(data); }
    }

    public long? Long(ulong window, string property) => Longs(window, property) is [var first, ..] ? first : null;

    public string? Text(ulong window, string property)
    {
        if (Xlib.XGetWindowProperty(Display, window, Atom(property), 0, 1024, Xlib.False, (ulong)Xlib.AnyPropertyType,
                out _, out int format, out ulong count, out _, out nint data) != 0 || data == 0)
        {
            return null;
        }
        try
        {
            return format == 8 ? Encoding.UTF8.GetString((byte*)data, (int)count).TrimEnd('\0') : null;
        }
        finally { Xlib.XFree(data); }
    }

    public HashSet<ulong> Atoms(ulong window, string property) =>
        Longs(window, property) is { } values ? values.Select(v => (ulong)v).ToHashSet() : [];

    public void SetLongs(ulong window, string property, string type, params long[] values)
    {
        fixed (long* p = values)
        {
            Xlib.XChangeProperty(Display, window, Atom(property), Atom(type), 32, Xlib.PropModeReplace, (byte*)p, values.Length);
        }
    }

    /// <summary>An EWMH client message to the root window: how a client asks the
    /// window manager to move, raise or change the state of a window.</summary>
    public void SendToRoot(ulong window, string messageType, long d0 = 0, long d1 = 0, long d2 = 0, long d3 = 0, long d4 = 0)
    {
        byte* ev = stackalloc byte[Xlib.EventSize];
        new Span<byte>(ev, Xlib.EventSize).Clear();
        *(int*)ev = Xlib.ClientMessage;
        *(int*)(ev + 16) = Xlib.True;            // send_event
        *(ulong*)(ev + 32) = window;
        *(ulong*)(ev + 40) = Atom(messageType);
        *(int*)(ev + 48) = 32;                   // format
        long* data = (long*)(ev + 56);
        data[0] = d0; data[1] = d1; data[2] = d2; data[3] = d3; data[4] = d4;
        Xlib.XSendEvent(Display, Root, Xlib.False, Xlib.SubstructureRedirectMask | Xlib.SubstructureNotifyMask, ev);
    }

    public (int X, int Y) Pointer()
    {
        Xlib.XQueryPointer(Display, Root, out _, out _, out int x, out int y, out _, out _, out _);
        return (x, y);
    }

    public uint PointerButtons()
    {
        Xlib.XQueryPointer(Display, Root, out _, out _, out _, out _, out _, out _, out uint mask);
        return mask;
    }

    public void Flush() => Xlib.XFlush(Display);

    public void Dispose() => Xlib.XCloseDisplay(Display);
}

/// <summary>A rectangle in root (desktop) coordinates.</summary>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    public Rect Intersect(Rect other)
    {
        int x = Math.Max(X, other.X), y = Math.Max(Y, other.Y);
        int r = Math.Min(Right, other.Right), b = Math.Min(Bottom, other.Bottom);
        return r <= x || b <= y ? default : new Rect(x, y, r - x, b - y);
    }

    public long Area => IsEmpty ? 0 : (long)Width * Height;

    public Rect Inflate(int by) => new(X - by, Y - by, Width + 2 * by, Height + 2 * by);

    public override string ToString() => $"{Width}x{Height}+{X}+{Y}";
}
