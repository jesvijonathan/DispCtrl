using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DispCtrl.Linux.Engine;
using DispCtrl.Linux.Hardware;
using DispCtrl.Linux.Ramps;
using DispCtrl.Linux.Settings;
using FluentAvalonia.UI.Controls;

namespace DispCtrl.Linux.Gui;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<MonitorRowViewModel> _rows = [];
    private readonly CommandSender _sender = new();
    private readonly DispatcherTimer _engineTimer;

    // Set once the night light controls hold what settings say. Before that,
    // every assignment raises the same events a person does, and was written
    // back - the gate the Windows app needs on every two-way control.
    private bool _nightLightReady;
    private int _scanGeneration;

    public MainWindow()
    {
        InitializeComponent();
        Rows.ItemsSource = _rows;

        NightLightSwitch.IsCheckedChanged += (_, _) => OnNightLightSwitched();
        StrengthSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty) OnStrengthChanged();
        };
        ScheduleSwitch.IsCheckedChanged += (_, _) => OnScheduleChanged();
        FromPicker.SelectedTimeChanged += (_, _) => OnScheduleChanged();
        ToPicker.SelectedTimeChanged += (_, _) => OnScheduleChanged();

        // Below these widths the controls go under their text, then the header
        // buttons drop their labels. Measured on the content, not the screen.
        SizeChanged += (_, e) => ApplyWidth(e.NewSize.Width);

        LoadNightLight();
        RefreshEngineLine();

        // Only while the window is open: the engine can start or stop under it.
        _engineTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => RefreshEngineLine());
        _engineTimer.Start();
        Closed += (_, _) => _engineTimer.Stop();

        _ = RescanAsync();
    }

    private const double NarrowWidth = 640;
    private const double CompactHeaderWidth = 460;

    private void ApplyWidth(double width)
    {
        Root.Classes.Set("narrow", width < NarrowWidth);
        bool labels = width >= CompactHeaderWidth;
        RestoreLabel.IsVisible = labels;
        RescanLabel.IsVisible = labels;
    }

    private void OnRescanClick(object? sender, RoutedEventArgs e) => _ = RescanAsync();

    private async void OnRestoreClick(object? sender, RoutedEventArgs e)
    {
        RestoreButton.IsEnabled = false;
        var reply = await CommandSender.RunAsync(["restore"]);
        RestoreButton.IsEnabled = true;
        LoadNightLight();
        SetNightLightStatus(reply.ExitCode == 0 ? null : reply.Stderr.Trim());
        _ = RescanAsync();
    }

    private void RefreshEngineLine()
    {
        _ = Task.Run(() => EngineClient.IsRunning()).ContinueWith(t =>
        {
            bool running = t.Result;
            EngineLine.Text = running ? "Engine running" : "Engine not running";
            EngineDot.Fill = (Avalonia.Media.IBrush?)this.FindResource(
                running ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush");
            EngineHint.IsVisible = !running;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void LoadNightLight()
    {
        _nightLightReady = false;
        var settings = SettingsStore.Load().NightLight;
        NightLightSwitch.IsChecked = settings.Enabled;
        StrengthSlider.Value = settings.Strength;
        ScheduleSwitch.IsChecked = settings.Scheduled;
        Schedule.TryParse(settings.From, out var from);
        Schedule.TryParse(settings.To, out var to);
        FromPicker.SelectedTime = from.ToTimeSpan();
        ToPicker.SelectedTime = to.ToTimeSpan();
        UpdateStrengthText();
        ScheduleTimes.IsEnabled = settings.Scheduled;

        bool x = GammaRamp.IsAvailable(out var reason);
        NightLightSwitch.IsEnabled = x || settings.Enabled;
        SetNightLightStatus(x ? null : $"Unavailable: {reason}.");
        _nightLightReady = true;
    }

    private void OnNightLightSwitched()
    {
        if (!_nightLightReady) return;
        Send(NightLightSwitch.IsChecked == true ? ["nightlight", "on"] : ["nightlight", "off"]);
    }

    private void OnStrengthChanged()
    {
        UpdateStrengthText();
        if (!_nightLightReady) return;
        int strength = (int)Math.Round(StrengthSlider.Value);
        // A strength is also "on", as on the command line; the switch follows.
        if (NightLightSwitch.IsChecked != true)
        {
            _nightLightReady = false;
            NightLightSwitch.IsChecked = true;
            _nightLightReady = true;
        }
        Send(["nightlight", strength.ToString(CultureInfo.InvariantCulture)]);
    }

    private void OnScheduleChanged()
    {
        ScheduleTimes.IsEnabled = ScheduleSwitch.IsChecked == true;
        if (!_nightLightReady) return;
        if (FromPicker.SelectedTime is not { } from || ToPicker.SelectedTime is not { } to) return;
        // Compared at minute resolution: a picker holds whole minutes, and
        // comparing seconds made the Windows app's TimePicker write back forever.
        var f = TimeOnly.FromTimeSpan(from);
        var t = TimeOnly.FromTimeSpan(to);
        if (f.Hour == t.Hour && f.Minute == t.Minute)
        {
            SetNightLightStatus("The schedule cannot start and end at the same time.");
            return;
        }
        Send(["nightlight", "--from", Schedule.Format(f), "--to", Schedule.Format(t),
              "--schedule", ScheduleSwitch.IsChecked == true ? "on" : "off"]);
    }

    private void Send(string[] args)
    {
        SetNightLightStatus(null);
        _sender.Post("nightlight", args, error =>
        {
            if (error is not null) LoadNightLight();
            SetNightLightStatus(error);
        });
    }

    private void SetNightLightStatus(string? text)
    {
        NightLightStatus.Text = text ?? "";
        NightLightStatus.IsVisible = !string.IsNullOrEmpty(text);
    }

    private void SetScanStatus(string? text)
    {
        ScanStatus.Text = text ?? "";
        ScanStatus.IsVisible = !string.IsNullOrEmpty(text);
    }

    private void UpdateStrengthText() =>
        StrengthText.Text = string.Create(CultureInfo.InvariantCulture, $"{Warmth.KelvinFor((int)Math.Round(StrengthSlider.Value)):0} K");

    /// <summary>Finds every writable target on this machine now. Off the UI
    /// thread: ddcutil takes seconds to detect and a few hundred milliseconds
    /// per read, and the window used to freeze for all of it.</summary>
    private async Task RescanAsync()
    {
        int generation = ++_scanGeneration;
        RescanButton.IsEnabled = false;
        SetScanStatus("Looking for displays…");
        _rows.Clear();

        var settings = SettingsStore.Load();
        var ramps = await Task.Run(GammaRamp.Outputs);
        if (generation != _scanGeneration) return;
        foreach (var output in ramps)
        {
            double dim = settings.Dim.TryGetValue(output.Name, out var d) ? d : 1;
            _rows.Add(new MonitorRowViewModel(
                title: $"Dimming - {output.Name}",
                subtitle: "Software, through the gamma ramp; the monitor's own brightness is unchanged",
                minimum: RampTarget.LowestDim * 100, maximum: 100, initialValue: dim * 100, unit: "%",
                _sender, key: $"dim:{output.Name}",
                command: v => ["dim", (v / 100).ToString("0.##", CultureInfo.InvariantCulture), "--output", output.Name],
                icon: FASymbol.WeatherSunnyLow));
        }

        foreach (var device in await Task.Run(Backlight.Enumerate))
        {
            if (generation != _scanGeneration) return;
            _rows.Add(new MonitorRowViewModel(
                title: "Built-in display",
                subtitle: $"Backlight brightness ({device.Name})",
                minimum: 0, maximum: 100, initialValue: Math.Round(device.Fraction * 100), unit: "%",
                _sender, key: $"backlight:{device.Name}",
                command: v => ["brightness", ((int)v).ToString(CultureInfo.InvariantCulture), "--backlight", device.Name],
                icon: FASymbol.WeatherSunnyHigh));
        }

        if (Ddcutil.IsAvailable)
        {
            SetScanStatus("Asking DDC/CI monitors (a few seconds)…");
            var monitors = await Task.Run(() => Ddcutil.Detect()
                .Select(m => (Monitor: m, Level: Ddcutil.GetVcp(m.DisplayNum, Ddcutil.VcpBrightness)))
                .ToList());
            if (generation != _scanGeneration) return;
            foreach (var (monitor, level) in monitors)
            {
                if (level is null) continue;
                _rows.Add(new MonitorRowViewModel(
                    title: monitor.Model ?? $"Display {monitor.DisplayNum}",
                    subtitle: "Brightness, set in the monitor itself over DDC/CI",
                    minimum: 0, maximum: level.Maximum ?? 100, initialValue: level.Current,
                    unit: (level.Maximum ?? 100) == 100 ? "%" : "",
                    _sender, key: $"ddc:{monitor.DisplayNum}",
                    command: v => ["brightness", ((int)v).ToString(CultureInfo.InvariantCulture), "--ddc", monitor.DisplayNum.ToString(CultureInfo.InvariantCulture)],
                    icon: FASymbol.WeatherSunnyHigh));
            }
            SetScanStatus(monitors.Count == 0
                ? "No monitor answered over DDC/CI. A built-in panel never does; for an external one, see dispctrl-linux doctor."
                : null);
        }
        else
        {
            SetScanStatus("ddcutil is not installed, so external monitors' own brightness cannot be changed.");
        }

        if (_rows.Count == 0)
        {
            _rows.Add(new MonitorRowViewModel(
                "Nothing to control", "No X display, backlight or DDC/CI monitor was found: run dispctrl-linux doctor.",
                0, 0, 0, "", _sender, "none", _ => [], FASymbol.Alert));
        }
        RescanButton.IsEnabled = true;
    }
}
