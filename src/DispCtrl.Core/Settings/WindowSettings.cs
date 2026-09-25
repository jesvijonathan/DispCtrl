namespace DispCtrl.Core.Settings;

/// <summary>Pinning a window on top of every other, and how a pinned window looks.</summary>
/// <remarks>
/// What is pinned is not stored here: it is the window's own state, marked with
/// a window property (<c>WindowPins</c>), so it lives exactly as long as the
/// window does and any DispCtrl process can see it. These are the preferences.
/// </remarks>
public sealed class PinSettings
{
    /// <summary>Whether pinning is offered at all. Switching it off unpins every window DispCtrl pinned.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Draw a coloured border around each pinned window.</summary>
    public bool Border { get; set; } = true;

    /// <summary>The border's colour as <c>#RRGGBB</c>; empty for Windows' accent colour.</summary>
    public string BorderColour { get; set; } = "";

    /// <summary>The border's width, in DIP.</summary>
    public int BorderThickness { get; set; } = 3;

    /// <summary>The border's opacity, 20-100.</summary>
    public int BorderOpacity { get; set; } = 100;

    /// <summary>Keep pinned windows clear of focus mode's dimming.</summary>
    /// <remarks>
    /// The part PowerToys cannot do: a video or a chat pinned beside the work is
    /// pinned to be seen, and focus mode dims everything that is not in use.
    /// </remarks>
    public bool ClearInFocus { get; set; } = true;

    /// <summary>Keep pinned windows clear of OLED idle dimming as well.</summary>
    /// <remarks>
    /// Off by default: idle dimming exists to rest a panel nobody is looking at,
    /// and a window that sits in one place for hours is the burn-in it protects
    /// against. On for a pinned video that plays while the desk is idle.
    /// </remarks>
    public bool ClearInOledCare { get; set; }

    /// <summary>Refuse to pin a window that fills its display, such as a game.</summary>
    public bool SkipFullscreen { get; set; } = true;

    /// <summary>Let a fullscreen window in front go over pinned windows on its display, for as long as it is there.</summary>
    /// <remarks>
    /// A film or a game started after something was pinned would otherwise play
    /// under it, and an exclusive-fullscreen game can minimize itself when a
    /// topmost window takes the front. The pins come back when it leaves.
    /// </remarks>
    public bool StepAsideForFullscreen { get; set; } = true;

    /// <summary>Executable names never pinned, separated by commas.</summary>
    public string ExcludedApps { get; set; } = "";

    public HashSet<string> Exclusions() => AppList.Parse(ExcludedApps);
}

/// <summary>Where a window returned to a display, or a new one, is decided to go.</summary>
public enum ActiveDisplay
{
    /// <summary>The display the pointer is on.</summary>
    Pointer,

    /// <summary>The display the active window is on.</summary>
    ActiveWindow,
}

/// <summary>Moving windows between displays: gathering, putting back, and new ones.</summary>
public sealed class PlacementSettings
{
    /// <summary>
    /// Put windows back on a display when it returns, as they were when it left.
    /// </summary>
    /// <remarks>
    /// Keyed on the monitor's own identity, so the same monitor on another port
    /// still counts, and only windows still where Windows parked them. Windows
    /// 11's own window memory is left as it is and works alongside: measured on
    /// a real unplug, it put every visible window back within the 1.5 s this
    /// waits, those were then already home and left alone, and this put back
    /// what it skips (minimized windows' restore positions).
    /// </remarks>
    public bool ReturnWindows { get; set; }

    /// <summary>Whether an earlier build switched Windows' own window memory off, and owes it back.</summary>
    /// <remarks>
    /// Development builds of 0.1.5 took Windows' memory over by its registry
    /// value, which Windows does not read live: it went on putting windows
    /// back, and the value was left to switch it off later. No release did;
    /// this only ever hands it back.
    /// </remarks>
    public bool TookOverWindowsMemory { get; set; }

    /// <summary>Move a newly opened window to the display in use, when it opens somewhere else.</summary>
    /// <remarks>
    /// FancyZones' "move newly created windows to the active monitor". Costs a
    /// window-shown hook while on, so it is off by default.
    /// </remarks>
    public bool NewWindowsOnActive { get; set; }

    /// <summary>Which display counts as the one in use for new windows and for gathering.</summary>
    public ActiveDisplay Active { get; set; } = ActiveDisplay.Pointer;

    /// <summary>Keep a gathered window's size in real units (DIP) rather than in pixels.</summary>
    /// <remarks>
    /// A window moved from a 100% display to a 200% one is otherwise half the
    /// size it was to the eye. Per-monitor aware apps rescale themselves; this
    /// sizes the rest the same way.
    /// </remarks>
    public bool KeepSize { get; set; } = true;

    /// <summary>Executable names never moved by gathering, returning or new-window placement.</summary>
    public string ExcludedApps { get; set; } = "";

    public HashSet<string> Exclusions() => AppList.Parse(ExcludedApps);
}

/// <summary>A comma-separated list of executable names, as typed into a settings box.</summary>
public static class AppList
{
    public static HashSet<string> Parse(string? text) => (text ?? "").Split([',', ';', '\r', '\n'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
