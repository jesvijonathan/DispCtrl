using Microsoft.Win32;

namespace DisplCtrl.Core.Displays;

/// <summary>Windows' own night light, as Windows itself records it.</summary>
/// <param name="Enabled">Warming right now.</param>
/// <param name="Strength">0-100, on the same scale as the slider in Settings.</param>
public readonly record struct WindowsNightLightState(bool Enabled, int Strength);

/// <summary>
/// Reads and sets the night light built into Windows.
/// </summary>
/// <remarks>
/// There is no public API for this. The feature lives in
/// <c>Windows.Shell.BlueLightReduction.dll</c>, whose own interface sits in the
/// <c>Windows.Internal.Shell.BlueLightReduction</c> namespace — deliberately
/// marked internal by Microsoft, absent from the SDK metadata, and rejected by
/// Store certification. Calling it would also mean guessing a vtable layout
/// with nothing to check the guess against.
/// <para>
/// What that component keeps in the registry is reachable, and is what this
/// class uses. Two CloudStore values hold the whole feature: one for on/off,
/// one for the strength and schedule. Both are Microsoft Bond compact binary.
/// </para>
/// <para>
/// **The timestamps are the entire trick.** CloudStore is a roaming store, so it
/// resolves conflicts by last-write-wins. Writing a byte-perfect copy of a blob
/// Windows itself produced is still rejected within milliseconds if it carries
/// the timestamps it was captured with: the owning service compares them,
/// decides its own state is newer, and writes its own back. Refreshing both
/// timestamps before writing is the difference between a change that is undone
/// before the next statement runs and one that Windows adopts and applies.
/// </para>
/// <para>
/// This is undocumented, and the layout may change between Windows builds.
/// Every read is therefore defensive: anything that does not parse is reported
/// as "no answer" rather than guessed at, and a write is refused outright rather
/// than attempted on a shape this code does not recognise.
/// </para>
/// </remarks>
public static class WindowsNightLight
{
    private const string Root =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current";

    private const string StatePath =
        Root + @"\default$windows.data.bluelightreduction.bluelightreductionstate"
             + @"\windows.data.bluelightreduction.bluelightreductionstate";

    private const string SettingsPath =
        Root + @"\default$windows.data.bluelightreduction.settings"
             + @"\windows.data.bluelightreduction.settings";

    private const string ValueName = "Data";

    /// <summary>
    /// The keys under <c>HKEY_CURRENT_USER</c> that change when Windows' night
    /// light does, for a caller that would rather be told than poll.
    /// </summary>
    /// <remarks>
    /// Two of them, and both are needed: the on/off state and the strength are
    /// kept apart, so watching one would miss half the changes.
    /// </remarks>
    public static IReadOnlyList<string> WatchPaths { get; } = [StatePath, SettingsPath];

    /// <summary>
    /// Seconds a written timestamp is pushed past the one it replaces.
    /// </summary>
    /// <remarks>
    /// The stored timestamp has one-second resolution, so a write landing in the
    /// same second as Windows' own last write would tie rather than win. Two
    /// seconds settles that without dating the record far enough ahead to make
    /// the user's next change in Settings look stale by comparison.
    /// </remarks>
    private const int FreshnessSeconds = 2;

    /// <summary>
    /// A timestamp that is certain to beat <paramref name="stored"/>.
    /// </summary>
    /// <remarks>
    /// Taking the clock alone is not enough, and the failure is silent when it
    /// is: a record can already be dated ahead of the clock — by a machine whose
    /// time has moved, or by an earlier write of this very method — and a write
    /// carrying an older timestamp is reverted within milliseconds, having
    /// reported success. Measured on this desk: the stored value sat two minutes
    /// ahead, and every write was undone before the next statement ran.
    /// </remarks>
    private static long Fresher(long stored, long now, long perSecond = 1) =>
        Math.Max(stored, now) + (FreshnessSeconds * perSecond);

    private const long TicksPerSecond = 10_000_000L;

    /// <summary>The colour temperature Windows warms to at a given strength.</summary>
    /// <remarks>
    /// Measured, not documented: the slider was swept through all 101 positions
    /// and the stored value read back at each. It is exactly linear —
    /// <c>6500K</c> at 0 and <c>1200K</c> at 100 — and the blob holds twice the
    /// kelvin figure.
    /// </remarks>
    public static int KelvinFor(int strength) => 6500 - (53 * Math.Clamp(strength, 0, 100));

    private static int EncodedFor(int strength) => 2 * KelvinFor(strength);

    private static int StrengthFor(int encoded) =>
        Math.Clamp((int)Math.Round((13000 - encoded) / 106.0), 0, 100);

    /// <summary>True when both values are present and in a shape this code knows.</summary>
    public static bool Available => Read() is not null;

    /// <summary>Opens Windows' own night light page in Settings.</summary>
    /// <remarks>
    /// The page carries things DisplCtrl deliberately does not mirror — Windows'
    /// own schedule, and its sunset-to-sunrise option, which needs a location
    /// this app has no business asking for. Pointing at it beats reimplementing
    /// it, and beats leaving someone to find it.
    /// </remarks>
    public static bool OpenSettings()
    {
        try
        {
            // A protocol launch is handed to the shell, which has no process of
            // its own to give back, so null here is success rather than failure.
            using System.Diagnostics.Process? _ = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("ms-settings:nightlight")
                {
                    UseShellExecute = true,
                });

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// What Windows' night light is set to, or null when it cannot be read.
    /// </summary>
    /// <remarks>
    /// Null is a real answer and has to stay distinguishable from "off at zero":
    /// a Windows build that moves this data should leave the feature looking
    /// unavailable, not looking switched off.
    /// </remarks>
    public static WindowsNightLightState? Read()
    {
        try
        {
            byte[]? state = ReadValue(StatePath);
            if (state is null || !TryParseState(state, out StateLayout layout)) return null;

            // Strength is optional. Windows only writes it once the slider has
            // been moved off its default, so its absence is not a failure.
            int strength = ReadStrength() ?? 48;

            return new WindowsNightLightState(layout.Enabled, strength);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Switches Windows' own night light on or off.</summary>
    /// <returns>False when the value could not be read, parsed or written.</returns>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            byte[]? data = ReadValue(StatePath);
            if (data is null || !TryParseState(data, out StateLayout layout)) return false;
            if (layout.Enabled == enabled) return true;

            byte[] stamp = Varint(Fresher(layout.Timestamp, UnixSeconds()));

            var built = new List<byte>(data.Length + 4);
            built.AddRange(data[..10]);
            built.AddRange(stamp);
            built.AddRange(data[layout.AfterFirstTimestamp..layout.NestedStart]);

            // The nested record, rebuilt around the two-byte marker whose mere
            // presence is what "on" means. Windows does not store a false; it
            // stores nothing.
            var nested = new List<byte>(24);
            nested.AddRange(data[layout.NestedStart..(layout.NestedStart + 4)]);
            if (enabled) nested.AddRange([0x10, 0x00]);
            nested.AddRange(data[layout.MarkerStart..(layout.MarkerStart + 5)]);
            nested.AddRange(Varint(Fresher(layout.SecondTimestamp, FileTimeTicks(), TicksPerSecond)));
            nested.Add(0x00);

            // Indexed against what is being written rather than what was read:
            // a timestamp that needed one more byte would otherwise move the
            // length byte out from under this.
            built[10 + stamp.Length + 3] = (byte)nested.Count;
            built.AddRange(nested);
            built.AddRange([0x00, 0x00, 0x00]);

            return WriteValue(StatePath, [.. built]);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Sets how warm Windows goes, on the 0-100 scale its own slider uses.</summary>
    public static bool SetStrength(int strength)
    {
        try
        {
            byte[]? data = ReadValue(SettingsPath);
            if (data is null || !TryParseRecord(data, out RecordLayout layout)) return false;

            int encoded = EncodedFor(strength);
            byte low = (byte)((encoded & 0x7F) | 0x80);
            byte high = (byte)((encoded >> 7) & 0x7F);

            byte[] stamp = Varint(Fresher(layout.Timestamp, UnixSeconds()));

            var built = new List<byte>(data.Length + 8);
            built.AddRange(data[..10]);
            built.AddRange(stamp);
            built.AddRange(data[layout.AfterFirstTimestamp..layout.NestedStart]);

            int existing = Find(data, layout.NestedStart, [0xCF, 0x28]);
            var nested = new List<byte>(data.Length);

            if (existing >= 0)
            {
                // Already overridden: only the two value bytes move, so nothing
                // else in the record — including settings this code does not
                // understand, like the schedule — is disturbed.
                nested.AddRange(data[layout.NestedStart..existing]);
                nested.AddRange([0xCF, 0x28, low, high]);
                nested.AddRange(data[(existing + 4)..layout.NestedEnd]);
            }
            else
            {
                // Never overridden. Windows writes the entry in field order, so
                // it belongs immediately before the next field rather than at
                // the end of the record.
                int next = Find(data, layout.NestedStart, [0xCA, 0x32]);
                if (next < 0) return false;

                nested.AddRange(data[layout.NestedStart..next]);
                nested.AddRange([0xCF, 0x28, low, high]);
                nested.AddRange(data[next..layout.NestedEnd]);
            }

            built[10 + stamp.Length + 3] = (byte)nested.Count;
            built.AddRange(nested);
            built.AddRange(data[layout.NestedEnd..]);

            return WriteValue(SettingsPath, [.. built]);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static int? ReadStrength()
    {
        byte[]? data = ReadValue(SettingsPath);
        if (data is null || !TryParseRecord(data, out RecordLayout layout)) return null;

        int at = Find(data, layout.NestedStart, [0xCF, 0x28]);
        if (at < 0 || at + 3 >= data.Length) return null;

        int encoded = (data[at + 2] & 0x7F) | ((data[at + 3] & 0x7F) << 7);
        return StrengthFor(encoded);
    }

    /// <summary>Where the parts of the on/off record sit, and what it is dated.</summary>
    private readonly record struct StateLayout(
        int AfterFirstTimestamp, int NestedStart, int MarkerStart,
        long Timestamp, long SecondTimestamp, bool Enabled);

    /// <summary>Where the parts of the settings record sit, and what it is dated.</summary>
    private readonly record struct RecordLayout(
        int AfterFirstTimestamp, int NestedStart, int NestedEnd, long Timestamp);

    private static bool TryParseState(byte[] data, out StateLayout layout)
    {
        layout = default;
        if (!TryParseRecord(data, out RecordLayout record)) return false;

        // The record carries a second timestamp of its own, behind a fixed
        // marker, and it has to be moved forward with the first one.
        int marker = Find(data, record.NestedStart, [0xD0, 0x0A, 0x02, 0xC6, 0x14]);
        if (marker < 0) return false;
        if (!TryVarint(data, marker + 5, out long second, out _)) return false;

        bool enabled = record.NestedStart + 5 < data.Length
            && data[record.NestedStart + 4] == 0x10
            && data[record.NestedStart + 5] == 0x00;

        layout = new StateLayout(
            record.AfterFirstTimestamp, record.NestedStart, marker,
            record.Timestamp, second, enabled);

        return true;
    }

    private static bool TryParseRecord(byte[] data, out RecordLayout layout)
    {
        layout = default;

        // Ten bytes of fixed preamble, then a timestamp, then three more fixed
        // bytes, then the length of everything that follows bar the padding.
        if (data.Length < 24) return false;
        if (data[0] != 0x43 || data[1] != 0x42) return false;

        if (!TryVarint(data, 10, out long timestamp, out int timestampLength)) return false;

        int afterTimestamp = 10 + timestampLength;
        int lengthIndex = afterTimestamp + 3;
        if (lengthIndex >= data.Length) return false;

        int nestedStart = lengthIndex + 1;
        int nestedEnd = nestedStart + data[lengthIndex];
        if (nestedEnd > data.Length) return false;
        if (data[nestedStart] != 0x43 || data[nestedStart + 1] != 0x42) return false;

        layout = new RecordLayout(afterTimestamp, nestedStart, nestedEnd, timestamp);
        return true;
    }

    private static int Find(byte[] data, int from, ReadOnlySpan<byte> pattern)
    {
        for (int i = from; i <= data.Length - pattern.Length; i++)
        {
            bool hit = true;
            for (int j = 0; j < pattern.Length; j++)
                if (data[i + j] != pattern[j]) { hit = false; break; }

            if (hit) return i;
        }

        return -1;
    }

    private static bool TryVarint(byte[] data, int start, out long value, out int length)
    {
        value = 0;
        length = 0;

        for (int i = start; i < data.Length && i - start < 10; i++)
        {
            value |= (long)(data[i] & 0x7F) << (7 * (i - start));
            length++;
            if ((data[i] & 0x80) == 0) return true;
        }

        value = 0;
        length = 0;
        return false;
    }

    private static byte[] Varint(long value)
    {
        var bytes = new List<byte>(10);
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            bytes.Add(value != 0 ? (byte)(b | 0x80) : b);
        }
        while (value != 0);

        return [.. bytes];
    }

    private static long UnixSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static long FileTimeTicks() => DateTime.UtcNow.ToFileTimeUtc();

    private static byte[]? ReadValue(string path)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(ValueName) as byte[];
    }

    private static bool WriteValue(string path, byte[] data)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path, writable: true);
        if (key is null) return false;

        key.SetValue(ValueName, data, RegistryValueKind.Binary);
        return true;
    }
}
