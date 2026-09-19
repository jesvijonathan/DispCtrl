using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace DispCtrl.Core.Settings;

/// <summary>Windows' own appearance preferences. Does not restart or inject into Explorer.</summary>
public static partial class WindowsTaskbarAppearance
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public static bool Transparency => Read(Personalize, "EnableTransparency", 1) != 0;
    public static bool Windows11Supported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
    public static bool SmallButtonsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100);
    // Windows: 0 always small, 1 never, 2 when full. UI: never, always, when full.
    public static int SmallButtons => Read(Advanced, "IconSizePreference", 1) switch { 0 => 1, 2 => 2, _ => 0 };
    public static int Alignment => Math.Clamp(Read(Advanced, "TaskbarAl", 1), 0, 1);
    public static int CombineButtons => Math.Clamp(Read(Advanced, "TaskbarGlomLevel", 0), 0, 2);
    public static int CombineButtonsOtherDisplays => Math.Clamp(Read(Advanced, "MMTaskbarGlomLevel", 0), 0, 2);
    public static bool ShowTaskView => Read(Advanced, "ShowTaskViewButton", 1) != 0;
    public static bool ShowWidgets => Read(Advanced, "TaskbarDa", 1) != 0;
    public static bool ShowBadges => Read(Advanced, "TaskbarBadges", 1) != 0;
    public static bool AllowFlashing => Read(Advanced, "TaskbarFlashing", 1) != 0;
    public static bool ShowDesktopCorner => Read(Advanced, "TaskbarSd", 1) != 0;
    public static bool SetTransparency(bool value) => Write(Personalize, "EnableTransparency", value ? 1 : 0, "ImmersiveColorSet");
    public static bool SetSmallButtons(int index) => SmallButtonsSupported && index is >= 0 and <= 2
        && Write(Advanced, "IconSizePreference", index switch { 1 => 0, 2 => 2, _ => 1 }, "TraySettings");
    public static bool SetAlignment(int value) => Windows11Supported && value is 0 or 1
        && Write(Advanced, "TaskbarAl", value, "TraySettings");
    public static bool SetCombineButtons(int value) => value is >= 0 and <= 2
        && Write(Advanced, "TaskbarGlomLevel", value, "TraySettings");
    public static bool SetCombineButtonsOtherDisplays(int value) => value is >= 0 and <= 2
        && Write(Advanced, "MMTaskbarGlomLevel", value, "TraySettings");
    public static bool SetShowTaskView(bool value) => Write(Advanced, "ShowTaskViewButton", value ? 1 : 0, "TraySettings");
    public static bool SetShowWidgets(bool value) => Windows11Supported
        && Write(Advanced, "TaskbarDa", value ? 1 : 0, "TraySettings");
    public static bool SetShowBadges(bool value) => Write(Advanced, "TaskbarBadges", value ? 1 : 0, "TraySettings");
    public static bool SetAllowFlashing(bool value) => Write(Advanced, "TaskbarFlashing", value ? 1 : 0, "TraySettings");
    public static bool SetShowDesktopCorner(bool value) => Write(Advanced, "TaskbarSd", value ? 1 : 0, "TraySettings");

    /// <summary>Restores the Windows taskbar preferences exposed by this page.</summary>
    public static bool ResetToDefaults()
    {
        bool ok = true;
        ok &= SetSmallButtons(0);              // never use the compact size
        ok &= SetAlignment(1);                 // centred on Windows 11
        ok &= SetCombineButtons(0);            // always combine
        ok &= SetCombineButtonsOtherDisplays(0);
        ok &= SetShowTaskView(true);
        ok &= SetShowWidgets(true);
        ok &= SetShowBadges(true);
        ok &= SetAllowFlashing(true);
        ok &= SetShowDesktopCorner(true);
        return ok;
    }

    private static int Read(string path, string name, int fallback)
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(path); return key?.GetValue(name) is int value ? value : fallback; }
        catch (Exception) { return fallback; }
    }
    private static bool Write(string path, string name, int value, string notification)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue(name, value, RegistryValueKind.DWord);
            SendMessageTimeout(0xFFFF, 0x1A, 0, notification, 2, 200, out _);
            return true;
        }
        catch (Exception) { return false; }
    }
    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessageTimeout(nint hwnd, uint message, nuint wparam, string lparam, uint flags, uint timeout, out nuint result);
}
