using System.Runtime.InteropServices;
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
public static partial class DisplayIdentifier
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
    /// Plate side, in real inches on the glass.
    /// </summary>
    /// <remarks>
    /// Physical, not pixels and not DIPs. Pixels are hopeless across panels of
    /// different density. DIPs are no better here: Windows renders this laptop
    /// at 192 DPI while the panel is physically 242 PPI, so a DIP is a
    /// different real size on each screen and the same constant still comes
    /// out visibly smaller on one than the other. Driving it from the EDID's
    /// millimetres is the only way both markers are genuinely the same size
    /// when you look from one screen to the other.
    /// </remarks>
    private const double PlateInches = 1.55;

    /// <summary>Fallback plate side, in DIPs, when the EDID gives no size.</summary>
    private const double PlateDip = 148;

    /// <summary>Numeral height as a fraction of the plate.</summary>
    private const double GlyphRatio = 0.5;

    /// <summary>Extra width each digit past the first adds, as a fraction.</summary>
    private const double DigitWidthRatio = 0.34;

    /// <summary>Largest share of the shorter panel edge the plate may take.</summary>
    private const double MaxShareOfPanel = 0.25;

    /// <summary><c>DWMWA_WINDOW_CORNER_PREFERENCE</c>.</summary>
    private const uint WindowCornerPreference = 33;

    /// <summary><c>DWMWCP_ROUND</c> — the full radius, as a dialog gets.</summary>
    private const uint CornerRound = 2;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, ref uint value, uint size);

    private static void RoundCorners(Window window)
    {
        try
        {
            nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            uint preference = CornerRound;
            _ = DwmSetWindowAttribute(hwnd, WindowCornerPreference, ref preference, sizeof(uint));
        }
        catch (Exception)
        {
            // Older builds do not know the attribute. Square corners are a
            // cosmetic loss, not a reason to fail showing the number.
        }
    }

    private static Window CreateOverlay(DisplayInfo display, int number)
    {
        var window = new Window();

        // AppWindow is positioned in raw pixels; the XAML inside is laid out in
        // DIPs. Conflating the two is what made the number overflow its plate
        // on a scaled panel and sit lost in the middle of it on an unscaled one.
        double scale = display.Scale > 0 ? display.Scale : 1.0;

        string caption = number.ToString();

        // Real pixel density, not the scaling factor — see PlateInches.
        double wanted = display.PhysicalPpi > 0
            ? PlateInches * display.PhysicalPpi / scale
            : PlateDip;

        double sideDip = Math.Min(
            wanted,
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
            // 8 DIP, because that is exactly what DWMWCP_ROUND cuts the window
            // frame to. A larger radius here would be drawn inside a frame
            // clipped tighter than it, which is what left the curve chopped.
            CornerRadius = new CornerRadius(8),
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

        // The plate's rounded corners were being drawn inside a square window,
        // so each corner showed the window's own background and the curve came
        // out chopped. DWM has to round the window itself; nothing done inside
        // the XAML can cut the frame.
        RoundCorners(window);

        // Bottom-left, inset a tenth of the panel on each side. Centred put it
        // squarely over whatever the user was looking at; a corner marker is
        // readable without obscuring the screen it is labelling.
        int width = (int)Math.Round(widthDip * scale);
        int height = (int)Math.Round(sideDip * scale);
        int padX = (int)Math.Round(display.Bounds.Width * 0.10);
        int padY = (int)Math.Round(display.Bounds.Height * 0.10);

        // Move first, then size. MoveAndResize in one call is the trap: a new
        // window is born on the primary display, so moving it to a panel at a
        // different scale raises a DPI change, and Windows rescales the window
        // by that ratio *after* applying the size. Asking for 375px on a 200%
        // panel got 750. Moving first lets the DPI change happen while the
        // window is still the wrong size, and the resize afterwards is taken
        // literally.
        app.Move(new PointInt32(
            display.Bounds.Left + padX,
            display.Bounds.Bottom - padY - height));

        app.Resize(new SizeInt32(width, height));

        window.Activate();
        return window;
    }
}
