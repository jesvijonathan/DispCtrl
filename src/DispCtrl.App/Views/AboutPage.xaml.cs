using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using DispCtrl.App.Services;

namespace DispCtrl.App.Views;

public sealed partial class AboutPage : Page
{
    public string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

    public string Runtime => RuntimeInformation.FrameworkDescription;

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
