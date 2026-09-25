using System.Runtime.InteropServices;

namespace DispCtrl.Display.Placement;

/// <summary>The window calls pinning and moving need, declared by hand.</summary>
/// <remarks>
/// LibraryImport rather than CsWin32: a handful of calls, and CsWin32 has
/// already generated one overload here that could not be called (CreateFont).
/// </remarks>
internal static unsafe partial class WindowNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PlacementData
    {
        public uint Length, Flags, ShowCmd;
        public Point MinPosition, MaxPosition;
        public Rect NormalPosition;
    }

    internal const uint SwShowNoActivate = 4, SwShowNormal = 1, SwShowMinimized = 2, SwShowMaximized = 3, SwShowMinNoActive = 7, SwRestore = 9;
    internal const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpNoActivate = 0x10, SwpNoOwnerZOrder = 0x200;
    internal const nint HwndTopmost = -1, HwndNoTopmost = -2;
    internal const int GwlStyle = -16, GwlExStyle = -20;
    internal const long WsCaption = 0x00C00000, WsChild = 0x40000000, WsExToolWindow = 0x80, WsExAppWindow = 0x40000,
        WsExTopmost = 0x8, WsExNoActivate = 0x08000000;

    [LibraryImport("user32.dll")]
    internal static partial int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint param);

    [LibraryImport("user32.dll")] internal static partial int IsWindow(nint hwnd);
    [LibraryImport("user32.dll")] internal static partial int IsWindowVisible(nint hwnd);
    [LibraryImport("user32.dll")] internal static partial int IsIconic(nint hwnd);
    [LibraryImport("user32.dll")] internal static partial int IsZoomed(nint hwnd);
    [LibraryImport("user32.dll")] internal static partial int IsHungAppWindow(nint hwnd);
    [LibraryImport("user32.dll")] internal static partial nint GetWindow(nint hwnd, uint command);
    [LibraryImport("user32.dll")] internal static partial nint GetAncestor(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] internal static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] internal static partial int SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")] internal static partial int GetWindowRect(nint hwnd, out Rect rect);
    [LibraryImport("user32.dll")] internal static partial uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static partial nint GetWindowLongPtr(nint hwnd, int index);
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")] internal static partial int GetClassName(nint hwnd, char* name, int count);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")] internal static partial int GetWindowText(nint hwnd, char* text, int count);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] internal static partial int GetWindowTextLength(nint hwnd);
    [LibraryImport("user32.dll", SetLastError = true)] internal static partial int SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [LibraryImport("user32.dll")] internal static partial int GetWindowPlacement(nint hwnd, ref PlacementData placement);
    [LibraryImport("user32.dll", SetLastError = true)] internal static partial int SetWindowPlacement(nint hwnd, ref PlacementData placement);
    [LibraryImport("user32.dll")] internal static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] internal static partial int GetCursorPos(out Point point);
    [LibraryImport("user32.dll")] internal static partial nint MonitorFromPoint(Point point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "SetPropW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial int SetProp(nint hwnd, string name, nint value);
    [LibraryImport("user32.dll", EntryPoint = "GetPropW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetProp(nint hwnd, string name);
    [LibraryImport("user32.dll", EntryPoint = "RemovePropW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint RemoveProp(nint hwnd, string name);

    [LibraryImport("dwmapi.dll")] internal static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, out Rect value, uint size);
    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] internal static partial int DwmGetWindowAttributeInt(nint hwnd, uint attribute, out int value, uint size);

    [LibraryImport("kernel32.dll")] internal static partial nint OpenProcess(uint access, int inherit, uint id);
    [LibraryImport("kernel32.dll")] internal static partial int CloseHandle(nint handle);
    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    internal static partial int QueryFullProcessImageName(nint process, uint flags, char* name, ref uint size);

    internal const uint ProcessQueryLimited = 0x1000;
    internal const uint DwmExtendedFrameBounds = 9, DwmCloaked = 14;
}
