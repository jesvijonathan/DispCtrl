using Microsoft.Win32;

namespace DispCtrl.Core.Displays;

/// <summary>Whether Windows will accept a full-range gamma ramp.</summary>
/// <param name="Unlocked">The clamp has been lifted.</param>
/// <param name="Value">What the registry currently says, or -1 when unset.</param>
public readonly record struct GammaRangeState(bool Unlocked, int Value);

/// <summary>
/// Lifts the clamp Windows puts on how far a gamma ramp may stray.
/// </summary>
/// <remarks>
/// By default Windows refuses any ramp whose weakest channel falls below about
/// half of the identity. That is what caps DispCtrl's warmth at 3300K and its
/// software dimming at 50%, and why the two compete for what is left.
/// <para>
/// One machine-wide registry value lifts it. This is the long-established route
/// — f.lux has set the same value for years — and it is the efficient one: the
/// alternative is a translucent always-on-top window per display, which DWM
/// composites every frame, which appears in screen recordings, and which has to
/// be fought with full-screen exclusive applications. A registry value costs one
/// elevated write, once, and nothing at all thereafter.
/// </para>
/// <para>
/// It is machine-wide and affects every application that sets a gamma ramp, so
/// it is offered rather than assumed, with the trade-off stated.
/// </para>
/// </remarks>
public static class GammaRange
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM";
    private const string ValueName = "GdiIcmGammaRange";

    /// <summary>
    /// 256, the full range.
    /// </summary>
    /// <remarks>
    /// The value is a count of distinguishable levels the clamp permits either
    /// side of the identity; 256 is the whole 8-bit range, which is to say no
    /// clamp at all.
    /// </remarks>
    public const int Unlocked = 256;

    public static GammaRangeState Read()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(KeyPath);
            object? value = key?.GetValue(ValueName);

            if (value is not int number) return new GammaRangeState(false, -1);
            return new GammaRangeState(number >= Unlocked, number);
        }
        catch (Exception)
        {
            // A locked-down machine may refuse even the read.
            return new GammaRangeState(false, -1);
        }
    }

    /// <summary>
    /// Sets or clears the value, asking for elevation once.
    /// </summary>
    /// <remarks>
    /// Shelled out to Windows' own <c>reg.exe</c> with the <c>runas</c> verb
    /// rather than elevating the app, for the reason auto-rotation does the
    /// same: everything else here works from a normal token, and a packaged app
    /// cannot request elevation at all.
    /// <para>
    /// Returns false when the prompt is dismissed, which is an answer rather
    /// than an error.
    /// </para>
    /// </remarks>
    public static bool Write(bool unlock)
    {
        string arguments = unlock
            ? $"add \"HKLM\\{KeyPath}\" /v {ValueName} /t REG_DWORD /d {Unlocked} /f"
            : $"delete \"HKLM\\{KeyPath}\" /v {ValueName} /f";

        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "reg.exe",
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        };

        try
        {
            using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(start);
            if (process is null) return false;

            process.WaitForExit(10_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
