using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Umbra.App.ViewModels;
using Umbra.Core.Displays;
using Umbra.Display;
using WinRT.Interop;

namespace Umbra.App.Views;

public sealed partial class DisplaysPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public DisplaysPage() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadArrangement();
        ViewModel.Presets.RefreshDrift();
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

    private async void OnWriteReport(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        button.IsEnabled = false;
        try
        {
            await ViewModel.WriteReportAsync();
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    /// <remarks>
    /// Written on demand if it is not there yet, so Open never dead-ends on a
    /// missing file.
    /// </remarks>
    private async void OnOpenReport(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(MainViewModel.ReportPath)) await ViewModel.WriteReportAsync();
        if (!File.Exists(MainViewModel.ReportPath)) return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = MainViewModel.ReportPath,
            UseShellExecute = true,
        });
    }

    private void OnIdentify(object sender, RoutedEventArgs e) => ViewModel.Identify();

    private void OnDetect(object sender, RoutedEventArgs e)
    {
        ViewModel.DetectDisplays();
        LoadArrangement();
    }

    private void OnOpenRotation(object sender, RoutedEventArgs e) =>
        MainViewModel.OpenRotationSettings();

    private void OnConnectWireless(object sender, RoutedEventArgs e) =>
        MainViewModel.ConnectWirelessDisplay();

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

    private void OnResetDisplay(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DisplayViewModel display)
            display.ResetToDefaults();
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
    }
}
