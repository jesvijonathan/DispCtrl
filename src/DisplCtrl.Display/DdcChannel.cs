using System.Collections.Concurrent;
using DisplCtrl.Core.Displays;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace DisplCtrl.Display;

/// <summary>
/// The one way to talk to a monitor over DDC/CI.
/// </summary>
/// <remarks>
/// A monitor has a single DDC/CI channel and will not serve two conversations
/// at once. Two callers opening it together do not queue — the monitor answers
/// one and fails the other, and on some panels a collision leaves it refusing
/// requests for a while afterwards.
/// <para>
/// That is not hypothetical. Reading a monitor's capabilities in parallel with
/// reading its brightness — both perfectly reasonable on their own — made the
/// panel report no brightness control and no capabilities string at all, which
/// looked exactly like a monitor that had stopped answering. Every DDC/CI call
/// in DisplCtrl goes through here so that cannot happen again.
/// </para>
/// <para>
/// Internal because <c>HANDLE</c> is: CsWin32 generates its types as internal,
/// so a public method taking one would not compile. Callers outside this
/// assembly use the typed wrappers — <see cref="Brightness"/> and
/// <see cref="MonitorCapabilities"/> — which is the right level anyway.
/// </para>
/// </remarks>
internal static class DdcChannel
{
    /// <summary>
    /// One gate per monitor, shared across every DisplCtrl process.
    /// </summary>
    /// <remarks>
    /// A named mutex rather than a semaphore, because the callers are in
    /// different processes. The engine sweeps capabilities on a timer, the panel
    /// reads brightness when it opens, and the command line does whatever it was
    /// asked — three processes, one channel per monitor. An in-process gate
    /// serialises none of that, and a collision on this channel does not fail
    /// loudly: the monitor answers one caller and hands the other a reply
    /// belonging to a different question.
    /// <para>
    /// Two monitors have two independent channels, so the name carries the
    /// device path's hash — serialising across monitors would double every
    /// sweep for nothing.
    /// </para>
    /// <para>
    /// <c>Local\</c> scopes it to the logon session, matching the engine's own
    /// single-instance mutex. DisplCtrl is per-user; a second user's monitors are
    /// not these monitors.
    /// </para>
    /// </remarks>
    private static readonly ConcurrentDictionary<string, Mutex> Gates = new();

    private static Mutex GateFor(DisplayInfo display) =>
        Gates.GetOrAdd(display.Key.DevicePath, static path => new Mutex(false, MutexName(path)));

    /// <summary>
    /// A mutex name derived from the device path.
    /// </summary>
    /// <remarks>
    /// Hashed rather than used directly: a device path contains backslashes,
    /// which are the namespace separator in a kernel object name, and is longer
    /// than the 260-character limit on some systems. FNV-1a for the same reason
    /// <c>DisplayKey.ToToken</c> uses it — <c>GetHashCode</c> is randomised per
    /// process, so two processes would derive different names for one monitor
    /// and the gate would not gate anything.
    /// </remarks>
    private static string MutexName(string devicePath)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;

        ulong hash = offset;
        foreach (char c in devicePath.ToUpperInvariant())
        {
            hash ^= c;
            hash *= prime;
        }

        return $"Local\\DisplCtrl.Ddc.{hash:x16}";
    }

    /// <summary>
    /// How long to wait for another caller to finish before giving up.
    /// </summary>
    /// <remarks>
    /// Generous, because the thing most likely to be holding the gate is a full
    /// capabilities sweep — dozens of round trips. Giving up early would put
    /// back exactly the failure this exists to prevent.
    /// </remarks>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Opens the physical monitor behind a display, runs <paramref name="work"/>,
    /// and always closes it again.
    /// </summary>
    /// <remarks>
    /// Blocking. The handle must be destroyed explicitly: leaking it holds the
    /// channel open, after which the monitor refuses later requests.
    /// </remarks>
    public static unsafe T With<T>(DisplayInfo display, Func<HANDLE, T> work, T fallback)
    {
        Mutex gate = GateFor(display);

        bool held;
        try
        {
            held = gate.WaitOne(Patience);
        }
        catch (AbandonedMutexException)
        {
            // The holder died mid-conversation. The channel is free, and the
            // monitor will have given up on whatever was in flight; taking it is
            // correct and the alternative is never talking to this monitor again.
            held = true;
        }

        if (!held) return fallback;

        try
        {
            var hmon = new HMONITOR((void*)display.Handle);
            if (hmon.IsNull) return fallback;

            if (!PInvoke.GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out uint count) || count == 0)
                return fallback;

            var monitors = new PHYSICAL_MONITOR[count];
            if (!PInvoke.GetPhysicalMonitorsFromHMONITOR(hmon, monitors)) return fallback;

            try
            {
                return work(monitors[0].hPhysicalMonitor);
            }
            catch (Exception)
            {
                return fallback;
            }
            finally
            {
                foreach (PHYSICAL_MONITOR m in monitors)
                    PInvoke.DestroyPhysicalMonitor(m.hPhysicalMonitor);
            }
        }
        finally
        {
            gate.ReleaseMutex();
        }
    }
}
