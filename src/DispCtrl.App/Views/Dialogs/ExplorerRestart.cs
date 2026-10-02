using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DispCtrl.App.Views.Dialogs;

/// <summary>The question asked before Windows Explorer is restarted, wherever the button is.</summary>
internal static class ExplorerRestart
{
    public static async Task<bool> ConfirmAsync(XamlRoot root)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "Restart Windows Explorer?",
            Content = "The taskbar and desktop disappear for a moment and come back, and any open File Explorer windows close. Programs keep running.",
            PrimaryButtonText = "Restart",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
