using System.Globalization;

namespace DispCtrl.Linux.Hardware;

/// <summary>One /sys/class/backlight/* device - the built-in-panel path with
/// no DDC/CI channel, same machine fact this project's Windows side already
/// documents (the ASUS OLED has none either, and reaches brightness through
/// WMI instead). Reading needs no privilege. Writing goes to sysfs when this
/// account may (root, or a udev rule and the video group), and otherwise
/// through systemd-logind, which allows the active session's user.</summary>
public sealed record BacklightDevice(string Name, int Current, int Max)
{
    public double Fraction => Max == 0 ? 0 : (double)Current / Max;
}

public static class Backlight
{
    private const string Root = "/sys/class/backlight";

    public static IReadOnlyList<BacklightDevice> Enumerate()
    {
        if (!Directory.Exists(Root)) return [];

        var results = new List<BacklightDevice>();
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            string name = Path.GetFileName(dir);
            if (!TryReadInt(Path.Combine(dir, "brightness"), out int current)) continue;
            if (!TryReadInt(Path.Combine(dir, "max_brightness"), out int max)) continue;
            results.Add(new BacklightDevice(name, current, max));
        }
        return results;
    }

    /// <summary>Absolute value in the device's own 0..max_brightness range -
    /// callers wanting a 0-100% scale should read Enumerate() first and
    /// multiply by the device's own Max, the same "never assume 0-100" rule
    /// DispCtrl's Windows brightness code already follows for VCP ranges.</summary>
    public static bool TrySet(string deviceName, int value, out string? error)
    {
        error = null;
        // The name reaches a sysfs path: only a device that enumerates is
        // accepted, so "../.." can never point the write somewhere else.
        if (!Enumerate().Any(b => b.Name == deviceName))
        {
            error = $"no backlight device '{deviceName}'";
            return false;
        }
        var path = Path.Combine(Root, deviceName, "brightness");
        try
        {
            File.WriteAllText(path, value.ToString(CultureInfo.InvariantCulture));
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // The usual case: sysfs is root's. logind lets the active session's
            // own user set it, with no udev rule or group membership.
            if (TrySetThroughLogind(deviceName, value, out var logindError)) return true;
            error = $"permission denied writing {path}, and logind refused too ({logindError}). "
                + "Install the backlight udev rule and join the video group (docs/LINUX.md, \"Backlight\"), or run from the active local session.";
            return false;
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>systemd-logind's <c>Session.SetBrightness</c> (systemd 243 and
    /// later), through busctl so no D-Bus library is needed.</summary>
    private static bool TrySetThroughLogind(string deviceName, int value, out string? error)
    {
        if (!Shell.TryWhich("busctl"))
        {
            error = "busctl not found";
            return false;
        }
        var result = Shell.Run("busctl",
        [
            "call", "org.freedesktop.login1", "/org/freedesktop/login1/session/auto",
            "org.freedesktop.login1.Session", "SetBrightness", "ssu",
            "backlight", deviceName, value.ToString(CultureInfo.InvariantCulture),
        ], timeoutMs: 5000);
        error = result.Ok ? null : result.Stderr.Trim();
        return result.Ok;
    }

    private static bool TryReadInt(string path, out int value)
    {
        value = 0;
        try
        {
            var text = File.ReadAllText(path).Trim();
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
