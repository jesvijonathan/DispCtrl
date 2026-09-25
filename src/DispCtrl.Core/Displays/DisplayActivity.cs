namespace DispCtrl.Core.Displays;

/// <summary>
/// How long one display has gone unused, for OLED care that rests each display on its own.
/// </summary>
/// <remarks>
/// The ordinary idle clock is the whole computer's: typing on one screen keeps
/// every screen awake, and moving the pointer across one wakes them all. Used
/// per display, a screen nobody is looking at - the one with a static page on
/// it while the work goes on beside it - rests even though the computer is in
/// use. See <see cref="Used"/> for what counts as using a display.
/// </remarks>
public sealed class DisplayActivity
{
    private long? _last;

    /// <summary>Milliseconds since the display was last used, counting this moment when it is.</summary>
    public uint Update(long now, bool used)
    {
        if (used || _last is null || now < _last) _last = now;
        return (uint)Math.Clamp(now - _last.Value, 0, uint.MaxValue);
    }

    /// <summary>Starts the clock again, as though the display had just been used.</summary>
    public void Reset(long now) => _last = now;

    /// <summary>
    /// Whether a display was used since the last look.
    /// </summary>
    /// <param name="bounds">The display.</param>
    /// <param name="pointerMoved">The pointer moved since the last look.</param>
    /// <param name="pointerX">Where it is now.</param>
    /// <param name="pointerY">Where it is now.</param>
    /// <param name="inputSince">A person gave any input since the last look (not Stay active's nudge).</param>
    /// <param name="front">The window in front, when there is one worth counting.</param>
    /// <param name="pointerOnly">Only the pointer wakes this display, as "Wake when the pointer returns" asks.</param>
    /// <remarks>
    /// Windows does not say which device the input came from, so any input
    /// counts for the display holding the window in front - where typing goes -
    /// or the pointer's display when nothing usable is in front, and a pointer
    /// that moved counts for the display it is on as well. Typing on one screen
    /// while the mouse wanders over another keeps both awake: an extra display
    /// left lit is a smaller mistake than dimming the one being typed into.
    /// </remarks>
    public static bool Used(DisplayRect bounds, bool pointerMoved, int pointerX, int pointerY,
        bool inputSince, DisplayRect? front, bool pointerOnly)
    {
        bool pointerHere = bounds.Contains(pointerX, pointerY);
        if (pointerMoved && pointerHere) return true;
        if (!inputSince || pointerOnly) return false;
        // Half of it or more: a window straddling two displays evenly counts for
        // both, and one overhanging its neighbour by a few pixels only for its own.
        if (front is { Width: > 0, Height: > 0 } window)
        {
            long w = Math.Min(window.Right, bounds.Right) - Math.Max(window.Left, bounds.Left);
            long h = Math.Min(window.Bottom, bounds.Bottom) - Math.Max(window.Top, bounds.Top);
            return w > 0 && h > 0 && w * h * 2 >= (long)window.Width * window.Height;
        }
        return pointerHere;
    }
}
