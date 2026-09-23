using Microsoft.Win32;
using DispCtrl.Core.Displays;

namespace DispCtrl.Display;

/// <summary>What a display reports about adaptive sync, and whether it is on.</summary>
public readonly record struct VrrState(bool Capable, bool Enabled, uint MinHz, uint MaxHz)
{
    public static VrrState Unsupported => new(false, false, 0, 0);

    public string Range => Capable ? $"{MinHz}–{MaxHz} Hz" : "—";
}

/// <summary>
/// Variable refresh rate.
/// </summary>
/// <remarks>
/// Windows ships no API for this, which is why it is split in two here.
/// <list type="bullet">
/// <item><b>Capability</b> comes from the panel's own EDID. A monitor that
/// advertises a <em>range</em> of vertical refresh rates rather than a single
/// value is describing adaptive sync; a fixed-rate panel reports a range only a
/// hertz or two wide.</item>
/// <item><b>The toggle</b> is the same per-user DirectX setting the Graphics
/// settings page writes. It is global rather than per-display, which is a real
/// limitation of what Windows exposes, not of this code.</item>
/// </list>
/// </remarks>
public static class VariableRefreshRate
{
    private const string GpuPreferencesKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string GlobalSettingsValue = "DirectXUserGlobalSettings";
    private const string VrrFlag = "VRROptimizeEnable";

    /// <summary>
    /// Hertz of spread before a panel is treated as adaptive-sync capable.
    /// </summary>
    /// <remarks>
    /// Fixed-rate monitors still report a small range — a 60Hz panel commonly
    /// says 59-61 — so a non-zero spread alone would call everything capable.
    /// </remarks>
    private const uint CapableSpreadHz = 10;

    public static VrrState Read(DisplayInfo display)
    {
        (uint min, uint max) = Edid.RefreshRange(display.Key.DevicePath);
        bool capable = max > min && (max - min) >= CapableSpreadHz;

        return capable
            ? new VrrState(true, IsEnabled(), min, max)
            : VrrState.Unsupported;
    }

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(GpuPreferencesKey);
            string? settings = key?.GetValue(GlobalSettingsValue) as string;
            return settings is not null && settings.Contains($"{VrrFlag}=1", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Turns the global variable refresh rate optimisation on or off.
    /// </summary>
    /// <remarks>
    /// The value is a semicolon-separated list of flags that Windows also uses
    /// for other graphics options, so it is parsed and rewritten rather than
    /// replaced — overwriting it would silently discard whatever else the user
    /// had set there.
    /// </remarks>
    public static bool SetEnabled(bool enabled)
    {
        using var stateChange = new DisplayStateChange();
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(GpuPreferencesKey);
            string existing = key.GetValue(GlobalSettingsValue) as string ?? string.Empty;

            var flags = new List<string>();
            bool replaced = false;

            foreach (string part in existing.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.StartsWith($"{VrrFlag}=", StringComparison.Ordinal))
                {
                    flags.Add($"{VrrFlag}={(enabled ? 1 : 0)}");
                    replaced = true;
                }
                else
                {
                    flags.Add(part);
                }
            }

            if (!replaced) flags.Add($"{VrrFlag}={(enabled ? 1 : 0)}");

            key.SetValue(GlobalSettingsValue, string.Join(';', flags) + ";");
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
