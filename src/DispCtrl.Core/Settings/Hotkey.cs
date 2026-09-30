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
    DisplayMode,
    AmbientToggle,
    ReturnWindowsToggle,
    NewWindowsToggle,
    SoftwareDimUp,
    SoftwareDimDown,
    MakePrimary,
    HdrToggle,
    VariableRefreshToggle,
    VolumeUp,
    VolumeDown,
    MuteToggle,

    /// <summary>Runs <c>dispctrl</c> with whatever <see cref="Hotkey.Command"/> holds.</summary>
    RunCommand,

    /// <summary>Opens a program, script, document or link, as Explorer would.</summary>
    OpenProgram,

    /// <summary>Sets <see cref="Hotkey.Control"/> to <see cref="Hotkey.Value"/>: any standard or mapped monitor control.</summary>
    SetControl,

    /// <summary>Moves a choice control to its next value, wrapping round.</summary>
    NextControlValue,

    /// <summary>Moves a choice control to its previous value, wrapping round.</summary>
    PreviousControlValue,

    /// <summary>Raises a range control by <see cref="Hotkey.Step"/>.</summary>
    ControlUp,

    /// <summary>Lowers a range control by <see cref="Hotkey.Step"/>.</summary>
    ControlDown,

    /// <summary>Runs the custom feature named in <see cref="Hotkey.Feature"/>.</summary>
    RunFeature,
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

    /// <summary>
    /// Which arrangement, for <see cref="HotkeyAction.DisplayMode"/>: Extend,
    /// Duplicate, InternalOnly or ExternalOnly.
    /// </summary>
    /// <remarks>
    /// A string because the enum lives in DispCtrl.Display, which Core does not
    /// reference; the same four names a preset records its topology under.
    /// </remarks>
    public string? Mode { get; set; }

    /// <summary>
    /// The <c>dispctrl</c> arguments for <see cref="HotkeyAction.RunCommand"/>,
    /// or what <see cref="HotkeyAction.OpenProgram"/> opens.
    /// </summary>
    /// <remarks>
    /// The same two fields a custom quick-panel tile carries, and run the same
    /// way: the command line is DispCtrl's scriptable surface and owns the
    /// grammar, so a shortcut and a tile can never mean different things by the
    /// same words.
    /// </remarks>
    public string? Command { get; set; }

    /// <summary>Arguments for <see cref="HotkeyAction.OpenProgram"/>; unused by a command.</summary>
    public string? Arguments { get; set; }

    /// <summary>How much a step changes, for the actions that step.</summary>
    public int Step { get; set; } = 10;

    /// <summary>
    /// The monitor control a control action works on: its key as
    /// <c>display controls</c> lists it (<c>picture-mode</c>), its name, or its
    /// code (<c>0x15</c>).
    /// </summary>
    public string? Control { get; set; }

    /// <summary>What <see cref="HotkeyAction.SetControl"/> sets: a value's key or name, or a number.</summary>
    public string? Value { get; set; }

    /// <summary>The custom feature <see cref="HotkeyAction.RunFeature"/> runs.</summary>
    public string? Feature { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>The arrangements <see cref="Mode"/> may name.</summary>
    public static readonly string[] Modes = ["Extend", "Duplicate", "InternalOnly", "ExternalOnly"];

    /// <summary>Whether an action works on a monitor control named in <see cref="Control"/>.</summary>
    public static bool IsControlAction(HotkeyAction action) => action is HotkeyAction.SetControl
        or HotkeyAction.NextControlValue or HotkeyAction.PreviousControlValue or HotkeyAction.ControlUp or HotkeyAction.ControlDown;

    public static bool NeedsWorker(HotkeyAction action) => IsControlAction(action) || action is
        HotkeyAction.ContrastUp or HotkeyAction.ContrastDown or HotkeyAction.NextInput
        or HotkeyAction.VolumeUp or HotkeyAction.VolumeDown or HotkeyAction.MuteToggle
        or HotkeyAction.DisplayMode or HotkeyAction.MakePrimary or HotkeyAction.HdrToggle
        or HotkeyAction.GatherWindows or HotkeyAction.RunCommand or HotkeyAction.OpenProgram;

    /// <summary>
    /// The value a control action asks for, in the words <c>display control --value</c>
    /// takes: a value, <c>next</c>, <c>previous</c>, or a signed step.
    /// </summary>
    [JsonIgnore]
    public string? ControlRequest => Action switch
    {
        HotkeyAction.SetControl => Value,
        HotkeyAction.NextControlValue => "next",
        HotkeyAction.PreviousControlValue => "previous",
        HotkeyAction.ControlUp => "+" + Step,
        HotkeyAction.ControlDown => "-" + Step,
        _ => null,
    };

    [JsonIgnore]
    public bool IsComplete =>
        Key != 0
        && (Action != HotkeyAction.ApplyPreset || !string.IsNullOrWhiteSpace(Preset))
        && (Action != HotkeyAction.DisplayMode || Modes.Contains(Mode))
        && (Action is not (HotkeyAction.RunCommand or HotkeyAction.OpenProgram) || !string.IsNullOrWhiteSpace(Command))
        && (!IsControlAction(Action) || !string.IsNullOrWhiteSpace(Control))
        && (Action != HotkeyAction.SetControl || !string.IsNullOrWhiteSpace(Value))
        && (Action != HotkeyAction.RunFeature || !string.IsNullOrWhiteSpace(Feature));

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
    /// brightness on the same keys, and Ctrl+Alt+1 to 4 are the four
    /// arrangements Win+P offers, in Win+P's own order of usefulness.
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
            new() { Modifiers = CtrlAlt, Key = '1', Action = HotkeyAction.DisplayMode, Mode = "Extend", Enabled = false },
            new() { Modifiers = CtrlAlt, Key = '2', Action = HotkeyAction.DisplayMode, Mode = "Duplicate", Enabled = false },
            new() { Modifiers = CtrlAlt, Key = '3', Action = HotkeyAction.DisplayMode, Mode = "InternalOnly", Enabled = false },
            new() { Modifiers = CtrlAlt, Key = '4', Action = HotkeyAction.DisplayMode, Mode = "ExternalOnly", Enabled = false },
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
    public const int DefaultsVersion = 6;

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
    /// Added in version 6, switched off: four shortcuts for what Win+P offers,
    /// Ctrl+Alt+1 to 4. Off because each one reconfigures the display stack -
    /// seconds of black screen - and a mistyped digit is an expensive accident.
    /// </summary>
    private static readonly HotkeyAction[] AddedInVersion6 = [HotkeyAction.DisplayMode];

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
            // Four display-mode defaults share one action, so each is judged by
            // the arrangement it applies as well.
            if (settings.Hotkeys.Any(h => h.Action == d.Action && (d.Action != HotkeyAction.DisplayMode || h.Mode == d.Mode))) continue;
            if (from < 2 && AddedInVersion2.Contains(d.Action))
            {
                d.Enabled = false;
                settings.Hotkeys.Add(d);
            }
            else if (from < 3 && AddedInVersion3.Contains(d.Action)) settings.Hotkeys.Add(d);
            else if (from < 4 && AddedInVersion4.Contains(d.Action)) settings.Hotkeys.Add(d);
            else if (from < 5 && AddedInVersion5.Contains(d.Action)) settings.Hotkeys.Add(d);
            else if (from < 6 && AddedInVersion6.Contains(d.Action)) settings.Hotkeys.Add(d);
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
            HotkeyAction.DisplayMode => $"Switch the displays to {ModeName(Mode)}",
            HotkeyAction.AmbientToggle => "Follow the room's light, or stop following it",
            HotkeyAction.ReturnWindowsToggle => "Put windows back when a display returns, on or off",
            HotkeyAction.NewWindowsToggle => "Open new windows on the display in use, on or off",
            HotkeyAction.SoftwareDimUp => $"Software brightness up {Step}% on {where}",
            HotkeyAction.SoftwareDimDown => $"Software brightness down {Step}% on {where}",
            HotkeyAction.MakePrimary => Display == 0 ? "Make the display in use the main one" : $"Make display {Display} the main one",
            HotkeyAction.HdrToggle => $"Turn HDR on or off on {where}",
            HotkeyAction.VariableRefreshToggle => "Variable refresh rate on or off",
            HotkeyAction.VolumeUp => $"Monitor volume up {Step}% on {where}",
            HotkeyAction.VolumeDown => $"Monitor volume down {Step}% on {where}",
            HotkeyAction.MuteToggle => $"Mute or unmute {where}",
            HotkeyAction.RunCommand => $"Run: dispctrl {Command}",
            HotkeyAction.OpenProgram => $"Open: {Command} {Arguments}".TrimEnd(),
            HotkeyAction.SetControl => $"Set {Control} to {Value} on {where}",
            HotkeyAction.NextControlValue => $"Next {Control} on {where}",
            HotkeyAction.PreviousControlValue => $"Previous {Control} on {where}",
            HotkeyAction.ControlUp => $"{Control} up {Step} on {where}",
            HotkeyAction.ControlDown => $"{Control} down {Step} on {where}",
            HotkeyAction.RunFeature => $"Run the feature “{Feature}”",
            _ => Action.ToString(),
        };
    }

    /// <summary>An arrangement as Win+P names it.</summary>
    public static string ModeName(string? mode) => mode switch
    {
        "Extend" => "Extend",
        "Duplicate" => "Duplicate",
        "InternalOnly" => "PC screen only",
        "ExternalOnly" => "Second screen only",
        _ => "no arrangement chosen",
    };

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
