using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace DispCtrl.Engine.Shell;

/// <summary>Lock, unlock and resume, as the focus thread's window hears them, for anything else that cares.</summary>
/// <remarks>One registration for session notifications in the engine, not one per service.</remarks>
internal static class SessionEvents
{
    public static event Action<TriggerEvent>? Raised;
    public static void Raise(TriggerEvent what) => Raised?.Invoke(what);
}

/// <summary>
/// Runs the custom feature a trigger names when its event happens.
/// </summary>
/// <remarks>
/// Displays arriving and leaving come from the settled display change, lock,
/// unlock and resume from <see cref="SessionEvents"/>; those cost nothing
/// between events. What has no event of its own - the app in front, being
/// away, mains power, a time of day - is looked at by one thread, only as often
/// as the triggers in use need: a second for the app in front and for being
/// away, five for power, the next minute for a time, and never when there is
/// nothing to look at, when it waits for the settings to change.
/// <para>
/// Each fires on the change: the first look records how things are and runs
/// nothing, so starting the engine at a desk with an app in front, or on
/// battery, is not taken as the app coming forward or the plug being pulled.
/// Features run off this thread, one at a time each: a feature still running
/// when its trigger fires again is not started twice.
/// </para>
/// </remarks>
internal sealed partial class TriggerService : IDisposable
{
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly PersonIdle _person = new();
    private readonly Dictionary<string, bool> _away = new(StringComparer.Ordinal);
    private volatile DispCtrlSettings _settings;
    private volatile bool _disposed;
    private string? _front;
    private bool _frontKnown;
    private bool? _battery;
    private DateTime _lastLook = DateTime.Now;
    private long _lastResume;

    public TriggerService(DispCtrlSettings settings)
    {
        _settings = settings;
        Color.DisplayChanges.Settled += OnSettled;
        SessionEvents.Raised += OnSession;
        _thread = new Thread(Run) { IsBackground = true, Name = "Triggers" };
        _thread.Start();
    }

    public void Update(DispCtrlSettings settings)
    {
        _settings = settings;
        _wake.Set();
    }

    private List<Trigger> Ready(DispCtrlSettings s) =>
        s.Triggers.Where(t => t.Enabled && t.Problem(s.Features) is null).ToList();

    private void OnSettled(Color.DisplayChange change)
    {
        DispCtrlSettings s = _settings;
        List<Trigger> ready = Ready(s);
        if (ready.Count == 0) return;
        foreach (DisplayInfo d in change.Arrived)
        {
            int number = change.Displays.FindIndex(x => x.Key == d.Key) + 1;
            foreach (Trigger t in ready.Where(t => t.Event == TriggerEvent.DisplayConnected && t.MatchesDisplay(d.Token, d.Label, number)))
                Run(t, $"{d.Label} connected");
        }
        foreach (string token in change.Departed)
        {
            string label = s.Monitors.TryGetValue(token, out MonitorSettings? m) ? m.Label ?? token : token;
            foreach (Trigger t in ready.Where(t => t.Event == TriggerEvent.DisplayDisconnected && t.MatchesDisplay(token, label, 0)))
                Run(t, $"{label} disconnected");
        }
    }

    private void OnSession(TriggerEvent what)
    {
        // Windows announces a resume twice (automatic, then by a person).
        if (what == TriggerEvent.Resumed)
        {
            long now = Environment.TickCount64;
            if (now - Interlocked.Exchange(ref _lastResume, now) < 10_000) return;
        }
        foreach (Trigger t in Ready(_settings).Where(t => t.Event == what)) Run(t, what.ToString().ToLowerInvariant());
    }

    private void Run()
    {
        while (!_disposed)
        {
            int wait = Timeout.Infinite;
            try { wait = Look(); }
            catch (Exception ex) { Log.Write($"triggers: {ex.Message}"); wait = 5000; }
            _wake.WaitOne(wait);
        }
    }

    /// <summary>One look at what has no event of its own; returns how long until the next is needed.</summary>
    private int Look()
    {
        List<Trigger> ready = Ready(_settings);
        bool front = ready.Any(t => t.Event is TriggerEvent.AppInFront or TriggerEvent.AppLeft);
        bool away = ready.Any(t => t.Event is TriggerEvent.Idle or TriggerEvent.Back);
        bool power = ready.Any(t => t.Event is TriggerEvent.OnBattery or TriggerEvent.OnPower);
        bool times = ready.Any(t => t.Event == TriggerEvent.AtTime);

        if (front)
        {
            string? image = ForegroundImage();
            if (_frontKnown && !string.Equals(image, _front, StringComparison.OrdinalIgnoreCase))
            {
                foreach (Trigger t in ready.Where(t => t.Event == TriggerEvent.AppLeft && t.MatchesApp(_front))) Run(t, $"{_front} left the front");
                foreach (Trigger t in ready.Where(t => t.Event == TriggerEvent.AppInFront && t.MatchesApp(image))) Run(t, $"{image} came to the front");
            }
            _front = image;
            _frontKnown = true;
        }
        else _frontKnown = false;

        if (away)
        {
            uint idle = _person.Update(Environment.TickCount64, IdleMs(), Power.PowerService.LastNudgeTick);
            foreach (Trigger t in ready.Where(t => t.Event is TriggerEvent.Idle or TriggerEvent.Back))
            {
                string key = $"{t.Minutes}";
                bool now = idle >= t.Minutes * 60_000L;
                bool known = _away.TryGetValue(key, out bool was);
                if (known && now != was && (t.Event == TriggerEvent.Idle) == now) Run(t, now ? $"away {t.Minutes} min" : "back");
            }
            foreach (int minutes in ready.Where(t => t.Event is TriggerEvent.Idle or TriggerEvent.Back).Select(t => t.Minutes).Distinct())
                _away[$"{minutes}"] = idle >= minutes * 60_000L;
        }
        else _away.Clear();

        if (power)
        {
            bool? battery = OnBattery();
            if (_battery is { } before && battery is { } after && before != after)
                foreach (Trigger t in ready.Where(t => t.Event == (after ? TriggerEvent.OnBattery : TriggerEvent.OnPower)))
                    Run(t, after ? "on battery" : "plugged in");
            _battery = battery;
        }
        else _battery = null;

        DateTime looked = DateTime.Now;
        if (times)
            foreach (Trigger t in ready.Where(t => t.Event == TriggerEvent.AtTime && t.TimeBetween(_lastLook, looked)))
                Run(t, $"it is {t.Match}");
        _lastLook = looked;

        if (front || away) return 1000;
        if (power) return 5000;
        if (times) return (int)Math.Clamp((looked.Date.AddHours(looked.Hour).AddMinutes(looked.Minute + 1) - looked).TotalMilliseconds + 50, 1000, 60_050);
        return Timeout.Infinite;
    }

    /// <summary>Runs a trigger's feature off the caller's thread, unless that feature is still running.</summary>
    private void Run(Trigger trigger, string why)
    {
        string name = trigger.Feature.Trim();
        lock (_running) if (!_running.Add(name)) { Log.Write($"trigger: {why}; “{name}” is still running"); return; }
        Log.Write($"trigger: {why}; running “{name}”");
        _ = Task.Run(() =>
        {
            try
            {
                JsonObject result = new DispCtrl.Control.ControlService().Execute(new JsonObject
                {
                    ["version"] = 1, ["command"] = "features.run", ["args"] = new JsonObject { ["name"] = name },
                });
                if (result["ok"]?.GetValue<bool>() != true || result["exitCode"]?.GetValue<int>() != 0)
                    Log.Write($"trigger: “{name}” did not complete: {result["error"]?["message"] ?? result["data"]?.ToJsonString()}");
            }
            catch (Exception ex) { Log.Write($"trigger: “{name}” failed: {ex.Message}"); }
            finally { lock (_running) _running.Remove(name); }
        });
    }

    private static unsafe string? ForegroundImage()
    {
        HWND hwnd = PInvoke.GetForegroundWindow();
        if (hwnd.IsNull) return null;
        uint pid = 0;
        _ = PInvoke.GetWindowThreadProcessId(hwnd, &pid);
        return pid == 0 ? null : Display.Placement.AppWindows.ProcessName(pid) is { Length: > 0 } name ? name : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput { public uint Size; public uint Tick; }

    [LibraryImport("user32.dll")]
    private static partial int GetLastInputInfo(ref LastInput input);

    private static uint IdleMs()
    {
        var input = new LastInput { Size = 8 };
        return GetLastInputInfo(ref input) == 0 ? 0 : unchecked((uint)Environment.TickCount - input.Tick);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus { public byte AcLine, Battery, Percent, Saver; public int LifeTime, FullLifeTime; }

    [LibraryImport("kernel32.dll")]
    private static partial int GetSystemPowerStatus(out PowerStatus status);

    /// <summary>True on battery, false on mains, null when Windows does not know (a desktop says 255).</summary>
    private static bool? OnBattery() =>
        GetSystemPowerStatus(out PowerStatus status) != 0 && status.AcLine is 0 or 1 ? status.AcLine == 0 : null;

    public void Dispose()
    {
        _disposed = true;
        Color.DisplayChanges.Settled -= OnSettled;
        SessionEvents.Raised -= OnSession;
        _wake.Set();
        _thread.Join(2000);
    }
}
