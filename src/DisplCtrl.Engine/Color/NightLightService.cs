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
    private readonly Lock _reconcileGate = new();
    private readonly Timer _timer;
    private readonly List<RegistryValueWatcher> _watchers = [];

    private DisplCtrlSettings _settings;
    private bool _warm;
    private int _appliedStrength = -1;
    private bool _disposed;

    /// <summary>
    /// What each side was last seen holding, so a tick can tell which one moved.
    /// </summary>
    /// <remarks>
    /// Without this the two would chase each other: adopting Windows' state
    /// writes the settings file, the watcher reloads it, the reload looks like a
    /// local change, and that pushes straight back to Windows. Comparing against
    /// what was last seen rather than against each other is what makes a tick
    /// able to say "nobody moved" and do nothing at all.
    /// </remarks>
    private WindowsNightLightState? _lastWindows;
    private (bool Enabled, int Strength)? _lastLocal;

    public NightLightService(DisplCtrlSettings settings)
    {
        _settings = settings;

        RecoverFromAbandonedRamps();
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, Interval);

        // The tick re-asserts the ramp; this is what makes Windows' own toggle
        // felt here at once rather than at the next one.
        foreach (string path in WindowsNightLight.WatchPaths)
            _watchers.Add(new RegistryValueWatcher(path, Tick));
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

    /// <summary>
    /// Brings this toggle and Windows' own night light back into agreement.
    /// </summary>
    /// <remarks>
    /// Whichever side moved since the last tick is the one that wins, which is
    /// the only rule that makes both the panel and the Quick Settings flyout
    /// feel like they are driving the same switch. When neither moved, nothing
    /// is written at all — Windows' store is a roaming one and a write costs a
    /// sync, so re-asserting a value every twenty seconds would be rude.
    /// </remarks>
    /// <returns>True when Windows is the one applying the warmth.</returns>
    private bool Reconcile(DisplCtrlSettings settings)
    {
        // Serialised against itself, because a tick now arrives from three
        // places: the timer and a watcher on each of the two keys. Two running
        // at once would both read the state before either recorded it, so a
        // push would be read back by the other as somebody else's change and
        // written to the settings file a second time.
        lock (_reconcileGate)
        {
            return ReconcileCore(settings);
        }
    }

    private bool ReconcileCore(DisplCtrlSettings settings)
    {
        NightLightSettings config = settings.Global.NightLight;

        if (!config.FollowWindows)
        {
            _lastWindows = null;
            _lastLocal = null;
            return false;
        }

        WindowsNightLightState? read = WindowsNightLight.Read();
        if (read is null)
        {
            // A Windows build that has moved this data leaves the feature
            // looking absent. Owning the ramp again is the safe answer: the
            // alternative is a night light that silently stops working.
            return false;
        }

        WindowsNightLightState windows = read.Value;
        (bool Enabled, int Strength) local = (config.Enabled, Math.Clamp(config.Strength, 0, 100));

        bool windowsMoved = _lastWindows is not null && _lastWindows.Value != windows;
        bool localMoved = _lastLocal is not null && _lastLocal.Value != local;

        if (windowsMoved)
        {
            config.Enabled = windows.Enabled;
            config.Strength = windows.Strength;
            SettingsStore.Save(settings);

            local = (windows.Enabled, windows.Strength);
            Log.Write($"night light followed Windows: {(windows.Enabled ? "on" : "off")} at {windows.Strength}%");
        }
        else if (localMoved || _lastLocal is null)
        {
            // The first tick after a start counts as a local change, so the
            // settings file is what a fresh engine imposes rather than silently
            // inheriting whatever Windows happened to be left on.
            bool wrote = false;
            if (windows.Enabled != local.Enabled) wrote |= WindowsNightLight.SetEnabled(local.Enabled);
            if (windows.Strength != local.Strength) wrote |= WindowsNightLight.SetStrength(local.Strength);

            if (wrote)
            {
                windows = WindowsNightLight.Read() ?? windows;
                Log.Write($"night light pushed to Windows: {(local.Enabled ? "on" : "off")} at {local.Strength}%");
            }
        }

        _lastWindows = windows;
        _lastLocal = local;
        return true;
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

            // Before anything is applied: if this toggle and Windows' own are
            // meant to be one setting, settle which of them moved since the last
            // tick and bring the other across.
            bool delegated = Reconcile(settings);

            bool warmNow = config.ActiveAt(DateTime.Now);

            // Warmth and software dimming share one ramp, so the decision to
            // write or clear has to consider both. Clearing on warmth alone
            // wiped a display's dimming every time night light went off.
            bool anyRamp = false;
            var wanted = new List<(DisplayInfo Display, int Warmth, int Dim)>();

            foreach (DisplayInfo d in DisplayRegistry.Enumerate())
            {
                // Delegated means Windows is warming these panels, so writing a
                // warm ramp here would stack on top of its own. Dimming still
                // belongs to this process either way.
                int warmth = warmNow && !delegated ? settings.NightLightStrengthFor(d.Token) : 0;
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
        foreach (RegistryValueWatcher watcher in _watchers) watcher.Dispose();

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
