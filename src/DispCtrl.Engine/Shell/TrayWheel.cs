using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

namespace DispCtrl.Engine.Shell;

/// <summary>
/// Brightness from the mouse wheel over the notification area icon.
/// </summary>
/// <remarks>
/// Windows sends a notification icon clicks and moves, never the wheel, so the
/// wheel is watched with a low-level mouse hook - and a low-level hook sits in
/// the path of every mouse message on the machine. It is installed only when
/// the icon reports the pointer arriving, and removed once the pointer has
/// left the icon's rectangle, so the rest of the time it costs nothing.
/// <para>
/// The hook itself only counts: Windows removes a low-level hook that keeps it
/// waiting (about 300 ms), and a DDC/CI write is longer than that. The notches
/// are applied on a worker, the newest total at a time.
/// </para>
/// </remarks>
internal sealed unsafe partial class TrayWheel : IDisposable
{
    private const int WhMouseLl = 14, WmMouseWheel = 0x020A;
    private const nuint LeaveTimer = 0x57;

    private static TrayWheel? _instance;

    private readonly nint _window;
    private readonly Func<DispCtrlSettings> _settings;
    private readonly Action<DispCtrlSettings> _persist;
    private readonly Action<string> _tip;
    private nint _hook;
    private Rect _icon;
    private bool _iconKnown;
    private long _lastMove;
    private int _pending, _remainder, _working;

    public TrayWheel(nint window, Func<DispCtrlSettings> settings, Action<DispCtrlSettings> persist, Action<string> tip)
    {
        _window = window;
        _settings = settings;
        _persist = persist;
        _tip = tip;
        _instance = this;
    }

    /// <summary>Whether the wheel is being watched, which is while the pointer is on the icon.</summary>
    public bool Watching => _hook != 0;

    /// <summary>The icon says the pointer is over it: watch the wheel until it leaves.</summary>
    public void PointerOver()
    {
        if (_settings().Global.QuickPanel.TrayWheel == TrayWheelTarget.Off) { Stop(); return; }
        _lastMove = Environment.TickCount64;
        _iconKnown = IconRect(_window, out _icon);
        if (_hook != 0) return;
        _hook = SetWindowsHookEx(WhMouseLl, &LowLevelMouse, GetModuleHandle(null), 0);
        if (_hook == 0) { Log.Write($"tray: the wheel could not be watched ({Marshal.GetLastPInvokeError()})"); return; }
        _ = SetTimer(_window, LeaveTimer, 200, 0);
    }

    /// <summary>The tray's timer: stops watching once the pointer is off the icon.</summary>
    public bool Timer(nuint id)
    {
        if (id != LeaveTimer) return false;
        bool over = GetCursorPos(out Point p) != 0 && (_iconKnown
            ? p.X >= _icon.Left - 2 && p.X < _icon.Right + 2 && p.Y >= _icon.Top - 2 && p.Y < _icon.Bottom + 2
            : Environment.TickCount64 - _lastMove < 1500);
        if (!over) Stop();
        return true;
    }

    public void Stop()
    {
        if (_hook == 0) return;
        _ = UnhookWindowsHookEx(_hook);
        _hook = 0;
        _ = KillTimer(_window, LeaveTimer);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint LowLevelMouse(int code, nuint message, nint data)
    {
        if (code >= 0 && message == WmMouseWheel && _instance is { } self)
        {
            var info = (MouseHook*)data;
            if (self._iconKnown
                ? info->X >= self._icon.Left && info->X < self._icon.Right && info->Y >= self._icon.Top && info->Y < self._icon.Bottom
                : Environment.TickCount64 - self._lastMove < 1500)
            {
                Interlocked.Add(ref self._pending, (short)(info->MouseData >> 16));
                self.Kick();
                return 1; // swallowed: the wheel over the icon is ours, not the taskbar's
            }
        }
        return CallNextHookEx(0, code, message, data);
    }

    private void Kick()
    {
        if (Interlocked.Exchange(ref _working, 1) != 0) return;
        _ = Task.Run(() =>
        {
            try
            {
                while (Interlocked.Exchange(ref _pending, 0) is var delta && delta != 0)
                {
                    // A precision touchpad sends small deltas; they add up to notches.
                    int total = _remainder + delta;
                    int notches = total / 120;
                    _remainder = total - notches * 120;
                    if (notches != 0) Apply(notches);
                }
            }
            catch (Exception ex) { Log.Write($"tray: wheel brightness failed: {ex.Message}"); }
            finally
            {
                Volatile.Write(ref _working, 0);
                if (Volatile.Read(ref _pending) != 0) Kick();
            }
        });
    }

    /// <summary>Moves brightness by whole notches, as the setting says.</summary>
    private void Apply(int notches)
    {
        // The calibration walkthrough is holding every display at an end it is
        // capturing; a notch now would be captured as the limit.
        if (UnisonCalibration.IsActive) return;
        DispCtrlSettings settings = SettingsStore.Load();
        QuickPanelSettings panel = settings.Global.QuickPanel;
        int step = Math.Clamp(panel.WheelStep, QuickPanelSettings.MinWheelStep, QuickPanelSettings.MaxWheelStep) * notches;
        List<DisplayInfo> displays = DisplayRegistry.Enumerate();
        DisplayInfo? main = displays.FirstOrDefault(d => d.IsPrimary);

        // Unison when it is on and the target is in it: moving one display of a
        // linked set on its own would pull it out of step with the rest.
        bool unison = settings.Global.UnisonBrightness && displays.Count > 1 && (panel.TrayWheel == TrayWheelTarget.All
            || (main is not null && settings.For(main.Token).InUnison));
        if (unison)
        {
            settings.Global.UnisonLevel = Math.Clamp(settings.Global.UnisonLevel + step, 0, 100);
            _persist(settings);
            if (Color.UnisonWriter.Apply(settings, displays)) _persist(settings);
            _tip($"DispCtrl - unison brightness {settings.Global.UnisonLevel}%");
            return;
        }

        IEnumerable<DisplayInfo> targets = panel.TrayWheel == TrayWheelTarget.All ? displays : main is null ? [] : [main];
        int? shown = null;
        foreach (DisplayInfo d in targets)
        {
            BrightnessRange range = Brightness.Read(d);
            if (!range.Supported)
            {
                // No hardware control: software dimming is this display's brightness.
                MonitorSettings m = settings.For(d.Token);
                m.SoftwareBrightness = Math.Clamp(settings.SoftwareBrightnessFor(d.Token) + step, NightLight.MinimumDim, 100);
                _persist(settings);
                shown ??= m.SoftwareBrightness;
                continue;
            }
            int level = Math.Clamp(range.Percent + step, 0, 100);
            _ = Brightness.Write(d, range.FromPercent(level));
            shown ??= level;
        }
        if (shown is { } now) _tip($"DispCtrl - brightness {now}%");
    }

    /// <summary>Where the icon is on screen, from the shell.</summary>
    private static bool IconRect(nint window, out Rect rect)
    {
        var id = new NotifyIconIdentifier { Size = (uint)sizeof(NotifyIconIdentifier), Window = window, Id = 1 };
        return Shell_NotifyIconGetRect(ref id, out rect) >= 0 && rect.Right > rect.Left;
    }

    public void Dispose()
    {
        Stop();
        if (_instance == this) _instance = null;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseHook { public int X, Y; public uint MouseData, Flags, Time; public nint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct NotifyIconIdentifier { public uint Size; public nint Window; public uint Id; public Guid Item; }

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static partial nint SetWindowsHookEx(int kind, delegate* unmanaged[Stdcall]<int, nuint, nint, nint> proc, nint module, uint thread);
    [LibraryImport("user32.dll")] private static partial int UnhookWindowsHookEx(nint hook);
    [LibraryImport("user32.dll")] private static partial nint CallNextHookEx(nint hook, int code, nuint wparam, nint lparam);
    [LibraryImport("user32.dll")] private static partial int GetCursorPos(out Point point);
    [LibraryImport("user32.dll")] private static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint proc);
    [LibraryImport("user32.dll")] private static partial int KillTimer(nint hwnd, nuint id);
    [LibraryImport("shell32.dll")] private static partial int Shell_NotifyIconGetRect(ref NotifyIconIdentifier id, out Rect rect);
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)] private static partial nint GetModuleHandle(string? name);
}
