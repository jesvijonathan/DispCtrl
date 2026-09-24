using DispCtrl.App.Services;
using DispCtrl.App.ViewModels;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Entry = DispCtrl.Core.Settings.QuickPanelCatalog.Entry;

namespace DispCtrl.App.Views;

/// <summary>
/// What every id in the quick panel is.
/// </summary>
/// <remarks>
/// <b>Adding a quick toggle</b> takes two lines: an entry in
/// <see cref="QuickPanelCatalog.Tiles"/> (its id, name, symbol and hover text,
/// which the customisation page picks up on its own) and a line in
/// <see cref="DeskTile"/> saying what it switches. The same pattern holds for a
/// display's rows (<see cref="DisplayRow"/>) and the switches in its strip
/// (<see cref="StripItem"/>). Anything not ready for a display - HDR on a panel
/// without it - returns null, and is simply left out for that display.
/// </remarks>
internal sealed partial class QuickPanelContent
{
    // ================================================================ desk tiles

    private FrameworkElement? DeskTile(string id)
    {
        Entry? e = QuickPanelCatalog.Find(QuickPanelGroup.Tiles, id);
        if (e is null) return null;

        return id switch
        {
            "nightLight" => Tile(e, () => _vm.NightLightEnabled, v => _vm.NightLightEnabled = v,
                nameof(MainViewModel.NightLightEnabled), NightLightFlyout()),
            "darkMode" => Tile(e, () => _vm.DarkMode, v => _vm.DarkMode = v,
                nameof(MainViewModel.DarkMode), DarkModeFlyout()),
            "unison" => Tile(e, () => _vm.UnisonBrightness, v => _vm.UnisonBrightness = v,
                nameof(MainViewModel.UnisonBrightness), UnisonFlyout()),
            "focus" => Tile(e, () => _vm.FocusEnabled, v => _vm.FocusEnabled = v,
                nameof(MainViewModel.FocusEnabled), FocusFlyout()),
            "oledIdle" => Tile(e, () => _vm.OledIdleEnabled, v => _vm.OledIdleEnabled = v,
                nameof(MainViewModel.OledIdleEnabled), OledFlyout()),

            // Keep-awake has four modes; the tile is the everyday question of
            // whether the computer may sleep, and the menu is the rest of it.
            "awake" => Tile(e, () => ReadAwake() != 0, v => WriteAwake(v ? 1 : 0),
                nameof(MainViewModel.AwakeModeIndex), AwakeFlyout()),
            "stayActive" => Tile(e, () => _vm.AwakeStayActive, v => _vm.AwakeStayActive = v,
                nameof(MainViewModel.AwakeStayActive)),
            "displaysOff" => Tile(e, () => _vm.DisplaysOff, v => _vm.DisplaysOff = v,
                nameof(MainViewModel.DisplaysOff), DisplaysOffFlyout()),

            // Everything about the taskbar behind one tile: the wide half is the
            // glass, the effect people reach for; the arrow opens the rest.
            "taskbar" => _vm.Windows11TaskbarSupported
                ? Tile(e, () => _vm.TaskbarGlassEnabled, v => _vm.TaskbarGlassEnabled = v,
                    nameof(MainViewModel.TaskbarGlassEnabled), TaskbarFlyout())
                : Tile(e, () => _vm.WindowsTransparency, v => _vm.WindowsTransparency = v,
                    nameof(MainViewModel.WindowsTransparency), TaskbarFlyout()),

            "engine" => Tile(e, () => _vm.EngineRunning, v => _vm.SetEngineRunning(v),
                nameof(MainViewModel.EngineRunning)),

            "identify" => ActionTile(e, () => _vm.Identify(), IdentifyMenu(), split: true),
            "detect" => ActionTile(e, () => _vm.DetectDisplays()),
            "project" => ActionTile(e, () => { _dismiss(); ShellFlyout.OpenProject(); }, ProjectMenu(), split: true),
            "cast" => ActionTile(e, () => { _dismiss(); ShellFlyout.OpenCast(); }),
            "restAll" => _vm.Displays.Any(d => d.IsOled)
                ? ActionTile(e, () =>
                {
                    // Each for its own duration, which is set per display.
                    foreach (DisplayViewModel d in _vm.Displays)
                        if (d.IsOled) d.RequestOledRest();
                    _dismiss();
                })
                : null,
            _ => null,
        };
    }

    // ---- keep awake ----

    // The mode's getter arms a guard against the page's own realisation
    // write-back, so it is read before it is ever set.
    private int ReadAwake() => _vm.AwakeModeIndex;

    private void WriteAwake(int mode)
    {
        _ = _vm.AwakeModeIndex;
        _vm.AwakeModeIndex = mode;
    }

    // ---- the flyouts behind each tile's arrow ----

    /// <summary>
    /// A tile's options: its sliders, switches and choices in one flyout.
    /// </summary>
    /// <remarks>
    /// The pattern the taskbar tile set. A menu can hold a tick but not a
    /// slider, and strength, dimming and delays are settings judged by moving
    /// them, so a menu left the useful half of each feature on another page.
    /// Everything that can be changed from the tile's page and fits a panel is
    /// here; the link at the end is for what does not.
    /// </remarks>
    private Flyout OptionsFlyout(string title, string page, params FrameworkElement?[] rows)
    {
        var body = new StackPanel { Spacing = 2, Width = 300 };
        body.Children.Add(Caption(title));
        foreach (FrameworkElement? row in rows)
            if (row is not null) body.Children.Add(row);

        var more = new HyperlinkButton { Content = $"All {title.ToLowerInvariant()} settings", Margin = new Thickness(0, 6, 0, 0) };
        more.Click += (_, _) => { _dismiss(); App.ShowMainWindow(page); };
        body.Children.Add(more);

        return new Flyout { Content = body, Placement = FlyoutPlacementMode.Bottom };
    }

    /// <summary>One of several, as radio buttons; re-read each time the flyout opens.</summary>
    private FrameworkElement Choices(params (string Text, Func<bool> On, Action Pick)[] options)
    {
        var panel = new StackPanel { Margin = new Thickness(RowInset, 2, 0, 2) };
        string group = "choice" + Guid.NewGuid().ToString("N");
        var buttons = new List<(RadioButton Button, Func<bool> On)>();

        void Sync()
        {
            foreach (var (button, on) in buttons) button.IsChecked = on();
        }

        foreach (var option in options)
        {
            var button = new RadioButton { Content = option.Text, GroupName = group, IsChecked = option.On(), MinHeight = 30 };
            AutomationProperties.SetName(button, option.Text);
            // Click, which only a person raises; Checked fires for Sync too.
            button.Click += (_, _) => { option.Pick(); Sync(); };
            buttons.Add((button, option.On));
            panel.Children.Add(button);
        }

        panel.Loaded += (_, _) => Sync();
        return panel;
    }

    private static HyperlinkButton WindowsLink(string text, string uri) =>
        new() { Content = text, NavigateUri = new Uri(uri), Margin = new Thickness(0, 2, 0, 0) };

    private Flyout UnisonFlyout() => OptionsFlyout("Unison brightness", "displays",
        SliderRow("", "Level", _vm.UnisonLevel, _vm.UnisonMinimum, 100,
            v => _vm.UnisonLevel = v, _vm, nameof(MainViewModel.UnisonLevel), () => _vm.UnisonLevel,
            "QuickUnisonFlyoutLevel", "%", labelled: true),
        _vm.UnisonFollowsWindowsAvailable
            ? SwitchRow("Use Windows brightness",
                "The Windows brightness slider and keys move unison, within each display's calibrated range.",
                () => _vm.UnisonFollowsWindows, v => _vm.UnisonFollowsWindows = v,
                nameof(MainViewModel.UnisonFollowsWindows), "QuickUnisonFlyoutWindows")
            : null);

    private Flyout DarkModeFlyout() => OptionsFlyout("Colours", "settings",
        SwitchRow("Transparency effects",
            "Windows' own transparency: Start, the taskbar and window backgrounds.",
            () => _vm.WindowsTransparency, v => _vm.WindowsTransparency = v,
            nameof(MainViewModel.WindowsTransparency), "QuickDarkTransparency"),
        WindowsLink("Accent colour and more in Windows", "ms-settings:colors"));

    // ---- every option of a feature, as rows ----
    //
    // One builder per feature, used by its section and by its tile's flyout, so
    // the two never offer different things. Everything its page offers that
    // fits a panel is here.

    /// <summary>A row enabled only while a condition holds, re-read when it may change.</summary>
    private FrameworkElement EnabledWhen(FrameworkElement row, Func<bool> enabled, params string[] properties)
    {
        var host = new ContentControl
        {
            Content = row,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsEnabled = enabled(),
        };
        foreach (string property in properties) Watch(_vm, property, () => host.IsEnabled = enabled());
        return host;
    }

    private FrameworkElement?[] NightLightRows(out Slider strength)
    {
        var schedule = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(RowInset, 0, 0, 4),
            Foreground = Res("TextFillColorSecondaryBrush"),
        };
        void Schedule()
        {
            schedule.Visibility = _vm.NightLightScheduled ? Visibility.Visible : Visibility.Collapsed;
            schedule.Text = $"From {_vm.NightLightFrom:hh\\:mm} to {_vm.NightLightTo:hh\\:mm}";
        }
        Schedule();
        Watch(_vm, nameof(MainViewModel.NightLightScheduled), Schedule);

        FrameworkElement strengthRow = SliderRow("", "Strength", _vm.NightLightStrength, 0, 100,
            v => _vm.NightLightStrength = v, _vm, nameof(MainViewModel.NightLightStrength), () => _vm.NightLightStrength,
            "QuickNightLightStrength", "%", null, null, labelled: true, out strength);

        // Per display, the shared strength is not the one in use: each display's
        // own warmth is, in its block.
        Slider shared = strength;
        void Shared()
        {
            bool applies = _vm.QuickNightLightShared || !_vm.NightLightEnabled;
            shared.IsEnabled = applies;
            ToolTipService.SetToolTip(shared, applies
                ? "How warm every display is."
                : "Night light is running per display, so each display's warmth is set in its own block.");
        }
        Shared();
        Watch(_vm, nameof(MainViewModel.PerDisplayWarmth), Shared);
        Watch(_vm, nameof(MainViewModel.NightLightEnabled), Shared);

        return
        [
            strengthRow,
            SwitchRow("Follow Windows' night light",
                "Windows does the warming, on its own schedule, and this switch and Windows' are one.",
                () => _vm.NightLightFollowsWindows, v => _vm.NightLightFollowsWindows = v,
                nameof(MainViewModel.NightLightFollowsWindows), "QuickNightFollow"),
            // Windows has one strength, so there is nothing to set per display
            // while it does the warming.
            EnabledWhen(SwitchRow("Warm each display separately",
                    "Each display gets its own warmth, set in its block.",
                    () => !_vm.NightLightUnison, v => _vm.NightLightUnison = !v,
                    nameof(MainViewModel.NightLightUnison), "QuickNightPerDisplay"),
                () => !_vm.NightLightFollowsWindows, nameof(MainViewModel.NightLightFollowsWindows)),
            SwitchRow("On a schedule", "Comes on and goes off at the times set on the Displays page.",
                () => _vm.NightLightScheduled, v => _vm.NightLightScheduled = v,
                nameof(MainViewModel.NightLightScheduled), "QuickNightSchedule"),
            schedule,
        ];
    }

    private FrameworkElement?[] FocusRows()
    {
        // Typed a whole list at a time; saved when the box is left, not per key.
        var excluded = new TextBox
        {
            Header = "Never dim these apps",
            PlaceholderText = "obs64.exe, vlc.exe",
            Text = _vm.FocusExcludedApps,
            Margin = new Thickness(RowInset, 4, 0, 2),
        };
        AutomationProperties.SetName(excluded, "QuickFocusExcluded");
        excluded.LostFocus += (_, _) => { if (excluded.Text != _vm.FocusExcludedApps) _vm.FocusExcludedApps = excluded.Text; };
        Watch(_vm, nameof(MainViewModel.FocusExcludedApps), () => { if (excluded.FocusState == FocusState.Unfocused) excluded.Text = _vm.FocusExcludedApps; });

        return
        [
            SliderRow("", "Dim others", _vm.FocusDim, 0, 100,
                v => _vm.FocusDim = v, _vm, nameof(MainViewModel.FocusDim), () => _vm.FocusDim,
                "QuickFocusDim", "%", labelled: true),
            SliderRow("", "Wait", _vm.FocusDelay, 0, 2000,
                v => _vm.FocusDelay = v, _vm, nameof(MainViewModel.FocusDelay), () => _vm.FocusDelay,
                "QuickFocusDelay", " ms", labelled: true),
            SliderRow("", "Fade", _vm.FocusFade, 0, 2000,
                v => _vm.FocusFade = v, _vm, nameof(MainViewModel.FocusFade), () => _vm.FocusFade,
                "QuickFocusFade", " ms", labelled: true),
            Caption("Between windows"),
            Choices(
                ("No transition", () => _vm.WindowTransition == 0, () => _vm.WindowTransition = 0),
                ("Fade the brightness", () => _vm.WindowTransition == 1, () => _vm.WindowTransition = 1),
                ("Slide the shape", () => _vm.WindowTransition == 2, () => _vm.WindowTransition = 2)),
            SwitchRow("One clear window per display", "Each display keeps its own window clear, not only the active one.",
                () => _vm.FocusPerMonitor, v => _vm.FocusPerMonitor = v, nameof(MainViewModel.FocusPerMonitor), "QuickFocusPerMonitor"),
            SwitchRow("Dim other monitors", "Displays without the active window are dimmed too.",
                () => _vm.FocusOtherMonitors, v => _vm.FocusOtherMonitors = v, nameof(MainViewModel.FocusOtherMonitors), "QuickFocusOthers"),
            SwitchRow("Follow the mouse", "The window under the pointer is the clear one.",
                () => _vm.FocusFollowMouse, v => _vm.FocusFollowMouse = v, nameof(MainViewModel.FocusFollowMouse), "QuickFocusMouse"),
            SwitchRow("Keep the hovered window clear", "A window under the pointer is never dimmed.",
                () => _vm.FocusKeepHoveredClear, v => _vm.FocusKeepHoveredClear = v, nameof(MainViewModel.FocusKeepHoveredClear), "QuickFocusHover"),
            SwitchRow("Keep the taskbar clear", "The taskbar is never dimmed.",
                () => _vm.FocusKeepTaskbar, v => _vm.FocusKeepTaskbar = v, nameof(MainViewModel.FocusKeepTaskbar), "QuickFocusTaskbar"),
            SwitchRow("Clear new windows first", "A window that has just opened is the clear one.",
                () => _vm.FocusPrioritizeNewWindows, v => _vm.FocusPrioritizeNewWindows = v, nameof(MainViewModel.FocusPrioritizeNewWindows), "QuickFocusNew"),
            SwitchRow("Scale with brightness", "Dim less on a display that is already dim.",
                () => _vm.FocusScaleWithBrightness, v => _vm.FocusScaleWithBrightness = v, nameof(MainViewModel.FocusScaleWithBrightness), "QuickFocusScale"),
            SwitchRow("OLED displays only", "Leave other displays undimmed.",
                () => _vm.FocusOledOnly, v => _vm.FocusOledOnly = v, nameof(MainViewModel.FocusOledOnly), "QuickFocusOled"),
            SwitchRow("Pause for fullscreen", "Nothing is dimmed while a fullscreen app or game runs.",
                () => _vm.FocusPauseFullscreen, v => _vm.FocusPauseFullscreen = v, nameof(MainViewModel.FocusPauseFullscreen), "QuickFocusFullscreen"),
            excluded,
        ];
    }

    private FrameworkElement?[] OledRows()
    {
        FrameworkElement? rest = null;
        if (_vm.Displays.Any(d => d.IsOled))
        {
            var button = new Button { Content = "Rest every OLED display now", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 0) };
            AutomationProperties.SetName(button, "QuickOledRestNow");
            button.Click += (_, _) =>
            {
                foreach (DisplayViewModel d in _vm.Displays)
                    if (d.IsOled) d.RequestOledRest();
                _dismiss();
            };
            rest = button;
        }

        return
        [
            SliderRow("", "Dim to", _vm.OledIdleDim, 0, 100,
                v => _vm.OledIdleDim = v, _vm, nameof(MainViewModel.OledIdleDim), () => _vm.OledIdleDim,
                "QuickOledDim", "%", labelled: true),
            SliderRow("", "After", _vm.OledIdleMinutes, 1, 60,
                v => _vm.OledIdleMinutes = v, _vm, nameof(MainViewModel.OledIdleMinutes), () => _vm.OledIdleMinutes,
                "QuickOledAfter", " min", labelled: true),
            SliderRow("", "Fade", _vm.OledIdleFade, 0, 2000,
                v => _vm.OledIdleFade = v, _vm, nameof(MainViewModel.OledIdleFade), () => _vm.OledIdleFade,
                "QuickOledFade", " ms", labelled: true),
            SwitchRow("Dim further after longer", "A second, deeper stage once the display has been idle a while more.",
                () => _vm.OledSecondStageEnabled, v => _vm.OledSecondStageEnabled = v,
                nameof(MainViewModel.OledSecondStageEnabled), "QuickOledSecondStage"),
            EnabledWhen(SliderRow("", "Then after", _vm.OledSecondStageMinutes, 1, 120,
                    v => _vm.OledSecondStageMinutes = v, _vm, nameof(MainViewModel.OledSecondStageMinutes), () => _vm.OledSecondStageMinutes,
                    "QuickOledSecondAfter", " min", labelled: true),
                () => _vm.OledSecondStageEnabled, nameof(MainViewModel.OledSecondStageEnabled)),
            EnabledWhen(SliderRow("", "Down to", _vm.OledSecondStageDim, 0, 100,
                    v => _vm.OledSecondStageDim = v, _vm, nameof(MainViewModel.OledSecondStageDim), () => _vm.OledSecondStageDim,
                    "QuickOledSecondDim", "%", labelled: true),
                () => _vm.OledSecondStageEnabled, nameof(MainViewModel.OledSecondStageEnabled)),
            SwitchRow("Pause during fullscreen", "No dimming while a fullscreen app or game runs.",
                () => _vm.OledPauseFullscreen, v => _vm.OledPauseFullscreen = v,
                nameof(MainViewModel.OledPauseFullscreen), "QuickOledFullscreen"),
            rest,
        ];
    }

    private Flyout NightLightFlyout() => OptionsFlyout("Night light", "displays",
        [.. NightLightRows(out _), WindowsLink("Night light in Windows", "ms-settings:nightlight")]);

    private Flyout FocusFlyout() => OptionsFlyout("Focus mode", "displays", FocusRows());

    private Flyout OledFlyout() => OptionsFlyout("OLED care", "displays", OledRows());

    private Flyout DisplaysOffFlyout() => OptionsFlyout("Displays off", "displays", DisplaysOffRows(inKeepAwake: false));

    /// <summary>"Turn off displays": how dark, which displays, how soon, what wakes them, and staying awake meanwhile.</summary>
    /// <remarks>Used by its own tile and inside Keep awake's, which is where people look for "walk away".</remarks>
    private FrameworkElement?[] DisplaysOffRows(bool inKeepAwake)
    {
        var shortcut = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(RowInset, 0, 0, 4),
            Foreground = Res("TextFillColorSecondaryBrush"),
            Text = _vm.DisplaysOffShortcut,
        };
        Watch(_vm, nameof(MainViewModel.DisplaysOffShortcut), () => shortcut.Text = _vm.DisplaysOffShortcut);

        Button? now = null;
        if (inKeepAwake)
        {
            now = new Button { Content = "Turn off displays now", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 2) };
            AutomationProperties.SetName(now, "QuickDisplaysOffNow");
            now.Click += (_, _) => { _vm.DisplaysOff = true; _dismiss(); };
        }

        return
        [
            now,
            shortcut,
            SliderRow("", "Darkness", _vm.DisplaysOffLevel, 50, 100,
                v => _vm.DisplaysOffLevel = v, _vm, nameof(MainViewModel.DisplaysOffLevel), () => _vm.DisplaysOffLevel,
                "QuickDisplaysOffLevel", "%", labelled: true),
            ComboRow("Which", _vm.DisplaysOffTargets, () => _vm.DisplaysOffTargetName, v => _vm.DisplaysOffTargetName = v,
                _vm, nameof(MainViewModel.DisplaysOffTargetName), "QuickDisplaysOffTarget"),
            SliderRow("", "After", _vm.DisplaysOffDelay, 0, 30,
                v => _vm.DisplaysOffDelay = v, _vm, nameof(MainViewModel.DisplaysOffDelay), () => _vm.DisplaysOffDelay,
                "QuickDisplaysOffDelay", " s", labelled: true),
            SwitchRow("Wake only by the pointer", "A display comes back when the pointer moves on it; typing leaves it off. Off: any input wakes them all.",
                () => _vm.DisplaysOffWakeOnPointer, v => _vm.DisplaysOffWakeOnPointer = v,
                nameof(MainViewModel.DisplaysOffWakeOnPointer), "QuickDisplaysOffWake"),
            SwitchRow("Hide the pointer", "Parks it in a corner of a display that went off, so no arrow floats on the black.",
                () => _vm.DisplaysOffHidePointer, v => _vm.DisplaysOffHidePointer = v,
                nameof(MainViewModel.DisplaysOffHidePointer), "QuickDisplaysOffPointer"),
            SwitchRow("Lock when they wake", "Whoever brings them back meets the lock screen, so nobody passing can use the computer.",
                () => _vm.DisplaysOffLockOnWake, v => _vm.DisplaysOffLockOnWake = v,
                nameof(MainViewModel.DisplaysOffLockOnWake), "QuickDisplaysOffLock"),
            // The real switches, not copies: in Keep awake's own flyout they are
            // already above, so they are shown only in this tile's.
            inKeepAwake ? null : SwitchRow("Keep awake", "No sleep while you are away. The same switch as Keep awake's.",
                () => ReadAwake() != 0, v => WriteAwake(v ? 1 : 0),
                nameof(MainViewModel.AwakeModeIndex), "QuickDisplaysOffKeepAwake"),
            inKeepAwake ? null : SwitchRow("Stay active", "Teams and other chat apps keep showing you as available. The same switch as Stay active's.",
                () => _vm.AwakeStayActive, v => _vm.AwakeStayActive = v,
                nameof(MainViewModel.AwakeStayActive), "QuickDisplaysOffStayActive"),
        ];
    }

    private Flyout AwakeFlyout()
    {
        (string Text, Func<bool> On, Action Pick) For(string text, int mode, int hours, int minutes) =>
            (text,
             () => ReadAwake() == mode && (mode != 2 || ((int)_vm.AwakeHours == hours && (int)_vm.AwakeMinutes == minutes)),
             () =>
             {
                 if (mode == 2)
                 {
                     // The length first, so the timer starts from the choice
                     // rather than from whatever interval was set before.
                     _vm.AwakeHours = hours;
                     _vm.AwakeMinutes = minutes;
                 }
                 WriteAwake(mode);
             });

        return OptionsFlyout("Keep awake", "displays",
        [
            Choices(
                For("Let the computer sleep", 0, 0, 0),
                For("Keep awake indefinitely", 1, 0, 0),
                For("For 30 minutes", 2, 0, 30),
                For("For 1 hour", 2, 1, 0),
                For("For 2 hours", 2, 2, 0),
                For("For 4 hours", 2, 4, 0)),
            SwitchRow("Stay active", "Screen on, and never Away: a tiny pointer nudge after a minute idle. No timer.",
                () => _vm.AwakeStayActive, v => _vm.AwakeStayActive = v,
                nameof(MainViewModel.AwakeStayActive), "QuickStayActive"),
            SwitchRow("Keep the displays on too", "Otherwise the displays still turn off on Windows' schedule.",
                () => _vm.AwakeKeepDisplaysOn, v => _vm.AwakeKeepDisplaysOn = v,
                nameof(MainViewModel.AwakeKeepDisplaysOn), "QuickAwakeDisplays"),
            Divider(),
            Caption("Turn off displays"),
            .. DisplaysOffRows(inKeepAwake: true),
        ]);
    }

    // ---- taskbar ----

    /// <summary>
    /// The taskbar's settings in one flyout: switches and sliders together.
    /// </summary>
    /// <remarks>
    /// A flyout rather than a menu, because a menu can hold a switch but not a
    /// slider, and tint and opacity are settings you judge by moving them.
    /// </remarks>
    private Flyout TaskbarFlyout()
    {
        var body = new StackPanel { Spacing = 2, Width = 300 };

        body.Children.Add(Caption("Taskbar"));
        body.Children.Add(SwitchRow("Transparency effects",
            "Windows' own transparency: Start, the taskbar and window backgrounds.",
            () => _vm.WindowsTransparency, v => _vm.WindowsTransparency = v, nameof(MainViewModel.WindowsTransparency),
            "QuickTaskbarTransparency"));
        body.Children.Add(SwitchRow("Auto-hide the main taskbar",
            "Windows' own auto-hide. The main taskbar is the one DispCtrl cannot move itself.",
            () => _vm.GlobalAutoHide, v => _vm.GlobalAutoHide = v, nameof(MainViewModel.GlobalAutoHide),
            "QuickTaskbarAutoHide"));

        if (_vm.Windows11TaskbarSupported)
        {
            body.Children.Add(SwitchRow("Glass",
                "DispCtrl's blurred glass behind the taskbar.",
                () => _vm.TaskbarGlassEnabled, v => _vm.TaskbarGlassEnabled = v, nameof(MainViewModel.TaskbarGlassEnabled),
                "QuickTaskbarGlass"));
            body.Children.Add(SliderRow("\uE790", "Glass tint", _vm.TaskbarGlassTint, 0, 100,
                v => _vm.TaskbarGlassTint = v, _vm, nameof(MainViewModel.TaskbarGlassTint), () => _vm.TaskbarGlassTint,
                "QuickTaskbarTint", "%", labelled: true));
            body.Children.Add(SliderRow("\uE7FB", "Corner rounding", _vm.TaskbarGlassRadius, 0, 100,
                v => _vm.TaskbarGlassRadius = v, _vm, nameof(MainViewModel.TaskbarGlassRadius), () => _vm.TaskbarGlassRadius,
                "QuickTaskbarRadius", "", labelled: true));
        }

        // The full range, 0 included, as everywhere else: somebody who wants
        // the bar gone can have it, and bring it back from here.
        body.Children.Add(SliderRow("\uE7C4", "Opacity", _vm.TaskbarOpacity, 0, 100,
            v => _vm.TaskbarOpacity = v, _vm, nameof(MainViewModel.TaskbarOpacity), () => _vm.TaskbarOpacity,
            "QuickTaskbarOpacity", "%", labelled: true));

        var more = new HyperlinkButton { Content = "All taskbar settings", Margin = new Thickness(0, 6, 0, 0) };
        more.Click += (_, _) => { _dismiss(); App.ShowMainWindow("taskbar"); };
        body.Children.Add(more);

        return new Flyout { Content = body, Placement = FlyoutPlacementMode.Bottom };
    }

    // ---- identify and display mode ----

    private MenuFlyout IdentifyMenu()
    {
        var menu = new MenuFlyout();

        var all = new MenuFlyoutItem { Text = "Identify every display", Icon = new FontIcon { Glyph = "\uE7C4" } };
        all.Click += (_, _) => _vm.Identify();
        menu.Items.Add(all);

        foreach (DisplayViewModel d in _vm.Displays)
        {
            DisplayViewModel display = d;
            var one = new MenuFlyoutItem { Text = $"Identify {display.Number}. {display.Name}" };
            one.Click += (_, _) => IdentifyOne(display);
            menu.Items.Add(one);
        }

        menu.Items.Add(new MenuFlyoutSeparator());

        var detect = new MenuFlyoutItem { Text = "Detect displays", Icon = new FontIcon { Glyph = "\uE72C" } };
        ToolTipService.SetToolTip(detect, "Look for displays again, including one connected but switched off.");
        detect.Click += (_, _) => _vm.DetectDisplays();
        menu.Items.Add(detect);

        return menu;
    }

    private static void IdentifyOne(DisplayViewModel display) =>
        DisplayIdentifier.Show([display.Info], TimeSpan.FromSeconds(3));

    /// <summary>The four choices Win+P offers, applied directly.</summary>
    private static readonly (string Text, string Glyph, DesktopArrangement Mode)[] Modes =
    [
        ("Extend", "\uEBC6", DesktopArrangement.Extend),
        ("Duplicate", "\uE8C8", DesktopArrangement.Duplicate),
        ("PC screen only", "\uE7F8", DesktopArrangement.InternalOnly),
        ("Second screen only", "\uE7F4", DesktopArrangement.ExternalOnly),
    ];

    /// <summary>What the desktop is doing now, in Win+P's terms, or null when it cannot be said.</summary>
    private DesktopArrangement? CurrentMode()
    {
        try
        {
            return DisplayRegistry.Topology() switch
            {
                DisplayRegistry.DisplayTopology.Extended => DesktopArrangement.Extend,
                DisplayRegistry.DisplayTopology.Duplicated => DesktopArrangement.Duplicate,
                DisplayRegistry.DisplayTopology.Single => _vm.Displays.Count == 1 && _vm.Displays[0].IsInternalPanel
                    ? DesktopArrangement.InternalOnly
                    : DesktopArrangement.ExternalOnly,
                _ => null,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ApplyMode(DesktopArrangement mode)
    {
        if (!DesktopLayout.Apply(mode))
        {
            _report("Windows refused that display mode.");
            return;
        }

        _report($"Display mode: {Modes.First(m => m.Mode == mode).Text}.");

        // The layout takes a moment to settle; the panel follows it then.
        _ = Task.Delay(1500).ContinueWith(_ => _vm.RefreshIfDisplaysChanged(),
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    private MenuFlyout ProjectMenu()
    {
        var menu = new MenuFlyout();
        var items = new List<(RadioMenuFlyoutItem Item, DesktopArrangement Mode)>();

        foreach (var m in Modes)
        {
            var item = new RadioMenuFlyoutItem { Text = m.Text, GroupName = "project", Icon = new FontIcon { Glyph = m.Glyph } };
            DesktopArrangement mode = m.Mode;
            item.Click += (_, _) => ApplyMode(mode);
            menu.Items.Add(item);
            items.Add((item, mode));
        }

        menu.Items.Add(new MenuFlyoutSeparator());

        var windows = new MenuFlyoutItem { Text = "Windows' Project flyout...", Icon = new FontIcon { Glyph = "\uE8A7" } };
        windows.Click += (_, _) => { _dismiss(); ShellFlyout.OpenProject(); };
        menu.Items.Add(windows);

        var cast = new MenuFlyoutItem { Text = "Cast to a wireless display...", Icon = new FontIcon { Glyph = "\uE7F7" } };
        cast.Click += (_, _) => { _dismiss(); ShellFlyout.OpenCast(); };
        menu.Items.Add(cast);

        menu.Opening += (_, _) =>
        {
            DesktopArrangement? now = CurrentMode();
            foreach (var i in items) i.Item.IsChecked = i.Mode == now;
        };

        return menu;
    }

    // ---- one display's own menu ----

    private MenuFlyout DisplayMenu(DisplayViewModel display)
    {
        var menu = new MenuFlyout();

        var main = new MenuFlyoutItem
        {
            Text = display.IsPrimary ? "This is your main display" : "Make this my main display",
            Icon = new FontIcon { Glyph = "\uE80F" },
            IsEnabled = !display.IsPrimary,
        };
        main.Click += (_, _) => _vm.MakePrimary(display);
        menu.Items.Add(main);

        var identify = new MenuFlyoutItem { Text = "Identify this display", Icon = new FontIcon { Glyph = "\uE7C4" } };
        identify.Click += (_, _) => IdentifyOne(display);
        menu.Items.Add(identify);

        if (display.IsOled)
        {
            var rest = new MenuFlyoutItem { Text = "Rest this display now", Icon = new FontIcon { Glyph = "\uEA14" } };
            rest.Click += (_, _) => { display.RequestOledRest(); _dismiss(); };
            menu.Items.Add(rest);
        }

        menu.Items.Add(new MenuFlyoutSeparator());

        var hide = new MenuFlyoutItem { Text = "Hide from this panel", Icon = new FontIcon { Glyph = "\uED1A" } };
        hide.Click += (_, _) => _vm.SetShowsInQuickPanel(display.Token, false);
        menu.Items.Add(hide);

        menu.Items.Add(Link("Windows display settings...", "ms-settings:display"));
        menu.Items.Add(Page("All settings for this display...", "displays"));
        return menu;
    }

    // ================================================================ tiles somebody made

    private FrameworkElement? CustomTile(string id)
    {
        QuickPanelCustomTile? tile = _panel.Custom(id);
        if (tile is null) return null;

        var entry = new Entry(tile.Id, tile.Label.Length > 0 ? tile.Label : "Untitled",
            QuickPanelCommands.Glyph(tile.Glyph), tile.Hint, true);

        return ActionTile(entry, async () =>
        {
            _report($"{entry.Label}: running...");
            _report(await QuickPanelCommands.RunAsync(tile));
        }, Menu(Page("Edit this tile...", "quickpanel")));
    }

    // ================================================================ rows under a display

    private FrameworkElement? DisplayRow(DisplayViewModel display, string id)
    {
        switch (id)
        {
            case "brightness":
                if (!display.BrightnessSupported) return null;
                return SliderRow(
                    "\uE706", "Brightness", display.BrightnessPercent, 0, 100,
                    v => display.BrightnessPercent = (int)Math.Round(v),
                    display, nameof(DisplayViewModel.BrightnessPercent), () => display.BrightnessPercent,
                    $"Brightness {display.Number}", "%");

            case "dimming":
                return SliderRow(
                    "", "Dimming", display.SoftwareBrightness, display.SoftwareBrightnessMinimum, 100,
                    v => display.SoftwareBrightness = v,
                    display, nameof(DisplayViewModel.SoftwareBrightness), () => display.SoftwareBrightness,
                    $"Software dimming {display.Number}", "%", labelled: true);

            case "warmth":
                // Only where it would do something. In unison, or while Windows
                // does the warming, a per-display value is saved and ignored.
                if (!_vm.PerDisplayWarmth) return null;
                return SliderRow(
                    "", "Warmth", display.NightLightStrength, 0, 100,
                    v => display.NightLightStrength = v,
                    display, nameof(DisplayViewModel.NightLightStrength), () => display.NightLightStrength,
                    $"Warmth {display.Number}", "%", labelled: true);

            case "toggles":
                return Strip(display);

            case "resolution":
                return ComboRow("Resolution", display.Resolutions,
                    () => display.SelectedResolution, v => display.SelectedResolution = v,
                    display, nameof(DisplayViewModel.SelectedResolution), $"Resolution {display.Number}");

            case "refresh":
                return ComboRow("Refresh rate", display.RefreshRates,
                    () => display.SelectedRefreshRate, v => display.SelectedRefreshRate = v,
                    display, nameof(DisplayViewModel.SelectedRefreshRate), $"Refresh rate {display.Number}");

            case "scaling":
                if (display.ScalingVisibility != Visibility.Visible) return null;
                return ComboRow("Scale", display.ScalingOptions,
                    () => display.SelectedScaling, v => display.SelectedScaling = v,
                    display, nameof(DisplayViewModel.SelectedScaling), $"Scale {display.Number}");

            case "orientation":
                return ComboRow("Orientation", display.Orientations,
                    () => display.SelectedOrientation, v => display.SelectedOrientation = v,
                    display, nameof(DisplayViewModel.SelectedOrientation), $"Orientation {display.Number}");

            case "input":
                foreach (MonitorControlViewModel c in display.MonitorControls)
                    if (IsInput(c) && c.Options.Count > 0) return ControlRow(c, display);
                if (!display.IsInternalPanel) Watch(display.MonitorControls, _rebuild);
                return null;

            case "controls":
                return MonitorControls(display);
        }

        return null;
    }

    private static bool IsInput(MonitorControlViewModel c) =>
        c.Name.Contains("input", StringComparison.OrdinalIgnoreCase);

    /// <summary>Everything the monitor lets you change, bar its input.</summary>
    /// <remarks>
    /// A capabilities sweep is about thirty seconds of DDC/CI, so the first
    /// summons after a plug-in may not have the list yet, and says so. The rows
    /// arrive on their own once it has been read: this process stays resident,
    /// and the list is watched.
    /// </remarks>
    private FrameworkElement? MonitorControls(DisplayViewModel display)
    {
        if (display.IsInternalPanel) return null;

        Watch(display.MonitorControls, _rebuild);

        var rows = new StackPanel { Spacing = _m.Spacing };
        foreach (MonitorControlViewModel c in display.MonitorControls)
        {
            if (IsInput(c)) continue;
            FrameworkElement? row = ControlRow(c, display);
            if (row is not null) rows.Children.Add(row);
        }

        if (rows.Children.Count > 0) return rows;
        return display.MonitorControls.Count == 0
            ? Note("Asking the monitor what it lets you change...")
            : null;
    }

    private FrameworkElement? ControlRow(MonitorControlViewModel c, DisplayViewModel display)
    {
        if (c.SliderVisibility == Visibility.Visible)
        {
            return SliderRow("", c.Name, c.Value, 0, c.Maximum,
                v => c.Value = v, c, nameof(MonitorControlViewModel.Value), () => c.Value,
                $"{c.Name} {display.Number}", "", labelled: true);
        }

        if (c.ChoiceVisibility == Visibility.Visible && c.Options.Count > 0)
        {
            return ComboRow(c.Name, c.Options, () => c.Selected, v => c.Selected = v,
                c, nameof(MonitorControlViewModel.Selected), $"{c.Name} {display.Number}");
        }

        return null;
    }

    // ================================================================ a display's strip

    /// <summary>The display's strip of small switches.</summary>
    private FrameworkElement? Strip(DisplayViewModel display)
    {
        var buttons = new List<FrameworkElement>();

        foreach (string id in _panel.Shown(QuickPanelGroup.DisplayTiles))
        {
            Entry? e = QuickPanelCatalog.Find(QuickPanelGroup.DisplayTiles, id);
            FrameworkElement? b = e is null ? null : StripItem(display, e);
            if (b is not null) buttons.Add(b);
        }

        return buttons.Count == 0 ? null : StripOf(buttons);
    }

    private FrameworkElement? StripItem(DisplayViewModel display, Entry e) => e.Id switch
    {
        // The main display's taskbar cannot be moved, but Windows can hide it,
        // so the same switch drives Windows' auto-hide there rather than being
        // left out.
        "hideTaskbar" => display.IsPrimary
            ? StripToggle(e, display, () => display.PrimaryAutoHide, v => display.PrimaryAutoHide = v,
                nameof(DisplayViewModel.PrimaryAutoHide),
                "Hide the taskbar\nOn the main display this is Windows' own auto-hide, which DispCtrl switches for you.")
            : StripToggle(e, display, () => display.HideTaskbar, v => display.HideTaskbar = v,
                nameof(DisplayViewModel.HideTaskbar)),

        "hdr" => display.HdrVisibility == Visibility.Visible
            ? StripToggle(e, display, () => display.HdrEnabled, v => display.HdrEnabled = v, nameof(DisplayViewModel.HdrEnabled))
            : null,
        "vrr" => display.VrrVisibility == Visibility.Visible
            ? StripToggle(e, display, () => display.VrrEnabled, v => display.VrrEnabled = v, nameof(DisplayViewModel.VrrEnabled))
            : null,
        "adaptive" => display.AdaptiveVisibility == Visibility.Visible
            ? StripToggle(e, display, () => display.AdaptiveOn, v => display.AdaptiveOn = v, nameof(DisplayViewModel.AdaptiveOn))
            : null,
        "rotation" => display.RotationVisibility == Visibility.Visible
            ? StripToggle(e, display, () => display.RotationOn, v => display.RotationOn = v, nameof(DisplayViewModel.RotationOn))
            : null,
        "focusDimming" => StripToggle(e, display, () => display.FocusDimming, v => display.FocusDimming = v,
            nameof(DisplayViewModel.FocusDimming)),
        "oledProtection" => display.IsOled
            ? StripToggle(e, display, () => display.OledProtectionEnabled, v => display.OledProtectionEnabled = v,
                nameof(DisplayViewModel.OledProtectionEnabled))
            : null,
        "sleep" => !display.IsInternalPanel
            ? StripToggle(e, display, () => display.MonitorSleepEnabled, v => display.MonitorSleepEnabled = v,
                nameof(DisplayViewModel.MonitorSleepEnabled))
            : null,

        "rest" => display.IsOled
            ? StripAction(e, display, () => { display.RequestOledRest(); _dismiss(); })
            : null,
        "identify" => StripAction(e, display, () => _vm.Identify()),

        // Shown on the main display too, lit and still, so the strip says which
        // one is main rather than going quiet about it.
        "primary" => display.IsPrimary
            ? StripState(e, display, "This is the main display.")
            : StripAction(e, display, () => _vm.MakePrimary(display)),

        _ => null,
    };

    /// <summary>
    /// Rebuilds when a display finishes telling us what it supports.
    /// </summary>
    /// <remarks>
    /// Whether a display has HDR, variable refresh, scaling steps or a
    /// brightness control is read in the background after it is found, and the
    /// first summons after the process starts usually comes before those reads
    /// finish. Built then, the panel left those rows out and never added them.
    /// Only a real change rebuilds - each gate is compared with what it was -
    /// so nothing is torn down under a pointer for a notification that changed
    /// nothing.
    /// </remarks>
    private void FollowLateReadings(DisplayViewModel display)
    {
        foreach ((string property, Func<object> read) in new (string, Func<object>)[]
        {
            (nameof(DisplayViewModel.BrightnessSupported), () => display.BrightnessSupported),
            (nameof(DisplayViewModel.ScalingVisibility), () => display.ScalingVisibility),
            (nameof(DisplayViewModel.HdrVisibility), () => display.HdrVisibility),
            (nameof(DisplayViewModel.VrrVisibility), () => display.VrrVisibility),
            (nameof(DisplayViewModel.AdaptiveVisibility), () => display.AdaptiveVisibility),
            (nameof(DisplayViewModel.RotationVisibility), () => display.RotationVisibility),
            (nameof(DisplayViewModel.IsOled), () => display.IsOled),
        })
        {
            object was = read();
            Watch(display, property, () =>
            {
                object now = read();
                if (Equals(now, was)) return;
                was = now;
                _rebuild();
            });
        }

        // And once everything has been read, for whatever does not announce
        // itself - the monitor's own controls arrive last, some thirty seconds in.
        if (!display.ReadingsReady.IsCompleted)
        {
            bool live = true;
            _release.Add(() => live = false);
            _ = display.ReadingsReady.ContinueWith(
                _ => { if (live) _rebuild(); },
                TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
