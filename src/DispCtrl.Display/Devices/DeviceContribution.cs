using System.Diagnostics;
using System.Net;
using System.Text;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;

namespace DispCtrl.Display.Devices;

/// <summary>The outcome of preparing a contribution, so callers can say what happened.</summary>
/// <param name="Body">The scrubbed markdown, exactly as it would be published.</param>
/// <param name="Path">Where the same text was saved locally.</param>
/// <param name="Url">The prefilled issue, or the plain new-issue page when the body is too long.</param>
/// <param name="Prefilled">False when the body must be pasted by hand.</param>
/// <param name="Heading">
/// What the issue is called, when that is not one display's own title. A
/// desk-wide record covers several displays and cannot borrow the first one's
/// name - which it silently did, so an issue about two monitors was titled after
/// whichever happened to be enumerated first.
/// </param>
public readonly record struct Contribution(
    DeviceSubmission Submission, string Body, string Path, Uri Url, bool Prefilled,
    string? Heading = null)
{
    public string Title => Heading ?? Submission.IssueTitle;
    public string Key => Submission.Key;
}

/// <summary>
/// Sends what a monitor can do to the project, and nothing else.
/// </summary>
/// <remarks>
/// DispCtrl can only drive a control it knows about, and the only way to learn
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
public static partial class DeviceContribution
{
    /// <summary>Where device records are collected.</summary>
    public const string Repository = "jesvijonathan/DispCtrl";

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

        // Scrub, then fold to ASCII. In that order: the scrub matches on real
        // text, and folding first could in principle change a path or a serial
        // into something its pattern no longer recognises.
        // The public issue is the stable per-model record that belongs under
        // devices/. The much larger machine report, serials and current user
        // settings stay in the local diagnostics file. Keeping those separate
        // also keeps the complete issue body within GitHub's prefill URL.
        string body = Redact.Ascii(Redact.Scrub(submission.ToRepositoryMarkdown(), Identifiers(all)))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        string path = Save(submission.Key, body);

        // Query-form encoding uses '+' for spaces. Uri.EscapeDataString turns
        // every space into three characters, which alone pushed detailed Dell
        // records over the browser limit despite the readable body fitting.
        string title = WebUtility.UrlEncode($"Device: {submission.IssueTitle}");
        string encoded = WebUtility.UrlEncode(body);
        string labels = WebUtility.UrlEncode("device");

        string prefilled = $"https://github.com/{Repository}/issues/new"
            + $"?title={title}&labels={labels}&body={encoded}";

        return prefilled.Length <= MaxUrlLength
            ? new Contribution(submission, body, path, new Uri(prefilled), true)
            : new Contribution(submission, body, path,
                new Uri($"https://github.com/{Repository}/issues/new?title={title}&labels={labels}"), false);
    }

    /// <summary>
    /// Prepares one issue for the whole desk, carrying everything.
    /// </summary>
    /// <remarks>
    /// One issue rather than one per monitor. Per monitor was the older shape,
    /// on the reasoning that a device record describes a model; what it actually
    /// produced, once records carried the full report, was two browser tabs that
    /// both opened <b>empty</b> — every record was past the length GitHub takes
    /// in a link, and the fallback only put a record on the clipboard when
    /// exactly one was over-long. Two monitors meant two blank forms and nothing
    /// to paste.
    /// <para>
    /// The desk-wide form fixes that by having one thing to open and one thing
    /// to paste. It also stops the report being repeated: the whole of
    /// <c>displays.log</c> goes in once, at the end, rather than each display
    /// carrying its own slice of it.
    /// </para>
    /// <para>
    /// The per-model files under <see cref="Folder"/> are still written, one
    /// each, because that is how <c>devices/</c> is organised and a maintainer
    /// splitting the issue wants them.
    /// </para>
    /// </remarks>
    public static Contribution PrepareDesk(IReadOnlyList<DisplayInfo> displays)
    {
        DispCtrlSettings settings;
        try
        {
            settings = SettingsStore.Load();
        }
        catch (Exception)
        {
            settings = new DispCtrlSettings();
        }

        var submissions = new List<DeviceSubmission>(displays.Count);
        var sb = new StringBuilder();

        sb.AppendLine($"### {DeskTitle(displays)}");
        sb.AppendLine();
        sb.AppendLine($"{displays.Count} display(s) on one desk. Each is recorded in full below, "
            + "followed by the whole display report"
            + (DispCtrl.Core.FeatureFlags.Presets ? " and every preset on the machine" : "") + ".");
        sb.AppendLine();

        foreach (DisplayInfo d in displays)
        {
            bool? isOled = null;
            try
            {
                isOled = settings.For(d.Token).IsOled;
            }
            catch (Exception)
            {
                // Unreadable settings must not stop a submission.
            }

            DeviceSubmission one = DeviceSubmission.Build(d, isOled);
            submissions.Add(one);

            // Without its own slice of the report: the whole file follows.
            sb.AppendLine("---");
            sb.AppendLine();
            sb.Append(one.ToMarkdown(includeReport: false));
            sb.AppendLine();
        }

        // The file itself, entire, exactly as it is written on this PC. This is
        // the part that was asked for by name.
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("<details><summary>displays.log, the whole report as written on this PC</summary>");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(Safely(() => DisplayReport.Build(displays), "").TrimEnd());
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("</details>");
        sb.AppendLine();

        if (DispCtrl.Core.FeatureFlags.Presets)
        {
            sb.AppendLine("<details><summary>Presets saved on this machine</summary>");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(Safely(DisplayReport.Presets, "").TrimEnd());
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("</details>");
        }

        string body = Redact.Ascii(Redact.Scrub(sb.ToString(), Identifiers(displays)));

        // One file per model as well, so devices/ can be filled from this.
        foreach (DeviceSubmission one in submissions)
            Save(one.Key, Redact.Ascii(Redact.Scrub(one.ToMarkdown(), Identifiers(displays))));

        string path = Save(DeskKey(submissions), body);

        string title = WebUtility.UrlEncode($"Displays: {DeskTitle(displays)}");
        string labels = WebUtility.UrlEncode("device");

        string prefilled = $"https://github.com/{Repository}/issues/new"
            + $"?title={title}&labels={labels}&body={WebUtility.UrlEncode(body)}";

        DeviceSubmission first = submissions.Count > 0 ? submissions[0] : DeskPlaceholder();

        string heading = DeskTitle(displays);

        return prefilled.Length <= MaxUrlLength
            ? new Contribution(first, body, path, new Uri(prefilled), true, heading)
            : new Contribution(first, body, path,
                new Uri($"https://github.com/{Repository}/issues/new?title={title}&labels={labels}"),
                false, heading);
    }

    /// <summary>
    /// Every distinct display on the desk, named, for the issue title.
    /// </summary>
    /// <remarks>
    /// Distinct, because two identical monitors are one entry in a device
    /// folder and "U2424H, U2424H" reads as a mistake. The count comes back
    /// instead, so a pair is still visible as a pair.
    /// </remarks>
    private static string DeskTitle(IReadOnlyList<DisplayInfo> displays)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        MachineInfo machine = MachineInfo.Read();

        foreach (DisplayInfo d in displays)
        {
            string name = string.IsNullOrWhiteSpace(d.Label) ? d.Key.Model : d.Label;
            if (string.IsNullOrWhiteSpace(name)) name = "unnamed display";

            // "Internal 2880x1800" says nothing about which laptop. The machine
            // model does, and it is what someone searching the folder for their
            // own would search by.
            if (d.IsInternal && machine.Model.Length > 0) name = $"{name} ({machine.Model})";

            if (counts.TryGetValue(name, out int n)) counts[name] = n + 1;
            else { counts[name] = 1; order.Add(name); }
        }

        var parts = new List<string>(order.Count);
        foreach (string name in order)
            parts.Add(counts[name] > 1 ? $"{name} x{counts[name]}" : name);

        return parts.Count == 0 ? "no displays" : string.Join(", ", parts);
    }

    /// <summary>The file name a desk-wide record is saved under.</summary>
    private static string DeskKey(IReadOnlyList<DeviceSubmission> submissions)
    {
        if (submissions.Count == 0) return "desk";

        var keys = new List<string>(submissions.Count);
        foreach (DeviceSubmission s in submissions)
            if (!keys.Contains(s.Key)) keys.Add(s.Key);

        return $"desk-{string.Join("+", keys)}";
    }

    private static DeviceSubmission DeskPlaceholder() => new()
    {
        Manufacturer = "",
        Model = "no displays",
        Product = "",
        Connector = "",
        PanelTechnology = "",
    };

    private static T Safely<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return fallback;
        }
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

    /// <summary>
    /// Everything that names a unit rather than a model, for every panel on the
    /// desk.
    /// </summary>
    /// <remarks>
    /// Two things here were bugs, and both surfaced the moment a record started
    /// carrying the full report and the presets:
    /// <list type="bullet">
    /// <item><b>Every attached panel, not only the one being submitted.</b> A
    /// preset names every display on the desk, so a record for one monitor
    /// carries the others' identifiers. Scrubbing against the caller's list —
    /// which <c>dispctrl contribute --display 2</c> narrows to one — published
    /// the other monitor's.</item>
    /// <item><b>Tokens as well as serials.</b> A panel with no EDID serial still
    /// has a token, and its suffix is an FNV-1a hash of the device path: stable,
    /// unique to that unit on that port, and invisible to a serial list precisely
    /// because there is no serial. This laptop is that case.</item>
    /// </list>
    /// Tokens go in first. A token contains the serial when there is one, so
    /// replacing the longer string first leaves a clean <c>[removed]</c> rather
    /// than a hollowed-out <c>DEL-A234-[removed]</c>.
    /// </remarks>
    internal static List<string> Identifiers(IReadOnlyList<DisplayInfo>? all)
    {
        var found = new List<string>();

        void Take(IEnumerable<DisplayInfo> displays)
        {
            foreach (DisplayInfo d in displays)
            {
                found.Add(d.Token);
                if (d.Key.HasSerial) found.Add(d.Key.Serial);
            }
        }

        if (all is not null) Take(all);

        try
        {
            // Always, and on top of whatever the caller passed. A narrowed list
            // is the caller saying which record to build, never which panels
            // exist.
            Take(DisplayRegistry.Enumerate());
        }
        catch (Exception)
        {
            // The pattern-based part of the scrub still applies.
        }

        return found;
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
