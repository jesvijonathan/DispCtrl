using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using DispCtrl.App.ViewModels;
using DispCtrl.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Entry = DispCtrl.Core.Settings.QuickPanelCatalog.Entry;

namespace DispCtrl.App.Views;

/// <summary>
/// The controls the quick panel is made of.
/// </summary>
/// <remarks>
/// Every control here follows the same two rules. Its value is set before its
/// handler is attached, so building it can never be mistaken for a person
/// using it. And every subscription it takes on the view model goes through
/// <see cref="Watch(INotifyPropertyChanged, string, Action)"/>, which records
/// it so <see cref="Detach"/> can release it: the view model outlives the rows,
/// and a handler left behind would keep a whole discarded panel alive.
/// </remarks>
internal sealed partial class QuickPanelContent
{
    /// <summary>What each density actually changes.</summary>
    private sealed record Metrics(
        double TileHeight, double TileGlyph, bool TileLabels, double Spacing, double SectionGap,
        bool SliderLabels, double StripButton, double HeaderSize, bool Subscripts);

    private static Metrics MetricsFor(QuickPanelDensity density) => density switch
    {
        QuickPanelDensity.Compact => new(36, 16, false, 0, 6, false, 30, 13, false),
        QuickPanelDensity.Spacious => new(56, 20, true, 6, 16, true, 38, 15, true),
        _ => new(48, 16, true, 2, 12, false, 34, 14, true),
    };

    private Metrics _m = MetricsFor(QuickPanelDensity.Comfortable);
    private readonly List<Action> _release = [];

    /// <summary>Releases every subscription the current rows hold.</summary>
    public void Detach()
    {
        foreach (Action release in _release) release();
        _release.Clear();
    }

    // ================================================================ rows

    /// <summary>A symbol, a slider and its value.</summary>
    private FrameworkElement SliderRow(
        string glyph, string label, double value, double minimum, double maximum,
        Action<double> write,
        INotifyPropertyChanged source, string property, Func<double> read,
        string automationName, string suffix, bool labelled = false)
        => SliderRow(glyph, label, value, minimum, maximum, write, source, property, read,
            automationName, suffix, null, null, labelled, out _);

    /// <summary>
    /// A symbol, a slider and its value - the symbol optionally a switch, and
    /// something optional at the end.
    /// </summary>
    /// <remarks>
    /// The handler that follows the source back is guarded by a flag, so a
    /// change arriving from the engine cannot be echoed to the hardware as
    /// though somebody had dragged it.
    /// </remarks>
    private FrameworkElement SliderRow(
        string glyph, string label, double value, double minimum, double maximum,
        Action<double> write,
        INotifyPropertyChanged source, string property, Func<double> read,
        string automationName, string suffix,
        (Func<bool> Read, Action<bool> Write, string Property, string OnTip, string OffTip)? toggle,
        FrameworkElement? trailing, bool labelled, out Slider slider)
    {
        // A row named in words puts the words where the symbol would be, so
        // every row starts at the same edge: a symbol, a switch or a name.
        bool inline = labelled && toggle is null;

        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1), ColumnSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = inline ? new GridLength(LabelWidth) : new GridLength(_m.StripButton) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        if (trailing is not null) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FrameworkElement lead;
        if (inline)
        {
            lead = RowLabel(label);
        }
        else if (toggle is { } t)
        {
            ToggleButton b = SmallToggle(glyph, automationName + " switch", t.Read());
            void Tip() => ToolTipService.SetToolTip(b, t.Read() ? t.OnTip : t.OffTip);
            Tip();
            b.Click += (_, _) => { t.Write(b.IsChecked == true); Tip(); };
            Watch(source, t.Property, () => { b.IsChecked = t.Read(); Tip(); });
            lead = b;
        }
        else
        {
            lead = Symbol(glyph, label);
        }

        var text = new TextBlock
        {
            Text = Format(value, suffix),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Res("TextFillColorSecondaryBrush"),
        };

        double max = Math.Max(maximum, minimum + 1);
        slider = new Slider
        {
            Minimum = minimum,
            Maximum = max,
            StepFrequency = 1,
            Value = Math.Clamp(value, minimum, max),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0),
        };

        // Lambdas cannot capture an out parameter, so the rest works on a local.
        Slider bar = slider;
        AutomationProperties.SetName(bar, automationName);
        ToolTipService.SetToolTip(bar, label);

        bool syncing = false;
        bar.ValueChanged += (_, e) =>
        {
            if (syncing) return;
            text.Text = Format(e.NewValue, suffix);
            write(e.NewValue);
        };

        // Only when asked for: a wheel meant to scroll the panel that lands on
        // a slider would otherwise change the brightness under it. A notch is a
        // share of the slider's range, so milliseconds move as far as percent.
        if (_panel.WheelOnSliders)
        {
            bar.PointerWheelChanged += (_, e) =>
            {
                int delta = e.GetCurrentPoint(bar).Properties.MouseWheelDelta;
                if (delta == 0 || !bar.IsEnabled) return;
                double step = (bar.Maximum - bar.Minimum)
                    * Math.Clamp(_panel.WheelStep, QuickPanelSettings.MinWheelStep, QuickPanelSettings.MaxWheelStep) / 100.0;
                bar.Value = Math.Clamp(bar.Value + Math.Max(1, Math.Round(step)) * Math.Sign(delta), bar.Minimum, bar.Maximum);
                e.Handled = true;
            };
        }

        Watch(source, property, () =>
        {
            double now = read();
            if (Math.Abs(now - bar.Value) < 0.5) return;
            syncing = true;
            bar.Value = Math.Clamp(now, minimum, max);
            text.Text = Format(now, suffix);
            syncing = false;
        });

        Grid.SetColumn(lead, 0);
        Grid.SetColumn(bar, 1);
        Grid.SetColumn(text, 2);
        grid.Children.Add(lead);
        grid.Children.Add(bar);
        grid.Children.Add(text);

        if (trailing is not null)
        {
            Grid.SetColumn(trailing, 3);
            grid.Children.Add(trailing);
        }

        // The name above the slider where density leaves room for it.
        if (inline || !_m.SliderLabels) return grid;

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = Res("TextFillColorSecondaryBrush"),
            Margin = new Thickness(_m.StripButton + 8, 2, 0, -4),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(grid);
        return stack;
    }

    private static string Format(double value, string suffix) => $"{(int)Math.Round(value)}{suffix}";

    /// <summary>How wide the name is on a row named in words, in DIP.</summary>
    private const double LabelWidth = 104;

    /// <summary>The name at the start of a row, aligned with every other row's.</summary>
    private static TextBlock RowLabel(string label) => new()
    {
        Text = label,
        FontSize = 13,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(RowInset, 0, 0, 0),
    };

    /// <summary>Where text starts on a row: the headers' padding, so words line up under them.</summary>
    private const double RowInset = 2;

    /// <summary>A name and a switch - the settings-page row, small.</summary>
    private FrameworkElement SwitchRow(string label, string hint,
        Func<bool> read, Action<bool> write, string property, string automationName)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1), ColumnSpacing = 4, MinHeight = 32 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock name = RowLabel(label);

        // No on/off words: the name is beside it already, and the words make
        // every row wider for nothing.
        var toggle = new ToggleSwitch
        {
            IsOn = read(),
            OnContent = null,
            OffContent = null,
            MinWidth = 0,
            Margin = new Thickness(0, -4, -8, -4),
        };
        AutomationProperties.SetName(toggle, automationName);
        ToolTipService.SetToolTip(grid, $"{label}\n{hint}");

        // Toggled is raised for a value set from code too, unlike Click, so the
        // way back from the view model is guarded.
        bool syncing = false;
        toggle.Toggled += (_, _) => { if (!syncing) write(toggle.IsOn); };
        Watch(_vm, property, () =>
        {
            bool now = read();
            if (now == toggle.IsOn) return;
            syncing = true;
            toggle.IsOn = now;
            syncing = false;
        });

        Grid.SetColumn(toggle, 1);
        grid.Children.Add(name);
        grid.Children.Add(toggle);
        return grid;
    }

    /// <summary>A name and a list to choose from.</summary>
    /// <remarks>
    /// Named in words: "2880 x 1800" beside a symbol left people guessing
    /// whether the symbol meant resolution, scale or the monitor itself.
    /// </remarks>
    private FrameworkElement ComboRow(
        string label, ObservableCollection<string> options,
        Func<string?> read, Action<string> write,
        INotifyPropertyChanged source, string property, string automationName)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1), ColumnSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var box = new ComboBox
        {
            ItemsSource = options,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = label,
        };
        AutomationProperties.SetName(box, automationName);
        ToolTipService.SetToolTip(box, label);

        bool syncing = false;

        // SelectedIndex, never SelectedItem: a ComboBox applies its selected item
        // before its source is filled, finds nothing matching, and renders blank.
        void Sync()
        {
            syncing = true;
            string? now = read();
            box.SelectedIndex = now is null ? -1 : options.IndexOf(now);
            syncing = false;
        }

        Sync();
        box.SelectionChanged += (_, _) =>
        {
            if (!syncing && box.SelectedItem is string chosen) write(chosen);
        };

        Watch(source, property, Sync);
        Watch(options, Sync);

        Grid.SetColumn(box, 1);
        grid.Children.Add(RowLabel(label));
        grid.Children.Add(box);
        return grid;
    }

    // ================================================================ tiles

    /// <summary>
    /// A switch in the grid, labelled underneath; split, with a menu, when it has more to offer.
    /// </summary>
    /// <remarks>
    /// Split the way Windows' own quick settings are: the wide part switches,
    /// the narrow part opens the options. A right-click anywhere on the tile
    /// opens them too, because a narrow target is easy to miss.
    /// </remarks>
    private FrameworkElement Tile(Entry e, Func<bool> read, Action<bool> write, string property, FlyoutBase? menu = null)
    {
        var main = new ToggleButton
        {
            IsChecked = read(),
            Content = new FontIcon { Glyph = e.Glyph, FontSize = _m.TileGlyph },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(0),
        };
        AutomationProperties.SetName(main, $"QuickTile {e.Id}");

        void Tip() => ToolTipService.SetToolTip(main,
            $"{e.Label}: {(read() ? "on" : "off")}\n{e.Hint}" + (menu is null ? "" : "\nRight-click for options."));
        Tip();

        if (menu is null)
        {
            main.Height = _m.TileHeight;
            main.Click += (_, _) => { write(main.IsChecked == true); Tip(); };
            Watch(_vm, property, () => { main.IsChecked = read(); Tip(); });
            return Labelled(main, e.Label);
        }

        // A toggle too, only so it wears the same colour as the half beside it.
        var more = new ToggleButton { IsChecked = read() };
        Grid split = Split(e, main, more, menu);

        main.Click += (_, _) => { write(main.IsChecked == true); more.IsChecked = main.IsChecked; Tip(); };
        more.Click += (_, _) => more.IsChecked = main.IsChecked;
        Watch(_vm, property, () => { main.IsChecked = read(); more.IsChecked = main.IsChecked; Tip(); });

        return Labelled(split, e.Label);
    }

    /// <summary>
    /// A tile in two parts: the wide one acts, the narrow one opens the options.
    /// </summary>
    /// <remarks>
    /// Split the way Windows' own quick settings are. A right-click anywhere on
    /// the tile opens the options too, because a narrow target is easy to miss.
    /// </remarks>
    private Grid Split(Entry e, ButtonBase main, ButtonBase more, FlyoutBase menu)
    {
        var split = new Grid { Height = _m.TileHeight, ColumnSpacing = 1 };
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });

        main.HorizontalAlignment = HorizontalAlignment.Stretch;
        main.VerticalAlignment = VerticalAlignment.Stretch;
        main.CornerRadius = new CornerRadius(4, 0, 0, 4);

        more.Content = new FontIcon { Glyph = "\uE70D", FontSize = 8 };
        more.HorizontalAlignment = HorizontalAlignment.Stretch;
        more.VerticalAlignment = VerticalAlignment.Stretch;
        more.Padding = new Thickness(0);
        more.CornerRadius = new CornerRadius(0, 4, 4, 0);
        AutomationProperties.SetName(more, $"QuickTile {e.Id} options");
        ToolTipService.SetToolTip(more, $"{e.Label} options");
        more.Click += (_, _) => menu.ShowAt(split, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Bottom });

        Grid.SetColumn(more, 1);
        split.Children.Add(main);
        split.Children.Add(more);
        split.ContextFlyout = menu;
        return split;
    }

    /// <summary>An action in the grid: it does something rather than holding a state.</summary>
    /// <remarks>
    /// With <paramref name="split"/>, the options get the narrow half of the
    /// tile, as a switch's do; without, they are on a right-click only.
    /// </remarks>
    private FrameworkElement ActionTile(Entry e, Action run, FlyoutBase? menu = null, bool split = false)
    {
        var b = new Button
        {
            Content = new FontIcon { Glyph = e.Glyph, FontSize = _m.TileGlyph },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = _m.TileHeight,
            Padding = new Thickness(0),
        };
        AutomationProperties.SetName(b, $"QuickTile {e.Id}");
        ToolTipService.SetToolTip(b, $"{e.Label}\n{e.Hint}");
        b.Click += (_, _) => run();

        if (menu is not null && split) return Labelled(Split(e, b, new Button(), menu), e.Label);
        if (menu is not null) b.ContextFlyout = menu;
        return Labelled(b, e.Label);
    }

    /// <summary>A small button at the end of a row that opens that row's options.</summary>
    private Button OptionsButton(FlyoutBase menu, string tip, string automationName)
    {
        var b = new Button
        {
            Content = new FontIcon { Glyph = "\uE712", FontSize = 14 },
            Width = _m.StripButton,
            Height = _m.StripButton,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Flyout = menu,
        };
        ToolTipService.SetToolTip(b, tip);
        AutomationProperties.SetName(b, automationName);
        return b;
    }

    private FrameworkElement Labelled(FrameworkElement control, string label)
    {
        if (!_m.TileLabels) return control;

        var stack = new StackPanel { Spacing = 3 };
        stack.Children.Add(control);
        // Two lines rather than one cut short: "Keep a..." says nothing, and
        // Windows' own quick settings wrap their labels the same way.
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextWrapping = TextWrapping.WrapWholeWords,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            LineHeight = 13,
            Foreground = Res("TextFillColorSecondaryBrush"),
        });
        return stack;
    }

    private static StackPanel Labelled(string glyph, string text)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        stack.Children.Add(new FontIcon { Glyph = glyph, FontSize = 13 });
        stack.Children.Add(new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis });
        return stack;
    }

    private ToggleButton SmallToggle(string glyph, string automationName, bool on)
    {
        var b = new ToggleButton
        {
            IsChecked = on,
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Width = _m.StripButton,
            Height = _m.StripButton,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(b, automationName);
        return b;
    }

    // ================================================================ a display's strip

    private ToggleButton StripToggle(Entry e, DisplayViewModel display, Func<bool> read, Action<bool> write,
        string property, string? tip = null)
    {
        ToggleButton b = SmallToggle(e.Glyph, $"Quick {e.Id} {display.Number}", read());
        void Tip() => ToolTipService.SetToolTip(b, $"{tip ?? $"{e.Label}\n{e.Hint}"}\n{(read() ? "On" : "Off")}");
        Tip();

        b.Click += (_, _) => { write(b.IsChecked == true); Tip(); };
        Watch(display, property, () => { b.IsChecked = read(); Tip(); });
        return b;
    }

    private Button StripAction(Entry e, DisplayViewModel display, Action run)
    {
        var b = new Button
        {
            Content = new FontIcon { Glyph = e.Glyph, FontSize = 14 },
            Width = _m.StripButton,
            Height = _m.StripButton,
            Padding = new Thickness(0),
        };
        ToolTipService.SetToolTip(b, $"{e.Label}\n{e.Hint}");
        AutomationProperties.SetName(b, $"Quick {e.Id} {display.Number}");
        b.Click += (_, _) => run();
        return b;
    }

    private ToggleButton StripState(Entry e, DisplayViewModel display, string tip)
    {
        ToggleButton b = SmallToggle(e.Glyph, $"Quick {e.Id} {display.Number}", true);
        b.IsHitTestVisible = false;
        ToolTipService.SetToolTip(b, tip);
        return b;
    }

    /// <summary>
    /// The switches of one display, wrapped to the width of the panel.
    /// </summary>
    /// <remarks>
    /// Wrapped, not scrolled. A strip that scrolled sideways hid everything
    /// past the panel's edge, and nothing said it was there. Rows of a fixed
    /// size wrap where the width runs out, and a divider under each display's
    /// block keeps a second row from reading as the next display's.
    /// </remarks>
    private FrameworkElement StripOf(List<FrameworkElement> buttons)
    {
        // Equal columns across the whole width, as the quick toggles are, rather
        // than fixed squares packed to the left: those left a ragged gap at the
        // end of the row that lined up with nothing above or below it.
        int fit = Math.Max(1, (int)((_panel.Width - 24 + 4) / (_m.StripButton + 4)));
        int columns = Math.Clamp(buttons.Count, 1, fit);
        foreach (FrameworkElement b in buttons)
        {
            b.Width = double.NaN;
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        Grid strip = Columns(buttons, columns, 4);
        strip.Margin = new Thickness(0, 4, 0, 4);
        return strip;
    }

    // ================================================================ menus

    /// <summary>A menu whose checkmarks are read as it opens, never as it was built.</summary>
    private static MenuFlyout Menu(params MenuFlyoutItemBase[] items)
    {
        var menu = new MenuFlyout();
        foreach (MenuFlyoutItemBase item in items) menu.Items.Add(item);

        menu.Opening += (_, _) =>
        {
            foreach (MenuFlyoutItemBase item in menu.Items)
            {
                if (item is RadioMenuFlyoutItem { Tag: Func<bool> chosen } radio) radio.IsChecked = chosen();
                else if (item is ToggleMenuFlyoutItem { Tag: Func<bool> read } check) check.IsChecked = read();
            }
        };

        return menu;
    }

    /// <summary>One of a set of choices in a menu, ticked when it is the current one.</summary>
    private static RadioMenuFlyoutItem Choice(string text, string group, Func<bool> isCurrent, Action choose)
    {
        var item = new RadioMenuFlyoutItem { Text = text, GroupName = group, Tag = isCurrent };
        item.Click += (_, _) => choose();
        return item;
    }

    private static ToggleMenuFlyoutItem Check(string text, Func<bool> read, Action<bool> write)
    {
        var item = new ToggleMenuFlyoutItem { Text = text, Tag = read };
        item.Click += (_, _) => write(item.IsChecked);
        return item;
    }

    /// <summary>A menu item that opens a page of the full window.</summary>
    private MenuFlyoutItem Page(string text, string page)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = "\uE8A7" } };
        item.Click += (_, _) => { _dismiss(); App.ShowMainWindow(page); };
        return item;
    }

    /// <summary>A menu item that opens one of Windows' own settings pages.</summary>
    private MenuFlyoutItem Link(string text, string uri)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = "\uE713" } };
        item.Click += (_, _) => { _dismiss(); _ = Windows.System.Launcher.LaunchUriAsync(new Uri(uri)); };
        return item;
    }

    // ================================================================ small pieces

    private static FontIcon Symbol(string glyph, string label)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(icon, label);
        return icon;
    }

    /// <summary>A header that is a button, so the whole row folds its block.</summary>
    private static Button HeaderButton(UIElement content, string tip, string automationName)
    {
        var b = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 4, 4, 4),
        };
        ToolTipService.SetToolTip(b, tip);
        AutomationProperties.SetName(b, automationName);
        return b;
    }

    /// <summary>Lays items out in equal columns, the way Windows' quick settings do.</summary>
    private static Grid Columns(List<FrameworkElement> items, int columns, double gap)
    {
        var grid = new Grid { ColumnSpacing = gap, RowSpacing = gap, Margin = new Thickness(0, 2, 0, 2) };
        for (int c = 0; c < columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int rows = (items.Count + columns - 1) / columns;
        for (int r = 0; r < rows; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < items.Count; i++)
        {
            Grid.SetRow(items[i], i / columns);
            Grid.SetColumn(items[i], i % columns);
            grid.Children.Add(items[i]);
        }

        return grid;
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = Res("TextFillColorSecondaryBrush"),
        Margin = new Thickness(2, 0, 2, 4),
    };

    private static FrameworkElement Divider() => new Border
    {
        Height = 1,
        Background = Res("DividerStrokeColorDefaultBrush"),
        Margin = new Thickness(-12, 8, -12, 4),
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        Margin = new Thickness(4, 6, 4, 6),
        Foreground = Res("TextFillColorSecondaryBrush"),
    };

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    // ================================================================ subscriptions

    private void Watch(INotifyPropertyChanged source, string property, Action changed)
    {
        void Handler(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == property || string.IsNullOrEmpty(e.PropertyName)) changed();
        }

        source.PropertyChanged += Handler;
        _release.Add(() => source.PropertyChanged -= Handler);
    }

    private void Watch(INotifyCollectionChanged source, Action changed)
    {
        void Handler(object? s, NotifyCollectionChangedEventArgs e) => changed();
        source.CollectionChanged += Handler;
        _release.Add(() => source.CollectionChanged -= Handler);
    }
}
