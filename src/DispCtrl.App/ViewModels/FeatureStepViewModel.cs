using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using DispCtrl.Core.Settings;

namespace DispCtrl.App.ViewModels;

/// <summary>One step of a custom feature, as the Features tab edits it: a kind and its fields.</summary>
/// <remarks>
/// The feature is still stored as the lines a person would write
/// (<see cref="CustomFeature"/>); this is a form over one line, so nobody has
/// to learn the step grammar to put a feature together. A line the form cannot
/// describe - a comment, or one written by hand that does not parse - is kept
/// as written rather than lost.
/// </remarks>
public sealed class FeatureStepViewModel : INotifyPropertyChanged
{
    public static IReadOnlyList<string> Kinds { get; } =
    [
        "Set a monitor control",
        "Run a DispCtrl command",
        "Open a program, file or link",
        "Run a script and wait",
        "Wait",
        "As written",
    ];

    private const int SetKind = 0, CommandKind = 1, RunKind = 2, ScriptKind = 3, WaitKind = 4, TextKind = 5;

    private readonly Action _changed;
    private int _kind;
    private string _monitor = "1", _control = "", _value = "", _target = "", _arguments = "", _text = "";
    private double _milliseconds = 500;
    private bool _raw;

    public FeatureStepViewModel(string line, Action changed)
    {
        _changed = changed;
        Read(line);
    }

    public FeatureStepViewModel(int kind, Action changed)
    {
        _changed = changed;
        _kind = kind;
    }

    public int Kind
    {
        get => _kind;
        set
        {
            // A ComboBox writes -1 back while its row is torn down.
            if (value < 0 || value == _kind) return;
            _kind = value;
            Raise(string.Empty);
            _changed();
        }
    }

    public string Monitor { get => _monitor; set => Set(ref _monitor, value); }
    public string Control { get => _control; set => Set(ref _control, value); }
    public string Value { get => _value; set => Set(ref _value, value); }
    public string Target { get => _target; set => Set(ref _target, value); }
    public string Arguments { get => _arguments; set => Set(ref _arguments, value); }
    public string Text { get => _text; set => Set(ref _text, value); }

    public double Milliseconds
    {
        get => _milliseconds;
        set
        {
            double ms = double.IsFinite(value) ? Math.Clamp(Math.Round(value), 0, 60000) : 500;
            if (ms == _milliseconds) return;
            _milliseconds = ms;
            Raise();
            _changed();
        }
    }

    public Visibility SetVisibility => Show(SetKind);
    public Visibility CommandVisibility => Show(CommandKind);
    public Visibility TargetVisibility => _kind is RunKind or ScriptKind ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WaitVisibility => Show(WaitKind);
    public Visibility TextVisibility => Show(TextKind);

    public string TargetPlaceholder => _kind == ScriptKind ? @"C:\Scripts\evening.ps1" : @"C:\Games\launcher.exe or https://...";

    /// <summary>The step as the line that is stored.</summary>
    public string Line => _kind switch
    {
        SetKind => "set " + FeatureStep.Join([Word(_monitor, "1"), Word(_control, "brightness"), Word(_value, "0")])
            + (_raw ? " raw" : ""),
        CommandKind => "dispctrl " + Strip(_text.Trim()),
        RunKind => ("run " + FeatureStep.Join([_target.Trim()]) + " " + _arguments.Trim()).TrimEnd(),
        ScriptKind => ("script " + FeatureStep.Join([_target.Trim()]) + " " + _arguments.Trim()).TrimEnd(),
        WaitKind => "wait " + ((int)_milliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => _text.Trim(),
    };

    private void Read(string line)
    {
        FeatureStep? step = null;
        try { step = FeatureStep.Parse(line); }
        catch (FormatException) { }
        switch (step?.Kind)
        {
            case FeatureStepKind.Set:
                _kind = SetKind; _monitor = step.Monitor; _control = step.Control; _value = step.Value; _raw = step.Raw;
                break;
            case FeatureStepKind.Command:
                _kind = CommandKind; _text = FeatureStep.Join(step.Words);
                break;
            case FeatureStepKind.Run or FeatureStepKind.Script:
                _kind = step.Kind == FeatureStepKind.Run ? RunKind : ScriptKind;
                _target = step.Words[0];
                _arguments = FeatureStep.Join(step.Words.Skip(1));
                break;
            case FeatureStepKind.Wait:
                _kind = WaitKind; _milliseconds = step.Milliseconds;
                break;
            default:
                _kind = TextKind; _text = line;
                break;
        }
    }

    private Visibility Show(int kind) => _kind == kind ? Visibility.Visible : Visibility.Collapsed;

    // A person pastes "dispctrl nightlight ..." into the command box as often as not.
    private static string Strip(string text) =>
        text.StartsWith("dispctrl ", StringComparison.OrdinalIgnoreCase) ? text[9..].TrimStart() : text;

    private static string Word(string text, string fallback) => text.Trim() is { Length: > 0 } t ? t : fallback;

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        value ??= "";
        if (field == value) return;
        field = value;
        Raise(name);
        _changed();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
