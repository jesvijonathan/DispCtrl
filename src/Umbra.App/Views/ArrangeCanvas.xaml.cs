using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Umbra.Core.Displays;
using Umbra.Display;
using Windows.Foundation;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Umbra.App.Views;

/// <summary>
/// The monitor arrangement surface, mirroring Windows' own Display settings.
/// </summary>
/// <remarks>
/// Displays are drawn to scale in desktop coordinates and dragged to new
/// positions. Nothing is applied while dragging: a mode change per mouse-move
/// would reflow the desktop continuously. The whole arrangement is committed
/// once, on Apply.
/// </remarks>
public sealed partial class ArrangeCanvas : UserControl
{
    private sealed class Tile
    {
        public required Border Element { get; init; }
        public required DisplayInfo Display { get; init; }

        /// <summary>Desktop-space position, updated as the tile is dragged.</summary>
        public int X { get; set; }
        public int Y { get; set; }
    }

    /// <summary>
    /// How close two edges must be, <em>on screen</em>, to snap together.
    /// </summary>
    /// <remarks>
    /// Deliberately expressed in screen pixels and converted to desktop pixels
    /// at the current scale. A fixed desktop-space threshold is unusable: the
    /// canvas draws a ~4800px-wide desktop into a few hundred pixels, so 60
    /// desktop pixels is under six on screen — the drop target was effectively
    /// pixel-perfect, which is why it never appeared to snap.
    /// </remarks>
    private const double SnapScreenPixels = 18;

    private int SnapThreshold => (int)Math.Round(SnapScreenPixels / Math.Max(_scale, 0.0001));

    private readonly List<Tile> _tiles = [];
    private IReadOnlyList<DisplayInfo> _displays = [];

    private double _scale = 0.1;
    private int _originX, _originY;

    private Tile? _dragging;
    private Point _grabOffset;
    private bool _dirty;

    public ArrangeCanvas() => InitializeComponent();

    /// <summary>Rebuilds the surface for a new set of displays.</summary>
    public void Load(IReadOnlyList<DisplayInfo> displays)
    {
        _displays = displays;
        _dirty = false;
        ApplyButton.IsEnabled = false;
        Rebuild();
    }

    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        Surface.Children.Clear();
        _tiles.Clear();

        if (_displays.Count == 0 || Surface.ActualWidth <= 0) return;

        // Bounding box of the whole desktop, so the drawing is to scale.
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (DisplayInfo d in _displays)
        {
            minX = Math.Min(minX, d.Bounds.Left);
            minY = Math.Min(minY, d.Bounds.Top);
            maxX = Math.Max(maxX, d.Bounds.Right);
            maxY = Math.Max(maxY, d.Bounds.Bottom);
        }

        _originX = minX;
        _originY = minY;

        double spanX = Math.Max(1, maxX - minX);
        double spanY = Math.Max(1, maxY - minY);

        // 0.82 leaves margin so a dragged tile is not clipped at the edge.
        _scale = Math.Min(Surface.ActualWidth / spanX, Surface.ActualHeight / spanY) * 0.82;

        double offsetX = (Surface.ActualWidth - spanX * _scale) / 2;
        double offsetY = (Surface.ActualHeight - spanY * _scale) / 2;

        for (int i = 0; i < _displays.Count; i++)
        {
            DisplayInfo d = _displays[i];

            var label = new TextBlock
            {
                Text = (i + 1).ToString(),
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.White),
            };

            var border = new Border
            {
                Width = Math.Max(24, d.Bounds.Width * _scale),
                Height = Math.Max(18, d.Bounds.Height * _scale),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(d.IsPrimary ? 2 : 1),
                Background = (Brush)Application.Current.Resources[
                    d.IsPrimary ? "AccentFillColorDefaultBrush" : "ControlAltFillColorSecondaryBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultSolidBrush"],
                Child = label,
            };

            _ = LoadTileWallpaperAsync(border, d);

            var tile = new Tile { Element = border, Display = d, X = d.Bounds.Left, Y = d.Bounds.Top };
            border.Tag = tile;

            border.PointerPressed += OnPointerPressed;
            border.PointerMoved += OnPointerMoved;
            border.PointerReleased += OnPointerReleased;

            Canvas.SetLeft(border, offsetX + (d.Bounds.Left - _originX) * _scale);
            Canvas.SetTop(border, offsetY + (d.Bounds.Top - _originY) * _scale);

            Surface.Children.Add(border);
            _tiles.Add(tile);
        }
    }

    /// <summary>Paints a tile with the wallpaper actually on that display.</summary>
    /// <remarks>
    /// Reading it is a COM round trip, so it happens off the UI thread and the
    /// tile simply stays flat-coloured until the image arrives. A tile that has
    /// been rebuilt in the meantime is harmlessly abandoned.
    /// </remarks>
    private static async Task LoadTileWallpaperAsync(Border border, DisplayInfo display)
    {
        try
        {
            string? path = await Task.Run(() => Wallpaper.Read(display)).ConfigureAwait(true);
            if (path is null || !File.Exists(path)) return;

            byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(true);

            var stream = new InMemoryRandomAccessStream();
            using (DataWriter writer = new(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
            }

            // Decoded small: these tiles are at most a couple of hundred pixels.
            var bitmap = new BitmapImage { DecodePixelWidth = 240 };
            await bitmap.SetSourceAsync(stream);

            border.Background = new ImageBrush
            {
                ImageSource = bitmap,
                Stretch = Stretch.UniformToFill,
            };
        }
        catch (Exception)
        {
            // An unreadable wallpaper just leaves the tile flat-coloured.
        }
    }

    // ---------------------------------------------------------------- drag --

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not Tile tile) return;

        _dragging = tile;
        Point p = e.GetCurrentPoint(Surface).Position;
        _grabOffset = new Point(p.X - Canvas.GetLeft(border), p.Y - Canvas.GetTop(border));

        border.CapturePointer(e.Pointer);
        Canvas.SetZIndex(border, 10);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is null || sender is not Border border) return;

        Point p = e.GetCurrentPoint(Surface).Position;
        Canvas.SetLeft(border, p.X - _grabOffset.X);
        Canvas.SetTop(border, p.Y - _grabOffset.Y);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is null || sender is not Border border) return;

        border.ReleasePointerCapture(e.Pointer);
        Canvas.SetZIndex(border, 0);
        ProtectedCursor = null;

        // Convert back to desktop space, then snap.
        double offsetX = (Surface.ActualWidth - TotalSpanX() * _scale) / 2;
        double offsetY = (Surface.ActualHeight - TotalSpanY() * _scale) / 2;

        _dragging.X = (int)Math.Round((Canvas.GetLeft(border) - offsetX) / _scale) + _originX;
        _dragging.Y = (int)Math.Round((Canvas.GetTop(border) - offsetY) / _scale) + _originY;

        Snap(_dragging);

        Canvas.SetLeft(border, offsetX + (_dragging.X - _originX) * _scale);
        Canvas.SetTop(border, offsetY + (_dragging.Y - _originY) * _scale);

        _dragging = null;
        _dirty = true;
        ApplyButton.IsEnabled = true;
        Hint.Text = "Not applied yet.";
    }

    /// <summary>
    /// Pulls a dragged display's edges onto its neighbours'.
    /// </summary>
    /// <remarks>
    /// Windows rejects an arrangement with gaps between displays, so landing
    /// a few pixels short would silently fail. Snapping makes the common intent
    /// — butt this panel against that one — reachable by hand.
    /// </remarks>
    private void Snap(Tile moved)
    {
        int w = moved.Display.Bounds.Width;
        int h = moved.Display.Bounds.Height;

        foreach (Tile other in _tiles)
        {
            if (ReferenceEquals(other, moved)) continue;

            int ow = other.Display.Bounds.Width;
            int oh = other.Display.Bounds.Height;

            // Horizontal: right-to-left and left-to-right.
            if (Math.Abs(moved.X - (other.X + ow)) < SnapThreshold) moved.X = other.X + ow;
            else if (Math.Abs(moved.X + w - other.X) < SnapThreshold) moved.X = other.X - w;

            // Vertical: bottom-to-top and top-to-bottom.
            if (Math.Abs(moved.Y - (other.Y + oh)) < SnapThreshold) moved.Y = other.Y + oh;
            else if (Math.Abs(moved.Y + h - other.Y) < SnapThreshold) moved.Y = other.Y - h;

            // Edge alignment, so displays line up rather than sitting askew.
            if (Math.Abs(moved.Y - other.Y) < SnapThreshold) moved.Y = other.Y;
            if (Math.Abs(moved.X - other.X) < SnapThreshold) moved.X = other.X;
        }
    }

    private double TotalSpanX()
    {
        int min = int.MaxValue, max = int.MinValue;
        foreach (DisplayInfo d in _displays)
        {
            min = Math.Min(min, d.Bounds.Left);
            max = Math.Max(max, d.Bounds.Right);
        }
        return Math.Max(1, max - min);
    }

    private double TotalSpanY()
    {
        int min = int.MaxValue, max = int.MinValue;
        foreach (DisplayInfo d in _displays)
        {
            min = Math.Min(min, d.Bounds.Top);
            max = Math.Max(max, d.Bounds.Bottom);
        }
        return Math.Max(1, max - min);
    }

    // --------------------------------------------------------------- apply --

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (!_dirty) return;

        ApplyButton.IsEnabled = false;
        Hint.Text = "Applying…";

        var positions = new Dictionary<string, (int X, int Y)>();
        foreach (Tile t in _tiles) positions[t.Display.Token] = (t.X, t.Y);

        var all = new List<DisplayInfo>(_displays);
        bool ok = await Task.Run(() => DisplayArrangement.SetPositions(positions, all));

        Hint.Text = ok
            ? "Arrangement applied."
            : "Windows rejected that arrangement — displays must touch without gaps.";

        _dirty = false;
        App.ViewModel.Refresh();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _dirty = false;
        ApplyButton.IsEnabled = false;
        Hint.Text = "Drag a display to move it. Edges snap to neighbours.";
        Rebuild();
    }
}
