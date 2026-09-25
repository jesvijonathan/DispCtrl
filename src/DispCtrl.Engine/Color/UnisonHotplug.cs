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
/// Told by <see cref="DisplayChanges"/> once a change is over, rather than
/// polling for one of its own. A DDC/CI channel is not ready the moment the
/// monitor appears, so the write waits and retries rather than taking the
/// first refusal as the answer, and a newer change abandons it: the monitor
/// it was for may have gone again.
/// </para>
/// </remarks>
internal sealed class UnisonHotplug : IDisposable
{
    private const int ReadyDelayMs = 1500;
    private const int Attempts = 3;
    private const int StartupLearnDelayMs = 20_000;

    private int _generation;
    private volatile bool _disposed;

    public UnisonHotplug(List<DisplayInfo> attached)
    {
        // The same arrivals feed the local device history. Off the start-up
        // path: each sighting loads and parses history.json, ~50 ms for two
        // displays that held every service behind it. (The CLI records in its
        // own thread because it may exit first; the engine does not.)
        _ = Task.Run(() => Display.Devices.DeviceObserver.Attached(attached));
        // Well after sign-in: nothing about learning a model is urgent, and the
        // first seconds belong to the taskbar and the tray.
        Learn(attached, StartupLearnDelayMs);
        DisplayChanges.Settled += OnSettled;
    }

    private void OnSettled(DisplayChange change)
    {
        if (_disposed) return;
        int generation = Interlocked.Increment(ref _generation);
        Display.Devices.DeviceObserver.Attached(change.Displays);
        List<DisplayInfo> arrived = change.Arrived.Where(d => !d.IsInternal).ToList();
        if (arrived.Count == 0) return;
        // The DDC/CI channel is not up when the monitor enumerates.
        Learn(change.Displays, ReadyDelayMs * 2);
        // Off the watcher's thread: the waits below are seconds, and every
        // other service is told about the same change after this one.
        _ = Task.Run(() => Sync(arrived, generation));
    }

    /// <summary>Reads the codes of any attached model never read here, in the background.</summary>
    private void Learn(IEnumerable<DisplayInfo> displays, int delayMs)
    {
        List<DisplayInfo> given = [.. displays];
        _ = Task.Run(async () =>
        {
            // In here, not before: deciding what is unread parses history.json,
            // ~40 ms the engine's start-up used to wait for.
            List<DisplayInfo> todo = Display.Devices.DeviceDiscovery.Unread(given);
            if (todo.Count == 0) return;
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

    private void Sync(List<DisplayInfo> arrived, int generation)
    {
        try
        {
            DispCtrlSettings settings = SettingsStore.Load();
            if (!settings.Global.UnisonBrightness || UnisonCalibration.IsActive) return;

            Thread.Sleep(ReadyDelayMs);
            foreach (DisplayInfo display in arrived)
            {
                if (_disposed || Volatile.Read(ref _generation) != generation) return;
                MonitorSettings monitor = settings.For(display.Token);
                if (!monitor.HasBrightnessRange && monitor.BrightnessBaseline <= 0) continue;
                // Read again at the moment of writing: the level may have moved
                // while this waited, from a key on the laptop alone, say.
                int level = SettingsStore.Load().Global.UnisonLevel;
                int target = UnisonResume.Target(monitor, settings.Global.UnisonCalibrated, level);
                Log.Write($"hot-plug: {display.Label} {(Write(display, target, generation) ? "set" : "could not be set")} to {target}% for unison {level}%");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"hot-plug: {ex.Message}");
        }
    }

    private bool Write(DisplayInfo display, int target, int generation)
    {
        for (int attempt = 0; attempt < Attempts && !_disposed; attempt++)
        {
            if (attempt > 0) Thread.Sleep(ReadyDelayMs);
            if (Volatile.Read(ref _generation) != generation) return false;
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
        DisplayChanges.Settled -= OnSettled;
    }
}

/// <summary>A change to the displays that is over: what is attached now, and what came and went.</summary>
internal sealed record DisplayChange(List<DisplayInfo> Displays, List<DisplayInfo> Arrived, List<string> Departed);

/// <summary>
/// Tells the engine's services when the displays have changed, once the change is over.
/// </summary>
/// <remarks>
/// Woken by <c>WM_DISPLAYCHANGE</c> and by resume, which Windows broadcasts to
/// every top-level window, so nothing polls to notice; a check every half
/// minute is only the safety net. Once woken it looks four times a second and
/// raises <see cref="Settled"/> only when the layout has held for
/// <see cref="DisplaySettle.QuietMs"/>: before, each service reacted to every
/// step of an arrival - the taskbar manager adopting Explorer's bars while
/// Explorer was still rebuilding them, the brightness written to a monitor
/// that was gone again a moment later. A loose cable that drops and returns
/// inside that window raises nothing at all.
/// <para>
/// Every service hears the same change in the same order, from one
/// enumeration, instead of each keeping a fingerprint and a timer of its own.
/// </para>
/// </remarks>
internal static class DisplayChanges
{
    private const int PollMs = 250;
    private const int SafetyNetMs = 30_000;

    /// <summary>How long after an event to keep looking, for a change that lands after the message.</summary>
    private const int WatchForMs = 5000;

    public static event Action<DisplayChange>? Settled;

    private static readonly Lock Gate = new();
    private static Timer? _timer;
    private static DisplaySettle? _settle;
    private static HashSet<string> _attached = [];
    private static long _watchUntil;
    private static bool _fast;
    private static int _busy;

    /// <summary>Starts watching, and hands back what is attached now.</summary>
    public static List<DisplayInfo> Start()
    {
        List<DisplayInfo> now = DisplayRegistry.Enumerate();
        lock (Gate)
        {
            _settle = new DisplaySettle(DisplayRegistry.CheapSignature());
            _attached = now.Select(d => d.Token).ToHashSet(StringComparer.Ordinal);
            _timer ??= new Timer(_ => Poll(), null, SafetyNetMs, SafetyNetMs);
        }
        return now;
    }

    /// <summary>Something says the displays may have changed: look closely for a while.</summary>
    public static void Raise()
    {
        lock (Gate)
        {
            if (_timer is null) return;
            _watchUntil = Environment.TickCount64 + WatchForMs;
            Pace(fast: true);
        }
    }

    private static void Pace(bool fast)
    {
        if (fast == _fast) return;
        _fast = fast;
        int ms = fast ? PollMs : SafetyNetMs;
        _timer?.Change(fast ? 0 : ms, ms);
    }

    private static void Poll()
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;
        try
        {
            string signature = DisplayRegistry.CheapSignature();
            lock (Gate)
            {
                if (_settle is null) return;
                long now = Environment.TickCount64;
                bool settled = _settle.Observe(signature, now);
                Pace(_settle.Pending || now < _watchUntil);
                if (!settled) return;
            }

            // Outside the gate: this is a CCD query and registry reads, and
            // Raise is called from a window procedure. Only this callback
            // touches the attached set, one at a time.
            List<DisplayInfo> displays = DisplayRegistry.Enumerate();
            var tokens = displays.Select(d => d.Token).ToHashSet(StringComparer.Ordinal);
            var change = new DisplayChange(
                displays,
                displays.Where(d => !_attached.Contains(d.Token)).ToList(),
                _attached.Where(t => !tokens.Contains(t)).ToList());
            _attached = tokens;

            Log.Write($"displays settled: {change.Displays.Count} attached"
                + (change.Arrived.Count > 0 ? $", arrived {string.Join(", ", change.Arrived.Select(d => d.Label))}" : "")
                + (change.Departed.Count > 0 ? $", {change.Departed.Count} left" : ""));

            foreach (Action<DisplayChange> handler in Settled?.GetInvocationList().Cast<Action<DisplayChange>>() ?? [])
            {
                try { handler(change); }
                catch (Exception ex) { Log.Write($"displays settled: a service failed to follow: {ex.Message}"); }
            }
            QuickPanelSignal.DisplaysChanged();
        }
        catch (Exception ex)
        {
            Log.Write($"displays: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
