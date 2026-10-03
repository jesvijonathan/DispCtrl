using System.Reflection;
using System.Runtime.InteropServices;
using DispCtrl.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DispCtrl.App.Views.Pages;

public sealed partial class AboutPage : Page
{
    public string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

    public string Runtime => RuntimeInformation.FrameworkDescription;

    /// <summary>The installed folder, and how much of it is DispCtrl itself rather than the runtimes it carries.</summary>
    public string SizeOnDisk
    {
        get
        {
            try
            {
                long all = 0, own = 0;
                foreach (FileInfo file in new DirectoryInfo(AppContext.BaseDirectory).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    all += file.Length;
                    if (file.Name.StartsWith("DispCtrl", StringComparison.OrdinalIgnoreCase) || file.Name.StartsWith("dispctrl.", StringComparison.OrdinalIgnoreCase)
                        || file.DirectoryName?.Contains("taskbar-glass", StringComparison.OrdinalIgnoreCase) == true)
                        own += file.Length;
                }
                return $"DispCtrl itself is {own / 1048576.0:N0} MB; with the .NET and Windows App SDK runtimes it carries, {all / 1048576.0:N0} MB.";
            }
            catch (Exception) { return "Could not be measured."; }
        }
    }

    public string OsVersion => $"{Environment.OSVersion.Version} ({RuntimeInformation.OSArchitecture})";

    public AboutPage()
    {
        InitializeComponent();
        // Read from beside the executable: the same file in the installer, the
        // zips and the MSIX, and no resource index to go stale.
        string qr = Path.Combine(AppContext.BaseDirectory, "Assets", "upi-qr.png");
        if (File.Exists(qr)) UpiQr.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(qr));
    }

    private void OnCopyUpi(object sender, RoutedEventArgs e)
    {
        var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
        data.SetText(ProjectLinks.UpiId);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
    }

    private void OnWebsite(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.Website);
    private void OnAuthorGitHub(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.AuthorGitHub);
    private void OnRepository(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.Repository);
    private void OnSponsor(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.Sponsor);
    private void OnPayPal(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.PayPal);
    private void OnSupportPage(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.SupportPage);
    private void OnEmail(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.Email);
}
