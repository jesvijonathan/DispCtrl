using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using DisplCtrl.App.Services;

namespace DisplCtrl.App.Views;

public sealed partial class HelpPage : Page
{
    public HelpPage() => InitializeComponent();
    private void OnSource(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.Repository);
    private void OnIssue(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.Issues);
    private void OnPullRequest(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.PullRequest);
}
