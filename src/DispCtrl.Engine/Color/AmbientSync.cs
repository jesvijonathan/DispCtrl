using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;
using Windows.Devices.Sensors;

namespace DispCtrl.Engine.Color;

/// <summary>
/// Unison brightness following the room's light, from an ambient light sensor.
/// </summary>
/// <remarks>
/// Told, then looking: the sensor wakes this only when the light changes, and
/// a clock runs only while a change is being weighed or the desk is on its way
/// to the new level, so a still room costs nothing. <see cref="AmbientFilter"/>
/// decides when the light has really changed; the level then walks there in a
/// few steps rather than jumping.
/// <para>
/// A level set by anything else while following - the unison slider, a
/// hotkey, Windows' brightness keys through the bridge - is a correction: it
/// is kept, and learned into the curve for this light
/// (<see cref="AmbientCurve.Learn"/>), so the next time the room is like this
/// the desk goes where it was put.
/// </para>
/// </remarks>
internal sealed class AmbientSync : IDisposable
{
    private const int TickMs = 250;

    /// <summary>Steps a change is spread over: about a second, like Windows' own adaptive brightness.</summary>
    private const int RampSteps = 4;

    /// <summary>How long the Windows brightness bridge ignores the built-in panel after this writes it.</summary>
    private const int EchoMs = 1500;

    private static long _quietUntil;

    /// <summary>
    /// True while the built-in panel's brightness events are this service's own.
    /// </summary>
    /// <remarks>
    /// The level is saved once, when a walk ends, not at every step: a save per
    /// step wakes every engine service. So mid-walk the panel is somewhere the
    /// saved level does not put it, and the bridge would read each step as a
    /// brightness key, save it as the level, and this service would then take
    /// that for somebody's correction. The price is that a key pressed in the
    /// second a walk is writing is not followed; the walk is short.
    /// </remarks>
    public static bool Writing => Environment.TickCount64 < Volatile.Read(ref _quietUntil);

    private readonly Lock _gate = new();
    private readonly Timer _tick;
    private readonly AmbientFilter _filter = new();
    private LightSensor? _sensor;
    private string? _openFor;
    private double _lux = -1;
    private bool _running;
    private volatile bool _disposed;
    private int _busy;

    /// <summary>Where the walk is, and where it is going; -1 before the first reading.</summary>
    private int _level = -1, _target = -1;

    /// <summary>The unison level as this service last saved or last saw it.</summary>
    private int _known = -1;

    public AmbientSync(DispCtrlSettings settings)
    {
        _tick = new Timer(_ => Tick());
        Update(settings);
    }

    public void Update(DispCtrlSettings settings)
    {
        lock (_gate)
        {
            if (_disposed) return;
            bool wanted = settings.Global.UnisonBrightness && settings.Global.Ambient.Enabled;
            string id = settings.Global.Ambient.SensorId ?? "";
            if (!wanted) { Close(); return; }

            int saved = settings.Global.UnisonLevel;
            if (_sensor is not null && _openFor == id)
            {
                // Somebody else moved unison since this service last did.
                if (saved != _known) Corrected(settings, saved);
                else if (_filter.SettledLux >= 0) Retarget(settings);
                return;
            }

            Close();
            _openFor = id;
            _known = _level = _target = saved;
            _sensor = AmbientSensors.Open(id);
            if (_sensor is null) { Log.Write("ambient: no light sensor to follow"); return; }
            // Every reading the sensor will give, a few a second at most: the
            // filter, not the sensor, decides what counts. The old 15% threshold
            // meant a torch coming on, or a hand coming off, could arrive as one
            // reading followed by silence. Both thresholds must be met, so near
            // darkness it takes a whole lux to wake this.
            try { _sensor.ReportInterval = Math.Max(_sensor.MinimumReportInterval, TickMs); } catch (Exception) { }
            try { _sensor.ReportThreshold.LuxPercentage = 0.05f; } catch (Exception) { }
            try { _sensor.ReportThreshold.AbsoluteLux = 1f; } catch (Exception) { }
            _sensor.ReadingChanged += OnReading;
            Log.Write($"ambient: unison follows the room's light ({settings.Global.Ambient.Points.Count} learned level(s))");
            // Two hands on one control fight: Windows' adaptive brightness moves
            // the built-in panel from the same sensor. The app switches it off
            // too; this covers the command line and a hand-edited file.
            _ = Task.Run(() => { try { if (AdaptiveBrightness.Read().Enabled && AdaptiveBrightness.Write(false)) Log.Write("ambient: switched Windows' adaptive brightness off"); } catch (Exception) { } });
            if (_sensor.GetCurrentReading() is { } now) _lux = now.IlluminanceInLux;
            Run();
        }
    }

    private void OnReading(LightSensor sender, LightSensorReadingChangedEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed || _sensor is null) return;
            _lux = e.Reading.IlluminanceInLux;
            Run();
        }
    }

    /// <summary>Starts the clock if it is not already running. Under the gate.</summary>
    private void Run()
    {
        if (_running) return;
        _running = true;
        _tick.Change(0, TickMs);
    }

    private void Tick()
    {
        // Timer callbacks overlap when a DDC/CI write outlasts the period.
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;
        try
        {
            int step;
            lock (_gate)
            {
                if (_disposed || _sensor is null) { Stop(); return; }
                // The sensor's newest value, when it will give one: a driver
                // that reports late still answers when asked.
                try { if (_sensor.GetCurrentReading() is { } now) _lux = now.IlluminanceInLux; } catch (Exception) { }
                if (_lux < 0) { Stop(); return; }

                if (_filter.Observe(_lux, Environment.TickCount64))
                {
                    DispCtrlSettings settings = SettingsStore.Load();
                    if (!settings.Global.UnisonBrightness || !settings.Global.Ambient.Enabled) { Stop(); return; }
                    Retarget(settings);
                }

                if (_level == _target)
                {
                    if (!_filter.Busy) Stop();
                    return;
                }
                int gap = _target - _level;
                int stride = Math.Max(1, (int)Math.Ceiling(Math.Abs(gap) / (double)RampSteps));
                step = _level + Math.Sign(gap) * Math.Min(stride, Math.Abs(gap));
            }

            if (!Write(step)) return;
            lock (_gate)
            {
                _level = step;
                if (_level == _target) Save(_level);
            }
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    /// <summary>Where the curve puts the settled light. Under the gate.</summary>
    private void Retarget(DispCtrlSettings settings)
    {
        int target = AmbientCurve.Level(_filter.SettledLux, settings.Global.Ambient);
        if (target == _target) return;
        Log.Write($"ambient: {_filter.SettledLux:0} lx, unison {_level}% -> {target}%");
        _target = target;
        Run();
    }

    /// <summary>Moves every display to one step of the walk.</summary>
    private static bool Write(int level)
    {
        // Calibration drives the displays itself, and turned-off displays hold
        // the laptop's backlight down: neither is this service's to undo.
        if (UnisonCalibration.IsActive || Power.DisplaysOffBacklight.Busy) return false;
        using var operations = new Mutex(false, @"Local\DispCtrl.Control.Operations");
        bool held = false;
        try
        {
            try { held = operations.WaitOne(200); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) return false; // The next tick tries again.

            // The file, not a copy this service was handed: calibrated limits
            // or baselines may have changed since.
            DispCtrlSettings settings = SettingsStore.Load();
            if (!settings.Global.UnisonBrightness || !settings.Global.Ambient.Enabled) return false;
            int saved = settings.Global.UnisonLevel;
            settings.Global.UnisonLevel = level;
            Volatile.Write(ref _quietUntil, Environment.TickCount64 + EchoMs);
            // A display joining unison for the first time needs its baseline
            // kept - but not this step as the level, which would read as a
            // correction on the next reload.
            if (UnisonWriter.Apply(settings, DisplayRegistry.Enumerate()))
            {
                settings.Global.UnisonLevel = saved;
                SettingsStore.Save(settings);
            }
            Volatile.Write(ref _quietUntil, Environment.TickCount64 + EchoMs);
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"ambient: could not follow the light: {ex.Message}");
            return false;
        }
        finally
        {
            if (held) operations.ReleaseMutex();
        }
    }

    /// <summary>Saves where a walk ended. Under the gate.</summary>
    private void Save(int level)
    {
        try
        {
            DispCtrlSettings settings = SettingsStore.Load();
            if (!settings.Global.UnisonBrightness || !settings.Global.Ambient.Enabled) return;
            // Moved by hand while the walk ran: that wins, and the next reload says so.
            if (settings.Global.UnisonLevel != _known) return;
            _known = level;
            settings.Global.UnisonLevel = level;
            SettingsStore.Save(settings);
        }
        catch (Exception ex) { Log.Write($"ambient: could not save the level: {ex.Message}"); }
    }

    /// <summary>
    /// Keeps a level set by something else, and learns it for this light. Under the gate.
    /// </summary>
    private void Corrected(DispCtrlSettings settings, int level)
    {
        _known = _level = _target = level;
        double lux = _filter.SettledLux;
        if (lux < 0 || !settings.Global.Ambient.LearnCorrections) return;
        try
        {
            DispCtrlSettings latest = SettingsStore.Load();
            if (!AmbientCurve.Learn(latest.Global.Ambient, lux, level)) return;
            SettingsStore.Save(latest);
            Log.Write($"ambient: learned unison {level}% at {lux:0} lx ({latest.Global.Ambient.Points.Count} learned)");
        }
        catch (Exception ex) { Log.Write($"ambient: could not learn the correction: {ex.Message}"); }
    }

    private void Stop()
    {
        _running = false;
        _tick.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void Close()
    {
        if (_sensor is not null) _sensor.ReadingChanged -= OnReading;
        _sensor = null;
        _openFor = null;
        _lux = -1;
        _filter.Reset();
        Stop();
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_gate) Close();
        _tick.Dispose();
    }
}
