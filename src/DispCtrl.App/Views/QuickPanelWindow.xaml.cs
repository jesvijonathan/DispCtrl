using System.Runtime.InteropServices;
using DispCtrl.App.Services;
using DispCtrl.App.ViewModels;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace DispCtrl.App.Views;

/// <summary>
/// The panel the tray icon summons.
/// </summary>
/// <remarks>
/// Everything in it is a view onto <see cref="App.ViewModel"/> — the same
/// instance the full window's pages bind to. That is the whole reason the panel
/// is cheap: there is no second copy of the settings to keep in step, and a
/// slider moved here and a slider moved on the Displays page are the same
/// slider.
/// <para>
/// The rows are built in code rather than declared in markup, because which
/// rows exist depends on the settings and on how many displays are attached.
/// It also keeps the panel clear of <c>SettingsExpander</c>, whose children
/// take the process down when they are not card-like.
/// </para>
/// </remarks>
public sealed partial class QuickPanelWindow : Window
{
    /// <summary><c>DWMWA_WINDOW_CORNER_PREFERENCE</c>.</summary>
    private const uint WindowCornerPreference = 33;

    /// <summary><c>DWMWCP_ROUND</c> — 8 DIP, which the root Border matches.</summary>
    private const uint CornerRound = 2;

    /// <summary>
    /// Height the panel is given before its content has been measured.
    /// </summary>
    /// <remarks>
    /// The window has to be placed before it can be laid out, and laid out
    /// before its height is known. Starting small and growing once is less
    /// visible than starting tall and shrinking, because the panel is anchored
    /// to the bottom of the screen and a shrink moves every row.
    /// </remarks>
    private const int InitialHeightDip = 220;

    private readonly AppWindow _appWindow;
    private readonly nint _hwnd;
    private readonly QuickPanelContent _content;

    private bool _closing;
    private bool _pinned;
    private int _lastHeightDip;

    /// <summary>Where the panel was last put, in physical pixels.</summary>
    private DisplayRect _intended;

    /// <summary>When it was put there, for <see cref="HoldPlacement"/>.</summary>
    private long _placedAt;

    /// <summary>
    /// How long after placing the panel a size change is taken to be Windows'
    /// DPI rescale rather than somebody dragging it.
    /// </summary>
    private const long HoldPlacementMs = 1000;

    public MainViewModel ViewModel => App.ViewModel;

    public QuickPanelWindow()
    {
        InitializeComponent();

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow;

        Title = "DispCtrl";

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        // Not a window anybody alt-tabs to. Leaving it in the switchers would
        // put a second DispCtrl entry there for something that is meant to be
        // one click away and then gone.
        _appWindow.IsShownInSwitchers = false;

        SystemBackdrop = new DesktopAcrylicBackdrop();
        RoundCorners();

        // The title row is the drag handle, so the panel can be moved off the
        // display being adjusted. Everything else has to stay clickable.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleRow);
        // Extending the title bar reinstates WinUI's dialog frame.
        if (_appWindow.Presenter is OverlappedPresenter chrome) chrome.SetBorderAndTitleBar(false, false);
        RoundCorners();

        _content = new QuickPanelContent(Sections, App.ViewModel, Dismiss, () => QueueRebuild(), Report);

        // The customisation page edits the same settings object. With the panel
        // open beside it, each change shows straight away, which is the only
        // way to judge a density or a width without closing and reopening.
        App.ViewModel.QuickPanelChanged += () => QueueRebuild(place: true);

        Activated += OnActivated;
        Closed += (_, _) => { _closing = true; _content.Detach(); };

        // The rows, not the window: the window's size only changes when this
        // resizes it, so a rebuild that added or removed rows went unmeasured.
        Sections.SizeChanged += (_, _) => FitToContent();
        _appWindow.Changed += HoldPlacement;
    }

    /// <summary>
    /// Puts the panel back where it was placed when Windows rescales it.
    /// </summary>
    /// <remarks>
    /// Moving first and resizing second, which is what fixed the same trap in
    /// the full window, is not enough here. The panel is created on whichever
    /// display it first lands on, and when it is moved from the 200% laptop to
    /// the 100% Dell the rescale arrives after the resize, not before it: the
    /// panel opened at 180 pixels wide instead of 360, with every label cut
    /// short. Measured, not supposed.
    /// <para>
    /// The panel cannot be resized, so a size it was not given can only be the
    /// rescale. Only shortly after placing it, though: its title is a drag
    /// handle, and a panel dragged onto another display should rescale the way
    /// any window does.
    /// </para>
    /// </remarks>
    private void HoldPlacement(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange || _intended.Width <= 0) return;
        if (Environment.TickCount64 - _placedAt > HoldPlacementMs) return;

        SizeInt32 now = sender.Size;
        if (Math.Abs(now.Width - _intended.Width) <= 2 && Math.Abs(now.Height - _intended.Height) <= 2) return;

        sender.Move(new PointInt32(_intended.Left, _intended.Top));
        sender.Resize(new SizeInt32(_intended.Width, _intended.Height));
    }

    private void Hold(DisplayRect at)
    {
        _intended = at;
        _placedAt = Environment.TickCount64;
    }

    private void RoundCorners()
    {
        try
        {
            const long frameStyles = 0x00C40000; // WS_CAPTION | WS_THICKFRAME
            long style = GetWindowLongPtr(_hwnd, -16).ToInt64();
            if ((style & frameStyles) != 0)
            {
                _ = SetWindowLongPtr(_hwnd, -16, (nint)(style & ~frameStyles));
                _ = SetWindowPos(_hwnd, 0, 0, 0, 0, 0, 0x37); // frame changed, no move/size/activation/z-order
            }
            // DWM's own show and hide transitions would play underneath the
            // slide; this one is the only animation the panel should have.
            uint off = 1;
            _ = DwmSetWindowAttribute(_hwnd, DwmwaTransitionsForceDisabled, ref off, sizeof(uint));
            uint preference = CornerRound;
            _ = DwmSetWindowAttribute(_hwnd, WindowCornerPreference, ref preference, sizeof(uint));
            uint noBorder = 0xFFFFFFFE; // DWMWA_COLOR_NONE, including the native non-client stroke.
            _ = DwmSetWindowAttribute(_hwnd, 34, ref noBorder, sizeof(uint));
        }
        catch (Exception)
        {
            // An older build that does not know the attribute. Square corners
            // are a cosmetic loss, not a reason to refuse to show the panel.
        }
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, ref uint value, uint size);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hwnd, int index);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [LibraryImport("user32.dll")]
    private static partial int SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);

    /// <summary>When the panel last closed because it lost focus.</summary>
    private long _deactivatedAt;

    /// <summary>
    /// How soon after closing on lost focus a summons is the same click.
    /// </summary>
    /// <remarks>
    /// Clicking the tray icon while the panel is open takes focus from it first:
    /// it closes, and then the click arrives and asks for it again. Without this
    /// the icon could only ever open the panel, never close it.
    /// </remarks>
    private const long SameClickMs = 500;

    /// <summary>Builds the rows if they are stale, places the panel and brings it up.</summary>
    /// <param name="toggle">
    /// From the tray icon: a click on an open panel closes it, as Windows' own
    /// flyouts do. The customisation page's preview always opens it.
    /// </param>
    public void Summon(bool toggle = false)
    {
        if (_closing) return;

        if (toggle)
        {
            if (_appWindow.IsVisible && !_leaving) { Dismiss(); return; }
            if (Environment.TickCount64 - _deactivatedAt < SameClickMs) return;
        }

        // The settings file is shared with the engine and the command line, so
        // what was on screen last time may no longer be true. The rows are still
        // attached, so they follow whatever this changes.
        App.ViewModel.ReloadFromDisk();

        // A process that has only ever shown the panel has no main window, and
        // with it no status timer: without these the engine tile and the
        // display list would show whatever was true when the process started.
        App.ViewModel.RefreshEngineStatus();
        App.ViewModel.RefreshIfDisplaysChanged();

        // Rebuilt only when something changed. Rebuilding on every summons put
        // fresh toggles on screen, and a toggle created checked plays its
        // off-to-on colour transition as it loads: every lit tile flashed grey
        // for the first 150 ms of the animation.
        if (_stale) { _stale = false; Rebuild(place: true); }
        else Place(App.ViewModel.QuickPanel);

        int token = ++_animation;
        _leaving = false;
        bool animate = AnimationsOn;

        // Cloaked until the rows have been laid out and the height fitted: shown
        // bare, the window drew as an empty grey block for two frames first.
        Cloak(true);
        PrepareContent(animate);
        _appWindow.Show();

        // Show() alone leaves the panel behind whatever was in front, because
        // a window that is not activated does not come forward even when it is
        // topmost. The activation is what makes a click on the tray icon feel
        // like the panel opened rather than appeared somewhere underneath.
        Activate();
        KeepOnTop();
        // Focus somewhere, but as a click would: activation otherwise gave the
        // first button keyboard focus, and its focus rectangle, on every opening.
        _ = DensityButton.Focus(FocusState.Pointer);
        RoundCorners();

        AfterFrames(2, () =>
        {
            if (token != _animation || _closing) return;
            Fit();
            if (animate) Slide(show: true, token);
            else { TuckUnderTaskbar(false); Cloak(false); }
        });
    }

    /// <summary>Builds the rows without showing anything; the next summons only shows them.</summary>
    public void Prepare()
    {
        if (_closing || !_stale) return;
        _stale = false;
        Rebuild(place: false);
    }

    // ================================================================ opening and closing

    /// <summary>How long the panel takes to rise, as Windows' own flyouts do.</summary>
    private const int SlideInMs = 260;

    /// <summary>Closing is quicker than opening: somebody closing it is done with it.</summary>
    private const int SlideOutMs = 180;


    /// <summary>Up from a bottom taskbar, down from a top one.</summary>
    private int _slideSign = 1;

    private int _animation;
    private bool _stale = true;
    private bool _leaving;

    /// <summary>
    /// Windows' own "Animation effects" setting, which its flyouts obey.
    /// </summary>
    /// <remarks>
    /// Somebody who has switched animation off - often because motion makes
    /// them unwell - has asked every window to stop moving, this one included.
    /// </remarks>
    private static bool AnimationsOn
    {
        get
        {
            if (!App.ViewModel.QuickPanel.Animate) return false;
            try { return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled; }
            catch (Exception) { return true; }
        }
    }

    /// <summary>Puts the contents where an opening fade starts from, or at rest.</summary>
    private void PrepareContent(bool animate)
    {
        var visual = ElementCompositionPreview.GetElementVisual(Root);
        visual.StopAnimation("Opacity");
        visual.Opacity = animate ? 0 : 1;
    }

    /// <summary>Runs <paramref name="then"/> once XAML has produced that many frames.</summary>
    private static void AfterFrames(int frames, Action then)
    {
        void Tick(object? sender, object e)
        {
            if (--frames > 0) return;
            CompositionTarget.Rendering -= Tick;
            then();
        }
        CompositionTarget.Rendering += Tick;
    }

    /// <summary>
    /// Moves the whole panel, backdrop and all, from behind the taskbar.
    /// </summary>
    /// <remarks>
    /// Three attempts came before this. Moving the window once a frame while the
    /// rows were still being built stuttered, because every move waited on a
    /// busy UI thread. Sliding only the contents left the acrylic backdrop to
    /// appear at full size in one frame, with the rows fading in on top of it.
    /// What Windows' flyouts do is move the whole surface and let the taskbar
    /// cover the part that has not risen yet; the panel is put just below the
    /// taskbar in the topmost band for the length of the slide, which gives
    /// exactly that clip. The rows are already built and attached, so the UI
    /// thread has nothing else to do while it moves the window; the contents
    /// fade on the compositor alongside.
    /// </remarks>
    private void Slide(bool show, int token, Action? done = null)
    {
        uint dpi = QuickPanelHost.MonitorOf(_hwnd).Dpi;
        int height = _intended.Height > 0 ? _intended.Height : _appWindow.Size.Height;
        // Its whole height plus the gap above the taskbar: it starts, and ends,
        // entirely behind the taskbar or past the edge of the screen, so no part
        // of the backdrop appears or vanishes in one frame.
        int travel = (height + QuickPanelPlacement.Scale(QuickPanelPlacement.MarginDip, dpi)) * _slideSign;
        int ms = show ? SlideInMs : SlideOutMs;

        TuckUnderTaskbar(true);
        if (show) MoveTo(travel);
        Cloak(false);
        // In only. Faded on the way out, the rows were gone within 60 ms and an
        // empty backdrop sank on its own for the rest of the slide.
        if (show) FadeContent(ms);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Tick(object? sender, object e)
        {
            if (token != _animation || _closing)
            {
                CompositionTarget.Rendering -= Tick;
                TuckUnderTaskbar(false);
                return;
            }

            double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / ms);
            double eased = show ? EaseOut(t) : EaseIn(t);
            MoveTo((int)Math.Round(travel * (show ? 1 - eased : eased)));

            if (t < 1) return;
            CompositionTarget.Rendering -= Tick;
            // Hidden before it is raised again on the way out, or the empty
            // backdrop would flash above the taskbar for a frame.
            if (show) TuckUnderTaskbar(false);
            done?.Invoke();
            if (!show) TuckUnderTaskbar(false);
        }
        CompositionTarget.Rendering += Tick;
    }

    // Windows' flyout curves: decelerate in (0.1, 0.9, 0.2, 1), accelerate out
    // (0.7, 0, 1, 0.5). Evaluated here because the window, not a visual, moves.
    private static double EaseOut(double t) => Bezier(t, 0.1, 0.9, 0.2, 1.0);
    private static double EaseIn(double t) => Bezier(t, 0.7, 0.0, 1.0, 0.5);

    private static double Bezier(double x, double x1, double y1, double x2, double y2)
    {
        double lo = 0, hi = 1, u = x;
        for (int i = 0; i < 24; i++)
        {
            u = (lo + hi) / 2;
            double bx = 3 * (1 - u) * (1 - u) * u * x1 + 3 * (1 - u) * u * u * x2 + u * u * u;
            if (bx < x) lo = u; else hi = u;
        }
        return 3 * (1 - u) * (1 - u) * u * y1 + 3 * (1 - u) * u * u * y2 + u * u * u;
    }

    private void FadeContent(int ms)
    {
        var visual = ElementCompositionPreview.GetElementVisual(Root);
        var compositor = visual.Compositor;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        // Finished two thirds of the way, so the rows are legible before the
        // panel settles.
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, compositor.CreateCubicBezierEasingFunction(new(0.1f, 0.9f), new(0.2f, 1f)));
        fade.Duration = TimeSpan.FromMilliseconds(ms * 2 / 3);
        visual.StartAnimation("Opacity", fade);
    }

    /// <summary>Puts the window <paramref name="offset"/> pixels from where it belongs.</summary>
    private void MoveTo(int offset)
    {
        if (_intended.Width <= 0) return;
        _ = SetWindowPos(_hwnd, 0, _intended.Left, _intended.Top + offset, 0, 0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    /// <summary>
    /// Puts the panel just below the taskbar of its display, or back on top.
    /// </summary>
    /// <remarks>
    /// Both are topmost, so this only reorders within the topmost band. A display
    /// whose taskbar is hidden has none to go under; the panel then rises from
    /// the edge of the screen, which clips it the same way.
    /// </remarks>
    private void TuckUnderTaskbar(bool under)
    {
        nint taskbar = under ? TaskbarOf(_hwnd) : 0;
        _ = SetWindowPos(_hwnd, under && taskbar != 0 ? taskbar : HwndTopmost, 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoActivate);
    }

    /// <remarks>
    /// Only a taskbar that is itself topmost and actually on the screen. A bar
    /// DispCtrl has hidden is parked off-screen and not topmost; slotting the panel
    /// in after it dropped the panel out of the topmost band, behind every
    /// ordinary window, where it stayed.
    /// </remarks>
    /// <summary>
    /// Asks the presenter, not the window, for topmost.
    /// </summary>
    /// <remarks>
    /// The presenter keeps its own idea of always-on-top and enforces it on every
    /// position change. Set in the constructor of a window that was not shown
    /// until later - the preloaded panel - it never took, and from then on the
    /// presenter stripped <c>WS_EX_TOPMOST</c> from every <c>SetWindowPos</c>:
    /// the panel opened behind whatever window had focus. Cycling the value makes
    /// the presenter apply it now.
    /// </remarks>
    private void KeepOnTop()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = false;
            presenter.IsAlwaysOnTop = true;
        }
        TuckUnderTaskbar(false);
    }

    private static nint TaskbarOf(nint window)
    {
        nint monitor = MonitorFromWindow(window, 2);
        (DisplayRect bounds, _, _) = QuickPanelHost.MonitorOf(window);
        foreach (string cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            nint bar = 0;
            while ((bar = FindWindowEx(0, bar, cls, null)) != 0)
            {
                if (MonitorFromWindow(bar, 2) != monitor || !IsWindowVisible(bar)) continue;
                if ((GetWindowLongPtr(bar, -20).ToInt64() & 0x8) == 0) continue; // WS_EX_TOPMOST
                if (!GetWindowRect(bar, out Rect r)) continue;
                int visibleX = Math.Min(r.Right, bounds.Right) - Math.Max(r.Left, bounds.Left);
                int visibleY = Math.Min(r.Bottom, bounds.Bottom) - Math.Max(r.Top, bounds.Top);
                if (visibleX > 8 && visibleY > 8) return bar;
            }
        }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out Rect rect);

    private void Cloak(bool cloaked)
    {
        uint value = cloaked ? 1u : 0u;
        _ = DwmSetWindowAttribute(_hwnd, DwmwaCloak, ref value, sizeof(uint));
    }

    private const uint DwmwaCloak = 13;
    private const uint DwmwaTransitionsForceDisabled = 3;
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpNoActivate = 0x10;
    private const nint HwndTopmost = -1;

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parent, nint after, string? cls, string? title);
    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);

    private void Rebuild(bool place)
    {
        QuickPanelSettings panel = App.ViewModel.QuickPanel;
        _pinned = panel.StayOpen;
        UpdatePinGlyph();
        ApplyLock();

        Footer.Visibility = panel.ShowFooter && !panel.Simple ? Visibility.Visible : Visibility.Collapsed;
        // Density means nothing to a column of sliders.
        DensityButton.Visibility = panel.Simple ? Visibility.Collapsed : Visibility.Visible;
        _content.Build(panel);
        if (place) Place(panel);
    }

    /// <summary>
    /// Rebuilds once, after whatever asked for it has finished.
    /// </summary>
    /// <remarks>
    /// The display list is cleared and refilled one display at a time, and each
    /// step raises a change. Rebuilding on every one of them would build the
    /// panel three times over to show it once.
    /// </remarks>
    private void QueueRebuild(bool place = false)
    {
        _placeOnRebuild |= place;
        if (!_appWindow.IsVisible) { _stale = true; return; }
        if (_rebuildQueued || _closing) return;
        _rebuildQueued = true;

        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _rebuildQueued = false;
            if (_closing || !_appWindow.IsVisible) return;

            bool again = _placeOnRebuild;
            _placeOnRebuild = false;
            Rebuild(again);
        });
    }

    private bool _rebuildQueued, _placeOnRebuild;

    private DispatcherTimer? _statusTimer;

    /// <summary>Shows a line under the rows for a few seconds.</summary>
    private void Report(string text)
    {
        if (_closing) return;

        Status.Text = text;
        Status.Visibility = Visibility.Visible;

        _statusTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _statusTimer.Stop();
        _statusTimer.Tick -= HideStatus;
        _statusTimer.Tick += HideStatus;
        _statusTimer.Start();
    }

    private void HideStatus(object? sender, object e)
    {
        _statusTimer?.Stop();
        Status.Visibility = Visibility.Collapsed;
    }

    /// <summary>Hides the panel without tearing the process down.</summary>
    /// <remarks>
    /// Hidden rather than closed, because the point of a resident panel is that
    /// the second summons is instant. Closing would mean building the XAML tree
    /// again, which is most of the cold start this design exists to avoid. The
    /// rows stay attached while hidden: following a property change costs next
    /// to nothing, and rebuilding them on the next summons is what made every
    /// lit tile flash on the way in.
    /// </remarks>
    public void Dismiss()
    {
        if (_closing || _leaving) return;

        int token = ++_animation;
        if (!AnimationsOn || !_appWindow.IsVisible)
        {
            _appWindow.Hide();
            PrepareContent(false);
            return;
        }

        _leaving = true;
        Slide(show: false, token, () =>
        {
            _leaving = false;
            _appWindow.Hide();
            MoveTo(0);
            PrepareContent(false);
        });
    }

    private void Place(QuickPanelSettings panel)
    {
        DisplayRect work, bounds;
        uint dpi;

        try
        {
            (bounds, work, dpi) = QuickPanelHost.MonitorUnderCursor();
        }
        catch (Exception)
        {
            return;
        }

        int margin = QuickPanelPlacement.Scale(QuickPanelPlacement.MarginDip, dpi);
        int width = QuickPanelPlacement.Scale(panel.Width, dpi);
        // Simple mode is a handful of sliders: a fixed height only left space
        // under them, so it always fits what it holds.
        int height = panel.FixedHeight && !panel.Simple
            ? QuickPanelPlacement.Scale(Math.Clamp(panel.Height, QuickPanelSettings.MinHeight, QuickPanelSettings.MaxHeight), dpi)
            : QuickPanelPlacement.Scale(_lastHeightDip > 0 ? _lastHeightDip : InitialHeightDip, dpi);

        height = Math.Min(height, QuickPanelPlacement.MaxHeight(work, margin));

        ScreenEdge edge = QuickPanelPlacement.EdgeOf(bounds, work);
        _slideSign = edge == ScreenEdge.Top ? -1 : 1;
        DisplayRect at = QuickPanelPlacement.Place(
            work, edge, QuickPanelHost.CursorX(), width, height, margin);

        // Move first, then resize. MoveAndResize double-applies the scale when
        // the move crosses to a monitor at a different DPI: the window is
        // resized and then rescaled again by the DPI change. Held before either,
        // so the rescale that still arrives late is recognised and undone.
        Hold(at);
        _appWindow.Move(new PointInt32(at.Left, at.Top));
        _appWindow.Resize(new SizeInt32(at.Width, at.Height));
    }

    /// <summary>
    /// Grows the window to whatever the rows actually need.
    /// </summary>
    /// <remarks>
    /// The panel's height is not knowable in advance: it depends on which
    /// sections are on and how many displays are attached. So it is measured
    /// once the tree is realised and the window is resized around it, staying
    /// anchored to the corner it was placed in rather than growing downward
    /// off the screen.
    /// <para>
    /// Never from inside the layout pass that reported the change. Resizing the
    /// window lays it out again synchronously, and with every section switched
    /// on the panel is taller than the screen: capped, it scrolls, the scroll
    /// bar narrows the rows, the rows rewrap a few pixels taller, and that
    /// reported another change - each one nested inside the last until the
    /// stack ran out and took the process with it. Deferred to after layout,
    /// once per pass, and skipped when it would not change the size, there is
    /// nothing left to nest.
    /// </para>
    /// </remarks>
    private void FitToContent()
    {
        if (_fitQueued || _closing || !_appWindow.IsVisible) return;
        _fitQueued = true;

        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _fitQueued = false;
            Fit();
        });
    }

    private bool _fitQueued;

    private void Fit()
    {
        if (_closing || !_appWindow.IsVisible) return;

        // A fixed height is kept whatever the rows need; what does not fit scrolls.
        if (App.ViewModel.QuickPanel.FixedHeight && !App.ViewModel.QuickPanel.Simple) return;

        double wanted = TitleRow.ActualHeight
            + Sections.DesiredSize.Height
            + (Footer.Visibility == Visibility.Visible ? Footer.ActualHeight : 0)
            + 16;

        int wantedDip = (int)Math.Ceiling(wanted);
        if (wantedDip <= 0) return;
        _lastHeightDip = wantedDip;

        try
        {
            // The monitor the panel is on, not the one the pointer is on: the
            // pointer may have moved to another screen while the panel opened.
            (DisplayRect _, DisplayRect work, uint dpi) = QuickPanelHost.MonitorOf(_hwnd);
            int margin = QuickPanelPlacement.Scale(QuickPanelPlacement.MarginDip, dpi);
            int height = Math.Min(
                QuickPanelPlacement.Scale(wantedDip, dpi),
                QuickPanelPlacement.MaxHeight(work, margin));

            if (Math.Abs(height - _appWindow.Size.Height) <= 2) return;

            // Only the top edge moves: the panel keeps its bottom against the
            // taskbar, which is where it was summoned from. Taken from where it
            // was placed rather than where it is, because where it is may be
            // mid-rescale and half its width.
            DisplayRect placed = _intended.Width > 0
                ? _intended
                : new DisplayRect(_appWindow.Position.X, _appWindow.Position.Y,
                    _appWindow.Position.X + _appWindow.Size.Width, _appWindow.Position.Y + _appWindow.Size.Height);

            DisplayRect at = placed with { Top = placed.Bottom - height };
            Hold(at);

            _appWindow.Move(new PointInt32(at.Left, at.Top));
            _appWindow.Resize(new SizeInt32(at.Width, at.Height));
        }
        catch (Exception)
        {
            // A layout change mid-measure. The panel is already on screen at a
            // usable size; leaving it be is better than throwing.
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated) return;
        if (_pinned || !_appWindow.IsVisible) return;

        _deactivatedAt = Environment.TickCount64;
        Dismiss();
    }

    private void OnPin(object sender, RoutedEventArgs e)
    {
        _pinned = !_pinned;
        App.ViewModel.QuickPanelStayOpen = _pinned;
        UpdatePinGlyph();
    }

    private void UpdatePinGlyph()
    {
        // Filled pin when held open, outline when it will dismiss itself.
        PinGlyph.Glyph = _pinned ? "\uE842" : "\uE718";
        UpdateDensityTip();
    }

    // ================================================================ title bar

    private static readonly string[] Densities = ["Compact", "Comfortable", "Spacious"];

    private void UpdateDensityTip()
    {
        int now = App.ViewModel.QuickPanelDensityIndex;
        ToolTipService.SetToolTip(DensityButton,
            $"Layout: {Densities[now]}. Click for {Densities[(now + 1) % Densities.Length]}.");
    }

    /// <summary>Cycles compact, comfortable, spacious - the right one is found by looking.</summary>
    private void OnDensity(object sender, RoutedEventArgs e)
    {
        App.ViewModel.QuickPanelDensityIndex = (App.ViewModel.QuickPanelDensityIndex + 1) % Densities.Length;
        UpdateDensityTip();
    }

    /// <summary>Locks the panel where it opens, or lets its title drag it again.</summary>
    /// <remarks>
    /// Locked, the title row stops being a drag handle - a panel nudged while
    /// reaching for a slider no longer wanders - and a panel dragged away goes
    /// back to where it belongs.
    /// </remarks>
    private void OnLock(object sender, RoutedEventArgs e)
    {
        App.ViewModel.QuickPanelLocked = !App.ViewModel.QuickPanelLocked;
        ApplyLock();
        if (App.ViewModel.QuickPanelLocked) Place(App.ViewModel.QuickPanel);
    }

    private void ApplyLock()
    {
        bool locked = App.ViewModel.QuickPanel.Locked;
        SetTitleBar(locked ? NoDrag : TitleRow);
        LockGlyph.Glyph = locked ? "\uE72E" : "\uE785";
        ToolTipService.SetToolTip(LockButton, locked
            ? "Locked where it opens. Click to let the title drag it."
            : "Can be dragged by its title. Click to lock it where it opens.");
    }

    private void OnOpenApp(object sender, RoutedEventArgs e)
    {
        Dismiss();
        App.ShowMainWindow("displays");
    }

    /// <remarks>
    /// Straight to the page that edits this panel. The gear in a flyout means
    /// "change this", and landing on Displays would leave the way to do that to
    /// be found in the navigation.
    /// </remarks>
    private void OnCustomise(object sender, RoutedEventArgs e)
    {
        Dismiss();
        App.ShowMainWindow("quickpanel");
    }
}
