using System.Collections.ObjectModel;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;
using DispCtrl.Display.Placement;
using Microsoft.UI.Xaml;

namespace DispCtrl.App.ViewModels;

/// <summary>One open window, as the pin lists show it.</summary>
public sealed class WindowItem(AppWindow window)
{
    public AppWindow Window { get; } = window;
    public nint Handle => Window.Handle;
    public string Title => Window.Title;
    public string Process => Window.Process;
    public bool Pinned => Window.Pinned;
    public string Detail => $"{Window.Process}{(Window.Show == WindowShow.Minimized ? "  ·  minimized" : "")}";
    public string AutomationName => $"Unpin {Window.Title}";
}

/// <summary>A monitor the DDC/CI guard stopped talking to, as the Settings page lists it.</summary>
public sealed class BlockedMonitor(DdcBlock block)
{
    public DdcBlock Block { get; } = block;
    public string Name => Block.Label.Length > 0 ? $"{Block.Label}  ({Block.Model})" : Block.Model;
    public string Reason => Block.Reason;
    public string AutomationName => $"Allow {Block.Model}";
}

public sealed partial class MainViewModel
{
    private PinSettings Pin => _settings.Global.Pin;
    private PlacementSettings Placement => _settings.Global.Placement;

    // ------------------------------------------------------------ pinning --

    public bool PinEnabled
    {
        get => Pin.Enabled;
        set
        {
            if (Pin.Enabled == value) return;
            Pin.Enabled = value;
            // Off takes every pin back, with or without the engine.
            if (!value) _ = Task.Run(WindowPins.UnpinAll).ContinueWith(_ => RefreshPinnedWindows(), TaskScheduler.FromCurrentSynchronizationContext());
            SaveWindows();
        }
    }

    public bool PinBorder { get => Pin.Border; set { if (Pin.Border == value) return; Pin.Border = value; SaveWindows(); } }
    public bool PinClearInFocus { get => Pin.ClearInFocus; set { if (Pin.ClearInFocus == value) return; Pin.ClearInFocus = value; SaveWindows(); } }
    public bool PinClearInOledCare { get => Pin.ClearInOledCare; set { if (Pin.ClearInOledCare == value) return; Pin.ClearInOledCare = value; SaveWindows(); } }
    public bool PinSkipFullscreen { get => Pin.SkipFullscreen; set { if (Pin.SkipFullscreen == value) return; Pin.SkipFullscreen = value; SaveWindows(); } }
    public bool PinStepAsideForFullscreen { get => Pin.StepAsideForFullscreen; set { if (Pin.StepAsideForFullscreen == value) return; Pin.StepAsideForFullscreen = value; SaveWindows(); } }
    public string PinExcludedApps { get => Pin.ExcludedApps; set { if (Pin.ExcludedApps == value) return; Pin.ExcludedApps = value ?? ""; SaveWindows(); } }

    public double PinBorderThickness
    {
        get => Math.Clamp(Pin.BorderThickness, 1, 16);
        set { int v = Number(value, 1, 16); if (Pin.BorderThickness == v) return; Pin.BorderThickness = v; PersistSoon(); Raise(); }
    }

    public double PinBorderOpacity
    {
        get => Math.Clamp(Pin.BorderOpacity, 20, 100);
        set { int v = Number(value, 20, 100); if (Pin.BorderOpacity == v) return; Pin.BorderOpacity = v; PersistSoon(); Raise(); }
    }

    /// <summary>Named colours for the border, the accent first; the value is what the settings file holds.</summary>
    private static readonly (string Name, string Value)[] BorderColours =
    [
        ("Windows accent colour", ""), ("Orange", "#FF8C00"), ("Red", "#E81123"), ("Green", "#10893E"),
        ("Blue", "#0078D4"), ("Purple", "#881798"), ("White", "#FFFFFF"),
    ];

    /// <summary>The choices, with a colour set elsewhere (the command line) kept as its own entry.</summary>
    public string[] PinBorderColours
    {
        get
        {
            string current = Pin.BorderColour.Trim();
            bool known = BorderColours.Any(c => string.Equals(c.Value, current, StringComparison.OrdinalIgnoreCase));
            return known ? BorderColours.Select(c => c.Name).ToArray()
                : [.. BorderColours.Select(c => c.Name), $"Custom ({current})"];
        }
    }

    private bool _pinColourReady;

    public int PinBorderColourIndex
    {
        get
        {
            _pinColourReady = true;
            int i = Array.FindIndex(BorderColours, c => string.Equals(c.Value, Pin.BorderColour.Trim(), StringComparison.OrdinalIgnoreCase));
            return i >= 0 ? i : BorderColours.Length;
        }
        set
        {
            // A ComboBox reports -1 while it fills; the custom entry is not a choice to make.
            if (!_pinColourReady || value < 0 || value >= BorderColours.Length || value == PinBorderColourIndex) return;
            Pin.BorderColour = BorderColours[value].Value;
            SaveWindows();
        }
    }

    /// <summary>The windows DispCtrl has pinned, re-read when asked: a window's pin is its own state.</summary>
    public ObservableCollection<WindowItem> PinnedWindows { get; } = [];

    public Visibility NoPinnedWindowsVisibility => PinnedWindows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public string PinShortcut
    {
        get
        {
            Hotkey? key = _settings.Hotkeys.FirstOrDefault(h => h.Action == HotkeyAction.PinWindow && h.Enabled && h.Key != 0);
            return key is null
                ? "No shortcut: add one for Pin the active window on the Hotkeys page, or pin from the quick panel."
                : $"{key.Describe()} pins the window in front, and unpins it again.";
        }
    }

    public void RefreshPinnedWindows()
    {
        List<AppWindow> pinned;
        try { pinned = WindowPins.List(); }
        catch (Exception) { pinned = []; }
        PinnedWindows.Clear();
        foreach (AppWindow w in pinned) PinnedWindows.Add(new WindowItem(w));
        Raise(nameof(NoPinnedWindowsVisibility));
    }

    /// <summary>Every window that could be pinned, for the quick panel's list.</summary>
    /// <remarks>
    /// Not the panel itself: it is this process's one window that is on top by
    /// itself, and listing it only offered a greyed switch for the list's own window.
    /// </remarks>
    public List<AppWindow> OpenWindows()
    {
        try
        {
            uint self = (uint)Environment.ProcessId;
            return AppWindows.List().Where(w => !(w.ProcessId == self && w.Topmost && !w.Pinned)).ToList();
        }
        catch (Exception) { return []; }
    }

    public string TogglePin(nint window)
    {
        PinOutcome outcome = WindowPins.Toggle(window, Pin);
        RefreshPinnedWindows();
        return outcome.Message;
    }

    public void Unpin(WindowItem item)
    {
        PinOutcome outcome = WindowPins.Unpin(item.Handle);
        ShowFooterStatus(outcome.Message);
        RefreshPinnedWindows();
    }

    public void UnpinAll()
    {
        int count = WindowPins.UnpinAll();
        ShowFooterStatus(count == 0 ? "Nothing was pinned." : $"{count} window{(count == 1 ? "" : "s")} unpinned.");
        RefreshPinnedWindows();
    }

    // ---------------------------------------------------------- placement --

    /// <summary>Putting windows back on a display that returns.</summary>
    public bool ReturnWindows
    {
        get => Placement.ReturnWindows;
        set
        {
            if (Placement.ReturnWindows == value) return;
            Placement.ReturnWindows = value;
            SaveWindows(needsEngine: value);
        }
    }

    public bool NewWindowsOnActive
    {
        get => Placement.NewWindowsOnActive;
        set { if (Placement.NewWindowsOnActive == value) return; Placement.NewWindowsOnActive = value; SaveWindows(needsEngine: value); }
    }

    public string[] ActiveDisplayChoices { get; } = ["The display with the pointer", "The display with the active window"];

    private bool _activeDisplayReady;

    public int ActiveDisplayIndex
    {
        get { _activeDisplayReady = true; return Placement.Active == ActiveDisplay.ActiveWindow ? 1 : 0; }
        set
        {
            if (!_activeDisplayReady || value is < 0 or > 1) return;
            ActiveDisplay wanted = value == 1 ? ActiveDisplay.ActiveWindow : ActiveDisplay.Pointer;
            if (Placement.Active == wanted) return;
            Placement.Active = wanted;
            SaveWindows();
        }
    }

    public bool PlacementKeepSize { get => Placement.KeepSize; set { if (Placement.KeepSize == value) return; Placement.KeepSize = value; SaveWindows(); } }
    public string PlacementExcludedApps { get => Placement.ExcludedApps; set { if (Placement.ExcludedApps == value) return; Placement.ExcludedApps = value ?? ""; SaveWindows(); } }

    /// <summary>Windows' own "Remember window locations based on monitor connection".</summary>
    public bool WindowsRemembersLocations
    {
        get => WindowsWindowMemory.Remember;
        set
        {
            if (WindowsWindowMemory.Remember == value) { Raise(); return; }
            try { WindowsWindowMemory.Remember = value; }
            catch (Exception ex) { ShowFooterStatus("Windows' setting could not be changed: " + ex.Message); }
            Raise();
        }
    }

    /// <summary>Windows' own "Minimize windows when a monitor is disconnected".</summary>
    public bool WindowsMinimizesOnDisconnect
    {
        get => WindowsWindowMemory.MinimizeOnDisconnect;
        set
        {
            if (WindowsWindowMemory.MinimizeOnDisconnect == value) return;
            try { WindowsWindowMemory.MinimizeOnDisconnect = value; }
            catch (Exception ex) { ShowFooterStatus("Windows' setting could not be changed: " + ex.Message); }
            Raise();
        }
    }

    public string GatherShortcut
    {
        get
        {
            Hotkey? key = _settings.Hotkeys.FirstOrDefault(h => h.Action == HotkeyAction.GatherWindows && h.Enabled && h.Key != 0);
            return key is null
                ? "Brings every window onto one display. No shortcut: add one for Gather windows on the Hotkeys page."
                : $"Brings every window onto one display. {key.Describe()} gathers them onto the display in use.";
        }
    }

    /// <summary>Moves every window onto one display, off the UI thread.</summary>
    /// <returns>What happened, in a sentence, for whichever surface asked.</returns>
    public async Task<string> GatherAsync(DisplayViewModel target)
    {
        ShowFooterStatus($"Gathering every window onto {target.Name}…", busy: true);
        string said;
        try
        {
            PlacementSettings placement = Placement;
            GatherOutcome outcome = await Task.Run(() => WindowMover.Gather(target.Info, placement)).ConfigureAwait(true);
            said = GatherText(outcome, target.Name);
        }
        catch (Exception ex) { said = "Gathering windows failed: " + ex.Message; }
        ShowFooterStatus(said);
        return said;
    }

    /// <summary>The display the pointer or the active window is on, as the setting says.</summary>
    public DisplayViewModel? ActiveDisplayNow()
    {
        DisplayInfo? info = WindowMover.Active(Placement, Displays.Select(d => d.Info).ToList());
        return info is null ? null : Displays.FirstOrDefault(d => d.Token == info.Token);
    }

    public static string GatherText(GatherOutcome outcome, string where) =>
        (outcome.Moved == 0 ? $"Every window was already on {where}." : $"{outcome.Moved} window{(outcome.Moved == 1 ? "" : "s")} brought onto {where}.")
        + (outcome.Skipped.Count > 0 ? " Left where they were: " + string.Join("; ", outcome.Skipped) + "." : "");

    // --------------------------------------------------- the DDC/CI guard --

    public bool DdcGuardEnabled
    {
        get => _settings.Global.DdcGuard.Enabled;
        set
        {
            if (_settings.Global.DdcGuard.Enabled == value) return;
            _settings.Global.DdcGuard.Enabled = value;
            Persist();
            DdcGuard.Invalidate();
            Raise();
            foreach (DisplayViewModel d in Displays) d.NotifySettingsReloaded();
        }
    }

    public ObservableCollection<BlockedMonitor> BlockedMonitors { get; } = [];

    public Visibility BlockedMonitorsVisibility => BlockedMonitors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string DdcGuardSummary => _settings.Global.DdcGuard.Blocked.Count switch
    {
        0 => "No monitor has taken Windows down while DispCtrl read it.",
        1 => "One monitor is blocked: DispCtrl no longer talks to it over DDC/CI.",
        int n => $"{n} monitors are blocked: DispCtrl no longer talks to them over DDC/CI.",
    };

    public void RefreshBlockedMonitors()
    {
        BlockedMonitors.Clear();
        foreach (DdcBlock block in _settings.Global.DdcGuard.Blocked) BlockedMonitors.Add(new BlockedMonitor(block));
        Raise(nameof(BlockedMonitorsVisibility));
        Raise(nameof(DdcGuardSummary));
    }

    public async Task AllowDdcAsync(BlockedMonitor monitor)
    {
        await Task.Run(() => DdcGuard.Allow(monitor.Block.Token)).ConfigureAwait(true);
        _settings.Global.DdcGuard.Blocked.RemoveAll(b => b.Token == monitor.Block.Token);
        RefreshBlockedMonitors();
        DisplayViewModel? display = Displays.FirstOrDefault(d => d.Token == monitor.Block.Token);
        if (display is not null) await display.AllowDdcAsync();
        ShowFooterStatus($"DispCtrl talks to {monitor.Name} over DDC/CI again.");
    }

    // ------------------------------------------------------- the tray wheel --

    public string[] TrayWheelChoices { get; } = ["Off", "The main display", "Every display"];

    private bool _trayWheelReady;

    public int TrayWheelIndex
    {
        get { _trayWheelReady = true; return (int)_settings.Global.QuickPanel.TrayWheel; }
        set
        {
            if (!_trayWheelReady || value is < 0 or > 2 || (int)_settings.Global.QuickPanel.TrayWheel == value) return;
            _settings.Global.QuickPanel.TrayWheel = (TrayWheelTarget)value;
            SaveQuickPanel(composition: false);
            Raise(nameof(TrayWheelIndex));
        }
    }

    public double WheelStep
    {
        get => Math.Clamp(_settings.Global.QuickPanel.WheelStep, QuickPanelSettings.MinWheelStep, QuickPanelSettings.MaxWheelStep);
        set
        {
            int v = Number(value, QuickPanelSettings.MinWheelStep, QuickPanelSettings.MaxWheelStep);
            if (_settings.Global.QuickPanel.WheelStep == v) return;
            _settings.Global.QuickPanel.WheelStep = v;
            PersistSoon();
            Raise();
        }
    }

    public bool WheelOnSliders
    {
        get => _settings.Global.QuickPanel.WheelOnSliders;
        set
        {
            if (_settings.Global.QuickPanel.WheelOnSliders == value) return;
            _settings.Global.QuickPanel.WheelOnSliders = value;
            SaveQuickPanel();
            Raise();
        }
    }

    // ------------------------------------------------- theme on the schedule --

    /// <summary>Dark mode for the night light's scheduled hours, light after.</summary>
    /// <remarks>Set again from scratch when switched on, so it takes effect now rather than at the next boundary.</remarks>
    public bool NightLightDarkModeOnSchedule
    {
        get => Night.DarkModeOnSchedule;
        set
        {
            if (Night.DarkModeOnSchedule == value) return;
            Night.DarkModeOnSchedule = value;
            Night.ThemeAppliedUtc = null;
            Persist();
            if (value) { try { _engine.Start(); } catch (Exception) { } }
            Raise();
            Raise(nameof(NightLightThemeDescription));
        }
    }

    public string NightLightThemeDescription => !Night.Scheduled
        ? "Switch the schedule on to use its hours for the theme too."
        : $"Windows goes dark at {Clock(Night.FromMinutes)} and light at {Clock(Night.ToMinutes)}, once each, so a theme you choose in between stays until the next.";

    // ---------------------------------------------------------------- saving --

    private void SaveWindows(bool needsEngine = false)
    {
        Persist();
        if (needsEngine) { try { _engine.Start(); } catch (Exception ex) { ShowFooterStatus($"Could not start the engine: {ex.Message}"); } }
        RaiseWindows();
    }

    private void RaiseWindows()
    {
        foreach (string name in new[]
        {
            nameof(PinEnabled), nameof(PinBorder), nameof(PinClearInFocus), nameof(PinClearInOledCare), nameof(PinSkipFullscreen), nameof(PinStepAsideForFullscreen),
            nameof(PinExcludedApps), nameof(PinBorderThickness), nameof(PinBorderOpacity), nameof(PinBorderColours), nameof(PinBorderColourIndex),
            nameof(PinShortcut), nameof(ReturnWindows), nameof(NewWindowsOnActive), nameof(ActiveDisplayIndex), nameof(PlacementKeepSize),
            nameof(PlacementExcludedApps), nameof(WindowsRemembersLocations),
            nameof(WindowsMinimizesOnDisconnect), nameof(GatherShortcut),
        }) Raise(name);
    }
}
