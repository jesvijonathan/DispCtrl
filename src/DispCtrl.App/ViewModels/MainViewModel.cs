using System.Text;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using DispCtrl.Display;
using DispCtrl.App.Services;
using DispCtrl.Core.Displays;
using DispCtrl.Display.Devices;
using DispCtrl.Core.Settings;

namespace DispCtrl.App.ViewModels;

public sealed partial class MainViewModel : INotifyPropertyChanged
{
    private readonly EngineController _engine = new();
    private DispCtrlSettings _settings = SettingsStore.Load();

    public ObservableCollection<DisplayViewModel> Displays { get; } = [];

    public MainViewModel()
    {
        Hotkeys = new HotkeysViewModel(() => _settings, Persist);
        if (PresetsEnabled)
        {
            DispatcherQueue ui = DispatcherQueue.GetForCurrentThread();
            DispCtrl.Display.Presets.PresetService.HardwareChanged += () => ui.TryEnqueue(ScheduleDriftCheck);
        }
        Refresh();
        StartSettingsSync();
    }

    /// <summary>The live display list, for anything that needs it after a rescan.</summary>
    private List<DisplayInfo> CurrentDisplays()
    {
        var result = new List<DisplayInfo>(Displays.Count);
        foreach (DisplayViewModel d in Displays) result.Add(d.Info);
        return result;
    }

    // ------------------------------------------------------------- displays --

    /// <summary>
    /// Re-reads the settings file and raises every derived property.
    /// </summary>
    /// <remarks>
    /// The file is shared state: the engine's CLI writes it too, and so does a
    /// second copy of this app. Holding a snapshot from startup means a later
    /// save writes stale values back over whatever changed in between, so the
    /// window reloads whenever it is activated.
    /// </remarks>
    private (DateTime Modified, long Length) _settingsStamp;
    private static (DateTime Modified, long Length) SettingsStamp()
    {
        var file = new FileInfo(SettingsStore.Path_);
        return file.Exists ? (file.LastWriteTimeUtc, file.Length) : default;
    }
    private Task _activationRefresh = Task.CompletedTask;
    private int _displayLoadVersion;

    private async Task RefreshExistingReadingsAsync()
    {
        ShowFooterStatus("Refreshing display information…", busy: true);
        try
        {
            await Task.WhenAll(Displays.Select(display => display.RefreshReadingsAsync()));
            ShowFooterStatus("Display information updated.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Display refresh failed: {ex.Message}");
            ShowFooterStatus($"Display refresh failed: {ex.Message}");
        }
        Raise(nameof(BrightnessSummary));
        if (PresetsEnabled) Presets.Reload();
    }

    public void ReloadFromDisk()
    {
        if (DisplayRegistry.CheapSignature() == _layoutSignature)
        {
            SyncExternalSettings();
            if (_activationRefresh.IsCompleted && Environment.TickCount64 - _lastReadingsAt > 5000)
            {
                _lastReadingsAt = Environment.TickCount64;
                _activationRefresh = RefreshExistingReadingsAsync();
            }
            return;
        }
        Refresh();
        Raise(nameof(HideDelayMs));
        Raise(nameof(AnimMs));
        Raise(nameof(AnimateTaskbar));
        Raise(nameof(AnimMsEnabled));
        Raise(nameof(RevealPx));
        Raise(nameof(Logging));
        RaiseProtectionSettings();
        RaiseAwakeSettings();
    }

    private FileSystemWatcher? _settingsWatcher;
    private System.Threading.Timer? _settingsDebounce;
    private long _lastReadingsAt;

    private void StartSettingsSync()
    {
        var ui = DispatcherQueue.GetForCurrentThread();
        Directory.CreateDirectory(SettingsStore.Directory);
        _settingsDebounce = new System.Threading.Timer(_ => ui.TryEnqueue(SyncExternalSettings));
        _settingsWatcher = new FileSystemWatcher(SettingsStore.Directory, "settings.json")
        { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
        _settingsWatcher.Changed += (_, _) => _settingsDebounce.Change(150, Timeout.Infinite);
        _settingsWatcher.Created += (_, _) => _settingsDebounce.Change(150, Timeout.Infinite);
        // A DispCtrl save renames a finished file over settings.json: whole, so
        // shown at once. In-place writes (an editor) wait out the debounce.
        _settingsWatcher.Renamed += (_, e) => _settingsDebounce.Change(
            string.Equals(e.Name, "settings.json", StringComparison.OrdinalIgnoreCase) ? 0 : 150, Timeout.Infinite);
        _settingsWatcher.Deleted += (_, _) => _settingsDebounce.Change(150, Timeout.Infinite);
        _settingsWatcher.EnableRaisingEvents = true;
    }

    private void SyncExternalSettings()
    {
        // A slider's pending value is only in memory; reloading over it would
        // undo it. Saving first merges it with whatever changed on disk.
        FlushPendingSave();
        var stamp = SettingsStamp();
        if (stamp == _settingsStamp) return;
        try
        {
            var incoming = SettingsStore.Load();
            // Active cards retain their settings object even when an external
            // reset removes that monitor from the file.
            foreach (var display in Displays) incoming.For(display.Token);
            SettingsStore.RefreshInPlace(_settings, incoming);
            _settingsStamp = stamp;
            foreach (var display in Displays) display.NotifySettingsReloaded();
            Raise(string.Empty);
            QuickPanelChanged?.Invoke();
        }
        catch (Exception ex) { ShowFooterStatus("Settings sync: " + ex.Message); }
    }

    /// <summary>
    /// The layout as it was when the display list was last built.
    /// </summary>
    /// <remarks>
    /// Compared against on a timer so a monitor plugged in or unplugged while
    /// the window is open is picked up on its own. Everything on the Displays
    /// page is discovered from the hardware — the modes offered, the scaling
    /// steps, and the monitor's own DDC/CI controls — so a stale list is not
    /// merely out of date, it offers settings for a panel that is no longer
    /// there.
    /// </remarks>
    private string _layoutSignature = "";

    /// <summary>
    /// Rebuilds the display list if, and only if, the layout actually changed.
    /// </summary>
    /// <remarks>
    /// The check reads GDI and DPI metadata — no CCD query, registry or DDC/CI — so it is cheap
    /// enough to run every couple of seconds. The rebuild behind it is not:
    /// it re-reads every monitor's capabilities over DDC/CI, which is seconds
    /// per panel. Hence the gate.
    /// <para>
    /// <paramref name="settled"/> is false from the window's own poll, which
    /// can land in the middle of an arrival: the new layout then has to be seen
    /// twice, a poll apart, before the list is rebuilt. The engine's signal and
    /// a summons of the panel come after the change is over.
    /// </para>
    /// </remarks>
    public void RefreshIfDisplaysChanged(bool settled = true)
    {
        string now = DisplayRegistry.CheapSignature();
        if (now == _layoutSignature) { _candidateSignature = now; return; }
        if (!settled && now != _candidateSignature) { _candidateSignature = now; return; }

        _layoutSignature = now;
        Refresh(hotplug: true);
    }

    private string _candidateSignature = "";

    public void Refresh() => Refresh(hotplug: false);

    /// <summary>Builds the display list again from the hardware.</summary>
    /// <param name="hotplug">
    /// A display arrived, left or changed. What the monitors said about
    /// themselves is kept, since a capabilities string describes the monitor,
    /// not the desk; Rescan asks them all again.
    /// </param>
    private void Refresh(bool hotplug)
    {
        FlushPendingSave();
        int loadVersion = ++_displayLoadVersion;
        ShowFooterStatus("Loading display information…", busy: true);
        _layoutSignature = _candidateSignature = DisplayRegistry.CheapSignature();
        _settings = SettingsStore.Load();
        _settingsStamp = SettingsStamp();
        DispCtrl.Display.Presets.PresetService.InvalidateHardware();
        var before = Displays.Select(d => d.Token).ToHashSet(StringComparer.Ordinal);
        if (!hotplug) foreach (DisplayViewModel existing in Displays) MonitorCapabilities.Forget(existing.Info);
        Displays.Clear();

        List<DisplayInfo> found = DisplayRegistry.Enumerate();

        // Built-in panel first, then left to right. Windows numbers displays by
        // an internal path order that has nothing to do with where they are, so
        // sorting here is what makes the numbering on the cards mean something.
        found.Sort((a, b) =>
        {
            if (a.IsInternal != b.IsInternal) return a.IsInternal ? -1 : 1;
            int byX = a.Bounds.Left.CompareTo(b.Bounds.Left);
            return byX != 0 ? byX : a.Bounds.Top.CompareTo(b.Bounds.Top);
        });

        for (int i = 0; i < found.Count; i++)
        {
            DisplayInfo d = found[i];
            MonitorSettings ms = _settings.For(d.Token);

            // Keep the human-readable label current, so the settings file stays
            // legible even though nothing matches on it.
            ms.Label = d.Label;

            Displays.Add(new DisplayViewModel(d, ms, _settings, i + 1, Persist, PersistSoon, () => PerDisplayWarmth, ScheduleDriftCheck));
        }

        ScalePreviews();
        RefreshTopology();
        RefreshArrangement();
        RefreshEngineStatus();

        // Whether Windows has a brightness slider to follow is only known once
        // the built-in panel has answered, which is after this returns.
        _ = Task.WhenAll(Displays.Select(d => d.BrightnessReady)).ContinueWith(_ =>
        {
            Raise(nameof(UnisonFollowsWindowsAvailable));
            Raise(nameof(UnisonFollowsWindowsDescription));
        }, TaskScheduler.FromCurrentSynchronizationContext());

        // The per-display brightness line is built from values the hardware has
        // not answered for yet — DDC/CI takes far longer than WMI — so it is
        // refreshed once every display has reported in, rather than showing
        // only whichever panel happened to be fast.
        _ = RefreshSummaryWhenReadyAsync(loadVersion);
        if (hotplug) _ = LookAgainAsync(loadVersion, Displays.Where(d => !before.Contains(d.Token)).ToList());
        Raise(nameof(HasDisplays));
        Raise(nameof(SeveralDisplays));
        Raise(nameof(UnisonDescription));
        Raise(nameof(UnisonSliderEnabled));
        if (PresetsEnabled) Presets.Reload();
        DisplaysRebuilt?.Invoke();
    }

    /// <summary>Raised once the display list has been built again, for views drawn from the whole set.</summary>
    public event Action? DisplaysRebuilt;

    public bool HasDisplays => Displays.Count > 0;

    /// <summary>
    /// Whether unison has anything to do: with one display, its own slider is
    /// the whole story.
    /// </summary>
    /// <remarks>
    /// Nothing is switched off when a monitor leaves - the settings describe
    /// the desk as it usually is, and unison takes up again, at the level it
    /// was left, the moment another display arrives.
    /// </remarks>
    public bool SeveralDisplays => Displays.Count > 1;

    public string UnisonDescription => SeveralDisplays
        ? "One control for every display, relative to the level each is already at."
        : "Only one display is connected, so its own brightness below is the one to use. Unison carries on from where it was left when another display is connected.";

    /// <summary>
    /// Reads a display that has just arrived again, a few seconds later, if it
    /// did not answer the first time.
    /// </summary>
    /// <remarks>
    /// A monitor's DDC/CI channel is not up when it enumerates, so the read
    /// made on arrival came back empty - no brightness, no controls - and
    /// stayed that way until Rescan. Monitorian scans again at widening
    /// intervals after a change for the same reason. Only arrivals, and only
    /// what came back empty: a monitor with no DDC/CI at all costs two more
    /// attempts, once.
    /// </remarks>
    private async Task LookAgainAsync(int loadVersion, List<DisplayViewModel> arrived)
    {
        foreach (int delayMs in (int[])[3000, 8000])
        {
            if (arrived.Count == 0) return;
            await Task.Delay(delayMs).ConfigureAwait(true);
            if (loadVersion != _displayLoadVersion) return;
            await Task.WhenAll(arrived.Select(d => d.ReadingsReady)).ConfigureAwait(true);
            arrived = arrived.Where(d => d.AnsweredNothing).ToList();
            foreach (DisplayViewModel d in arrived) _ = d.RefreshReadingsAsync();
        }
    }

    /// <summary>
    /// Sizes every preview against the physically largest panel.
    /// </summary>
    /// <remarks>
    /// Done here rather than per display because relative size is the point:
    /// a 14-inch laptop next to a 24-inch monitor should look like one, and
    /// neither view model can know that on its own.
    /// </remarks>
    private void ScalePreviews()
    {
        int widest = 0;
        foreach (DisplayViewModel d in Displays)
            widest = Math.Max(widest, d.Info.PhysicalWidthMm);

        foreach (DisplayViewModel d in Displays)
        {
            d.PreviewScale = widest > 0 && d.Info.PhysicalWidthMm > 0
                ? d.Info.PhysicalWidthMm / (double)widest
                : 1.0;
        }
    }

    // ------------------------------------------------------------ topology --

    private string _topologyText = string.Empty;
    private string _inactiveText = string.Empty;

    public string TopologyText => _topologyText;

    public string InactiveText => _inactiveText;

    public Visibility InactiveVisibility =>
        string.IsNullOrEmpty(_inactiveText) ? Visibility.Collapsed : Visibility.Visible;

    private void RefreshTopology()
    {
        _topologyText = DisplayRegistry.Topology() switch
        {
            DisplayRegistry.DisplayTopology.Single => "Single display",
            DisplayRegistry.DisplayTopology.Extended => $"Extended across {Displays.Count} displays",
            DisplayRegistry.DisplayTopology.Duplicated => "Duplicated on all displays",
            _ => "Mixed — some displays duplicated, some extended",
        };

        List<string> inactive = DisplayRegistry.InactiveDisplays();
        _connected = Displays.Count + inactive.Count;
        _inactiveText = inactive.Count == 0
            ? string.Empty
            : inactive.Count == 1
                ? $"{inactive[0]} is connected but switched off in Display settings."
                : $"{inactive.Count} connected displays are switched off in Display settings: {string.Join(", ", inactive)}.";

        Raise(nameof(TopologyText));
        Raise(nameof(InactiveText));
        Raise(nameof(InactiveVisibility));
        Raise(nameof(ArrangementsApply));
        Raise(nameof(ArrangementsDescription));
    }

    private int _connected;

    /// <summary>
    /// Whether there is a second display to extend to, duplicate on or switch to.
    /// </summary>
    /// <remarks>
    /// Counts displays connected but switched off too: "PC screen only" with a
    /// monitor plugged in is exactly when Extend is wanted.
    /// </remarks>
    public bool ArrangementsApply => _connected > 1;

    public string ArrangementsDescription => ArrangementsApply
        ? "How the desktop is spread across your monitors."
        : "Only one display is connected. Extend, duplicate and the rest come back when another is plugged in.";

    /// <summary>
    /// Writes settings out. The engine watches this file, so a toggle takes
    /// effect without restarting or otherwise signalling it.
    /// </summary>
    private DispatcherQueueTimer? _saveTimer;
    private long _saveDueBy;
    private const int SaveSettleMs = 100, SaveAtMostEveryMs = 300;

    /// <summary>
    /// Saves soon, once, for values a slider sets.
    /// </summary>
    /// <remarks>
    /// A drag moves a slider a step at a time, and each step used to be a whole
    /// save on the UI thread: serialise, re-read and merge the file, write, and
    /// rename under the cross-process lock. The engine then reloaded and
    /// re-applied every service for each of them. Coalesced, a drag saves at
    /// most every 300 ms and once more 100 ms after it stops, which also lets
    /// the engine follow a long drag it previously only caught up with once the
    /// pointer paused. Anything that calls <see cref="Persist"/> flushes the
    /// pending save with its own, and closing the window flushes it.
    /// </remarks>
    private void PersistSoon()
    {
        long now = Environment.TickCount64;
        if (_saveTimer is null)
        {
            _saveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += (_, _) => Persist();
        }
        if (!_saveTimer.IsRunning) _saveDueBy = now + SaveAtMostEveryMs;
        _saveTimer.Stop();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_saveDueBy - now, 0, SaveSettleMs));
        _saveTimer.Start();
    }

    /// <summary>Writes a save a slider left pending, before the process can go.</summary>
    public void FlushPendingSave()
    {
        if (_saveTimer?.IsRunning == true) Persist();
    }

    private void Persist()
    {
        _saveTimer?.Stop();
        // Setters bound to sliders call this, so an exception here comes out of
        // a XAML callback and ends the process. The value stays in memory and
        // goes out with the next save.
        try { SettingsStore.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            ShowFooterStatus("Settings could not be saved yet: " + ex.Message);
            return;
        }
        if (SettingsStore.HasExternalChanges(_settings))
        {
            _settingsStamp = default;
            _settingsDebounce?.Change(150, Timeout.Infinite);
        }
        else _settingsStamp = SettingsStamp();
        ShowFooterStatus("Settings saved.");

        // Per-display power features are saved through DisplayViewModel rather
        // than the shared protection setters, so they must be able to bring the
        // resident engine up on their own.
        if (_settings.Global.OledCare.Enabled
            || _settings.Global.Awake.ActiveAt(DateTimeOffset.UtcNow)
            || _settings.Monitors.Values.Any(monitor => monitor.MonitorSleepEnabled
                || monitor.OledRestUntilUtc > DateTimeOffset.UtcNow))
            _engine.Start();

        // Settings-driven changes count as desk changes too — night light and
        // taskbar hiding live here rather than in the hardware.
        ScheduleDriftCheck();

        // Marking a panel as OLED, or turning its protection off, changes which
        // displays the shared rest settings cover. That is stated on the card
        // above them, and it lands here.
        Raise(nameof(OledCoverage));
    }

    /// <summary>
    /// How long the desk must sit still before the preset drift is re-checked.
    /// </summary>
    /// <remarks>
    /// A drift check reads every display's hardware, DDC/CI included, so it
    /// cannot run per change — dragging a brightness slider raises a change per
    /// pixel. Debounced instead: the check runs once the user has stopped, and
    /// the banner catches up a moment later rather than fighting the drag.
    /// </remarks>
    private static readonly TimeSpan DriftSettle = TimeSpan.FromMilliseconds(1200);

    private DispatcherQueueTimer? _driftTimer;

    public void ScheduleDriftCheck()
    {
        if (!PresetsEnabled) return;
        DispatcherQueue? ui = DispatcherQueue.GetForCurrentThread();
        if (ui is null) return;

        _driftTimer ??= ui.CreateTimer();
        _driftTimer.Interval = DriftSettle;
        _driftTimer.IsRepeating = false;

        // Re-hooked each time rather than once: the timer is created lazily, and
        // a handler added per schedule would fire once per change after the
        // first, which is exactly the storm the debounce exists to prevent.
        _driftTimer.Stop();
        _driftTimer.Tick -= OnDriftTick;
        _driftTimer.Tick += OnDriftTick;
        _driftTimer.Start();
    }

    private void OnDriftTick(DispatcherQueueTimer sender, object args) => Presets.RefreshDrift();

    // --------------------------------------------------------------- engine --

    private EngineStatus _status = EngineStatus.Stopped;
    private bool? _engineRequestedState;
    private DateTime _engineChangeDeadline;

    private string _footerStatus = "";
    private bool _footerBusy;
    private CancellationTokenSource? _footerClear;

    public string FooterStatus => _footerStatus;
    public bool FooterBusy => _footerBusy;
    public Visibility FooterBusyVisibility => _footerBusy ? Visibility.Visible : Visibility.Collapsed;

    public void ShowFooterStatus(string message, bool busy = false)
    {
        _footerClear?.Cancel();
        _footerClear?.Dispose();
        _footerClear = null;

        _footerStatus = string.IsNullOrWhiteSpace(message) ? "" : message;
        _footerBusy = busy;
        Raise(nameof(FooterStatus));
        Raise(nameof(FooterBusy));
        Raise(nameof(FooterBusyVisibility));

        if (busy || _footerStatus.Length == 0) return;

        var clear = new CancellationTokenSource();
        _footerClear = clear;
        _ = ClearFooterLaterAsync(clear);
    }

    private async Task ClearFooterLaterAsync(CancellationTokenSource clear)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), clear.Token);
            _dispatcher.TryEnqueue(() =>
            {
                if (_footerClear != clear) return;
                _footerClear.Dispose();
                _footerClear = null;
                _footerStatus = "";
                Raise(nameof(FooterStatus));
            });
        }
        catch (OperationCanceledException) { }
    }

    public bool EngineRunning => _status.Running;

    public string EngineStateText => _status.Running ? "Running" : "Stopped";

    public string EngineDetailText => _status.Running
        ? $"Process {_status.ProcessId}  ·  {_status.MemoryMb:N1} MB"
        : _engine.EnginePath is null
            ? "Engine executable not found — build DispCtrl.Engine first."
            : "Taskbars are not being managed.";

    public bool CanToggleEngine => _engine.EnginePath is not null && _engineRequestedState is null;

    public string EnginePath => _engine.EnginePath ?? "not found";

    public string LogPath => SettingsStore.LogPath;

    public string SettingsPath => SettingsStore.Path_;

    public void RefreshEngineStatus()
    {
        EngineStatus next = EngineController.Query();
        if (next == _status)
        {
            if (_engineRequestedState is not null && DateTime.UtcNow >= _engineChangeDeadline)
            {
                bool wanted = _engineRequestedState.Value;
                _engineRequestedState = null;
                Raise(nameof(CanToggleEngine));
                ShowFooterStatus(wanted ? "The engine did not start." : "The engine did not stop safely.");
            }
            return;
        }

        bool wasRunning = _status.Running;
        _status = next;
        if (_engineRequestedState == next.Running)
        {
            _engineRequestedState = null;
            Raise(nameof(CanToggleEngine));
        }
        Raise(nameof(EngineRunning));
        Raise(nameof(EngineStateText));
        Raise(nameof(EngineDetailText));
        if (wasRunning != next.Running)
            ShowFooterStatus(next.Running ? "Engine started." : "Engine stopped.");
    }

    /// <summary>
    /// The Presets page's state.
    /// </summary>
    /// <remarks>
    /// Hung off the shared view model rather than created per navigation, so
    /// the selected preset survives leaving the page and coming back. A picker
    /// that forgets what was selected is not a picker anyone trusts.
    /// </remarks>
    public bool PresetsEnabled => DispCtrl.Core.FeatureFlags.Presets;
    private PresetsViewModel? _presets;
    // x:Bind reads this even when the footer is not loaded. The disabled model
    // stays empty and performs no disk reads, captures or background work.
    public PresetsViewModel Presets => _presets ??=
        new PresetsViewModel(() => _settings, CurrentDisplays, Persist, Refresh);

    /// <summary>The Hotkeys page's state, shared for the same reason presets are.</summary>
    public HotkeysViewModel Hotkeys { get; }

    // ---------------------------------------------------- unison brightness --

    /// <summary>
    /// Drives every display's brightness from one relative control.
    /// </summary>
    /// <remarks>
    /// Switching it on captures each display's current level as its baseline,
    /// so the relation between panels is the one already dialled in. From then
    /// on the slider is a multiplier on those baselines, never an absolute
    /// value — which is what makes one control meaningful across panels whose
    /// peak brightness differs by a factor of several.
    /// </remarks>
    public bool UnisonBrightness
    {
        get => _settings.Global.UnisonBrightness;
        set
        {
            if (_settings.Global.UnisonBrightness == value) return;
            _settings.Global.UnisonBrightness = value;

            Persist();
            Raise();
            Raise(nameof(UnisonVisibility));
            Raise(nameof(UnisonSliderEnabled));
            Raise(nameof(AmbientStatus));

            if (value && !Calibrating) _ = CaptureBaselinesAsync();
        }
    }

    /// <summary>
    /// Records every display's current level before unison takes over.
    /// </summary>
    /// <remarks>
    /// Sequenced through each display's own readiness rather than assuming the
    /// values are already in hand. The two brightness backends differ in speed
    /// by orders of magnitude, so capturing synchronously reads zero for any
    /// display whose hardware has not answered yet — and a baseline of zero can
    /// never be scaled back up.
    /// </remarks>
    /// <remarks>
    /// Switching unison off leaves every display wherever it is, free to be set
    /// by hand. Switching it on brings them all back to where unison puts them:
    /// the slider stays where it was left, each display keeps the baseline it
    /// had, and each is set to that baseline at that level. Only a display that
    /// has never had a baseline gets one, worked back from where it is now so it
    /// joins without a jump. See <see cref="UnisonResume"/>.
    /// </remarks>
    private async Task CaptureBaselinesAsync()
    {
        var driven = new List<DisplayViewModel>();
        var current = new List<int>();
        var baselines = new List<int>();

        foreach (DisplayViewModel d in Displays)
        {
            // A display with captured limits is driven across its own range,
            // not against a baseline, so it has nothing to remember.
            if (d.UsesBrightnessRange) continue;

            int? now = await d.ReadBrightnessAsync();
            if (now is null) continue;

            driven.Add(d);
            current.Add(now.Value);
            baselines.Add(d.BrightnessBaseline);
        }

        (int level, int[] kept) = UnisonResume.Enable(
            _settings.Global.UnisonLevel, current, baselines, (int)Math.Round(UnisonMinimum));

        for (int i = 0; i < driven.Count; i++)
            if (driven[i].BrightnessBaseline != kept[i]) driven[i].BrightnessBaseline = kept[i];

        _settings.Global.UnisonLevel = level;
        Persist();
        Raise(nameof(UnisonLevel));
        Raise(nameof(UnisonPercentText));

        await ApplyUnisonAsync(level / 100.0);
    }

    // ------------------------------------------- Windows' brightness drives unison --

    /// <summary>
    /// Whether Windows' own brightness slider and keys drive unison.
    /// </summary>
    /// <remarks>
    /// The engine does the following, because the keys must work with the app
    /// closed. It reads the built-in panel's brightness back through that
    /// panel's own unison range, so the keys move unison within the calibrated
    /// limits: the built-in panel never leaves its range, and every display
    /// stays in step with it.
    /// </remarks>
    public bool UnisonFollowsWindows
    {
        get => _settings.Global.UnisonFollowsWindows;
        set
        {
            if (_settings.Global.UnisonFollowsWindows == value) return;
            _settings.Global.UnisonFollowsWindows = value;
            Persist();

            // The engine listens; without it the switch would sit there doing nothing.
            if (value)
            {
                try { _engine.Start(); }
                catch (Exception) { }

                // Puts the built-in panel inside its range before the first key press.
                if (UnisonBrightness && !Calibrating) _ = ApplyUnisonAsync(_settings.Global.UnisonLevel / 100.0);
            }

            Raise();
        }
    }

    /// <summary>Whether this machine has a Windows brightness slider to follow.</summary>
    public bool UnisonFollowsWindowsAvailable =>
        Displays.Any(d => d.IsInternalPanel && d.BrightnessSupported);

    public string UnisonFollowsWindowsDescription => UnisonFollowsWindowsAvailable
        ? "The brightness slider in Windows' Quick Settings and the keyboard's brightness keys move unison instead of the built-in screen alone. Every display, the built-in screen included, stays within its own unison range, so calibrated limits hold. Works with DispCtrl closed, through the engine."
        : "Windows only offers a brightness slider for a built-in screen, and none was found, so there is nothing to follow.";

    public Visibility UnisonVisibility =>
        _settings.Global.UnisonBrightness ? Visibility.Visible : Visibility.Collapsed;

    // ----------------------------------------------------- gamma range --

    /// <summary>
    /// Whether Windows will accept a full-range gamma ramp on this machine.
    /// </summary>
    /// <remarks>
    /// Offered rather than assumed. It is one machine-wide registry value that
    /// affects every application setting a gamma ramp, so it is the user's
    /// decision — but without it warmth stops at 3300K and software dimming at
    /// 50%, and the two compete for what is left.
    /// <para>
    /// A translucent overlay window per display would avoid the registry, and
    /// was rejected: DWM composites it every frame, it appears in screen
    /// recordings, and it has to be fought with full-screen exclusive apps. One
    /// elevated write costs nothing afterwards.
    /// </para>
    /// </remarks>
    public bool GammaRangeUnlocked => NightLight.FullRange;

    public string GammaRangeDetail => NightLight.FullRange
        ? "Unlocked. Night light reaches 1900K and software dimming goes down to near-black."
        : "Windows limits how far a gamma ramp may stray, which caps night light at "
          + "3300K and software dimming at 50% — and the two share that range, so warmth "
          + "eats into dimming. Lifting the limit is one machine-wide registry value; it "
          + "needs administrator rights once, and affects any app that sets a gamma ramp.";

    public string GammaRangeButtonText => NightLight.FullRange ? "Restore the limit" : "Lift the limit";

    /// <summary>Asks for elevation, then re-reads what the machine now allows.</summary>
    public async Task ToggleGammaRangeAsync()
    {
        bool unlock = !NightLight.FullRange;

        await Task.Run(() => GammaRange.Write(unlock));
        NightLight.Recheck();

        Raise(nameof(GammaRangeUnlocked));
        Raise(nameof(GammaRangeDetail));
        Raise(nameof(GammaRangeButtonText));

        // Every dimming floor moves with it.
        foreach (DisplayViewModel d in Displays) d.RaiseNightLight();
    }


    // ------------------------------------------------------------ night light --

    /// <summary>The lowest warmth worth storing. See <see cref="NightLightStrength"/>.</summary>
    private const int MinStrength = 5;

    private NightLightSettings Night => _settings.Global.NightLight;

    /// <summary>
    /// Warms every display together.
    /// </summary>
    /// <remarks>
    /// The engine owns the schedule, because a schedule that only runs while
    /// the panel is open is not one. The panel writes the setting and applies
    /// the same warmth immediately, so the slider responds while you are
    /// looking at it rather than up to a tick later.
    /// </remarks>
    public bool NightLightEnabled
    {
        get => Night.Enabled;
        set
        {
            if (Night.Enabled == value) return;
            Night.Enabled = value;

            // Recover from a settings file that already carries a zero, written
            // before the floor above existed.
            if (value && Night.Strength < MinStrength) Night.Strength = new NightLightSettings().Strength;

            Persist();
            Raise();
            RaiseNightLight();
            ApplyNightLightPreview();
        }
    }

    /// <summary>
    /// Windows' dark mode, read from the system rather than remembered here.
    /// </summary>
    /// <remarks>
    /// There is no DispCtrl copy of this and there should not be: dark mode
    /// belongs to Windows, and a second stored answer could disagree with it.
    /// Reading it live is what keeps the switch honest whatever changed it —
    /// this page, Windows' own Settings, or a theme.
    /// <para>
    /// The app sets no <c>RequestedTheme</c>, so it follows the system like any
    /// other app, and flipping this repaints DispCtrl along with the desktop.
    /// </para>
    /// </remarks>
    public bool DarkMode
    {
        get => WindowsTheme.IsDark ?? false;
        set
        {
            if ((WindowsTheme.IsDark ?? false) == value) return;

            WindowsTheme.SetDark(value);
            Raise();
        }
    }

    /// <summary>Re-reads dark mode, for when something outside this app changed it.</summary>
    public void RaiseTheme() => Raise(nameof(DarkMode));

    /// <summary>
    /// Treat this toggle and Windows' own night light as one switch.
    /// </summary>
    /// <remarks>
    /// With it on, Windows does the warming and DispCtrl stops writing a warm
    /// ramp of its own — one ramp, one writer. Switching it off hands the ramp
    /// back, which is what per-display and calibrated warmth need.
    /// </remarks>
    public bool NightLightFollowsWindows
    {
        get => Night.FollowWindows;
        set
        {
            if (Night.FollowWindows == value) return;
            Night.FollowWindows = value;
            Persist();
            Raise();
            RaiseNightLight();
            ApplyNightLightPreview();
        }
    }

    /// <summary>
    /// How warm, as a percentage of the usable range.
    /// </summary>
    /// <remarks>
    /// Floored at <see cref="MinStrength"/> rather than zero. Zero warmth is
    /// indistinguishable from night light being off, so it is not a setting
    /// anyone wants — and it is exactly what a two-way slider writes back the
    /// moment it is realised, before binding has pushed the real value in. That
    /// is how switching night light on used to silently set it to 0%.
    /// </remarks>
    public double NightLightStrength
    {
        get => Night.Strength;
        set
        {
            int v = Math.Clamp((int)value, MinStrength, 100);
            if (Night.Strength == v) return;
            Night.Strength = v;
            PersistSoon();
            Raise();
            RaiseNightLight();
            ApplyNightLightPreview();
        }
    }

    public bool NightLightScheduled
    {
        get => Night.Scheduled;
        set
        {
            if (Night.Scheduled == value) return;
            Night.Scheduled = value;
            Persist();
            Raise();
            RaiseNightLight();
            ApplyNightLightPreview();
        }
    }

    public TimeSpan NightLightFrom
    {
        get => TimeSpan.FromMinutes(Night.FromMinutes);
        set
        {
            int v = (int)value.TotalMinutes;
            if (Night.FromMinutes == v) return;
            Night.FromMinutes = v;
            Persist();
            Raise();
            RaiseNightLight();
            ApplyNightLightPreview();
        }
    }

    public TimeSpan NightLightTo
    {
        get => TimeSpan.FromMinutes(Night.ToMinutes);
        set
        {
            int v = (int)value.TotalMinutes;
            if (Night.ToMinutes == v) return;
            Night.ToMinutes = v;
            Persist();
            Raise();
            RaiseNightLight();
            ApplyNightLightPreview();
        }
    }

    /// <summary>One warmth for the desk, or one per display.</summary>
    public bool NightLightUnison
    {
        get => Night.Unison;
        set
        {
            if (Night.Unison == value) return;
            Night.Unison = value;
            Persist();
            Raise();
            RaiseNightLight();
        }
    }

    /// <summary>Run the shared slider between each display's captured limits.</summary>
    public bool NightLightCalibrated
    {
        get => Night.Calibrated;
        set
        {
            if (Night.Calibrated == value) return;
            Night.Calibrated = value;
            Persist();
            Raise();

            if (value) BeginWarmthCalibration();
            else ClearWarmthCalibration();

            RaiseNightLight();
        }
    }

    private CalibrationStep _warmthStep = CalibrationStep.None;

    public void BeginWarmthCalibration()
    {
        _warmthStep = CalibrationStep.Lower;
        RaiseNightLight();
    }

    public void CancelWarmthCalibration()
    {
        _warmthStep = CalibrationStep.None;
        RaiseNightLight();
    }

    private void ClearWarmthCalibration()
    {
        _warmthStep = CalibrationStep.None;
        foreach (DisplayViewModel d in Displays) d.ClearWarmthLimits();
    }

    /// <summary>
    /// Takes each display's current warmth as the limit being asked for.
    /// </summary>
    /// <remarks>
    /// The per-display sliders are shown for the duration whatever unison says,
    /// because the whole point of the step is setting the panels to look alike
    /// by eye — which cannot be done through one shared control.
    /// </remarks>
    public void CaptureWarmthLimits()
    {
        if (_warmthStep == CalibrationStep.None) return;

        bool upper = _warmthStep == CalibrationStep.Upper;
        foreach (DisplayViewModel d in Displays) d.CaptureWarmthLimit(upper);

        if (upper)
        {
            _warmthStep = CalibrationStep.None;
            Night.Strength = 100;
            Persist();
            Raise(nameof(NightLightStrength));
        }
        else
        {
            _warmthStep = CalibrationStep.Upper;
        }

        RaiseNightLight();
    }

    public bool CalibratingWarmth => _warmthStep != CalibrationStep.None;

    public Visibility WarmthStepVisibility =>
        CalibratingWarmth ? Visibility.Visible : Visibility.Collapsed;

    public Visibility WarmthLimitsVisibility =>
        Night.Enabled && Night.Unison && Night.Calibrated && !CalibratingWarmth
            ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NightLightCalibrateVisibility =>
        Night.Enabled && Night.Unison ? Visibility.Visible : Visibility.Collapsed;

    public string WarmthStepHeader => _warmthStep switch
    {
        CalibrationStep.Lower => "Step 1 of 2 — the least warmth you want",
        CalibrationStep.Upper => "Step 2 of 2 — the most warmth you want",
        _ => "Calibrated warmth",
    };

    public string WarmthStepPrompt => _warmthStep switch
    {
        CalibrationStep.Lower =>
            "Each display now has its own warmth slider below. Set them so the screens look equally "
            + "warm at the gentlest setting you would use, then capture.",
        CalibrationStep.Upper =>
            "Now set them so they look equally warm at the strongest setting you would use, then capture.",
        _ => "",
    };

    public string WarmthStepButton =>
        _warmthStep == CalibrationStep.Upper ? "Capture the most warmth" : "Capture the least warmth";

    public string WarmthLimitsSummary
    {
        get
        {
            var parts = new List<string>(Displays.Count);
            foreach (DisplayViewModel d in Displays) parts.Add(d.WarmthRangeSummary);
            return parts.Count == 0 ? "No displays" : string.Join("  |  ", parts);
        }
    }

    /// <summary>
    /// Whether each display shows its own warmth slider right now.
    /// </summary>
    /// <remarks>
    /// True in per-display mode, and also mid-calibration whatever the mode —
    /// see <see cref="CaptureWarmthLimits"/>.
    /// </remarks>
    public bool PerDisplayWarmth => Night.PerDisplayApplies || (Night.Enabled && CalibratingWarmth);

    public Visibility NightLightVisibility =>
        Night.Enabled ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The shared slider only means anything in unison mode.</summary>
    public Visibility NightLightSharedVisibility =>
        Night.SharedApplies ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NightLightScheduleVisibility =>
        Night.Enabled && Night.Scheduled ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Warmth as a colour temperature, which is how it is normally described.</summary>
    /// <summary>What the strength slider spans, which depends on who is warming.</summary>
    /// <remarks>
    /// The gamma clamp is DispCtrl's limit, not Windows', so quoting it while
    /// Windows is doing the warming would explain a restriction that is not in
    /// force and understate how warm the slider actually goes.
    /// </remarks>
    public string NightLightStrengthDetail => Night.FollowWindows
        ? "How far towards warm. Windows applies this, and runs the whole desk from 6500K down to 1200K."
        : "How far towards warm. The top of the slider is as warm as Windows will accept a gamma change; past that it refuses the ramp outright.";

    /// <summary>The warmth as a percentage, and the temperature it lands on.</summary>
    /// <remarks>
    /// The two scales do not agree, so the figure has to follow whichever is
    /// actually doing the warming: Windows runs 6500K to 1200K, DispCtrl 6500K to
    /// 3300K, or to 1900K with the clamp lifted. The same percentage is a
    /// different colour on each, and quoting the wrong one would make the panel
    /// state a temperature the screen is not at.
    /// </remarks>
    public string NightLightStrengthText
    {
        get
        {
            double kelvin = Night.FollowWindows
                ? WindowsNightLight.KelvinFor(Night.Strength)
                : NightLight.KelvinFor(Night.Strength);

            return $"{Night.Strength}%  ·  {kelvin:0}K";
        }
    }

    /// <summary>Whether the warmth is in force right now, and why or why not.</summary>
    public string NightLightStatus
    {
        get
        {
            if (!Night.Enabled) return "Off. Displays show their own colour.";

            // The engine applies it; saying so beats a screen that stays cold
            // with the toggle sitting on.
            if (!EngineRunning)
                return "The engine is not running, so nothing is being applied. Start it on the Engine page.";

            if (!Night.Scheduled) return "On now, and staying on until you switch it off.";

            string from = Clock(Night.FromMinutes);
            string to = Clock(Night.ToMinutes);

            return Night.ActiveAt(DateTime.Now)
                ? $"On now — inside the {from} to {to} window."
                : $"Waiting. Comes on at {from}, off at {to}.";
        }
    }

    private static string Clock(int minutes) =>
        $"{((minutes / 60) % 24):00}:{(minutes % 60):00}";

    /// <summary>
    /// The panel deliberately does not touch the gamma ramp.
    /// </summary>
    /// <remarks>
    /// It did, as a live preview, and that was a bug rather than a feature. A
    /// display has exactly one gamma ramp and no way to say who set it, so two
    /// processes writing it means whichever reads second captures the other's
    /// warmth as the display's own baseline — and restores to that. Switching
    /// night light off then left the screens permanently tinted, a little
    /// further every cycle.
    /// <para>
    /// So the engine owns the ramp outright. The panel writes the setting, the
    /// engine's watcher picks it up within about a tenth of a second, and the
    /// screen changes while the slider is still under the cursor.
    /// </para>
    /// </remarks>
    private void ApplyNightLightPreview() => RefreshEngineStatus();

    private void RaiseNightLight()
    {
        Raise(nameof(NightLightVisibility));
        Raise(nameof(NightLightSharedVisibility));
        Raise(nameof(NightLightScheduleVisibility));
        Raise(nameof(NightLightStrengthText));
        Raise(nameof(NightLightStrengthDetail));
        Raise(nameof(NightLightStatus));
        Raise(nameof(NightLightCalibrateVisibility));
        Raise(nameof(CalibratingWarmth));
        Raise(nameof(WarmthStepVisibility));
        Raise(nameof(WarmthLimitsVisibility));
        Raise(nameof(WarmthStepHeader));
        Raise(nameof(WarmthStepPrompt));
        Raise(nameof(WarmthStepButton));
        Raise(nameof(WarmthLimitsSummary));
        Raise(nameof(PerDisplayWarmth));

        foreach (DisplayViewModel d in Displays) d.RaiseNightLight();
    }

    // ----------------------------------------------- calibrated unison range --

    /// <summary>Which limit the calibration walkthrough is currently asking for.</summary>
    public enum CalibrationStep
    {
        None,
        Lower,
        Upper,
    }

    private CalibrationStep _step = CalibrationStep.None;
    private int _calibrationGeneration;
    private bool _capturingLimits;
    private Task _calibrationEndpoint = Task.CompletedTask;

    /// <summary>
    /// Runs unison between per-display limits instead of scaling one captured
    /// level.
    /// </summary>
    /// <remarks>
    /// Switching it on starts the walkthrough rather than taking effect at
    /// once: the limits have to come from the user physically deciding how dim
    /// and how bright each panel should go, and there is nothing DispCtrl can read
    /// off the hardware that would stand in for that judgement.
    /// </remarks>
    public bool UnisonCalibrated
    {
        get => _settings.Global.UnisonCalibrated;
        set
        {
            if (_settings.Global.UnisonCalibrated == value) return;
            _settings.Global.UnisonCalibrated = value;
            Persist();
            Raise();

            if (value) BeginCalibration();
            else ClearCalibration();

            RaiseCalibration();
        }
    }

    /// <summary>Starts, or restarts, the two-step walkthrough.</summary>
    public void BeginCalibration()
    {
        UnisonCalibration.SetActive(true);
        _step = CalibrationStep.Lower;
        _calibrationEndpoint = RememberThenLowerAsync();
        RaiseCalibration();
    }

    /// <summary>Where the desk was before the walkthrough moved it, for Cancel.</summary>
    private (int Level, List<(DisplayViewModel Display, int Percent)> Displays)? _beforeCalibration;

    private async Task RememberThenLowerAsync()
    {
        var levels = new List<(DisplayViewModel, int)>();
        foreach (DisplayViewModel d in Displays.ToArray())
            if (await d.ReadBrightnessAsync() is int percent) levels.Add((d, percent));
        _beforeCalibration = (_settings.Global.UnisonLevel, levels);
        // Cancelled while reading: starting the endpoint now would take a new
        // generation and strand the cancel's restore behind it.
        if (_step == CalibrationStep.Lower) await SetCalibrationEndpointAsync(false);
    }

    private void ClearCalibration()
    {
        _step = CalibrationStep.None;
        _beforeCalibration = null;
        _ = ReleaseCalibrationAsync(++_calibrationGeneration);
        foreach (DisplayViewModel d in Displays) d.ClearLimits();
    }

    /// <summary>Abandons the walkthrough, leaving any limits already captured.</summary>
    /// <remarks>
    /// The walkthrough drove every display to an endpoint and the slider with
    /// it. Cancelling used to leave them there, so backing out of Recalibrate
    /// left the desk at its dimmest with the slider on zero.
    /// </remarks>
    public void CancelCalibration()
    {
        _step = CalibrationStep.None;
        int generation = ++_calibrationGeneration;
        _ = RestoreThenReleaseAsync(generation);
        RaiseCalibration();
    }

    private async Task RestoreThenReleaseAsync(int generation)
    {
        try
        {
            try { await _calibrationEndpoint; }
            catch (Exception) { }
            if (_beforeCalibration is not { } before || generation != _calibrationGeneration) return;
            _beforeCalibration = null;
            _settings.Global.UnisonLevel = before.Level;
            Persist();
            Raise(nameof(UnisonLevel));
            Raise(nameof(UnisonPercentText));
            foreach ((DisplayViewModel display, int percent) in before.Displays)
                if (Displays.Contains(display)) await display.SetBrightnessAsync(percent);
        }
        catch (Exception ex) { ShowFooterStatus("Calibration: " + ex.Message); }
        finally { await ReleaseCalibrationAsync(generation); }
    }

    /// <summary>
    /// Takes every display's current level as the limit being asked for, then
    /// moves the walkthrough on.
    /// </summary>
    /// <remarks>
    /// Captures all displays at once rather than one at a time, because the
    /// point of the step is a whole-desk judgement: the user looks at both
    /// panels together, gets them to a matched dimness, and that pairing is
    /// what makes the single slider mean the same thing on both afterwards.
    /// </remarks>
    public async Task CaptureLimitsAsync()
    {
        if (_step == CalibrationStep.None || _capturingLimits) return;

        int generation = _calibrationGeneration;
        _capturingLimits = true;
        try
        {
        await _calibrationEndpoint;
        await Task.WhenAll(Displays.Select(d => d.WaitForBrightnessWriteAsync()));
        if (generation != _calibrationGeneration || !Calibrating) return;

        bool upper = _step == CalibrationStep.Upper;

        var pending = new List<Task>(Displays.Count);
        foreach (DisplayViewModel d in Displays) pending.Add(d.CaptureLimitAsync(upper));
        await Task.WhenAll(pending);
        if (generation != _calibrationGeneration || !Calibrating) return;

        if (upper)
        {
            _step = CalibrationStep.None;
            _beforeCalibration = null;
            _ = ReleaseCalibrationAsync(++_calibrationGeneration);

            // The displays are sitting at their ceilings right now, so the
            // slider belongs at the top of its travel. Writing the value
            // directly skips the apply a setter would trigger — there is
            // nothing to change, and a redundant DDC/CI round trip here is
            // visible as a flicker.
            _settings.Global.UnisonLevel = 100;
            Persist();
            Raise(nameof(UnisonLevel));
            Raise(nameof(UnisonPercentText));
        }
        else
        {
            _step = CalibrationStep.Upper;
            _calibrationEndpoint = SetCalibrationEndpointAsync(true);
        }

        RaiseCalibration();
        }
        catch (Exception ex) { ShowFooterStatus("Calibration: " + ex.Message); }
        finally { _capturingLimits = false; }
    }

    public bool Calibrating => _step != CalibrationStep.None;

    public bool UnisonSliderEnabled => UnisonBrightness && !Calibrating && SeveralDisplays;

    private async Task ReleaseCalibrationAsync(int generation)
    {
        try
        {
            await Task.WhenAll(Displays.Select(d => d.WaitForBrightnessWriteAsync()));
            // WMI can deliver a brightness notification just after a write completes.
            await Task.Delay(500);
        }
        finally
        {
            if (generation == _calibrationGeneration && !Calibrating) UnisonCalibration.SetActive(false);
        }
    }

    private async Task SetCalibrationEndpointAsync(bool upper)
    {
        int generation = ++_calibrationGeneration;
        try
        {
        _settings.Global.UnisonLevel = upper ? 100 : 0;
        Persist();
        Raise(nameof(UnisonLevel));
        Raise(nameof(UnisonPercentText));
        foreach (DisplayViewModel display in Displays.ToArray())
        {
            await display.BrightnessReady;
            if (_calibrationGeneration != generation || !Calibrating) return;
            int endpoint = upper ? display.BrightnessCeiling : display.BrightnessFloor;
            await display.SetBrightnessAsync(endpoint >= 0 ? endpoint : upper ? 100 : 0);
        }
        await Task.WhenAll(Displays.Select(d => d.WaitForBrightnessWriteAsync()));
        }
        catch (Exception ex) { ShowFooterStatus("Calibration: " + ex.Message); }
    }

    public Visibility CalibrationVisibility =>
        _settings.Global.UnisonCalibrated ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CalibrationStepVisibility =>
        Calibrating ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RecalibrateVisibility =>
        _settings.Global.UnisonCalibrated && !Calibrating ? Visibility.Visible : Visibility.Collapsed;

    public string CalibrationHeader => _step switch
    {
        CalibrationStep.Lower => "Step 1 of 2 — the dimmest you want to go",
        CalibrationStep.Upper => "Step 2 of 2 — the brightest you want to go",
        _ => "Calibrated range",
    };

    public string CalibrationPrompt => _step switch
    {
        CalibrationStep.Lower =>
            "Set each display below to the dimmest level you would still work at, then capture. "
            + "They do not have to read the same number — matching how they look is the point.",
        CalibrationStep.Upper =>
            "Now set each display to the brightest level you want unison to reach, then capture.",
        _ => "The slider runs between the limits you captured, so 0% and 100% mean the same thing on every panel.",
    };

    public string CalibrationButtonText =>
        _step == CalibrationStep.Upper ? "Capture the upper limit" : "Capture the lower limit";

    /// <summary>Each display's captured limits, e.g. "1: 20–100%  |  2: 15–75%".</summary>
    public string CalibrationSummary
    {
        get
        {
            var parts = new List<string>(Displays.Count);
            foreach (DisplayViewModel d in Displays)
                if (d.SupportsBrightness)
                    parts.Add(d.RangeSummary);

            return parts.Count == 0 ? "No display reports brightness control" : string.Join("  |  ", parts);
        }
    }

    /// <summary>
    /// The slider's floor. Calibrated, 0% is the level the user chose as their
    /// dimmest, so it is a real setting; as a multiplier it is black, which is
    /// not something a brightness control should offer by accident.
    /// </summary>
    public double UnisonMinimum => _settings.Global.UnisonCalibrated ? 0 : 10;

    public string UnisonPercentText => $"{_settings.Global.UnisonLevel}%";

    public string UnisonLevelDescription => _settings.Global.UnisonCalibrated
        ? "Where each display sits between its own captured limits. 0% is your dimmest setting, not black."
        : "A multiplier on each display's own baseline, not an absolute brightness — so panels with different peak output stay in proportion.";

    private void RaiseCalibration()
    {
        Raise(nameof(Calibrating));
        Raise(nameof(UnisonSliderEnabled));
        Raise(nameof(CalibrationVisibility));
        Raise(nameof(CalibrationStepVisibility));
        Raise(nameof(RecalibrateVisibility));
        Raise(nameof(CalibrationHeader));
        Raise(nameof(CalibrationPrompt));
        Raise(nameof(CalibrationButtonText));
        Raise(nameof(CalibrationSummary));
        Raise(nameof(UnisonMinimum));
        Raise(nameof(UnisonLevelDescription));
        Raise(nameof(BrightnessSummary));
    }

    public double UnisonLevel
    {
        get => _settings.Global.UnisonLevel;
        set
        {
            if (Calibrating) return;
            int v = (int)value;
            if (_settings.Global.UnisonLevel == v) return;
            _settings.Global.UnisonLevel = v;
            PersistSoon();
            Raise();
            Raise(nameof(UnisonPercentText));

            if (UnisonBrightness && !Calibrating) _ = ApplyUnisonAsync(v / 100.0);
        }
    }

    /// <summary>Applies the unison multiplier to every display.</summary>
    /// <remarks>
    /// Runs the displays concurrently. Sequencing them would make a drag feel
    /// laggy, because each external monitor's DDC/CI write is a slow round trip
    /// and they would queue behind one another.
    /// </remarks>
    private async Task ApplyUnisonAsync(double factor)
    {
        var pending = new List<Task>(Displays.Count);
        // The built-in panel is driven like any other display, within its own
        // range, including while Windows' slider drives unison: the engine reads
        // that slider back through the same range (UnisonResume.LevelFor), so
        // Quick Settings showing the panel's real value does not jump the level.
        foreach (DisplayViewModel d in Displays) pending.Add(d.ApplyUnisonAsync(factor));

        await Task.WhenAll(pending);
        Raise(nameof(BrightnessSummary));
    }

    /// <summary>
    /// Where each display actually sits, e.g. "Internal 50%  |  DELL U2424H 72%".
    /// </summary>
    /// <remarks>
    /// The unison slider is a multiplier, so its own number says nothing about
    /// what any given panel is at. Showing the resolved values is what makes it
    /// legible — particularly when two panels started from very different
    /// levels.
    /// </remarks>
    public string BrightnessSummary
    {
        get
        {
            var parts = new List<string>(Displays.Count);
            foreach (DisplayViewModel d in Displays)
                if (d.SupportsBrightness)
                    parts.Add($"{d.Number}: {d.BrightnessPercent}%");

            return parts.Count == 0 ? "No display reports brightness control" : string.Join("  |  ", parts);
        }
    }

    private async Task RefreshSummaryWhenReadyAsync(int loadVersion)
    {
        var waits = new List<Task>(Displays.Count);
        foreach (DisplayViewModel d in Displays) waits.Add(d.BrightnessReady);

        await Task.WhenAll(waits);
        Raise(nameof(BrightnessSummary));
        if (loadVersion == _displayLoadVersion)
            ShowFooterStatus($"{Displays.Count} display{(Displays.Count == 1 ? "" : "s")} ready.");
    }

    /// <summary>Re-reads the per-display brightness line.</summary>
    public void RefreshBrightnessSummary() => Raise(nameof(BrightnessSummary));

    /// <summary>Makes one display the main one, then reloads everything.</summary>
    /// <remarks>
    /// Every display shifts, because Windows defines the main display as the
    /// one at (0,0) — so the whole list is stale afterwards, not just the two
    /// that swapped roles.
    /// </remarks>
    public void MakePrimary(DisplayViewModel display)
    {
        var all = new List<DisplayInfo>(Displays.Count);
        foreach (DisplayViewModel d in Displays) all.Add(d.Info);

        DisplayInfo target = display.Info;

        _ = Task.Run(() =>
        {
            DisplayArrangement.SetPrimary(target, all);
            _dispatcher.TryEnqueue(Refresh);
        });
    }

    // ------------------------------------------------------ detect / cast --

    private string _detectStatus = string.Empty;

    public string DetectStatus => _detectStatus;

    public Visibility DetectStatusVisibility =>
        string.IsNullOrEmpty(_detectStatus) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Re-enumerates displays, including ones connected but switched off.
    /// </summary>
    /// <remarks>
    /// Windows' own Detect button has no public API behind it. This does the
    /// part that is actually useful: a full re-scan of active <em>and</em>
    /// inactive paths, so a monitor that is plugged in but disabled shows up
    /// rather than silently missing. Waking a monitor the driver has not
    /// noticed at all still needs Windows' own Display settings.
    /// </remarks>
    public void DetectDisplays()
    {
        int before = Displays.Count;
        Refresh();

        List<string> inactive = DisplayRegistry.InactiveDisplays();

        _detectStatus = Displays.Count != before
            ? $"Found {Displays.Count} displays."
            : inactive.Count > 0
                ? $"No new active displays. {inactive.Count} connected but switched off: {string.Join(", ", inactive)}."
                : $"No change — {Displays.Count} displays, none switched off.";

        Raise(nameof(DetectStatus));
        Raise(nameof(DetectStatusVisibility));

        Identify();
        ShowFooterStatus(_detectStatus);
    }

    /// <summary>
    /// Flashes each display's number on the screen itself.
    /// </summary>
    /// <remarks>
    /// Re-scanning answers "what is attached"; this answers "which physical
    /// monitor is number two", which is the question someone actually has in
    /// front of two identical panels.
    /// </remarks>
    public void Identify()
    {
        var displays = new List<DisplayInfo>(Displays.Count);
        foreach (DisplayViewModel d in Displays) displays.Add(d.Info);

        DisplayIdentifier.Show(displays, TimeSpan.FromSeconds(3));
        ShowFooterStatus("Showing display numbers for 3 seconds.");
    }

    /// <summary>Opens Windows' Cast pane — the same one Win+K raises.</summary>
    /// <remarks>
    /// Deliberately a hand-off, and deliberately the Cast pane rather than the
    /// Settings page: Miracast discovery, PIN entry and driver negotiation are
    /// already implemented there, and the pane is where casting actually starts.
    /// </remarks>
    public static void ConnectWirelessDisplay() => ShellFlyout.OpenCast();

    // ---------------------------------------------------------- arrangement --

    public ObservableCollection<string> Arrangements { get; } =
    [
        "Extend these displays",
        "Duplicate these displays",
        "Show only on built-in display",
        "Show only on external displays",
    ];

    private string? _selectedArrangement;
    private bool _arrangementReady;
    private string _arrangementStatus = string.Empty;

    public string? SelectedArrangement
    {
        get => _selectedArrangement;
        set
        {
            if (_selectedArrangement == value || value is null) return;
            _selectedArrangement = value;
            Raise();

            if (!_arrangementReady) return;

            DesktopArrangement target = Arrangements.IndexOf(value) switch
            {
                1 => DesktopArrangement.Duplicate,
                2 => DesktopArrangement.InternalOnly,
                3 => DesktopArrangement.ExternalOnly,
                _ => DesktopArrangement.Extend,
            };

            ArrangementStatus = "Applying…";
            ShowFooterStatus("Applying display arrangement…", busy: true);

            _ = Task.Run(() =>
            {
                bool ok = DesktopLayout.Apply(target);

                _dispatcher.TryEnqueue(() =>
                {
                    ArrangementStatus = ok ? string.Empty : "Windows refused that arrangement.";
                    ShowFooterStatus(ok ? "Display arrangement applied." : "Windows refused that display arrangement.");
                    // The whole display set has changed; everything is stale.
                    Refresh();
                });
            });
        }
    }

    public string ArrangementStatus
    {
        get => _arrangementStatus;
        private set { _arrangementStatus = value; Raise(); Raise(nameof(ArrangementStatusVisibility)); }
    }

    public Visibility ArrangementStatusVisibility =>
        string.IsNullOrEmpty(_arrangementStatus) ? Visibility.Collapsed : Visibility.Visible;

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    private void RefreshArrangement()
    {
        _arrangementReady = false;

        _selectedArrangement = DisplayRegistry.Topology() switch
        {
            DisplayRegistry.DisplayTopology.Duplicated => Arrangements[1],
            DisplayRegistry.DisplayTopology.Single =>
                Displays.Count == 1 && Displays[0].IsInternalPanel ? Arrangements[2] : Arrangements[3],
            _ => Arrangements[0],
        };

        Raise(nameof(SelectedArrangement));
        _arrangementReady = true;
    }

    // --------------------------------------------------- global taskbar hide --

    /// <summary>
    /// Windows' own auto-hide, which is the only thing that can hide the
    /// primary taskbar. Global, and it leaves a lit sliver — see
    /// <see cref="GlobalTaskbar"/>.
    /// </summary>
    public bool GlobalAutoHide
    {
        get => GlobalTaskbar.IsAutoHide;
        set
        {
            if (GlobalTaskbar.IsAutoHide == value) return;
            GlobalTaskbar.SetAutoHide(value);
            Raise();
        }
    }

    // ---------------------------------------------------------------- reset --

    /// <summary>Restores DispCtrl's own defaults, globally and for every monitor.</summary>
    public void ResetEverything()
    {
        _settings.ResetAll();
        Persist();
        // Not ReloadFromDisk: Persist has just recorded the file's new stamp,
        // so the sync there saw nothing to do and the page kept showing the
        // settings it had before. Every card and every binding is told here.
        foreach (DisplayViewModel display in Displays) display.NotifySettingsReloaded();
        Raise(string.Empty);
        RaiseProtectionSettings();
        RaiseAwakeSettings();
        Hotkeys.Reload();
        QuickPanelChanged?.Invoke();
    }

    // ------------------------------------------------------------- tuning --

    public double HideDelayMs
    {
        get => _settings.Global.HideDelayMs;
        set
        {
            int v = (int)value;
            if (_settings.Global.HideDelayMs == v) return;
            _settings.Global.HideDelayMs = v;
            PersistSoon();
            Raise();
        }
    }

    /// <summary>
    /// Whether the taskbar slides or snaps.
    /// </summary>
    /// <remarks>
    /// Backed by the same duration the slider edits — zero means instant — so
    /// there is one source of truth rather than two settings that can disagree.
    /// The last non-zero duration is remembered, so switching to instant and
    /// back does not silently reset a tuned value.
    /// </remarks>
    public bool AnimateTaskbar
    {
        get => _settings.Global.AnimMs > 0;
        set
        {
            if (AnimateTaskbar == value) return;

            if (value)
            {
                _settings.Global.AnimMs = _lastAnimMs > 0 ? _lastAnimMs : 270;
            }
            else
            {
                _lastAnimMs = _settings.Global.AnimMs;
                _settings.Global.AnimMs = 0;
            }

            Persist();
            Raise();
            Raise(nameof(AnimMs));
            Raise(nameof(AnimMsEnabled));
        }
    }

    private int _lastAnimMs = 270;

    public bool AnimMsEnabled => AnimateTaskbar;

    public double AnimMs
    {
        get => _settings.Global.AnimMs;
        set
        {
            int v = (int)value;
            if (_settings.Global.AnimMs == v) return;
            _settings.Global.AnimMs = v;
            PersistSoon();
            Raise();
        }
    }

    public double RevealPx
    {
        get => _settings.Global.RevealPx;
        set
        {
            int v = (int)value;
            if (_settings.Global.RevealPx == v) return;
            _settings.Global.RevealPx = v;
            PersistSoon();
            Raise();
        }
    }

    /// <summary>
    /// How often the engine looks at the cursor, in four bands.
    /// </summary>
    /// <remarks>
    /// One property each rather than a group, because each is bound to its own
    /// slider and a group would mean re-raising all four whenever any moved.
    /// The clamps are the same ones the engine applies when it reads the file,
    /// so a value set here cannot mean something different once it gets there.
    /// </remarks>
    public double ArmDistancePx
    {
        get => _settings.Global.ArmDistancePx;
        set => SetPoll(v => _settings.Global.ArmDistancePx = v, _settings.Global.ArmDistancePx, value, 10, 4000);
    }

    public double IdlePollMs
    {
        get => _settings.Global.IdlePollMs;
        set => SetPoll(v => _settings.Global.IdlePollMs = v, _settings.Global.IdlePollMs, value, 8, 2000);
    }

    public double FarPollMs
    {
        get => _settings.Global.FarPollMs;
        set => SetPoll(v => _settings.Global.FarPollMs = v, _settings.Global.FarPollMs, value, 8, 5000);
    }

    public double ArmedPollMs
    {
        get => _settings.Global.ArmedPollMs;
        set => SetPoll(v => _settings.Global.ArmedPollMs = v, _settings.Global.ArmedPollMs, value, 4, 500);
    }

    public double ShownPollMs
    {
        get => _settings.Global.ShownPollMs;
        set => SetPoll(v => _settings.Global.ShownPollMs = v, _settings.Global.ShownPollMs, value, 4, 500);
    }

    private void SetPoll(Action<int> write, int current, double value, int min, int max,
                         [CallerMemberName] string? name = null)
    {
        int v = Math.Clamp((int)value, min, max);
        if (current == v) return;

        write(v);
        PersistSoon();
        Raise(name);
        Raise(nameof(PollSummary));
    }

    /// <summary>The four intervals in one line, for the collapsed expander.</summary>
    public string PollSummary =>
        $"Idle {_settings.Global.IdlePollMs} ms, armed {_settings.Global.ArmedPollMs} ms within "
        + $"{_settings.Global.ArmDistancePx} px, shown {_settings.Global.ShownPollMs} ms.";

    /// <summary>Puts the polling back where it started.</summary>
    /// <remarks>
    /// Worth a button: these are the settings most likely to be dragged around
    /// out of curiosity, and the least likely to be remembered afterwards.
    /// </remarks>
    public void RestorePolling()
    {
        var defaults = new GlobalSettings();
        GlobalSettings g = _settings.Global;

        g.ArmDistancePx = defaults.ArmDistancePx;
        g.IdlePollMs = defaults.IdlePollMs;
        g.FarPollMs = defaults.FarPollMs;
        g.ArmedPollMs = defaults.ArmedPollMs;
        g.ShownPollMs = defaults.ShownPollMs;

        Persist();

        Raise(nameof(ArmDistancePx));
        Raise(nameof(IdlePollMs));
        Raise(nameof(FarPollMs));
        Raise(nameof(ArmedPollMs));
        Raise(nameof(ShownPollMs));
        Raise(nameof(PollSummary));
    }

    public bool Logging
    {
        get => _settings.Global.Logging;
        set
        {
            if (_settings.Global.Logging == value) return;
            _settings.Global.Logging = value;
            Persist();
            Raise();
        }
    }

    /// <summary>Stops the engine gracefully, waits for it to go, and starts it again.</summary>
    /// <returns>False when it did not stop within 20 seconds, or would not start.</returns>
    public async Task<bool> RestartEngineAsync()
    {
        if (EngineController.Query().Running)
        {
            EngineController.Stop();
            // It restores every taskbar before it exits; starting a second one
            // meanwhile would have two engines managing Explorer.
            for (int i = 0; i < 80 && EngineController.Query().Running; i++) await Task.Delay(250);
            if (EngineController.Query().Running) return false;
        }
        bool started = _engine.Start();
        RefreshEngineStatus();
        return started;
    }

    /// <summary>See <see cref="GlobalSettings.ArrangeByResolution"/>.</summary>
    public bool ArrangeByResolution
    {
        get => _settings.Global.ArrangeByResolution;
        set { if (_settings.Global.ArrangeByResolution == value) return; _settings.Global.ArrangeByResolution = value; Persist(); Raise(); }
    }

    /// <summary>
    /// Starts the engine if it is not running, and switches on starting it at
    /// sign-in the first time a Store install is opened.
    /// </summary>
    /// <remarks>
    /// The engine is what works with the window shut: the tray icon, hotkeys,
    /// taskbar hiding and glass, night light. A Store install cannot run
    /// anything when it is installed, so the first opening is the first chance;
    /// without this, a fresh install had none of those until the engine was
    /// found and started by hand. The sign-in task is offered once only, so
    /// switching it off afterwards stays off.
    /// </remarks>
    public async Task StartEngineByDefaultAsync()
    {
        RefreshEngineStatus();
        if (!_status.Running) SetEngineRunning(true);

        if (!_settings.Global.EngineStartupOffered && StartupIntegration.IsPackaged)
        {
            try { await StartupIntegration.SetEngineStartupAsync(true, _engine.EnginePath); }
            catch (Exception ex) { ShowFooterStatus("Start at sign-in: " + ex.Message); }
            _settings.Global.EngineStartupOffered = true;
            Persist();
        }
    }

    public void SetEngineRunning(bool running)
    {
        if (running == _status.Running) return;

        bool accepted = running ? _engine.Start() : EngineController.Stop();
        if (accepted)
        {
            _engineRequestedState = running;
            _engineChangeDeadline = DateTime.UtcNow.AddSeconds(15);
            Raise(nameof(CanToggleEngine));
        }
        ShowFooterStatus(accepted
            ? running ? "Starting engine…" : "Stopping engine safely…"
            : running ? "The engine could not be started." : "The engine could not be stopped.",
            busy: accepted);

        // The change is not instant either way — the engine restores taskbars
        // before exiting, and takes a moment to start. The poll timer picks the
        // new state up; forcing it here would just show a stale value.
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
