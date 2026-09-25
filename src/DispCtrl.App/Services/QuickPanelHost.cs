using System.Runtime.InteropServices;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using Microsoft.UI.Dispatching;

namespace DispCtrl.App.Services;

/// <summary>
/// Everything around the quick panel that is not XAML: where the cursor is,
/// which monitor that makes it, and how a summons from the tray gets here.
/// </summary>
/// <remarks>
/// The waiting is done on a thread of its own rather than on a timer, because a
/// tray click has to land now and a poll that was quick enough not to be
/// noticed would be a poll running all day. A thread blocked in
/// <c>WaitHandle.WaitAny</c> costs nothing until it is signalled.
/// </remarks>
public static partial class QuickPanelHost
{
    private const uint MonitorDefaultToNearest = 2;

    /// <summary><c>MDT_EFFECTIVE_DPI</c>.</summary>
    private const uint EffectiveDpi = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(POINT point, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, uint type, out uint x, out uint y);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    /// <summary>Where the pointer is, horizontally, in physical pixels.</summary>
    public static int CursorX() => GetCursorPos(out POINT point) ? point.X : 0;

    /// <summary>
    /// The monitor the pointer is on: its bounds, its work area, and its DPI.
    /// </summary>
    /// <remarks>
    /// The pointer rather than the primary display, because the panel is
    /// summoned by clicking a tray icon and the tray the click landed on is the
    /// one on that monitor. Opening on the primary instead would send somebody
    /// who clicked on their second screen looking for the panel on their first.
    /// </remarks>
    public static (DisplayRect Bounds, DisplayRect Work, uint Dpi) MonitorUnderCursor()
    {
        _ = GetCursorPos(out POINT point);
        nint monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        return Describe(monitor);
    }

    /// <summary>The monitor a window is on: its bounds, its work area, and its DPI.</summary>
    public static (DisplayRect Bounds, DisplayRect Work, uint Dpi) MonitorOf(nint hwnd) =>
        Describe(MonitorFromWindow(hwnd, MonitorDefaultToNearest));

    private static (DisplayRect, DisplayRect, uint) Describe(nint monitor)
    {
        MONITORINFO info = new() { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
            throw new InvalidOperationException("no monitor");

        uint dpi = 96;
        if (GetDpiForMonitor(monitor, EffectiveDpi, out uint x, out _) == 0 && x > 0) dpi = x;

        return (
            new DisplayRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom),
            new DisplayRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom),
            dpi);
    }

    /// <summary>The scale factor of whichever monitor a window is on.</summary>
    public static double ScaleOf(nint hwnd)
    {
        uint dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    // ---- being summoned ----

    private static Thread? _listener;
    private static Action? _onSummon;

    /// <summary>
    /// Starts waiting for the tray icon, and holds the token that says so.
    /// </summary>
    /// <param name="queue">The UI thread's queue; the panel may only be shown from there.</param>
    /// <param name="summon">What to do when the tray icon is clicked.</param>
    /// <returns>
    /// The mutex proving this process is the listener, to be held for as long
    /// as it is. Null when another process already holds it, in which case this
    /// one has nothing to listen for.
    /// </returns>
    public static Mutex? Listen(DispatcherQueue queue, Action summon, Action? identify = null, Action? window = null,
                                Action? quit = null, Action? displays = null)
    {
        Mutex alive = QuickPanelSignal.OpenAlive(out bool held);
        if (!held) return null;

        _onSummon = summon;

        _listener = new Thread(() =>
        {
            try
            {
                using EventWaitHandle show = QuickPanelSignal.OpenShow();
                using EventWaitHandle number = QuickPanelSignal.OpenIdentify();
                using EventWaitHandle full = QuickPanelSignal.OpenWindowEvent();
                using EventWaitHandle leave = QuickPanelSignal.OpenQuit();
                using EventWaitHandle changed = QuickPanelSignal.OpenDisplaysChanged();
                WaitHandle[] both = [show, number, full, leave, changed];

                while (true)
                {
                    int which = WaitHandle.WaitAny(both);

                    // Enqueued rather than called: this is not the UI thread,
                    // and touching a XAML tree from here is the kind of bug
                    // that shows up as a crash somewhere unrelated later.
                    Action? handler = which switch { 0 => _onSummon, 1 => identify, 2 => window, 3 => quit, _ => displays };
                    if (handler is not null) _ = queue.TryEnqueue(() => handler());
                }
            }
            catch (Exception)
            {
                // The process is going away. Nothing to recover.
            }
        })
        {
            IsBackground = true,
            Name = "DispCtrl panel summons",
        };

        _listener.Start();

        QuickPanelSignal.RecordAppPath(Environment.ProcessPath ?? string.Empty);
        return alive;
    }

    /// <summary>Brings up the full window in this process.</summary>
    /// <remarks>
    /// In this process rather than a new one: the view model here already
    /// holds every display, and a second process would enumerate them all again
    /// and then disagree with the panel about what it had read.
    /// </remarks>
    public static void OpenMainWindow(string? page = null) => App.ShowMainWindow(page);
}
