using System.Runtime.InteropServices;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using static DispCtrl.Display.Placement.WindowNative;

namespace DispCtrl.Display.Placement;

/// <summary>What pinning or unpinning a window came to.</summary>
public sealed record PinOutcome(bool Done, bool Pinned, string Message);

/// <summary>
/// Keeps a window above every other one: Always On Top.
/// </summary>
/// <remarks>
/// The pin is the window's own state - Windows' topmost flag - plus a window
/// property that says DispCtrl set it. The property is what lets any DispCtrl
/// process see what is pinned without sharing a list, and what keeps an unpin
/// from taking topmost off a window that was on top by itself (Task Manager
/// with its own "always on top", say). Both go when the window does, so a
/// pin never outlives what it pinned. The engine is told through a named event
/// and draws the border; without the engine a pin still works, unbordered.
/// </remarks>
public static class WindowPins
{
    /// <summary>The property that marks a window DispCtrl pinned.</summary>
    public const string Property = "DispCtrl.Pinned";

    /// <summary>Set whenever a window is pinned or unpinned, from any process; the engine redraws borders on it.</summary>
    public const string ChangedEvent = @"Local\DispCtrl.Pins.Changed";

    public static bool IsPinned(nint hwnd) => hwnd != 0 && GetProp(hwnd, Property) != 0;

    /// <summary>Every window DispCtrl has pinned, top of the z-order first.</summary>
    public static List<AppWindow> List() => AppWindows.List().Where(w => w.Pinned).ToList();

    /// <summary>Pins the window, or unpins it if it is pinned.</summary>
    public static PinOutcome Toggle(nint hwnd, PinSettings settings) =>
        IsPinned(hwnd) ? Unpin(hwnd) : Pin(hwnd, settings);

    public static PinOutcome Pin(nint hwnd, PinSettings settings)
    {
        if (!settings.Enabled) return new(false, false, "Pinning windows is switched off.");
        if (AppWindows.Describe(hwnd) is not { } window)
            return new(false, false, "That is not an app window that can be pinned.");
        if (window.Pinned) return new(true, true, $"{Name(window)} is already pinned.");
        if (window.Topmost) return new(false, false, $"{Name(window)} already stays on top by itself.");
        if (window.Process.Length > 0 && settings.Exclusions().Contains(window.Process))
            return new(false, false, $"{window.Process} is on the list of apps never pinned.");
        if (settings.SkipFullscreen && AppWindows.IsFullscreen(hwnd, window.Show, DisplayRegistry.Enumerate()))
            return new(false, false, $"{Name(window)} fills its display; a fullscreen window is not pinned.");

        if (SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder) == 0)
            return new(false, false, Refused(window));
        if (SetProp(hwnd, Property, 1) == 0)
        {
            // Topmost without the mark would be a pin nothing could take back.
            _ = SetWindowPos(hwnd, HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
            return new(false, false, Refused(window));
        }
        Announce();
        return new(true, true, $"{Name(window)} is pinned on top.");
    }

    public static PinOutcome Unpin(nint hwnd)
    {
        if (!IsPinned(hwnd)) return new(true, false, "That window is not pinned.");
        string name = AppWindows.Title(hwnd);
        bool moved = SetWindowPos(hwnd, HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder) != 0;
        _ = RemoveProp(hwnd, Property);
        Announce();
        return moved
            ? new(true, false, $"{Short(name)} is no longer pinned.")
            : new(false, true, $"{Short(name)} runs as administrator, and Windows lets only an app running as administrator take it off the top.");
    }

    /// <summary>Unpins every window DispCtrl pinned.</summary>
    /// <returns>How many were unpinned.</returns>
    public static int UnpinAll()
    {
        int count = 0;
        foreach (AppWindow window in List())
            if (Unpin(window.Handle).Done) count++;
        return count;
    }

    /// <summary>The window in front, for "pin the active window".</summary>
    public static nint Foreground() => GetForegroundWindow();

    /// <summary>
    /// A window named on the command line: a handle (<c>0x1A2B</c>), an
    /// executable name, or part of a title - the first match, top of the z-order first.
    /// </summary>
    public static AppWindow? Find(string text)
    {
        List<AppWindow> windows = AppWindows.List();
        string trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out long handle))
            return windows.FirstOrDefault(w => w.Handle == (nint)handle) ?? AppWindows.Describe((nint)handle);
        string process = trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed;
        return windows.FirstOrDefault(w => string.Equals(w.Process, process, StringComparison.OrdinalIgnoreCase))
            ?? windows.FirstOrDefault(w => w.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Tells the engine the set of pinned windows has changed.</summary>
    public static void Announce()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ChangedEvent, out EventWaitHandle? changed))
                using (changed) changed.Set();
        }
        catch (Exception) { }
    }

    /// <summary>Why a pin was refused, in the only case Windows refuses one.</summary>
    /// <remarks>
    /// UIPI: a window of a process running as administrator cannot be moved
    /// in the z-order by a process that is not. DispCtrl never runs elevated,
    /// on purpose, so this is said rather than worked around.
    /// </remarks>
    private static string Refused(AppWindow window) =>
        $"{Name(window)} runs as administrator, and Windows does not let an ordinary app pin it (error {Marshal.GetLastPInvokeError()}).";

    private static string Name(AppWindow window) => Short(window.Title);

    private static string Short(string title) => title.Length > 48 ? "“" + title[..47] + "…”" : "“" + title + "”";
}
