using System.Globalization;
using System.Text.RegularExpressions;

namespace DispCtrl.Linux.Hardware;

/// <summary>One output line from <c>xrandr --query</c>: the layout DispCtrl's
/// CCD code reads on Windows.</summary>
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

/// <summary>Reads the layout through the <c>xrandr</c> command.</summary>
/// <remarks>
/// Only reads. Ramps are written through libXrandr by <see cref="GammaRamp"/>:
/// <c>xrandr --gamma</c> takes a per-channel exponent, not a multiplier, so
/// white stays white and it cannot warm a screen (1:0.9:0.8 read back as
/// 1.0:1.1:1.3), and <c>--gamma</c> and <c>--brightness</c> written by separate
/// calls each re-derive the other from the current ramp.
/// </remarks>
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
        var result = Shell.Run("xrandr", ["--query"]);
        return result.Ok ? ParseQuery(result.Stdout) : [];
    }

    internal static IReadOnlyList<XRandROutput> ParseQuery(string stdout)
    {
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
}
