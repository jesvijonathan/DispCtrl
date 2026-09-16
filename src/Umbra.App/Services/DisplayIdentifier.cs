using Microsoft.UI;
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

    private static Window CreateOverlay(DisplayInfo display, int number)
    {
        var window = new Window();

        // Scale with the panel so the number reads the same physical size on a
        // 14-inch laptop and a 24-inch monitor.
        double box = Math.Min(display.Bounds.Width, display.Bounds.Height) * 0.28;

        var text = new TextBlock
        {
            Text = number.ToString(),
            FontSize = box * 0.55,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        window.Content = new Border
        {
            Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
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

        int size = (int)Math.Round(box);
        app.MoveAndResize(new RectInt32(
            display.Bounds.Left + (display.Bounds.Width - size) / 2,
            display.Bounds.Top + (display.Bounds.Height - size) / 2,
            size,
            size));

        window.Activate();
        return window;
    }
}
