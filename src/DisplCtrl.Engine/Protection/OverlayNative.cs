using System.Runtime.InteropServices;

namespace DisplCtrl.Engine.Protection;

internal static unsafe partial class OverlayNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
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
    internal struct LastInput { public uint Size, Tick; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW")] internal static partial ushort RegisterClassEx(ref WindowClass value);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)] internal static partial nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")] internal static partial nint DefWindowProc(nint hwnd, uint msg, nuint wparam, nint lparam);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")] internal static partial int GetMessage(out Message message, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")] internal static partial nint DispatchMessage(ref Message message);
    [LibraryImport("user32.dll")] internal static partial int DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")] internal static partial int PostMessage(nint hwnd, uint msg, nuint wparam, nint lparam);
    [LibraryImport("user32.dll")] internal static partial void PostQuitMessage(int code);
    [LibraryImport("user32.dll")] internal static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint proc);
    [LibraryImport("user32.dll")] internal static partial int KillTimer(nint hwnd, nuint id);
    [LibraryImport("user32.dll")] internal static partial nint SetWinEventHook(uint min, uint max, nint module, delegate* unmanaged[Stdcall]<nint, uint, nint, int, int, uint, uint, void> callback, uint process, uint thread, uint flags);
    [LibraryImport("user32.dll")] internal static partial int UnhookWinEvent(nint hook);
    [LibraryImport("user32.dll")] internal static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] internal static partial int GetCursorPos(out Point point);
    [LibraryImport("user32.dll")] internal static partial nint WindowFromPoint(Point point);
    [LibraryImport("user32.dll")] internal static partial nint GetAncestor(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] internal static partial int GetWindowRect(nint hwnd, out Rect rect);
    [LibraryImport("user32.dll")] internal static partial int IsWindow(nint hwnd);
    [LibraryImport("user32.dll")] internal static partial int IsIconic(nint hwnd);
    [LibraryImport("user32.dll")] internal static partial uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")] internal static partial int GetClassName(nint hwnd, char* name, int count);
    [LibraryImport("user32.dll")] internal static partial int GetLastInputInfo(ref LastInput input);
    [LibraryImport("user32.dll")] internal static partial int SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [LibraryImport("user32.dll")] internal static partial int ShowWindow(nint hwnd, int show);
    [LibraryImport("user32.dll", EntryPoint = "SetPropW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)] internal static partial int SetProp(nint hwnd, string name, nint value);
    [LibraryImport("user32.dll", EntryPoint = "RemovePropW", StringMarshalling = StringMarshalling.Utf16)] internal static partial nint RemoveProp(nint hwnd, string name);
    [LibraryImport("user32.dll")] internal static partial int SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [LibraryImport("user32.dll")] internal static partial int GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static partial nint GetWindowLongPtr(nint hwnd, int index);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] internal static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)] internal static partial nint FindWindowEx(nint parent, nint after, string className, string? title);
    [LibraryImport("user32.dll")] internal static partial int SetWindowRgn(nint hwnd, nint region, int redraw);
    [LibraryImport("gdi32.dll")] internal static partial nint CreateRectRgn(int left, int top, int right, int bottom);
    [LibraryImport("gdi32.dll")] internal static partial nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [LibraryImport("gdi32.dll")] internal static partial int CombineRgn(nint result, nint first, nint second, int mode);
    [LibraryImport("gdi32.dll")] internal static partial int DeleteObject(nint obj);
    [LibraryImport("gdi32.dll")] internal static partial nint GetStockObject(int obj);
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)] internal static partial nint GetModuleHandle(string? name);
    [LibraryImport("dwmapi.dll")] internal static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, out Rect value, uint size);
}
