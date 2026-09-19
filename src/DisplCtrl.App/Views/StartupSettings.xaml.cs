using DisplCtrl.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.System;

namespace DisplCtrl.App.Views;

public sealed partial class StartupSettings : UserControl
{
    private bool _ready;
    private bool _updating;

    public bool EngineStartupEnabled { get; private set; }
    public bool StartMenuShortcutEnabled { get; private set; }
    public bool DesktopShortcutEnabled { get; private set; }
    public bool EngineAvailable => App.ViewModel.CanToggleEngine;

    public StartupSettings()
    {
        ReadState();
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ReadState();
        Bindings.Update();
        _ready = true;
    }

    private void ReadState()
    {
        EngineStartupEnabled = StartupIntegration.StartsEngineAtSignIn;
        StartMenuShortcutEnabled = StartupIntegration.HasStartMenuShortcut;
        DesktopShortcutEnabled = StartupIntegration.HasDesktopShortcut;
    }

    private void OnEngineStartupToggled(object sender, RoutedEventArgs e) =>
        Change(
            () => StartupIntegration.SetEngineStartup(
                EngineStartupToggle.IsOn,
                EngineAvailable ? App.ViewModel.EnginePath : null),
            EngineStartupToggle.IsOn
                ? "The engine will start when you sign in."
                : "Engine auto-start is off.");

    private void OnStartMenuToggled(object sender, RoutedEventArgs e) =>
        Change(
            () => StartupIntegration.SetStartMenuShortcut(StartMenuToggle.IsOn),
            StartMenuToggle.IsOn
                ? "Start Menu shortcut created."
                : "Start Menu shortcut removed.");

    private void OnDesktopToggled(object sender, RoutedEventArgs e) =>
        Change(
            () => StartupIntegration.SetDesktopShortcut(DesktopToggle.IsOn),
            DesktopToggle.IsOn
                ? "Desktop shortcut created."
                : "Desktop shortcut removed.");

    private void Change(Action action, string successMessage)
    {
        if (!_ready || _updating) return;

        try
        {
            action();
            ShowResult(successMessage, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowResult(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _updating = true;
            ReadState();
            Bindings.Update();
            _updating = false;
        }
    }

    private async void OnOpenStartupApps(object sender, RoutedEventArgs e)
    {
        if (!await Launcher.LaunchUriAsync(new Uri("ms-settings:startupapps")))
            ShowResult("Windows Startup Apps could not be opened.", InfoBarSeverity.Error);
    }

    private async void OnOpenStartupFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(StartupIntegration.StartupFolderPath);
            StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(StartupIntegration.StartupFolderPath);
            if (!await Launcher.LaunchFolderAsync(folder))
                ShowResult("The Startup folder could not be opened.", InfoBarSeverity.Error);
        }
        catch (Exception ex)
        {
            ShowResult(ex.Message, InfoBarSeverity.Error);
        }
    }

    private void ShowResult(string message, InfoBarSeverity severity)
    {
        ResultBar.Message = message;
        ResultBar.Severity = severity;
        ResultBar.IsOpen = true;
    }
}
