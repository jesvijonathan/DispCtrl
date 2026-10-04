using System.Runtime.InteropServices;

namespace DispCtrl.Linux.Hardware;

/// <summary>A connected output with a CRTC driving it: the unit a gamma ramp
/// belongs to.</summary>
public sealed record RampOutput(string Name, ulong Crtc, int Size);

/// <summary>The top of each channel of a ramp, as fractions of full scale - what
/// warmth and dimming did to white.</summary>
public readonly record struct RampPeaks(double Red, double Green, double Blue)
{
    public bool IsIdentity => Math.Abs(Red - 1) < 0.002 && Math.Abs(Green - 1) < 0.002 && Math.Abs(Blue - 1) < 0.002;
}

/// <summary>Reads and writes per-CRTC gamma ramps through libX11 and libXrandr.</summary>
/// <remarks>
/// The Windows engine scales a ramp's channels (<c>NightLight.Scaled</c>); this
/// does the same, writing <c>i/(n-1) * channel * dim</c>, so warmth and dimming
/// compose in one write and never fight. X has no equivalent of Windows'
/// identity-distance clamp, so there is nothing to lift.
/// <para>
/// One connection per call: the engine touches ramps every few seconds at most,
/// and a connection kept open would need its own error and reconnect handling
/// when the X server restarts. Every call holds one lock, because Xlib is not
/// thread-safe without <c>XInitThreads</c>.
/// </para>
/// </remarks>
public static unsafe class GammaRamp
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXrandr = "libXrandr.so.2";
    private const ushort RRConnected = 0;

    private static readonly Lock Gate = new();
    private static bool _handlerInstalled;

    /// <summary>The X display name a call opens: <c>null</c> means <c>$DISPLAY</c>.
    /// The engine sets it from a client when it was started without one.</summary>
    public static string? DisplayName { get; set; }

    /// <summary>Whether an X server answers at all - false on a pure Wayland
    /// session, on a console, or with libXrandr missing.</summary>
    public static bool IsAvailable(out string? reason)
    {
        if (OnWayland)
        {
            reason = "this is a Wayland session, whose compositor keeps gamma to itself (log in to an X11 session for night light and dimming)";
            return false;
        }
        lock (Gate)
        {
            try
            {
                var display = Open();
                if (display == 0)
                {
                    reason = DisplayName is null && Environment.GetEnvironmentVariable("DISPLAY") is null
                        ? "no X display ($DISPLAY is not set)"
                        : $"cannot open X display {DisplayName ?? Environment.GetEnvironmentVariable("DISPLAY")}";
                    return false;
                }
                XCloseDisplay(display);
                reason = null;
                return true;
            }
            catch (DllNotFoundException ex)
            {
                reason = $"libX11 or libXrandr missing ({ex.Message.Split(':')[0]})";
                return false;
            }
        }
    }

    /// <summary>Every connected output that has a CRTC, in the server's order.
    /// Empty when there is no X server.</summary>
    public static IReadOnlyList<RampOutput> Outputs()
    {
        if (OnWayland) return [];
        lock (Gate)
        {
            var display = SafeOpen();
            if (display == 0) return [];
            try { return OutputsOn(display); }
            finally { XCloseDisplay(display); }
        }
    }

    public static RampPeaks? Read(string output)
    {
        lock (Gate)
        {
            var display = SafeOpen();
            if (display == 0) return null;
            try
            {
                var target = OutputsOn(display).FirstOrDefault(o => o.Name == output);
                if (target is null) return null;
                var gamma = XRRGetCrtcGamma(display, target.Crtc);
                if (gamma == null) return null;
                try
                {
                    int top = gamma->size - 1;
                    if (top < 1) return null;
                    return new RampPeaks(gamma->red[top] / 65535.0, gamma->green[top] / 65535.0, gamma->blue[top] / 65535.0);
                }
                finally { XRRFreeGamma(gamma); }
            }
            finally { XCloseDisplay(display); }
        }
    }

    /// <summary>Writes a linear ramp scaled per channel and by <paramref name="dim"/>.
    /// Values are clamped to 0..1; a dim below <see cref="Ramps.RampTarget.LowestDim"/>
    /// is the caller's to refuse.</summary>
    public static bool Write(string output, double red, double green, double blue, double dim, out string? error)
    {
        lock (Gate)
        {
            if (OnWayland) { error = "a Wayland session ignores X gamma ramps"; return false; }
            var display = SafeOpen();
            if (display == 0) { error = "no X display"; return false; }
            try
            {
                var target = OutputsOn(display).FirstOrDefault(o => o.Name == output);
                if (target is null) { error = $"no connected output '{output}'"; return false; }
                if (target.Size < 2) { error = $"output '{output}' has no gamma ramp"; return false; }

                var gamma = XRRAllocGamma(target.Size);
                if (gamma == null) { error = "XRRAllocGamma failed"; return false; }
                try
                {
                    double r = Math.Clamp(red * dim, 0, 1), g = Math.Clamp(green * dim, 0, 1), b = Math.Clamp(blue * dim, 0, 1);
                    int top = target.Size - 1;
                    for (int i = 0; i <= top; i++)
                    {
                        double level = (double)i / top * 65535.0;
                        gamma->red[i] = (ushort)Math.Round(level * r);
                        gamma->green[i] = (ushort)Math.Round(level * g);
                        gamma->blue[i] = (ushort)Math.Round(level * b);
                    }
                    XRRSetCrtcGamma(display, target.Crtc, gamma);
                    XSync(display, 0);
                }
                finally { XRRFreeGamma(gamma); }
                error = null;
                return true;
            }
            finally { XCloseDisplay(display); }
        }
    }

    /// <summary>XWayland lists outputs and accepts ramps, and the compositor
    /// ignores them: every write would report success and change nothing.</summary>
    public static bool OnWayland =>
        string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);

    public static bool Reset(string output, out string? error) => Write(output, 1, 1, 1, 1, out error);

    private static List<RampOutput> OutputsOn(nint display)
    {
        var results = new List<RampOutput>();
        var root = XDefaultRootWindow(display);
        var resources = XRRGetScreenResourcesCurrent(display, root);
        if (resources == null) return results;
        try
        {
            for (int i = 0; i < resources->noutput; i++)
            {
                var info = XRRGetOutputInfo(display, resources, resources->outputs[i]);
                if (info == null) continue;
                try
                {
                    if (info->connection != RRConnected || info->crtc == 0) continue;
                    string name = Marshal.PtrToStringUTF8((nint)info->name, info->nameLen);
                    int size = XRRGetCrtcGammaSize(display, info->crtc);
                    results.Add(new RampOutput(name, info->crtc, size));
                }
                finally { XRRFreeOutputInfo(info); }
            }
        }
        finally { XRRFreeScreenResources(resources); }
        return results;
    }

    private static nint SafeOpen()
    {
        try { return Open(); }
        catch (DllNotFoundException) { return 0; }
    }

    private static nint Open()
    {
        if (!_handlerInstalled)
        {
            // Xlib's default handler prints and calls exit(): a CRTC that went
            // away between enumerating and writing would end the engine.
            XSetErrorHandler(&OnXError);
            _handlerInstalled = true;
        }
        return XOpenDisplay(DisplayName);
    }

    [UnmanagedCallersOnly]
    private static int OnXError(nint display, nint error) => 0;

    // Layouts from Xrandr.h, LP64.
    [StructLayout(LayoutKind.Sequential)]
    private struct XRRScreenResources
    {
        public ulong timestamp;
        public ulong configTimestamp;
        public int ncrtc;
        public ulong* crtcs;
        public int noutput;
        public ulong* outputs;
        public int nmode;
        public nint modes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XRROutputInfo
    {
        public ulong timestamp;
        public ulong crtc;
        public byte* name;
        public int nameLen;
        public ulong mm_width;
        public ulong mm_height;
        public ushort connection;
        public ushort subpixel_order;
        public int ncrtc;
        public ulong* crtcs;
        public int nclone;
        public ulong* clones;
        public int nmode;
        public int npreferred;
        public ulong* modes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XRRCrtcGamma
    {
        public int size;
        public ushort* red;
        public ushort* green;
        public ushort* blue;
    }

    [DllImport(LibX11, CharSet = CharSet.Ansi)]
    private static extern nint XOpenDisplay([MarshalAs(UnmanagedType.LPUTF8Str)] string? name);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(nint display);

    [DllImport(LibX11)]
    private static extern ulong XDefaultRootWindow(nint display);

    [DllImport(LibX11)]
    private static extern int XSync(nint display, int discard);

    [DllImport(LibX11)]
    private static extern nint XSetErrorHandler(delegate* unmanaged<nint, nint, int> handler);

    [DllImport(LibXrandr)]
    private static extern XRRScreenResources* XRRGetScreenResourcesCurrent(nint display, ulong window);

    [DllImport(LibXrandr)]
    private static extern void XRRFreeScreenResources(XRRScreenResources* resources);

    [DllImport(LibXrandr)]
    private static extern XRROutputInfo* XRRGetOutputInfo(nint display, XRRScreenResources* resources, ulong output);

    [DllImport(LibXrandr)]
    private static extern void XRRFreeOutputInfo(XRROutputInfo* info);

    [DllImport(LibXrandr)]
    private static extern int XRRGetCrtcGammaSize(nint display, ulong crtc);

    [DllImport(LibXrandr)]
    private static extern XRRCrtcGamma* XRRGetCrtcGamma(nint display, ulong crtc);

    [DllImport(LibXrandr)]
    private static extern XRRCrtcGamma* XRRAllocGamma(int size);

    [DllImport(LibXrandr)]
    private static extern void XRRSetCrtcGamma(nint display, ulong crtc, XRRCrtcGamma* gamma);

    [DllImport(LibXrandr)]
    private static extern void XRRFreeGamma(XRRCrtcGamma* gamma);
}
