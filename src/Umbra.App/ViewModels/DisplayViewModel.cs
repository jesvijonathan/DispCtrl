using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Umbra.Core.Displays;
using Umbra.Core.Settings;
using Umbra.Display;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Umbra.App.ViewModels;

/// <summary>One monitor, as the panel presents it.</summary>
public sealed class DisplayViewModel : INotifyPropertyChanged
{
    private readonly DisplayInfo _display;
    private readonly MonitorSettings _settings;
    private readonly Action _persist;
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

    public DisplayViewModel(DisplayInfo display, MonitorSettings settings, int number, Action persist)
    {
        _display = display;
        _settings = settings;
        _persist = persist;
        Number = number;

        // Nothing blocking here. Everything this view model needs comes from
        // the display driver, the monitor over DDC/CI, or COM — all of which
        // are slow enough to stall the window visibly if done inline. Mode
        // enumeration alone walks every mode the driver reports, which is 163
        // on the external monitor this was built against.
        _ = LoadEverythingAsync();
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
    private async Task LoadEverythingAsync()
    {
        DisplayInfo d = _display;

        (List<(uint, uint)> resolutions, DisplayMode? current, string? wallpaper, WallpaperFit fit) =
            await Task.Run(() => (
                DisplayModes.Resolutions(d.GdiName),
                DisplayModes.Current(d.GdiName),
                Wallpaper.Read(d),
                Wallpaper.ReadFit())).ConfigureAwait(true);

        Resolutions.Clear();
        foreach ((uint w, uint h) in resolutions) Resolutions.Add($"{w} × {h}");

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

        _wallpaperPath = wallpaper;
        Raise(nameof(WallpaperName));
        if (_wallpaperPath is not null && File.Exists(_wallpaperPath))
            _ = DecodeWallpaperAsync(_wallpaperPath);

        await Task.WhenAll(LoadBrightnessAsync(), LoadAdvancedAsync());
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

        (HdrState hdr, ScalingState scaling, DisplayDetail detail, VrrState vrr) = await Task.Run(
            () => (AdvancedDisplay.ReadHdr(d), AdvancedDisplay.ReadScaling(d),
                   DisplayDetails.Read(d), VariableRefreshRate.Read(d)))
            .ConfigureAwait(false);

        _ui.TryEnqueue(() =>
        {
            _hdrReady = true;

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

    public string Name => _display.Label;

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
    public double PreviewScale
    {
        get => _previewScale;
        set
        {
            if (Math.Abs(_previewScale - value) < 0.001) return;
            _previewScale = value;
            Raise(nameof(PreviewWidth));
            Raise(nameof(PreviewHeight));
        }
    }

    private double _previewScale = 1.0;

    private const double MaxPreviewWidth = 240;

    /// <summary>
    /// Preview width, scaled to the panel's real size rather than its resolution.
    /// </summary>
    /// <remarks>
    /// Resolution is the wrong measure for a picture of a monitor: this
    /// machine's 14-inch laptop panel has half again as many pixels as the
    /// 24-inch display beside it, so sizing by pixels drew the small screen
    /// larger. The physical dimensions come from the panel's own EDID.
    /// </remarks>
    public double PreviewWidth => Math.Round(MaxPreviewWidth * Math.Clamp(_previewScale, 0.35, 1.0));

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

    public string PreviewCaption => $"{_display.Bounds.Width} × {_display.Bounds.Height}";

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

    public string HideTaskbarDescription => _display.IsPrimary
        ? "Windows does not allow this on the primary monitor — explorer restores its taskbar immediately."
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

    private void LoadModes()
    {
        Resolutions.Clear();
        foreach ((uint w, uint h) in DisplayModes.Resolutions(_display.GdiName))
            Resolutions.Add($"{w} × {h}");

        _selectedResolution = $"{_display.Bounds.Width} × {_display.Bounds.Height}";
        PopulateRefreshRates();
        _selectedRefreshRate = $"{_display.RefreshHz} Hz";
    }

    private void PopulateRefreshRates()
    {
        if (!TryParseResolution(_selectedResolution, out uint w, out uint h)) return;

        RefreshRates.Clear();
        foreach (uint hz in DisplayModes.RefreshRatesAt(_display.GdiName, w, h))
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

        var target = new DisplayMode(w, h, hz, 32);
        if (target.Width == _display.Bounds.Width
            && target.Height == _display.Bounds.Height
            && target.RefreshHz == _display.RefreshHz)
            return;

        _applyingMode = true;
        string device = _display.GdiName;

        _ = Task.Run(() =>
        {
            ModeChangeResult result = DisplayModes.Apply(device, target);

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

    public string ActiveSignalMode => _detail.ActiveSignalMode;
    public string DesktopModeText => _detail.DesktopMode;
    public string ColorFormat => _detail.ColorFormat;
    public string ColorSpace => _detail.ColorSpace;
    public string BitDepth => _detail.BitDepth;
    public string HdrStatus => _detail.HdrStatus;
    public string ScanLineOrdering => _detail.ScanLineOrdering;
    public string PixelClock => _detail.PixelClock;
    public string ColorProfileName => _detail.ColorProfile;

    /// <summary>
    /// The at-a-glance line on the collapsed card.
    /// </summary>
    /// <remarks>
    /// Replaces the bare "Taskbar hidden" that used to sit here. A row in a
    /// list should answer "what is this display doing?", and the taskbar is
    /// only one small part of that — so the signal, colour depth and HDR state
    /// lead, with the taskbar noted only when Umbra is actually managing it.
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

    /// <summary>Returns this display's Umbra settings to their defaults.</summary>
    /// <remarks>
    /// Deliberately does not touch resolution, refresh rate, HDR or scaling.
    /// Those belong to Windows, not to Umbra, and silently rewriting them would
    /// be far more destructive than the word "reset" implies.
    /// </remarks>
    public void ResetToDefaults()
    {
        _settings.ResetToDefaults();
        _persist();

        Raise(nameof(HideTaskbar));
        Raise(nameof(ReclaimWorkArea));
        Raise(nameof(TaskbarSummary));
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

        int baseline = _settings.BrightnessBaseline;
        if (baseline <= 0)
        {
            // No usable baseline — recover one rather than multiplying by zero
            // and pinning the display dark.
            baseline = _brightnessPercent > 0 ? _brightnessPercent : 100;
            BrightnessBaseline = baseline;
        }

        int target = (int)Math.Round(Math.Clamp(baseline * factor, 0, 100));

        _brightnessPercent = target;
        Raise(nameof(BrightnessPercent));
        QueueBrightnessWrite(target);
    }

    private string ConnectorLabel => _display.Connector switch
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
