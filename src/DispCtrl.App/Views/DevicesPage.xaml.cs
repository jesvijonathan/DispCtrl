using System.Text.Json.Nodes;
using DispCtrl.Control;
using DispCtrl.Core.Devices;
using DispCtrl.Core.Displays;
using DispCtrl.Display;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace DispCtrl.App.Views;

/// <summary>
/// The device library: every monitor seen here, what each of its codes is, and
/// sharing what this PC has worked out.
/// </summary>
/// <remarks>
/// Nothing on this page has to be pressed for it to fill: the engine reads each
/// new model the first time it is plugged in (<c>DeviceDiscovery</c>), the page
/// lists what the history holds and refreshes when that file changes, and the
/// codes under each monitor come from the history in milliseconds. A live read
/// is only for watching codes move while somebody works out what one does.
/// <para>
/// Every action is a <c>devices.*</c> request to <see cref="ControlService"/>,
/// the same operations <c>dispctrl devices</c> runs, so the page and the command
/// line cannot disagree. Requests run off the UI thread.
/// </para>
/// </remarks>
public sealed partial class DevicesPage : Page
{
    private readonly ControlService _service = new();
    private CancellationTokenSource? _watch;
    private FileSystemWatcher? _history;
    private DispatcherQueueTimer? _refresh;

    public DevicesPage()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        WatchHistory();
        await RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopWatching();
        _history?.Dispose();
        _history = null;
        _refresh?.Stop();
    }

    /// <summary>Refreshes the list when the engine learns a monitor, a half second after the file settles.</summary>
    private void WatchHistory()
    {
        string folder = Path.GetDirectoryName(DeviceHistory.PathOnDisk)!;
        try
        {
            Directory.CreateDirectory(folder);
            _refresh = DispatcherQueue.CreateTimer();
            _refresh.Interval = TimeSpan.FromMilliseconds(500);
            _refresh.IsRepeating = false;
            _refresh.Tick += async (_, _) => { if (_watch is null) await RefreshAsync(); };
            _history = new FileSystemWatcher(folder, Path.GetFileName(DeviceHistory.PathOnDisk))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            // Saves are a rename over the file, so Renamed as well as Changed.
            _history.Changed += (_, _) => DispatcherQueue.TryEnqueue(() => { _refresh?.Stop(); _refresh?.Start(); });
            _history.Renamed += (_, _) => DispatcherQueue.TryEnqueue(() => { _refresh?.Stop(); _refresh?.Start(); });
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
    }

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
            {
                int read = data["scanned"]?.AsArray().Count(s => s?["ddc"]?.GetValue<bool>() == true) ?? 0;
                Show(read == 0 ? "Synced. No attached monitor answers DDC/CI, so there were no codes to read."
                    : $"Synced {read} monitor(s).", InfoBarSeverity.Success);
            }
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
        JsonNode? data = await RunAsync("devices.list");
        StopWatching();
        Models.Children.Clear();
        JsonArray models = data?["models"]?.AsArray() ?? [];
        if (models.Count == 0)
        {
            Models.Children.Add(Muted("No monitors recorded yet. DispCtrl's engine records each monitor when it starts and whenever one is plugged in; Sync now does it at once."));
            return;
        }
        foreach (JsonNode? m in models)
            if (m is not null) Models.Children.Add(ModelCard(m));
    }

    private FrameworkElement ModelCard(JsonNode m)
    {
        string model = m["model"]!.GetValue<string>();
        string name = m["name"]?.GetValue<string>() is { Length: > 0 } n ? n : model;
        bool attached = m["attached"]?.GetValue<bool>() == true;
        bool builtIn = m["builtIn"]?.GetValue<bool>() == true;
        bool read = m["capabilitiesRead"]?.GetValue<bool>() == true;
        int codes = m["codes"]?.GetValue<int>() ?? 0, unnamed = m["unnamed"]?.GetValue<int>() ?? 0, mapped = m["mapped"]?.GetValue<int>() ?? 0;

        var body = new StackPanel { Spacing = 6 };
        var head = new Grid { ColumnSpacing = 10 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        title.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(Muted(model));
        if (attached) title.Children.Add(Badge("Attached"));
        head.Children.Add(title);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Grid.SetColumn(buttons, 1);
        if (!builtIn && read)
        {
            var share = new Button { Content = "Share", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
            AutomationProperties.SetName(share, $"Share {model}");
            ToolTipService.SetToolTip(share, "Shows exactly what would be published - the model's record, your mappings, and what its unnamed codes were seen to do - then opens a GitHub issue for you to submit.");
            share.Click += async (_, _) => await ShareAsync(model);
            buttons.Children.Add(share);
        }
        var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 } };
        AutomationProperties.SetName(remove, $"Remove {model}");
        ToolTipService.SetToolTip(remove, attached
            ? "Remove from this list. It stays off until you press Sync now, even while attached."
            : "Remove from this list. It comes back if it is plugged in again.");
        remove.Click += async (_, _) =>
        {
            if (await RunAsync("devices.forget", new JsonObject { ["model"] = model }) is not null)
            {
                Show($"Removed {name}. Any codes you named for it are kept.", InfoBarSeverity.Informational);
                await RefreshAsync();
            }
        };
        buttons.Children.Add(remove);
        head.Children.Add(buttons);
        body.Children.Add(head);

        string seen = $"Seen {Date(m["firstSeen"])} to {Date(m["lastSeen"])}";
        body.Children.Add(Muted(builtIn
            ? $"{seen}. A built-in panel: it has no DDC/CI channel, so there are no codes to map."
            : !read && attached
                ? $"{seen}. Its codes are being read by the engine; they appear here by themselves."
                : !read
                    ? $"{seen}. Its codes were never read; they will be the next time it is attached."
                    : unnamed == 0
                        ? $"{seen}. {codes} codes, every one of them known ({mapped} from the library)."
                        : $"{seen}. {codes} codes: {unnamed} nobody has named yet, {mapped} from the library."));

        if (!builtIn && read && codes > 0)
        {
            var list = new StackPanel { Spacing = 2 };
            var expander = new Expander
            {
                Header = unnamed > 0 ? $"Codes ({codes}, {unnamed} to name)" : $"Codes ({codes})",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = list,
            };
            AutomationProperties.SetName(expander, $"Codes {model}");
            bool filled = false;
            expander.Expanding += async (_, _) =>
            {
                if (filled) return;
                filled = true;
                await FillCodesAsync(list, model, attached);
            };
            expander.Collapsed += (_, _) => StopWatching();
            body.Children.Add(expander);
        }

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

    // ================================================================ the codes

    private sealed record Row(string Code, TextBlock Value, TextBlock Change, Border Host);

    /// <summary>Every code the model listed, from the history; a live watch is one switch away.</summary>
    private async Task FillCodesAsync(StackPanel host, string model, bool attached)
    {
        host.Children.Clear();
        JsonNode? data = await RunAsync("devices.show", new JsonObject { ["model"] = model, ["history"] = true });
        if (data is null) return;

        var watch = new ToggleSwitch
        {
            Header = "Watch the unnamed codes live",
            OnContent = "Watching - change one setting at a time in the monitor's own menu",
            OffContent = attached ? "Off" : "Attach the monitor to watch it",
            IsEnabled = attached,
        };
        AutomationProperties.SetName(watch, $"Watch {model}");
        host.Children.Add(watch);

        var header = RowGrid();
        AddCells(header, Muted("Code"), Muted("What it is"), Muted("Values"), new TextBlock());
        host.Children.Add(header);

        var rows = new List<Row>();
        var entries = (data["codes"]?.AsArray() ?? []).Where(c => c is not null)
            .OrderBy(c => c!["status"]!.GetValue<string>() switch { "unnamed" => 0, "mapped" => 1, "named" => 2, _ => 3 })
            .ThenBy(c => c!["code"]!.GetValue<string>(), StringComparer.Ordinal);
        foreach (JsonNode? c in entries)
        {
            string code = c!["code"]!.GetValue<string>();
            string status = c["status"]!.GetValue<string>();
            string name = c["name"]?.GetValue<string>() ?? c["reported"]?.GetValue<string>() ?? code;

            var what = new StackPanel();
            what.Children.Add(new TextBlock { Text = status == "unnamed" ? "Not named yet" : name, TextTrimming = TextTrimming.CharacterEllipsis });
            what.Children.Add(Muted(status switch
            {
                "standard" => "Standard; DispCtrl controls it",
                "named" => "Named by the standard; read-only here",
                "mapped" => $"From the {Origin(c["origin"]?.GetValue<string>())}{(c["writable"]?.GetValue<bool>() == true ? ", writable" : ", read-only")}",
                _ => Shape(c),
            }));

            var value = new TextBlock { Text = Values(c), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var change = Muted("");
            var valueStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            valueStack.Children.Add(value);
            valueStack.Children.Add(change);

            FrameworkElement action = new TextBlock();
            if (status is "unnamed" or "mapped")
            {
                var nameIt = new Button { Content = status == "mapped" ? "Edit" : "Name it" };
                AutomationProperties.SetName(nameIt, $"Name {code}");
                nameIt.Click += async (_, _) =>
                {
                    if (await NameAsync(model, code, c)) await FillCodesAsync(host, model, attached);
                };
                action = nameIt;
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

    private static string Origin(string? origin) => origin switch
    {
        null => "library",
        _ when origin.StartsWith("local", StringComparison.Ordinal) => "mappings made on this PC",
        _ when origin.EndsWith(" *", StringComparison.Ordinal) => "library, for every monitor",
        _ when origin.Length == "shipped DEL".Length => "library, for the brand",
        _ => "library",
    };

    /// <summary>What an unnamed code looks like, from what the monitor said and what it was seen to do.</summary>
    /// <remarks>
    /// The evidence a person names a code from: a code the monitor lists with
    /// values is a choice among them; one without, a range - and the values it
    /// has been read at say how wide.
    /// </remarks>
    private static string Shape(JsonNode c)
    {
        int[] listed = Ints(c["listed"]);
        int[] seen = Ints(c["observed"]);
        if (listed.Length > 0) return $"A choice of {listed.Length} value(s) the monitor lists";
        if (seen.Length > 1) return $"A range, seen from {seen.Min()} to {seen.Max()}";
        return c["reported"]?.GetValue<string>() is { Length: > 0 } r ? r : "Listed by the monitor, never read";
    }

    private static string Values(JsonNode c)
    {
        if (c["values"]?.AsArray() is { Count: > 0 } named)
            return string.Join(", ", named.Take(6).Select(v => $"{v!["name"]}")) + (named.Count > 6 ? ", ..." : "");
        int[] listed = Ints(c["listed"]);
        int[] seen = Ints(c["observed"]);
        string Hex(int v) => $"0x{v:X2}";
        if (listed.Length > 0)
        {
            string list = string.Join(", ", listed.Take(8).Select(Hex)) + (listed.Length > 8 ? ", ..." : "");
            // A value read that the monitor never listed is worth seeing: it is
            // how an advertised-but-unimplemented control gives itself away.
            int[] stray = seen.Where(v => !listed.Contains(v & 0xFF)).ToArray();
            return stray.Length == 0 ? list : $"{list}; also read {string.Join(", ", stray.Take(3).Select(Hex))}";
        }
        return seen.Length == 0 ? "-" : $"seen {string.Join(", ", seen.Take(6))}{(seen.Length > 6 ? ", ..." : "")}";
    }

    private static int[] Ints(JsonNode? array) =>
        array?.AsArray().Where(v => v is not null).Select(v => v!.GetValue<int>()).ToArray() ?? [];

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
                        row.Value.Text = $"now {now} (0x{now & 0xFF:X2})";
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
    /// <remarks>
    /// The form starts from what is known: a code the monitor lists values for
    /// starts as a choice with those values, named by their hex, so naming it
    /// is editing rather than typing out every value.
    /// </remarks>
    private async Task<bool> NameAsync(string model, string code, JsonNode entry)
    {
        int[] listed = Ints(entry["listed"]);
        string[] kinds = ["choice", "range", "action", "information"];
        string startKind = entry["mappedKind"]?.GetValue<string>() ?? (listed.Length > 0 ? "choice" : Ints(entry["observed"]).Length > 1 ? "range" : "information");
        string startValues = entry["values"]?.AsArray() is { Count: > 0 } named
            ? string.Join(", ", named.Select(v => $"{v!["value"]}={v["name"]}"))
            : string.Join(", ", listed.Select(v => $"0x{v:X2}=Value {v:X2}"));

        var name = new TextBox { Header = "What it does", PlaceholderText = "Preset mode", Text = entry["name"]?.GetValue<string>() ?? "" };
        var kind = new ComboBox { Header = "Kind", ItemsSource = kinds, HorizontalAlignment = HorizontalAlignment.Stretch };
        kind.SelectedIndex = Math.Max(0, Array.IndexOf(kinds, startKind));
        var values = new TextBox
        {
            Header = "Values it takes (for a choice)",
            PlaceholderText = "0x00=Standard, 0x0B=ComfortView",
            Text = startValues,
            TextWrapping = TextWrapping.Wrap,
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
        form.Children.Add(Muted($"{code} on {model}. Saved on this PC and used at once; share it to make it everyone's."));
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
                string chosen = kind.SelectedItem as string ?? "information";
                var request = new JsonObject
                {
                    ["model"] = model, ["code"] = code, ["name"] = name.Text.Trim(),
                    ["kind"] = chosen,
                    ["scope"] = scope.SelectedIndex switch { 1 => "brand", 2 => "all", _ => "model" },
                    ["writable"] = writable.IsChecked == true,
                };
                // Values only mean something for a choice; sent for another kind
                // they would be refused as a contradiction.
                if (chosen == "choice" && values.Text.Trim().Length > 0) request["values"] = values.Text.Trim();
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
    /// Nothing leaves the PC from here. The link carries as much as fits - always
    /// the mappings and codes, which are what the project needs - and whatever
    /// does not fit, usually the model's record, goes to the clipboard with a
    /// line in the issue saying where to paste it. It used to open an empty form
    /// with no explanation, which looked like the button doing nothing.
    /// </remarks>
    private async Task ShareAsync(string model)
    {
        JsonNode? data = await RunAsync("devices.share", new JsonObject { ["model"] = model });
        if (data is null) return;
        string body = data["body"]!.GetValue<string>();
        string? paste = data["paste"]?.GetValue<string>();
        string url = data["url"]!.GetValue<string>();

        var text = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"), FontSize = 12, Height = 420, MinWidth = 560, Text = body,
        };
        var dialog = new ContentDialog
        {
            Title = $"Share {model}",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Muted("This is everything that would be published: no serial, no path, no current setting. It opens as a GitHub issue in your browser; nothing is sent until you press Submit there."),
                    text,
                    Muted(paste is null ? "All of it fits in the link." : "Too long for one link: the issue opens with your mappings filled in, and the rest is copied - paste it where the issue says."),
                },
            },
            PrimaryButtonText = "Open GitHub issue",
            SecondaryButtonText = "Copy all",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        ContentDialogResult choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.Secondary)
        {
            Copy(body);
            Show("Copied the whole share.", InfoBarSeverity.Success);
            return;
        }
        if (choice != ContentDialogResult.Primary) return;

        if (paste is not null) Copy(paste);
        bool opened = await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        Show(!opened ? $"Could not open the browser. The share is saved at {data["path"]}."
            : paste is null ? "Opened the issue with everything filled in. Press Submit there."
            : "Opened the issue with your mappings filled in; the rest is on the clipboard - paste it where the issue says, then Submit.",
            opened ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    private static void Copy(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    // ================================================================ bits

    private static Grid RowGrid()
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
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
