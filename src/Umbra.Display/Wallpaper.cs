using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Umbra.Core.Displays;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Umbra.Display;

/// <summary>How a wallpaper image is fitted to the screen.</summary>
public enum WallpaperFit
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5,
}

/// <summary>
/// <c>IDesktopWallpaper</c> — the only Windows API that can set a different
/// wallpaper per monitor.
/// </summary>
/// <remarks>
/// Declared with <see cref="GeneratedComInterfaceAttribute"/> rather than
/// classic <c>ComImport</c>: the source generator emits the marshalling at
/// compile time, which is both faster and the only form that survives Native
/// AOT, should this ever move into the engine.
/// <para>
/// The method order below is the vtable order and must not be rearranged.
/// Entries this code never calls are still declared, because omitting one
/// would shift every method after it onto the wrong slot.
/// </para>
/// </remarks>
[GeneratedComInterface]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
internal partial interface IDesktopWallpaper
{
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId,
                      [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

    void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, out nint wallpaper);

    void GetMonitorDevicePathAt(uint monitorIndex, out nint monitorId);

    void GetMonitorDevicePathCount(out uint count);

    void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out RectL displayRect);

    void SetBackgroundColor(uint color);

    void GetBackgroundColor(out uint color);

    void SetPosition(WallpaperFit position);

    void GetPosition(out WallpaperFit position);
}

[StructLayout(LayoutKind.Sequential)]
internal struct RectL
{
    public int Left, Top, Right, Bottom;
}

/// <summary>Reads and sets the wallpaper on individual monitors.</summary>
public static unsafe partial class Wallpaper
{
    private static readonly Guid ClsidDesktopWallpaper =
        new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

    /// <summary>The image currently shown on one display, or null.</summary>
    /// <remarks>
    /// The monitor id <c>IDesktopWallpaper</c> expects is the same device path
    /// Umbra already keys its settings on, so no translation is needed.
    /// </remarks>
    /// <summary>
    /// The image currently shown on a display.
    /// </summary>
    /// <remarks>
    /// <c>IDesktopWallpaper</c> is the only API that knows about individual
    /// monitors, so it is tried first. <c>SystemParametersInfo</c> backs it up
    /// for the case where activation genuinely fails, at the cost of being
    /// system-wide rather than per-display.
    /// </remarks>
    public static string? Read(DisplayInfo display)
    {
        return ReadPerMonitor(display) ?? ReadSystemWide();
    }

    /// <summary>The system-wide wallpaper, which every Windows build can answer.</summary>
    private static unsafe string? ReadSystemWide()
    {
        const int MaxPath = 260;
        Span<char> buffer = stackalloc char[MaxPath];

        fixed (char* p = buffer)
        {
            if (!PInvoke.SystemParametersInfo(
                    SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETDESKWALLPAPER,
                    MaxPath, p, 0))
                return null;
        }

        string path = new string(buffer).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private static string? ReadPerMonitor(DisplayInfo display)
    {
        try
        {
            IDesktopWallpaper? api = Create();
            if (api is null) return null;

            // Guarded separately. If the monitor id is not one this interface
            // recognises the call throws, and a shared catch would swallow the
            // fallback along with it.
            try
            {
                api.GetWallpaper(display.Key.DevicePath, out nint perMonitor);
                string? path = TakeString(perMonitor);
                if (path is not null) return path;
            }
            catch (COMException)
            {
                // Fall through to the global wallpaper.
            }

            // A wallpaper set the ordinary way is not recorded against any
            // monitor, so the per-monitor query comes back empty even though
            // something is plainly on screen. Passing null asks for the one
            // that applies everywhere.
            api.GetWallpaper(null, out nint global);
            return TakeString(global);
        }
        catch (COMException)
        {
            return null;
        }
    }

    public static unsafe bool Write(DisplayInfo display, string imagePath)
    {
        if (!File.Exists(imagePath)) return false;

        try
        {
            IDesktopWallpaper? api = Create();
            if (api is not null)
            {
                api.SetWallpaper(display.Key.DevicePath, imagePath);
                return true;
            }
        }
        catch (COMException)
        {
            // Fall through to the system-wide path below.
        }

        // System-wide fallback. Applies to every monitor, which is a real
        // difference in behaviour — the caller says so in the UI rather than
        // letting it look like a per-display change that silently was not.
        fixed (char* p = imagePath)
        {
            return PInvoke.SystemParametersInfo(
                SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETDESKWALLPAPER, 0, p,
                SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_UPDATEINIFILE
                | SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_SENDCHANGE);
        }
    }

    /// <summary>True when per-monitor wallpaper is actually available here.</summary>
    /// <remarks>
    /// Lets the UI tell the user that a change will apply to every display
    /// rather than just this one, instead of quietly doing something different
    /// from what the control implies.
    /// </remarks>
    public static bool SupportsPerMonitor
    {
        get
        {
            try
            {
                return Create() is not null;
            }
            catch (COMException)
            {
                return false;
            }
        }
    }

    private const string DesktopKey = @"Control Panel\Desktop";

    /// <summary>
    /// How the image is fitted to the screen.
    /// </summary>
    /// <remarks>
    /// Windows stores this as two values, not one: <c>WallpaperStyle</c> plus a
    /// separate <c>TileWallpaper</c> flag, because tiling predates the style
    /// list and was never folded into it. Centre and Tile therefore share a
    /// style of 0 and differ only in the flag.
    /// </remarks>
    public static WallpaperFit ReadFit()
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(DesktopKey);
            if (key is null) return WallpaperFit.Fill;

            string style = key.GetValue("WallpaperStyle")?.ToString() ?? "10";
            string tile = key.GetValue("TileWallpaper")?.ToString() ?? "0";

            if (tile == "1") return WallpaperFit.Tile;

            return style switch
            {
                "0" => WallpaperFit.Center,
                "2" => WallpaperFit.Stretch,
                "6" => WallpaperFit.Fit,
                "22" => WallpaperFit.Span,
                _ => WallpaperFit.Fill,
            };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return WallpaperFit.Fill;
        }
    }

    /// <remarks>
    /// Windows applies the fit mode to every monitor; it is genuinely not
    /// per-display, even when <c>IDesktopWallpaper</c> is available.
    /// </remarks>
    public static unsafe bool WriteFit(WallpaperFit fit)
    {
        (string style, string tile) = fit switch
        {
            WallpaperFit.Center => ("0", "0"),
            WallpaperFit.Tile => ("0", "1"),
            WallpaperFit.Stretch => ("2", "0"),
            WallpaperFit.Fit => ("6", "0"),
            WallpaperFit.Span => ("22", "0"),
            _ => ("10", "0"),
        };

        try
        {
            using Microsoft.Win32.RegistryKey? key =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(DesktopKey, writable: true);
            if (key is null) return false;

            key.SetValue("WallpaperStyle", style);
            key.SetValue("TileWallpaper", tile);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }

        // The registry values are only read when the wallpaper is re-applied,
        // so setting them alone changes nothing on screen. Re-setting the
        // current image is what makes the new fit take effect.
        string? current = ReadSystemWide();
        if (current is null) return true;

        fixed (char* p = current)
        {
            return PInvoke.SystemParametersInfo(
                SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETDESKWALLPAPER, 0, p,
                SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_UPDATEINIFILE
                | SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_SENDCHANGE);
        }
    }

    /// <summary>Sets the colour shown where no wallpaper covers — black, for an OLED panel.</summary>
    public static bool WriteBackgroundColor(byte r, byte g, byte b)
    {
        try
        {
            IDesktopWallpaper? api = Create();
            if (api is null) return false;

            // COLORREF is 0x00BBGGRR, not RGB.
            api.SetBackgroundColor((uint)(r | (g << 8) | (b << 16)));
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }

    [LibraryImport("ole32.dll")]
    private static unsafe partial int CoCreateInstance(
        Guid* rclsid, nint pUnkOuter, uint dwClsContext, Guid* riid, nint* ppv);

    /// <summary>
    /// Bridges the source-generated interface to a real COM object.
    /// </summary>
    /// <remarks>
    /// <c>Activator.CreateInstance</c> would hand back a classic runtime
    /// callable wrapper, which cannot be cast to a
    /// <see cref="GeneratedComInterfaceAttribute"/> interface. The object has
    /// to come through a <see cref="ComWrappers"/> instance instead.
    /// </remarks>
    private static readonly StrategyBasedComWrappers Wrappers = new();

    private static unsafe IDesktopWallpaper? Create()
    {
        // CLSCTX_ALL, emphatically not CLSCTX_INPROC_SERVER. DesktopWallpaper
        // is registered as a local server, and asking for in-proc activation
        // fails with REGDB_E_CLASSNOTREG — an error that reads as "this class
        // does not exist" and is very easy to misdiagnose as a broken
        // installation rather than a wrong activation context.
        const uint ClsCtxAll = 0x17;

        Guid clsid = ClsidDesktopWallpaper;
        Guid iid = typeof(IDesktopWallpaper).GUID;
        nint raw = 0;

        if (CoCreateInstance(&clsid, 0, ClsCtxAll, &iid, &raw) < 0 || raw == 0)
            return null;

        try
        {
            return (IDesktopWallpaper)Wrappers.GetOrCreateObjectForComInstance(
                raw, CreateObjectFlags.None);
        }
        finally
        {
            // GetOrCreateObjectForComInstance takes its own reference.
            Marshal.Release(raw);
        }
    }

    /// <summary>
    /// Converts a COM-allocated string and frees it.
    /// </summary>
    /// <remarks>
    /// <c>IDesktopWallpaper</c> returns strings the caller owns. Without the
    /// free this leaks a little memory on every read, and the panel reads on
    /// every refresh.
    /// </remarks>
    private static string? TakeString(nint raw)
    {
        if (raw == 0) return null;

        try
        {
            string? value = Marshal.PtrToStringUni(raw);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        finally
        {
            Marshal.FreeCoTaskMem(raw);
        }
    }
}
