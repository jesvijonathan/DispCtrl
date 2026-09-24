namespace DispCtrl.Core.Displays;

/// <summary>
/// Decides when the room's light has really changed, from a light sensor's readings.
/// </summary>
/// <remarks>
/// The first version averaged each reading with the last, once per reading.
/// A sensor reports only when the light changes, so when the light stopped
/// changing so did the average, partway there: a covered sensor settled at
/// about three-fifths of the old light and the desk barely moved. And its wait
/// restarted with every reading, so a torch, which flickers, never settled at
/// all. This keeps the newest reading as the room's light until another
/// arrives, and is asked again on a clock while it has somewhere to go.
/// <para>
/// Android's shape (<c>AutomaticBrightnessController</c>): a change has to
/// pass a threshold, brightening more readily than darkening, and hold for a
/// moment before it counts - a hand near the bezel or a passing shadow must not
/// make every screen breathe. Measured in log lux, as wluma does, because a
/// 15% change is as visible at 20 lux as at 2,000. Darkening waits longer:
/// a lean towards the screen should not dim it.
/// </para>
/// <para>Pure, and driven by the caller's clock, so presetverify can run it.</para>
/// </remarks>
public sealed class AmbientFilter
{
    /// <summary>How quickly the smoothed light follows a new reading.</summary>
    public const int TimeConstantMs = 800;

    /// <summary>How long a brighter room has to last before it counts.</summary>
    public const int BrightenAfterMs = 1500;

    /// <summary>How long a darker room has to last before it counts.</summary>
    public const int DarkenAfterMs = 3000;

    /// <summary>About +15% in log lux.</summary>
    private const double BrightenStep = 0.06;

    /// <summary>About -20% in log lux.</summary>
    private const double DarkenStep = 0.1;

    /// <summary>Near darkness a few lux is noise, whatever percentage it is.</summary>
    private const double NoiseLux = 2;

    private double _fast = double.NaN;
    private double _settled = double.NaN;
    private double _raw = double.NaN;
    private long _last;
    private long _since;
    private int _direction;

    /// <summary>The room's light as last accepted, in lux; negative before the first reading.</summary>
    public double SettledLux => double.IsNaN(_settled) ? -1 : Math.Pow(10, _settled) - 1;

    /// <summary>Whether another look is needed: a change still being weighed, or the smoothing not caught up.</summary>
    public bool Busy => _direction != 0 || !double.IsNaN(_fast) && Math.Abs(_fast - _raw) > 0.005;

    /// <summary>Takes the sensor's newest reading, which stands until the next.</summary>
    /// <returns>True when the settled light changed: the first reading, or a change that held.</returns>
    public bool Observe(double lux, long nowMs)
    {
        double x = AmbientCurve.Coordinate(lux);
        _raw = x;
        if (double.IsNaN(_fast))
        {
            // The first reading acts at once: switching this on should show it working.
            _fast = _settled = x;
            _last = nowMs;
            return true;
        }

        double dt = Math.Max(0, nowMs - _last);
        _last = nowMs;
        _fast += (1 - Math.Exp(-dt / TimeConstantMs)) * (x - _fast);

        // The smoothed light and the newest reading have to agree, as Android's
        // fast and slow averages do: after a deep shadow the average takes
        // seconds to climb back, and alone it counted a hand that had already
        // moved away as the room going dark.
        int direction = Direction(_fast);
        if (Direction(x) != direction) direction = 0;

        if (direction == 0) { _direction = 0; return false; }
        if (direction != _direction) { _direction = direction; _since = nowMs; return false; }
        if (nowMs - _since < (direction > 0 ? BrightenAfterMs : DarkenAfterMs)) return false;

        _settled = _fast;
        _direction = 0;
        return true;
    }

    private int Direction(double x)
    {
        double diff = x - _settled;
        if (Math.Abs(Math.Pow(10, x) - Math.Pow(10, _settled)) < NoiseLux) return 0;
        return diff >= BrightenStep ? 1 : diff <= -DarkenStep ? -1 : 0;
    }

    /// <summary>Forgets everything, as for a different sensor.</summary>
    public void Reset()
    {
        _fast = _settled = _raw = double.NaN;
        _direction = 0;
    }
}
