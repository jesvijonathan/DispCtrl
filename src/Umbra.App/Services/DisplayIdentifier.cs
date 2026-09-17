using Microsoft.UI;
using Windows.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Umbra.Core.Displays;
using Windows.Graphics;

namespace Umbra.App.Services;

/// <summary>
/// Flashes a large number on each display, the way Windows' Identify does.
/// </summary>
/// <remarks>
/// The number shown is the one Umbra uses throughout — sorted built-in first,
/// then left to right — rather than Windows' own ordering. That is the point:
/// it answers "which card on screen is this monitor?", which is only useful if
/// it matches the list beside it.
/// </remarks>
public static class DisplayIdentifier
{
    private static readonly List<Window> Open = [];
    private static DispatcherTimer? _timer;

    /// <summary>Shows the overlays, replacing any already on screen.</summary>
    public static void Show(IReadOnlyList<DisplayInfo> displays, TimeSpan duration)
    {
        Hide();

        for (int i = 0; i < displays.Count; i++)
            Open.Add(CreateOverlay(displays[i], i + 1));

        _timer = new DispatcherTimer { Interval = duration };
        _timer.Tick += (_, _) => Hide();
        _timer.Start();
    }

    public static void Hide()
    {
        _timer?.Stop();
        _timer = null;

        foreach (Window w in Open)
        {
            try
            {
                w.Close();
            }
            catch (Exception)
            {
                // Already gone; nothing to recover.
            }
        }

        Open.Clear();
    }

    /// <summary>
    /// Plate side, in device-independent pixels.
    /// </summary>
    /// <remarks>
    /// Deliberately a DIP measurement rather than a fraction of the pixel
    /// count. Two panels at the same physical size but different resolutions
    /// are the same number of DIPs across, so one constant is the same
    /// apparent size on both — which is the whole job of an identify marker
    /// shown on several screens at once.
    /// </remarks>
    private const double PlateDip = 148;

    /// <summary>Numeral height as a fraction of the plate.</summary>
    private const double GlyphRatio = 0.5;

    /// <summary>Extra width each digit past the first adds, as a fraction.</summary>
    private const double DigitWidthRatio = 0.34;

    /// <summary>Largest share of the shorter panel edge the plate may take.</summary>
    private const double MaxShareOfPanel = 0.25;

    private static Window CreateOverlay(DisplayInfo display, int number)
    {
        var window = new Window();

        // AppWindow is positioned in raw pixels; the XAML inside is laid out in
        // DIPs. Conflating the two is what made the number overflow its plate
        // on a scaled panel and sit lost in the middle of it on an unscaled one.
        double scale = display.Scale > 0 ? display.Scale : 1.0;

        string caption = number.ToString();

        double sideDip = Math.Min(
            PlateDip,
            Math.Min(display.Bounds.Width, display.Bounds.Height) / scale * MaxShareOfPanel);

        // Grow with the digit count instead of cramming "10" into the width of
        // a "1": the plate stays the same height on every display, and only a
        // wider number widens it.
        double widthDip = sideDip * (1 + (DigitWidthRatio * (caption.Length - 1)));

        var text = new TextBlock
        {
            Text = caption,
            FontSize = sideDip * GlyphRatio,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Windows' own identifier is a soft-cornered dark plate with a light
        // numeral, not a flat accent block. Matching that is most of what makes
        // it read as part of the system.
        window.Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 32, 32, 32)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(sideDip * 0.08),
            Child = text,
        };

        AppWindow app = window.AppWindow;

        // Borderless, always on top, and absent from the task bar and Alt+Tab:
        // this is a transient marker, not a window anyone should switch to.
        if (app.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
        }

        app.IsShownInSwitchers = false;

        // Bottom-left, inset a tenth of the panel on each side. Centred put it
        // squarely over whatever the user was looking at; a corner marker is
        // readable without obscuring the screen it is labelling.
        int width = (int)Math.Round(widthDip * scale);
        int height = (int)Math.Round(sideDip * scale);
        int padX = (int)Math.Round(display.Bounds.Width * 0.10);
        int padY = (int)Math.Round(display.Bounds.Height * 0.10);

        app.MoveAndResize(new RectInt32(
            display.Bounds.Left + padX,
            display.Bounds.Bottom - padY - height,
            width,
            height));

        window.Activate();
        return window;
    }
}
