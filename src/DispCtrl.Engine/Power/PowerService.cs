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

        if (wanted == _executionState) return;
        SetThreadExecutionState(wanted);
        _executionState = wanted;
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
