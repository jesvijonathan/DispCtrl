using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DispCtrl.Core.Settings;
using DispCtrl.Display.Placement;
using static DispCtrl.Engine.Placement.PinNative;

namespace DispCtrl.Engine.Placement;

/// <summary>
/// Draws a border around each window pinned on top, and keeps it with the window.
/// </summary>
/// <remarks>
/// Pinning itself is <see cref="WindowPins"/>, shared by every DispCtrl process;
/// this is the part that needs a resident process. It hears about pins through
/// <see cref="WindowPins.ChangedEvent"/> and rediscovers them by their window
/// property, so a pin made by the command line or the app, or one that outlived
/// an engine restart, is bordered like one made by the hotkey.
/// <para>
/// Each border is four thin click-through strips rather than one window the
/// size of the pinned one: a layered window is a surface of its own full size,
/// and a 4K window framed that way would cost 33 MB to draw three pixels. The
/// strips' regions carry the rounded corners Windows 11 draws with.
/// </para>
/// <para>
/// A border follows its window through WinEvent hooks scoped to that window's
/// own thread - never a global location hook, which is the ~180 wake-ups a
/// second focus mode was measured paying on an idle desk. Nothing is hooked,
/// and the thread sleeps in <c>GetMessage</c>, while nothing is pinned.
/// </para>
/// </remarks>
internal sealed unsafe partial class PinService : IDisposable
{
    private const string ControlClass = "DispCtrl.PinControl", BorderClass = "DispCtrl.PinBorder";
    private const uint SyncMessage = 0x8001, UpdateMessage = 0x8002;

    /// <summary>A last look after a pinned window stops moving.</summary>
    /// <remarks>
    /// The window's last location event can arrive before DWM has its new
    /// frame: moved to a display at another scale, a pinned window's border was
    /// left at the size it had halfway through, and no later event came to fix it.
    /// </remarks>
    private const nuint SettleTimer = 1;
    private const uint SettleMs = 150;

    private static PinService? _instance;

    /// <summary>The pinned windows, for focus mode to keep clear; replaced whole, never changed in place.</summary>
    public static IReadOnlyList<nint> Pinned { get; private set; } = [];

    /// <summary>Raised on this service's thread when the pinned set or a pinned window's place changes.</summary>
    public static event Action? Changed;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly EventWaitHandle _changed;
    private readonly RegisteredWaitHandle _wait;
    private DispCtrlSettings _settings;
    private DispCtrlSettings? _pending;
    private nint _control;
    private bool _disposed;

    private sealed class Border
    {
        public nint Window;
        public uint Thread;
        public readonly nint[] Strips = new nint[4];
        public PinNative.Rect Drawn;
        public bool Shown;
        public int Thickness;
        public bool Inset;
    }

    private readonly Dictionary<nint, Border> _borders = [];

    /// <summary>Hooks per thread that owns a pinned window: several pins in one app share them.</summary>
    private readonly Dictionary<uint, (nint[] Hooks, int Count)> _hooks = [];

    /// <summary>Pinned windows that have stepped aside for a fullscreen window in front of their display.</summary>
    private readonly HashSet<nint> _stepped = [];

    /// <summary>The window in front, and the hooks that watch it: foreground changes, and that window's own moves.</summary>
    private nint _front, _frontHook, _frontMoves;

    private nint _brush;
    private uint _colour = uint.MaxValue;
    private byte _alpha = 255;

    public PinService(DispCtrlSettings settings)
    {
        _settings = settings;
        _changed = new EventWaitHandle(false, EventResetMode.AutoReset, WindowPins.ChangedEvent);
        _thread = new Thread(Pump) { IsBackground = true, Name = "Pinned windows" };
        _thread.Start();
        _ready.Wait();
        // A pin from any process sets the event; the pool thread only forwards it.
        _wait = ThreadPool.RegisterWaitForSingleObject(_changed, (_, _) =>
        {
            if (_control != 0) PostMessage(_control, SyncMessage, 0, 0);
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Update(DispCtrlSettings settings)
    {
        Volatile.Write(ref _pending, settings);
        if (_control != 0) PostMessage(_control, UpdateMessage, 0, 0);
    }

    private void Pump()
    {
        try
        {
            _instance = this;
            nint module = GetModuleHandle(null);
            fixed (char* control = ControlClass)
            fixed (char* border = BorderClass)
            {
                var cls = new WindowClass { Size = (uint)sizeof(WindowClass), Proc = &WindowProc, Instance = module, Name = control };
                RegisterClassEx(ref cls);
                cls.Name = border;
                RegisterClassEx(ref cls);
            }
            // HWND_MESSAGE: it only ever receives what is posted to it.
            _control = CreateWindowEx(0, ControlClass, "DispCtrl pinned windows", 0, 0, 0, 0, 0, -3, 0, module, 0);
            _ready.Set();
            if (_control == 0) { Log.Write("pins: no control window, borders not drawn"); return; }

            // Windows pinned before this engine started, by an earlier one or by the command line.
            Sync();
            while (GetMessage(out Message message, 0, 0, 0) > 0) DispatchMessage(ref message);
        }
        catch (Exception ex) { Log.Write($"pins stopped: {ex.Message}"); }
        finally
        {
            // Pins that stepped aside for a fullscreen window are put back on
            // top before the engine goes: without it they would stay unpinned
            // in all but name.
            foreach (nint window in _stepped) SetTopmost(window);
            _stepped.Clear();
            foreach (nint window in _borders.Keys.ToArray()) Drop(window);
            foreach (var hook in _hooks.Values) foreach (nint h in hook.Hooks) UnhookWinEvent(h);
            _hooks.Clear();
            WatchFront(false);
            if (_brush != 0) DeleteObject(_brush);
            if (_control != 0) DestroyWindow(_control);
            _control = 0;
            _instance = null;
            Pinned = [];
            _ready.Set();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint window, uint message, nuint wparam, nint lparam)
    {
        try
        {
            if (_instance is { } self)
            {
                if (window == self._control)
                {
                    switch (message)
                    {
                        case 0x10: PostQuitMessage(0); return 0; // WM_CLOSE
                        case SyncMessage: self.Sync(); return 0;
                        case 0x113 when wparam == SettleTimer: // WM_TIMER
                            KillTimer(window, SettleTimer);
                            self.ReviewFullscreen();
                            foreach (nint pinned in self._borders.Keys.ToArray()) self.Follow(pinned);
                            return 0;
                        case UpdateMessage:
                            if (Interlocked.Exchange(ref self._pending, null) is { } settings)
                            {
                                self._settings = settings;
                                self.Configure();
                            }
                            return 0;
                    }
                }
                else if (message == 0x14) return 1; // WM_ERASEBKGND: painted whole below
                else if (message == 0x0F) // WM_PAINT
                {
                    nint hdc = BeginPaint(window, out PaintStruct paint);
                    GetClientRect(window, out PinNative.Rect client);
                    if (self._brush != 0) FillRect(hdc, ref client, self._brush);
                    EndPaint(window, ref paint);
                    return 0;
                }
            }
            if (message == 0x84) return -1; // HTTRANSPARENT: clicks go to what is underneath
            if (message == 0x21) return 3; // MA_NOACTIVATE
        }
        catch (Exception ex) { Log.Write($"pins: {ex.Message}"); }
        return DefWindowProc(window, message, wparam, lparam);
    }

    /// <summary>Every window's own thread reports here; only a pinned top-level window is followed.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void WindowEvent(nint hook, uint evt, nint window, int obj, int child, uint thread, uint time)
    {
        try
        {
            if (_instance is not { } self || obj != 0 || child != 0) return;
            // Another window came to the front, or the one in front changed
            // size - going fullscreen, or leaving it: whether pins step aside
            // is decided once the events stop.
            if (hook == self._frontHook || hook == self._frontMoves)
            {
                if (evt == 0x0003 || window == self._front) SetTimer(self._control, SettleTimer, SettleMs, 0);
                if (evt == 0x0003) self.FrontChanged(window);
                return;
            }
            if (!self._borders.ContainsKey(window)) return;
            // Destroyed, shown, hidden, moved, minimized, restored, cloaked,
            // brought to the front: every one of them is "look again" - now,
            // and once more when the events stop.
            self.Follow(window);
            SetTimer(self._control, SettleTimer, SettleMs, 0);
        }
        catch (Exception ex) { Log.Write($"pins: {ex.Message}"); }
    }

    // ------------------------------------------------------------ the set --

    /// <summary>Brings the bordered set into line with the windows that carry the pin mark.</summary>
    private void Sync()
    {
        var pinned = new List<nint>();
        GCHandle box = GCHandle.Alloc(pinned);
        try { EnumWindows(&CollectPinned, GCHandle.ToIntPtr(box)); }
        finally { box.Free(); }

        foreach (nint gone in _borders.Keys.Where(w => !pinned.Contains(w)).ToArray()) Drop(gone);
        foreach (nint window in pinned) if (!_borders.ContainsKey(window)) Add(window);
        _stepped.RemoveWhere(w => !_borders.ContainsKey(w));
        Publish();
        WatchFront(_borders.Count > 0 && _settings.Global.Pin.StepAsideForFullscreen);
        ReviewFullscreen();
        foreach (nint window in pinned) Follow(window);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CollectPinned(nint window, nint param)
    {
        if (GetProp(window, WindowPins.Property) != 0 && GCHandle.FromIntPtr(param).Target is List<nint> list) list.Add(window);
        return 1;
    }

    private void Add(nint window)
    {
        uint thread = GetWindowThreadProcessId(window, out uint process);
        if (thread == 0) return;
        var border = new Border { Window = window, Thread = thread };
        nint module = GetModuleHandle(null);
        for (int i = 0; i < 4; i++)
        {
            // Layered and transparent: drawn by DWM, never hit-tested, never
            // activated, never in Alt+Tab or on the taskbar.
            border.Strips[i] = CreateWindowEx(0x08000000 | 0x80 | 0x20 | 0x80000 | 0x8, BorderClass, "DispCtrl pin border",
                0x80000000, 0, 0, 0, 0, 0, 0, module, 0);
            if (border.Strips[i] != 0)
            {
                // Not a fullscreen app, whatever size it is: see the focus overlays.
                SetProp(border.Strips[i], "NonRudeHWND", 1);
                SetLayeredWindowAttributes(border.Strips[i], 0, _alpha, 2);
            }
        }
        _borders[window] = border;

        if (_hooks.TryGetValue(thread, out var hooks)) _hooks[thread] = hooks with { Count = hooks.Count + 1 };
        else
        {
            // The window's own thread only, out of context, and only the events
            // that move or hide a window. One range from destroy to uncloak
            // took every name, value and state change too: a browser's UI
            // thread raises those by the thousand, each one a wake-up here.
            _hooks[thread] = ([
                SetWinEventHook(0x0003, 0x0003, 0, &WindowEvent, process, thread, 0), // foreground
                SetWinEventHook(0x0016, 0x0017, 0, &WindowEvent, process, thread, 0), // minimize start, end
                SetWinEventHook(0x8001, 0x8003, 0, &WindowEvent, process, thread, 0), // destroy, show, hide
                SetWinEventHook(0x800B, 0x800B, 0, &WindowEvent, process, thread, 0), // location
                SetWinEventHook(0x8017, 0x8018, 0, &WindowEvent, process, thread, 0), // cloaked, uncloaked
            ], 1);
        }
    }

    private void Drop(nint window)
    {
        if (!_borders.Remove(window, out Border? border)) return;
        _stepped.Remove(window);
        foreach (nint strip in border.Strips)
            if (strip != 0) { _stripShown.Remove(strip); RemoveProp(strip, "NonRudeHWND"); DestroyWindow(strip); }
        if (_hooks.TryGetValue(border.Thread, out var hooks))
        {
            if (hooks.Count <= 1)
            {
                foreach (nint h in hooks.Hooks) if (h != 0) UnhookWinEvent(h);
                _hooks.Remove(border.Thread);
            }
            else _hooks[border.Thread] = hooks with { Count = hooks.Count - 1 };
        }
    }

    private void Publish()
    {
        Pinned = [.. _borders.Keys];
        Changed?.Invoke();
    }

    /// <summary>Settings changed: colour, thickness, opacity, or pinning switched off.</summary>
    private void Configure()
    {
        PinSettings pin = _settings.Global.Pin;
        if (!pin.Enabled && _borders.Count > 0)
        {
            // Switched off: every pin DispCtrl made is taken back. The event
            // those unpins raise comes back here and drops the borders.
            int count = WindowPins.UnpinAll();
            Log.Write($"pins: pinning switched off, {count} window(s) unpinned");
            return;
        }
        WatchFront(_borders.Count > 0 && pin.StepAsideForFullscreen);
        ReviewFullscreen();
        foreach (Border border in _borders.Values) border.Drawn = default;
        foreach (nint window in _borders.Keys.ToArray()) Follow(window);
    }

    // ------------------------------------------------- stepping aside for fullscreen --

    /// <summary>
    /// Watches which window is in front, and that window's own moves, while
    /// something is pinned and pins step aside for fullscreen.
    /// </summary>
    /// <remarks>
    /// The foreground hook is global but rare - one event per switch of window.
    /// The moves hook is scoped to the front window's thread, so a film going
    /// fullscreen with F11 or a double-click is seen without watching every
    /// window on the desk move.
    /// </remarks>
    private void WatchFront(bool on)
    {
        if (on && _frontHook == 0)
        {
            _frontHook = SetWinEventHook(0x0003, 0x0003, 0, &WindowEvent, 0, 0, 2);
            FrontChanged(GetForegroundWindow());
        }
        else if (!on && _frontHook != 0)
        {
            UnhookWinEvent(_frontHook);
            _frontHook = 0;
            if (_frontMoves != 0) { UnhookWinEvent(_frontMoves); _frontMoves = 0; }
            _front = 0;
            foreach (nint window in _stepped) SetTopmost(window);
            _stepped.Clear();
        }
    }

    private void FrontChanged(nint window)
    {
        if (window == _front) return;
        _front = window;
        if (_frontMoves != 0) { UnhookWinEvent(_frontMoves); _frontMoves = 0; }
        if (window == 0 || _borders.ContainsKey(window)) return;
        uint thread = GetWindowThreadProcessId(window, out uint process);
        if (thread != 0) _frontMoves = SetWinEventHook(0x800B, 0x800B, 0, &WindowEvent, process, thread, 0);
    }

    /// <summary>Steps pins aside on the display a fullscreen window is in front of, and back when it leaves.</summary>
    private void ReviewFullscreen()
    {
        nint fullscreenOn = 0;
        if (_settings.Global.Pin.StepAsideForFullscreen && _front != 0 && !_borders.ContainsKey(_front)
            && IsWindow(_front) != 0 && AppWindows.IsCandidate(_front) && IsIconic(_front) == 0
            && DwmGetWindowAttribute(_front, 9, out PinNative.Rect frame, (uint)sizeof(PinNative.Rect)) == 0)
        {
            nint monitor = MonitorFromWindow(_front, 2);
            var info = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
            bool captioned = (GetWindowLongPtr(_front, -16) & 0x00C00000) == 0x00C00000;
            // Content fullscreen, as focus mode judges it: a maximized window
            // with a title bar fills the screen too when the taskbar is hidden,
            // and is not a film.
            if (GetMonitorInfo(monitor, ref info) != 0
                && frame.Left <= info.Monitor.Left && frame.Top <= info.Monitor.Top
                && frame.Right >= info.Monitor.Right && frame.Bottom >= info.Monitor.Bottom
                && !(IsZoomed(_front) != 0 && captioned))
                fullscreenOn = monitor;
        }

        bool changed = false;
        foreach (nint window in _borders.Keys)
        {
            bool aside = fullscreenOn != 0 && MonitorFromWindow(window, 2) == fullscreenOn;
            if (aside && _stepped.Add(window))
            {
                // Just below the fullscreen window: still above ordinary ones.
                SetWindowPos(window, -2, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
                SetWindowPos(window, _front, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
                changed = true;
            }
            else if (!aside && _stepped.Remove(window))
            {
                SetTopmost(window);
                changed = true;
            }
        }
        if (changed) Log.Write(fullscreenOn != 0
            ? $"pins: {_stepped.Count} pinned window(s) stepped aside for a fullscreen window"
            : "pins: fullscreen window gone; pinned windows back on top");
    }

    private static void SetTopmost(nint window) =>
        SetWindowPos(window, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);

    // --------------------------------------------------------- the border --

    /// <summary>Puts one window's border where the window is, or hides it.</summary>
    private void Follow(nint window)
    {
        if (!_borders.TryGetValue(window, out Border? border)) return;
        if (IsWindow(window) == 0 || GetProp(window, WindowPins.Property) == 0)
        {
            Drop(window);
            Publish();
            return;
        }

        PinSettings pin = _settings.Global.Pin;
        bool cloaked = DwmGetWindowAttributeInt(window, 14, out int cloak, sizeof(int)) == 0 && cloak != 0;
        bool shown = IsWindowVisible(window) != 0 && IsIconic(window) == 0 && !cloaked;
        bool aside = _stepped.Contains(window);

        // Something took topmost off it - some players and games drop it on a
        // mode change. The mark says it is meant to be pinned. Before the
        // border is considered: with the border off this used to be skipped,
        // and such a window quietly stopped being on top.
        if (shown && !aside && (GetWindowLongPtr(window, -20) & 0x8) == 0) SetTopmost(window);

        if (!pin.Border || !shown || aside)
        {
            if (border.Shown) { Hide(border); Changed?.Invoke(); }
            return;
        }

        if (DwmGetWindowAttribute(window, 9, out PinNative.Rect frame, (uint)sizeof(PinNative.Rect)) != 0) return;
        nint monitor = MonitorFromWindow(window, 2);
        var info = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
        GetMonitorInfo(monitor, ref info);
        uint dpi = GetDpiForMonitor(monitor, 0, out uint x, out _) == 0 && x > 0 ? x : 96;
        double scale = dpi / 96.0;
        int thickness = Math.Max(1, (int)Math.Round(Math.Clamp(pin.BorderThickness, 1, 16) * scale));
        // A maximized window, or one filling its display's work area, has no
        // room around it: the border goes inside its edge instead of onto the
        // next display.
        bool inset = IsZoomed(window) != 0
            || (frame.Left <= info.Work.Left && frame.Top <= info.Work.Top && frame.Right >= info.Work.Right && frame.Bottom >= info.Work.Bottom);
        int radius = inset || !RoundedCorners ? 0 : (int)Math.Round(8 * scale);

        EnsureBrush(pin);
        bool geometryChanged = !frame.Equals(border.Drawn) || border.Thickness != thickness || border.Inset != inset || !border.Shown;
        if (geometryChanged) Shape(border, frame, info.Monitor, thickness, radius, inset);

        // Directly above the pinned window, so another window on top of it is
        // on top of its border too.
        nint above = GetWindow(window, 3); // GW_HWNDPREV
        while (above != 0 && border.Strips.Contains(above)) above = GetWindow(above, 3);
        nint after = above == 0 ? HwndTopmost : above;
        foreach (nint strip in border.Strips)
            if (strip != 0 && _stripShown.Contains(strip))
                SetWindowPos(strip, after, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow | SwpNoOwnerZOrder);
        border.Shown = true;
        border.Drawn = frame;
        border.Thickness = thickness;
        border.Inset = inset;
        // Only a real change: a title changing every second raised a focus-mode
        // tick every second for nothing.
        if (geometryChanged) Changed?.Invoke();
    }

    private readonly HashSet<nint> _stripShown = [];

    /// <summary>Sizes the four strips and cuts each to its part of the ring.</summary>
    private void Shape(Border border, PinNative.Rect frame, PinNative.Rect monitor, int t, int radius, bool inset)
    {
        PinNative.Rect outer = inset ? frame : new() { Left = frame.Left - t, Top = frame.Top - t, Right = frame.Right + t, Bottom = frame.Bottom + t };
        PinNative.Rect inner = inset ? new() { Left = frame.Left + t, Top = frame.Top + t, Right = frame.Right - t, Bottom = frame.Bottom - t } : frame;
        int band = t + radius;
        PinNative.Rect[] parts =
        [
            new() { Left = outer.Left, Top = outer.Top, Right = outer.Right, Bottom = outer.Top + band },
            new() { Left = outer.Left, Top = outer.Bottom - band, Right = outer.Right, Bottom = outer.Bottom },
            new() { Left = outer.Left, Top = outer.Top + band, Right = outer.Left + t, Bottom = outer.Bottom - band },
            new() { Left = outer.Right - t, Top = outer.Top + band, Right = outer.Right, Bottom = outer.Bottom - band },
        ];

        for (int i = 0; i < 4; i++)
        {
            nint strip = border.Strips[i];
            if (strip == 0) continue;
            // Never onto the next display: clipped to this window's own.
            PinNative.Rect part = Intersect(parts[i], monitor);
            if (part.Right - part.Left <= 0 || part.Bottom - part.Top <= 0)
            {
                ShowWindow(strip, 0);
                _stripShown.Remove(strip);
                continue;
            }

            nint ring = CreateRoundRectRgn(outer.Left, outer.Top, outer.Right + 1, outer.Bottom + 1, (radius + t) * 2, (radius + t) * 2);
            nint hole = CreateRoundRectRgn(inner.Left, inner.Top, inner.Right + 1, inner.Bottom + 1, radius * 2, radius * 2);
            nint clip = CreateRectRgn(part.Left, part.Top, part.Right, part.Bottom);
            CombineRgn(ring, ring, hole, RgnDiff);
            CombineRgn(ring, ring, clip, RgnAnd);
            DeleteObject(hole);
            DeleteObject(clip);
            OffsetRgn(ring, -part.Left, -part.Top);

            SetWindowPos(strip, 0, part.Left, part.Top, part.Right - part.Left, part.Bottom - part.Top, SwpNoZOrder | SwpNoActivate);
            // Windows owns the region once it is set.
            if (SetWindowRgn(strip, ring, 1) == 0) DeleteObject(ring);
            InvalidateRect(strip, 0, 1);
            _stripShown.Add(strip);
        }
    }

    private void Hide(Border border)
    {
        if (!border.Shown) return;
        foreach (nint strip in border.Strips)
            if (strip != 0) { ShowWindow(strip, 0); _stripShown.Remove(strip); }
        border.Shown = false;
        border.Drawn = default;
    }

    private static PinNative.Rect Intersect(PinNative.Rect a, PinNative.Rect b) => new()
    {
        Left = Math.Max(a.Left, b.Left), Top = Math.Max(a.Top, b.Top),
        Right = Math.Min(a.Right, b.Right), Bottom = Math.Min(a.Bottom, b.Bottom),
    };

    /// <summary>Windows 11 rounds window corners; Windows 10 does not.</summary>
    private static readonly bool RoundedCorners = Environment.OSVersion.Version.Build >= 22000;

    /// <summary>The border's colour and opacity, remade only when they change.</summary>
    private void EnsureBrush(PinSettings pin)
    {
        uint colour = Colour(pin.BorderColour);
        byte alpha = (byte)Math.Round(Math.Clamp(pin.BorderOpacity, 20, 100) * 2.55);
        if (colour != _colour)
        {
            if (_brush != 0) DeleteObject(_brush);
            // COLORREF is 0x00BBGGRR.
            _brush = CreateSolidBrush(((colour & 0xFF) << 16) | (colour & 0xFF00) | ((colour >> 16) & 0xFF));
            _colour = colour;
            foreach (Border b in _borders.Values)
                foreach (nint strip in b.Strips) if (strip != 0) InvalidateRect(strip, 0, 1);
        }
        if (alpha != _alpha)
        {
            _alpha = alpha;
            foreach (Border b in _borders.Values)
                foreach (nint strip in b.Strips) if (strip != 0) SetLayeredWindowAttributes(strip, 0, alpha, 2);
        }
    }

    /// <summary>The border colour as 0xRRGGBB: the setting's, or Windows' accent colour.</summary>
    internal static uint Colour(string? setting)
    {
        string text = (setting ?? "").Trim().TrimStart('#');
        if (text.Length == 6 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint rgb)) return rgb;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr)
            {
                uint v = unchecked((uint)abgr);
                return ((v & 0xFF) << 16) | (v & 0xFF00) | ((v >> 16) & 0xFF);
            }
        }
        catch (Exception) { }
        return 0x0078D4; // Windows' default blue
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wait.Unregister(null);
        if (_control != 0) PostMessage(_control, 0x10, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _changed.Dispose();
        _ready.Dispose();
    }
}
