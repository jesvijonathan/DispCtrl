using System.Runtime.InteropServices;

namespace DispCtrl.Linux.X11;

/// <summary>The libX11, libXrandr, libXi, libXfixes and libXcomposite calls the
/// window tools use. Layouts are LP64 (x86-64, arm64).</summary>
/// <remarks>
/// Hand-written rather than generated: the surface is small and every struct
/// read here is read by offset from a fixed buffer, which is how Xlib's
/// unions have to be read from C# anyway.
/// </remarks>
internal static unsafe class Xlib
{
    private const string X11 = "libX11.so.6";
    private const string Xrandr = "libXrandr.so.2";
    private const string Xi = "libXi.so.6";
    private const string Xfixes = "libXfixes.so.3";
    private const string Xcomposite = "libXcomposite.so.1";

    public const int False = 0, True = 1;
    public const long AnyPropertyType = 0;
    public const int PropModeReplace = 0;
    public const ulong None = 0;

    // Event types.
    public const int KeyPress = 2, KeyRelease = 3, ButtonPress = 4, ButtonRelease = 5, MotionNotify = 6,
        EnterNotify = 7, LeaveNotify = 8, Expose = 12, DestroyNotify = 17, UnmapNotify = 18, MapNotify = 19,
        ConfigureNotify = 22, PropertyNotify = 28, ClientMessage = 33, MappingNotify = 34, GenericEvent = 35;

    // Event masks.
    public const long KeyPressMask = 1L << 0, ButtonPressMask = 1L << 2, ButtonReleaseMask = 1L << 3,
        PointerMotionMask = 1L << 6, ExposureMask = 1L << 15, StructureNotifyMask = 1L << 17,
        SubstructureNotifyMask = 1L << 19, SubstructureRedirectMask = 1L << 20, PropertyChangeMask = 1L << 22;

    // Modifier masks.
    public const uint ShiftMask = 1, LockMask = 2, ControlMask = 4, Mod1Mask = 8, Mod2Mask = 16, Mod4Mask = 64;
    public const int GrabModeAsync = 1;
    public const int CurrentTime = 0;

    // XCreateWindow value mask bits.
    public const ulong CWBackPixel = 1 << 1, CWBorderPixel = 1 << 3, CWOverrideRedirect = 1 << 9,
        CWEventMask = 1 << 11, CWColormap = 1 << 13;
    public const int InputOutput = 1, TrueColor = 4, AllocNone = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct XVisualInfo
    {
        public nint visual;
        public ulong visualid;
        public int screen;
        public int depth;
        public int @class;
        public ulong red_mask, green_mask, blue_mask;
        public int colormap_size;
        public int bits_per_rgb;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XSetWindowAttributes
    {
        public ulong background_pixmap;
        public ulong background_pixel;
        public ulong border_pixmap;
        public ulong border_pixel;
        public int bit_gravity;
        public int win_gravity;
        public int backing_store;
        public ulong backing_planes;
        public ulong backing_pixel;
        public int save_under;
        public long event_mask;
        public long do_not_propagate_mask;
        public int override_redirect;
        public ulong colormap;
        public ulong cursor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XWindowAttributes
    {
        public int x, y, width, height, border_width, depth;
        public nint visual;
        public ulong root;
        public int @class, bit_gravity, win_gravity, backing_store;
        public ulong backing_planes, backing_pixel;
        public int save_under;
        public ulong colormap;
        public int map_installed, map_state;
        public long all_event_masks, your_event_mask, do_not_propagate_mask;
        public int override_redirect;
        public nint screen;
    }

    public const int IsViewable = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct XRRMonitorInfo
    {
        public ulong name;
        public int primary;
        public int automatic;
        public int noutput;
        public int x, y, width, height, mwidth, mheight;
        public ulong* outputs;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XIEventMask
    {
        public int deviceid;
        public int mask_len;
        public byte* mask;
    }

    public const int XIAllMasterDevices = 1;
    public const int XI_RawButtonPress = 15, XI_RawButtonRelease = 16, XI_RawMotion = 17;

    /// <summary>The last X error's code (XErrorEvent.error_code), so a caller can
    /// XSync and ask whether its request was refused - a key another client
    /// already grabbed answers BadAccess (10).</summary>
    public static volatile int LastErrorCode;
    public const int BadAccess = 10;

    [UnmanagedCallersOnly]
    private static int IgnoreError(nint display, nint error)
    {
        LastErrorCode = *(byte*)(error + 32);
        return 0;
    }

    private static bool _handlerInstalled;
    private static readonly Lock HandlerGate = new();

    /// <summary>Xlib's default error handler prints and calls exit(): a window
    /// that closed between being listed and being read would end the engine.</summary>
    public static void InstallErrorHandler()
    {
        lock (HandlerGate)
        {
            if (_handlerInstalled) return;
            XSetErrorHandler(&IgnoreError);
            _handlerInstalled = true;
        }
    }

    [DllImport(X11)] public static extern int XInitThreads();
    [DllImport(X11)] public static extern nint XOpenDisplay([MarshalAs(UnmanagedType.LPUTF8Str)] string? name);
    [DllImport(X11)] public static extern int XCloseDisplay(nint display);
    [DllImport(X11)] public static extern nint XSetErrorHandler(delegate* unmanaged<nint, nint, int> handler);
    [DllImport(X11)] public static extern int XDefaultScreen(nint display);
    [DllImport(X11)] public static extern ulong XDefaultRootWindow(nint display);
    [DllImport(X11)] public static extern int XConnectionNumber(nint display);
    [DllImport(X11)] public static extern int XPending(nint display);
    [DllImport(X11)] public static extern int XNextEvent(nint display, byte* ev);
    [DllImport(X11)] public static extern int XFlush(nint display);
    [DllImport(X11)] public static extern int XSync(nint display, int discard);
    [DllImport(X11)] public static extern int XFree(nint data);
    [DllImport(X11)] public static extern ulong XInternAtom(nint display, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int onlyIfExists);
    [DllImport(X11)] public static extern nint XGetAtomName(nint display, ulong atom);

    [DllImport(X11)]
    public static extern int XGetWindowProperty(nint display, ulong w, ulong property, long offset, long length, int delete,
        ulong reqType, out ulong actualType, out int actualFormat, out ulong nitems, out ulong bytesAfter, out nint prop);

    [DllImport(X11)]
    public static extern int XChangeProperty(nint display, ulong w, ulong property, ulong type, int format, int mode, byte* data, int nelements);

    [DllImport(X11)] public static extern int XSendEvent(nint display, ulong w, int propagate, long eventMask, byte* ev);
    [DllImport(X11)] public static extern int XGetWindowAttributes(nint display, ulong w, out XWindowAttributes attributes);

    [DllImport(X11)]
    public static extern int XTranslateCoordinates(nint display, ulong src, ulong dest, int srcX, int srcY, out int destX, out int destY, out ulong child);

    [DllImport(X11)]
    public static extern int XQueryTree(nint display, ulong w, out ulong root, out ulong parent, out nint children, out uint nchildren);

    [DllImport(X11)]
    public static extern int XQueryPointer(nint display, ulong w, out ulong root, out ulong child, out int rootX, out int rootY,
        out int winX, out int winY, out uint mask);

    [DllImport(X11)] public static extern int XSelectInput(nint display, ulong w, long mask);
    [DllImport(X11)] public static extern int XMatchVisualInfo(nint display, int screen, int depth, int @class, out XVisualInfo info);
    [DllImport(X11)] public static extern ulong XCreateColormap(nint display, ulong w, nint visual, int alloc);
    [DllImport(X11)] public static extern int XFreeColormap(nint display, ulong colormap);

    [DllImport(X11)]
    public static extern ulong XCreateWindow(nint display, ulong parent, int x, int y, uint width, uint height, uint borderWidth,
        int depth, uint @class, nint visual, ulong valueMask, ref XSetWindowAttributes attributes);

    [DllImport(X11)] public static extern int XDestroyWindow(nint display, ulong w);
    [DllImport(X11)] public static extern int XMapRaised(nint display, ulong w);
    [DllImport(X11)] public static extern int XUnmapWindow(nint display, ulong w);
    [DllImport(X11)] public static extern int XRaiseWindow(nint display, ulong w);
    [DllImport(X11)] public static extern int XMoveResizeWindow(nint display, ulong w, int x, int y, uint width, uint height);
    [DllImport(X11)] public static extern int XStoreName(nint display, ulong w, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(X11)] public static extern int XKeysymToKeycode(nint display, ulong keysym);
    [DllImport(X11)] public static extern ulong XStringToKeysym([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(X11)] public static extern ulong XLookupKeysym(byte* keyEvent, int index);

    [DllImport(X11)]
    public static extern int XGrabKey(nint display, int keycode, uint modifiers, ulong grabWindow, int ownerEvents, int pointerMode, int keyboardMode);

    [DllImport(X11)] public static extern int XUngrabKey(nint display, int keycode, uint modifiers, ulong grabWindow);
    [DllImport(X11)] public static extern int XGrabKeyboard(nint display, ulong w, int ownerEvents, int pointerMode, int keyboardMode, ulong time);
    [DllImport(X11)] public static extern int XUngrabKeyboard(nint display, ulong time);

    [DllImport(X11)]
    public static extern int XGrabPointer(nint display, ulong w, int ownerEvents, uint eventMask, int pointerMode, int keyboardMode,
        ulong confineTo, ulong cursor, ulong time);

    [DllImport(X11)] public static extern int XUngrabPointer(nint display, ulong time);
    [DllImport(X11)] public static extern int XSetInputFocus(nint display, ulong focus, int revertTo, ulong time);

    [DllImport(X11)]
    public static extern int XQueryExtension(nint display, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, out int majorOpcode, out int firstEvent, out int firstError);

    [DllImport(X11)] public static extern int XGetEventData(nint display, byte* cookie);
    [DllImport(X11)] public static extern void XFreeEventData(nint display, byte* cookie);

    [DllImport(Xrandr)] public static extern XRRMonitorInfo* XRRGetMonitors(nint display, ulong window, int getActive, out int nmonitors);
    [DllImport(Xrandr)] public static extern void XRRFreeMonitors(XRRMonitorInfo* monitors);
    [DllImport(Xrandr)] public static extern void XRRSelectInput(nint display, ulong window, int mask);
    [DllImport(Xrandr)] public static extern int XRRQueryExtension(nint display, out int eventBase, out int errorBase);

    [DllImport(Xi)] public static extern int XIQueryVersion(nint display, ref int major, ref int minor);
    [DllImport(Xi)] public static extern int XISelectEvents(nint display, ulong window, XIEventMask* masks, int numMasks);

    [DllImport(Xfixes)] public static extern ulong XFixesCreateRegion(nint display, nint rectangles, int nrectangles);
    [DllImport(Xfixes)] public static extern void XFixesSetWindowShapeRegion(nint display, ulong w, int shapeKind, int xOffset, int yOffset, ulong region);
    [DllImport(Xfixes)] public static extern void XFixesDestroyRegion(nint display, ulong region);
    public const int ShapeInput = 2;

    [DllImport(Xcomposite)] public static extern ulong XCompositeNameWindowPixmap(nint display, ulong window);
    [DllImport(X11)] public static extern int XFreePixmap(nint display, ulong pixmap);

    // ---- reading events by offset (XEvent is a 192-byte union)

    public const int EventSize = 192;

    public static int Type(byte* ev) => *(int*)ev;
    public static ulong Window(byte* ev) => *(ulong*)(ev + 32);

    /// <summary>XKeyEvent and XButtonEvent share a layout up to keycode/button.</summary>
    public static (int X, int Y, int RootX, int RootY, uint State, uint Detail) Input(byte* ev) =>
        (*(int*)(ev + 64), *(int*)(ev + 68), *(int*)(ev + 72), *(int*)(ev + 76), *(uint*)(ev + 80), *(uint*)(ev + 84));

    public static (int Extension, int EvType) Generic(byte* ev) => (*(int*)(ev + 32), *(int*)(ev + 36));

    /// <summary>XIRawEvent.detail (the button), at 56: type, serial, send_event,
    /// display, extension, evtype, time, deviceid, sourceid, then detail. Read
    /// at 64 (flags) it said button 8 for every click, so no drag was ever seen.</summary>
    public const int RawDetailOffset = 56;

    public static int RawDetail(byte* cookie)
    {
        var data = *(byte**)(cookie + 48);
        return data == null ? 0 : *(int*)(data + RawDetailOffset);
    }

    // ---- libc poll(), so the event thread can wait on X and a wake pipe together

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }

    public const short POLLIN = 1;

    [DllImport("libc", SetLastError = true)] public static extern int poll(PollFd* fds, ulong nfds, int timeout);
    [DllImport("libc", SetLastError = true)] public static extern int pipe(int* fds);
    [DllImport("libc", SetLastError = true)] public static extern nint read(int fd, byte* buf, nint count);
    [DllImport("libc", SetLastError = true)] public static extern nint write(int fd, byte* buf, nint count);
    [DllImport("libc", SetLastError = true)] public static extern int close(int fd);
    [DllImport("libc", SetLastError = true)] public static extern int fcntl(int fd, int cmd, int arg);
    public const int F_SETFL = 4, O_NONBLOCK = 0x800;
}
