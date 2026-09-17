using System.Collections.Concurrent;
using Umbra.Core.Displays;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Umbra.Display;

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
/// in Umbra goes through here so that cannot happen again.
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
    /// One gate per monitor, not one globally.
    /// </summary>
    /// <remarks>
    /// Two monitors have two independent channels, and serialising across them
    /// would double the time a two-monitor sweep takes for no reason.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    private static SemaphoreSlim GateFor(DisplayInfo display) =>
        Gates.GetOrAdd(display.Key.DevicePath, static _ => new SemaphoreSlim(1, 1));

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
        SemaphoreSlim gate = GateFor(display);
        if (!gate.Wait(Patience)) return fallback;

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
            gate.Release();
        }
    }
}
