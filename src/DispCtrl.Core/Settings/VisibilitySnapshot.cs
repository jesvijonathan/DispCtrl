namespace DispCtrl.Core.Settings;

/// <summary>
/// What the way back (<see cref="DispCtrlSettings.RestoreVisibility"/>) switched
/// off, so it can be put back afterwards.
/// </summary>
/// <remarks>
/// The way back is blunt on purpose: it has to work for someone looking at a
/// black screen. Its cost was that every monitor's taskbar hiding and dimming
/// had to be switched back on one by one; this is the one step that undoes it.
/// Only what was actually on is recorded, and a way back pressed again with
/// nothing left to switch off keeps a record under ten minutes old, so pressing
/// it twice never loses what the first press took away. Displays off and a screen rest are
/// not recorded: both are moments, not settings.
/// </remarks>
public sealed class VisibilitySnapshot
{
    public DateTimeOffset TakenUtc { get; set; }
    public bool Focus { get; set; }
    public bool OledCare { get; set; }
    public bool NightLight { get; set; }
    public int TaskbarOpacity { get; set; } = 100;

    /// <summary>Monitors that had taskbar hiding or software dimming on, by token.</summary>
    public Dictionary<string, MonitorVisibility> Monitors { get; set; } = [];
}

/// <summary>One monitor's part of a <see cref="VisibilitySnapshot"/>.</summary>
public sealed class MonitorVisibility
{
    public bool HideTaskbar { get; set; }
    public int SoftwareBrightness { get; set; } = 100;
}
