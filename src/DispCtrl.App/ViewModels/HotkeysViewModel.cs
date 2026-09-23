using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using DispCtrl.Core.Presets;
using DispCtrl.Core.Settings;

namespace DispCtrl.App.ViewModels;

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

    private static readonly (HotkeyAction Action, string Name)[] AvailableActions = new (HotkeyAction, string)[]
    {
        (HotkeyAction.UnisonUp, "Unison brightness up"),
        (HotkeyAction.UnisonDown, "Unison brightness down"),
        (HotkeyAction.UnisonToggle, "Unison brightness on or off"),
        (HotkeyAction.BrightnessUp, "Brightness up, one display"),
        (HotkeyAction.BrightnessDown, "Brightness down, one display"),
        (HotkeyAction.NightLightToggle, "Night light on or off"),
        (HotkeyAction.NightLightWarmer, "Night light warmer"),
        (HotkeyAction.NightLightCooler, "Night light cooler"),
        (HotkeyAction.FocusToggle, "Focus mode on or off"),
        (HotkeyAction.OledCareToggle, "OLED care on or off"),
        (HotkeyAction.OledRestNow, "Rest the OLED displays now"),
        (HotkeyAction.KeepAwakeToggle, "Keep awake on or off"),
        (HotkeyAction.DarkModeToggle, "Dark or light mode"),
        (HotkeyAction.TaskbarToggle, "Hide or show the taskbar"),
        (HotkeyAction.TaskbarGlassToggle, "Taskbar glass on or off"),
        (HotkeyAction.ContrastUp, "Contrast up"),
        (HotkeyAction.ContrastDown, "Contrast down"),
        (HotkeyAction.NextInput, "Next input source"),
        (HotkeyAction.Identify, "Show the display numbers"),
        (HotkeyAction.QuickPanel, "Open or close the quick panel"),
        (HotkeyAction.ApplyPreset, "Apply a preset (Beta)"),
    }.Where(item => DispCtrl.Core.FeatureFlags.Presets || item.Item1 != HotkeyAction.ApplyPreset).ToArray();

    private static readonly string[] AllActionNames = AvailableActions.Select(item => item.Name).ToArray();
    private static readonly HotkeyAction[] Actions = AvailableActions.Select(item => item.Action).ToArray();

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
        or HotkeyAction.ContrastUp or HotkeyAction.ContrastDown
        ? Visibility.Visible : Visibility.Collapsed;

    public double DisplayNumber
    {
        get => Hotkey.Display;
        set
        {
            int v = double.IsNaN(value) ? 0 : (int)value;
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
            int v = double.IsNaN(value) ? Hotkey.Step : Math.Clamp((int)value, 1, 50);
            if (Hotkey.Step == v) return;

            Hotkey.Step = v;
            persist();
            Raise();
            RaiseAll();
        }
    }

    /// <summary>Only for the actions that move something by an amount.</summary>
    public Visibility StepVisibility => Hotkey.Action is
        HotkeyAction.UnisonUp or HotkeyAction.UnisonDown or HotkeyAction.BrightnessUp or HotkeyAction.BrightnessDown
        or HotkeyAction.NightLightWarmer or HotkeyAction.NightLightCooler or HotkeyAction.ContrastUp or HotkeyAction.ContrastDown
        ? Visibility.Visible : Visibility.Collapsed;

    public bool Enabled
    {
        get => Hotkey.Enabled;
        set
        {
            // A ToggleSwitch writes its value back as it is realised; only a
            // real change is saved.
            if (Hotkey.Enabled == value) return;
            Hotkey.Enabled = value;
            persist();
            Raise();
            Changed?.Invoke();
        }
    }

    public string Shortcut => Hotkey.Describe();

    /// <summary>The combination as keycaps: Ctrl, Alt, Page Up.</summary>
    public string[] KeyParts => IsCapturing ? ["Press the keys..."]
        : Hotkey.Key == 0 ? ["Not set"] : Hotkey.Describe().Split(" + ");

    public string Summary => Hotkey.IsComplete
        ? Hotkey.DescribeAction()
        : Hotkey.Key == 0 ? "New shortcut: choose what it does, then its keys"
        : "Pick a preset for this shortcut.";

    // ---------------------------------------------------------------- state

    /// <summary>What the page says about the shortcut: working, off, taken, a clash.</summary>
    public string StateText { get; private set; } = "";

    /// <summary>A known problem with the combination itself, said under Keys.</summary>
    public string KeysNote => Hotkey.Key == 0 ? "Press Change, then the keys you want. Escape cancels."
        : Hotkey.Caution(Hotkey.Key, Hotkey.Modifiers) ?? "Ctrl+Alt combinations are the least likely to be taken by something else.";

    public void SetState(string text)
    {
        if (StateText == text) return;
        StateText = text;
        Raise(nameof(StateText));
    }

    private bool _capturing;

    /// <summary>Waiting for the next key press; the card says so where the keys are shown.</summary>
    public bool IsCapturing
    {
        get => _capturing;
        set
        {
            if (_capturing == value) return;
            _capturing = value;
            Raise();
            Raise(nameof(KeyParts));
            Raise(nameof(CaptureLabel));
        }
    }

    public string CaptureLabel => IsCapturing ? "Press the keys (Esc cancels)" : "Change keys";

    private bool _expanded;

    /// <summary>Open, for a shortcut just added; closed otherwise.</summary>
    public bool IsExpanded
    {
        get => _expanded;
        set { if (_expanded == value) return; _expanded = value; Raise(); }
    }

    /// <summary>Raised when something the page summarises changed.</summary>
    public event Action? Changed;

    /// <summary>Records a captured key press.</summary>
    public void Capture(uint key, uint modifiers)
    {
        Hotkey.Key = key;
        Hotkey.Modifiers = modifiers;
        persist();
        IsCapturing = false;
        RaiseAll();
        Changed?.Invoke();
    }

    public void RaiseAll()
    {
        Raise(nameof(Shortcut));
        Raise(nameof(KeyParts));
        Raise(nameof(Summary));
        Raise(nameof(KeysNote));
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
/// the opposite of what a global shortcut is for. What the engine managed to
/// register comes back through <see cref="HotkeyStatus"/>.
/// </remarks>
public sealed class HotkeysViewModel : INotifyPropertyChanged
{
    private readonly Func<DispCtrlSettings> _settings;
    private readonly Action _persist;
    private bool _engineRunning = true;

    public HotkeysViewModel(Func<DispCtrlSettings> settings, Action persist)
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
            if (DispCtrl.Core.FeatureFlags.Presets || h.Action != HotkeyAction.ApplyPreset)
                Items.Add(Wrap(h));

        RefreshStates();
    }

    private HotkeyViewModel Wrap(Hotkey h)
    {
        var item = new HotkeyViewModel(h, _persist, PresetNames);
        item.Changed += RefreshStates;
        return item;
    }

    private static IReadOnlyList<string> PresetNames()
    {
        if (!DispCtrl.Core.FeatureFlags.Presets) return [];
        var names = new List<string>();
        foreach (Preset p in PresetStore.Load()) names.Add(p.Name);

        return names;
    }

    public Visibility EmptyVisibility => Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>"4 on, 9 set but off".</summary>
    public string Counts
    {
        get
        {
            int on = Items.Count(i => i.Hotkey.Enabled && i.Hotkey.IsComplete);
            int off = Items.Count - on;
            return Items.Count == 0 ? "" : off == 0 ? $"{on} on" : $"{on} on, {off} set but off";
        }
    }

    /// <summary>
    /// Says, on each card, what became of it: working, off, taken by another
    /// program, or sharing its keys with another shortcut.
    /// </summary>
    public void RefreshStates()
    {
        HotkeyStatus? status = HotkeyStatus.Read();
        var duplicates = Items.Where(i => i.Hotkey.Key != 0 && i.Hotkey.Enabled)
            .GroupBy(i => (i.Hotkey.Key, i.Hotkey.Modifiers)).Where(g => g.Count() > 1)
            .SelectMany(g => g).ToHashSet();
        foreach (HotkeyViewModel item in Items)
        {
            Hotkey h = item.Hotkey;
            string keys = h.Describe();
            item.SetState(
                !h.IsComplete ? "Not finished: it needs keys" + (h.Action == HotkeyAction.ApplyPreset ? " and a preset" : "")
                : !h.Enabled ? "Off. Switch it on to use it."
                : duplicates.Contains(item) ? "Shares its keys with another shortcut; only one of them can work"
                : !_engineRunning ? "Not active: the engine is not running"
                : status?.Refused.Contains(keys) == true ? "Taken by another program. Choose other keys."
                : status?.Registered.Contains(keys) == true ? "Working"
                : "Waiting for the engine to pick it up");
        }
        Raise(nameof(EmptyVisibility));
        Raise(nameof(Counts));
    }

    /// <summary>Adds a shortcut, opened and waiting for its keys.</summary>
    public HotkeyViewModel Add()
    {
        // Ctrl+Alt is the default because it is the combination least likely to
        // be taken: Win is Windows', and Ctrl+Shift belongs to applications.
        var hotkey = new Hotkey { Modifiers = 1 | 2, Action = HotkeyAction.UnisonUp, Step = 5 };

        _settings().Hotkeys.Add(hotkey);
        _persist();

        HotkeyViewModel item = Wrap(hotkey);
        item.IsExpanded = true;
        Items.Add(item);
        RefreshStates();
        return item;
    }

    /// <summary>Replaces every shortcut with the defaults: a few on, the rest set but off.</summary>
    public void RestoreDefaults()
    {
        DispCtrlSettings settings = _settings();
        settings.Hotkeys.Clear();
        settings.Hotkeys.AddRange(Hotkey.Defaults());
        settings.Global.HotkeyDefaultsOffered = true; settings.Global.HotkeyDefaultsVersion = Hotkey.DefaultsVersion;
        _persist();
        Reload();
    }

    public void Remove(HotkeyViewModel item)
    {
        _settings().Hotkeys.Remove(item.Hotkey);
        _persist();

        Items.Remove(item);
        RefreshStates();
    }

    /// <summary>
    /// Whether the engine is running, since nothing works if it is not.
    /// </summary>
    public string EngineNote { get; private set; } = "";

    public void SetEngineRunning(bool running)
    {
        _engineRunning = running;
        string note = running
            ? "Global shortcuts, registered by DispCtrl's engine, so they work with this window closed. A few are on to start with; the rest are set up and a switch away."
            : "The engine registers these and is not running, so none of them works. Start it from Settings.";

        if (EngineNote != note)
        {
            EngineNote = note;
            Raise(nameof(EngineNote));
        }
        RefreshStates();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
