using DisplCtrl.Core.Displays;
using DisplCtrl.Core.Presets;
using DisplCtrl.Core.Settings;
using DisplCtrl.Display;
using DisplCtrl.Display.Presets;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DisplCtrl.Engine.Input;

/// <summary>
/// Global keyboard shortcuts, owned by the resident process.
/// </summary>
/// <remarks>
/// <c>RegisterHotKey</c> delivers <c>WM_HOTKEY</c> to the message queue of the
/// thread that registered it, and the engine has no message queue: its loop
/// polls the cursor. So this owns a dedicated thread with a pump of its own.
/// One thread, asleep in <c>GetMessage</c> except when a shortcut fires, which
/// is as close to free as a background thread gets.
/// <para>
/// Registration and unregistration must both happen on that thread — a hotkey
/// belongs to the thread that took it, and releasing it from elsewhere silently
/// does nothing, which would leave the combination held until the process
/// exited.
/// </para>
/// </remarks>
internal sealed class HotkeyService : IDisposable
{
    /// <summary>Base identifier for registered hotkeys, unique within the thread.</summary>
    private const int FirstId = 0xB00;

    private readonly Lock _gate = new();
    private readonly Action<DisplCtrlSettings> _persist;

    private Thread? _thread;
    private uint _threadId;
    private DisplCtrlSettings _settings;
    private bool _disposed;

    /// <summary>The bindings the pump thread has registered, by hotkey id.</summary>
    private readonly Dictionary<int, Hotkey> _registered = [];

    public HotkeyService(DisplCtrlSettings settings, Action<DisplCtrlSettings> persist)
    {
        _settings = settings;
        _persist = persist;

        if (settings.Hotkeys.Count == 0) return;

        _thread = new Thread(Pump)
        {
            IsBackground = true,
            Name = "DisplCtrl hotkeys",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Takes a reloaded settings file and re-registers.</summary>
    public void Update(DisplCtrlSettings settings)
    {
        lock (_gate) { _settings = settings; }

        // The pump re-reads its bindings when poked. Posting rather than
        // touching the table directly, because registration only works from the
        // thread that owns it.
        if (_threadId != 0) _ = PInvoke.PostThreadMessage(_threadId, PInvoke.WM_APP, default, default);
    }

    private unsafe void Pump()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        Register();

        try
        {
            MSG message;
            while (PInvoke.GetMessage(&message, HWND.Null, 0, 0) > 0)
            {
                if (message.message == PInvoke.WM_HOTKEY)
                {
                    Fire((int)message.wParam.Value);
                }
                else if (message.message == PInvoke.WM_APP)
                {
                    Unregister();
                    Register();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write($"hotkey pump stopped: {ex.Message}");
        }
        finally
        {
            Unregister();
        }
    }

    private void Register()
    {
        DisplCtrlSettings settings;
        lock (_gate)
        {
            if (_disposed) return;
            settings = _settings;
        }

        int id = FirstId;
        int taken = 0, refused = 0;

        foreach (Hotkey hotkey in settings.Hotkeys)
        {
            if (!hotkey.Enabled || !hotkey.IsComplete) continue;

            // MOD_NOREPEAT: holding the combination fires once, not forty times.
            // Without it a held shortcut walks brightness to an end stop.
            const uint NoRepeat = 0x4000;

            if (PInvoke.RegisterHotKey(HWND.Null, id, (HOT_KEY_MODIFIERS)(hotkey.Modifiers | NoRepeat), hotkey.Key))
            {
                _registered[id] = hotkey;
                id++;
                taken++;
            }
            else
            {
                // Almost always another application holding the combination.
                // Worth a line: a shortcut that silently does nothing is the
                // hardest kind of setting to diagnose.
                Log.Write($"hotkey {hotkey.Describe()} refused — something else has it");
                refused++;
            }
        }

        if (taken > 0 || refused > 0) Log.Write($"hotkeys: {taken} registered, {refused} refused");
    }

    private void Unregister()
    {
        foreach (int id in _registered.Keys) _ = PInvoke.UnregisterHotKey(HWND.Null, id);
        _registered.Clear();
    }

    private void Fire(int id)
    {
        if (!_registered.TryGetValue(id, out Hotkey? hotkey)) return;

        DisplCtrlSettings settings;
        lock (_gate) { settings = _settings; }

        try
        {
            Act(hotkey, settings);
        }
        catch (Exception ex)
        {
            // A shortcut that throws must not take the pump down with it, or
            // every other shortcut stops working too.
            Log.Write($"hotkey {hotkey.Describe()} failed: {ex.Message}");
        }
    }

    private void Act(Hotkey hotkey, DisplCtrlSettings settings)
    {
        switch (hotkey.Action)
        {
            case HotkeyAction.BrightnessUp:
                Step(hotkey, settings, hotkey.Step);
                break;

            case HotkeyAction.BrightnessDown:
                Step(hotkey, settings, -hotkey.Step);
                break;

            case HotkeyAction.UnisonUp:
            case HotkeyAction.UnisonDown:
            {
                int delta = hotkey.Action == HotkeyAction.UnisonUp ? hotkey.Step : -hotkey.Step;
                settings.Global.UnisonBrightness = true;
                settings.Global.UnisonLevel = Math.Clamp(settings.Global.UnisonLevel + delta, 0, 100);
                _persist(settings);
                break;
            }

            case HotkeyAction.NightLightToggle:
                settings.Global.NightLight.Enabled = !settings.Global.NightLight.Enabled;
                _persist(settings);
                break;

            case HotkeyAction.NightLightWarmer:
            case HotkeyAction.NightLightCooler:
            {
                int delta = hotkey.Action == HotkeyAction.NightLightWarmer ? hotkey.Step : -hotkey.Step;
                settings.Global.NightLight.Enabled = true;
                settings.Global.NightLight.Strength =
                    Math.Clamp(settings.Global.NightLight.Strength + delta, 5, 100);
                _persist(settings);
                break;
            }

            case HotkeyAction.ApplyPreset:
            {
                Preset? preset = PresetStore.Read(PresetStore.PathFor(hotkey.Preset!));
                if (preset is null)
                {
                    Log.Write($"hotkey wanted preset '{hotkey.Preset}', which is not on disk");
                    return;
                }

                PresetResult result = PresetService.Apply(preset, Displays(), settings);
                _persist(settings);

                Log.Write($"hotkey applied preset '{hotkey.Preset}'");
                foreach (string note in result.Notes) Log.Write($"  {note}");
                break;
            }

            case HotkeyAction.NextInput:
                NextInput(hotkey);
                break;

            case HotkeyAction.Identify:
                // The overlays are the panel's, and raising a window from here
                // would mean the engine carrying XAML. Logged so the binding is
                // at least visibly doing something until that is wired up.
                Log.Write("hotkey: identify is only available from the panel");
                break;
        }
    }

    private void Step(Hotkey hotkey, DisplCtrlSettings settings, int delta)
    {
        foreach (DisplayInfo d in Targets(hotkey))
        {
            BrightnessRange range = Brightness.Read(d);
            if (!range.Supported)
            {
                // No hardware control: move the software dimming instead, which
                // is the only brightness this display has.
                int now = settings.SoftwareBrightnessFor(d.Token);
                settings.For(d.Token).SoftwareBrightness =
                    Math.Clamp(now + delta, NightLight.MinimumDim, 100);

                _persist(settings);
                continue;
            }

            _ = Brightness.Write(d, (uint)Math.Clamp((int)range.Current + delta, 0, 100));
        }
    }

    private void NextInput(Hotkey hotkey)
    {
        foreach (DisplayInfo d in Targets(hotkey))
        {
            VcpControl? input = null;
            foreach (VcpControl c in MonitorCapabilities.ReadSettable(d).Controls)
                if (c.Code == 0x60 && c.Settable) { input = c; break; }

            if (input is null || input.Values.Count < 2) continue;

            int at = 0;
            for (int i = 0; i < input.Values.Count; i++)
                if (input.Values[i].Value == input.CurrentValue) { at = i; break; }

            VcpValue next = input.Values[(at + 1) % input.Values.Count];
            _ = MonitorCapabilities.Write(d, 0x60, next.Value);
        }
    }

    /// <remarks>
    /// Resolved fresh each time, not cached: a hotkey outlives any particular
    /// arrangement, and "display 2" has to mean whatever is second right now.
    /// </remarks>
    private static List<DisplayInfo> Targets(Hotkey hotkey)
    {
        List<DisplayInfo> all = Displays();
        if (hotkey.Display <= 0) return all;

        return hotkey.Display <= all.Count ? [all[hotkey.Display - 1]] : [];
    }

    private static List<DisplayInfo> Displays()
    {
        List<DisplayInfo> all = DisplayRegistry.Enumerate();

        all.Sort((a, b) =>
        {
            if (a.IsInternal != b.IsInternal) return a.IsInternal ? -1 : 1;
            int byX = a.Bounds.Left.CompareTo(b.Bounds.Left);
            return byX != 0 ? byX : a.Bounds.Top.CompareTo(b.Bounds.Top);
        });

        return all;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        // Ends GetMessage, which unregisters on the way out — on the thread that
        // holds the hotkeys, which is the only thread that can.
        if (_threadId != 0) _ = PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);

        _thread?.Join(TimeSpan.FromSeconds(2));
    }
}
