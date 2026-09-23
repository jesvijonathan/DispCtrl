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
    private const int CmdPanel = 1, CmdOpen = 2, CmdHide = 3, CmdPromote = 4;

    private const string ClassName = "DispCtrl.Tray";

    private const nuint ActiveCheckTimer = 1;

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
    /// What the current icon was drawn for, so a broadcast that changed
    /// nothing the icon depends on does not redraw it.
    /// </summary>
    private (TrayIconStyle Style, bool Dark, int Size, bool Active, uint Rgb) _drawnFor = (TrayIconStyle.AppLogo, false, -1, false, 0);

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

            Redraw();
            Apply();

            // Once a minute, so a timed keep-awake that runs out stops showing
            // as active; Redraw costs nothing when nothing changed.
            _ = PInvoke.SetTimer(_window, ActiveCheckTimer, 60_000, null);

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
    /// The engine's own icon, taken from its executable at the tray's size.
    /// </summary>
    /// <remarks>
    /// From the running binary rather than a resource id: which id
    /// <c>ApplicationIcon</c> lands on in the PE is the build system's business,
    /// not something to hard-code and have silently break.
    /// <para>
    /// <c>SHDefExtractIcon</c> rather than <c>ExtractIconEx</c>, because the
    /// icon file holds a single 256-pixel image and the tray wants 16.
    /// <c>ExtractIconEx</c>'s small icon is the system's crude resize of that;
    /// this asks the shell for the exact size, resampled the way Explorer
    /// resamples its own.
    /// </para>
    /// </remarks>
    private static unsafe HICON LoadOwnIcon(int size)
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return default;

        HICON large = default, small = default;
        uint sizes = ((uint)size << 16) | (uint)size;

        fixed (char* path = exe)
        {
            if (PInvoke.SHDefExtractIcon(path, 0, 0, &large, &small, sizes).Failed) return default;
        }

        if (!small.IsNull) PInvoke.DestroyIcon(small);
        return large;
    }

    /// <summary>
    /// Draws the icon for the current style, taskbar theme and size, and hands
    /// it to the shell if it is already showing.
    /// </summary>
    private void Redraw()
    {
        TrayIconStyle style;
        bool active;
        TrayIconColour colour;
        lock (_gate)
        {
            style = _settings.Global.QuickPanel.Icon;
            colour = _settings.Global.QuickPanel.IconColour;
            // Drawn denser while something is holding the PC awake, so the icon
            // itself says so; a timed keep-awake that runs out is caught by the
            // minute timer on this window.
            AwakeSettings awake = _settings.Global.Awake;
            active = _settings.Global.QuickPanel.IconShowsActive && (awake.StayActive || awake.ActiveAt(DateTimeOffset.UtcNow));
        }

        // A theme that cannot be read is taken as dark, the Windows 11 default,
        // because a white glyph on a light bar is still visible and a black one
        // on a dark bar is not.
        bool dark = WindowsTheme.IsShellDark ?? true;
        int size = TrayGlyph.Size;

        uint rgb = colour == TrayIconColour.Accent && AccentColour() is uint accent ? accent : dark ? 0xFFFFFFu : 0x000000u;
        if (_drawnFor == (style, dark, size, active, rgb) && !_icon.IsNull) return;

        // Recorded as asked for, not as drawn: a logo that falls back to the
        // glyph must not look like a changed setting to every later broadcast.
        TrayIconStyle requested = style;

        HICON next = default;
        string how = "";

        if (style == TrayIconStyle.AppLogo)
        {
            next = LoadOwnIcon(size);
            how = $"the application icon, {size}px";
        }

        // Never an empty slot. An executable built without its icon gave a null
        // handle here, which the shell draws as nothing at all.
        if (next.IsNull && style == TrayIconStyle.AppLogo)
        {
            Log.Write("tray: the executable carries no icon, drawing the glyph instead");
            style = TrayIconStyle.Brightness;
        }

        if (next.IsNull)
        {
            char glyph = style == TrayIconStyle.Display ? TrayGlyph.Display : TrayGlyph.Brightness;
            int covered = 0;

            // Caught here rather than left to the pump: an exception out of
            // drawing ended the pump, and with it the icon, the menu, and the
            // only way to reach the panel. A plainer icon is always better.
            try { next = TrayGlyph.Draw(glyph, size, rgb, active, out covered); }
            catch (Exception ex) { Log.Write($"tray: drawing the glyph failed: {ex.Message}"); }

            how = $"{style} glyph, {size}px, #{rgb:X6}{(active ? ", bold while active" : "")}, {covered} pixels";

            // A glyph that drew nothing would be an invisible icon. The logo is
            // worse-looking and better than nothing.
            if (next.IsNull || covered == 0)
            {
                if (!next.IsNull) PInvoke.DestroyIcon(next);
                next = LoadOwnIcon(size);
                how = "the application icon, because the glyph did not draw";
            }
        }

        HICON previous = _icon;
        _icon = next;
        _drawnFor = (requested, dark, size, active, rgb);

        if (_shown) _ = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, Data());

        // Released only after the shell has the new one, so there is no moment
        // when it is asked to draw a destroyed handle.
        if (!previous.IsNull) PInvoke.DestroyIcon(previous);

        Log.Write($"tray: icon is {how}");
    }

    /// <summary>Windows' accent colour as 0xRRGGBB, or null when it cannot be read.</summary>
    /// <remarks>
    /// DWM keeps it as 0xAABBGGRR under the user's key; a change arrives as the
    /// same setting-change broadcast the theme does, so Redraw already hears it.
    /// </remarks>
    private static uint? AccentColour()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is not int abgr) return null;
            uint v = unchecked((uint)abgr);
            return ((v & 0xFF) << 16) | (v & 0xFF00) | ((v >> 16) & 0xFF);
        }
        catch (Exception) { return null; }
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
        PromoteOnce();
    }

    /// <summary>
    /// Puts the icon on the taskbar rather than behind the ^, once per engine
    /// location, the first time Windows keeps a record of it.
    /// </summary>
    /// <remarks>
    /// Windows 11 hides every new icon in the overflow and gives programs no
    /// way to ask otherwise; the icon is DispCtrl's way in, so hidden it looks
    /// as if DispCtrl did not start. The record appears a moment after the icon
    /// is first shown, hence the retries. Once per path, remembered in
    /// settings: after that, where the icon sits is the person's choice, made
    /// on the Quick panel page, the icon's menu or Windows' own settings.
    /// </remarks>
    private void PromoteOnce()
    {
        string? path = Environment.ProcessPath;
        if (path is null) return;
        _ = Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(1500).ConfigureAwait(false);
                if (_disposed) return;
                DispCtrlSettings settings = SettingsStore.Load();
                if (settings.Global.TrayPromotedFor.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
                if (TrayIconPromotion.IsPromoted(path) is null) continue;
                if (TrayIconPromotion.SetPromoted(path, true))
                {
                    settings.Global.TrayPromotedFor.Add(path);
                    SettingsStore.Save(settings);
                    Log.Write("tray: kept on the taskbar (first run for this location)");
                }
                return;
            }
        });
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
                Redraw();
                Apply();
                return new LRESULT(0);

            // The taskbar switching between light and dark arrives as a setting
            // change ("ImmersiveColorSet"), and a change of scale as a display
            // change. Neither is filtered by its details: Redraw compares what
            // the icon depends on and does nothing when none of it moved.
            case PInvoke.WM_TIMER:
                Redraw();
                break;

            case PInvoke.WM_SETTINGCHANGE:
            case PInvoke.WM_DISPLAYCHANGE:
            case PInvoke.WM_DPICHANGED:
                Redraw();
                break;

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

            // Windows' own per-icon switch, offered where somebody is looking at
            // the icon. Greyed until Windows has a record of it to change.
            bool? promoted = TrayIconPromotion.IsPromoted(Environment.ProcessPath);
            Item(menu, CmdPromote, "Keep on the taskbar",
                (promoted == true ? MENU_ITEM_FLAGS.MF_CHECKED : 0)
                | (promoted is null ? MENU_ITEM_FLAGS.MF_GRAYED : 0));
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

            static unsafe void Item(HMENU menu, int id, string text, MENU_ITEM_FLAGS extra = 0)
            {
                fixed (char* label = text)
                    _ = PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING | extra, (nuint)id, label);
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

            case CmdPromote:
                bool now = TrayIconPromotion.IsPromoted(Environment.ProcessPath) == true;
                if (!TrayIconPromotion.SetPromoted(Environment.ProcessPath, !now))
                    Log.Write("tray: Windows has no record of the icon to move yet");
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
