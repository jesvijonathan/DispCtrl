using System.Runtime.InteropServices;
using DispCtrl.Core.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DispCtrl.Engine.Shell;

/// <summary>
/// DispCtrl's icon in the notification area, and what a click on it does.
/// </summary>
/// <remarks>
/// It belongs to the engine because the notification area is a place to be, not
/// a thing to do: an icon that appeared when the panel was opened and vanished
/// when it closed would be useless. The engine is the process that is already
/// resident, so it is the one that can hold a permanent icon for a couple of
/// kilobytes and a sleeping thread.
/// <para>
/// Like <see cref="Input.HotkeyService"/>, this owns a thread with a message
/// pump, for the same reason: <c>Shell_NotifyIcon</c> delivers its callback to
/// a window's queue, and the engine's loop polls rather than pumping.
/// </para>
/// </remarks>
internal sealed class TrayIconService : IDisposable
{
    /// <summary>The callback message the shell posts for this icon.</summary>
    private const uint TrayCallback = PInvoke.WM_APP + 0x20;

    /// <summary>Menu command ids. Any value; they only travel within this window.</summary>
    private const int CmdPanel = 1, CmdOpen = 2, CmdHide = 3;

    private const string ClassName = "DispCtrl.Tray";

    private readonly Lock _gate = new();
    private readonly Action<DispCtrlSettings> _persist;

    private Thread? _thread;
    private HWND _window;
    private HICON _icon;
    private uint _taskbarCreated;
    private DispCtrlSettings _settings;
    private bool _shown;
    private bool _disposed;

    /// <summary>
    /// Held for the life of the service so the shell's callback does not arrive
    /// at a collected delegate. A window procedure outlives every managed
    /// reference that looks like it owns it.
    /// </summary>
    private WNDPROC? _procedure;

    public TrayIconService(DispCtrlSettings settings, Action<DispCtrlSettings> persist)
    {
        _settings = settings;
        _persist = persist;

        if (!settings.Global.QuickPanel.Enabled) return;
        Start();
    }

    private void Start()
    {
        _thread = new Thread(Pump)
        {
            IsBackground = true,
            Name = "DispCtrl tray",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Takes a reloaded settings file; adds or removes the icon to match.</summary>
    public void Update(DispCtrlSettings settings)
    {
        lock (_gate) { _settings = settings; }

        if (settings.Global.QuickPanel.Enabled && _thread is null) { Start(); return; }
        if (_window.IsNull) return;

        // Adding and removing the icon must happen on the thread that owns the
        // window, so the pump is poked rather than the shell called from here.
        _ = PInvoke.PostMessage(_window, PInvoke.WM_APP, default, default);
    }

    private unsafe void Pump()
    {
        try
        {
            _procedure = Procedure;
            _taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");

            HINSTANCE instance = PInvoke.GetModuleHandle((PCWSTR)null);

            fixed (char* name = ClassName)
            {
                WNDCLASSEXW cls = new()
                {
                    // Not sizeof: the struct carries a delegate, so it is a
                    // managed type and only the marshalled size is meaningful.
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = _procedure,
                    hInstance = instance,
                    lpszClassName = name,
                };

                // Zero means the class was already registered, which is not
                // fatal: the window below is created against it either way.
                _ = PInvoke.RegisterClassEx(cls);

                fixed (char* title = "DispCtrl")
                {
                    _window = PInvoke.CreateWindowEx(
                        0, name, title, 0,
                        0, 0, 0, 0, HWND.Null, default, instance, null);
                }
            }

            if (_window.IsNull)
            {
                Log.Write("tray: no window, icon not shown");
                return;
            }

            _icon = LoadOwnIcon();
            Apply();

            MSG message;
            while (PInvoke.GetMessage(&message, HWND.Null, 0, 0) > 0)
            {
                PInvoke.TranslateMessage(&message);
                PInvoke.DispatchMessage(&message);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"tray pump stopped: {ex.Message}");
        }
        finally
        {
            Remove();
            if (!_icon.IsNull) { PInvoke.DestroyIcon(_icon); _icon = default; }
            if (!_window.IsNull) { PInvoke.DestroyWindow(_window); _window = default; }
        }
    }

    /// <summary>
    /// The engine's own icon, taken from its executable.
    /// </summary>
    /// <remarks>
    /// <c>ExtractIconEx</c> on the running binary rather than a resource id:
    /// both executables carry the icon through <c>ApplicationIcon</c>, and which
    /// id that lands on in the PE is the build system's business, not something
    /// to hard-code and have silently break.
    /// </remarks>
    private static unsafe HICON LoadOwnIcon()
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return default;

        Span<HICON> large = stackalloc HICON[1];
        Span<HICON> small = stackalloc HICON[1];
        if (PInvoke.ExtractIconEx(exe, 0, large, small) == 0) return default;

        // The notification area draws at small-icon size, so the small one is
        // the one that is not resampled.
        if (!small[0].IsNull)
        {
            if (!large[0].IsNull) PInvoke.DestroyIcon(large[0]);
            return small[0];
        }

        return large[0];
    }

    private unsafe NOTIFYICONDATAW Data()
    {
        NOTIFYICONDATAW data = new()
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _window,
            uID = 1,
            uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE
                   | NOTIFY_ICON_DATA_FLAGS.NIF_ICON
                   | NOTIFY_ICON_DATA_FLAGS.NIF_TIP,
            hIcon = _icon,
        };

        data.uCallbackMessage = TrayCallback;

        // What the icon is for, not what it is called: the name alone would
        // tell somebody hovering nothing they did not already know.
        data.szTip = "DispCtrl - brightness and displays";

        return data;
    }

    /// <summary>Adds or removes the icon to match the current setting.</summary>
    private void Apply()
    {
        bool wanted;
        lock (_gate)
        {
            wanted = !_disposed && _settings.Global.QuickPanel.Enabled;
        }

        if (wanted == _shown) return;
        if (wanted) Add(); else Remove();
    }

    private void Add()
    {
        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, Data()))
        {
            // Explorer is not ready yet. TaskbarCreated brings us back.
            Log.Write("tray: shell refused the icon, waiting for TaskbarCreated");
            return;
        }

        _shown = true;
        Log.Write("tray: icon shown");
    }

    private unsafe void Remove()
    {
        if (!_shown) return;

        NOTIFYICONDATAW data = new()
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _window,
            uID = 1,
        };

        _ = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, data);
        _shown = false;
    }

    private LRESULT Procedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == _taskbarCreated && _taskbarCreated != 0)
        {
            // Explorer restarted and threw away every icon it was holding.
            _shown = false;
            Apply();
            return new LRESULT(0);
        }

        switch (message)
        {
            case PInvoke.WM_APP:
                Apply();
                return new LRESULT(0);

            case TrayCallback:
                OnTrayMessage((uint)(lParam.Value & 0xFFFF));
                return new LRESULT(0);

            case PInvoke.WM_COMMAND:
                OnCommand((int)(wParam.Value & 0xFFFF));
                return new LRESULT(0);

            case PInvoke.WM_DESTROY:
                Remove();
                return new LRESULT(0);
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private void OnTrayMessage(uint message)
    {
        switch (message)
        {
            case PInvoke.WM_LBUTTONUP:
                Summon();
                break;

            case PInvoke.WM_RBUTTONUP:
            case PInvoke.WM_CONTEXTMENU:
                ShowMenu();
                break;
        }
    }

    private static void Summon()
    {
        if (!QuickPanelSignal.Summon())
            Log.Write("tray: nothing to summon - the panel app has not been run yet");
    }

    private unsafe void ShowMenu()
    {
        HMENU menu = PInvoke.CreatePopupMenu();
        if (menu.IsNull) return;

        try
        {
            Item(menu, CmdPanel, "Quick panel");
            Item(menu, CmdOpen, "Open DispCtrl");
            _ = PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, default);
            Item(menu, CmdHide, "Hide this icon");

            if (!PInvoke.GetCursorPos(out System.Drawing.Point point)) return;

            // Without this the menu never dismisses when clicked away from: a
            // popup belonging to a window that is not foreground is not told to
            // close. A documented shell quirk, and the reason every tray menu in
            // Windows does the same dance.
            _ = PInvoke.SetForegroundWindow(_window);

            _ = PInvoke.TrackPopupMenu(
                menu,
                TRACK_POPUP_MENU_FLAGS.TPM_RIGHTALIGN | TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN,
                point.X, point.Y, 0, _window, null);

            static unsafe void Item(HMENU menu, int id, string text)
            {
                fixed (char* label = text)
                    _ = PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, (nuint)id, label);
            }

            // The other half of the same quirk: the window has to be given a
            // message after the menu closes or the next click is swallowed.
            _ = PInvoke.PostMessage(_window, 0, default, default);
        }
        finally
        {
            _ = PInvoke.DestroyMenu(menu);
        }
    }

    private void OnCommand(int id)
    {
        switch (id)
        {
            case CmdPanel:
                Summon();
                break;

            case CmdOpen:
                OpenApp();
                break;

            case CmdHide:
                // Turned off in the file both processes read, so the panel's own
                // switch shows it off too rather than disagreeing with the tray.
                lock (_gate)
                {
                    _settings.Global.QuickPanel.Enabled = false;
                    _persist(_settings);
                }

                Apply();
                break;
        }
    }

    private static void OpenApp()
    {
        try
        {
            if (!File.Exists(QuickPanelSignal.AppPathFile))
            {
                Log.Write("tray: no recorded app path");
                return;
            }

            string exe = File.ReadAllText(QuickPanelSignal.AppPathFile).Trim();
            if (!File.Exists(exe)) { Log.Write("tray: recorded app path is stale"); return; }

            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Write($"tray: could not open the app: {ex.Message}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        // Ends the pump, whose finally clause removes the icon on the thread
        // that owns it. Removing it from here would be ignored.
        if (_window.IsNull) return;
        _ = PInvoke.PostMessage(_window, PInvoke.WM_CLOSE, default, default);
        _ = PInvoke.PostMessage(_window, PInvoke.WM_QUIT, default, default);
    }
}
