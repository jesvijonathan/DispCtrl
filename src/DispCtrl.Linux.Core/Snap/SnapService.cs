using System.Collections.Concurrent;
using DispCtrl.Linux.Graphics;
using DispCtrl.Linux.Settings;
using DispCtrl.Linux.X11;

namespace DispCtrl.Linux.Snap;

/// <summary>Snap layouts and Snap Assist, as on Windows 11, for X11 desktops:
/// drag a window to the top centre of a display and drop it on a zone, or press
/// the shortcut to choose one for the window in front; then pick windows for
/// the zones left.</summary>
/// <remarks>
/// <para>Runs on a thread of its own with its own X connection: everything X is
/// done there, and other threads hand it work through <see cref="Post"/>.</para>
/// <para>Drags are seen through XInput2 raw events, which reach a client even
/// while the window manager holds the pointer for a move. Motion is subscribed
/// to only while button 1 is down, so an idle desk wakes this thread only for a
/// click - the engine's cost is wake-ups, not work.</para>
/// <para>A drag is a window whose position changed between two samples while
/// its size did not: dragging a maximized window resizes it once (the manager
/// restores it) and then moves it, and resizing from a left or top edge changes
/// position and size together.</para>
/// </remarks>
public sealed unsafe class SnapService : IDisposable
{
    private readonly Action<string> _log;

    // DISPCTRL_SNAP_TRACE=1: every step of a drag in the log, for a desktop
    // where snapping does not happen and nothing says why.
    private static readonly bool Tracing = Environment.GetEnvironmentVariable("DISPCTRL_SNAP_TRACE") == "1";
    private void Trace(string message) { if (Tracing) _log($"snap trace: {message}"); }
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly Thread _thread;
    private readonly int[] _wake = new int[2];
    private volatile bool _stop;

    private XConnection? _x;
    private int _xiOpcode = -1;
    private SnapSettings _settings;
    private Shortcut? _grabbed;
    private bool _gnomeShortcut;
    private const string GnomeId = "dispctrl-snap";
    private int _grabbedKeycode;
    private Overlay? _overlay;
    private DesktopTheme _theme = DesktopTheme.Fallback;

    // Drag tracking.
    private bool _buttonDown;
    private bool _motionSubscribed;
    private ulong _dragCandidate;
    private Rect? _lastFrame;
    private bool _dragging;
    private long _nextSample;
    private IReadOnlyList<MonitorInfo> _monitors = [];
    private (string Monitor, bool Handle, bool Open, ZoneRef? Hover)? _painted;
    private SnapPanel? _panel;
    private bool _open;
    private ZoneRef? _hover;

    public SnapService(SnapSettings settings, Action<string> log)
    {
        _settings = settings;
        _log = log;
        fixed (int* p = _wake)
        {
            if (Xlib.pipe(p) != 0) throw new IOException("pipe() failed");
        }
        Xlib.fcntl(_wake[0], Xlib.F_SETFL, Xlib.O_NONBLOCK);
        Xlib.fcntl(_wake[1], Xlib.F_SETFL, Xlib.O_NONBLOCK);
        _thread = new Thread(Run) { Name = "snap", IsBackground = true };
    }

    /// <summary>Why the shortcut is not held, when it is not: another program
    /// has it, or it does not parse.</summary>
    public string? ShortcutProblem { get; private set; }

    public bool Running { get; private set; }

    public void Start() => _thread.Start();

    /// <summary>Runs <paramref name="action"/> on the snap thread.</summary>
    public void Post(Action action)
    {
        _work.Enqueue(action);
        byte b = 1;
        Xlib.write(_wake[1], &b, 1);
    }

    public void Reload(SnapSettings settings) => Post(() =>
    {
        _settings = settings;
        GrabShortcut();
    });

    /// <summary>The shortcut's picker, for the window in front: the same thing
    /// the shortcut does, for <c>dispctrl-linux snap pick</c>.</summary>
    public void Pick() => Post(() => OpenPicker());

    private void Run()
    {
        _x = XConnection.Open();
        if (_x is null)
        {
            _log("snap: no X display; snap layouts are off");
            return;
        }
        try
        {
            if (!SelectRawInput(motion: false)) _log("snap: XInput2 is missing; dragging to the top will not open the layouts");
            GrabShortcut();
            Running = true;
            _log($"snap: ready ({(_settings.DragToTop ? "drag to the top" : "no drag")}, shortcut {(_grabbed?.Text ?? "none")})");
            Loop();
        }
        catch (Exception ex)
        {
            _log($"snap: stopped after an error: {ex}");
        }
        finally
        {
            Running = false;
            // Stopping takes the GNOME shortcut away too: without the engine it
            // would only say the engine is not running.
            UngrabShortcut(removeGnome: true);
            _overlay?.Dispose();
            _x.Dispose();
        }
    }

    private void Loop()
    {
        var fds = stackalloc Xlib.PollFd[2];
        fds[0] = new Xlib.PollFd { fd = Xlib.XConnectionNumber(_x!.Display), events = Xlib.POLLIN };
        fds[1] = new Xlib.PollFd { fd = _wake[0], events = Xlib.POLLIN };
        byte* ev = stackalloc byte[Xlib.EventSize];
        byte* drain = stackalloc byte[64];

        while (!_stop)
        {
            while (Xlib.XPending(_x.Display) > 0)
            {
                Xlib.XNextEvent(_x.Display, ev);
                Handle(ev);
            }
            while (_work.TryDequeue(out var action)) action();
            if (_stop) break;

            // While a button is down the frame is sampled even without motion
            // (a window can be moved by keyboard during a drag); otherwise sleep
            // until X or another thread has something.
            int timeout = _buttonDown ? 30 : -1;
            fds[0].revents = fds[1].revents = 0;
            Xlib.poll(fds, 2, timeout);
            if ((fds[1].revents & Xlib.POLLIN) != 0) while (Xlib.read(_wake[0], drain, 64) > 0) { }
            if (_buttonDown) SampleDrag();
        }
    }

    // ---- input

    private bool SelectRawInput(bool motion)
    {
        if (_xiOpcode < 0)
        {
            if (Xlib.XQueryExtension(_x!.Display, "XInputExtension", out int opcode, out _, out _) == 0) return false;
            int major = 2, minor = 2;
            if (Xlib.XIQueryVersion(_x.Display, ref major, ref minor) != 0) return false;
            _xiOpcode = opcode;
        }
        byte* mask = stackalloc byte[4];
        new Span<byte>(mask, 4).Clear();
        void Set(int bit) => mask[bit >> 3] |= (byte)(1 << (bit & 7));
        if (_settings.Enabled && _settings.DragToTop)
        {
            Set(Xlib.XI_RawButtonPress);
            Set(Xlib.XI_RawButtonRelease);
            if (motion) Set(Xlib.XI_RawMotion);
        }
        var m = new Xlib.XIEventMask { deviceid = Xlib.XIAllMasterDevices, mask_len = 4, mask = mask };
        Xlib.XISelectEvents(_x!.Display, _x.Root, &m, 1);
        _motionSubscribed = motion;
        _x.Flush();
        return true;
    }

    private void Handle(byte* ev)
    {
        int type = Xlib.Type(ev);
        if (type == Xlib.GenericEvent)
        {
            var (extension, evtype) = Xlib.Generic(ev);
            if (extension != _xiOpcode) return;
            if (Xlib.XGetEventData(_x!.Display, ev) == 0) return;
            try
            {
                int detail = Xlib.RawDetail(ev);
                if (evtype is Xlib.XI_RawButtonPress or Xlib.XI_RawButtonRelease) Trace($"raw {(evtype == Xlib.XI_RawButtonPress ? "press" : "release")} {detail}");
                if (evtype == Xlib.XI_RawButtonPress && detail == 1) OnButton(true);
                else if (evtype == Xlib.XI_RawButtonRelease && detail == 1) OnButton(false);
            }
            finally { Xlib.XFreeEventData(_x!.Display, ev); }
            return;
        }

        if (type == Xlib.KeyPress && Xlib.Window(ev) == _x!.Root && !_gnomeShortcut && _grabbed is { } shortcut)
        {
            var (_, _, _, _, state, keycode) = Xlib.Input(ev);
            if (keycode == _grabbedKeycode && shortcut.Matches(state)) OpenPicker();
            return;
        }

        // Events for an interactive overlay are handled by its own modal loop.
    }

    private void OnButton(bool down)
    {
        if (down)
        {
            if (_overlay is { Visible: true } && !_dragging) return; // an interactive overlay's own click
            _buttonDown = true;
            _dragCandidate = 0;
            _lastFrame = null;
            _dragging = false;
            _nextSample = 0;
            SelectRawInput(motion: true);
            return;
        }

        _buttonDown = false;
        if (_motionSubscribed) SelectRawInput(motion: false);
        if (!_dragging) return;
        _dragging = false;
        var (panel, hover, window) = (_panel, _open ? _hover : null, _dragCandidate);
        HideOverlay();
        if (panel is not null && hover is { } zone && window != 0)
        {
            // The window manager finishes its own move on the release; place
            // after it, and once more if something (a tiling extension) moved
            // the window again.
            Thread.Sleep(60);
            Snap(window, panel, zone);
        }
    }

    private void SampleDrag()
    {
        long now = Environment.TickCount64;
        if (now < _nextSample) return;
        _nextSample = now + 25;

        if (!_dragging)
        {
            var active = Desktop.Active(_x!);
            if (active is not { } id) return;
            if (id != _dragCandidate)
            {
                _dragCandidate = id;
                _lastFrame = null;
            }
            var frame = Desktop.VisibleFrame(_x!, id);
            if (frame is not { } f) return;
            Trace($"sample 0x{id:x} {f}");
            if (_lastFrame is { } last && last.Width == f.Width && last.Height == f.Height
                && (Math.Abs(last.X - f.X) >= 2 || Math.Abs(last.Y - f.Y) >= 2))
            {
                if (Desktop.Describe(_x!, id) is null) return; // not an application window
                _dragging = true;
                _monitors = Desktop.Monitors(_x!);
                _theme = DesktopTheme.Current();
                _painted = null;
                _open = false;
                _hover = null;
            }
            _lastFrame = f;
            if (!_dragging) return;
        }

        var (px, py) = _x!.Pointer();
        var monitor = Desktop.MonitorAt(_monitors, px, py);
        if (_panel?.Monitor != monitor)
        {
            _panel = SnapPanel.For(monitor);
            _open = false;
        }
        var panel = _panel;
        bool nearTop = py < monitor.Bounds.Y + monitor.Bounds.Height / 3;
        if (!_open && panel.HandleHotspot.Contains(px, py)) _open = true;
        else if (_open && !panel.KeepOpen.Contains(px, py)) _open = false;
        _hover = _open ? panel.HitTest(px, py) : null;
        Trace($"drag at {px},{py} on {monitor.Name}: {(_open ? "open" : nearTop ? "handle" : "none")}{(_hover is { } th ? $" zone {th.Layout}/{th.Zone}" : "")}");

        bool showHandle = nearTop && !_open;
        var state = (monitor.Name, showHandle, _open, _hover);
        if (_painted == state) return;
        _painted = state;
        if (!showHandle && !_open)
        {
            _overlay?.Hide();
            return;
        }
        var overlay = EnsureOverlay(monitor.Bounds, interactive: false);
        if (overlay is null) return;
        var theme = _theme;
        var hover = _hover;
        int gap = _settings.Gap;
        overlay.Paint(c =>
        {
            if (_open) SnapPainter.Panel(c, panel, hover, gap, theme, monitor.Bounds);
            else SnapPainter.Handle(c, panel, theme, monitor.Bounds);
        });
    }

    // ---- the shortcut

    private void GrabShortcut()
    {
        UngrabShortcut(removeGnome: false);
        ShortcutProblem = null;
        var shortcut = _settings.Enabled ? Shortcut.Parse(_settings.Shortcut) : null;
        if (shortcut is null)
        {
            if (_gnomeShortcut || GnomeShortcuts.Available) GnomeShortcuts.Remove(GnomeId);
            _gnomeShortcut = false;
            if (_settings.Enabled && _settings.Shortcut.Length > 0)
            {
                ShortcutProblem = $"'{_settings.Shortcut}' is not a shortcut (write it as Super+Z or Ctrl+Alt+S)";
                _log($"snap: {ShortcutProblem}");
            }
            return;
        }

        if (GnomeShortcuts.Available)
        {
            var accelerator = GnomeShortcuts.Accelerator(shortcut);
            if (GnomeShortcuts.TakenBy(accelerator, GnomeId) is { } other)
            {
                GnomeShortcuts.Remove(GnomeId);
                ShortcutProblem = $"{shortcut.Text} is already GNOME's shortcut for \"{other}\"; choose another";
                _log($"snap: {ShortcutProblem}");
                return;
            }
            var exe = Environment.ProcessPath ?? "dispctrl-linux";
            var command = $"\"{exe}\" snap pick";
            // GNOME runs it without this process's environment: a build that
            // needs a .NET found through DOTNET_ROOT would not start there.
            if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root)
                command = $"env DOTNET_ROOT=\"{root}\" {command}";
            if (GnomeShortcuts.Set(GnomeId, "DispCtrl: snap layouts", command, accelerator) is { } error)
            {
                ShortcutProblem = error;
                _log($"snap: {error}");
                return;
            }
            _gnomeShortcut = true;
            _grabbed = shortcut;
            return;
        }

        var keysym = Xlib.XStringToKeysym(shortcut.Keysym);
        int keycode = keysym == 0 ? 0 : Xlib.XKeysymToKeycode(_x!.Display, keysym);
        if (keycode == 0)
        {
            ShortcutProblem = $"this keyboard has no {shortcut.Keysym} key";
            _log($"snap: {ShortcutProblem}");
            return;
        }
        Xlib.LastErrorCode = 0;
        foreach (var modifiers in shortcut.WithLocks())
            Xlib.XGrabKey(_x!.Display, keycode, modifiers, _x.Root, Xlib.False, Xlib.GrabModeAsync, Xlib.GrabModeAsync);
        Xlib.XSync(_x!.Display, Xlib.False);
        if (Xlib.LastErrorCode == Xlib.BadAccess)
        {
            foreach (var modifiers in shortcut.WithLocks()) Xlib.XUngrabKey(_x.Display, keycode, modifiers, _x.Root);
            ShortcutProblem = $"{shortcut.Text} is taken by another program; choose another";
            _log($"snap: {ShortcutProblem}");
            return;
        }
        _grabbed = shortcut;
        _grabbedKeycode = keycode;
    }

    private void UngrabShortcut(bool removeGnome)
    {
        if (_gnomeShortcut)
        {
            if (removeGnome) { GnomeShortcuts.Remove(GnomeId); _gnomeShortcut = false; }
            _grabbed = null;
            return;
        }
        if (_grabbed is null || _x is null) return;
        foreach (var modifiers in _grabbed.WithLocks()) Xlib.XUngrabKey(_x.Display, _grabbedKeycode, modifiers, _x.Root);
        _x.Flush();
        _grabbed = null;
    }

    // ---- the picker (shortcut)

    private void OpenPicker()
    {
        if (_x is null || _dragging) return;
        if (Desktop.Active(_x) is not { } id || Desktop.Describe(_x, id) is not { } window) return;
        _monitors = Desktop.Monitors(_x);
        _theme = DesktopTheme.Current();
        var monitor = Desktop.MonitorOf(_monitors, window.Frame);
        var panel = SnapPanel.For(monitor);
        var overlay = EnsureOverlay(monitor.Bounds, interactive: true);
        if (overlay is null) return;

        ZoneRef? hover = null;
        void Draw() => overlay.Paint(c => SnapPainter.Panel(c, panel, hover, _settings.Gap, _theme, monitor.Bounds));
        Draw();
        if (!overlay.Grab())
        {
            _log("snap: the keyboard is held by another program; the layouts could not open");
            HideOverlay();
            return;
        }

        var choice = Modal(overlay, (ev, type) =>
        {
            switch (type)
            {
                case Xlib.MotionNotify:
                {
                    var (x, y, _, _, _, _) = Xlib.Input((byte*)ev);
                    var h = panel.HitTest(x + monitor.Bounds.X, y + monitor.Bounds.Y);
                    if (h != hover) { hover = h; Draw(); }
                    return null;
                }
                case Xlib.ButtonPress:
                {
                    var (x, y, _, _, _, button) = Xlib.Input((byte*)ev);
                    if (button != 1) return Result.Cancel;
                    var h = panel.HitTest(x + monitor.Bounds.X, y + monitor.Bounds.Y);
                    return h is { } zone ? Result.Pick(zone) : Result.Cancel;
                }
                case Xlib.KeyPress:
                {
                    var keysym = Xlib.XLookupKeysym((byte*)ev, 0);
                    var name = KeyName(keysym);
                    if (name is "Escape") return Result.Cancel;
                    if (name is "Return" or "KP_Enter" or "space") return hover is { } zone ? Result.Pick(zone) : null;
                    int step = name switch { "Right" or "Tab" => 1, "Left" => -1, "Down" => 100, "Up" => -100, _ => 0 };
                    if (step == 0) return null;
                    hover = Math.Abs(step) == 100 ? StepLayout(panel, hover, Math.Sign(step)) : (hover is { } hz ? panel.Step(hz, step) : new ZoneRef(0, 0));
                    Draw();
                    return null;
                }
            }
            return null;
        });
        HideOverlay();
        if (choice is { Zone: { } picked }) Snap(id, panel, picked);
    }

    private static ZoneRef StepLayout(SnapPanel panel, ZoneRef? from, int direction)
    {
        int layout = from is { } f ? ((f.Layout + direction) % panel.Layouts.Count + panel.Layouts.Count) % panel.Layouts.Count : 0;
        return new ZoneRef(layout, 0);
    }

    private static string KeyName(ulong keysym) => keysym switch
    {
        0xff1b => "Escape", 0xff0d => "Return", 0xff8d => "KP_Enter", 0x20 => "space", 0xff09 => "Tab",
        0xff51 => "Left", 0xff52 => "Up", 0xff53 => "Right", 0xff54 => "Down", _ => "",
    };

    // ---- placing, then Snap Assist

    private void Snap(ulong window, SnapPanel panel, ZoneRef zone)
    {
        var target = panel.Target(zone, _settings.Gap);
        Desktop.Place(_x!, window, target, activate: true);
        Thread.Sleep(200);
        if (Desktop.VisibleFrame(_x!, window) is { } after && !Near(after, target))
            Desktop.Place(_x!, window, target, activate: true);
        _log($"snap: 0x{window:x} to {panel.Layouts[zone.Layout].Id} zone {zone.Zone + 1} on {panel.Monitor.Name}");
        if (_settings.Assist) Assist(window, panel, zone);
    }

    private static bool Near(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) <= 4 && Math.Abs(a.Y - b.Y) <= 4 && Math.Abs(a.Width - b.Width) <= 4 && Math.Abs(a.Height - b.Height) <= 4;

    private void Assist(ulong snapped, SnapPanel panel, ZoneRef filled)
    {
        var layout = panel.Layouts[filled.Layout];
        var free = Enumerable.Range(0, layout.Zones.Count).Where(z => z != filled.Zone).ToList();
        var candidates = Desktop.Windows(_x!).Where(w => w.Id != snapped && !w.Fullscreen).Take(12)
            .Select(w => new AssistCandidate(w.Id, w.Title, w.AppClass, w.Frame)).ToList();
        if (free.Count == 0 || candidates.Count == 0) return;

        using var pictures = new WindowPictures(_x!);
        foreach (var c in candidates) pictures.Capture(c.Id);
        var monitor = panel.Monitor;
        var overlay = EnsureOverlay(monitor.Bounds, interactive: true);
        if (overlay is null) return;

        foreach (var zoneIndex in free)
        {
            if (candidates.Count == 0) break;
            var zone = panel.Target(new ZoneRef(filled.Layout, zoneIndex), _settings.Gap);
            int? hover = null;
            void Draw() => overlay.Paint(c => SnapPainter.Assist(c, zone, candidates, hover, _theme, monitor.Bounds, pictures.Draw));
            Draw();
            if (!overlay.Grab()) break;

            int? Hit(int x, int y)
            {
                var cards = AssistGrid.Cards(zone, candidates.Count, AssistGrid.AspectOf(candidates));
                for (int i = 0; i < cards.Count; i++)
                    if (cards[i].Contains(x + monitor.Bounds.X, y + monitor.Bounds.Y)) return i;
                return null;
            }

            var choice = Modal(overlay, (ev, type) =>
            {
                switch (type)
                {
                    case Xlib.MotionNotify:
                    {
                        var (x, y, _, _, _, _) = Xlib.Input((byte*)ev);
                        var h = Hit(x, y);
                        if (h != hover) { hover = h; Draw(); }
                        return null;
                    }
                    case Xlib.ButtonPress:
                    {
                        var (x, y, _, _, _, _) = Xlib.Input((byte*)ev);
                        return Hit(x, y) is { } i ? Result.Card(i) : Result.Cancel;
                    }
                    case Xlib.KeyPress:
                    {
                        var name = KeyName(Xlib.XLookupKeysym((byte*)ev, 0));
                        if (name == "Escape") return Result.Cancel;
                        if (name is "Return" or "KP_Enter" or "space") return hover is { } i ? Result.Card(i) : null;
                        int step = name switch { "Right" or "Tab" or "Down" => 1, "Left" or "Up" => -1, _ => 0 };
                        if (step == 0) return null;
                        hover = ((hover ?? -step) + step + candidates.Count) % candidates.Count;
                        Draw();
                        return null;
                    }
                }
                return null;
            });
            overlay.Hide();
            if (choice is not { CardIndex: { } chosen }) break;
            var pick = candidates[chosen];
            candidates.RemoveAt(chosen);
            Desktop.Place(_x!, pick.Id, zone, activate: true);
            _log($"snap assist: 0x{pick.Id:x} to zone {zoneIndex + 1}");
            Thread.Sleep(120);
        }
        HideOverlay();
    }

    // ---- overlay plumbing

    private readonly record struct Result(ZoneRef? Zone, int? CardIndex, bool Cancelled)
    {
        public static Result Cancel => new(null, null, true);
        public static Result Pick(ZoneRef zone) => new(zone, null, false);
        public static Result Card(int index) => new(null, index, false);
    }

    /// <summary>Runs the overlay's own events until <paramref name="handle"/>
    /// returns a result. Raw button events from the same clicks are drained
    /// and ignored meanwhile, and the stop flag and posted work still apply.</summary>
    private Result? Modal(Overlay overlay, Func<nint, int, Result?> handleRaw)
    {
        byte* ev = stackalloc byte[Xlib.EventSize];
        var fds = stackalloc Xlib.PollFd[2];
        fds[0] = new Xlib.PollFd { fd = Xlib.XConnectionNumber(_x!.Display), events = Xlib.POLLIN };
        fds[1] = new Xlib.PollFd { fd = _wake[0], events = Xlib.POLLIN };
        byte* drain = stackalloc byte[64];
        long deadline = Environment.TickCount64 + 120_000; // never hold the keyboard forever
        while (!_stop && Environment.TickCount64 < deadline)
        {
            while (Xlib.XPending(_x.Display) > 0)
            {
                Xlib.XNextEvent(_x.Display, ev);
                int type = Xlib.Type(ev);
                if (type == Xlib.GenericEvent)
                {
                    if (Xlib.XGetEventData(_x.Display, ev) != 0) Xlib.XFreeEventData(_x.Display, ev);
                    continue;
                }
                if (Xlib.Window(ev) != overlay.Window) continue;
                var result = handleRaw((nint)ev, type);
                if (result is not null) return result;
            }
            fds[0].revents = fds[1].revents = 0;
            Xlib.poll(fds, 2, 500);
            if ((fds[1].revents & Xlib.POLLIN) != 0)
            {
                while (Xlib.read(_wake[0], drain, 64) > 0) { }
                if (_stop) break;
            }
        }
        return Result.Cancel;
    }

    private Overlay? EnsureOverlay(Rect bounds, bool interactive)
    {
        if (_overlay is null)
        {
            _overlay = Overlay.Create(_x!, bounds);
            if (_overlay is null)
            {
                _log("snap: this X server has no 32-bit visual (no compositor), so the layouts cannot be drawn");
                return null;
            }
        }
        _overlay.MoveTo(bounds);
        _overlay.SetInteractive(interactive);
        return _overlay;
    }

    private void HideOverlay()
    {
        _overlay?.Hide();
        _painted = null;
        _panel = null;
        _open = false;
        _hover = null;
    }

    public void Dispose()
    {
        _stop = true;
        Post(() => { });
        if (_thread.IsAlive) _thread.Join(3000);
        Xlib.close(_wake[0]);
        Xlib.close(_wake[1]);
    }
}
