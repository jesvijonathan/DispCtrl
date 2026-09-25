using System.Management;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

namespace DispCtrl.Engine.Color;

/// <summary>
/// Lets Windows' own brightness - the Quick Settings slider and the keyboard's
/// brightness keys - drive unison brightness.
/// </summary>
/// <remarks>
/// Those controls only ever move the built-in panel, and they do it through
/// WMI, which announces every change as a <c>WmiMonitorBrightnessEvent</c>.
/// This listens for that event and carries it to every other display: the
/// built-in panel's brightness is read back as a unison level through the
/// panel's own range (<see cref="UnisonResume.LevelFor"/>), and each external
/// display is set to where unison puts it at that level, the same arithmetic
/// the app's unison slider uses. A value past the end of the panel's range is
/// pulled back to the end.
/// <para>
/// It lives in the engine because the keys have to work with the app closed.
/// It listens rather than polls - WMI raises the event, and nothing runs
/// between presses.
/// </para>
/// <para>
/// The built-in panel is written only to pull it back inside its range, and
/// only after the level is saved, so the event that write raises matches
/// where unison has the panel and is dropped - as are the events the app's own
/// unison slider causes when it moves the panel.
/// </para>
/// </remarks>
internal sealed class WindowsBrightnessBridge : IDisposable
{
    /// <summary>
    /// The least time between two passes while the slider is moving.
    /// </summary>
    /// <remarks>
    /// A throttle, not a debounce. It was a debounce - wait until the events
    /// stop - so dragging the Windows slider left every other display still
    /// until the drag ended and then jumped them in one go, which is what made
    /// the link feel choppy. Now the first event acts at once and the rest are
    /// taken at this pace, the newest value each time, so a held key or a drag
    /// cannot queue DDC/CI traffic behind a slider already let go.
    /// </remarks>
    private const int ThrottleMs = 90;

    /// <summary>How long the slider has to be still before the built-in panel is put back in range.</summary>
    /// <remarks>
    /// Corrected mid-drag, the panel was pulled back to its floor under the
    /// pointer while Windows kept moving it down: the two fought, and it flickered.
    /// </remarks>
    private const int QuietMs = 400;

    private readonly Lock _gate = new();
    private readonly Timer _settle;
    private readonly Timer _quiet;
    private long _lastPass;
    private long _lastEvent;

    /// <summary>The value this bridge last wrote to the built-in panel, and when.</summary>
    /// <remarks>
    /// Its WMI echo must not stand in for a key press. It used to go into the
    /// same pending slot as everything else, and at the floor - where the
    /// correction writes - it landed just after the brightness-up key and
    /// replaced it: every press up from the bottom was lost.
    /// </remarks>
    private int _ownWrite = -1;
    private long _ownWriteAt;
    private readonly Dictionary<string, (uint Min, uint Max)> _ranges = [];

    private ManagementEventWatcher? _watcher;
    private int _pending = -1;
    private bool _disposed;
    private int _applying;

    public WindowsBrightnessBridge(DispCtrlSettings settings)
    {
        _settle = new Timer(_ => Apply());
        _quiet = new Timer(_ => HoldInRange());
        Update(settings);
    }

    private static bool Wanted(DispCtrlSettings settings) =>
        settings.Global.UnisonBrightness && settings.Global.UnisonFollowsWindows;

    /// <summary>Starts or stops listening to match the settings.</summary>
    public void Update(DispCtrlSettings settings)
    {
        lock (_gate)
        {
            if (_disposed) return;

            bool wanted = Wanted(settings);
            if (wanted && _watcher is null) Start();
            else if (!wanted && _watcher is not null) Stop();
        }
    }

    private void Start()
    {
        try
        {
            var watcher = new ManagementEventWatcher(
                new ManagementScope(@"root\WMI"),
                new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));

            watcher.EventArrived += (_, e) =>
            {
                try
                {
                    // Turned-off displays taking the laptop's backlight down, and
                    // back, are not somebody moving Windows' slider.
                    // Nor is following the room's light walking the panel.
                    if (_disposed || UnisonCalibration.IsActive || Power.DisplaysOffBacklight.Busy || AmbientSync.Writing) return;
                    int value = Convert.ToInt32(e.NewEvent["Brightness"]);
                    long at = Environment.TickCount64;
                    if (value == Volatile.Read(ref _ownWrite) && at - Volatile.Read(ref _ownWriteAt) < 1500)
                    {
                        Volatile.Write(ref _ownWrite, -1);
                        return;
                    }
                    Volatile.Write(ref _lastEvent, at);
                    Interlocked.Exchange(ref _pending, value);
                    long since = Environment.TickCount64 - Volatile.Read(ref _lastPass);
                    _settle.Change(Math.Max(0, ThrottleMs - since), Timeout.Infinite);
                    _quiet.Change(QuietMs, Timeout.Infinite);
                }
                catch (Exception ex)
                {
                    Log.Write($"windows brightness: unreadable event: {ex.Message}");
                }
            };

            watcher.Start();
            _watcher = watcher;
            Log.Write("windows brightness: following the Windows slider for unison");
        }
        catch (Exception ex)
        {
            // A desktop has no built-in panel and no such event to wait for.
            Log.Write($"windows brightness: cannot follow ({ex.Message})");
            _watcher = null;
        }
    }

    private void Stop()
    {
        Interlocked.Exchange(ref _pending, -1);
        _settle.Change(Timeout.Infinite, Timeout.Infinite);
        _quiet.Change(Timeout.Infinite, Timeout.Infinite);
        try { _watcher?.Stop(); _watcher?.Dispose(); }
        catch (Exception) { }
        _watcher = null;
        Log.Write("windows brightness: no longer following");
    }

    private void Apply()
    {
        if (_disposed || Interlocked.CompareExchange(ref _applying, 1, 0) != 0) return;
        Volatile.Write(ref _lastPass, Environment.TickCount64);
        using var operations = new Mutex(false, @"Local\DispCtrl.Control.Operations");
        bool held = false;
        try
        {
            try { held = operations.WaitOne(200); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) return; // Keep the pending value for the next timer tick.
            int value = Interlocked.Exchange(ref _pending, -1);
            if (value < 0 || UnisonCalibration.IsActive) return;
            // Read now rather than trusted from the last reload: the app may
            // have saved a new level a moment ago, and the watcher that tells
            // this process so has not caught up yet.
            DispCtrlSettings settings = SettingsStore.Load();
            if (!Wanted(settings)) return;

            var displays = DisplayRegistry.Enumerate();
            DisplayInfo? panel = displays.FirstOrDefault(d => d.IsInternal);
            bool calibrated = settings.Global.UnisonCalibrated;
            MonitorSettings reference = Reference(panel is null ? null : settings.For(panel.Token));

            // Where unison already has the panel: the app's slider, or this
            // bridge's own correction below, arriving back as an event.
            if (value == UnisonResume.Target(reference, calibrated, settings.Global.UnisonLevel)) return;

            int level = UnisonResume.LevelFor(reference, calibrated, value);
            int allowed = UnisonResume.Target(reference, calibrated, level);

            bool levelChanged = level != settings.Global.UnisonLevel;
            if (levelChanged)
            {
                settings.Global.UnisonLevel = level;
                SettingsStore.Save(settings);
            }

            if (!levelChanged)
            {
                // Said, not silent: a press that changes nothing is the one
                // worth seeing when the keys seem not to work.
                Log.Write($"windows brightness: {value}% is still unison {level}%; nothing to move");
                return;
            }

            int moved = 0;
            var connected = displays.Select(d => d.Token).ToHashSet(StringComparer.Ordinal);
            foreach (string token in _ranges.Keys.Where(t => !connected.Contains(t)).ToArray()) _ranges.Remove(token);
            foreach (DisplayInfo display in displays)
            {
                if (_disposed || UnisonCalibration.IsActive || Volatile.Read(ref _pending) >= 0) break;
                if (display.IsInternal) continue;
                if (MoveTo(display, settings.For(display.Token), settings.Global.UnisonCalibrated, level)) moved++;
            }

            Log.Write($"windows brightness: {value}% -> unison {level}%{(allowed != value ? $" (built-in returns to {allowed}% when the slider stops)" : "")}, {moved} display(s) followed");
        }
        catch (Exception ex)
        {
            Log.Write($"windows brightness: could not follow: {ex.Message}");
        }
        finally
        {
            if (held) operations.ReleaseMutex();
            Volatile.Write(ref _applying, 0);
            lock (_gate)
                if (!_disposed && Volatile.Read(ref _pending) >= 0) _settle.Change(ThrottleMs, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Puts the built-in panel back inside its unison range once the slider is still.
    /// </summary>
    /// <remarks>
    /// Windows can take the panel past either end - a key pressed at the floor,
    /// the slider dragged to the top. The level already reads as that end; this
    /// moves the panel to match. Its own WMI event then equals where unison has
    /// the panel and is dropped.
    /// </remarks>
    private void HoldInRange()
    {
        if (_disposed || UnisonCalibration.IsActive || Volatile.Read(ref _pending) >= 0) return;
        // Something moved it since this was scheduled: not quiet yet.
        long since = Environment.TickCount64 - Volatile.Read(ref _lastEvent);
        if (since < QuietMs) { _quiet.Change(QuietMs - since, Timeout.Infinite); return; }
        using var operations = new Mutex(false, @"Local\DispCtrl.Control.Operations");
        bool held = false;
        try
        {
            try { held = operations.WaitOne(500); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) return;

            DispCtrlSettings settings = SettingsStore.Load();
            if (!Wanted(settings)) return;
            List<DisplayInfo> displays = DisplayRegistry.Enumerate();
            DisplayInfo? panel = displays.FirstOrDefault(d => d.IsInternal);
            if (panel is null) return;
            // Alone, the range only fought the keys: the calibrated limits
            // exist to match the panel to the others, and there are none. The
            // level is still followed above, so a monitor plugged in later
            // arrives where the keys left things.
            if (displays.Count < 2) return;

            MonitorSettings reference = Reference(settings.For(panel.Token));
            bool calibrated = settings.Global.UnisonCalibrated;
            BrightnessRange range = Brightness.Read(panel);
            if (!range.Supported) return;

            // Only a panel outside its range is corrected. Compared with where
            // the saved level puts it instead, a key press already on the panel
            // but not yet read as a level looked out of place, and was undone.
            int now = range.Percent;
            int allowed = UnisonResume.Target(reference, calibrated, UnisonResume.LevelFor(reference, calibrated, now));
            if (now == allowed) return;
            Volatile.Write(ref _ownWriteAt, Environment.TickCount64);
            Volatile.Write(ref _ownWrite, allowed);
            if (Brightness.Write(panel, range.FromPercent(allowed)))
                Log.Write($"windows brightness: built-in back in range at {allowed}% (was {now}%)");
        }
        catch (Exception ex)
        {
            Log.Write($"windows brightness: could not hold the built-in panel in range: {ex.Message}");
        }
        finally
        {
            if (held) operations.ReleaseMutex();
        }
    }

    /// <summary>
    /// The built-in panel's unison settings, copied, with full scale standing
    /// in for a baseline it has not been given yet - never saved as one.
    /// </summary>
    private static MonitorSettings Reference(MonitorSettings? saved) => new()
    {
        BrightnessFloor = saved?.BrightnessFloor ?? -1,
        BrightnessCeiling = saved?.BrightnessCeiling ?? -1,
        BrightnessBaseline = saved is { BrightnessBaseline: > 0 } ? saved.BrightnessBaseline : 100,
    };

    /// <summary>Puts one display where unison says, as the app's slider would.</summary>
    private bool MoveTo(DisplayInfo display, MonitorSettings monitor, bool calibrated, int level)
    {
        int target;
        if (calibrated && monitor.HasBrightnessRange)
        {
            target = UnisonResume.Target(monitor, true, level);
        }
        else if (monitor.BrightnessBaseline > 0)
        {
            target = UnisonResume.Target(monitor.BrightnessBaseline, level);
        }
        else
        {
            // Never given a baseline, so unison has no say over it yet.
            return false;
        }

        if (!_ranges.TryGetValue(display.Token, out (uint Min, uint Max) range))
        {
            BrightnessRange read = Brightness.Read(display);
            if (!read.Supported) return false;
            range = (read.Min, read.Max);
            _ranges[display.Token] = range;
        }

        var scale = new BrightnessRange(range.Min, range.Min, range.Max, true);
        bool written = Brightness.Write(display, scale.FromPercent(Math.Clamp(target, 0, 100)));
        if (!written) _ranges.Remove(display.Token);
        return written;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_watcher is not null) Stop();
        }

        _settle.Dispose();
        _quiet.Dispose();
    }
}
