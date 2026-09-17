using Umbra.Core.Displays;
using Umbra.Core.Settings;

namespace Umbra.Engine.Color;

/// <summary>
/// Keeps every display's warmth matching the schedule, for as long as the
/// engine is running.
/// </summary>
/// <remarks>
/// This belongs in the engine rather than the panel because a schedule is only
/// worth having if it fires when nobody is looking. The panel writes the
/// settings and shows an immediate preview; what actually holds the displays
/// warm at 2am is this.
/// <para>
/// Windows resets a display's gamma ramp on a mode change, a resume from sleep
/// and a replug. Re-asserting on a slow tick covers all three for less code
/// than subscribing to each would take.
/// </para>
/// </remarks>
internal sealed class NightLightService : IDisposable
{
    /// <summary>
    /// How often the schedule is re-evaluated and the ramp re-asserted.
    /// </summary>
    /// <remarks>
    /// 20 seconds. Long enough that the cost is nothing — a gamma write is a
    /// few hundred microseconds, so this is well under a millisecond a minute —
    /// and short enough that a display coming back from sleep is warm again
    /// before it is worth noticing.
    /// </remarks>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    private readonly Lock _gate = new();
    private readonly Timer _timer;

    private UmbraSettings _settings;
    private bool _warm;
    private int _appliedStrength = -1;
    private bool _disposed;

    public NightLightService(UmbraSettings settings)
    {
        _settings = settings;

        RecoverFromAbandonedRamps();
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, Interval);
    }

    /// <summary>
    /// Clears warmth left behind by an engine that did not get to shut down.
    /// </summary>
    /// <remarks>
    /// A gamma ramp outlives the process that set it, and the baseline needed
    /// to undo it does not. Killed rather than stopped — a crash, a forced
    /// logoff — the displays would otherwise stay warm with no control on
    /// screen admitting to it, because night light may well be switched off.
    /// <para>
    /// Only runs when the warmth is not wanted right now; when it is, the
    /// upcoming tick overwrites the ramp anyway.
    /// </para>
    /// </remarks>
    private void RecoverFromAbandonedRamps()
    {
        try
        {
            if (_settings.Global.NightLight.ActiveAt(DateTime.Now)) return;

            foreach (DisplayInfo d in DisplayRegistry.Enumerate())
            {
                if (!NightLight.LooksWarmed(d)) continue;

                _ = NightLight.ResetToNeutral(d);
                Log.Write($"cleared an abandoned warm ramp on {d.GdiName}");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"night light recovery failed: {ex.Message}");
        }
    }

    /// <summary>Takes a reloaded settings file and reacts at once.</summary>
    public void Update(UmbraSettings settings)
    {
        lock (_gate) { _settings = settings; }
        Tick();
    }

    private void Tick()
    {
        try
        {
            NightLightSettings config;
            lock (_gate)
            {
                if (_disposed) return;
                config = _settings.Global.NightLight;
            }

            if (!config.ActiveAt(DateTime.Now))
            {
                if (!_warm) return;

                NightLight.ClearAll();
                _warm = false;
                _appliedStrength = -1;
                Log.Write("night light off");
                return;
            }

            // Re-applied every tick even when nothing changed, for the reasons
            // in the type remarks: otherwise the displays quietly go cold after
            // a resume and stay that way.
            int strength = Math.Clamp(config.Strength, 0, 100);
            foreach (DisplayInfo d in DisplayRegistry.Enumerate())
                _ = NightLight.Apply(d, strength);

            if (!_warm || strength != _appliedStrength)
                Log.Write($"night light on at {strength}% ({NightLight.KelvinFor(strength):0}K)");

            _warm = true;
            _appliedStrength = strength;
        }
        catch (Exception ex)
        {
            // A tick that throws must not take the engine down with it: the
            // taskbar work matters more than the warmth does.
            Log.Write($"night light tick failed: {ex.Message}");
        }
    }

    /// <summary>Puts every display back the way it was found.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _timer.Dispose();

        try
        {
            NightLight.ClearAll();
        }
        catch (Exception ex)
        {
            Log.Write($"night light restore failed: {ex.Message}");
        }
    }
}
