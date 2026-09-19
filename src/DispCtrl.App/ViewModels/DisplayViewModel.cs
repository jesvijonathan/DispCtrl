using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DispCtrl.App.ViewModels;

/// <summary>One monitor, as the panel presents it.</summary>
public sealed class DisplayViewModel : INotifyPropertyChanged
{
    private readonly DisplayInfo _display;
    private readonly MonitorSettings _settings;
    private readonly DispCtrlSettings _root;

    /// <summary>
    /// Whether each display is showing its own warmth slider right now.
    /// </summary>
    /// <remarks>
    /// A callback rather than a setting, because it is true in per-display mode
    /// <em>and</em> mid-calibration whatever the mode — and the calibration step
    /// is transient UI state that has no business being written to disk.
    /// </remarks>
    private readonly Func<bool> _perDisplayWarmth;

    private readonly Action _persist;

    /// <summary>
    /// Raised when the user changes something a preset captures.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="_persist"/> because most of what a preset
    /// captures never reaches the settings file: brightness, resolution,
    /// refresh rate, scaling and HDR live in the hardware. Hooking drift
    /// detection onto persistence therefore missed every one of them, and the
    /// preset bar sat claiming everything matched while the screen visibly did
    /// not.
    /// </remarks>
    private readonly Action _deskChanged;
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();

    /// <summary>
    /// Coalesces slider drags into one hardware write.
    /// </summary>
    /// <remarks>
    /// A DDC/CI round trip takes tens to hundreds of milliseconds, and a slider
    /// raises a change per pixel dragged. Writing each one would queue hundreds
    /// of requests the monitor answers slowly, and the panel would keep moving
    /// long after the user let go.
    /// </remarks>
    private CancellationTokenSource? _brightnessWrite;

    public DisplayViewModel(DisplayInfo display, MonitorSettings settings, DispCtrlSettings root,
                            int number, Action persist, Func<bool> perDisplayWarmth, Action deskChanged)
    {
        _display = display;
        _settings = settings;
        _root = root;
        _persist = persist;
        _perDisplayWarmth = perDisplayWarmth;
        _deskChanged = deskChanged;
        Number = number;

        // Nothing blocking here. Everything this view model needs comes from
        // the display driver, the monitor over DDC/CI, or COM — all of which
        // are slow enough to stall the window visibly if done inline. Mode
        // enumeration alone walks every mode the driver reports, which is 163
        // on the external monitor this was built against.
        ReadingsReady = LoadEverythingAsync();
    }

    /// <summary>
    /// Pulls every slow value in one pass, off the UI thread.
    /// </summary>
    /// <remarks>
    /// Batched deliberately: these are independent reads against the same
    /// hardware, and issuing them together rather than one view model at a
    /// time is what keeps the window responsive while a second monitor's
    /// DDC/CI channel takes its time answering.
    /// </remarks>
    public Task ReadingsReady { get; private set; } = Task.CompletedTask;
    public Task RefreshReadingsAsync()
    {
        if (!ReadingsReady.IsCompleted) return ReadingsReady;
        return ReadingsReady = LoadEverythingAsync();
    }

    private IReadOnlyDictionary<(uint Width, uint Height), uint[]> _modeRates = new Dictionary<(uint, uint), uint[]>();
    private async Task LoadEverythingAsync()
    {
        _modesReady = _fitReady = _brightnessReady = _hdrReady = _orientationReady = _scalingReady = false;
        DisplayInfo d = _display;

        // Started first and awaited last. The information table is the first
        // thing an expanded card shows, and it has no dependency on the
        // wallpaper — but it used to queue behind it, and IDesktopWallpaper is
        // an out-of-process COM server whose first activation costs hundreds of
        // milliseconds. That wait was the whole reason the table sat full of
        // em-dashes after launch.
        Task advanced = LoadAdvancedAsync();
        Task brightness = LoadBrightnessAsync();
        Task monitorControls = LoadMonitorControlsAsync();
        LoadMachineSettings();

        (List<DisplayMode> modes, string? wallpaper, WallpaperFit fit) =
            await Task.Run(() => (
                DisplayModes.Available(d.GdiName),
                Wallpaper.Read(d),
                Wallpaper.ReadFit())).ConfigureAwait(true);

        Resolutions.Clear();
        _modeRates = modes.GroupBy(mode => (mode.Width, mode.Height))
            .ToDictionary(group => group.Key, group => group.Select(mode => mode.RefreshHz).Distinct().Order().ToArray());
        foreach (var (w, h) in _modeRates.Keys) Resolutions.Add($"{w} × {h}");

        _selectedResolution = $"{d.Bounds.Width} × {d.Bounds.Height}";
        PopulateRefreshRates();
        _selectedRefreshRate = $"{d.RefreshHz} Hz";
        Raise(nameof(SelectedResolution));
        Raise(nameof(SelectedRefreshRate));

        // Only now can a combo-box selection be attributed to the user.
        _modesReady = true;

        _selectedFit = fit switch
        {
            WallpaperFit.Fit => "Fit",
            WallpaperFit.Stretch => "Stretch",
            WallpaperFit.Tile => "Tile",
            WallpaperFit.Center => "Centre",
            WallpaperFit.Span => "Span",
            _ => "Fill",
        };
        _fitReady = true;
        Raise(nameof(SelectedWallpaperFit));

        bool wallpaperChanged = _wallpaperPath != wallpaper;
        _wallpaperPath = wallpaper;
        Raise(nameof(WallpaperName));
        if (wallpaperChanged && _wallpaperPath is not null && File.Exists(_wallpaperPath))
            _ = DecodeWallpaperAsync(_wallpaperPath);

        await Task.WhenAll(advanced, brightness, monitorControls);
    }

    /// <summary>
    /// Reads HDR and scaling, both of which go through the display driver.
    /// </summary>
    /// <remarks>
    /// Off the UI thread for the same reason as brightness: these are driver
    /// round trips, not property reads.
    /// </remarks>
    private async Task LoadAdvancedAsync()
    {
        DisplayInfo d = _display;

        (HdrState hdr, ScalingState scaling, DisplayDetail detail, VrrState vrr, EdidDetails edid) =
            await Task.Run(
                () => (AdvancedDisplay.ReadHdr(d), AdvancedDisplay.ReadScaling(d),
                       DisplayDetails.Read(d), VariableRefreshRate.Read(d),
                       EdidReader.Describe(d.Key.DevicePath)))
            .ConfigureAwait(false);

        _ui.TryEnqueue(() =>
        {
            _hdrReady = true;
            _edid = edid;

            _vrr = vrr;
            Raise(nameof(VrrEnabled));
            Raise(nameof(VrrVisibility));
            Raise(nameof(NoVrrVisibility));
            Raise(nameof(VrrDescription));

            _detail = detail;
            foreach (string name in new[]
            {
                nameof(ActiveSignalMode), nameof(DesktopModeText), nameof(ColorFormat),
                nameof(ColorSpace), nameof(BitDepth), nameof(HdrStatus),
                nameof(ScanLineOrdering), nameof(PixelClock), nameof(ColorProfileName),
                nameof(SummaryLine),
            })
                Raise(name);

            RaiseInformation();

            _selectedOrientation = detail.Orientation is "—" ? Orientations[0] : detail.Orientation;
            Raise(nameof(SelectedOrientation));
            _orientationReady = true;

            _hdr = hdr;
            Raise(nameof(HdrEnabled));
            Raise(nameof(HdrVisibility));
            Raise(nameof(NoHdrVisibility));
            Raise(nameof(HdrDescription));

            _scaling = scaling;
            ScalingOptions.Clear();
            foreach (int step in scaling.Available)
                ScalingOptions.Add($"{step}%");

            _selectedScaling = $"{scaling.Current}%";
            Raise(nameof(SelectedScaling));
            Raise(nameof(ScalingVisibility));
            Raise(nameof(NoScalingVisibility));

            // Only now can a selection change be attributed to the user.
            _scalingReady = true;
        });
    }

    // -------------------------------------------------------------- identity --

    /// <summary>Position in the list, matching the badge shown on the card.</summary>
    public int Number { get; }

    /// <summary>
    /// The display's name, with the machine's model beside it when it is built in.
    /// </summary>
    /// <remarks>
    /// A built-in panel has no name of its own — no DDC/CI, and usually no EDID
    /// name descriptor either — so "Internal 2880x1800" is all there was, and it
    /// says nothing about which laptop. The firmware knows: this one is an ASUS
    /// M7400QC, and that is what someone looking for their own machine would
    /// recognise.
    /// </remarks>
    public string Name => _display.IsInternal && Machine.Model.Length > 0
        ? $"{_display.Label}  ({Machine.Model})"
        : _display.Label;

    private static MachineInfo Machine => MachineInfo.Read();

    /// <summary>
    /// The one-line summary under the display's name.
    /// </summary>
    /// <remarks>
    /// The display number lives here rather than in a coloured badge beside the
    /// name. Repeated down a list, a badge per row is noise competing with the
    /// icon next to it; as the first field of the description it still answers
    /// "which one is this?" without shouting.
    /// </remarks>
    public string Details =>
        $"Display {Number}  ·  {_display.Bounds.Width} × {_display.Bounds.Height}  ·  " +
        $"{_display.RefreshHz} Hz  ·  {_display.Scale * 100:0}%  ·  {ConnectorLabel}";

    /// <summary>Short label for compact lines, e.g. the unison brightness readout.</summary>
    public string ShortName
    {
        get
        {
            string n = _display.Label;
            int space = n.IndexOf(' ');
            return _display.IsInternal ? "Built-in" : (space > 0 ? n[..space] : n);
        }
    }

    public string Role => _display.IsPrimary ? "Primary" : "Secondary";

    public string RoleAndConnection => _display.IsInternal
        ? $"{Role}  ·  built-in panel"
        : $"{Role}  ·  {ConnectorLabel}";

    public string Token => _display.Token;

    public string ScaleText => $"{_display.Scale * 100:0}%";

    public bool IsPrimary => _display.IsPrimary;

    public bool IsInternalPanel => _display.IsInternal;

    // ------------------------------------------------------------- preview --

    /// <summary>
    /// Relative size of this display's preview, 0-1 against the largest panel.
    /// </summary>
    /// <remarks>
    /// Set by the owning view model once every display is known, because
    /// "how big is this one" only means anything next to the others.
    /// </remarks>
    /// <summary>
    /// Retained so the owning view model can still report relative size, even
    /// though the preview itself no longer varies with it.
    /// </summary>
    public double PreviewScale { get; set; } = 1.0;

    private const double MaxPreviewWidth = 340;

    /// <summary>
    /// Preview width. Deliberately the same for every display.
    /// </summary>
    /// <remarks>
    /// Scaling these by physical size was tried and looked wrong: a row of
    /// cards with previews of differing sizes reads as ragged rather than
    /// informative, and the actual size is stated a few lines below as a
    /// diagonal in inches, which is how anyone describes a monitor anyway.
    /// Only the aspect ratio varies here, which is the part a picture conveys
    /// better than words.
    /// </remarks>
    public double PreviewWidth => MaxPreviewWidth;

    /// <summary>
    /// Preview height, from the panel's physical aspect ratio where known.
    /// </summary>
    /// <remarks>
    /// Falls back to the pixel aspect, which is the same on any display with
    /// square pixels — which is nearly all of them, but not a safe assumption.
    /// </remarks>
    public double PreviewHeight
    {
        get
        {
            double aspect = _display.HasPhysicalSize
                ? _display.PhysicalHeightMm / (double)_display.PhysicalWidthMm
                : _display.Bounds.Height / (double)Math.Max(1, _display.Bounds.Width);

            return Math.Round(PreviewWidth * aspect);
        }
    }

    /// <summary>Physical size, as a monitor is normally described.</summary>
    public string ScreenSizeText => _display.HasPhysicalSize
        ? $"{_display.DiagonalInches:0.0} inches  ({_display.PhysicalWidthMm} × {_display.PhysicalHeightMm} mm)"
        : "Not reported by this display";

    /// <summary>
    /// Real pixel density, which is not the same thing as the scaling factor.
    /// </summary>
    public string PixelDensityText => _display.HasPhysicalSize
        ? $"{_display.PhysicalPpi:0} PPI  ·  Windows renders at {_display.Scale * 100:0}%"
        : $"Windows renders at {_display.Scale * 100:0}%";

    /// <summary>
    /// The essentials beneath the display drawing. Keep these in the same order
    /// people use to describe a panel: pixels, rate, shape, then physical size.
    /// </summary>
    public string PreviewCaption
    {
        get
        {
            var parts = new List<string>
            {
                ResolutionText,
                RefreshText,
                AspectRatioText,
            };

            if (_display.HasPhysicalSize)
                parts.Add($"{_display.DiagonalInches:0.0}\u2033");

            return string.Join("  |  ", parts);
        }
    }

    // -------------------------------------------------------------- taskbar --

    /// <summary>
    /// False on the primary monitor, where the control is replaced by an
    /// explanation.
    /// </summary>
    /// <remarks>
    /// Not a guess: <c>SetWindowPos</c> on the primary <c>Shell_TrayWnd</c>
    /// reports success and explorer restores it within ~120ms.
    /// </remarks>
    public bool CanHideTaskbar => !_display.IsPrimary;

    public Visibility ToggleVisibility => CanHideTaskbar ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PrimaryNoteVisibility => CanHideTaskbar ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// One-line summary for the collapsed card.
    /// </summary>
    /// <remarks>
    /// The card header states what is true rather than offering a control:
    /// changing something is a deliberate act that belongs behind the expander,
    /// so a glance down the list cannot toggle anything by accident.
    /// </remarks>
    public string TaskbarSummary => !CanHideTaskbar
        ? "Taskbar always shown"
        : _settings.HideTaskbar ? "Taskbar hidden" : "Taskbar shown";

    /// <summary>
    /// Windows' own auto-hide, surfaced on the primary display's own card.
    /// </summary>
    /// <remarks>
    /// DispCtrl cannot move the primary taskbar, but Windows can — so rather than
    /// showing a dead control and pointing at another page, the primary card
    /// offers the switch that actually works for it. It is global underneath,
    /// which is why it appears only here: every other display is handled by
    /// DispCtrl's own parking, which overrides the sliver Explorer leaves.
    /// </remarks>
    public bool PrimaryAutoHide
    {
        get => GlobalTaskbar.IsAutoHide;
        set
        {
            if (GlobalTaskbar.IsAutoHide == value) return;
            GlobalTaskbar.SetAutoHide(value);
            Raise();
        }
    }

    public string HideTaskbarDescription => _display.IsPrimary
        ? "DispCtrl cannot move the primary taskbar — Explorer restores it immediately. This uses Windows' own auto-hide instead, which does work here but leaves a one-pixel lit strip along the edge."
        : "Parks the taskbar fully off this panel. No pixels stay lit, so nothing can burn in.";

    public bool HideTaskbar
    {
        get => _settings.HideTaskbar;
        set
        {
            if (_settings.HideTaskbar == value) return;
            _settings.HideTaskbar = value;
            _persist();
            Raise();
            Raise(nameof(TaskbarSummary));
        }
    }

    public bool ReclaimWorkArea
    {
        get => _settings.ReclaimWorkArea;
        set
        {
            if (_settings.ReclaimWorkArea == value) return;
            _settings.ReclaimWorkArea = value;
            _persist();
            Raise();
        }
    }

    // ----------------------------------------------------------- brightness --

    private BrightnessRange _brightness = BrightnessRange.Unsupported;
    private int _brightnessPercent;

    /// <summary>
    /// Gates hardware writes until the real value has been read back.
    /// </summary>
    /// <remarks>
    /// A two-way bound Slider starts life at 0 and can push that to the source
    /// before the asynchronous read completes. Without this gate that would be
    /// indistinguishable from the user dragging to zero, and the app would
    /// black out a monitor on startup.
    /// </remarks>
    private bool _brightnessReady;

    public bool BrightnessSupported => _brightness.Supported;

    public Visibility BrightnessVisibility =>
        _brightness.Supported ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoBrightnessVisibility =>
        _brightness.Supported ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Distinguishes this display's brightness slider from the others'.</summary>
    public string BrightnessAutomationName => $"Brightness {Number} — {_display.Label}";

    public string BrightnessBackend => _display.IsInternal
        ? "Backlight, over WMI — this panel has no DDC/CI"
        : "Over DDC/CI, on the video cable";

    public int BrightnessPercent
    {
        get => _brightnessPercent;
        set
        {
            if (_brightnessPercent == value) return;
            _brightnessPercent = value;
            Raise();

            if (!_brightnessReady || !_brightness.Supported) return;
            QueueBrightnessWrite(value);
            _deskChanged();
        }
    }

    /// <summary>
    /// Completes once the hardware brightness has been read back.
    /// </summary>
    /// <remarks>
    /// Unison has to wait on this. The two backends are wildly different in
    /// speed — WMI answers for the internal panel in milliseconds, while a
    /// DDC/CI round trip to an external monitor takes tens to hundreds. Acting
    /// before this completes means the fast display is adjusted and the slow one
    /// is silently skipped.
    /// </remarks>
    public Task BrightnessReady => _brightnessLoaded.Task;

    private readonly TaskCompletionSource _brightnessLoaded =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task LoadBrightnessAsync()
    {
        // Blocking hardware call; never on the UI thread.
        BrightnessRange range = await Task.Run(() => Brightness.Read(_display)).ConfigureAwait(false);

        _ui.TryEnqueue(() =>
        {
            _brightness = range;
            _brightnessPercent = range.Percent;
            Raise(nameof(BrightnessSupported));
            Raise(nameof(BrightnessVisibility));
            Raise(nameof(NoBrightnessVisibility));
            Raise(nameof(SoftwareBrightnessVisibility));
            Raise(nameof(BrightnessPercent));

            // Only now can a change be attributed to the user.
            _brightnessReady = true;

            // Signalled even when brightness is unsupported, so a waiter can
            // never hang on a display that has no control to read.
            _brightnessLoaded.TrySetResult();
        });
    }

    private void QueueBrightnessWrite(int percent)
    {
        _brightnessWrite?.Cancel();
        _brightnessWrite?.Dispose();
        var cts = new CancellationTokenSource();
        _brightnessWrite = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(120, cts.Token).ConfigureAwait(false);
                Brightness.Write(_display, _brightness.FromPercent(percent));
            }
            catch (OperationCanceledException)
            {
                // Superseded by a later drag position; nothing to do.
            }
        }, cts.Token);
    }

    // ---------------------------------------------------------------- modes --

    public ObservableCollection<string> Resolutions { get; } = [];
    public ObservableCollection<string> RefreshRates { get; } = [];

    private string? _selectedResolution;
    private string? _selectedRefreshRate;
    private string _modeStatus = string.Empty;
    private bool _applyingMode;

    /// <summary>
    /// Blocks mode changes until the real values have been read back.
    /// </summary>
    /// <remarks>
    /// The same hazard as brightness, and worse in consequence. Populating the
    /// resolution and refresh-rate lists raises selection changes through a
    /// two-way binding, and without this gate one of those was indistinguishable
    /// from the user picking a mode — which silently dropped an external monitor
    /// from 120Hz to 59Hz during startup.
    /// </remarks>
    private bool _modesReady;

    public string? SelectedResolution
    {
        get => _selectedResolution;
        set
        {
            if (_selectedResolution == value || value is null) return;
            _selectedResolution = value;
            Raise();
            PopulateRefreshRates();
            ApplyMode();
        }
    }

    public string? SelectedRefreshRate
    {
        get => _selectedRefreshRate;
        set
        {
            if (_selectedRefreshRate == value || value is null) return;
            _selectedRefreshRate = value;
            Raise();
            ApplyMode();
        }
    }

    public string ModeStatus
    {
        get => _modeStatus;
        private set { _modeStatus = value; Raise(); }
    }

    public Visibility ModeStatusVisibility =>
        string.IsNullOrEmpty(_modeStatus) ? Visibility.Collapsed : Visibility.Visible;

    private void PopulateRefreshRates()
    {
        if (!TryParseResolution(_selectedResolution, out uint w, out uint h)) return;

        RefreshRates.Clear();
        foreach (uint hz in _modeRates.TryGetValue((w, h), out uint[]? rates) ? rates : [])
            RefreshRates.Add($"{hz} Hz");

        // Keep the current rate selected when it survives the resolution change.
        if (_selectedRefreshRate is not null && RefreshRates.Contains(_selectedRefreshRate)) return;

        _selectedRefreshRate = RefreshRates.Count > 0 ? RefreshRates[^1] : null;
        Raise(nameof(SelectedRefreshRate));
    }

    private void ApplyMode()
    {
        // Suppressed while the lists are being rebuilt, so repopulating a combo
        // cannot be mistaken for the user choosing something.
        if (!_modesReady || _applyingMode) return;
        if (!TryParseResolution(_selectedResolution, out uint w, out uint h)) return;
        if (_selectedRefreshRate is null) return;
        if (!uint.TryParse(_selectedRefreshRate.AsSpan(0, _selectedRefreshRate.IndexOf(' ')), out uint hz)) return;

        _deskChanged();

        var target = new DisplayMode(w, h, hz, 32);
        if (target.Width == _display.Bounds.Width
            && target.Height == _display.Bounds.Height
            && target.RefreshHz == _display.RefreshHz)
            return;

        _applyingMode = true;
        string device = _display.GdiName;
        DisplayInfo info = _display;
        bool rateOnly = target.Width == _display.Bounds.Width && target.Height == _display.Bounds.Height;

        _ = Task.Run(() =>
        {
            // A rate-only change goes through the CCD API. The legacy
            // ChangeDisplaySettingsEx refuses every mode change on this
            // hardware, so the resolution path is best-effort while this one
            // is known to work.
            ModeChangeResult result = rateOnly
                ? DisplayArrangement.SetRefreshRate(info, target.RefreshHz)
                    ? ModeChangeResult.AppliedSeamlessly
                    : ModeChangeResult.Failed
                : DisplayModes.Apply(device, target);

            _ui.TryEnqueue(() =>
            {
                _applyingMode = false;
                ModeStatus = result switch
                {
                    ModeChangeResult.AppliedSeamlessly => "Refresh rate changed without a modeset.",
                    ModeChangeResult.Applied => "Display mode changed.",
                    ModeChangeResult.NotSupported => "This monitor does not support that combination.",
                    ModeChangeResult.NeedsRestart => "That mode needs a restart to take effect.",
                    _ => "Could not change the display mode.",
                };
                Raise(nameof(ModeStatusVisibility));
            });
        });
    }

    private static bool TryParseResolution(string? text, out uint width, out uint height)
    {
        width = height = 0;
        if (text is null) return false;

        int x = text.IndexOf('×');
        if (x < 0) return false;

        return uint.TryParse(text.AsSpan(0, x).Trim(), out width)
            && uint.TryParse(text.AsSpan(x + 1).Trim(), out height);
    }

    // -------------------------------------------------- advanced detail --

    private DisplayDetail _detail = new();

    /// <summary>
    /// The panel's own EDID, decoded.
    /// </summary>
    /// <remarks>
    /// Read once with the other slow values and kept. It cannot change while the
    /// monitor is attached at the same path, and it is the only source for most
    /// of what the information table shows about the panel itself.
    /// </remarks>
    private EdidDetails _edid = EdidDetails.None;

    public string ActiveSignalMode => _detail.ActiveSignalMode;
    public string DesktopModeText => _detail.DesktopMode;
    public string ColorFormat => _detail.ColorFormat;
    public string ColorSpace => _detail.ColorSpace;
    public string BitDepth => _detail.BitDepth;
    public string HdrStatus => _detail.HdrStatus;
    public string ScanLineOrdering => _detail.ScanLineOrdering;
    public string PixelClock => _detail.PixelClock;
    public string ColorProfileName => _detail.ColorProfile;

    // ------------------------------------------------------ what it is --
    // Everything the panel, its EDID and Windows will say about it. The
    // expensive reads all happen elsewhere and land in the fields these read
    // from, so every one of these is a property access.

    /// <summary>EDID manufacturer and product code, e.g. DEL-A234.</summary>
    public string ModelCode => _display.Key.Model.Length > 0 ? _display.Key.Model : "Not reported";

    /// <summary>The name the panel gives itself, which is the marketing one.</summary>
    public string ProductName => string.IsNullOrWhiteSpace(_display.FriendlyName)
        ? "Not reported"
        : _display.FriendlyName;

    /// <remarks>
    /// Shown because it is how a monitor is identified for a warranty or a
    /// support call, and because DispCtrl's own identity token is built from it —
    /// seeing both makes the token legible rather than mysterious.
    /// </remarks>
    public string SerialText => _display.Key.HasSerial ? _display.Key.Serial : "Not reported";

    public string ConnectorText => ConnectorLabel;

    /// <summary>The name Windows uses, e.g. \\.\DISPLAY1.</summary>
    /// <remarks>
    /// Transient — it is reassigned on replug — which is exactly why it is
    /// worth showing next to the token that is not.
    /// </remarks>
    public string WindowsName => _display.GdiName;

    public string ResolutionText => $"{_display.Bounds.Width} \u00d7 {_display.Bounds.Height}";

    /// <summary>The current pixel aspect ratio, reduced to the familiar form.</summary>
    public string AspectRatioText
    {
        get
        {
            uint width = (uint)Math.Max(0, _display.Bounds.Width);
            uint height = (uint)Math.Max(0, _display.Bounds.Height);
            if (width == 0 || height == 0) return "Not reported";

            uint divisor = GreatestCommonDivisor(width, height);
            return $"{width / divisor}:{height / divisor}";
        }
    }

    /// <summary>The largest mode this panel offers, which is its native one.</summary>
    public string NativeResolutionText
    {
        get
        {
            if (Resolutions.Count == 0) return "\u2014";

            string best = Resolutions[0];
            return best == ResolutionText ? $"{best}  (running natively)" : best;
        }
    }

    public string RefreshText => $"{_display.RefreshHz} Hz";

    private static uint GreatestCommonDivisor(uint left, uint right)
    {
        while (right != 0)
        {
            uint remainder = left % right;
            left = right;
            right = remainder;
        }

        return left;
    }

    public string PositionText => $"{_display.Bounds.Left}, {_display.Bounds.Top}";

    /// <remarks>
    /// Bounds minus whatever the shell has reserved. Worth showing beside the
    /// resolution: when DispCtrl reclaims a hidden taskbar's strip, this is the
    /// number that proves it worked.
    /// </remarks>
    public string WorkAreaText =>
        $"{_display.WorkArea.Width} \u00d7 {_display.WorkArea.Height}  at  {_display.WorkArea.Left}, {_display.WorkArea.Top}";

    public string OrientationText => _display.OrientationDegrees switch
    {
        0 => "Landscape",
        90 => "Portrait  (rotated 90\u00b0)",
        180 => "Landscape, flipped  (180\u00b0)",
        270 => "Portrait, flipped  (270\u00b0)",
        _ => $"{_display.OrientationDegrees}\u00b0",
    };

    public string ColorDepthText => $"{_display.BitsPerPixel}-bit colour";

    public string DpiText => $"{_display.Dpi} DPI  \u00b7  {_display.Scale * 100:0}% scaling";

    public string VrrRangeText => _vrr.Capable
        ? $"{_vrr.Range}  \u00b7  {(_vrr.Enabled ? "on" : "off")}"
        : "Not advertised";

    public string PrimaryText => _display.IsPrimary ? "Yes" : "No";

    /// <summary>Whether the monitor answers DDC/CI, and what it said.</summary>
    public string DdcText
    {
        get
        {
            if (_display.IsInternal) return "Built-in panels have no DDC/CI channel";
            if (!_capabilitiesRead) return "Asking\u2026";

            return _reportedControls > 0
                ? $"Answers  \u00b7  {_reportedControls} controls reported, {MonitorControls.Count} offered here"
                : "No answer";
        }
    }

    // ------------------------------------------------ what the panel says --
    // Straight from the EDID. Windows surfaces almost none of this, and several
    // of them are the only answer there is: when the panel was built, what its
    // glass actually is, and what it will take on its cable.

    private const string Unstated = "Not stated by the panel";

    /// <summary>The machine a built-in panel is part of, or why the row is blank.</summary>
    /// <remarks>
    /// Only meaningful for an internal display: an external monitor is attached
    /// to this PC, not built into it, and saying "built into an M7400QC" about a
    /// Dell on the end of a cable would be false.
    /// </remarks>
    public string BuiltIntoText
    {
        get
        {
            if (!_display.IsInternal) return "Not a built-in panel";
            if (!Machine.Present) return "The firmware does not say";

            string label = Machine.Label;
            return Machine.BiosVersion.Length > 0
                ? $"{label}  ·  BIOS {Machine.BiosVersion}"
                  + (Machine.BiosDate.Length > 0 ? $" ({Machine.BiosDate})" : "")
                : label;
        }
    }

    public string ManufacturerText => !_edid.Present
        ? Unstated
        : _edid.ManufacturerName.Length > 0
            ? $"{_edid.ManufacturerName}  ·  {_edid.ManufacturerCode}"
            : $"{_edid.ManufacturerCode}  ·  no name on record for this code";

    /// <remarks>
    /// Week and year, or a model year. EDID 1.4 lets a panel give either, and
    /// the two say different things: one is when this unit was built, the other
    /// when the design was current.
    /// </remarks>
    public string MadeText => _edid.Present ? _edid.Made : Unstated;

    /// <summary>The free-text descriptor, which is usually the panel's part number.</summary>
    public string PanelPartText => _edid.FreeText ?? Unstated;

    public string EdidSignalText
    {
        get
        {
            if (!_edid.Present) return Unstated;
            if (!_edid.Digital) return $"Analogue  ·  {_edid.AnalogueSignal}";

            var parts = new List<string> { "Digital" };
            if (_edid.BitsPerColour > 0) parts.Add($"{_edid.BitsPerColour} bits per colour");
            if (_edid.Interface.Length > 0) parts.Add(_edid.Interface);

            return parts.Count > 1
                ? string.Join("  ·  ", parts)
                : "Digital  ·  EDID 1.3 records no depth or interface";
        }
    }

    public string ColourEncodingsText =>
        _edid.Present && _edid.ColourEncodings.Length > 0 ? _edid.ColourEncodings : Unstated;

    public string GammaText => _edid.Gamma > 0 ? $"{_edid.Gamma:0.00}" : Unstated;

    public string SrgbText => _edid.Present ? (_edid.SrgbDefault ? "Yes" : "No") : Unstated;

    public string ContinuousFrequencyText => !_edid.Present
        ? Unstated
        : _edid.ContinuousFrequency
            ? "Yes  ·  accepts rates it was not given"
            : "No  ·  only the modes it lists";

    public string DpmsText
    {
        get
        {
            if (!_edid.Present) return Unstated;

            var modes = new List<string>(3);
            if (_edid.Standby) modes.Add("standby");
            if (_edid.Suspend) modes.Add("suspend");
            if (_edid.ActiveOff) modes.Add("active off");

            return modes.Count > 0 ? string.Join(", ", modes) : "None advertised";
        }
    }

    /// <summary>The panel's preferred detailed timing, which is its native mode.</summary>
    public string NativeTimingText =>
        _edid.DetailedTimings.Count > 0 ? _edid.DetailedTimings[0] : Unstated;

    public string VerticalRangeText =>
        _edid.MaxVerticalHz > 0 ? $"{_edid.MinVerticalHz}–{_edid.MaxVerticalHz} Hz" : Unstated;

    public string HorizontalRangeText =>
        _edid.MaxHorizontalKHz > 0 ? $"{_edid.MinHorizontalKHz}–{_edid.MaxHorizontalKHz} kHz" : Unstated;

    public string MaxPixelClockText =>
        _edid.MaxPixelClockMHz > 0 ? $"{_edid.MaxPixelClockMHz} MHz" : Unstated;

    public string EdidVersionText => _edid.Present
        ? $"{_edid.Version}  ·  checksum {(_edid.ChecksumValid ? "valid" : "does not add up")}"
        : "Windows has none cached for this display";

    public string EdidBlocksText => _edid.Present
        ? $"{_edid.Bytes} bytes  ·  {_edid.Extensions} extension block(s) declared"
        : Unstated;

    public string DescriptorsText =>
        _edid.Descriptors.Count > 0 ? string.Join(", ", _edid.Descriptors) : Unstated;

    // ----------------------------------------------- what DDC/CI reports --

    /// <summary>The value a read-only VCP code answered, in words.</summary>
    private string FromVcp(byte code)
    {
        if (_display.IsInternal) return "Built-in panels have no DDC/CI channel";
        if (!_capabilitiesRead) return "Asking…";

        foreach (VcpControl c in _allControls)
            if (c.Code == code) return c.Display;

        return "Not reported by this monitor";
    }

    /// <remarks>
    /// VCP 0xC9. The only place a monitor states its own firmware level, and the
    /// thing to quote when a panel misbehaves in a way a later revision fixed.
    /// </remarks>
    public string FirmwareText => FromVcp(0xC9);

    public string HoursInUseText => FromVcp(0xC0);

    public string ControllerText => FromVcp(0xC8);

    public string MccsText
    {
        get
        {
            if (_display.IsInternal) return "Built-in panels have no DDC/CI channel";
            if (!_capabilitiesRead) return "Asking…";

            return _mccsVersion is { Length: > 0 } v ? v : "Not reported by this monitor";
        }
    }

    /// <summary>The low-level DDC/CI opcodes the monitor says it accepts.</summary>
    public string CommandsText
    {
        get
        {
            if (_display.IsInternal) return "Built-in panels have no DDC/CI channel";
            if (!_capabilitiesRead) return "Asking…";

            return _commands.Count > 0 ? string.Join("  ", _commands) : "None listed";
        }
    }

    /// <summary>Everything above, raised together after a rescan.</summary>
    private void RaiseInformation()
    {
        Raise(nameof(ModelCode));
        Raise(nameof(ProductName));
        Raise(nameof(SerialText));
        Raise(nameof(ConnectorText));
        Raise(nameof(WindowsName));
        Raise(nameof(ResolutionText));
        Raise(nameof(NativeResolutionText));
        Raise(nameof(RefreshText));
        Raise(nameof(AspectRatioText));
        Raise(nameof(PreviewCaption));
        Raise(nameof(PositionText));
        Raise(nameof(WorkAreaText));
        Raise(nameof(OrientationText));
        Raise(nameof(ColorDepthText));
        Raise(nameof(DpiText));
        Raise(nameof(VrrRangeText));
        Raise(nameof(PrimaryText));
        Raise(nameof(DdcText));
        Raise(nameof(PanelTechnology));
        Raise(nameof(ScreenSizeText));
        Raise(nameof(PixelDensityText));
        Raise(nameof(ColorProfileName));

        // The table reads from these, so it is rebuilt by the same signal that
        // says any of them changed. One raise rather than a row apiece: a value
        // arriving late is the normal case here, and every field lands in the
        // same pass anyway.
        Raise(nameof(Information));
    }

    /// <summary>
    /// Everything the panel, its EDID and Windows will say about it, as a table.
    /// </summary>
    /// <remarks>
    /// Assembled here rather than written out in markup so that what the copy
    /// button puts on the clipboard is what the screen shows, by construction.
    /// The order is the order someone reads it in: what the panel is, how it is
    /// attached, what it is showing, then the identifiers that only matter when
    /// something has gone wrong.
    /// </remarks>
    public IReadOnlyList<InfoRow> Information =>
    [
        InfoRow.Heading("The panel"),
        InfoRow.Of("Model", ProductName),
        InfoRow.Of("Manufacturer", ManufacturerText),
        InfoRow.Of("Manufacturer and product", ModelCode),
        InfoRow.Identifier("Serial", SerialText),
        InfoRow.Of("Made", MadeText),
        InfoRow.Of("Panel part", PanelPartText),
        InfoRow.Of("Screen size", ScreenSizeText),
        InfoRow.Of("Pixel density", PixelDensityText),
        InfoRow.Of("Panel technology", PanelTechnology),
        InfoRow.Of("Built into", BuiltIntoText),
        InfoRow.Of("Firmware", FirmwareText),
        InfoRow.Of("Hours in use", HoursInUseText),
        InfoRow.Of("Controller", ControllerText),

        InfoRow.Heading("Connection"),
        InfoRow.Of("Connector", ConnectorText),
        InfoRow.Of("Panel declares", EdidSignalText),
        InfoRow.Of("Main display", PrimaryText),
        InfoRow.Of("Active signal mode", ActiveSignalMode),
        InfoRow.Of("Pixel clock", PixelClock),
        InfoRow.Of("Scan type", ScanLineOrdering),
        InfoRow.Of("DDC/CI", DdcText),
        InfoRow.Of("MCCS version", MccsText),
        InfoRow.Of("Low-level commands", CommandsText),

        InfoRow.Heading("Picture"),
        InfoRow.Of("Resolution", ResolutionText),
        InfoRow.Of("Aspect ratio", AspectRatioText),
        InfoRow.Of("Native resolution", NativeResolutionText),
        InfoRow.Of("Refresh rate", RefreshText),
        InfoRow.Of("Variable refresh", VrrRangeText),
        InfoRow.Of("Scaling", DpiText),
        InfoRow.Of("Orientation", OrientationText),
        InfoRow.Of("Desktop mode", DesktopModeText),

        InfoRow.Heading("Picture, as the panel describes it"),
        InfoRow.Of("Native timing", NativeTimingText),
        InfoRow.Of("Vertical range", VerticalRangeText),
        InfoRow.Of("Horizontal range", HorizontalRangeText),
        InfoRow.Of("Max pixel clock", MaxPixelClockText),
        InfoRow.Of("Continuous frequency", ContinuousFrequencyText),
        InfoRow.Of("Power saving", DpmsText),

        InfoRow.Heading("Colour"),
        InfoRow.Of("Bit depth", BitDepth),
        InfoRow.Of("Colour depth", ColorDepthText),
        InfoRow.Of("Colour format", ColorFormat),
        InfoRow.Of("Colour space", ColorSpace),
        InfoRow.Of("Colour profile", ColorProfileName),
        InfoRow.Of("HDR status", HdrStatus),
        InfoRow.Of("Colour encodings", ColourEncodingsText),
        InfoRow.Of("Gamma", GammaText),
        InfoRow.Of("sRGB is the default", SrgbText),
        InfoRow.Of("Red primary", _edid.Red.ToString()),
        InfoRow.Of("Green primary", _edid.Green.ToString()),
        InfoRow.Of("Blue primary", _edid.Blue.ToString()),
        InfoRow.Of("White point", _edid.White.ToString()),

        InfoRow.Heading("Placement"),
        InfoRow.Of("Position", PositionText),
        InfoRow.Of("Work area", WorkAreaText),

        InfoRow.Heading("How DispCtrl knows it"),
        InfoRow.Identifier("Windows name", WindowsName),
        InfoRow.Identifier("Identity", Token),
        InfoRow.Of("EDID version", EdidVersionText),
        InfoRow.Of("EDID blocks", EdidBlocksText),
        InfoRow.Of("Descriptor blocks", DescriptorsText),
    ];

    /// <summary>The whole table as plain text, for the copy button.</summary>
    /// <remarks>
    /// Headed by the display's name, because a block of values pasted into a
    /// message says nothing about which monitor answered them.
    /// </remarks>
    public string InformationText
    {
        get
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"{Name}  ({Details})");
            sb.AppendLine();

            foreach (InfoRow row in Information)
            {
                if (row.IsSection) sb.AppendLine();
                sb.AppendLine(row.AsText);
            }

            return sb.ToString();
        }
    }

    /// <summary>Distinct per display, so a test can name which button it means.</summary>
    public string CopyInfoAutomationName => $"Copy details {Number}";

    /// <summary>
    /// The at-a-glance line on the collapsed card.
    /// </summary>
    /// <remarks>
    /// Replaces the bare "Taskbar hidden" that used to sit here. A row in a
    /// list should answer "what is this display doing?", and the taskbar is
    /// only one small part of that — so the signal, colour depth and HDR state
    /// lead, with the taskbar noted only when DispCtrl is actually managing it.
    /// </remarks>
    public string SummaryLine
    {
        get
        {
            var parts = new List<string>(4);
            if (_display.IsPrimary) parts.Add("Main");
            if (_brightness.Supported) parts.Add($"{_brightnessPercent}% brightness");
            if (_detail.HdrStatus is "On") parts.Add("HDR on");
            if (_settings.HideTaskbar && CanHideTaskbar) parts.Add("Taskbar hidden");
            return parts.Count == 0 ? string.Empty : string.Join("  ·  ", parts);
        }
    }

    // ------------------------------------------------------------ orientation --

    public ObservableCollection<string> Orientations { get; } =
        ["Landscape", "Portrait", "Landscape (flipped)", "Portrait (flipped)"];

    private string? _selectedOrientation;
    private bool _orientationReady;

    public string? SelectedOrientation
    {
        get => _selectedOrientation;
        set
        {
            if (_selectedOrientation == value || value is null) return;
            _selectedOrientation = value;
            Raise();

            if (!_orientationReady) return;

            var target = (ScreenOrientation)Orientations.IndexOf(value);
            DisplayInfo d = _display;
            _ = Task.Run(() => DisplayArrangement.SetOrientation(d, target));
            _deskChanged();
        }
    }

    // -------------------------------------------------------------- primary --

    public bool CanSetPrimary => !_display.IsPrimary;

    public Visibility SetPrimaryVisibility => CanSetPrimary ? Visibility.Visible : Visibility.Collapsed;

    public Visibility IsPrimaryVisibility => CanSetPrimary ? Visibility.Collapsed : Visibility.Visible;

    public DisplayInfo Info => _display;

    // ------------------------------------------------------------------ HDR --

    private HdrState _hdr = HdrState.Unsupported;

    /// <summary>
    /// Blocks HDR writes until the real state has been read back.
    /// </summary>
    /// <remarks>
    /// Same hazard as brightness and display mode: a two-way bound
    /// ToggleSwitch settles to a value during load, and without a gate that is
    /// indistinguishable from the user flipping it. Turning HDR on unasked
    /// changes how every SDR application is tone-mapped, so it is worth the
    /// same protection.
    /// </remarks>
    private bool _hdrReady;

    public Visibility HdrVisibility => _hdr.Supported ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoHdrVisibility => _hdr.Supported ? Visibility.Collapsed : Visibility.Visible;

    public string HdrDescription => _hdr.Supported
        ? $"{_hdr.BitsPerChannel}-bit per channel. Turning HDR on changes how SDR content is tone-mapped."
        : "This display does not report HDR support.";

    public bool HdrEnabled
    {
        get => _hdr.Enabled;
        set
        {
            if (!_hdrReady || _hdr.Enabled == value || !_hdr.Supported) return;
            _hdr = _hdr with { Enabled = value };
            Raise();
            _deskChanged();

            DisplayInfo d = _display;
            _ = Task.Run(() =>
            {
                AdvancedDisplay.WriteHdr(d, value);
                HdrState after = AdvancedDisplay.ReadHdr(d);
                _ui.TryEnqueue(() =>
                {
                    // Read back rather than assume: a display can refuse.
                    _hdr = after;
                    Raise(nameof(HdrEnabled));
                });
            });
        }
    }

    // ------------------------------------------------------------------ VRR --

    private VrrState _vrr = VrrState.Unsupported;

    public Visibility VrrVisibility => _vrr.Capable ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoVrrVisibility => _vrr.Capable ? Visibility.Collapsed : Visibility.Visible;

    public string VrrDescription => _vrr.Capable
        ? $"This panel advertises {_vrr.Range}. Windows applies variable refresh rate globally, not per display."
        : "This panel does not advertise a variable refresh range.";

    public bool VrrEnabled
    {
        get => _vrr.Enabled;
        set
        {
            if (_vrr.Enabled == value) return;
            _vrr = _vrr with { Enabled = value };
            Raise();
            _ = Task.Run(() => VariableRefreshRate.SetEnabled(value));
        }
    }

    // -------------------------------------------------------------- scaling --

    private ScalingState _scaling = ScalingState.Unsupported;
    private string? _selectedScaling;
    private bool _scalingReady;

    public ObservableCollection<string> ScalingOptions { get; } = [];

    public Visibility ScalingVisibility => _scaling.Supported ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoScalingVisibility => _scaling.Supported ? Visibility.Collapsed : Visibility.Visible;

    public string? SelectedScaling
    {
        get => _selectedScaling;
        set
        {
            if (_selectedScaling == value || value is null) return;
            _selectedScaling = value;
            Raise();

            if (!_scalingReady) return;
            if (!int.TryParse(value.TrimEnd('%'), out int percent)) return;

            DisplayInfo d = _display;
            _ = Task.Run(() => AdvancedDisplay.WriteScaling(d, percent));
            _deskChanged();
        }
    }

    // ------------------------------------------------------------ wallpaper --

    private string? _wallpaperPath;
    private ImageSource? _wallpaperImage;

    public ImageSource? WallpaperImage
    {
        get => _wallpaperImage;
        private set { _wallpaperImage = value; Raise(); Raise(nameof(HasWallpaper)); Raise(nameof(NoWallpaperVisibility)); }
    }

    public Visibility HasWallpaper => _wallpaperImage is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility NoWallpaperVisibility => _wallpaperImage is null ? Visibility.Visible : Visibility.Collapsed;

    public string WallpaperName => _wallpaperPath is null
        ? "No wallpaper set for this display"
        : Path.GetFileName(_wallpaperPath);

    public async Task PickWallpaperAsync(nint windowHandle)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, windowHandle);
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".bmp");

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null) return;

        DisplayInfo d = _display;
        string path = file.Path;
        await Task.Run(() => Wallpaper.Write(d, path)).ConfigureAwait(true);
        LoadWallpaper();
    }

    private void LoadWallpaper()
    {
        _wallpaperPath = Wallpaper.Read(_display);
        Raise(nameof(WallpaperName));

        if (_wallpaperPath is null || !File.Exists(_wallpaperPath))
        {
            WallpaperImage = null;
            return;
        }

        _ = DecodeWallpaperAsync(_wallpaperPath);
    }

    /// <summary>
    /// Decodes the wallpaper thumbnail from a stream rather than a URI.
    /// </summary>
    /// <remarks>
    /// The active wallpaper is usually Windows' own <c>TranscodedWallpaper</c>,
    /// which is a JPEG with no file extension. Handing that path to
    /// <c>UriSource</c> relies on the decoder sniffing the content; opening the
    /// file and decoding the stream does not.
    /// <para>
    /// Decoded small on purpose — this is a 96px thumbnail beside a settings
    /// row, and decoding a 4K image at full size for it would be wasteful.
    /// </para>
    /// </remarks>
    private async Task DecodeWallpaperAsync(string path)
    {
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(true);

            var stream = new InMemoryRandomAccessStream();
            using (DataWriter writer = new(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
            }

            var bitmap = new BitmapImage { DecodePixelWidth = 320 };
            await bitmap.SetSourceAsync(stream);
            WallpaperImage = bitmap;
        }
        catch (Exception)
        {
            // An unreadable or exotic format degrades to the filename only.
            WallpaperImage = null;
        }
    }

    // ------------------------------------------------------------------ fit --

    public ObservableCollection<string> WallpaperFits { get; } =
        ["Fill", "Fit", "Stretch", "Tile", "Centre", "Span"];

    private string? _selectedFit;
    private bool _fitReady;

    public string? SelectedWallpaperFit
    {
        get => _selectedFit;
        set
        {
            if (_selectedFit == value || value is null) return;
            _selectedFit = value;
            Raise();

            if (!_fitReady) return;

            WallpaperFit fit = value switch
            {
                "Fit" => WallpaperFit.Fit,
                "Stretch" => WallpaperFit.Stretch,
                "Tile" => WallpaperFit.Tile,
                "Centre" => WallpaperFit.Center,
                "Span" => WallpaperFit.Span,
                _ => WallpaperFit.Fill,
            };

            _ = Task.Run(() => Wallpaper.WriteFit(fit));
        }
    }

    /// <summary>
    /// Warns when a wallpaper change will hit every display, not just this one.
    /// </summary>
    /// <remarks>
    /// Per-monitor wallpaper needs <c>IDesktopWallpaper</c>, whose COM class
    /// does not activate on every machine. Saying so beats a control that
    /// quietly does something broader than it implies.
    /// </remarks>
    public Visibility SystemWideWallpaperNotice =>
        Wallpaper.SupportsPerMonitor ? Visibility.Collapsed : Visibility.Visible;

    private void LoadFit()
    {
        _selectedFit = Wallpaper.ReadFit() switch
        {
            WallpaperFit.Fit => "Fit",
            WallpaperFit.Stretch => "Stretch",
            WallpaperFit.Tile => "Tile",
            WallpaperFit.Center => "Centre",
            WallpaperFit.Span => "Span",
            _ => "Fill",
        };
        _fitReady = true;
    }

    // ---------------------------------------------------------------- reset --

    /// <summary>Returns this display's DispCtrl settings to their defaults.</summary>
    /// <remarks>
    /// Deliberately does not touch resolution, refresh rate, HDR or scaling.
    /// Those belong to Windows, not to DispCtrl, and silently rewriting them would
    /// be far more destructive than the word "reset" implies.
    /// </remarks>
    /// <summary>
    /// Puts this display back to defaults — DispCtrl's, and the monitor's own.
    /// </summary>
    /// <remarks>
    /// Resetting DispCtrl's settings alone looked like a button that did nothing,
    /// because the settings it clears are mostly invisible while the things the
    /// user had actually changed — contrast, picture mode, colour preset — sat
    /// untouched. Those belong to the monitor, and the monitor has a standard
    /// command for restoring them, so this sends it.
    /// <para>
    /// Resolution, refresh rate, HDR and scaling still belong to Windows and are
    /// deliberately left alone: they are not DispCtrl's to reset, and changing them
    /// here would blank the screen for something nobody asked for.
    /// </para>
    /// </remarks>
    public void ResetToDefaults()
    {
        _settings.ResetToDefaults();
        _persist();

        DisplayInfo d = _display;
        if (!d.IsInternal)
        {
            _ = Task.Run(async () =>
            {
                bool ok = MonitorCapabilities.RestoreFactory(d);
                if (!ok) return;

                // The monitor needs a moment to settle before its new values
                // read back correctly.
                await Task.Delay(1200).ConfigureAwait(false);
                _ui.TryEnqueue(() => _ = LoadMonitorControlsAsync());
            });
        }

        _deskChanged();

        Raise(nameof(HideTaskbar));
        Raise(nameof(ReclaimWorkArea));
        Raise(nameof(TaskbarSummary));
        Raise(nameof(BrightnessFloor));
        Raise(nameof(BrightnessCeiling));
        Raise(nameof(HasBrightnessRange));
        Raise(nameof(RangeSummary));
    }

    // --------------------------------------------------------------- unison --

    /// <summary>The level this display sits at when unison is at 100%.</summary>
    public int BrightnessBaseline
    {
        get => _settings.BrightnessBaseline;
        set { _settings.BrightnessBaseline = value; _persist(); }
    }

    public bool SupportsBrightness => _brightness.Supported;

    /// <summary>
    /// Captures the current level as the unison baseline.
    /// </summary>
    /// <remarks>
    /// Waits for the hardware read, and refuses to record a baseline of zero.
    /// Zero is the one value that cannot be scaled back out of: every later
    /// level multiplies it, so a display captured at zero stays dark no matter
    /// where the unison slider goes. That is the trap this guard exists for.
    /// </remarks>
    public async Task CaptureBaselineAsync()
    {
        await BrightnessReady.ConfigureAwait(true);
        if (!_brightness.Supported) return;

        if (_brightnessPercent > 0)
        {
            BrightnessBaseline = _brightnessPercent;
        }
        else if (_settings.BrightnessBaseline <= 0)
        {
            // Genuinely sitting at zero with nothing remembered. Full
            // brightness is the only baseline that leaves the slider useful.
            BrightnessBaseline = 100;
        }
    }

    /// <summary>
    /// Applies a unison multiplier against this display's own baseline.
    /// </summary>
    /// <remarks>
    /// Relative rather than absolute, which is the whole point: two panels with
    /// different peak brightness stay in the relation the user set them to
    /// while they dim together.
    /// </remarks>
    public async Task ApplyUnisonAsync(double factor)
    {
        // Without this wait, a slow DDC/CI display is skipped while a fast WMI
        // one is adjusted — which looks exactly like unison only working on the
        // built-in panel.
        await BrightnessReady.ConfigureAwait(true);
        if (!_brightness.Supported) return;

        if (_settings.HasBrightnessRange)
        {
            ApplyLevel(_settings.BrightnessFloor
                + (int)Math.Round((_settings.BrightnessCeiling - _settings.BrightnessFloor) * factor));
            return;
        }

        int baseline = _settings.BrightnessBaseline;
        if (baseline <= 0)
        {
            // No usable baseline — recover one rather than multiplying by zero
            // and pinning the display dark.
            baseline = _brightnessPercent > 0 ? _brightnessPercent : 100;
            BrightnessBaseline = baseline;
        }

        ApplyLevel((int)Math.Round(baseline * factor));
    }

    private void ApplyLevel(int percent)
    {
        int target = Math.Clamp(percent, 0, 100);

        _brightnessPercent = target;
        Raise(nameof(BrightnessPercent));
        QueueBrightnessWrite(target);
    }

    // ---------------------------------------------------------- calibration --

    /// <summary>The dimmest level unison will take this display to; -1 if unset.</summary>
    public int BrightnessFloor => _settings.BrightnessFloor;

    /// <summary>The brightest level unison will take this display to; -1 if unset.</summary>
    public int BrightnessCeiling => _settings.BrightnessCeiling;

    public bool HasBrightnessRange => _settings.HasBrightnessRange;

    /// <summary>Where this display's calibrated limits stand, for the summary line.</summary>
    public string RangeSummary => _settings.HasBrightnessRange
        ? $"{Number}: {_settings.BrightnessFloor}–{_settings.BrightnessCeiling}%"
        : $"{Number}: not set";

    /// <summary>
    /// Records where the user has just left this display as one end of its
    /// unison range.
    /// </summary>
    /// <remarks>
    /// Reads the level the app already holds rather than going back to the
    /// hardware. The user set it through this same slider moments ago, and a
    /// fresh DDC/CI read would sometimes answer with the pre-write value —
    /// capturing a limit the display is no longer at.
    /// </remarks>
    public async Task CaptureLimitAsync(bool upper)
    {
        await BrightnessReady.ConfigureAwait(true);
        if (!_brightness.Supported) return;

        if (upper) _settings.BrightnessCeiling = _brightnessPercent;
        else _settings.BrightnessFloor = _brightnessPercent;

        _persist();

        Raise(nameof(BrightnessFloor));
        Raise(nameof(BrightnessCeiling));
        Raise(nameof(HasBrightnessRange));
        Raise(nameof(RangeSummary));
    }

    // ----------------------------------------------- software brightness --

    /// <summary>
    /// Brightness for a panel that reports no hardware control.
    /// </summary>
    /// <remarks>
    /// Offered only where the real thing is unavailable. Where a panel has a
    /// backlight DispCtrl can reach, dimming in software instead would be strictly
    /// worse — same light output, less contrast — so the two are never both on
    /// screen.
    /// <para>
    /// Written to settings and applied by the engine, which owns the gamma ramp
    /// and composes this with night light. The panel writing the ramp itself is
    /// the bug that made warmth compound until it could not be undone.
    /// </para>
    /// </remarks>
    public Visibility SoftwareBrightnessVisibility =>
        _brightnessLoaded.Task.IsCompleted && !_brightness.Supported
            ? Visibility.Visible : Visibility.Collapsed;

    public double SoftwareBrightness
    {
        get => _settings.SoftwareBrightness;
        set
        {
            int v = Math.Clamp((int)value, NightLight.MinimumDim, 100);
            if (_settings.SoftwareBrightness == v) return;

            _settings.SoftwareBrightness = v;
            _persist();
            Raise();
            Raise(nameof(SoftwareBrightnessText));
            _deskChanged();
        }
    }

    public string SoftwareBrightnessDescription =>
        "This display reports no brightness control, so this dims the signal rather than the backlight. "
        + "The panel emits the same light, and contrast falls as it goes down." + SoftwareBrightnessLimitNote;

    public string SoftwareBrightnessText => _settings.SoftwareBrightness >= 100
        ? "Full"
        : $"{_settings.SoftwareBrightness}%";

    public string SoftwareBrightnessAutomationName => $"Software brightness {Number}";

    /// <summary>
    /// How far this display can be dimmed at the warmth currently set.
    /// </summary>
    /// <remarks>
    /// Warmth and dimming share one gamma ramp and Windows refuses a ramp that
    /// strays too far from the identity, so the two compete: a warm screen has
    /// less room left to dim. Binding the slider's floor to the measured limit
    /// means it never offers a level that silently does nothing.
    /// </remarks>
    public double SoftwareBrightnessMinimum =>
        NightLight.LowestDim(_root.NightLightStrengthFor(Token));

    public string SoftwareBrightnessLimitNote
    {
        get
        {
            int floor = (int)SoftwareBrightnessMinimum;
            if (floor <= NightLight.MinimumDim + 1) return "";

            return floor >= 100
                ? "  Night light is at full warmth, which uses up the whole gamma range — there is none left to dim with."
                : $"  Night light is using part of the gamma range, so this stops at {floor}%.";
        }
    }

    // ------------------------------------- Windows settings for this panel --

    /// <summary>
    /// Machine-wide display settings that only affect the built-in panel.
    /// </summary>
    /// <remarks>
    /// Adaptive brightness and auto-rotation are Windows settings rather than
    /// monitor ones, but both act on the built-in screen and nothing else: the
    /// ambient light sensor drives the laptop's backlight, and the accelerometer
    /// rotates the laptop's panel. Shown on that display's card because that is
    /// where anyone would look for them, with wording that says plainly they are
    /// Windows' and machine-wide.
    /// </remarks>
    public Visibility MachineSettingsVisibility =>
        _display.IsInternal ? Visibility.Visible : Visibility.Collapsed;

    private AdaptiveBrightnessState _adaptive = AdaptiveBrightnessState.Unsupported;
    private AutoRotationState _rotation = AutoRotationState.Unsupported("");

    /// <summary>Gate: a two-way toggle writes its own value back as it is realised.</summary>
    private bool _adaptiveReady;

    public Visibility AdaptiveVisibility =>
        _display.IsInternal && _adaptive.Supported ? Visibility.Visible : Visibility.Collapsed;

    public bool AdaptiveOn
    {
        get => _adaptive.Enabled;
        set
        {
            if (!_adaptiveReady || !_adaptive.Supported || _adaptive.Enabled == value) return;

            _ = Task.Run(() =>
            {
                AdaptiveBrightness.Write(value);
                AdaptiveBrightnessState after = AdaptiveBrightness.Read();

                _ui.TryEnqueue(() =>
                {
                    _adaptive = after;
                    Raise(nameof(AdaptiveOn));
                    Raise(nameof(AdaptiveDetail));
                });
            });
        }
    }

    public string AdaptiveDetail
    {
        get
        {
            string state = (_adaptive.PluggedIn, _adaptive.OnBattery) switch
            {
                (true, true) => "On, plugged in and on battery",
                (true, false) => "On when plugged in, off on battery",
                (false, true) => "On when on battery, off when plugged in",
                _ => "Off",
            };

            return $"{state}. A Windows power setting, driven by the ambient light sensor \u2014 "
                 + "which is why it is not among the monitor's own controls.";
        }
    }

    private bool _rotationReady;

    public Visibility RotationVisibility =>
        _display.IsInternal && _rotation.Supported ? Visibility.Visible : Visibility.Collapsed;

    public bool RotationOn
    {
        get => _rotation.Enabled;
        set
        {
            if (!_rotationReady || !_rotation.Supported || _rotation.Enabled == value) return;

            _ = Task.Run(() =>
            {
                AutoRotation.Write(value);
                AutoRotationState after = AutoRotation.Read();

                // Read back rather than assumed: the prompt can be dismissed,
                // and a toggle that moves when the setting did not is worse
                // than one that refuses to move.
                _ui.TryEnqueue(() =>
                {
                    _rotation = after;
                    Raise(nameof(RotationOn));
                    Raise(nameof(RotationDetail));
                });
            });
        }
    }

    public string RotationDetail =>
        $"{_rotation.Reason} The setting lives in a machine-wide key, so changing it asks for "
        + "administrator rights once \u2014 the rest of DispCtrl never needs them.";

    private void LoadMachineSettings()
    {
        if (!_display.IsInternal) return;

        _ = Task.Run(() =>
        {
            AdaptiveBrightnessState adaptive = AdaptiveBrightness.Read();
            AutoRotationState rotation = AutoRotation.Read();

            _ui.TryEnqueue(() =>
            {
                _adaptive = adaptive;
                _rotation = rotation;

                Raise(nameof(AdaptiveOn));
                Raise(nameof(AdaptiveDetail));
                Raise(nameof(AdaptiveVisibility));
                Raise(nameof(RotationOn));
                Raise(nameof(RotationDetail));
                Raise(nameof(RotationVisibility));

                _adaptiveReady = true;
                _rotationReady = true;
            });
        });
    }

    // ----------------------------------------------- the monitor's own controls --

    /// <summary>
    /// Controls the panel itself reports, discovered rather than assumed.
    /// </summary>
    /// <remarks>
    /// Populated from the monitor's MCCS capabilities string, so the list is
    /// exactly what this panel has — contrast, colour presets, RGB gains,
    /// sharpness, which input it is showing, its OSD language. None of it is
    /// reachable anywhere in Windows.
    /// </remarks>
    public ObservableCollection<MonitorControlViewModel> MonitorControls { get; } = [];

    public Visibility MonitorControlsVisibility =>
        MonitorControls.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoMonitorControlsVisibility =>
        _capabilitiesRead && MonitorControls.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public string MonitorControlsSummary => _capabilitiesRead
        ? (MonitorControls.Count > 0
            ? $"{MonitorControls.Count} of {_reportedControls} controls this panel reports over DDC/CI"
            : _display.IsInternal
                ? "A built-in panel has no DDC/CI channel."
                : "This monitor answered no capabilities string.")
        : "Asking the monitor what it supports…";

    /// <summary>
    /// The controls the panel reported that are not offered here.
    /// </summary>
    /// <remarks>
    /// Named rather than hidden. A monitor reports a great deal DispCtrl will not
    /// write — read-only facts like firmware level, and manufacturer-specific
    /// codes whose meaning is undocumented — and silently dropping them would
    /// make the list look like everything the panel has. Saying how many there
    /// are, and where to read them, is what turns an unrecognised control into
    /// something that can be looked into rather than something nobody knew was
    /// there.
    /// </remarks>
    public string UnofferedSummary
    {
        get
        {
            int rest = _reportedControls - MonitorControls.Count;
            if (!_capabilitiesRead || rest <= 0) return "";

            return $"{rest} more are reported but not offered: read-only facts, and codes specific to "
                 + "this manufacturer that DispCtrl will not write blind. All of them, with their current "
                 + "values, are in the display report.";
        }
    }

    public Visibility UnofferedVisibility =>
        UnofferedSummary.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    private bool _capabilitiesRead;
    private int _reportedControls;

    /// <summary>
    /// Every control the monitor listed, not only the ones offered as controls.
    /// </summary>
    /// <remarks>
    /// The information table reads firmware level, hours in use and controller
    /// type out of here. All three are read-only codes, so none of them reaches
    /// <see cref="MonitorControls"/> — and they are among the most useful things
    /// a monitor will tell you.
    /// </remarks>
    private IReadOnlyList<VcpControl> _allControls = [];

    private string? _mccsVersion;

    private IReadOnlyList<string> _commands = [];

    /// <summary>
    /// Asks the monitor what it supports, once.
    /// </summary>
    /// <remarks>
    /// Slow even by DDC/CI standards — the capabilities string is several round
    /// trips and every control read is another — so it happens once per rescan,
    /// off the UI thread, and never on a timer.
    /// </remarks>
    private async Task LoadMonitorControlsAsync()
    {
        DisplayInfo d = _display;

        MonitorCapability cap;
        try
        {
            cap = await Task.Run(() => MonitorCapabilities.ReadForUi(d)).ConfigureAwait(true);
        }
        catch (Exception)
        {
            cap = MonitorCapability.None;
        }

        MonitorControls.Clear();

        foreach (VcpControl c in cap.Controls)
        {
            // Settable only. A manufacturer-specific code's meaning is
            // undocumented and model-specific; it belongs in the report, not
            // behind a slider somebody might drag.
            if (!c.Settable) continue;

            // Brightness already has its own card, driven through the same
            // code, and two controls for one value would fight each other.
            if (c.Code is 0x10 or 0xD6) continue;

            MonitorControls.Add(new MonitorControlViewModel(d, c, _deskChanged));
        }

        _reportedControls = cap.Controls.Count;
        _allControls = cap.Controls;
        _mccsVersion = cap.MccsVersion;
        _commands = cap.Commands;

        // VCP B6 is the only thing that ever says what the panel is made of,
        // and only an external monitor can answer it.
        foreach (VcpControl c in cap.Controls)
        {
            if (c.Code != 0xB6 || c.Current < 0) continue;

            _panelTechnology = c.Display;
            break;
        }

        _capabilitiesRead = true;
        RaiseInformation();
        Raise(nameof(PanelTechnology));
        Raise(nameof(OledSummary));
        bool detectedOled = _panelTechnology?.Contains("OLED", StringComparison.OrdinalIgnoreCase) == true;
        if (_settings.OledDetected != detectedOled && _panelTechnology is not null)
        {
            _settings.OledDetected = detectedOled;
            _persist();
        }
        Raise(nameof(IsOled));
        Raise(nameof(OledProtectionVisibility));
        Raise(nameof(MonitorPowerVisibility));
        Raise(nameof(MonitorPowerDescription));

        Raise(nameof(MonitorControlsVisibility));
        Raise(nameof(NoMonitorControlsVisibility));
        Raise(nameof(MonitorControlsSummary));
        Raise(nameof(UnofferedSummary));
        Raise(nameof(UnofferedVisibility));
    }

    // --------------------------------------------------------- night light --

    /// <summary>
    /// This display's own warmth, 0-100.
    /// </summary>
    /// <remarks>
    /// Writes the setting and nothing else. The engine owns the gamma ramp
    /// outright — two processes writing one ramp is how warmth used to compound
    /// until it could not be undone.
    /// </remarks>
    public double NightLightStrength
    {
        get
        {
            int own = _settings.NightLightStrength;
            return own >= 0 ? own : _root.Global.NightLight.Strength;
        }
        set
        {
            int v = Math.Clamp((int)value, 0, 100);
            if (_settings.NightLightStrength == v) return;
            _settings.NightLightStrength = v;
            _persist();
            _deskChanged();
            Raise();
            Raise(nameof(NightLightStrengthText));
        }
    }

    public string NightLightStrengthText
    {
        get
        {
            int resolved = (int)NightLightStrength;
            return $"{resolved}%  ·  {NightLight.KelvinFor(resolved):0}K";
        }
    }

    public string WarmthAutomationName => $"Warmth {Number} — {_display.Label}";

    // ----------------------------------------------------------- panel type --

    public string OledAutomationName => $"OLED {Number}";

    private string? _panelTechnology;

    /// <summary>
    /// What the panel is made of, when anything says.
    /// </summary>
    /// <remarks>
    /// Only ever comes from the monitor itself, over DDC/CI. EDID has no field
    /// for it, Windows exposes none, and a built-in panel has no DDC/CI channel
    /// — so the honest answer for a laptop screen is that nothing reports it.
    /// </remarks>
    public string PanelTechnology => _panelTechnology
        ?? (_display.IsInternal
            ? "Not reported \u2014 built-in panels have no DDC/CI channel"
            : "Not reported by this monitor");

    /// <summary>
    /// Whether this display is treated as OLED, and why.
    /// </summary>
    /// <remarks>
    /// Everything OLED-specific hangs off this, and getting it wrong is not
    /// symmetric: burn-in protection on an LCD is a pointless annoyance, while
    /// its absence on an OLED is permanent damage. So it is a setting with a
    /// sensible default rather than a guess presented as a fact.
    /// </remarks>
    public bool IsOled
    {
        get => _settings.TreatAsOled;
        set
        {
            if (IsOled == value) return;
            _settings.IsOled = value;
            _persist();
            Raise();
            Raise(nameof(OledSummary));
            Raise(nameof(OledProtectionVisibility));
        }
    }

    public string OledSummary
    {
        get
        {
            if (_settings.IsOled is bool chosen)
                return chosen ? "Set by you: treated as OLED." : "Set by you: treated as not OLED.";

            if (_panelTechnology is not null)
                return $"From the monitor: {_panelTechnology}.";

            return "Nothing reports this. Set it yourself if this panel is OLED \u2014 "
                 + "it is what the burn-in protection will key off.";
        }
    }

    /// <summary>Whether focus mode may dim this panel at all.</summary>
    /// <remarks>
    /// A display, not a mode, so it lives here rather than with the shared focus
    /// settings: a second screen kept at full brightness for a video should stay
    /// that way whichever window happens to have focus.
    /// </remarks>
    public bool FocusDimming
    {
        get => _settings.FocusDimming;
        set { if (_settings.FocusDimming == value) return; _settings.FocusDimming = value; _persist(); Raise(); }
    }

    public string FocusDimmingAutomationName => $"FocusDimming {Number}";

    public Visibility OledProtectionVisibility => IsOled ? Visibility.Visible : Visibility.Collapsed;
    public bool OledProtectionEnabled
    {
        get => _settings.OledProtection;
        set { if (_settings.OledProtection == value) return; _settings.OledProtection = value; _persist(); Raise(); }
    }

    public double OledRestMinutes
    {
        get => Math.Clamp(_settings.OledRestMinutes, 1, 30);
        set
        {
            int minutes = Math.Clamp((int)Math.Round(value), 1, 30);
            if (_settings.OledRestMinutes == minutes) return;
            _settings.OledRestMinutes = minutes;
            _persist();
            Raise();
        }
    }

    public void RequestOledRest()
    {
        _settings.OledRestUntilUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(_settings.OledRestMinutes, 1, 30));
        _persist();
    }

    private bool SupportsMonitorPower => _allControls.Any(control => control.Code == 0xD6 && control.Settable);
    public Visibility MonitorPowerVisibility => SupportsMonitorPower ? Visibility.Visible : Visibility.Collapsed;
    public string MonitorPowerDescription => SupportsMonitorPower
        ? "Turn this monitor off through its hardware power control after inactivity. Keyboard or mouse input wakes it."
        : "This monitor does not report a hardware power control.";
    public bool MonitorSleepEnabled
    {
        get => _settings.MonitorSleepEnabled;
        set
        {
            if (_settings.MonitorSleepEnabled == value) return;
            _settings.MonitorSleepEnabled = value;
            _persist();
            Raise();
        }
    }
    public double MonitorSleepMinutes
    {
        get => Math.Clamp(_settings.MonitorSleepMinutes, 1, 240);
        set
        {
            int minutes = Math.Clamp((int)Math.Round(value), 1, 240);
            if (_settings.MonitorSleepMinutes == minutes) return;
            _settings.MonitorSleepMinutes = minutes;
            _persist();
            Raise();
        }
    }

    /// <summary>Where this display's captured warmth limits stand.</summary>
    public string WarmthRangeSummary => _settings.HasNightLightRange
        ? $"{Number}: {_settings.NightLightFloor}–{_settings.NightLightCeiling}%"
        : $"{Number}: not set";

    /// <summary>Takes where the user has just left this display as one warmth limit.</summary>
    public void CaptureWarmthLimit(bool upper)
    {
        int current = (int)NightLightStrength;

        if (upper) _settings.NightLightCeiling = current;
        else _settings.NightLightFloor = current;

        _persist();
        Raise(nameof(WarmthRangeSummary));
    }

    public void ClearWarmthLimits()
    {
        _settings.NightLightFloor = -1;
        _settings.NightLightCeiling = -1;
        _persist();
        Raise(nameof(WarmthRangeSummary));
    }

    /// <summary>Re-reads everything night light drives on this card.</summary>
    public void RaiseNightLight()
    {
        // Warmth moves the dimming floor, so the slider has to hear about it.
        Raise(nameof(SoftwareBrightnessMinimum));
        Raise(nameof(SoftwareBrightnessLimitNote));
        Raise(nameof(SoftwareBrightnessDescription));

        Raise(nameof(NightLightStrength));
        Raise(nameof(NightLightStrengthText));
        Raise(nameof(WarmthRangeSummary));
        Raise(nameof(NightLightVisibility));
    }

    public Visibility NightLightVisibility =>
        _perDisplayWarmth() ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Forgets both limits, returning this display to the multiplier.</summary>
    public void ClearLimits()
    {
        _settings.BrightnessFloor = -1;
        _settings.BrightnessCeiling = -1;
        _persist();

        Raise(nameof(BrightnessFloor));
        Raise(nameof(BrightnessCeiling));
        Raise(nameof(HasBrightnessRange));
        Raise(nameof(RangeSummary));
    }

    public string ConnectorLabel => _display.Connector switch
    {
        ConnectorKind.Internal => "Internal",
        ConnectorKind.Hdmi => "HDMI",
        ConnectorKind.DisplayPort => "DisplayPort",
        ConnectorKind.Dvi => "DVI",
        ConnectorKind.Vga => "VGA",
        ConnectorKind.Usb => "USB-C",
        ConnectorKind.Virtual => "Virtual",
        _ => "Unknown",
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
