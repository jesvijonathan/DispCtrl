using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DispCtrl.Control;
using Microsoft.UI.Xaml;
using DispCtrl.Core.Presets;
using DispCtrl.Core.Settings;

namespace DispCtrl.App.ViewModels;

/// <summary>One shortcut, as the Hotkeys page edits it.</summary>
public sealed class HotkeyViewModel(Hotkey hotkey, Action persist, Func<IReadOnlyList<string>> presets, Func<IReadOnlyList<string>> features)
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
        (HotkeyAction.SoftwareDimUp, "Software brightness up (less dimming)"),
        (HotkeyAction.SoftwareDimDown, "Software brightness down (more dimming)"),
        (HotkeyAction.AmbientToggle, "Follow the room's light, on or off"),
        (HotkeyAction.NightLightToggle, "Night light on or off"),
        (HotkeyAction.NightLightWarmer, "Night light warmer"),
        (HotkeyAction.NightLightCooler, "Night light cooler"),
        (HotkeyAction.FocusToggle, "Focus mode on or off"),
        (HotkeyAction.OledCareToggle, "OLED care on or off"),
        (HotkeyAction.OledRestNow, "Rest the OLED displays now"),
        (HotkeyAction.KeepAwakeToggle, "Keep awake on or off"),
        (HotkeyAction.StayActiveToggle, "Stay active on or off"),
        (HotkeyAction.DisplaysOffToggle, "Turn the displays off, or back on"),
        (HotkeyAction.RestoreDisplays, "Put every display back (emergency)"),
        (HotkeyAction.DarkModeToggle, "Dark or light mode"),
        (HotkeyAction.TaskbarToggle, "Hide or show the taskbar"),
        (HotkeyAction.TaskbarGlassToggle, "Taskbar glass on or off"),
        (HotkeyAction.ContrastUp, "Contrast up"),
        (HotkeyAction.ContrastDown, "Contrast down"),
        (HotkeyAction.VolumeUp, "Monitor volume up"),
        (HotkeyAction.VolumeDown, "Monitor volume down"),
        (HotkeyAction.MuteToggle, "Mute or unmute the monitor"),
        (HotkeyAction.NextInput, "Next input source"),
        (HotkeyAction.SetControl, "Set a monitor control"),
        (HotkeyAction.NextControlValue, "Next value of a monitor control"),
        (HotkeyAction.PreviousControlValue, "Previous value of a monitor control"),
        (HotkeyAction.ControlUp, "Monitor control up"),
        (HotkeyAction.ControlDown, "Monitor control down"),
        (HotkeyAction.RunFeature, "Run a custom feature"),
        (HotkeyAction.Identify, "Show the display numbers"),
        (HotkeyAction.DisplayMode, "Display mode: extend, duplicate, one screen"),
        (HotkeyAction.MakePrimary, "Make a display the main one"),
        (HotkeyAction.HdrToggle, "HDR on or off"),
        (HotkeyAction.VariableRefreshToggle, "Variable refresh rate on or off"),
        (HotkeyAction.QuickPanel, "Open or close the quick panel"),
        (HotkeyAction.PinWindow, "Pin the active window on top, or unpin it"),
        (HotkeyAction.UnpinAllWindows, "Unpin every pinned window"),
        (HotkeyAction.GatherWindows, "Gather every window onto one display"),
        (HotkeyAction.ReturnWindowsToggle, "Put windows back on or off"),
        (HotkeyAction.NewWindowsToggle, "Open new windows on the display in use"),
        (HotkeyAction.RunCommand, "Run a dispctrl command"),
        (HotkeyAction.OpenProgram, "Open a program, file or link"),
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
            // The picker below shows the first arrangement, so record it rather
            // than leaving the shortcut incomplete under a filled-in control.
            if (Hotkey.Action == HotkeyAction.DisplayMode && !Hotkey.Modes.Contains(Hotkey.Mode)) Hotkey.Mode = Hotkey.Modes[0];
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

    public IReadOnlyList<string> Features => features();

    public string? SelectedFeature
    {
        get => Hotkey.Feature;
        set
        {
            if (Hotkey.Feature == value) return;
            Hotkey.Feature = value;
            persist();
            Raise();
            RaiseAll();
        }
    }

    public Visibility FeatureVisibility =>
        Hotkey.Action == HotkeyAction.RunFeature ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The four arrangements, worded as Win+P words them.</summary>
    private static readonly string[] AllModeNames = Hotkey.Modes.Select(Hotkey.ModeName).ToArray();
    public IReadOnlyList<string> ModeNames => AllModeNames;

    /// <summary>The arrangement, as an index; see <see cref="SelectedActionIndex"/> for why not the string.</summary>
    public int SelectedModeIndex
    {
        get
        {
            int at = Array.IndexOf(Hotkey.Modes, Hotkey.Mode);
            return at < 0 ? 0 : at;
        }
        set
        {
            // A ComboBox writes its index back as it is realised, collapsed or
            // not; only a shortcut that is about the arrangement records one.
            if (Hotkey.Action != HotkeyAction.DisplayMode) return;
            if (value < 0 || value >= Hotkey.Modes.Length) return;
            string mode = Hotkey.Modes[value];
            if (Hotkey.Mode == mode) return;
            Hotkey.Mode = mode;
            persist();
            Raise();
            RaiseAll();
        }
    }

    public Visibility ModeVisibility =>
        Hotkey.Action == HotkeyAction.DisplayMode ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The dispctrl arguments, or what to open.</summary>
    public string Command
    {
        get => Hotkey.Command ?? "";
        set
        {
            if ((Hotkey.Command ?? "") == value) return;
            Hotkey.Command = value;
            persist();
            Raise();
            RaiseAll();
        }
    }

    public string Arguments
    {
        get => Hotkey.Arguments ?? "";
        set
        {
            if ((Hotkey.Arguments ?? "") == value) return;
            Hotkey.Arguments = value;
            persist();
            Raise();
            RaiseAll();
        }
    }

    public Visibility CommandVisibility =>
        Hotkey.Action is HotkeyAction.RunCommand or HotkeyAction.OpenProgram ? Visibility.Visible : Visibility.Collapsed;

    public string Control
    {
        get => Hotkey.Control ?? "";
        set
        {
            if ((Hotkey.Control ?? "") == value) return;
            Hotkey.Control = value;
            persist();
            Raise();
            RaiseAll();
        }
    }

    public string Value
    {
        get => Hotkey.Value ?? "";
        set
        {
            if ((Hotkey.Value ?? "") == value) return;
            Hotkey.Value = value;
            persist();
            Raise();
            RaiseAll();
        }
    }

    public Visibility ControlVisibility =>
        Hotkey.IsControlAction(Hotkey.Action) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ValueVisibility =>
        Hotkey.Action == HotkeyAction.SetControl ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Only a program takes arguments; a command carries its own.</summary>
    public Visibility ArgumentsVisibility =>
        Hotkey.Action == HotkeyAction.OpenProgram ? Visibility.Visible : Visibility.Collapsed;

    public string CommandHeader => Hotkey.Action == HotkeyAction.OpenProgram ? "What to open" : "Command";

    public string CommandHint => Hotkey.Action == HotkeyAction.OpenProgram
        ? "A program, script, document or link, opened the way Explorer would: notepad.exe, ms-settings:display, https://..."
        : "The arguments to dispctrl, without the program name: topology set duplicate, brightness -10 --all, preset apply Evening.";

    /// <summary>Which display, as "All" or a number.</summary>
    /// <remarks>
    /// Hidden for the actions that are not per display — a display picker beside
    /// "night light on or off" would imply something the setting cannot do.
    /// </remarks>
    public Visibility DisplayVisibility => Hotkey.Action is
        HotkeyAction.BrightnessUp or HotkeyAction.BrightnessDown or HotkeyAction.NextInput
        or HotkeyAction.SetControl or HotkeyAction.NextControlValue or HotkeyAction.PreviousControlValue
        or HotkeyAction.ControlUp or HotkeyAction.ControlDown
        or HotkeyAction.ContrastUp or HotkeyAction.ContrastDown or HotkeyAction.GatherWindows
        or HotkeyAction.SoftwareDimUp or HotkeyAction.SoftwareDimDown or HotkeyAction.MakePrimary
        or HotkeyAction.HdrToggle or HotkeyAction.VolumeUp or HotkeyAction.VolumeDown or HotkeyAction.MuteToggle
        ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>What 0 means for this action: every display, or for gathering the one in use.</summary>
    public string DisplayHint => Hotkey.Action is HotkeyAction.GatherWindows or HotkeyAction.MakePrimary
        ? "The number shown by Identify; 0 for the display in use, as chosen on the Displays page."
        : "The number shown by Identify; 0 for every display.";

    public double DisplayNumber
    {
        get => Hotkey.Display;
        set
        {
            int v = double.IsNaN(value) ? 0 : (int)Math.Clamp(value, 0, 16);
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
            int v = double.IsNaN(value) ? Hotkey.Step : (int)Math.Clamp(value, 1, 100);
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
        or HotkeyAction.SoftwareDimUp or HotkeyAction.SoftwareDimDown or HotkeyAction.VolumeUp or HotkeyAction.VolumeDown
        or HotkeyAction.ControlUp or HotkeyAction.ControlDown
        ? Visibility.Visible : Visibility.Collapsed;

    public bool Enabled
    {
        get => Hotkey.Enabled;
        set
        {
            // A ToggleSwitch writes its value back as it is realised; only a
            // real change is saved.
            if (Hotkey.Enabled == value) return;
            // The way back from a black screen is only any use if it is still
            // there when needed, so switching it off is asked about first. The
            // switch springs back; the page confirms and calls TurnOff.
            if (!value && IsSafetyNet && !_offConfirmed)
            {
                Raise();
                SafetyNetOffRequested?.Invoke(this);
                return;
            }
            _offConfirmed = false;
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
        : Hotkey.Action == HotkeyAction.DisplayMode ? "Pick an arrangement for this shortcut."
        : Hotkey.Action is HotkeyAction.RunCommand or HotkeyAction.OpenProgram ? "Say what this shortcut should run."
        : Hotkey.IsControlAction(Hotkey.Action) ? "Name the monitor control for this shortcut."
        : Hotkey.Action == HotkeyAction.RunFeature ? "Pick the custom feature for this shortcut."
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

    /// <summary>This is the shortcut that puts every display back.</summary>
    public bool IsSafetyNet => Hotkey.Action == HotkeyAction.RestoreDisplays;

    private bool _offConfirmed;

    /// <summary>Raised instead of switching the safety net off; the page asks, then calls <see cref="TurnOff"/>.</summary>
    public static event Action<HotkeyViewModel>? SafetyNetOffRequested;

    public void RaiseEnabled() => Raise(nameof(Enabled));

    /// <summary>Switches it off once the person has confirmed.</summary>
    public void TurnOff()
    {
        _offConfirmed = true;
        Enabled = false;
    }

    /// <summary>Records a captured key press.</summary>
    public void Capture(uint key, uint modifiers)
    {
        Hotkey.Key = key;
        Hotkey.Modifiers = modifiers;
        persist();
        IsCapturing = false;
        RaiseAll();
    }

    public void RaiseAll()
    {
        Raise(nameof(Shortcut));
        Raise(nameof(KeyParts));
        Raise(nameof(Summary));
        Raise(nameof(KeysNote));
        Raise(nameof(PresetVisibility));
        Raise(nameof(FeatureVisibility));
        Raise(nameof(SelectedFeature));
        Raise(nameof(Features));
        Raise(nameof(ModeVisibility));
        Raise(nameof(SelectedModeIndex));
        Raise(nameof(CommandVisibility));
        Raise(nameof(ArgumentsVisibility));
        Raise(nameof(CommandHeader));
        Raise(nameof(CommandHint));
        Raise(nameof(ControlVisibility));
        Raise(nameof(ValueVisibility));
        Raise(nameof(DisplayVisibility));
        Raise(nameof(DisplayHint));
        Raise(nameof(StepVisibility));
        Raise(nameof(Presets));
        Changed?.Invoke();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CustomFeatureViewModel(CustomFeature feature) : INotifyPropertyChanged
{
    public CustomFeature Feature { get; } = feature;
    public string? SavedName { get; set; }
    private bool _busy;
    public bool IsIdle => !_busy;
    public bool Busy
    {
        get => _busy;
        set { _busy = value; Raise(nameof(IsIdle)); }
    }

    public string Name
    {
        get => Feature.Name;
        set { if (Feature.Name == value) return; Feature.Name = value; Raise(); }
    }

    public string Description
    {
        get => Feature.Description;
        set { if (Feature.Description == value) return; Feature.Description = value; Raise(); }
    }

    public string Steps
    {
        get => string.Join(Environment.NewLine, Feature.Steps);
        set
        {
            List<string> steps = CustomFeature.SplitSteps(value);
            if (Feature.Steps.SequenceEqual(steps)) return;
            Feature.Steps = steps;
            Raise();
        }
    }

    public string Status
    {
        get => _status;
        set { if (_status == value) return; _status = value; Raise(); }
    }
    private string _status = "";

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
    public ObservableCollection<CustomFeatureViewModel> Features { get; } = [];
    public string FeatureGrammar => CustomFeature.Grammar;

    public void Reload()
    {
        Items.Clear();
        foreach (Hotkey h in _settings().Hotkeys)
            if (DispCtrl.Core.FeatureFlags.Presets || h.Action != HotkeyAction.ApplyPreset)
                Items.Add(Wrap(h));
        ReloadFeatures();

        RefreshStates();
    }

    public void ReloadFeatures()
    {
        Features.Clear();
        foreach (CustomFeature f in _settings().Features.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
            Features.Add(new CustomFeatureViewModel(new CustomFeature { Name = f.Name, Description = f.Description, Steps = [.. f.Steps] }) { SavedName = f.Name });
        Raise(nameof(Features));
    }

    private HotkeyViewModel Wrap(Hotkey h)
    {
        var item = new HotkeyViewModel(h, _persist, PresetNames, FeatureNames);
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

    private IReadOnlyList<string> FeatureNames() =>
        _settings().Features.Select(f => f.Name).Where(n => n.Length > 0).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

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
                !h.IsComplete ? "Not finished: it needs keys"
                    + (h.Action == HotkeyAction.ApplyPreset ? " and a preset"
                        : h.Action == HotkeyAction.DisplayMode ? " and an arrangement"
                        : h.Action is HotkeyAction.RunCommand or HotkeyAction.OpenProgram ? " and something to run"
                        : Hotkey.IsControlAction(h.Action) ? h.Action == HotkeyAction.SetControl ? " and a control/value" : " and a control"
                        : h.Action == HotkeyAction.RunFeature ? " and a feature" : "")
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

    public CustomFeatureViewModel AddFeature()
    {
        var feature = new CustomFeatureViewModel(new CustomFeature { Name = "New feature" });
        Features.Add(feature);
        return feature;
    }

    public async Task SaveFeatureAsync(CustomFeatureViewModel feature)
    {
        if (feature.Busy) return;
        feature.Busy = true;
        string requestedName = feature.Name.Trim();
        try
        {
            JsonObject result = await FeatureRequestAsync(feature.SavedName is null ? "features.add" : "features.set", new JsonObject
            {
                ["name"] = feature.SavedName ?? requestedName,
                ["rename"] = requestedName,
                ["description"] = feature.Description,
                ["steps"] = feature.Steps,
            });
            if (result["ok"]?.GetValue<bool>() == true)
            {
                SyncFeaturesFromDisk();
                feature.SavedName = requestedName;
                feature.Status = $"Saved “{requestedName}”.";
            }
            else feature.Status = result["error"]?["message"]?.GetValue<string>() ?? "Not saved.";
        }
        catch (Exception ex) { feature.Status = ex.Message; }
        finally { feature.Busy = false; }
    }

    public async Task DeleteFeatureAsync(CustomFeatureViewModel feature)
    {
        if (feature.Busy) return;
        if (feature.SavedName is null) { Features.Remove(feature); return; }
        feature.Busy = true;
        try
        {
            JsonObject result = await FeatureRequestAsync("features.remove", new JsonObject { ["name"] = feature.SavedName });
            if (result["ok"]?.GetValue<bool>() == true)
            {
                SyncFeaturesFromDisk();
                Features.Remove(feature);
            }
            else feature.Status = result["error"]?["message"]?.GetValue<string>() ?? "Not deleted.";
        }
        catch (Exception ex) { feature.Status = ex.Message; }
        finally { feature.Busy = false; }
    }

    public async Task RunFeatureAsync(CustomFeatureViewModel feature, bool dryRun)
    {
        if (feature.Busy) return;
        var draft = new CustomFeature { Name = feature.Name.Trim(), Description = feature.Description, Steps = [.. feature.Feature.Steps] };
        if (CustomFeature.Problem([draft]) is { } problem) { feature.Status = problem; return; }
        JsonObject result;
        try
        {
            feature.Busy = true;
            result = await Task.Run(() => new ControlService().RunFeature(draft, dryRun));
        }
        catch (Exception ex) { feature.Status = ex.Message; return; }
        finally { feature.Busy = false; }
        int passed = result["steps"]!.AsArray().Count(s => s?["ok"]?.GetValue<bool>() == true);
        string? error = result["steps"]!.AsArray().FirstOrDefault(s => s?["ok"]?.GetValue<bool>() == false)?["error"]?.GetValue<string>();
        feature.Status = $"{result["state"]!.GetValue<string>()}: {passed} step(s) ok." + (error is null ? "" : " " + error);
    }

    private static Task<JsonObject> FeatureRequestAsync(string command, JsonObject args) =>
        Task.Run(() => new ControlService().Execute(new JsonObject { ["version"] = 1, ["command"] = command, ["args"] = args }));

    private void SyncFeaturesFromDisk()
    {
        DispCtrlSettings fresh = SettingsStore.Load();
        SettingsStore.RefreshFeatures(_settings(), fresh);
        foreach (HotkeyViewModel item in Items) item.RaiseAll();
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
