using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DispCtrl.App.ViewModels;

namespace DispCtrl.App.Views;

public sealed partial class EngineSettings : UserControl
{
    public MainViewModel ViewModel => App.ViewModel;
    public EngineSettings() => InitializeComponent();
    private void OnOpenSettingsFolder(object sender, RoutedEventArgs e) => Reveal(ViewModel.SettingsPath, true);
    private void OnOpenLog(object sender, RoutedEventArgs e) => Reveal(ViewModel.LogPath, false);

    private static void Reveal(string path, bool select)
    {
        try
        {
            if (select)
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
            else if (File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception) { }
    }
}
