using DispCtrl.Core.Settings;
using Microsoft.Win32;

namespace DispCtrl.Display.Placement;

/// <summary>
/// Windows 11's own multiple-display window settings: remembering where windows
/// were per monitor, and minimizing them when a monitor is disconnected.
/// </summary>
/// <remarks>
/// Settings &gt; System &gt; Display &gt; Multiple displays writes these two values
/// under <c>HKCU\Control Panel\Desktop</c>; so does this. Both are read by
/// Windows when a monitor comes or goes, so a change applies from the next one.
/// </remarks>
public static class WindowsWindowMemory
{
    private const string KeyPath = @"Control Panel\Desktop";
    private const string RememberValue = "RestorePreviousStateRecalcBehavior";
    private const string MinimizeValue = "MonitorRemovalRecalcBehavior";

    /// <summary>"Remember window locations based on monitor connection". 0 means on; absent is on too.</summary>
    public static bool Remember
    {
        get => Read(RememberValue) is not 1;
        set => Write(RememberValue, value ? 0 : 1);
    }

    /// <summary>"Minimize windows when a monitor is disconnected". 1 means on; absent is off.</summary>
    public static bool MinimizeOnDisconnect
    {
        get => Read(MinimizeValue) is 1;
        set => Write(MinimizeValue, value ? 1 : 0);
    }

    /// <summary>
    /// Hands Windows' own window memory back if an earlier build switched it
    /// off. Never switches it off: the value is not read live (measured on an unplug), so
    /// Windows went on putting windows back regardless - and DispCtrl's putting
    /// back, which leaves a window already home alone, works beside it.
    /// </summary>
    /// <returns>True when the settings' bookkeeping changed and wants saving.</returns>
    public static bool Reconcile(PlacementSettings placement)
    {
        try
        {
            if (placement.TookOverWindowsMemory)
            {
                Remember = true;
                placement.TookOverWindowsMemory = false;
                return true;
            }
        }
        catch (Exception) { }
        return false;
    }

    private static int? Read(string name)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(name) is int value ? value : null;
        }
        catch (Exception) { return null; }
    }

    private static void Write(string name, int value)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.SetValue(name, value, RegistryValueKind.DWord);
    }
}
