using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Umbra.App.Views;
using Windows.Graphics;

namespace Umbra.App;

public sealed partial class MainWindow : Window
{
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

        Title = "Umbra";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        AppWindow.Resize(new SizeInt32(1020, 800));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.PreferredMinimumWidth = 640;

        _statusTimer = new DispatcherTimer { Interval = StatusPollInterval };
        _statusTimer.Tick += (_, _) =>
        {
            App.ViewModel.RefreshEngineStatus();

            // A monitor plugged in or unplugged while the window is open should
            // appear or disappear on its own, without reaching for Rescan. The
            // check is one cheap enumeration; the rebuild behind it only runs
            // when the layout genuinely changed.
            App.ViewModel.RefreshIfDisplaysChanged();
        };
        _statusTimer.Start();

        // Nothing on screen means nothing worth polling for.
        AppWindow.Changed += (s, e) =>
        {
            if (!e.DidVisibilityChange) return;
            if (s.IsVisible) _statusTimer.Start(); else _statusTimer.Stop();
        };

        // The settings file is shared with the engine's CLI, so anything shown
        // here can be stale by the time the window is looked at again.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) return;
            App.ViewModel.ReloadFromDisk();
        };

        Closed += (_, _) => _statusTimer.Stop();

        ContentFrame.Navigate(typeof(DisplaysPage), null, new EntranceNavigationTransitionInfo());
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string? tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;

        Type target = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : tag switch
            {
                "taskbar" => typeof(TaskbarPage),
                "presets" => typeof(PresetsPage),
                "hotkeys" => typeof(HotkeysPage),
                "engine" => typeof(EnginePage),
                "help" => typeof(HelpPage),
                "about" => typeof(AboutPage),
                _ => typeof(DisplaysPage),
            };

        if (ContentFrame.CurrentSourcePageType == target) return;

        ContentFrame.Navigate(target, null, new EntranceNavigationTransitionInfo());
    }
}
