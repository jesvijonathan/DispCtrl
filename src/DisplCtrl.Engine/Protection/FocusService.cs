using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DisplCtrl.Core.Displays;
using DisplCtrl.Core.Settings;
using static DisplCtrl.Engine.Protection.OverlayNative;

namespace DisplCtrl.Engine.Protection;

/// <summary>One sleeping message pump; one cached black overlay per monitor.</summary>
internal sealed unsafe class FocusService : IDisposable
{
    private const uint UpdateMessage = 0x8001;


    private const string ClassName = "DisplCtrl.ProtectionOverlay";
    private static FocusService? _instance;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private DisplCtrlSettings _settings;
    private DisplCtrlSettings? _pending;
    private readonly List<nint> _hooks = [];
    private readonly List<Mask> _masks = [];
    private HashSet<string> _excluded = [];
    private nint _control, _foreground;
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
        public double Alpha, From, Target;
        public long Started;
        public int Duration;
        public long CrossStarted;
        public int CrossMs;
    }

    public FocusService(DisplCtrlSettings settings)
    {
        _settings = settings;
        _thread = new Thread(Pump) { IsBackground = true, Name = "Display focus" };
        _thread.Start();
        _ready.Wait();
    }

    public void Update(DisplCtrlSettings settings)
    {
        Volatile.Write(ref _pending, settings);
        if (_control != 0) PostMessage(_control, UpdateMessage, 0, 0);
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
            _control = CreateWindowEx(0x08000080, ClassName, "DisplCtrl protection service", 0x80000000,
                0, 0, 0, 0, 0, 0, GetModuleHandle(null), 0);
            if (_control == 0) throw new InvalidOperationException("Cannot create protection message window.");
            Configure();
            _ready.Set();
            while (GetMessage(out Message message, 0, 0, 0) > 0) DispatchMessage(ref message);
        }
        catch (Exception ex) { Log.Write($"Display protection stopped: {ex.Message}"); }
        finally
        {
            ClearMasks();
            ClearHooks();
            if (_control != 0) DestroyWindow(_control);
            _control = 0;
            _instance = null;
            _ready.Set();
        }
    }

    private void Configure()
    {
        _excluded = _settings.Global.Focus.Exclusions();
        ClearHooks();
        ClearMasks();
        bool active = _settings.Global.Focus.Enabled || _settings.Global.OledCare.Enabled;
        if (active)
        {
            // Out-of-context notifications: no DLL injection, no keyboard hook.
            AddHook(3, 7); // foreground and menu start/end
            AddHook(0x800B, 0x800B); // location; filtered to foreground below
            AddHook(0x0016, 0x0017); // minimize start/end
            foreach (DisplayInfo d in DisplayRegistry.Enumerate())
            {
                nint live = CreateWindowEx(0x080800A8, ClassName, "DisplCtrl dim overlay", 0x80000000,
                    d.Bounds.Left, d.Bounds.Top, d.Bounds.Width, d.Bounds.Height, 0, 0, GetModuleHandle(null), 0);
                nint ghost = CreateWindowEx(0x080800A8, ClassName, "DisplCtrl dim overlay", 0x80000000,
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
        ForegroundChanged();
        Tick();
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
            if (pointerMoved) self._pointerEvents = true;
            if (evt == 3) self.ForegroundChanged();
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
                    if (Interlocked.Exchange(ref self._pending, null) is { } settings) self._settings = settings;
                    self.Configure();
                    return 0;
                }
                if (message == 0x7E) { self.Configure(); return 0; } // display topology/DPI change
                if (message == 0x218) // suspend/resume: never leave a stale mask on resume
                {
                    self._suspended = wparam == 4;
                    self.ForegroundChanged();
                    self.Tick();
                }
                if (message == 0x113) { self.Tick(); return 0; }

            }
            if (message == 0x82) RemoveProp(window, "NonRudeHWND"); // WM_NCDESTROY
            if (message == 0x84) return -1; // HTTRANSPARENT
            if (message == 0x21) return 3; // MA_NOACTIVATE
        }
        catch (Exception ex) { _instance?.FailOpen(ex); }
        return DefWindowProc(window, message, wparam, lparam);
    }

    private void ForegroundChanged()
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

    private void Tick()
    {
        if (_timerMs != 0) KillTimer(_control, 1);
        _timerMs = 0;
        if (_masks.Count == 0) return;
        long now = Environment.TickCount64;
        FocusSettings focus = _settings.Global.Focus;
        OledCareSettings care = _settings.Global.OledCare;
        // Which window counts as the one being used. Normally the focused one;
        // with follow-mouse, whatever sits under the pointer, resolved to its
        // top-level window so hovering a control does not cut a hole the size of
        // a button.
        nint hovered = 0;
        if (GetCursorPos(out Point cursor) != 0)
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
        bool wantIdle = care.Enabled || _restPending;
        uint idleMs = 0;
        bool inputKnown = false;
        if (wantIdle)
        {
            var input = new LastInput { Size = (uint)sizeof(LastInput) };
            inputKnown = GetLastInputInfo(ref input) != 0;
            idleMs = unchecked((uint)Environment.TickCount - input.Tick);
        }
        bool resting = FocusGeometry.RestingWhenIdle(care.Enabled, inputKnown, idleMs,
            care.IdleMinutes, _suspended, care.PauseFullscreen && fullscreen);
        bool animating = false, anyRest = false, anyRestPending = false;
        foreach (Mask mask in _masks)
        {
            _settings.Monitors.TryGetValue(mask.Display.Token, out MonitorSettings? monitor);
            bool oled = monitor?.TreatAsOled == true;

            // Tracked per mask so a fresh request restarts the grace period
            // rather than inheriting the age of the one before it.
            DateTimeOffset? restUntil = monitor?.OledRestUntilUtc;
            if (restUntil != mask.RestUntil) { mask.RestUntil = restUntil; mask.RestSince = now; }

            bool restRequested = restUntil is { } until && until > DateTimeOffset.UtcNow;
            bool manualRest = FocusGeometry.RestingByHand(restRequested, now - mask.RestSince, idleMs, inputKnown);
            bool rest = (manualRest || resting) && oled && monitor?.OledProtection == true;
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
            double target = rest ? FocusGeometry.Alpha(care.DimPercent) : dim ? FocusGeometry.Alpha(wanted) : 0;
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
                mask.Duration = Math.Clamp(rest ? care.FadeMs : focus.FadeMs, 0, 2000);
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
        bool interlude = shell || _menuOpen;
        _dimming = (focusAllowed && settled) || (interlude && _dimming);
        _restPending = anyRestPending;

        animating |= sliding;

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
        else if (anyRest) Schedule(100);
        else if (care.Enabled || anyRestPending) Schedule(1000);
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
    private void ClearHooks() { foreach (nint hook in _hooks) UnhookWinEvent(hook); _hooks.Clear(); }
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
