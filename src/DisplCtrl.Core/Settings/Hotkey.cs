using System.Text.Json.Serialization;

namespace DisplCtrl.Core.Settings;

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
