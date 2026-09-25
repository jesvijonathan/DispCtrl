using System.Diagnostics;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DispCtrl.Engine.Taskbar;

/// <summary>Which side of its monitor a taskbar is docked to.</summary>
internal enum Edge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>
/// Hides the taskbar on individually chosen monitors, revealing it when the
/// cursor reaches that monitor's edge.
/// </summary>
/// <remarks>
/// Windows 11's auto-hide is a single global switch — it hides every taskbar or
/// none. This repositions the taskbar windows directly instead, so one monitor
/// can be clean while another keeps a normal bar.
/// <para>
/// The bar is parked <em>completely</em> off the monitor rather than leaving
/// the 1px sliver native auto-hide uses. Native needs that sliver because the
/// window itself has to catch the hover; reveal here is driven by cursor
/// position, so nothing need be lit. On OLED that sliver is a permanently-lit
/// static line — measured at #434343 across 75% of the width on the panel this
/// was built against, which is precisely the burn-in case DispCtrl exists to avoid.
/// </para>
/// </remarks>
internal sealed class TaskbarManager
{
    private const string SecondaryClass = "Shell_SecondaryTrayWnd";
    private const int RescanIntervalMs = 1000;
    private const int AnimFrameMs = 8;

    /// <summary>A bar taller/wider than this is not a taskbar — refuse to move it.</summary>
    private const int MaxPlausibleThickness = 400;

    /// <summary>
    /// Settled ticks a bar may ignore its target before it is given up on.
    /// </summary>
    /// <remarks>
    /// The primary taskbar cannot be moved from outside explorer: SetWindowPos
    /// reports success and explorer snaps it straight back, measured within
    /// 120ms. It is no longer adopted at all, but a secondary bar Explorer
    /// decides to hold behaves the same way. Without this cap the engine would
    /// call SetWindowPos forever at the armed poll rate, burning CPU on a fight
    /// it cannot win — so a bar that refuses to stay put is dropped, once,
    /// with an explanation.
    /// </remarks>
    private const int StubbornLimit = 25;

    /// <summary>
    /// Assumed ceiling on cursor speed, in pixels per second, used to work out
    /// how long it is safe to sleep for.
    /// </summary>
    /// <remarks>
    /// Deliberately generous — a hard flick across a high-DPI panel is fast,
    /// and under-estimating would add latency to a reveal. Over-estimating only
    /// costs a few extra wake-ups.
    /// </remarks>
    private const int MaxCursorSpeedPxPerSec = 6000;

    /// <summary>
    /// Windows belonging to the taskbar or its flyouts. While one of these has
    /// focus the bar stays out, so clicking a tray icon or the overflow chevron
    /// does not yank the bar away mid-interaction.
    /// </summary>
    private static readonly HashSet<string> StickyClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "TopLevelWindowForOverflowXamlIsland",
        "Xaml_WindowedPopupClass", "Windows.UI.Core.CoreWindow", "ControlCenterWindow",
        "NotifyIconOverflowWindow", "TrayNotifyWnd",
    };

    private sealed class Bar
    {
        public required HWND Hwnd { get; set; }
        public required string MonitorToken { get; init; }
        public required string MonitorLabel { get; init; }
        public DisplayRect Monitor { get; set; }
        public Edge Edge { get; set; }
        public int Thickness { get; set; }
        public bool ReclaimWorkArea { get; set; }

        /// <summary>
        /// Another monitor lies against the edge the bar hides past, so the
        /// strip just beyond its own monitor is on that one.
        /// </summary>
        public bool Blocked { get; set; }

        /// <summary>A window region is clipping a blocked bar's slide to its own monitor.</summary>
        public bool Clipped { get; set; }

        /// <summary>The far side of the whole desktop along the hiding axis, used when <see cref="Blocked"/>.</summary>
        public int Beyond { get; set; }

        public bool Shown { get; set; } = true;
        public long HideAtTick { get; set; }

        /// <summary>Consecutive settled ticks where the bar ignored its target.</summary>
        public int Stubborn { get; set; }

        /// <summary>Set once a bar has proven it cannot be moved. See <see cref="StubbornLimit"/>.</summary>
        public bool Unmanageable { get; set; }

        // Slide state. Native auto-hide eases the bar in and out rather than
        // snapping it, so the target is approached over AnimMs.
        public int TargetPos { get; set; }
        public bool Placed { get; set; }
        public bool Animating { get; set; }
        public long AnimStart { get; set; }
        public double AnimFrom { get; set; }

        // Explorer normally keeps a visible taskbar above ordinary windows,
        // but a foreground app can temporarily disturb that order while the
        // bar is being revealed. Reassert it at a low cadence while visible;
        // doing it every cursor tick would churn the shell's z-order.
        public long NextZOrderTick { get; set; }

        public bool Horizontal => Edge is Edge.Bottom or Edge.Top;
    }

    private DispCtrlSettings _settings;
    private DispCtrlSettings? _pendingSettings;
    private readonly bool _trace;
    private List<Bar> _bars = [];
    private string _lastBarKey = string.Empty;

    /// <summary>Set by <see cref="Color.DisplayChanges"/> when the displays have changed and settled.</summary>
    private int _layoutChanged = 1;

    /// <summary>Secondary displays set to hide their bar, as of the last discovery.</summary>
    private int _wantedBars;

    private void OnDisplaysSettled(Color.DisplayChange change)
    {
        Interlocked.Exchange(ref _layoutChanged, 1);
        _wake.Set();
    }

    /// <summary>Explorer's taskbar exists now: discover at once.</summary>
    /// <remarks>
    /// At sign-in the engine is often up before Explorer's bars, and with no bar
    /// found the loop looked again once a second: hiding, glass and opacity took
    /// hold up to a second after the taskbar appeared, and again after every
    /// Explorer restart.
    /// </remarks>
    public void ShellReady()
    {
        Interlocked.Exchange(ref _layoutChanged, 1);
        _wake.Set();
    }
    private int _lastBarCount = -1;
    private bool _highRes;

    public TaskbarManager(DispCtrlSettings settings, bool trace = false)
    {
        _settings = settings;
        _trace = trace;
    }

    /// <summary>
    /// Hands the loop a new configuration, to be picked up on its next tick.
    /// </summary>
    /// <remarks>
    /// Called from a file-watcher thread when the settings file changes, so the
    /// new value is parked rather than applied in place — swapping settings
    /// underneath a tick that is midway through reading them would be a data
    /// race, and the loop is the only thread allowed to touch bar state.
    /// </remarks>
    public void ApplySettings(DispCtrlSettings settings)
    {
        Volatile.Write(ref _pendingSettings, settings);
        // The loop may be in a long sleep with nothing to watch; a new
        // configuration is exactly what it is waiting for.
        _wake.Set();
    }

    private readonly AutoResetEvent _wake = new(false);
    private WaitHandle[]? _waitOn;

    public void Run(CancellationToken ct)
    {
        using var appearance = new TaskbarAppearance();
        using var glass = new TaskbarGlassController();
        var clock = Stopwatch.StartNew();
        long nextRescan = 0;

        Log.Write("taskbar manager started");
        Color.DisplayChanges.Settled += OnDisplaysSettled;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                long now = clock.ElapsedMilliseconds;

                if (Interlocked.Exchange(ref _pendingSettings, null) is { } incoming)
                {
                    AdoptSettings(incoming);
                    nextRescan = now;   // re-discover immediately
                }
                if (Volatile.Read(ref _layoutChanged) != 0) nextRescan = now;

                if (now >= nextRescan)
                {
                    appearance.Update(_settings.Global.TaskbarOpacity);
                    glass.Update(_settings.Global);
                    nextRescan = now + RescanIntervalMs;
                    Rescan();
                }

                // No bar to move means no cursor to watch: sleep to the next
                // rescan, and with nothing at all to keep up - no bar, no glass,
                // no opacity - until the settings change. This used to poll at
                // the idle rate, ten wakes a second on a desk managing nothing.
                if (_bars.Count == 0)
                {
                    Sleep(NothingToMaintain() ? Timeout.Infinite : (int)Math.Max(1, nextRescan - now), ct);
                    continue;
                }

                // Fails on the secure desktop: locked, UAC, sign-in. Nothing can
                // be revealed there, so check once a second rather than ten times.
                if (!PInvoke.GetCursorPos(out System.Drawing.Point cur))
                {
                    Sleep(RescanIntervalMs, ct);
                    continue;
                }

                bool anyShown = false;
                foreach (Bar b in _bars) if (b.Shown) { anyShown = true; break; }

                // Only asked while a bar is out, because that is the only time
                // stickiness can matter. Two Win32 calls saved on every idle
                // tick, which is the common case by a wide margin.
                bool sticky = anyShown && IsStickyForeground();

                bool anyAnimating = false;
                int nearestEdge = int.MaxValue;

                foreach (Bar b in _bars)
                    UpdateBar(b, cur, now, sticky, ref anyAnimating, ref nearestEdge);

                Sleep(ChooseInterval(anyAnimating, anyShown, nearestEdge), ct);
            }
            catch (Exception ex)
            {
                // A transient Win32 failure during a display change must not
                // kill the loop — that is exactly when it is needed. Drop the
                // cached state and carry on from a clean slate.
                Log.Write($"ERROR in loop: {ex.GetType().Name}: {ex.Message}");
                _bars = [];
                _lastBarCount = -1;
                _lastBarKey = string.Empty;
                Sleep(1000, ct);
            }
        }

        Color.DisplayChanges.Settled -= OnDisplaysSettled;
        EndHighResTimer();
        Restore();
        Log.Write("taskbar manager stopped");
    }

    /// <summary>
    /// Swaps in a new configuration, restoring only what is no longer managed.
    /// </summary>
    /// <remarks>
    /// A bar that stops being managed <em>must</em> be restored here: simply
    /// forgetting it would leave it parked off-screen forever, with nothing
    /// left that knows how to bring it back.
    /// <para>
    /// But only that bar. Restoring everything would flash every managed
    /// taskbar into view on any settings change at all — including one that
    /// merely altered the slide duration — which is exactly the jarring
    /// behaviour this is supposed to avoid. Bars still managed keep their
    /// current shown/hidden state and just take the new parameters.
    /// </para>
    /// </remarks>
    private void AdoptSettings(DispCtrlSettings settings)
    {
        var keep = new List<Bar>();

        foreach (Bar b in _bars)
        {
            MonitorSettings ms = settings.For(b.MonitorToken);
            if (ms.ManagesTaskbar)
            {
                b.ReclaimWorkArea = ms.ReclaimWorkArea;
                keep.Add(b);
            }
            else
            {
                RestoreBar(b);
            }
        }

        _settings = settings;
        _bars = keep;
        _lastBarCount = keep.Count;

        // Force the next rescan to re-discover, so a monitor that was just
        // switched on gets adopted without waiting for a layout change.
        _lastBarKey = string.Empty;

        if (DispCtrl.Core.BuildInfo.Diagnostics) Log.Write($"settings reloaded (managing {keep.Count})");
    }

    // --------------------------------------------------------- adaptive loop --

    /// <summary>
    /// Picks the poll interval for the next tick.
    /// </summary>
    /// <remarks>
    /// The predecessor polled flat out at 25Hz whether or not anything could
    /// happen, doing four Win32 calls a tick forever. Almost all of that work
    /// was spent confirming the cursor was nowhere near a managed edge, so the
    /// rate now follows what is actually possible: idle far away, fast inside
    /// the armed band, faster still mid-slide. Worst-case reveal latency is one
    /// idle tick, which is no worse than native auto-hide.
    /// </remarks>
    private int ChooseInterval(bool animating, bool anyShown, int nearestEdge)
    {
        GlobalSettings g = _settings.Global;

        if (animating) return AnimFrameMs;
        if (anyShown) return g.ShownPollMs;
        if (nearestEdge <= g.ArmDistancePx) return g.ArmedPollMs;

        // Cursor is not on any managed monitor's axis at all.
        if (nearestEdge == int.MaxValue) return g.FarPollMs;

        // Otherwise scale the wait with the ground still to cover. A cursor
        // 2000px away physically cannot reach the edge within a few
        // milliseconds, so waking to confirm it has not is wasted work — and
        // each needless wake keeps the CPU out of its deeper idle states,
        // which costs battery out of proportion to the CPU time itself.
        int travelMs = nearestEdge * 1000 / MaxCursorSpeedPxPerSec;
        return Math.Clamp(travelMs, g.ArmedPollMs, g.FarPollMs);
    }

    private void Sleep(int ms, CancellationToken ct)
    {
        // A 1ms system timer is only worth its power cost while a slide is
        // actually running.
        if (ms != Timeout.Infinite && ms <= AnimFrameMs) BeginHighResTimer();
        else EndHighResTimer();

        _waitOn ??= [ct.WaitHandle, _wake];
        WaitHandle.WaitAny(_waitOn, ms);
    }

    private bool NothingToMaintain()
    {
        GlobalSettings g = _settings.Global;
        if (g.TaskbarGlassEnabled || g.TaskbarOpacity < 100) return false;
        foreach (MonitorSettings m in _settings.Monitors.Values)
            if (m.ManagesTaskbar) return false;
        return true;
    }

    private void BeginHighResTimer()
    {
        if (_highRes) return;
        _ = PInvoke.timeBeginPeriod(1);
        _highRes = true;
    }

    private void EndHighResTimer()
    {
        if (!_highRes) return;
        _ = PInvoke.timeEndPeriod(1);
        _highRes = false;
    }

    // ------------------------------------------------------------ per-bar --

    private void UpdateBar(Bar b, System.Drawing.Point cur, long now, bool sticky,
                           ref bool anyAnimating, ref int nearestEdge)
    {
        if (b.Unmanageable) return;
        if (!PInvoke.IsWindow(b.Hwnd)) return;
        if (!PInvoke.GetWindowRect(b.Hwnd, out RECT r)) return;

        int shownPos = ShownPosition(b);
        int hiddenPos = HiddenPosition(b);
        // Just past the bar's own edge. A blocked bar slides to here, clipped to
        // its monitor so the neighbour never shows it, and only then parks.
        int edgePos = EdgePosition(b);

        bool onAxis = OnMonitorCrossAxis(b, cur);
        int distance = onAxis ? EdgeDistance(b, cur) : int.MaxValue;
        if (distance < nearestEdge) nearestEdge = distance;

        bool atEdge = onAxis && distance <= _settings.Global.RevealPx;
        bool overBar = b.Shown && onAxis && IsOverShownBar(b, cur);

        bool wasShown = b.Shown;

        if (atEdge || overBar || (b.Shown && sticky))
        {
            b.Shown = true;
            b.HideAtTick = now + _settings.Global.HideDelayMs;
        }
        else if (b.Shown && now >= b.HideAtTick)
        {
            b.Shown = false;
        }

        if (_trace && wasShown != b.Shown)
        {
            Log.Write(
                $"trace {b.MonitorLabel}: {(b.Shown ? "SHOW" : "HIDE")} " +
                $"cursor=({cur.X},{cur.Y}) onAxis={onAxis} dist={(distance == int.MaxValue ? "-" : distance)} " +
                $"atEdge={atEdge} overBar={overBar} sticky={sticky} " +
                $"pos={(b.Horizontal ? r.top : r.left)} mon={b.Monitor} edge={b.Edge} thick={b.Thickness}");
        }

        int want = b.Shown ? shownPos : hiddenPos;
        int currentPos = b.Horizontal ? r.top : r.left;
        bool reassertZOrder = b.Shown && now >= b.NextZOrderTick;
        if (reassertZOrder) b.NextZOrderTick = now + 500;

        bool raise = false;
        if (!b.Placed)
        {
            b.TargetPos = want;
            b.Placed = true;
            b.Animating = false;   // never animate the very first placement
        }
        else if (want != b.TargetPos)
        {
            // Start a new slide from wherever the bar is right now, so
            // reversing mid-animation is seamless.
            b.AnimFrom = currentPos;
            b.TargetPos = want;
            b.AnimStart = now;
            b.Animating = _settings.Global.AnimMs > 0;
            // A blocked bar comes back from beyond the desktop by way of its own
            // edge: the jump from the parking place is hidden by the clip.
            if (b.Blocked && b.Shown && currentPos == hiddenPos) b.AnimFrom = edgePos;
            raise = b.Shown;       // coming into view: lift above maximized windows
        }

        int newPos;
        bool settled = false;
        if (b.Animating)
        {
            double t = (now - b.AnimStart) / (double)_settings.Global.AnimMs;
            if (t >= 1.0) { t = 1.0; b.Animating = false; settled = true; }

            // Ease-out cubic: quick off the mark, settles gently — what the
            // native show/hide slide looks like.
            double eased = 1.0 - Math.Pow(1.0 - t, 3.0);
            // A blocked bar hiding slides only as far as its own edge; the last
            // frame then parks it beyond the desktop.
            int slideTo = b.Blocked && b.TargetPos == hiddenPos ? edgePos : b.TargetPos;
            newPos = b.Animating ? (int)Math.Round(b.AnimFrom + (slideTo - b.AnimFrom) * eased) : b.TargetPos;
            anyAnimating = true;
        }
        else
        {
            newPos = b.TargetPos;
            settled = newPos != currentPos;
        }

        // The first animation frame can still have the hidden position. Raise
        // immediately even when that frame does not move by a pixel yet.
        // Clipped before it moves: set after, the first frame out of the
        // parking place showed the whole bar on the neighbouring monitor.
        // At the end of a slide the clip goes: before the move when arriving in
        // view, or the last frame shows the previous frame's cut; after it when
        // parking, or the bar shows whole at its own edge for a frame.
        if (b.Blocked && b.Animating) Clip(b, r, newPos);
        else if (b.Clipped && newPos != hiddenPos) Unclip(b);
        if (newPos != currentPos || raise || reassertZOrder) Move(b, r, newPos, raise || reassertZOrder);
        if (b.Clipped && !b.Animating) Unclip(b);

        // Once, when a reveal comes to rest. Explorer draws the bar's content
        // through a composition bridge child that does not always repaint after
        // its parent is moved from outside: on the 200% panel the bar sometimes
        // arrived with its lower half never drawn. The geometry was right every
        // time it was measured - the pixels were stale.
        if (settled && b.Shown) { HealRegion(b); Repaint(b); }

        TrackStubbornness(b, currentPos);
    }

    /// <summary>
    /// Gives up on a bar that keeps snapping back to where it was.
    /// </summary>
    /// <remarks>
    /// Only counted once the slide has settled, so a bar that is merely
    /// mid-animation is never mistaken for one that is refusing to move.
    /// </remarks>
    private static void TrackStubbornness(Bar b, int currentPos)
    {
        if (b.Animating || !b.Placed || currentPos == b.TargetPos)
        {
            b.Stubborn = 0;
            return;
        }

        if (++b.Stubborn < StubbornLimit) return;

        b.Unmanageable = true;
        Log.Write(
            $"giving up on the taskbar for {b.MonitorLabel}: it returns to {currentPos} " +
            $"however it is positioned (wanted {b.TargetPos}); Explorer is holding it, " +
            "as it does the primary display's bar. Left to Explorer until the displays change.");
    }

    /// <summary>
    /// Clears a window region that no longer covers the whole bar.
    /// </summary>
    /// <remarks>
    /// Explorer gives the taskbar a region the size of the bar. On the 200%
    /// panel it was measured holding one sized for 100% - 2880 x 48 on a bar
    /// 96 tall - and Windows clips a window to its region, so the lower half of
    /// the taskbar was simply never drawn: the glitch reported as "the bottom
    /// half is gone". Explorer does not put it back once cleared (watched for
    /// five seconds), and a region equal to the bar, which is what the primary
    /// taskbar carries, is left alone.
    /// </remarks>
    private static unsafe void HealRegion(Bar b)
    {
        if (!PInvoke.GetWindowRect(b.Hwnd, out RECT window)) return;
        RECT box;
        GDI_REGION_TYPE kind = PInvoke.GetWindowRgnBox(b.Hwnd, &box);
        if (kind is GDI_REGION_TYPE.RGN_ERROR or GDI_REGION_TYPE.NULLREGION) return;

        int covered = b.Horizontal ? box.bottom - box.top : box.right - box.left;
        int thickness = b.Horizontal ? window.bottom - window.top : window.right - window.left;
        if (covered >= thickness) return;

        if (PInvoke.SetWindowRgn(b.Hwnd, HRGN.Null, true) != 0)
            Log.Write($"taskbar on {b.MonitorLabel}: cleared a stale {covered}px region on a {thickness}px bar");
    }

    private static unsafe void Repaint(Bar b) =>
        _ = PInvoke.RedrawWindow(b.Hwnd, null, HRGN.Null,
            REDRAW_WINDOW_FLAGS.RDW_INVALIDATE | REDRAW_WINDOW_FLAGS.RDW_ALLCHILDREN
            | REDRAW_WINDOW_FLAGS.RDW_UPDATENOW | REDRAW_WINDOW_FLAGS.RDW_FRAME);

    private static void Move(Bar b, RECT r, int pos, bool raise)
    {
        int x = b.Horizontal ? r.left : pos;
        int y = b.Horizontal ? pos : r.top;

        SET_WINDOW_POS_FLAGS flags = SET_WINDOW_POS_FLAGS.SWP_NOSIZE
                                   | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        if (!raise) flags |= SET_WINDOW_POS_FLAGS.SWP_NOZORDER;

        // Raise on reveal and at the low-cadence visible check. Intermediate
        // animation frames preserve z-order and never activate the taskbar.
        var after = raise ? new HWND(-1) : HWND.Null;   // HWND_TOPMOST
        _ = PInvoke.SetWindowPos(b.Hwnd, after, x, y, 0, 0, flags);
    }

    /// <summary>
    /// Limits a blocked bar mid-slide to the part inside its own monitor.
    /// </summary>
    /// <remarks>
    /// A monitor stacked against the edge the bar hides past would otherwise
    /// show the slide, which is why a blocked bar used to snap: on the laptop
    /// under the Dell, the reveal was instant and the hide abrupt. Per frame
    /// only while the slide runs; the system owns a region once it is set.
    /// </remarks>
    private static unsafe void Clip(Bar b, RECT r, int pos)
    {
        int width = r.right - r.left, height = r.bottom - r.top;
        int x0 = 0, y0 = 0, x1 = width, y1 = height;
        if (b.Horizontal)
        {
            y0 = Math.Clamp(b.Monitor.Top - pos, 0, height);
            y1 = Math.Clamp(b.Monitor.Bottom - pos, y0, height);
        }
        else
        {
            x0 = Math.Clamp(b.Monitor.Left - pos, 0, width);
            x1 = Math.Clamp(b.Monitor.Right - pos, x0, width);
        }
        nint region = Protection.OverlayNative.CreateRectRgn(x0, y0, x1, y1);
        if (region == 0) return;
        if (Protection.OverlayNative.SetWindowRgn((nint)b.Hwnd.Value, region, 1) == 0)
        {
            _ = Protection.OverlayNative.DeleteObject(region);
            return;
        }
        b.Clipped = true;
    }

    private static unsafe void Unclip(Bar b)
    {
        _ = Protection.OverlayNative.SetWindowRgn((nint)b.Hwnd.Value, 0, 1);
        b.Clipped = false;
    }

    private static int ShownPosition(Bar b) => b.Edge switch
    {
        Edge.Bottom => b.Monitor.Bottom - b.Thickness,
        Edge.Top => b.Monitor.Top,
        Edge.Left => b.Monitor.Left,
        Edge.Right => b.Monitor.Right - b.Thickness,
        _ => b.Monitor.Bottom - b.Thickness,
    };

    /// <summary>Fully off the monitor — not one row short of it. See the class remarks.</summary>
    /// <remarks>
    /// Just past the edge, unless another monitor is there: a monitor stacked
    /// on top of the laptop parked its bar along the top of the laptop's screen,
    /// in plain view. A blocked bar goes past the far side of the whole desktop,
    /// where no monitor is.
    /// </remarks>
    private static int HiddenPosition(Bar b) => b.Blocked ? b.Edge switch
    {
        Edge.Top or Edge.Left => b.Beyond - b.Thickness,
        _ => b.Beyond,
    } : EdgePosition(b);

    /// <summary>Just past the bar's own monitor edge.</summary>
    private static int EdgePosition(Bar b) => b.Edge switch
    {
        Edge.Bottom => b.Monitor.Bottom,
        Edge.Top => b.Monitor.Top - b.Thickness,
        Edge.Left => b.Monitor.Left - b.Thickness,
        Edge.Right => b.Monitor.Right,
        _ => b.Monitor.Bottom,
    };

    /// <summary>Is the cursor within this monitor's span along the bar's long axis?</summary>
    private static bool OnMonitorCrossAxis(Bar b, System.Drawing.Point cur) =>
        b.Horizontal
            ? cur.X >= b.Monitor.Left && cur.X < b.Monitor.Right
            : cur.Y >= b.Monitor.Top && cur.Y < b.Monitor.Bottom;

    private static int EdgeDistance(Bar b, System.Drawing.Point cur) => b.Edge switch
    {
        Edge.Bottom => Math.Abs(b.Monitor.Bottom - cur.Y),
        Edge.Top => Math.Abs(cur.Y - b.Monitor.Top),
        Edge.Left => Math.Abs(cur.X - b.Monitor.Left),
        Edge.Right => Math.Abs(b.Monitor.Right - cur.X),
        _ => int.MaxValue,
    };

    private static bool IsOverShownBar(Bar b, System.Drawing.Point cur)
    {
        const int slack = 2;
        return b.Edge switch
        {
            Edge.Bottom => cur.Y >= b.Monitor.Bottom - b.Thickness - slack,
            Edge.Top => cur.Y <= b.Monitor.Top + b.Thickness + slack,
            Edge.Left => cur.X <= b.Monitor.Left + b.Thickness + slack,
            Edge.Right => cur.X >= b.Monitor.Right - b.Thickness - slack,
            _ => false,
        };
    }

    private static unsafe bool IsStickyForeground()
    {
        HWND fg = PInvoke.GetForegroundWindow();
        if (fg.IsNull) return false;

        Span<char> buf = stackalloc char[256];
        fixed (char* p = buf)
        {
            int n = PInvoke.GetClassName(fg, p, buf.Length);
            return n > 0 && StickyClasses.Contains(new string(buf[..n]));
        }
    }

    // ------------------------------------------------------------ discovery --

    /// <summary>
    /// Once-a-second upkeep: catches explorer restarts, replugs, resolution
    /// changes and taskbar resizes.
    /// </summary>
    /// <remarks>
    /// Deliberately layered by cost. Full display enumeration means a CCD query
    /// plus a registry EDID read per monitor, far too expensive to run every
    /// second for an answer that only changes when the desk does. So it runs
    /// when <see cref="Color.DisplayChanges"/> says a change is over, when
    /// Explorer's set of bars changes (a restart, a bar built after a monitor
    /// arrived), or when a handle dies; otherwise only the per-bar geometry
    /// (one <c>GetWindowRect</c> each) is refreshed.
    /// <para>
    /// It used to re-enumerate every second whenever it held no bar, which is
    /// exactly the state of a laptop whose external monitor has been
    /// unplugged, for as long as it stayed unplugged.
    /// </para>
    /// </remarks>
    private void Rescan()
    {
        bool any = false;
        foreach (MonitorSettings m in _settings.Monitors.Values) if (m.ManagesTaskbar) { any = true; break; }
        if (!any) return;

        bool layoutChanged = Interlocked.Exchange(ref _layoutChanged, 0) != 0;
        List<HWND> windows = FindSecondaryTaskbars();
        string key = BarKey(windows);
        bool barsChanged = key != _lastBarKey;

        bool stale = false;
        foreach (Bar b in _bars)
            if (!PInvoke.IsWindow(b.Hwnd)) { stale = true; break; }

        if (layoutChanged || barsChanged || stale)
        {
            List<DisplayInfo> displays = DisplayRegistry.Enumerate();
            List<Bar> fresh = Discover(displays, windows);
            Release(fresh, displays);
            _bars = fresh;
            _wantedBars = displays.Count(d => !d.IsPrimary && _settings.For(d.Token).ManagesTaskbar);
            // Discovery moves nothing, so the key it saw still stands, but a
            // managed bar's rectangle is no longer part of it.
            _lastBarKey = BarKey(windows);

            if (_bars.Count != _lastBarCount)
            {
                string why = stale ? " [stale handle]" : layoutChanged ? " [displays changed]" : " [taskbars changed]";
                Log.Write($"managing {_bars.Count} taskbar(s){why}");
                _lastBarCount = _bars.Count;
            }
            if (layoutChanged) NotePrimary(displays);
        }
        else
        {
            // A DPI or taskbar-size change alters the bar's thickness without
            // touching the display layout, so the cheap fingerprint cannot see
            // it. One GetWindowRect per bar catches it for almost nothing.
            foreach (Bar b in _bars) RefreshBarGeometry(b);
        }

        foreach (Bar b in _bars)
        {
            if (b.Unmanageable) continue;
            EnsureWorkArea(b);
            // Mid-slide, a short region is the clip, not Explorer's mistake;
            // one left behind by a rediscovered bar is healed here afterwards.
            if (!b.Clipped) HealRegion(b);
        }
    }

    /// <summary>
    /// Explorer's secondary bars as a string: each handle, and where the ones
    /// not managed here sit.
    /// </summary>
    /// <remarks>
    /// A bar Explorer has only just built can sit at a default position that
    /// resolves to the wrong monitor, and is then moved into place without a
    /// new handle; its rectangle is in the key so that move is noticed. A
    /// managed bar's is not, or every slide would rediscover; and only while a
    /// display that wants its bar hidden has none, or an unmanaged bar under
    /// Windows' own auto-hide would rediscover at every reveal.
    /// </remarks>
    private string BarKey(List<HWND> windows)
    {
        bool looking = _bars.Count < _wantedBars;
        var key = new System.Text.StringBuilder();
        foreach (HWND h in windows)
        {
            key.Append(((nint)h).ToString("X")).Append(';');
            bool managed = false;
            foreach (Bar b in _bars) if (b.Hwnd == h) { managed = true; break; }
            if (looking && !managed && PInvoke.GetWindowRect(h, out RECT r))
                key.Append(r.left).Append(',').Append(r.top).Append(',').Append(r.right).Append(',').Append(r.bottom).Append(';');
        }
        return key.ToString();
    }

    /// <summary>
    /// Puts back a bar that is still there but no longer managed.
    /// </summary>
    /// <remarks>
    /// Rediscovery used to keep the old list whenever it found nothing, so a
    /// dead handle was rediscovered every second, and to drop a live bar
    /// without a word, so one that stopped resolving stayed parked off-screen
    /// with nothing left that knew it was there. It is moved to its own
    /// monitor as that monitor is now, not as it was.
    /// </remarks>
    private void Release(List<Bar> fresh, List<DisplayInfo> displays)
    {
        foreach (Bar old in _bars)
        {
            bool kept = false;
            foreach (Bar b in fresh) if (b.Hwnd == old.Hwnd) { kept = true; break; }
            if (kept || !PInvoke.IsWindow(old.Hwnd)) continue;
            DisplayInfo? home = displays.FirstOrDefault(d => d.Token == old.MonitorToken);
            if (home is null) continue; // its monitor has gone; Explorer takes the bar down with it
            old.Monitor = home.Bounds;
            RestoreBar(old);
            Log.Write($"taskbar on {old.MonitorLabel}: no longer managed, put back");
        }
    }

    /// <summary>Says why a display set to hide its taskbar is showing it.</summary>
    /// <remarks>
    /// Unplug the primary monitor and the laptop becomes primary, and the
    /// primary bar belongs to Explorer: it puts it back within ~120 ms of any
    /// move. Managing it anyway was the disconnect "disaster" - the bar fought
    /// over two dozen times, and the work area was taken for the whole screen
    /// under a bar that stayed. The setting is kept, and applies again the
    /// moment the display is secondary.
    /// </remarks>
    private void NotePrimary(List<DisplayInfo> displays)
    {
        foreach (DisplayInfo d in displays)
            if (d.IsPrimary && _settings.For(d.Token).ManagesTaskbar)
                Log.Write($"taskbar on {d.Label}: the primary display's bar is Explorer's; hidden again once {d.Label} is not the primary display");
    }

    private List<Bar> Discover(List<DisplayInfo> displays, List<HWND> windows)
    {
        var bars = new List<Bar>();

        foreach (HWND h in windows)
        {
            if (!PInvoke.GetWindowRect(h, out RECT r)) continue;

            int w = r.right - r.left, ht = r.bottom - r.top;
            if (w <= 0 || ht <= 0) continue;

            bool horizontal = w >= ht;
            int thickness = horizontal ? ht : w;
            if (thickness is <= 0 or > MaxPlausibleThickness) continue;   // not a real bar

            DisplayInfo? d = ResolveMonitor(displays, h, r, horizontal);
            if (d is null || d.IsPrimary) continue;

            string token = d.Token;
            MonitorSettings ms = _settings.For(token);

            // Explicit opt-in per monitor. This is also the safety net that the
            // predecessor needed a special case for: while explorer is still
            // building its taskbars a bar can briefly sit at a default position
            // resolving to the wrong monitor, and acting on that would hide the
            // wrong bar. Here a mis-resolved bar simply is not opted in, so
            // nothing happens.
            if (!ms.ManagesTaskbar) continue;

            var bar = new Bar
            {
                Hwnd = h,
                MonitorToken = token,
                MonitorLabel = d.Label,
                Monitor = d.Bounds,
                Edge = DetectEdge(r, d.Bounds, horizontal),
                Thickness = thickness,
                ReclaimWorkArea = ms.ReclaimWorkArea,
            };
            PlanParking(bar, displays);
            bars.Add(bar);
        }

        return bars;
    }

    /// <summary>
    /// Explorer's secondary bars. Never the primary one, <c>Shell_TrayWnd</c>.
    /// </summary>
    /// <remarks>
    /// Explorer restores the primary bar within ~120 ms of any move from
    /// outside, so a display that hides its taskbar gets Windows' own
    /// auto-hide while it is primary, and DispCtrl's parking only while it is
    /// not. See <see cref="NotePrimary"/>.
    /// </remarks>
    private static List<HWND> FindSecondaryTaskbars()
    {
        var found = new List<HWND>();
        HWND h = HWND.Null;
        while (!(h = PInvoke.FindWindowEx(HWND.Null, h, SecondaryClass, null)).IsNull)
            found.Add(h);
        return found;
    }

    /// <summary>
    /// Finds the monitor a bar belongs to, using the axis that stays put when
    /// the bar is parked.
    /// </summary>
    /// <remarks>
    /// <c>MonitorFromWindow</c> is unreliable here: a parked bar sits outside
    /// every monitor, so "nearest" can resolve to the wrong one. A horizontal
    /// bar only ever moves vertically, so its X centre is stable and identifies
    /// the monitor; the reverse holds for a vertical bar.
    /// </remarks>
    private DisplayInfo? ResolveMonitor(List<DisplayInfo> displays, HWND h, RECT r, bool horizontal)
    {
        // A bar this engine parked is where it put it, not where it belongs.
        // Parked against a monitor stacked below, it sat exactly on that
        // monitor's top edge, the tie-break below chose that monitor, and the
        // bar was stranded on it at the next rescan.
        foreach (Bar known in _bars)
        {
            if (known.Hwnd != h) continue;
            foreach (DisplayInfo d in displays)
                if (d.Token == known.MonitorToken) return d;
        }

        int cx = r.left + (r.right - r.left) / 2;
        int cy = r.top + (r.bottom - r.top) / 2;

        DisplayInfo? best = null;
        int bestDistance = int.MaxValue;

        foreach (DisplayInfo d in displays)
        {
            bool onStableAxis = horizontal
                ? cx >= d.Bounds.Left && cx < d.Bounds.Right
                : cy >= d.Bounds.Top && cy < d.Bounds.Bottom;
            if (!onStableAxis) continue;

            // Several monitors can share that span when they are stacked, so
            // break the tie on the moving axis.
            int distance = horizontal
                ? Math.Min(Math.Abs(r.top - d.Bounds.Top), Math.Abs(r.bottom - d.Bounds.Bottom))
                : Math.Min(Math.Abs(r.left - d.Bounds.Left), Math.Abs(r.right - d.Bounds.Right));

            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = d;
        }

        return best;
    }

    /// <summary>Decides where the bar hides; see <see cref="TaskbarParking.Plan"/>.</summary>
    private static void PlanParking(Bar b, List<DisplayInfo> displays)
    {
        TaskbarSide side = b.Edge switch
        {
            Edge.Top => TaskbarSide.Top,
            Edge.Left => TaskbarSide.Left,
            Edge.Right => TaskbarSide.Right,
            _ => TaskbarSide.Bottom,
        };
        (b.Blocked, b.Beyond) = TaskbarParking.Plan(b.Monitor, side, b.Thickness, displays.Select(d => d.Bounds));
        if (b.Blocked)
            Log.Write($"taskbar on {b.MonitorLabel}: another monitor is past its {side.ToString().ToLowerInvariant()} edge; it slides clipped to its own screen and parks beyond the desktop");
    }

    private static Edge DetectEdge(RECT r, DisplayRect mon, bool horizontal)
    {
        if (horizontal)
        {
            int centre = r.top + (r.bottom - r.top) / 2;
            return centre >= mon.Top + mon.Height / 2 ? Edge.Bottom : Edge.Top;
        }

        int cx = r.left + (r.right - r.left) / 2;
        return cx >= mon.Left + mon.Width / 2 ? Edge.Right : Edge.Left;
    }

    /// <summary>
    /// Re-reads only what can change without the display layout changing:
    /// the bar's thickness and which edge it is docked to.
    /// </summary>
    /// <remarks>
    /// The monitor rect is deliberately left alone. It is already correct
    /// unless the layout changed, and re-deriving it from the bar's position
    /// would be wrong anyway while the bar is parked outside every monitor.
    /// </remarks>
    private static void RefreshBarGeometry(Bar b)
    {
        if (!PInvoke.GetWindowRect(b.Hwnd, out RECT r)) return;

        int w = r.right - r.left, h = r.bottom - r.top;
        if (w <= 0 || h <= 0) return;

        bool horizontal = w >= h;
        int thickness = horizontal ? h : w;
        if (thickness is <= 0 or > MaxPlausibleThickness) return;

        b.Thickness = thickness;
        b.Edge = DetectEdge(r, b.Monitor, horizontal);
    }

    // ----------------------------------------------------------- work area --

    /// <summary>
    /// Keeps the full monitor available when reclaim is enabled. Revealing
    /// the taskbar overlays the application without resizing its work area.
    /// </summary>
    /// <remarks>
    /// Reads the current work area straight from <c>GetMonitorInfo</c> rather
    /// than from a display snapshot, so it stays correct without forcing a full
    /// re-enumeration on the poll path.
    /// </remarks>
    private static unsafe void EnsureWorkArea(Bar b)
    {
        var centre = new System.Drawing.Point(
            b.Monitor.Left + b.Monitor.Width / 2,
            b.Monitor.Top + b.Monitor.Height / 2);

        HMONITOR hmon = PInvoke.MonitorFromPoint(centre, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL);
        if (hmon.IsNull) return;

        var mi = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (!PInvoke.GetMonitorInfo(hmon, ref mi)) return;

        RECT desired = b.ReclaimWorkArea
            ? mi.rcMonitor
            : new RECT
            {
                left = mi.rcMonitor.left + (b.Edge == Edge.Left ? b.Thickness : 0),
                top = mi.rcMonitor.top + (b.Edge == Edge.Top ? b.Thickness : 0),
                right = mi.rcMonitor.right - (b.Edge == Edge.Right ? b.Thickness : 0),
                bottom = mi.rcMonitor.bottom - (b.Edge == Edge.Bottom ? b.Thickness : 0),
            };

        // Already in the desired state — the common case, and the cheap way
        // out of the one-second rescan path.
        if (mi.rcWork.left == desired.left && mi.rcWork.top == desired.top &&
            mi.rcWork.right == desired.right && mi.rcWork.bottom == desired.bottom)
            return;

        // fWinIni = 0 deliberately: broadcasting WM_SETTINGCHANGE makes
        // explorer recompute the work area and fight back in a flicker loop.
        _ = PInvoke.SystemParametersInfo(
            SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWORKAREA, 0, &desired, 0);
    }

    // ------------------------------------------------------------- shutdown --

    /// <summary>
    /// Puts every managed bar back on screen and hands its work area back.
    /// </summary>
    /// <remarks>
    /// Without this, stopping the engine would strand the user with a
    /// permanently hidden taskbar recoverable only by restarting explorer —
    /// which is exactly how the predecessor had to be uninstalled.
    /// </remarks>
    private void Restore()
    {
        foreach (Bar b in _bars) RestoreBar(b);
    }

    /// <summary>
    /// Puts every bar back from a thread that is not the manager's, as the
    /// process dies of an exception somewhere else.
    /// </summary>
    /// <remarks>
    /// The main loop unwinds through <see cref="Restore"/>; an exception on a
    /// timer or a pool thread does not, and killed the engine with its bars
    /// still parked off-screen. The list is copied first because the loop may
    /// be halfway through changing it; a bar restored twice is harmless.
    /// </remarks>
    public void EmergencyRestore()
    {
        Bar[] bars;
        try { bars = [.. _bars]; }
        catch (Exception) { return; }
        foreach (Bar b in bars) RestoreBar(b);
    }

    private static unsafe void RestoreBar(Bar b)
    {
        try
        {
            if (!PInvoke.IsWindow(b.Hwnd)) return;
            if (!PInvoke.GetWindowRect(b.Hwnd, out RECT r)) return;

            Move(b, r, ShownPosition(b), raise: true);

            if (!b.ReclaimWorkArea) return;

            // Give the bar's strip back rather than resetting to the whole
            // screen, which is what a null rect would do.
            var work = new RECT
            {
                left = b.Monitor.Left + (b.Edge == Edge.Left ? b.Thickness : 0),
                top = b.Monitor.Top + (b.Edge == Edge.Top ? b.Thickness : 0),
                right = b.Monitor.Right - (b.Edge == Edge.Right ? b.Thickness : 0),
                bottom = b.Monitor.Bottom - (b.Edge == Edge.Bottom ? b.Thickness : 0),
            };
            _ = PInvoke.SystemParametersInfo(
                SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWORKAREA, 0, &work, 0);
        }
        catch (Exception ex)
        {
            Log.Write($"restore failed for {b.MonitorLabel}: {ex.Message}");
        }
    }
}
