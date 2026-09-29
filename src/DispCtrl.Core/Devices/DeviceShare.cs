using System.Text.RegularExpressions;

namespace DispCtrl.Core.Devices;

/// <summary>
/// The text a device share is recognised by, shared by the app that writes a
/// share and the intake that reads one, so the two cannot drift apart.
/// </summary>
/// <remarks>
/// A share too long for an issue link is opened with a placeholder and the rest
/// on the clipboard. When that paste is forgotten the issue still arrives, and
/// the intake must tell it apart from a share that simply had nothing more to
/// say: #24 went in with no record and #25 with no model at all, silently.
/// </remarks>
public static partial class DeviceShare
{
    /// <summary>The marker every share's JSON block carries.</summary>
    public const string Kind = "dispctrl-device-mapping";

    /// <summary>Stands where a model's record goes when the record is on the clipboard.</summary>
    public const string RecordPlaceholder =
        "_DispCtrl copied this model's full record to the clipboard, because it is too long for a link. Paste it here, in place of this line, before you submit._";

    /// <summary>Stands in for a whole contribution that is on the clipboard.</summary>
    public const string BodyPlaceholder =
        "Paste the complete device contribution copied by DispCtrl here, replacing this text, before you submit.";

    /// <summary>
    /// True when the text still holds a placeholder DispCtrl asked to be pasted
    /// over, in this wording or an earlier release's.
    /// </summary>
    public static bool HasPlaceholder(string text) => PlaceholderPattern().IsMatch(text);

    [GeneratedRegex(@"DispCtrl copied|copied by DispCtrl", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
