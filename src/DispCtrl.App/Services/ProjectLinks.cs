using Windows.System;

namespace DispCtrl.App.Services;

internal static class ProjectLinks
{
    public const string Repository = "https://github.com/jesvijonathan/DispCtrl";
    public const string Issues = Repository + "/issues/new/choose";
    public const string Releases = Repository + "/releases/latest";
    public const string Email = "mailto:jesvi22j@gmail.com";
    public const string PullRequest = Repository + "/compare";
    public const string AuthorGitHub = "https://github.com/jesvijonathan";
    public const string Website = "https://www.jesvi.net/";
    public const string Sponsor = "https://github.com/sponsors/jesvijonathan?frequency=one-time";
    public const string PayPal = "https://paypal.me/jesvijon";
    public const string UpiId = "jesvi22j-2@okaxis";
    /// <summary>The website's support section: every way to give, with UPI offered in India.</summary>
    public const string SupportPage = "https://jesvijonathan.github.io/DispCtrl/#support";

    public static async void Open(string address)
    {
        try { await Launcher.LaunchUriAsync(new Uri(address)); }
        catch (Exception) { /* A browser action must never bring down the settings app. */ }
    }
}
