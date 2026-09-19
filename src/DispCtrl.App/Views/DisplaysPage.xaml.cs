using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DispCtrl.App.ViewModels;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;
using WinRT.Interop;

namespace DispCtrl.App.Views;

public sealed partial class DisplaysPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public DisplaysPage()
    {
        InitializeComponent();

        // Dark mode is read from Windows rather than stored here, so the switch
        // has to be told when something else moves it - Windows' own Settings,
        // a theme, or its sunset schedule. This is the notification WinUI
        // already raises for exactly that.
        ActualThemeChanged += (_, _) => ViewModel.RaiseTheme();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadArrangement();
        if (ViewModel.PresetsEnabled) ViewModel.Presets.RefreshDrift();
    }

    private void OnOpenWindowsColours(object sender, RoutedEventArgs e) =>
        WindowsTheme.OpenSettings();

    private void OnResetFocus(object sender, RoutedEventArgs e) => ViewModel.ResetFocusSettings();

    private void OnResetOled(object sender, RoutedEventArgs e) => ViewModel.ResetOledSettings();
    private void OnResetAwake(object sender, RoutedEventArgs e) => ViewModel.ResetAwakeSettings();

    /// <remarks>
    /// Tagged rather than read from the DataContext, which is how every other
    /// per-display button on this page finds its display. The two are the same
    /// object here, but one of them stays true if this card is ever moved
    /// inside another template.
    /// </remarks>
    private void OnScreenRest(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DisplayViewModel display)
            display.RequestOledRest();
    }

    private async void OnPresetApply(object sender, RoutedEventArgs e) => await RunPreset(sender);

    private async void OnPresetDiscard(object sender, RoutedEventArgs e) => await RunPreset(sender);

    /// <remarks>
    /// Apply and Discard are the same operation — Discard means "put the desk
    /// back to the preset", which is exactly what applying it does. Two buttons
    /// because the two intentions read differently when something has drifted.
    /// </remarks>
    private static async Task RunPreset(object sender)
    {
        if (sender is not Button button) return;

        // Applying can mean a topology change, which blocks for seconds.
        button.IsEnabled = false;
        try
        {
            await App.ViewModel.Presets.ApplyAsync();
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void OnPresetSave(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        button.IsEnabled = false;
        try
        {
            await ViewModel.Presets.SaveOrCreateAsync();
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    /// <summary>
    /// Feeds the arrangement surface the current display geometry.
    /// </summary>
    /// <remarks>
    /// Pushed rather than bound: the canvas draws to scale from the whole
    /// desktop's bounding box, so it has to be rebuilt as a set rather than
    /// tracking individual items.
    /// </remarks>
    private void LoadArrangement()
    {
        var displays = new List<DisplayInfo>(ViewModel.Displays.Count);
        foreach (DisplayViewModel d in ViewModel.Displays) displays.Add(d.Info);
        ArrangeSurface.Load(displays);
    }

    /// <summary>
    /// Reads every display: the report for this PC, and a record per monitor.
    /// </summary>
    /// <remarks>
    /// Disabled while it runs. The sweep is seconds of DDC/CI traffic per
    /// external panel, and a second press part way through would start a second
    /// conversation on a channel that serves only one.
    /// </remarks>
    private async void OnCollect(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        List<DisplayInfo> unknown = ViewModel.UnknownDisplays();
        if (unknown.Count > 0)
        {
            string names = string.Join("\n", unknown.Select(display => $"• {display.Label} ({display.Key.Model})"));
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = unknown.Count == 1 ? "New monitor model found" : "New monitor models found",
                Content = "These models are not in the repository catalog:\n\n" + names
                    + "\n\nCollect a private diagnostic report and prepare public model records? "
                    + "You can inspect everything before opening the prefilled GitHub issue.",
                PrimaryButtonText = "Collect and review",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }

        button.IsEnabled = false;
        try
        {
            await ViewModel.CollectAsync(unknown);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    /// <summary>
    /// Shows exactly what would be published, and nothing but.
    /// </summary>
    /// <remarks>
    /// The text is shown in full rather than summarised. Someone deciding
    /// whether to publish a record of their hardware is entitled to read the
    /// record, and a dialog saying "device details will be sent" asks them to
    /// take it on trust. It is selectable as well as copyable, so it can be
    /// checked line by line before anything leaves the machine.
    /// <para>
    /// The full local report is offered from here too, because it is the other
    /// half of what was collected — and it is the half that is never published,
    /// carrying serials and device paths that the records above do not.
    /// </para>
    /// </remarks>
    private async void OnViewDetails(object sender, RoutedEventArgs e)
    {
        var body = new TextBlock
        {
            Text = ViewModel.CollectedText,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "What would be sent, in full",
            Content = new ScrollViewer
            {
                Content = body,
                MaxHeight = 460,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
            PrimaryButtonText = "Copy",
            SecondaryButtonText = "Open the full report",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };

        ContentDialogResult result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary) Copy(ViewModel.CollectedText);
        else if (result == ContentDialogResult.Secondary) OpenReport();
    }

    /// <summary>
    /// Opens GitHub with the complete report already on the clipboard when the
    /// report is too large for a prefilled URL.
    /// </summary>
    private async void OnSubmitDetails(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.SubmissionNeedsPaste)
        {
            if (ViewModel.Submit() is { } paste) Copy(paste);
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Paste the full report into GitHub",
            Content = "The complete report is too large for GitHub to accept in an issue link. "
                + "Copy it now, then GitHub will open with the correct title. Click the issue body "
                + "and press Ctrl+V to include the entire report exactly as collected.",
            PrimaryButtonText = "Copy and open GitHub",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        // Copy before opening the browser. This is both more reliable than a
        // post-launch copy and makes the full body explicitly visible in the
        // handoff instead of looking like GitHub received an empty report.
        Copy(ViewModel.CollectedText);
        _ = ViewModel.Submit();
    }

    /// <remarks>
    /// Nothing is written on demand here: the report only exists because
    /// Collect wrote it, and this is only reachable from the dialog Collect
    /// unlocks.
    /// </remarks>
    private static void OpenReport()
    {
        if (!File.Exists(MainViewModel.ReportPath)) return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = MainViewModel.ReportPath,
            UseShellExecute = true,
        });
    }

    private void OnCopyInfo(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DisplayViewModel display)
            Copy(display.InformationText);
    }

    private static void Copy(string text)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    private void OnConnectWireless(object sender, RoutedEventArgs e) =>
        MainViewModel.ConnectWirelessDisplay();

    private void OnOpenWindowsNightLight(object sender, RoutedEventArgs e) =>
        WindowsNightLight.OpenSettings();

    private void OnRescan(object sender, RoutedEventArgs e)
    {
        ViewModel.Refresh();
        LoadArrangement();
    }

    private void OnMakePrimary(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DisplayViewModel display)
            ViewModel.MakePrimary(display);
    }

    /// <summary>
    /// Hands off to Windows' Colour Management.
    /// </summary>
    /// <remarks>
    /// Installing and associating an ICC profile means enumerating installed
    /// profiles, validating them and juggling per-user versus system scope.
    /// Windows already does all of that; a half-built copy could leave a
    /// display with a broken profile, which is worse than one extra click.
    /// </remarks>
    private void OnColorManagement(object sender, RoutedEventArgs e) =>
        ColorProfile.OpenColorManagement();

    private void OnResetDisplay(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DisplayViewModel display)
            display.ResetToDefaults();
    }

    private async void OnCaptureLimits(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            // A DDC/CI capture is a round trip per external monitor. Without
            // this the button invites a second press that would capture the
            // same step twice and skip the other limit.
            button.IsEnabled = false;
            try
            {
                await ViewModel.CaptureLimitsAsync();
            }
            finally
            {
                button.IsEnabled = true;
            }

            return;
        }

        await ViewModel.CaptureLimitsAsync();
    }

    private async void OnToggleGammaRange(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        button.IsEnabled = false;
        try
        {
            await ViewModel.ToggleGammaRangeAsync();
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void OnCaptureWarmth(object sender, RoutedEventArgs e) =>
        ViewModel.CaptureWarmthLimits();

    private void OnCancelWarmth(object sender, RoutedEventArgs e) =>
        ViewModel.CancelWarmthCalibration();

    private void OnRecalibrateWarmth(object sender, RoutedEventArgs e) =>
        ViewModel.BeginWarmthCalibration();

    private void OnCancelCalibration(object sender, RoutedEventArgs e) =>
        ViewModel.CancelCalibration();

    private void OnRecalibrate(object sender, RoutedEventArgs e) =>
        ViewModel.BeginCalibration();

    private async void OnPickWallpaper(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DisplayViewModel display) return;

        // A WinUI 3 picker has no implicit parent window, so it must be told
        // which one to sit over. Without this it throws on an unpackaged app.
        nint handle = WindowNative.GetWindowHandle(App.MainWindow);
        await display.PickWallpaperAsync(handle);
    }
}
