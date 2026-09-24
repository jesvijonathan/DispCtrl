namespace DispCtrl.Core.Settings;

/// <summary>Shared window focus settings. No per-application injection or pixel sampling.</summary>
public sealed class FocusSettings
{
    public bool Enabled { get; set; }
    public int DimPercent { get; set; } = 65;
    public int DelayMs { get; set; } = 250;
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
    public bool PerMonitorFocus { get; set; }

    /// <summary>Keep the window under the pointer clear as well as the focused one.</summary>
    /// <remarks>
    /// For reading one window while typing in another: the pointer is resting on
    /// the thing being read, the keyboard belongs to the thing being written, and
    /// dimming either of them is wrong. Both cut-outs are held open until the
    /// pair changes.
    /// </remarks>
    public bool KeepHoveredClear { get; set; }

    /// <summary>Keep whatever the pointer is over clear, rather than the focused window.</summary>
    /// <remarks>
    /// For reading on one screen while something plays on another: the window
    /// with keyboard focus is not always the one being looked at. Mouse movement
    /// raises no window event, so this is the one thing here that has to be
    /// polled; it costs nothing while it is off.
    /// </remarks>
    public bool FollowMouse { get; set; }

    /// <summary>Temporarily prefer a newly shown or activated top-level window.</summary>
    /// <remarks>
    /// This matters when focus otherwise follows the pointer: launchers,
    /// scripts and accessibility tools can bring up a window without moving
    /// the mouse. The new window stays clear until the pointer moves again.
    /// </remarks>
    public bool PrioritizeNewWindows { get; set; } = true;
    public bool DimOtherMonitors { get; set; } = true;
    public bool PauseFullscreen { get; set; } = true;
    public bool KeepTaskbarVisible { get; set; } = true;
    /// <summary>Executable names separated by commas, semicolons or newlines.</summary>
    public string ExcludedApps { get; set; } = "";

    public HashSet<string> Exclusions() => (ExcludedApps ?? "").Split([',', ';', '\r', '\n'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Black or dimmed screen rest for monitors identified as OLED.</summary>
public sealed class OledCareSettings
{
    /// <summary>Dim automatically after <see cref="IdleMinutes"/> without input.</summary>
    public bool Enabled { get; set; }
    public int IdleMinutes { get; set; } = 5;
    public int DimPercent { get; set; } = 100;
    public bool SecondStageEnabled { get; set; }
    public int SecondStageMinutes { get; set; } = 5;
    public int SecondStageDimPercent { get; set; } = 100;
    public int FadeMs { get; set; } = 800;
    public bool PauseFullscreen { get; set; } = true;

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
