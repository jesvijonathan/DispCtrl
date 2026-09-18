using Microsoft.Win32;

namespace DisplCtrl.Core.Displays;

/// <summary>
/// Reads the raw EDID a monitor reports about itself, straight out of the
/// device's registry key.
/// </summary>
/// <remarks>
/// The CCD API hands back a manufacturer id and product code, but not the
/// serial — and without a serial two identical monitors are indistinguishable,
/// so their settings would fight over one identity. The serial only exists in
/// the EDID blob, which the PnP stack caches under the device's
/// <c>Device Parameters</c> key.
/// <para>
/// Registry is used rather than WMI deliberately: <c>System.Management</c> is
/// not Native-AOT friendly, and this runs in the AOT engine.
/// </para>
/// </remarks>
public static class Edid
{
    /// <summary>
    /// Cache keyed on device path.
    /// </summary>
    /// <remarks>
    /// A panel's EDID cannot change while it is attached at the same path, and
    /// this is on the engine's once-a-second rescan path — uncached it was two
    /// registry opens and parses per second, forever, to recompute a constant.
    /// </remarks>
    private static readonly Dictionary<string, Identity> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock CacheGate = new();

    /// <summary>Model and serial as the panel reports them; either may be empty.</summary>
    /// <param name="Model">e.g. <c>DEL-A234</c> — manufacturer plus product code.</param>
    /// <param name="Serial">e.g. <c>3QQQ2X3</c>, or empty when none is reported.</param>
    public readonly record struct Identity(string Model, string Serial);

    /// <summary>
    /// Reads the panel's model and serial out of its EDID.
    /// </summary>
    /// <param name="devicePath">
    /// A CCD monitor device path, e.g.
    /// <c>\\?\DISPLAY#DELA234#5&amp;1af48b2f&amp;0&amp;UID257#{guid}</c>.
    /// </param>
    /// <remarks>
    /// Model and serial stay separate rather than being pre-joined: a model
    /// with no serial is a description, not an identity, and callers must be
    /// able to tell the difference. See <see cref="DisplayKey"/>.
    /// </remarks>
    public static Identity Read(string devicePath)
    {
        lock (CacheGate)
        {
            if (Cache.TryGetValue(devicePath, out Identity hit)) return hit;
        }

        Identity id = ReadUncached(devicePath);

        lock (CacheGate)
        {
            Cache[devicePath] = id;
        }
        return id;
    }

    private static Identity ReadUncached(string devicePath)
    {
        byte[]? blob = ReadBlob(devicePath);
        if (blob is null || blob.Length < 128) return new Identity(string.Empty, string.Empty);

        string mfg = DecodeManufacturer(blob);
        if (mfg.Length == 0) return new Identity(string.Empty, string.Empty);

        ushort product = (ushort)(blob[10] | (blob[11] << 8));

        // Prefer the descriptor-block serial string: it is the one printed on
        // the chassis label, and unlike the numeric field it is rarely zero.
        string serial = ReadDescriptorString(blob, 0xFF) ?? ReadNumericSerial(blob);

        return new Identity($"{mfg}-{product:X4}", serial);
    }

    /// <summary>
    /// The panel's physical image size in millimetres, or (0,0) if unknown.
    /// </summary>
    /// <remarks>
    /// Read from the first detailed timing descriptor, which carries the size
    /// to the millimetre, falling back to EDID bytes 21-22 which give it only
    /// to the centimetre. This is the one place a display's <em>real</em> size
    /// is available: resolution says nothing about it, and DPI is a scaling
    /// preference rather than a measurement — a 14-inch laptop panel and a
    /// 27-inch monitor can report identical values for both.
    /// </remarks>
    public static (int WidthMm, int HeightMm) PhysicalSize(string devicePath)
    {
        byte[]? e = ReadBlob(devicePath);
        if (e is null || e.Length < 128) return (0, 0);

        // First detailed timing descriptor, bytes 54-71. Bytes 12 and 13 hold
        // the low bits of width and height; byte 14 packs the high nibbles.
        const int dtd = 54;
        bool isTiming = e[dtd] != 0 || e[dtd + 1] != 0;
        if (isTiming)
        {
            int w = ((e[dtd + 14] & 0xF0) << 4) | e[dtd + 12];
            int h = ((e[dtd + 14] & 0x0F) << 8) | e[dtd + 13];
            if (w > 0 && h > 0) return (w, h);
        }

        // Basic display parameters, in centimetres.
        int cmW = e[21], cmH = e[22];
        return cmW > 0 && cmH > 0 ? (cmW * 10, cmH * 10) : (0, 0);
    }

    /// <summary>
    /// The panel's supported vertical refresh range, in Hz.
    /// </summary>
    /// <remarks>
    /// Read from EDID descriptor 0xFD, the Monitor Range Limits block. A panel
    /// that advertises a genuine span here is describing adaptive sync; a
    /// fixed-rate monitor reports a span only a hertz or two wide.
    /// </remarks>
    public static (uint Min, uint Max) RefreshRange(string devicePath)
    {
        byte[]? blob = ReadBlob(devicePath);
        if (blob is null || blob.Length < 128) return (0, 0);

        for (int off = 54; off + 18 <= 128; off += 18)
        {
            if (blob[off] != 0 || blob[off + 1] != 0 || blob[off + 2] != 0) continue;
            if (blob[off + 3] != 0xFD) continue;

            uint min = blob[off + 5];
            uint max = blob[off + 6];

            // Byte 4 flags offsets that extend the range past 255Hz.
            byte flags = blob[off + 4];
            if ((flags & 0x01) != 0) max += 255;
            if ((flags & 0x02) != 0) { min += 255; max += 255; }

            return min > 0 && max >= min ? (min, max) : (0, 0);
        }

        return (0, 0);
    }

    /// <summary>Monitor name from EDID descriptor 0xFC, when present.</summary>
    public static string? NameFromEdid(string devicePath)
    {
        byte[]? blob = ReadBlob(devicePath);
        return blob is null || blob.Length < 128 ? null : ReadDescriptorString(blob, 0xFC);
    }

    private static byte[]? ReadBlob(string devicePath)
    {
        string? sub = ToEnumSubKey(devicePath);
        if (sub is null) return null;

        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Enum\{sub}\Device Parameters");
            return key?.GetValue("EDID") as byte[];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // A panel whose EDID is unreadable degrades to device-path-only
            // identity, which still works; it just cannot survive a port change.
            return null;
        }
    }

    /// <summary>
    /// <c>\\?\DISPLAY#DELA234#5&amp;1af48b2f&amp;0&amp;UID257#{guid}</c> becomes
    /// <c>DISPLAY\DELA234\5&amp;1af48b2f&amp;0&amp;UID257</c>.
    /// </summary>
    private static string? ToEnumSubKey(string devicePath)
    {
        if (string.IsNullOrEmpty(devicePath)) return null;

        ReadOnlySpan<char> s = devicePath;
        if (s.StartsWith(@"\\?\", StringComparison.Ordinal)) s = s[4..];
        else if (s.StartsWith(@"\\.\", StringComparison.Ordinal)) s = s[4..];

        // Drop the trailing interface GUID.
        int guid = s.IndexOf('{');
        if (guid > 0) s = s[..guid];
        s = s.TrimEnd('#');

        if (s.IsEmpty) return null;

        Span<char> buf = stackalloc char[s.Length];
        s.CopyTo(buf);
        for (int i = 0; i < buf.Length; i++)
            if (buf[i] == '#') buf[i] = '\\';

        string sub = new(buf);
        // Guard against a malformed path escaping the Enum subtree.
        return sub.Contains("..", StringComparison.Ordinal) ? null : sub;
    }

    /// <summary>
    /// EDID bytes 8-9 pack three letters as 5 bits each, big-endian, with
    /// 1 = 'A'.
    /// </summary>
    private static string DecodeManufacturer(byte[] e)
    {
        int v = (e[8] << 8) | e[9];
        Span<char> c =
        [
            (char)('A' - 1 + ((v >> 10) & 0x1F)),
            (char)('A' - 1 + ((v >> 5) & 0x1F)),
            (char)('A' - 1 + (v & 0x1F)),
        ];

        foreach (char ch in c)
            if (ch is < 'A' or > 'Z') return string.Empty;

        return new string(c);
    }

    private static string ReadNumericSerial(byte[] e)
    {
        uint n = (uint)(e[12] | (e[13] << 8) | (e[14] << 16) | (e[15] << 24));
        return n == 0 ? string.Empty : n.ToString("X8");
    }

    /// <summary>
    /// Scans the four 18-byte descriptor blocks for one tagged
    /// <paramref name="tag"/> (0xFF serial, 0xFC name).
    /// </summary>
    private static string? ReadDescriptorString(byte[] e, byte tag)
    {
        Span<char> buf = stackalloc char[13];

        for (int off = 54; off + 18 <= 128; off += 18)
        {
            // A descriptor is a display descriptor (not a timing block) only
            // when its first three bytes are zero.
            if (e[off] != 0 || e[off + 1] != 0 || e[off + 2] != 0) continue;
            if (e[off + 3] != tag) continue;

            int len = 0;
            for (int i = 0; i < 13; i++)
            {
                byte b = e[off + 5 + i];
                if (b is 0x0A or 0x00) break;         // 0x0A terminates, padded with 0x20
                if (b is < 0x20 or > 0x7E) continue;  // ignore non-printable noise
                buf[len++] = (char)b;
            }

            string s = new string(buf[..len]).Trim();
            if (s.Length > 0) return s;
        }
        return null;
    }
}
