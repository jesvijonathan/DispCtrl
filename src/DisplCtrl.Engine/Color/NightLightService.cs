using DisplCtrl.Core.Displays;
using DisplCtrl.Core.Settings;

namespace DisplCtrl.Engine.Color;

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

    private DisplCtrlSettings _settings;
    private bool _warm;
    private int _appliedStrength = -1;
    private bool _disposed;

    public NightLightService(DisplCtrlSettings settings)
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
            // Only when nothing wants a ramp; otherwise the upcoming tick
            // writes over whatever is there anyway.
            if (_settings.Global.NightLight.ActiveAt(DateTime.Now)) return;

            foreach (DisplayInfo d in DisplayRegistry.Enumerate())
                if (_settings.SoftwareBrightnessFor(d.Token) < 100) return;

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
    public void Update(DisplCtrlSettings settings)
    {
        lock (_gate) { _settings = settings; }
        Tick();
    }

    private void Tick()
    {
        try
        {
            DisplCtrlSettings settings;
            lock (_gate)
            {
                if (_disposed) return;
                settings = _settings;
            }

            NightLightSettings config = settings.Global.NightLight;
            bool warmNow = config.ActiveAt(DateTime.Now);

            // Warmth and software dimming share one ramp, so the decision to
            // write or clear has to consider both. Clearing on warmth alone
            // wiped a display's dimming every time night light went off.
            bool anyRamp = false;
            var wanted = new List<(DisplayInfo Display, int Warmth, int Dim)>();

            foreach (DisplayInfo d in DisplayRegistry.Enumerate())
            {
                int warmth = warmNow ? settings.NightLightStrengthFor(d.Token) : 0;
                int dim = settings.SoftwareBrightnessFor(d.Token);

                wanted.Add((d, warmth, dim));
                anyRamp |= NightLight.NeedsRamp(warmth, dim);
            }

            if (!anyRamp)
            {
                if (!_warm) return;

                NightLight.ClearAll();
                _warm = false;
                _appliedStrength = -1;
                Log.Write("gamma cleared");
                return;
            }

            // Re-applied every tick even when nothing changed, for the reasons
            // in the type remarks: otherwise the displays quietly go cold after
            // a resume and stay that way.
            // Resolved per display: unison, calibration and per-monitor
            // overrides all land in one place, in DisplCtrl.Core, so the engine and
            // the panel cannot disagree about what a slider position means.
            foreach ((DisplayInfo d, int warmth, int dim) in wanted)
                _ = NightLight.Apply(d, warmth, dim);

            int strength = warmNow ? Math.Clamp(config.Strength, 0, 100) : 0;
            if (!_warm || strength != _appliedStrength)
            {
                Log.Write($"gamma applied, warmth {strength}%"
                    + (config.Unison ? "" : " (per display)"));
            }

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
