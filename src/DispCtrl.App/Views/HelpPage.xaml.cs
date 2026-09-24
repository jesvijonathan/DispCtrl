using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using DispCtrl.App.Services;
using DispCtrl.Control;
using System.Text.Json.Nodes;
using Windows.ApplicationModel.DataTransfer;

namespace DispCtrl.App.Views;

public sealed partial class HelpPage : Page
{
    public HelpPage() => InitializeComponent();
    private void OnSource(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.Repository);
    private void OnIssue(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.Issues);
    private void OnPullRequest(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.PullRequest);

    private async void OnReport(object sender, RoutedEventArgs e)
    {
        ReportButton.IsEnabled = false;
        ReportStatus.IsOpen = false;
        try
        {
            var what = new TextBox
            {
                Header = "What happened?", PlaceholderText = "What you expected, and what happened instead.",
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 120, MaxLength = 12000,
            };
            var steps = new TextBox
            {
                Header = "Steps to reproduce (optional)", AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap, Height = 100, MaxLength = 12000,
            };
            var describe = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "Report a problem",
                Content = new StackPanel
                {
                    Spacing = 12, Children =
                    {
                        new TextBlock { Text = "The next step shows your description, display details, relevant settings and recent logs. Nothing is sent until you submit the issue on GitHub.", TextWrapping = TextWrapping.Wrap },
                        what, steps,
                    },
                },
                PrimaryButtonText = "Review report", CloseButtonText = "Cancel", IsPrimaryButtonEnabled = false,
                DefaultButton = ContentDialogButton.None,
            };
            what.TextChanged += (_, _) => describe.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(what.Text);
            if (await describe.ShowAsync() != ContentDialogResult.Primary) return;
            string description = what.Text, reproduction = steps.Text;
            JsonObject result = await Task.Run(() => new ControlService().Execute(new JsonObject
            {
                ["version"] = 1, ["command"] = "report",
                ["args"] = new JsonObject { ["what"] = description, ["steps"] = reproduction },
            }));
            if (result["ok"]?.GetValue<bool>() != true)
                throw new InvalidOperationException(result["error"]?["message"]?.GetValue<string>() ?? "Could not prepare the report.");
            JsonNode data = result["data"]!;
            string body = data["body"]!.GetValue<string>();
            string? paste = data["paste"]?.GetValue<string>();
            var notice = new TextBlock
            {
                Text = paste is null ? "The report fits in the issue link."
                    : "Some text is too long for the link. Opening GitHub will copy it; paste it where the issue asks for it.",
                TextWrapping = TextWrapping.Wrap,
            };
            var preview = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "Review problem report",
                Content = new StackPanel
                {
                    Spacing = 12, Children =
                    {
                        new TextBlock { Text = "Known monitor identifiers and user paths have been removed. Review the text for any other personal details before sharing.", TextWrapping = TextWrapping.Wrap },
                        new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 300, FontSize = 12, Text = body },
                        notice,
                    },
                },
                PrimaryButtonText = "Open GitHub issue", SecondaryButtonText = "Copy report", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.None,
            };
            preview.SecondaryButtonClick += (_, args) =>
            {
                args.Cancel = true;
                try { Copy(body); notice.Text = "Report copied."; }
                catch (Exception ex) { notice.Text = "Could not copy the report: " + ex.Message; }
            };
            preview.PrimaryButtonClick += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    if (paste is not null) Copy(paste);
                    if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(data["url"]!.GetValue<string>())))
                        throw new InvalidOperationException("Windows could not open the browser.");
                    ShowReportStatus(paste is null ? "Opened the prefilled issue. Review it and press Submit on GitHub."
                        : "Opened the issue. Paste the copied text where it asks for it, then press Submit on GitHub.", InfoBarSeverity.Success);
                }
                catch (Exception ex) { args.Cancel = true; notice.Text = ex.Message; }
                finally { deferral.Complete(); }
            };
            await preview.ShowAsync();
        }
        catch (Exception ex) { ShowReportStatus("Could not prepare the report: " + ex.Message, InfoBarSeverity.Error); }
        finally { ReportButton.IsEnabled = true; }
    }

    private static void Copy(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private void ShowReportStatus(string message, InfoBarSeverity severity)
    {
        ReportStatus.Message = message;
        ReportStatus.Severity = severity;
        ReportStatus.IsOpen = true;
    }
}
