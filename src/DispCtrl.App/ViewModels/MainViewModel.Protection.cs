using DispCtrl.Core.Settings;
using Microsoft.UI.Xaml;

namespace DispCtrl.App.ViewModels;

public sealed partial class MainViewModel
{
    private FocusSettings Focus => _settings.Global.Focus;
    private OledCareSettings Care => _settings.Global.OledCare;
    public bool FocusEnabled { get => Focus.Enabled; set { if (Focus.Enabled == value) return; Focus.Enabled = value; SaveProtection(); } }
    public double FocusDim { get => Focus.DimPercent; set { int v = Number(value, 0, 100); if (Focus.DimPercent == v) return; Focus.DimPercent = v; SaveProtection(); } }
    public double FocusDelay { get => Focus.DelayMs; set { int v = Number(value, 0, 10000); if (Focus.DelayMs == v) return; Focus.DelayMs = v; SaveProtection(); } }
    public double FocusFade { get => Focus.FadeMs; set { int v = Number(value, 0, 2000); if (Focus.FadeMs == v) return; Focus.FadeMs = v; SaveProtection(); } }
    public bool FocusOledOnly { get => Focus.OledOnly; set { if (Focus.OledOnly == value) return; Focus.OledOnly = value; SaveProtection(); } }
    public bool FocusPerMonitor { get => Focus.PerMonitorFocus; set { if (Focus.PerMonitorFocus == value) return; Focus.PerMonitorFocus = value; SaveProtection(); } }
    public bool FocusKeepHoveredClear { get => Focus.KeepHoveredClear; set { if (Focus.KeepHoveredClear == value) return; Focus.KeepHoveredClear = value; SaveProtection(); } }

    public string[] WindowTransitions { get; } = ["None", "Fade the brightness", "Slide the shape"];

    /// <summary>How a switch between windows is shown, as one choice of three.</summary>
    /// <remarks>
    /// Two switches let both be turned on at once, and both at once is the worst
    /// of the pair: the shape travels and cross-fades at the same time. Stored as
    /// the two flags it was, so an older settings file still loads.
    /// </remarks>
    private bool _transitionReady;

    public int WindowTransition
    {
        get { _transitionReady = true; return Focus.CrossFadeWindows ? 1 : Focus.EaseBetweenWindows ? 2 : 0; }
        set
        {
            // A ComboBox reports -1 while its items are still being filled, and
            // pushes its own index back before the bound value has reached it -
            // which would quietly write the first entry over the real setting.
            // Nothing is accepted until the value has been read out at least once.
            if (!_transitionReady || value < 0 || value == WindowTransition) return;

            Focus.CrossFadeWindows = value == 1;
            Focus.EaseBetweenWindows = value == 2;
            SaveProtection();
        }
    }
    public bool FocusScaleWithBrightness { get => Focus.ScaleWithBrightness; set { if (Focus.ScaleWithBrightness == value) return; Focus.ScaleWithBrightness = value; SaveProtection(); } }
    public bool FocusFollowMouse { get => Focus.FollowMouse; set { if (Focus.FollowMouse == value) return; Focus.FollowMouse = value; SaveProtection(); } }
    public bool FocusPrioritizeNewWindows { get => Focus.PrioritizeNewWindows; set { if (Focus.PrioritizeNewWindows == value) return; Focus.PrioritizeNewWindows = value; SaveProtection(); } }
    public bool FocusOtherMonitors { get => Focus.DimOtherMonitors; set { if (Focus.DimOtherMonitors == value) return; Focus.DimOtherMonitors = value; SaveProtection(); } }
    public bool FocusPauseFullscreen { get => Focus.PauseFullscreen; set { if (Focus.PauseFullscreen == value) return; Focus.PauseFullscreen = value; SaveProtection(); } }
    public bool FocusKeepTaskbar { get => Focus.KeepTaskbarVisible; set { if (Focus.KeepTaskbarVisible == value) return; Focus.KeepTaskbarVisible = value; SaveProtection(); } }
    public string FocusExcludedApps { get => Focus.ExcludedApps; set { if (Focus.ExcludedApps == value) return; Focus.ExcludedApps = value; SaveProtection(); } }
    public bool OledIdleEnabled { get => Care.Enabled; set { if (Care.Enabled == value) return; Care.Enabled = value; SaveProtection(); } }
    public double OledIdleMinutes { get => Care.IdleMinutes; set { int v = Number(value, 1, 120); if (Care.IdleMinutes == v) return; Care.IdleMinutes = v; SaveProtection(); } }
    public double OledIdleDim { get => Care.DimPercent; set { int v = Number(value, 0, 100); if (Care.DimPercent == v) return; Care.DimPercent = v; SaveProtection(); PreviewOled(v); Raise(nameof(OledSecondStageVisibility)); Raise(nameof(OledSecondStageDim)); } }
    public Visibility OledSecondStageVisibility => Care.DimPercent is > 0 and < 100 ? Visibility.Visible : Visibility.Collapsed;
    public bool OledSecondStageEnabled { get => Care.SecondStageEnabled; set { if (Care.SecondStageEnabled == value) return; Care.SecondStageEnabled = value; SaveProtection(); } }
    public double OledSecondStageMinutes { get => Care.SecondStageMinutes; set { int v = Number(value, 1, 120); if (Care.SecondStageMinutes == v) return; Care.SecondStageMinutes = v; SaveProtection(); } }
    public double OledSecondStageDim { get => Math.Clamp(Care.SecondStageDimPercent, Care.DimPercent, 100); set { int v = Number(value, Care.DimPercent, 100); if (Care.SecondStageDimPercent == v) return; Care.SecondStageDimPercent = v; SaveProtection(); PreviewOled(v); } }

    private int _oledPreviewVersion;
    private async void PreviewOled(int percent)
    {
        int version = ++_oledPreviewVersion;
        if (!Care.Enabled) return;
        // Startup may still be creating the message window. Only the latest
        // slider value is allowed to retry; preview requests never hit disk.
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (version != _oledPreviewVersion || !Care.Enabled) return;
            if (DispCtrl.Display.OledPreview.TryShow(percent)) return;
            await Task.Delay(100);
        }
    }
    public double OledIdleFade { get => Care.FadeMs; set { int v = Number(value, 0, 2000); if (Care.FadeMs == v) return; Care.FadeMs = v; SaveProtection(); } }
    public bool OledPauseFullscreen { get => Care.PauseFullscreen; set { if (Care.PauseFullscreen == value) return; Care.PauseFullscreen = value; SaveProtection(); } }
    public double TaskbarOpacity { get => _settings.Global.TaskbarOpacity; set { int v = Number(value, 0, 100); if (_settings.Global.TaskbarOpacity == v) return; _settings.Global.TaskbarOpacity = v; SaveProtection(); } }
    public bool TaskbarGlassEnabled { get => _settings.Global.TaskbarGlassEnabled; set { if (_settings.Global.TaskbarGlassEnabled == value) return; _settings.Global.TaskbarGlassEnabled = value; SaveProtection(); Raise(nameof(TaskbarGlassStatus)); } }
    public double TaskbarGlassRadius { get => _settings.Global.TaskbarGlassRadius; set { int v = Number(value, 0, 100); if (_settings.Global.TaskbarGlassRadius == v) return; _settings.Global.TaskbarGlassRadius = v; SaveProtection(); } }
    public double TaskbarGlassTint { get => _settings.Global.TaskbarGlassTint; set { int v = Number(value, 0, 100); if (_settings.Global.TaskbarGlassTint == v) return; _settings.Global.TaskbarGlassTint = v; SaveProtection(); } }
    public string TaskbarGlassStatus
    {
        get
        {
            if (!TaskbarGlassEnabled) return "Explorer compositor blur is off.";
            try
            {
                string path = Path.Combine(SettingsStore.Directory, "taskbar-glass.status");
                if (File.Exists(path))
                {
                    string status = File.ReadAllText(path).Trim();
                    if (status.Length > 0 && !string.Equals(status, "off", StringComparison.OrdinalIgnoreCase)) return status;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return "Explorer compositor blur is enabled; waiting for the engine.";
        }
    }

    public void ResetTaskbarSurface()
    {
        GlobalSettings fresh = new();
        _settings.Global.TaskbarOpacity = fresh.TaskbarOpacity;
        _settings.Global.TaskbarGlassEnabled = fresh.TaskbarGlassEnabled;
        _settings.Global.TaskbarGlassRadius = fresh.TaskbarGlassRadius;
        _settings.Global.TaskbarGlassTint = fresh.TaskbarGlassTint;

        bool transparency = WindowsTaskbarAppearance.SetTransparency(true);
        AppearanceStatus = transparency
            ? "Taskbar surface restored to its defaults."
            : "Taskbar surface settings restored; Windows transparency could not be changed.";
        SaveProtection();
    }

    public void ResetTaskbarFeatures()
    {
        bool ok = WindowsTaskbarAppearance.ResetToDefaults();
        AppearanceStatus = ok
            ? "Windows taskbar preferences restored to their defaults."
            : "Some Windows taskbar preferences could not be restored.";
        Raise(nameof(AppearanceStatus));
        RaiseProtectionSettings();
    }

    public void ResetTaskbarBehaviour()
    {
        GlobalSettings fresh = new();
        _settings.Global.HideDelayMs = fresh.HideDelayMs;
        _settings.Global.AnimMs = fresh.AnimMs;
        _settings.Global.RevealPx = fresh.RevealPx;
        Persist();
        foreach (string name in new[] { nameof(HideDelayMs), nameof(AnimMs), nameof(AnimateTaskbar), nameof(AnimMsEnabled), nameof(RevealPx) }) Raise(name);
    }
    public bool WindowsTransparency
    {
        get => WindowsTaskbarAppearance.Transparency;
        set { if (value == WindowsTransparency) return; AppearanceStatus = WindowsTaskbarAppearance.SetTransparency(value) ? "" : "Windows could not change transparency."; Raise(); Raise(nameof(AppearanceStatus)); }
    }
    public string[] SmallButtonChoices { get; } = ["Never", "Always", "When taskbar is full"];
    public string[] TaskbarAlignmentChoices { get; } = ["Left", "Center"];
    public string[] TaskbarCombineChoices { get; } = ["Always", "When taskbar is full", "Never"];
    public bool Windows11TaskbarSupported => WindowsTaskbarAppearance.Windows11Supported;
    public bool SmallButtonsSupported => WindowsTaskbarAppearance.SmallButtonsSupported;
    public int SmallTaskbarButtons
    {
        get => WindowsTaskbarAppearance.SmallButtons;
        set
        {
            if (value < 0 || value == SmallTaskbarButtons) return;
            AppearanceStatus = WindowsTaskbarAppearance.SetSmallButtons(value)
                ? "Preference saved. Windows controls the resulting size; if it has not updated, use Taskbar settings or sign out and back in."
                : "Windows could not change the button size. Use Taskbar settings.";
            Raise(); Raise(nameof(AppearanceStatus));
        }
    }
    public int TaskbarAlignment
    {
        get => WindowsTaskbarAppearance.Alignment;
        set { if (value < 0 || value == TaskbarAlignment) return; SetWindowsTaskbar(() => WindowsTaskbarAppearance.SetAlignment(value)); Raise(); }
    }
    public int TaskbarCombineButtons
    {
        get => WindowsTaskbarAppearance.CombineButtons;
        set { if (value < 0 || value == TaskbarCombineButtons) return; SetWindowsTaskbar(() => WindowsTaskbarAppearance.SetCombineButtons(value)); Raise(); }
    }
    public int OtherTaskbarCombineButtons
    {
        get => WindowsTaskbarAppearance.CombineButtonsOtherDisplays;
        set { if (value < 0 || value == OtherTaskbarCombineButtons) return; SetWindowsTaskbar(() => WindowsTaskbarAppearance.SetCombineButtonsOtherDisplays(value)); Raise(); }
    }
    public bool TaskbarShowTaskView { get => WindowsTaskbarAppearance.ShowTaskView; set { if (value == TaskbarShowTaskView) return; SetWindowsTaskbar(() => WindowsTaskbarAppearance.SetShowTaskView(value)); Raise(); } }
    public bool TaskbarShowWidgets { get => WindowsTaskbarAppearance.ShowWidgets; set { if (value == TaskbarShowWidgets) return; SetWindowsTaskbar(() => WindowsTaskbarAppearance.SetShowWidgets(value)); Raise(); } }
    public bool TaskbarShowBadges { get => WindowsTaskbarAppearance.ShowBadges; set { if (value == TaskbarShowBadges) return; SetWindowsTaskbar(() => WindowsTaskbarAppearance.SetShowBadges(value)); Raise(); } }
    public bool TaskbarAllowFlashing { get => WindowsTaskbarAppearance.AllowFlashing; set { if (value == TaskbarAllowFlashing) return; SetWindowsTaskbar(() => WindowsTaskbarAppearance.SetAllowFlashing(value)); Raise(); } }
    public bool TaskbarShowDesktopCorner { get => WindowsTaskbarAppearance.ShowDesktopCorner; set { if (value == TaskbarShowDesktopCorner) return; SetWindowsTaskbar(() => WindowsTaskbarAppearance.SetShowDesktopCorner(value)); Raise(); } }

    private void SetWindowsTaskbar(Func<bool> apply)
    {
        AppearanceStatus = apply()
            ? "Windows taskbar preference saved. Explorer may take a moment to refresh."
            : "Windows could not change this taskbar preference.";
        Raise(nameof(AppearanceStatus));
    }
    public string AppearanceStatus { get; private set; } = "";
    /// <summary>Which displays the shared OLED settings actually reach.</summary>
    /// <remarks>
    /// The switch above it is one control for the whole desk, but the feature is
    /// per panel twice over: a display has to be marked as OLED and have its own
    /// protection left on. Without this the toggle reads as covering everything,
    /// and the only way to find out otherwise is to leave the machine idle and
    /// watch which screens go black.
    /// </remarks>
    public string OledCoverage
    {
        get
        {
            if (Displays.Count == 0) return "No displays detected.";

            string[] covered = [.. Displays.Where(d => d.IsOled && d.OledProtectionEnabled).Select(d => d.Number.ToString())];
            string[] notOled = [.. Displays.Where(d => !d.IsOled).Select(d => d.Number.ToString())];
            string[] switchedOff = [.. Displays.Where(d => d.IsOled && !d.OledProtectionEnabled).Select(d => d.Number.ToString())];

            var parts = new List<string>
            {
                covered.Length > 0
                    ? $"Rests display {string.Join(", ", covered)}."
                    : "Rests nothing: no display is both marked as OLED and left switched on.",
            };

            if (notOled.Length > 0) parts.Add($"Display {string.Join(", ", notOled)} is not marked as OLED.");
            if (switchedOff.Length > 0) parts.Add($"Display {string.Join(", ", switchedOff)} has its own protection off.");

            return string.Join(" ", parts);
        }
    }

    public string ProtectionStatus => EngineRunning
        ? "The engine keeps focus mode and OLED idle protection running when this window is closed."
        : "Enable a feature to start the engine. Protection needs the engine to stay running.";
    /// <summary>Puts focus mode's tuning back to the shipped defaults.</summary>
    /// <remarks>
    /// The switch itself is left where it is. Someone pressing reset is tuning
    /// the feature, not turning it off, and a reset that also switched it off
    /// would have to be undone every single time.
    /// </remarks>
    public void ResetFocusSettings()
    {
        _settings.Global.Focus = new FocusSettings { Enabled = Focus.Enabled };
        SaveProtection();
    }

    /// <summary>Puts the OLED rest settings back to the shipped defaults.</summary>
    /// <remarks>
    /// Shared settings only. Which panels are OLED, and whether each has its own
    /// protection on, belong to the displays and are reset from their own cards.
    /// </remarks>
    public void ResetOledSettings()
    {
        _settings.Global.OledCare = new OledCareSettings { Enabled = Care.Enabled };
        SaveProtection();
    }

    private static int Number(double value, int min, int max) => double.IsFinite(value) ? Math.Clamp((int)Math.Round(value), min, max) : min;
    private void SaveProtection()
    {
        Persist();
        if (Focus.Enabled || Care.Enabled || _settings.Global.TaskbarOpacity < 100 || _settings.Global.TaskbarGlassEnabled)
        {
            try { _engine.Start(); }
            catch (Exception ex) { AppearanceStatus = $"Could not start the engine: {ex.Message}"; }
        }
        RaiseProtectionSettings();
    }
    private void RaiseProtectionSettings()
    {
        foreach (string name in new[] { nameof(FocusEnabled), nameof(FocusDim), nameof(FocusDelay), nameof(FocusFade),
            nameof(FocusOledOnly), nameof(WindowTransition), nameof(FocusPerMonitor), nameof(FocusKeepHoveredClear), nameof(FocusScaleWithBrightness), nameof(FocusFollowMouse), nameof(FocusPrioritizeNewWindows), nameof(FocusOtherMonitors), nameof(FocusPauseFullscreen), nameof(FocusKeepTaskbar),
            nameof(FocusExcludedApps), nameof(OledIdleEnabled), nameof(OledIdleMinutes), nameof(OledIdleDim), nameof(OledSecondStageVisibility),
            nameof(OledSecondStageEnabled), nameof(OledSecondStageMinutes), nameof(OledSecondStageDim), nameof(OledIdleFade),
            nameof(OledPauseFullscreen), nameof(TaskbarOpacity), nameof(WindowsTransparency),
            nameof(SmallTaskbarButtons), nameof(TaskbarAlignment), nameof(TaskbarCombineButtons), nameof(OtherTaskbarCombineButtons),
            nameof(TaskbarShowTaskView), nameof(TaskbarShowWidgets), nameof(TaskbarShowBadges), nameof(TaskbarAllowFlashing),
            nameof(TaskbarShowDesktopCorner), nameof(TaskbarGlassEnabled), nameof(TaskbarGlassRadius), nameof(TaskbarGlassTint), nameof(TaskbarGlassStatus), nameof(AppearanceStatus), nameof(ProtectionStatus),
            nameof(OledCoverage) }) Raise(name);
    }
}
