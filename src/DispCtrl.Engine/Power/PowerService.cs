using System.Runtime.InteropServices;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

namespace DispCtrl.Engine.Power;

/// <summary>
/// Holds Windows awake requests and powers supported external monitors down
/// after inactivity. One sleeping worker handles both; at rest it wakes once a
/// second and performs no DDC traffic unless a monitor changes state.
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
    }

    public void Update(DispCtrlSettings settings)
    {
        Volatile.Write(ref _pending, settings);
        _wake.Set();
    }

    private void Run()
    {
        try
        {
            RefreshDisplays();
            while (!_disposed)
            {
                if (Interlocked.Exchange(ref _pending, null) is { } settings)
                {
                    _settings = settings;
                    RefreshDisplays();
                }
                else if (DateTimeOffset.UtcNow >= _nextDisplayRefresh) RefreshDisplays();
                ApplyAwake();
                StayActive();
                ApplyMonitorSleep();
                _wake.WaitOne(1000);
            }
        }
        catch (Exception ex) { Log.Write($"power service stopped: {ex.Message}"); }
        finally
        {
            WakeAll();
            SetThreadExecutionState(Continuous);
        }
    }

    private void ApplyAwake()
    {
        AwakeSettings awake = _settings.Global.Awake;
        uint wanted = Continuous;
        if (awake.ActiveAt(DateTimeOffset.UtcNow))
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
    /// everything that waits for inactivity - OLED idle dimming, monitor sleep -
    /// waits while this is on; that is what staying active means.
    /// </remarks>
    private unsafe void StayActive()
    {
        bool wanted = _settings.Global.Awake.StayActive;
        if (wanted != _stayActive)
        {
            _stayActive = wanted;
            Log.Write(wanted ? "stay active: on" : "stay active: off");
        }
        if (!wanted) return;

        var last = new LastInput { Size = 8 };
        if (GetLastInputInfo(ref last) == 0) return;
        uint idleMs = unchecked((uint)Environment.TickCount - last.Tick);
        if (idleMs < NudgeAfterIdleMs) return;

        Input* moves = stackalloc Input[2];
        moves[0] = new Input { Type = InputMouse, Mouse = new MouseInput { Dx = 1, Flags = MouseMove } };
        moves[1] = new Input { Type = InputMouse, Mouse = new MouseInput { Dx = -1, Flags = MouseMove } };
        if (SendInput(2, moves, sizeof(Input)) != 2)
            Log.Write($"stay active: the nudge was refused ({Marshal.GetLastPInvokeError()}); a secure desktop or an elevated window may have the input");
    }

    private void ApplyMonitorSleep()
    {
        var input = new LastInput { Size = 8 };
        if (GetLastInputInfo(ref input) == 0) return;
        uint idleMs = unchecked((uint)Environment.TickCount - input.Tick);
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
            if (_retryAfter.TryGetValue(token, out DateTimeOffset retry) && retry > DateTimeOffset.UtcNow) continue;

            if (MonitorCapabilities.Write(display!, 0xD6, 0x04)) _sleeping.Add(token);
            else _retryAfter[token] = DateTimeOffset.UtcNow.AddSeconds(30);
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
        _wake.Set();
        _thread.Join();
        _wake.Dispose();
    }
}
