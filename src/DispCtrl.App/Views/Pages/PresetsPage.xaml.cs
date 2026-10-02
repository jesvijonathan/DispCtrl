using DispCtrl.App.ViewModels;
using DispCtrl.Core.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace DispCtrl.App.Views.Pages;

public sealed partial class PresetsPage : Page
{
    public PresetsViewModel ViewModel => App.ViewModel.Presets;

    public PresetsPage() => InitializeComponent();

    /// <remarks>
    /// The drift check is a hardware read per display, DDC/CI included, so it
    /// runs when the page is opened and after an action — never on a timer.
    /// </remarks>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Reload();
    }

    private void Say(string message, bool ok = true)
    {
        Toast.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        Toast.Message = message;
        Toast.IsOpen = true;
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        // Applying can mean a topology change, which blocks for seconds. Without
        // disabling the button that reads as the app having hung, and invites a
        // second press part-way through the first.
        button.IsEnabled = false;
        try
        {
            Say(await ViewModel.ApplyAsync(), ViewModel.LastOperationOk);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void OnSave(object sender, RoutedEventArgs e) => Say(await ViewModel.SaveAsync(), ViewModel.LastOperationOk);

    private static PresetRow? Row(object sender) => (sender as FrameworkElement)?.Tag as PresetRow;

    private static readonly Dictionary<PresetPart, string> PartHints = new()
    {
        [PresetPart.Layout] = "Arrangement, main display, resolution, refresh rate, scale, orientation, HDR",
        [PresetPart.Brightness] = "Each display's brightness, software dimming, unison and its limits",
        [PresetPart.NightLight] = "Night light, its schedule, each display's warmth",
        [PresetPart.Wallpaper] = "Each display's wallpaper and how it fits",
        [PresetPart.Controls] = "The monitor's own controls: contrast, input, picture mode",
        [PresetPart.Taskbar] = "Which taskbars hide, the work area, reveal timing",
        [PresetPart.Windows] = "Where the open windows were",
    };

    /// <summary>A tick per part, ticked where the preset restores it; and a way to read the answer back.</summary>
    private static (StackPanel Panel, Func<List<PresetPart>> Skipped) PartBoxes(IReadOnlyCollection<PresetPart> skipped)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock
        {
            Text = "What it restores. Anything unticked is left as it is when the preset applies, and is never counted as a change.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        });
        var boxes = new Dictionary<PresetPart, CheckBox>();
        foreach (PresetPart part in PresetParts.All)
        {
            string label = PresetParts.Label(part);
            var box = new CheckBox { Content = char.ToUpperInvariant(label[0]) + label[1..], IsChecked = !skipped.Contains(part) };
            ToolTipService.SetToolTip(box, PartHints[part]);
            AutomationProperties.SetName(box, "PresetPart " + part);
            boxes[part] = box;
            panel.Children.Add(box);
        }
        return (panel, () => boxes.Where(p => p.Value.IsChecked != true).Select(p => p.Key).ToList());
    }

    private async void OnPartsRow(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is not { } row) return;
        var (panel, skipped) = PartBoxes(row.Preset.Skip);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"What “{row.Name}” restores",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) Say(ViewModel.SetSkip(row.Name, skipped()));
    }

    /// <summary>Applying a preset makes it the one in use, so it is selected first.</summary>
    private async void OnApplyRow(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is not { } row || sender is not Button button) return;
        ViewModel.Selected = row.Name;
        button.IsEnabled = false;
        try { Say(await ViewModel.ApplyAsync(), ViewModel.LastOperationOk); }
        finally { button.IsEnabled = true; }
    }

    private async void OnSaveRow(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is not { } row) return;
        ViewModel.Selected = row.Name;
        Say(await ViewModel.SaveAsync(), ViewModel.LastOperationOk);
    }

    private void OnDeskProfileRow(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is not { } row || sender is not ToggleMenuFlyoutItem item) return;
        Say(ViewModel.SetDeskProfile(row.Name, item.IsChecked));
    }

    /// <remarks>
    /// A dialog, so the page is the list and nothing else: a name box and a
    /// scope choice sat on it permanently for something done now and then.
    /// </remarks>
    private async void OnNewPreset(object sender, RoutedEventArgs e)
    {
        var name = new TextBox { Header = "Name", PlaceholderText = "Work, evening, gaming…" };
        AutomationProperties.SetName(name, "NewPresetName");
        var scope = new ComboBox
        {
            Header = "What it holds", ItemsSource = ViewModel.CaptureScopes, DisplayMemberPath = "Label",
            SelectedItem = ViewModel.CaptureScope, HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(scope, "PresetCaptureScope");
        var (parts, skipped) = PartBoxes([]);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "New preset",
            Content = new StackPanel
            {
                Spacing = 12, MinWidth = 380,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Saves the displays as they are now. The whole desk includes the layout, night light, unison and the taskbar; one display keeps to that display's own settings.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    name, scope, parts, error,
                },
            },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var wait = args.GetDeferral();
            try
            {
                ViewModel.CaptureScope = scope.SelectedItem as PresetScopeChoice ?? ViewModel.CaptureScope;
                ViewModel.NewSkip = skipped();
                string message = await ViewModel.SaveAsAsync(name.Text);
                if (message.StartsWith("Saved", StringComparison.Ordinal)) Say(message);
                else { error.Text = message; args.Cancel = true; }
            }
            finally { wait.Complete(); }
        };
        await dialog.ShowAsync();
    }

    private async void OnDiscard(object sender, RoutedEventArgs e) => Say(await ViewModel.DiscardAsync());

    /// <remarks>
    /// A dialog rather than a text box on the page. Renaming happens once in
    /// the life of a preset, and a permanently visible field for it was taking
    /// up a row that the everyday buttons wanted.
    /// </remarks>
    private async void OnRename(object sender, RoutedEventArgs e)
    {
        if (Row(sender)?.Name is not { } current) return;

        var field = new TextBox
        {
            Text = current,
            PlaceholderText = "New name",
            SelectionStart = current.Length,
        };
        AutomationProperties.SetName(field, "RenamePresetTo");

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Rename \u201c{current}\u201d",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    field,
                    new TextBlock
                    {
                        Text = "The name is the file name, so this renames the file too.",
                        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        string message = ViewModel.On(current, () => ViewModel.Rename(field.Text));
        Say(message, message.StartsWith("Renamed", StringComparison.Ordinal));
    }

    /// <remarks>
    /// Asks first. Deleting is the one action here that destroys something, it
    /// now sits in a menu where a mis-click is easier, and the preset file is
    /// not in the recycle bin afterwards.
    /// </remarks>
    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Row(sender)?.Name is not { } current) return;

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete \u201c{current}\u201d?",
            Content = "The preset file is removed. Nothing on screen changes.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary) Say(ViewModel.On(current, ViewModel.Delete));
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (Row(sender)?.Name is not { } current) return;
        var picker = new FileSavePicker { SuggestedFileName = current };
        picker.FileTypeChoices.Add("DispCtrl preset", [".json"]);

        // WinUI 3 has no ambient parent window, so a picker must be told which
        // window to sit over or it throws outright in an unpackaged app.
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null) return;

        Say(ViewModel.On(current, () => ViewModel.Export(file.Path)));
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null) return;

        string message = ViewModel.Import(file.Path);
        Say(message, message.StartsWith("Imported", StringComparison.Ordinal));
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(PresetsViewModel.Folder);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = PresetsViewModel.Folder,
            UseShellExecute = true,
        });
    }

    private async void OnEditJson(object sender, RoutedEventArgs e)
    {
        if (Row(sender)?.Name is not { } current) return;
        var editor = new TextBox
        {
            Text = ViewModel.JsonOf(current), AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            MinWidth = 460, Height = 420,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = $"\u201c{current}\u201d as JSON",
            Content = new StackPanel { Spacing = 8, Children = { new TextBlock
            {
                Text = "Changes update the saved file. Apply restores them to your displays. Use includeGlobal and includeLayout to control shared settings and layout.",
                TextWrapping = TextWrapping.Wrap,
            }, editor, error } },
            PrimaryButtonText = "Save file", CloseButtonText = "Cancel",
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { Say(ViewModel.On(current, () => ViewModel.SaveJson(editor.Text))); }
            catch (Exception ex) { error.Text = ex.Message; args.Cancel = true; }
        };
        await dialog.ShowAsync();
    }

    private async void OnMapDisplays(object sender, RoutedEventArgs e)
    {
        if (Row(sender)?.Name is not { } current || ViewModel.Find(current) is not { } preset) return;
        var fields = new Dictionary<string, ComboBox>();
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Choose the local display for each saved monitor. Values stay unchanged; unsupported settings are reported when applied.", TextWrapping = TextWrapping.Wrap });
        foreach (var (token, state) in preset.Monitors)
        {
            var choices = new List<PresetScopeChoice> { new(token, "Keep saved identity") };
            choices.AddRange(ViewModel.AvailableDisplays.Where(d => d.Token != token).Select(d => new PresetScopeChoice(d.Token, d.Label)));
            var field = new ComboBox { Header = state.Label ?? token, ItemsSource = choices,
                DisplayMemberPath = "Label", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            fields[token] = field;
            panel.Children.Add(field);
        }
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Map saved displays", Content = new ScrollViewer { Content = panel, MaxHeight = 440 },
            PrimaryButtonText = "Save mapping", CloseButtonText = "Cancel" };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { Say(ViewModel.On(current, () => ViewModel.MapDisplays(fields.ToDictionary(pair => pair.Key, pair => ((PresetScopeChoice)pair.Value.SelectedItem).Token!)))); }
            catch (Exception ex) { error.Text = ex.Message; args.Cancel = true; }
        };
        await dialog.ShowAsync();
    }

    private void OnClearReturn(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AppRuleViewModel rule) rule.RevertTo = "";
    }

    private void OnAddRule(object sender, RoutedEventArgs e) => ViewModel.AddRule();

    private void OnRemoveRule(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AppRuleViewModel rule)
            ViewModel.RemoveRule(rule);
    }
}
