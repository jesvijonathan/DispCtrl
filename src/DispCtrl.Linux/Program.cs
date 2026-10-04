using System.Globalization;

namespace DispCtrl.Linux;

/// <summary>
/// dispctrl-linux: the Linux command line for DDC/CI brightness (over
/// ddcutil), backlight brightness (over sysfs) and display layout/gamma
/// (over XRandR). See docs/LINUX.md for scope and docs/design/LINUX-PORT.md
/// for what is still ahead of it (taskbar, hotkeys, presets, Wayland, Snap
/// packaging). Mirrors dispctrl.exe's command vocabulary and exit codes
/// (0 done, 1 refused, 2 asked wrongly) on purpose, so the two stay a
/// recognisable pair rather than diverging designs.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0) return PrintUsage();
        if (args[0] == "engine") return Engine.Run();
        return Dispatch(args);
    }

    /// <summary>Runs one command's worth of args and returns its exit code,
    /// writing to whatever Console.Out/Error currently point at. Used
    /// directly by Main for the CLI, and by Engine.Run with Console.Out/Error
    /// temporarily swapped for per-request StringWriters, so the socket
    /// broker (`engine`, see docs/design/LINUX-PORT.md "Engine and IPC")
    /// reuses these same command bodies instead of a parallel copy.</summary>
    internal static int Dispatch(string[] args)
    {
        if (args.Length == 0) return PrintUsage();

        try
        {
            return args[0] switch
            {
                "doctor" => Doctor(),
                "displays" => Displays(),
                "brightness" => Brightness(args[1..]),
                "gamma" => Gamma(args[1..]),
                "help" or "--help" or "-h" => PrintUsage(),
                _ => Unknown(args[0]),
            };
        }
        catch (IndexOutOfRangeException)
        {
            Console.Error.WriteLine("dispctrl-linux: missing argument");
            return 2;
        }
    }

    private static int PrintUsage()
    {
        Console.WriteLine("""
            dispctrl-linux - Linux DDC/CI, backlight and XRandR control (docs/LINUX.md)

              doctor                                   check for xrandr, ddcutil, backlight, i2c
              displays                                  list XRandR outputs, ddcutil monitors, backlight devices
              brightness <0-100> --backlight <name>     write /sys/class/backlight/<name>/brightness (scaled to its max)
              brightness <0-100> --ddc <display-num>     write VCP 0x10 (brightness) via ddcutil
              brightness <0-1.0> --xrandr <output>       software dimming via xrandr --brightness (gamma scalar)
              gamma <r> <g> <b> --xrandr <output>        set a gamma ramp scalar per channel, e.g. 1.0 0.9 0.8
              engine                                     run as a resident Unix-socket command broker (see docs/LINUX.md "Engine")

            Exit codes: 0 done, 1 refused, 2 asked wrongly.
            """);
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"dispctrl-linux: unknown command '{command}'");
        return 2;
    }

    private static int Doctor()
    {
        Console.WriteLine($"xrandr        : {(XRandR.IsAvailable ? "found" : "MISSING (apt install x11-xserver-utils)")}");
        Console.WriteLine($"ddcutil       : {(Ddcutil.IsAvailable ? "found" : "MISSING (apt install ddcutil)")}");

        var buses = Directory.Exists("/dev") ? Directory.GetFiles("/dev", "i2c-*") : [];
        Console.WriteLine($"/dev/i2c-*    : {(buses.Length > 0 ? $"{buses.Length} bus(es)" : "none found")}");

        var backlights = Backlight.Enumerate();
        Console.WriteLine($"backlight     : {(backlights.Count > 0 ? string.Join(", ", backlights.Select(b => b.Name)) : "none found")}");

        bool sessionIsX11 = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "x11";
        Console.WriteLine($"session type  : {Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "unknown"}"
            + (sessionIsX11 ? "" : " (XRandR calls need an X11 or XWayland session; see LINUX-PORT.md \"Display configuration\")"));
        return 0;
    }

    private static int Displays()
    {
        if (XRandR.IsAvailable)
        {
            Console.WriteLine("-- XRandR outputs --");
            foreach (var output in XRandR.Query())
            {
                Console.WriteLine(output.Connected
                    ? $"{output.Name,-10} {(output.Primary ? "primary" : "       ")} {output.WidthPx}x{output.HeightPx}+{output.PosX}+{output.PosY}  {output.WidthMm}mm x {output.HeightMm}mm"
                    : $"{output.Name,-10} disconnected");
            }
        }
        else
        {
            Console.WriteLine("-- XRandR outputs -- (xrandr not found)");
        }

        Console.WriteLine();
        if (Ddcutil.IsAvailable)
        {
            Console.WriteLine("-- DDC/CI monitors (ddcutil) --");
            var monitors = Ddcutil.Detect();
            if (monitors.Count == 0)
            {
                Console.WriteLine("(none answered - a monitor with no DDC/CI channel, like a built-in panel, will never appear here)");
            }
            foreach (var m in monitors)
            {
                Console.WriteLine($"Display {m.DisplayNum,-3} {m.I2CBus,-12} {m.Model ?? "(model unknown)"} {m.Serial}");
            }
        }
        else
        {
            Console.WriteLine("-- DDC/CI monitors -- (ddcutil not found)");
        }

        Console.WriteLine();
        Console.WriteLine("-- Backlight devices (/sys/class/backlight) --");
        var backlights = Backlight.Enumerate();
        if (backlights.Count == 0)
        {
            Console.WriteLine("(none - this desk has no internal panel reachable this way, or amdgpu_bl/intel_backlight is not loaded)");
        }
        foreach (var b in backlights)
        {
            Console.WriteLine($"{b.Name,-14} {b.Current}/{b.Max}  ({b.Fraction:P0})");
        }

        return 0;
    }

    private static int Brightness(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("brightness: needs a value and a --backlight/--ddc/--xrandr target"); return 2; }

        string valueArg = args[0];
        var flags = ParseFlags(args[1..]);

        if (flags.TryGetValue("--backlight", out var backlightName))
        {
            if (!int.TryParse(valueArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct) || pct is < 0 or > 100)
            {
                Console.Error.WriteLine("brightness: value for --backlight must be 0-100");
                return 2;
            }
            var device = Backlight.Enumerate().FirstOrDefault(b => b.Name == backlightName);
            if (device is null) { Console.Error.WriteLine($"brightness: no backlight device '{backlightName}'"); return 2; }
            int raw = (int)Math.Round(device.Max * (pct / 100.0));
            if (!Backlight.TrySet(backlightName, raw, out var error))
            {
                Console.Error.WriteLine($"brightness: refused - {error}");
                return 1;
            }
            Console.WriteLine($"{backlightName}: {pct}% ({raw}/{device.Max})");
            return 0;
        }

        if (flags.TryGetValue("--ddc", out var ddcNum))
        {
            if (!int.TryParse(ddcNum, out var displayNum)) { Console.Error.WriteLine("brightness: --ddc needs a display number"); return 2; }
            if (!int.TryParse(valueArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct) || pct is < 0 or > 100)
            {
                Console.Error.WriteLine("brightness: value for --ddc must be 0-100");
                return 2;
            }
            if (!Ddcutil.IsAvailable) { Console.Error.WriteLine("brightness: ddcutil not found"); return 1; }
            const byte VcpBrightness = 0x10;
            bool ok = Ddcutil.SetVcp(displayNum, VcpBrightness, pct);
            if (!ok) { Console.Error.WriteLine("brightness: ddcutil setvcp refused (see docs/design/LINUX-PORT.md \"DDC/CI\")"); return 1; }
            Console.WriteLine($"display {displayNum}: brightness set to {pct}");
            return 0;
        }

        if (flags.TryGetValue("--xrandr", out var output))
        {
            if (!double.TryParse(valueArg, NumberStyles.Float, CultureInfo.InvariantCulture, out var factor) || factor is < 0 or > 1)
            {
                Console.Error.WriteLine("brightness: value for --xrandr must be 0.0-1.0 (it is a gamma scalar, not a hardware level)");
                return 2;
            }
            if (!XRandR.IsAvailable) { Console.Error.WriteLine("brightness: xrandr not found"); return 1; }
            bool ok = XRandR.SetSoftwareBrightness(output, factor);
            if (!ok) { Console.Error.WriteLine($"brightness: xrandr refused output '{output}'"); return 1; }
            Console.WriteLine($"{output}: software brightness {factor:P0}");
            return 0;
        }

        Console.Error.WriteLine("brightness: needs one of --backlight <name>, --ddc <display-num>, --xrandr <output>");
        return 2;
    }

    private static int Gamma(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("gamma: needs <r> <g> <b> and --xrandr <output>"); return 2; }

        var flags = ParseFlags(args[3..]);
        if (!flags.TryGetValue("--xrandr", out var output))
        {
            Console.Error.WriteLine("gamma: needs --xrandr <output>");
            return 2;
        }
        if (!double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var r)
            || !double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var g)
            || !double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
        {
            Console.Error.WriteLine("gamma: r/g/b must be numbers, e.g. 1.0 0.9 0.8");
            return 2;
        }
        if (!XRandR.IsAvailable) { Console.Error.WriteLine("gamma: xrandr not found"); return 1; }
        bool ok = XRandR.SetGamma(output, r, g, b);
        if (!ok) { Console.Error.WriteLine($"gamma: xrandr refused output '{output}'"); return 1; }
        Console.WriteLine($"{output}: gamma set to {r:0.###}:{g:0.###}:{b:0.###}");
        return 0;
    }

    private static Dictionary<string, string> ParseFlags(string[] args)
    {
        var result = new Dictionary<string, string>();
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal)) result[args[i]] = args[i + 1];
        }
        return result;
    }
}
