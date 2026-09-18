using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Umbra.Core.Displays;
using Umbra.Core.Presets;
using Umbra.Core.Settings;
using Umbra.Display.Presets;

namespace Umbra.App.ViewModels;

/// <summary>One app rule, as the list on the Presets page edits it.</summary>
public sealed class AppRuleViewModel(AppRule rule, Action persist) : INotifyPropertyChanged
{
    public AppRule Rule { get; } = rule;

    public string Process
    {
        get => Rule.Process;
        set { Rule.Process = value; persist(); Raise(); Raise(nameof(Summary)); }
    }

    public string Preset
    {
        get => Rule.Preset;
        set { Rule.Preset = value; persist(); Raise(); Raise(nameof(Summary)); }
    }

    public string RevertTo
    {
        get => Rule.RevertTo ?? "";
        set
        {
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
    private readonly Func<UmbraSettings> _settingsSource;

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

    private UmbraSettings _settings => _settingsSource();

    public PresetsViewModel(Func<UmbraSettings> settings, Func<IReadOnlyList<DisplayInfo>> displays,
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
    public ObservableCollection<AppRuleViewModel> Rules { get; } = [];

    private readonly List<Preset> _presets = [];
    private string? _selected;
    private string _status = "";
    private List<PresetChange> _differences = [];

    // ------------------------------------------------------------- listing --

    public void Reload()
    {
        _presets.Clear();
        _presets.AddRange(PresetStore.Load());

        Names.Clear();
        foreach (Preset p in _presets)
        {
            // A preset file literally named after the sentinel would otherwise
            // be impossible to select, because picking it would read as "start
            // a new one". Only reachable by creating the file by hand.
            if (p.Name == NewEntry) continue;
            Names.Add(p.Name);
        }

        Names.Add(NewEntry);

        if (_selected is not null && !Names.Contains(_selected)) _selected = null;
        _selected ??= _presets.Count > 0 ? _presets[0].Name : NewEntry;

        Rules.Clear();
        foreach (AppRule r in _settings.AppRules) Rules.Add(new AppRuleViewModel(r, _persist));

        Raise(nameof(Selected));
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
    /// Deliberately not the list of differences. In a bar it would be four
    /// lines of detail nobody reads in passing, and it would make the bar tall
    /// enough to be furniture. What belongs here is whether there is anything
    /// to save; the drift flyout on the Presets page spells it out,
    /// where there is room to read it.
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
            if (_selected == value) return;
            _selected = value;
            Raise();
            RefreshDrift();
                Raise(nameof(Details));
            Raise(nameof(Creating));
            Raise(nameof(CreatingVisibility));
            Raise(nameof(ExistingVisibility));
        Raise(nameof(ActionVisibility));
            Raise(nameof(ActionVisibility));
            Raise(nameof(SaveButtonText));
        }
    }

    private Preset? Current
    {
        get
        {
            if (_selected is null || _selected == NewEntry) return null;

            foreach (Preset p in _presets)
                if (p.Name == _selected) return p;

            return null;
        }
    }

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

        UmbraSettings settings = _settings;
        IReadOnlyList<DisplayInfo> displays = _displays();

        try
        {
            Preset live = await Task.Run(
                () => PresetService.Capture(saved.Name, displays, settings)).ConfigureAwait(true);

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
        Changes.Clear();
        foreach (PresetChange c in _differences) Changes.Add(c);

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
    public async Task<string> ApplyAsync()
    {
        if (Current is not Preset preset) return "No preset selected.";

        UmbraSettings settings = _settings;
        IReadOnlyList<DisplayInfo> displays = _displays();

        PresetResult result = await Task.Run(() => PresetService.Apply(preset, displays, settings));
        _persist();

        _refreshDisplays();
        RefreshDrift();

        if (!result.Ok) return string.Join("  ", result.Notes);

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
    public async Task<string> SaveAsync()
    {
        if (Current is not Preset preset) return "No preset selected.";

        Preset fresh = await CaptureAsync(preset.Name).ConfigureAwait(true);

        // The description belongs to the preset, not to the desk, so it
        // survives a re-capture.
        fresh.Description = preset.Description;

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
        UmbraSettings settings = _settings;
        IReadOnlyList<DisplayInfo> displays = _displays();

        return Task.Run(() => PresetService.Capture(name, displays, settings));
    }

    /// <summary>Captures the desk under a new name.</summary>
    public async Task<string> SaveAsAsync(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return "Give the preset a name first.";

        if (name == NewEntry) return "That name is reserved. Pick another.";

        // Checked against the file, not the display name: two names can
        // sanitise to one file, and comparing names alone would silently
        // overwrite a preset the user never mentioned.
        if (Names.Contains(name) || PresetStore.Exists(name))
            return $"“{name}” already exists — pick another name, or use Save to overwrite it.";

        PresetStore.Save(await CaptureAsync(name).ConfigureAwait(true));
        Reload();
        Selected = name;

        return $"Saved “{name}”.";
    }

    /// <summary>Puts the desk back to the selected preset, discarding the drift.</summary>
    public Task<string> DiscardAsync() => ApplyAsync();

    public string Rename(string name)
    {
        if (Current is not Preset preset) return "No preset selected.";

        name = name.Trim();
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

        PresetStore.Rename(preset.Name, name);
        Reload();
        Selected = name;

        return $"Renamed to “{name}”.";
    }

    public string Delete()
    {
        if (Current is not Preset preset) return "No preset selected.";

        PresetStore.Delete(preset.Name);
        _selected = null;
        Reload();

        return $"Deleted “{preset.Name}”.";
    }

    public string Export(string destination)
    {
        if (Current is not Preset preset) return "No preset selected.";

        PresetStore.Export(preset, destination);
        return $"Exported to {destination}.";
    }

    public string Import(string source)
    {
        string? name = PresetStore.Import(source);
        if (name is null) return "That file is not a preset Umbra can read.";

        Reload();
        Selected = name;

        return $"Imported as “{name}”.";
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

            return lines.Count == 0 ? "This preset has no monitors in it." : string.Join("\n", lines);
        }
    }

    // ----------------------------------------------------------- app rules --

    public void AddRule()
    {
        var rule = new AppRule();
        _settings.AppRules.Add(rule);
        _persist();

        Rules.Add(new AppRuleViewModel(rule, _persist));
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
