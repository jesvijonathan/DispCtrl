using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Umbra.Display;
using Umbra.App.Services;
using Umbra.Core.Displays;
using Umbra.Core.Settings;

namespace Umbra.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly EngineController _engine = new();
    private UmbraSettings _settings = SettingsStore.Load();

    public ObservableCollection<DisplayViewModel> Displays { get; } = [];

    public MainViewModel()
    {
        Presets = new PresetsViewModel(() => _settings, CurrentDisplays, Persist, Refresh);
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
    public void ReloadFromDisk()
    {
        Refresh();
        Raise(nameof(HideDelayMs));
        Raise(nameof(AnimMs));
        Raise(nameof(AnimateTaskbar));
        Raise(nameof(AnimMsEnabled));
        Raise(nameof(RevealPx));
        Raise(nameof(Logging));
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
        _layoutSignature = DisplayRegistry.CheapSignature();
        _settings = SettingsStore.Load();
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
        _ = RefreshSummaryWhenReadyAsync();
        Raise(nameof(HasDisplays));
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

        // Settings-driven changes count as desk changes too — night light and
        // taskbar hiding live here rather than in the hardware.
        ScheduleDriftCheck();
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

    public bool EngineRunning => _status.Running;

    public string EngineStateText => _status.Running ? "Running" : "Stopped";

    public string EngineDetailText => _status.Running
        ? $"Process {_status.ProcessId}  ·  {_status.MemoryMb:N1} MB"
        : _engine.EnginePath is null
            ? "Engine executable not found — build Umbra.Engine first."
            : "Taskbars are not being managed.";

    public bool CanToggleEngine => _engine.EnginePath is not null;

    /// <summary>
    /// Start and Stop are separate buttons rather than one whose caption
    /// changes, so each can carry its own colour — accent to invite starting,
    /// critical to warn that stopping hands the taskbars back.
    /// </summary>
    public Visibility StartVisibility => _status.Running ? Visibility.Collapsed : Visibility.Visible;

    public Visibility StopVisibility => _status.Running ? Visibility.Visible : Visibility.Collapsed;

    public string EnginePath => _engine.EnginePath ?? "not found";

    public string LogPath => SettingsStore.LogPath;

    public string SettingsPath => SettingsStore.Path_;

    public void RefreshEngineStatus()
    {
        EngineStatus next = EngineController.Query();
        if (next == _status) return;

        _status = next;
        Raise(nameof(EngineRunning));
        Raise(nameof(EngineStateText));
        Raise(nameof(EngineDetailText));
        Raise(nameof(StartVisibility));
        Raise(nameof(StopVisibility));
    }

    /// <summary>
    /// The Presets page's state.
    /// </summary>
    /// <remarks>
    /// Hung off the shared view model rather than created per navigation, so
    /// the selected preset survives leaving the page and coming back. A picker
    /// that forgets what was selected is not a picker anyone trusts.
    /// </remarks>
    public PresetsViewModel Presets { get; }

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

    // --------------------------------------------------------------- report --

    private string _reportStatus =
        "Everything each display reports about itself, including the controls only the monitor "
        + "knows about. Written to a text file on this PC; nothing is sent anywhere.";

    public string ReportStatus
    {
        get => _reportStatus;
        private set { _reportStatus = value; Raise(); }
    }

    /// <summary>Writes the display report and says where it went.</summary>
    /// <remarks>
    /// Off the UI thread: the report enumerates every mode and asks each
    /// external monitor over DDC/CI what it supports, which is seconds of
    /// blocking calls on a busy desk.
    /// </remarks>
    public async Task WriteReportAsync()
    {
        ReportStatus = "Reading every display\u2026";

        List<DisplayInfo> displays = CurrentDisplays();

        try
        {
            string path = await Task.Run(() => DisplayReport.Write(displays));
            ReportStatus = $"Written to {path}";
        }
        catch (Exception ex)
        {
            ReportStatus = $"Could not write the report: {ex.Message}";
        }
    }

    public static string ReportPath => DisplayReport.Path_;

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
    public string NightLightStrengthText => $"{Night.Strength}%  ·  {NightLight.KelvinFor(Night.Strength):0}K";

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
    /// and how bright each panel should go, and there is nothing Umbra can read
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

    private async Task RefreshSummaryWhenReadyAsync()
    {
        var waits = new List<Task>(Displays.Count);
        foreach (DisplayViewModel d in Displays) waits.Add(d.BrightnessReady);

        await Task.WhenAll(waits);
        Raise(nameof(BrightnessSummary));
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

            _ = Task.Run(() =>
            {
                bool ok = DesktopLayout.Apply(target);

                _dispatcher.TryEnqueue(() =>
                {
                    ArrangementStatus = ok ? string.Empty : "Windows refused that arrangement.";
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

    /// <summary>Restores Umbra's own defaults, globally and for every monitor.</summary>
    public void ResetEverything()
    {
        _settings.Global.ResetToDefaults();
        foreach (MonitorSettings ms in _settings.Monitors.Values) ms.ResetToDefaults();
        Persist();
        ReloadFromDisk();
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

    public void ToggleEngine()
    {
        if (_status.Running) EngineController.Stop();
        else _engine.Start();

        // The change is not instant either way — the engine restores taskbars
        // before exiting, and takes a moment to start. The poll timer picks the
        // new state up; forcing it here would just show a stale value.
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
