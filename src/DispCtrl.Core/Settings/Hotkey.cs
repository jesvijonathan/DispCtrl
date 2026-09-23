using System.Text.Json.Serialization;

namespace DispCtrl.Core.Settings;

/// <summary>What a hotkey does.</summary>
public enum HotkeyAction
{
    BrightnessUp,
    BrightnessDown,
    NightLightToggle,
    NightLightWarmer,
    NightLightCooler,
    ApplyPreset,
    NextInput,
    Identify,
    UnisonUp,
    UnisonDown,

    // Appended, never inserted: settings store the name, but an older build
    // reading a newer file should still find every name it knows where it was.
    UnisonToggle,
    FocusToggle,
    OledCareToggle,
    OledRestNow,
    KeepAwakeToggle,
    DarkModeToggle,
    QuickPanel,
    TaskbarToggle,
    TaskbarGlassToggle,
    ContrastUp,
    ContrastDown,
}

/// <summary>One global keyboard shortcut.</summary>
/// <remarks>
/// Stored as a virtual-key code plus modifier flags rather than as a string.
/// Parsing "Ctrl+Alt+Up" back into what <c>RegisterHotKey</c> wants is a
/// surprising amount of guesswork about layouts and synonyms, and the panel is
/// capturing a real key press anyway — it already has the code.
/// </remarks>
public sealed class Hotkey
{
    /// <summary>Win32 virtual-key code.</summary>
    public uint Key { get; set; }

    /// <summary>MOD_ALT 1, MOD_CONTROL 2, MOD_SHIFT 4, MOD_WIN 8.</summary>
    public uint Modifiers { get; set; }

    public HotkeyAction Action { get; set; }

    /// <summary>
    /// Which display, as the number shown in the panel. Zero means all.
    /// </summary>
    /// <remarks>
    /// A number rather than a token because a hotkey is about a position on the
    /// desk — "the left one" — not about a particular panel. Someone who swaps
    /// monitors expects the shortcut to keep meaning the left one.
    /// </remarks>
    public int Display { get; set; }

    /// <summary>Preset name, for <see cref="HotkeyAction.ApplyPreset"/>.</summary>
    public string? Preset { get; set; }

    /// <summary>How much a step changes, for the actions that step.</summary>
    public int Step { get; set; } = 10;

    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public bool IsComplete =>
        Key != 0
        && (Action != HotkeyAction.ApplyPreset || !string.IsNullOrWhiteSpace(Preset));

    /// <summary>
    /// The shortcut as a person reads it.
    /// </summary>
    /// <remarks>
    /// Built from the stored code rather than kept alongside it, so the two can
    /// never disagree about what the shortcut is.
    /// </remarks>
    public string Describe()
    {
        if (Key == 0) return "Not set";

        var parts = new List<string>(4);
        if ((Modifiers & 8) != 0) parts.Add("Win");
        if ((Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((Modifiers & 1) != 0) parts.Add("Alt");
        if ((Modifiers & 4) != 0) parts.Add("Shift");

        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Reads a shortcut written the way <see cref="Describe"/> writes one:
    /// <c>Ctrl + Alt + Up</c>, <c>Win+Shift+F9</c>.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="KeyName"/> rather than a second table, so a
    /// shortcut the page shows can always be typed back in on the command line.
    /// Needs at least one modifier: a bare key registered globally would stop
    /// that key working anywhere else.
    /// </remarks>
    public static bool TryParse(string text, out uint key, out uint modifiers)
    {
        key = 0;
        modifiers = 0;
        string[] parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        foreach (string part in parts[..^1])
        {
            uint flag = part.ToLowerInvariant() switch
            {
                "win" or "windows" => 8, "ctrl" or "control" => 2, "alt" => 1, "shift" => 4, _ => 0,
            };
            if (flag == 0) return false;
            modifiers |= flag;
        }
        string wanted = parts[^1].Replace(" ", "", StringComparison.Ordinal);
        for (uint code = 1; code < 256; code++)
        {
            string name = KeyName(code);
            if (name.StartsWith("Key ", StringComparison.Ordinal)) continue;
            if (!name.Replace(" ", "", StringComparison.Ordinal).Equals(wanted, StringComparison.OrdinalIgnoreCase)) continue;
            key = code;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The shortcuts a new desk starts with.
    /// </summary>
    /// <remarks>
    /// Ctrl+Alt, the combination least likely to be taken: Win is Windows' and
    /// Ctrl+Shift belongs to applications. Not Ctrl+Alt with the arrow keys,
    /// which many Intel graphics drivers take to rotate the screen. Page Up and
    /// Page Down for unison, because brightness is the thing reached for most;
    /// a letter for each switch, named for what it does.
    /// </remarks>
    public static List<Hotkey> Defaults()
    {
        const uint CtrlAlt = 1 | 2;
        return
        [
            new() { Modifiers = CtrlAlt, Key = 0x21, Action = HotkeyAction.UnisonUp, Step = 5 },      // Page Up
            new() { Modifiers = CtrlAlt, Key = 0x22, Action = HotkeyAction.UnisonDown, Step = 5 },    // Page Down
            new() { Modifiers = CtrlAlt, Key = 'N', Action = HotkeyAction.NightLightToggle },
            new() { Modifiers = CtrlAlt, Key = 'F', Action = HotkeyAction.FocusToggle },
            new() { Modifiers = CtrlAlt, Key = 'K', Action = HotkeyAction.KeepAwakeToggle },
            new() { Modifiers = CtrlAlt, Key = 'I', Action = HotkeyAction.Identify },
            new() { Modifiers = CtrlAlt, Key = 'D', Action = HotkeyAction.QuickPanel },
        ];
    }

    /// <summary>
    /// Adds the defaults once, to a desk that has never been offered them.
    /// </summary>
    /// <remarks>
    /// Only combinations not already bound, and never again after the first
    /// time - so a default somebody removed stays removed.
    /// </remarks>
    /// <returns>True when the settings changed and want saving.</returns>
    public static bool OfferDefaults(DispCtrlSettings settings)
    {
        if (settings.Global.HotkeyDefaultsOffered) return false;
        settings.Global.HotkeyDefaultsOffered = true;
        foreach (Hotkey d in Defaults())
            if (!settings.Hotkeys.Any(h => h.Key == d.Key && h.Modifiers == d.Modifiers)) settings.Hotkeys.Add(d);
        return true;
    }

    /// <summary>What the whole binding does, in words.</summary>
    public string DescribeAction()
    {
        string where = Display == 0 ? "every display" : $"display {Display}";

        return Action switch
        {
            HotkeyAction.BrightnessUp => $"Brightness up {Step}% on {where}",
            HotkeyAction.BrightnessDown => $"Brightness down {Step}% on {where}",
            HotkeyAction.NightLightToggle => "Turn night light on or off",
            HotkeyAction.NightLightWarmer => $"Night light warmer by {Step}%",
            HotkeyAction.NightLightCooler => $"Night light cooler by {Step}%",
            HotkeyAction.ApplyPreset => $"Apply the preset “{Preset}”",
            HotkeyAction.NextInput => $"Next input source on {where}",
            HotkeyAction.Identify => "Show the display numbers on screen",
            HotkeyAction.UnisonUp => $"Unison brightness up {Step}%",
            HotkeyAction.UnisonDown => $"Unison brightness down {Step}%",
            HotkeyAction.UnisonToggle => "Turn unison brightness on or off",
            HotkeyAction.FocusToggle => "Turn focus mode on or off",
            HotkeyAction.OledCareToggle => "Turn OLED care on or off",
            HotkeyAction.OledRestNow => $"Rest the OLED displays on {where}",
            HotkeyAction.KeepAwakeToggle => "Keep the computer awake, or let it sleep",
            HotkeyAction.DarkModeToggle => "Switch between dark and light mode",
            HotkeyAction.QuickPanel => "Open or close the quick panel",
            HotkeyAction.TaskbarToggle => $"Hide or show the taskbar on {where}",
            HotkeyAction.TaskbarGlassToggle => "Turn taskbar glass on or off",
            HotkeyAction.ContrastUp => $"Contrast up {Step}% on {where}",
            HotkeyAction.ContrastDown => $"Contrast down {Step}% on {where}",
            _ => Action.ToString(),
        };
    }

    /// <summary>
    /// A readable name for a virtual-key code.
    /// </summary>
    /// <remarks>
    /// Only the keys anyone binds. Anything else falls back to its code, which
    /// is honest and still identifies the key.
    /// </remarks>
    private static string KeyName(uint key) => key switch
    {
        >= 0x30 and <= 0x39 => ((char)key).ToString(),
        >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x70 and <= 0x87 => $"F{key - 0x6F}",
        0x21 => "Page Up",
        0x22 => "Page Down",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2D => "Insert",
        0x2E => "Delete",
        0x20 => "Space",
        0xBB => "Plus",
        0xBD => "Minus",
        0xAE => "Volume Down",
        0xAF => "Volume Up",
        _ => $"Key {key}",
    };
}
