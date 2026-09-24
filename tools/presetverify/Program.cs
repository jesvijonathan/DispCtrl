using DispCtrl.Core.Presets;
using DispCtrl.Core.Settings;

// No hardware writes, user presets or settings. Exercise files through the in-memory parser.
int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS {name}");
    passed++;
}
void Reject(string json, string name)
{
    bool rejected = false;
    try { PresetStore.Parse(json); }
    catch (Exception e) when (e is FormatException or System.Text.Json.JsonException) { rejected = true; }
    Check(rejected, name);
}
var saved = new Preset
{
    Name = "Evening", Description = "Shared example", IncludeGlobal = false, IncludeLayout = false,
    Monitors = new() { ["panel-a"] = new() { Brightness = 42, MonitorControls = new() { ["0x12"] = 70 } } },
};
var parsed = PresetStore.Parse(PresetStore.ToJson(saved));
Check(parsed.Name == saved.Name && parsed.Description == saved.Description, "JSON metadata round trip");
Check(!parsed.IncludeGlobal && !parsed.IncludeLayout, "monitor-only scope round trip");
Check(parsed.Monitors["panel-a"].MonitorControls["0x12"] == 70, "hardware control values round trip");
var completeExport = new CurrentConfigurationExport
{
    Settings = new DispCtrlSettings { Global = new GlobalSettings { TaskbarOpacity = 37 } },
    CurrentDesk = new Preset
    {
        Name = "Current configuration",
        Monitors = new() { ["DEL-A234-SERIAL"] = new() { Serial = "SERIAL", Brightness = 64 } },
    },
};
string completeJson = System.Text.Json.JsonSerializer.Serialize(
    completeExport, PresetJsonContext.Default.CurrentConfigurationExport);
var completeRoundTrip = System.Text.Json.JsonSerializer.Deserialize(
    completeJson, PresetJsonContext.Default.CurrentConfigurationExport)!;
Check(completeRoundTrip.Settings.Global.TaskbarOpacity == 37
    && completeRoundTrip.CurrentDesk.Monitors["DEL-A234-SERIAL"].Serial == "SERIAL",
    "complete export retains shared settings, monitor identity and live state");
Check(new FocusSettings().PrioritizeNewWindows, "new and activated windows are followed by default");
var careCheck = new OledCareSettings { Enabled = true, IdleMinutes = 1, DimPercent = 50,
    SecondStageEnabled = true, SecondStageMinutes = 2, SecondStageDimPercent = 95 };
Check(DispCtrl.Core.Displays.FocusGeometry.RestingWhenIdle(careCheck.Enabled, true, 60_000, 1, false, false),
    "OLED idle activation has no focus-mode dependency");
Check(careCheck.DimAtIdle(60_000) == 50 && careCheck.DimAtIdle(179_999) == 50
    && careCheck.DimAtIdle(180_000) == 95, "OLED stages use their selected levels and additional delay");
var oledBounds = new DispCtrl.Core.Displays.DisplayRect(0, 0, 1920, 1080);
var panelIdleState = new DispCtrl.Core.Displays.OledIdleState();
uint PanelAge(long now, uint idle, int x, int y, bool enabled = true, bool sticky = true, bool pointer = true) =>
    panelIdleState.Update(enabled, sticky, now, true, idle, 1, pointer, x, y, oledBounds);
Check(PanelAge(60_000, 60_000, 2000, 100) == 60_000, "pointer-return mode enters rest after normal idle threshold");
Check(PanelAge(90_000, 0, 2100, 100) == 90_000, "movement on another panel does not wake an idle OLED");
Check(PanelAge(180_000, 0, 2100, 100) == 180_000, "keyboard activity elsewhere preserves the idle age for second-stage dimming");
Check(PanelAge(181_000, 0, 100, 100) == 0, "pointer returning to the OLED wakes it");
Check(PanelAge(182_000, 182_000, 100, 100) == 1000, "programmatic pointer return restarts the idle wait even without Windows input");
Check(PanelAge(242_000, 60_000, 100, 100) == 60_000, "a stationary pointer does not prevent a new idle rest");
Check(PanelAge(243_000, 0, 101, 100) == 0, "movement within an idle OLED wakes it too");
PanelAge(303_000, 60_000, 2100, 100);
Check(PanelAge(304_000, 0, 2101, 100, sticky: false) == 0, "turning pointer-return mode off restores normal global-input wake");
PanelAge(364_000, 60_000, 2100, 100);
Check(PanelAge(365_000, 0, 2100, 100, enabled: false) == 0, "disabling protection clears a held rest");
PanelAge(425_000, 60_000, 2100, 100);
Check(PanelAge(426_000, 0, 0, 0, pointer: false) == 0, "unreadable pointer fails open rather than trapping a dimmed panel");
var wakeSettings = new MonitorSettings { OledWakeOnPointerReturn = true };
completeExport.Settings.Monitors["test-panel"] = wakeSettings;
var wakeJson = System.Text.Json.JsonSerializer.Serialize(completeExport, PresetJsonContext.Default.CurrentConfigurationExport);
Check(System.Text.Json.JsonSerializer.Deserialize(wakeJson, PresetJsonContext.Default.CurrentConfigurationExport)!
    .Settings.Monitors["test-panel"].OledWakeOnPointerReturn, "per-monitor wake preference survives JSON export");
wakeSettings.ResetToDefaults();
Check(!wakeSettings.OledWakeOnPointerReturn, "monitor reset restores the default wake behavior");
Check(!DispCtrl.Core.Displays.FocusGeometry.IsContentFullscreen(oledBounds, oledBounds, true, true),
    "maximized ordinary window over reclaimed taskbar space does not pause OLED protection");
Check(DispCtrl.Core.Displays.FocusGeometry.IsContentFullscreen(oledBounds, oledBounds, true, false),
    "borderless fullscreen still pauses protection even with maximized window state");
Check(!DispCtrl.Core.Displays.FocusGeometry.IsContentFullscreen(oledBounds,
    new(1920, 0, 3840, 1080), false, false), "fullscreen content on another monitor does not pause this OLED");
var sampleRecord = new DispCtrl.Display.Devices.DeviceSubmission
{
    Manufacturer = "DEL", Product = "A234", Model = "U2424H", Connector = "Hdmi", PanelTechnology = "LCD",
    ActiveSignalMode = "1920 x 1080 @ 120 Hz", PixelClock = "297 MHz", ControllerType = "0x09",
    PixelDensity = 93, EffectiveDpi = 96, Commands = ["0x01", "0x02"]
}.ToRepositoryMarkdown();
Check(sampleRecord.Contains("297 MHz") && sampleRecord.Contains("0x09")
    && sampleRecord.Contains("93 PPI") && sampleRecord.Contains("100% scaling")
    && sampleRecord.Contains("1920 x 1080 @ 120 Hz") && sampleRecord.Contains("0x01 0x02")
    && sampleRecord.Contains("may differ between setups"), "public report includes controller, signal, density and commands with observed-state label");
Check(DispCtrl.Core.Devices.DeviceLibrary.Shipped("DEL-A234")?.Controls.Any(c => c.Code == "0x66") == true
    && File.Exists(Path.Combine(AppContext.BaseDirectory, "devices", "DEL", "A234", "definition.json"))
    && !Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "devices"), "*.md", SearchOption.AllDirectories).Any(),
    "the shipped library resolves a model from its own folder, and ships definitions, not records");
Check(PresetStore.Parse("{ /* editable */ \"monitors\": {}, }").Version == 3, "comments and trailing commas");
Check(PresetStore.Parse("{\"version\":2,\"monitors\":{}}").IncludeGlobal, "legacy whole-desk scope preserved");
Reject("{}", "unrelated JSON rejected");
Reject("{\"version\":99,\"monitors\":{}}", "future version rejected");
Reject("{\"monitors\":null}", "null monitors rejected");
Reject("{\"global\":null,\"monitors\":{}}", "null global rejected");
Reject("{\"monitors\":{\"x\":null}}", "null monitor state rejected");
Reject("{\"monitors\":{\"x\":{\"brightness\":101}}}", "invalid brightness rejected");
Reject("{\"monitors\":{\"x\":{\"orientationDegrees\":45}}}", "invalid rotation rejected");
Reject("{\"monitors\":{\"x\":{\"monitorControls\":{\"0x12\":-1}}}}", "negative VCP value rejected");
Reject("{\"monitors\":{\"x\":{\"monitorControls\":null}}}", "null controls rejected");
Reject("{\"monitors\":{\"x\":{\"primary\":true},\"y\":{\"primary\":true}}}", "ambiguous primary rejected");
Reject("{\"global\":{\"nightLightFrom\":1440},\"monitors\":{}}", "invalid schedule rejected");
var live = PresetStore.Parse(PresetStore.ToJson(saved));
live.Global.NightLightStrength = 95;
live.Monitors["panel-a"].X = 1920;
live.Monitors["panel-a"].Primary = true;
live.Monitors["panel-b"] = new() { Brightness = 80 };
Check(PresetDiff.Describe(saved, live).Count == 0, "monitor scope ignores globals, positions and unrelated panels");
live.Monitors["panel-a"].Brightness = 75;
Check(PresetDiff.Describe(saved, live).Any(d => d.What == "Brightness"), "monitor scope detects captured value changes");
saved.Monitors["offline"] = new() { Brightness = 30 };
var fresh = PresetValidation.RetainScope(live, saved);
Check(!fresh.IncludeGlobal && !fresh.IncludeLayout && fresh.Monitors.Count == 2,
    "recapture retains scope and excludes newly attached monitors");
Check(fresh.Monitors["offline"].Brightness == 30, "recapture preserves disconnected monitor state");
var whole = new Preset { Name = "Desk" };
var changed = PresetStore.Parse(PresetStore.ToJson(whole));
changed.Global.NightLightStrength++;
changed.Global.UnisonLevel--;
changed.Global.NightLightFrom++;
Check(PresetDiff.Describe(whole, changed).Count == 3, "disabled features still preserve their stored values");
var rule = new AppRule { Process = "Game.exe", Preset = "Gaming", RestorePrevious = true, DwellSeconds = 1.5 };
Check(rule.Matches("game"), "app names match case-insensitively without extension");
rule.Enabled = false;
Check(!rule.Matches("game"), "disabled rules cannot match");
var settings = new DispCtrlSettings { AppRules = [rule] };
string settingsJson = System.Text.Json.JsonSerializer.Serialize(settings, SettingsJsonContext.Default.DispCtrlSettings);
var restored = System.Text.Json.JsonSerializer.Deserialize(settingsJson, SettingsJsonContext.Default.DispCtrlSettings)!;
Check(restored.AppRules[0].RestorePrevious && restored.AppRules[0].DwellSeconds == 1.5, "app restoration and dwell persist");
Check(PresetText.Describe(saved).Contains("0x12"), "human-readable description includes monitor controls");
var oldSettings = new DispCtrlSettings();
oldSettings.For("panel-a").SoftwareBrightness = 60;
oldSettings.Global.UnisonLevel = 20;
var newerSettings = new DispCtrlSettings { AppRules = [new() { Process = "new-app", Preset = "New rule" }] };
newerSettings.Global.UnisonLevel = 90;
newerSettings.For("panel-b").SoftwareBrightness = 80;
var merged = PresetSettings.Merge(saved, oldSettings, newerSettings);
Check(merged.AppRules[0].Process == "new-app", "commit preserves rules edited during apply");
Check(merged.Global.UnisonLevel == 90, "monitor-only commit preserves current shared settings");
Check(merged.For("panel-a").SoftwareBrightness == 60 && merged.For("panel-b").SoftwareBrightness == 80,
    "commit changes only owned monitor settings");
CacheChecks.Run(Check);
ShellChecks.Run(Check);
SettingsChecks.Run(Check);
var screen = new DispCtrl.Core.Displays.DisplayRect(0, 0, 1920, 1080);
var active = new DispCtrl.Core.Displays.DisplayRect(500, 100, 1400, 900);
var cut = DispCtrl.Core.Displays.FocusGeometry.Intersect(screen, active);
Check(cut == active && DispCtrl.Core.Displays.FocusGeometry.Covers(screen, screen), "focus geometry clips and covers monitor bounds");
Check(DispCtrl.Core.Displays.FocusGeometry.Alpha(50) is >= 127 and <= 128, "focus dim percent maps to overlay alpha");
Check(Math.Abs(DispCtrl.Core.Displays.FocusGeometry.Fade(0, 100, 50, 100) - 50) < 0.1, "focus fade reaches midpoint smoothly");
if (!DispCtrl.Core.FeatureFlags.Presets)
{
    var before = System.Text.Json.JsonSerializer.Serialize(settings, SettingsJsonContext.Default.DispCtrlSettings);
    var disabled = DispCtrl.Display.Presets.PresetService.Apply(saved, [], settings);
    Check(!disabled.Ok && !disabled.Attempted, "stable build refuses preset application before hardware access");
    Check(before == System.Text.Json.JsonSerializer.Serialize(settings, SettingsJsonContext.Default.DispCtrlSettings),
        "disabled preset application leaves settings intact");
    foreach (string verb in new[] { "list", "apply", "save", "delete" })
        Check(DispCtrl.Display.Cli.CommandLine.Run("preset", [verb, "Disabled feature check"]) == 1,
            $"stable CLI refuses preset {verb}");
    Check(DispCtrl.Display.DisplayReport.Presets().Length == 0, "stable reports omit saved presets");
    using var help = new StringWriter();
    TextWriter previousOutput = Console.Out;
    try
    {
        Console.SetOut(help);
        DispCtrl.Display.Cli.CommandLine.Usage(null);
    }
    finally { Console.SetOut(previousOutput); }
    Check(!help.ToString().Contains("preset", StringComparison.OrdinalIgnoreCase),
        "stable CLI help omits presets");
}
Console.WriteLine($"{passed} checks passed.");

if (args.Contains("--capture-live"))
{
    var displays = DispCtrl.Core.Displays.DisplayRegistry.Enumerate();
    var capture = DispCtrl.Display.Presets.PresetService.Capture("Read-only verification", displays, SettingsStore.Load());
    var roundTrip = PresetStore.Parse(PresetStore.ToJson(capture));
    Check(roundTrip.Monitors.Count == displays.Count, "live capture serializes every active display");
    Check(PresetDiff.Describe(capture, roundTrip).Count == 0, "all live captured values survive JSON round trip");
    Console.WriteLine($"Read-only capture: {displays.Count} displays, {capture.CaptureNotes.Count} capture notes. No settings applied or saved.");
}

if (args.Contains("--report-live"))
{
    var displays = DispCtrl.Core.Displays.DisplayRegistry.Enumerate();
    foreach (var display in displays)
    {
        var record = DispCtrl.Display.Devices.DeviceContribution.Prepare(display, displays);
        Check(record.Prefilled, $"{record.Key} report fits prefilled GitHub URL ({record.Url.AbsoluteUri.Length} characters)");
        string queryBody = record.Url.Query.Split("&body=", 2)[1];
        Check(System.Net.WebUtility.UrlDecode(queryBody) == record.Body, "prefilled body round-trips without manual paste");
    }
}
