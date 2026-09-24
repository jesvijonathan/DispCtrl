using Microsoft.UI;
using Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using DispCtrl.App.Services;
using DispCtrl.Core.Displays;
using DispCtrl.Display;
using Windows.Foundation;

namespace DispCtrl.App.Views;

/// <summary>A physical-size preview with separate Windows desktop coordinates.</summary>
public sealed partial class ArrangeCanvas : UserControl
{
    private readonly record struct WallpaperStamp(string Path, long Length, DateTime ModifiedUtc);

    private sealed class Tile
    {
        public required Border Element { get; init; }
        public required TextBlock Label { get; init; }
        public required DisplayInfo Display { get; init; }
        public required Brush EmptyBackground { get; init; }
        public WallpaperStamp? WallpaperSignature { get; set; }

        /// <summary>The reported file that last failed to decode, so it is not decoded again every ten seconds.</summary>
        public WallpaperStamp? Undecodable { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width => Display.Bounds.Width;
        public int Height => Display.Bounds.Height;
        public int Right => X + Width;
        public int Bottom => Y + Height;
    }

    private static string RestingHint => App.ViewModel.ArrangeByResolution
        ? "Drag to arrange; nearby edges and centres snap into alignment. Tiles are sized by resolution, as in Windows."
        : "Drag to arrange; nearby edges and centres snap into alignment. Tiles reflect physical display size when available.";
    private const double GridSpacing = 24;

    private readonly List<Tile> _tiles = [];
    private readonly Dictionary<string, PhysicalLayout.Placed> _preview = [];
    private readonly List<Line> _gridLines = [];
    private readonly Dictionary<string, (int X, int Y)> _staged = [];
    private readonly Dictionary<string, (int X, int Y)> _livePositions = [];
    private IReadOnlyList<DisplayInfo> _displays = [];
    private double _scale = 1, _offsetX, _offsetY, _originX, _originY;
    private Tile? _dragging;
    private Point _grabOffset;
    private (int X, int Y) _grabbedAt;
    private bool _panning;
    private Point _panAt;

    // Wheel input is a burst; fit shortly after the final notch.
    private static readonly TimeSpan SettleAfter = TimeSpan.FromMilliseconds(450);
    private readonly System.Diagnostics.Stopwatch _fitClock = new();
    private (double Scale, double X, double Y) _fitFrom, _fitTo;
    private bool _hasView;

    private readonly DispatcherTimer _settle = new();
    private Task _wallpaperRefreshTask = Task.CompletedTask;
    private bool _wallpaperRefreshPending;
    public bool WallpapersActive { get; set; }

    public ArrangeCanvas()
    {
        InitializeComponent();
        Hint.Text = RestingHint;
        ResolutionToggle.IsChecked = App.ViewModel.ArrangeByResolution;

        _settle.Interval = SettleAfter;
        _settle.Tick += OnSettled;
        Unloaded += (_, _) => { _settle.Stop(); StopFit(); };
        Loaded += (_, _) => { if (_tiles.Count > 0) Layout(); };
    }

    private void OnSettled(object? sender, object e)
    {
        _settle.Stop();
        if (_dragging is null && !_panning) Layout();
    }

    private void Touch()
    {
        StopFit();
        _settle.Stop();
        _settle.Start();
    }

    private void StopFit()
    {
        CompositionTarget.Rendering -= OnFitFrame;
        _fitClock.Stop();
    }

    private void OnFitFrame(object? sender, object e)
    {
        double progress = Math.Clamp(_fitClock.Elapsed.TotalMilliseconds / 280, 0, 1);
        double eased = 1 - Math.Pow(1 - progress, 3);
        _scale = _fitFrom.Scale + (_fitTo.Scale - _fitFrom.Scale) * eased;
        _offsetX = _fitFrom.X + (_fitTo.X - _fitFrom.X) * eased;
        _offsetY = _fitFrom.Y + (_fitTo.Y - _fitFrom.Y) * eased;
        PlaceTiles();
        if (progress >= 1) StopFit();
    }

    /// <summary>Loads the live arrangement used to detect pending changes.</summary>
    public void Load(IReadOnlyList<DisplayInfo> displays)
    {
        _displays = displays;
        _staged.Clear();
        _livePositions.Clear();
        foreach (DisplayInfo display in displays)
            _livePositions[display.Token] = (display.Bounds.Left, display.Bounds.Top);
        ApplyButton.IsEnabled = false;
        Hint.Text = RestingHint;
        Build();
    }

    private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // This control sits in a vertically unbounded ScrollViewer.
        Surface.Height = Math.Clamp(e.NewSize.Width * 0.38, 220, 440);
    }

    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Surface.Clip = new RectangleGeometry { Rect = new Rect(0, 0, Surface.ActualWidth, Surface.ActualHeight) };
        if (_tiles.Count == 0) Build();
        else if (_dragging is null) Layout();
    }

    private void Build()
    {
        StopFit();
        _hasView = false;
        Surface.Children.Clear();
        _tiles.Clear();
        _gridLines.Clear();
        if (_displays.Count == 0 || Surface.ActualWidth <= 0 || Surface.ActualHeight <= 0) return;

        DrawGrid();
        foreach (DisplayInfo display in _displays)
        {
            (int x, int y) = _staged.TryGetValue(display.Token, out (int X, int Y) staged)
                ? staged : (display.Bounds.Left, display.Bounds.Top);
            var label = new TextBlock
            {
                Text = (_tiles.Count + 1).ToString(),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.White),
            };
            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(display.IsPrimary ? 0 : 1),
                Background = (Brush)Application.Current.Resources[
                    display.IsPrimary ? "AccentFillColorDefaultBrush" : "ControlAltFillColorSecondaryBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultSolidBrush"],
                Child = new Grid { CornerRadius = new CornerRadius(8), Children = {
                    new Border { Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)) },
                    label } },
            };
            ToolTipService.SetToolTip(border, display.Label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(border, $"Display {_tiles.Count + 1}, {display.Label}");
            border.PointerPressed += OnPointerPressed;
            border.PointerMoved += OnPointerMoved;
            border.PointerReleased += OnPointerReleased;
            border.PointerCaptureLost += OnPointerCaptureLost;
            var tile = new Tile { Element = border, Label = label, Display = display,
                EmptyBackground = border.Background, X = x, Y = y };
            border.Tag = tile;
            Canvas.SetZIndex(border, 1);
            Surface.Children.Add(border);
            _tiles.Add(tile);
        }
        _ = RefreshWallpapersAsync();
        UpdatePreview();
        Layout();
    }

    /// <summary>Fits the current desktop bounds into the canvas.</summary>
    private void Layout()
    {
        if (_tiles.Count == 0 || Surface.ActualWidth <= 0 || Surface.ActualHeight <= 0) return;
        double minX = _preview.Values.Min(tile => tile.X), minY = _preview.Values.Min(tile => tile.Y);
        double maxX = _preview.Values.Max(tile => tile.X + tile.Width), maxY = _preview.Values.Max(tile => tile.Y + tile.Height);
        double scale = Math.Min(Surface.ActualWidth / Math.Max(1, maxX - minX),
                                Surface.ActualHeight / Math.Max(1, maxY - minY)) * 0.86;
        double offsetX = (Surface.ActualWidth - ((maxX - minX) * scale)) / 2;
        double offsetY = (Surface.ActualHeight - ((maxY - minY) * scale)) / 2;
        StopFit();
        // Rebase without moving the current picture, then interpolate the view.
        _offsetX += (minX - _originX) * _scale;
        _offsetY += (minY - _originY) * _scale;
        _originX = minX;
        _originY = minY;
        DrawGrid();
        if (!_hasView || !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            _hasView = true;
            _scale = scale;
            _offsetX = offsetX;
            _offsetY = offsetY;
            PlaceTiles();
            return;
        }
        _fitFrom = (_scale, _offsetX, _offsetY);
        _fitTo = (scale, offsetX, offsetY);
        _fitClock.Restart();
        CompositionTarget.Rendering += OnFitFrame;
    }

    private void DrawGrid()
    {
        foreach (Line line in _gridLines) Surface.Children.Remove(line);
        _gridLines.Clear();
        var stroke = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        for (double x = GridSpacing; x < Surface.ActualWidth; x += GridSpacing)
            AddGridLine(new Line { X1 = x, X2 = x, Y2 = Surface.ActualHeight, Stroke = stroke });
        for (double y = GridSpacing; y < Surface.ActualHeight; y += GridSpacing)
            AddGridLine(new Line { Y1 = y, Y2 = y, X2 = Surface.ActualWidth, Stroke = stroke });
    }

    private void AddGridLine(Line line)
    {
        line.StrokeThickness = 1;
        line.Opacity = 0.12;
        line.IsHitTestVisible = false;
        Canvas.SetZIndex(line, 0);
        Surface.Children.Add(line);
        _gridLines.Add(line);
    }

    private void PlaceTiles()
    {
        foreach (Tile tile in _tiles)
        {
            PhysicalLayout.Placed at = _preview[tile.Display.Token];
            double width = at.Width * _scale, height = at.Height * _scale;
            tile.Element.Width = width;
            tile.Element.Height = height;
            tile.Label.FontSize = Math.Clamp(Math.Min(width, height) * 0.3, 13, 44);
            Canvas.SetLeft(tile.Element, _offsetX + ((at.X - _originX) * _scale));
            Canvas.SetTop(tile.Element, _offsetY + ((at.Y - _originY) * _scale));
        }
    }

    // -------------------------------------------------------------- view --

    /// <summary>Pans the canvas with the middle mouse button.</summary>
    private void OnSurfacePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Surface).Properties.IsMiddleButtonPressed || _dragging is not null) return;

        Touch();
        _panning = true;
        _panAt = e.GetCurrentPoint(Surface).Position;
        Surface.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnSurfacePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_panning) return;

        Touch();
        Point pointer = e.GetCurrentPoint(Surface).Position;
        _offsetX += pointer.X - _panAt.X;
        _offsetY += pointer.Y - _panAt.Y;
        _panAt = pointer;
        PlaceTiles();
        e.Handled = true;
    }

    private void OnSurfacePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_panning) return;
        Touch();
        _panning = false;
        Surface.ReleasePointerCapture(e.Pointer);
        _settle.Stop();
        Layout();
        e.Handled = true;
    }

    private void OnSurfacePointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        _settle.Stop();
        Layout();
    }

    /// <summary>Zooms around the pointer, so the location being inspected stays put.</summary>
    private void OnSurfacePointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is not null || _panning || _scale <= 0) return;

        Touch();
        Point pointer = e.GetCurrentPoint(Surface).Position;
        double desktopX = ((pointer.X - _offsetX) / _scale) + _originX;
        double desktopY = ((pointer.Y - _offsetY) / _scale) + _originY;
        double factor = e.GetCurrentPoint(Surface).Properties.MouseWheelDelta > 0 ? 1.12 : 1 / 1.12;
        _scale = Math.Clamp(_scale * factor, 0.02, 2.5);
        _offsetX = pointer.X - ((desktopX - _originX) * _scale);
        _offsetY = pointer.Y - ((desktopY - _originY) * _scale);
        PlaceTiles();
        e.Handled = true;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not Tile tile) return;
        if (!e.GetCurrentPoint(Surface).Properties.IsLeftButtonPressed) return;
        Touch();
        _dragging = tile;
        _grabbedAt = (tile.X, tile.Y);
        Point pointer = e.GetCurrentPoint(Surface).Position;
        _grabOffset = new Point(pointer.X - Canvas.GetLeft(border), pointer.Y - Canvas.GetTop(border));
        border.CapturePointer(e.Pointer);
        Canvas.SetZIndex(border, 10);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is null || sender is not Border border || !ReferenceEquals(border.Tag, _dragging)) return;
        Touch();
        Point pointer = e.GetCurrentPoint(Surface).Position;
        double x = ((pointer.X - _grabOffset.X - _offsetX) / _scale) + _originX;
        double y = ((pointer.Y - _grabOffset.Y - _offsetY) / _scale) + _originY;
        var panels = _tiles.Select(tile => new ArrangementSolver.Panel(tile.Display.Token,
            tile.X, tile.Y, tile.Width, tile.Height, tile.Display.IsPrimary)).ToList();
        var moving = PreviewPanel(_dragging);
        double nearest = double.MaxValue;
        ArrangementSlots.Slot? best = null;
        PhysicalLayout.Placed landing = default;
        foreach (var slot in ArrangementSlots.For(panels, _dragging.Display.Token))
        {
            Tile other = _tiles.First(tile => tile.Display.Token == slot.Against);
            var at = PhysicalLayout.Hang(PreviewPanel(other), moving with { X = slot.X, Y = slot.Y },
                _preview[slot.Against]);
            if (_tiles.Any(tile => tile != _dragging && Intersects(at, _preview[tile.Display.Token]))) continue;
            double distance = Math.Pow(at.X - x, 2) + Math.Pow(at.Y - y, 2);
            if (distance >= nearest) continue;
            nearest = distance;
            best = slot;
            landing = at;
        }
        if (best is { } target)
        {
            _dragging.X = target.X;
            _dragging.Y = target.Y;
            _preview[_dragging.Display.Token] = landing;
            PlaceTiles();
        }
        e.Handled = true;
    }

    private static bool Intersects(PhysicalLayout.Placed a, PhysicalLayout.Placed b) =>
        a.X < b.X + b.Width - 0.001 && b.X < a.X + a.Width - 0.001 &&
        a.Y < b.Y + b.Height - 0.001 && b.Y < a.Y + a.Height - 0.001;

    private PhysicalLayout.Panel PreviewPanel(Tile tile)
    {
        // Use one unit system for the whole preview if EDID size is unavailable,
        // or pixels throughout when asked to draw it the way Windows does.
        bool physical = !App.ViewModel.ArrangeByResolution && _tiles.All(item => item.Display.HasPhysicalSize);
        bool rotated = tile.Display.OrientationDegrees is 90 or 270;
        double width = physical ? (rotated ? tile.Display.PhysicalHeightMm : tile.Display.PhysicalWidthMm) : tile.Width;
        double height = physical ? (rotated ? tile.Display.PhysicalWidthMm : tile.Display.PhysicalHeightMm) : tile.Height;
        return new(tile.Display.Token, tile.X, tile.Y, tile.Width, tile.Height,
            width / tile.Width, height / tile.Height);
    }

    private void OnResolutionToggled(object sender, RoutedEventArgs e)
    {
        App.ViewModel.ArrangeByResolution = ResolutionToggle.IsChecked == true;
        if (ApplyButton.IsEnabled is false) Hint.Text = RestingHint;
        if (_dragging is not null || _tiles.Count == 0) return;
        UpdatePreview();
        // Millimetres to pixels changes every coordinate; refit rather than
        // animate between two scales that measure different things.
        _hasView = false;
        Layout();
    }

    private void UpdatePreview()
    {
        _preview.Clear();
        foreach (var at in PhysicalLayout.Resolve(_tiles.Select(PreviewPanel).ToList()))
            _preview[at.Token] = at;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border) Commit(border);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border) Commit(border);
    }

    private void Commit(Border border)
    {
        if (_dragging is null || !ReferenceEquals(border.Tag, _dragging)) return;
        Tile moved = _dragging;
        _dragging = null;
        Touch();
        Canvas.SetZIndex(border, 1);
        if (moved.X == _grabbedAt.X && moved.Y == _grabbedAt.Y) { Layout(); return; }

        NormaliseToPrimary();
        foreach (Tile tile in _tiles) _staged[tile.Display.Token] = (tile.X, tile.Y);
        PlaceTiles();
        _settle.Stop();
        Layout();
        ApplyButton.IsEnabled = _tiles.Any(tile => !_livePositions.TryGetValue(tile.Display.Token, out var original) || original != (tile.X, tile.Y));
        Hint.Text = "Aligned with the nearest display. Not applied yet.";
    }

    private void NormaliseToPrimary()
    {
        Tile? primary = _tiles.FirstOrDefault(tile => tile.Display.IsPrimary);
        if (primary is null || (primary.X == 0 && primary.Y == 0)) return;
        int shiftX = primary.X, shiftY = primary.Y;
        foreach (Tile tile in _tiles) { tile.X -= shiftX; tile.Y -= shiftY; }
        // Preview coordinates stay in physical units and do not need rebasing.
    }

    private void OnIdentify(object sender, RoutedEventArgs e)
    {
        DisplayIdentifier.Show(_displays, TimeSpan.FromSeconds(3));
        App.ViewModel.ShowFooterStatus("Showing display numbers for 3 seconds.");
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        ApplyButton.IsEnabled = false;
        ResetButton.IsEnabled = false;
        Surface.IsHitTestVisible = false;
        Hint.Text = "Applying…";
        App.ViewModel.ShowFooterStatus("Applying display arrangement…", busy: true);
        var positions = new Dictionary<string, (int X, int Y)>();
        foreach (Tile tile in _tiles) positions[tile.Display.Token] = (tile.X, tile.Y);
        var all = new List<DisplayInfo>(_displays);
        (bool ok, string? why) = await Task.Run(() =>
        {
            bool result = DisplayArrangement.SetPositions(positions, all, out string? error);
            return (result, error);
        });
        ResetButton.IsEnabled = true;
        Surface.IsHitTestVisible = true;
        Hint.Text = ok ? "Arrangement applied." : $"Not applied — {why}.";
        App.ViewModel.ShowFooterStatus(ok ? "Display arrangement applied." : $"Arrangement not applied: {why}.");
        if (ok) { _staged.Clear(); App.ViewModel.Refresh(); }
        else ApplyButton.IsEnabled = true;
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _settle.Stop();
        // Keep the live left-to-right order, removing gaps and vertical offsets.
        int left = 0;
        foreach (Tile tile in _tiles.OrderBy(tile => tile.Display.Bounds.Left)
                                   .ThenBy(tile => tile.Display.Bounds.Top))
        {
            tile.X = left;
            tile.Y = 0;
            left += tile.Width;
        }
        NormaliseToPrimary();
        _staged.Clear();
        foreach (Tile tile in _tiles) _staged[tile.Display.Token] = (tile.X, tile.Y);
        ApplyButton.IsEnabled = _tiles.Any(tile => !_livePositions.TryGetValue(tile.Display.Token, out var original)
            || original != (tile.X, tile.Y));
        Hint.Text = ApplyButton.IsEnabled
            ? "Displays arranged side by side. Select Apply to save."
            : "Displays are already arranged side by side.";
        UpdatePreview();
        Layout();
    }

    /// <summary>Refreshes wallpaper thumbnails without re-enumerating displays.</summary>
    public Task RefreshWallpapersAsync()
    {
        if (!WallpapersActive) return Task.CompletedTask;
        _wallpaperRefreshPending = true;
        if (!_wallpaperRefreshTask.IsCompleted) return _wallpaperRefreshTask;
        return _wallpaperRefreshTask = RefreshWallpapersCoreAsync();
    }

    private async Task RefreshWallpapersCoreAsync()
    {
        do
        {
            _wallpaperRefreshPending = false;
            // A display rescan may replace the tiles while a read is pending.
            foreach (Tile tile in _tiles.ToArray())
            {
                if (!WallpapersActive) break;
                await LoadTileWallpaperAsync(tile);
            }
        } while (WallpapersActive && _wallpaperRefreshPending);
    }

    private async Task LoadTileWallpaperAsync(Tile tile)
    {
        WallpaperStamp? previous = tile.WallpaperSignature;
        // The path Windows reports first, then Windows' own decoded copy: the
        // reported file can be gone (deleted or moved after it was set), online
        // only, or a format this decoder cannot read (HEIC, WebP), and on one
        // laptop the preview stayed empty for exactly that.
        foreach (bool transcoded in new[] { false, true })
        {
            try
            {
                var snapshot = await Task.Run(() => ReadWallpaper(tile.Display, previous, transcoded));
                using var file = snapshot.Stream;
                if (!WallpapersActive || !_tiles.Contains(tile)) return;
                if (snapshot.Stamp is not null && snapshot.Stamp == previous) return;
                if (file is null) continue;
                // Known not to decode: straight to Windows' copy, until it changes.
                if (!transcoded && snapshot.Stamp == tile.Undecodable) continue;
                // Decode from the file stream: no full-sized byte array or second
                // in-memory copy of a potentially large wallpaper.
                using var stream = file.AsRandomAccessStream();
                var bitmap = new BitmapImage { DecodePixelWidth = 320 };
                try { await bitmap.SetSourceAsync(stream); }
                catch (Exception) { if (!transcoded) tile.Undecodable = snapshot.Stamp; throw; }
                if (!WallpapersActive || !_tiles.Contains(tile)) return;
                tile.Element.Background = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
                tile.WallpaperSignature = snapshot.Stamp;
                return;
            }
            catch (Exception) { }
        }
        if (!WallpapersActive || !_tiles.Contains(tile)) return;
        tile.WallpaperSignature = null;
        tile.Element.Background = tile.EmptyBackground;
    }

    /// <summary>Windows' decoded copy of the current wallpaper, kept whatever the original was.</summary>
    private static readonly string TranscodedWallpaper = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Themes", "TranscodedWallpaper");

    private static (WallpaperStamp? Stamp, FileStream? Stream) ReadWallpaper(DisplayInfo display, WallpaperStamp? previous, bool transcoded)
    {
        string? path = transcoded ? TranscodedWallpaper : Wallpaper.Read(display);
        if (string.IsNullOrEmpty(path)) return (null, null);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0) return (null, null);
        var stamp = new WallpaperStamp(path, info.Length, info.LastWriteTimeUtc);
        if (stamp == previous) return (stamp, null);
        return (stamp, new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan));
    }
}
