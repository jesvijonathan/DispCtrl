using DisplCtrl.Core.Displays;
using DisplCtrl.Display.Devices;
using DisplCtrl.Core.Presets;
using DisplCtrl.Core.Settings;
using DisplCtrl.Display.Presets;

namespace DisplCtrl.Display.Cli;

/// <summary>
/// Everything DisplCtrl can do, from a script.
/// </summary>
/// <remarks>
/// In the engine because the engine is the process that already exists,
/// already references every control assembly, and already runs without a
/// window. A separate CLI binary would duplicate all of that to save nothing.
/// <para>
/// Commands here do not talk to the running engine — there is no IPC, by
/// design. Hardware commands act directly; settings commands write
/// <c>settings.json</c>, which the running engine picks up through its watcher
/// within about a tenth of a second. That means the CLI works identically
/// whether or not the engine is running, which is what makes it usable in a
/// login script.
/// </para>
/// <para>
/// DDC/CI traffic from here is serialised against the engine and the panel by a
/// named mutex — see <c>DdcChannel</c>. Without it a script adjusting contrast
/// while the engine swept capabilities would get replies belonging to the other
/// conversation.
/// </para>
/// </remarks>
public static class CommandLine
{
    /// <summary>Runs a command, returning the process exit code.</summary>
    /// <remarks>
    /// Zero is success, one is a refusal the caller could have avoided (no such
    /// display, monitor would not take the value), two is a usage error. A
    /// script can tell "it did not work" from "you asked wrongly".
    /// </remarks>
    public static int Run(string verb, string[] args)
    {
        try
        {
            return verb switch
            {
                "brightness" => BrightnessCommand(args),
                "dim" => Dim(args),
                "contrast" => Vcp(args, 0x12, "contrast"),
                "volume" => Vcp(args, 0x62, "volume"),
                "sharpness" => Vcp(args, 0x87, "sharpness"),
                "input" => Input(args),
                "power" => Power(args),
                "vcp" => RawVcp(args),
                "unison" => Unison(args),
                "nightlight" => Nightlight(args),
                "preset" => Preset(args),
                "topology" => Topology(args),
                "refresh" => Refresh(args),
                "resolution" => Resolution(args),
                "primary" => Primary(args),
                "report" => Report(),
                "contribute" => Contribute(args),
                _ => Usage($"unknown command: {verb}"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"failed: {ex.Message}");
            return 1;
        }
    }

    // ------------------------------------------------------------ targeting --

    /// <summary>
    /// The displays a command applies to.
    /// </summary>
    /// <remarks>
    /// Numbered the way the panel numbers them — built-in first, then left to
    /// right — so "display 2" means the same thing in both. Windows' own order
    /// is a path enumeration order that has nothing to do with where a monitor
    /// physically is.
    /// <para>
    /// A token may be given instead, which is what a script should use if it
    /// must survive the monitors being rearranged.
    /// </para>
    /// </remarks>
    /// <summary>Resolves one display argument, for callers with their own parsing.</summary>
    public static List<DisplayInfo> Resolve(string which, out string? error) =>
        Targets(["--display", which], out error);

    private static List<DisplayInfo> Targets(string[] args, out string? error)
    {
        error = null;

        List<DisplayInfo> all = Sorted();
        string? which = Option(args, "--display") ?? Option(args, "-d");

        if (which is null || which.Equals("all", StringComparison.OrdinalIgnoreCase) || Has(args, "--all"))
            return all;

        if (int.TryParse(which, out int index))
        {
            if (index >= 1 && index <= all.Count) return [all[index - 1]];

            error = $"there is no display {index}; this machine has {all.Count}";
            return [];
        }

        foreach (DisplayInfo d in all)
            if (string.Equals(d.Token, which, StringComparison.OrdinalIgnoreCase)) return [d];

        error = $"no display matches '{which}'";
        return [];
    }

    public static List<DisplayInfo> Sorted()
    {
        List<DisplayInfo> all = DisplayRegistry.Enumerate();

        all.Sort((a, b) =>
        {
            if (a.IsInternal != b.IsInternal) return a.IsInternal ? -1 : 1;
            int byX = a.Bounds.Left.CompareTo(b.Bounds.Left);
            return byX != 0 ? byX : a.Bounds.Top.CompareTo(b.Bounds.Top);
        });

        return all;
    }

    private static string? Option(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            return i + 1 < args.Length ? args[i + 1] : null;
        }

        // Also accept --display=2, which is what people type.
        foreach (string arg in args)
            if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return arg[(name.Length + 1)..];

        return null;
    }

    private static bool Has(string[] args, string flag)
    {
        foreach (string arg in args)
            if (arg.Equals(flag, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    /// <summary>The first argument that is not an option or an option's value.</summary>
    /// <remarks>
    /// A leading dash is not enough to call something an option, because a
    /// negative offset looks exactly like one. `brightness -8` was read as an
    /// unknown switch whose value was `--display`, leaving `2` as the
    /// positional — so asking for eight percent dimmer set the monitor to two
    /// percent. A token that parses as a number is a value, whatever it starts
    /// with.
    /// </remarks>
    private static string? Positional(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (IsOption(args[i]))
            {
                // Skip its value too, unless it was written --name=value.
                if (!args[i].Contains('=')) i++;
                continue;
            }

            return args[i];
        }

        return null;
    }

    private static bool IsOption(string arg) =>
        arg.StartsWith('-') && !int.TryParse(arg, out _);

    // ----------------------------------------------------------- brightness --

    /// <remarks>
    /// Accepts an absolute level or a signed offset, because a hotkey or a
    /// script almost always wants "a bit brighter" rather than a number.
    /// </remarks>
    private static int BrightnessCommand(string[] args)
    {
        string? value = Positional(args);
        List<DisplayInfo> targets = Targets(args, out string? error);

        if (error is not null) return Fail(error);
        if (value is null) return Report(targets, d =>
        {
            BrightnessRange r = Brightness.Read(d);
            return r.Supported ? $"{r.Current}%" : "not supported";
        });

        bool relative = value.StartsWith('+') || value.StartsWith('-');
        if (!int.TryParse(value, out int number)) return Usage($"'{value}' is not a level or an offset");

        int failures = 0;
        foreach (DisplayInfo d in targets)
        {
            BrightnessRange range = Brightness.Read(d);
            if (!range.Supported)
            {
                Console.Error.WriteLine($"{d.Label}: reports no brightness control");
                failures++;
                continue;
            }

            int wanted = Math.Clamp(relative ? (int)range.Current + number : number, 0, 100);
            if (!Brightness.Write(d, (uint)wanted))
            {
                Console.Error.WriteLine($"{d.Label}: would not take {wanted}%");
                failures++;
                continue;
            }

            Console.WriteLine($"{d.Label}: {wanted}%");
        }

        return failures == 0 ? 0 : 1;
    }

    /// <summary>Software dimming, which is a setting rather than a hardware write.</summary>
    private static int Dim(string[] args)
    {
        string? value = Positional(args);
        List<DisplayInfo> targets = Targets(args, out string? error);
        if (error is not null) return Fail(error);

        DisplCtrlSettings settings = SettingsStore.Load();

        if (value is null)
            return Report(targets, d => $"{settings.SoftwareBrightnessFor(d.Token)}%");

        if (!int.TryParse(value, out int number)) return Usage($"'{value}' is not a level");

        foreach (DisplayInfo d in targets)
        {
            settings.For(d.Token).SoftwareBrightness = Math.Clamp(number, NightLight.MinimumDim, 100);
            Console.WriteLine($"{d.Label}: software brightness {settings.SoftwareBrightnessFor(d.Token)}%");
        }

        SettingsStore.Save(settings);
        return 0;
    }

    // ------------------------------------------------- the monitor's own --

    private static int Vcp(string[] args, byte code, string name)
    {
        string? value = Positional(args);
        List<DisplayInfo> targets = Targets(args, out string? error);
        if (error is not null) return Fail(error);

        if (value is null)
            return Report(targets, d => Describe(d, code) ?? $"no {name} control");

        if (!int.TryParse(value, out int number)) return Usage($"'{value}' is not a value");

        int failures = 0;
        foreach (DisplayInfo d in targets)
        {
            if (!Supports(d, code))
            {
                Console.Error.WriteLine($"{d.Label}: does not report a {name} control");
                failures++;
                continue;
            }

            if (!MonitorCapabilities.Write(d, code, (uint)number))
            {
                Console.Error.WriteLine($"{d.Label}: would not take {number}");
                failures++;
                continue;
            }

            Console.WriteLine($"{d.Label}: {name} {number}");
        }

        return failures == 0 ? 0 : 1;
    }

    /// <remarks>
    /// Input sources are named rather than numbered, because nobody knows that
    /// HDMI 1 is 0x11 and a script that hard-codes it breaks on the next monitor.
    /// </remarks>
    private static int Input(string[] args)
    {
        string? wanted = Positional(args);
        List<DisplayInfo> targets = Targets(args, out string? error);
        if (error is not null) return Fail(error);

        if (wanted is null)
            return Report(targets, d => Describe(d, 0x60) ?? "no input control");

        int failures = 0;
        foreach (DisplayInfo d in targets)
        {
            VcpControl? control = Find(d, 0x60);
            if (control is null)
            {
                Console.Error.WriteLine($"{d.Label}: does not report an input control");
                failures++;
                continue;
            }

            VcpValue? match = null;
            foreach (VcpValue v in control.Values)
            {
                if (!v.Name.Replace(" ", "").Equals(wanted.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)
                    && !$"0x{v.Value:X2}".Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                match = v;
                break;
            }

            if (match is null)
            {
                Console.Error.WriteLine(
                    $"{d.Label}: no input called '{wanted}'. It offers: "
                    + string.Join(", ", control.Values));
                failures++;
                continue;
            }

            if (!MonitorCapabilities.Write(d, 0x60, match.Value.Value))
            {
                Console.Error.WriteLine($"{d.Label}: would not switch to {match.Value.Name}");
                failures++;
                continue;
            }

            Console.WriteLine($"{d.Label}: input {match.Value.Name}");
        }

        return failures == 0 ? 0 : 1;
    }

    private static int Power(string[] args)
    {
        string? wanted = Positional(args);
        if (wanted is null) return Usage("power needs on, standby or off");

        byte value = wanted.ToLowerInvariant() switch
        {
            "on" => 0x01,
            "standby" => 0x02,
            "suspend" => 0x03,
            "off" => 0x04,
            _ => 0,
        };

        if (value == 0) return Usage($"'{wanted}' is not on, standby, suspend or off");

        List<DisplayInfo> targets = Targets(args, out string? error);
        if (error is not null) return Fail(error);

        int failures = 0;
        foreach (DisplayInfo d in targets)
        {
            if (!Supports(d, 0xD6))
            {
                Console.Error.WriteLine($"{d.Label}: does not report a power control");
                failures++;
                continue;
            }

            if (!MonitorCapabilities.Write(d, 0xD6, value)) failures++;
            else Console.WriteLine($"{d.Label}: power {wanted}");
        }

        return failures == 0 ? 0 : 1;
    }

    /// <summary>Any allow-listed code, for the things that have no verb.</summary>
    private static int RawVcp(string[] args)
    {
        string? code = Positional(args);
        if (code is null) return Usage("vcp needs a code, for example vcp 0x14");

        ReadOnlySpan<char> text = code.AsSpan();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];

        if (!byte.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out byte number))
            return Usage($"'{code}' is not a hex VCP code");

        var rest = new List<string>(args);
        rest.Remove(code);

        return Vcp([.. rest], number, code);
    }

    private static VcpControl? Find(DisplayInfo display, byte code)
    {
        foreach (VcpControl c in MonitorCapabilities.ReadSettable(display).Controls)
            if (c.Code == code && c.Settable) return c;

        return null;
    }

    private static bool Supports(DisplayInfo display, byte code) => Find(display, code) is not null;

    private static string? Describe(DisplayInfo display, byte code) => Find(display, code)?.Display;

    // ---------------------------------------------------------- settings --

    private static int Unison(string[] args)
    {
        string? value = Positional(args);
        DisplCtrlSettings settings = SettingsStore.Load();

        if (value is null)
        {
            Console.WriteLine(settings.Global.UnisonBrightness
                ? $"on, at {settings.Global.UnisonLevel}%"
                : "off");
            return 0;
        }

        switch (value.ToLowerInvariant())
        {
            case "on": settings.Global.UnisonBrightness = true; break;
            case "off": settings.Global.UnisonBrightness = false; break;
            default:
                if (!int.TryParse(value, out int level)) return Usage($"'{value}' is not on, off or a level");
                settings.Global.UnisonBrightness = true;
                settings.Global.UnisonLevel = Math.Clamp(level, 0, 100);
                break;
        }

        SettingsStore.Save(settings);
        Console.WriteLine(settings.Global.UnisonBrightness
            ? $"unison on, at {settings.Global.UnisonLevel}%"
            : "unison off");

        return 0;
    }

    private static int Nightlight(string[] args)
    {
        string? value = Positional(args);
        DisplCtrlSettings settings = SettingsStore.Load();
        NightLightSettings night = settings.Global.NightLight;

        if (value is null)
        {
            Console.WriteLine(night.Enabled
                ? $"on at {night.Strength}% ({NightLight.KelvinFor(night.Strength):0}K)"
                  + (night.Scheduled ? ", scheduled" : "")
                : "off");
            return 0;
        }

        switch (value.ToLowerInvariant())
        {
            case "on": night.Enabled = true; break;
            case "off": night.Enabled = false; break;
            default:
                if (!int.TryParse(value, out int strength)) return Usage($"'{value}' is not on, off or a strength");
                night.Enabled = true;
                night.Strength = Math.Clamp(strength, 5, 100);
                break;
        }

        string? from = Option(args, "--from");
        string? to = Option(args, "--to");
        if (from is not null && to is not null)
        {
            if (!TryMinutes(from, out int f) || !TryMinutes(to, out int t))
                return Usage("--from and --to want times like 20:00");

            night.Scheduled = true;
            night.FromMinutes = f;
            night.ToMinutes = t;
        }
        else if (Has(args, "--no-schedule"))
        {
            night.Scheduled = false;
        }

        SettingsStore.Save(settings);
        Console.WriteLine(night.Enabled ? $"night light on at {night.Strength}%" : "night light off");
        return 0;
    }

    private static bool TryMinutes(string text, out int minutes)
    {
        minutes = 0;
        string[] parts = text.Split(':');

        if (parts.Length != 2 || !int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m))
            return false;

        minutes = (((h * 60) + m) % 1440 + 1440) % 1440;
        return true;
    }

    // ----------------------------------------------------------- presets --

    private static int Preset(string[] args)
    {
        string? action = Positional(args);
        if (action is null) return Usage("preset needs list, apply, save or delete");

        List<Preset> presets = PresetStore.Load();

        switch (action.ToLowerInvariant())
        {
            case "list":
                if (presets.Count == 0) Console.WriteLine("no presets");
                foreach (Preset p in presets)
                    Console.WriteLine($"{p.Name}  ({p.Monitors.Count} display(s), saved {p.SavedUtc.LocalDateTime:yyyy-MM-dd HH:mm})");
                return 0;

            case "apply":
            case "save":
            case "delete":
                break;

            default:
                return Usage($"'{action}' is not list, apply, save or delete");
        }

        var rest = new List<string>(args);
        rest.Remove(action);
        string? name = Positional([.. rest]);

        if (name is null) return Usage($"preset {action} needs a name");

        DisplCtrlSettings settings = SettingsStore.Load();
        List<DisplayInfo> displays = Sorted();

        switch (action.ToLowerInvariant())
        {
            case "apply":
            {
                Preset? preset = PresetStore.Read(PresetStore.PathFor(name));
                if (preset is null) return Fail($"there is no preset called '{name}'");

                PresetResult result = PresetService.Apply(preset, displays, settings);
                SettingsStore.Save(settings);

                foreach (string note in result.Notes) Console.Error.WriteLine(note);
                Console.WriteLine(result.Ok ? $"applied '{name}'" : $"'{name}' was not fully applied");

                return result.Ok ? 0 : 1;
            }

            case "save":
            {
                Preset fresh = PresetService.Capture(name, displays, settings);

                // Keep the scope a preset already had; overwriting it with the
                // defaults would quietly widen what an existing preset controls.
                Preset? existing = PresetStore.Read(PresetStore.PathFor(name));


                PresetStore.Save(fresh);
                Console.WriteLine($"saved '{name}'");
                return 0;
            }

            default:
                if (!PresetStore.Exists(name)) return Fail($"there is no preset called '{name}'");

                PresetStore.Delete(name);
                Console.WriteLine($"deleted '{name}'");
                return 0;
        }
    }

    // ------------------------------------------------------------ layout --

    private static int Topology(string[] args)
    {
        string? value = Positional(args);
        if (value is null) return Usage("topology needs extend, duplicate, internal or external");

        DesktopArrangement wanted = value.ToLowerInvariant() switch
        {
            "extend" => DesktopArrangement.Extend,
            "duplicate" or "clone" => DesktopArrangement.Duplicate,
            "internal" => DesktopArrangement.InternalOnly,
            "external" => DesktopArrangement.ExternalOnly,
            _ => (DesktopArrangement)(-1),
        };

        if ((int)wanted < 0) return Usage($"'{value}' is not a topology");

        if (!DesktopLayout.Apply(wanted)) return Fail($"Windows refused {wanted}");

        Console.WriteLine($"topology {wanted}");
        return 0;
    }

    private static int Refresh(string[] args)
    {
        string? value = Positional(args);
        List<DisplayInfo> targets = Targets(args, out string? error);
        if (error is not null) return Fail(error);

        if (value is null) return Report(targets, d => $"{d.RefreshHz} Hz");
        if (!uint.TryParse(value.TrimEnd('h', 'z', 'H', 'Z'), out uint hz))
            return Usage($"'{value}' is not a refresh rate");

        int failures = 0;
        foreach (DisplayInfo d in targets)
        {
            if (!DisplayArrangement.SetRefreshRate(d, hz))
            {
                Console.Error.WriteLine($"{d.Label}: would not take {hz} Hz");
                failures++;
                continue;
            }

            Console.WriteLine($"{d.Label}: {hz} Hz");
        }

        return failures == 0 ? 0 : 1;
    }

    private static int Resolution(string[] args)
    {
        string? value = Positional(args);
        List<DisplayInfo> targets = Targets(args, out string? error);
        if (error is not null) return Fail(error);

        if (value is null) return Report(targets, d => $"{d.Bounds.Width} x {d.Bounds.Height}");

        string[] parts = value.ToLowerInvariant().Split('x');
        if (parts.Length != 2 || !uint.TryParse(parts[0], out uint w) || !uint.TryParse(parts[1], out uint h))
            return Usage($"'{value}' is not a resolution like 1920x1080");

        int failures = 0;
        foreach (DisplayInfo d in targets)
        {
            var mode = new DisplayMode(w, h, d.RefreshHz, d.BitsPerPixel);
            ModeChangeResult result = DisplayModes.Apply(d.GdiName, mode);

            if (result is ModeChangeResult.NotSupported or ModeChangeResult.Failed)
            {
                Console.Error.WriteLine($"{d.Label}: would not take {w}x{h}");
                failures++;
                continue;
            }

            Console.WriteLine($"{d.Label}: {w} x {h}");
        }

        return failures == 0 ? 0 : 1;
    }

    private static int Primary(string[] args)
    {
        List<DisplayInfo> all = Sorted();
        string? value = Positional(args);

        if (value is null)
        {
            foreach (DisplayInfo d in all)
                if (d.IsPrimary) Console.WriteLine(d.Label);

            return 0;
        }

        List<DisplayInfo> targets = Targets(["--display", value], out string? error);
        if (error is not null) return Fail(error);
        if (targets.Count != 1) return Usage("primary wants one display");

        if (!DisplayArrangement.SetPrimary(targets[0], all))
            return Fail($"Windows would not make {targets[0].Label} the main display");

        Console.WriteLine($"main display: {targets[0].Label}");
        return 0;
    }

    private static int Report()
    {
        string path = DisplayReport.Write(Sorted());
        Console.WriteLine(path);
        return 0;
    }

    /// <summary>
    /// Prepares a device record and, on request, opens it as an issue.
    /// </summary>
    /// <remarks>
    /// Prints by default and opens nothing. Publishing is an outward step that
    /// cannot be taken back, so it takes the extra word: someone running
    /// <c>dispctrl contribute</c> to see what it would send must not find it sent.
    /// </remarks>
    private static int Contribute(string[] args)
    {
        List<DisplayInfo> targets = Targets(args, out string? error);
        if (error is not null) return Usage(error);

        bool open = Has(args, "--open");
        bool quiet = Has(args, "--quiet");

        foreach (DisplayInfo d in targets)
        {
            Contribution c = DeviceContribution.Prepare(d, targets);

            if (!quiet)
            {
                Console.WriteLine(c.Body);
                Console.WriteLine();
            }

            if (c.Path.Length > 0) Console.WriteLine($"saved: {c.Path}");

            if (!open)
            {
                Console.WriteLine("not sent. Add --open to review and submit it at "
                    + $"github.com/{DeviceContribution.Repository}");
                continue;
            }

            if (!c.Prefilled)
                Console.WriteLine("too long for a prefilled issue — paste the saved file into the form");

            if (DeviceContribution.Open(c))
                Console.WriteLine("opened in your browser — review it, then press Submit");
            else
                Console.Error.WriteLine($"could not open a browser. The text is at {c.Path}");
        }

        return 0;
    }

    // ------------------------------------------------------------ output --

    private static int Report(List<DisplayInfo> targets, Func<DisplayInfo, string> read)
    {
        foreach (DisplayInfo d in targets) Console.WriteLine($"{d.Label}: {read(d)}");
        return 0;
    }

    /// <summary>
    /// The whole command surface, in one place.
    /// </summary>
    /// <remarks>
    /// Written out rather than generated from the switch. Generated help lists
    /// what exists; this says what each command is for and how the arguments
    /// behave, which is the part anyone actually needs.
    /// </remarks>
    private const string UsageText = """
        dispctrl — display control from the command line

        Displays
          displays                 list monitors, with the numbers these commands use
          enable  <n|token>        hide the taskbar on that monitor
          disable <n|token>        stop hiding it

        Brightness and colour
          brightness [<n>|+n|-n]   read, set, or nudge by a relative amount
          dim [<n>]                software dimming, for panels with no hardware control
          unison [on|off|<n>]      one level across every display
          nightlight [on|off|<n>]  warmth, 5-100
              --from 20:00 --to 07:00     schedule it
              --no-schedule               always on while enabled

        The monitor's own settings
          contrast [<n>]
          volume [<n>]
          sharpness [<n>]
          input [<name>]           switch source by name, for example "HDMI 1"
          power <on|standby|off>   the monitor's own power state
          vcp <code> [<value>]     any allow-listed VCP code, for example vcp 0x14

        Layout
          topology <extend|duplicate|internal|external>
          resolution [<WxH>]
          refresh [<hz>]
          primary [<n|token>]

        Presets
          preset list
          preset apply  <name>
          preset save   <name>
          preset delete <name>

          report                   write the display report, print its path

        Contributing
          contribute               show the device record for each monitor, send nothing
          contribute --open        open it as a prefilled issue to review and submit
            --quiet                print only the saved path
          Carries what the model is — controls, ranges, capabilities string. Never
          the serial, the device path, a file path, your user name or your settings.

        Targeting
          --display <n|token>      one display; --all for every one
          Without either, a command reads rather than writes and reports them all.
          <n> is the number from `displays`: built-in first, then left to right,
          the same numbering the panel shows.

        Exit codes
          0  done
          1  refused — no such display, or the monitor would not take it
          2  asked wrongly

        Examples
          dispctrl brightness -10 --all
          dispctrl nightlight 60 --from 20:00 --to 07:00
          dispctrl input "DisplayPort 1" --display 2
          dispctrl preset apply Evening
          dispctrl contribute --display 2 --open
        """;

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    /// <summary>Prints the command list, and an optional complaint first.</summary>
    public static int Usage(string? message)
    {
        TextWriter w = message is null ? Console.Out : Console.Error;
        if (message is not null) w.WriteLine(message);

        w.WriteLine(UsageText);
        return message is null ? 0 : 2;
    }
}
