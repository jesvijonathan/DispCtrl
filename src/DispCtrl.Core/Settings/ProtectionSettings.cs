using System.Text.Json.Serialization;

namespace DispCtrl.Core.Settings;

/// <summary>Which windows focus mode keeps clear: one choice over two stored switches.</summary>
public enum FocusClear
{
    /// <summary>The window with keyboard focus.</summary>
    Focused,

    /// <summary>The window under the pointer, instead of the focused one.</summary>
    Pointer,

    /// <summary>Both: the focused window and the one under the pointer.</summary>
    Both,
}

/// <summary>Shared window focus settings. No per-application injection or pixel sampling.</summary>
public sealed class FocusSettings
{
    /// <summary>
    /// <see cref="FollowMouse"/> and <see cref="KeepHoveredClear"/> as the one
    /// question they answer.
    /// </summary>
    /// <remarks>
    /// Two switches read as the same thing twice. Following the mouse makes the
    /// window under the pointer the clear one <em>instead of</em> the focused
    /// one; keeping the hovered window clear keeps it clear <em>as well</em>.
    /// With the second on, the first changes only which of the pair counts as
    /// in use, so Both leaves it as it was.
    /// </remarks>
    [JsonIgnore]
    public FocusClear Clear
    {
        get => KeepHoveredClear ? FocusClear.Both : FollowMouse ? FocusClear.Pointer : FocusClear.Focused;
        set
        {
            switch (value)
            {
                case FocusClear.Focused: FollowMouse = false; KeepHoveredClear = false; break;
                case FocusClear.Pointer: FollowMouse = true; KeepHoveredClear = false; break;
                default: KeepHoveredClear = true; break;
            }
        }
    }

    public bool Enabled { get; set; }
    public int DimPercent { get; set; } = 64;
    public int DelayMs { get; set; } = 500;
    public int FadeMs { get; set; } = 300;
    public bool OledOnly { get; set; }

    /// <summary>Slide the clear area across instead of cutting it at the new window.</summary>
    /// <remarks>
    /// Off by default, and it is the other reading of what a transition between
    /// windows should be: the shape travelling rather than the brightness
    /// changing. The shape arriving late is the part that reads as lag, so the
    /// default is to cut it instantly and cross-fade the brightness instead -
    /// see <see cref="CrossFadeWindows"/>.
    /// </remarks>
    public bool EaseBetweenWindows { get; set; }

    /// <summary>Fade the old cut-out out and the new one in when the window changes.</summary>
    /// <remarks>
    /// The cut-outs are placed instantly; only their brightness moves. Uses
    /// <see cref="FadeMs"/>, which otherwise only covers dimming starting and
    /// stopping and so never ran on a switch - the reason that setting looked
    /// like it did nothing.
    /// </remarks>
    public bool CrossFadeWindows { get; set; } = true;

    /// <summary>Ease the dimming on panels that unison brightness is already running dim.</summary>
    /// <remarks>
    /// On by default because the alternative is a setting that means something
    /// different on every panel: the same overlay over a screen at a fifth of
    /// its brightness is far heavier than over one at full output.
    /// </remarks>
    public bool ScaleWithBrightness { get; set; } = true;

    /// <summary>Give every display its own clear window rather than sharing one.</summary>
    /// <remarks>
    /// With one subject for the whole desk, the display you are not typing on is
    /// dimmed entire - including whatever you put there to watch or refer to.
    /// Each display instead remembers the last window used on it and keeps that
    /// one clear, so glancing across finds the screen already legible.
    /// <para>
    /// A display that has not been used yet has nothing to keep clear, and is
    /// dimmed until something is focused on it.
    /// </para>
    /// </remarks>
    public bool PerMonitorFocus { get; set; } = true;

    /// <summary>Keep the window under the pointer clear as well as the focused one.</summary>
    /// <remarks>
    /// For reading one window while typing in another: the pointer is resting on
    /// the thing being read, the keyboard belongs to the thing being written, and
    /// dimming either of them is wrong. Both cut-outs are held open until the
    /// pair changes.
    /// </remarks>
    public bool KeepHoveredClear { get; set; } = true;

    /// <summary>Keep whatever the pointer is over clear, rather than the focused window.</summary>
    /// <remarks>
    /// For reading on one screen while something plays on another: the window
    /// with keyboard focus is not always the one being looked at. Mouse movement
    /// raises no window event, so this is the one thing here that has to be
    /// polled; it costs nothing while it is off.
    /// </remarks>
    public bool FollowMouse { get; set; } = true;

    /// <summary>Temporarily prefer a newly shown or activated top-level window.</summary>
    /// <remarks>
    /// This matters when focus otherwise follows the pointer: launchers,
    /// scripts and accessibility tools can bring up a window without moving
    /// the mouse. The new window stays clear until the pointer moves again.
    /// </remarks>
    public bool PrioritizeNewWindows { get; set; } = true;
    public bool DimOtherMonitors { get; set; } = true;
    public bool PauseFullscreen { get; set; } = true;
    public bool KeepTaskbarVisible { get; set; }
    /// <summary>Executable names separated by commas, semicolons or newlines.</summary>
    public string ExcludedApps { get; set; } = "";

    public HashSet<string> Exclusions() => AppList.Parse(ExcludedApps);
}

/// <summary>Black or dimmed screen rest for monitors identified as OLED.</summary>
public sealed class OledCareSettings
{
    /// <summary>Dim automatically after <see cref="IdleMinutes"/> without input.</summary>
    public bool Enabled { get; set; }
    public int IdleMinutes { get; set; } = 4;
    public int DimPercent { get; set; } = 60;
    public bool SecondStageEnabled { get; set; } = true;
    public int SecondStageMinutes { get; set; } = 12;
    public int SecondStageDimPercent { get; set; } = 95;
    public int FadeMs { get; set; } = 2000;
    public bool PauseFullscreen { get; set; } = true;

    /// <summary>Rest each display when it goes unused, rather than when the whole computer does.</summary>
    /// <remarks>
    /// Off, typing on one screen keeps every screen awake. On, a display rests
    /// once neither the pointer nor the window being typed into has been on it
    /// for <see cref="IdleMinutes"/>; see <see cref="Displays.DisplayActivity"/>.
    /// </remarks>
    public bool PerDisplayActivity { get; set; }

    /// <summary>Executable names that keep the display showing them awake, separated by commas.</summary>
    /// <remarks>
    /// A film or a dashboard is looked at without being touched. Any of these
    /// apps' windows showing on a display - not minimized, mostly on it - keeps
    /// that display from resting, whichever window has focus.
    /// </remarks>
    public string ExcludedApps { get; set; } = "";

    public HashSet<string> Exclusions() => AppList.Parse(ExcludedApps);

    /// <summary>The configured idle-rest level at a given system idle age.</summary>
    public int DimAtIdle(uint idleMilliseconds)
    {
        int first = Math.Clamp(DimPercent, 0, 100);
        if (!SecondStageEnabled || first is 0 or 100) return first;

        long threshold = (Math.Clamp(IdleMinutes, 1, 120)
            + Math.Clamp(SecondStageMinutes, 1, 120)) * 60_000L;
        return idleMilliseconds >= threshold
            ? Math.Clamp(SecondStageDimPercent, first, 100)
            : first;
    }
}
