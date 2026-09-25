using System.Runtime.InteropServices;

namespace DispCtrl.Engine.Placement;

/// <summary>Window, region and hook calls for the pin borders and window placement.</summary>
internal static unsafe partial class PinNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct PaintStruct { public nint Hdc; public int Erase; public Rect Paint; public int Restore, IncUpdate; public fixed byte Reserved[32]; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowClass
    {
        public uint Size, Style;
        public delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> Proc;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Brush;
        public char* Menu;
        public char* Name;
        public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW")] internal static partial ushort RegisterClassEx(ref WindowClass value);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")] internal static partial nint DefWindowProc(nint hwnd, uint msg, nuint wparam, nint lparam);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")] internal static partial int GetMessage(out Message message, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")] internal static partial nint DispatchMessage(ref Message message);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")] internal static partial int PostMessage(nint hwnd, uint msg, nuint wparam, nint lparam);
    [LibraryImport("user32.dll")] internal static partial void PostQuitMessage(int code);
    [LibraryImport("user32.dll")] internal static partial int DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll")] internal static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint proc);
    [LibraryImport("user32.dll")] internal static partial int KillTimer(nint hwnd, nuint id);
    [LibraryImport("user32.dll")] internal static partial nint SetWinEventHook(uint min, uint max, nint module, delegate* unmanaged[Stdcall]<nint, uint, nint, int, int, uint, uint, void> callback, uint process, uint thread, uint flags);
    [LibraryImport("user32.dll")] internal static partial int UnhookWinEvent(nint hook);
    [LibraryImport("user32.dll")] internal static partial int SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [LibraryImport("user32.dll")] internal static partial int ShowWindow(nint hwnd, int show);
    [LibraryImport("user32.dll")] internal static partial int SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [LibraryImport("user32.dll")] internal static partial int SetWindowRgn(nint hwnd, nint region, int redraw);
    [LibraryImport("user32.dll")] internal static partial int InvalidateRect(nint hwnd, nint rect, int erase);
    [LibraryImport("user32.dll")] internal static partial nint BeginPaint(nint hwnd, out PaintStruct paint);
    [LibraryImport("user32.dll")] internal static partial int EndPaint(nint hwnd, ref PaintStruct paint);
    [LibraryImport("user32.dll")] internal static partial int GetClientRect(nint hwnd, out Rect rect);
    [LibraryImport("user32.dll")] internal static partial int FillRect(nint hdc, ref Rect rect, nint brush);
    [LibraryImport("user32.dll")] internal static partial nint GetWindow(nint hwnd, uint command);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static partial nint GetWindowLongPtr(nint hwnd, int index);
    [LibraryImport("user32.dll")] internal static partial int IsWindow(nint hwnd);
    [LibraryImport("user32.dll")] internal static partial int IsIconic(nint hwnd);
    [LibraryImport("user32.dll")] internal static partial int IsZoomed(nint hwnd);
    [LibraryImport("user32.dll")] internal static partial int IsWindowVisible(nint hwnd);
    [LibraryImport("user32.dll")] internal static partial uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [LibraryImport("user32.dll")] internal static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")] internal static partial int GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [LibraryImport("user32.dll", EntryPoint = "GetPropW", StringMarshalling = StringMarshalling.Utf16)] internal static partial nint GetProp(nint hwnd, string name);
    [LibraryImport("user32.dll", EntryPoint = "SetPropW", StringMarshalling = StringMarshalling.Utf16)] internal static partial int SetProp(nint hwnd, string name, nint value);
    [LibraryImport("user32.dll", EntryPoint = "RemovePropW", StringMarshalling = StringMarshalling.Utf16)] internal static partial nint RemoveProp(nint hwnd, string name);
    [LibraryImport("user32.dll")] internal static partial int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint param);
    [LibraryImport("user32.dll")] internal static partial nint GetForegroundWindow();
    [LibraryImport("gdi32.dll")] internal static partial nint CreateRectRgn(int left, int top, int right, int bottom);
    [LibraryImport("gdi32.dll")] internal static partial nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [LibraryImport("gdi32.dll")] internal static partial int CombineRgn(nint result, nint first, nint second, int mode);
    [LibraryImport("gdi32.dll")] internal static partial int OffsetRgn(nint region, int x, int y);
    [LibraryImport("gdi32.dll")] internal static partial int DeleteObject(nint obj);
    [LibraryImport("gdi32.dll")] internal static partial nint CreateSolidBrush(uint colour);
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)] internal static partial nint GetModuleHandle(string? name);
    [LibraryImport("dwmapi.dll")] internal static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, out Rect value, uint size);
    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] internal static partial int DwmGetWindowAttributeInt(nint hwnd, uint attribute, out int value, uint size);
    [LibraryImport("shcore.dll")] internal static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    internal const int RgnAnd = 1, RgnDiff = 4;
    internal const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpNoActivate = 0x10, SwpShowWindow = 0x40, SwpNoOwnerZOrder = 0x200;
    internal const nint HwndTopmost = -1;
}
