using Microsoft.UI;
using Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using DisplCtrl.App.Services;
using DisplCtrl.Core.Displays;
using DisplCtrl.Display;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace DisplCtrl.App.Views;

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
        public required TextBlock Label { get; init; }
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

    private Tile? _dragging;
    private Point _grabOffset;

    public ArrangeCanvas() => InitializeComponent();

    /// <summary>
    /// The snap distance in desktop pixels, for the display being dragged.
    /// </summary>
    /// <remarks>
    /// The diagram is drawn in millimetres now, so the conversion runs screen
    /// pixels to millimetres to desktop pixels. Skipping the middle step made
    /// the threshold wrong by the panel's own density — several times too small
    /// on a dense laptop panel, which is exactly where snapping matters most.
    /// </remarks>
    private int SnapThreshold
    {
        get
        {
            double millimetres = SnapScreenPixels / Math.Max(_scale, 0.0001);

            DisplayInfo? d = _dragging?.Display;
            double perPx = d is { HasPhysicalSize: true }
                ? d.PhysicalWidthMm / (double)d.Bounds.Width
                : 1;

            return (int)Math.Round(millimetres / Math.Max(perPx, 0.0001));
        }
    }

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

            // Windows draws a plain light numeral straight on the panel, with
            // no chip behind it. The chip was the thing that made this read as
            // someone's approximation of the real control.
            var label = new TextBlock
            {
                Text = (_tiles.Count + 1).ToString(),
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.White),
            };

            // Each tile carries the wallpaper actually on that display, which
            // is what makes the diagram answer "which one is this?" at a
            // glance. Windows draws flat plates here; this is deliberately not
            // that, because the wallpaper is the fastest identifier there is.
            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(d.IsPrimary ? 0 : 1),
                Background = (Brush)Application.Current.Resources[
                    d.IsPrimary ? "AccentFillColorDefaultBrush" : "ControlAltFillColorSecondaryBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultSolidBrush"],

                // A scrim under the numeral, so a bright or busy wallpaper
                // cannot swallow it. Same treatment as the per-display preview.
                Child = new Grid
                {
                    Children =
                    {
                        new Border { Background = new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)) },
                        label,
                    },
                },
            };

            border.PointerPressed += OnPointerPressed;
            border.PointerMoved += OnPointerMoved;
            border.PointerReleased += OnPointerReleased;
            border.PointerCaptureLost += OnPointerCaptureLost;

            _ = LoadTileWallpaperAsync(border, d);

            var tile = new Tile { Element = border, Label = label, Display = d, X = x, Y = y };
            border.Tag = tile;

            Surface.Children.Add(border);
            _tiles.Add(tile);
        }

        Layout();
    }

    /// <summary>
    /// Where each tile sits in the diagram, in millimetres.
    /// </summary>
    /// <remarks>
    /// Recomputed whenever the arrangement changes, and consulted by both the
    /// layout and the drag maths so the two cannot disagree about where a tile
    /// is.
    /// </remarks>
    private readonly Dictionary<string, PhysicalLayout.Placed> _placed = [];

    /// <summary>Recomputes scale and places every tile at its physical size.</summary>
    /// <remarks>
    /// Drawn in millimetres rather than pixels: a 14-inch panel at 2880x1800 has
    /// more pixels than a 24-inch monitor, and drawing by pixel count makes the
    /// laptop the bigger of the two on a diagram whose entire job is to show
    /// where things physically are.
    /// </remarks>
    private void Layout()
    {
        if (_tiles.Count == 0 || Surface.ActualWidth <= 0 || Surface.ActualHeight <= 0) return;

        var panels = new List<PhysicalLayout.Panel>(_tiles.Count);
        foreach (Tile t in _tiles)
        {
            DisplayInfo d = t.Display;

            double mmX = d.HasPhysicalSize ? d.PhysicalWidthMm / (double)d.Bounds.Width : 0;
            double mmY = d.HasPhysicalSize ? d.PhysicalHeightMm / (double)d.Bounds.Height : 0;

            panels.Add(new PhysicalLayout.Panel(d.Token, t.X, t.Y, t.Width, t.Height, mmX, mmY));
        }

        _placed.Clear();
        foreach (PhysicalLayout.Placed p in PhysicalLayout.Resolve(panels)) _placed[p.Token] = p;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (PhysicalLayout.Placed p in _placed.Values)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X + p.Width);
            maxY = Math.Max(maxY, p.Y + p.Height);
        }

        double spanX = Math.Max(1, maxX - minX);
        double spanY = Math.Max(1, maxY - minY);

        _scale = Math.Min(Surface.ActualWidth / spanX, Surface.ActualHeight / spanY) * 0.86;
        _offsetX = (Surface.ActualWidth - (spanX * _scale)) / 2;
        _offsetY = (Surface.ActualHeight - (spanY * _scale)) / 2;
        _originMmX = minX;
        _originMmY = minY;

        PlaceTiles();
    }

    private double _originMmX, _originMmY;

    /// <summary>Paints a tile with the wallpaper actually on that display.</summary>
    /// <remarks>
    /// Decoded small and scrimmed by the numeral's own contrast, so a busy or
    /// bright wallpaper cannot swallow the number sitting on it.
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

            var bitmap = new BitmapImage { DecodePixelWidth = 320 };
            await bitmap.SetSourceAsync(stream);

            border.Background = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
        }
        catch (Exception)
        {
            // An unreadable wallpaper just leaves the tile flat-coloured.
        }
    }

    private void OnIdentify(object sender, RoutedEventArgs e) =>
        DisplayIdentifier.Show(_displays, TimeSpan.FromSeconds(3));

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

    /// <remarks>
    /// The display is resolved to a legal position on every move rather than
    /// only on release, so an invalid arrangement is never even drawn. Letting
    /// it float free and correcting on drop meant the user spent the whole drag
    /// aiming at positions that were going to be rejected.
    /// <para>
    /// The scale is held fixed for the duration of the drag. Recomputing it as
    /// the bounding box changes would move the tile under the cursor.
    /// </para>
    /// </remarks>
    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is null || sender is not Border border) return;
        if (!ReferenceEquals(border.Tag, _dragging)) return;

        Point p = e.GetCurrentPoint(Surface).Position;

        // Screen position back to millimetres, then millimetres back to desktop
        // pixels through this display's own density. Going straight to pixels
        // would move the tile at the wrong rate now that the diagram is drawn
        // physically — a dense panel would crawl under the cursor.
        double mmX = ((p.X - _grabOffset.X - _offsetX) / _scale) + _originMmX;
        double mmY = ((p.Y - _grabOffset.Y - _offsetY) / _scale) + _originMmY;

        DisplayInfo moved = _dragging.Display;
        double perPxX = moved.HasPhysicalSize ? moved.PhysicalWidthMm / (double)moved.Bounds.Width : 1;
        double perPxY = moved.HasPhysicalSize ? moved.PhysicalHeightMm / (double)moved.Bounds.Height : 1;

        _dragging.X = (int)Math.Round(mmX / Math.Max(perPxX, 0.0001));
        _dragging.Y = (int)Math.Round(mmY / Math.Max(perPxY, 0.0001));

        Snap(_dragging);
        ApplySolver(_dragging);
        Layout();

        e.Handled = true;
    }

    /// <summary>Positions and sizes every tile from the millimetre map.</summary>
    private void PlaceTiles()
    {
        foreach (Tile t in _tiles)
        {
            if (!_placed.TryGetValue(t.Display.Token, out PhysicalLayout.Placed p)) continue;

            double w = Math.Max(26, p.Width * _scale);
            double h = Math.Max(20, p.Height * _scale);

            t.Element.Width = w;
            t.Element.Height = h;

            // The numeral grows with its plate, as Windows' does. A fixed size
            // swamps a small tile and looks lost on a large one.
            t.Label.FontSize = Math.Clamp(Math.Min(w, h) * 0.3, 13, 44);

            Canvas.SetLeft(t.Element, _offsetX + ((p.X - _originMmX) * _scale));
            Canvas.SetTop(t.Element, _offsetY + ((p.Y - _originMmY) * _scale));
        }
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

        // Already resolved on every move, so there is nothing left to correct.
        ApplySolver(tile);

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
    /// Hands the arrangement to the shared solver and writes the result back.
    /// </summary>
    /// <remarks>
    /// The rules live in <see cref="ArrangementSolver"/> rather than here so
    /// they are pure geometry and can be tested directly, instead of only
    /// through a pointer device. What runs is what is covered.
    /// </remarks>
    private void ApplySolver(Tile moved)
    {
        var panels = new List<ArrangementSolver.Panel>(_tiles.Count);
        foreach (Tile t in _tiles)
            panels.Add(new ArrangementSolver.Panel(
                t.Display.Token, t.X, t.Y, t.Width, t.Height, t.Display.IsPrimary));

        List<ArrangementSolver.Panel> solved =
            ArrangementSolver.Resolve(panels, moved.Display.Token);

        foreach (ArrangementSolver.Panel p in solved)
        {
            foreach (Tile t in _tiles)
            {
                if (t.Display.Token != p.Token) continue;
                t.X = p.X;
                t.Y = p.Y;
                break;
            }
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

        (bool ok, string? why) = await Task.Run(() =>
        {
            bool r = DisplayArrangement.SetPositions(positions, all, out string? e);
            return (r, e);
        });

        // Saying what Windows objected to, rather than guessing at it. The old
        // message blamed a corner overlap every time, which was wrong whenever
        // the real cause was something else.
        Hint.Text = ok ? "Arrangement applied." : $"Not applied — {why}.";

        _staged.Clear();
        App.ViewModel.Refresh();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _staged.Clear();
        ApplyButton.IsEnabled = false;
        Hint.Text = "Drag a display to move it. It snaps flush against its neighbour.";
        Build();
    }
}
