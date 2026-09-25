using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DispCtrl.Core.Displays;
using static DispCtrl.Display.Placement.WindowNative;

namespace DispCtrl.Display.Placement;

/// <summary>One window a person would call a window: what Alt+Tab lists.</summary>
/// <param name="Handle">The HWND, valid for as long as the window lives.</param>
/// <param name="Title">Its title bar text.</param>
/// <param name="Process">The executable's name, without <c>.exe</c>.</param>
/// <param name="Frame">What is drawn, in physical pixels: DWM's frame, without the invisible resize borders.</param>
/// <param name="Show">Normal, maximized or minimized.</param>
/// <param name="Pinned">Whether DispCtrl has pinned it on top.</param>
/// <param name="Topmost">Whether it stays on top at all, pinned by DispCtrl or by itself.</param>
public sealed record AppWindow(nint Handle, string Title, string Process, uint ProcessId, DisplayRect Frame,
    WindowShow Show, bool Pinned, bool Topmost)
{
    /// <summary>The handle as <c>dispctrl</c> accepts it back: <c>0x1A2B3C</c>.</summary>
    public string Id => $"0x{Handle:X}";
}

/// <summary>Finds the app windows on the desktop in use.</summary>
/// <remarks>
/// The rule is Alt+Tab's: visible, not cloaked (which leaves out other virtual
/// desktops and suspended Store apps), not a tool window, and either unowned or
/// marked as an app window. The shell's own surfaces and DispCtrl's overlays are
/// never in it.
/// </remarks>
public static unsafe class AppWindows
{
    /// <summary>Every app window, top of the z-order first.</summary>
    public static List<AppWindow> List()
    {
        var handles = new List<nint>();
        GCHandle box = GCHandle.Alloc(handles);
        try { _ = EnumWindows(&Collect, GCHandle.ToIntPtr(box)); }
        finally { box.Free(); }

        var result = new List<AppWindow>(handles.Count);
        foreach (nint hwnd in handles)
            if (Describe(hwnd) is { } window) result.Add(window);
        return result;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Collect(nint hwnd, nint param)
    {
        if (GCHandle.FromIntPtr(param).Target is List<nint> list && IsCandidate(hwnd)) list.Add(hwnd);
        return 1;
    }

    /// <summary>The window as a person sees it, or null when it is not an app window.</summary>
    public static AppWindow? Describe(nint hwnd)
    {
        if (!IsCandidate(hwnd)) return null;
        string title = Title(hwnd);
        if (title.Length == 0) return null;
        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        string process = ProcessName(pid);
        WindowShow show = IsIconic(hwnd) != 0 ? WindowShow.Minimized : IsZoomed(hwnd) != 0 ? WindowShow.Maximized : WindowShow.Normal;
        long ex = GetWindowLongPtr(hwnd, GwlExStyle);
        return new AppWindow(hwnd, title, process, pid, Frame(hwnd), show,
            WindowPins.IsPinned(hwnd), (ex & WsExTopmost) != 0);
    }

    /// <summary>Alt+Tab's test, and none of the shell's own windows.</summary>
    public static bool IsCandidate(nint hwnd)
    {
        if (hwnd == 0 || IsWindow(hwnd) == 0 || IsWindowVisible(hwnd) == 0) return false;
        if (GetAncestor(hwnd, 2) != hwnd) return false; // GA_ROOT: top-level only
        long style = GetWindowLongPtr(hwnd, GwlStyle), ex = GetWindowLongPtr(hwnd, GwlExStyle);
        if ((style & WsChild) != 0 || (ex & WsExToolWindow) != 0 || (ex & WsExNoActivate) != 0) return false;
        nint owner = GetWindow(hwnd, 4); // GW_OWNER
        if (owner != 0 && (ex & WsExAppWindow) == 0) return false;
        if (DwmGetWindowAttributeInt(hwnd, DwmCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        string cls = ClassOf(hwnd);
        return !IsShell(cls) && !cls.StartsWith("DispCtrl.", StringComparison.Ordinal);
    }

    /// <summary>The shell's surfaces, which are windows to Windows and not to anybody else.</summary>
    public static bool IsShell(string cls) =>
        cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "MultitaskingViewFrame"
            or "XamlExplorerHostIslandWindow" or "Windows.UI.Core.CoreWindow" or "ForegroundStaging"
            or "TopLevelWindowForOverflowXamlIsland" or "NotifyIconOverflowWindow";

    public static string ClassOf(nint hwnd)
    {
        char* name = stackalloc char[256];
        int count = GetClassName(hwnd, name, 256);
        return count > 0 ? new string(name, 0, count) : "";
    }

    public static string Title(nint hwnd)
    {
        int length = Math.Min(GetWindowTextLength(hwnd), 512);
        if (length <= 0) return "";
        char* text = stackalloc char[length + 1];
        int count = GetWindowText(hwnd, text, length + 1);
        return count > 0 ? new string(text, 0, count) : "";
    }

    /// <summary>What is drawn: DWM's extended frame, or the window rectangle when DWM will not say.</summary>
    public static DisplayRect Frame(nint hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DwmExtendedFrameBounds, out Rect frame, (uint)sizeof(Rect)) != 0)
            _ = GetWindowRect(hwnd, out frame);
        return new DisplayRect(frame.Left, frame.Top, frame.Right, frame.Bottom);
    }

    /// <summary>The window's own rectangle, invisible resize borders included - what SetWindowPos takes.</summary>
    public static DisplayRect Outer(nint hwnd)
    {
        _ = GetWindowRect(hwnd, out Rect rect);
        return new DisplayRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    /// <summary>An executable's name without <c>.exe</c>, or empty when the process cannot be opened.</summary>
    /// <remarks>
    /// Limited-information access, which is granted even for an elevated
    /// process; <c>Process.GetProcessById</c> takes a snapshot of every process
    /// on the machine to answer the same question.
    /// </remarks>
    public static string ProcessName(uint pid)
    {
        // Remembered briefly: OLED care's exception list asks for every
        // window's app once a second, and a process keeps its name for life.
        long now = Environment.TickCount64;
        lock (Names)
        {
            if (Names.TryGetValue(pid, out var known) && now - known.At < NameLifetimeMs) return known.Name;
            if (Names.Count > 512) Names.Clear();
        }
        string name = "";
        nint handle = OpenProcess(ProcessQueryLimited, 0, pid);
        if (handle != 0)
        {
            try
            {
                char* buffer = stackalloc char[1024];
                uint size = 1024;
                if (QueryFullProcessImageName(handle, 0, buffer, ref size) != 0)
                    name = Path.GetFileNameWithoutExtension(new string(buffer, 0, (int)size));
            }
            finally { _ = CloseHandle(handle); }
        }
        lock (Names) Names[pid] = (name, now);
        return name;
    }

    /// <summary>Short enough that an id Windows hands to a new process is not answered with the old name for long.</summary>
    private const long NameLifetimeMs = 30_000;
    private static readonly Dictionary<uint, (string Name, long At)> Names = [];

    /// <summary>Whether a window fills the whole of the display it is on, as a game or a film does.</summary>
    public static bool FillsDisplay(nint hwnd, IReadOnlyList<DisplayInfo> displays)
    {
        DisplayRect frame = Frame(hwnd);
        foreach (DisplayInfo d in displays)
            if (frame.Left <= d.Bounds.Left && frame.Top <= d.Bounds.Top && frame.Right >= d.Bounds.Right && frame.Bottom >= d.Bounds.Bottom)
                return true;
        return false;
    }

    /// <summary>
    /// Where the showing, not minimized windows of these apps are: OLED care's
    /// exception list, asked once a second.
    /// </summary>
    /// <remarks>
    /// Built on <see cref="List"/>, it described every window on the desk each
    /// time - title, class, cloaking, frame, pin mark - to find two apps, and
    /// was measured as the whole cost of the window features at idle (+130
    /// million cycles a minute). The cheap tests go first: visible, then the
    /// app's name from the per-process cache; only a match is looked at further.
    /// </remarks>
    public static List<DisplayRect> ShowingFrames(HashSet<string> processes)
    {
        var found = new List<DisplayRect>();
        if (processes.Count == 0) return found;
        var handles = new List<nint>();
        GCHandle box = GCHandle.Alloc(handles);
        try { _ = EnumWindows(&CollectVisible, GCHandle.ToIntPtr(box)); }
        finally { box.Free(); }
        foreach (nint hwnd in handles)
        {
            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            if (!processes.Contains(ProcessName(pid)) || IsIconic(hwnd) != 0 || !IsCandidate(hwnd)) continue;
            found.Add(Frame(hwnd));
        }
        return found;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CollectVisible(nint hwnd, nint param)
    {
        if (IsWindowVisible(hwnd) != 0 && GCHandle.FromIntPtr(param).Target is List<nint> list) list.Add(hwnd);
        return 1;
    }

    /// <summary>A film or a game filling its display, rather than an ordinary window maximized.</summary>
    /// <remarks>
    /// Content fullscreen, as focus mode and OLED care judge it: a maximized
    /// window with a title bar fills the whole display too when its taskbar is
    /// hidden - the laptop's on this desk - and is not fullscreen.
    /// </remarks>
    public static bool IsFullscreen(nint hwnd, WindowShow show, IReadOnlyList<DisplayInfo> displays) =>
        !(show == WindowShow.Maximized && (GetWindowLongPtr(hwnd, GwlStyle) & WsCaption) == WsCaption)
        && FillsDisplay(hwnd, displays);

    /// <summary>The display a window mostly sits on, or null when it is on none.</summary>
    public static DisplayInfo? DisplayOf(nint hwnd, IReadOnlyList<DisplayInfo> displays)
    {
        // A minimized window's own rectangle is parked off-screen; where it
        // will come back is its restore position, on the display it left.
        DisplayRect rect = IsIconic(hwnd) != 0 ? RestoreRect(hwnd, displays) : Frame(hwnd);
        int index = WindowGeometry.MostlyOn(rect, displays.Select(d => d.Bounds).ToList());
        if (index >= 0) return displays[index];
        nint monitor = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
        return displays.FirstOrDefault(d => d.Handle == monitor);
    }

    /// <summary>Where a window goes when restored, in screen coordinates.</summary>
    /// <remarks>
    /// The placement is in workspace coordinates, which are offset by the work
    /// area of the display the rectangle lies on; the display it was left on is
    /// the nearest guess at that before the conversion.
    /// </remarks>
    public static DisplayRect RestoreRect(nint hwnd, IReadOnlyList<DisplayInfo> displays)
    {
        var placement = new PlacementData { Length = (uint)sizeof(PlacementData) };
        if (GetWindowPlacement(hwnd, ref placement) == 0) return Frame(hwnd);
        Rect n = placement.NormalPosition;
        var workspace = new DisplayRect(n.Left, n.Top, n.Right, n.Bottom);
        return WindowMover.FromWorkspace(workspace, displays);
    }
}
