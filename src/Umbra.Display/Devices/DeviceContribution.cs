using System.Diagnostics;
using Umbra.Core.Displays;
using Umbra.Core.Settings;

namespace Umbra.Display.Devices;

/// <summary>The outcome of preparing a contribution, so callers can say what happened.</summary>
/// <param name="Body">The scrubbed markdown, exactly as it would be published.</param>
/// <param name="Path">Where the same text was saved locally.</param>
/// <param name="Url">The prefilled issue, or the plain new-issue page when the body is too long.</param>
/// <param name="Prefilled">False when the body must be pasted by hand.</param>
public readonly record struct Contribution(
    DeviceSubmission Submission, string Body, string Path, Uri Url, bool Prefilled)
{
    public string Title => Submission.IssueTitle;
    public string Key => Submission.Key;
}

/// <summary>
/// Sends what a monitor can do to the project, and nothing else.
/// </summary>
/// <remarks>
/// Umbra can only drive a control it knows about, and the only way to learn
/// about the controls on a monitor nobody here owns is for someone who owns one
/// to say. This turns an attached panel into a device record and hands it to
/// the person to publish.
/// <para>
/// <b>There is no access token in the application and no request is made from
/// it.</b> The submission opens as a prefilled GitHub issue in the browser the
/// person is already signed into, so they read the exact text first and press
/// Submit themselves. A token shipped in a Store app is a token given to
/// everyone who downloads it, and consent that the application grants itself is
/// not consent. The same text is written to a file either way, so nothing is
/// lost if they close the tab.
/// </para>
/// </remarks>
public static class DeviceContribution
{
    /// <summary>Where device records are collected.</summary>
    public const string Repository = "jesvijonathan/Display-Control";

    /// <summary>
    /// How long a prefilled issue body may be.
    /// </summary>
    /// <remarks>
    /// GitHub answers a long enough query string with 414, and the body is
    /// percent-encoded on the way in, which roughly triples anything with
    /// punctuation in it. A monitor listing forty controls overruns this
    /// easily, so the long case has to work rather than merely be handled: the
    /// file is written either way, the plain issue page opens, and the person
    /// pastes it in.
    /// <para>
    /// Seven thousand rather than the eight where GitHub begins answering 414,
    /// leaving room for the title, the label, and whatever a browser adds.
    /// </para>
    /// </remarks>
    private const int MaxUrlLength = 7000;

    /// <summary>Where submissions are saved, alongside the display report.</summary>
    public static string Folder => Path.Combine(SettingsStore.Directory, "devices");

    /// <summary>
    /// Prepares one monitor's record without sending anything.
    /// </summary>
    /// <remarks>
    /// Slow — see <see cref="DeviceSubmission.Build"/>. The scrub runs over the
    /// finished text rather than the fields, so that it also covers anything a
    /// later field might carry in.
    /// </remarks>
    public static Contribution Prepare(DisplayInfo display, IReadOnlyList<DisplayInfo>? all = null)
    {
        // The owner's marking is the only panel-technology answer available for
        // a laptop screen, which reports nothing over DDC/CI.
        bool? isOled = null;
        try
        {
            isOled = SettingsStore.Load().For(display.Token).IsOled;
        }
        catch (Exception)
        {
            // Unreadable settings must not stop a submission.
        }

        DeviceSubmission submission = DeviceSubmission.Build(display, isOled);

        string body = Redact.Scrub(submission.ToMarkdown(), Serials(all));
        string path = Save(submission.Key, body);

        string title = Uri.EscapeDataString($"Device: {submission.IssueTitle}");
        string encoded = Uri.EscapeDataString(body);
        string labels = Uri.EscapeDataString("device");

        string prefilled = $"https://github.com/{Repository}/issues/new"
            + $"?title={title}&labels={labels}&body={encoded}";

        return prefilled.Length <= MaxUrlLength
            ? new Contribution(submission, body, path, new Uri(prefilled), true)
            : new Contribution(submission, body, path,
                new Uri($"https://github.com/{Repository}/issues/new?title={title}&labels={labels}"), false);
    }

    /// <summary>Opens the prepared issue in the browser.</summary>
    /// <remarks>
    /// <c>UseShellExecute</c> because the target is a URL and the browser is
    /// whatever the person has chosen, which only the shell knows.
    /// </remarks>
    public static bool Open(Contribution contribution)
    {
        try
        {
            using Process? p = Process.Start(new ProcessStartInfo(contribution.Url.ToString())
            {
                UseShellExecute = true,
            });

            return true;
        }
        catch (Exception)
        {
            // No browser, or the shell refused. The file is already written, so
            // the person still has everything they need.
            return false;
        }
    }

    /// <summary>The serials of the attached panels, which must never be published.</summary>
    private static List<string> Serials(IReadOnlyList<DisplayInfo>? all)
    {
        var serials = new List<string>();

        try
        {
            foreach (DisplayInfo d in all ?? DisplayRegistry.Enumerate())
                if (d.Key.HasSerial) serials.Add(d.Key.Serial);
        }
        catch (Exception)
        {
            // The pattern-based part of the scrub still applies.
        }

        return serials;
    }

    private static string Save(string key, string body)
    {
        try
        {
            Directory.CreateDirectory(Folder);

            string path = Path.Combine(Folder, $"{Safe(key)}.md");
            File.WriteAllText(path, body);
            return path;
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>A device key as a filename.</summary>
    private static string Safe(string key)
    {
        Span<char> buffer = stackalloc char[key.Length];
        for (int i = 0; i < key.Length; i++)
            buffer[i] = char.IsLetterOrDigit(key[i]) || key[i] is '-' or '_' ? key[i] : '-';

        return buffer.ToString();
    }
}
