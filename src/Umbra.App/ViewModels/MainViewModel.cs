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

    public MainViewModel() => Refresh();

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

    public void Refresh()
    {
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

            Displays.Add(new DisplayViewModel(d, ms, i + 1, Persist));
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
    private void Persist() => SettingsStore.Save(_settings);

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
