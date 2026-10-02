using System.Text.Json;
using DispCtrl.Core.Displays;

namespace DispCtrl.Core.Settings;

/// <summary>
/// Gives an attached display the settings saved for it under a former token.
/// </summary>
/// <remarks>
/// A display's token changes when the thing it was made from changes: a panel
/// with no serial was keyed by its whole device path until that path proved to
/// renumber after docks and driver updates, and a placeholder serial
/// (0x01010101) is no longer taken as an identity. Either way the display came
/// back as a stranger, with default settings, while its calibration, night
/// light range, taskbar and OLED choices sat under a token nothing matched.
/// <para>
/// Adopted only when unambiguous: the display has nothing of its own, it is the
/// only one of its model attached without settings, and the entry it takes is
/// of the same model and not attached itself. Several such entries - a laptop
/// renumbered more than once - go to the one seen most recently. Two identical
/// serial-less monitors arriving together adopt nothing; guessing which is
/// which would swap their settings.
/// </para>
/// </remarks>
public static class MonitorAdoption
{
    /// <summary>One move of a settings entry from a former token to the current one.</summary>
    public readonly record struct Move(string From, string To);

    /// <summary>Adopts what can be adopted, in <paramref name="settings"/>; returns the moves made.</summary>
    public static List<Move> Adopt(DispCtrlSettings settings, IReadOnlyList<DisplayInfo> attached) =>
        Adopt(settings, attached.Select(d => (d.Token, d.Key.Model)).ToList());

    /// <summary>The same, from tokens and models: what the checks drive.</summary>
    public static List<Move> Adopt(DispCtrlSettings settings, IReadOnlyList<(string Token, string Model)> attached)
    {
        var moves = new List<Move>();
        var attachedTokens = attached.Select(a => a.Token).ToHashSet(StringComparer.Ordinal);
        foreach (var group in attached.GroupBy(a => a.Model, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(group.Key)) continue;
            var homeless = group.Where(a => !settings.Monitors.TryGetValue(a.Token, out MonitorSettings? own) || IsPristine(own)).ToList();
            if (homeless.Count != 1) continue;
            string prefix = Prefix(group.Key);
            string? from = settings.Monitors
                .Where(p => !attachedTokens.Contains(p.Key) && p.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !IsPristine(p.Value))
                .OrderByDescending(p => p.Value.LastSeenUtc ?? DateTimeOffset.MinValue)
                .Select(p => p.Key).FirstOrDefault();
            if (from is null) continue;
            string to = homeless[0].Token;
            MonitorSettings moved = settings.Monitors[from];
            settings.Monitors.Remove(from);
            settings.Monitors[to] = moved;
            if (!moved.FormerTokens.Contains(from)) moved.FormerTokens.Add(from);
            Rename(settings.Global.QuickPanel.HiddenDisplays, from, to);
            Rename(settings.Global.QuickPanel.Collapsed, "display:" + from, "display:" + to);
            Rename(settings.Global.QuickPanel.Expanded, "display:" + from, "display:" + to);
            moves.Add(new Move(from, to));
        }
        return moves;
    }

    /// <summary>The token prefix every entry of a model shares: <c>DEL-A234-</c>.</summary>
    private static string Prefix(string model)
    {
        Span<char> buf = stackalloc char[model.Length];
        for (int i = 0; i < model.Length; i++)
            buf[i] = char.IsLetterOrDigit(model[i]) || model[i] is '-' or '_' ? model[i] : '-';
        return new string(buf) + "-";
    }

    private static void Rename(List<string> list, string from, string to)
    {
        int at = list.IndexOf(from);
        if (at < 0) return;
        if (list.Contains(to)) list.RemoveAt(at);
        else list[at] = to;
    }

    private static readonly string Default = Shape(new MonitorSettings());

    /// <summary>
    /// An entry nobody has set anything in: what <c>For</c> creates on first
    /// sight, and the app or engine may have saved before adoption ran.
    /// </summary>
    private static bool IsPristine(MonitorSettings m) => Shape(m) == Default;

    private static string Shape(MonitorSettings m)
    {
        var copy = JsonSerializer.Deserialize(JsonSerializer.Serialize(m, SettingsJsonContext.Default.MonitorSettings),
            SettingsJsonContext.Default.MonitorSettings)!;
        copy.LastSeenUtc = null;
        copy.Label = null;
        return JsonSerializer.Serialize(copy, SettingsJsonContext.Default.MonitorSettings);
    }
}
