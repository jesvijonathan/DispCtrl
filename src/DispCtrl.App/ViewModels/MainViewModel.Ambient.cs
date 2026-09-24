using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using DispCtrl.Control;
using DispCtrl.Core.Settings;
using DispCtrl.Display;
using Microsoft.UI.Xaml;

namespace DispCtrl.App.ViewModels;

public sealed partial class MainViewModel
{
    private AmbientSettings Ambient => _settings.Global.Ambient;
    private IReadOnlyList<AmbientSensor> _sensors = [];

    /// <summary>The sensors found, by name, for the choice on the page.</summary>
    public ObservableCollection<string> AmbientSensorNames { get; } = [];

    /// <summary>Looks for light sensors once, off the UI thread; enumerating devices is slow.</summary>
    public async Task LoadAmbientSensorsAsync()
    {
        IReadOnlyList<AmbientSensor> found = await Task.Run(AmbientSensors.List);
        _sensors = found;
        AmbientSensorNames.Clear();
        foreach (AmbientSensor s in found) AmbientSensorNames.Add(s.Name);
        Raise(nameof(AmbientSensorIndex));
        Raise(nameof(AmbientAvailable));
        Raise(nameof(AmbientStatus));
        _ = RefreshAmbientReadingAsync();
    }

    public bool AmbientAvailable => _sensors.Count > 0;

    public bool AmbientEnabled
    {
        get => Ambient.Enabled;
        set
        {
            if (Ambient.Enabled == value) return;
            Ambient.Enabled = value;
            // Windows' own adaptive brightness steers the same panel from the
            // same sensor; two hands on one control fight, so it steps aside.
            if (value) _ = Task.Run(() => { if (AdaptiveBrightness.Read().Enabled) AdaptiveBrightness.Write(false); });
            Persist();
            if (value) _engine.Start();
            Raise();
            Raise(nameof(AmbientStatus));
        }
    }

    public int AmbientSensorIndex
    {
        get
        {
            if (_sensors.Count == 0) return -1;
            int i = _sensors.ToList().FindIndex(s => s.Id == Ambient.SensorId);
            return i < 0 ? 0 : i;
        }
        set
        {
            if (value < 0 || value >= _sensors.Count) return;
            string id = value == 0 && string.IsNullOrEmpty(Ambient.SensorId) ? "" : _sensors[value].Id;
            if (Ambient.SensorId == id) return;
            Ambient.SensorId = id;
            Persist();
            Raise();
            _ = RefreshAmbientReadingAsync();
        }
    }

    public double AmbientDark { get => Ambient.DarkLevel; set { int v = Number(value, 0, 100); if (Ambient.DarkLevel == v) return; Ambient.DarkLevel = v; PersistSoon(); Raise(); } }
    public double AmbientBright { get => Ambient.BrightLevel; set { int v = Number(value, 0, 100); if (Ambient.BrightLevel == v) return; Ambient.BrightLevel = v; PersistSoon(); Raise(); } }

    /// <summary>Learn a level set by hand while following, for that light.</summary>
    public bool AmbientLearn
    {
        get => Ambient.LearnCorrections;
        set { if (Ambient.LearnCorrections == value) return; Ambient.LearnCorrections = value; Persist(); Raise(); }
    }

    private bool _ambientOptionsOpen;

    /// <summary>The light sensor's options, folded under its switch; not saved, as a section's fold is not.</summary>
    public bool AmbientOptionsOpen
    {
        get => _ambientOptionsOpen;
        set
        {
            if (_ambientOptionsOpen == value) return;
            _ambientOptionsOpen = value;
            Raise();
            Raise(nameof(AmbientOptionsVisibility));
            Raise(nameof(AmbientOptionsGlyph));
            if (value) _ = RefreshAmbientReadingAsync();
        }
    }

    public Visibility AmbientOptionsVisibility => _ambientOptionsOpen ? Visibility.Visible : Visibility.Collapsed;
    public string AmbientOptionsGlyph => _ambientOptionsOpen ? "\uE70E" : "\uE70D";

    /// <summary>Where the two ends sit and what has been learned, in a sentence.</summary>
    public string AmbientCalibrationSummary =>
        $"Dark at {Ambient.DarkLux} lx or less, bright at {Ambient.BrightLux} lx or more"
        + (_lux is { } lux ? $"; the sensor reads {lux:0} lx now" : "")
        + (Ambient.Points.Count == 0 ? ". Nothing learned yet."
            : $". {Ambient.Points.Count} level(s) learned from your own adjustments.");

    /// <summary>Makes what the sensor reads now the dark or the bright end.</summary>
    /// <returns>Null when done; otherwise why not, to show.</returns>
    public Task<string?> CaptureAmbientAsync(bool dark) =>
        AmbientRequestAsync("ambient.capture", new JsonObject { ["as"] = dark ? "dark" : "bright" });

    /// <summary>Drops every learned level, back to the two ends.</summary>
    public Task<string?> ForgetAmbientAsync() => AmbientRequestAsync("ambient.forget", new JsonObject());

    private async Task<string?> AmbientRequestAsync(string command, JsonObject args)
    {
        FlushPendingSave();
        JsonObject result = await Task.Run(() => new ControlService().Execute(new JsonObject
        {
            ["version"] = 1, ["command"] = command, ["args"] = args,
        }));
        ReloadFromDisk();
        await RefreshAmbientReadingAsync();
        return result["ok"]?.GetValue<bool>() == true ? null
            : result["error"]?["message"]?.GetValue<string>() ?? "The sensor did not answer.";
    }

    private double? _lux;

    private async Task RefreshAmbientReadingAsync()
    {
        string id = Ambient.SensorId;
        _lux = await Task.Run(() => AmbientSensors.ReadLux(id));
        Raise(nameof(AmbientStatus));
        Raise(nameof(AmbientCalibrationSummary));
    }

    /// <summary>Which sensor is followed and what it sees, or why there is nothing to follow.</summary>
    public string AmbientStatus => _sensors.Count == 0
        ? "No ambient light sensor on this PC. Laptops usually have one beside the camera; most monitors do not share theirs."
        : $"Following {_sensors[Math.Max(0, AmbientSensorIndex)].Name}{(_lux is { } lux ? $", which reads {lux:0} lux now" : "")}. "
          + "Windows' own adaptive brightness is switched off while this is on.";
}
