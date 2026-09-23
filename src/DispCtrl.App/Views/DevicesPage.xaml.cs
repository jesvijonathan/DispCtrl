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
    private bool _sharing;
    private bool _loaded;

    public DevicesPage()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        WatchHistory();
        await RefreshAsync();
        if (!App.ViewModel.EngineRunning)
        {
            // A stopped engine should not turn automatic discovery into a
            // manual Sync requirement while the Devices page is open.
            await Task.Run(() =>
            {
                foreach (DisplayInfo display in DispCtrl.Display.Devices.DeviceDiscovery.Unread(DisplayRegistry.Enumerate()))
                    try { DispCtrl.Display.Devices.DeviceDiscovery.Learn(display); } catch (Exception) { }
            });
            if (_loaded) await RefreshAsync();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _loaded = false;
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
            _refresh.Tick += async (_, _) => { if (_loaded && _watch is null && !_sharing) await RefreshAsync(); };
            _history = new FileSystemWatcher(folder, Path.GetFileName(DeviceHistory.PathOnDisk))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            // Saves are a rename over the file, so Renamed as well as Changed.
            _history.Changed += (_, _) => DispatcherQueue.TryEnqueue(() => { _refresh?.Stop(); _refresh?.Start(); });
            _history.Renamed += (_, _) => DispatcherQueue.TryEnqueue(() => { _refresh?.Stop(); _refresh?.Start(); });
            _history.Created += (_, _) => DispatcherQueue.TryEnqueue(() => { _refresh?.Stop(); _refresh?.Start(); });
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
        if (!_loaded || _sharing) return;
        StopWatching();
        Models.Children.Clear();
        JsonArray models = data?["models"]?.AsArray() ?? [];
        ShareAllButton.IsEnabled = models.Count > 0;
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
        var title = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap });
        var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var modelLabel = Muted($"Model: {model}");
        modelLabel.VerticalAlignment = VerticalAlignment.Center;
        identity.Children.Add(modelLabel);
        identity.Children.Add(Badge(attached ? "Attached" : "Disconnected"));
        title.Children.Add(identity);
        head.Children.Add(title);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(buttons, 1);
        if (DeviceDefinitions.IsModel(model))
        {
            var share = new Button { Content = "Share", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
            AutomationProperties.SetName(share, $"Share {model}");
            ToolTipService.SetToolTip(share, "Shows exactly what would be published - the model's record, your mappings, and what its unnamed codes were seen to do - then opens a GitHub issue for you to submit.");
            share.Click += async (_, _) => await ShareAsync(model);
            buttons.Children.Add(share);
        }
        var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 } };
        AutomationProperties.SetName(remove, $"Remove {model}");
        ToolTipService.SetToolTip(remove, "Remove from this list. It stays off until you press Sync now, including after reconnecting.");
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
        string startKind = entry["mappedKind"]?.GetValue<string>() ?? (listed.Length > 0 ? "choice" : "information");
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
        var maximum = new NumberBox { Header = "Maximum (optional, for a range)", Minimum = 0, Maximum = 65535,
            Value = entry["maximum"]?.GetValue<int>() ?? double.NaN };
        var notes = new TextBox { Header = "Notes", PlaceholderText = "Moves when the OSD's Preset Modes item changes.", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 72,
            Text = entry["notes"]?.GetValue<string>() ?? "" };
        var error = new TextBlock { Foreground = Brush("SystemFillColorCriticalBrush"), TextWrapping = TextWrapping.Wrap };

        var form = new StackPanel { Spacing = 10, MinWidth = 420 };
        form.Children.Add(Muted($"{code} on {model}. Saved on this PC and used at once; share it to make it everyone's."));
        foreach (UIElement e in new UIElement[] { name, kind, values, maximum, scope, writable, notes, error }) form.Children.Add(e);
        void UpdateKind()
        {
            values.Visibility = kind.SelectedItem as string == "choice" ? Visibility.Visible : Visibility.Collapsed;
            maximum.Visibility = kind.SelectedItem as string == "range" ? Visibility.Visible : Visibility.Collapsed;
        }
        kind.SelectionChanged += (_, _) => UpdateKind();
        UpdateKind();

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
                if (chosen == "range" && !double.IsNaN(maximum.Value))
                {
                    if (maximum.Value != Math.Truncate(maximum.Value)) throw new ArgumentException("Maximum must be a whole number.");
                    request["maximum"] = (int)maximum.Value;
                }
                request["notes"] = notes.Text.Trim();
                JsonObject result = await Task.Run(() => _service.Execute(new JsonObject { ["version"] = 1, ["command"] = "devices.map", ["args"] = request }));
                if (result["ok"]?.GetValue<bool>() == true) saved = true;
                else
                {
                    error.Text = result["error"]?["message"]?.GetValue<string>() ?? "Not saved.";
                    args.Cancel = true;
                }
            }
            catch (Exception ex) { error.Text = ex.Message; args.Cancel = true; }
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
    private async void OnShareAll(object sender, RoutedEventArgs e) => await ShareAsync(null);

    private async Task ShareAsync(string? model)
    {
        if (_sharing) return;
        _sharing = true;
        StopWatching();
        ShareAllButton.IsEnabled = ScanButton.IsEnabled = false;
        ContentDialog? dialog = null;
        try
        {
            var progress = new ProgressRing { IsActive = true, Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Left };
            var notice = Muted("Preparing the device details. Reading a monitor can take a few seconds; you can cancel while it finishes.");
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(progress);
            content.Children.Add(notice);
            dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = model is null ? "Share all devices" : $"Share {model}", Content = content,
                PrimaryButtonText = "Open GitHub issue", SecondaryButtonText = "Copy all", CloseButtonText = "Cancel",
                IsPrimaryButtonEnabled = false, IsSecondaryButtonEnabled = false, DefaultButton = ContentDialogButton.None,
            };
            bool closed = false;
            dialog.Closed += (_, _) => closed = true;
            var shown = dialog.ShowAsync();
            JsonNode? data = await RunAsync("devices.share", model is null
                ? new JsonObject { ["all"] = true } : new JsonObject { ["model"] = model });
            if (closed || !_loaded) return;
            progress.IsActive = false;
            progress.Visibility = Visibility.Collapsed;
            if (data is null) { notice.Text = "Could not prepare the share. Close this window to see the error and try again."; await shown; return; }
            string body = data["body"]!.GetValue<string>();
            string? paste = data["paste"]?.GetValue<string>();
            content.Children.Insert(0, Muted("Review the model records and mappings below. Monitor serials and user paths are removed. Nothing is submitted until you press Submit on GitHub."));
            content.Children.Insert(1, new TextBox
            {
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"), FontSize = 12, Height = 320, Text = body,
            });
            notice.Text = paste is null ? $"All {body.Length:N0} characters fit in the issue link."
                : $"The complete share is {body.Length:N0} characters. Opening GitHub copies the text that needs pasting; replace the placeholder in the issue with it.";
            dialog.IsPrimaryButtonEnabled = dialog.IsSecondaryButtonEnabled = true;
            dialog.SecondaryButtonClick += (_, args) =>
            {
                args.Cancel = true;
                try { Copy(body); notice.Text = "Copied the complete share."; }
                catch (Exception ex) { notice.Text = "Could not copy: " + ex.Message; }
            };
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    if (paste is not null) Copy(paste);
                    if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(data["url"]!.GetValue<string>())))
                        throw new InvalidOperationException("Windows could not open the browser. Copy the share and try again.");
                    Show(paste is null ? "Opened the prefilled issue. Review it and press Submit there."
                        : "Opened the issue. Paste the copied text over its placeholder, then press Submit there.", InfoBarSeverity.Success);
                }
                catch (Exception ex) { args.Cancel = true; notice.Text = ex.Message; }
                finally { deferral.Complete(); }
            };
            await shown;
        }
        catch (Exception ex) { dialog?.Hide(); Show("Could not share: " + ex.Message, InfoBarSeverity.Error); }
        finally
        {
            _sharing = false;
            ShareAllButton.IsEnabled = ScanButton.IsEnabled = true;
            if (_loaded) await RefreshAsync();
        }
    }

    private static void Copy(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
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
