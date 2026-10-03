using System.Diagnostics;
using System.Text.Json;

namespace DispCtrl.Display.Placement;

/// <summary>Other tools that move windows, which DispCtrl's own placement must not fight.</summary>
public static class OtherWindowTools
{
    private static long _checkedAt = long.MinValue;
    private static bool _fancyZonesPlacesNewWindows;

    /// <summary>
    /// Whether PowerToys FancyZones is running and set to move new windows to
    /// their last zone. Looked at once every 30 seconds at most.
    /// </summary>
    /// <remarks>
    /// With both on, a new window was moved twice - to its last zone by
    /// FancyZones, then to the display in use by DispCtrl, or the other way
    /// round - and ended wherever the slower one put it. FancyZones' choice is
    /// the more specific one, so DispCtrl's new-window placement steps aside
    /// while it is in charge. Gathering and putting back are asked for, and go
    /// ahead.
    /// </remarks>
    public static bool FancyZonesPlacesNewWindows()
    {
        long now = Environment.TickCount64;
        if (_checkedAt != long.MinValue && now - _checkedAt < 30_000) return _fancyZonesPlacesNewWindows;
        _checkedAt = now;
        _fancyZonesPlacesNewWindows = Read();
        return _fancyZonesPlacesNewWindows;
    }

    private static bool Read()
    {
        try
        {
            Process[] running = Process.GetProcessesByName("PowerToys.FancyZones");
            bool on = running.Length > 0;
            foreach (Process p in running) p.Dispose();
            if (!on) return false;
            string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "PowerToys", "FancyZones", "settings.json");
            if (!File.Exists(file)) return false;
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            return doc.RootElement.TryGetProperty("properties", out JsonElement props)
                && props.TryGetProperty("fancyzones_appLastZone_moveWindows", out JsonElement setting)
                && setting.TryGetProperty("value", out JsonElement value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch (Exception) { return false; }
    }
}
