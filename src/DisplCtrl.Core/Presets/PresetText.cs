using System.Text;

namespace DisplCtrl.Core.Presets;

/// <summary>
/// Renders a preset as readable text, for the report and for a submission.
/// </summary>
/// <remarks>
/// Prose rather than the JSON on disk. The file is the record; this is the
/// explanation, and someone reading a device record or deciding whether to
/// publish one needs to be able to see what a preset actually holds without
/// learning the schema.
/// <para>
/// Every field is written out, including the ones that are not recorded. "not
/// recorded" and "set to zero" are different states — a preset saved before a
/// field existed must not read as one that deliberately sets it — and a record
/// that silently drops the blanks cannot show the difference.
/// </para>
/// </remarks>
public static class PresetText
{
    /// <summary>One preset, in full.</summary>
    public static string Describe(Preset p)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"{p.Name}  (schema v{p.Version}, saved {p.SavedUtc:yyyy-MM-dd HH:mm} UTC)");
        if (!string.IsNullOrWhiteSpace(p.Description)) sb.AppendLine($"    {p.Description}");

        sb.AppendLine(p.IncludeGlobal ? "    Across the desk (restored)" : "    Across the desk (reference only; not restored)");
        Line(sb, "Restore layout", p.IncludeLayout ? "yes" : "no");
        foreach (string note in p.CaptureNotes) Line(sb, "Capture note", note);
        Line(sb, "Topology", p.Global.Topology);
        Line(sb, "Unison brightness", p.Global.UnisonBrightness
            ? $"on at {p.Global.UnisonLevel}%{(p.Global.UnisonCalibrated ? ", calibrated" : "")}"
            : "off");
        Line(sb, "Night light", p.Global.NightLightEnabled
            ? $"on at {p.Global.NightLightStrength}%"
              + (p.Global.NightLightUnison ? ", unison" : ", per display")
              + (p.Global.NightLightCalibrated ? ", calibrated" : "")
            : "off");
        Line(sb, "Night light schedule", p.Global.NightLightScheduled
            ? $"{Clock(p.Global.NightLightFrom)} to {Clock(p.Global.NightLightTo)}"
            : "not scheduled");
        Line(sb, "Wallpaper fit", p.Global.WallpaperFit.ToString());
        Line(sb, "Variable refresh", p.Global.VariableRefreshRate switch
        {
            true => "on",
            false => "off",
            null => "not recorded",
        });

        if (p.Global.Taskbar is { } bar)
        {
            Line(sb, "Taskbar hide delay", $"{bar.HideDelayMs} ms");
            Line(sb, "Taskbar animation", $"{bar.AnimMs} ms");
            Line(sb, "Taskbar reveal strip", $"{bar.RevealPx} px");
            Line(sb, "Taskbar arm distance", $"{bar.ArmDistancePx} px");
            Line(sb, "Taskbar polling", $"idle {bar.IdlePollMs}, far {bar.FarPollMs}, "
                + $"armed {bar.ArmedPollMs}, shown {bar.ShownPollMs} ms");
        }
        else
        {
            Line(sb, "Taskbar", "not recorded");
        }

        foreach ((string token, PresetMonitor m) in p.Monitors)
        {
            sb.AppendLine($"    Display {token}");

            Line(sb, "Label", m.Label ?? "not recorded");
            Line(sb, "Model", m.Model ?? "not recorded");
            Line(sb, "Serial", m.Serial ?? "not recorded");
            Line(sb, "Connector", m.Connector ?? "not recorded");
            Line(sb, "Physical size", m.PhysicalWidthMm > 0
                ? $"{m.PhysicalWidthMm} x {m.PhysicalHeightMm} mm" : "not recorded");
            Line(sb, "Rendered DPI", m.Dpi > 0 ? m.Dpi.ToString() : "not recorded");
            Line(sb, "Colour profile", m.ColorProfile ?? "not recorded");
            Line(sb, "Position", $"{m.X}, {m.Y}{(m.Primary ? "  (main display)" : "")}");
            Line(sb, "Mode", $"{m.Width} x {m.Height} @ {m.RefreshHz} Hz");
            Line(sb, "Scale", $"{m.ScalePercent}%");
            Line(sb, "Orientation", $"{m.OrientationDegrees} degrees");
            Line(sb, "HDR", m.Hdr ? "on" : "off");
            Line(sb, "Brightness", Level(m.Brightness));
            Line(sb, "Software brightness", $"{m.SoftwareBrightness}%");
            Line(sb, "Brightness calibration", m.BrightnessFloor >= 0 || m.BrightnessCeiling >= 0
                ? $"floor {Level(m.BrightnessFloor)}, ceiling {Level(m.BrightnessCeiling)}, "
                  + $"baseline {Level(m.BrightnessBaseline)}"
                : "not recorded");
            Line(sb, "Night light", Level(m.NightLightStrength));
            Line(sb, "Night light range", m.NightLightFloor >= 0 || m.NightLightCeiling >= 0
                ? $"floor {Level(m.NightLightFloor)}, ceiling {Level(m.NightLightCeiling)}"
                : "not recorded");
            Line(sb, "OLED", m.IsOled switch { true => "yes", false => "no", null => "not recorded" });
            Line(sb, "Wallpaper", m.WallpaperPath ?? "not recorded");
            Line(sb, "Hide the taskbar", m.HideTaskbar ? "yes" : "no");
            Line(sb, "Reclaim work area", m.ReclaimWorkArea ? "yes" : "no");
            foreach (var (code, value) in m.MonitorControls) Line(sb, $"Monitor control {code}", value.ToString());
        }

        return sb.ToString();
    }

    /// <summary>Every preset on this machine, in the order the store lists them.</summary>
    public static string DescribeAll(IReadOnlyList<Preset> presets, string? current = null)
    {
        if (presets.Count == 0) return "No presets are saved on this machine.\n";

        var sb = new StringBuilder();

        sb.AppendLine($"{presets.Count} preset(s) saved"
            + (string.IsNullOrWhiteSpace(current) ? "" : $", currently on \"{current}\""));
        sb.AppendLine();

        foreach (Preset p in presets)
        {
            sb.Append(Describe(p));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>A level that may not have been captured at all.</summary>
    private static string Level(int value) => value < 0 ? "not recorded" : $"{value}%";

    /// <summary>Minutes past midnight, as a clock time.</summary>
    private static string Clock(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    private static void Line(StringBuilder sb, string label, string value) =>
        sb.AppendLine($"      {label,-24} {value}");
}
