using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

namespace DispCtrl.Engine.Color;

/// <summary>
/// Brings a monitor that has just been connected back into unison.
/// </summary>
/// <remarks>
/// A monitor keeps its own brightness while unplugged, so one reconnected after
/// the unison level moved comes back at the old level and stays there until the
/// slider is touched again - on a desk where every display is meant to agree,
/// that looks like unison having broken. Only monitors that arrive are written;
/// the ones that stayed attached are already where unison put them.
/// <para>
/// The GDI fingerprint is polled once a second, which costs one
/// <c>EnumDisplayMonitors</c>. A DDC/CI channel is not ready the moment the
/// monitor appears, so the write waits and retries rather than taking the first
/// refusal as the answer.
/// </para>
/// </remarks>
internal sealed class UnisonHotplug : IDisposable
{
    private const int PollMs = 1000;
    private const int ReadyDelayMs = 1500;
    private const int Attempts = 3;

    private readonly Timer _timer;
    private HashSet<string> _attached;
    private string _signature;
    private int _busy;
    private volatile bool _disposed;

    public UnisonHotplug()
    {
        _signature = DisplayRegistry.CheapSignature();
        List<DisplayInfo> now = DisplayRegistry.Enumerate();
        _attached = now.Select(d => d.Token).ToHashSet(StringComparer.Ordinal);
        // The same arrivals feed the local device history; it costs nothing
        // more than the enumeration already done here.
        Display.Devices.DeviceObserver.Attached(now);
        // Well after sign-in: nothing about learning a model is urgent, and the
        // first seconds belong to the taskbar and the tray.
        Learn(now, StartupLearnDelayMs);
        _timer = new Timer(_ => Poll(), null, PollMs, PollMs);
    }

    private const int StartupLearnDelayMs = 20_000;

    /// <summary>Reads the codes of any attached model never read here, in the background.</summary>
    private void Learn(IEnumerable<DisplayInfo> displays, int delayMs)
    {
        List<DisplayInfo> todo = Display.Devices.DeviceDiscovery.Unread(displays);
        if (todo.Count == 0) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMs).ConfigureAwait(false);
            foreach (DisplayInfo display in todo)
            {
                if (_disposed) return;
                try
                {
                    int codes = Display.Devices.DeviceDiscovery.Learn(display);
                    Log.Write(codes < 0
                        ? $"devices: {display.Key.Model} ({display.Label}) did not answer DDC/CI; not recorded"
                        : $"devices: learned {display.Key.Model} ({display.Label}), {codes} code(s)");
                }
                catch (Exception ex) { Log.Write($"devices: reading {display.Key.Model} failed: {ex.Message}"); }
            }
        });
    }

    private void Poll()
    {
        if (_disposed || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        try
        {
            string signature = DisplayRegistry.CheapSignature();
            if (signature == _signature) return;
            _signature = signature;

            List<DisplayInfo> displays = DisplayRegistry.Enumerate();
            Display.Devices.DeviceObserver.Attached(displays);
            List<DisplayInfo> arrived = displays.Where(d => !_attached.Contains(d.Token) && !d.IsInternal).ToList();
            _attached = displays.Select(d => d.Token).ToHashSet(StringComparer.Ordinal);
            if (arrived.Count == 0) return;
            // The DDC/CI channel is not up when the monitor enumerates.
            Learn(displays, ReadyDelayMs * 2);

            DispCtrlSettings settings = SettingsStore.Load();
            if (!settings.Global.UnisonBrightness || UnisonCalibration.IsActive) return;

            Thread.Sleep(ReadyDelayMs);
            foreach (DisplayInfo display in arrived)
            {
                if (_disposed) return;
                MonitorSettings monitor = settings.For(display.Token);
                if (!monitor.HasBrightnessRange && monitor.BrightnessBaseline <= 0) continue;
                int target = UnisonResume.Target(monitor, settings.Global.UnisonCalibrated, settings.Global.UnisonLevel);
                Log.Write($"hot-plug: {display.Label} {(Sync(display, target) ? "set" : "could not be set")} to {target}% for unison {settings.Global.UnisonLevel}%");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"hot-plug: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private bool Sync(DisplayInfo display, int target)
    {
        for (int attempt = 0; attempt < Attempts && !_disposed; attempt++)
        {
            if (attempt > 0) Thread.Sleep(ReadyDelayMs);
            using var operations = new Mutex(false, @"Local\DispCtrl.Control.Operations");
            bool held;
            try { held = operations.WaitOne(2000); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) continue;
            try
            {
                BrightnessRange range = Brightness.Read(display);
                if (range.Supported && Brightness.Write(display, range.FromPercent(target))) return true;
            }
            finally { operations.ReleaseMutex(); }
        }
        return false;
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
