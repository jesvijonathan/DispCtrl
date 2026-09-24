namespace DispCtrl.Core.Settings;

/// <summary>How the resident engine temporarily overrides system sleep.</summary>
public enum AwakeMode
{
    PowerPlan,
    Indefinite,
    Timed,
    Expiration,
}

/// <summary>
/// PowerToys Awake-style power requests. These do not rewrite the active
/// Windows power plan; they only remain in force while the engine is running.
/// </summary>
/// <summary>Which displays "Turn off displays" blacks out.</summary>
/// <remarks>Decided by the engine when they go off, so "the one with the pointer" is where it is then.</remarks>
public enum DisplaysOffTarget
{
    All,
    ExceptMain,
    ExceptPointer,
    ExceptActiveWindow,
    OnlyMain,
}

public sealed class AwakeSettings
{
    public AwakeMode Mode { get; set; }
    public bool KeepDisplaysOn { get; set; }

    /// <summary>
    /// Keeps the screen on and the session looking attended, until switched off:
    /// no timer. See the engine's <c>PowerService</c>.
    /// </summary>
    /// <remarks>
    /// Keep awake stops Windows sleeping, but Windows and other programs still
    /// read a quiet keyboard and mouse as absence: the screen saver and lock
    /// screen come, and chat apps mark the person Away. Staying active answers
    /// that the way a mouse jiggler does - a one-pixel nudge there and back when
    /// nobody has touched anything for a minute - and holds the display on.
    /// </remarks>
    public bool StayActive { get; set; }

    /// <summary>When "Turn off displays" was asked for; null while the displays are on.</summary>
    /// <remarks>
    /// A request rather than a state. The engine blacks the displays out once
    /// <see cref="DisplaysOffDelaySeconds"/> has passed, with the overlay OLED
    /// care rests with - "off" to the eye, while the monitor, its brightness and
    /// every window stay as they were - and writes this back to null when every
    /// display it turned off has been woken, so the switch goes off by itself.
    /// </remarks>
    public DateTimeOffset? DisplaysOffUtc { get; set; }

    /// <summary>How dark "off" is: 100 is black.</summary>
    public int DisplaysOffPercent { get; set; } = 100;

    /// <summary>Seconds between asking and the displays going dark.</summary>
    /// <remarks>Time to take a hand off the mouse, or to move the pointer to the screen being kept.</remarks>
    public int DisplaysOffDelaySeconds { get; set; } = 1;

    public DisplaysOffTarget DisplaysOffTarget { get; set; } = DisplaysOffTarget.All;

    /// <summary>
    /// Wake a display only when the pointer moves on it, rather than on any
    /// input: a download or a render can be typed at, or a long read scrolled
    /// with the keys, without lighting every screen.
    /// </summary>
    public bool DisplaysOffWakeOnPointer { get; set; }

    // Staying awake or active while they are off is Keep awake's and Stay
    // active's own switches, shown again beside these: one setting in every
    // place it appears, never a second copy that only applies meanwhile.

    /// <summary>Park the pointer in a corner of a display that went off, so no arrow floats on the black.</summary>
    /// <remarks>
    /// Moved, not hidden: a hidden system cursor that outlived a crash would be
    /// far worse than a visible one. Put back where it was if they are turned
    /// back on without the mouse; a mouse that wakes them simply carries on.
    /// </remarks>
    public bool DisplaysOffHidePointer { get; set; } = true;

    /// <summary>Lock the computer the moment the displays come back on.</summary>
    /// <remarks>
    /// Walking away with the screens black and everything still running, while
    /// nobody passing can use it: whoever wakes it meets the lock screen. Any
    /// end counts - the first display woken, the shortcut, the panel, the way
    /// back - or the shortcut would be the way round it.
    /// </remarks>
    public bool DisplaysOffLockOnWake { get; set; }
    public int IntervalHours { get; set; } = 1;
    public int IntervalMinutes { get; set; }
    public DateTimeOffset TimedUntilUtc { get; set; }
    public DateTimeOffset ExpirationUtc { get; set; } = DateTimeOffset.Now.AddHours(1);

    public bool ActiveAt(DateTimeOffset now) => Mode switch
    {
        AwakeMode.Indefinite => true,
        AwakeMode.Timed => TimedUntilUtc > now,
        AwakeMode.Expiration => ExpirationUtc > now,
        _ => false,
    };
}
