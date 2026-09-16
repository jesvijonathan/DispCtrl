using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Umbra.App.ViewModels;
using Umbra.Display;
using WinRT.Interop;

namespace Umbra.App.Views;

public sealed partial class DisplaysPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public DisplaysPage() => InitializeComponent();

    private void OnRescan(object sender, RoutedEventArgs e) => ViewModel.Refresh();

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

    private async void OnPickWallpaper(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DisplayViewModel display) return;

        // A WinUI 3 picker has no implicit parent window, so it must be told
        // which one to sit over. Without this it throws on an unpackaged app.
        nint handle = WindowNative.GetWindowHandle(App.MainWindow);
        await display.PickWallpaperAsync(handle);
    }
}
