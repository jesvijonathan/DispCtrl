namespace DispCtrl.Core.Displays;

/// <summary>The edge of its monitor a taskbar is docked to.</summary>
public enum TaskbarSide { Bottom, Top, Left, Right }

/// <summary>Where a hidden taskbar can go without showing on another monitor.</summary>
/// <remarks>
/// Pure geometry, so it is checked in presetverify: stacking monitors to test it
/// on hardware means rearranging the desk.
/// </remarks>
public static class TaskbarParking
{
    /// <summary>
    /// Whether the strip just past the bar's edge lies on another monitor, and
    /// the far side of the whole desktop along that edge.
    /// </summary>
    /// <remarks>
    /// A bar hidden just past its edge is off its own monitor, but with a
    /// monitor stacked against that edge it is on that one instead, in plain
    /// view. A blocked bar parks at <c>Beyond</c> (less its thickness for the
    /// top and left edges), where no monitor is.
    /// </remarks>
    public static (bool Blocked, int Beyond) Plan(DisplayRect monitor, TaskbarSide side, int thickness, IEnumerable<DisplayRect> displays)
    {
        DisplayRect m = monitor;
        int t = thickness;
        DisplayRect strip = side switch
        {
            TaskbarSide.Top => new DisplayRect(m.Left, m.Top - t, m.Right, m.Top),
            TaskbarSide.Left => new DisplayRect(m.Left - t, m.Top, m.Left, m.Bottom),
            TaskbarSide.Right => new DisplayRect(m.Right, m.Top, m.Right + t, m.Bottom),
            _ => new DisplayRect(m.Left, m.Bottom, m.Right, m.Bottom + t),
        };

        bool blocked = false;
        int beyond = side switch
        {
            TaskbarSide.Top => m.Top,
            TaskbarSide.Left => m.Left,
            TaskbarSide.Right => m.Right,
            _ => m.Bottom,
        };
        foreach (DisplayRect o in displays)
        {
            beyond = side switch
            {
                TaskbarSide.Top => Math.Min(beyond, o.Top),
                TaskbarSide.Left => Math.Min(beyond, o.Left),
                TaskbarSide.Right => Math.Max(beyond, o.Right),
                _ => Math.Max(beyond, o.Bottom),
            };
            if (o.Left < strip.Right && strip.Left < o.Right && o.Top < strip.Bottom && strip.Top < o.Bottom)
                blocked = true;
        }
        return (blocked, beyond);
    }
}
