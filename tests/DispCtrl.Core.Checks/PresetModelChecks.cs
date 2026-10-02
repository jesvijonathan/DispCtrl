using DispCtrl.Core.Placement;
using DispCtrl.Core.Presets;
using DispCtrl.Core.Settings;

/// <summary>
/// Presets as data: every field through the file, validation at each edge,
/// what counts as drift, the settings merge, scope on re-capture, desk
/// profiles and app rules. No files, no hardware.
/// </summary>
internal static class PresetModelChecks
{
    public static void Run(Action<bool, string> check)
    {
        RoundTrip(check);
        Copies(check);
        Validation(check);
        Parsing(check);
        Drift(check);
        Merge(check);
        Scope(check);
        Desks(check);
        Rules(check);
    }

    /// <summary>A preset with every field away from its default.</summary>
    internal static Preset Full(string name = "Everything")
    {
        var p = new Preset
        {
            Name = name, Description = "Evening, with the TV", IncludeGlobal = true, IncludeLayout = true, ApplyWhenConnected = true,
            CaptureNotes = ["TV: hardware brightness unavailable."],
            Global = new PresetGlobal
            {
                Topology = "Duplicate", UnisonBrightness = true, UnisonLevel = 37, UnisonCalibrated = true,
                NightLightEnabled = true, NightLightStrength = 61, NightLightUnison = false, NightLightCalibrated = true,
                NightLightScheduled = true, NightLightFrom = 1290, NightLightTo = 405, WallpaperFit = 5, VariableRefreshRate = false,
                Taskbar = new PresetTaskbar { HideDelayMs = 900, AnimMs = 0, RevealPx = 5, ArmDistancePx = 120, IdlePollMs = 250, FarPollMs = 700, ArmedPollMs = 8, ShownPollMs = 60 },
            },
            Windows = [new PresetWindow { Process = "code", Title = "plan.md", Spot = new WindowSpot("DEL-A234-X", 1, 2, 300, 200, 1920, 1040, 96, WindowShow.Maximized) }],
        };
        p.Monitors["DEL-A234-X"] = new PresetMonitor
        {
            Label = "DELL U2424H", CustomLabel = "Left", Model = "DEL-A234", Serial = "X", Connector = "DisplayPort",
            PhysicalWidthMm = 527, PhysicalHeightMm = 296, Dpi = 96, ColorProfile = "Dell.icm",
            X = -1920, Y = 120, Primary = false, Width = 1920, Height = 1080, RefreshHz = 120, ScalePercent = 125,
            OrientationDegrees = 270, Hdr = true, Brightness = 0, SoftwareBrightness = 10, NightLightStrength = 100,
            NightLightFloor = 0, NightLightCeiling = 100, BrightnessBaseline = 55, BrightnessFloor = 20, BrightnessCeiling = 80,
            IsOled = false, WallpaperPath = @"D:\Pictures\a b\left.jpg", HideTaskbar = true, ReclaimWorkArea = false,
            MonitorControls = new() { ["0x12"] = 75, ["0x60"] = 0x0F, ["0xDC"] = 65535 },
        };
        p.Monitors["SDC-4154-Y"] = new PresetMonitor { Label = "Internal", Primary = true, Width = 2880, Height = 1800, RefreshHz = 90, ScalePercent = 200, Brightness = 100, IsOled = true };
        return p;
    }

    private static void RoundTrip(Action<bool, string> check)
    {
        Preset full = Full();
        string json = PresetStore.ToJson(full);
        Preset back = PresetStore.Parse(json);
        check(PresetStore.ToJson(back) == json, "every preset field, at a non-default value, survives the file unchanged");
        check(PresetDiff.Describe(full, back).Count == 0, "a preset read back from its file shows no drift from itself");
        check(back.Windows is [{ Spot.Show: WindowShow.Maximized, Spot.Token: "DEL-A234-X" }] && back.Global.VariableRefreshRate == false
            && back.Monitors["DEL-A234-X"].IsOled == false && back.Monitors["SDC-4154-Y"].IsOled == true,
            "windows, an explicit false and both OLED answers are kept, not dropped as defaults");
        var bare = new Preset { Name = "Bare" };
        Preset bareBack = PresetStore.Parse(PresetStore.ToJson(bare));
        check(bareBack.Global.Taskbar is null && bareBack.Global.VariableRefreshRate is null && bareBack.Windows is null,
            "what a preset did not record stays unrecorded through the file, not turned into defaults");
    }

    private static void Copies(Action<bool, string> check)
    {
        Preset original = Full();
        Preset copy = original.Copy();
        copy.Global.Taskbar!.HideDelayMs = 1;
        copy.Global.UnisonLevel = 1;
        copy.Monitors["DEL-A234-X"].MonitorControls["0x12"] = 1;
        copy.Monitors["DEL-A234-X"].Brightness = 99;
        copy.CaptureNotes.Add("extra");
        copy.Windows![0].Title = "changed";
        copy.Monitors.Remove("SDC-4154-Y");
        check(original.Global.Taskbar!.HideDelayMs == 900 && original.Global.UnisonLevel == 37
            && original.Monitors["DEL-A234-X"].MonitorControls["0x12"] == 75 && original.Monitors["DEL-A234-X"].Brightness == 0
            && original.CaptureNotes.Count == 1 && original.Windows![0].Title == "plan.md" && original.Monitors.Count == 2,
            "a copy shares nothing with its original - the store hands out copies of what it caches");
    }

    private static void Validation(Action<bool, string> check)
    {
        bool Refused(Action<Preset> change)
        {
            Preset p = Full();
            change(p);
            try { PresetValidation.Validate(p); return false; }
            catch (FormatException) { return true; }
        }
        PresetMonitor Dell(Preset p) => p.Monitors["DEL-A234-X"];

        check(!Refused(_ => { }), "a complete, sensible preset is valid");
        var refusals = new (string What, Action<Preset> Change)[]
        {
            ("version 0", p => p.Version = 0), ("version 4", p => p.Version = 4),
            ("an empty name", p => p.Name = ""), ("a blank name", p => p.Name = "   "),
            ("an unknown topology", p => p.Global.Topology = "Mirror"), ("a lower-case topology", p => p.Global.Topology = "extend"),
            ("unison 101", p => p.Global.UnisonLevel = 101), ("unison -1", p => p.Global.UnisonLevel = -1),
            ("warmth 101", p => p.Global.NightLightStrength = 101),
            ("a schedule at 24:00", p => p.Global.NightLightFrom = 1440), ("a negative schedule", p => p.Global.NightLightTo = -1),
            ("wallpaper fit 6", p => p.Global.WallpaperFit = 6),
            ("a negative reveal delay", p => p.Global.Taskbar!.HideDelayMs = -1), ("a zero poll", p => p.Global.Taskbar!.ArmedPollMs = 0),
            ("two main displays", p => Dell(p).Primary = true),
            ("an orientation of 45", p => Dell(p).OrientationDegrees = 45),
            ("a width without a height", p => Dell(p).Height = 0),
            ("50% scaling", p => Dell(p).ScalePercent = 50), ("600% scaling", p => Dell(p).ScalePercent = 600),
            ("brightness -2", p => Dell(p).Brightness = -2), ("brightness 101", p => Dell(p).Brightness = 101),
            ("software brightness 9", p => Dell(p).SoftwareBrightness = 9),
            ("a calibration value of -2", p => Dell(p).BrightnessFloor = -2), ("warmth ceiling 101", p => Dell(p).NightLightCeiling = 101),
            ("a control code that is not hex", p => Dell(p).MonitorControls["0xZZ"] = 1),
            ("a control code past one byte", p => Dell(p).MonitorControls["0x100"] = 1),
            ("a control value past 16 bits", p => Dell(p).MonitorControls["0x12"] = 70000),
            ("a negative control value", p => Dell(p).MonitorControls["0x12"] = -1),
            ("a blank monitor token", p => p.Monitors[" "] = new PresetMonitor()),
            ("a window without a process", p => p.Windows![0].Process = ""),
            ("a window of no size", p => p.Windows![0].Spot = p.Windows[0].Spot with { Width = 0 }),
        };
        string[] accepted = refusals.Where(r => !Refused(r.Change)).Select(r => r.What).ToArray();
        check(accepted.Length == 0, "validation refuses each impossible value" + (accepted.Length > 0 ? ": accepted " + string.Join(", ", accepted) : ""));

        var edges = new (string What, Action<Preset> Change)[]
        {
            ("version 1", p => p.Version = 1), ("version 2", p => p.Version = 2),
            ("brightness -1, unrecorded", p => Dell(p).Brightness = -1), ("brightness 0", p => Dell(p).Brightness = 0),
            ("scaling 0, unrecorded", p => Dell(p).ScalePercent = 0), ("500% scaling", p => Dell(p).ScalePercent = 500),
            ("midnight and 23:59", p => { p.Global.NightLightFrom = 0; p.Global.NightLightTo = 1439; }),
            ("no main display", p => p.Monitors["SDC-4154-Y"].Primary = false),
            ("no monitors at all", p => p.Monitors.Clear()),
            ("a control code without 0x", p => Dell(p).MonitorControls["14"] = 5),
            ("a size of zero by zero, unrecorded", p => { Dell(p).Width = 0; Dell(p).Height = 0; }),
            ("no windows", p => p.Windows = null),
        };
        string[] refused = edges.Where(e => Refused(e.Change)).Select(e => e.What).ToArray();
        check(refused.Length == 0, "validation accepts every legal edge" + (refused.Length > 0 ? ": refused " + string.Join(", ", refused) : ""));
    }

    private static void Parsing(Action<bool, string> check)
    {
        bool Fails(string json)
        {
            try { PresetStore.Parse(json); return false; }
            catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException) { return true; }
        }
        check(Fails("[]") && Fails("{}") && Fails("") && Fails("{\"monitors\":[]}") && Fails("{\"monitors\":{},\"global\":null}")
            && Fails("{\"monitors\":{\"A\":null}}") && Fails("{\"monitors\":{},\"version\":\"three\"}"),
            "a file that is not a preset is refused, not read as an empty one");
        Preset legacy = PresetStore.Parse("{\"version\":1,\"name\":\"Old\",\"scope\":{\"brightness\":true},\"monitors\":{\"DEL-A234-X\":{\"width\":1920,\"height\":1080,\"brightness\":50}}}");
        check(legacy.Version == 1 && legacy.IncludeGlobal && legacy.IncludeLayout && legacy.Monitors["DEL-A234-X"].IsOled is null
            && legacy.Global.Taskbar is null,
            "a version-1 file loads whole-desk, its old scope ignored, and what it never recorded stays unrecorded");
        Preset future = PresetStore.Parse("{\"monitors\":{},\"futureField\":{\"x\":1},\"global\":{\"topology\":\"Extend\",\"somethingNew\":true}}");
        check(future.Monitors.Count == 0, "fields a newer DispCtrl adds are ignored rather than refusing the file");
        check(PresetStore.Parse("{ // hand edited\n \"monitors\": {}, }").Monitors.Count == 0, "comments and trailing commas are allowed in a hand-edited file");
    }

    private static void Drift(Action<bool, string> check)
    {
        Preset saved = Full();
        int Count(Action<Preset> live, Action<Preset>? savedChange = null)
        {
            Preset s = Full(), l = Full();
            savedChange?.Invoke(s);
            live(l);
            return PresetDiff.Describe(s, l).Count;
        }
        PresetMonitor Dell(Preset p) => p.Monitors["DEL-A234-X"];

        check(Count(p => Dell(p).Brightness = 2) == 0 && Count(p => Dell(p).Brightness = 3) == 1,
            "brightness within two points is not drift - DDC/CI rounds - and three is");
        check(Count(p => Dell(p).Brightness = -1) == 0 && Count(p => Dell(p).Brightness = 50, s => Dell(s).Brightness = -1) == 0,
            "brightness nobody could read, on either side, is not drift");
        check(Count(p => p.Monitors.Remove("DEL-A234-X")) == 0, "a display that is not attached is absent, not different");
        check(Count(p => p.Windows = []) == 0 && Count(p => p.CaptureNotes.Clear()) == 0 && Count(p => Dell(p).Serial = "other") == 0
            && Count(p => Dell(p).ColorProfile = "other") == 0 && Count(p => Dell(p).Dpi = 144) == 0,
            "windows, capture notes and identity fields never count as drift");
        check(Count(p => { Dell(p).X = 0; Dell(p).Primary = true; p.Global.Topology = "Extend"; }, s => s.IncludeLayout = false) == 0,
            "a preset without layout ignores position, the main display and topology");
        check(Count(p => { p.Global.UnisonLevel = 1; p.Global.NightLightEnabled = false; p.Global.Taskbar!.AnimMs = 500; }, s => s.IncludeGlobal = false) == 0,
            "a preset without the shared settings ignores them");
        check(Count(p => p.Global.VariableRefreshRate = true, s => s.Global.VariableRefreshRate = null) == 0
            && Count(p => p.Global.Taskbar!.AnimMs = 500, s => s.Global.Taskbar = null) == 0,
            "what the preset never recorded is never drift");
        check(Count(p => Dell(p).Width = 0) == 0 && Count(p => Dell(p).RefreshHz = 0) == 0 && Count(p => Dell(p).ScalePercent = 0) == 0,
            "a live value that could not be read is not drift");
        check(Count(p => Dell(p).MonitorControls.Remove("0x12")) == 0 && Count(p => Dell(p).MonitorControls["0x12"] = 76) == 1,
            "a monitor control that stopped answering is not drift; one at another value is");
        check(Count(p => Dell(p).CustomLabel = null, s => s.Version = 2) == 0 && Count(p => Dell(p).CustomLabel = null) == 0
                && Count(p => Dell(p).CustomLabel = "Internal 2880x1800") == 0,
            "a display's name is never drift: the app rewrites it from the monitor, so Discard could not clear it");
        check(Count(p => Dell(p).IsOled = true, s => { s.Version = 2; Dell(s).IsOled = null; }) == 0,
            "an older preset that never said whether a panel is OLED does not disagree with it");
        string picture = Path.Combine(Path.GetTempPath(), "dispctrl-check-wallpaper-" + Guid.NewGuid().ToString("N") + ".jpg");
        File.WriteAllText(picture, "x");
        try
        {
            check(Count(p => Dell(p).WallpaperPath = picture.ToUpperInvariant(), s => Dell(s).WallpaperPath = picture) == 0
                && Count(p => Dell(p).WallpaperPath = null, s => Dell(s).WallpaperPath = picture) == 1
                && Count(p => Dell(p).WallpaperPath = null, s => Dell(s).WallpaperPath = null) == 0,
                "wallpaper paths compare as Windows compares them, and none recorded is none wanted");
        }
        finally { File.Delete(picture); }
        check(Count(p => Dell(p).WallpaperPath = null, s => Dell(s).WallpaperPath = @"Z:\gone
otated.jpg") == 0,
            "a wallpaper whose file is gone is not drift: applying could not put it back");
        check(Count(p => { p.Monitors.Remove("SDC-4154-Y"); p.Global.Topology = "ExternalOnly"; Dell(p).X = 0; Dell(p).Primary = true; }) == 0,
            "with one of the preset's displays unplugged, its layout is not drift: no apply could make it the preset's");
        string line = PresetDiff.Lines(saved, PresetChangeOf(saved)).Single();
        check(line.Contains("DELL U2424H") && line.Contains("Brightness") && line.Contains("50%") && line.Contains("0%"),
            "a drift line names the display, the setting, the value now and the value saved");
    }

    private static Preset PresetChangeOf(Preset saved)
    {
        Preset live = saved.Copy();
        live.Monitors["DEL-A234-X"].Brightness = 50;
        return live;
    }

    private static void Merge(Action<bool, string> check)
    {
        // What applying wrote, into the settings it loaded...
        var applied = new DispCtrlSettings();
        applied.Global.UnisonBrightness = true;
        applied.Global.UnisonLevel = 30;
        applied.Global.NightLight.Enabled = true;
        applied.Global.NightLight.Strength = 70;
        applied.Global.HideDelayMs = 999;
        applied.For("DEL-A234-X").SoftwareBrightness = 40;
        applied.For("DEL-A234-X").HideTaskbar = true;
        // ...while somebody else saved changes the preset has nothing to do with.
        var latest = new DispCtrlSettings();
        latest.Global.NightLight.FollowWindows = true;
        latest.Global.NightLight.DarkModeOnSchedule = true;
        latest.Global.NightLight.ThemeAppliedUtc = new DateTimeOffset(2026, 10, 2, 20, 0, 0, TimeSpan.Zero);
        latest.Global.Focus.Enabled = true;
        latest.For("DEL-A234-X").LastSeenUtc = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        latest.For("DEL-A234-X").Alias = "office";
        latest.For("GSM-0001-Z").SoftwareBrightness = 55;

        Preset whole = Full();
        DispCtrlSettings merged = PresetSettings.Merge(whole, applied, latest);
        check(merged.Global.UnisonLevel == 30 && merged.Global.NightLight.Enabled && merged.Global.NightLight.Strength == 70
            && merged.Global.HideDelayMs == 999 && merged.For("DEL-A234-X").SoftwareBrightness == 40 && merged.For("DEL-A234-X").HideTaskbar,
            "what the preset set is kept");
        check(merged.Global.NightLight.FollowWindows && merged.Global.NightLight.DarkModeOnSchedule && merged.Global.NightLight.ThemeAppliedUtc is not null
            && merged.Global.Focus.Enabled && merged.For("DEL-A234-X").LastSeenUtc is not null && merged.For("DEL-A234-X").Alias == "office"
            && merged.For("GSM-0001-Z").SoftwareBrightness == 55,
            "what somebody else changed meanwhile, and the preset does not hold, survives the merge");
        applied.Global.NightLight.Strength = 5;
        check(merged.Global.NightLight.Strength == 70, "the merged settings share no objects with the ones the apply wrote into");

        var latest2 = new DispCtrlSettings();
        latest2.Global.UnisonLevel = 77;
        latest2.Global.HideDelayMs = 111;
        Preset monitorOnly = Full();
        monitorOnly.IncludeGlobal = false;
        DispCtrlSettings merged2 = PresetSettings.Merge(monitorOnly, applied, latest2);
        check(merged2.Global.UnisonLevel == 77 && merged2.Global.HideDelayMs == 111 && merged2.For("DEL-A234-X").SoftwareBrightness == 40,
            "a monitor-only preset changes its monitors and leaves the shared settings alone");
        Preset noTaskbar = Full();
        noTaskbar.Global.Taskbar = null;
        check(PresetSettings.Merge(noTaskbar, applied, new DispCtrlSettings { Global = { HideDelayMs = 123 } }).Global.HideDelayMs == 123,
            "an older preset without taskbar timings does not reset them");
    }

    private static void Scope(Action<bool, string> check)
    {
        Preset saved = Full();
        saved.IncludeGlobal = false;
        saved.Description = "kept";
        Preset fresh = Full();
        fresh.Description = null;
        fresh.ApplyWhenConnected = false;
        fresh.Global.UnisonLevel = 5;
        fresh.Windows = null;
        fresh.Monitors.Remove("SDC-4154-Y");
        fresh.Monitors["GSM-0001-Z"] = new PresetMonitor { Label = "Not in the preset" };
        Preset kept = PresetValidation.RetainScope(fresh, saved);
        check(!kept.IncludeGlobal && kept.ApplyWhenConnected && kept.Description == "kept" && kept.Global.UnisonLevel == 37
            && kept.Windows is { Count: 1 },
            "re-saving keeps the preset's scope, its desk switch, its description, and the shared settings it chose not to own");
        check(kept.Monitors.ContainsKey("SDC-4154-Y") && !kept.Monitors.ContainsKey("GSM-0001-Z") && kept.Monitors.Count == 2,
            "re-saving keeps a display that is unplugged now, and does not add one the preset never had");
    }

    private static void Desks(Action<bool, string> check)
    {
        var settings = new DispCtrlSettings();
        settings.For("SDC-4154-NEW").FormerTokens.Add("SDC-4154-OLD");
        Preset P(string name, params string[] tokens)
        {
            var p = new Preset { Name = name, ApplyWhenConnected = true };
            foreach (string t in tokens) p.Monitors[t] = new PresetMonitor();
            return p;
        }
        check(DeskProfiles.Due([P("b"), P("A")], [], settings) is null,
            "an empty preset is never the desk for no displays: a laptop lid closing is not a desk to apply");
        check(DeskProfiles.Due([P("zeta", "A", "B"), P("Alpha", "A", "B")], ["B", "A"], settings)?.Name == "Alpha",
            "two presets for one desk: the first by name applies, the same one every time");
        Preset both = P("Both", "SDC-4154-OLD", "SDC-4154-NEW");
        check(!DeskProfiles.Covers(both, ["SDC-4154-NEW"], settings) && DeskProfiles.WithCurrentTokens(both, settings).Monitors.Count == 1,
            "a preset naming one laptop under its old and new token is not a two-display desk, and applies to it once");
        check(DeskProfiles.Current("UNKNOWN", settings) == "UNKNOWN", "a token nothing has replaced is itself");
    }

    private static void Rules(Action<bool, string> check)
    {
        var rule = new AppRule { Process = "Game.exe", Preset = "Gaming" };
        check(rule.Matches("game") && rule.Matches("GAME") && !rule.Matches("game2") && !rule.Matches("gam"),
            "an app rule matches the program by name, whatever its case, and nothing longer or shorter");
        check(new AppRule { Process = "  game  ", Preset = "x" }.Matches("game") && new AppRule { Process = "game.EXE", Preset = "x" }.Matches("game"),
            "spaces around the name and the .exe in any case are forgiven");
        check(new AppRule { Process = @"C:\Games\Steam\Game.exe", Preset = "x" }.Matches("Game")
            && new AppRule { Process = "\"C:\\Program Files\\Game\\game.exe\"", Preset = "x" }.Matches("game"),
            "a rule written as the program's full path, quoted or not, still matches it");
        check(!new AppRule { Process = "game", Preset = "x", Enabled = false }.Matches("game") && !new AppRule { Process = "", Preset = "x" }.Matches("")
            && !new AppRule { Process = "game", Preset = "" }.IsComplete,
            "a switched-off rule, an empty one and one without a preset never fire");
    }
}
