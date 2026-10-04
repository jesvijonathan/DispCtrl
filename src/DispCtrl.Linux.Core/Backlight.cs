using System.Globalization;

namespace DispCtrl.Linux;

/// <summary>One /sys/class/backlight/* device - the built-in-panel path with
/// no DDC/CI channel, same machine fact this project's Windows side already
/// documents (the ASUS OLED has none either, and reaches brightness through
/// WMI instead). Reading needs no privilege; writing needs either root or a
/// udev rule granting the seat's user group write access to the sysfs node -
/// there is no unelevated-by-default story here the way Windows' session
/// brightness API gives one for free.</summary>
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
        var path = Path.Combine(Root, deviceName, "brightness");
        try
        {
            File.WriteAllText(path, value.ToString(CultureInfo.InvariantCulture));
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            error = $"permission denied writing {path} - needs root or a udev rule (e.g. SUBSYSTEM==\"backlight\", RUN+=\"/bin/chgrp video $sys$devpath/brightness\", RUN+=\"/bin/chmod g+w $sys$devpath/brightness\")";
            return false;
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryReadInt(string path, out int value)
    {
        value = 0;
        if (!File.Exists(path)) return false;
        var text = File.ReadAllText(path).Trim();
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
