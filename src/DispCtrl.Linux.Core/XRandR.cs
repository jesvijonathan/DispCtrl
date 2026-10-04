using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DispCtrl.Linux;

/// <summary>One output line from `xrandr --query`: what DispCtrl's CCD/topology
/// code reads on Windows, XRandR gives for free without a clamp to fight (see
/// docs/design/LINUX-PORT.md, "Gamma / night light / dimming").</summary>
public sealed record XRandROutput(
    string Name,
    bool Connected,
    bool Primary,
    int WidthPx,
    int HeightPx,
    int PosX,
    int PosY,
    int WidthMm,
    int HeightMm);

/// <summary>Shells out to the `xrandr` binary rather than binding libxrandr
/// directly - simpler to get right first. A future revision should bind
/// Xlib/XRandR for gamma ramp writes directly, since per-ramp precision (not
/// just a scalar) is what DispCtrl's night-light and dimming maths needs.</summary>
public static class XRandR
{
    // "eDP connected primary 1920x1080+333+1440 (normal left inverted right x axis y axis) 344mm x 193mm"
    private static readonly Regex ConnectedLine = new(
        @"^(?<name>\S+)\s+connected\s+(?<primary>primary\s+)?(?:(?<w>\d+)x(?<h>\d+)\+(?<x>\d+)\+(?<y>\d+)\s+)?.*?(?:(?<mmw>\d+)mm x (?<mmh>\d+)mm)?$",
        RegexOptions.Compiled);

    private static readonly Regex DisconnectedLine = new(@"^(?<name>\S+)\s+disconnected\b", RegexOptions.Compiled);

    public static bool IsAvailable => Shell.TryWhich("xrandr");

    public static IReadOnlyList<XRandROutput> Query()
    {
        var (exitCode, stdout, _) = Shell.Run("xrandr", "--query");
        if (exitCode != 0) return [];

        var results = new List<XRandROutput>();
        foreach (var line in stdout.Split('\n'))
        {
            if (line.StartsWith("Screen ", StringComparison.Ordinal)) continue;
            if (line.Length == 0 || char.IsWhiteSpace(line[0])) continue; // mode lines, indented

            var connected = ConnectedLine.Match(line);
            if (connected.Success && line.Contains(" connected", StringComparison.Ordinal))
            {
                int w = ParseIntOr(connected.Groups["w"].Value, 0);
                int h = ParseIntOr(connected.Groups["h"].Value, 0);
                int x = ParseIntOr(connected.Groups["x"].Value, 0);
                int y = ParseIntOr(connected.Groups["y"].Value, 0);
                int mmw = ParseIntOr(connected.Groups["mmw"].Value, 0);
                int mmh = ParseIntOr(connected.Groups["mmh"].Value, 0);
                results.Add(new XRandROutput(
                    connected.Groups["name"].Value,
                    Connected: true,
                    Primary: connected.Groups["primary"].Success,
                    w, h, x, y, mmw, mmh));
                continue;
            }

            var disconnected = DisconnectedLine.Match(line);
            if (disconnected.Success)
            {
                results.Add(new XRandROutput(disconnected.Groups["name"].Value, Connected: false, Primary: false, 0, 0, 0, 0, 0, 0));
            }
        }
        return results;
    }

    private static int ParseIntOr(string s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>Per-channel gamma ramp scale, XRandR's own equivalent of the
    /// clamp DispCtrl fights on Windows (GammaRange) - there is none here to
    /// fight. Values are the same 0.1-10.0 range xrandr(1) documents.</summary>
    public static bool SetGamma(string output, double red, double green, double blue)
    {
        var arg = string.Create(CultureInfo.InvariantCulture, $"{red:0.###}:{green:0.###}:{blue:0.###}");
        var (exitCode, _, _) = Shell.Run("xrandr", $"--output {output} --gamma {arg}");
        return exitCode == 0;
    }

    /// <summary>XRandR's own "--brightness" is a gamma-ramp scalar, not a
    /// hardware brightness write - this is the software-dimming path, the
    /// same shade-overlay-adjacent idea DispCtrl already borrows from
    /// MonitorControl-mac, not a DDC/CI substitute.</summary>
    public static bool SetSoftwareBrightness(string output, double factor)
    {
        var arg = factor.ToString("0.###", CultureInfo.InvariantCulture);
        var (exitCode, _, _) = Shell.Run("xrandr", $"--output {output} --brightness {arg}");
        return exitCode == 0;
    }
}

internal static class Shell
{
    public static bool TryWhich(string exe)
    {
        var (exitCode, _, _) = Run("which", exe);
        return exitCode == 0;
    }

    public static (int ExitCode, string Stdout, string Stderr) Run(string fileName, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        try
        {
            process.Start();
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdout, stderr);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Binary not on PATH - callers check *.IsAvailable first where it matters.
            return (-1, string.Empty, $"{fileName}: not found");
        }
    }
}
