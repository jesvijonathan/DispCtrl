using System.Text.Json.Nodes;
using DispCtrl.App.Services;
using DispCtrl.App.ViewModels;
using DispCtrl.Control;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DispCtrl.App.Views.Pages;

public sealed partial class SettingsPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public SettingsPage()
    {
        InitializeComponent();
    }

    // ------------------------------------------------- company laptop switches --

    private bool _machineBusy;

    private void OnMachineExpanded(object sender, EventArgs e) => _ = FillMachineAsync();

    private static Task<JsonObject> Machine(string action, JsonObject? args = null) => Task.Run(() =>
        new ControlService().Execute(new JsonObject { ["version"] = 1, ["command"] = "machine." + action, ["args"] = args ?? [] }));

    /// <summary>Each switch as Windows has it now; read again after every change.</summary>
    private async Task FillMachineAsync(string? message = null)
    {
        JsonObject result = await Machine("get");
        MachineRows.Children.Clear();
        if (result["ok"]?.GetValue<bool>() != true)
        {
            MachineRows.Children.Add(new TextBlock { Text = result["error"]?["message"]?.ToString() ?? "Could not read the switches.", TextWrapping = TextWrapping.Wrap });
            return;
        }
        if (message is not null)
            MachineRows.Children.Add(new InfoBar { IsOpen = true, IsClosable = true, Message = message, Severity = InfoBarSeverity.Informational });
        if (result["data"]?["managed"]?.ToString() is { Length: > 0 } managed)
            MachineRows.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Message = managed, Severity = InfoBarSeverity.Warning,
                Title = "Managed by an organisation" });
        foreach (JsonNode? item in result["data"]?["switches"]?.AsArray() ?? [])
            if (item is not null) MachineRows.Children.Add(MachineRow(item));
    }

    private FrameworkElement MachineRow(JsonNode item)
    {
        string id = item["id"]!.ToString(), kind = item["kind"]!.ToString();
        string? value = item["value"]?.ToString();
        bool admin = item["needsAdmin"]?.GetValue<bool>() == true;

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel();
        words.Children.Add(new TextBlock { Text = item["label"]!.ToString() + (admin ? "  (administrator)" : "") });
        words.Children.Add(new TextBlock { Text = item["hint"]!.ToString(), TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        grid.Children.Add(words);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(controls, 1);
        grid.Children.Add(controls);

        async Task Set(object wanted)
        {
            if (_machineBusy) return;
            _machineBusy = true;
            try
            {
                JsonObject result = await Machine("set", new JsonObject { ["switch"] = id, ["value"] = JsonValue.Create(wanted.ToString()) });
                await FillMachineAsync(result["ok"]?.GetValue<bool>() == true ? null
                    : result["error"]?["message"]?.ToString() ?? "The change was not made.");
            }
            finally { _machineBusy = false; }
        }

        switch (kind)
        {
            case "switch":
            {
                // Value first, handler after: a switch set from code raises Toggled too.
                var toggle = new ToggleSwitch { IsOn = value == "on", OnContent = null, OffContent = null, MinWidth = 0 };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, "Machine " + id);
                toggle.Toggled += async (_, _) => await Set(toggle.IsOn ? "on" : "off");
                controls.Children.Add(toggle);
                break;
            }
            case "minutes":
            {
                var minutes = new NumberBox { Value = double.TryParse(value, out double m) ? m : 0, Minimum = 0, Maximum = 9999,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 120 };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(minutes, "Machine " + id);
                var apply = new Button { Content = "Set" };
                apply.Click += async (_, _) => await Set(double.IsNaN(minutes.Value) ? 0 : (int)minutes.Value);
                controls.Children.Add(minutes);
                controls.Children.Add(apply);
                break;
            }
            default:
            {
                controls.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(value) ? "Windows' own" : Path.GetFileName(value),
                    VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis });
                var choose = new Button { Content = "Choose…" };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choose, "Machine " + id);
                choose.Click += async (_, _) =>
                {
                    var picker = new Windows.Storage.Pickers.FileOpenPicker();
                    foreach (string type in new[] { ".jpg", ".jpeg", ".png", ".bmp" }) picker.FileTypeFilter.Add(type);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
                    if (await picker.PickSingleFileAsync() is { } file) await Set(file.Path);
                };
                controls.Children.Add(choose);
                if (!string.IsNullOrEmpty(value))
                {
                    var clear = new Button { Content = "Remove" };
                    clear.Click += async (_, _) => await Set("off");
                    controls.Children.Add(clear);
                }
                break;
            }
        }

        if (item["canUndo"]?.GetValue<bool>() == true)
        {
            var undo = new Button { Content = "Put back" };
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(undo, "Put back what Windows had before DispCtrl changed it.");
            undo.Click += async (_, _) =>
            {
                if (_machineBusy) return;
                _machineBusy = true;
                try
                {
                    JsonObject result = await Machine("undo", new JsonObject { ["switch"] = id });
                    await FillMachineAsync(result["ok"]?.GetValue<bool>() == true ? null : result["error"]?["message"]?.ToString());
                }
                finally { _machineBusy = false; }
            };
            controls.Children.Add(undo);
        }
        return grid;
    }

    /// <summary>A click is somebody asking, so this one check reaches GitHub whether or not the daily one is on.</summary>
    /// <remarks>
    /// A Store install opens the Store's updates page instead; a check that gets
    /// no answer falls back to the releases page, as the button always did.
    /// </remarks>
    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel.UpdatesFromStore) { ProjectLinks.Open("ms-windows-store://downloadsandupdates"); return; }
            if (!await ViewModel.CheckForUpdatesAsync(automatic: false)) ProjectLinks.Open(ProjectLinks.Releases);
        }
        catch (Exception) { ProjectLinks.Open(ProjectLinks.Releases); }
    }

    private void OnDownloadUpdate(object sender, RoutedEventArgs e) => ProjectLinks.Open(ViewModel.UpdateUrl);

    private void OnSkipUpdate(object sender, RoutedEventArgs e) => ViewModel.SkipUpdate();

    private async void OnAllowDdc(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BlockedMonitor monitor) return;
        try
        {
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = $"Talk to {monitor.Name} over DDC/CI again?",
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "If this monitor caused the crash, the next capabilities read may crash Windows again. Allow it only if the crash had another explanation.",
                },
                PrimaryButtonText = "Allow again",
                CloseButtonText = "Keep it blocked",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirm.ShowAsync() == ContentDialogResult.Primary) await ViewModel.AllowDdcAsync(monitor);
        }
        catch (Exception ex) { Say("Could not allow it: " + ex.Message, InfoBarSeverity.Error); }
    }

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

    /// <summary>The same step as <c>dispctrl restore undo</c>.</summary>
    private async void OnUndoRestore(object sender, RoutedEventArgs e)
    {
        JsonObject result = await Task.Run(() => new ControlService().Execute(new JsonObject
        {
            ["version"] = 1, ["command"] = "restore.undo", ["args"] = new JsonObject(),
        }));
        if (result["ok"]?.GetValue<bool>() != true)
        {
            Say(result["error"]?["message"]?.GetValue<string>() ?? "Nothing was put back.", InfoBarSeverity.Informational);
            return;
        }
        ViewModel.ReloadFromDisk();
        Say("Put back what the way back had switched off.", InfoBarSeverity.Success);
    }

    private void Say(string message, InfoBarSeverity severity)
    {
        MaintenanceResult.Message = message;
        MaintenanceResult.Severity = severity;
        MaintenanceResult.IsOpen = true;
    }
}
