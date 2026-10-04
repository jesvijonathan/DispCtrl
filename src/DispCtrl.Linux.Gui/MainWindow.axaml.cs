using System.Collections.ObjectModel;
using Avalonia.Controls;
using DispCtrl.Linux;
using FluentAvalonia.UI.Controls;

namespace DispCtrl.Linux.Gui;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<MonitorRowViewModel> _rows = [];

    public MainWindow()
    {
        InitializeComponent();
        Rows.ItemsSource = _rows;
        Rescan();
    }

    private void OnRescanClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Rescan();

    /// <summary>Builds one row per real, writable target found on this
    /// machine right now - no fixtures, matching the "verify against monitors
    /// actually attached" rule inherited from the Windows side's
    /// presetcheck.</summary>
    private void Rescan()
    {
        _rows.Clear();

        if (Ddcutil.IsAvailable)
        {
            foreach (var monitor in Ddcutil.Detect())
            {
                const byte VcpBrightness = 0x10;
                var current = Ddcutil.GetVcp(monitor.DisplayNum, VcpBrightness);
                if (current is null) continue;

                _rows.Add(new MonitorRowViewModel(
                    title: $"{monitor.Model ?? $"Display {monitor.DisplayNum}"} (DDC/CI)",
                    subtitle: $"{monitor.I2CBus}  serial {monitor.Serial}",
                    maximum: current.Maximum ?? 100,
                    initialValue: current.Current,
                    apply: value => Ddcutil.SetVcp(monitor.DisplayNum, VcpBrightness, (int)value)
                        ? null
                        : "ddcutil setvcp refused",
                    icon: FASymbol.Settings));
            }
        }

        foreach (var device in Backlight.Enumerate())
        {
            _rows.Add(new MonitorRowViewModel(
                title: $"{device.Name} (backlight)",
                subtitle: $"sysfs 0-{device.Max}, currently {device.Current}",
                maximum: 100,
                initialValue: device.Fraction * 100,
                apply: value => Backlight.TrySet(device.Name, (int)Math.Round(device.Max * (value / 100.0)), out var error)
                    ? null
                    : error,
                icon: FASymbol.WeatherSunnyHigh));
        }

        if (XRandR.IsAvailable)
        {
            foreach (var output in XRandR.Query().Where(o => o.Connected))
            {
                _rows.Add(new MonitorRowViewModel(
                    title: $"{output.Name} (software dimming via XRandR)",
                    subtitle: $"{output.WidthPx}x{output.HeightPx}, gamma scalar - not a hardware level",
                    maximum: 100,
                    initialValue: 100,
                    apply: value => XRandR.SetSoftwareBrightness(output.Name, value / 100.0)
                        ? null
                        : "xrandr refused",
                    icon: FASymbol.View));
            }
        }

        if (_rows.Count == 0)
        {
            _rows.Add(new MonitorRowViewModel(
                title: "No controllable target found",
                subtitle: "Neither ddcutil, backlight nor xrandr produced anything - see dispctrl-linux doctor",
                maximum: 1,
                initialValue: 0,
                apply: _ => null,
                icon: FASymbol.Alert));
        }
    }
}
