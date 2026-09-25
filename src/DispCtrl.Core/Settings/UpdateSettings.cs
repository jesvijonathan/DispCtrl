namespace DispCtrl.Core.Settings;

/// <summary>Finding out about new versions, for installs the Microsoft Store does not update.</summary>
/// <remarks>
/// Opt-in: DispCtrl makes no network request of its own until somebody asks it
/// to, either by switching <see cref="CheckAutomatically"/> on or by pressing
/// "Check for updates". A check is one anonymous request for the latest release's
/// tag; nothing is sent, and nothing is downloaded or installed by itself.
/// </remarks>
public sealed class UpdateSettings
{
    /// <summary>Look for a new release at most once a day. Off until switched on.</summary>
    public bool CheckAutomatically { get; set; }

    /// <summary>When a check last got an answer; bookkeeping.</summary>
    public DateTimeOffset? CheckedUtc { get; set; }

    /// <summary>The newest release that check found, as 0.1.6; empty before any.</summary>
    public string LatestVersion { get; set; } = "";

    /// <summary>Its release page.</summary>
    public string LatestUrl { get; set; } = "";

    /// <summary>A release somebody chose "Not now" for: not announced again, though a later one is.</summary>
    public string SkippedVersion { get; set; } = "";

    /// <summary>How long an automatic check waits after the last one.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>Whether an automatic check is due now.</summary>
    public bool Due(DateTimeOffset now) =>
        CheckAutomatically && (CheckedUtc is not { } last || now - last >= Interval || last > now);
}

/// <summary>Release versions compared, as DispCtrl tags them: v1.2.3, v1.2.3-beta[.N], v1.2.3-test[.N].</summary>
public static class UpdateVersion
{
    /// <summary>
    /// Whether <paramref name="tag"/> is newer than the running build.
    /// </summary>
    /// <param name="tag">A release tag, with or without its v.</param>
    /// <param name="current">This build's version, as 0.1.5.</param>
    /// <param name="channel">This build's channel: stable, beta, test or dev.</param>
    /// <remarks>
    /// A stable release of the same numbers as a beta or test build is newer:
    /// 0.1.6 is what 0.1.6 beta was on the way to. A tag that does not read as
    /// a version is never newer, so a malformed answer announces nothing.
    /// </remarks>
    public static bool IsNewer(string? tag, string current, string channel)
    {
        if (!TryParse(tag, out Version? latest, out bool prerelease) || !TryParse(current, out Version? mine, out _)) return false;
        int order = latest.CompareTo(mine);
        if (order != 0) return order > 0;
        return !prerelease && channel is not ("stable" or "dev");
    }

    /// <summary>The tag as a display version: v0.1.6 to 0.1.6; empty when it is not one.</summary>
    public static string Display(string? tag) =>
        TryParse(tag, out Version? v, out _) ? $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}" : "";

    private static bool TryParse(string? text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Version? version, out bool prerelease)
    {
        version = null;
        prerelease = false;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        int dash = t.IndexOfAny(['-', '+']);
        if (dash >= 0) { prerelease = t[dash] == '-'; t = t[..dash]; }
        if (!System.Version.TryParse(t, out Version? parsed) || parsed.Major < 0) return false;
        // 0.1 and 0.1.0 are the same release.
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }
}
