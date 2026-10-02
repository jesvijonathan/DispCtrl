using System.ComponentModel;
using Microsoft.UI.Xaml;

namespace DispCtrl.App.ViewModels;

/// <summary>A section's advanced options, folded away until asked for.</summary>
/// <remarks>
/// Each feature leads with the few settings most people change, and the rest
/// sit behind one "Advanced options" row. A fold is not a nested expander: a
/// SettingsExpander inside another's items takes the process down when it is
/// realised, so the advanced cards stay ordinary items whose Visibility
/// follows <see cref="Visibility"/> - the pattern the light sensor's options
/// already use. Opened by a ToggleButton bound to <see cref="Open"/>, not by
/// making the card clickable: a clickable SettingsCard exposed no usable invoke
/// to UI Automation, so a screen reader could not be relied on to open it.
/// Folded again on every start: what was open last time is not a setting
/// anyone made.
/// </remarks>
public sealed class Fold : INotifyPropertyChanged
{
    private bool _open;
    private readonly Action? _changed;

    public Fold(Action? changed = null) => _changed = changed;

    public bool Open
    {
        get => _open;
        set
        {
            if (_open == value) return;
            _open = value;
            foreach (string name in new[] { nameof(Open), nameof(Visibility), nameof(Glyph), nameof(Label) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            _changed?.Invoke();
        }
    }

    public Visibility Visibility => _open ? Visibility.Visible : Visibility.Collapsed;
    public string Glyph => _open ? "\uE70E" : "\uE70D";
    public string Label => _open ? "Fewer options" : "Advanced options";

    public event PropertyChangedEventHandler? PropertyChanged;
}
