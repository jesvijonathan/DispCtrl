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
    /// Named mutexes, not a process lookup: several DispCtrl.App processes can
    /// legitimately exist — the full window is one — and only the one holding
    /// this is listening for a summons.
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

        string? exe = null;
        try
        {
            if (System.IO.File.Exists(AppPathFile))
            {
                string recorded = System.IO.File.ReadAllText(AppPathFile).Trim();
                if (System.IO.File.Exists(recorded)) exe = recorded;
            }
        }
        catch (Exception) { }

        // A deployed layout has both executables in one folder, so the sibling
        // is worth a look before giving up on a desk where the app has not been
        // run since the path file was last cleared.
        exe ??= Sibling();
        if (exe is null) return false;

        try
        {
            Process.Start(new ProcessStartInfo(exe, "--panel") { UseShellExecute = false });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
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
