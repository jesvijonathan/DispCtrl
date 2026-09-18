using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Umbra.Core.Presets;
using Umbra.Core.Settings;

namespace Umbra.App.ViewModels;

/// <summary>One shortcut, as the Hotkeys page edits it.</summary>
public sealed class HotkeyViewModel(Hotkey hotkey, Action persist, Func<IReadOnlyList<string>> presets)
    : INotifyPropertyChanged
{
    public Hotkey Hotkey { get; } = hotkey;

    /// <summary>
    /// The actions, in the order they are worth reaching for.
    /// </summary>
    /// <remarks>
    /// Names written out rather than taken from the enum: "Brightness up" reads
    /// better than "BrightnessUp", and the enum's job is storage, not wording.
    /// </remarks>
    public string[] ActionNames => AllActionNames;

    private static readonly string[] AllActionNames =
    [
        "Brightness up",
        "Brightness down",
        "Unison brightness up",
        "Unison brightness down",
        "Night light on or off",
        "Night light warmer",
        "Night light cooler",
        "Apply a preset",
        "Next input source",
    ];

    private static readonly HotkeyAction[] Actions =
    [
        HotkeyAction.BrightnessUp,
        HotkeyAction.BrightnessDown,
        HotkeyAction.UnisonUp,
        HotkeyAction.UnisonDown,
        HotkeyAction.NightLightToggle,
        HotkeyAction.NightLightWarmer,
        HotkeyAction.NightLightCooler,
        HotkeyAction.ApplyPreset,
        HotkeyAction.NextInput,
    ];

    /// <summary>
    /// The action, as an index into <see cref="ActionNames"/>.
    /// </summary>
    /// <remarks>
    /// An index rather than the string, because a ComboBox applies SelectedItem
    /// before its ItemsSource has been filled and then finds nothing matching,
    /// leaving the control blank. SelectedIndex has no such ordering problem.
    /// </remarks>
    public int SelectedActionIndex
    {
        get
        {
            int at = Array.IndexOf(Actions, Hotkey.Action);
            return at < 0 ? 0 : at;
        }
        set
        {
            if (value < 0 || value >= Actions.Length || Actions[value] == Hotkey.Action) return;

            Hotkey.Action = Actions[value];
            persist();
            Raise();
            RaiseAll();
        }
    }

    /// <summary>Preset names, plus a blank so one can be cleared.</summary>
    public IReadOnlyList<string> Presets => presets();

    public string? SelectedPreset
    {
        get => Hotkey.Preset;
        set
        {
            if (Hotkey.Preset == value) return;
            Hotkey.Preset = value;
            persist();
            Raise();
            RaiseAll();
        }
    }

    public Visibility PresetVisibility =>
        Hotkey.Action == HotkeyAction.ApplyPreset ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Which display, as "All" or a number.</summary>
    /// <remarks>
    /// Hidden for the actions that are not per display — a display picker beside
    /// "night light on or off" would imply something the setting cannot do.
    /// </remarks>
    public Visibility DisplayVisibility => Hotkey.Action is
        HotkeyAction.BrightnessUp or HotkeyAction.BrightnessDown or HotkeyAction.NextInput
        ? Visibility.Visible : Visibility.Collapsed;

    public double DisplayNumber
    {
        get => Hotkey.Display;
        set
        {
            int v = (int)value;
            if (Hotkey.Display == v) return;

            Hotkey.Display = v;
            persist();
            Raise();
            RaiseAll();
        }
    }

    public double Step
    {
        get => Hotkey.Step;
        set
        {
            int v = Math.Clamp((int)value, 1, 50);
            if (Hotkey.Step == v) return;

            Hotkey.Step = v;
            persist();
            Raise();
            RaiseAll();
        }
    }

    public Visibility StepVisibility =>
        Hotkey.Action is HotkeyAction.NightLightToggle or HotkeyAction.ApplyPreset or HotkeyAction.NextInput
        ? Visibility.Collapsed : Visibility.Visible;

    public bool Enabled
    {
        get => Hotkey.Enabled;
        set { Hotkey.Enabled = value; persist(); Raise(); }
    }

    public string Shortcut => Hotkey.Describe();

    public string Summary => Hotkey.IsComplete
        ? Hotkey.DescribeAction()
        : Hotkey.Key == 0 ? "Press Set and then the keys you want."
        : "Pick a preset for this shortcut.";

    /// <summary>Records a captured key press.</summary>
    public void Capture(uint key, uint modifiers)
    {
        Hotkey.Key = key;
        Hotkey.Modifiers = modifiers;
        persist();
        RaiseAll();
    }

    public void RaiseAll()
    {
        Raise(nameof(Shortcut));
        Raise(nameof(Summary));
        Raise(nameof(PresetVisibility));
        Raise(nameof(DisplayVisibility));
        Raise(nameof(StepVisibility));
        Raise(nameof(Presets));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// The Hotkeys page.
/// </summary>
/// <remarks>
/// The bindings live in settings and are registered by the engine, so this page
/// only edits the list. Nothing here touches the keyboard: a panel that
/// registered shortcuts itself would lose them the moment it closed, which is
/// the opposite of what a global shortcut is for.
/// </remarks>
public sealed class HotkeysViewModel : INotifyPropertyChanged
{
    private readonly Func<UmbraSettings> _settings;
    private readonly Action _persist;

    public HotkeysViewModel(Func<UmbraSettings> settings, Action persist)
    {
        _settings = settings;
        _persist = persist;
        Reload();
    }

    public ObservableCollection<HotkeyViewModel> Items { get; } = [];

    public void Reload()
    {
        Items.Clear();
        foreach (Hotkey h in _settings().Hotkeys)
            Items.Add(new HotkeyViewModel(h, _persist, PresetNames));

        Raise(nameof(EmptyVisibility));
    }

    private static IReadOnlyList<string> PresetNames()
    {
        var names = new List<string>();
        foreach (Preset p in PresetStore.Load()) names.Add(p.Name);

        return names;
    }

    public Visibility EmptyVisibility => Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public void Add()
    {
        // Ctrl+Alt is the default because it is the combination least likely to
        // be taken: Win is Windows', and Ctrl+Shift belongs to applications.
        var hotkey = new Hotkey { Modifiers = 1 | 2, Action = HotkeyAction.BrightnessUp };

        _settings().Hotkeys.Add(hotkey);
        _persist();

        Items.Add(new HotkeyViewModel(hotkey, _persist, PresetNames));
        Raise(nameof(EmptyVisibility));
    }

    public void Remove(HotkeyViewModel item)
    {
        _settings().Hotkeys.Remove(item.Hotkey);
        _persist();

        Items.Remove(item);
        Raise(nameof(EmptyVisibility));
    }

    /// <summary>
    /// Whether the engine is running, since nothing works if it is not.
    /// </summary>
    public string EngineNote { get; private set; } = "";

    public void SetEngineRunning(bool running)
    {
        string note = running
            ? "The engine registers these, and is running."
            : "The engine registers these and is not running, so none of them will work. Start it on the Engine page.";

        if (EngineNote == note) return;

        EngineNote = note;
        Raise(nameof(EngineNote));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
