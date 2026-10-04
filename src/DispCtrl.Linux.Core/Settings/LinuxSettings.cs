using System.Text.Json;
using System.Text.Json.Serialization;
using DispCtrl.Linux.Ramps;

namespace DispCtrl.Linux.Settings;

/// <summary>Everything the Linux client stores. Hardware levels (DDC/CI,
/// backlight) are not stored: the monitor holds them itself.</summary>
public sealed class LinuxSettings
{
    public NightLightSettings NightLight { get; set; } = new();

    /// <summary>Software dimming per XRandR output, 0.1 to 1. Absent means 1.</summary>
    public Dictionary<string, double> Dim { get; set; } = new(StringComparer.Ordinal);

    public SnapSettings Snap { get; set; } = new();

    /// <summary>Properties a newer build wrote: kept and saved back untouched.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>Pulls every value back inside its range, so a hand edit can
    /// never ask for a black screen or an unreadable time.</summary>
    public void Normalise()
    {
        NightLight ??= new NightLightSettings();
        NightLight.Normalise();
        Snap ??= new SnapSettings();
        Snap.Normalise();
        Dim ??= new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var key in Dim.Keys.ToList())
        {
            double value = Dim[key];
            if (double.IsNaN(value) || value >= 1) Dim.Remove(key);
            else Dim[key] = Math.Max(RampTarget.LowestDim, value);
        }
    }
}

public sealed class NightLightSettings
{
    public const int DefaultStrength = 50;

    public bool Enabled { get; set; }

    /// <summary>0 to 100: 6500 K to 1900 K (<see cref="Warmth.KelvinFor"/>).</summary>
    public int Strength { get; set; } = DefaultStrength;

    /// <summary>On: warm only between <see cref="From"/> and <see cref="To"/>.
    /// Off: warm whenever <see cref="Enabled"/>.</summary>
    public bool Scheduled { get; set; }

    public string From { get; set; } = "20:00";
    public string To { get; set; } = "07:00";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    public void Normalise()
    {
        Strength = Math.Clamp(Strength, 0, 100);
        if (!Schedule.TryParse(From, out var from)) from = new TimeOnly(20, 0);
        if (!Schedule.TryParse(To, out var to)) to = new TimeOnly(7, 0);
        From = Schedule.Format(from);
        To = Schedule.Format(to);
    }

    /// <summary>Whether warmth applies at <paramref name="now"/>.</summary>
    public bool ActiveAt(TimeOnly now)
    {
        if (!Enabled || Strength == 0) return false;
        if (!Scheduled) return true;
        Schedule.TryParse(From, out var from);
        Schedule.TryParse(To, out var to);
        return Schedule.IsActive(now, from, to);
    }
}

/// <summary>Snap layouts and Snap Assist (Linux only, X11).</summary>
public sealed class SnapSettings
{
    /// <summary>The engine watches for drags and holds the shortcut.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Dragging a window to the top centre of a display drops the
    /// layouts down, as on Windows 11.</summary>
    public bool DragToTop { get; set; } = true;

    /// <summary>The shortcut that opens the layouts for the window in front, in
    /// the form Super+Z, Ctrl+Alt+S. Empty: none.</summary>
    public string Shortcut { get; set; } = "Super+Z";

    /// <summary>After a window snaps, offer the other windows for the zones left.</summary>
    public bool Assist { get; set; } = true;

    /// <summary>Pixels between snapped windows and around them, 0 to 32.</summary>
    public int Gap { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    public void Normalise()
    {
        Gap = Math.Clamp(Gap, 0, 32);
        Shortcut = (Shortcut ?? "").Trim();
    }
}
