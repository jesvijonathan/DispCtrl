namespace DisplCtrl.Core.Presets;

/// <summary>Validates both imported files and in-app edits before any hardware writes.</summary>
public static class PresetValidation
{
    public static void Validate(Preset p)
    {
        void Require(bool valid, string message) { if (!valid) throw new FormatException(message); }
        Require(p.Version is >= 1 and <= 3, "Unsupported preset version. Supported versions: 1–3.");
        Require(!string.IsNullOrWhiteSpace(p.Name), "Give the preset a name.");
        Require(p.Global is not null && p.Monitors is not null && p.CaptureNotes is not null,
            "Global, monitors and captureNotes cannot be null.");
        var g = p.Global!;
        Require(g.Topology is "Extend" or "Duplicate" or "InternalOnly" or "ExternalOnly", "Invalid topology.");
        Require(g.UnisonLevel is >= 0 and <= 100 && g.NightLightStrength is >= 0 and <= 100,
            "Global brightness and warmth must be 0–100.");
        Require(g.NightLightFrom is >= 0 and < 1440 && g.NightLightTo is >= 0 and < 1440,
            "Schedule times must be minutes after midnight (0–1439).");
        Require(g.WallpaperFit is >= 0 and <= 5, "Wallpaper fit must be 0–5.");
        if (g.Taskbar is { } t)
        {
            Require(t.HideDelayMs >= 0 && t.AnimMs >= 0 && t.RevealPx >= 0 && t.ArmDistancePx >= 0,
                "Taskbar dimensions and delays cannot be negative.");
            Require(t.IdlePollMs > 0 && t.FarPollMs > 0 && t.ArmedPollMs > 0 && t.ShownPollMs > 0,
                "Taskbar polling intervals must be positive.");
        }
        Require(p.Monitors!.Values.Count(m => m is not null && m.Primary) <= 1, "Only one display can be primary.");
        foreach (var (token, m) in p.Monitors!)
        {
            Require(!string.IsNullOrWhiteSpace(token) && m is not null, "Each monitor needs a token and state.");
            Require(m!.OrientationDegrees is 0 or 90 or 180 or 270, $"{token}: invalid orientation.");
            Require((m.Width == 0) == (m.Height == 0) && m.Width <= 65535 && m.Height <= 65535,
                $"{token}: provide both width and height, within 1–65535, or both zero for unrecorded.");
            Require(m.ScalePercent == 0 || m.ScalePercent is >= 100 and <= 500, $"{token}: invalid scale.");
            Require(m.Brightness is >= -1 and <= 100 && m.SoftwareBrightness is >= 10 and <= 100,
                $"{token}: brightness must be -1 or 0–100; software brightness 10–100.");
            foreach (int level in new[] { m.NightLightStrength, m.NightLightFloor, m.NightLightCeiling,
                m.BrightnessBaseline, m.BrightnessFloor, m.BrightnessCeiling })
                Require(level is >= -1 and <= 100, $"{token}: calibration values must be -1 or 0–100.");
            Require(m.MonitorControls is not null, $"{token}: monitorControls cannot be null.");
            foreach (var (code, value) in m.MonitorControls!)
            {
                string hex = code.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? code[2..] : code;
                Require(byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out _)
                    && value is >= 0 and <= 65535, $"{token}: invalid monitor control {code}.");
            }
        }
    }

    /// <summary>Retains a saved scope during re-capture, including disconnected monitors.</summary>
    public static Preset RetainScope(Preset fresh, Preset saved)
    {
        fresh.IncludeGlobal = saved.IncludeGlobal;
        fresh.IncludeLayout = saved.IncludeLayout;
        fresh.Description = saved.Description;
        if (!saved.IncludeGlobal) fresh.Global = saved.Global;
        foreach (string token in fresh.Monitors.Keys.ToList())
            if (!saved.Monitors.ContainsKey(token)) fresh.Monitors.Remove(token);
        foreach (var (token, state) in saved.Monitors)
            if (!fresh.Monitors.ContainsKey(token)) fresh.Monitors[token] = state;
        return fresh;
    }
}
