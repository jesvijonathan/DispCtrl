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
    private const string PrimaryClass = "Shell_TrayWnd";
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
    /// 120ms. Without this cap the engine would call SetWindowPos forever at
    /// the armed poll rate, burning CPU on a fight it cannot win — so a bar
    /// that refuses to stay put is dropped, once, with an explanation.
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
    private string _lastSignature = string.Empty;
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
                _lastSignature = string.Empty;
                Sleep(1000, ct);
            }
        }

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
        _lastSignature = string.Empty;

        Log.Write($"settings reloaded (managing {keep.Count})");
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
            newPos = (int)Math.Round(b.AnimFrom + (b.TargetPos - b.AnimFrom) * eased);
            anyAnimating = true;
        }
        else
        {
            newPos = b.TargetPos;
            settled = newPos != currentPos;
        }

        // The first animation frame can still have the hidden position. Raise
        // immediately even when that frame does not move by a pixel yet.
        if (newPos != currentPos || raise || reassertZOrder) Move(b, r, newPos, raise || reassertZOrder);

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
            $"however it is positioned (wanted {b.TargetPos}). This is expected for the " +
            "PRIMARY monitor's taskbar, which explorer actively restores; only secondary " +
            "taskbars can be moved from outside explorer.");
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

    private static int ShownPosition(Bar b) => b.Edge switch
    {
        Edge.Bottom => b.Monitor.Bottom - b.Thickness,
        Edge.Top => b.Monitor.Top,
        Edge.Left => b.Monitor.Left,
        Edge.Right => b.Monitor.Right - b.Thickness,
        _ => b.Monitor.Bottom - b.Thickness,
    };

    /// <summary>Fully off the monitor — not one row short of it. See the class remarks.</summary>
    private static int HiddenPosition(Bar b) => b.Edge switch
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
    /// plus a registry EDID read per monitor, which is far too expensive to run
    /// every second to recompute an answer that only changes when the layout
    /// does — measured as the dominant cost of this loop. So the cheap GDI
    /// fingerprint gates it, and only the per-bar geometry (one
    /// <c>GetWindowRect</c> each) is refreshed unconditionally.
    /// </remarks>
    private void Rescan()
    {
        bool any = false;
        foreach (MonitorSettings m in _settings.Monitors.Values) if (m.ManagesTaskbar) { any = true; break; }
        if (!any) return;
        string signature = DisplayRegistry.CheapSignature();
        bool layoutChanged = signature != _lastSignature;

        bool stale = false;
        foreach (Bar b in _bars)
            if (!PInvoke.IsWindow(b.Hwnd)) { stale = true; break; }

        if (layoutChanged || stale || _bars.Count == 0)
        {
            if (layoutChanged && _lastSignature.Length > 0)
                Log.Write($"monitor layout changed: {_lastSignature}  ->  {signature}");
            _lastSignature = signature;

            List<DisplayInfo> displays = DisplayRegistry.Enumerate();
            List<Bar> fresh = Discover(displays);
            if (fresh.Count > 0 || _bars.Count == 0) _bars = fresh;

            if (_bars.Count != _lastBarCount)
            {
                string why = stale ? " [stale handle]" : layoutChanged ? " [layout change]" : "";
                Log.Write($"managing {_bars.Count} taskbar(s){why}");
                _lastBarCount = _bars.Count;
            }
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
            HealRegion(b);
        }
    }

    private List<Bar> Discover(List<DisplayInfo> displays)
    {
        var bars = new List<Bar>();

        foreach (HWND h in FindTaskbars())
        {
            if (!PInvoke.GetWindowRect(h, out RECT r)) continue;

            int w = r.right - r.left, ht = r.bottom - r.top;
            if (w <= 0 || ht <= 0) continue;

            bool horizontal = w >= ht;
            int thickness = horizontal ? ht : w;
            if (thickness is <= 0 or > MaxPlausibleThickness) continue;   // not a real bar

            DisplayInfo? d = ResolveMonitor(displays, r, horizontal);
            if (d is null) continue;

            string token = d.Token;
            MonitorSettings ms = _settings.For(token);

            // Explicit opt-in per monitor. This is also the safety net that the
            // predecessor needed a special case for: while explorer is still
            // building its taskbars a bar can briefly sit at a default position
            // resolving to the wrong monitor, and acting on that would hide the
            // wrong bar. Here a mis-resolved bar simply is not opted in, so
            // nothing happens.
            if (!ms.ManagesTaskbar) continue;

            bars.Add(new Bar
            {
                Hwnd = h,
                MonitorToken = token,
                MonitorLabel = d.Label,
                Monitor = d.Bounds,
                Edge = DetectEdge(r, d.Bounds, horizontal),
                Thickness = thickness,
                ReclaimWorkArea = ms.ReclaimWorkArea,
            });
        }

        return bars;
    }

    private static List<HWND> FindTaskbars()
    {
        var found = new List<HWND>();

        HWND primary = PInvoke.FindWindowEx(HWND.Null, HWND.Null, PrimaryClass, null);
        if (!primary.IsNull) found.Add(primary);

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
    private static DisplayInfo? ResolveMonitor(List<DisplayInfo> displays, RECT r, bool horizontal)
    {
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
