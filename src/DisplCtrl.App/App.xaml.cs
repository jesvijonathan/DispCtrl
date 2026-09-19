using Microsoft.UI.Xaml;
using DisplCtrl.App.ViewModels;

namespace DisplCtrl.App;

public partial class App : Application
{
    private Window? _window;

    /// <summary>
    /// The main window, so pages can parent a file picker to it. WinUI 3 has no
    /// ambient parent window the way UWP did.
    /// </summary>
    public static Window MainWindow { get; private set; } = null!;
    public static bool HasMainWindow => MainWindow is not null;
    public static void ShowMainWindow() => MainWindow?.Activate();

    /// <summary>
    /// The one view model, shared by every page.
    /// </summary>
    /// <remarks>
    /// Shared rather than per-page so a toggle on Displays and the status shown
    /// on Engine cannot disagree: they are reading the same objects, not two
    /// copies of the settings file that drifted apart.
    /// </remarks>
    public static MainViewModel ViewModel { get; } = new();

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        MainWindow = _window;
        _window.Activate();
    }
}
