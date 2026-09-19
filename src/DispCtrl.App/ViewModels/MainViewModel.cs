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
        if (SettingsStamp() == _settingsStamp && DisplayRegistry.CheapSignature() == _layoutSignature)
        {
            if (_activationRefresh.IsCompleted) _activationRefresh = RefreshExistingReadingsAsync();
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
    /// The check is one <c>EnumDisplayMonitors</c> sweep of data the kernel
    /// already has — no CCD query, no registry, no DDC/CI — so it is cheap
    /// enough to run every couple of seconds. The rebuild behind it is not:
    /// it re-reads every monitor's capabilities over DDC/CI, which is seconds
    /// per panel. Hence the gate.
    /// </remarks>
    public void RefreshIfDisplaysChanged()
    {
        string now = DisplayRegistry.CheapSignature();
        if (now == _layoutSignature) return;

        _layoutSignature = now;
        Refresh();
    }

    public void Refresh()
    {
        int loadVersion = ++_displayLoadVersion;
        ShowFooterStatus("Loading display information…", busy: true);
        _layoutSignature = DisplayRegistry.CheapSignature();
        _settings = SettingsStore.Load();
        _settingsStamp = SettingsStamp();
        DispCtrl.Display.Presets.PresetService.InvalidateHardware();
        foreach (DisplayViewModel existing in Displays) MonitorCapabilities.Forget(existing.Info);
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

            Displays.Add(new DisplayViewModel(d, ms, _settings, i + 1, Persist, () => PerDisplayWarmth, ScheduleDriftCheck));
        }

        ScalePreviews();
        RefreshTopology();
        RefreshArrangement();
        RefreshEngineStatus();

        // The per-display brightness line is built from values the hardware has
        // not answered for yet — DDC/CI takes far longer than WMI — so it is
        // refreshed once every display has reported in, rather than showing
        // only whichever panel happened to be fast.
        _ = RefreshSummaryWhenReadyAsync(loadVersion);
        Raise(nameof(HasDisplays));
        if (PresetsEnabled) Presets.Reload();
    }

    public bool HasDisplays => Displays.Count > 0;

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
        _inactiveText = inactive.Count == 0
            ? string.Empty
            : inactive.Count == 1
                ? $"{inactive[0]} is connected but switched off in Display settings."
                : $"{inactive.Count} connected displays are switched off in Display settings: {string.Join(", ", inactive)}.";

        Raise(nameof(TopologyText));
        Raise(nameof(InactiveText));
        Raise(nameof(InactiveVisibility));
    }

    /// <summary>
    /// Writes settings out. The engine watches this file, so a toggle takes
    /// effect without restarting or otherwise signalling it.
    /// </summary>
    private void Persist()
    {
        SettingsStore.Save(_settings);
        _settingsStamp = SettingsStamp();
        ShowFooterStatus("Settings saved.");

        // Per-display power features are saved through DisplayViewModel rather
        // than the shared protection setters, so they must be able to bring the
        // resident engine up on their own.
        if (_settings.Global.Awake.ActiveAt(DateTimeOffset.UtcNow)
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

            if (value) _ = CaptureBaselinesAsync();
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
    private async Task CaptureBaselinesAsync()
    {
        foreach (DisplayViewModel d in Displays)
            await d.CaptureBaselineAsync();

        _settings.Global.UnisonLevel = 100;
        Persist();
        Raise(nameof(UnisonLevel));
    }

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

    // ----------------------------------------------------- monitor details --

    /// <summary>
    /// The records prepared by the last collect, one per attached monitor.
    /// </summary>
    /// <remarks>
    /// Held rather than rebuilt so that View and Submit work on exactly what was
    /// read. A capabilities sweep is around a hundred DDC/CI round trips per
    /// monitor at 40 ms apiece, so asking twice for the same answer costs seconds
    /// and gives the channel a second chance to come back empty.
    /// </remarks>
    private readonly List<Contribution> _collected = [];

    /// <remarks>
    /// Says what is actually published, which is now a great deal more than it
    /// was: the whole report for each display, what it is set to at this moment,
    /// and every preset. Consent to "device details" would not be consent to
    /// that, so the sentence had to change with the payload.
    /// </remarks>
    private static readonly string DetailsPrompt =
        "Checks attached monitor models against the repository catalog. New models can be collected "
        + "as a reviewable device record with controls, capabilities and modes. The complete diagnostic "
        + "report stays on this PC; serial numbers, paths, current settings and your user name are never "
        + "put in the public issue.";

    private string _detailsStatus = DetailsPrompt;

    public string DetailsStatus
    {
        get => _detailsStatus;
        private set { _detailsStatus = value; Raise(); }
    }

    /// <summary>
    /// Whether there is anything to view or submit yet.
    /// </summary>
    /// <remarks>
    /// Both buttons appear only once the displays have been read. Neither can do
    /// its job before that, and a button that is always there but only sometimes
    /// works is one whose state has to be learned by pressing it.
    /// </remarks>
    public Visibility CollectedVisibility =>
        _collected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// True when the complete report cannot fit in GitHub's prefilled-issue URL.
    /// </summary>
    /// <remarks>
    /// The browser can safely receive the title in that case, but not a report
    /// that has grown to tens of thousands of encoded characters. The UI copies
    /// the exact body before opening the browser so it is ready to paste.
    /// </remarks>
    public bool SubmissionNeedsPaste => _collected.Any(item => !item.Prefilled);

    public List<DisplayInfo> UnknownDisplays()
    {
        var result = new List<DisplayInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DisplayInfo display in CurrentDisplays())
            if (!KnownMonitorCatalog.Contains(display.Key.Model) && seen.Add(display.Key.Model))
                result.Add(display);
        return result;
    }

    /// <summary>
    /// Reads every display once: the full local report, and a publishable record
    /// per monitor.
    /// </summary>
    /// <remarks>
    /// One press for the whole desk. This was a button per display inside an
    /// expander, which asked the user to understand that a device record
    /// describes a model before they could send one — and to press it again for
    /// every monitor they own.
    /// <para>
    /// Off the UI thread. Both halves enumerate every mode and ask each external
    /// monitor over DDC/CI what it supports, which is seconds of blocking calls
    /// per panel.
    /// </para>
    /// </remarks>
    public async Task CollectAsync(IReadOnlyList<DisplayInfo> unknown)
    {
        List<DisplayInfo> displays = CurrentDisplays();

        _collected.Clear();
        Raise(nameof(CollectedVisibility));

        DetailsStatus = "Reading every display…";
        ShowFooterStatus(DetailsStatus, busy: true);

        string? report = null;
        string? trouble = null;

        try
        {
            report = await Task.Run(() => DisplayReport.Write(displays));
        }
        catch (Exception ex)
        {
            trouble = $"the report could not be written: {ex.Message}";
        }

        if (unknown.Count > 0)
        {
            DetailsStatus = "Reading the new monitor models…";
            ShowFooterStatus(DetailsStatus, busy: true);
            try
            {
                List<Contribution> records = await Task.Run(() =>
                    unknown.Select(display => DeviceContribution.Prepare(display, displays)).ToList());
                _collected.AddRange(records);
            }
            catch (Exception ex)
            {
                trouble ??= $"the records could not be built: {ex.Message}";
            }
        }

        Raise(nameof(CollectedVisibility));
        DetailsStatus = unknown.Count == 0
            ? "Every attached monitor model is already present in the repository catalog."
                + (report is null ? "" : $" The local report is at {report}.")
            : Summarise(report, trouble);
        ShowFooterStatus(trouble is null ? "Display report ready." : $"Display report incomplete: {trouble}");
    }

    private string Summarise(string? report, string? trouble)
    {
        var sb = new StringBuilder();

        sb.Append(_collected.Count == 0
            ? "None of the displays could be read."
            : $"{_collected.Count} new monitor record{(_collected.Count == 1 ? "" : "s")} read, and nothing has been sent.");

        if (report is not null) sb.Append($" The full report is at {report}.");
        if (trouble is not null) sb.Append($" One thing went wrong — {trouble}.");

        return sb.ToString();
    }

    /// <summary>
    /// The text that would be published, exactly as it would be published.
    /// </summary>
    /// <remarks>
    /// Shown in full rather than summarised. Someone deciding whether to publish
    /// a record of their hardware is entitled to read the record, and a dialog
    /// saying "device details will be sent" asks them to take it on trust.
    /// <para>
    /// Each monitor's record stands on its own, under its own heading, because
    /// that is how they are submitted and how the device folder is organised.
    /// </para>
    /// </remarks>
    public string CollectedText
    {
        get
        {
            var sb = new StringBuilder();

            foreach (Contribution c in _collected)
            {
                if (sb.Length > 0) sb.AppendLine().AppendLine();
                sb.AppendLine(c.Body.TrimEnd());
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Opens one prefilled issue per monitor. The person still presses Submit.
    /// </summary>
    /// <remarks>
    /// One issue for the desk, titled with every display on it. Per monitor came
    /// first, on the reasoning that a record describes a model - and it produced
    /// a browser tab per monitor, every one of them empty, because a record that
    /// carries the full report is always past the length GitHub takes in a link.
    /// The per-model files are still written, so the folder can be filled from
    /// the issue.
    /// <para>
    /// Opening a form is not submitting one. There is no token in this
    /// application and it makes no request: the browser shows the exact text and
    /// the person presses Submit there, or closes the tab.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The one record that must be pasted by hand, for the caller to put on the
    /// clipboard, or null when every record prefilled.
    /// </returns>
    public string? Submit()
    {
        if (_collected.Count == 0) return null;

        int opened = 0;
        var fallback = new StringBuilder();
        foreach (Contribution record in _collected)
        {
            if (!DeviceContribution.Open(record)) continue;
            opened++;
            if (!record.Prefilled)
            {
                if (fallback.Length > 0) fallback.AppendLine().AppendLine();
                fallback.Append(record.Body);
            }
        }

        if (opened == 0)
        {
            DetailsStatus = "No browser would open. The records remain in " + DeviceContribution.Folder + ".";
            return null;
        }

        DetailsStatus = fallback.Length == 0
            ? $"{opened} prefilled GitHub issue{(opened == 1 ? " is" : "s are")} open. Review and submit there."
            : "A record still exceeded the browser limit and was copied for fallback.";
        return fallback.Length == 0 ? null : fallback.ToString();
    }

    public static string ReportPath => DisplayReport.Path_;

    public static string ContributeFolder => DeviceContribution.Folder;

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
            Persist();
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
    public bool PerDisplayWarmth => Night.Enabled && (!Night.Unison || CalibratingWarmth);

    public Visibility NightLightVisibility =>
        Night.Enabled ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The shared slider only means anything in unison mode.</summary>
    public Visibility NightLightSharedVisibility =>
        Night.Enabled && Night.Unison ? Visibility.Visible : Visibility.Collapsed;

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
        _step = CalibrationStep.Lower;
        RaiseCalibration();
    }

    private void ClearCalibration()
    {
        _step = CalibrationStep.None;
        foreach (DisplayViewModel d in Displays) d.ClearLimits();
    }

    /// <summary>Abandons the walkthrough, leaving any limits already captured.</summary>
    public void CancelCalibration()
    {
        _step = CalibrationStep.None;
        RaiseCalibration();
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
        if (_step == CalibrationStep.None) return;

        bool upper = _step == CalibrationStep.Upper;

        var pending = new List<Task>(Displays.Count);
        foreach (DisplayViewModel d in Displays) pending.Add(d.CaptureLimitAsync(upper));
        await Task.WhenAll(pending);

        if (upper)
        {
            _step = CalibrationStep.None;

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
        }

        RaiseCalibration();
    }

    public bool Calibrating => _step != CalibrationStep.None;

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
            int v = (int)value;
            if (_settings.Global.UnisonLevel == v) return;
            _settings.Global.UnisonLevel = v;
            Persist();
            Raise();
            Raise(nameof(UnisonPercentText));

            _ = ApplyUnisonAsync(v / 100.0);
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
        foreach (DisplayViewModel d in Displays)
            pending.Add(d.ApplyUnisonAsync(factor));

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
        _settings.Global.ResetToDefaults();
        foreach (MonitorSettings ms in _settings.Monitors.Values) ms.ResetToDefaults();
        Persist();
        ReloadFromDisk();
        RaiseProtectionSettings();
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
            Persist();
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
                _settings.Global.AnimMs = _lastAnimMs > 0 ? _lastAnimMs : 180;
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

    private int _lastAnimMs = 180;

    public bool AnimMsEnabled => AnimateTaskbar;

    public double AnimMs
    {
        get => _settings.Global.AnimMs;
        set
        {
            int v = (int)value;
            if (_settings.Global.AnimMs == v) return;
            _settings.Global.AnimMs = v;
            Persist();
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
            Persist();
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
        Persist();
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
