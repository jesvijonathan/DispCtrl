using DispCtrl.App.ViewModels;
using DispCtrl.App.Views.Controls;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DispCtrl.App.Views.Pages;

/// <summary>Focus mode, OLED protection, and keeping awake or turning the displays off.</summary>
/// <remarks>
/// Moved off the Displays page, which had grown to every feature DispCtrl has:
/// that page is now the displays themselves, and each feature for the whole
/// desk has a page named for what it is about.
/// </remarks>
public sealed partial class ScreenCarePage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public ScreenCarePage()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
    }

    /// <summary>An opened card realises all its rows: see <see cref="ExpanderLayout"/>.</summary>
    private void OnExpanderExpanded(object? sender, EventArgs e) => ExpanderLayout.RealiseAll(sender);

    private void OnResetFocus(object sender, RoutedEventArgs e) => ViewModel.ResetFocusSettings();
    private void OnResetOled(object sender, RoutedEventArgs e) => ViewModel.ResetOledSettings();
    private void OnTurnOffDisplays(object sender, RoutedEventArgs e) => ViewModel.DisplaysOff = true;
    private void OnResetAwake(object sender, RoutedEventArgs e) => ViewModel.ResetAwakeSettings();
}
