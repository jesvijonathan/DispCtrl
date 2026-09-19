using System.Runtime.InteropServices;
using DispCtrl.App.Services;
using DispCtrl.App.ViewModels;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

    private bool _closing;
    private bool _pinned;
    private int _lastHeightDip;

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

        Activated += OnActivated;
        Closed += (_, _) => _closing = true;

        Root.SizeChanged += (_, _) => FitToContent();
    }

    private void RoundCorners()
    {
        try
        {
            uint preference = CornerRound;
            _ = DwmSetWindowAttribute(_hwnd, WindowCornerPreference, ref preference, sizeof(uint));
        }
        catch (Exception)
        {
            // An older build that does not know the attribute. Square corners
            // are a cosmetic loss, not a reason to refuse to show the panel.
        }
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, ref uint value, uint size);

    /// <summary>Builds the rows, places the panel and brings it up.</summary>
    public void Summon()
    {
        if (_closing) return;

        // The settings file is shared with the engine and the command line, so
        // what was on screen last time may no longer be true.
        App.ViewModel.ReloadFromDisk();

        QuickPanelSettings panel = App.ViewModel.QuickPanel;
        _pinned = panel.StayOpen;
        UpdatePinGlyph();

        Footer.Visibility = panel.ShowFooter ? Visibility.Visible : Visibility.Collapsed;

        QuickPanelContent.Build(Sections, App.ViewModel, panel, Dismiss);

        Place(panel);
        _appWindow.Show();

        // Show() alone leaves the panel behind whatever was in front, because
        // a window that is not activated does not come forward even when it is
        // topmost. The activation is what makes a click on the tray icon feel
        // like the panel opened rather than appeared somewhere underneath.
        Activate();
    }

    /// <summary>Hides the panel without tearing the process down.</summary>
    /// <remarks>
    /// Hidden rather than closed, because the point of a resident panel is that
    /// the second summons is instant. Closing would mean building the XAML tree
    /// again, which is most of the cold start this design exists to avoid.
    /// </remarks>
    public void Dismiss()
    {
        if (_closing) return;
        _appWindow.Hide();
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
        int height = QuickPanelPlacement.Scale(_lastHeightDip > 0 ? _lastHeightDip : InitialHeightDip, dpi);

        height = Math.Min(height, QuickPanelPlacement.MaxHeight(work, margin));

        ScreenEdge edge = QuickPanelPlacement.EdgeOf(bounds, work);
        DisplayRect at = QuickPanelPlacement.Place(
            work, edge, QuickPanelHost.CursorX(), width, height, margin);

        // Move first, then resize. MoveAndResize double-applies the scale when
        // the move crosses to a monitor at a different DPI: the window is
        // resized and then rescaled again by the DPI change.
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
    /// </remarks>
    private void FitToContent()
    {
        if (_closing || !_appWindow.IsVisible) return;

        double scale = QuickPanelHost.ScaleOf(_hwnd);
        if (scale <= 0) scale = 1;

        double wanted = TitleRow.ActualHeight
            + Sections.DesiredSize.Height
            + (Footer.Visibility == Visibility.Visible ? Footer.ActualHeight : 0)
            + 16;

        int wantedDip = (int)Math.Ceiling(wanted);
        if (wantedDip <= 0 || Math.Abs(wantedDip - _lastHeightDip) <= 2) return;

        _lastHeightDip = wantedDip;

        try
        {
            (DisplayRect _, DisplayRect work, uint dpi) = QuickPanelHost.MonitorUnderCursor();
            int margin = QuickPanelPlacement.Scale(QuickPanelPlacement.MarginDip, dpi);
            int height = Math.Min(
                QuickPanelPlacement.Scale(wantedDip, dpi),
                QuickPanelPlacement.MaxHeight(work, margin));

            // Only the top edge moves: the panel keeps its bottom against the
            // taskbar, which is where it was summoned from.
            int bottom = _appWindow.Position.Y + _appWindow.Size.Height;
            _appWindow.Move(new PointInt32(_appWindow.Position.X, bottom - height));
            _appWindow.Resize(new SizeInt32(_appWindow.Size.Width, height));
        }
        catch (Exception)
        {
            // A layout change mid-measure. The panel is already on screen at a
            // usable size; leaving it be is better than throwing out of a
            // layout pass.
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated) return;
        if (_pinned) return;

        Dismiss();
    }

    private void OnPin(object sender, RoutedEventArgs e)
    {
        _pinned = !_pinned;
        App.ViewModel.QuickPanelStayOpen = _pinned;
        UpdatePinGlyph();
    }

    private void UpdatePinGlyph() =>
        // Filled pin when held open, outline when it will dismiss itself.
        PinGlyph.Glyph = _pinned ? "\uE842" : "\uE718";

    private void OnOpenApp(object sender, RoutedEventArgs e)
    {
        Dismiss();
        QuickPanelHost.OpenMainWindow();
    }
}
