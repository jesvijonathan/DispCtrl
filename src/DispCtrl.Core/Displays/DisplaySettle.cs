namespace DispCtrl.Core.Displays;

/// <summary>
/// Decides when a change to the set of displays is over, from a fingerprint of the desk.
/// </summary>
/// <remarks>
/// A monitor does not arrive in one step. Windows enumerates it, applies a
/// mode, moves the desktop around it and then Explorer rebuilds its taskbars,
/// and a loose cable adds a departure and a return inside a second. Acting on
/// each step - rediscovering taskbars, writing brightness, rebuilding the
/// panel - acted on layouts that were gone a moment later. Monitorian and
/// Twinkle Tray both wait for the events to stop before they rescan; this is
/// the same idea, keyed on the layout itself: a fingerprint has to hold for
/// <see cref="QuietMs"/> before it counts, and one that comes back to where it
/// started never counts at all, so a cable that drops and returns does nothing.
/// <para>Pure, and driven by the caller's clock, so presetverify can run it.</para>
/// </remarks>
public sealed class DisplaySettle
{
    /// <summary>How long a new layout has to hold before it is the desk.</summary>
    public const int QuietMs = 1500;

    private string _settled;
    private string _candidate;
    private long _since;

    public DisplaySettle(string current)
    {
        _settled = _candidate = current;
    }

    /// <summary>The fingerprint of the desk as last settled.</summary>
    public string Settled => _settled;

    /// <summary>Whether a different layout is being waited out.</summary>
    public bool Pending => _candidate != _settled;

    /// <summary>Takes the desk's fingerprint now.</summary>
    /// <returns>True when a new layout has held for <see cref="QuietMs"/>.</returns>
    public bool Observe(string signature, long nowMs)
    {
        if (signature != _candidate)
        {
            _candidate = signature;
            _since = nowMs;
            return false;
        }
        if (_candidate == _settled || nowMs - _since < QuietMs) return false;
        _settled = _candidate;
        return true;
    }
}
