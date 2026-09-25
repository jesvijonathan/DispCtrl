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
    StayActiveToggle,
    DisplaysOffToggle,
    RestoreDisplays,
    PinWindow,
    UnpinAllWindows,
    GatherWindows,
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
    /// <para>
    /// Eight are on: brightness both ways, night light, the quick panel,
    /// Ctrl+Alt+L to turn the displays off - L as in Win+L, which locks and
    /// which Windows keeps for itself - Ctrl+Alt+Backspace, which puts every
    /// display back however DispCtrl left it: the one to give somebody looking
    /// at a black screen - and P and G, to pin the active window on top and to
    /// gather every window onto the display in use. Not Win+Ctrl+T, which
    /// PowerToys' Always On Top holds.
    /// The rest are set but off - a shortcut nobody asked for that fires by
    /// accident, or holds a combination another program wanted, is worse than
    /// one that is a switch away. Contrast takes Shift as well, beside
    /// brightness on the same keys.
    /// </para>
    /// </remarks>
    public static List<Hotkey> Defaults()
    {
        const uint CtrlAlt = 1 | 2, CtrlAltShift = 1 | 2 | 4;
        return
        [
            new() { Modifiers = CtrlAlt, Key = 0x21, Action = HotkeyAction.UnisonUp, Step = 5 },      // Page Up
            new() { Modifiers = CtrlAlt, Key = 0x22, Action = HotkeyAction.UnisonDown, Step = 5 },    // Page Down
            new() { Modifiers = CtrlAlt, Key = 'D', Action = HotkeyAction.QuickPanel },
            new() { Modifiers = CtrlAlt, Key = 'N', Action = HotkeyAction.NightLightToggle },
            new() { Modifiers = CtrlAlt, Key = 'L', Action = HotkeyAction.DisplaysOffToggle },
            new() { Modifiers = CtrlAlt, Key = 0x08, Action = HotkeyAction.RestoreDisplays },   // Backspace
            new() { Modifiers = CtrlAlt, Key = 'P', Action = HotkeyAction.PinWindow },
            new() { Modifiers = CtrlAlt, Key = 'G', Action = HotkeyAction.GatherWindows },
            new() { Modifiers = CtrlAlt, Key = 'U', Action = HotkeyAction.UnisonToggle, Enabled = false },
            new() { Modifiers = CtrlAlt, Key = 'F', Action = HotkeyAction.FocusToggle, Enabled = false },
            new() { Modifiers = CtrlAlt, Key = 'K', Action = HotkeyAction.KeepAwakeToggle, Enabled = false },
            new() { Modifiers = CtrlAlt, Key = 'I', Action = HotkeyAction.Identify, Enabled = false },
            new() { Modifiers = CtrlAlt, Key = 'M', Action = HotkeyAction.DarkModeToggle, Enabled = false },
            new() { Modifiers = CtrlAlt, Key = 'T', Action = HotkeyAction.TaskbarToggle, Enabled = false },
            new() { Modifiers = CtrlAlt, Key = 'O', Action = HotkeyAction.OledCareToggle, Enabled = false },
            new() { Modifiers = CtrlAltShift, Key = 0x21, Action = HotkeyAction.ContrastUp, Step = 5, Enabled = false },
            new() { Modifiers = CtrlAltShift, Key = 0x22, Action = HotkeyAction.ContrastDown, Step = 5, Enabled = false },
        ];
    }

    /// <summary>Why a combination may never reach DispCtrl, or null when nothing is known against it.</summary>
    /// <remarks>
    /// Windows and graphics drivers take some combinations before any program
    /// can: registering them either fails or succeeds and never fires. Said at
    /// the moment the keys are pressed, rather than left to be found out.
    /// </remarks>
    public static string? Caution(uint key, uint modifiers)
    {
        bool win = (modifiers & 8) != 0, ctrl = (modifiers & 2) != 0, alt = (modifiers & 1) != 0;
        if (win) return "Windows keeps most Win shortcuts for itself, so this one may never reach DispCtrl. Ctrl+Alt combinations are the safest.";
        if (ctrl && alt && key is >= 0x25 and <= 0x28) return "Some graphics drivers rotate the screen on Ctrl+Alt and an arrow key.";
        if (alt && !ctrl && key is 0x09 or 0x73 or 0x1B) return "Alt with Tab, F4 or Escape belongs to Windows.";
        if (ctrl && !alt && key == 0x1B) return "Ctrl+Escape opens Start.";
        return null;
    }

    /// <summary>The defaults version this build offers; see <see cref="OfferDefaults"/>.</summary>
    public const int DefaultsVersion = 5;

    /// <summary>Actions a defaults version added, offered to desks set up before it.</summary>
    private static readonly HotkeyAction[] AddedInVersion2 =
        [HotkeyAction.UnisonToggle, HotkeyAction.DarkModeToggle, HotkeyAction.TaskbarToggle, HotkeyAction.OledCareToggle,
         HotkeyAction.ContrastUp, HotkeyAction.ContrastDown];

    /// <summary>
    /// Added in version 3, and offered switched on - the one exception to
    /// offering new actions off, because turning the displays off is only any
    /// use as a keystroke that is already there. Never over a combination
    /// something else holds.
    /// </summary>
    private static readonly HotkeyAction[] AddedInVersion3 = [HotkeyAction.DisplaysOffToggle];

    /// <summary>Added in version 4, switched on for the same reason: a way back that has to be there already.</summary>
    private static readonly HotkeyAction[] AddedInVersion4 = [HotkeyAction.RestoreDisplays];

    /// <summary>
    /// Added in version 5, switched on: pinning a window and gathering windows
    /// are keystrokes by nature (the owner asked for both as shortcuts), and
    /// Ctrl+Alt+P and G collide with nothing Windows or the drivers hold.
    /// </summary>
    private static readonly HotkeyAction[] AddedInVersion5 = [HotkeyAction.PinWindow, HotkeyAction.GatherWindows];

    /// <summary>
    /// Adds the defaults once, to a desk that has never been offered them, and
    /// later defaults once to a desk that was offered earlier ones.
    /// </summary>
    /// <remarks>
    /// Only combinations not already bound. A desk set up before a version gets
    /// that version's new actions - switched off, except the displays-off and
    /// restore shortcuts of versions 3 and 4 - and never the older defaults
    /// again, so a default somebody removed stays removed.
    /// </remarks>
    /// <returns>True when the settings changed and want saving.</returns>
    public static bool OfferDefaults(DispCtrlSettings settings)
    {
        GlobalSettings g = settings.Global;
        int from = g.HotkeyDefaultsVersion > 0 ? g.HotkeyDefaultsVersion : g.HotkeyDefaultsOffered ? 1 : 0;
        if (from >= DefaultsVersion) return false;
        foreach (Hotkey d in Defaults())
        {
            if (settings.Hotkeys.Any(h => h.Key == d.Key && h.Modifiers == d.Modifiers)) continue;
            if (from == 0) { settings.Hotkeys.Add(d); continue; }
            if (settings.Hotkeys.Any(h => h.Action == d.Action)) continue;
            if (from < 2 && AddedInVersion2.Contains(d.Action))
            {
                d.Enabled = false;
                settings.Hotkeys.Add(d);
            }
            else if (from < 3 && AddedInVersion3.Contains(d.Action)) settings.Hotkeys.Add(d);
            else if (from < 4 && AddedInVersion4.Contains(d.Action)) settings.Hotkeys.Add(d);
            else if (from < 5 && AddedInVersion5.Contains(d.Action)) settings.Hotkeys.Add(d);
        }
        g.HotkeyDefaultsOffered = true;
        g.HotkeyDefaultsVersion = DefaultsVersion;
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
            HotkeyAction.StayActiveToggle => "Stay active on or off: screen on, never Away",
            HotkeyAction.DisplaysOffToggle => "Turn the displays off, or back on",
            HotkeyAction.RestoreDisplays => "Put every display back: undo dimming, night light, hiding and displays off",
            HotkeyAction.DarkModeToggle => "Switch between dark and light mode",
            HotkeyAction.QuickPanel => "Open or close the quick panel",
            HotkeyAction.TaskbarToggle => $"Hide or show the taskbar on {where}",
            HotkeyAction.TaskbarGlassToggle => "Turn taskbar glass on or off",
            HotkeyAction.ContrastUp => $"Contrast up {Step}% on {where}",
            HotkeyAction.ContrastDown => $"Contrast down {Step}% on {where}",
            HotkeyAction.PinWindow => "Pin the active window on top, or unpin it",
            HotkeyAction.UnpinAllWindows => "Unpin every window DispCtrl pinned",
            HotkeyAction.GatherWindows => Display == 0
                ? "Bring every window onto the display in use"
                : $"Bring every window onto display {Display}",
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
        0x08 => "Backspace",
        0xBB => "Plus",
        0xBD => "Minus",
        0xAE => "Volume Down",
        0xAF => "Volume Up",
        _ => $"Key {key}",
    };
}
