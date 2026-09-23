using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using DispCtrl.App.Views;
using Windows.Graphics;

namespace DispCtrl.App;

public sealed partial class MainWindow : Window
{
    public ViewModels.MainViewModel ViewModel => App.ViewModel;

    public bool PresetsEnabled => DispCtrl.Core.FeatureFlags.Presets;
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

        Title = "DispCtrl";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        AppWindow.Resize(new SizeInt32(1020, 800));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.PreferredMinimumWidth = 640;

        _statusTimer = new DispatcherTimer { Interval = StatusPollInterval };
        _statusTimer.Tick += (_, _) =>
        {
            if (!CanPollStatus) { _statusTimer.Stop(); return; }
            App.ViewModel.RefreshEngineStatus();
            App.ViewModel.RaiseAwakeStatus();

            // A monitor plugged in or unplugged while the window is open should
            // appear or disappear on its own, without reaching for Rescan. The
            // check is one cheap enumeration; the rebuild behind it only runs
            // when the layout genuinely changed.
            App.ViewModel.RefreshIfDisplaysChanged();
        };
        _statusTimer.Start();

        // Nothing on screen means nothing worth polling for.
        AppWindow.Changed += (_, _) => UpdateStatusPolling();

        // The settings file is shared with the engine's CLI, so anything shown
        // here can be stale by the time the window is looked at again.
        Activated += (_, e) =>
        {
            UpdateStatusPolling();
            if (e.WindowActivationState == WindowActivationState.Deactivated) return;
            App.ViewModel.ReloadFromDisk();
        };

        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            // Closing the last window ends the process; a save a slider left
            // pending would go with it.
            App.ViewModel.FlushPendingSave();
        };

        ContentFrame.Navigate(typeof(DisplaysPage), null, new EntranceNavigationTransitionInfo());
    }

    private bool CanPollStatus => AppWindow.IsVisible
        && AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    private void UpdateStatusPolling()
    {
        if (CanPollStatus) _statusTimer.Start(); else _statusTimer.Stop();
    }

    private void OnEngineToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.IsOn != ViewModel.EngineRunning)
            ViewModel.SetEngineRunning(toggle.IsOn);
    }

    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if ((args.InvokedItemContainer as NavigationViewItem)?.Tag as string == "sponsor")
            DispCtrl.App.Services.ProjectLinks.Open(DispCtrl.App.Services.ProjectLinks.Sponsor);
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string? tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;

        Type target = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : tag switch
            {
                "taskbar" => typeof(TaskbarPage),
                "presets" => PresetsEnabled ? typeof(PresetsPage) : typeof(PresetsPreviewPage),
                "quickpanel" => typeof(QuickPanelPage),
                "hotkeys" => typeof(HotkeysPage),
                "devices" => typeof(DevicesPage),
                "help" => typeof(HelpPage),
                "about" => typeof(AboutPage),
                _ => typeof(DisplaysPage),
            };

        if (ContentFrame.CurrentSourcePageType == target) return;

        ContentFrame.Navigate(target, null, new EntranceNavigationTransitionInfo());
    }

    /// <summary>Selects a page by its navigation tag, as though it had been clicked.</summary>
    /// <remarks>
    /// Through the navigation item rather than the frame, so the pane's
    /// highlight moves with the page. Navigating the frame directly would show
    /// the Quick panel page with Displays still selected beside it.
    /// </remarks>
    public void ShowPage(string tag)
    {
        foreach (object item in Nav.MenuItems.Concat(Nav.FooterMenuItems))
        {
            if (item is NavigationViewItem entry && entry.Tag as string == tag)
            {
                Nav.SelectedItem = entry;
                return;
            }
        }
    }
}
