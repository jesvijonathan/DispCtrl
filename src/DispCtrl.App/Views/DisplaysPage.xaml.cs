using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using DispCtrl.App.ViewModels;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;
using WinRT.Interop;

namespace DispCtrl.App.Views;

public sealed partial class DisplaysPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;
    // Each check asks Explorer for every display's wallpaper over COM. Coming
    // back to the window refreshes at once; the timer only catches a slideshow
    // changing while the page is watched.
    private readonly DispatcherTimer _wallpaperRefresh = new() { Interval = TimeSpan.FromSeconds(10) };
    private Window? _wallpaperWindow;

    public DisplaysPage()
    {
        InitializeComponent();
        _wallpaperRefresh.Tick += OnWallpaperRefresh;
        Unloaded += (_, _) => StopWallpaperRefresh();

        // Dark mode is read from Windows rather than stored here, so the switch
        // has to be told when something else moves it - Windows' own Settings,
        // a theme, or its sunset schedule. This is the notification WinUI
        // already raises for exactly that.
        ActualThemeChanged += (_, _) => ViewModel.RaiseTheme();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadArrangement();
        _ = ViewModel.LoadAmbientSensorsAsync();
        _wallpaperWindow = App.MainWindow;
        _wallpaperWindow.AppWindow.Changed += OnWallpaperWindowChanged;
        _wallpaperWindow.Activated += OnWallpaperWindowActivated;
        _wallpaperWindow.Closed += OnWallpaperWindowClosed;
        UpdateWallpaperVisibility();
        if (ViewModel.PresetsEnabled) ViewModel.Presets.RefreshDrift();
    }

    private void OnDisplayOverviewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Grid grid || grid.Children.Count < 2) return;
        bool narrow = e.NewSize.Width < 600;
        var preview = (FrameworkElement)grid.Children[0];
        var table = (FrameworkElement)grid.Children[1];
        Grid.SetRow(preview, 0);
        Grid.SetColumn(preview, narrow ? 0 : 1);
        Grid.SetColumnSpan(preview, narrow ? 2 : 1);
        Grid.SetRow(table, narrow ? 1 : 0);
        Grid.SetColumn(table, 0);
        Grid.SetColumnSpan(table, narrow ? 2 : 1);
        grid.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        grid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        table.Width = narrow ? double.NaN : Math.Clamp(e.NewSize.Width * 0.56, 400, 660);
        preview.HorizontalAlignment = HorizontalAlignment.Center;
        preview.MaxWidth = narrow ? Math.Min(300, e.NewSize.Width) :
            Math.Min(340, Math.Max(1, e.NewSize.Width - table.Width - grid.ColumnSpacing));
    }

    private bool CanRefreshWallpaper => _wallpaperWindow?.AppWindow.IsVisible == true
        && _wallpaperWindow.AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    private void OnWallpaperWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) => UpdateWallpaperVisibility();
    private void OnWallpaperWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        bool wasActive = ArrangeSurface.WallpapersActive;
        UpdateWallpaperVisibility();
        if (wasActive && args.WindowActivationState != WindowActivationState.Deactivated)
            OnWallpaperRefresh(null, EventArgs.Empty);
    }
    private void OnWallpaperWindowClosed(object sender, WindowEventArgs args) => StopWallpaperRefresh();

    private void UpdateWallpaperVisibility()
    {
        bool visible = CanRefreshWallpaper;
        if (ArrangeSurface.WallpapersActive == visible) return;
        ArrangeSurface.WallpapersActive = visible;
        _wallpaperRefresh.Stop();
        if (visible) OnWallpaperRefresh(null, EventArgs.Empty);
    }

    private async void OnWallpaperRefresh(object? sender, object args)
    {
        // Schedule the next check after this one finishes. A slow COM server
        // or wallpaper file must not create an ever-growing queue of reads.
        _wallpaperRefresh.Stop();
        ArrangeSurface.WallpapersActive = CanRefreshWallpaper;
        await ArrangeSurface.RefreshWallpapersAsync();
        if (CanRefreshWallpaper) _wallpaperRefresh.Start();
    }

    private void StopWallpaperRefresh()
    {
        _wallpaperRefresh.Stop();
        ArrangeSurface.WallpapersActive = false;
        if (_wallpaperWindow is not { } window) return;
        window.AppWindow.Changed -= OnWallpaperWindowChanged;
        window.Activated -= OnWallpaperWindowActivated;
        window.Closed -= OnWallpaperWindowClosed;
        _wallpaperWindow = null;
    }

    private void OnToggleMonitorControls(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Parent is not StackPanel panel || panel.Children.Count < 2) return;
        var controls = (ItemsControl)panel.Children[1];
        bool show = controls.Visibility != Visibility.Visible;
        controls.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        button.Content = show ? "Hide controls" : "Show controls";
    }

    private void OnOpenWindowsColours(object sender, RoutedEventArgs e) =>
        WindowsTheme.OpenSettings();

    private void OnResetFocus(object sender, RoutedEventArgs e) => ViewModel.ResetFocusSettings();

    private void OnResetOled(object sender, RoutedEventArgs e) => ViewModel.ResetOledSettings();
    private void OnTurnOffDisplays(object sender, RoutedEventArgs e) => ViewModel.DisplaysOff = true;

    private void OnResetAwake(object sender, RoutedEventArgs e) => ViewModel.ResetAwakeSettings();

    /// <remarks>
    /// Tagged rather than read from the DataContext, which is how every other
    /// per-display button on this page finds its display. The two are the same
    /// object here, but one of them stays true if this card is ever moved
    /// inside another template.
    /// </remarks>
    private void OnScreenRest(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DisplayViewModel display)
            display.RequestOledRest();
    }

    private async void OnPresetApply(object sender, RoutedEventArgs e) => await RunPreset(sender);

    private async void OnPresetDiscard(object sender, RoutedEventArgs e) => await RunPreset(sender);

    /// <remarks>
    /// Apply and Discard are the same operation — Discard means "put the desk
    /// back to the preset", which is exactly what applying it does. Two buttons
    /// because the two intentions read differently when something has drifted.
    /// </remarks>
    private static async Task RunPreset(object sender)
    {
        if (sender is not Button button) return;

        // Applying can mean a topology change, which blocks for seconds.
        button.IsEnabled = false;
        try
        {
            await App.ViewModel.Presets.ApplyAsync();
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void OnPresetSave(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        button.IsEnabled = false;
        try
        {
            await ViewModel.Presets.SaveOrCreateAsync();
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    /// <summary>
    /// Feeds the arrangement surface the current display geometry.
    /// </summary>
    /// <remarks>
    /// Pushed rather than bound: the canvas draws to scale from the whole
    /// desktop's bounding box, so it has to be rebuilt as a set rather than
    /// tracking individual items.
    /// </remarks>
    private void LoadArrangement()
    {
        var displays = new List<DisplayInfo>(ViewModel.Displays.Count);
        foreach (DisplayViewModel d in ViewModel.Displays) displays.Add(d.Info);
        ArrangeSurface.Load(displays);
    }

    /// <remarks>
    /// The device library replaced collect, view and submit here: its page
    /// shares one model at a time from the history, with nothing to collect first.
    /// </remarks>
    private void OnOpenDevices(object sender, RoutedEventArgs e) => App.ShowMainWindow("devices");

    private void OnCopyInfo(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DisplayViewModel display)
            Copy(display.InformationText);
    }

    private static void Copy(string text)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    private void OnConnectWireless(object sender, RoutedEventArgs e) =>
        MainViewModel.ConnectWirelessDisplay();

    private void OnOpenWindowsNightLight(object sender, RoutedEventArgs e) =>
        WindowsNightLight.OpenSettings();

    private void OnRescan(object sender, RoutedEventArgs e)
    {
        ViewModel.Refresh();
        LoadArrangement();
    }

    private void OnMakePrimary(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DisplayViewModel display)
            ViewModel.MakePrimary(display);
    }

    /// <summary>
    /// Hands off to Windows' Colour Management.
    /// </summary>
    /// <remarks>
    /// Installing and associating an ICC profile means enumerating installed
    /// profiles, validating them and juggling per-user versus system scope.
    /// Windows already does all of that; a half-built copy could leave a
    /// display with a broken profile, which is worse than one extra click.
    /// </remarks>
    private void OnColorManagement(object sender, RoutedEventArgs e) =>
        ColorProfile.OpenColorManagement();

    private async void OnResetDisplay(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DisplayViewModel display) return;
        var factory = new CheckBox
        {
            Content = "Also restore the monitor's own factory settings: colour, contrast and picture mode, as set in its menu. The monitor does this itself and it cannot be undone.",
            Visibility = display.Info.IsInternal ? Visibility.Collapsed : Visibility.Visible,
        };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"DispCtrl's settings for {display.Info.Label} go back to their defaults: taskbar hiding, OLED care, focus dimming, night light and its brightness range. Its name is kept.",
        });
        content.Children.Add(factory);
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Reset {display.Info.Label}?",
            Content = content,
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            display.ResetToDefaults(factory.IsChecked == true);
    }

    private async void OnCaptureLimits(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            // A DDC/CI capture is a round trip per external monitor. Without
            // this the button invites a second press that would capture the
            // same step twice and skip the other limit.
            button.IsEnabled = false;
            try
            {
                await ViewModel.CaptureLimitsAsync();
            }
            finally
            {
                button.IsEnabled = true;
            }

            return;
        }

        await ViewModel.CaptureLimitsAsync();
    }

    private async void OnToggleGammaRange(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        button.IsEnabled = false;
        try
        {
            await ViewModel.ToggleGammaRangeAsync();
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void OnCaptureWarmth(object sender, RoutedEventArgs e) =>
        ViewModel.CaptureWarmthLimits();

    private void OnCancelWarmth(object sender, RoutedEventArgs e) =>
        ViewModel.CancelWarmthCalibration();

    private void OnRecalibrateWarmth(object sender, RoutedEventArgs e) =>
        ViewModel.BeginWarmthCalibration();

    private void OnCancelCalibration(object sender, RoutedEventArgs e) =>
        ViewModel.CancelCalibration();

    private void OnRecalibrate(object sender, RoutedEventArgs e) =>
        ViewModel.BeginCalibration();

    private async void OnPickWallpaper(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DisplayViewModel display) return;

        // A WinUI 3 picker has no implicit parent window, so it must be told
        // which one to sit over. Without this it throws on an unpackaged app.
        nint handle = WindowNative.GetWindowHandle(App.MainWindow);
        await display.PickWallpaperAsync(handle);
        await ArrangeSurface.RefreshWallpapersAsync();
    }
}
