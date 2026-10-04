namespace DispCtrl.Linux.X11;

/// <summary>A global shortcut, written as people write them: <c>Super+Z</c>,
/// <c>Ctrl+Alt+Page Down</c>.</summary>
public sealed record Shortcut(uint Modifiers, string Keysym, string Text)
{
    private static readonly Dictionary<string, uint> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = Xlib.ControlMask, ["control"] = Xlib.ControlMask,
        ["alt"] = Xlib.Mod1Mask, ["shift"] = Xlib.ShiftMask,
        ["super"] = Xlib.Mod4Mask, ["win"] = Xlib.Mod4Mask, ["meta"] = Xlib.Mod4Mask,
    };

    private static readonly Dictionary<string, string> KeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pageup"] = "Prior", ["page up"] = "Prior", ["pagedown"] = "Next", ["page down"] = "Next",
        ["space"] = "space", ["enter"] = "Return", ["return"] = "Return", ["tab"] = "Tab",
        ["backspace"] = "BackSpace", ["delete"] = "Delete", ["insert"] = "Insert", ["home"] = "Home", ["end"] = "End",
        ["escape"] = "Escape", ["esc"] = "Escape", ["[" ] = "bracketleft", ["]"] = "bracketright",
        ["-"] = "minus", ["="] = "equal", [","] = "comma", ["."] = "period", ["/"] = "slash", [";"] = "semicolon",
        ["up"] = "Up", ["down"] = "Down", ["left"] = "Left", ["right"] = "Right",
    };

    /// <summary>Null for text that is not a shortcut, or one without a modifier:
    /// a bare key grabbed globally would stop that key typing anywhere.</summary>
    public static Shortcut? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Any(p => p.Length == 0)) return null;
        uint modifiers = 0;
        foreach (var part in parts[..^1])
        {
            if (!ModifierNames.TryGetValue(part, out var m)) return null;
            modifiers |= m;
        }
        string key = parts[^1];
        string? keysym = KeyNames.TryGetValue(key, out var named) ? named
            : key.Length == 1 && char.IsLetterOrDigit(key[0]) ? key.ToLowerInvariant()
            : key.Length is 2 or 3 && (key[0] is 'F' or 'f') && int.TryParse(key[1..], out int f) && f is >= 1 and <= 24 ? $"F{f}"
            : null;
        return keysym is null ? null : new Shortcut(modifiers, keysym, text.Trim());
    }

    /// <summary>The same shortcut whatever Caps Lock and Num Lock are doing: X
    /// counts them as modifiers, and a grab of Super+Z alone misses Super+Z
    /// with Num Lock on.</summary>
    public IEnumerable<uint> WithLocks()
    {
        yield return Modifiers;
        yield return Modifiers | Xlib.LockMask;
        yield return Modifiers | Xlib.Mod2Mask;
        yield return Modifiers | Xlib.LockMask | Xlib.Mod2Mask;
    }

    /// <summary>Whether a key event's state is this shortcut, locks aside.</summary>
    public bool Matches(uint state) =>
        (state & (Xlib.ControlMask | Xlib.Mod1Mask | Xlib.ShiftMask | Xlib.Mod4Mask)) == Modifiers;
}
