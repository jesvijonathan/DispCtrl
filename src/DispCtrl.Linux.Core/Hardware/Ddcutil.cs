using System.Globalization;
using System.Text.RegularExpressions;

namespace DispCtrl.Linux.Hardware;

/// <summary>One monitor as ddcutil enumerates it. ddcutil numbers displays in
/// enumeration order, which is not a stable identity across boots or
/// hot-plugs; a persistent cache should key on ddcutil's own
/// <c>&lt;mfg&gt;-&lt;model&gt;-&lt;product&gt;</c> convention instead.</summary>
public sealed record DdcMonitor(int DisplayNum, string? Model, string? Serial, string? I2CBus);

/// <summary>One VCP feature read back: the current value and, where the
/// monitor reports one, its maximum.</summary>
public sealed record DdcVcpValue(int Current, int? Maximum);

/// <summary>Wraps the <c>ddcutil</c> command rather than binding libddcutil.</summary>
/// <remarks>
/// ddcutil already does what the Windows side had to learn the hard way: the
/// pause between messages, retries, and locking a bus against a second
/// ddcutil. A process per call costs a few hundred milliseconds, which is why
/// the window throttles slider writes rather than sending every step.
/// </remarks>
public static class Ddcutil
{
    public const byte VcpBrightness = 0x10;

    public static bool IsAvailable => Shell.TryWhich("ddcutil");

    // ddcutil also emits an "Invalid display" block for a bus with no working
    // DDC/CI (an eDP laptop panel: an EDID, but "DDC communication failed").
    // It must end the previous block, not merge into it - merged, the panel's
    // fields overwrote the real monitor's model, serial and bus.
    private static readonly Regex DisplayHeader = new(@"^Display (?<num>\d+)$", RegexOptions.Compiled);
    private static readonly Regex InvalidDisplayHeader = new(@"^Invalid display$", RegexOptions.Compiled);
    private static readonly Regex I2CBus = new(@"^\s*I2C bus:\s*(?<bus>\S+)", RegexOptions.Compiled);
    private static readonly Regex MonitorLine = new(@"^\s*Monitor:\s*(?<mfg>[^:]*):(?<model>[^:]*):(?<serial>.*)$", RegexOptions.Compiled);

    public static IReadOnlyList<DdcMonitor> Detect()
    {
        var result = Shell.Run("ddcutil", ["detect", "--brief"]);
        return result.Ok ? ParseDetect(result.Stdout) : [];
    }

    internal static IReadOnlyList<DdcMonitor> ParseDetect(string stdout)
    {
        var results = new List<DdcMonitor>();
        int? currentNum = null;
        string? i2cBus = null;
        string? model = null;
        string? serial = null;

        void Flush()
        {
            if (currentNum is int n) results.Add(new DdcMonitor(n, model, serial, i2cBus));
            currentNum = null; i2cBus = null; model = null; serial = null;
        }

        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var header = DisplayHeader.Match(line);
            if (header.Success)
            {
                Flush();
                currentNum = int.Parse(header.Groups["num"].Value, CultureInfo.InvariantCulture);
                continue;
            }
            if (InvalidDisplayHeader.IsMatch(line))
            {
                // No display number to control it by, and a channel that did
                // not answer is never written to.
                Flush();
                continue;
            }
            if (currentNum is null) continue;

            var bus = I2CBus.Match(line);
            if (bus.Success) { i2cBus = bus.Groups["bus"].Value; continue; }

            var monitor = MonitorLine.Match(line);
            if (monitor.Success)
            {
                model = NullIfEmpty(monitor.Groups["model"].Value.Trim());
                serial = NullIfEmpty(monitor.Groups["serial"].Value.Trim());
            }
        }
        Flush();
        return results;
    }

    // "VCP 10 C 62 100" - current 62, max 100. Some read-only codes omit the max.
    private static readonly Regex VcpLine = new(
        @"VCP\s+[0-9A-Fa-f]{2}\s+[A-Z]+\s+(?<cur>\d+)(?:\s+(?<max>\d+))?",
        RegexOptions.Compiled);

    public static DdcVcpValue? GetVcp(int displayNum, byte vcpCode)
    {
        // --brief is required: the default output is prose ("VCP code 0x10
        // (Brightness ): current value = 70, max value = 100") which this
        // never matched, so every read silently returned nothing.
        var result = Shell.Run("ddcutil", ["--display", displayNum.ToString(CultureInfo.InvariantCulture), "getvcp", $"x{vcpCode:x2}", "--brief"]);
        return result.Ok ? ParseVcp(result.Stdout) : null;
    }

    internal static DdcVcpValue? ParseVcp(string stdout)
    {
        var match = VcpLine.Match(stdout);
        if (!match.Success) return null;
        int current = int.Parse(match.Groups["cur"].Value, CultureInfo.InvariantCulture);
        int? max = match.Groups["max"].Success ? int.Parse(match.Groups["max"].Value, CultureInfo.InvariantCulture) : null;
        return new DdcVcpValue(current, max);
    }

    /// <summary>Writes a VCP code. Only ever called with brightness (0x10),
    /// a standard code every DDC/CI monitor lists - the same rule as
    /// <c>VcpControl.Settables</c> on Windows: never a code the monitor did not
    /// list, never a manufacturer-specific one.</summary>
    public static bool SetVcp(int displayNum, byte vcpCode, int value, out string? error)
    {
        var result = Shell.Run("ddcutil", ["--display", displayNum.ToString(CultureInfo.InvariantCulture), "setvcp", $"x{vcpCode:x2}", value.ToString(CultureInfo.InvariantCulture)]);
        error = result.Ok ? null : FirstLine(result.Stderr) ?? FirstLine(result.Stdout) ?? $"ddcutil exited with {result.ExitCode}";
        return result.Ok;
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static string? FirstLine(string s) =>
        s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
}
