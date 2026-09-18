using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;

namespace DisplCtrl.App.Views;

public sealed partial class AboutPage : Page
{
    public string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

    public string Runtime => RuntimeInformation.FrameworkDescription;

    public string OsVersion => $"{Environment.OSVersion.Version} ({RuntimeInformation.OSArchitecture})";

    public AboutPage() => InitializeComponent();
}
