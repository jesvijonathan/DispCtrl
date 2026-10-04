using System.Runtime.InteropServices;

namespace DispCtrl.Linux.X11;

/// <summary>One monitor: its bounds, and what windows may use of it once
/// panels and docks have taken theirs.</summary>
public sealed record MonitorInfo(string Name, bool Primary, Rect Bounds, Rect WorkArea);

/// <summary>One top-level application window as the window manager lists it.</summary>
/// <param name="Frame">The visible window - decorations included, invisible
/// client-side shadows left out - in root coordinates.</param>
public sealed record AppWindow(
    ulong Id, string Title, string? AppClass, int Pid, Rect Frame,
    bool Minimized, bool Maximized, bool Fullscreen, bool Above, int Desktop);

/// <summary>Reads and arranges the desktop through EWMH: what the window
/// manager lists, and the messages it accepts to move and restate a window.</summary>
/// <remarks>
/// <para>A window's "visible frame" is not one X rectangle. A window the manager
/// decorates (server-side) is wrapped in a frame <c>_NET_FRAME_EXTENTS</c> wide;
/// a GTK window that draws its own title bar (client-side) is larger than it
/// looks, by shadows <c>_GTK_FRAME_EXTENTS</c> wide. Snapping one to a zone by
/// its X geometry alone left gaps beside every GTK window and overlapped every
/// decorated one, so <see cref="Place"/> works in visible frames.</para>
/// <para>Moves go through <c>_NET_MOVERESIZE_WINDOW</c> with static gravity
/// (client coordinates), never XMoveResizeWindow on a client: a reparenting
/// manager would read that as the frame's position and shift it by the title
/// bar.</para>
/// </remarks>
public static unsafe class Desktop
{
    private const int StaticGravity = 10;

    public static IReadOnlyList<MonitorInfo> Monitors(XConnection x)
    {
        var monitors = new List<MonitorInfo>();
        var info = Xlib.XRRGetMonitors(x.Display, x.Root, Xlib.True, out int count);
        if (info == null) return monitors;
        try
        {
            var workAreas = WorkAreas(x);
            for (int i = 0; i < count; i++)
            {
                var m = info[i];
                var bounds = new Rect(m.x, m.y, m.width, m.height);
                string name = AtomName(x, m.name) ?? $"monitor {i}";
                monitors.Add(new MonitorInfo(name, m.primary != 0, bounds, WorkAreaFor(bounds, workAreas)));
            }
        }
        finally { Xlib.XRRFreeMonitors(info); }
        return monitors;
    }

    /// <summary>Mutter publishes one work area per monitor (<c>_GTK_WORKAREAS_Dn</c>);
    /// <c>_NET_WORKAREA</c> is one rectangle spanning every monitor, which on a
    /// desk with a top bar on one display only is wrong for the other.</summary>
    private static List<Rect> WorkAreas(XConnection x)
    {
        var result = new List<Rect>();
        long desktop = x.Long(x.Root, "_NET_CURRENT_DESKTOP") ?? 0;
        var values = x.Longs(x.Root, $"_GTK_WORKAREAS_D{desktop}");
        if (values is null)
        {
            var all = x.Longs(x.Root, "_NET_WORKAREA");
            if (all is { Length: >= 4 })
            {
                int i = (int)Math.Min(desktop, all.Length / 4 - 1) * 4;
                result.Add(new Rect((int)all[i], (int)all[i + 1], (int)all[i + 2], (int)all[i + 3]));
            }
            return result;
        }
        for (int i = 0; i + 3 < values.Length; i += 4)
            result.Add(new Rect((int)values[i], (int)values[i + 1], (int)values[i + 2], (int)values[i + 3]));
        return result;
    }

    internal static Rect WorkAreaFor(Rect monitor, IReadOnlyList<Rect> workAreas)
    {
        Rect best = default;
        foreach (var area in workAreas)
        {
            var overlap = area.Intersect(monitor);
            if (overlap.Area > best.Area) best = overlap;
        }
        return best.IsEmpty ? monitor : best;
    }

    public static MonitorInfo MonitorAt(IReadOnlyList<MonitorInfo> monitors, int x, int y) =>
        monitors.FirstOrDefault(m => m.Bounds.Contains(x, y))
        ?? monitors.OrderBy(m => Distance(m.Bounds, x, y)).First();

    public static MonitorInfo MonitorOf(IReadOnlyList<MonitorInfo> monitors, Rect frame) =>
        monitors.OrderByDescending(m => m.Bounds.Intersect(frame).Area).ThenBy(m => Distance(m.Bounds, frame.CenterX, frame.CenterY)).First();

    private static long Distance(Rect r, int x, int y)
    {
        long dx = Math.Max(Math.Max(r.X - x, 0), x - r.Right);
        long dy = Math.Max(Math.Max(r.Y - y, 0), y - r.Bottom);
        return dx * dx + dy * dy;
    }

    public static ulong? Active(XConnection x) =>
        x.Long(x.Root, "_NET_ACTIVE_WINDOW") is long id and not 0 ? (ulong)id : null;

    /// <summary>Ordinary application windows on the current workspace, front
    /// first. Docks, desktops, menus, tooltips, splash screens, and windows that
    /// ask to stay off the taskbar are left out.</summary>
    public static IReadOnlyList<AppWindow> Windows(XConnection x, bool includeMinimized = true)
    {
        var stacking = x.Longs(x.Root, "_NET_CLIENT_LIST_STACKING") ?? x.Longs(x.Root, "_NET_CLIENT_LIST") ?? [];
        long current = x.Long(x.Root, "_NET_CURRENT_DESKTOP") ?? 0;
        var result = new List<AppWindow>();
        for (int i = stacking.Length - 1; i >= 0; i--)
        {
            var window = Describe(x, (ulong)stacking[i]);
            if (window is null) continue;
            if (window.Desktop != -1 && window.Desktop != current) continue;
            if (window.Minimized && !includeMinimized) continue;
            result.Add(window);
        }
        return result;
    }

    public static AppWindow? Describe(XConnection x, ulong id)
    {
        var types = x.Atoms(id, "_NET_WM_WINDOW_TYPE");
        if (types.Count > 0 && !types.Contains(x.Atom("_NET_WM_WINDOW_TYPE_NORMAL"))
            && !types.Contains(x.Atom("_NET_WM_WINDOW_TYPE_DIALOG")))
        {
            return null;
        }
        var state = x.Atoms(id, "_NET_WM_STATE");
        if (state.Contains(x.Atom("_NET_WM_STATE_SKIP_TASKBAR"))) return null;
        if (Xlib.XGetWindowAttributes(x.Display, id, out var attributes) == 0) return null;

        var frame = VisibleFrame(x, id, attributes);
        if (frame is null) return null;
        string title = x.Text(id, "_NET_WM_NAME") ?? x.Text(id, "WM_NAME") ?? "";
        string? appClass = AppClass(x, id);
        int pid = (int)(x.Long(id, "_NET_WM_PID") ?? 0);
        int desktop = x.Long(id, "_NET_WM_DESKTOP") is long d ? (d == 0xFFFFFFFF ? -1 : (int)d) : -1;

        return new AppWindow(id, title, appClass, pid, frame.Value,
            Minimized: state.Contains(x.Atom("_NET_WM_STATE_HIDDEN")),
            Maximized: state.Contains(x.Atom("_NET_WM_STATE_MAXIMIZED_HORZ")) && state.Contains(x.Atom("_NET_WM_STATE_MAXIMIZED_VERT")),
            Fullscreen: state.Contains(x.Atom("_NET_WM_STATE_FULLSCREEN")),
            Above: state.Contains(x.Atom("_NET_WM_STATE_ABOVE")),
            Desktop: desktop);
    }

    private static string? AppClass(XConnection x, ulong id)
    {
        // WM_CLASS is two NUL-separated strings: instance, then class.
        var raw = x.Text(id, "WM_CLASS");
        if (raw is null) return null;
        var parts = raw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1] : parts.FirstOrDefault();
    }

    /// <summary>What the eye sees of a window, in root coordinates.</summary>
    public static Rect? VisibleFrame(XConnection x, ulong id)
    {
        if (Xlib.XGetWindowAttributes(x.Display, id, out var attributes) == 0) return null;
        return VisibleFrame(x, id, attributes);
    }

    private static Rect? VisibleFrame(XConnection x, ulong id, Xlib.XWindowAttributes attributes)
    {
        if (Xlib.XTranslateCoordinates(x.Display, id, x.Root, 0, 0, out int rx, out int ry, out _) == 0) return null;
        var client = new Rect(rx, ry, attributes.width, attributes.height);
        var (l, r, t, b) = Extents(x, id);
        return new Rect(client.X - l, client.Y - t, client.Width + l + r, client.Height + t + b);
    }

    /// <summary>How far the visible frame reaches past the X window on each side:
    /// positive for a manager's decorations, negative for a GTK window's
    /// invisible shadow.</summary>
    internal static (int Left, int Right, int Top, int Bottom) Extents(XConnection x, ulong id)
    {
        if (x.Longs(id, "_NET_FRAME_EXTENTS") is { Length: 4 } f && (f[0] | f[1] | f[2] | f[3]) != 0)
            return ((int)f[0], (int)f[1], (int)f[2], (int)f[3]);
        if (x.Longs(id, "_GTK_FRAME_EXTENTS") is { Length: 4 } g)
            return (-(int)g[0], -(int)g[1], -(int)g[2], -(int)g[3]);
        return (0, 0, 0, 0);
    }

    /// <summary>The X window to the client rectangle that makes the window look
    /// like <paramref name="target"/>: the inverse of <see cref="VisibleFrame(XConnection, ulong)"/>.</summary>
    internal static Rect ClientFor(Rect target, (int Left, int Right, int Top, int Bottom) e) =>
        new(target.X + e.Left, target.Y + e.Top,
            Math.Max(1, target.Width - e.Left - e.Right), Math.Max(1, target.Height - e.Top - e.Bottom));

    /// <summary>Moves and sizes a window so its visible frame is <paramref name="target"/>,
    /// leaving maximized and fullscreen first (a maximized window ignores a
    /// move), and showing it if it was minimized.</summary>
    public static void Place(XConnection x, ulong id, Rect target, bool activate = false)
    {
        var state = x.Atoms(id, "_NET_WM_STATE");
        if (state.Contains(x.Atom("_NET_WM_STATE_FULLSCREEN")))
            SetState(x, id, false, "_NET_WM_STATE_FULLSCREEN");
        if (state.Contains(x.Atom("_NET_WM_STATE_MAXIMIZED_HORZ")) || state.Contains(x.Atom("_NET_WM_STATE_MAXIMIZED_VERT")))
            SetState(x, id, false, "_NET_WM_STATE_MAXIMIZED_VERT", "_NET_WM_STATE_MAXIMIZED_HORZ");

        var client = ClientFor(target, Extents(x, id));
        const long flags = (1 << 8) | (1 << 9) | (1 << 10) | (1 << 11); // x, y, width, height present
        const long sourcePager = 2L << 12;                              // a pager: obeyed, not second-guessed
        x.SendToRoot(id, "_NET_MOVERESIZE_WINDOW", StaticGravity | flags | sourcePager,
            client.X, client.Y, client.Width, client.Height);
        if (activate || state.Contains(x.Atom("_NET_WM_STATE_HIDDEN"))) Activate(x, id);
        x.Flush();
    }

    public static void Activate(XConnection x, ulong id) =>
        x.SendToRoot(id, "_NET_ACTIVE_WINDOW", 2, Xlib.CurrentTime, 0);

    /// <summary>_NET_WM_STATE: 1 adds, 0 removes, up to two states per message.</summary>
    public static void SetState(XConnection x, ulong id, bool on, string state, string? second = null) =>
        x.SendToRoot(id, "_NET_WM_STATE", on ? 1 : 0, (long)x.Atom(state), second is null ? 0 : (long)x.Atom(second), 2);

    /// <summary>The top-level ancestor (the manager's frame, or the window itself
    /// when undecorated): what the compositor keeps a picture of.</summary>
    public static ulong TopLevel(XConnection x, ulong id)
    {
        ulong current = id;
        for (int depth = 0; depth < 16; depth++)
        {
            if (Xlib.XQueryTree(x.Display, current, out ulong root, out ulong parent, out nint children, out _) == 0) break;
            if (children != 0) Xlib.XFree(children);
            if (parent == 0 || parent == root) return current;
            current = parent;
        }
        return id;
    }

    private static string? AtomName(XConnection x, ulong atom)
    {
        if (atom == 0) return null;
        var p = Xlib.XGetAtomName(x.Display, atom);
        if (p == 0) return null;
        try { return Marshal.PtrToStringUTF8(p); }
        finally { Xlib.XFree(p); }
    }
}
