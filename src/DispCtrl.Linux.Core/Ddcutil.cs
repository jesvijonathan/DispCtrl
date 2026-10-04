using System.Globalization;
using System.Text.RegularExpressions;

namespace DispCtrl.Linux;

/// <summary>One monitor as ddcutil enumerates it. `ddcutil` numbers displays
/// per boot/enumeration order, not a stable identity - matching this
/// project's own note (docs/design/LINUX-PORT.md) to copy ddcutil's own
/// "&lt;mfg&gt;-&lt;model&gt;-&lt;product&gt;" convention for a persistent cache later,
/// rather than trusting the number across restarts.</summary>
public sealed record DdcMonitor(int DisplayNum, string? Model, string? Serial, string? I2CBus);

/// <summary>One VCP feature read back: current value and, where the monitor
/// reports one, its maximum - DispCtrl's own MonitorCapabilities.InterMessageMs
/// note (40ms between messages so a fast reply doesn't answer the wrong code)
/// applies here too, since ddcutil talks the same DDC/CI wire protocol.</summary>
public sealed record DdcVcpValue(int Current, int? Maximum);

/// <summary>Wraps the `ddcutil` CLI rather than binding libddcutil directly -
/// simpler to get right first and cheap to replace later. A future revision
/// should P/Invoke libddcutil.so instead, to avoid a process spawn (and
/// ddcutil's own multi-hundred-ms per-command latency) on every VCP read.</summary>
public static class Ddcutil
{
    public static bool IsAvailable => Shell.TryWhich("ddcutil");

    // "Display 1\n   I2C bus:  /dev/i2c-3\n   Monitor:  DEL:U2424H:ABCDE12345\n"
    // ddcutil also emits an "Invalid display" block for a bus with no working
    // DDC/CI (found on real hardware: an eDP laptop panel enumerates one, with
    // an EDID but "DDC communication failed"). It must end the previous block,
    // not merge into it - found only by running against real hardware, see
    // src/DispCtrl.Linux/README.md.
    private static readonly Regex DisplayHeader = new(@"^Display (?<num>\d+)$", RegexOptions.Compiled);
    private static readonly Regex InvalidDisplayHeader = new(@"^Invalid display$", RegexOptions.Compiled);
    private static readonly Regex I2CBus = new(@"^\s*I2C bus:\s*(?<bus>\S+)", RegexOptions.Compiled);
    private static readonly Regex MonitorLine = new(@"^\s*Monitor:\s*(?<mfg>[^:]*):(?<model>[^:]*):(?<serial>.*)$", RegexOptions.Compiled);

    public static IReadOnlyList<DdcMonitor> Detect()
    {
        var (exitCode, stdout, _) = Shell.Run("ddcutil", "detect --brief");
        if (exitCode != 0) return [];

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
                // A bus with no working DDC/CI channel (e.g. an internal panel) -
                // end whatever came before and record nothing for this block; it
                // has no display number to control by, and DispCtrl never writes
                // to a channel that did not answer.
                Flush();
                continue;
            }
            var bus = I2CBus.Match(line);
            if (bus.Success) { i2cBus = bus.Groups["bus"].Value; continue; }

            var monitor = MonitorLine.Match(line);
            if (monitor.Success)
            {
                model = monitor.Groups["model"].Value.Trim();
                serial = monitor.Groups["serial"].Value.Trim();
            }
        }
        Flush();
        return results;
    }

    // "VCP 10 C 62 100" - current 62, max 100. Some read-only codes omit the max.
    private static readonly Regex VcpLine = new(
        @"VCP\s+[0-9A-Fa-f]{2}\s+[A-Z]\s+(?<cur>\d+)(?:\s+(?<max>\d+))?",
        RegexOptions.Compiled);

    public static DdcVcpValue? GetVcp(int displayNum, byte vcpCode)
    {
        // --brief is required: the default getvcp output is prose
        // ("VCP code 0x10 (Brightness ): current value = 70, max value = 100"),
        // not the "VCP 10 C 70 100" form this parses - found only by running
        // both against real hardware (see src/DispCtrl.Linux/README.md).
        var (exitCode, stdout, _) = Shell.Run("ddcutil", $"--display {displayNum} getvcp x{vcpCode:x2} --brief");
        if (exitCode != 0) return null;
        var match = VcpLine.Match(stdout);
        if (!match.Success) return null;
        int current = int.Parse(match.Groups["cur"].Value, CultureInfo.InvariantCulture);
        int? max = match.Groups["max"].Success ? int.Parse(match.Groups["max"].Value, CultureInfo.InvariantCulture) : null;
        return new DdcVcpValue(current, max);
    }

    /// <summary>Never called with an unlisted or manufacturer-specific code -
    /// same rule as VcpControl.Settables on Windows. Brightness (0x10) is the
    /// one this covers.</summary>
    public static bool SetVcp(int displayNum, byte vcpCode, int value)
    {
        var (exitCode, _, _) = Shell.Run("ddcutil", $"--display {displayNum} setvcp x{vcpCode:x2} {value}");
        return exitCode == 0;
    }
}
