using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Presets;
using DispCtrl.Core.Settings;
using DispCtrl.Display.Presets;

namespace DispCtrl.App.Views;

public sealed partial class PresetsPreviewPage : Page
{
    public PresetsPreviewPage() => InitializeComponent();

    private async void OnExportCurrent(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = $"DispCtrl-{DateTime.Now:yyyy-MM-dd-HHmm}" };
        picker.FileTypeChoices.Add("DispCtrl configuration", [".json"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null) return;

        ExportCurrentButton.IsEnabled = false;
        App.ViewModel.ShowFooterStatus("Reading the current configuration…", busy: true);
        try
        {
            await Task.Run(() =>
            {
                DispCtrlSettings settings = SettingsStore.Load();
                List<DisplayInfo> displays = DisplayRegistry.Enumerate();
                var export = new CurrentConfigurationExport
                {
                    Settings = settings,
                    CurrentDesk = PresetService.Capture("Current configuration", displays, settings),
                };
                File.WriteAllText(file.Path,
                    System.Text.Json.JsonSerializer.Serialize(export, PresetJsonContext.Default.CurrentConfigurationExport));
            });
            App.ViewModel.ShowFooterStatus($"Configuration exported to {file.Name}.");
        }
        catch (Exception ex)
        {
            App.ViewModel.ShowFooterStatus($"Export failed: {ex.Message}");
        }
        finally
        {
            ExportCurrentButton.IsEnabled = true;
        }
    }
}
