using DispCtrl.App.Services;
using DispCtrl.Display;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.System;

namespace DispCtrl.App.Views;

public sealed partial class StartupSettings : UserControl
{
    private bool _ready;
    private bool _updating;

    public bool EngineStartupEnabled { get; private set; }
    public bool StartMenuShortcutEnabled { get; private set; }
    public bool DesktopShortcutEnabled { get; private set; }
    public bool EngineAvailable => App.ViewModel.CanToggleEngine;
    public DispCtrl.App.ViewModels.MainViewModel ViewModel => App.ViewModel;

    public StartupSettings()
    {
        ReadState();
        InitializeComponent();
        if (StartupIntegration.IsPackaged)
        {
            StartMenuToggle.IsEnabled = false;
            DesktopToggle.IsEnabled = false;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ReadState();
        try { EngineStartupEnabled = await StartupIntegration.ReadEngineStartupAsync(); }
        catch (Exception ex) { ShowResult(ex.Message, InfoBarSeverity.Error); }
        Bindings.Update();
        _ready = true;
    }

    private void ReadState()
    {
        if (!StartupIntegration.IsPackaged) EngineStartupEnabled = StartupIntegration.StartsEngineAtSignIn;
        StartMenuShortcutEnabled = StartupIntegration.IsPackaged || StartupIntegration.HasStartMenuShortcut;
        DesktopShortcutEnabled = StartupIntegration.HasDesktopShortcut;
    }

    private async void OnEngineStartupToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready || _updating) return;
        _updating = true;
        try
        {
            await StartupIntegration.SetEngineStartupAsync(EngineStartupToggle.IsOn, EngineAvailable ? App.ViewModel.EnginePath : null);
            EngineStartupEnabled = await StartupIntegration.ReadEngineStartupAsync();
            ShowResult(EngineStartupEnabled ? "The engine will start when you sign in." : "Engine auto-start is off.", InfoBarSeverity.Success);
        }
        catch (Exception ex) { ShowResult(ex.Message, InfoBarSeverity.Error); }
        finally { Bindings.Update(); _updating = false; }
    }

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
        App.ViewModel.ShowFooterStatus(message);
        ResultBar.Message = message;
        ResultBar.Severity = severity;
        ResultBar.IsOpen = true;
    }
}
