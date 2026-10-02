using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DispCtrl.App.ViewModels;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

namespace DispCtrl.App.Views;

/// <summary>Pinning windows on top, gathering them, and putting them back.</summary>
/// <remarks>
/// Moved off the Displays page, which had grown to every feature DispCtrl has:
/// that page is now the displays themselves, and each feature for the whole
/// desk has a page named for what it is about.
/// </remarks>
public sealed partial class WindowsPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public WindowsPage()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshPinnedWindows();
    }

    /// <summary>An opened card realises all its rows: see <see cref="ExpanderLayout"/>.</summary>
    private void OnExpanderExpanded(object? sender, EventArgs e) => ExpanderLayout.RealiseAll(sender);

    private void OnUnpin(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is WindowItem item) ViewModel.Unpin(item);
    }
    private void OnUnpinAll(object sender, RoutedEventArgs e) => ViewModel.UnpinAll();
    private void OnRefreshPins(object sender, RoutedEventArgs e) => ViewModel.RefreshPinnedWindows();
    private async void OnGather(object sender, RoutedEventArgs e)
    {
        // async void: an exception here would take the window down with it.
        try
        {
            if ((sender as FrameworkElement)?.Tag is DisplayViewModel display) await ViewModel.GatherAsync(display);
        }
        catch (Exception) { }
    }
}
