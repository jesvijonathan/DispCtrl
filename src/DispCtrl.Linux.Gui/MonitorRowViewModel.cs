using System.ComponentModel;
using System.Runtime.CompilerServices;
using FluentAvalonia.UI.Controls;

namespace DispCtrl.Linux.Gui;

/// <summary>One row: a slider bound to a real write path (DDC/CI VCP 0x10,
/// a backlight sysfs node, or an XRandR software-brightness scalar). Kept as
/// one shape for all three, the same way DispCtrl's own quick panel treats
/// "a slider that writes somewhere" uniformly regardless of backend - see
/// .claude/CLAUDE.md, "Quick panel (the tray icon)".</summary>
public sealed class MonitorRowViewModel : INotifyPropertyChanged
{
    private readonly Func<double, string?> _apply;
    private double _value;
    private string _status = string.Empty;

    /// <summary>
    /// A constructor, not an object initializer with a public settable Value:
    /// the initial value comes from reading the hardware, and it must never be
    /// written straight back to it. Assigning through the property setter did
    /// exactly that on the first run - the Dell got a redundant "set brightness
    /// to 70" on every launch, and the backlight row only *looked* harmless
    /// because sysfs refused it for lack of permission, not because the write
    /// itself was safe. This is the same trap `.claude/CLAUDE.md` documents
    /// for WinUI's two-way sliders ("every such binding needs a `_xxxReady`
    /// gate") - found here by running the app and reading its own status line,
    /// not by inspection.
    /// </summary>
    public MonitorRowViewModel(string title, string subtitle, double maximum, double initialValue, Func<double, string?> apply, FASymbol icon)
    {
        Title = title;
        Subtitle = subtitle;
        Maximum = maximum;
        Icon = icon;
        _apply = apply;
        _value = initialValue;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public double Maximum { get; }
    public FASymbol Icon { get; }

    public double Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            OnPropertyChanged();
            Status = _apply(value) is { } error ? $"refused: {error}" : "applied";
        }
    }

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
