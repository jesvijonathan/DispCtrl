namespace DispCtrl.Core.Displays;

/// <summary>How a window is shown, as far as moving it between displays cares.</summary>
public enum WindowShow
{
    Normal,
    Maximized,
    Minimized,
}

/// <summary>
/// Where a window sat on one display, kept relative to that display so it can
/// be put back after the display has moved, or returned from being unplugged.
/// </summary>
/// <param name="Token">The display's identity token.</param>
/// <param name="Left">Distance of the window's left edge from the work area's, in pixels.</param>
/// <param name="Top">Distance of the window's top edge from the work area's, in pixels.</param>
/// <param name="Width">The window's width, in pixels, when it was recorded.</param>
/// <param name="Height">The window's height, in pixels.</param>
/// <param name="WorkWidth">The work area's width then, so a changed resolution scales the spot.</param>
/// <param name="WorkHeight">The work area's height then.</param>
/// <param name="Dpi">The display's DPI then.</param>
/// <param name="Show">Normal, maximized or minimized.</param>
public sealed record WindowSpot(string Token, int Left, int Top, int Width, int Height,
    int WorkWidth, int WorkHeight, uint Dpi, WindowShow Show);

/// <summary>
/// The arithmetic of moving windows between displays, apart from moving them.
/// </summary>
/// <remarks>
/// Pure, so it is checked in presetverify: moving another program's window can
/// only be watched, not tested, and every rule about where it lands is here.
/// All rectangles are physical pixels, the coordinates a per-monitor aware
/// process sees.
/// </remarks>
public static class WindowGeometry
{
    /// <summary>
    /// Where a window from one work area lands on another: the same place
    /// proportionally, at a size carried by <paramref name="scale"/>, and never
    /// larger than the target or hanging off it.
    /// </summary>
    /// <param name="scale">
    /// Target DPI over source DPI to keep the window's size to the eye, or 1 to
    /// keep its pixels.
    /// </param>
    public static DisplayRect Carry(DisplayRect window, DisplayRect fromWork, DisplayRect toWork, double scale)
    {
        if (toWork.Width <= 0 || toWork.Height <= 0) return window;
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;

        int width = Math.Clamp((int)Math.Round(window.Width * scale), 1, toWork.Width);
        int height = Math.Clamp((int)Math.Round(window.Height * scale), 1, toWork.Height);

        // The centre keeps its place across the work area, so a window on the
        // right of a wide screen lands on the right of a narrow one.
        double cx = fromWork.Width > 0 ? (window.Left + window.Width / 2.0 - fromWork.Left) / fromWork.Width : 0.5;
        double cy = fromWork.Height > 0 ? (window.Top + window.Height / 2.0 - fromWork.Top) / fromWork.Height : 0.5;
        cx = Math.Clamp(cx, 0, 1);
        cy = Math.Clamp(cy, 0, 1);

        int left = (int)Math.Round(toWork.Left + cx * toWork.Width - width / 2.0);
        int top = (int)Math.Round(toWork.Top + cy * toWork.Height - height / 2.0);
        left = Math.Clamp(left, toWork.Left, toWork.Right - width);
        top = Math.Clamp(top, toWork.Top, toWork.Bottom - height);
        return new DisplayRect(left, top, left + width, top + height);
    }

    /// <summary>Records a window's place on a display, relative to that display's work area.</summary>
    public static WindowSpot Spot(string token, DisplayRect window, DisplayRect work, uint dpi, WindowShow show) =>
        new(token, window.Left - work.Left, window.Top - work.Top, window.Width, window.Height,
            work.Width, work.Height, dpi, show);

    /// <summary>
    /// Where a recorded spot is on its display now: exactly where it was when the
    /// display has not changed, otherwise carried to the display as it is.
    /// </summary>
    public static DisplayRect Place(WindowSpot spot, DisplayRect work, uint dpi)
    {
        var then = new DisplayRect(spot.Left + work.Left, spot.Top + work.Top,
            spot.Left + work.Left + spot.Width, spot.Top + work.Top + spot.Height);
        if (spot.WorkWidth == work.Width && spot.WorkHeight == work.Height && spot.Dpi == dpi)
            return then;
        var was = new DisplayRect(work.Left, work.Top, work.Left + spot.WorkWidth, work.Top + spot.WorkHeight);
        double scale = spot.Dpi > 0 && dpi > 0 ? dpi / (double)spot.Dpi : 1;
        return Carry(then, was, work, scale);
    }

    /// <summary>
    /// Whether a window is still where Windows put it when its display left,
    /// so putting it back undoes nothing anybody chose.
    /// </summary>
    /// <remarks>
    /// A couple of pixels of slack: some apps nudge themselves once after a
    /// DPI change, and that is not somebody moving the window.
    /// </remarks>
    public static bool Untouched(DisplayRect placedByWindows, WindowShow showThen, DisplayRect now, WindowShow showNow)
    {
        if (showThen != showNow) return false;
        if (showNow == WindowShow.Minimized) return true;
        const int Slack = 3;
        return Math.Abs(placedByWindows.Left - now.Left) <= Slack && Math.Abs(placedByWindows.Top - now.Top) <= Slack
            && Math.Abs(placedByWindows.Right - now.Right) <= Slack && Math.Abs(placedByWindows.Bottom - now.Bottom) <= Slack;
    }

    /// <summary>The display a rectangle mostly lies on, by the area of overlap; -1 for none.</summary>
    public static int MostlyOn(DisplayRect window, IReadOnlyList<DisplayRect> displays)
    {
        int best = -1;
        long most = 0;
        for (int i = 0; i < displays.Count; i++)
        {
            DisplayRect d = displays[i];
            long w = Math.Min(window.Right, d.Right) - Math.Max(window.Left, d.Left);
            long h = Math.Min(window.Bottom, d.Bottom) - Math.Max(window.Top, d.Top);
            if (w <= 0 || h <= 0) continue;
            if (w * h > most) { most = w * h; best = i; }
        }
        return best;
    }
}
