using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using DisplCtrl.App.ViewModels;
using DisplCtrl.Core.Presets;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DisplCtrl.App.Views;

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

    private async void OnSaveAs(object sender, RoutedEventArgs e)
    {
        string message = await ViewModel.SaveAsAsync(NewName.Text);
        Say(message, message.StartsWith("Saved", StringComparison.Ordinal));

        if (message.StartsWith("Saved", StringComparison.Ordinal)) NewName.Text = "";
    }

    private async void OnDiscard(object sender, RoutedEventArgs e) => Say(await ViewModel.DiscardAsync());

    /// <remarks>
    /// A dialog rather than a text box on the page. Renaming happens once in
    /// the life of a preset, and a permanently visible field for it was taking
    /// up a row that the everyday buttons wanted.
    /// </remarks>
    private async void OnRename(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not { } current || current == PresetsViewModel.NewEntry) return;

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

        string message = ViewModel.Rename(field.Text);
        Say(message, message.StartsWith("Renamed", StringComparison.Ordinal));
    }

    /// <remarks>
    /// Asks first. Deleting is the one action here that destroys something, it
    /// now sits in a menu where a mis-click is easier, and the preset file is
    /// not in the recycle bin afterwards.
    /// </remarks>
    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not { } current || current == PresetsViewModel.NewEntry) return;

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete \u201c{current}\u201d?",
            Content = "The preset file is removed. Nothing on screen changes.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary) Say(ViewModel.Delete());
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = ViewModel.Selected ?? "preset" };
        picker.FileTypeChoices.Add("DisplCtrl preset", [".json"]);

        // WinUI 3 has no ambient parent window, so a picker must be told which
        // window to sit over or it throws outright in an unpackaged app.
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null) return;

        Say(ViewModel.Export(file.Path));
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
        if (ViewModel.SelectedPreset is null) return;
        var editor = new TextBox
        {
            Text = ViewModel.SelectedJson, AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            MinWidth = 460, Height = 420,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "View / edit preset values",
            Content = new StackPanel { Spacing = 8, Children = { new TextBlock
            {
                Text = "Changes update the saved file. Apply restores them to your displays. Use includeGlobal and includeLayout to control shared settings and layout.",
                TextWrapping = TextWrapping.Wrap,
            }, editor, error } },
            PrimaryButtonText = "Save file", CloseButtonText = "Cancel",
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { Say(ViewModel.SaveJson(editor.Text)); }
            catch (Exception ex) { error.Text = ex.Message; args.Cancel = true; }
        };
        await dialog.ShowAsync();
    }

    private async void OnMapDisplays(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPreset is not { } preset) return;
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
            try { Say(ViewModel.MapDisplays(fields.ToDictionary(pair => pair.Key, pair => ((PresetScopeChoice)pair.Value.SelectedItem).Token!))); }
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
