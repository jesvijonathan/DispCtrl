using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using DispCtrl.App.ViewModels;
using DispCtrl.Core.Settings;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace DispCtrl.App.Views;

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
        ViewModel.Reload();
        ViewModel.SetEngineRunning(App.ViewModel.EngineRunning);

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

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _status?.Dispose();
        _status = null;
        StopCapture();
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        HotkeyViewModel item = ViewModel.Add();
        StartCapture(item);
    }

    private async void OnRestoreDefaults(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "Restore the default shortcuts?",
            Content = "Every shortcut here is replaced with DispCtrl's own set: Ctrl+Alt+Page Up and Page Down for brightness, D for the quick panel and N for night light, switched on, and nine more set up but switched off.",
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

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not HotkeyViewModel item) return;
        if (_capturing == item) StopCapture();
        ViewModel.Remove(item);
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
