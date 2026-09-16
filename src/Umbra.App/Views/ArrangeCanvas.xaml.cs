using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Umbra.Core.Displays;
using Umbra.Display;
using Windows.Foundation;
using Windows.Storage.Streams;

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
        public int X { get; set; }
        public int Y { get; set; }

        public int Width => Display.Bounds.Width;
        public int Height => Display.Bounds.Height;
    }

    /// <summary>
    /// How close two edges must be, <em>on screen</em>, to snap together.
    /// </summary>
    /// <remarks>
    /// Expressed in screen pixels and converted at the current scale. A fixed
    /// desktop-space threshold is unusable: the canvas draws a ~4800px-wide
    /// desktop into a few hundred pixels, so even 60 desktop pixels lands under
    /// six on screen.
    /// </remarks>
    private const double SnapScreenPixels = 22;

    private readonly List<Tile> _tiles = [];
    private IReadOnlyList<DisplayInfo> _displays = [];

    /// <summary>
    /// Positions staged by dragging, keyed by display token.
    /// </summary>
    /// <remarks>
    /// Held separately from the tiles so a relayout cannot discard them. The
    /// surface used to rebuild from <see cref="DisplayInfo.Bounds"/> on every
    /// size change, which silently reverted a staged arrangement — and that
    /// looked exactly like Apply doing nothing, because by then it was writing
    /// back the original coordinates.
    /// </remarks>
    private readonly Dictionary<string, (int X, int Y)> _staged = [];

    private double _scale = 0.1;
    private double _offsetX, _offsetY;
    private int _originX, _originY;

    private Tile? _dragging;
    private Point _grabOffset;

    public ArrangeCanvas() => InitializeComponent();

    private int SnapThreshold => (int)Math.Round(SnapScreenPixels / Math.Max(_scale, 0.0001));

    /// <summary>Rebuilds the surface for a new set of displays.</summary>
    public void Load(IReadOnlyList<DisplayInfo> displays)
    {
        _displays = displays;
        _staged.Clear();
        ApplyButton.IsEnabled = false;
        Build();
    }

    /// <remarks>
    /// A size change only re-lays-out; it never rebuilds from scratch, so a
    /// staged arrangement survives the window being resized or maximised.
    /// </remarks>
    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_tiles.Count == 0) Build();
        else Layout();
    }

    // ---------------------------------------------------------------- build --

    private void Build()
    {
        Surface.Children.Clear();
        _tiles.Clear();

        if (_displays.Count == 0 || Surface.ActualWidth <= 0) return;

        foreach (DisplayInfo d in _displays)
        {
            (int x, int y) = _staged.TryGetValue(d.Token, out (int X, int Y) s)
                ? s
                : (d.Bounds.Left, d.Bounds.Top);

            var label = new TextBlock
            {
                Text = (_tiles.Count + 1).ToString(),
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.White),
            };

            var border = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(d.IsPrimary ? 2 : 1),
                Background = (Brush)Application.Current.Resources[
                    d.IsPrimary ? "AccentFillColorDefaultBrush" : "ControlAltFillColorSecondaryBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultSolidBrush"],
                Child = label,
            };

            border.PointerPressed += OnPointerPressed;
            border.PointerMoved += OnPointerMoved;
            border.PointerReleased += OnPointerReleased;
            border.PointerCaptureLost += OnPointerCaptureLost;

            var tile = new Tile { Element = border, Display = d, X = x, Y = y };
            border.Tag = tile;

            _ = LoadTileWallpaperAsync(border, d);

            Surface.Children.Add(border);
            _tiles.Add(tile);
        }

        Layout();
    }

    /// <summary>Recomputes scale and places every tile from its desktop position.</summary>
    private void Layout()
    {
        if (_tiles.Count == 0 || Surface.ActualWidth <= 0 || Surface.ActualHeight <= 0) return;

        // Bounding box of the staged arrangement, not the committed one, so a
        // display dragged well clear still fits on screen.
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Tile t in _tiles)
        {
            minX = Math.Min(minX, t.X);
            minY = Math.Min(minY, t.Y);
            maxX = Math.Max(maxX, t.X + t.Width);
            maxY = Math.Max(maxY, t.Y + t.Height);
        }

        _originX = minX;
        _originY = minY;

        double spanX = Math.Max(1, maxX - minX);
        double spanY = Math.Max(1, maxY - minY);

        _scale = Math.Min(Surface.ActualWidth / spanX, Surface.ActualHeight / spanY) * 0.86;
        _offsetX = (Surface.ActualWidth - spanX * _scale) / 2;
        _offsetY = (Surface.ActualHeight - spanY * _scale) / 2;

        foreach (Tile t in _tiles)
        {
            t.Element.Width = Math.Max(26, t.Width * _scale);
            t.Element.Height = Math.Max(20, t.Height * _scale);
            Canvas.SetLeft(t.Element, _offsetX + (t.X - _originX) * _scale);
            Canvas.SetTop(t.Element, _offsetY + (t.Y - _originY) * _scale);
        }
    }

    /// <summary>Paints a tile with the wallpaper actually on that display.</summary>
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

            var bitmap = new BitmapImage { DecodePixelWidth = 240 };
            await bitmap.SetSourceAsync(stream);

            border.Background = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
        }
        catch (Exception)
        {
            // An unreadable wallpaper just leaves the tile flat-coloured.
        }
    }

    // ----------------------------------------------------------------- drag --

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not Tile tile) return;

        _dragging = tile;
        Point p = e.GetCurrentPoint(Surface).Position;
        _grabOffset = new Point(p.X - Canvas.GetLeft(border), p.Y - Canvas.GetTop(border));

        border.CapturePointer(e.Pointer);
        Canvas.SetZIndex(border, 10);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is null || sender is not Border border) return;
        if (!ReferenceEquals(border.Tag, _dragging)) return;

        Point p = e.GetCurrentPoint(Surface).Position;
        Canvas.SetLeft(border, p.X - _grabOffset.X);
        Canvas.SetTop(border, p.Y - _grabOffset.Y);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border) Commit(border);
        e.Handled = true;
    }

    /// <remarks>
    /// Capture loss is treated as a completed drag rather than a cancellation:
    /// the pointer can be taken away by the window losing focus mid-drag, and
    /// discarding the move then would look like the drag simply not working.
    /// </remarks>
    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border) Commit(border);
    }

    private void Commit(Border border)
    {
        if (_dragging is null || !ReferenceEquals(border.Tag, _dragging)) return;

        Tile tile = _dragging;
        _dragging = null;

        Canvas.SetZIndex(border, 0);

        // Screen position back into desktop coordinates.
        tile.X = (int)Math.Round((Canvas.GetLeft(border) - _offsetX) / _scale) + _originX;
        tile.Y = (int)Math.Round((Canvas.GetTop(border) - _offsetY) / _scale) + _originY;

        Snap(tile);
        Normalise();

        foreach (Tile t in _tiles) _staged[t.Display.Token] = (t.X, t.Y);

        Layout();

        ApplyButton.IsEnabled = true;
        Hint.Text = "Not applied yet.";
    }

    /// <summary>
    /// Pulls a dragged display onto its neighbours' edges and centre lines.
    /// </summary>
    /// <remarks>
    /// Windows rejects an arrangement with gaps between displays, so landing a
    /// few pixels short would silently fail. Centre-line snapping is what makes
    /// it behave like a grid: panels of different heights line up on their
    /// middles, not only on their top or bottom edges.
    /// </remarks>
    private void Snap(Tile moved)
    {
        int threshold = SnapThreshold;

        foreach (Tile other in _tiles)
        {
            if (ReferenceEquals(other, moved)) continue;

            // Butt against a neighbour, left or right.
            if (Math.Abs(moved.X - (other.X + other.Width)) < threshold) moved.X = other.X + other.Width;
            else if (Math.Abs(moved.X + moved.Width - other.X) < threshold) moved.X = other.X - moved.Width;

            // Above or below.
            if (Math.Abs(moved.Y - (other.Y + other.Height)) < threshold) moved.Y = other.Y + other.Height;
            else if (Math.Abs(moved.Y + moved.Height - other.Y) < threshold) moved.Y = other.Y - moved.Height;

            // Edge alignment.
            if (Math.Abs(moved.Y - other.Y) < threshold) moved.Y = other.Y;
            else if (Math.Abs(moved.Y + moved.Height - (other.Y + other.Height)) < threshold)
                moved.Y = other.Y + other.Height - moved.Height;

            if (Math.Abs(moved.X - other.X) < threshold) moved.X = other.X;
            else if (Math.Abs(moved.X + moved.Width - (other.X + other.Width)) < threshold)
                moved.X = other.X + other.Width - moved.Width;

            // Centre lines, which is what makes unequal panels line up neatly.
            int otherCentreY = other.Y + other.Height / 2;
            if (Math.Abs(moved.Y + moved.Height / 2 - otherCentreY) < threshold)
                moved.Y = otherCentreY - moved.Height / 2;

            int otherCentreX = other.X + other.Width / 2;
            if (Math.Abs(moved.X + moved.Width / 2 - otherCentreX) < threshold)
                moved.X = otherCentreX - moved.Width / 2;
        }
    }

    /// <summary>
    /// Shifts the whole arrangement so its leftmost and topmost edges sit at zero.
    /// </summary>
    /// <remarks>
    /// Windows anchors the desktop origin at the primary display and refuses a
    /// layout whose origin has drifted. Normalising here stops Apply being
    /// rejected for a reason that is invisible on screen.
    /// </remarks>
    private void Normalise()
    {
        int minX = int.MaxValue, minY = int.MaxValue;
        foreach (Tile t in _tiles)
        {
            minX = Math.Min(minX, t.X);
            minY = Math.Min(minY, t.Y);
        }

        if (minX == 0 && minY == 0) return;

        foreach (Tile t in _tiles)
        {
            t.X -= minX;
            t.Y -= minY;
        }
    }

    // ---------------------------------------------------------------- apply --

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        ApplyButton.IsEnabled = false;
        Hint.Text = "Applying…";

        var positions = new Dictionary<string, (int X, int Y)>();
        foreach (Tile t in _tiles) positions[t.Display.Token] = (t.X, t.Y);

        var all = new List<DisplayInfo>(_displays);
        bool ok = await Task.Run(() => DisplayArrangement.SetPositions(positions, all));

        Hint.Text = ok
            ? "Arrangement applied."
            : "Windows refused that arrangement — displays must touch without gaps.";

        _staged.Clear();
        App.ViewModel.Refresh();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _staged.Clear();
        ApplyButton.IsEnabled = false;
        Hint.Text = "Drag a display to move it. Edges and centres snap.";
        Build();
    }
}
