using DispCtrl.Core.Settings;

namespace DispCtrl.Core.Presets;

/// <summary>
/// A desk recognised by the displays on it, and the preset that belongs to it.
/// </summary>
/// <remarks>
/// DisplayMagician's whole product is a saved desk applied on demand. A
/// whole-desk preset already is one: its monitors are the desk, captured with
/// their layout, modes, brightness and the rest. What this adds is knowing
/// when that desk has been plugged in - the same set of displays attached,
/// no more and no fewer - so a preset marked for it applies by itself.
/// <para>
/// A preset names monitors by token, and a token can change (see
/// <see cref="MonitorAdoption"/>); a token recorded as a monitor's former one
/// counts as that monitor.
/// </para>
/// </remarks>
public static class DeskProfiles
{
    /// <summary>One string for a set of displays, whatever order they were found in.</summary>
    public static string Fingerprint(IEnumerable<string> tokens) =>
        string.Join("|", tokens.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    /// <summary>The token a monitor has now, given one it may have had before.</summary>
    public static string Current(string token, DispCtrlSettings settings)
    {
        if (settings.Monitors.ContainsKey(token)) return token;
        foreach (var (now, monitor) in settings.Monitors)
            if (monitor.FormerTokens.Contains(token)) return now;
        return token;
    }

    /// <summary>The preset with monitors it names by a former token renamed to the current one.</summary>
    /// <remarks>The same preset when nothing needs renaming; a copy otherwise, so the file is untouched.</remarks>
    public static Preset WithCurrentTokens(Preset preset, DispCtrlSettings settings)
    {
        if (preset.Monitors.Keys.All(t => Current(t, settings) == t)) return preset;
        Preset copy = preset.Copy();
        copy.Monitors = [];
        foreach (var (token, state) in preset.Monitors)
            copy.Monitors.TryAdd(Current(token, settings), state);
        return copy;
    }

    /// <summary>Whether a preset describes exactly the desk attached: a whole-desk capture of these displays.</summary>
    public static bool Covers(Preset preset, IReadOnlyCollection<string> attached, DispCtrlSettings settings)
    {
        if (!preset.IncludeLayout || preset.Monitors.Count != attached.Count) return false;
        var saved = preset.Monitors.Keys.Select(t => Current(t, settings)).ToHashSet(StringComparer.Ordinal);
        return saved.SetEquals(attached);
    }

    /// <summary>The preset to apply when this desk arrives: marked for it, and the first by name if several are.</summary>
    public static Preset? Due(IEnumerable<Preset> presets, IReadOnlyCollection<string> attached, DispCtrlSettings settings) =>
        presets.Where(p => p.ApplyWhenConnected && Covers(p, attached, settings))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
}
