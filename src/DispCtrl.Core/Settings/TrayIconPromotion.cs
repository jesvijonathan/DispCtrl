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

    // Explorer records a path under a known folder by the folder's id rather
    // than its location: the Store engine is "{6D809377-...}\WindowsApps\...",
    // never "C:\Program Files\WindowsApps\...". Compared as written, the
    // Store engine's record was never found, so its icon was never kept on
    // the taskbar and the switch that keeps it there stayed greyed out.
    private static readonly (string Id, Environment.SpecialFolder Folder)[] KnownFolders =
    [
        ("{6D809377-6AF0-444B-8957-A3773F02200E}", Environment.SpecialFolder.ProgramFiles),
        ("{905E63B6-C1BF-494E-B29C-65B732D3D21A}", Environment.SpecialFolder.ProgramFiles),
        ("{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}", Environment.SpecialFolder.ProgramFilesX86),
        ("{F38BF404-1D43-42F2-9305-67DE0B28FC23}", Environment.SpecialFolder.Windows),
        ("{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}", Environment.SpecialFolder.System),
        ("{F1B32785-6FBA-4FCF-9D55-7B8E7F157091}", Environment.SpecialFolder.LocalApplicationData),
        ("{3EB685DB-65F9-4CF6-A03A-E3EF65729F3D}", Environment.SpecialFolder.ApplicationData),
        ("{5E6C858F-0E22-4760-9AFE-EA3317B67173}", Environment.SpecialFolder.UserProfile),
    ];

    /// <summary>A path as Explorer recorded it, as a real path.</summary>
    public static string Expand(string path)
    {
        try
        {
            if (path.StartsWith('{'))
            {
                foreach ((string id, Environment.SpecialFolder folder) in KnownFolders)
                    if (path.StartsWith(id, StringComparison.OrdinalIgnoreCase))
                        return Path.GetFullPath(Environment.GetFolderPath(folder) + path[id.Length..]);
                // Programs installed per user: %LOCALAPPDATA%\Programs.
                const string userPrograms = "{5CD7AEE2-2219-4A67-B85D-6C9CE15660CB}";
                if (path.StartsWith(userPrograms, StringComparison.OrdinalIgnoreCase))
                    return Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
                        + path[userPrograms.Length..]);
            }
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
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
                    && string.Equals(Expand(path), Path.GetFullPath(enginePath), StringComparison.OrdinalIgnoreCase))
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
