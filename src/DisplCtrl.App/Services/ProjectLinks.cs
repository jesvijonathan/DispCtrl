using Windows.System;

namespace DisplCtrl.App.Services;

internal static class ProjectLinks
{
    public const string Repository = "https://github.com/jesvijonathan/Display-Control";
    public const string Issues = Repository + "/issues/new/choose";
    public const string PullRequest = Repository + "/compare";
    public const string AuthorGitHub = "https://github.com/jesvijonathan";
    public const string Website = "https://www.jesvi.net/";
    public const string Sponsor = "https://github.com/sponsors/jesvijonathan?frequency=one-time";

    public static async void Open(string address)
    {
        try { await Launcher.LaunchUriAsync(new Uri(address)); }
        catch (Exception) { /* A browser action must never bring down the settings app. */ }
    }
}
