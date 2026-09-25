using DispCtrl.App.ViewModels;
using DispCtrl.Display.Placement;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DispCtrl.App.Views;

/// <summary>
/// Pinning windows on top and moving them between displays, in the panel.
/// </summary>
/// <remarks>
/// The panel is in front whenever it is clicked, so "pin the active window" is
/// not something it can mean: pinning here is choosing from the open windows,
/// and the hotkey is the way to pin the one in front. The list is read as the
/// flyout opens, never kept, because a pin is the window's own state and the
/// hotkey or the command line change it without the panel hearing.
/// </remarks>
internal sealed partial class QuickPanelContent
{
    /// <summary>Every open window with a switch to pin it; the border's main options after.</summary>
    private Flyout PinFlyout()
    {
        var body = new StackPanel { Spacing = 2, Width = 320 };
        var list = new StackPanel { Spacing = 0 };
        var flyout = new Flyout { Content = body, Placement = FlyoutPlacementMode.Bottom };

        body.Children.Add(Caption("Pin on top"));
        body.Children.Add(SwitchRow("Pinning", "Off unpins every window DispCtrl pinned.",
            () => _vm.PinEnabled, v => { _vm.PinEnabled = v; Fill(); }, nameof(MainViewModel.PinEnabled), "QuickPinEnabled"));
        body.Children.Add(Note(_vm.PinShortcut));
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var unpinAll = new Button { Content = "Unpin all", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 2) };
        AutomationProperties.SetName(unpinAll, "QuickUnpinAll");
        unpinAll.Click += (_, _) => { _vm.UnpinAll(); Fill(); };
        body.Children.Add(unpinAll);

        body.Children.Add(Divider());
        body.Children.Add(SwitchRow("Border", "A coloured frame around each pinned window.",
            () => _vm.PinBorder, v => _vm.PinBorder = v, nameof(MainViewModel.PinBorder), "QuickPinBorder"));
        body.Children.Add(SwitchRow("Clear of focus mode", "Pinned windows stay lit while focus mode dims the rest.",
            () => _vm.PinClearInFocus, v => _vm.PinClearInFocus = v, nameof(MainViewModel.PinClearInFocus), "QuickPinClearFocus"));

        var more = new HyperlinkButton { Content = "All pinning settings", Margin = new Thickness(0, 6, 0, 0) };
        more.Click += (_, _) => { _dismiss(); App.ShowMainWindow("displays"); };
        body.Children.Add(more);

        void Fill()
        {
            list.Children.Clear();
            if (!_vm.PinEnabled) { list.Children.Add(Note("Pinning is switched off.")); return; }
            List<AppWindow> windows = _vm.OpenWindows();
            if (windows.Count == 0) { list.Children.Add(Note("No windows are open.")); return; }
            foreach (AppWindow window in windows)
            {
                nint handle = window.Handle;
                var row = new Grid { ColumnSpacing = 8, MinHeight = 36 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(RowInset, 0, 0, 0) };
                names.Children.Add(new TextBlock { Text = window.Title, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
                names.Children.Add(new TextBlock { Text = window.Process, FontSize = 11, Foreground = Res("TextFillColorSecondaryBrush") });
                var toggle = new ToggleSwitch
                {
                    IsOn = window.Pinned,
                    OnContent = null,
                    OffContent = null,
                    MinWidth = 0,
                    Margin = new Thickness(0, -4, -8, -4),
                    // On top by itself: not DispCtrl's to pin or unpin.
                    IsEnabled = window.Pinned || !window.Topmost,
                };
                AutomationProperties.SetName(toggle, $"QuickPin {window.Title}");
                ToolTipService.SetToolTip(row, window.Topmost && !window.Pinned ? $"{window.Title}\nStays on top by itself." : window.Title);
                bool settling = false;
                toggle.Toggled += (_, _) =>
                {
                    if (settling) return;
                    string said = _vm.TogglePin(handle);
                    _report(said);
                    // Put back what Windows actually did, if it refused.
                    bool now = WindowPins.IsPinned(handle);
                    if (now != toggle.IsOn) { settling = true; toggle.IsOn = now; settling = false; }
                };
                Grid.SetColumn(toggle, 1);
                row.Children.Add(names);
                row.Children.Add(toggle);
                list.Children.Add(row);
            }
        }

        flyout.Opening += (_, _) => Fill();
        return flyout;
    }

    /// <summary>One entry per display: gather every window onto it.</summary>
    private MenuFlyout GatherMenu()
    {
        var menu = new MenuFlyout();
        foreach (DisplayViewModel d in _vm.Displays)
        {
            DisplayViewModel display = d;
            var item = new MenuFlyoutItem { Text = $"Onto {display.Number}: {display.Name}", Icon = new FontIcon { Glyph = "" } };
            item.Click += (_, _) => GatherInto(display);
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Page("Window settings...", "displays"));
        return menu;
    }

    /// <summary>Gathers every window onto the display in use: where the pointer is, which is where the panel was clicked.</summary>
    private void GatherHere()
    {
        if (_vm.ActiveDisplayNow() is { } display) GatherInto(display);
        else _report("No display is in use to gather onto.");
    }

    private async void GatherInto(DisplayViewModel display)
    {
        // async void from a click: nothing may escape, or it takes the panel down.
        try
        {
            _report($"Gathering every window onto {display.Name}...");
            _report(await _vm.GatherAsync(display));
        }
        catch (Exception ex) { _report("Gathering windows failed: " + ex.Message); }
    }

    /// <summary>The Windows section: putting windows back, new windows, gathering, and what is pinned.</summary>
    private void WindowsSection()
    {
        StackPanel body = Foldable("windows", SectionHeader("Windows"));

        if (_vm.SeveralDisplays)
        {
            body.Children.Add(Caption("Gather every window"));
            var buttons = new List<FrameworkElement>();
            foreach (DisplayViewModel d in _vm.Displays)
            {
                DisplayViewModel display = d;
                var b = new Button
                {
                    Content = $"Onto {display.Number}",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Height = 32,
                };
                ToolTipService.SetToolTip(b, $"Bring every window onto {display.Name}.");
                AutomationProperties.SetName(b, $"QuickGather {display.Number}");
                b.Click += (_, _) => GatherInto(display);
                buttons.Add(b);
            }
            body.Children.Add(Columns(buttons, Math.Min(buttons.Count, 4), 6));

            body.Children.Add(SwitchRow("Put windows back", "When a display returns, its windows go back where they were.",
                () => _vm.ReturnWindows, v => _vm.ReturnWindows = v, nameof(MainViewModel.ReturnWindows), "QuickReturnWindows"));
            body.Children.Add(SwitchRow("New windows here", "A window that opens on another display is moved to the one in use.",
                () => _vm.NewWindowsOnActive, v => _vm.NewWindowsOnActive = v, nameof(MainViewModel.NewWindowsOnActive), "QuickNewWindows"));
        }

        // What is pinned, read now and again whenever the list is re-read: the
        // panel stays built while hidden, and a pin made with the hotkey in the
        // meantime changes no setting that would rebuild it. Every summons
        // re-reads the list (ReloadFromDisk), and that redraws these rows.
        _vm.RefreshPinnedWindows();
        body.Children.Add(Caption("Pinned on top"));
        var pinnedRows = new StackPanel();
        body.Children.Add(pinnedRows);
        void FillPinned()
        {
            pinnedRows.Children.Clear();
            if (_vm.PinnedWindows.Count == 0) { pinnedRows.Children.Add(Note("Nothing is pinned.")); return; }
            foreach (WindowItem item in _vm.PinnedWindows.ToList())
            {
                WindowItem pinned = item;
                var row = new Grid { ColumnSpacing = 8, MinHeight = 32 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = RowLabel(pinned.Title);
                var unpin = new Button { Content = "Unpin", Padding = new Thickness(10, 3, 10, 4) };
                AutomationProperties.SetName(unpin, $"QuickUnpin {pinned.Title}");
                unpin.Click += (_, _) => _vm.Unpin(pinned);
                Grid.SetColumn(unpin, 1);
                row.Children.Add(name);
                row.Children.Add(unpin);
                pinnedRows.Children.Add(row);
            }
        }
        FillPinned();
        // A refresh is a clear and an add per window: drawn once, after the last.
        bool fillQueued = false;
        Microsoft.UI.Dispatching.DispatcherQueue queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Watch(_vm.PinnedWindows, () =>
        {
            if (fillQueued) return;
            fillQueued = true;
            queue.TryEnqueue(() => { fillQueued = false; FillPinned(); });
        });

        var pinSome = new Button { Content = "Pin a window...", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetName(pinSome, "QuickPinChoose");
        Flyout choose = PinFlyout();
        pinSome.Flyout = choose;
        body.Children.Add(pinSome);
    }
}
