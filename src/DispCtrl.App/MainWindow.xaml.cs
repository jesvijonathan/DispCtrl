using DispCtrl.App.Views.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace DispCtrl.App;

public sealed partial class MainWindow : Window
{
    public ViewModels.MainViewModel ViewModel => App.ViewModel;

    public bool PresetsEnabled => DispCtrl.Core.FeatureFlags.Presets;
    /// <summary>
    /// How often the engine's state and the display layout are re-read.
    /// </summary>
    /// <remarks>
    /// Polled rather than subscribed because the engine is a separate process
    /// that can be started or stopped by the scheduled task, the CLI, or a
    /// crash — none of which this window would hear about. The timer stops
    /// whenever the window is not visible, so a minimised panel costs nothing.
    /// </remarks>
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _statusTimer;

    public MainWindow()
    {
        InitializeComponent();

        Title = AppTitle.Text = DispCtrl.Core.BuildInfo.AppTitle;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        OpenAtSize(1020, 800);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.PreferredMinimumWidth = 640;

        _statusTimer = new DispatcherTimer { Interval = StatusPollInterval };
        _statusTimer.Tick += (_, _) =>
        {
            if (!CanPollStatus) { _statusTimer.Stop(); return; }
            App.ViewModel.RefreshEngineStatus();
            App.ViewModel.RaiseAwakeStatus();

            // A monitor plugged in or unplugged while the window is open should
            // appear or disappear on its own, without reaching for Rescan. The
            // check is one cheap enumeration; the rebuild behind it only runs
            // when the layout genuinely changed.
            App.ViewModel.RefreshIfDisplaysChanged(settled: false);
        };
        _statusTimer.Start();

        // Nothing on screen means nothing worth polling for.
        AppWindow.Changed += (_, args) =>
        {
            UpdateStatusPolling();
            // Restored from the taskbar goes straight back to where it was,
            // which may be a display that has since gone. That restore is a
            // move (from -32000), not a presenter change. A drag never trips
            // this: the title bar stays under the pointer, on a display.
            if (args.DidPositionChange || args.DidPresenterChange || args.DidVisibilityChange) KeepOnScreen();
        };

        // The settings file is shared with the engine's CLI, so anything shown
        // here can be stale by the time the window is looked at again.
        Activated += (_, e) =>
        {
            UpdateStatusPolling();
            if (e.WindowActivationState == WindowActivationState.Deactivated) return;
            App.ViewModel.ReloadFromDisk();
        };

        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            // Closing the last window ends the process; a save a slider left
            // pending would go with it.
            App.ViewModel.FlushPendingSave();
        };

        ContentFrame.Navigate(typeof(DisplaysPage), null, new EntranceNavigationTransitionInfo());

        // A display unplugged while the window was on it: bring the window
        // over now, not at the next time someone tries to open it.
        App.ViewModel.DisplaysRebuilt += KeepOnScreen;
        Closed += (_, _) => App.ViewModel.DisplaysRebuilt -= KeepOnScreen;
    }

    /// <summary>Moves the window onto a display that is attached, when it is no longer on one.</summary>
    /// <remarks>
    /// Opened while a monitor was connected and left there, the window kept
    /// that monitor's coordinates after it was unplugged. Opening the app
    /// again, or clicking it on the taskbar, showed it for a frame and then
    /// drew it where no display is - it looked like it opened and vanished.
    /// Enough of the title bar to grab (120 x 32) must be on a work area;
    /// otherwise the window is centred on the main display, no larger than it.
    /// A maximized window is restored first, moved, and maximized there.
    /// </remarks>
    public void KeepOnScreen()
    {
        // Minimized, a window's position is -32000: nowhere, and not a problem.
        if (!AppWindow.IsVisible || AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized }) return;
        PointInt32 at = AppWindow.Position;
        SizeInt32 size = AppWindow.Size;
        var grip = new RectInt32(at.X + Math.Max(0, size.Width / 2 - 60), at.Y, Math.Min(120, size.Width), 32);
        DisplayArea? area = DisplayArea.GetFromRect(grip, DisplayAreaFallback.None);
        if (area is not null && Overlap(grip, area.WorkArea) >= grip.Width * grip.Height / 2) return;

        bool maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized } p && Restore(p);
        RectInt32 work = DisplayArea.Primary.WorkArea;
        size = AppWindow.Size;
        int width = Math.Min(size.Width, work.Width), height = Math.Min(size.Height, work.Height);
        // Moved, then sized: a move across scales rescales after a combined one.
        AppWindow.Move(new PointInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2));
        AppWindow.Resize(new SizeInt32(width, height));
        if (maximized && AppWindow.Presenter is OverlappedPresenter again) again.Maximize();

        static bool Restore(OverlappedPresenter presenter) { presenter.Restore(); return true; }
        static long Overlap(RectInt32 a, RectInt32 b)
        {
            long w = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
            long h = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
            return w > 0 && h > 0 ? w * h : 0;
        }
    }

    /// <summary>Opens at a size in DIPs, scaled for the display it opens on and kept inside it.</summary>
    /// <remarks>
    /// <c>AppWindow.Resize</c> takes physical pixels. 1020 x 800 was right on
    /// the Dell at 100%, which was primary; with the laptop alone (200%) it
    /// opened at half that, a 510 x 400 window. Resized only, never moved:
    /// a move across displays at different scales rescales after a resize.
    /// </remarks>
    private void OpenAtSize(int width, int height)
    {
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (scale <= 0) scale = 1;
        RectInt32 area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new SizeInt32(
            Math.Min((int)Math.Round(width * scale), area.Width),
            Math.Min((int)Math.Round(height * scale), area.Height)));
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    private bool CanPollStatus => AppWindow.IsVisible
        && AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    private void UpdateStatusPolling()
    {
        if (CanPollStatus) _statusTimer.Start(); else _statusTimer.Stop();
    }

    private void OnEngineToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.IsOn != ViewModel.EngineRunning)
            ViewModel.SetEngineRunning(toggle.IsOn);
    }

    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if ((args.InvokedItemContainer as NavigationViewItem)?.Tag as string == "sponsor")
            DispCtrl.App.Services.ProjectLinks.Open(DispCtrl.App.Services.ProjectLinks.SupportPage);
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string? tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;

        Type target = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : tag switch
            {
                "brightness" => typeof(BrightnessPage),
                "care" => typeof(ScreenCarePage),
                "windows" => typeof(WindowsPage),
                "taskbar" => typeof(TaskbarPage),
                "presets" => PresetsEnabled ? typeof(PresetsPage) : typeof(PresetsPreviewPage),
                "quickpanel" => typeof(QuickPanelPage),
                "hotkeys" => typeof(HotkeysPage),
                "devices" => typeof(DevicesPage),
                "help" => typeof(HelpPage),
                "about" => typeof(AboutPage),
                _ => typeof(DisplaysPage),
            };

        if (ContentFrame.CurrentSourcePageType == target) return;

        ContentFrame.Navigate(target, null, new EntranceNavigationTransitionInfo());
    }

    /// <summary>Selects a page by its navigation tag, as though it had been clicked.</summary>
    /// <remarks>
    /// Through the navigation item rather than the frame, so the pane's
    /// highlight moves with the page. Navigating the frame directly would show
    /// the Quick panel page with Displays still selected beside it.
    /// </remarks>
    public void ShowPage(string tag)
    {
        foreach (object item in Nav.MenuItems.Concat(Nav.FooterMenuItems))
        {
            if (item is NavigationViewItem entry && entry.Tag as string == tag)
            {
                Nav.SelectedItem = entry;
                return;
            }
        }
    }
}
