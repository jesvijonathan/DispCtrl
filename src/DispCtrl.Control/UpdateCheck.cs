using System.Text.Json;
using DispCtrl.Core;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

namespace DispCtrl.Control;

/// <summary>What a check for a new version found.</summary>
/// <param name="Current">This build's version.</param>
/// <param name="Latest">The newest release, as 0.1.6; empty when the check could not tell.</param>
/// <param name="Url">Its release page, always on the project's own repository.</param>
/// <param name="Newer">Whether it is newer than this build.</param>
/// <param name="Store">A Microsoft Store install, which the Store updates: nothing was asked.</param>
public sealed record UpdateResult(string Current, string Latest, string Url, bool Newer, bool Store);

/// <summary>
/// Asks GitHub for the latest release, when somebody has asked DispCtrl to.
/// </summary>
/// <remarks>
/// One anonymous GET of the latest release's tag - no identifier, no settings,
/// nothing about the desk - and nothing is downloaded: a newer release is
/// announced with a link to its page. Prereleases are never "latest" on
/// GitHub, so a beta is only ever offered to somebody who went looking for it.
/// A Store install is never checked; the Store updates it.
/// </remarks>
public static class UpdateCheck
{
    public const string LatestApi = "https://api.github.com/repos/jesvijonathan/DispCtrl/releases/latest";
    public const string ReleasesPage = "https://github.com/jesvijonathan/DispCtrl/releases/latest";

    /// <summary>Only a page on the project's own releases is ever offered as the link.</summary>
    private const string TrustedPrefix = "https://github.com/jesvijonathan/DispCtrl/releases/";

    /// <summary>Checks now and records the answer in settings.</summary>
    /// <exception cref="HttpRequestException">No answer: offline, or GitHub refused.</exception>
    public static async Task<UpdateResult> CheckAsync(CancellationToken cancel = default)
    {
        string current = BuildInfo.Version;
        if (StartupIntegration.IsPackaged) return new(current, "", ReleasesPage, false, Store: true);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // GitHub refuses requests without one; the version is the only thing it says.
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"DispCtrl/{current}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using HttpResponseMessage response = await http.GetAsync(LatestApi, cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, default, cancel).ConfigureAwait(false);

        JsonElement root = document.RootElement;
        string tag = root.TryGetProperty("tag_name", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
        string url = root.TryGetProperty("html_url", out JsonElement u) && u.ValueKind == JsonValueKind.String ? u.GetString()! : "";
        if (!url.StartsWith(TrustedPrefix, StringComparison.Ordinal)) url = ReleasesPage;
        string latest = UpdateVersion.Display(tag);
        if (latest.Length == 0) throw new HttpRequestException("The latest release does not name a version.");

        var result = new UpdateResult(current, latest, url, UpdateVersion.IsNewer(tag, current, BuildInfo.Channel), Store: false);
        Record(result);
        return result;
    }

    /// <summary>Remembers what was found, so a restart does not ask again the same day.</summary>
    private static void Record(UpdateResult result)
    {
        try
        {
            DispCtrlSettings settings = SettingsStore.Load();
            UpdateSettings updates = settings.Global.Updates;
            updates.CheckedUtc = DateTimeOffset.UtcNow;
            updates.LatestVersion = result.Latest;
            updates.LatestUrl = result.Url;
            SettingsStore.Save(settings);
        }
        catch (Exception) { /* the answer still stands; only its record is lost */ }
    }

    /// <summary>What the last check found, when it is newer than this build and not set aside with "Not now".</summary>
    public static UpdateResult? Known(UpdateSettings updates)
    {
        if (StartupIntegration.IsPackaged || updates.LatestVersion.Length == 0) return null;
        if (!UpdateVersion.IsNewer(updates.LatestVersion, BuildInfo.Version, BuildInfo.Channel)) return null;
        if (updates.SkippedVersion == updates.LatestVersion) return null;
        string url = updates.LatestUrl.StartsWith(TrustedPrefix, StringComparison.Ordinal) ? updates.LatestUrl : ReleasesPage;
        return new(BuildInfo.Version, updates.LatestVersion, url, true, false);
    }
}
