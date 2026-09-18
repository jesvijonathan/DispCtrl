using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DisplCtrl.App.ViewModels;

namespace DisplCtrl.App.Views;

/// <summary>
/// Everything about how the hidden taskbars behave.
/// </summary>
/// <remarks>
/// Its own page rather than a section of Settings, because it is the bulk of
/// what DisplCtrl started as and it was crowding out the handful of genuinely
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
}
