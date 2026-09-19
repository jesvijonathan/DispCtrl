namespace DisplCtrl.Core.Displays;

/// <summary>Screen-space geometry shared by the dimmer and its tests.</summary>
public static class FocusGeometry
{
    public static DisplayRect Intersect(DisplayRect a, DisplayRect b)
    {
        int left = Math.Max(a.Left, b.Left), top = Math.Max(a.Top, b.Top);
        return new(left, top, Math.Max(left, Math.Min(a.Right, b.Right)), Math.Max(top, Math.Min(a.Bottom, b.Bottom)));
    }
    public static bool Covers(DisplayRect window, DisplayRect monitor) =>
        window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
    public static byte Alpha(int dim) => (byte)Math.Round(Math.Clamp(dim, 0, 100) * 2.55);

    /// <summary>
    /// The alpha the arriving layer needs so that two stacked layers still read
    /// as exactly <paramref name="dim"/> while the leaving one fades away.
    /// </summary>
    /// <remarks>
    /// A switch between windows is one cut-out vanishing and another forming, and
    /// one region cannot fade in two places at once - it is either dim or clear.
    /// Two layers can: the old hole leaves with its layer, the new hole arrives
    /// with the other. Left to fade independently they would both sit near half
    /// for a moment and the whole surround would visibly lighten, because two
    /// layers compose as <c>1-(1-a)(1-b)</c> rather than adding. Solving that for
    /// the arriving layer keeps the background perfectly still while the holes
    /// trade places.
    /// </remarks>
    public static double Overlay(double dim, double leaving)
    {
        double d = Math.Clamp(dim, 0, 255) / 255.0;
        double g = Math.Clamp(leaving, 0, 255) / 255.0;

        if (g >= 1) return 0;

        return Math.Clamp(1 - ((1 - d) / (1 - g)), 0, 1) * 255;
    }

    /// <summary>The least of the chosen dim a panel keeps at its dimmest, as a percentage.</summary>
    /// <remarks>
    /// Not zero. Scaling all the way down would mean a panel at its calibrated
    /// floor got no dimming at all, which is not "relative" so much as "off" -
    /// and the surroundings still need to recede, however dim the panel is.
    /// </remarks>
    public const int MinimumDimShare = 50;

    /// <summary>
    /// The dim to actually apply to a panel expected to be at
    /// <paramref name="brightness"/> percent of its own range.
    /// </summary>
    /// <remarks>
    /// One overlay alpha does not mean one amount of dimming: laid over a panel
    /// at a fifth of its brightness it is far heavier than over one at full
    /// output, so a single figure that looks right on the bright screen buries
    /// the dim one. Scaling by where the panel sits between its own captured
    /// limits is what makes the setting mean the same thing on both.
    /// <para>
    /// Deliberately not applied to the OLED rest: that one is protecting a
    /// panel rather than framing a window, and easing it off on a dim screen
    /// would weaken exactly what it is for.
    /// </para>
    /// </remarks>
    public static int ScaledDim(int dim, int brightness)
    {
        int share = MinimumDimShare + ((100 - MinimumDimShare) * Math.Clamp(brightness, 0, 100) / 100);
        return Math.Clamp(Math.Clamp(dim, 0, 100) * share / 100, 0, 100);
    }

    /// <summary>
    /// The ground a window covered between two frames, so a hole cut from it
    /// cannot expose what the window has just left.
    /// </summary>
    /// <remarks>
    /// The overlay is repainted a frame behind the window it cuts around, which
    /// shows as dim flickering along the trailing edge of a dragged window. A
    /// fixed margin cannot fix that, because the distance travelled per frame
    /// depends entirely on how fast the drag is: measured on this desk, a gentle
    /// drag left about twelve pixels showing and a fast one far more. The union
    /// of where the window was and where it is covers exactly that gap at any
    /// speed.
    /// <para>
    /// <paramref name="limit"/> stops a window that jumped — snapped to an edge,
    /// moved to another monitor — from clearing a huge band of dim for a frame.
    /// Past it, the sweep is abandoned and only the new position is used.
    /// </para>
    /// </remarks>
    public static DisplayRect Sweep(DisplayRect from, DisplayRect to, int limit)
    {
        if (from.Width <= 0 || from.Height <= 0) return to;

        // A jump rather than a drag: nothing in between is worth clearing.
        if (Math.Abs(to.Left - from.Left) > limit || Math.Abs(to.Top - from.Top) > limit) return to;

        return new(Math.Min(from.Left, to.Left), Math.Min(from.Top, to.Top),
                   Math.Max(from.Right, to.Right), Math.Max(from.Bottom, to.Bottom));
    }

    /// <summary>How long a manual screen rest ignores input before input ends it.</summary>
    /// <remarks>
    /// A rest is asked for with a click, and that click is itself input, so with
    /// no grace period the rest would be cancelled by the very press that
    /// started it. Long enough to cover the click and the hand coming off the
    /// mouse; short enough that a deliberate nudge still wakes the panel at once.
    /// </remarks>
    public const long ManualRestGraceMs = 1500;

    /// <summary>
    /// Whether a manual screen rest is still wanted, given how long ago it was
    /// asked for and how long the machine has been idle.
    /// </summary>
    /// <remarks>
    /// Idle rest ends by itself, because it is keyed off the idle timer. A
    /// manual one is not: without this it stayed black under a moving cursor for
    /// its whole duration, while the panel that offers it promises that moving
    /// the mouse ends it early.
    /// <para>
    /// The test is whether any input has arrived since the rest began. Input at
    /// time T leaves <paramref name="idleMs"/> short of
    /// <paramref name="elapsedMs"/> by however long the rest had already been
    /// running when it landed, so the grace is what stops the starting click
    /// from counting.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The clear area part way through a move from one window to another.
    /// </summary>
    /// <remarks>
    /// Switching windows changes where the hole is, not how dark the dim is, so
    /// the fade that covers dimming starting and stopping never ran on the one
    /// interaction people do constantly - which is why the fade setting looked
    /// like it did nothing. Sliding the cut-out is the transition that belongs
    /// to a switch, and it uses the same easing so the two feel related.
    /// </remarks>
    public static DisplayRect Between(DisplayRect from, DisplayRect to, long elapsed, int duration)
    {
        if (duration <= 0 || from.Width <= 0 || from.Height <= 0) return to;

        double t = Math.Clamp(elapsed / (double)duration, 0, 1);
        double eased = t * t * (3 - (2 * t));

        int Mix(int a, int b) => (int)Math.Round(a + ((b - a) * eased));

        return new(Mix(from.Left, to.Left), Mix(from.Top, to.Top),
                   Mix(from.Right, to.Right), Mix(from.Bottom, to.Bottom));
    }

    /// <summary>
    /// Whether an OLED panel should be resting because the machine has been
    /// left alone.
    /// </summary>
    /// <remarks>
    /// Fails closed on purpose: with no readable idle clock this says no, so a
    /// broken reading leaves the screen alone rather than blacking a panel
    /// somebody is watching. The minutes are clamped because the figure comes
    /// from a settings file that is edited by hand.
    /// </remarks>
    public static bool RestingWhenIdle(bool enabled, bool inputKnown, long idleMs,
                                       int idleMinutes, bool suspended, bool pausedByFullscreen) =>
        enabled && inputKnown && !suspended && !pausedByFullscreen
        && idleMs >= Math.Clamp(idleMinutes, 1, 120) * 60_000L;

    public static bool RestingByHand(bool requested, long elapsedMs, long idleMs, bool inputKnown) =>
        requested && (!inputKnown
                      || elapsedMs <= ManualRestGraceMs
                      || idleMs + ManualRestGraceMs >= elapsedMs);
    public static double Fade(double from, double to, long elapsed, int duration)
    {
        double t = duration <= 0 ? 1 : Math.Clamp(elapsed / (double)duration, 0, 1);
        return from + (to - from) * t * t * (3 - 2 * t);
    }
}
