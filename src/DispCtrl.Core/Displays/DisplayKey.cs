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
    public bool HasSerial => !string.IsNullOrEmpty(Serial) && !IsPlaceholderSerial(Serial);

    /// <summary>A serial that many units of a model share, so it identifies nothing.</summary>
    /// <remarks>
    /// The EDID's numeric serial is often a filler - 0x01010101 above all (an
    /// LG television here), 0xFFFFFFFF, 1, 12345678 - and some descriptor
    /// serials are a run of one character. Taken as an identity, two such
    /// monitors of one model fuse into one settings entry. They are treated as
    /// having no serial, which falls back to the port.
    /// </remarks>
    public static bool IsPlaceholderSerial(string serial)
    {
        string s = serial.Trim();
        if (s.Length == 0) return true;
        if (s is "01010101" or "FFFFFFFF" or "00000001" or "12345678" or "0123456789" or "123456789" or "1234567890") return true;
        foreach (char c in s) if (c != s[0]) return false;
        return true;
    }

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
    /// Without a serial the port is the only thing that distinguishes this
    /// panel, so it contributes a hash rather than being dropped. The model
    /// prefix is kept so settings files stay human-readable.
    /// <para>
    /// The port, not the whole device path. The path's middle segment
    /// (<c>5&amp;3c9e07d1&amp;0&amp;UID256</c>) begins with a parent instance
    /// that Windows renumbers after a driver update, a dock or a GPU switch,
    /// and hashing all of it gave the laptop's panel a new token - and every
    /// setting it had, calibration included, was left behind under the old
    /// one. Only the trailing UID, the target on its adapter, is kept.
    /// Entries saved under a former token are adopted by
    /// <see cref="Settings.MonitorAdoption"/>.
    /// </para>
    /// </remarks>
    public string ToToken()
    {
        if (HasSerial) return Sanitize(EdidFingerprint);

        string model = string.IsNullOrEmpty(Model) ? "UNKNOWN" : Model;
        return $"{Sanitize(model)}-{StableHash(Port(DevicePath))}";
    }

    /// <summary>The device path without its renumbered parent instance: model and target UID.</summary>
    public static string Port(string devicePath)
    {
        string[] parts = devicePath.Split('#');
        if (parts.Length < 3) return devicePath;
        int uid = parts[2].LastIndexOf('&');
        return uid < 0 ? devicePath : parts[1] + "#" + parts[2][(uid + 1)..];
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
