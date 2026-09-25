using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display.Placement;
using static DispCtrl.Engine.Placement.PinNative;

namespace DispCtrl.Engine.Placement;

/// <summary>
/// Puts windows back on a display that returns, and opens new windows on the display in use.
/// </summary>
/// <remarks>
/// <b>Putting back.</b> By the time a display's departure is noticed, Windows
/// has already moved its windows, so where they were has to be known before:
/// a snapshot of every window's place, relative to its display, taken after
/// the things that move windows - a drag, a minimize, a change of window in
/// front - and every half minute besides. Snapshots stop while the display
/// layout differs from the last settled one, so the moment of change is never
/// recorded as where things were. When a display leaves, its windows are
/// parked with where Windows put them; when it returns, each one nobody has
/// moved since goes back. By the display's identity token, so the same monitor
/// on another port still counts.
/// <para>
/// <b>New windows.</b> A window-shown hook, held only while the option is on:
/// a top-level window never seen before that opens on another display than
/// the one in use is moved there, after a moment for the app to finish placing
/// it itself. Windows that existed when the option came on are never "new".
/// </para>
/// <para>
/// Both need a message pump for out-of-context WinEvent hooks, so this owns a
/// thread, asleep in <c>GetMessage</c> when neither is on and nothing is hooked.
/// </para>
/// </remarks>
internal sealed unsafe partial class PlacementService : IDisposable
{
    private const string ControlClass = "DispCtrl.PlacementControl";
    private const uint UpdateMessage = 0x8001, SettledMessage = 0x8002;
    private const nuint SnapshotTimer = 1, PeriodicTimer = 2, NewWindowTimer = 3, ReturnTimer = 4;

    /// <summary>How long after a move or a change of window the places are recorded.</summary>
    private const uint SnapshotDelayMs = 1000;

    /// <summary>A snapshot this often regardless, for moves no event reports (an app resizing itself).</summary>
    private const uint PeriodicMs = 30_000;

    /// <summary>How long a returned display's windows wait, for the display's scale and apps' DPI changes to settle.</summary>
    private const uint ReturnDelayMs = 1500;

    /// <summary>How long a new window is given to finish placing itself before it is judged.</summary>
    private const long NewWindowDelayMs = 250;

    /// <summary>How long a departed display's windows are kept waiting for it.</summary>
    private static readonly TimeSpan ParkedFor = TimeSpan.FromHours(24);

    private static PlacementService? _instance;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly ConcurrentQueue<Color.DisplayChange> _changes = new();
    private DispCtrlSettings _settings;
    private DispCtrlSettings? _pending;
    private nint _control;
    private bool _disposed;
    private readonly List<nint> _hooks = [];
    private (bool Return, bool NewWindows)? _hookState;

    /// <summary>Where every window was, the last time the layout was settled.</summary>
    private Dictionary<nint, WindowSpot> _snapshot = [];
    private string _settledSignature = "";
    private List<DisplayInfo>? _displays;
    private string _displaysSignature = "";

    private sealed record Parked(nint Window, WindowSpot Spot, DisplayRect Placed, WindowShow PlacedShow, DateTimeOffset At);

    /// <summary>Windows waiting for their display, by its identity token.</summary>
    private readonly Dictionary<string, List<Parked>> _parked = new(StringComparer.Ordinal);
    private readonly HashSet<string> _returning = new(StringComparer.Ordinal);

    private readonly HashSet<nint> _seen = [];
    private readonly List<(nint Window, long Due)> _newWindows = [];

    public PlacementService(DispCtrlSettings settings)
    {
        _settings = settings;
        _thread = new Thread(Pump) { IsBackground = true, Name = "Window placement" };
        _thread.Start();
        _ready.Wait();
        Color.DisplayChanges.Settled += OnSettled;
    }

    public void Update(DispCtrlSettings settings)
    {
        Volatile.Write(ref _pending, settings);
        if (_control != 0) PostMessage(_control, UpdateMessage, 0, 0);
    }

    private void OnSettled(Color.DisplayChange change)
    {
        _changes.Enqueue(change);
        if (_control != 0) PostMessage(_control, SettledMessage, 0, 0);
    }

    private void Pump()
    {
        try
        {
            _instance = this;
            nint module = GetModuleHandle(null);
            fixed (char* name = ControlClass)
            {
                var cls = new WindowClass { Size = (uint)sizeof(WindowClass), Proc = &WindowProc, Instance = module, Name = name };
                RegisterClassEx(ref cls);
            }
            _control = CreateWindowEx(0, ControlClass, "DispCtrl window placement", 0, 0, 0, 0, 0, -3, 0, module, 0);
            _ready.Set();
            if (_control == 0) { Log.Write("placement: no control window"); return; }
            _settledSignature = DisplayRegistry.CheapSignature();
            Configure();
            while (GetMessage(out Message message, 0, 0, 0) > 0) DispatchMessage(ref message);
        }
        catch (Exception ex) { Log.Write($"placement stopped: {ex.Message}"); }
        finally
        {
            ClearHooks();
            if (_control != 0) DestroyWindow(_control);
            _control = 0;
            _instance = null;
            _ready.Set();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint window, uint message, nuint wparam, nint lparam)
    {
        try
        {
            if (_instance is { } self && window == self._control)
            {
                switch (message)
                {
                    case 0x10: PostQuitMessage(0); return 0;
                    case UpdateMessage:
                        if (Interlocked.Exchange(ref self._pending, null) is { } settings) { self._settings = settings; self.Configure(); }
                        return 0;
                    case SettledMessage:
                        while (self._changes.TryDequeue(out Color.DisplayChange? change)) self.Settled(change);
                        return 0;
                    case 0x113: // WM_TIMER
                        self.Timer(wparam);
                        return 0;
                }
            }
        }
        catch (Exception ex) { Log.Write($"placement: {ex.Message}"); }
        return DefWindowProc(window, message, wparam, lparam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void WindowEvent(nint hook, uint evt, nint window, int obj, int child, uint thread, uint time)
    {
        try
        {
            if (_instance is not { } self || obj != 0 || child != 0) return;
            switch (evt)
            {
                case 0x8002: case 0x8018: self.Shown(window); break; // shown, uncloaked
                default: SetTimer(self._control, SnapshotTimer, SnapshotDelayMs, 0); break; // foreground, move/size end, minimize
            }
        }
        catch (Exception ex) { Log.Write($"placement: {ex.Message}"); }
    }

    private void Configure()
    {
        PlacementSettings placement = _settings.Global.Placement;

        // Two hands on one job fight: DispCtrl's putting back and Windows' own.
        if (Display.Placement.WindowsWindowMemory.Reconcile(placement))
        {
            Log.Write("placement: Windows' own window memory, switched off by an earlier build, handed back");
            _ = Task.Run(() =>
            {
                try
                {
                    DispCtrlSettings saved = SettingsStore.Load();
                    saved.Global.Placement.TookOverWindowsMemory = placement.TookOverWindowsMemory;
                    SettingsStore.Save(saved);
                }
                catch (Exception ex) { Log.Write($"placement: could not record the change: {ex.Message}"); }
            });
        }

        var state = (Return: placement.ReturnWindows, NewWindows: placement.NewWindowsOnActive);
        if (_hookState == state) return;
        ClearHooks();
        KillTimer(_control, PeriodicTimer);
        if (state.Return)
        {
            AddHook(0x0003, 0x0003); // foreground
            AddHook(0x000B, 0x000B); // move or resize ended
            AddHook(0x0016, 0x0017); // minimize start and end
            SetTimer(_control, PeriodicTimer, PeriodicMs, 0);
            Snapshot();
        }
        else
        {
            _snapshot = [];
            _parked.Clear();
        }
        if (state.NewWindows)
        {
            // Everything already there, hidden or not, is not new.
            _seen.Clear();
            GCHandle box = GCHandle.Alloc(_seen);
            try { EnumWindows(&CollectAll, GCHandle.ToIntPtr(box)); }
            finally { box.Free(); }
            // Shown and uncloaked only. Destroyed was here to keep the seen set
            // tidy, and it fires for every object in every process - menus,
            // tooltips, controls - each one a wake-up here; dead handles are
            // pruned from the set instead.
            AddHook(0x8002, 0x8002); // shown
            AddHook(0x8018, 0x8018); // uncloaked: Store apps appear this way
        }
        else
        {
            _seen.Clear();
            _newWindows.Clear();
        }
        _hookState = state;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CollectAll(nint window, nint param)
    {
        if (GCHandle.FromIntPtr(param).Target is HashSet<nint> seen) seen.Add(window);
        return 1;
    }

    private void AddHook(uint min, uint max)
    {
        nint hook = SetWinEventHook(min, max, 0, &WindowEvent, 0, 0, 2); // out of context, skip own process
        if (hook != 0) _hooks.Add(hook);
        else Log.Write($"placement: could not watch window events 0x{min:X}-0x{max:X}");
    }

    private void ClearHooks()
    {
        foreach (nint hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear();
        _hookState = null;
    }

    private void Timer(nuint id)
    {
        switch (id)
        {
            case SnapshotTimer: KillTimer(_control, SnapshotTimer); Snapshot(); break;
            case PeriodicTimer: Snapshot(); break;
            case NewWindowTimer: PlaceNewWindows(); break;
            case ReturnTimer: KillTimer(_control, ReturnTimer); ReturnDue(); break;
        }
    }

    // ------------------------------------------------------------ putting back --

    /// <summary>Records where every window is, while the layout is the settled one.</summary>
    private void Snapshot()
    {
        if (!_settings.Global.Placement.ReturnWindows) return;
        // Mid-change: what the windows look like now is Windows moving them,
        // not where they belong.
        string signature = DisplayRegistry.CheapSignature();
        if (signature != _settledSignature) return;

        // The layout is the settled one, so the displays are the ones read
        // then: a full enumeration every 30 s, for a desk that has not changed,
        // was most of what this cost at idle. The signature does not cover work
        // areas - a taskbar hidden or shown moves one - and a spot is measured
        // from its work area, so those are read afresh, one cheap call each.
        if (_displays is null || _displaysSignature != signature)
        {
            _displays = DisplayRegistry.Enumerate();
            _displaysSignature = signature;
        }
        var displays = new List<DisplayInfo>(_displays.Count);
        foreach (DisplayInfo d in _displays)
        {
            var info = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
            displays.Add(GetMonitorInfo(d.Handle, ref info) != 0
                ? d with { WorkArea = new DisplayRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom) }
                : d);
        }
        var next = new Dictionary<nint, WindowSpot>();
        foreach (AppWindow window in AppWindows.List())
        {
            DisplayInfo? on = AppWindows.DisplayOf(window.Handle, displays);
            if (on is null) continue;
            DisplayRect rect = window.Show == WindowShow.Normal ? window.Frame : AppWindows.RestoreRect(window.Handle, displays);
            next[window.Handle] = WindowGeometry.Spot(on.Token, rect, on.WorkArea, on.Dpi, window.Show);
        }
        _snapshot = next;
    }

    private void Settled(Color.DisplayChange change)
    {
        PlacementSettings placement = _settings.Global.Placement;
        if (!placement.ReturnWindows)
        {
            _settledSignature = DisplayRegistry.CheapSignature();
            return;
        }

        foreach (string token in _parked.Where(p => p.Value.All(w => DateTimeOffset.UtcNow - w.At > ParkedFor)).Select(p => p.Key).ToArray())
            _parked.Remove(token);

        // Gone: its windows are wherever Windows put them now, which is what
        // "nobody has moved it since" is measured against.
        foreach (string token in change.Departed)
        {
            var waiting = new List<Parked>();
            var names = new List<string>();
            foreach ((nint window, WindowSpot spot) in _snapshot)
            {
                if (spot.Token != token || IsWindow(window) == 0) continue;
                if (AppWindows.Describe(window) is not { } now) continue;
                names.Add($"{now.Process} ({spot.Show})");
                DisplayRect placed = now.Show == WindowShow.Normal ? now.Frame : AppWindows.RestoreRect(window, change.Displays);
                waiting.Add(new Parked(window, spot, placed, now.Show, DateTimeOffset.UtcNow));
            }
            if (waiting.Count > 0)
            {
                _parked[token] = waiting;
                Log.Write($"placement: {waiting.Count} window(s) will go back when their display returns"
                    + (DispCtrl.Core.BuildInfo.Diagnostics ? ": " + string.Join(", ", names) : ""));
            }
        }

        _settledSignature = DisplayRegistry.CheapSignature();
        bool any = false;
        foreach (DisplayInfo arrived in change.Arrived)
            if (_parked.ContainsKey(arrived.Token)) { _returning.Add(arrived.Token); any = true; }

        if (any) SetTimer(_control, ReturnTimer, ReturnDelayMs, 0);
        else Snapshot();
    }

    private void ReturnDue()
    {
        List<DisplayInfo> displays = DisplayRegistry.Enumerate();
        nint foreground = GetForegroundWindow();
        HashSet<string> excluded = _settings.Global.Placement.Exclusions();
        foreach (string token in _returning.ToArray())
        {
            _returning.Remove(token);
            if (!_parked.Remove(token, out List<Parked>? waiting)) continue;
            DisplayInfo? display = displays.FirstOrDefault(d => d.Token == token);
            if (display is null) continue;

            int moved = 0, kept = 0;
            foreach (Parked parked in waiting)
            {
                if (AppWindows.Describe(parked.Window) is not { } now) continue;
                if (now.Process.Length > 0 && excluded.Contains(now.Process)) continue;
                DisplayRect current = now.Show == WindowShow.Normal ? now.Frame : AppWindows.RestoreRect(parked.Window, displays);
                // Moved, resized, restored or minimized since: somebody's choice, left alone.
                DisplayRect back = WindowGeometry.Place(parked.Spot, display.WorkArea, display.Dpi);
                if (!WindowGeometry.Untouched(parked.Placed, parked.PlacedShow, current, now.Show))
                {
                    kept++;
                    // Already home says something else moved it back - Windows' own
                    // memory, or the app - rather than a person moving it away.
                    if (DispCtrl.Core.BuildInfo.Diagnostics)
                        Log.Write($"placement: {now.Process} ({now.Show}) moved since it was parked; "
                            + (WindowGeometry.Untouched(back, parked.Spot.Show, current, now.Show) ? "already back where it was" : "left where it is"));
                    continue;
                }
                bool placed = WindowMover.Place(parked.Window, back, parked.Spot.Show, display);
                if (placed) moved++;
                if (DispCtrl.Core.BuildInfo.Diagnostics) Log.Write($"placement: {now.Process} ({now.Show}) " + (placed ? "put back" : "could not be moved"));
            }
            Log.Write($"placement: {display.Label} is back; {moved} window(s) put back" + (kept > 0 ? $", {kept} left where they were moved to" : ""));
        }
        if (foreground != 0 && IsWindow(foreground) != 0 && GetForegroundWindow() != foreground) SetForegroundWindowSafe(foreground);
        Snapshot();
    }

    // ------------------------------------------------------------ new windows --

    private void Shown(nint window)
    {
        if (!_settings.Global.Placement.NewWindowsOnActive) return;
        // Controls inside windows are shown too, by the hundred in a browser;
        // only a top-level window can be a new one.
        if ((GetWindowLongPtr(window, -16) & 0x40000000) != 0 || !_seen.Add(window)) return; // WS_CHILD
        // Handles of windows that have gone, now and then: Windows reuses them,
        // and a reused one must count as new again.
        if (_seen.Count > 4096) _seen.RemoveWhere(h => IsWindow(h) == 0);
        _newWindows.Add((window, Environment.TickCount64 + NewWindowDelayMs));
        SetTimer(_control, NewWindowTimer, (uint)NewWindowDelayMs, 0);
    }

    private void PlaceNewWindows()
    {
        KillTimer(_control, NewWindowTimer);
        long now = Environment.TickCount64;
        var due = _newWindows.Where(w => w.Due <= now).Select(w => w.Window).ToList();
        _newWindows.RemoveAll(w => w.Due <= now);
        if (_newWindows.Count > 0) SetTimer(_control, NewWindowTimer, (uint)Math.Max(16, _newWindows.Min(w => w.Due) - now), 0);
        if (due.Count == 0) return;

        PlacementSettings placement = _settings.Global.Placement;
        List<DisplayInfo> displays = DisplayRegistry.Enumerate();
        if (displays.Count < 2) return;
        DisplayInfo? target = WindowMover.Active(placement, displays);
        if (target is null) return;
        HashSet<string> excluded = placement.Exclusions();
        foreach (nint window in due)
        {
            // Not dialogs, tool windows or anything Alt+Tab would not list, and
            // not a game taking a whole display.
            if (AppWindows.Describe(window) is not { Show: WindowShow.Normal or WindowShow.Maximized } app) continue;
            if (app.Process.Length > 0 && excluded.Contains(app.Process)) continue;
            if (AppWindows.IsFullscreen(window, app.Show, displays)) continue;
            DisplayInfo? on = AppWindows.DisplayOf(window, displays);
            if (on is null || on.Key == target.Key) continue;
            if (WindowMover.Move(window, on, target, placement.KeepSize))
                Log.Write($"placement: {app.Process} opened on {on.Label}, moved to {target.Label}");
        }
    }

    private static void SetForegroundWindowSafe(nint window)
    {
        try { _ = SetForegroundWindow(window); } catch (Exception) { }
    }

    [LibraryImport("user32.dll")] private static partial int SetForegroundWindow(nint hwnd);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Color.DisplayChanges.Settled -= OnSettled;
        if (_control != 0) PostMessage(_control, 0x10, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
