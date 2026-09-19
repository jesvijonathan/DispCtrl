namespace DisplCtrl.Core.Displays;

/// <summary>Which edge of a display the taskbar is on.</summary>
public enum ScreenEdge
{
    Bottom,
    Top,
    Left,
    Right,
    /// <summary>The taskbar is hidden or on another display; the work area is the whole screen.</summary>
    None,
}

/// <summary>
/// Where the quick panel sits, in physical pixels.
/// </summary>
/// <remarks>
/// Pure geometry, and separate from the window for the reason every other piece
/// of geometry in this project is: synthetic pointer input does not reach a
/// WinUI surface, so a flyout cannot be positioned and then measured through
/// automation. What can be checked is the arithmetic, and it is checked in
/// <c>presetcheck</c>.
/// </remarks>
public static class QuickPanelPlacement
{
    /// <summary>
    /// Gap between the panel and the edges of the work area, in DIP.
    /// </summary>
    /// <remarks>
    /// Windows' own quick settings leaves 12. Matching it is most of what makes
    /// a flyout read as part of the shell rather than as an application window
    /// that happens to be small.
    /// </remarks>
    public const int MarginDip = 12;

    /// <summary>Works out which edge the taskbar occupies from the space it took.</summary>
    /// <remarks>
    /// Derived rather than asked for, because <c>ABM_GETTASKBARPOS</c> answers
    /// for the primary taskbar only, and the panel can be summoned on any
    /// display. The work area is per display and already accounts for whichever
    /// bar is there — including none, on a display whose taskbar DisplCtrl hides.
    /// </remarks>
    public static ScreenEdge EdgeOf(DisplayRect monitor, DisplayRect work)
    {
        int left = work.Left - monitor.Left;
        int top = work.Top - monitor.Top;
        int right = monitor.Right - work.Right;
        int bottom = monitor.Bottom - work.Bottom;

        int widest = Math.Max(Math.Max(left, top), Math.Max(right, bottom));
        if (widest <= 0) return ScreenEdge.None;

        // Bottom first: it is where the taskbar is on almost every machine, and
        // a tie should not resolve to something exotic.
        if (bottom == widest) return ScreenEdge.Bottom;
        if (top == widest) return ScreenEdge.Top;
        if (left == widest) return ScreenEdge.Left;
        return ScreenEdge.Right;
    }

    /// <summary>
    /// Places a panel of <paramref name="width"/> by <paramref name="height"/>
    /// inside <paramref name="work"/>, anchored near <paramref name="anchorX"/>.
    /// </summary>
    /// <param name="work">The display's work area, in physical pixels.</param>
    /// <param name="edge">Which edge the taskbar is on.</param>
    /// <param name="anchorX">
    /// Where along the bar the panel was summoned from — the tray icon, usually.
    /// Ignored for a side taskbar, where there is only one sensible position.
    /// </param>
    /// <param name="margin">The gap, already scaled to this display's DPI.</param>
    /// <remarks>
    /// The panel hugs the corner the pointer came from rather than centring on
    /// the anchor, because a flyout that slides along the bar with the icon it
    /// was opened from is a flyout whose position has to be re-found every time.
    /// Windows anchors its own to the corner for the same reason.
    /// </remarks>
    public static DisplayRect Place(
        DisplayRect work, ScreenEdge edge, int anchorX, int width, int height, int margin)
    {
        // A panel taller than the screen is not made to fit by moving it; the
        // window shrinks itself instead. Clamping here only keeps the maths sane.
        width = Math.Min(width, Math.Max(1, work.Width - (2 * margin)));
        height = Math.Min(height, Math.Max(1, work.Height - (2 * margin)));

        bool nearLeft = anchorX < work.Left + (work.Width / 2);

        int x = edge switch
        {
            // Against a side bar the panel goes beside it, at the bottom, which
            // is where the tray is even when the bar is vertical.
            ScreenEdge.Left => work.Left + margin,
            ScreenEdge.Right => work.Right - width - margin,

            // Along a horizontal bar, the corner the anchor is nearest.
            _ => nearLeft ? work.Left + margin : work.Right - width - margin,
        };

        int y = edge switch
        {
            ScreenEdge.Top => work.Top + margin,
            _ => work.Bottom - height - margin,
        };

        // The work area is the last word: a monitor narrower than the panel, or
        // an anchor reported outside it, must not push the panel off screen.
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));

        return new DisplayRect(x, y, x + width, y + height);
    }

    /// <summary>The tallest the panel may be on this display, in physical pixels.</summary>
    /// <remarks>
    /// The panel scrolls rather than growing past the screen. Windows' quick
    /// settings does not scroll because its contents are fixed; this one's are
    /// not — a desk with four monitors and every section turned on is taller
    /// than any display.
    /// </remarks>
    public static int MaxHeight(DisplayRect work, int margin) =>
        Math.Max(120, work.Height - (2 * margin));

    /// <summary>Scales a DIP measurement to a display's pixels.</summary>
    public static int Scale(int dip, uint dpi) => (int)Math.Round(dip * dpi / 96.0);
}
