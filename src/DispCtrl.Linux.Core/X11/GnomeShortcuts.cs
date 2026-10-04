using System.Text.RegularExpressions;
using DispCtrl.Linux.Hardware;

namespace DispCtrl.Linux.X11;

/// <summary>A shortcut kept as one of GNOME's custom keyboard shortcuts, which
/// runs a command.</summary>
/// <remarks>
/// GNOME Shell takes the Super key for the overview (<c>overlay-key</c>), so an
/// X key grab of Super+Z is never delivered: measured, Ctrl+Alt+Z grabbed the
/// same way worked and Super+Z did nothing. Shortcuts GNOME itself runs do
/// work, and show in Settings &gt; Keyboard &gt; Custom Shortcuts, where they
/// can be seen and changed. DispCtrl adds its own entries at its own paths and
/// never edits another.
/// </remarks>
public static partial class GnomeShortcuts
{
    private const string ListSchema = "org.gnome.settings-daemon.plugins.media-keys";
    private const string EntrySchema = "org.gnome.settings-daemon.plugins.media-keys.custom-keybinding";
    private const string Base = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/";

    /// <summary>On a GNOME session with the media-keys schema installed.</summary>
    public static bool Available =>
        (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Contains("GNOME", StringComparison.OrdinalIgnoreCase)
        && Shell.TryWhich("gsettings")
        && Shell.Run("gsettings", ["get", ListSchema, "custom-keybindings"], 3000).Ok;

    /// <summary>GNOME's accelerator syntax: <c>&lt;Super&gt;z</c>.</summary>
    public static string Accelerator(Shortcut shortcut)
    {
        var text = "";
        if ((shortcut.Modifiers & Xlib.ControlMask) != 0) text += "<Control>";
        if ((shortcut.Modifiers & Xlib.Mod1Mask) != 0) text += "<Alt>";
        if ((shortcut.Modifiers & Xlib.ShiftMask) != 0) text += "<Shift>";
        if ((shortcut.Modifiers & Xlib.Mod4Mask) != 0) text += "<Super>";
        return text + shortcut.Keysym;
    }

    /// <summary>Adds or updates the entry <paramref name="id"/> (a path segment
    /// such as <c>dispctrl-snap</c>). Returns why it could not, or null.</summary>
    public static string? Set(string id, string name, string command, string accelerator)
    {
        string path = Base + id + "/";
        var paths = Paths();
        if (paths is null) return "GNOME's shortcut list could not be read";
        if (!paths.Contains(path))
        {
            paths.Add(path);
            if (!Write(ListSchema, "custom-keybindings", Array(paths))) return "GNOME's shortcut list could not be written";
        }
        string entry = $"{EntrySchema}:{path}";
        bool ok = Write(entry, "name", Quote(name)) & Write(entry, "command", Quote(command)) & Write(entry, "binding", Quote(accelerator));
        return ok ? null : "the shortcut could not be saved in GNOME's settings";
    }

    public static void Remove(string id)
    {
        string path = Base + id + "/";
        var paths = Paths();
        if (paths is null || !paths.Remove(path)) return;
        Write(ListSchema, "custom-keybindings", Array(paths));
        string entry = $"{EntrySchema}:{path}";
        foreach (var key in new[] { "name", "command", "binding" }) Shell.Run("gsettings", ["reset", entry, key], 3000);
    }

    /// <summary>The accelerator another GNOME custom shortcut already uses for
    /// <paramref name="accelerator"/>, by its name, so a clash is said.</summary>
    public static string? TakenBy(string accelerator, string exceptId)
    {
        foreach (var path in Paths() ?? [])
        {
            if (path.EndsWith($"/{exceptId}/", StringComparison.Ordinal)) continue;
            var entry = $"{EntrySchema}:{path}";
            var binding = Read(entry, "binding");
            if (binding is not null && string.Equals(binding, accelerator, StringComparison.OrdinalIgnoreCase))
                return Read(entry, "name") ?? path;
        }
        return null;
    }

    private static List<string>? Paths()
    {
        var raw = Read(ListSchema, "custom-keybindings");
        if (raw is null) return null;
        return QuotedString().Matches(raw).Select(m => m.Groups[1].Value).ToList();
    }

    private static string? Read(string schema, string key)
    {
        var r = Shell.Run("gsettings", ["get", schema, key], 3000);
        if (!r.Ok) return null;
        var text = r.Stdout.Trim();
        return text.Length >= 2 && text[0] == '\'' && text[^1] == '\'' ? text[1..^1] : text;
    }

    private static bool Write(string schema, string key, string value) =>
        Shell.Run("gsettings", ["set", schema, key, value], 3000).Ok;

    private static string Array(IEnumerable<string> items) => "[" + string.Join(", ", items.Select(Quote)) + "]";

    private static string Quote(string s) => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    [GeneratedRegex(@"'((?:[^'\\]|\\.)*)'")]
    private static partial Regex QuotedString();
}
