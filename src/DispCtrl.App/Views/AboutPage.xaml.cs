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

    public AboutPage() => InitializeComponent();

    private void OnWebsite(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.Website);
    private void OnAuthorGitHub(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.AuthorGitHub);
    private void OnRepository(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.Repository);
    private void OnSponsor(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.Sponsor);
}
