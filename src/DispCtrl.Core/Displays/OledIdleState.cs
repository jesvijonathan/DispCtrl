namespace DispCtrl.Core.Displays;

/// <summary>Per-panel idle rest that survives activity on other displays.</summary>
public sealed class OledIdleState
{
    private long? _restStarted, _wokeAt;
    private bool _pointerKnown;
    private int _x, _y;

    public uint Update(bool enabled, bool returnToWake, long now, bool inputKnown, uint systemIdle,
        int idleMinutes, bool pointerKnown, int x, int y, DisplayRect bounds)
    {
        bool movedHere = pointerKnown && _pointerKnown && (x != _x || y != _y)
            && x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom;
        _pointerKnown = pointerKnown;
        _x = x;
        _y = y;
        if (!enabled || !inputKnown || (returnToWake && !pointerKnown))
        {
            _restStarted = _wokeAt = null;
            return 0;
        }
        if (!returnToWake)
        {
            _restStarted = _wokeAt = null;
            return systemIdle;
        }
        if (movedHere)
        {
            _restStarted = null;
            _wokeAt = now;
            return 0;
        }
        uint age = _restStarted is { } started ? Age(now - started)
            : _wokeAt is { } woke ? Math.Min(systemIdle, Age(now - woke)) : systemIdle;
        if (_restStarted is null && age >= Math.Clamp(idleMinutes, 1, 120) * 60_000L)
            _restStarted = now - age;
        return age;
    }

    private static uint Age(long milliseconds) => (uint)Math.Clamp(milliseconds, 0, uint.MaxValue);
}
