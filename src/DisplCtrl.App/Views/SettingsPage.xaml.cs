using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DisplCtrl.App.ViewModels;

namespace DisplCtrl.App.Views;

public sealed partial class SettingsPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public SettingsPage() => InitializeComponent();

    private async void OnResetAll(object sender, RoutedEventArgs e)
    {
        // Wholesale and not obviously undoable, so it asks first — unlike the
        // per-display reset, which affects one card the user is looking at.
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Reset all DisplCtrl settings?",
            Content = "Every monitor returns to DisplCtrl's defaults, including which taskbars are hidden. "
                    + "Your Windows display settings are not touched.",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.ResetEverything();
    }
}
