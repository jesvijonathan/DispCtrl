using System.Text.Json.Nodes;
using DispCtrl.Control;
using DispCtrl.Core.Devices;
using DispCtrl.Core.Displays;
using DispCtrl.Display;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace DispCtrl.App.Views;

/// <summary>
/// The device library: every monitor seen here, naming the codes nobody has,
/// and sharing the result - one page where there used to be collect, view,
/// submit.
/// </summary>
/// <remarks>
/// Every action is a <c>devices.*</c> request to <see cref="ControlService"/>,
/// the same operations <c>dispctrl devices</c> runs, so the page and the command
/// line cannot disagree about what a mapping is. Requests run off the UI thread:
/// a capabilities read is seconds of DDC/CI.
/// </remarks>
public sealed partial class DevicesPage : Page
{
    private readonly ControlService _service = new();
    private CancellationTokenSource? _watch;

    public DevicesPage()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void OnUnloaded(object sender, RoutedEventArgs e) => StopWatching();

    private async Task<JsonNode?> RunAsync(string command, JsonObject? args = null)
    {
        JsonObject result = await Task.Run(() => _service.Execute(new JsonObject
        {
            ["version"] = 1, ["command"] = command, ["args"] = args ?? new JsonObject(),
        }));
        if (result["ok"]?.GetValue<bool>() == true) return result["data"];
        Show(result["error"]?["message"]?.GetValue<string>() ?? "The request failed.", InfoBarSeverity.Error);
        return null;
    }

    private void Show(string message, InfoBarSeverity severity)
    {
        Result.Message = message;
        Result.Severity = severity;
        Result.IsOpen = true;
    }

    private async void OnScan(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        try
        {
            JsonNode? data = await RunAsync("devices.scan");
            if (data is not null)
                Show("Read every attached monitor into this PC's history.", InfoBarSeverity.Success);
            await RefreshAsync();
        }
        finally { ScanButton.IsEnabled = true; }
    }

    private async void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        string folder = Path.GetDirectoryName(DeviceHistory.PathOnDisk)!;
        Directory.CreateDirectory(folder);
        _ = await Windows.System.Launcher.LaunchFolderPathAsync(folder);
    }

    // ================================================================ the list

    private async Task RefreshAsync()
    {
        StopWatching();
        JsonNode? data = await RunAsync("devices.list");
        Models.Children.Clear();
        JsonArray models = data?["models"]?.AsArray() ?? [];
        if (models.Count == 0)
        {
            Models.Children.Add(Muted("No monitors recorded yet. Read the attached monitors to start the history."));
            return;
        }
        foreach (JsonNode? m in models)
            if (m is not null) Models.Children.Add(ModelCard(m));
    }

    private FrameworkElement ModelCard(JsonNode m)
    {
        string model = m["model"]!.GetValue<string>();
        string name = m["name"]?.GetValue<string>() ?? model;
        bool attached = m["attached"]?.GetValue<bool>() == true;
        bool builtIn = m["builtIn"]?.GetValue<bool>() == true;
        int codes = m["codes"]?.GetValue<int>() ?? 0, unnamed = m["unnamed"]?.GetValue<int>() ?? 0, mapped = m["mapped"]?.GetValue<int>() ?? 0;

        var body = new StackPanel { Spacing = 6 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        head.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 15 });
        head.Children.Add(Muted(model));
        if (attached) head.Children.Add(Badge("Attached"));
        body.Children.Add(head);

        string seen = $"Seen {Date(m["firstSeen"])} to {Date(m["lastSeen"])}";
        body.Children.Add(Muted(builtIn
            ? $"{seen}. A built-in panel: it has no DDC/CI channel, so there are no codes to map."
            : codes == 0
                ? $"{seen}. Its codes have not been read yet."
                : $"{seen}. {codes} codes: {unnamed} nobody has named, {mapped} with a definition."));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        var mapping = new StackPanel { Spacing = 2, Visibility = Visibility.Collapsed };

        if (attached && !builtIn)
        {
            var map = new Button { Content = "Map codes" };
            AutomationProperties.SetName(map, $"Map {model}");
            ToolTipService.SetToolTip(map, "List every code with its value, watch which one moves while you change a setting in the monitor's own menu, and name it.");
            map.Click += async (_, _) =>
            {
                if (mapping.Visibility == Visibility.Visible)
                {
                    StopWatching();
                    mapping.Visibility = Visibility.Collapsed;
                    return;
                }
                mapping.Visibility = Visibility.Visible;
                await FillMappingAsync(mapping, model);
            };
            buttons.Children.Add(map);
        }

        if (!builtIn)
        {
            var share = new Button { Content = "Share with the project", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
            AutomationProperties.SetName(share, $"Share {model}");
            ToolTipService.SetToolTip(share, "Shows exactly what would be published - the model's record, your mappings, and what its unnamed codes were seen to do - then opens a GitHub issue for you to submit.");
            share.Click += async (_, _) => await ShareAsync(model);
            buttons.Children.Add(share);
        }

        body.Children.Add(buttons);
        body.Children.Add(mapping);

        return new Border
        {
            Child = body,
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = Brush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = Brush("CardStrokeColorDefaultBrush"),
        };
    }

    // ================================================================ mapping

    private sealed record Row(string Code, TextBlock Value, TextBlock Change, Border Host);

    private async Task FillMappingAsync(StackPanel host, string model)
    {
        host.Children.Clear();
        host.Children.Add(Muted("Reading every code from the monitor - a few seconds..."));
        JsonNode? data = await RunAsync("devices.show", new JsonObject { ["model"] = model });
        host.Children.Clear();
        if (data is null) return;

        var watch = new ToggleSwitch { Header = "Watch the unnamed codes", OnContent = "Watching - change one setting at a time in the monitor's menu", OffContent = "Off" };
        AutomationProperties.SetName(watch, $"Watch {model}");
        host.Children.Add(watch);

        var header = RowGrid();
        AddCells(header, Muted("Code"), Muted("What it is"), Muted("Value"), new TextBlock());
        host.Children.Add(header);

        var rows = new List<Row>();
        foreach (JsonNode? c in data["codes"]?.AsArray() ?? [])
        {
            if (c is null) continue;
            string code = c["code"]!.GetValue<string>();
            string status = c["status"]!.GetValue<string>();
            string name = c["name"]?.GetValue<string>() ?? c["reported"]?.GetValue<string>() ?? code;

            var what = new StackPanel();
            what.Children.Add(new TextBlock { Text = status == "unnamed" ? "Not named yet" : name, TextTrimming = TextTrimming.CharacterEllipsis });
            what.Children.Add(Muted(status switch
            {
                "standard" => "Standard; DispCtrl controls it",
                "named" => "Named by the standard; read-only here",
                "mapped" => $"Mapped ({c["origin"]?.GetValue<string>()}){(c["writable"]?.GetValue<bool>() == true ? ", writable" : "")}",
                _ => c["reported"]?.GetValue<string>() ?? "",
            }));

            var value = new TextBlock { Text = c["current"]?.ToString() ?? "-", VerticalAlignment = VerticalAlignment.Center };
            var change = Muted("");
            var valueStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            valueStack.Children.Add(value);
            valueStack.Children.Add(change);

            FrameworkElement action = new TextBlock();
            if (status is "unnamed" or "mapped")
            {
                var name_it = new Button { Content = status == "mapped" ? "Edit" : "Name it" };
                AutomationProperties.SetName(name_it, $"Name {code}");
                name_it.Click += async (_, _) =>
                {
                    if (await NameAsync(model, code, c)) await FillMappingAsync(host, model);
                };
                action = name_it;
            }

            var grid = RowGrid();
            AddCells(grid, new TextBlock { Text = code, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center },
                what, valueStack, action);
            var rowHost = new Border { Child = grid, CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 2, 4, 2) };
            host.Children.Add(rowHost);
            if (status == "unnamed") rows.Add(new Row(code, value, change, rowHost));
        }

        watch.Toggled += (_, _) =>
        {
            if (watch.IsOn) StartWatching(model, rows);
            else StopWatching();
        };
    }

    /// <summary>
    /// Reads the unnamed codes again and again, and lights up whichever moved.
    /// </summary>
    /// <remarks>
    /// Straight to <see cref="MonitorCapabilities.ReadValues"/>, which bypasses
    /// the cache, on a background thread; the channel's own named mutex keeps
    /// this from colliding with the engine or the command line.
    /// </remarks>
    private void StartWatching(string model, List<Row> rows)
    {
        StopWatching();
        var cancel = _watch = new CancellationTokenSource();
        DisplayInfo? display = DisplayRegistry.Enumerate().FirstOrDefault(d => d.Key.Model == model);
        if (display is null || rows.Count == 0) return;

        _ = Task.Run(async () =>
        {
            MonitorCapability capabilities = MonitorCapabilities.Read(display, readValues: false);
            var codes = rows.Select(r => DeviceDefinitions.ParseCode(r.Code)).ToHashSet();
            List<VcpControl> wanted = capabilities.Controls.Where(c => codes.Contains(c.Code)).ToList();
            var last = new Dictionary<byte, int>();
            while (!cancel.IsCancellationRequested)
            {
                MonitorCapabilities.ReadValues(display, wanted);
                foreach (VcpControl c in wanted)
                {
                    if (c.Current < 0) continue;
                    bool had = last.TryGetValue(c.Code, out int before);
                    last[c.Code] = c.Current;
                    int now = c.Current;
                    Row? row = rows.FirstOrDefault(r => DeviceDefinitions.ParseCode(r.Code) == c.Code);
                    if (row is null) continue;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        row.Value.Text = $"{now} (0x{now & 0xFF:X2})";
                        if (had && before != now)
                        {
                            row.Change.Text = $"changed from {before} at {DateTime.Now:HH:mm:ss}";
                            row.Host.Background = Brush("AccentFillColorTertiaryBrush");
                        }
                    });
                }
                try { await Task.Delay(400, cancel.Token); } catch (OperationCanceledException) { break; }
            }
        }, cancel.Token);
    }

    private void StopWatching()
    {
        _watch?.Cancel();
        _watch = null;
    }

    /// <summary>Asks what a code is, and saves the answer as a local definition.</summary>
    private async Task<bool> NameAsync(string model, string code, JsonNode entry)
    {
        var name = new TextBox { Header = "What it does", PlaceholderText = "Preset mode", Text = entry["name"]?.GetValue<string>() ?? "" };
        var kind = new ComboBox { Header = "Kind", ItemsSource = new[] { "choice", "range", "action", "information" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        kind.SelectedIndex = Array.IndexOf(new[] { "choice", "range", "action", "information" }, entry["mappedKind"]?.GetValue<string>() ?? "information");
        var values = new TextBox
        {
            Header = "Values it takes (for a choice)",
            PlaceholderText = "0x00=Standard, 0x0B=ComfortView",
            Text = string.Join(", ", (entry["values"]?.AsArray() ?? []).Select(v => $"{v!["value"]}={v["name"]}")),
        };
        string brand = DeviceDefinitions.Brand(model);
        var scope = new ComboBox
        {
            Header = "Applies to",
            ItemsSource = new[] { $"This model ({model})", $"Every {brand} monitor that lists it", "Every monitor that lists it" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var writable = new CheckBox { Content = "I have written it and seen what the monitor does - let DispCtrl write it", IsChecked = entry["writable"]?.GetValue<bool>() == true };
        var notes = new TextBox { Header = "Notes", PlaceholderText = "Moves when the OSD's Preset Modes item changes.", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 72 };
        var error = new TextBlock { Foreground = Brush("SystemFillColorCriticalBrush"), TextWrapping = TextWrapping.Wrap };

        var form = new StackPanel { Spacing = 10, MinWidth = 420 };
        form.Children.Add(Muted($"{code} on {model}. Saved on this PC; share it to make it everyone's."));
        foreach (UIElement e in new UIElement[] { name, kind, values, scope, writable, notes, error }) form.Children.Add(e);

        var dialog = new ContentDialog
        {
            Title = $"Name {code}",
            Content = new ScrollViewer { Content = form },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        bool saved = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                var request = new JsonObject
                {
                    ["model"] = model, ["code"] = code, ["name"] = name.Text.Trim(),
                    ["kind"] = kind.SelectedItem as string ?? "information",
                    ["scope"] = scope.SelectedIndex switch { 1 => "brand", 2 => "all", _ => "model" },
                    ["writable"] = writable.IsChecked == true,
                };
                if (values.Text.Trim().Length > 0) request["values"] = values.Text.Trim();
                if (notes.Text.Trim().Length > 0) request["notes"] = notes.Text.Trim();
                JsonObject result = await Task.Run(() => _service.Execute(new JsonObject { ["version"] = 1, ["command"] = "devices.map", ["args"] = request }));
                if (result["ok"]?.GetValue<bool>() == true) saved = true;
                else
                {
                    error.Text = result["error"]?["message"]?.GetValue<string>() ?? "Not saved.";
                    args.Cancel = true;
                }
            }
            finally { deferral.Complete(); }
        };
        await dialog.ShowAsync();
        if (saved) Show($"{code} saved for {model}. Share it when you are happy with it.", InfoBarSeverity.Success);
        return saved;
    }

    // ================================================================ sharing

    /// <summary>
    /// Shows the exact text, then opens the issue the person submits themselves.
    /// </summary>
    /// <remarks>
    /// Nothing leaves the PC from here: the browser opens with the body filled
    /// in, or - when it is too long for a link - with an empty form and the body
    /// on the clipboard. There is no token and no request from the app.
    /// </remarks>
    private async Task ShareAsync(string model)
    {
        JsonNode? data = await RunAsync("devices.share", new JsonObject { ["model"] = model });
        if (data is null) return;
        string body = data["body"]!.GetValue<string>();
        bool prefilled = data["prefilled"]?.GetValue<bool>() == true;
        string url = data["url"]!.GetValue<string>();

        var text = new TextBox
        {
            Text = body, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"), FontSize = 12, Height = 420, MinWidth = 560,
        };
        var dialog = new ContentDialog
        {
            Title = $"Share {model}",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Muted("This is everything that would be published: no serial, no path, no current setting. It opens as a GitHub issue; nothing is sent until you press Submit there."),
                    text,
                    Muted(prefilled ? "" : "Too long to fit in a link: the form opens empty and this text is copied, ready to paste."),
                },
            },
            PrimaryButtonText = "Open GitHub issue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        if (!prefilled)
        {
            var package = new DataPackage();
            package.SetText(body);
            Clipboard.SetContent(package);
        }
        _ = await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        Show(prefilled ? "Opened the issue with everything filled in." : "Opened the issue form; the text is on the clipboard.", InfoBarSeverity.Success);
    }

    // ================================================================ bits

    private static Grid RowGrid()
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        return g;
    }

    private static void AddCells(Grid grid, params FrameworkElement[] cells)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }
    }

    private static TextBlock Muted(string text) => new()
    {
        Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap,
        Foreground = Brush("TextFillColorSecondaryBrush"),
    };

    private static Border Badge(string text) => new()
    {
        Child = new TextBlock { Text = text, FontSize = 11 },
        BorderBrush = Brush("CardStrokeColorDefaultBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(6, 1, 6, 2),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static string Date(JsonNode? iso) =>
        DateTimeOffset.TryParse(iso?.GetValue<string>(), out var d) ? d.LocalDateTime.ToString("d MMM yyyy") : "-";

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
