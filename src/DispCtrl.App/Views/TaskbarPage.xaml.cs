using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DispCtrl.App.ViewModels;

namespace DispCtrl.App.Views;

/// <summary>
/// Everything about how the hidden taskbars behave.
/// </summary>
/// <remarks>
/// Its own page rather than a section of Settings, because it is the bulk of
/// what DispCtrl started as and it was crowding out the handful of genuinely
/// application-wide settings. What stayed behind in Settings is what is not
/// about the taskbar: logging and the reset.
/// <para>
/// Which monitors hide their taskbar is not here. That is per display, and it
/// belongs beside the display it applies to.
/// </para>
/// </remarks>
public sealed partial class TaskbarPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public TaskbarPage() => InitializeComponent();

    private void OnRestorePolling(object sender, RoutedEventArgs e) => ViewModel.RestorePolling();

    private void OnResetTaskbarSurface(object sender, RoutedEventArgs e) => ViewModel.ResetTaskbarSurface();

    private async void OnRestartExplorer(object sender, RoutedEventArgs e) => await ViewModel.RestartExplorerAsync();

    private void OnResetTaskbarFeatures(object sender, RoutedEventArgs e) => ViewModel.ResetTaskbarFeatures();

    private void OnResetTaskbarBehaviour(object sender, RoutedEventArgs e) => ViewModel.ResetTaskbarBehaviour();

    private async void OnWindowsTaskbar(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:taskbar"));
}
