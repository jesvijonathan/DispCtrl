using System.Globalization;

namespace DispCtrl.Control;

/// <summary>
/// The first command line's verbs, kept as aliases for the control API.
/// </summary>
/// <remarks>
/// <c>dispctrl brightness -10 --all</c>, <c>dispctrl input "HDMI 1" --display 2</c>
/// and the rest are in people's scripts and hotkeys, so they keep working - but
/// as words rewritten into a control command, not as a second implementation.
/// There was one, in the Display library, with its own targeting and its own
/// rules for what a display number meant; every fix had to be made twice and
/// some were not. Now <c>--display N</c> becomes <c>--monitor N</c>, a missing
/// one means every display, and the request runs through the same service the
/// app, the engine and the broker use.
/// </remarks>
public static class LegacyCommands
{
    /// <summary>The control command for an old verb, or null when the words are not one.</summary>
    /// <exception cref="ArgumentException">An old verb asked wrongly.</exception>
    public static string[]? Translate(IReadOnlyList<string> words)
    {
        if (words.Count == 0) return null;
        string verb = words[0].ToLowerInvariant();
        var rest = words.Skip(1).ToList();
        string target = TakeDisplay(rest);
        string? value = Positional(rest);
        string[] passthrough = [.. rest];

        string[]? control = verb switch
        {
            "list" => ["displays", "list"],
            "enable" or "disable" => Hide(value ?? Explicit(target), verb == "enable"),
            "brightness" => value is null ? ["display", "get", "--monitor", target, "--hardware"]
                : value[0] is '+' or '-' ? ["display", "set", "--monitor", target, "--brightness-by", Number(value.TrimStart('+'), verb)]
                : ["display", "set", "--monitor", target, "--brightness", Number(value, verb)],
            "dim" => value is null ? ["display", "get", "--monitor", target]
                : ["display", "set", "--monitor", target, "--dim", Number(value, verb)],
            "contrast" => Control(target, "0x12", value),
            "volume" => Control(target, "0x62", value),
            "sharpness" => Control(target, "0x87", value),
            "input" => Control(target, "0x60", value),
            "power" => Control(target, "0xD6", PowerValue(value)),
            "vcp" => Control(target, value ?? throw new ArgumentException("vcp needs a code, for example vcp 0x14"), Positional(rest, skip: 1)),
            "unison" => value?.ToLowerInvariant() switch
            {
                null => ["unison", "get"],
                "on" or "off" => ["unison", "set", "--enabled", value.ToLowerInvariant()],
                _ => ["unison", "set", "--enabled", "on", "--level", Number(value, verb)],
            },
            "nightlight" when value is not ("get" or "set" or "reset") => NightLight(value, rest),
            "topology" when value is not ("get" or "set") =>
                value is null ? ["topology", "get"] : ["topology", "set", "--mode", value.ToLowerInvariant() == "clone" ? "duplicate" : value.ToLowerInvariant()],
            "refresh" => value is null ? ["displays", "list"] : ["display", "set", "--monitor", target, "--refresh", Number(value.TrimEnd('h', 'z', 'H', 'Z'), verb)],
            "resolution" => value is null ? ["displays", "list"] : ["display", "set", "--monitor", target, "--resolution", value],
            "primary" => value is null && target == "all" ? ["displays", "list"] : ["display", "set", "--monitor", value ?? target, "--primary", "on"],
            "contribute" => ["devices", "contribute", .. target == "all" ? new[] { "--all" } : ["--monitor", target], .. passthrough.Where(w => w == "--open")],
            _ => null,
        };
        return control is null ? null : [.. control, .. Common(words)];
    }

    /// <summary>The old verbs, for help.</summary>
    public const string Help = """
    Short forms (the first command line's verbs; each runs the control command beside it)
      brightness [N|+N|-N] [--display N]    display get --hardware / display set --brightness N | --brightness-by N
      dim [N]                               display set --dim N (software dimming)
      contrast|volume|sharpness [N]         display control --name 0x12|0x62|0x87 [--value N]
      input [NAME]                          display control --name 0x60 [--value "HDMI 1"]
      power on|standby|off                  display control --name 0xD6 --value ...
      vcp CODE [N]                          display control --name CODE [--value N]
      unison [on|off|N]                     unison get|set
      nightlight [on|off|N] [--from 20:00 --to 07:00 | --no-schedule]   nightlight set
      topology extend|duplicate|internal|external                        topology set --mode
      resolution WxH  refresh HZ  primary N                              display set
      enable|disable N                      hide or show that display's taskbar
      contribute [--display N] [--open]     devices contribute
      Without --display, a command reads or changes every display.
    """;

    private static string[] Hide(string monitor, bool hide) =>
        ["settings", "set", "--monitor", monitor, "--path", "hideTaskbar", "--value", hide ? "true" : "false"];

    private static string[] Control(string target, string code, string? value) => value is null
        ? ["display", "control", "--monitor", target, "--name", code]
        : ["display", "control", "--monitor", target, "--name", code, "--value", value];

    private static string? PowerValue(string? value) => value?.ToLowerInvariant() switch
    {
        null => throw new ArgumentException("power needs on, standby or off"),
        "on" => "1",
        "standby" => "2",
        "suspend" => "3",
        "off" => "4",
        _ => throw new ArgumentException($"'{value}' is not on, standby, suspend or off"),
    };

    private static string[] NightLight(string? value, List<string> rest)
    {
        if (value is null) return ["nightlight", "get"];
        var words = new List<string> { "nightlight", "set" };
        switch (value.ToLowerInvariant())
        {
            case "on": words.AddRange(["--enabled", "on"]); break;
            case "off": words.AddRange(["--enabled", "off"]); break;
            default:
                int strength = int.Parse(Number(value, "nightlight"), CultureInfo.InvariantCulture);
                words.AddRange(["--enabled", "on", "--strength", Math.Clamp(strength, 5, 100).ToString(CultureInfo.InvariantCulture)]);
                break;
        }
        string? from = Option(rest, "--from"), to = Option(rest, "--to");
        if (from is not null && to is not null)
            words.AddRange(["--scheduled", "on", "--from-minutes", Minutes(from), "--to-minutes", Minutes(to)]);
        else if (from is not null || to is not null) throw new ArgumentException("--from and --to go together, as --from 20:00 --to 07:00");
        else if (rest.Contains("--no-schedule", StringComparer.OrdinalIgnoreCase)) words.AddRange(["--scheduled", "off"]);
        return [.. words];
    }

    private static string Minutes(string time)
    {
        string[] parts = time.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m))
            throw new ArgumentException("--from and --to want times like 20:00");
        return ((((h * 60) + m) % 1440 + 1440) % 1440).ToString(CultureInfo.InvariantCulture);
    }

    private static string Number(string value, string verb) =>
        int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n)
            ? n.ToString(CultureInfo.InvariantCulture)
            : throw new ArgumentException($"{verb}: '{value}' is not a number");

    /// <summary>Removes --display N, -d N or --display=N (and --all) and returns the target.</summary>
    private static string TakeDisplay(List<string> rest)
    {
        string target = "all";
        for (int i = 0; i < rest.Count; i++)
        {
            string word = rest[i];
            if (word.Equals("--all", StringComparison.OrdinalIgnoreCase)) { rest.RemoveAt(i--); continue; }
            if (word.StartsWith("--display=", StringComparison.OrdinalIgnoreCase)) { target = word[10..]; rest.RemoveAt(i--); continue; }
            if (word is "--display" or "-d" && i + 1 < rest.Count) { target = rest[i + 1]; rest.RemoveRange(i, 2); i--; }
        }
        return target;
    }

    private static string Explicit(string target) => target != "all" ? target
        : throw new ArgumentException("enable and disable need a display number or token");

    /// <summary>
    /// The first word that is not an option or an option's value. A word that
    /// parses as a number is a value, whatever it starts with: <c>brightness -8</c>
    /// is eight darker, not an option called 8.
    /// </summary>
    private static string? Positional(List<string> rest, int skip = 0)
    {
        for (int i = 0; i < rest.Count; i++)
        {
            string word = rest[i];
            if (word.StartsWith('-') && !int.TryParse(word, out _))
            {
                if (!word.Contains('=') && word is not ("--no-schedule" or "--open" or "--json" or "--text" or "--local" or "--dry-run")) i++;
                continue;
            }
            if (skip-- == 0) return word;
        }
        return null;
    }

    private static string? Option(List<string> rest, string name)
    {
        int at = rest.FindIndex(w => w.Equals(name, StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at + 1 < rest.Count ? rest[at + 1] : null;
    }

    /// <summary>The control terminal's own switches, carried through unchanged.</summary>
    private static IEnumerable<string> Common(IReadOnlyList<string> words) =>
        words.Where(w => w is "--json" or "--text" or "--local" or "--dry-run");
}
