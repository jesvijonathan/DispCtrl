namespace DispCtrl.Core.Settings;

/// <summary>
/// Protection against the Windows kernel bug a monitor's capabilities string can trigger.
/// </summary>
/// <remarks>
/// Microsoft documents that reading the capabilities string of a monitor with a
/// malformed one can crash Windows (a blue screen) - PowerToys' Power Display
/// guards the same read. Every DispCtrl process marks the read on disk before it
/// starts and clears the mark after; a mark found from an earlier boot means that
/// read never finished because Windows went down, and the monitor is blocked
/// here. See <c>DispCtrl.Display.DdcGuard</c>.
/// </remarks>
public sealed class DdcGuardSettings
{
    /// <summary>Watch capabilities reads and stop talking to a monitor that took Windows down.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Monitors DispCtrl no longer talks to over DDC/CI, until allowed again.</summary>
    public List<DdcBlock> Blocked { get; set; } = [];

    /// <summary>Whether a monitor, by identity token, is blocked.</summary>
    public bool IsBlocked(string token) => Enabled && Blocked.Any(b => string.Equals(b.Token, token, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One monitor DDC/CI is not used on, and why.</summary>
public sealed class DdcBlock
{
    /// <summary>The monitor's identity token; this unit, not every one of its model.</summary>
    public string Token { get; set; } = "";

    /// <summary>The model, as <c>DEL-A234</c>, so the list says what kind of monitor it is.</summary>
    public string Model { get; set; } = "";

    /// <summary>The monitor's name as Windows reported it.</summary>
    public string Label { get; set; } = "";

    public DateTimeOffset SinceUtc { get; set; }

    /// <summary>Why, in a sentence.</summary>
    public string Reason { get; set; } = "";
}
