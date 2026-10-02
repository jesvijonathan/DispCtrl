using System.Text.Json.Nodes;
using DispCtrl.App.ViewModels;
using DispCtrl.App.Views.Dialogs;
using DispCtrl.Control;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace DispCtrl.App.Views.Pages;

/// <summary>
/// The extras: Windows' sign-in and lock screen switches, and tools for when
/// Windows needs a nudge.
/// </summary>
/// <remarks>
/// The switches were at the end of Settings, which is about DispCtrl itself;
/// they are about Windows, and had nowhere else to go.
/// </remarks>
public sealed partial class MiscellaneousPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public MiscellaneousPage() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e) => _ = FillMachineAsync();

    private async void OnRefreshTaskbar(object sender, RoutedEventArgs e)
    {
        if (await ExplorerRestart.ConfirmAsync(XamlRoot)) await ViewModel.RestartExplorerAsync();
    }

    private async void OnRestoreNow(object sender, RoutedEventArgs e)
    {
        JsonObject result = await Task.Run(() => new ControlService().Execute(
            new JsonObject { ["version"] = 1, ["command"] = "restore.now", ["args"] = new JsonObject() }));
        ViewModel.ShowFooterStatus(result["ok"]?.GetValue<bool>() == true
            ? "Every display is back. Settings, Undo the way back, switches it all on again."
            : "Not put back: " + result["error"]?["message"]);
    }

    private void OnOpenWindowsDisplay(object sender, RoutedEventArgs e) =>
        _ = Launcher.LaunchUriAsync(new Uri("ms-settings:display"));

    private void OnOpenColourManagement(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("colorcpl.exe") { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { ViewModel.ShowFooterStatus("Colour management did not open: " + ex.Message); }
    }

    // ------------------------------------------------- company laptop switches --

    private bool _machineBusy;

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
}
