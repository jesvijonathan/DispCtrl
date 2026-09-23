using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DispCtrl.App.ViewModels;

namespace DispCtrl.App.Views;

public sealed partial class EnginePage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public EnginePage() => InitializeComponent();

    private void OnEngineToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.IsOn != ViewModel.EngineRunning)
            ViewModel.SetEngineRunning(toggle.IsOn);
    }

    private void OnOpenSettingsFolder(object sender, RoutedEventArgs e) =>
        Reveal(ViewModel.SettingsPath, select: true);

    private void OnOpenLog(object sender, RoutedEventArgs e) =>
        Reveal(ViewModel.LogPath, select: false);

    /// <summary>
    /// Opens a file, or shows it in Explorer.
    /// </summary>
    /// <remarks>
    /// <c>UseShellExecute</c> is required: these are documents, not
    /// executables, and only the shell knows what to open them with.
    /// </remarks>
    private static void Reveal(string path, bool select)
    {
        try
        {
            if (select)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true,
                })?.Dispose();
                return;
            }

            if (!File.Exists(path)) return;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception)
        {
            // Opening a file is a convenience. If the shell refuses, the path
            // is on screen beside the button and can be copied.
        }
    }
}
