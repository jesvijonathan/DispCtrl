using DispCtrl.Linux.Hardware;

namespace DispCtrl.Linux.Graphics;

/// <summary>The desktop's own look - dark or light, its accent colour, its
/// interface font - so the overlays look like part of it rather than a
/// foreign window.</summary>
public sealed record DesktopTheme(bool Dark, Rgba Accent, string Font)
{
    public Rgba PanelBackground => Dark ? Rgba.Hex("#2c2c2c", 0.97) : Rgba.Hex("#fafafa", 0.97);
    public Rgba PanelBorder => Dark ? new Rgba(1, 1, 1, 0.10) : new Rgba(0, 0, 0, 0.12);
    public Rgba TileBackground => Dark ? new Rgba(0, 0, 0, 0.22) : new Rgba(0, 0, 0, 0.04);
    public Rgba TileBorder => Dark ? new Rgba(1, 1, 1, 0.08) : new Rgba(0, 0, 0, 0.08);
    public Rgba ZoneFill => Dark ? new Rgba(1, 1, 1, 0.14) : new Rgba(0, 0, 0, 0.10);
    public Rgba ZoneBorder => Dark ? new Rgba(1, 1, 1, 0.22) : new Rgba(0, 0, 0, 0.20);
    public Rgba Text => Dark ? new Rgba(1, 1, 1, 0.92) : new Rgba(0, 0, 0, 0.88);
    public Rgba SecondaryText => Dark ? new Rgba(1, 1, 1, 0.62) : new Rgba(0, 0, 0, 0.58);
    public Rgba CardBackground => Dark ? Rgba.Hex("#3a3a3a") : Rgba.Hex("#ffffff");
    /// <summary>A shadow strong enough to lift the panel off a dark desktop is a
    /// grey halo on a light one.</summary>
    public double ShadowAlpha => Dark ? 0.35 : 0.14;
    public Rgba Backdrop => new(0, 0, 0, Dark ? 0.30 : 0.18);

    public static DesktopTheme Fallback { get; } = new(true, Rgba.Hex("#3584e4"), "Sans 10");

    /// <summary>Read once per overlay from GNOME's settings; anything missing
    /// falls back to a dark theme and GNOME's blue.</summary>
    public static DesktopTheme Current()
    {
        if (!Shell.TryWhich("gsettings")) return Fallback;
        string? Get(string schema, string key)
        {
            var r = Shell.Run("gsettings", ["get", schema, key], 2000);
            return r.Ok ? r.Stdout.Trim().Trim('\'') : null;
        }

        const string iface = "org.gnome.desktop.interface";
        var scheme = Get(iface, "color-scheme");
        var gtkTheme = Get(iface, "gtk-theme") ?? "";
        bool dark = scheme == "prefer-dark" || gtkTheme.EndsWith("-dark", StringComparison.OrdinalIgnoreCase);
        var font = Get(iface, "font-name") ?? "Sans 10";
        var accent = AccentFrom(Get(iface, "accent-color"), gtkTheme);
        return new DesktopTheme(dark, accent, font);
    }

    // GNOME 47's accent names, and Ubuntu's Yaru variants (Yaru-blue-dark ...),
    // which is where Ubuntu 24.04 keeps the accent.
    private static readonly Dictionary<string, string> Accents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["blue"] = "#3584e4", ["teal"] = "#2190a4", ["green"] = "#3a944a", ["yellow"] = "#c88800",
        ["orange"] = "#ed5b00", ["red"] = "#e62d42", ["pink"] = "#d56199", ["purple"] = "#9141ac",
        ["slate"] = "#6f8396", ["bark"] = "#787859", ["sage"] = "#657b69", ["olive"] = "#4b8501",
        ["viridian"] = "#03875b", ["prussiangreen"] = "#308280", ["magenta"] = "#b34cb3",
    };

    internal static Rgba AccentFrom(string? accentColour, string gtkTheme)
    {
        if (accentColour is not null && Accents.TryGetValue(accentColour, out var hex)) return Rgba.Hex(hex);
        if (gtkTheme.StartsWith("Yaru", StringComparison.OrdinalIgnoreCase))
        {
            var parts = gtkTheme.Split('-');
            if (parts.Length > 1 && Accents.TryGetValue(parts[1], out var yaru)) return Rgba.Hex(yaru);
            return Rgba.Hex("#e95420"); // Yaru's default, Ubuntu orange
        }
        return Rgba.Hex("#3584e4");
    }
}
