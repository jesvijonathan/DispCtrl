using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using FluentAvalonia.UI.Controls;

namespace DispCtrl.Linux.Gui;

/// <summary>One row: a slider that sends one command, whether it ends at a
/// DDC/CI monitor, a backlight or an output's ramp.</summary>
public sealed class MonitorRowViewModel : INotifyPropertyChanged
{
    private readonly CommandSender _sender;
    private readonly string _key;
    private readonly Func<double, string[]> _command;
    private double _value;
    private string _status = string.Empty;

    /// <summary>
    /// The starting value comes through the constructor, never the setter: it
    /// is read from the hardware, and assigning it through the setter wrote it
    /// straight back - the Dell got a redundant brightness write on every
    /// launch. The same trap as WinUI's two-way sliders on Windows.
    /// </summary>
    public MonitorRowViewModel(
        string title, string subtitle, double minimum, double maximum, double initialValue, string unit,
        CommandSender sender, string key, Func<double, string[]> command, FASymbol icon)
    {
        Title = title;
        Subtitle = subtitle;
        Minimum = minimum;
        Maximum = maximum;
        Unit = unit;
        Icon = icon;
        _sender = sender;
        _key = key;
        _command = command;
        _value = initialValue;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public string Unit { get; }
    public FASymbol Icon { get; }

    /// <summary>False for a row that only explains why nothing is there.</summary>
    public bool IsInteractive => Maximum > Minimum;

    public string ValueText => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(_value)}{Unit}");

    public double Value
    {
        get => _value;
        set
        {
            if (Math.Round(_value) == Math.Round(value)) return;
            _value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ValueText));
            _sender.Post(_key, _command(Math.Round(value)), error => Status = error ?? "");
        }
    }

    /// <summary>Why the last change was refused; empty when it took.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => _status.Length > 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
