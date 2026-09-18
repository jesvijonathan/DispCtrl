using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using DisplCtrl.Core.Displays;
using DisplCtrl.Display;

namespace DisplCtrl.App.ViewModels;

/// <summary>
/// One control a monitor said it supports, bound to a widget.
/// </summary>
/// <remarks>
/// Built from what the panel reported, never from a fixed list, so DisplCtrl never
/// shows a control the monitor in front of you does not have — and shows the
/// ones it does have that nothing in Windows exposes.
/// </remarks>
public sealed class MonitorControlViewModel : INotifyPropertyChanged
{
    private readonly DisplayInfo _display;
    private readonly VcpControl _control;

    /// <summary>
    /// Reports that the desk changed, so the preset bar re-checks.
    /// </summary>
    /// <remarks>
    /// Without this, changing contrast or picture mode left the bar saying
    /// everything matched while the monitor plainly did not — these are
    /// hardware settings that never touch the settings file, so nothing else
    /// would have noticed.
    /// </remarks>
    private readonly Action _deskChanged;

    /// <summary>
    /// Coalesces a drag into one write.
    /// </summary>
    /// <remarks>
    /// The same reasoning as the brightness slider: a DDC/CI write is a slow
    /// round trip and a slider raises a change per pixel dragged. Writing each
    /// one queues requests the monitor answers long after the user let go, and
    /// on some panels enough of them in a row makes it stop answering at all.
    /// </remarks>
    private CancellationTokenSource? _pending;

    public MonitorControlViewModel(DisplayInfo display, VcpControl control, Action deskChanged)
    {
        _display = display;
        _control = control;
        _deskChanged = deskChanged;

        foreach (VcpValue v in control.Values) Options.Add(v.Name);
        _selected = control.CurrentOption?.Name;
    }

    public string Name => _control.Name;

    public string Code => _control.Hex;

    /// <summary>What this control is, for the line under its name.</summary>
    public string Description => _control.Kind switch
    {
        VcpKind.Continuous => $"{_control.Hex} · the monitor's own setting, {_control.Maximum} steps",
        VcpKind.Discrete => $"{_control.Hex} · the monitor's own setting",
        _ => _control.Hex,
    };

    public Visibility SliderVisibility =>
        _control.Kind == VcpKind.Continuous ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ChoiceVisibility =>
        _control.Kind == VcpKind.Discrete && Options.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ReadOnlyVisibility =>
        SliderVisibility == Visibility.Collapsed && ChoiceVisibility == Visibility.Collapsed
            ? Visibility.Visible : Visibility.Collapsed;

    public string ReadOnlyText => _control.Display;

    public double Maximum => _control.Maximum > 0 ? _control.Maximum : 100;

    public double Value
    {
        get => _control.Current < 0 ? 0 : _control.Current;
        set
        {
            int v = (int)Math.Clamp(value, 0, Maximum);
            if (_control.Current == v) return;

            _control.Current = v;
            Raise();
            Raise(nameof(ValueText));

            Queue(() => MonitorCapabilities.Write(_display, _control.Code, (uint)v));
            _deskChanged();
        }
    }

    public string ValueText => _control.Current < 0 ? "—" : $"{_control.Current}";

    public ObservableCollection<string> Options { get; } = [];

    private string? _selected;

    public string? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value || value is null) return;
            _selected = value;
            Raise();

            foreach (VcpValue v in _control.Values)
            {
                if (v.Name != value) continue;

                _control.Current = v.Value;
                Queue(() => MonitorCapabilities.Write(_display, _control.Code, v.Value));
                _deskChanged();
                break;
            }
        }
    }

    private void Queue(Func<bool> write)
    {
        _pending?.Cancel();
        _pending = new CancellationTokenSource();
        CancellationToken token = _pending.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(180, token).ConfigureAwait(false);
                if (!token.IsCancellationRequested) write();
            }
            catch (TaskCanceledException)
            {
                // Superseded by a later position in the same drag.
            }
        }, token);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
