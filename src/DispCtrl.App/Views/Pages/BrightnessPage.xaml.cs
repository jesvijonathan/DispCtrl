using DispCtrl.App.ViewModels;
using DispCtrl.App.Views.Controls;
using DispCtrl.Core.Color;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DispCtrl.App.Views.Pages;

/// <summary>Unison brightness and the light sensor, night light, and dark mode.</summary>
/// <remarks>
/// Moved off the Displays page, which had grown to every feature DispCtrl has:
/// that page is now the displays themselves, and each feature for the whole
/// desk has a page named for what it is about.
/// </remarks>
public sealed partial class BrightnessPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;
    // The light sensor's reading, live while its options are open, so covering
    // it or shining a light at it shows before "This is dark" or "bright" is
    // pressed. Only then, and only while the window can be seen.
    private readonly DispatcherTimer _ambientRefresh = new() { Interval = TimeSpan.FromSeconds(2) };

    public BrightnessPage()
    {
        InitializeComponent();
        _ambientRefresh.Tick += (_, _) => _ = ViewModel.RefreshAmbientReadingAsync();
        Unloaded += (_, _) =>
        {
            _ambientRefresh.Stop();
            ViewModel.PropertyChanged -= OnViewModelChanged;
        };
        // Dark mode is read from Windows rather than stored here, so the switch
        // has to be told when something else moves it - Windows' own Settings,
        // a theme, or its sunset schedule. This is the notification WinUI
        // already raises for exactly that.
        ActualThemeChanged += (_, _) => ViewModel.RaiseTheme();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.LoadAmbientSensorsAsync();
        ViewModel.PropertyChanged -= OnViewModelChanged;
        ViewModel.PropertyChanged += OnViewModelChanged;
        UpdateAmbientRefresh();
    }

    /// <summary>An opened card realises all its rows: see <see cref="ExpanderLayout"/>.</summary>
    private void OnExpanderExpanded(object? sender, EventArgs e) => ExpanderLayout.RealiseAll(sender);

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.AmbientOptionsOpen) or "") UpdateAmbientRefresh();
    }

    private void UpdateAmbientRefresh()
    {
        if (ViewModel.AmbientOptionsOpen && App.MainWindow?.AppWindow.IsVisible == true) _ambientRefresh.Start();
        else _ambientRefresh.Stop();
    }

    private void OnOpenWindowsColours(object sender, RoutedEventArgs e) =>
        WindowsTheme.OpenSettings();
    private void OnOpenWindowsNightLight(object sender, RoutedEventArgs e) =>
        WindowsNightLight.OpenSettings();
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
    private void OnCancelCalibration(object sender, RoutedEventArgs e) =>
        ViewModel.CancelCalibration();
    private void OnRecalibrate(object sender, RoutedEventArgs e) =>
        ViewModel.BeginCalibration();
    private async void OnAmbientCaptureDark(object sender, RoutedEventArgs e) =>
        SayAmbient(await ViewModel.CaptureAmbientAsync(dark: true));
    private async void OnAmbientCaptureBright(object sender, RoutedEventArgs e) =>
        SayAmbient(await ViewModel.CaptureAmbientAsync(dark: false));
    private async void OnAmbientForget(object sender, RoutedEventArgs e) =>
        SayAmbient(await ViewModel.ForgetAmbientAsync());
    private void SayAmbient(string? problem)
    {
        if (problem is not null) ViewModel.ShowFooterStatus(problem);
    }
}
