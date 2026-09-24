using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using static DispCtrl.Engine.Protection.OverlayNative;

namespace DispCtrl.Engine.Protection;

/// <summary>One sleeping message pump; one cached black overlay per monitor.</summary>
internal sealed unsafe partial class FocusService : IDisposable
{
    private const uint UpdateMessage = 0x8001;


    private const string ClassName = "DispCtrl.ProtectionOverlay";
    private static FocusService? _instance;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private DispCtrlSettings _settings;
    private DispCtrlSettings? _pending;
    private readonly List<nint> _hooks = [];
    private (bool Active, bool Focus, bool Prioritize)? _hookState;
    private readonly List<Mask> _masks = [];
    private HashSet<string> _excluded = [];
    private string? _excludedApps;
    private (bool FollowMouse, bool KeepHoveredClear)? _pointerMode;
    private nint _control, _foreground;
    private bool _locked;

    [System.Runtime.InteropServices.LibraryImport("wtsapi32.dll")]
    private static partial int WTSRegisterSessionNotification(nint hwnd, uint flags);
    [System.Runtime.InteropServices.LibraryImport("wtsapi32.dll")]
    private static partial int WTSUnRegisterSessionNotification(nint hwnd);
    private nint _automaticWindow;
    private string _automaticClass = "";
    private long _focusedAt;
    private bool _menuOpen, _suspended, _excludedForeground, _disposed;

    /// <summary>Whether the surroundings are already dimmed, so the delay is spent.</summary>
    private bool _dimming;

    /// <summary>The window the hole is currently cut for, focused or hovered.</summary>
    private nint _subject;

    /// <summary>That window's class, so the shell test follows the subject.</summary>
    private string _subjectClass = "";

    /// <summary>The other windows currently held clear, to notice the set changing.</summary>
    private nint _companion, _bar;

    /// <summary>Whether a screen rest is outstanding, so the idle clock is worth reading.</summary>
    private bool _restPending;

    // "Turn off displays": the request being honoured, when it took effect (0 while
    // its delay runs), one already finished, and where the pointer last was.
    private DateTimeOffset? _dimRequest, _dimDone;
    private long _dimAppliedAt;
    private bool _dimCursorKnown;
    private int _dimCursorX, _dimCursorY;

    // Where the pointer was before it was parked, and where it was parked, so
    // a turn-back-on that did not use the mouse can put it back.
    private Point? _parkedFrom, _parkedAt;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LockWorkStation();

    /// <summary>Whether the displays-off session now ending asked to lock on the way out.</summary>
    private bool _lockOnWake;

    // Raw input, registered only while a rest is showing. A rest is ended by
    // somebody coming back, and waiting to be told costs nothing: polling for it
    // ran this thread ten times a second for as long as a screen rested - all
    // night, for an OLED idle rest or displays left off.
    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public nint Target; }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterRawInputDevices(RawInputDevice* devices, uint count, uint size);

    private const uint WmInput = 0x00FF, RidevInputSink = 0x100, RidevRemove = 0x1;
    private bool _inputSink;

    private void WatchInput(bool on)
    {
        if (on == _inputSink || (on && _control == 0)) return;
        RawInputDevice* devices = stackalloc RawInputDevice[2];
        uint flags = on ? RidevInputSink : RidevRemove;
        nint target = on ? _control : 0;
        devices[0] = new RawInputDevice { UsagePage = 1, Usage = 2, Flags = flags, Target = target }; // mouse
        devices[1] = new RawInputDevice { UsagePage = 1, Usage = 6, Flags = flags, Target = target }; // keyboard
        if (RegisterRawInputDevices(devices, 2, (uint)sizeof(RawInputDevice))) _inputSink = on;
        else if (on) Log.Write($"rest: input could not be watched ({Marshal.GetLastPInvokeError()}); polling instead");
    }

    /// <summary>Whether the pointer announces itself, so it need not be polled for.</summary>
    private bool _pointerEvents;

    /// <summary>Where the active window was last seen, to notice it moving.</summary>
    private DisplayRect _lastActive;

    /// <summary>While this is in the future, the window counts as still moving.</summary>
    private long _movingUntil;

    /// <summary>Where the clear area is being slid from, and where it has got to.</summary>
    private DisplayRect _holeFrom, _holeShown;

    /// <summary>When the current slide between windows began.</summary>
    private long _holeStarted;
    private string _foregroundClass = "";
    private uint _timerMs;
    private long _previewUntil;
    private int _previewPercent;

    /// <summary>
    /// One display's dimming, as two interchangeable layers.
    /// </summary>
    /// <remarks>
    /// Only one is doing anything at rest. The second exists so a switch between
    /// windows can be one cut-out fading out while the other fades in, which a
    /// single region cannot express - see <c>FocusGeometry.Overlay</c>.
    /// </remarks>
    private sealed class Mask(DisplayInfo display, nint live, nint ghost)
    {
        public DisplayInfo Display = display;

        /// <summary>
        /// Whether the device library says this model is OLED. Read once per
        /// mask, because the loop runs every frame of a fade: a panel the app
        /// has never been opened on is still protected.
        /// </summary>
        public readonly bool LibraryOled = Core.Devices.DeviceLibrary.Panel(display.Key.Model)?.IsOled == true;

        /// <summary>Holds the cut-out for the window in use.</summary>
        public nint Live = live;

        /// <summary>Holds the cut-out being left behind, while it fades out.</summary>
        public nint Ghost = ghost;

        public DisplayRect? LiveHole, GhostHole;
        public DisplayRect? LiveHole2, GhostHole2;
        public DisplayRect? LiveHole3, GhostHole3;
        public DisplayRect LiveArea, GhostArea;
        public int LiveBleed = -1, GhostBleed = -1;
        public bool LiveVisible, GhostVisible;

        /// <summary>The alpha each layer was last given, so an unchanged tick costs nothing.</summary>
        public int LiveApplied = -1, GhostApplied = -1;

        /// <summary>The window this display keeps clear, when each has its own.</summary>
        public nint Owner, LastOwner;
        public DateTimeOffset? RestUntil;
        public long RestSince;

        /// <summary>Chosen by "Turn off displays" when they went off, and whether it has since been woken.</summary>
        public bool DimChosen, DimWoken;
        public OledIdleState IdleState { get; } = new();
        public double Alpha, From, Target;
        public long Started;
        public int Duration;
        public long CrossStarted;
        public int CrossMs;
    }

    public FocusService(DispCtrlSettings settings)
    {
        _settings = settings;
        _thread = new Thread(Pump) { IsBackground = true, Name = "Display focus" };
        _thread.Start();
        _ready.Wait();
    }

    public void Update(DispCtrlSettings settings)
    {
        // One queued message consumes the latest settings from a save burst.
        // A message that could not be posted must not leave _pending set, or
        // every later save would assume one is already on its way.
        if (Interlocked.Exchange(ref _pending, settings) is null
            && (_control == 0 || PostMessage(_control, UpdateMessage, 0, 0) == 0))
            Interlocked.CompareExchange(ref _pending, null, settings);
    }

    private void Pump()
    {
        try
        {
            _instance = this;
            fixed (char* name = ClassName)
            {
                var cls = new WindowClass { Size = (uint)sizeof(WindowClass), Proc = &WindowProc,
                    Instance = GetModuleHandle(null), Name = name, Brush = GetStockObject(4) }; // BLACK_BRUSH
                if (RegisterClassEx(ref cls) == 0) throw new InvalidOperationException("Cannot register protection overlay.");
            }
            _control = CreateWindowEx(0x08000080, ClassName, "DispCtrl protection service", 0x80000000,
                0, 0, 0, 0, 0, 0, GetModuleHandle(null), 0);
            if (_control == 0) throw new InvalidOperationException("Cannot create protection message window.");
            // Lock and unlock arrive as messages, so a locked session costs no polling.
            _ = WTSRegisterSessionNotification(_control, 0);
            Configure();
            _ready.Set();
            while (GetMessage(out Message message, 0, 0, 0) > 0) DispatchMessage(ref message);
        }
        catch (Exception ex) { Log.Write($"Display protection stopped: {ex.Message}"); }
        finally
        {
            ClearMasks();
            ClearHooks();
            if (_control != 0) { _ = WTSUnRegisterSessionNotification(_control); DestroyWindow(_control); }
            _control = 0;
            _instance = null;
            _ready.Set();
        }
    }

    private void Configure(bool rebuildMasks = true)
    {
        FocusSettings focus = _settings.Global.Focus;
        string excludedApps = focus.ExcludedApps ?? "";
        bool exclusionsChanged = _excludedApps != excludedApps;
        if (exclusionsChanged)
        {
            _excludedApps = excludedApps;
            _excluded = focus.Exclusions();
        }
        if (rebuildMasks) ClearMasks();
        bool active = _settings.Global.Focus.Enabled || _settings.Global.OledCare.Enabled
            || _settings.Monitors.Values.Any(monitor => monitor.OledRestUntilUtc > DateTimeOffset.UtcNow)
            || _settings.Global.Awake.DisplaysOffUtc is not null;
        bool focusMode = _settings.Global.Focus.Enabled;
        bool prioritize = focusMode && _settings.Global.Focus.PrioritizeNewWindows;
        var hookState = (Active: active, Focus: focusMode, Prioritize: prioritize);
        bool hooksChanged = _hookState != hookState;
        if (hooksChanged)
        {
            ClearHooks();
            if (active)
            {
                AddHook(3, focusMode ? 7u : 3u);
                if (focusMode)
                {
                    AddHook(0x800B, 0x800B);
                    AddHook(0x0016, 0x0017);
                }
                if (prioritize) AddHook(0x8001, 0x8003);
            }
            _hookState = hookState;
            _pointerEvents = false;
        }
        var pointerMode = (focus.FollowMouse, focus.KeepHoveredClear);
        if (rebuildMasks || hooksChanged || _pointerMode != pointerMode)
        {
            _automaticWindow = 0;
            _automaticClass = "";
        }
        _pointerMode = pointerMode;
        if (!active) { _previewUntil = 0; ClearMasks(); WatchInput(false); }
        if (active)
        {
            // Out-of-context notifications: no DLL injection, no keyboard hook.
            // Location changes are the expensive one: out of context, every
            // caret, progress bar and animation in every process is a wake-up
            // of this thread - measured at about 180 a second on an idle desk,
            // nearly all of them thrown away. Only focus mode follows windows and
            // the pointer live. OLED care alone needs the foreground window, and
            // its once-a-second tick already re-reads that window's rectangle,
            // the idle time and the pointer.
            foreach (DisplayInfo d in _masks.Count == 0 ? DisplayRegistry.Enumerate() : [])
            {
                nint live = CreateWindowEx(0x080800A8, ClassName, "DispCtrl dim overlay", 0x80000000,
                    d.Bounds.Left, d.Bounds.Top, d.Bounds.Width, d.Bounds.Height, 0, 0, GetModuleHandle(null), 0);
                nint ghost = CreateWindowEx(0x080800A8, ClassName, "DispCtrl dim overlay", 0x80000000,
                    d.Bounds.Left, d.Bounds.Top, d.Bounds.Width, d.Bounds.Height, 0, 0, GetModuleHandle(null), 0);
                if (live == 0 || ghost == 0)
                {
                    if (live != 0) DestroyWindow(live);
                    if (ghost != 0) DestroyWindow(ghost);
                    throw new InvalidOperationException("Cannot create dim overlay.");
                }
                // These monitor-sized windows are overlays, not fullscreen
                // applications. Without this documented shell property,
                // Explorer's fullscreen heuristic demotes the secondary
                // taskbar behind every app, even after a topmost reveal.
                // Set it before either layer can be shown (including fades).
                if (SetProp(live, "NonRudeHWND", 1) == 0 || SetProp(ghost, "NonRudeHWND", 1) == 0)
                {
                    DestroyWindow(live);
                    DestroyWindow(ghost);
                    throw new InvalidOperationException("Cannot exclude dim overlays from fullscreen detection.");
                }
                SetLayeredWindowAttributes(live, 0, 0, 2);
                SetLayeredWindowAttributes(ghost, 0, 0, 2);
                _masks.Add(new(d, live, ghost));
            }
        }
        // ForegroundChanged already ticks. Unrelated saves must not restart
        // the focus delay, clear a newly shown window, or restore pointer polling.
        if (rebuildMasks || hooksChanged || exclusionsChanged) ForegroundChanged();
        else Tick();
    }

    private void AddHook(uint min, uint max)
    {
        nint hook = SetWinEventHook(min, max, 0, &WindowEvent, 0, 0, 2); // skip own process
        if (hook == 0) throw new InvalidOperationException("Cannot observe foreground windows.");
        _hooks.Add(hook);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void WindowEvent(nint hook, uint evt, nint window, int obj, int child, uint thread, uint time)
    {
        try
        {
            if (_instance is not { } self) return;
            // OBJID_CURSOR. The pointer reports its own movement through the very
            // hook already watching window positions, so nothing has to poll for
            // it - and unlike raw input this also catches a cursor moved by
            // software rather than by hand.
            bool pointerMoved = evt == 0x800B && obj == -9;
            if (evt == 0x800B && !pointerMoved && (window != self._foreground || obj != 0)) return;
            if (pointerMoved)
            {
                self._pointerEvents = true;
                self._automaticWindow = 0;
                self._automaticClass = "";
            }
            if (evt == 3) self.ForegroundChanged(prioritize: true);
            if (evt == 0x8002 && obj == 0 && child == 0) self.WindowShown(window);
            if (evt is 0x8001 or 0x8003 && window == self._automaticWindow)
            {
                self._automaticWindow = 0;
                self._automaticClass = "";
                self.Tick();
            }
            if (evt is 4 or 6) self._menuOpen = true;
            if (evt is 5 or 7) self._menuOpen = false;

            // A moving pointer reports far faster than a window does, and what it
            // drives - which window is hovered - cannot be seen changing at frame
            // rate. Coalesced more coarsely than a drag or a fade, which do need it.
            self.Schedule(pointerMoved ? PointerCoalesceMs : 16);
        }
        catch (Exception ex) { _instance?.FailOpen(ex); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint window, uint message, nuint wparam, nint lparam)
    {
        try
        {
            if (_instance is { } self && window == self._control)
            {
                if (message == 0x10) { PostQuitMessage(0); return 0; }
                if (message == UpdateMessage)
                {
                    if (Interlocked.Exchange(ref self._pending, null) is { } settings)
                    {
                        self._settings = settings;
                        self.Configure(rebuildMasks: false);
                    }
                    return 0;
                }
                if (message == DispCtrl.Display.OledPreview.MessageId)
                {
                    self._previewPercent = (int)Math.Min(wparam, 100);
                    self._previewUntil = Environment.TickCount64 + 2000;
                    self.Tick();
                    return 0;
                }
                if (message == 0x7E) { Color.DisplayChanges.Raise(); self.Configure(); return 0; } // display topology/DPI change
                if (message == 0x2B1 && wparam is 7 or 8) // WM_WTSSESSION_CHANGE: lock, unlock
                {
                    self._locked = wparam == 7;
                    self.Tick();
                    return 0;
                }
                if (message == 0x218) // suspend/resume: never leave a stale mask on resume
                {
                    self._suspended = wparam == 4;
                    self.ForegroundChanged();
                    self.Tick();
                }
                if (message == 0x113) { self.Tick(); return 0; }
                // Somebody is at the keyboard or mouse. One tick shortly after,
                // however many reports arrive: a mouse can send a thousand a second.
                if (message == WmInput) self.Schedule(33);

            }
            if (message == 0x82) RemoveProp(window, "NonRudeHWND"); // WM_NCDESTROY
            if (message == 0x84) return -1; // HTTRANSPARENT
            if (message == 0x21) return 3; // MA_NOACTIVATE
        }
        catch (Exception ex) { _instance?.FailOpen(ex); }
        return DefWindowProc(window, message, wparam, lparam);
    }

    private void ForegroundChanged(bool prioritize = false)
    {
        _foreground = GetForegroundWindow();
        _focusedAt = Environment.TickCount64;
        _menuOpen = false;
        char* name = stackalloc char[256];
        int count = GetClassName(_foreground, name, 256);
        _foregroundClass = count > 0 ? new string(name, 0, count) : "";
        _excludedForeground = false;
        if (_excluded.Count > 0)
        {
            GetWindowThreadProcessId(_foreground, out uint pid);
            try { using var process = Process.GetProcessById((int)pid); _excludedForeground = _excluded.Contains(process.ProcessName); }
            catch (Exception) { _excludedForeground = true; }
        }
        if (prioritize && _settings.Global.Focus.PrioritizeNewWindows && IsAutomaticCandidate(_foreground, _foregroundClass))
        {
            _automaticWindow = _foreground;
            _automaticClass = _foregroundClass;
        }
        // Forgotten deliberately: the sweep that covers a dragged window's path
        // must not run between two different windows, or the first frame after a
        // switch cuts one hole spanning both of them.
        _lastActive = default;
        _movingUntil = 0;

        // Deliberately does not clear the masks. Snapping every one to fully
        // transparent here meant each switch between windows undimmed the whole
        // desk and faded it back, which is the flashing: the dim is meant to
        // stay put while only the hole moves to the new window.
        Tick();
    }

    /// <summary>Remembers an app window that appeared without taking foreground focus.</summary>
    private void WindowShown(nint window)
    {
        if (!_settings.Global.Focus.PrioritizeNewWindows || window == 0 || GetAncestor(window, 2) != window)
            return;

        string cls = ClassOf(window);
        if (!IsAutomaticCandidate(window, cls)) return;

        _automaticWindow = window;
        _automaticClass = cls;
        Tick();
    }

    private void Tick()
    {
        if (_timerMs != 0) KillTimer(_control, 1);
        _timerMs = 0;
        if (_masks.Count == 0) return;
        long now = Environment.TickCount64;
        FocusSettings focus = _settings.Global.Focus;
        OledCareSettings care = _settings.Global.OledCare;
        AwakeSettings awake = _settings.Global.Awake;
        // Which window counts as the one being used. Normally the focused one;
        // with follow-mouse, whatever sits under the pointer, resolved to its
        // top-level window so hovering a control does not cut a hole the size of
        // a button.
        nint hovered = 0;
        bool pointerKnown = GetCursorPos(out Point cursor) != 0;
        // Only focus mode cares which window is under the pointer. OLED care
        // wants the position alone, so a care-only tick skips two window
        // lookups and a class-name string.
        if (pointerKnown && focus.Enabled && (focus.FollowMouse || focus.KeepHoveredClear))
        {
            nint under = WindowFromPoint(cursor);
            nint root = under == 0 ? 0 : GetAncestor(under, 2); // GA_ROOT
            if (root != 0) hovered = root;
        }

        // Read once. Three separate calls asked the same window for the same
        // name and allocated a string each time, at up to sixty ticks a second.
        string hoveredClass = hovered == 0 ? "" : ClassOf(hovered);

        nint subject = _foreground;
        string subjectClass = _foregroundClass;
        if (focus.FollowMouse && hovered != 0)
        {
            {

                // Wallpaper and the taskbar are what lies between two windows,
                // so the pointer crossing them is travel rather than a change of
                // subject. Treating them as one undimmed the desk every time the
                // pointer left a window and dimmed it again on arrival, which is
                // the flashing between screens.
                //
                // Clicking the desktop is the exception, and it is the difference
                // between passing over it and meaning it: a click puts the shell
                // itself in front, so holding the last window then would leave the
                // desk dimmed around a window the user has deliberately left.
                bool clickedAway = IsShell(_foregroundClass);
                bool passingOver = IsShell(hoveredClass) && !clickedAway
                    && _subject != 0 && IsWindow(_subject) != 0;
                subject = passingOver ? _subject : hovered;
                subjectClass = passingOver ? _subjectClass : hoveredClass;
            }
        }


        // A newly opened or activated window may have been brought forward by
        // a launcher, script or accessibility tool while the pointer remained
        // over an older window. Prefer it until the next pointer movement, then
        // return naturally to the user's mouse-follow setting.
        if (focus.PrioritizeNewWindows && _automaticWindow != 0)
        {
            if (IsAutomaticCandidate(_automaticWindow, _automaticClass))
            {
                subject = _automaticWindow;
                subjectClass = _automaticClass;
            }
            else
            {
                _automaticWindow = 0;
                _automaticClass = "";
            }
        }

        // A different window entirely is a jump, not a drag. Without this the
        // sweep would union two unrelated windows for a frame and clear a hole
        // spanning both - most visible with follow-mouse, where the subject
        // changes every time the pointer crosses a window.
        bool subjectChanged = subject != _subject;
        if (subjectChanged) { _subject = subject; _lastActive = default; _movingUntil = 0; }
        _subjectClass = subjectClass;

        // A switch slides the cut-out from the old window to the new one. Only
        // on a switch: during a drag the sweep already tracks the window, and
        // easing that as well would put the hole permanently behind it.
        int crossMs = focus.CrossFadeWindows ? Math.Clamp(focus.FadeMs, 0, 2000) : 0;

        // Only ever one of them. A settings file written before these became a
        // single choice can still carry both, and sliding a shape while also
        // cross-fading it is the worst of the two rather than a blend.
        int easeMs = crossMs == 0 && focus.EaseBetweenWindows ? Math.Clamp(focus.FadeMs, 0, 2000) : 0;
        if (subjectChanged && easeMs > 0 && _holeShown.Width > 0 && _holeShown.Height > 0)
        {
            _holeFrom = _holeShown;
            _holeStarted = now;
        }

        bool valid = subject != 0 && IsIconic(subject) == 0 && GetWindowRect(subject, out _) != 0;
        DisplayRect active = valid ? FrameOf(subject) : default;

        // The other half of the pair: whichever of focused and hovered is not
        // already the subject. Kept clear alongside it rather than instead of it.
        nint companion = 0;
        if (focus.KeepHoveredClear)
        {
            nint other = focus.FollowMouse ? _foreground : hovered;
            string otherClass = other == _foreground ? _foregroundClass
                : other == hovered ? hoveredClass
                : other == 0 ? "" : ClassOf(other);

            if (other != 0 && other != subject && IsWindow(other) != 0 && IsIconic(other) == 0
                && !IsShell(otherClass))
                companion = other;
        }

        DisplayRect companionRect = companion == 0 ? default : FrameOf(companion);

        // What a display remembers is the window focused on it, which is not
        // always the subject: with follow-mouse the subject is whatever the
        // pointer is over, and a glance must not be recorded as that screen's
        // window and left standing there after the pointer has gone.
        bool focusedUsable = _foreground != 0 && !IsShell(_foregroundClass)
            && IsWindow(_foreground) != 0 && IsIconic(_foreground) == 0;

        DisplayRect focusedRect = !focusedUsable ? default
            : _foreground == subject ? active
            : FrameOf(_foreground);

        // The hovered window is cleared while it is hovered and no longer, so it
        // is held separately from whatever the display remembers.
        bool hoverClears = focus.FollowMouse || focus.KeepHoveredClear;
        bool hoveredUsable = hoverClears && hovered != 0 && !IsShell(hoveredClass)
            && IsWindow(hovered) != 0 && IsIconic(hovered) == 0;

        DisplayRect hoveredRect = !hoveredUsable ? default
            : hovered == subject ? active
            : hovered == _foreground ? focusedRect
            : FrameOf(hovered);

        // A revealed taskbar under the pointer is being used, so it is cut clear
        // like any other window rather than dimmed while it is aimed at.
        // Tied to keeping the hovered window clear, not to the setting that keeps
        // the taskbar strip permanently undimmed: the point is that the bar is a
        // window being aimed at, so it is cut clear exactly like any other.
        nint bar = focus.KeepHoveredClear && hovered != 0 && IsTaskbar(hoveredClass) && IsWindow(hovered) != 0 ? hovered : 0;
        DisplayRect barRect = bar == 0 ? default : FrameOf(bar);

        // Any change to the set of clear areas is a switch worth showing, not just
        // a change of subject: with the hovered window kept clear as well, its
        // highlight otherwise appeared and vanished with no transition at all
        // while the focused one faded.
        bool holesChanged = subjectChanged || companion != _companion || bar != _bar;
        _companion = companion;
        _bar = bar;
        // Watching something fullscreen is about the window in front, not the one
        // the pointer happens to rest over. Measuring the subject meant that with
        // follow-mouse, glancing at another window stopped a playing film counting
        // as fullscreen - and the idle rest was then free to black it out.
        DisplayRect frontRect = focusedUsable ? focusedRect : active;
        bool fullscreen = false;
        if (focusedUsable || valid)
        {
            // A foreach rather than Any(): the lambda captured the rect, so this
            // allocated a closure and a delegate every tick, for a list of two.
            foreach (Mask m in _masks)
            {
                if (!FocusGeometry.Covers(frontRect, m.Display.Bounds)) continue;
                fullscreen = true;
                break;
            }
        }
        bool shell = IsShell(subjectClass);
        bool focusAllowed = focus.Enabled && valid && !shell && !_menuOpen && !_excludedForeground && !_suspended
            && !(focus.PauseFullscreen && fullscreen);
        // The delay is there so a quick alt-tab through several windows does not
        // dim on each one. It applies to starting to dim, not to staying dimmed:
        // re-arming it on every switch dropped the dim and faded it back in, so
        // the setting meant to stop flicker was itself causing it.
        long remainingDelay = Math.Clamp(focus.DelayMs, 0, 10000) - (now - _focusedAt);
        bool settled = remainingDelay <= 0 || _dimming;

        // The overlay is a separate window, so it is always repainted a frame
        // behind the window it is cutting around: while a window is dragged, the
        // dim shows along the edge it has just left. Widening the hole while it
        // moves hides that frame, and it tightens again once the window settles.
        DisplayRect previous = _lastActive;
        bool hadPrevious = previous.Width > 0 && previous.Height > 0;
        if (active != previous)
        {
            _lastActive = active;
            if (hadPrevious) _movingUntil = now + MovingSettleMs;
        }
        bool moving = now < _movingUntil;
        int bleedDip = moving ? MovingBleedDip : HoleBleedDip;
        DisplayRect holeRect = moving ? FocusGeometry.Sweep(previous, active, SweepLimitPx) : active;

        bool sliding = easeMs > 0 && now - _holeStarted < easeMs;
        if (sliding) holeRect = FocusGeometry.Between(_holeFrom, holeRect, now - _holeStarted, easeMs);
        _holeShown = holeRect;
        // Only the rest features read this, and it is a syscall: skipped entirely
        // when neither idle care nor a manual rest is waiting on it.
        bool wantIdle = care.Enabled || _restPending || awake.DisplaysOffUtc is not null;
        uint idleMs = 0;
        bool inputKnown = false;
        if (wantIdle)
        {
            var input = new LastInput { Size = (uint)sizeof(LastInput) };
            inputKnown = GetLastInputInfo(ref input) != 0;
            idleMs = unchecked((uint)Environment.TickCount - input.Tick);
        }
        bool preview = care.Enabled && now < _previewUntil && !_suspended;
        bool frontMaximized = focusedUsable && IsZoomed(_foreground) != 0;
        bool frontHasCaption = focusedUsable && (GetWindowLongPtr(_foreground, -16) & 0x00C00000) == 0x00C00000;
        bool animating = false, anyRest = false, anyRestPending = false;

        // "Turn off displays". Which screens it covers is decided once, when
        // the delay runs out, so "except the one with the pointer" means where
        // the pointer is then - not where the quick panel was when it was asked.
        // Every display, not only OLED ones: this is about the person leaving,
        // not the panel's wear.
        DateTimeOffset? dimRequest = awake.DisplaysOffUtc == _dimDone ? null : awake.DisplaysOffUtc;
        if (dimRequest != _dimRequest)
        {
            // Switched off from outside - the panel, the CLI, the hotkey - rather
            // than woken: the pointer goes back the same way, and a session that
            // was to lock on wake locks here too, or the shortcut would get round it.
            if (_dimAppliedAt != 0) { RestorePointer(); LockIfAsked("switched back on"); }
            _dimRequest = dimRequest;
            _dimAppliedAt = 0;
            foreach (Mask m in _masks) { m.DimChosen = false; m.DimWoken = false; }
        }
        bool dimWaiting = false;
        uint dimWaitMs = 0;
        if (dimRequest is { } asked && _dimAppliedAt == 0)
        {
            if (DateTimeOffset.UtcNow >= asked.AddSeconds(Math.Clamp(awake.DisplaysOffDelaySeconds, 0, 30)))
            {
                _dimAppliedAt = now;
                _lockOnWake = awake.DisplaysOffLockOnWake;
                _dimCursorKnown = pointerKnown;
                (_dimCursorX, _dimCursorY) = (cursor.X, cursor.Y);
                int chosen = 0;
                foreach (Mask m in _masks)
                {
                    bool pointerHere = pointerKnown && Inside(m.Display.Bounds, cursor);
                    bool windowHere = focusedUsable
                        && FocusGeometry.Intersect(focusedRect, m.Display.Bounds) is { Width: > 0, Height: > 0 };
                    m.DimChosen = awake.DisplaysOffTarget switch
                    {
                        DisplaysOffTarget.ExceptMain => !m.Display.IsPrimary,
                        DisplaysOffTarget.ExceptPointer => !pointerHere,
                        DisplaysOffTarget.ExceptActiveWindow => !windowHere,
                        DisplaysOffTarget.OnlyMain => m.Display.IsPrimary,
                        _ => true,
                    };
                    if (m.DimChosen) chosen++;
                }
                Log.Write($"displays off: {chosen} of {_masks.Count} display(s), at {Math.Clamp(awake.DisplaysOffPercent, 0, 100)}%");

                // Into the bottom-right corner of the last display that went off.
                // Recorded as the pointer's position, so parking it is not taken
                // for the pointer moving there.
                Mask? corner = null;
                foreach (Mask m in _masks) if (m.DimChosen) corner = m;
                if (awake.DisplaysOffHidePointer && pointerKnown && corner is not null)
                {
                    // One pixel in from the corner: Stay active's nudge goes a pixel
                    // each way, and on the last pixel it could only come back.
                    var park = new Point { X = corner.Display.Bounds.Right - 2, Y = corner.Display.Bounds.Bottom - 2 };
                    if (SetCursorPos(park.X, park.Y))
                    {
                        _parkedFrom = cursor;
                        _parkedAt = park;
                        (_dimCursorX, _dimCursorY) = (park.X, park.Y);
                        cursor = park;
                    }
                }
            }
            else
            {
                dimWaiting = true;
                // One wake for the moment it is due, not five a second until then.
                dimWaitMs = (uint)Math.Clamp((asked.AddSeconds(Math.Clamp(awake.DisplaysOffDelaySeconds, 0, 30)) - DateTimeOffset.UtcNow).TotalMilliseconds + 5, 16, 30_000);
            }
        }
        bool dimPointerMoved = false;
        bool dimEndedByInput = false;
        if (_dimAppliedAt != 0)
        {
            // Stay active's nudge moves the pointer too, and is not somebody coming back.
            dimPointerMoved = pointerKnown && _dimCursorKnown && (cursor.X != _dimCursorX || cursor.Y != _dimCursorY)
                && now - Power.PowerService.LastNudgeTick > 500;
            _dimCursorKnown = pointerKnown;
            (_dimCursorX, _dimCursorY) = (cursor.X, cursor.Y);
            // Without pointer waking, any input ends it, after the same grace a
            // manual rest gets for the click that asked for it.
            // Stay active's own nudge is input too, and is not somebody coming back.
            long lastInput = now - idleMs;
            bool nudged = Math.Abs(lastInput - Power.PowerService.LastNudgeTick) < 500;
            dimEndedByInput = !awake.DisplaysOffWakeOnPointer && !nudged
                && !FocusGeometry.RestingByHand(true, now - _dimAppliedAt, idleMs, inputKnown);
        }

        foreach (Mask mask in _masks)
        {
            _settings.Monitors.TryGetValue(mask.Display.Token, out MonitorSettings? monitor);
            bool oled = monitor?.IsOled ?? (monitor?.OledDetected == true || mask.LibraryOled);
            if (mask.DimChosen && !mask.DimWoken
                && (dimEndedByInput || (awake.DisplaysOffWakeOnPointer && dimPointerMoved && Inside(mask.Display.Bounds, cursor))))
                mask.DimWoken = true;
            bool dimNow = mask.DimChosen && !mask.DimWoken;
            // A fullscreen window on a different panel must not suppress this
            // panel's idle protection. Ordinary maximized windows are not media
            // fullscreen, even when taskbar hiding reclaims the entire work area.
            bool panelFullscreen = focusedUsable && FocusGeometry.IsContentFullscreen(
                focusedRect, mask.Display.Bounds, frontMaximized, frontHasCaption);
            uint panelIdle = mask.IdleState.Update(care.Enabled && oled && monitor?.OledProtection == true
                && !_suspended && !(care.PauseFullscreen && panelFullscreen),
                monitor?.OledWakeOnPointerReturn == true, now, inputKnown, idleMs, care.IdleMinutes,
                pointerKnown, cursor.X, cursor.Y, mask.Display.Bounds);
            bool resting = FocusGeometry.RestingWhenIdle(care.Enabled, inputKnown, panelIdle,
                care.IdleMinutes, _suspended, care.PauseFullscreen && panelFullscreen);

            // Tracked per mask so a fresh request restarts the grace period
            // rather than inheriting the age of the one before it.
            DateTimeOffset? restUntil = monitor?.OledRestUntilUtc;
            if (restUntil != mask.RestUntil) { mask.RestUntil = restUntil; mask.RestSince = now; }

            bool restRequested = restUntil is { } until && until > DateTimeOffset.UtcNow;
            bool manualRest = FocusGeometry.RestingByHand(restRequested, now - mask.RestSince, idleMs, inputKnown);
            bool rest = ((preview || manualRest || resting) && oled && monitor?.OledProtection == true) || dimNow;
            anyRest |= rest;
            anyRestPending |= restRequested;
            DisplayRect intersection = FocusGeometry.Intersect(active, mask.Display.Bounds);
            bool onActiveMonitor = intersection.Width > 0 && intersection.Height > 0;

            // Which window this particular display is keeping clear. Shared with
            // every other display unless each is meant to hold its own, in which
            // case it is whatever was last used here.
            nint owner = subject;
            DisplayRect ownerRect = holeRect;
            if (focus.PerMonitorFocus)
            {
                // Claimed by focus alone. Hovering a window on another screen
                // used to make it that screen's window for good.
                if (focusedUsable)
                {
                    DisplayRect landed = FocusGeometry.Intersect(focusedRect, mask.Display.Bounds);
                    if (landed.Width > 0 && landed.Height > 0) mask.Owner = _foreground;
                }

                // A window may be shown without becoming the Win32 foreground
                // window. When the user opted into following those launches,
                // let it claim its display after the foreground assignment so
                // it also wins when both windows are on the same monitor.
                if (focus.PrioritizeNewWindows && subject == _automaticWindow && valid)
                {
                    DisplayRect landed = FocusGeometry.Intersect(active, mask.Display.Bounds);
                    if (landed.Width > 0 && landed.Height > 0) mask.Owner = subject;
                }

                owner = mask.Owner;
                // Subject first: while this display's window is the one being
                // used, its rect carries the drag sweep, and the plain frame
                // would hand back the trailing-edge flicker that fixed.
                if (owner == subject) ownerRect = holeRect;
                else if (owner == _foreground) ownerRect = focusedRect;
                else if (owner != 0 && IsWindow(owner) != 0 && IsIconic(owner) == 0) ownerRect = FrameOf(owner);
                else { owner = 0; ownerRect = default; }

                // A window carried off to another screen stops being this one's.
                if (owner != 0)
                {
                    DisplayRect still = FocusGeometry.Intersect(ownerRect, mask.Display.Bounds);
                    if (still.Width <= 0 || still.Height <= 0) { owner = 0; ownerRect = default; }
                }

                mask.Owner = owner;
            }

            bool ownerChanged = owner != mask.LastOwner;
            mask.LastOwner = owner;
            bool dim = focusAllowed && settled && (!focus.OledOnly || oled)
                && monitor?.FocusDimming != false
                // Every display dims when each holds its own window, because each
                // has something of its own to frame rather than one shared window.
                && (focus.PerMonitorFocus || focus.DimOtherMonitors || onActiveMonitor);
            int wanted = focus.ScaleWithBrightness
                ? FocusGeometry.ScaledDim(focus.DimPercent, PanelLevel(monitor))
                : focus.DimPercent;
            int restDim = preview ? _previewPercent : manualRest ? 100
                : dimNow ? Math.Max(Math.Clamp(awake.DisplaysOffPercent, 0, 100), resting ? care.DimAtIdle(panelIdle) : 0)
                : care.DimAtIdle(panelIdle);
            double target = rest ? FocusGeometry.Alpha(restDim) : dim ? FocusGeometry.Alpha(wanted) : 0;
            DisplayRect area = !rest && focus.KeepTaskbarVisible ? mask.Display.WorkArea : mask.Display.Bounds;
            // The hole has to outlive the dim it is cut from. Dropping it the
            // instant focus was lost meant the window that had been clear was
            // suddenly covered by a mask still at full alpha, so it darkened
            // before everything faded out - read as the dim jumping on and then
            // washing away. While the mask is still visible, the cut-out stays.
            DisplayRect? hole = rest ? null
                : dim ? (focus.PerMonitorFocus && owner == 0 ? null : ownerRect)
                : mask.Alpha >= 0.5 ? mask.LiveHole
                : null;

            // While each display holds its own window, the pointer's window is
            // the extra one rather than the pair's other half - and only for as
            // long as the pointer is on it.
            bool hoverHere = hoveredUsable
                && FocusGeometry.Intersect(hoveredRect, mask.Display.Bounds) is { Width: > 0, Height: > 0 }
                && hovered != owner;

            DisplayRect? hole2 = rest ? null
                : dim ? (focus.PerMonitorFocus
                            ? (hoverHere ? hoveredRect : null)
                            : (companion == 0 ? null : companionRect))
                : mask.Alpha >= 0.5 ? mask.LiveHole2
                : null;

            // Shaped like the others on purpose: while dimming is on, no taskbar
            // under the pointer means no hole. Folding that into the condition
            // above let it fall through to the keep-while-fading branch, so the
            // bar stayed clear for good once it had been pointed at.
            bool barHere = bar != 0
                && FocusGeometry.Intersect(barRect, mask.Display.Bounds) is { Width: > 0, Height: > 0 };

            DisplayRect? hole3 = rest ? null
                : dim ? (barHere ? barRect : null)
                : mask.Alpha >= 0.5 ? mask.LiveHole3
                : null;

            // A switch hands the old cut-out to the other layer to fade out on,
            // and cuts the new one instantly on this layer. The shape never
            // travels - only the two brightnesses move.
            if ((holesChanged || ownerChanged) && crossMs > 0 && dim && !rest && mask.LiveHole is not null && mask.Alpha >= 0.5)
            {
                (mask.Live, mask.Ghost) = (mask.Ghost, mask.Live);
                (mask.LiveHole, mask.GhostHole) = (mask.GhostHole, mask.LiveHole);
                (mask.LiveHole2, mask.GhostHole2) = (mask.GhostHole2, mask.LiveHole2);
                (mask.LiveHole3, mask.GhostHole3) = (mask.GhostHole3, mask.LiveHole3);
                (mask.LiveArea, mask.GhostArea) = (mask.GhostArea, mask.LiveArea);
                (mask.LiveBleed, mask.GhostBleed) = (mask.GhostBleed, mask.LiveBleed);
                (mask.LiveVisible, mask.GhostVisible) = (mask.GhostVisible, mask.LiveVisible);
                (mask.LiveApplied, mask.GhostApplied) = (mask.GhostApplied, mask.LiveApplied);
                mask.CrossStarted = now;
                mask.CrossMs = crossMs;
            }

            if (mask.LiveArea != area || mask.LiveHole != hole || mask.LiveHole2 != hole2
                || mask.LiveHole3 != hole3 || mask.LiveBleed != bleedDip)
            {
                SetRegion(mask.Live, mask.Display, area, hole, hole2, hole3, bleedDip);
                mask.LiveArea = area;
                mask.LiveHole = hole;
                mask.LiveHole2 = hole2;
                mask.LiveHole3 = hole3;
                mask.LiveBleed = bleedDip;
            }

            if (Math.Abs(target - mask.Target) > 0.1)
            {
                mask.From = mask.Alpha;
                mask.Target = target;
                mask.Started = now;
                mask.Duration = preview ? 120 : Math.Clamp(rest || !focus.Enabled ? care.FadeMs : focus.FadeMs, 0, 2000);
            }

            mask.Alpha = FocusGeometry.Fade(mask.From, mask.Target, now - mask.Started, mask.Duration);
            animating |= Math.Abs(mask.Alpha - target) > 0.5;

            bool crossing = mask.CrossMs > 0 && now - mask.CrossStarted < mask.CrossMs;
            double ghostAlpha = crossing
                ? FocusGeometry.Fade(mask.Alpha, 0, now - mask.CrossStarted, mask.CrossMs)
                : 0;
            double liveAlpha = crossing ? FocusGeometry.Overlay(mask.Alpha, ghostAlpha) : mask.Alpha;
            animating |= crossing;

            Apply(mask.Live, liveAlpha, ref mask.LiveVisible, ref mask.LiveApplied);
            Apply(mask.Ghost, ghostAlpha, ref mask.GhostVisible, ref mask.GhostApplied);
        }
        // Cleared whenever dimming is not allowed, so the next window that does
        // qualify waits out the delay again rather than snapping straight to it -
        // except for a visit to the shell, which is an interlude rather than a
        // fresh start. Clearing it there meant clicking the desktop or taskbar
        // and coming back sat at full brightness for the delay and then faded,
        // which reads as the dim washing out and returning.
        // Every dimmed screen has woken, or there was none to dim: the request
        // is done, and the switch in the panel goes off with it.
        if (_dimAppliedAt != 0 && _dimRequest is { } finished)
        {
            bool stillDim = false, anyWoken = false;
            foreach (Mask m in _masks) { stillDim |= m.DimChosen && !m.DimWoken; anyWoken |= m.DimChosen && m.DimWoken; }
            // Locking waits for nobody: the first display woken ends it for all.
            if (!stillDim || (_lockOnWake && anyWoken)) FinishDim(finished);
        }

        WatchInput(anyRest);

        bool interlude = shell || _menuOpen;
        _dimming = (focusAllowed && settled) || (interlude && _dimming);
        _restPending = anyRestPending;

        animating |= sliding;

        // Nothing drawn here shows on the lock screen, and a rest already in
        // place stays put. Polling for input at ten a second behind it, which a
        // screen resting for an absent person did all night, waits for unlock.
        if (_locked) return;

        if (animating) Schedule(16);
        // Every frame while it moves, not once when the drag is judged over:
        // scheduling the settle deadline instead meant the hole was re-cut about
        // eight times a second during a drag, which is the lag itself.
        else if (moving) Schedule(16);
        else if (focusAllowed && !settled) Schedule((uint)Math.Min(Math.Max(remainingDelay, 1), 1000));
        // Only when the raw-input sink could not be registered. With it, a still
        // pointer schedules nothing whatsoever and the next tick comes from the
        // mouse itself.
        else if (!_pointerEvents && focus.Enabled && focus.FollowMouse) Schedule(FollowMousePollMs);
        else if (!_pointerEvents && focus.Enabled && focus.KeepHoveredClear) Schedule(PointerPollMs);

        // Belt and braces behind the cursor events. They arrive while the pointer
        // is moving, so the last one can describe a position it has already left;
        // a slow sweep afterwards settles whatever that missed. Two wake-ups a
        // second is nothing next to the polling this replaced.
        else if (focus.Enabled && (focus.FollowMouse || focus.KeepHoveredClear)) Schedule(SettlePollMs);
        // A manual rest can be running with idle care switched off entirely.
        // Waking only for care.Enabled left that black screen with nothing
        // scheduled to end it, so it stayed until some unrelated window event.
        // With input watched, a still desk costs one wake a second - enough for a
        // second rest stage coming due, or a pointer moved by software, which
        // raw input does not see. Without it, the old poll.
        else if (anyRest) Schedule(_inputSink ? 1000u : 100u);
        else if (dimWaiting) Schedule(dimWaitMs);
        else if (care.Enabled || anyRestPending) Schedule(1000);
    }

    private static bool Inside(DisplayRect r, Point p) => p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;

    private void LockIfAsked(string how)
    {
        if (!_lockOnWake) return;
        _lockOnWake = false;
        bool locked = LockWorkStation();
        Log.Write(locked ? $"displays off: {how}; computer locked" : $"displays off: {how}; locking was refused ({Marshal.GetLastPInvokeError()})");
    }

    /// <summary>Puts a parked pointer back, unless the mouse has already taken it somewhere of its own.</summary>
    private void RestorePointer()
    {
        if (_parkedFrom is { } from && _parkedAt is { } at
            && GetCursorPos(out Point now) != 0 && now.X == at.X && now.Y == at.Y)
            _ = SetCursorPos(from.X, from.Y);
        _parkedFrom = _parkedAt = null;
    }

    /// <summary>Marks a "Turn off displays" request done, here and in the settings file.</summary>
    /// <remarks>
    /// Written back so the switch in the panel and the CLI read it as off. Only
    /// cleared if it is still the same request: a new one made meanwhile stands.
    /// </remarks>
    private void FinishDim(DateTimeOffset request)
    {
        RestorePointer();
        LockIfAsked("woken");
        _dimDone = request;
        _dimRequest = null;
        _dimAppliedAt = 0;
        foreach (Mask m in _masks) { m.DimChosen = false; m.DimWoken = false; }
        // Off this thread: it is the one drawing the fade back in, and a read
        // and a rename of the settings file in the middle of it stuttered the
        // first frames.
        _ = Task.Run(() =>
        {
            try
            {
                DispCtrlSettings saved = SettingsStore.Load();
                if (saved.Global.Awake.DisplaysOffUtc == request)
                {
                    saved.Global.Awake.DisplaysOffUtc = null;
                    SettingsStore.Save(saved);
                }
            }
            catch (Exception ex) { Log.Write($"displays off: could not clear the request: {ex.Message}"); }
        });
        Log.Write("displays off: every display is back on");
    }

    /// <summary>Corner radius Windows 11 draws a window with, in DIP.</summary>
    /// <remarks>
    /// The hole was a plain rectangle, so the dim stayed in the four corner arcs
    /// of a rounded window and read as a dark outline around it. Matching DWM's
    /// own radius is what makes the cut disappear.
    /// </remarks>
    private const int CornerRadiusDip = 8;

    /// <summary>
    /// How far the hole is grown past the window, in DIP.
    /// </summary>
    /// <remarks>
    /// The frame bounds and the region are rounded independently, so a hole cut
    /// exactly to size can leave a one-pixel seam of dim along an edge. A pixel
    /// of overlap costs nothing and removes it.
    /// </remarks>
    private const int HoleBleedDip = 1;

    /// <summary>How far the hole is grown while the window is being moved, in DIP.</summary>
    /// <remarks>
    /// Sized to cover a frame of travel at a normal drag speed. Too small and
    /// the dim still flickers along the trailing edge; too large and the gap
    /// around a dragged window becomes the thing you notice instead.
    /// </remarks>
    private const int MovingBleedDip = 24;

    /// <summary>How long after the last movement the wide hole is kept.</summary>
    /// <remarks>
    /// Long enough to cover the gap between two mouse-move events mid-drag, so
    /// the hole does not tighten and widen repeatedly during one drag, which
    /// would look worse than the lag it is hiding.
    /// </remarks>
    private const long MovingSettleMs = 120;

    /// <summary>How far a window may travel in one frame and still be swept, in pixels.</summary>
    /// <remarks>
    /// Beyond this it is a jump rather than a drag — a snap to an edge, or a move
    /// to another monitor — and clearing everything in between would undim a band
    /// across the screen for a frame, which is worse than the lag.
    /// </remarks>
    private const int SweepLimitPx = 300;

    /// <summary>How often the pointer is sampled when focus follows it.</summary>
    /// <remarks>
    /// Two syscalls and a hit test, so the cost is negligible next to the gamma
    /// and taskbar work this process already does on a timer - but it only runs
    /// while the option is on.
    /// </remarks>
    private const uint FollowMousePollMs = 40;

    /// <summary>How often the pointer is sampled for the things that merely notice it.</summary>
    /// <remarks>
    /// Keeping the hovered window or a revealed taskbar clear is not tracking
    /// the cursor, so it does not need the rate that following it does. A sixth
    /// of the wake-ups, and still faster than a taskbar slides out.
    /// </remarks>
    private const uint PointerPollMs = 240;

    /// <summary>A slow sweep that catches anything the cursor events did not.</summary>
    private const uint SettlePollMs = 500;

    /// <summary>The finest the hovered-window checks are worth repeating at.</summary>
    private const uint PointerCoalesceMs = 50;

    /// <summary>The taskbar, as opposed to the wallpaper behind everything.</summary>
    /// <remarks>
    /// Both are shell surfaces, but only one is a thing people aim at and use.
    /// Keeping the work area clear covers a docked bar and does nothing at all
    /// for an auto-hidden one, because the work area then spans the whole screen
    /// and the bar slides out over the dim.
    /// </remarks>
    private static bool IsTaskbar(string cls) =>
        cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    private static bool IsShell(string cls) =>
        cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
            or "MultitaskingViewFrame" or "XamlExplorerHostIslandWindow" or "Windows.UI.Core.CoreWindow";

    private static bool IsAutomaticCandidate(nint window, string cls)
    {
        if (window == 0 || IsWindow(window) == 0 || IsIconic(window) != 0 || IsShell(cls)
            || cls is "#32768" or "tooltips_class32" or ClassName)
            return false;

        if (GetWindowRect(window, out Rect rect) == 0) return false;
        return rect.Right - rect.Left >= 120 && rect.Bottom - rect.Top >= 80;
    }

    private static string ClassOf(nint window)
    {
        char* name = stackalloc char[256];
        int count = GetClassName(window, name, 256);
        return count > 0 ? new string(name, 0, count) : "";
    }

    /// <summary>
    /// How bright unison brightness is expected to be running one panel, 0-100.
    /// </summary>
    /// <remarks>
    /// Taken from the settings rather than from the panel. Asking the hardware
    /// would mean a DDC/CI round trip per monitor on a timer, and this process
    /// deliberately never polls DDC for protection - the numbers that decide the
    /// brightness are all here anyway.
    /// </remarks>
    private int PanelLevel(MonitorSettings? monitor)
    {
        GlobalSettings global = _settings.Global;
        if (!global.UnisonBrightness) return 100;

        int level = Math.Clamp(global.UnisonLevel, 0, 100);
        if (global.UnisonCalibrated && monitor is { HasBrightnessRange: true })
            return Math.Clamp(monitor.BrightnessFloor
                + ((monitor.BrightnessCeiling - monitor.BrightnessFloor) * level / 100), 0, 100);

        return level;
    }

    /// <summary>
    /// Pushes one layer's opacity, and only when it has actually moved.
    /// </summary>
    /// <remarks>
    /// Called for both layers of every display on every tick, so the guard is
    /// the difference between four syscalls a frame and none at all. Nothing
    /// changes for most ticks: the pointer is still, the windows are still, and
    /// the only reason the tick ran is that something had to check.
    /// </remarks>
    private static void Apply(nint window, double alpha, ref bool visible, ref int applied)
    {
        int wanted = (int)Math.Clamp(Math.Round(alpha), 0, 255);
        if (wanted != applied)
        {
            SetLayeredWindowAttributes(window, 0, (byte)wanted, 2);
            applied = wanted;
        }

        bool show = wanted >= 1;
        if (show == visible) return;

        if (show) SetWindowPos(window, -1, 0, 0, 0, 0, 0x53); // show, no move/size/activate
        else ShowWindow(window, 0);
        visible = show;
    }

    private DisplayRect FrameOf(nint window)
    {
        Rect frame = default;
        if (DwmGetWindowAttribute(window, 9, out frame, (uint)sizeof(Rect)) != 0)
            GetWindowRect(window, out frame);

        return new DisplayRect(frame.Left, frame.Top, frame.Right, frame.Bottom);
    }

    private static void SetRegion(nint window, DisplayInfo display, DisplayRect area,
                                  DisplayRect? hole, DisplayRect? second, DisplayRect? third, int bleedDip)
    {
        DisplayRect bounds = display.Bounds;
        nint region = CreateRectRgn(area.Left - bounds.Left, area.Top - bounds.Top, area.Right - bounds.Left, area.Bottom - bounds.Top);
        if (region == 0) throw new InvalidOperationException("Cannot allocate dim region.");
        double scale = display.Scale <= 0 ? 1 : display.Scale;
        int bleed = (int)Math.Round(bleedDip * scale);
        int radius = (int)Math.Round(CornerRadiusDip * scale * 2);

        // Two of them when the focused and hovered windows are both being kept
        // clear, so this subtracts whichever it is given rather than exactly one.
        void Cut(DisplayRect h)
        {
            nint cut = CreateRoundRectRgn(
                h.Left - bounds.Left - bleed, h.Top - bounds.Top - bleed,
                h.Right - bounds.Left + bleed, h.Bottom - bounds.Top + bleed,
                radius, radius);
            if (cut == 0) { DeleteObject(region); throw new InvalidOperationException("Cannot allocate focus region."); }

            int combined = CombineRgn(region, region, cut, 4); // RGN_DIFF
            DeleteObject(cut);
            if (combined == 0) { DeleteObject(region); throw new InvalidOperationException("Cannot subtract focus region."); }
        }

        if (hole is { } first) Cut(first);
        if (second is { } other && other != hole) Cut(other);
        if (third is { } bar && bar != hole && bar != second) Cut(bar);
        // Windows owns the region only after a successful SetWindowRgn.
        if (SetWindowRgn(window, region, 1) == 0) { DeleteObject(region); throw new InvalidOperationException("Cannot update focus region."); }
    }

    private void Schedule(uint ms)
    {
        if (_timerMs != 0 && _timerMs <= ms) return;
        SetTimer(_control, 1, Math.Max(16, ms), 0);
        _timerMs = Math.Max(16, ms);
    }
    private void ClearMasks()
    {
        if (_control != 0) KillTimer(_control, 1);
        _timerMs = 0;
        foreach (Mask mask in _masks) { DestroyWindow(mask.Live); DestroyWindow(mask.Ghost); }
        _masks.Clear();
    }
    private void ClearHooks() { foreach (nint hook in _hooks) UnhookWinEvent(hook); _hooks.Clear(); _hookState = null; }
    private void FailOpen(Exception ex) { ClearMasks(); ClearHooks(); Log.Write($"Display protection cleared: {ex.Message}"); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_control != 0) PostMessage(_control, 0x10, 0, 0);
        _thread.Join();
        _ready.Dispose();
    }
}
