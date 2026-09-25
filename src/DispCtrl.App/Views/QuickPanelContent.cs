using DispCtrl.App.ViewModels;
using DispCtrl.Core.Settings;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DispCtrl.App.Views;

/// <summary>
/// Builds the quick panel from the settings that say what it should hold.
/// </summary>
/// <remarks>
/// Split by concern, so a contributor adding something touches one file:
/// <list type="bullet">
/// <item><b>This file</b> - the layout: sections in their order, the headers
/// that fold them away, and the block each display gets.</item>
/// <item><b>QuickPanelContent.Registry.cs</b> - what every id <em>is</em>: which
/// setting a tile switches, what a display's row controls, what a switch in a
/// display's strip does. Adding a tile is one line there and one entry in
/// <see cref="QuickPanelCatalog"/>.</item>
/// <item><b>QuickPanelContent.Blocks.cs</b> - the controls everything is made
/// of, and the bookkeeping that releases their subscriptions.</item>
/// </list>
/// Built in code rather than markup so that a control's value is set before its
/// handler is attached: a two-way XAML binding writes its realisation default
/// back to the source before the real value arrives, and that is how this
/// project once zeroed a brightness and switched a machine-wide setting on.
/// Toggles here listen to <c>Click</c>, which only a person raises.
/// </remarks>
internal sealed partial class QuickPanelContent
{
    private readonly StackPanel _host;
    private readonly MainViewModel _vm;
    private readonly Action _dismiss;
    private readonly Action _rebuild;
    private readonly Action<string> _report;

    private QuickPanelSettings _panel = new();

    /// <summary>How far a section's rows sit in from its header, in DIP.</summary>
    private const double SubIndent = 8;

    /// <summary>The narrowest a quick toggle is allowed to become, in DIP.</summary>
    private const int MinTileWidth = 62;

    public QuickPanelContent(StackPanel host, MainViewModel vm, Action dismiss, Action rebuild, Action<string> report)
    {
        _host = host;
        _vm = vm;
        _dismiss = dismiss;
        _rebuild = rebuild;
        _report = report;
    }

    public void Build(QuickPanelSettings panel)
    {
        Detach();
        _host.Children.Clear();

        _panel = panel;
        _m = MetricsFor(panel.Density);
        _host.Spacing = _m.Spacing;

        // Rows that exist or not depending on state - per-display warmth, the
        // display blocks themselves - are rebuilt when that state moves rather
        // than left stale. Warmth only when the answer flips: night light raises
        // it on every step of its strength slider, and a rebuild mid-drag would
        // pull the slider out from under the pointer.
        bool perDisplay = _vm.PerDisplayWarmth;
        Watch(_vm, nameof(MainViewModel.PerDisplayWarmth), () =>
        {
            if (_vm.PerDisplayWarmth == perDisplay) return;
            perDisplay = _vm.PerDisplayWarmth;
            _rebuild();
        });
        Watch(_vm.Displays, _rebuild);

        if (panel.Simple)
        {
            Simple();
            return;
        }

        bool first = true;
        foreach (string section in panel.Shown(QuickPanelGroup.Sections))
        {
            int before = _host.Children.Count;

            switch (section)
            {
                case "unison": Unison(); break;
                case "tiles": Tiles(Foldable("tiles", SectionHeader("Quick toggles"))); break;
                case "nightLight": NightLight(); break;
                case "displayMode": DisplayMode(); break;
                case "focus": Focus(); break;
                case "oledCare": OledCare(); break;
                case "displays": Displays(); break;
                case "taskbar": Taskbar(); break;
                case "presets": Presets(); break;
                case "windows": WindowsSection(); break;
            }

            // A gap between sections that drew something, none above the first.
            if (_host.Children.Count > before && !first && _host.Children[before] is FrameworkElement top)
                top.Margin = new Thickness(top.Margin.Left, top.Margin.Top + _m.SectionGap, top.Margin.Right, top.Margin.Bottom);

            if (_host.Children.Count > before) first = false;
        }

        if (_host.Children.Count == 0) _host.Children.Add(Note(
            "Nothing is switched on for this panel. Choose what it shows with the gear above."));
    }

    // ================================================================ sections

    private void Unison()
    {
        if (!_vm.SeveralDisplays)
        {
            AloneBrightness();
            return;
        }

        ToggleSwitch on = HeaderSwitch(() => _vm.UnisonBrightness, v => _vm.UnisonBrightness = v,
            nameof(MainViewModel.UnisonBrightness), "QuickUnisonSwitch",
            "Unison brightness: one slider for every display. Switching it on again carries on from where it was left.");
        StackPanel target = Foldable("unison", SectionHeader("Unison brightness"), on);

        target.Children.Add(SliderRow(
            "\uE706", "Unison brightness",
            _vm.UnisonLevel, _vm.UnisonMinimum, 100,
            v => _vm.UnisonLevel = v,
            _vm, nameof(MainViewModel.UnisonLevel), () => _vm.UnisonLevel,
            "QuickUnisonLevel", "%", null, null, false, out Slider row));

        // Idle while unison is off, rather than hidden: the slider is where
        // people look for it, and the switch above it is the way to turn it on.
        void RefreshEnabled() => row.IsEnabled = _vm.UnisonBrightness && !_vm.Calibrating;
        RefreshEnabled();
        Watch(_vm, nameof(MainViewModel.UnisonBrightness), RefreshEnabled);
        Watch(_vm, nameof(MainViewModel.Calibrating), RefreshEnabled);
        row.ContextFlyout = Menu(Page("More unison options...", "displays"));

        var windows = SwitchRow("Use Windows brightness",
            "The Windows brightness slider and keys move unison, within each display's calibrated range. Works with the panel closed.",
            () => _vm.UnisonFollowsWindows, v => _vm.UnisonFollowsWindows = v,
            nameof(MainViewModel.UnisonFollowsWindows), "QuickUnisonWindows");
        var windowsHost = new ContentControl { Content = windows, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsEnabled = _vm.UnisonFollowsWindowsAvailable };
        Watch(_vm, nameof(MainViewModel.UnisonFollowsWindowsAvailable), () => windowsHost.IsEnabled = _vm.UnisonFollowsWindowsAvailable);
        target.Children.Add(windowsHost);
    }

    /// <summary>
    /// The unison section with one display connected: that display's own brightness.
    /// </summary>
    /// <remarks>
    /// A slider that moves every display, with nothing to move together, is
    /// one control too many - and it was the laptop's slider squeezed into a
    /// calibrated range meant for matching another panel. Brightness stays
    /// where people look for it, and unison returns, switch and level as they
    /// were, when a second display arrives: the list changing rebuilds this.
    /// </remarks>
    private void AloneBrightness()
    {
        if (_vm.Displays.FirstOrDefault() is not { } display) return;
        const string note = "One display connected. Unison carries on from where it was left when another is plugged in.";
        StackPanel target = Foldable("unison", SectionHeader("Brightness"));
        // The display's own section below already has it: one slider, not two.
        if (_panel.IsShown(QuickPanelGroup.Sections, "displays") && _panel.Shows(display.Token)
            && _panel.IsShown(QuickPanelGroup.DisplayRows, "brightness"))
        {
            target.Children.Add(Note(note));
            return;
        }
        if (!display.BrightnessSupported)
        {
            // Brightness arrives seconds after start on a DDC/CI monitor.
            Watch(display, nameof(DisplayViewModel.BrightnessSupported), _rebuild);
            target.Children.Add(Note("Waiting for the display to answer."));
            return;
        }
        target.Children.Add(SliderRow(
            "\uE706", display.Name,
            display.BrightnessPercent, 0, 100,
            v => display.BrightnessPercent = (int)Math.Round(v),
            display, nameof(DisplayViewModel.BrightnessPercent), () => display.BrightnessPercent,
            $"Brightness {display.Number}", "%"));
        target.Children.Add(Note(note));
    }

    /// <summary>A switch that sits in a section's header, beside its chevron.</summary>
    private ToggleSwitch HeaderSwitch(Func<bool> read, Action<bool> write, string property, string automationName, string tip)
    {
        var toggle = new ToggleSwitch
        {
            IsOn = read(),
            OnContent = null,
            OffContent = null,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -6, -8, -6),
        };
        AutomationProperties.SetName(toggle, automationName);
        ToolTipService.SetToolTip(toggle, tip);

        // Toggled is raised for a value set from code too, unlike Click.
        bool syncing = false;
        toggle.Toggled += (_, _) => { if (!syncing) write(toggle.IsOn); };
        Watch(_vm, property, () =>
        {
            if (read() == toggle.IsOn) return;
            syncing = true;
            toggle.IsOn = read();
            syncing = false;
        });
        return toggle;
    }

    /// <summary>
    /// Brightness and nothing else: all displays together, then each one.
    /// </summary>
    /// <remarks>
    /// No symbols, headers or folds - every row is a slider under its name.
    /// Nothing here depends on the lists the full panel is built from, so
    /// switching simple mode off brings back the panel exactly as it was.
    /// </remarks>
    private void Simple()
    {
        _host.Spacing = 10;
        _m = _m with { SliderLabels = false }; // the name is already above each slider

        // "All displays" over one display is the same slider twice.
        if (_vm.SeveralDisplays) SimpleUnison();
        SimpleDisplays();
    }

    private void SimpleUnison()
    {
        var all = SimpleSlider("All displays", _vm.UnisonLevel, _vm.UnisonMinimum,
            v => _vm.UnisonLevel = v, _vm, nameof(MainViewModel.UnisonLevel), () => _vm.UnisonLevel, "QuickUnisonLevel",
            HeaderSwitch(() => _vm.UnisonBrightness, v => _vm.UnisonBrightness = v,
                nameof(MainViewModel.UnisonBrightness), "QuickUnisonSwitch",
                "Unison brightness: one slider for every display."), out Slider unison);
        void RefreshEnabled() => unison.IsEnabled = _vm.UnisonBrightness && !_vm.Calibrating;
        RefreshEnabled();
        Watch(_vm, nameof(MainViewModel.UnisonBrightness), RefreshEnabled);
        Watch(_vm, nameof(MainViewModel.Calibrating), RefreshEnabled);
        _host.Children.Add(all);
    }

    private void SimpleDisplays()
    {
        bool any = false;
        foreach (DisplayViewModel display in _vm.Displays)
        {
            if (!_panel.Shows(display.Token)) continue;
            if (!any) { if (_host.Children.Count > 0) _host.Children.Add(Divider()); any = true; }

            if (!display.BrightnessSupported)
            {
                // Brightness arrives seconds after start on a DDC/CI monitor.
                Watch(display, nameof(DisplayViewModel.BrightnessSupported), _rebuild);
                continue;
            }

            _host.Children.Add(SimpleSlider(display.Name, display.BrightnessPercent, 0,
                v => display.BrightnessPercent = (int)Math.Round(v),
                display, nameof(DisplayViewModel.BrightnessPercent), () => display.BrightnessPercent,
                $"Brightness {display.Number}", null, out _));
        }
    }

    /// <summary>
    /// Simple mode's one edge, inside the panel's 12: the title bar's text
    /// starts 16 from the window, and so do each name and each slider's track.
    /// </summary>
    private const double SimpleEdge = 4;

    /// <summary>A name, optionally a switch beside it, and a slider underneath.</summary>
    private FrameworkElement SimpleSlider(string name, double value, double minimum, Action<double> write,
        System.ComponentModel.INotifyPropertyChanged source, string property, Func<double> read,
        string automationName, FrameworkElement? beside, out Slider slider)
    {
        var head = new Grid { MinHeight = 24 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(SimpleEdge, 0, 0, 0),
        });
        if (beside is not null)
        {
            Grid.SetColumn(beside, 1);
            head.Children.Add(beside);
        }

        // The ordinary slider row with no symbol. With nothing to its left, the
        // track starts on the name's edge (the empty column's spacing is the
        // edge), and the value ends under the switch rather than past it.
        FrameworkElement row = SliderRow("", name, value, minimum, 100, write, source, property, read,
            automationName, "%", null, null, false, out slider);
        if (row is Grid grid)
        {
            grid.ColumnDefinitions[0].Width = new GridLength(0);
            grid.ColumnSpacing = SimpleEdge;
            slider.Margin = new Thickness(0, 0, 4, 0);
            if (grid.Children.OfType<TextBlock>().LastOrDefault() is { } percent) percent.Margin = new Thickness(0, 0, SimpleEdge, 0);
        }

        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(head);
        stack.Children.Add(row);
        return stack;
    }

    /// <summary>The four choices Win+P offers, one click each, the current one lit.</summary>
    private void DisplayMode()
    {
        DesktopArrangementButtons();
    }

    private void DesktopArrangementButtons()
    {
        var now = CurrentMode();
        var buttons = new List<FrameworkElement>();

        foreach (var m in Modes)
        {
            var mode = m.Mode;
            var b = new ToggleButton
            {
                IsChecked = now == mode,
                Content = new FontIcon { Glyph = m.Glyph, FontSize = _m.TileGlyph },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = _m.TileHeight,
                Padding = new Thickness(0),
            };
            AutomationProperties.SetName(b, $"QuickMode {m.Text}");
            ToolTipService.SetToolTip(b, m.Text);
            // With one display connected there is nothing to extend to or
            // duplicate on; the lit button still says which mode this is.
            b.IsEnabled = _vm.ArrangementsApply || now == mode;
            b.Click += (_, _) =>
            {
                // Lit by what Windows says afterwards, not by the click: a mode
                // it refuses must not look chosen.
                b.IsChecked = now == mode;
                ApplyMode(mode);
            };
            buttons.Add(Labelled(b, m.Text));
        }

        StackPanel section = Foldable("displayMode", SectionHeader("Display mode"));
        section.Children.Add(Columns(buttons, 4, 6));
        if (!_vm.ArrangementsApply) section.Children.Add(Note("Only one display is connected."));
    }

    /// <summary>Focus mode: its switch in the header, every option beneath.</summary>
    private void Focus()
    {
        ToggleSwitch on = HeaderSwitch(() => _vm.FocusEnabled, v => _vm.FocusEnabled = v,
            nameof(MainViewModel.FocusEnabled), "QuickFocusSwitch",
            "Focus mode: everything but the window in use is dimmed.");
        AddRows(Foldable("focus", SectionHeader("Focus mode"), on), FocusRows());
    }

    /// <summary>OLED care: its switch in the header, both stages and their timing beneath.</summary>
    private void OledCare()
    {
        ToggleSwitch on = HeaderSwitch(() => _vm.OledIdleEnabled, v => _vm.OledIdleEnabled = v,
            nameof(MainViewModel.OledIdleEnabled), "QuickOledSwitch",
            "OLED care: OLED displays dim after a spell of inactivity.");
        AddRows(Foldable("oledCare", SectionHeader("OLED protection"), on), OledRows());
    }

    private static void AddRows(Panel target, IEnumerable<FrameworkElement?> rows)
    {
        foreach (FrameworkElement? row in rows)
            if (row is not null) target.Children.Add(row);
    }

    private void Tiles(Panel target)
    {
        var tiles = new List<FrameworkElement>();
        foreach (string id in _panel.Shown(QuickPanelGroup.Tiles))
        {
            FrameworkElement? tile = id.StartsWith(QuickPanelCustomTile.Prefix, StringComparison.Ordinal)
                ? CustomTile(id)
                : DeskTile(id);
            if (tile is not null) tiles.Add(tile);
        }

        if (tiles.Count == 0) return;

        // No narrower than a tile's label can use. Eight to a row suits a wide
        // panel; at the default width it left each tile a few pixels of label.
        int fit = Math.Max(QuickPanelSettings.MinColumns, (_panel.Width - 24) / MinTileWidth);
        int columns = Math.Clamp(Math.Min(_panel.TileColumns, fit), QuickPanelSettings.MinColumns, QuickPanelSettings.MaxColumns);
        target.Children.Add(Columns(tiles, columns, 6));
    }

    private void NightLight()
    {
        ToggleSwitch on = HeaderSwitch(() => _vm.NightLightEnabled, v => _vm.NightLightEnabled = v,
            nameof(MainViewModel.NightLightEnabled), "QuickNightLightSwitch", "Night light: warmer colours in the evening.");
        AddRows(Foldable("nightLight", SectionHeader("Night light"), on), NightLightRows(out _));
    }

    private void Displays()
    {
        foreach (DisplayViewModel display in _vm.Displays)
        {
            if (!_panel.Shows(display.Token)) continue;

            // A divider above every display, the first included, so where the
            // shared controls end and each display begins is never a matter of
            // reading the rows. Not when a display is the first thing in the panel.
            if (_host.Children.Count > 0) _host.Children.Add(Divider());

            StackPanel body = Foldable("display:" + display.Token, DisplayHeader(display),
                OptionsButton(DisplayMenu(display), $"Options for {display.Name}", $"QuickDisplayOptions {display.Number}"));

            foreach (string id in _panel.Shown(QuickPanelGroup.DisplayRows))
            {
                FrameworkElement? row = DisplayRow(display, id);
                if (row is null) continue;
                if (id == "controls")
                    Foldable("controls:" + display.Token, SectionHeader("Monitor controls"), target: body).Children.Add(row);
                else body.Children.Add(row);
            }

            FollowLateReadings(display);
        }
    }

    private void Taskbar()
    {
        StackPanel body = Foldable("taskbar", SectionHeader("Taskbar"));

        body.Children.Add(SwitchRow("Transparency effects",
            "Windows' own transparency: Start, the taskbar and window backgrounds.",
            () => _vm.WindowsTransparency, v => _vm.WindowsTransparency = v, nameof(MainViewModel.WindowsTransparency),
            "QuickTransparency"));

        body.Children.Add(SwitchRow("Auto-hide the main taskbar",
            "Windows' own auto-hide. The main taskbar is the one DispCtrl cannot move itself.",
            () => _vm.GlobalAutoHide, v => _vm.GlobalAutoHide = v, nameof(MainViewModel.GlobalAutoHide),
            "QuickAutoHide"));

        if (_vm.Windows11TaskbarSupported)
        {
            body.Children.Add(SwitchRow("Taskbar glass",
                "DispCtrl's blurred glass behind the taskbar.",
                () => _vm.TaskbarGlassEnabled, v => _vm.TaskbarGlassEnabled = v, nameof(MainViewModel.TaskbarGlassEnabled),
                "QuickGlass"));

            body.Children.Add(SliderRow("\uE790", "Glass tint", _vm.TaskbarGlassTint, 0, 100,
                v => _vm.TaskbarGlassTint = v, _vm, nameof(MainViewModel.TaskbarGlassTint), () => _vm.TaskbarGlassTint,
                "QuickGlassTint", "%", labelled: true));
        }

        body.Children.Add(SliderRow("\uE7C4", "Taskbar opacity", _vm.TaskbarOpacity, 0, 100,
            v => _vm.TaskbarOpacity = v, _vm, nameof(MainViewModel.TaskbarOpacity), () => _vm.TaskbarOpacity,
            "QuickTaskbarOpacity", "%", labelled: true));
    }

    private void Presets()
    {
        if (!_vm.PresetsEnabled || !_vm.Presets.Names.Any(n => n != PresetsViewModel.NewEntry))
        {
            PresetsPlaceholder();
            return;
        }

        var buttons = new List<FrameworkElement>();
        foreach (string name in _vm.Presets.Names)
        {
            if (name == PresetsViewModel.NewEntry) continue;
            string captured = name;

            var button = new Button
            {
                Content = Labelled("\uE768", captured),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 5, 8, 5),
            };

            ToolTipService.SetToolTip(button, $"Apply {captured}");
            AutomationProperties.SetName(button, $"QuickPreset {captured}");
            button.Click += (_, _) =>
            {
                // Selecting is what loads it; applying without that would apply
                // whatever the Presets page happened to be showing.
                _vm.Presets.Selected = captured;
                _dismiss();
                _ = _vm.Presets.ApplyAsync();
            };

            buttons.Add(button);
        }

        if (buttons.Count == 0) return;

        StackPanel body = Foldable("presets", SectionHeader("Presets"));
        body.Children.Add(Columns(buttons, 2, 4));
    }

    /// <summary>
    /// Where presets will go, drawn but not yet usable.
    /// </summary>
    /// <remarks>
    /// Greyed rather than left out, so the panel's shape does not change the day
    /// presets arrive, and so it is plain that something belongs here.
    /// </remarks>
    private void PresetsPlaceholder()
    {
        StackPanel body = Foldable("presets", SectionHeader("Presets"));

        var slots = new List<FrameworkElement>();
        foreach (string name in new[] { "Evening", "Work", "Movie", "Game" })
        {
            var slot = new Button
            {
                Content = Labelled("\uE768", name),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 5, 8, 5),
                IsEnabled = false,
            };
            slots.Add(slot);
        }

        var grid = Columns(slots, 2, 4);
        grid.Opacity = 0.6;
        ToolTipService.SetToolTip(grid, "Presets are coming: the whole desk saved under a name, applied here in one click.");
        body.Children.Add(grid);
        body.Children.Add(Note("Presets are coming - save the whole desk under a name and apply it here in one click."));
    }

    // ================================================================ folding

    /// <summary>
    /// A header that folds the rows under it away, remembered across openings.
    /// </summary>
    /// <remarks>
    /// Folded in place, not by rebuilding the panel: a rebuild re-measures and
    /// re-places it, and a fold should not make the panel jump.
    /// </remarks>
    private StackPanel Foldable(string key, (Button Header, FontIcon Chevron) head, FrameworkElement? beside = null, Panel? target = null)
    {
        target ??= _host;
        // A little in from the header, so what belongs to a section reads as its
        // own, and a nested section (a monitor's controls) steps in again.
        var body = new StackPanel { Spacing = _m.Spacing, Margin = new Thickness(SubIndent, 0, 0, 0) };

        void Show(bool folded)
        {
            body.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
            head.Chevron.Glyph = folded ? "\uE76C" : "\uE70D";
        }

        Show(_panel.IsCollapsed(key));

        void Toggle(object sender, RoutedEventArgs e)
        {
            bool folded = body.Visibility == Visibility.Visible;
            Show(folded);
            _vm.SetQuickPanelCollapsed(key, folded);
        }
        head.Header.Click += Toggle;

        if (beside is null)
        {
            target.Children.Add(head.Header);
        }
        else
        {
            // The chevron stays last on the row, after whatever sits beside the
            // title, so it lines up with every other header's.
            var row = new Grid { ColumnSpacing = 2 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // Found through the header's content: Parent is still null on an
            // element that has not reached the live tree.
            if (head.Header.Content is Panel owner && owner.Children.Contains(head.Chevron))
            {
                owner.Children.Remove(head.Chevron);
                Button fold = HeaderButton(head.Chevron, "Fold or unfold", AutomationProperties.GetName(head.Header) + " fold");
                fold.HorizontalAlignment = HorizontalAlignment.Right;
                fold.Padding = new Thickness(8, 4, 4, 4);
                fold.Click += Toggle;
                Grid.SetColumn(fold, 2);
                row.Children.Add(fold);
            }
            Grid.SetColumn(beside, 1);
            row.Children.Add(head.Header);
            row.Children.Add(beside);
            target.Children.Add(row);
        }

        target.Children.Add(body);
        return body;
    }

    /// <remarks>
    /// Words alone. A symbol here repeated the one on the first row beneath it:
    /// the unison section showed the same sun three times in four lines.
    /// </remarks>
    private (Button, FontIcon) SectionHeader(string title)
    {
        var chevron = new FontIcon { Glyph = "\uE70D", FontSize = 10, VerticalAlignment = VerticalAlignment.Center };

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(chevron, 1);
        row.Children.Add(text);
        row.Children.Add(chevron);

        return (HeaderButton(row, $"Fold or unfold {title}", $"QuickSection {title}"), chevron);
    }

    /// <remarks>
    /// No coloured number badge: the subscript already says "Monitor 2", and
    /// two markers for one fact made the header busier without saying more.
    /// </remarks>
    private (Button, FontIcon) DisplayHeader(DisplayViewModel display)
    {
        var chevron = new FontIcon { Glyph = "\uE70D", FontSize = 10, VerticalAlignment = VerticalAlignment.Center };

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock
        {
            Text = display.Name,
            FontSize = _m.HeaderSize,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        // The serial is what tells two identical monitors apart, so it is the
        // subscript - with the monitor's number, which is what Identify shows.
        string subscript = Subscript(display);
        if (_m.Subscripts)
        {
            names.Children.Add(new TextBlock
            {
                Text = subscript,
                FontSize = 11,
                Foreground = Res("TextFillColorSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        row.Children.Add(names);

        if (display.IsPrimary)
        {
            var main = new Border
            {
                BorderBrush = Res("CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "Main", FontSize = 11, Foreground = Res("TextFillColorSecondaryBrush") },
            };
            Grid.SetColumn(main, 1);
            row.Children.Add(main);
        }

        Grid.SetColumn(chevron, 2);
        row.Children.Add(chevron);

        Button header = HeaderButton(row,
            $"{display.Name}\n{subscript}\n{display.RoleAndConnection}\nClick to fold or unfold.",
            $"QuickDisplay {display.Number}");
        return (header, chevron);
    }

    /// <summary>"Monitor 2 · SN 9XYZ7K1", or what stands in for a serial when there is none.</summary>
    public static string Subscript(DisplayViewModel display)
    {
        bool hasSerial = display.SerialText.Length > 0 && display.SerialText != "Not reported";
        return hasSerial
            ? $"Monitor {display.Number} · SN {display.SerialText}"
            : $"Monitor {display.Number} · {display.ConnectorLabel} · no serial reported";
    }
}
