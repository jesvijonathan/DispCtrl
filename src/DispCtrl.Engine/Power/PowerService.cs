using System.Runtime.InteropServices;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

namespace DispCtrl.Engine.Power;

/// <summary>
/// Holds Windows awake requests and powers supported external monitors down
/// after inactivity. One sleeping worker handles both; it wakes only when one of
/// them next needs it, and performs no DDC traffic unless a monitor changes state.
/// </summary>
internal sealed partial class PowerService : IDisposable
{
    private const uint Continuous = 0x80000000;
    private const uint SystemRequired = 0x00000001;
    private const uint DisplayRequired = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput { public uint Size, Tick; }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint SetThreadExecutionState(uint flags);

    [LibraryImport("user32.dll")]
    private static partial int GetLastInputInfo(ref LastInput input);

    // SendInput's INPUT with the mouse member of its union, the largest, so the
    // size matches Windows' own (40 bytes on x64).
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int Dx, Dy; public uint MouseData, Flags, Time; public nint ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public MouseInput Mouse; }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial uint SendInput(uint count, Input* inputs, int size);

    private const uint InputMouse = 0, MouseMove = 0x0001;

    /// <summary>How long nothing must have been touched before Stay active nudges the pointer.</summary>
    private const int NudgeAfterIdleMs = 55_000;

    /// <summary>When Stay active last nudged the pointer, so input it made itself is not taken for a person.</summary>
    /// <remarks>
    /// Turned-off displays wake on any input, and the nudge is input: with Stay
    /// active on they came back on by themselves after a minute.
    /// </remarks>
    public static long LastNudgeTick => Volatile.Read(ref _lastNudgeTick);
    private static long _lastNudgeTick;

    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private readonly HashSet<string> _sleeping = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _retryAfter = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, DisplayInfo> _displays = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _nextDisplayRefresh;
    private volatile bool _disposed;
    private DispCtrlSettings _settings;
    private DispCtrlSettings? _pending;
    private uint _executionState = Continuous;
    private bool _stayActive;

    public PowerService(DispCtrlSettings settings)
    {
        _settings = settings;
        _thread = new Thread(Run) { IsBackground = true, Name = "Display power" };
        _thread.Start();
        Color.DisplayChanges.Settled += OnDisplaysSettled;
    }

    /// <summary>A monitor reconnected is a new handle: the old one reaches nothing.</summary>
    private void OnDisplaysSettled(Color.DisplayChange change)
    {
        Volatile.Write(ref _displaysChanged, true);
        _wake.Set();
    }

    private bool _displaysChanged;

    public void Update(DispCtrlSettings settings)
    {
        Volatile.Write(ref _pending, settings);
        _wake.Set();
    }

    private void Run()
    {
        try
        {
            while (!_disposed)
            {
                if (Interlocked.Exchange(ref _pending, null) is { } settings)
                {
                    _settings = settings;
                    _nextDisplayRefresh = default;
                }
                if (Volatile.Read(ref _displaysChanged))
                {
                    Volatile.Write(ref _displaysChanged, false);
                    _nextDisplayRefresh = default;
                }

                DateTimeOffset now = DateTimeOffset.UtcNow;
                uint idleMs = IdleMs();
                bool sleepWanted = _sleeping.Count > 0 || AnyMonitorSleep();
                // The display list is a CCD query plus registry reads, and only
                // monitor sleep needs it. Most desks never switch that on.
                if (sleepWanted && now >= _nextDisplayRefresh) RefreshDisplays();

                ApplyAwake(now);
                StayActive(idleMs);
                if (sleepWanted) ApplyMonitorSleep(idleMs, now);

                _wake.WaitOne(NextWait(now, idleMs, sleepWanted));
            }
        }
        catch (Exception ex) { Log.Write($"power service stopped: {ex.Message}"); }
        finally
        {
            WakeAll();
            SetThreadExecutionState(Continuous);
        }
    }

    /// <summary>
    /// How long nothing here can change, so the thread can sleep that long.
    /// </summary>
    /// <remarks>
    /// This loop used to wake every second whatever was switched on: 86,400
    /// wakes a day to confirm that nothing was. Each duty now says when it next
    /// needs looking at. Keep awake needs its end time, Stay active the moment
    /// the idle time reaches the nudge, and monitor sleep its threshold, or a
    /// second while a monitor is asleep, so that a touch wakes it quickly. With
    /// none of them on it waits for a settings change, which sets the event.
    /// Recomputed on every wake, so input that resets the idle time only means
    /// a wake finds nothing to do and sleeps again.
    /// </remarks>
    private int NextWait(DateTimeOffset now, uint idleMs, bool sleepWanted)
    {
        const int Soonest = 1000, Latest = 3_600_000;
        long wait = Latest;
        AwakeSettings awake = _settings.Global.Awake;

        DateTimeOffset? ends = awake.Mode switch
        {
            AwakeMode.Timed => awake.TimedUntilUtc,
            AwakeMode.Expiration => awake.ExpirationUtc,
            _ => null,
        };
        if (ends is { } end && end > now) wait = Math.Min(wait, (long)(end - now).TotalMilliseconds + 50);

        if (awake.StayActive)
            wait = Math.Min(wait, NudgeAfterIdleMs - (long)Math.Min(idleMs, NudgeAfterIdleMs));

        if (sleepWanted)
        {
            if (_sleeping.Count > 0) wait = Soonest;
            foreach (MonitorSettings monitor in _settings.Monitors.Values)
                if (monitor.MonitorSleepEnabled)
                    wait = Math.Min(wait, Math.Clamp(monitor.MonitorSleepMinutes, 1, 240) * 60_000L - idleMs);
            if (_retryAfter.Count > 0) wait = Math.Min(wait, 30_000);
            // Enumerated displays go stale on a hotplug; this is how often they are re-read.
            wait = Math.Min(wait, 30_000);
        }

        return (int)Math.Clamp(wait, Soonest, Latest);
    }

    private bool AnyMonitorSleep()
    {
        foreach (MonitorSettings monitor in _settings.Monitors.Values)
            if (monitor.MonitorSleepEnabled) return true;
        return false;
    }

    private static uint IdleMs()
    {
        var input = new LastInput { Size = 8 };
        return GetLastInputInfo(ref input) == 0 ? 0 : unchecked((uint)Environment.TickCount - input.Tick);
    }

    private void ApplyAwake(DateTimeOffset now)
    {
        AwakeSettings awake = _settings.Global.Awake;
        uint wanted = Continuous;
        if (awake.ActiveAt(now))
        {
            wanted |= SystemRequired;
            if (awake.KeepDisplaysOn) wanted |= DisplayRequired;
        }
        // Stay active holds the screen on too: an attended-looking session
        // behind a blank display is no use to anyone.
        if (awake.StayActive) wanted |= SystemRequired | DisplayRequired;

        if (wanted == _executionState) return;
        SetThreadExecutionState(wanted);
        _executionState = wanted;
    }

    /// <summary>
    /// Nudges the pointer one pixel and back once nothing has been touched for
    /// about a minute, so the lock screen, the screen saver and chat apps' Away
    /// status do not come while Stay active is on.
    /// </summary>
    /// <remarks>
    /// Only when idle: a person using the mouse needs no help, and a nudge under
    /// their hand would be felt. There and back in one call, so the pointer ends
    /// where it was and no program sees it move. The nudge is real input, so
    /// Windows' own idle timers and monitor sleep wait while this is on. OLED
    /// idle care does not (<c>PersonIdle</c>): it once did, and never came at all.
    /// </remarks>
    private unsafe void StayActive(uint idleMs)
    {
        AwakeSettings awake = _settings.Global.Awake;
        bool wanted = awake.StayActive;
        if (wanted != _stayActive)
        {
            _stayActive = wanted;
            Log.Write(wanted ? "stay active: on" : "stay active: off");
        }
        if (!wanted) return;

        if (idleMs < NudgeAfterIdleMs) return;

        Input* moves = stackalloc Input[2];
        moves[0] = new Input { Type = InputMouse, Mouse = new MouseInput { Dx = 1, Flags = MouseMove } };
        moves[1] = new Input { Type = InputMouse, Mouse = new MouseInput { Dx = -1, Flags = MouseMove } };
        if (SendInput(2, moves, sizeof(Input)) == 2) Volatile.Write(ref _lastNudgeTick, Environment.TickCount64);
        else
            Log.Write($"stay active: the nudge was refused ({Marshal.GetLastPInvokeError()}); a secure desktop or an elevated window may have the input");
    }

    private void ApplyMonitorSleep(uint idleMs, DateTimeOffset now)
    {
        bool recentInput = idleMs < 1500;
        foreach ((string token, MonitorSettings monitor) in _settings.Monitors)
        {
            _displays.TryGetValue(token, out DisplayInfo? display);
            bool enabled = monitor.MonitorSleepEnabled && display is not null && !display.IsInternal;
            bool asleep = _sleeping.Contains(token);

            if (!enabled || (asleep && recentInput))
            {
                if (asleep && _displays.TryGetValue(token, out DisplayInfo? wakeDisplay)
                    && MonitorCapabilities.Write(wakeDisplay, 0xD6, 0x01))
                    _sleeping.Remove(token);
                continue;
            }

            if (asleep || idleMs < Math.Clamp(monitor.MonitorSleepMinutes, 1, 240) * 60_000L) continue;
            if (_retryAfter.TryGetValue(token, out DateTimeOffset retry) && retry > now) continue;

            if (MonitorCapabilities.Write(display!, 0xD6, 0x04)) _sleeping.Add(token);
            else _retryAfter[token] = now.AddSeconds(30);
        }

        foreach (string token in _sleeping.ToArray())
        {
            if (_settings.Monitors.TryGetValue(token, out MonitorSettings? monitor) && monitor.MonitorSleepEnabled) continue;
            if (_displays.TryGetValue(token, out DisplayInfo? display)) MonitorCapabilities.Write(display, 0xD6, 0x01);
            _sleeping.Remove(token);
        }
    }

    private void WakeAll()
    {
        foreach (string token in _sleeping)
            if (_displays.TryGetValue(token, out DisplayInfo? display)) MonitorCapabilities.Write(display, 0xD6, 0x01);
        _sleeping.Clear();
    }

    private void RefreshDisplays()
    {
        _displays = DisplayRegistry.Enumerate()
            .ToDictionary(display => display.Token, StringComparer.OrdinalIgnoreCase);
        _nextDisplayRefresh = DateTimeOffset.UtcNow.AddSeconds(30);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Color.DisplayChanges.Settled -= OnDisplaysSettled;
        _wake.Set();
        _thread.Join();
        _wake.Dispose();
    }
}
