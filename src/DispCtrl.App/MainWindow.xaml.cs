using DispCtrl.App.Views.Pages;
using Microsoft.UI.Input;
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
        // A build without presets has no switcher; the flag is compiled in.
        PresetSwitcher.Visibility = ViewModel.PresetsEnabled ? Visibility.Visible : Visibility.Collapsed;
        TitleBar.SizeChanged += (_, _) => UpdateTitleBarPassthrough();
        WatchPresetDrift();
        if (ViewModel.CountWindowOpen()) SupportBanner.Visibility = Visibility.Visible;

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

    // ------------------------------------------------------------ presets --

    private string? _bannerDismissedFor;

    /// <summary>
    /// The switcher sits in the title bar, which is all drag region; its
    /// rectangle is handed back to the window so a press reaches the button.
    /// </summary>
    private void OnPresetSwitcherSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTitleBarPassthrough();

    private void UpdateTitleBarPassthrough()
    {
        if (PresetSwitcher is not { Visibility: Visibility.Visible } button || button.XamlRoot is null) return;
        double scale = button.XamlRoot.RasterizationScale;
        var bounds = button.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, button.ActualWidth, button.ActualHeight));
        var rect = new RectInt32((int)Math.Round(bounds.X * scale), (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale), (int)Math.Round(bounds.Height * scale));
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, [rect]);
    }

    /// <summary>Compared afresh as it opens, so it never shows a stale count.</summary>
    private void OnPresetFlyoutOpening(object? sender, object e) => ViewModel.Presets.RefreshDrift();

    /// <summary>Apply and Discard are one operation: put the desk back to the preset.</summary>
    private async void OnPresetApply(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        // Applying can mean a topology change, which blocks for seconds.
        button.IsEnabled = false;
        try { await ViewModel.Presets.ApplyAsync(); }
        finally { button.IsEnabled = true; }
    }

    private async void OnPresetSave(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        button.IsEnabled = false;
        try { await ViewModel.Presets.SaveOrCreateAsync(); }
        finally { button.IsEnabled = true; }
    }

    private void OnManagePresets(object sender, RoutedEventArgs e)
    {
        PresetSwitcher?.Flyout?.Hide();
        App.ShowMainWindow("presets");
    }

    /// <summary>
    /// The banner appears when the desk moves from the preset in use and goes
    /// once that is settled. Closed by hand, it stays away until the desk
    /// moves again - a different set of changes - rather than nagging about
    /// the same ones.
    /// </summary>
    private void WatchPresetDrift()
    {
        if (!ViewModel.PresetsEnabled) return;
        // The view model outlives this window - the process stays for the
        // quick panel and builds a new window each time - so the handler goes
        // with the window, or every closed window would stay alive and updated.
        ViewModel.Presets.PropertyChanged += OnPresetsChanged;
        Closed += (_, _) => ViewModel.Presets.PropertyChanged -= OnPresetsChanged;
        UpdatePresetBanner();
    }

    private void OnPresetsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModels.PresetsViewModel.DriftTooltip) or nameof(ViewModels.PresetsViewModel.Selected))
            DispatcherQueue.TryEnqueue(UpdatePresetBanner);
    }

    private string DriftSignature => $"{ViewModel.Presets.Selected}|{string.Join(";", ViewModel.Presets.Changes.Select(c => c.Setting + "=" + c.Now))}";

    private void UpdatePresetBanner()
    {
        var presets = ViewModel.Presets;
        bool show = presets.IsDirty && presets.Notices && !presets.Creating && DriftSignature != _bannerDismissedFor;
        if (show)
        {
            PresetDriftText.Text = $"The displays have changed from {presets.Selected}:";
            PresetDriftCount.Content = presets.Changes.Count == 1 ? "1 setting" : $"{presets.Changes.Count} settings";
        }
        PresetDriftBanner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSupportStar(object sender, RoutedEventArgs e) => SupportGiven(DispCtrl.App.Services.ProjectLinks.Repository);

    private void OnSupportSponsor(object sender, RoutedEventArgs e) => SupportGiven(DispCtrl.App.Services.ProjectLinks.SupportPage);

    private void SupportGiven(string link)
    {
        DispCtrl.App.Services.ProjectLinks.Open(link);
        ViewModel.SupportGiven();
        SupportActions.Visibility = Visibility.Collapsed;
        SupportText.Text = "Thank you - that helps more than you would think.";
        HideSupportSoon();
    }

    /// <summary>Closed without either: said so, a little sheepishly, and gone.</summary>
    /// <remarks>
    /// The owner asked for something goofy here rather than a plain line: the
    /// dots type out one at a time while the text gives a small wobble, then
    /// the rest of the sentence arrives and the banner fades. Skipped, like
    /// every animation in DispCtrl, when Windows has animation effects off.
    /// </remarks>
    private void OnSupportClose(object sender, RoutedEventArgs e)
    {
        ViewModel.SupportDeclined();
        SupportActions.Visibility = Visibility.Collapsed;
        const string rest = " maybe another time. Thanks for using DispCtrl.";
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            SupportText.Text = "Ok…" + rest;
            HideSupportSoon();
            return;
        }

        var wobble = new Microsoft.UI.Xaml.Media.CompositeTransform();
        SupportText.RenderTransformOrigin = new Windows.Foundation.Point(0, 0.5);
        SupportText.RenderTransform = wobble;
        string[] beats = ["Ok", "Ok.", "Ok..", "Ok…", "Ok…" + rest];
        int beat = 0;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(380);
        timer.Tick += (_, _) =>
        {
            SupportText.Text = beats[beat];
            Wobble(wobble, beat == beats.Length - 1 ? 0 : (beat % 2 == 0 ? -3 : 3));
            if (++beat < beats.Length) return;
            timer.Stop();
            HideSupportSoon();
        };
        timer.Start();
    }

    private static void Wobble(Microsoft.UI.Xaml.Media.CompositeTransform target, double degrees)
    {
        var tilt = new DoubleAnimationUsingKeyFrames();
        tilt.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(120), Value = degrees });
        tilt.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(320), Value = 0,
            EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut } });
        var bounce = new DoubleAnimationUsingKeyFrames();
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(100), Value = -2 });
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(300), Value = 0,
            EasingFunction = new BounceEase { Bounces = 1, EasingMode = EasingMode.EaseOut } });
        var board = new Storyboard();
        Storyboard.SetTarget(tilt, target);
        Storyboard.SetTargetProperty(tilt, "Rotation");
        Storyboard.SetTarget(bounce, target);
        Storyboard.SetTargetProperty(bounce, "TranslateY");
        board.Children.Add(tilt);
        board.Children.Add(bounce);
        board.Begin();
    }

    private void HideSupportSoon()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(3);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400) };
            var board = new Storyboard();
            Storyboard.SetTarget(fade, SupportBanner);
            Storyboard.SetTargetProperty(fade, "Opacity");
            board.Children.Add(fade);
            board.Completed += (_, _) => SupportBanner.Visibility = Visibility.Collapsed;
            board.Begin();
        };
        timer.Start();
    }

    private void OnPresetBannerClose(object sender, RoutedEventArgs e)
    {
        _bannerDismissedFor = DriftSignature;
        PresetDriftBanner.Visibility = Visibility.Collapsed;
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
                "misc" => typeof(MiscellaneousPage),
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
