using System.Globalization;

namespace DispCtrl.Core.Displays;

/// <summary>
/// A monitor identity that survives unplugging, reordering and resolution
/// changes — which is what every per-monitor setting in DispCtrl is keyed on.
/// </summary>
/// <remarks>
/// The obvious key, <c>\\.\DISPLAY1</c>, is useless for persistence: Windows
/// reassigns those numbers whenever the set of attached displays changes, so
/// settings saved against "DISPLAY1" silently land on the wrong panel after a
/// replug. Two identities are carried instead, and matched in order:
/// <list type="number">
/// <item><see cref="DevicePath"/> — exact, and the same string
/// <c>IDesktopWallpaper</c> uses, so wallpaper calls need no translation. It
/// encodes the adapter port, so moving a cable HDMI→DP changes it.</item>
/// <item>Model plus <see cref="Serial"/> — read from the panel's own EDID.
/// Survives a port change, and is the only thing that tells two identical
/// monitors apart.</item>
/// </list>
/// </remarks>
/// <param name="DevicePath">
/// CCD monitor device path, e.g. <c>\\?\DISPLAY#DELA234#5&amp;3c9e07d1&amp;0&amp;UID257#{guid}</c>.
/// </param>
/// <param name="Model">
/// EDID manufacturer and product code, e.g. <c>DEL-A234</c>. Deliberately
/// <em>not</em> an identity on its own — see <see cref="Matches"/>.
/// </param>
/// <param name="Serial">EDID serial, e.g. <c>9XYZ7K1</c>. Empty when the panel reports none.</param>
public sealed record DisplayKey(string DevicePath, string Model, string Serial)
{
    /// <summary>
    /// True only when the panel reports a serial, which is what makes the EDID
    /// usable as an identity rather than merely a description.
    /// </summary>
    public bool HasSerial => !string.IsNullOrEmpty(Serial);

    /// <summary>Full EDID identity, or empty when the panel reports no serial.</summary>
    public string EdidFingerprint => HasSerial ? $"{Model}-{Serial}" : string.Empty;

    /// <summary>
    /// True when <paramref name="other"/> is the same physical panel.
    /// </summary>
    /// <remarks>
    /// The EDID fallback requires a serial. Matching on model alone looks
    /// tempting and is actively wrong: two identical monitors — the exact case
    /// the fallback exists for — report the same model, so it would fuse them
    /// into one identity and make their settings overwrite each other. This
    /// machine's internal Samsung panel reports no serial at all, so that path
    /// is not hypothetical.
    /// </remarks>
    public bool Matches(DisplayKey other)
    {
        if (string.Equals(DevicePath, other.DevicePath, StringComparison.OrdinalIgnoreCase))
            return true;

        return HasSerial
            && other.HasSerial
            && string.Equals(EdidFingerprint, other.EdidFingerprint, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Short, stable, filesystem-safe token used to key settings files.</summary>
    /// <remarks>
    /// Without a serial the device path is the only thing that distinguishes
    /// this panel, so it contributes a hash rather than being dropped. The
    /// model prefix is kept purely so settings files stay human-readable.
    /// </remarks>
    public string ToToken()
    {
        if (HasSerial) return Sanitize(EdidFingerprint);

        string model = string.IsNullOrEmpty(Model) ? "UNKNOWN" : Model;
        return $"{Sanitize(model)}-{StableHash(DevicePath)}";
    }

    /// <summary>
    /// FNV-1a. Chosen over <see cref="string.GetHashCode()"/>, which is
    /// randomised per process and would therefore produce a different settings
    /// key on every launch.
    /// </summary>
    private static string StableHash(string s)
    {
        const uint offset = 2166136261, prime = 16777619;
        uint h = offset;
        foreach (char c in s.ToUpperInvariant())
        {
            h ^= c;
            h *= prime;
        }
        return h.ToString("X8", CultureInfo.InvariantCulture);
    }

    private static string Sanitize(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        for (int i = 0; i < s.Length; i++)
            buf[i] = char.IsLetterOrDigit(s[i]) || s[i] is '-' or '_' ? s[i] : '-';
        return new string(buf);
    }

    public override string ToString() => ToToken();
}
