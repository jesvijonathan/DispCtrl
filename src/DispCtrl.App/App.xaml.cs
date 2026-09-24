using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using DispCtrl.App.Services;
using DispCtrl.App.ViewModels;
using DispCtrl.App.Views;
using DispCtrl.Core.Settings;

namespace DispCtrl.App;

public partial class App : Application
{
    /// <summary>
    /// The main window, so pages can parent a file picker to it. WinUI 3 has no
    /// ambient parent window the way UWP did.
    /// </summary>
    /// <remarks>
    /// Null while only the quick panel is up: a process started from the tray
    /// has no main window until somebody asks for one.
    /// </remarks>
    public static Window MainWindow { get; private set; } = null!;

    public static bool HasMainWindow => MainWindow is not null;

    /// <summary>
    /// The one view model, shared by every page.
    /// </summary>
    /// <remarks>
    /// Shared rather than per-page so a toggle on Displays and the status shown
    /// on Engine cannot disagree: they are reading the same objects, not two
    /// copies of the settings file that drifted apart. The quick panel binds to
    /// it too, which is why the panel and the window can be open at once.
    /// </remarks>
    public static MainViewModel ViewModel { get; } = new();

    private static QuickPanelWindow? _panel;

    /// <summary>
    /// Proof that this process is the one the tray icon wakes. Held in a field
    /// for the life of the process, because a mutex that is collected is a
    /// mutex that is released.
    /// </summary>
    private static Mutex? _listening;

    public App()
    {
        InitializeComponent();

        // Written down before the process goes. An exception thrown inside a
        // XAML callback reaches Windows as a stowed exception from
        // CoreMessagingXP.dll, and Windows Error Reporting records nothing about
        // where in this code it started - which is how a crash in the quick
        // panel was first seen: as a process that silently vanished.
        UnhandledException += (_, e) => RecordCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) RecordCrash(ex);
        };
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);

    public static string CrashLogPath => System.IO.Path.Combine(SettingsStore.Directory, "app-crash.log");

    private static void RecordCrash(Exception ex)
    {
        try
        {
            System.IO.File.AppendAllText(CrashLogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Nothing left to report it with.
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] words = Environment.GetCommandLineArgs().Skip(1).ToArray();
        bool panelOnly = words.Any(a => a.Equals("--panel", StringComparison.OrdinalIgnoreCase));
        bool background = words.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));

        // Every launch offers to listen, not only a --panel one. The full window
        // being open is the commonest moment for a tray click, and a second
        // process started for it would build a second copy of every display
        // just to show a flyout.
        _listening = QuickPanelHost.Listen(DispatcherQueue.GetForCurrentThread(), () => SummonPanel(toggle: true),
            () => ViewModel.Identify(), () => ShowMainWindow());

        if (panelOnly)
        {
            if (_listening is null)
            {
                // Another DispCtrl is already listening, and it can show the
                // panel faster than this process could finish starting.
                if (!background) QuickPanelSignal.Summon();
                Exit();
                return;
            }

            // Started by the engine to be ready: build the panel, show nothing.
            if (background) PreparePanel();
            else SummonPanel();
            if (words.Any(a => a.Equals("--identify", StringComparison.OrdinalIgnoreCase))) ViewModel.Identify();
            return;
        }

        // One DispCtrl at a time. A second copy - a second click on the Start
        // entry, the tray's "Open", another build - hands the window to the one
        // already running and leaves, so two windows never write the same
        // settings over each other. Foreground rights go with it, or Windows
        // flashes the taskbar button instead of bringing the window up.
        if (_listening is null)
        {
            _ = AllowSetForegroundWindow(-1);
            if (QuickPanelSignal.ShowRunningWindow()) { Exit(); return; }
        }

        ShowMainWindow();
        _ = ViewModel.StartEngineByDefaultAsync();
    }

    /// <summary>Brings up the full window, creating it if this process has none.</summary>
    /// <param name="page">A navigation tag to land on, or null to leave it where it was.</param>
    public static void ShowMainWindow(string? page = null)
    {
        if (MainWindow is not DispCtrl.App.MainWindow window)
        {
            window = new DispCtrl.App.MainWindow();
            MainWindow = window;

            // Forgotten on close, so the next request builds a fresh one rather
            // than activating a window that no longer exists.
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(MainWindow, window)) MainWindow = null!;
            };
        }

        if (page is not null) window.ShowPage(page);
        // Activate alone leaves a minimized window on the taskbar, which reads
        // as the second launch having done nothing.
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        window.Activate();
    }

    /// <summary>Shows the quick panel, building it the first time.</summary>
    /// <remarks>
    /// Built lazily. A process that only ever shows the full window has no
    /// reason to carry a hidden panel, and a hidden panel is what keeps the
    /// process alive after its last visible window closes.
    /// </remarks>
    /// <param name="toggle">True for the tray icon, which closes an open panel.</param>
    /// <summary>Builds the panel hidden, so the first summons only has to show it.</summary>
    public static void PreparePanel()
    {
        _panel ??= new QuickPanelWindow();
        _panel.Prepare();
    }

    public static void SummonPanel(bool toggle = false)
    {
        _panel ??= new QuickPanelWindow();
        _panel.Summon(toggle);
    }
}
