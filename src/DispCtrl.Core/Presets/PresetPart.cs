namespace DispCtrl.Core.Presets;

/// <summary>A part of the desk a preset can leave alone.</summary>
/// <remarks>
/// A preset still captures everything; a part it skips is neither restored nor
/// counted as a change. Asked for after brightness moved by the room's light
/// or a key press kept a preset "changed" all day: somebody who wants a
/// preset for the layout should not have it argue with their brightness.
/// Which parts are skipped is said on the preset's row, so the old trap - a
/// preset that quietly left part of the desk alone - stays shut.
/// Stored by name; never rename a member.
/// </remarks>
public enum PresetPart
{
    /// <summary>Arrangement, main display, resolution, refresh rate, scale, orientation, HDR, variable refresh.</summary>
    Layout,
    /// <summary>Hardware brightness, software dimming, unison and the calibrated ranges.</summary>
    Brightness,
    /// <summary>Night light, its schedule and each display's warmth.</summary>
    NightLight,
    /// <summary>Each display's wallpaper and how it fits.</summary>
    Wallpaper,
    /// <summary>The monitor's own controls: contrast, input, picture mode and the rest.</summary>
    Controls,
    /// <summary>Which taskbars hide, the work area, and the reveal timing.</summary>
    Taskbar,
    /// <summary>Where the open windows were.</summary>
    Windows,
}

public static class PresetParts
{
    public static IReadOnlyList<PresetPart> All { get; } = Enum.GetValues<PresetPart>();

    /// <summary>The words the app and the command line use for a part.</summary>
    public static string Label(PresetPart part) => part switch
    {
        PresetPart.Layout => "layout",
        PresetPart.Brightness => "brightness",
        PresetPart.NightLight => "night light",
        PresetPart.Wallpaper => "wallpaper",
        PresetPart.Controls => "monitor controls",
        PresetPart.Taskbar => "taskbar",
        _ => "windows",
    };

    /// <summary>A part from its name on the command line: layout, brightness, nightlight, wallpaper, controls, taskbar, windows.</summary>
    public static PresetPart? Parse(string word)
    {
        string key = word.Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        if (key.Equals("monitorcontrols", StringComparison.OrdinalIgnoreCase)) return PresetPart.Controls;
        // Names only: Enum.TryParse would take "3" for a part too.
        return Enum.GetNames<PresetPart>().FirstOrDefault(n => n.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } name
            ? Enum.Parse<PresetPart>(name) : null;
    }

    /// <summary>Which part a reported difference belongs to.</summary>
    public static PresetPart Of(PresetChange change) => change.What switch
    {
        "Topology" or "Variable refresh rate" or "Position" or "Main display" or "Resolution" or "Refresh rate"
            or "Scaling" or "Orientation" or "HDR" or "OLED panel" => PresetPart.Layout,
        "Brightness" or "Software dimming" or "Brightness range" or "Brightness baseline" or "Brightness calibration" => PresetPart.Brightness,
        "Wallpaper" or "Wallpaper fit" => PresetPart.Wallpaper,
        "Hide the taskbar" or "Reclaim the work area" => PresetPart.Taskbar,
        string what when what.StartsWith("Monitor control", StringComparison.Ordinal) => PresetPart.Controls,
        string what when what.StartsWith("Unison", StringComparison.Ordinal) => PresetPart.Brightness,
        string what when what.StartsWith("Night light", StringComparison.Ordinal) || what == "Warmth range" => PresetPart.NightLight,
        _ => PresetPart.Taskbar,
    };
}
