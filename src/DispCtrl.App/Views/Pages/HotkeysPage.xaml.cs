using DispCtrl.App.ViewModels;
using DispCtrl.Core.Settings;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace DispCtrl.App.Views.Pages;

public sealed partial class HotkeysPage : Page
{
    public HotkeysViewModel ViewModel => App.ViewModel.Hotkeys;

    // Windows' own shortcuts, drawn with the same keycaps as DispCtrl's.
    public string[] WinP { get; } = ["Win", "P"];
    public string[] WinShiftArrow { get; } = ["Win", "Shift", "Left / Right"];
    public string[] WinAltB { get; } = ["Win", "Alt", "B"];
    public string[] WinA { get; } = ["Win", "A"];
    public string[] WinK { get; } = ["Win", "K"];
    public string[] WinCtrlC { get; } = ["Win", "Ctrl", "C"];
    public string[] WinCtrlShiftB { get; } = ["Win", "Ctrl", "Shift", "B"];

    /// <summary>The binding waiting for a key press, if any.</summary>
    private HotkeyViewModel? _capturing;

    private FileSystemWatcher? _status;

    public HotkeysPage()
    {
        InitializeComponent();

        // Handled events included: a bare arrow key is swallowed by focus
        // navigation before a normal KeyDown handler ever sees it, and arrows
        // are exactly what people bind to brightness.
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDown), handledEventsToo: true);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        HotkeyViewModel.SafetyNetOffRequested -= OnSafetyNetOff;
        HotkeyViewModel.SafetyNetOffRequested += OnSafetyNetOff;
        ViewModel.Reload();
        ViewModel.SetEngineRunning(App.ViewModel.EngineRunning);
        _ = FillTriggersAsync();

        // The engine rewrites its status each time it registers, so a card
        // changes from "waiting" to "working" or "taken" by itself.
        try
        {
            Directory.CreateDirectory(SettingsStore.Directory);
            _status = new FileSystemWatcher(SettingsStore.Directory, Path.GetFileName(HotkeyStatus.PathOnDisk))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _status.Changed += (_, _) => DispatcherQueue.TryEnqueue(ViewModel.RefreshStates);
            _status.Renamed += (_, _) => DispatcherQueue.TryEnqueue(ViewModel.RefreshStates);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
    }

    // -------------------------------------------------------------- triggers --

    private static Task<System.Text.Json.Nodes.JsonObject> TriggerCommand(string action, System.Text.Json.Nodes.JsonObject? args = null) => Task.Run(() =>
        new DispCtrl.Control.ControlService().Execute(new System.Text.Json.Nodes.JsonObject
        { ["version"] = 1, ["command"] = "triggers." + action, ["args"] = args ?? [] }));

    /// <summary>Every trigger as a row, and a row to add one; read again after each change.</summary>
    private async Task FillTriggersAsync(string? message = null)
    {
        var result = await TriggerCommand("list");
        TriggerRows.Children.Clear();
        if (message is not null)
            TriggerRows.Children.Add(new InfoBar { IsOpen = true, IsClosable = true, Message = message, Severity = InfoBarSeverity.Warning });
        var data = result["data"];
        foreach (var item in data?["triggers"]?.AsArray() ?? [])
        {
            if (item is null) continue;
            int index = item["index"]!.GetValue<int>();
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = item["does"]!.ToString(), TextWrapping = TextWrapping.Wrap });
            if (item["problem"]?.ToString() is { Length: > 0 } problem)
                words.Children.Add(new TextBlock { Text = problem, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] });
            row.Children.Add(words);
            // Value first, handler after: a switch set from code raises Toggled too.
            var on = new ToggleSwitch { IsOn = item["enabled"]!.GetValue<bool>(), OnContent = null, OffContent = null, MinWidth = 0 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(on, $"Trigger {index}");
            on.Toggled += async (_, _) =>
            {
                var set = await TriggerCommand("set", new() { ["index"] = index, ["enabled"] = on.IsOn });
                if (set["ok"]?.GetValue<bool>() != true) await FillTriggersAsync(set["error"]?["message"]?.ToString());
            };
            Grid.SetColumn(on, 1);
            row.Children.Add(on);
            var remove = new Button { Content = "Remove" };
            remove.Click += async (_, _) =>
            {
                var gone = await TriggerCommand("remove", new() { ["index"] = index });
                await FillTriggersAsync(gone["ok"]?.GetValue<bool>() == true ? null : gone["error"]?["message"]?.ToString());
            };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            TriggerRows.Children.Add(row);
        }

        string[] events = [.. (data?["events"]?.AsArray() ?? []).Select(e => e!.ToString())];
        var when = new ComboBox { Header = "When", ItemsSource = events, SelectedIndex = 0, MinWidth = 180 };
        var match = new TextBox { Header = "Which", PlaceholderText = "vlc.exe, 2, DELL or 20:00", Width = 170 };
        var minutes = new NumberBox { Header = "Minutes away", Value = 10, Minimum = 1, Maximum = 1440, Width = 120,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var feature = new ComboBox { Header = "Run", ItemsSource = ViewModel.Features.Select(f => f.Name).ToArray(), MinWidth = 160,
            PlaceholderText = "A saved feature" };
        var add = new Button { Content = "Add trigger", VerticalAlignment = VerticalAlignment.Bottom };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(add, "AddTrigger");
        void Shape()
        {
            string e = when.SelectedItem as string ?? "";
            minutes.Visibility = e is "idle" or "back" ? Visibility.Visible : Visibility.Collapsed;
            match.Visibility = e is "app-in-front" or "app-left" or "at-time" or "display-connected" or "display-disconnected"
                ? Visibility.Visible : Visibility.Collapsed;
            match.PlaceholderText = e switch { "at-time" => "20:00", "app-in-front" or "app-left" => "vlc.exe", _ => "Any display, or 2, or DELL" };
        }
        when.SelectionChanged += (_, _) => Shape();
        Shape();
        add.Click += async (_, _) =>
        {
            var args = new System.Text.Json.Nodes.JsonObject
            {
                ["event"] = when.SelectedItem as string ?? "", ["feature"] = feature.SelectedItem as string ?? "", ["match"] = match.Text.Trim(),
            };
            if (minutes.Visibility == Visibility.Visible && !double.IsNaN(minutes.Value)) args["minutes"] = (int)minutes.Value;
            var added = await TriggerCommand("add", args);
            await FillTriggersAsync(added["ok"]?.GetValue<bool>() == true ? null : added["error"]?["message"]?.ToString());
        };
        var form = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (UIElement e in new UIElement[] { when, match, minutes, feature, add }) form.Children.Add(e);
        TriggerRows.Children.Add(new ScrollViewer { Content = form, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
    }

    /// <summary>Shows one of the page's four parts.</summary>
    private void OnSection(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        int at = sender.Items.IndexOf(sender.SelectedItem);
        StackPanel[] sections = [ShortcutsSection, FeaturesSection, TriggersSection, WindowsSection];
        for (int i = 0; i < sections.Length; i++) sections[i].Visibility = i == at ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        HotkeyViewModel.SafetyNetOffRequested -= OnSafetyNetOff;
        _status?.Dispose();
        _status = null;
        StopCapture();
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        HotkeyViewModel item = ViewModel.Add();
        StartCapture(item);
    }

    private void OnAddFeature(object sender, RoutedEventArgs e) => ViewModel.AddFeature();

    private async void OnSaveFeature(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CustomFeatureViewModel feature)
            await ViewModel.SaveFeatureAsync(feature);
    }

    private async void OnDeleteFeature(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CustomFeatureViewModel feature)
            await ViewModel.DeleteFeatureAsync(feature);
    }

    private async void OnRunFeature(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CustomFeatureViewModel feature)
            await ViewModel.RunFeatureAsync(feature, false);
    }

    private async void OnTestFeature(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CustomFeatureViewModel feature)
            await ViewModel.RunFeatureAsync(feature, true);
    }

    private async void OnRestoreDefaults(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "Restore the default shortcuts?",
            Content = "Every shortcut here is replaced with DispCtrl's own set: Ctrl+Alt+Page Up and Page Down for brightness, D for the quick panel, N for night light, L to turn the displays off and Backspace to put every display back, switched on, and nine more set up but switched off.",
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        StopCapture();
        ViewModel.RestoreDefaults();
        Say("Restored DispCtrl's shortcuts. The engine registers them straight away.");
    }

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not HotkeyViewModel item) return;
        if (item.IsSafetyNet && !await ConfirmLosingSafetyNet("Remove")) return;
        if (_capturing == item) StopCapture();
        ViewModel.Remove(item);
    }

    private async void OnSafetyNetOff(HotkeyViewModel item)
    {
        if (await ConfirmLosingSafetyNet("Switch off")) item.TurnOff();
        // Read back after the binding has finished writing: raised from inside
        // its own write, the switch kept showing off while the shortcut stayed on.
        else item.RaiseEnabled();
    }

    /// <summary>Asks before the shortcut that puts every display back stops working.</summary>
    private async Task<bool> ConfirmLosingSafetyNet(string verb)
    {
        var confirm = new ContentDialog
        {
            Title = "Turn off the way back?",
            Content = "This shortcut puts every display back when something DispCtrl does leaves a screen black, dim, tinted or without its taskbar - "
                + "including when you cannot see enough to find this page. Without it, the only way back from a black screen is signing out or restarting.",
            PrimaryButtonText = verb,
            CloseButtonText = "Keep it",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        return await confirm.ShowAsync() == ContentDialogResult.Primary;
    }

    private void OnCapture(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not HotkeyViewModel item) return;
        if (_capturing == item) { StopCapture(); Say("Cancelled."); return; }
        StartCapture(item);
    }

    private void OnOpenWindowsDisplay(object sender, RoutedEventArgs e) =>
        _ = Launcher.LaunchUriAsync(new Uri("ms-settings:display"));

    private void StartCapture(HotkeyViewModel item)
    {
        StopCapture();
        _capturing = item;
        item.IsExpanded = true;
        item.IsCapturing = true;
    }

    private void StopCapture()
    {
        if (_capturing is not null) _capturing.IsCapturing = false;
        _capturing = null;
    }

    /// <remarks>
    /// The modifiers are read from the keyboard state rather than from the
    /// event, because the event that carries the non-modifier key does not say
    /// which modifiers were down with it.
    /// </remarks>
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_capturing is null) return;

        if (e.Key is VirtualKey.Escape)
        {
            StopCapture();
            Say("Cancelled.");
            e.Handled = true;
            return;
        }

        // Wait for a real key: a modifier on its own is not a shortcut.
        if (e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows
            or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftMenu or VirtualKey.RightMenu)
        {
            return;
        }

        uint modifiers = 0;
        if (Down(VirtualKey.Menu)) modifiers |= 1;      // MOD_ALT
        if (Down(VirtualKey.Control)) modifiers |= 2;   // MOD_CONTROL
        if (Down(VirtualKey.Shift)) modifiers |= 4;     // MOD_SHIFT
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) modifiers |= 8;

        if (modifiers == 0)
        {
            Say("A shortcut needs at least one of Ctrl, Alt, Shift or Win, or it would take that key from every program.");
            e.Handled = true;
            return;
        }

        HotkeyViewModel item = _capturing;
        _capturing = null;
        item.Capture((uint)e.Key, modifiers);
        string? caution = Hotkey.Caution((uint)e.Key, modifiers);
        Say(caution is null ? $"Set to {item.Shortcut}. The engine picks it up straight away." : $"Set to {item.Shortcut}. {caution}",
            caution is null ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        e.Handled = true;
    }

    private static bool Down(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void Say(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        Toast.Message = message;
        Toast.Severity = severity;
        Toast.IsOpen = true;
    }
}
