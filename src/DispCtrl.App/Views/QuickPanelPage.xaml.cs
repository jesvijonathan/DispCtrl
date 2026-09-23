using CommunityToolkit.WinUI.Controls;
using DispCtrl.App.Services;
using DispCtrl.App.ViewModels;
using DispCtrl.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DispCtrl.App.Views;

/// <summary>Chooses what the quick panel shows, in what order, and how it looks.</summary>
/// <remarks>
/// Controls with a value are built in code for the same reason the panel's are:
/// set first, handler second, so building the page can never be mistaken for
/// somebody changing it.
/// </remarks>
public sealed partial class QuickPanelPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public QuickPanelPage() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Windows' own Settings, or dragging the icon out of the ^, can change
        // this while the page is away.
        ViewModel.RefreshQuickPanelPromotion();

        IconHost.Content = Choice(ViewModel.QuickPanelIcons, ViewModel.QuickPanelIconIndex,
            i => ViewModel.QuickPanelIconIndex = i, "QuickPanelIcon");

        // Comfortable is the default, and says so.
        string[] densities = ViewModel.QuickPanelDensities
            .Select(d => d == "Comfortable" ? "Comfortable (default)" : d).ToArray();
        DensityHost.Content = Choice(densities, ViewModel.QuickPanelDensityIndex,
            i => ViewModel.QuickPanelDensityIndex = i, "QuickPanelDensity");

        BuildSlider(WidthHost, ViewModel.QuickPanelWidthMinimum, ViewModel.QuickPanelWidthMaximum, 10,
            ViewModel.QuickPanelWidth, v => ViewModel.QuickPanelWidth = v, () => ViewModel.QuickPanelWidthText,
            "QuickPanelWidth");
        BuildSlider(ColumnsHost, ViewModel.QuickPanelColumnsMinimum, ViewModel.QuickPanelColumnsMaximum, 1,
            ViewModel.QuickPanelColumns, v => ViewModel.QuickPanelColumns = v, () => ViewModel.QuickPanelColumnsText,
            "QuickPanelColumns");

        BuildHeight();
        LoadEditors();
        BuildCustomTiles();
        BuildDisplayChoices();
    }

    /// <summary>Grow-to-fit or a fixed height, and the height when fixed.</summary>
    private void BuildHeight()
    {
        HeightHost.Children.Clear();

        var sliderHost = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        BuildSlider(sliderHost, QuickPanelSettings.MinHeight, QuickPanelSettings.MaxHeight, 20,
            ViewModel.QuickPanel.Height, v => ViewModel.QuickPanelHeight = v, () => ViewModel.QuickPanelHeightText,
            "QuickPanelHeight");
        sliderHost.Visibility = ViewModel.QuickPanel.FixedHeight ? Visibility.Visible : Visibility.Collapsed;

        var mode = Choice(["Grow to fit", "Fixed height"], ViewModel.QuickPanel.FixedHeight ? 1 : 0, i =>
        {
            ViewModel.QuickPanelFixedHeight = i == 1;
            sliderHost.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
        }, "QuickPanelHeightMode");
        mode.MinWidth = 150;

        HeightHost.Children.Add(sliderHost);
        HeightHost.Children.Add(mode);
    }

    private void LoadEditors()
    {
        SectionsEditor.Load(ViewModel, QuickPanelGroup.Sections);
        TilesEditor.Load(ViewModel, QuickPanelGroup.Tiles);
        RowsEditor.Load(ViewModel, QuickPanelGroup.DisplayRows);
        StripEditor.Load(ViewModel, QuickPanelGroup.DisplayTiles);
    }

    private static ComboBox Choice(string[] options, int selected, Action<int> write, string automationName)
    {
        var box = new ComboBox { ItemsSource = options, SelectedIndex = selected, MinWidth = 190 };
        AutomationProperties.SetName(box, automationName);
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) write(box.SelectedIndex); };
        return box;
    }

    private static void BuildSlider(StackPanel host, double min, double max, double step, double value,
        Action<double> write, Func<string> text, string automationName)
    {
        host.Children.Clear();

        var label = new TextBlock
        {
            Text = text(),
            MinWidth = 72,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            StepFrequency = step,
            SnapsTo = Microsoft.UI.Xaml.Controls.Primitives.SliderSnapsTo.StepValues,
            Value = value,
            Width = 220,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(slider, automationName);

        slider.ValueChanged += (_, e) =>
        {
            write(e.NewValue);
            label.Text = text();
        };

        host.Children.Add(label);
        host.Children.Add(slider);
    }

    // ================================================================ your tiles

    private void BuildCustomTiles()
    {
        CustomTilesHost.Children.Clear();

        foreach (QuickPanelCustomTile tile in ViewModel.QuickCustomTiles.ToList())
        {
            var card = new SettingsCard
            {
                Header = tile.Label.Length > 0 ? tile.Label : "Untitled",
                Description = tile.Hint,
                HeaderIcon = new FontIcon { Glyph = QuickPanelCommands.Glyph(tile.Glyph) },
            };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            var run = new Button { Content = "Try it" };
            AutomationProperties.SetName(run, $"QuickCustomRun {tile.Label}");
            run.Click += async (_, _) => card.Description = await QuickPanelCommands.RunAsync(tile);

            var edit = new Button { Content = "Edit" };
            AutomationProperties.SetName(edit, $"QuickCustomEdit {tile.Label}");
            edit.Click += async (_, _) =>
            {
                if (await EditAsync(tile, isNew: false)) { ViewModel.UpdateQuickCustomTile(tile); Refresh(); }
            };

            var remove = new Button { Content = "Delete" };
            AutomationProperties.SetName(remove, $"QuickCustomDelete {tile.Label}");
            remove.Click += (_, _) => { ViewModel.RemoveQuickCustomTile(tile.Id); Refresh(); };

            buttons.Children.Add(run);
            buttons.Children.Add(edit);
            buttons.Children.Add(remove);
            card.Content = buttons;

            CustomTilesHost.Children.Add(card);
        }

        var addCard = new SettingsCard
        {
            Header = "Make a tile",
            Description = ViewModel.QuickCustomTiles.Count == 0
                ? "You have not made any yet."
                : "Add another.",
            HeaderIcon = new FontIcon { Glyph = "\uE710" },
        };

        var add = new Button { Content = "Make a tile", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        AutomationProperties.SetName(add, "QuickCustomAdd");
        add.Click += async (_, _) =>
        {
            QuickPanelCustomTile tile = QuickPanelCustomTile.Create("");
            if (await EditAsync(tile, isNew: true)) { ViewModel.AddQuickCustomTile(tile); Refresh(); }
        };
        addCard.Content = add;
        CustomTilesHost.Children.Add(addCard);
    }

    private void Refresh()
    {
        BuildCustomTiles();
        LoadEditors();
    }

    /// <summary>Symbols offered for a tile, all from the font the panel draws with.</summary>
    private static readonly string[] Symbols =
    [
        "\uE768", "\uE706", "\uE793", "\uF08C", "\uE708", "\uE890", "\uE916", "\uE7F4",
        "\uE9A6", "\uE8CB", "\uE767", "\uE790", "\uE713", "\uE72C", "\uE7E8", "\uE80F",
        "\uE7F7", "\uEBC6", "\uE8A7", "\uE756", "\uE943", "\uE9F5", "\uE734", "\uE81C",
        "\uE8FD", "\uE7C4", "\uF4A5", "\uEA80", "\uE774", "\uE71D",
    ];

    /// <summary>Asks for a tile's name, symbol and what it runs.</summary>
    /// <returns>True when it was saved; the tile is only changed then.</returns>
    private async Task<bool> EditAsync(QuickPanelCustomTile tile, bool isNew)
    {
        var name = new TextBox { Header = "Name", Text = tile.Label, PlaceholderText = "Bright desk" };
        AutomationProperties.SetName(name, "QuickCustomName");

        var symbols = new GridView
        {
            Header = "Symbol",
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 150,
        };
        foreach (string glyph in Symbols)
            symbols.Items.Add(new FontIcon { Glyph = glyph, FontSize = 18, Width = 36, Height = 36 });
        string current = QuickPanelCommands.Glyph(tile.Glyph);
        symbols.SelectedIndex = Math.Max(0, Array.IndexOf(Symbols, current));

        var kind = new RadioButtons { Header = "It runs" };
        kind.Items.Add("A dispctrl command");
        kind.Items.Add("A program, script, document or web address");
        kind.SelectedIndex = tile.Kind == QuickPanelCustomKind.Command ? 0 : 1;

        var target = new TextBox { Text = tile.Target };
        AutomationProperties.SetName(target, "QuickCustomTarget");
        var arguments = new TextBox { Header = "Arguments", Text = tile.Arguments, PlaceholderText = "Optional" };
        var result = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };

        // The field's label and example follow the kind, so the example is
        // always in the language the field expects.
        void Describe()
        {
            bool command = kind.SelectedIndex == 0;
            target.Header = command ? "Command, after dispctrl" : "Program, file or address";
            target.PlaceholderText = command ? "brightness 40 --all" : "ms-settings:display   or   C:\\Tools\\script.cmd";
            arguments.Visibility = command ? Visibility.Collapsed : Visibility.Visible;
        }
        Describe();
        kind.SelectionChanged += (_, _) => Describe();

        QuickPanelCustomTile Draft() => new()
        {
            Id = tile.Id,
            Label = name.Text.Trim(),
            Glyph = symbols.SelectedIndex >= 0 ? Symbols[symbols.SelectedIndex] : "\uE768",
            Kind = kind.SelectedIndex == 0 ? QuickPanelCustomKind.Command : QuickPanelCustomKind.Program,
            Target = target.Text.Trim(),
            Arguments = arguments.Text.Trim(),
        };

        var tryIt = new Button { Content = "Try it" };
        tryIt.Click += async (_, _) => result.Text = await QuickPanelCommands.RunAsync(Draft());

        var body = new StackPanel { Spacing = 12, MinWidth = 420 };
        body.Children.Add(name);
        body.Children.Add(symbols);
        body.Children.Add(kind);
        body.Children.Add(target);
        body.Children.Add(arguments);
        var trial = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        trial.Children.Add(tryIt);
        trial.Children.Add(result);
        body.Children.Add(trial);

        var dialog = new ContentDialog
        {
            Title = isNew ? "Make a tile" : "Edit tile",
            Content = new ScrollViewer { Content = body },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        // A tile with no name and nothing to run is not a tile.
        void Validate() => dialog.IsPrimaryButtonEnabled = name.Text.Trim().Length > 0 && target.Text.Trim().Length > 0;
        Validate();
        name.TextChanged += (_, _) => Validate();
        target.TextChanged += (_, _) => Validate();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;

        QuickPanelCustomTile saved = Draft();
        tile.Label = saved.Label;
        tile.Glyph = saved.Glyph;
        tile.Kind = saved.Kind;
        tile.Target = saved.Target;
        tile.Arguments = saved.Arguments;
        return true;
    }

    // ================================================================ which displays

    /// <remarks>
    /// One card per display, with the serial as what tells it apart: two
    /// identical monitors share a name, never a serial. Rebuilt on every visit,
    /// because the displays attached are whatever they are now.
    /// </remarks>
    private void BuildDisplayChoices()
    {
        DisplayChoices.Children.Clear();

        foreach (DisplayViewModel display in ViewModel.Displays)
        {
            string token = display.Token;

            var toggle = new ToggleSwitch { IsOn = ViewModel.ShowsInQuickPanel(token), OnContent = "Shown", OffContent = "Hidden" };
            AutomationProperties.SetName(toggle, $"QuickPanelDisplay {display.Number}");

            // Attached after IsOn is set, so building the list is not taken for
            // somebody switching every display on.
            toggle.Toggled += (_, _) => ViewModel.SetShowsInQuickPanel(token, toggle.IsOn);

            DisplayChoices.Children.Add(new SettingsCard
            {
                Header = $"{display.Number}.  {display.Name}",
                Description = $"{QuickPanelContent.Subscript(display)} · {display.RoleAndConnection}",
                HeaderIcon = new FontIcon { Glyph = display.IsInternalPanel ? "\uE7F8" : "\uE7F4" },
                Content = toggle,
            });
        }

        if (DisplayChoices.Children.Count == 0)
        {
            DisplayChoices.Children.Add(new SettingsCard
            {
                Header = "No displays have been read yet",
                Description = "They appear here once DispCtrl has found them.",
                HeaderIcon = new FontIcon { Glyph = "\uE7F4" },
            });
        }
    }

    private void OnPreview(object sender, RoutedEventArgs e) => App.SummonPanel();

    private void OnReset(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetQuickPanel();
        OnLoaded(this, e);
    }
}
