using Microsoft.Win32;

namespace DispCtrl.Core.Settings;

/// <summary>
/// Whether Windows shows DispCtrl's tray icon on the taskbar or behind the ^.
/// </summary>
/// <remarks>
/// In Core so both sides can use it: the customisation page's switch and the
/// tray icon's own menu change the same value.
/// <para>
/// Windows 11 puts every new notification area icon in the overflow, and an
/// application has no API to ask otherwise. What it has is the per-icon record
/// Windows keeps under <c>Control Panel\NotifyIconSettings</c>, one subkey per
/// icon keyed by the executable that shows it, whose <c>IsPromoted</c> value is
/// exactly the switch on Settings > Personalization > Taskbar > Other system
/// tray icons. This reads and writes that value - and only when somebody flips
/// that switch, on the page or on the icon's own menu, because where an icon
/// sits is the user's decision, not the application's.
/// </para>
/// <para>
/// The record is keyed on the engine's path, not the app's: the engine is the
/// process that owns the icon. It only exists once the icon has been shown at
/// least once, so until then there is nothing to change and the switch says so.
/// A record left behind by an older path - the rename left one for
/// <c>DisplCtrl.Engine.exe</c> - belongs to an icon that no longer exists and
/// is left alone.
/// </para>
/// </remarks>
public static class TrayIconPromotion
{
    private const string KeyPath = @"Control Panel\NotifyIconSettings";

    /// <summary>True, false, or null when Windows has no record of the icon yet.</summary>
    public static bool? IsPromoted(string? enginePath)
    {
        using RegistryKey? entry = Find(enginePath, writable: false);
        if (entry is null) return null;
        return entry.GetValue("IsPromoted") is int promoted && promoted != 0;
    }

    /// <returns>False when there is no record to change, or it could not be written.</returns>
    public static bool SetPromoted(string? enginePath, bool promoted)
    {
        try
        {
            using RegistryKey? entry = Find(enginePath, writable: true);
            if (entry is null) return false;
            entry.SetValue("IsPromoted", promoted ? 1 : 0, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static RegistryKey? Find(string? enginePath, bool writable)
    {
        if (string.IsNullOrEmpty(enginePath)) return null;

        try
        {
            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (root is null) return null;

            foreach (string name in root.GetSubKeyNames())
            {
                RegistryKey? entry = root.OpenSubKey(name, writable);
                if (entry?.GetValue("ExecutablePath") is string path
                    && string.Equals(Path.GetFullPath(path), Path.GetFullPath(enginePath), StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }

                entry?.Dispose();
            }
        }
        catch (Exception)
        {
            // An unreadable record reads as no record: the switch is disabled
            // rather than claiming a state it could not see.
        }

        return null;
    }
}
