using DispCtrl.Core.Displays;
using DispCtrl.Core.Presets;
using DispCtrl.Core.Settings;
using DispCtrl.Display;
using DispCtrl.Display.Presets;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DispCtrl.Engine.Input;

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
    private readonly Action<DispCtrlSettings> _persist;

    private Thread? _thread;
    private uint _threadId;
    private DispCtrlSettings _settings;
    private bool _disposed;

    /// <summary>The bindings the pump thread has registered, by hotkey id.</summary>
    private readonly Dictionary<int, Hotkey> _registered = [];

    public HotkeyService(DispCtrlSettings settings, Action<DispCtrlSettings> persist)
    {
        _settings = settings;
        _persist = persist;

        if (settings.Hotkeys.Count == 0) return;

        _thread = new Thread(Pump)
        {
            IsBackground = true,
            Name = "DispCtrl hotkeys",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Takes a reloaded settings file and re-registers.</summary>
    public void Update(DispCtrlSettings settings)
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
        DispCtrlSettings settings;
        lock (_gate)
        {
            if (_disposed) return;
            settings = _settings;
        }

        int id = FirstId;
        int taken = 0, refused = 0;
        var registeredKeys = new List<string>();
        var refusedKeys = new List<string>();

        foreach (Hotkey hotkey in settings.Hotkeys)
        {
            if (!hotkey.Enabled || !hotkey.IsComplete) continue;
            if (!DispCtrl.Core.FeatureFlags.Presets && hotkey.Action == HotkeyAction.ApplyPreset) continue;

            // MOD_NOREPEAT: holding the combination fires once, not forty times.
            // Without it a held shortcut walks brightness to an end stop.
            const uint NoRepeat = 0x4000;

            if (PInvoke.RegisterHotKey(HWND.Null, id, (HOT_KEY_MODIFIERS)(hotkey.Modifiers | NoRepeat), hotkey.Key))
            {
                _registered[id] = hotkey;
                id++;
                taken++;
                registeredKeys.Add(hotkey.Describe());
            }
            else
            {
                // Almost always another application holding the combination.
                // Worth a line: a shortcut that silently does nothing is the
                // hardest kind of setting to diagnose.
                Log.Write($"hotkey {hotkey.Describe()} refused — something else has it");
                refused++;
                refusedKeys.Add(hotkey.Describe());
            }
        }

        // Per reload in beta and test; in stable only when a shortcut was refused.
        if (refused > 0 || (DispCtrl.Core.BuildInfo.Diagnostics && taken > 0)) Log.Write($"hotkeys: {taken} registered, {refused} refused");
        // For the Hotkeys page and `hotkeys list`: a refused shortcut used to
        // look set and silently do nothing.
        HotkeyStatus.Write(registeredKeys, refusedKeys);
    }

    private void Unregister()
    {
        foreach (int id in _registered.Keys) _ = PInvoke.UnregisterHotKey(HWND.Null, id);
        _registered.Clear();
    }

    private void Fire(int id)
    {
        if (!_registered.TryGetValue(id, out Hotkey? hotkey)) return;

        DispCtrlSettings settings;
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

    private void Act(Hotkey hotkey, DispCtrlSettings settings)
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
                // Saving the level used to be all this did, so with the app
                // closed the displays never moved.
                ApplyUnison(settings);
                break;
            }

            case HotkeyAction.UnisonToggle:
                settings.Global.UnisonBrightness = !settings.Global.UnisonBrightness;
                _persist(settings);
                // Off leaves the displays where they are; on brings them back
                // to where unison puts them.
                if (settings.Global.UnisonBrightness) ApplyUnison(settings);
                break;

            case HotkeyAction.FocusToggle:
                settings.Global.Focus.Enabled = !settings.Global.Focus.Enabled;
                _persist(settings);
                break;

            case HotkeyAction.OledCareToggle:
                settings.Global.OledCare.Enabled = !settings.Global.OledCare.Enabled;
                _persist(settings);
                break;

            case HotkeyAction.OledRestNow:
            {
                int rested = 0;
                foreach (DisplayInfo d in Targets(hotkey))
                {
                    MonitorSettings m = settings.For(d.Token);
                    if (m.IsOled != true) continue;
                    m.OledRestUntilUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, m.OledRestMinutes));
                    rested++;
                }
                if (rested > 0) _persist(settings);
                else Log.Write("hotkey: no display marked as OLED to rest");
                break;
            }

            case HotkeyAction.KeepAwakeToggle:
                settings.Global.Awake.Mode = settings.Global.Awake.Mode == AwakeMode.PowerPlan ? AwakeMode.Indefinite : AwakeMode.PowerPlan;
                _persist(settings);
                break;

            case HotkeyAction.RestoreDisplays:
                settings.RestoreVisibility();
                _persist(settings);
                Log.Write("hotkey: every display put back (dimming, night light, hiding, displays off); Settings > Undo the way back, or dispctrl restore undo, reverses it");
                break;

            case HotkeyAction.DisplaysOffToggle:
                settings.Global.Awake.DisplaysOffUtc = settings.Global.Awake.DisplaysOffUtc is null ? DateTimeOffset.UtcNow : null;
                _persist(settings);
                break;

            case HotkeyAction.StayActiveToggle:
                settings.Global.Awake.StayActive = !settings.Global.Awake.StayActive;
                _persist(settings);
                break;

            case HotkeyAction.DarkModeToggle:
                WindowsTheme.SetDark(!(WindowsTheme.IsDark ?? false));
                break;

            case HotkeyAction.QuickPanel:
                // The hotkey makes this process the one allowed to take the
                // foreground; the panel is another, and needs it passed on.
                _ = Windows.Win32.PInvoke.AllowSetForegroundWindow(Windows.Win32.PInvoke.ASFW_ANY);
                if (!QuickPanelSignal.Summon()) Log.Write("hotkey: the quick panel needs the app, which could not be found");
                break;

            case HotkeyAction.TaskbarToggle:
            {
                // One answer for every target: if any is hidden, show them all.
                List<DisplayInfo> targets = Targets(hotkey);
                bool anyHidden = targets.Any(d => d.IsPrimary ? GlobalTaskbar.IsAutoHide : settings.For(d.Token).HideTaskbar);
                foreach (DisplayInfo d in targets)
                {
                    if (d.IsPrimary) GlobalTaskbar.SetAutoHide(!anyHidden);
                    else settings.For(d.Token).HideTaskbar = !anyHidden;
                }
                _persist(settings);
                break;
            }

            case HotkeyAction.TaskbarGlassToggle:
                settings.Global.TaskbarGlassEnabled = !settings.Global.TaskbarGlassEnabled;
                _persist(settings);
                break;

            case HotkeyAction.ContrastUp:
            case HotkeyAction.ContrastDown:
            {
                int delta = hotkey.Action == HotkeyAction.ContrastUp ? hotkey.Step : -hotkey.Step;
                foreach (DisplayInfo d in Targets(hotkey))
                {
                    if (d.IsInternal) continue;
                    VcpControl? contrast = MonitorCapabilities.ReadSettable(d).Controls.FirstOrDefault(c => c.Code == 0x12 && c.Settable);
                    if (contrast is null || contrast.Current < 0) continue;
                    int max = contrast.Maximum > 0 ? contrast.Maximum : 100;
                    int step = (int)Math.Round(max * delta / 100.0);
                    _ = MonitorCapabilities.Write(d, 0x12, (uint)Math.Clamp(contrast.Current + step, 0, max));
                }
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
                if (!DispCtrl.Core.FeatureFlags.Presets) break;
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
                // The overlays are XAML, so the app draws them; the engine only
                // asks, and starts the app hidden if none is running.
                if (!QuickPanelSignal.Identify()) Log.Write("hotkey: identify needs the app, which could not be found");
                break;

            case HotkeyAction.PinWindow:
            {
                // The window in front when the keys went down: the hotkey does
                // not take the foreground from it.
                nint window = Display.Placement.WindowPins.Foreground();
                Display.Placement.PinOutcome outcome = Display.Placement.WindowPins.Toggle(window, settings.Global.Pin);
                Log.Write($"hotkey: {outcome.Message}");
                break;
            }

            case HotkeyAction.UnpinAllWindows:
                Log.Write($"hotkey: {Display.Placement.WindowPins.UnpinAll()} window(s) unpinned");
                break;

            case HotkeyAction.GatherWindows:
            {
                // Off the pump: moving a desk of windows is a second or two of
                // other programs answering, and every other shortcut waits on this thread.
                int wanted = hotkey.Display;
                PlacementSettings placement = settings.Global.Placement;
                _ = Task.Run(() =>
                {
                    try
                    {
                        List<DisplayInfo> displays = Displays();
                        DisplayInfo? target = wanted > 0
                            ? (wanted <= displays.Count ? displays[wanted - 1] : null)
                            : Display.Placement.WindowMover.Active(placement, displays);
                        if (target is null) { Log.Write($"hotkey: no display {wanted} to gather windows onto"); return; }
                        Display.Placement.GatherOutcome gathered = Display.Placement.WindowMover.Gather(target, placement);
                        Log.Write($"hotkey: {gathered.Moved} window(s) gathered onto {target.Label}"
                            + (gathered.Skipped.Count > 0 ? $"; left: {string.Join("; ", gathered.Skipped)}" : ""));
                    }
                    catch (Exception ex) { Log.Write($"hotkey: gathering windows failed: {ex.Message}"); }
                });
                break;
            }
        }
    }

    /// <summary>Puts every display where unison says, as the app's slider would.</summary>
    /// <remarks>
    /// The built-in panel included, through its own range: with Windows'
    /// brightness following unison, the event this write raises matches where
    /// unison has the panel and the bridge drops it.
    /// </remarks>
    private void ApplyUnison(DispCtrlSettings settings)
    {
        if (Color.UnisonWriter.Apply(settings, Displays())) _persist(settings);
    }

    private void Step(Hotkey hotkey, DispCtrlSettings settings, int delta)
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
