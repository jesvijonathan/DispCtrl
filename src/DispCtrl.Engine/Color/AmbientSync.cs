using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;
using Windows.Devices.Sensors;

namespace DispCtrl.Engine.Color;

/// <summary>
/// Unison brightness following the room's light, from an ambient light sensor.
/// </summary>
/// <remarks>
/// Told, never polling: the sensor reports only a change of about a sixth or
/// more, at most once a second, so a still room costs nothing. Readings are
/// smoothed, and the level applied a moment after they settle and only when it
/// moves by three points or more - a cloud passing, or a hand near the bezel,
/// must not make every screen breathe.
/// </remarks>
internal sealed class AmbientSync : IDisposable
{
    private const int SettleMs = 1500, Hysteresis = 3;

    private readonly Lock _gate = new();
    private readonly Timer _settle;
    private LightSensor? _sensor;
    private string? _openFor;
    private double _lux = -1;
    private volatile bool _disposed;

    public AmbientSync(DispCtrlSettings settings)
    {
        _settle = new Timer(_ => Apply());
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
            if (_sensor is not null && _openFor == id) return;

            Close();
            _openFor = id;
            _sensor = AmbientSensors.Open(id);
            if (_sensor is null) { Log.Write("ambient: no light sensor to follow"); return; }
            try { _sensor.ReportInterval = Math.Max(_sensor.MinimumReportInterval, 1000u); } catch (Exception) { }
            try { _sensor.ReportThreshold.LuxPercentage = 0.15f; } catch (Exception) { }
            _sensor.ReadingChanged += OnReading;
            Log.Write("ambient: unison follows the room's light");
            if (_sensor.GetCurrentReading() is { } now) Take(now.IlluminanceInLux);
        }
    }

    private void OnReading(LightSensor sender, LightSensorReadingChangedEventArgs e) => Take(e.Reading.IlluminanceInLux);

    private void Take(double lux)
    {
        lock (_gate) _lux = _lux < 0 ? lux : _lux * 0.6 + lux * 0.4;
        _settle.Change(SettleMs, Timeout.Infinite);
    }

    private void Apply()
    {
        double lux;
        lock (_gate)
        {
            if (_disposed || _sensor is null || _lux < 0) return;
            lux = _lux;
        }
        try
        {
            // The file, not the copy this service was handed: the slider may
            // have moved since, and a save from here must not undo it.
            DispCtrlSettings settings = SettingsStore.Load();
            if (!settings.Global.UnisonBrightness || !settings.Global.Ambient.Enabled) return;
            int level = AmbientCurve.Level(lux, settings.Global.Ambient);
            if (Math.Abs(level - settings.Global.UnisonLevel) < Hysteresis) return;
            settings.Global.UnisonLevel = level;
            UnisonWriter.Apply(settings, DisplayRegistry.Enumerate());
            SettingsStore.Save(settings);
            Log.Write($"ambient: {lux:0} lx, unison {level}%");
        }
        catch (Exception ex) { Log.Write($"ambient: could not follow the light: {ex.Message}"); }
    }

    private void Close()
    {
        if (_sensor is not null) _sensor.ReadingChanged -= OnReading;
        _sensor = null;
        _openFor = null;
        _lux = -1;
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_gate) Close();
        _settle.Dispose();
    }
}
