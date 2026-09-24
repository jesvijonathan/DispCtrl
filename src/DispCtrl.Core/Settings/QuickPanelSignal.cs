using System.Diagnostics;
using System.Threading;

namespace DispCtrl.Core.Settings;

/// <summary>
/// How the tray icon reaches the panel.
/// </summary>
/// <remarks>
/// The two DispCtrl processes otherwise share only <c>settings.json</c>, and that
/// is deliberate: a file both can read is not a protocol either has to keep. A
/// tray icon breaks the arrangement, because a click has to arrive now rather
/// than at the next poll, and a file watched with a 120 ms debounce is not now.
/// <para>
/// A named event is the smallest thing that does it: no pipe, no serialisation,
/// nothing to version. The engine sets it, the panel waits on it, and neither
/// has to know whether the other exists — a set with nobody waiting is simply
/// lost, which is the right behaviour for "show yourself".
/// </para>
/// <para>
/// Session-local (<c>Local\</c>), not <c>Global\</c>. Two people signed in at
/// once have two engines and two panels, and a global name would cross them.
/// </para>
/// </remarks>
public static class QuickPanelSignal
{
    private const string ShowName = @"Local\DispCtrl.QuickPanel.Show";
    private const string AliveName = @"Local\DispCtrl.QuickPanel.Alive";
    private const string IdentifyName = @"Local\DispCtrl.Identify";
    private const string WindowName = @"Local\DispCtrl.App.ShowWindow";
    private const string QuitName = @"Local\DispCtrl.App.Quit";
    private const string DisplaysName = @"Local\DispCtrl.Displays.Changed";

    /// <summary>Opens, or creates, the event the engine sets when the displays have changed and settled.</summary>
    public static EventWaitHandle OpenDisplaysChanged() =>
        new EventWaitHandle(false, EventResetMode.AutoReset, DisplaysName);

    /// <summary>
    /// Tells a running app that a display arrived, left or changed, once the
    /// change is over. Nothing when no app is running.
    /// </summary>
    /// <remarks>
    /// The engine has already waited for the layout to hold; the app would
    /// otherwise notice only when its window next polled, and a quick panel
    /// open on screen never polls at all.
    /// </remarks>
    public static void DisplaysChanged()
    {
        if (!PanelIsListening()) return;
        try
        {
            using EventWaitHandle changed = OpenDisplaysChanged();
            changed.Set();
        }
        catch (Exception) { }
    }

    /// <summary>Opens, or creates, the event that asks the running app to close everything and exit.</summary>
    public static EventWaitHandle OpenQuit() =>
        new EventWaitHandle(false, EventResetMode.AutoReset, QuitName);

    /// <summary>
    /// Asks the DispCtrl app to exit: the window, the quick panel, and the
    /// process that keeps it preloaded. False when no app is running.
    /// </summary>
    public static bool RequestQuit()
    {
        if (!PanelIsListening()) return false;
        try
        {
            using EventWaitHandle quit = OpenQuit();
            return quit.Set();
        }
        catch (Exception) { return false; }
    }

    /// <summary>Opens, or creates, the event that asks the running app to bring up its window.</summary>
    public static EventWaitHandle OpenWindowEvent() =>
        new EventWaitHandle(false, EventResetMode.AutoReset, WindowName);

    /// <summary>Asks the DispCtrl already running to show its window; false when none is listening.</summary>
    public static bool ShowRunningWindow()
    {
        if (!PanelIsListening()) return false;
        try
        {
            using EventWaitHandle window = OpenWindowEvent();
            return window.Set();
        }
        catch (Exception) { return false; }
    }

    /// <summary>Opens, or creates, the event that asks the app to number the displays.</summary>
    public static EventWaitHandle OpenIdentify() =>
        new EventWaitHandle(false, EventResetMode.AutoReset, IdentifyName);

    /// <summary>
    /// Numbers every display: through the app that is listening, or by starting
    /// one hidden to do it. The overlays are XAML, so they are the app's to draw.
    /// </summary>
    public static bool Identify()
    {
        if (PanelIsListening())
        {
            try
            {
                using EventWaitHandle identify = OpenIdentify();
                return identify.Set();
            }
            catch (Exception) { }
        }
        return Start("--panel --background --identify");
    }

    /// <summary>Where the panel records the executable to start it with.</summary>
    /// <remarks>
    /// The engine cannot work out where the app is: in development the two live
    /// in separate <c>bin</c> trees, and the engine's own folder has no app in
    /// it. So the app leaves its path behind on every run, and the engine reads
    /// it. Self-healing, and wrong only until the app is next started.
    /// </remarks>
    public static string AppPathFile => System.IO.Path.Combine(SettingsStore.Directory, "app.path");

    /// <summary>Opens, or creates, the event the panel waits on.</summary>
    public static EventWaitHandle OpenShow() =>
        new EventWaitHandle(false, EventResetMode.AutoReset, ShowName);

    /// <summary>
    /// Held for as long as a panel process is running.
    /// </summary>
    /// <remarks>
    /// Named mutexes, not a process lookup. Whoever holds it is the one
    /// DispCtrl.App: it listens for the panel, Identify and the window, and a
    /// second launch hands its request over and exits.
    /// </remarks>
    public static Mutex OpenAlive(out bool held)
    {
        var mutex = new Mutex(true, AliveName, out bool created);
        held = created;
        if (!held) mutex.Dispose();
        return mutex;
    }

    /// <summary>True when a process is already waiting to be summoned.</summary>
    public static bool PanelIsListening()
    {
        try
        {
            using var mutex = new Mutex(false, AliveName, out _);
            if (!mutex.WaitOne(0)) return true;
            mutex.ReleaseMutex();
            return false;
        }
        catch (AbandonedMutexException)
        {
            // The holder died without releasing. Nothing is listening.
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Records this process as the one to start for a summons.</summary>
    public static void RecordAppPath(string path)
    {
        try { System.IO.File.WriteAllText(AppPathFile, path); }
        catch (Exception) { /* A panel that cannot be started from the tray is still a panel. */ }
    }

    /// <summary>
    /// Shows the panel: wakes a listening one, or starts one that will listen.
    /// </summary>
    /// <returns>False when there is no app to start and none listening.</returns>
    public static bool Summon()
    {
        if (PanelIsListening())
        {
            try
            {
                using EventWaitHandle show = OpenShow();
                return show.Set();
            }
            catch (Exception)
            {
                // Fall through and try starting one instead.
            }
        }

        return Start("--panel");
    }

    /// <summary>
    /// Starts a panel process that listens without showing anything, so the
    /// first summons finds it warm.
    /// </summary>
    /// <returns>False when one is already listening or there is no app to start.</returns>
    public static bool Preload() => !PanelIsListening() && Start("--panel --background");

    /// <summary>Starts the app with the full window.</summary>
    public static bool OpenWindow() => Start("");

    private static bool Start(string arguments)
    {
        string? exe = AppExecutable();
        if (exe is null) return false;

        try
        {
            Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Where the app is: as it last recorded, or beside this executable.</summary>
    public static string? AppExecutable()
    {
        try
        {
            if (System.IO.File.Exists(AppPathFile))
            {
                string recorded = System.IO.File.ReadAllText(AppPathFile).Trim();
                if (System.IO.File.Exists(recorded)) return recorded;
            }
        }
        catch (Exception) { }

        // A deployed layout has both executables in one folder, so the sibling
        // is worth a look before giving up on a desk where the app has not been
        // run since the path file was last cleared.
        return Sibling();
    }

    private static string? Sibling()
    {
        try
        {
            string? here = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
            if (here is null) return null;

            string candidate = System.IO.Path.Combine(here, "DispCtrl.App.exe");
            return System.IO.File.Exists(candidate) ? candidate : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
