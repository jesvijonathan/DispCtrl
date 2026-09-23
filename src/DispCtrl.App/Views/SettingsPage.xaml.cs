using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DispCtrl.App.Services;
using DispCtrl.App.ViewModels;
using DispCtrl.Control;

namespace DispCtrl.App.Views;

public sealed partial class SettingsPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public SettingsPage()
    {
        InitializeComponent();
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
        UpdateCard.Header = $"Updates (this is {version})";
    }

    private void OnCheckUpdates(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.Releases);

    /// <summary>The same steps as <c>dispctrl maintenance repair</c>, then an engine restart.</summary>
    private async void OnRepair(object sender, RoutedEventArgs e)
    {
        RepairButton.IsEnabled = false;
        try
        {
            JsonObject result = await Task.Run(() => new ControlService().Execute(new JsonObject
            {
                ["version"] = 1, ["command"] = "maintenance.repair", ["args"] = new JsonObject(),
            }));
            if (result["ok"]?.GetValue<bool>() != true)
            {
                Say(result["error"]?["message"]?.GetValue<string>() ?? "Repair failed.", InfoBarSeverity.Error);
                return;
            }
            bool restarted = await ViewModel.RestartEngineAsync();
            IEnumerable<string> lines = (result["data"]?["done"]?.AsArray() ?? []).Concat(result["data"]?["notes"]?.AsArray() ?? [])
                .Select(n => n?.GetValue<string>()).OfType<string>();
            Say(string.Join(" ", lines) + (restarted ? " The engine was restarted." : " The engine did not restart; start it from the Engine card above."),
                restarted ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        finally { RepairButton.IsEnabled = true; }
    }

    private async void OnClearCache(object sender, RoutedEventArgs e)
    {
        JsonObject result = await Task.Run(() => new ControlService().Execute(new JsonObject
        {
            ["version"] = 1, ["command"] = "maintenance.clear-cache", ["args"] = new JsonObject(),
        }));
        if (result["ok"]?.GetValue<bool>() != true)
        {
            Say(result["error"]?["message"]?.GetValue<string>() ?? "Could not clear the cache.", InfoBarSeverity.Error);
            return;
        }
        int files = result["data"]?["removed"]?.AsArray().Count ?? 0;
        Say(files == 0 ? "There was nothing to clear."
            : $"Cleared {files} file(s), {result["data"]?["kilobytes"]} KB. Monitors are read again the next time the engine starts or one is plugged in.",
            InfoBarSeverity.Success);
    }

    private async void OnResetAll(object sender, RoutedEventArgs e)
    {
        // A whole-desk reset can change several visible features at once.
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Reset all DispCtrl settings?",
            Content = "Restore DispCtrl's display settings, taskbar behaviour, night light, focus, OLED care, keep awake, quick panel and default shortcuts. "
                    + "Monitor names and preset rules are kept. Windows display settings and the monitors' own factory settings are not changed.",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.ResetEverything();
            Say("Every setting is back to its default.", InfoBarSeverity.Success);
        }
    }

    private void Say(string message, InfoBarSeverity severity)
    {
        MaintenanceResult.Message = message;
        MaintenanceResult.Severity = severity;
        MaintenanceResult.IsOpen = true;
    }
}
