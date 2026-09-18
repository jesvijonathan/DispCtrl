using System.Runtime.InteropServices;

namespace DisplCtrl.Display;

/// <summary>
/// Windows' own taskbar auto-hide — the single global switch from Settings.
/// </summary>
/// <remarks>
/// This exists because of a genuine limit DisplCtrl cannot engineer around. The
/// primary taskbar cannot be repositioned from outside explorer: the call
/// reports success and explorer restores it within about 120ms. Windows' own
/// auto-hide <em>can</em> hide it, because explorer is doing the hiding.
/// <para>
/// The catch is that it is not per-monitor. Switching it on auto-hides every
/// taskbar, and it leaves the 1px reveal sliver that DisplCtrl exists to avoid on
/// an OLED panel — explorer needs that strip to catch the hover. So it is
/// offered as a deliberate choice rather than folded into the per-monitor
/// controls, and it is the right choice only when the primary display is not
/// the one at risk of burn-in.
/// </para>
/// </remarks>
public static partial class GlobalTaskbar
{
    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public Rect rc;
        public nint lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    private const uint AbmSetState = 0x0000000A;
    private const uint AbmGetState = 0x00000004;

    private const int AbsAutoHide = 0x01;
    private const int AbsAlwaysOnTop = 0x02;

    [LibraryImport("shell32.dll", EntryPoint = "SHAppBarMessage")]
    private static partial nuint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    public static bool IsAutoHide
    {
        get
        {
            APPBARDATA data = New();
            return ((int)SHAppBarMessage(AbmGetState, ref data) & AbsAutoHide) != 0;
        }
    }

    /// <summary>
    /// Turns Windows' global taskbar auto-hide on or off.
    /// </summary>
    /// <remarks>
    /// Always-on-top is preserved rather than cleared. <c>ABM_SETSTATE</c>
    /// takes both flags as one value, so writing only the auto-hide bit would
    /// silently drop the other.
    /// </remarks>
    public static void SetAutoHide(bool enabled)
    {
        APPBARDATA current = New();
        int state = (int)SHAppBarMessage(AbmGetState, ref current);

        int onTop = state & AbsAlwaysOnTop;
        int wanted = enabled ? AbsAutoHide | onTop : onTop;

        APPBARDATA data = New();
        data.lParam = wanted;
        SHAppBarMessage(AbmSetState, ref data);
    }

    private static APPBARDATA New() => new() { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
}
