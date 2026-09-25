using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using static DispCtrl.Display.Placement.WindowNative;

namespace DispCtrl.Display.Placement;

/// <summary>What gathering windows came to.</summary>
/// <param name="Moved">Windows now on the target display.</param>
/// <param name="Skipped">Windows left where they were, with the reason for each kind.</param>
public sealed record GatherOutcome(int Moved, IReadOnlyList<string> Skipped);

/// <summary>
/// Moves other programs' windows between displays.
/// </summary>
/// <remarks>
/// Three shapes of window, three ways to move one:
/// <list type="bullet">
/// <item>Normal: moved first and sized second. Crossing to a display at another
/// scale sends the app a DPI change it answers by resizing itself; sized in the
/// same call, the size was applied and then rescaled on top - the trap
/// <c>AppWindow.MoveAndResize</c> fell into in this project too.</item>
/// <item>Maximized: given a restore position on the target and maximized there,
/// so it restores onto the display it now fills rather than back where it was.</item>
/// <item>Minimized: only its restore position moves; it stays minimized.</item>
/// </list>
/// A hung window is skipped - a synchronous move would wait on it - and so is one
/// running as administrator, which Windows does not let an ordinary process move.
/// The z-order is left alone except where maximizing takes the foreground, which
/// is handed back to whichever window had it.
/// </remarks>
public static unsafe class WindowMover
{
    /// <summary>
    /// Moves every app window not already on <paramref name="target"/> onto it.
    /// </summary>
    /// <param name="from">Only windows on these displays; null for every other display.</param>
    public static GatherOutcome Gather(DisplayInfo target, PlacementSettings settings, IReadOnlyCollection<DisplayInfo>? from = null)
    {
        List<DisplayInfo> displays = DisplayRegistry.Enumerate();
        HashSet<string> excluded = settings.Exclusions();
        nint foreground = GetForegroundWindow();
        int moved = 0;
        var skipped = new List<string>();
        int hung = 0, elevated = 0, excludedCount = 0;

        // Bottom of the z-order first, so each move lands under the one after it
        // and the stack reads the same on the target as it did before.
        List<AppWindow> windows = AppWindows.List();
        windows.Reverse();
        foreach (AppWindow window in windows)
        {
            DisplayInfo? on = AppWindows.DisplayOf(window.Handle, displays);
            if (on is null || on.Key == target.Key) continue;
            if (from is not null && !from.Any(d => d.Key == on.Key)) continue;
            if (window.Process.Length > 0 && excluded.Contains(window.Process)) { excludedCount++; continue; }
            if (IsHungAppWindow(window.Handle) != 0) { hung++; continue; }
            if (Move(window.Handle, on, target, settings.KeepSize)) moved++;
            else elevated++;
        }

        if (foreground != 0 && GetForegroundWindow() != foreground) _ = SetForegroundWindow(foreground);
        if (excludedCount > 0) skipped.Add($"{excludedCount} on the list of apps never moved");
        if (hung > 0) skipped.Add($"{hung} not responding");
        if (elevated > 0) skipped.Add($"{elevated} running as administrator, which Windows does not let DispCtrl move");
        return new GatherOutcome(moved, skipped);
    }

    /// <summary>The display a gather or a new window goes to, as the settings decide it.</summary>
    public static DisplayInfo? Active(PlacementSettings settings, IReadOnlyList<DisplayInfo> displays)
    {
        if (settings.Active == ActiveDisplay.ActiveWindow && GetForegroundWindow() is var fg && fg != 0
            && AppWindows.IsCandidate(fg) && AppWindows.DisplayOf(fg, displays) is { } byWindow)
            return byWindow;
        if (GetCursorPos(out Point point) == 0) return displays.FirstOrDefault(d => d.IsPrimary);
        return displays.FirstOrDefault(d => d.Bounds.Contains(point.X, point.Y)) ?? displays.FirstOrDefault(d => d.IsPrimary);
    }

    /// <summary>Moves one window from one display to another, keeping its place proportionally.</summary>
    /// <returns>False when Windows refused the move.</returns>
    public static bool Move(nint hwnd, DisplayInfo from, DisplayInfo to, bool keepSize)
    {
        double scale = keepSize && from.Dpi > 0 ? to.Dpi / (double)from.Dpi : 1;
        if (IsIconic(hwnd) != 0 || IsZoomed(hwnd) != 0)
        {
            var placement = new PlacementData { Length = (uint)sizeof(PlacementData) };
            if (GetWindowPlacement(hwnd, ref placement) == 0) return false;
            DisplayRect normal = FromWorkspace(ToRect(placement.NormalPosition), from);
            DisplayRect landed = WindowGeometry.Carry(normal, from.WorkArea, to.WorkArea, scale);
            return Place(hwnd, landed, IsIconic(hwnd) != 0 ? WindowShow.Minimized : WindowShow.Maximized, to);
        }
        DisplayRect frame = AppWindows.Frame(hwnd);
        // Borderless fullscreen - a film, a game in a window the size of the
        // screen: it fills the new display too. Carried like any window, it was
        // squeezed into the work area and stopped being fullscreen.
        if (FocusGeometry.Covers(frame, from.Bounds)) return PlaceFullscreen(hwnd, to);
        return Place(hwnd, WindowGeometry.Carry(frame, from.WorkArea, to.WorkArea, scale), WindowShow.Normal, to);
    }

    /// <summary>Makes a borderless fullscreen window fill another display.</summary>
    private static bool PlaceFullscreen(nint hwnd, DisplayInfo to)
    {
        const uint Quiet = SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder;
        DisplayRect b = to.Bounds;
        // Moved, then sized, for the same reason as any window.
        if (SetWindowPos(hwnd, 0, b.Left, b.Top, 0, 0, Quiet | SwpNoSize) == 0) return false;
        return SetWindowPos(hwnd, 0, b.Left, b.Top, b.Width, b.Height, Quiet) != 0;
    }

    /// <summary>
    /// Puts a window at a frame rectangle on a display, shown as asked.
    /// </summary>
    /// <param name="frame">Where the visible frame should be, in screen pixels.</param>
    public static bool Place(nint hwnd, DisplayRect frame, WindowShow show, DisplayInfo on)
    {
        if (IsWindow(hwnd) == 0) return false;
        if (show == WindowShow.Normal)
        {
            // A minimized or maximized window restored into place first.
            if (IsIconic(hwnd) != 0 || IsZoomed(hwnd) != 0)
            {
                var restore = new PlacementData { Length = (uint)sizeof(PlacementData) };
                if (GetWindowPlacement(hwnd, ref restore) == 0) return false;
                restore.ShowCmd = SwShowNoActivate;
                restore.Flags = 0;
                restore.NormalPosition = ToNative(ToWorkspace(OuterFor(hwnd, frame), on));
                return SetWindowPlacement(hwnd, ref restore) != 0;
            }

            // Moved, then sized: see the type remarks.
            DisplayRect outer = OuterFor(hwnd, frame);
            const uint Quiet = SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder;
            if (SetWindowPos(hwnd, 0, outer.Left, outer.Top, 0, 0, Quiet | SwpNoSize) == 0) return false;
            // Borders are measured again after the move: they scale with the DPI.
            outer = OuterFor(hwnd, frame);
            return SetWindowPos(hwnd, 0, outer.Left, outer.Top, outer.Width, outer.Height, Quiet) != 0;
        }

        var placement = new PlacementData { Length = (uint)sizeof(PlacementData) };
        if (GetWindowPlacement(hwnd, ref placement) == 0) return false;
        // WPF_RESTORETOMAXIMIZED survives: a maximized window minimized and then
        // carried elsewhere comes back maximized there, not at its normal size.
        placement.Flags &= 0x2;
        placement.NormalPosition = ToNative(ToWorkspace(OuterFor(hwnd, frame), on));
        if (show == WindowShow.Minimized)
        {
            placement.ShowCmd = SwShowMinNoActive;
            return SetWindowPlacement(hwnd, ref placement) != 0;
        }

        // Maximized: restored onto the target first, then maximized there.
        // Given the new restore rectangle alone, a window already maximized
        // stayed maximized where it was - which is why gathering moved only the
        // windows that were not.
        nint front = GetForegroundWindow();
        placement.ShowCmd = SwShowNoActivate;
        if (SetWindowPlacement(hwnd, ref placement) == 0) return false;
        placement.ShowCmd = SwShowMaximized;
        bool done = SetWindowPlacement(hwnd, ref placement) != 0;
        // Maximizing takes the foreground; the window that had it gets it back.
        if (front != 0 && front != hwnd && GetForegroundWindow() != front) _ = SetForegroundWindow(front);
        return done;
    }

    /// <summary>
    /// The window rectangle that draws a frame at <paramref name="frame"/>: the
    /// invisible resize borders Windows 10 and 11 add around every frame put back on.
    /// </summary>
    private static DisplayRect OuterFor(nint hwnd, DisplayRect frame)
    {
        DisplayRect outer = AppWindows.Outer(hwnd), drawn = AppWindows.Frame(hwnd);
        // Minimized, both rectangles are the parked icon's; the borders are unknown and taken as none.
        if (IsIconic(hwnd) != 0 || drawn.Width <= 0) return frame;
        int left = drawn.Left - outer.Left, top = drawn.Top - outer.Top;
        int right = outer.Right - drawn.Right, bottom = outer.Bottom - drawn.Bottom;
        if (left is < 0 or > 64 || top is < 0 or > 64 || right is < 0 or > 64 || bottom is < 0 or > 64) return frame;
        return new DisplayRect(frame.Left - left, frame.Top - top, frame.Right + right, frame.Bottom + bottom);
    }

    // ---------------------------------------------------------- workspace --
    //
    // A placement's rectangles are in workspace coordinates: screen coordinates
    // less the space the display's taskbar and appbars take at its top and left.
    // With a taskbar at the bottom, as on most desks, the two are the same, which
    // is how getting this wrong goes unnoticed - until a window creeps up under a
    // taskbar docked at the top.

    /// <summary>Workspace to screen, for a rectangle on <paramref name="on"/>.</summary>
    public static DisplayRect FromWorkspace(DisplayRect workspace, DisplayInfo on)
    {
        int dx = on.WorkArea.Left - on.Bounds.Left, dy = on.WorkArea.Top - on.Bounds.Top;
        return new DisplayRect(workspace.Left + dx, workspace.Top + dy, workspace.Right + dx, workspace.Bottom + dy);
    }

    /// <summary>Workspace to screen, for a rectangle whose display is found from where it lies.</summary>
    public static DisplayRect FromWorkspace(DisplayRect workspace, IReadOnlyList<DisplayInfo> displays)
    {
        int index = WindowGeometry.MostlyOn(workspace, displays.Select(d => d.Bounds).ToList());
        return index < 0 ? workspace : FromWorkspace(workspace, displays[index]);
    }

    /// <summary>Screen to workspace, for a rectangle on <paramref name="on"/>.</summary>
    public static DisplayRect ToWorkspace(DisplayRect screen, DisplayInfo on)
    {
        int dx = on.WorkArea.Left - on.Bounds.Left, dy = on.WorkArea.Top - on.Bounds.Top;
        return new DisplayRect(screen.Left - dx, screen.Top - dy, screen.Right - dx, screen.Bottom - dy);
    }

    private static DisplayRect ToRect(Rect r) => new(r.Left, r.Top, r.Right, r.Bottom);
    private static Rect ToNative(DisplayRect r) => new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
}
