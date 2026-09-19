using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DispCtrl.Core.Settings;

/// <summary>
/// Windows' light and dark mode, as the system records it.
/// </summary>
/// <remarks>
/// Unlike night light, this one is a plain registry setting with no service
/// guarding it: two values under <c>Themes\Personalize</c>, and whoever writes
/// them last wins. Windows' own Settings page writes exactly the same values.
/// <para>
/// Windows keeps apps and the shell separate — the taskbar and Start can be dark
/// while apps stay light — but the Settings page presents them as one choice
/// unless you go looking, and a toggle that silently left half the desktop
/// behind would be the more surprising answer. Both move together here.
/// </para>
/// </remarks>
public static class WindowsTheme
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Apps: 0 is dark, 1 is light. The value is named for the light case.</summary>
    private const string AppsValue = "AppsUseLightTheme";

    /// <summary>The shell — taskbar, Start, Action Centre.</summary>
    private const string SystemValue = "SystemUsesLightTheme";

    /// <summary>
    /// True when Windows is in dark mode, or null when it cannot be read.
    /// </summary>
    /// <remarks>
    /// Read from the apps value, because that is the one that decides what this
    /// app itself looks like. Null stays distinguishable from light: a setting
    /// that cannot be read is not a setting that is off.
    /// </remarks>
    public static bool? IsDark
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
                return key?.GetValue(AppsValue) is int light ? light == 0 : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>Switches Windows between light and dark.</summary>
    /// <returns>False when the value could not be written.</returns>
    public static bool SetDark(bool dark)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key is null) return false;

            int light = dark ? 0 : 1;
            key.SetValue(AppsValue, light, RegistryValueKind.DWord);
            key.SetValue(SystemValue, light, RegistryValueKind.DWord);

            Announce();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Tells everything already running that the colours have changed.
    /// </summary>
    /// <remarks>
    /// The registry write is the setting; this is what makes it visible. Without
    /// it the value is correct and the screen is not, until each app happens to
    /// restart — which reads as the toggle having done nothing.
    /// <para>
    /// Sent with a timeout rather than <c>SendMessage</c>: this goes to every
    /// top-level window on the desktop, and one hung app would otherwise hang
    /// this one behind it.
    /// </para>
    /// </remarks>
    private static unsafe void Announce()
    {
        const int HwndBroadcast = 0xFFFF;

        fixed (char* setting = "ImmersiveColorSet")
        {
            _ = PInvoke.SendMessageTimeout(
                new HWND(HwndBroadcast),
                PInvoke.WM_SETTINGCHANGE,
                default,
                new LPARAM((nint)setting),
                SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG,
                200,
                null);
        }
    }

    /// <summary>Opens Windows' own colours page in Settings.</summary>
    /// <remarks>
    /// It carries what is deliberately not mirrored here: accent colour,
    /// transparency, and the apps-versus-shell split this toggle moves together.
    /// </remarks>
    public static bool OpenSettings()
    {
        try
        {
            // A protocol launch is handed to the shell, which has no process of
            // its own to give back, so null here is success rather than failure.
            using System.Diagnostics.Process? _ = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("ms-settings:colors")
                {
                    UseShellExecute = true,
                });

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
