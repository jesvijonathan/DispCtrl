using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using DispCtrl.App.ViewModels;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace DispCtrl.App.Views;

public sealed partial class HotkeysPage : Page
{
    public HotkeysViewModel ViewModel => App.ViewModel.Hotkeys;

    /// <summary>The binding waiting for a key press, if any.</summary>
    private HotkeyViewModel? _capturing;

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
    }

    private void OnAdd(object sender, RoutedEventArgs e) => ViewModel.Add();

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is HotkeyViewModel item) ViewModel.Remove(item);
    }

    private void OnCapture(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not HotkeyViewModel item) return;

        _capturing = item;
        Say("Now press the keys you want. Escape cancels.");
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
            _capturing = null;
            Say("Cancelled.");
            e.Handled = true;
            return;
        }

        // Wait for a real key: a modifier on its own is not a shortcut.
        if (e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows)
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
            Say("A shortcut needs at least one of Ctrl, Alt, Shift or Win.");
            e.Handled = true;
            return;
        }

        _capturing.Capture((uint)e.Key, modifiers);
        Say($"Set to {_capturing.Shortcut}. The engine picks it up straight away.");

        _capturing = null;
        e.Handled = true;
    }

    private static bool Down(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void Say(string message)
    {
        Toast.Message = message;
        Toast.IsOpen = true;
    }
}
