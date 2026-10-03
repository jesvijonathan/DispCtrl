using System.Text.Json.Nodes;
using DispCtrl.App.Views.Dialogs;
using DispCtrl.Control;
using DispCtrl.Core.Devices;
using DispCtrl.Core.Displays;
using DispCtrl.Display.Ddc;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace DispCtrl.App.Views.Pages;

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
    private bool _editing, _scanning;
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
            _refresh.Tick += async (_, _) => { if (_loaded && _watch is null && !_sharing && !_editing && !_scanning) await RefreshAsync(); };
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
        if (_scanning) return;
        _scanning = true;
        ShareAllButton.IsEnabled = ScanButton.IsEnabled = false;
        StopWatching();
        try
        {
            JsonNode? data = await RunAsync("devices.scan");
            if (data is not null)
            {
                int read = data["scanned"]?.AsArray().Count(s => s?["ddc"]?.GetValue<bool>() == true) ?? 0;
                int added = data["scanned"]?.AsArray().Sum(s => s?["newCodes"]?.AsArray().Count ?? 0) ?? 0;
                Show(read == 0 ? "Scan complete. No attached monitor answers DDC/CI, so there were no codes to read."
                    : $"Scanned {read} monitor(s), found {added} new code(s). Discoveries are ready for Contribute.", InfoBarSeverity.Success);
            }
            await RefreshAsync();
        }
        finally { _scanning = false; ScanButton.IsEnabled = true; ShareAllButton.IsEnabled = _models.Count > 0; }
    }

    private async void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        string folder = Path.GetDirectoryName(DeviceHistory.PathOnDisk)!;
        Directory.CreateDirectory(folder);
        _ = await Windows.System.Launcher.LaunchFolderPathAsync(folder);
    }

    // ================================================================ the monitor

    private JsonArray _models = [];
    private string? _selected;
    private string _filter = "";
    private bool _picking;

    private async Task RefreshAsync()
    {
        JsonNode? data = await RunAsync("devices.list");
        if (!_loaded || _sharing || _editing) return;
        _models = data?["models"]?.AsArray() ?? [];
        ShareAllButton.IsEnabled = _models.Count > 0;
        _picking = true;
        try
        {
            MonitorPicker.ItemsSource = _models.Select(m => PickerLabel(m!)).ToList();
            MonitorPicker.IsEnabled = _models.Count > 0;
            // The one last looked at; otherwise an attached monitor with codes
            // left to name, which is what someone opening the page came for.
            int index = _models.ToList().FindIndex(m => m?["model"]?.GetValue<string>() == _selected);
            if (index < 0) index = _models.ToList().FindIndex(m => m?["attached"]?.GetValue<bool>() == true && (m["unnamed"]?.GetValue<int>() ?? 0) > 0);
            if (index < 0) index = _models.ToList().FindIndex(m => m?["attached"]?.GetValue<bool>() == true && m["builtIn"]?.GetValue<bool>() != true);
            if (index < 0 && _models.Count > 0) index = 0;
            MonitorPicker.SelectedIndex = index;
        }
        finally { _picking = false; }
        if (_models.Count == 0)
        {
            StopWatching();
            Detail.Children.Clear();
            Detail.Children.Add(Muted("No monitors recorded yet. Choose Scan to read the attached monitors and their controls."));
            return;
        }
        await ShowModelAsync(_models[MonitorPicker.SelectedIndex]!);
    }

    private static string PickerLabel(JsonNode m)
    {
        string name = m["name"]?.GetValue<string>() is { Length: > 0 } n ? n : m["model"]!.GetValue<string>();
        int unnamed = m["unnamed"]?.GetValue<int>() ?? 0;
        string state = m["attached"]?.GetValue<bool>() == true ? "attached" : "not attached";
        return m["builtIn"]?.GetValue<bool>() == true ? $"{name}  ·  built-in, {state}"
            : unnamed > 0 ? $"{name}  ·  {state}, {unnamed} to name" : $"{name}  ·  {state}";
    }

    private async void OnMonitorChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_picking || MonitorPicker.SelectedIndex < 0 || MonitorPicker.SelectedIndex >= _models.Count) return;
        _filter = "";
        await ShowModelAsync(_models[MonitorPicker.SelectedIndex]!);
    }

    /// <summary>Everything about one model: what it is, the quick ways to name its codes, and the codes.</summary>
    private async Task ShowModelAsync(JsonNode m)
    {
        StopWatching();
        string model = m["model"]!.GetValue<string>();
        _selected = model;
        bool builtIn = m["builtIn"]?.GetValue<bool>() == true;
        bool attached = m["attached"]?.GetValue<bool>() == true;
        Detail.Children.Clear();
        Detail.Children.Add(Summary(m));
        if (builtIn)
        {
            Detail.Children.Add(await PanelCardAsync(model));
            return;
        }
        if (m["capabilitiesRead"]?.GetValue<bool>() != true) return;

        JsonNode? data = await RunAsync("devices.show", new JsonObject { ["model"] = model, ["history"] = true });
        if (data is null || _selected != model) return;
        JsonArray codes = data["codes"]?.AsArray() ?? [];
        int unnamed = codes.Count(c => c?["status"]?.GetValue<string>() == "unnamed");
        if (_filter.Length == 0) _filter = unnamed > 0 ? "name" : "all";
        if (unnamed > 0) Detail.Children.Add(await QuickWaysAsync(model, attached, unnamed));
        Detail.Children.Add(CodesCard(model, attached, codes));
    }

    private FrameworkElement Summary(JsonNode m)
    {
        string model = m["model"]!.GetValue<string>();
        string name = m["name"]?.GetValue<string>() is { Length: > 0 } n ? n : model;
        bool attached = m["attached"]?.GetValue<bool>() == true;
        bool builtIn = m["builtIn"]?.GetValue<bool>() == true;
        bool read = m["capabilitiesRead"]?.GetValue<bool>() == true;
        int codes = m["codes"]?.GetValue<int>() ?? 0, unnamed = m["unnamed"]?.GetValue<int>() ?? 0, mapped = m["mapped"]?.GetValue<int>() ?? 0;

        var head = new Grid { ColumnSpacing = 10 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Spacing = 4 };
        title.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 18, TextWrapping = TextWrapping.Wrap });
        var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        identity.Children.Add(new TextBlock { Text = model, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center });
        identity.Children.Add(Badge(attached ? "Attached" : "Not attached"));
        if (builtIn) identity.Children.Add(Badge("Built-in"));
        title.Children.Add(identity);
        string seen = $"Seen {Date(m["firstSeen"])} to {Date(m["lastSeen"])}.";
        title.Children.Add(Muted(builtIn ? $"{seen} A built-in panel has no DDC/CI channel, so it has no codes; what it is still matters to burn-in protection."
            : !read && attached ? $"{seen} The engine is reading its codes; they appear here by themselves."
            : !read ? $"{seen} Its codes were never read; they will be the next time it is attached."
            : $"{seen} {codes} codes: {codes - unnamed - mapped} named by the standard, {mapped} by you or the library, {unnamed} by nobody yet."));
        head.Children.Add(title);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(buttons, 1);
        if (DeviceDefinitions.IsModel(model))
        {
            var share = new Button { Content = "Contribute", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
            AutomationProperties.SetName(share, $"Contribute {model}");
            ToolTipService.SetToolTip(share, "Shows exactly what would be published - the model's record, your names, and what its unnamed codes were seen to do - then opens a GitHub issue for you to submit.");
            share.Click += async (_, _) => await ShareAsync(model);
            buttons.Children.Add(share);
        }
        var remove = new Button { Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 } };
        AutomationProperties.SetName(remove, $"Remove {model}");
        ToolTipService.SetToolTip(remove, "Remove from this list. It stays off until you choose Scan, including after reconnecting. Names you made are kept.");
        remove.Click += async (_, _) =>
        {
            if (await RunAsync("devices.forget", new JsonObject { ["model"] = model }) is not null)
            {
                Show($"Removed {name}. Any codes you named for it are kept.", InfoBarSeverity.Informational);
                _selected = null;
                await RefreshAsync();
            }
        };
        buttons.Children.Add(remove);
        head.Children.Add(buttons);
        return Card(head);
    }

    /// <summary>What a built-in panel is: the one fact it can be described by.</summary>
    private async Task<FrameworkElement> PanelCardAsync(string model)
    {
        JsonNode? data = await RunAsync("devices.show", new JsonObject { ["model"] = model, ["history"] = true });
        string[] kinds = ["Not said", "LCD", "OLED", "QD-OLED", "Mini-LED"];
        string current = data?["panel"]?.GetValue<string>() ?? "";
        var picker = new ComboBox { Header = "What the panel is", ItemsSource = kinds, MinWidth = 200,
            SelectedIndex = Math.Max(0, Array.FindIndex(kinds, k => k.Equals(current, StringComparison.OrdinalIgnoreCase))) };
        AutomationProperties.SetName(picker, $"Panel {model}");
        picker.SelectionChanged += async (_, _) =>
        {
            string technology = picker.SelectedIndex <= 0 ? "none" : kinds[picker.SelectedIndex];
            if (await RunAsync("devices.panel", new JsonObject { ["model"] = model, ["technology"] = technology }) is not null)
                Show(technology == "none" ? "Cleared." : $"Saved: {technology}. Contribute shares it with every owner of this laptop.", InfoBarSeverity.Success);
        };
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(Heading("The panel"));
        body.Children.Add(Muted("Nothing a built-in panel reports says whether it is OLED, and OLED care keys off it. Say it here once; shared, the library answers for everyone with the same laptop."));
        body.Children.Add(picker);
        return Card(body);
    }

    /// <summary>The three quickest ways to name a code, best first.</summary>
    /// <remarks>
    /// Learning a setting is the one that needs no knowledge of codes at all:
    /// change it with the monitor's buttons and the code that moved is the
    /// answer. Borrowing a sibling model's names needs none either, when the
    /// library has one. Naming by hand is for those who already know.
    /// </remarks>
    private async Task<FrameworkElement> QuickWaysAsync(string model, bool attached, int unnamed)
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Heading($"Name the {unnamed} unknown code{(unnamed == 1 ? "" : "s")}"));

        var learn = new Button { Content = "Learn a setting…", IsEnabled = attached, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        AutomationProperties.SetName(learn, "DevicesLearnSetting");
        learn.Click += async (_, _) => await LearnAsync(model);
        body.Children.Add(Way(learn, "Easiest. Change one setting with the monitor's own buttons - a picture mode, say - and DispCtrl finds the code that moved and its values.",
            attached ? null : "Attach the monitor to use this."));

        JsonNode? similar = await RunAsync("devices.similar", new JsonObject { ["model"] = model });
        var known = new StackPanel { Spacing = 6 };
        JsonArray candidates = similar?["candidates"]?.AsArray() ?? [];
        string brand = DeviceDefinitions.Brand(model);
        if (candidates.Count == 0)
            known.Children.Add(Muted($"No other {brand} model in the library names these codes yet. Naming them here and choosing Contribute is how one gets there."));
        foreach (JsonNode? c in candidates)
        {
            string other = c!["model"]!.GetValue<string>();
            bool linked = c["linked"]!.GetValue<bool>();
            int would = c["wouldName"]!.GetValue<int>();
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = c["name"]?.GetValue<string>() is { Length: > 0 } n ? $"{n}  ({other})" : other });
            text.Children.Add(Muted(linked ? $"In use: its names apply to this monitor. From the {c["from"]}."
                : $"Would name {would} of this monitor's unknown codes. From the {c["from"]}."));
            row.Children.Add(text);
            var use = new Button { Content = linked ? "Stop using" : "Use its names", VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(use, $"{(linked ? "Stop using" : "Use")} {other}");
            ToolTipService.SetToolTip(use, "Writes one line in this model's own definition on this PC; nothing on the monitor changes. Names are read-only until you confirm a value.");
            use.Click += async (_, _) =>
            {
                var request = new JsonObject { ["model"] = model, ["to"] = other };
                if (linked) request["remove"] = true;
                if (await RunAsync("devices.link", request) is null) return;
                Show(linked ? $"{other}'s names no longer apply." : $"{other}'s names now apply to {model}. Check each on Displays before enabling it.", InfoBarSeverity.Success);
                await RefreshAsync();
            };
            Grid.SetColumn(use, 1);
            row.Children.Add(use);
            known.Children.Add(row);
        }
        body.Children.Add(Way(known, "Borrow the names from a known monitor. A maker reuses its codes across a range, so a sibling model often names most of them.", null));

        var byHand = new Button { Content = "Add a code by hand…" };
        AutomationProperties.SetName(byHand, $"Add code {model}");
        byHand.Click += async (_, _) =>
        {
            StopWatching();
            if (await NameAsync(model, "", new JsonObject())) await RefreshAsync();
        };
        body.Children.Add(Way(byHand, "If you already know a code and what it does, from a manual or ddcutil, name it directly. Each code below has its own Name button too.", null));
        return Card(body);
    }

    private static FrameworkElement Way(FrameworkElement action, string explanation, string? note)
    {
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap });
        if (note is not null) text.Children.Add(Muted(note));
        var row = new StackPanel { Spacing = 6 };
        row.Children.Add(text);
        row.Children.Add(action);
        return row;
    }

    private async Task LearnAsync(string model)
    {
        DisplayInfo? display = DisplayRegistry.Enumerate().FirstOrDefault(d => d.Key.Model == model);
        if (display is null) { Show("Attach the monitor first.", InfoBarSeverity.Warning); return; }
        StopWatching();
        var dialog = new LearnSettingDialog(display);
        _editing = true;
        bool saved;
        try { saved = await dialog.ShowAsync(XamlRoot); }
        finally { _editing = false; }
        if (!saved) return;
        Show("Saved. It is a control on Displays now; Contribute shares it.", InfoBarSeverity.Success);
        foreach (var d in App.ViewModel.Displays.Where(d => d.Info.Key.Model == model))
            await d.RefreshMonitorControlsAsync();
        await RefreshAsync();
    }

    // ================================================================ the codes

    private sealed record Row(string Code, TextBlock Value, TextBlock Change, Border Host);

    /// <summary>The codes, filtered to what is left to name by default and grouped by who defines them.</summary>
    /// <remarks>
    /// 0xE0 to 0xFF is MCCS's manufacturer range: nothing there is standard, so
    /// it is where nearly every unknown code lives and gets its own heading.
    /// </remarks>
    private FrameworkElement CodesCard(string model, bool attached, JsonArray codes)
    {
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(Heading("Codes"));
        int unnamed = codes.Count(c => c?["status"]?.GetValue<string>() == "unnamed");
        string[] keys = ["name", "named", "all"];
        var filter = new RadioButtons { MaxColumns = 3 };
        filter.Items.Add($"To name ({unnamed})");
        filter.Items.Add($"Named ({codes.Count - unnamed})");
        filter.Items.Add($"Everything ({codes.Count})");
        filter.SelectedIndex = Math.Max(0, Array.IndexOf(keys, _filter));
        AutomationProperties.SetName(filter, $"Code filter {model}");
        body.Children.Add(filter);

        var watch = new ToggleSwitch
        {
            Header = "Watch the unknown codes live",
            OnContent = "Watching - change one setting at a time in the monitor's own menu",
            OffContent = attached ? "Off" : "Attach the monitor to watch it",
            IsEnabled = attached,
        };
        AutomationProperties.SetName(watch, $"Watch {model}");
        body.Children.Add(watch);

        var list = new StackPanel { Spacing = 2 };
        body.Children.Add(list);
        var rows = new List<Row>();

        void Fill()
        {
            StopWatching();
            watch.IsOn = false;
            list.Children.Clear();
            rows.Clear();
            var shown = codes.Where(c => c is not null && _filter switch
            {
                "name" => c["status"]!.GetValue<string>() == "unnamed",
                "named" => c["status"]!.GetValue<string>() != "unnamed",
                _ => true,
            }).OrderBy(c => c!["code"]!.GetValue<string>(), StringComparer.Ordinal).ToList();
            if (shown.Count == 0)
            {
                list.Children.Add(Muted(_filter == "name" ? "Nothing left to name." : "None."));
                return;
            }
            foreach (var group in shown.GroupBy(c => DeviceDefinitions.ParseCode(c!["code"]!.GetValue<string>()) >= 0xE0).OrderBy(g => g.Key))
            {
                var heading = Muted(group.Key ? "Manufacturer-specific codes (0xE0 to 0xFF): the maker's own, never in the standard"
                    : "Standard range (0x00 to 0xDF)");
                heading.Margin = new Thickness(0, 8, 0, 2);
                heading.FontWeight = FontWeights.SemiBold;
                list.Children.Add(heading);
                var header = RowGrid();
                AddCells(header, Muted("Code"), Muted("What it is"), Muted("Values"), new TextBlock());
                list.Children.Add(header);
                foreach (JsonNode? c in group) list.Children.Add(CodeRow(model, c!, rows, watch));
            }
            watch.IsEnabled = attached && rows.Count > 0;
        }
        filter.SelectionChanged += (_, _) =>
        {
            if (filter.SelectedIndex < 0) return;
            _filter = keys[filter.SelectedIndex];
            Fill();
        };
        watch.Toggled += (_, _) =>
        {
            if (watch.IsOn) StartWatching(model, rows, watch);
            else StopWatching();
        };
        Fill();
        return Card(body);
    }

    private FrameworkElement CodeRow(string model, JsonNode c, List<Row> rows, ToggleSwitch watch)
    {
        string code = c["code"]!.GetValue<string>();
        string status = c["status"]!.GetValue<string>();
        string name = c["name"]?.GetValue<string>() ?? c["reported"]?.GetValue<string>() ?? code;

        var what = new StackPanel();
        what.Children.Add(new TextBlock { Text = status == "unnamed" ? "Not named yet" : name, TextTrimming = TextTrimming.CharacterEllipsis });
        what.Children.Add(Muted(status switch
        {
            "standard" => "Standard; DispCtrl controls it",
            "named" => "Named by the standard; read-only here",
            "mapped" => $"Named in the {Origin(c["origin"]?.GetValue<string>())}{(c["writable"]?.GetValue<bool>() == true ? ", writable" : ", read-only")}",
            _ => Shape(c),
        }));
        if (c["discovery"]?.GetValue<string>() == "probed") what.Children.Add(Muted("Found by a read-only probe; not advertised"));
        if (c["discovery"]?.GetValue<string>() == "mapping") what.Children.Add(Muted(c["ddcWrite"] is null
            ? "Not reported by this monitor; saved for reference" : "Uses an explicit input-switching method"));

        var value = new TextBlock { Text = Values(c), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var change = Muted("");
        var valueStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        valueStack.Children.Add(value);
        valueStack.Children.Add(change);

        var nameIt = new Button { Content = status == "mapped" ? "Edit" : "Name" };
        // Standard codes DispCtrl already controls have nothing to name.
        if (status == "standard") nameIt.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(nameIt, $"Name {code}");
        nameIt.Click += async (_, _) =>
        {
            watch.IsOn = false;
            StopWatching();
            JsonNode? fresh = await RunAsync("devices.show", new JsonObject { ["model"] = model, ["history"] = true });
            JsonNode current = fresh?["codes"]?.AsArray().FirstOrDefault(item => item?["code"]?.GetValue<string>() == code) ?? c;
            if (await NameAsync(model, code, current)) await RefreshAsync();
        };

        var grid = RowGrid();
        AddCells(grid, new TextBlock { Text = code, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center },
            what, valueStack, nameIt);
        var host = new Border { Child = grid, CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 2, 4, 2) };
        if (DeviceDefinitions.ParseCode(code) is byte number && !MonitorCapabilities.IsNamed(number)
            && c["ddcWrite"] is null && c["mappedKind"]?.GetValue<string>() != "action" && c["discovery"]?.GetValue<string>() != "mapping")
            rows.Add(new Row(code, value, change, host));
        return host;
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 15 };

    private static Border Card(UIElement child) => new()
    {
        Child = child,
        Padding = new Thickness(16, 12, 16, 14),
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        Background = Brush("CardBackgroundFillColorDefaultBrush"),
        BorderBrush = Brush("CardStrokeColorDefaultBrush"),
    };

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
        if (seen.Length > 1) return $"Seen at {seen.Length} different values; watch the menu to identify it";
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
    private void StartWatching(string model, List<Row> rows, ToggleSwitch watch)
    {
        StopWatching();
        DisplayInfo? display = DisplayRegistry.Enumerate().FirstOrDefault(d => d.Key.Model == model);
        if (display is null || rows.Count == 0) return;
        var cancel = _watch = new CancellationTokenSource();
        CancellationToken token = cancel.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                MonitorCapability capabilities = MonitorCapabilities.Read(display, readValues: false, includeMappings: false);
                var byCode = rows.ToDictionary(r => DeviceDefinitions.ParseCode(r.Code)!.Value);
                List<VcpControl> wanted = capabilities.Controls.Where(c => byCode.ContainsKey(c.Code)).ToList();
                if (wanted.Count == 0) throw new InvalidOperationException("No manufacturer controls are available to watch. Scan controls again with the monitor connected.");
                var last = new Dictionary<byte, int>();
                while (!token.IsCancellationRequested)
                {
                    MonitorCapabilities.ObserveValues(display, capabilities, wanted);
                    foreach (VcpControl c in wanted)
                    {
                        if (c.Current < 0) continue;
                        bool had = last.TryGetValue(c.Code, out int before);
                        last[c.Code] = c.Current;
                        if (had && before == c.Current) continue;
                        int now = c.Current;
                        if (!byCode.TryGetValue(c.Code, out Row? row)) continue;
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (token.IsCancellationRequested || !_loaded) return;
                            row.Value.Text = $"now {now} (0x{now:X2})";
                            if (had)
                            {
                                row.Change.Text = $"changed from {before} at {DateTime.Now:HH:mm:ss}";
                                row.Host.Background = Brush("AccentFillColorTertiaryBrush");
                            }
                        });
                    }
                    await Task.Delay(400, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { DispatcherQueue.TryEnqueue(() => { if (!token.IsCancellationRequested && _loaded) Show(ex.Message, InfoBarSeverity.Error); }); }
            finally
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!ReferenceEquals(_watch, cancel)) return;
                    StopWatching();
                    watch.IsOn = false;
                });
            }
        });
    }

    private void StopWatching()
    {
        _watch?.Cancel();
        _watch?.Dispose();
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
        string startKind = entry["mappedKind"]?.GetValue<string>() ?? (listed.Length > 0 ? "choice"
            : entry["kind"]?.GetValue<string>() == "continuous" ? "range" : "information");
        var codeBox = new TextBox { Header = "Control code", PlaceholderText = "0xE2", Text = code, IsReadOnly = code.Length > 0 };
        var name = new TextBox { Header = "What it does", PlaceholderText = "Preset mode", Text = entry["name"]?.GetValue<string>() ?? "" };
        var kind = new ComboBox { Header = "How to use it", ItemsSource = new[] { "Choices (menu options)", "Slider (a number)", "Button (sends value 1)", "Read-only information" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        kind.SelectedIndex = Math.Max(0, Array.IndexOf(kinds, startKind));
        var values = new StackPanel { Spacing = 6 };
        values.Children.Add(Muted("Name the options you recognise. Values found by scanning or watching are filled in for you."));
        var valueRows = new StackPanel { Spacing = 4 };
        values.Children.Add(valueRows);
        var choices = new List<(TextBox Value, TextBox Name)>();
        void AddChoice(string value, string label)
        {
            var raw = new TextBox { Text = value, PlaceholderText = "Value", Width = 92 };
            var title = new TextBox { Text = label, PlaceholderText = "What this option does", MinWidth = 200 };
            var remove = new Button { Content = "Remove" };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(raw); row.Children.Add(title); row.Children.Add(remove);
            choices.Add((raw, title)); valueRows.Children.Add(row);
            remove.Click += (_, _) => { choices.Remove((raw, title)); valueRows.Children.Remove(row); };
        }
        if (entry["values"]?.AsArray() is { Count: > 0 } named)
            foreach (JsonNode? v in named) AddChoice(v!["value"]!.ToString(), v["name"]!.GetValue<string>());
        else foreach (int v in listed.Concat(Ints(entry["observed"])).Distinct().Order()) AddChoice($"0x{v:X2}", "");
        var addValue = new Button { Content = "Add an option" };
        addValue.Click += (_, _) => AddChoice("", "");
        values.Children.Add(addValue);
        string brand = DeviceDefinitions.Brand(model);
        string? localTarget = entry["origin"]?.GetValue<string>() is { } origin && origin.StartsWith("local ", StringComparison.Ordinal)
            ? origin[6..] : null;
        var scope = new ComboBox
        {
            Header = "Applies to",
            ItemsSource = new[] { $"This model ({model})", $"Every {brand} monitor that lists it", "Every monitor that lists it" },
            SelectedIndex = localTarget == "*" ? 2 : localTarget == brand ? 1 : 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var writable = new CheckBox { Content = "I confirmed what these values do. Enable this control.", IsChecked = entry["writable"]?.GetValue<bool>() == true };
        var maximum = new NumberBox { Header = "Maximum (optional, for a range)", Minimum = 0, Maximum = 65535,
            Value = entry["maximum"]?.GetValue<int>() ?? entry["reportedMaximum"]?.GetValue<int>() ?? double.NaN };
        // For a monitor that shows and reads a setting in bigger steps than it
        // takes when written: Black Stabilizer 0-100 read, 0-20 written (#29).
        var writeScale = new NumberBox { Header = "Written in smaller units (optional): divide by", Minimum = 2, Maximum = 100,
            PlaceholderText = "e.g. 5 when it reads 0-100 but takes 0-20",
            Value = entry["writeScale"]?.GetValue<int>() ?? double.NaN };
        var transport = new ComboBox { Header = "Input switching method", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "Standard DDC/CI", "LG alternate input (model-specific)" },
            SelectedIndex = entry["ddcWrite"] is null ? 0 : 1,
            Visibility = DeviceDefinitions.IsLgModel(model) ? Visibility.Visible : Visibility.Collapsed };
        void UpdateTransport()
        {
            bool alternate = transport.SelectedIndex == 1;
            scope.IsEnabled = !alternate;
            if (!alternate) return;
            scope.SelectedIndex = 0;
            codeBox.Text = "0x60";
            kind.SelectedIndex = 0;
            if (string.IsNullOrWhiteSpace(name.Text)) name.Text = "Input source";
        }
        transport.SelectionChanged += (_, _) => UpdateTransport();
        UpdateTransport();
        // A transport choice applies to logical input only; editing another
        // code must not silently turn it into an input mapping.
        if (code.Length > 0 && DeviceDefinitions.ParseCode(code) != 0x60) transport.Visibility = Visibility.Collapsed;
        var notes = new TextBox { Header = "Notes", PlaceholderText = "Moves when the OSD's Preset Modes item changes.", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 72,
            Text = entry["notes"]?.GetValue<string>() ?? "" };
        var error = new TextBlock { Foreground = Brush("SystemFillColorCriticalBrush"), TextWrapping = TextWrapping.Wrap };

        var form = new StackPanel { Spacing = 10, MinWidth = 420 };
        form.Children.Add(Muted($"Save for {model}, use from Displays, then Contribute. A shared brand mapping applies only where the monitor exposes that code."));
        form.Children.Add(Muted("Model mappings take priority over brand mappings; brand mappings take priority over all monitors."));
        foreach (UIElement e in new UIElement[] { codeBox, name, kind, values, maximum, writeScale, scope, transport, writable, notes, error }) form.Children.Add(e);
        void UpdateKind()
        {
            values.Visibility = kind.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            maximum.Visibility = kind.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            writeScale.Visibility = maximum.Visibility;
            writable.IsEnabled = kind.SelectedIndex != 3;
            if (!writable.IsEnabled) writable.IsChecked = false;
        }
        kind.SelectionChanged += (_, _) => UpdateKind();
        UpdateKind();

        var dialog = new ContentDialog
        {
            Title = code.Length == 0 ? "Add a monitor control" : $"Map {code}",
            Content = new ScrollViewer { Content = form },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        string removeScope = localTarget == "*" ? "all" : localTarget == brand ? "brand" : "model";
        if (localTarget is not null) dialog.SecondaryButtonText = removeScope == "model" ? "Remove local mapping" : $"Remove {removeScope} mapping";
        bool saved = false;
        bool removed = false;
        dialog.SecondaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                JsonNode? result = await RunAsync("devices.unmap", new JsonObject { ["model"] = model, ["code"] = code, ["scope"] = removeScope });
                removed = saved = result is not null;
                args.Cancel = !saved;
                if (removed) scope.SelectedIndex = removeScope == "all" ? 2 : removeScope == "brand" ? 1 : 0;
            }
            finally { deferral.Complete(); }
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                string chosen = kinds[kind.SelectedIndex];
                var namedChoices = choices.Where(c => c.Name.Text.Trim().Length > 0).ToArray();
                if (chosen == "choice" && namedChoices.Select(c => DeviceDefinitions.KeyFor(c.Name.Text))
                    .Distinct(StringComparer.Ordinal).Count() != namedChoices.Length)
                    throw new ArgumentException("Give each option a different name so it can be selected unambiguously.");
                var request = new JsonObject
                {
                    ["model"] = model, ["code"] = codeBox.Text.Trim(), ["name"] = name.Text.Trim(),
                    ["kind"] = chosen,
                    ["scope"] = scope.SelectedIndex switch { 1 => "brand", 2 => "all", _ => "model" },
                    ["writable"] = writable.IsChecked == true,
                    ["confidence"] = "observed",
                };
                // Values only mean something for a choice; sent for another kind
                // they would be refused as a contradiction.
                request["values"] = chosen == "choice" ? new JsonArray(namedChoices
                    .Select(c => (JsonNode)new JsonObject { ["value"] = c.Value.Text.Trim(), ["name"] = c.Name.Text.Trim() }).ToArray()) : new JsonArray();
                request["maximum"] = null;
                if (chosen == "range" && !double.IsNaN(maximum.Value))
                {
                    if (maximum.Value != Math.Truncate(maximum.Value)) throw new ArgumentException("Maximum must be a whole number.");
                    request["maximum"] = (int)maximum.Value;
                }
                request["writeScale"] = null;
                if (chosen == "range" && !double.IsNaN(writeScale.Value))
                {
                    if (writeScale.Value != Math.Truncate(writeScale.Value)) throw new ArgumentException("The divisor must be a whole number.");
                    request["writeScale"] = (int)writeScale.Value;
                }
                request["notes"] = notes.Text.Trim();
                request["transport"] = transport.SelectedIndex == 1 ? "lg-input" : "standard";
                bool broadened = localTarget == model && scope.SelectedIndex > 0
                    || localTarget == brand && scope.SelectedIndex == 2;
                JsonObject result = await Task.Run(() =>
                {
                    JsonObject mapped = _service.Execute(new JsonObject { ["version"] = 1, ["command"] = "devices.map", ["args"] = request });
                    if (mapped["ok"]?.GetValue<bool>() != true || !broadened) return mapped;
                    // Save first. Only then remove the narrower local override,
                    // otherwise it would silently hide the newly broadened mapping.
                    return _service.Execute(new JsonObject { ["version"] = 1, ["command"] = "devices.unmap",
                        ["args"] = new JsonObject { ["model"] = model, ["code"] = code, ["scope"] = removeScope } });
                });
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
        _editing = true;
        try { await dialog.ShowAsync(); }
        finally { _editing = false; }
        if (saved)
        {
            await Task.WhenAll(App.ViewModel.Displays.Where(d => scope.SelectedIndex == 2
                || scope.SelectedIndex == 1 && DeviceDefinitions.Brand(d.ModelCode) == brand
                || d.ModelCode == model).Select(d => d.RefreshMonitorControlsAsync()));
            Show(removed ? "Local mapping removed. Any inherited mapping applies again."
                : $"{codeBox.Text} saved. Supported, enabled controls are available on Displays. Contribute includes your mapping and discoveries.", InfoBarSeverity.Success);
        }
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
        if (_scanning) { Show("The scan is still running. Contribute will include the results when it finishes.", InfoBarSeverity.Informational); return; }
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
                XamlRoot = XamlRoot, Title = model is null ? "Contribute my monitors" : $"Contribute {model}", Content = content,
                PrimaryButtonText = "Open GitHub issue", SecondaryButtonText = "Copy all", CloseButtonText = "Cancel",
                IsPrimaryButtonEnabled = false, IsSecondaryButtonEnabled = false, DefaultButton = ContentDialogButton.None,
            };
            bool closed = false;
            dialog.Closed += (_, _) => closed = true;
            var shown = dialog.ShowAsync();
            JsonNode? data = await RunAsync("devices.contribute", model is null
                ? new JsonObject { ["all"] = true } : new JsonObject { ["model"] = model });
            if (closed || !_loaded) return;
            progress.IsActive = false;
            progress.Visibility = Visibility.Collapsed;
            if (data is null) { notice.Text = "Could not prepare the contribution. Close this window to see the error and try again."; await shown; return; }
            string body = data["body"]!.GetValue<string>();
            string? paste = data["paste"]?.GetValue<string>();
            content.Children.Insert(0, Muted("Review the model records and mappings below. Monitor serials and user paths are removed. Nothing is submitted until you press Submit on GitHub."));
            content.Children.Insert(1, new TextBox
            {
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"), FontSize = 12, Height = 320, Text = body,
            });
            notice.Text = paste is null ? $"All {body.Length:N0} characters fit in the issue link."
                : $"The complete contribution is {body.Length:N0} characters. Opening GitHub copies the text that needs pasting; replace the placeholder in the issue with it.";
            dialog.IsPrimaryButtonEnabled = dialog.IsSecondaryButtonEnabled = true;
            dialog.SecondaryButtonClick += (_, args) =>
            {
                args.Cancel = true;
                try { Copy(body); notice.Text = "Copied the complete contribution."; }
                catch (Exception ex) { notice.Text = "Could not copy: " + ex.Message; }
            };
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    if (paste is not null) Copy(paste);
                    if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(data["url"]!.GetValue<string>())))
                        throw new InvalidOperationException("Windows could not open the browser. Copy the contribution and try again.");
                    Show(paste is null ? "Opened the prefilled issue. Review it and press Submit there."
                        : "Opened the issue. Paste the copied text over its placeholder, then press Submit there.", InfoBarSeverity.Success);
                }
                catch (Exception ex) { args.Cancel = true; notice.Text = ex.Message; }
                finally { deferral.Complete(); }
            };
            await shown;
        }
        catch (Exception ex) { dialog?.Hide(); Show("Could not contribute: " + ex.Message, InfoBarSeverity.Error); }
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
