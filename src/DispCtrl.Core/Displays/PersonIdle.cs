namespace DispCtrl.Core.Displays;

/// <summary>
/// How long the desk has been left alone, not counting Stay active's nudge.
/// </summary>
/// <remarks>
/// The nudge is real input to Windows, so <c>GetLastInputInfo</c> never got
/// past its 55 s: OLED idle care, both stages, never came with Stay active on,
/// and a manual rest ended at the first nudge. Staying active is about the
/// lock screen and chat status; an unattended static screen is exactly what
/// burn-in care is for. Input within half a second of a nudge is taken for the
/// nudge, the same allowance turned-off displays use. Needs to be asked at least
/// once between a person's input and the next nudge, which the protection
/// thread's once-a-second tick is, against a nudge that waits 55 s.
/// </remarks>
public sealed class PersonIdle
{
    private const long NudgeWindowMs = 500;
    private long? _lastPerson;

    /// <param name="now">The current <c>Environment.TickCount64</c>.</param>
    /// <param name="systemIdle">Milliseconds since Windows last saw any input.</param>
    /// <param name="lastNudge">When Stay active last nudged, on the same clock; 0 for never.</param>
    public uint Update(long now, uint systemIdle, long lastNudge)
    {
        long lastInput = now - systemIdle;
        bool nudge = lastNudge != 0 && Math.Abs(lastInput - lastNudge) < NudgeWindowMs;
        if (!nudge || _lastPerson is null) _lastPerson = lastInput;
        return (uint)Math.Clamp(now - _lastPerson.Value, 0, uint.MaxValue);
    }
}
