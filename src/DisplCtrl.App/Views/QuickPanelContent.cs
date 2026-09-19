using System.ComponentModel;
using DisplCtrl.App.ViewModels;
using DisplCtrl.Core.Settings;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DisplCtrl.App.Views;

/// <summary>
/// Builds the quick panel's rows from the settings that say which it should have.
/// </summary>
/// <remarks>
/// In code rather than in markup, and not only because the composition is
/// dynamic. Building a control means its value can be set <em>before</em> its
/// handler is attached, which removes by construction the trap that has cost
/// this project a zeroed brightness, a night light set to 0%, and a
/// machine-wide power setting switched on: a two-way XAML binding writes its
/// realisation default back to the source before the async read has said what
/// the value is, and is indistinguishable from the user acting. There is no
/// <c>_xxxReady</c> gate anywhere in this file because there is nothing to gate.
/// </remarks>
internal static class QuickPanelContent
{
    /// <summary>Icon column width, matching the glyphs used on the pages.</summary>
    private const double IconColumn = 20;

    public static void Build(StackPanel host, MainViewModel vm, QuickPanelSettings panel, Action dismiss)
    {
        host.Children.Clear();
        host.Spacing = panel.Density == QuickPanelDensity.Compact ? 0 : 4;

        bool anything = false;

        // ---- one block per display ----

        if (panel.AnyPerDisplay)
        {
            foreach (DisplayViewModel display in vm.Displays)
            {
                if (!panel.Shows(display.Token)) continue;

                var rows = new List<FrameworkElement>();

                if (panel.ShowBrightness && display.BrightnessSupported)
                {
                    rows.Add(Slider(
                        "\uE706", display.BrightnessPercent, 0, 100,
                        v => display.BrightnessPercent = (int)Math.Round(v),
                        display, nameof(display.BrightnessPercent), () => display.BrightnessPercent,
                        $"Brightness {display.Number}", suffix: "%"));
                }

                if (panel.ShowSoftwareDimming)
                {
                    rows.Add(Slider(
                        "\uE7E8", display.SoftwareBrightness, display.SoftwareBrightnessMinimum, 100,
                        v => display.SoftwareBrightness = v,
                        display, nameof(display.SoftwareBrightness), () => display.SoftwareBrightness,
                        $"Software dimming {display.Number}", suffix: "%"));
                }

                if (panel.ShowPerDisplayWarmth)
                {
                    rows.Add(Slider(
                        "\uE706", display.NightLightStrength, 0, 100,
                        v => display.NightLightStrength = v,
                        display, nameof(display.NightLightStrength), () => display.NightLightStrength,
                        $"Warmth {display.Number}", suffix: "%", glyph: "\uEC8A"));
                }

                if (panel.ShowInputSource)
                {
                    FrameworkElement? input = InputRow(display);
                    if (input is not null) rows.Add(input);
                }

                if (panel.ShowHideTaskbar && display.CanHideTaskbar)
                {
                    rows.Add(Toggle(
                        "\uE71D", "Hide the taskbar", display.HideTaskbar,
                        v => display.HideTaskbar = v,
                        display, nameof(display.HideTaskbar), () => display.HideTaskbar,
                        $"Hide taskbar {display.Number}"));
                }

                if (panel.ShowScreenRest && display.IsOled)
                {
                    int minutes = (int)Math.Round(vm.OledRestMinutes);
                    rows.Add(Action_(
                        "\uE708", $"Rest this screen for {minutes} min",
                        () => { display.RequestOledRest(minutes); dismiss(); },
                        $"Rest display {display.Number}"));
                }

                if (rows.Count == 0) continue;

                anything = true;
                host.Children.Add(Caption(display.Name, panel));
                foreach (FrameworkElement row in rows) host.Children.Add(row);
            }
        }

        // ---- the whole desk ----

        var global = new List<FrameworkElement>();

        if (panel.ShowUnison)
        {
            global.Add(Toggle(
                "\uE7F4", "Unison brightness", vm.UnisonBrightness,
                v => vm.UnisonBrightness = v,
                vm, nameof(vm.UnisonBrightness), () => vm.UnisonBrightness,
                "QuickUnison"));

            global.Add(Slider(
                "\uE706", vm.UnisonLevel, 0, 100,
                v => vm.UnisonLevel = v,
                vm, nameof(vm.UnisonLevel), () => vm.UnisonLevel,
                "QuickUnisonLevel", suffix: "%"));
        }

        if (panel.ShowNightLight)
        {
            global.Add(Toggle(
                "\uEC8A", "Night light", vm.NightLightEnabled,
                v => vm.NightLightEnabled = v,
                vm, nameof(vm.NightLightEnabled), () => vm.NightLightEnabled,
                "QuickNightLight"));

            global.Add(Slider(
                "\uEC8A", vm.NightLightStrength, 0, 100,
                v => vm.NightLightStrength = v,
                vm, nameof(vm.NightLightStrength), () => vm.NightLightStrength,
                "QuickNightLightStrength", suffix: "%"));
        }

        if (panel.ShowDarkMode)
        {
            global.Add(Toggle(
                "\uE708", "Dark mode", vm.DarkMode,
                v => vm.DarkMode = v,
                vm, nameof(vm.DarkMode), () => vm.DarkMode,
                "QuickDarkMode"));
        }

        if (panel.ShowFocusMode)
        {
            global.Add(Toggle(
                "\uE7B3", "Focus mode", vm.FocusEnabled,
                v => vm.FocusEnabled = v,
                vm, nameof(vm.FocusEnabled), () => vm.FocusEnabled,
                "QuickFocusMode"));
        }

        if (panel.ShowOledIdle)
        {
            global.Add(Toggle(
                "\uE708", "OLED idle protection", vm.OledIdleEnabled,
                v => vm.OledIdleEnabled = v,
                vm, nameof(vm.OledIdleEnabled), () => vm.OledIdleEnabled,
                "QuickOledIdle"));
        }

        if (global.Count > 0)
        {
            anything = true;
            host.Children.Add(Caption("Everything", panel));
            foreach (FrameworkElement row in global) host.Children.Add(row);
        }

        // ---- things that are done rather than set ----

        var actions = new List<FrameworkElement>();

        if (panel.ShowIdentify)
        {
            actions.Add(Action_("\uE8AF", "Identify displays",
                () => { vm.Identify(); dismiss(); }, "QuickIdentify"));
        }

        if (panel.ShowArrangement)
        {
            actions.Add(Action_("\uE7F4", "Project to a second screen",
                () => { dismiss(); Services.ShellFlyout.OpenProject(); }, "QuickProject"));
        }

        if (panel.ShowPresets && vm.PresetsEnabled)
        {
            foreach (string name in vm.Presets.Names)
            {
                if (name == PresetsViewModel.NewEntry) continue;

                string captured = name;
                actions.Add(Action_("\uE728", captured, () =>
                {
                    // Selecting is what loads it; applying without that would
                    // apply whatever the Presets page happened to be showing.
                    vm.Presets.Selected = captured;
                    dismiss();
                    _ = vm.Presets.ApplyAsync();
                }, $"QuickPreset {captured}"));
            }
        }

        if (actions.Count > 0)
        {
            anything = true;
            host.Children.Add(Caption("Do", panel));
            foreach (FrameworkElement row in actions) host.Children.Add(row);
        }

        if (!anything) host.Children.Add(Empty());
    }

    // ---- rows ----

    private static TextBlock Caption(string text, QuickPanelSettings panel) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        Margin = new Thickness(4, panel.Density == QuickPanelDensity.Compact ? 8 : 12, 4, 2),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>
    /// An icon, a slider and its value.
    /// </summary>
    /// <remarks>
    /// The value is set before <c>ValueChanged</c> is attached, and the handler
    /// that follows the source back is guarded by a flag, so a change arriving
    /// from the engine cannot be echoed to the hardware as though somebody had
    /// dragged it.
    /// </remarks>
    private static FrameworkElement Slider(
        string icon, double value, double minimum, double maximum,
        Action<double> write,
        INotifyPropertyChanged source, string property, Func<double> read,
        string automationName, string suffix = "", string? glyph = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconColumn + 8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });

        var symbol = new FontIcon
        {
            Glyph = glyph ?? icon,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var text = new TextBlock
        {
            Text = Format(value, suffix),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };

        var slider = new Microsoft.UI.Xaml.Controls.Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            StepFrequency = 1,
            Value = Math.Clamp(value, minimum, maximum),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, automationName);

        bool syncing = false;

        slider.ValueChanged += (_, e) =>
        {
            if (syncing) return;
            text.Text = Format(e.NewValue, suffix);
            write(e.NewValue);
        };

        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != property) return;

            double now = read();
            if (Math.Abs(now - slider.Value) < 0.5) return;

            syncing = true;
            slider.Value = Math.Clamp(now, minimum, maximum);
            text.Text = Format(now, suffix);
            syncing = false;
        };

        Grid.SetColumn(symbol, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(text, 2);
        grid.Children.Add(symbol);
        grid.Children.Add(slider);
        grid.Children.Add(text);

        return grid;
    }

    private static string Format(double value, string suffix) =>
        $"{(int)Math.Round(value)}{suffix}";

    private static FrameworkElement Toggle(
        string icon, string label, bool value,
        Action<bool> write,
        INotifyPropertyChanged source, string property, Func<bool> read,
        string automationName)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconColumn + 8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var symbol = new FontIcon
        {
            Glyph = icon,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        // No on/off header: the label is to its left already, and the two
        // words underneath make every row a line taller for nothing.
        var toggle = new ToggleSwitch
        {
            IsOn = value,
            OnContent = null,
            OffContent = null,
            MinWidth = 0,
            Margin = new Thickness(0, -4, -8, -4),
        };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, automationName);

        bool syncing = false;

        toggle.Toggled += (_, _) =>
        {
            if (syncing) return;
            write(toggle.IsOn);
        };

        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != property) return;

            bool now = read();
            if (now == toggle.IsOn) return;

            syncing = true;
            toggle.IsOn = now;
            syncing = false;
        };

        Grid.SetColumn(symbol, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(toggle, 2);
        grid.Children.Add(symbol);
        grid.Children.Add(text);
        grid.Children.Add(toggle);

        return grid;
    }

    /// <summary>A row that does something rather than holding a value.</summary>
    private static FrameworkElement Action_(string icon, string label, Action run, string automationName)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new FontIcon { Glyph = icon, FontSize = 15 });
        content.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var button = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 6, 8, 6),
            Margin = new Thickness(0, 1, 0, 1),
        };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => run();

        return button;
    }

    /// <summary>
    /// The monitor's own input control, when it has one and has been asked.
    /// </summary>
    /// <remarks>
    /// A capabilities sweep is about thirty seconds of DDC/CI, so the first
    /// summons after a plug-in will not have one yet and says so rather than
    /// showing an empty box. It is worth asking at all only because this
    /// process stays resident: the answer is still there on the next summons.
    /// </remarks>
    private static FrameworkElement? InputRow(DisplayViewModel display)
    {
        foreach (MonitorControlViewModel control in display.MonitorControls)
        {
            if (control.Options.Count == 0) continue;
            if (!control.Name.Contains("input", StringComparison.OrdinalIgnoreCase)) continue;

            var box = new ComboBox
            {
                ItemsSource = control.Options,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 2, 0, 2),
            };

            // SelectedIndex, never SelectedItem: a ComboBox applies its selected
            // item before its ItemsSource is filled, finds nothing matching, and
            // renders blank.
            int index = control.Selected is null ? -1 : control.Options.IndexOf(control.Selected);
            box.SelectedIndex = index;

            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, $"Input {display.Number}");

            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is string chosen) control.Selected = chosen;
            };

            return box;
        }

        return null;
    }

    private static FrameworkElement Empty() => new TextBlock
    {
        Text = "Nothing is turned on for this panel yet. "
             + "Choose what it shows under Quick panel in DisplCtrl.",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4, 12, 4, 12),
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };
}
