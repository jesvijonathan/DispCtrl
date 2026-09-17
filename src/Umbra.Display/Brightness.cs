using System.Management;
using Umbra.Core.Displays;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Umbra.Display;

/// <summary>A monitor's brightness range, or <see cref="Supported"/> = false.</summary>
public readonly record struct BrightnessRange(uint Min, uint Current, uint Max, bool Supported)
{
    public static BrightnessRange Unsupported => new(0, 0, 0, false);

    /// <summary>Current value as 0-100, for a UI that should not care about the raw range.</summary>
    public int Percent => Max > Min ? (int)Math.Round((Current - Min) * 100.0 / (Max - Min)) : 0;

    public uint FromPercent(int percent) =>
        (uint)Math.Round(Min + (Max - Min) * Math.Clamp(percent, 0, 100) / 100.0);
}

/// <summary>
/// Reads and writes monitor brightness, over whichever channel the panel
/// actually supports.
/// </summary>
/// <remarks>
/// There is no single API for this, which is the whole reason this class
/// exists. An external monitor is driven over DDC/CI — a slow serial protocol
/// running on the video cable. The internal laptop panel has no DDC/CI at all
/// and its backlight is reached through WMI instead.
/// <para>
/// Which one to use is decided by the connector reported by the CCD API, not
/// by guessing: see <see cref="DisplayInfo.IsInternal"/>.
/// </para>
/// <para>
/// <b>Every call here blocks.</b> A DDC/CI round trip is tens to hundreds of
/// milliseconds, so callers must stay off the UI thread.
/// </para>
/// </remarks>
public static class Brightness
{
    public static BrightnessRange Read(DisplayInfo display) =>
        display.IsInternal ? ReadInternal() : ReadDdc(display);

    public static bool Write(DisplayInfo display, uint value) =>
        display.IsInternal ? WriteInternal(value) : WriteDdc(display, value);

    // ---------------------------------------------------------- internal --

    private const string WmiScope = @"root\wmi";

    private static BrightnessRange ReadInternal()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                WmiScope, "SELECT CurrentBrightness FROM WmiMonitorBrightness");

            foreach (ManagementBaseObject o in searcher.Get())
            {
                using (o)
                {
                    byte current = (byte)o["CurrentBrightness"];
                    // WMI reports the backlight directly as a percentage.
                    return new BrightnessRange(0, current, 100, true);
                }
            }
        }
        catch (ManagementException)
        {
            // Desktop machines have no WmiMonitorBrightness class at all.
        }

        return BrightnessRange.Unsupported;
    }

    /// <remarks>
    /// The documentation implies this needs elevation. Measured on this
    /// hardware it succeeds unelevated, which is what lets Umbra avoid
    /// requesting admin rights at all — but it may be driver-dependent, so a
    /// failure here is reported rather than treated as impossible.
    /// </remarks>
    private static bool WriteInternal(uint value)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                WmiScope, "SELECT * FROM WmiMonitorBrightnessMethods");

            foreach (ManagementBaseObject o in searcher.Get())
            {
                using var method = (ManagementObject)o;
                ManagementBaseObject args = method.GetMethodParameters("WmiSetBrightness");
                args["Timeout"] = (uint)1;
                args["Brightness"] = (byte)Math.Clamp(value, 0, 100);
                method.InvokeMethod("WmiSetBrightness", args, null);
                return true;
            }
        }
        catch (ManagementException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    // ----------------------------------------------------------- DDC/CI --

    private static unsafe BrightnessRange ReadDdc(DisplayInfo display)
    {
        return DdcChannel.With(display, handle =>
        {
            uint min = 0, cur = 0, max = 0;
            // Returns a BOOL as int; zero means the monitor does not implement
            // the brightness VCP code, which is common on older panels.
            return PInvoke.GetMonitorBrightness(handle, &min, &cur, &max) != 0
                ? new BrightnessRange(min, cur, max, true)
                : BrightnessRange.Unsupported;
        }, BrightnessRange.Unsupported);
    }

    private static bool WriteDdc(DisplayInfo display, uint value) =>
        DdcChannel.With(display, handle => PInvoke.SetMonitorBrightness(handle, value) != 0, false);

}
