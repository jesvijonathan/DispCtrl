using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using DisplCtrl.Core.Displays;
using DisplCtrl.Core.Presets;
using DisplCtrl.Core.Settings;
using DisplCtrl.Display.Presets;

namespace DisplCtrl.App.ViewModels;

/// <summary>One app rule, as the list on the Presets page edits it.</summary>
public sealed class AppRuleViewModel(AppRule rule, Action persist, ObservableCollection<string> names) : INotifyPropertyChanged
{
    public AppRule Rule { get; } = rule;
    public ObservableCollection<string> PresetNames { get; } = names;
    public bool RestorePrevious
    {
        get => Rule.RestorePrevious;
        set { Rule.RestorePrevious = value; persist(); Raise(); Raise(nameof(Summary)); }
    }
    public double DwellSeconds
    {
        get => Rule.DwellSeconds;
        set { Rule.DwellSeconds = double.IsFinite(value) ? Math.Clamp(value, 0.5, 60) : 2; persist(); Raise(); }
    }

    public string Process
    {
        get => Rule.Process;
        set { Rule.Process = value; persist(); Raise(); Raise(nameof(Summary)); }
    }

    public string Preset
    {
        get => Rule.Preset;
        set { if (value is null) return; Rule.Preset = value; persist(); Raise(); Raise(nameof(Summary)); }
    }

    public string RevertTo
    {
        get => Rule.RevertTo ?? "";
        set
        {
            if (value is null) return; // Ignore selection teardown during collection refresh.
            Rule.RevertTo = string.IsNullOrWhiteSpace(value) ? null : value;
            persist();
            Raise();
            Raise(nameof(Summary));
        }
    }

    public bool Enabled
    {
        get => Rule.Enabled;
        set { Rule.Enabled = value; persist(); Raise(); }
    }

    public string Summary
    {
        get
        {
            if (!Rule.IsComplete) return "Incomplete — needs an app and a preset.";

            if (!PresetNames.Contains(Rule.Preset)) return "The selected preset is missing. Choose an existing preset.";
            if (Rule.RestorePrevious) return $"Use “{Rule.Preset}” while {Rule.Process} is in front, then restore the previous setup.";
            string back = string.IsNullOrWhiteSpace(Rule.RevertTo)
                ? "and stays there afterwards"
                : $"then back to “{Rule.RevertTo}”";

            return $"When {Rule.Process} is in front, use “{Rule.Preset}”, {back}.";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// The Presets page.
/// </summary>
/// <remarks>
/// Holds the selected preset as "sticky": the desk drifts as the user changes
/// things, and the page keeps saying how it differs from what was saved, with
/// Save and Discard both one press away. That is the whole idea — a preset you
/// have to remember to re-save is one that silently goes stale.
/// </remarks>
public sealed record PresetScopeChoice(string? Token, string Label);

public sealed class PresetsViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// Read through a callback, not held.
    /// </summary>
    /// <remarks>
    /// A rescan replaces the whole settings instance, so a captured reference
    /// would silently go stale — this page would then be saving presets from,
    /// and writing app rules into, an object nothing else still reads.
    /// </remarks>
    private readonly Func<DisplCtrlSettings> _settingsSource;

    private readonly Func<IReadOnlyList<DisplayInfo>> _displays;
    private readonly Action _persist;

    /// <summary>
    /// Re-reads every display after a preset has been written.
    /// </summary>
    /// <remarks>
    /// Applying a preset changes the hardware behind the other pages' backs, so
    /// without this their controls keep showing what was true before. It is not
    /// cosmetic: a brightness slider still reading 25 after the preset put the
    /// panel back to 62 will write 25 again the moment it is nudged.
    /// </remarks>
    private readonly Action _refreshDisplays;

    private DisplCtrlSettings _settings => _settingsSource();

    public PresetsViewModel(Func<DisplCtrlSettings> settings, Func<IReadOnlyList<DisplayInfo>> displays,
                            Action persist, Action refreshDisplays)
    {
        _settingsSource = settings;
        _displays = displays;
        _persist = persist;
        _refreshDisplays = refreshDisplays;

        Reload();
    }

    /// <summary>
    /// The last entry in the picker, which starts a new preset rather than
    /// selecting one.
    /// </summary>
    /// <remarks>
    /// A command living in the data list is a small impurity, and it buys the
    /// thing that matters: creating a preset is reachable from the same control
    /// that switches between them, so the bar never needs a second name box
    /// sitting empty next to it.
    /// </remarks>
    public const string NewEntry = "New preset\u2026";

    public ObservableCollection<string> Names { get; } = [];
    public ObservableCollection<string> PresetNames { get; } = [];
    public ObservableCollection<PresetScopeChoice> CaptureScopes { get; } = [];
    public PresetScopeChoice? CaptureScope { get; set; }
    public bool IsBusy { get; private set; }
    public bool CanEdit => !IsBusy;
    public bool LastOperationOk { get; private set; } = true;
    public string SelectedJson => Current is { } p ? PresetStore.ToJson(p) : "";
    public IReadOnlyList<DisplayInfo> AvailableDisplays => _displays();
    public Preset? SelectedPreset => Current;

    private async Task<string> RunAsync(Func<Task<string>> action)
    {
        if (!DisplCtrl.Core.FeatureFlags.Presets) return "Presets are disabled in this build.";
        if (IsBusy) return "A preset operation is already running.";
        IsBusy = true;
        Raise(nameof(CanEdit));
        LastOperationOk = true;
        try { return await action(); }
        catch (Exception ex) { LastOperationOk = false; return $"Preset operation failed: {ex.Message}"; }
        finally { IsBusy = false; Raise(nameof(CanEdit)); RefreshDrift(); }
    }
    public ObservableCollection<AppRuleViewModel> Rules { get; } = [];

    private readonly List<Preset> _presets = [];
    private readonly Dictionary<string, Preset> _byName = new(StringComparer.Ordinal);
    private string? _selected;
    private bool _reloading;
    private string _status = "";
    private List<PresetChange> _differences = [];

    // ------------------------------------------------------------- listing --

    public void Reload()
    {
        if (!DisplCtrl.Core.FeatureFlags.Presets) return;
        _reloading = true;
        _presets.Clear();
        _presets.AddRange(PresetStore.Load());
        _byName.Clear();
        foreach (Preset preset in _presets) _byName[preset.Name] = preset;

        Names.Clear();
        PresetNames.Clear();
        string? captureToken = CaptureScope?.Token;
        CaptureScopes.Clear();
        CaptureScopes.Add(new(null, "Whole desk"));
        foreach (var display in _displays()) CaptureScopes.Add(new(display.Token, display.Label));
        CaptureScope = CaptureScopes.FirstOrDefault(choice => choice.Token == captureToken) ?? CaptureScopes[0];
        Raise(nameof(CaptureScope));
        foreach (Preset p in _presets)
        {
            // A preset file literally named after the sentinel would otherwise
            // be impossible to select, because picking it would read as "start
            // a new one". Only reachable by creating the file by hand.
            if (p.Name == NewEntry) continue;
            Names.Add(p.Name);
            PresetNames.Add(p.Name);
        }

        Names.Add(NewEntry);

        if (_selected is not null && !Names.Contains(_selected)) _selected = null;
        _selected ??= _presets.Count > 0 ? _presets[0].Name : NewEntry;

        Rules.Clear();
        foreach (AppRule r in _settings.AppRules) Rules.Add(new AppRuleViewModel(r, _persist, PresetNames));

        _reloading = false;
        Raise(nameof(Selected));
        Raise(nameof(Details));
        Raise(nameof(RulesEmptyVisibility));
        Raise(nameof(HasPresets));
        Raise(nameof(EmptyVisibility));
        Raise(nameof(PresetVisibility));
        Raise(nameof(Creating));
        Raise(nameof(CreatingVisibility));
        Raise(nameof(ExistingVisibility));
        Raise(nameof(ActionVisibility));
        Raise(nameof(SaveButtonText));
        RefreshDrift();
    }

    public bool HasPresets => _presets.Count > 0;

    /// <summary>True while the picker is on <see cref="NewEntry"/>.</summary>
    public bool Creating => _selected == NewEntry;

    public Visibility CreatingVisibility => Creating ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Apply and Discard mean nothing until there is a preset to act on.</summary>
    public Visibility ExistingVisibility =>
        !Creating && Current is not null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Whether the bar has anything worth offering to do.
    /// </summary>
    /// <remarks>
    /// Buttons that are always there stop meaning anything. Save is only honest
    /// once something has drifted, and applying a preset the desk already
    /// matches would blank the screen for a mode change that changes nothing.
    /// So when everything matches, the bar says so and offers nothing.
    /// </remarks>
    public Visibility ActionVisibility =>
        !Creating && Current is not null && IsDirty ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Name for the preset being created.</summary>
    public string NewName
    {
        get => _newName;
        set
        {
            if (_newName == value) return;
            _newName = value;
            Raise();
            Raise(nameof(SaveButtonText));
        }
    }

    private string _newName = "";

    public string SaveButtonText => Creating ? "Create" : "Save";

    /// <summary>
    /// The one-line state for the docked bar.
    /// </summary>
    /// <remarks>
    /// Not shown in the docked bar any more: there it read "Saved" almost all
    /// of the time, which is a word that is always present and never informs.
    /// The drift sign took over, because it appears only when there is
    /// something to say. This is still what the Presets page shows while
    /// creating or when nothing is selected.
    /// </remarks>
    public string ShortStatus
    {
        get
        {
            if (Creating) return "Name it, then Create.";
            if (Current is null) return "No preset selected.";

            return IsDirty ? "Unsaved changes" : "Saved";
        }
    }

    public Visibility EmptyVisibility => HasPresets ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Always shown. With no presets saved, the bar is how the first one gets
    /// made — hiding it until one exists would hide the only way to start.
    /// </summary>
    public Visibility PresetVisibility => Visibility.Visible;

    public string? Selected
    {
        get => _selected;
        set
        {
            if (_reloading || _selected == value) return;
            _selected = value;
            Raise();
            RefreshDrift();
            Raise(nameof(Details));
            Raise(nameof(Creating));
            Raise(nameof(CreatingVisibility));
            Raise(nameof(ExistingVisibility));
            Raise(nameof(ActionVisibility));
            Raise(nameof(SaveButtonText));
        }
    }

    private Preset? Current => _selected is not null && _selected != NewEntry
        && _byName.TryGetValue(_selected, out Preset? preset) ? preset : null;

    // --------------------------------------------------------------- drift --

    private bool _checking;
    private bool _checkAgain;

    /// <summary>Re-compares the desk against the selected preset.</summary>
    public void RefreshDrift() => _ = RefreshDriftAsync();

    /// <summary>
    /// Re-compares the desk against the selected preset, off the UI thread.
    /// </summary>
    /// <remarks>
    /// A capture is a hardware read per display: DDC/CI for an external
    /// monitor's brightness, and IDesktopWallpaper for its wallpaper. Both are
    /// tens to hundreds of milliseconds, and running them inline froze the
    /// window every time the bar re-checked itself.
    /// <para>
    /// Overlapping runs are collapsed rather than queued. The debounce can fire
    /// again while a slow DDC/CI read is still outstanding, and stacking those
    /// would leave the monitor answering a backlog long after the user stopped.
    /// </para>
    /// </remarks>
    public async Task RefreshDriftAsync()
    {
        if (!DisplCtrl.Core.FeatureFlags.Presets) return;
        if (IsBusy) return;
        if (_checking)
        {
            _checkAgain = true;
            return;
        }

        _checking = true;
        try
        {
            do
            {
                _checkAgain = false;
                await CheckOnceAsync().ConfigureAwait(true);
            }
            while (_checkAgain);
        }
        finally
        {
            _checking = false;
        }
    }

    private async Task CheckOnceAsync()
    {
        Preset? saved = Current;
        if (saved is null)
        {
            _differences = [];
            _status = Creating ? "Name it, then Create." : "No preset selected.";
            RaiseDrift();
            return;
        }

        // Snapshot settings on the UI thread; background reads must not race slider edits.
        DisplCtrlSettings settings = System.Text.Json.JsonSerializer.Deserialize(
            System.Text.Json.JsonSerializer.Serialize(_settings, SettingsJsonContext.Default.DisplCtrlSettings),
            SettingsJsonContext.Default.DisplCtrlSettings)!;
        IReadOnlyList<DisplayInfo> displays = _displays();

        try
        {
            Preset live = await Task.Run(
                () => PresetService.Capture(saved.Name, displays, settings, useCache: true)).ConfigureAwait(true);

            // The selection can change while a slow capture is in flight, and
            // reporting the old preset's drift against the new one would be
            // worse than reporting nothing.
            if (!ReferenceEquals(Current, saved)) return;

            _differences = PresetDiff.Describe(saved, live);
        }
        catch (Exception ex)
        {
            _differences = [];
            _status = $"Could not read the current setup: {ex.Message}";
            RaiseDrift();
            return;
        }

        _status = _differences.Count == 0
            ? "Everything matches this preset."
            : $"{_differences.Count} difference(s) from this preset.";

        RaiseDrift();
    }

    public string Status => _status;

    /// <summary>
    /// Every difference, for the flyout that opens off the drift icon.
    /// </summary>
    /// <remarks>
    /// An observable collection rather than a rebuilt list, so the flyout can
    /// stay open while a re-check finishes underneath it. Re-assigning the
    /// property instead would close it every time the debounce fired, which on
    /// a moving slider is continuously.
    /// </remarks>
    public ObservableCollection<PresetChange> Changes { get; } = [];

    public bool IsDirty => _differences.Count > 0;

    public Visibility DirtyVisibility => IsDirty ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>What the drift icon says when pointed at.</summary>
    public string DriftTooltip => _differences.Count switch
    {
        0 => "Everything matches this preset.",
        1 => "1 setting differs from this preset. Click to see it.",
        _ => $"{_differences.Count} settings differ from this preset. Click to see them.",
    };

    public string DriftCount => _differences.Count.ToString();

    private void RaiseDrift()
    {
        // Mutated in place rather than replaced, so an open flyout survives.
        // Preserve existing rows and bindings when the comparison has not changed.
        if (!Changes.SequenceEqual(_differences))
        {
            for (int i = 0; i < _differences.Count; i++)
                if (i >= Changes.Count) Changes.Add(_differences[i]);
                else if (Changes[i] != _differences[i]) Changes[i] = _differences[i];
            while (Changes.Count > _differences.Count) Changes.RemoveAt(Changes.Count - 1);
        }

        Raise(nameof(DriftTooltip));
        Raise(nameof(DriftCount));
        Raise(nameof(ActionVisibility));
        Raise(nameof(Status));
        Raise(nameof(ShortStatus));
        Raise(nameof(IsDirty));
        Raise(nameof(DirtyVisibility));
    }

    // ------------------------------------------------------------- actions --

    /// <summary>Writes the selected preset onto the desk.</summary>
    public Task<string> ApplyAsync() => RunAsync(ApplyCoreAsync);

    private async Task<string> ApplyCoreAsync()
    {
        if (Current is not Preset preset) return "No preset selected.";

        DisplCtrlSettings settings = _settings;
        IReadOnlyList<DisplayInfo> displays = _displays();

        PresetResult result = await Task.Run(() => PresetService.Apply(preset, displays, settings));
        if (result.Attempted) SettingsStore.Save(PresetSettings.Merge(preset, settings, SettingsStore.Load()));

        _refreshDisplays();
        RefreshDrift();

        LastOperationOk = result.Ok;
        if (!result.Ok) return "Not fully restored: " + string.Join("  ", result.Notes);

        return result.Notes.Count == 0
            ? $"Applied “{preset.Name}”."
            : $"Applied “{preset.Name}”, with notes: " + string.Join("  ", result.Notes);
    }

    /// <summary>Overwrites the selected preset with the desk as it is now.</summary>
    /// <remarks>
    /// Async for the same reason the drift check is: capturing reads every
    /// display's hardware, and a Save button that freezes the window for half a
    /// second reads as the app having hung.
    /// </remarks>
    public Task<string> SaveAsync() => RunAsync(SaveCoreAsync);

    private async Task<string> SaveCoreAsync()
    {
        if (Current is not Preset preset) return "No preset selected.";

        Preset fresh = await CaptureAsync(preset.Name).ConfigureAwait(true);

        // The description belongs to the preset, not to the desk, so it
        // survives a re-capture.
        fresh = PresetValidation.RetainScope(fresh, preset);

        PresetStore.Save(fresh);
        Reload();
        Selected = fresh.Name;

        return $"Saved “{fresh.Name}”.";
    }

    /// <summary>
    /// Saves over the selected preset, or creates the new one being named.
    /// </summary>
    /// <remarks>
    /// One button for both, because from the bar they are the same intention —
    /// "remember the desk as it is" — and which one happens is already decided
    /// by what the picker is showing.
    /// </remarks>
    public async Task<string> SaveOrCreateAsync()
    {
        if (!Creating) return await SaveAsync().ConfigureAwait(true);

        string message = await SaveAsAsync(NewName).ConfigureAwait(true);
        if (message.StartsWith("Saved", StringComparison.Ordinal)) NewName = "";

        return message;
    }

    private Task<Preset> CaptureAsync(string name)
    {
        DisplCtrlSettings settings = System.Text.Json.JsonSerializer.Deserialize(
            System.Text.Json.JsonSerializer.Serialize(_settings, SettingsJsonContext.Default.DisplCtrlSettings),
            SettingsJsonContext.Default.DisplCtrlSettings)!;
        IReadOnlyList<DisplayInfo> displays = _displays();

        return Task.Run(() => PresetService.Capture(name, displays, settings));
    }

    /// <summary>Captures the desk under a new name.</summary>
    public Task<string> SaveAsAsync(string name) => RunAsync(() => SaveAsCoreAsync(name));

    private async Task<string> SaveAsCoreAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Give the preset a name first.";
        name = Path.GetFileNameWithoutExtension(PresetStore.PathFor(name.Trim()));
        if (name.Length == 0) return "Give the preset a name first.";

        if (name == NewEntry) return "That name is reserved. Pick another.";

        // Checked against the file, not the display name: two names can
        // sanitise to one file, and comparing names alone would silently
        // overwrite a preset the user never mentioned.
        if (Names.Contains(name) || PresetStore.Exists(name))
            return $"“{name}” already exists — pick another name, or use Save to overwrite it.";

        string? monitorToken = CaptureScope?.Token;
        Preset fresh = await CaptureAsync(name).ConfigureAwait(true);
        if (monitorToken is not null)
        {
            fresh.IncludeGlobal = false;
            fresh.IncludeLayout = false;
            foreach (string token in fresh.Monitors.Keys.Where(token => token != monitorToken).ToList())
                fresh.Monitors.Remove(token);
            if (fresh.Monitors.Count == 0) return "The selected display is no longer attached.";
        }
        PresetStore.Save(fresh);
        Reload();
        Selected = name;

        return $"Saved “{name}”.";
    }

    /// <summary>Puts the desk back to the selected preset, discarding the drift.</summary>
    public Task<string> DiscardAsync() => ApplyAsync();

    public string Rename(string name) => RunFileAction(() => RenameCore(name));

    private string RenameCore(string name)
    {
        if (IsBusy) return "Wait for the preset operation to finish.";
        if (Current is not Preset preset) return "No preset selected.";

        if (string.IsNullOrWhiteSpace(name)) return "Give the preset a name first.";
        name = Path.GetFileNameWithoutExtension(PresetStore.PathFor(name.Trim()));
        if (name.Length == 0) return "Give the preset a name first.";
        if (name == preset.Name) return "That is already its name.";
        if (name == NewEntry) return "That name is reserved. Pick another.";

        // Same file, different spelling: renaming onto it is a no-op the store
        // handles safely, but any *other* existing file must not be clobbered.
        if (!PresetStore.SameFile(preset.Name, name)
            && (Names.Contains(name) || PresetStore.Exists(name)))
        {
            return $"“{name}” already exists.";
        }

        string oldName = preset.Name;
        PresetStore.Rename(oldName, name);
        foreach (AppRule rule in _settings.AppRules)
        {
            if (PresetStore.SameFile(rule.Preset, oldName)) rule.Preset = name;
            if (rule.RevertTo is not null && PresetStore.SameFile(rule.RevertTo, oldName)) rule.RevertTo = name;
        }
        _persist();
        Reload();
        Selected = name;

        return $"Renamed to “{name}”.";
    }

    public string Delete() => RunFileAction(() => DeleteCore());

    private string DeleteCore()
    {
        if (IsBusy) return "Wait for the preset operation to finish.";
        if (Current is not Preset preset) return "No preset selected.";

        PresetStore.Delete(preset.Name);
        foreach (AppRule rule in _settings.AppRules)
        {
            if (PresetStore.SameFile(rule.Preset, preset.Name)) rule.Enabled = false;
            if (rule.RevertTo is not null && PresetStore.SameFile(rule.RevertTo, preset.Name)) rule.RevertTo = null;
        }
        _persist();
        _selected = null;
        Reload();

        return $"Deleted “{preset.Name}”.";
    }

    public string Export(string destination) => RunFileAction(() => ExportCore(destination));

    private string ExportCore(string destination)
    {
        if (Current is not Preset preset) return "No preset selected.";

        PresetStore.Export(preset, destination);
        return $"Exported to {destination}.";
    }

    public string Import(string source) => RunFileAction(() => ImportCore(source));

    private string ImportCore(string source)
    {
        string? name = PresetStore.Import(source);
        if (name is null) return "That file is not a preset DisplCtrl can read.";

        Reload();
        Selected = name;

        return $"Imported as “{name}”.";
    }

    private string RunFileAction(Func<string> action)
    {
        if (!DisplCtrl.Core.FeatureFlags.Presets) return "Presets are disabled in this build.";
        if (IsBusy) return "Wait for the preset operation to finish.";
        try { LastOperationOk = true; return action(); }
        catch (Exception ex) { LastOperationOk = false; return ex.Message; }
    }

    public string SaveJson(string json)
    {
        if (Current is not { } current) return "No preset selected.";
        Preset edited = PresetStore.Parse(json);
        // Renaming is a separate operation so app-rule references stay valid.
        edited.Name = current.Name;
        PresetStore.Save(edited);
        Reload();
        return "Saved preset values. Apply to restore them to the displays.";
    }

    public string MapDisplays(IReadOnlyDictionary<string, string> mapping)
    {
        if (Current is not { } current) return "No preset selected.";
        Preset mapped = PresetStore.Parse(PresetStore.ToJson(current));
        var states = new Dictionary<string, PresetMonitor>();
        foreach (var (token, state) in mapped.Monitors)
        {
            string target = mapping.TryGetValue(token, out string? replacement) ? replacement : token;
            if (!states.TryAdd(target, state)) throw new FormatException("Choose a different target for each saved display.");
        }
        mapped.Monitors = states;
        PresetStore.Save(mapped);
        Reload();
        return "Display mapping saved. Review the values before applying to different hardware.";
    }

    public static string Folder => PresetStore.Directory;

    // --------------------------------------------------------------- scope --

    public string Details
    {
        get
        {
            if (Current is not Preset preset) return "";

            var attached = new HashSet<string>(StringComparer.Ordinal);
            foreach (DisplayInfo d in _displays()) attached.Add(d.Token);

            var lines = new List<string>(preset.Monitors.Count);
            foreach ((string token, PresetMonitor m) in preset.Monitors)
            {
                string state = attached.Contains(token) ? "attached" : "not attached";
                string rate = m.RefreshHz > 0 ? $" @ {m.RefreshHz} Hz" : "";
                string primary = m.Primary ? ", main" : "";

                lines.Add($"{m.Label ?? token} — {m.Width} x {m.Height}{rate} at {m.X},{m.Y}{primary}  ({state})");
            }

            string scope = preset.IncludeGlobal ? "Whole desk" : "Selected displays only";
            string notes = preset.CaptureNotes.Count == 0 ? "" : "\nCapture notes: " + string.Join("; ", preset.CaptureNotes);
            return scope + (preset.IncludeLayout ? " · restores layout" : " · keeps the current layout")
                + "\n" + string.Join("\n", lines) + notes;
        }
    }

    // ----------------------------------------------------------- app rules --

    public void AddRule()
    {
        var rule = new AppRule { RestorePrevious = true, Preset = Current?.Name ?? "" };
        _settings.AppRules.Add(rule);
        _persist();

        Rules.Add(new AppRuleViewModel(rule, _persist, PresetNames));
        Raise(nameof(RulesEmptyVisibility));
    }

    public void RemoveRule(AppRuleViewModel rule)
    {
        _settings.AppRules.Remove(rule.Rule);
        _persist();

        Rules.Remove(rule);
        Raise(nameof(RulesEmptyVisibility));
    }

    public Visibility RulesEmptyVisibility =>
        Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
