using System.Text.Json.Nodes;
using DispCtrl.Control;
using DispCtrl.Core.Settings;
using DispCtrl.Display.Devices;

// Every mutation targets this isolated directory, never the user's live settings.
string scratch = Path.Combine(Path.GetTempPath(), "DispCtrl-controlcheck-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("DISPCTRL_DATA_DIR", scratch);
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name); checks++;
}
JsonObject Request(string command, JsonObject? args = null) => new() { ["version"] = 1, ["command"] = command, ["args"] = args ?? new() };
var service = new ControlService();
try
{
    Check(SettingsStore.Directory == scratch, "test settings are isolated");
    var defaults = SettingsDocument.Read();
    Check(SettingsDocument.Validate(defaults).Version == 1, "default settings pass validation");
    Check(SettingsDocument.Schema().ToJsonString().Contains("oledWakeOnPointerReturn"), "schema exposes current monitor features");
    var invalid = (JsonObject)defaults.DeepClone(); invalid["global"]!["focus"]!["dimPercent"] = 101;
    var bad = service.Execute(Request("settings.import", new() { ["document"] = invalid }));
    Check(bad["exitCode"]!.GetValue<int>() == 2 && !File.Exists(SettingsStore.Path_), "invalid imports fail before persistence");
    bad = service.Execute(Request("settings.set", new() { ["path"] = "/global/focus/unknown", ["value"] = 1 }));
    Check(bad["exitCode"]!.GetValue<int>() == 2, "unknown fields are rejected");
    bad = service.Execute(Request("settings.get", new() { ["typo"] = true }));
    Check(bad["exitCode"]!.GetValue<int>() == 2, "unknown command options are rejected");
    var dry = service.Execute(Request("focus.set", new() { ["dimPercent"] = 40, ["dryRun"] = true }));
    Check(dry["ok"]!.GetValue<bool>() && !File.Exists(SettingsStore.Path_), "dry run validates without creating settings");
    var good = service.Execute(Request("focus.set", new() { ["dimPercent"] = 40 }));
    Check(good["ok"]!.GetValue<bool>() && SettingsStore.Load().Global.Focus.DimPercent == 40, "group setter persists through shared API");
    var left = SettingsStore.Load(); var right = SettingsStore.Load();
    left.Global.Focus.DimPercent = 45; right.Global.OledCare.DimPercent = 65;
    SettingsStore.Save(left); SettingsStore.Save(right);
    var merged = SettingsStore.Load();
    Check(merged.Global.Focus.DimPercent == 45 && merged.Global.OledCare.DimPercent == 65, "independent stale clients preserve each other's edits");
    right.Global.TaskbarOpacity = 37; SettingsStore.Save(right);
    Check(SettingsStore.Load().Global.Focus.DimPercent == 45, "a second save from a stale client cannot revert another client's edit");
    Check(SettingsStore.HasExternalChanges(right), "a merging save tells the UI to refresh external values");
    // This process reuses the text of its own last save rather than reading it
    // back; a save from anybody else must still be read. Same length on purpose:
    // only the file's times can tell the two apart.
    var mine = SettingsStore.Load(); mine.Global.Focus.DimPercent = 87; SettingsStore.Save(mine);
    string ours = File.ReadAllText(SettingsStore.Path_);
    Check(ours.Split("\"dimPercent\": 87").Length == 2, "the own-write test has one value to change");
    string theirs = SettingsStore.Path_ + ".other.tmp";
    File.WriteAllText(theirs, ours.Replace("\"dimPercent\": 87", "\"dimPercent\": 88"));
    File.Move(theirs, SettingsStore.Path_, overwrite: true);
    Check(SettingsStore.Load().Global.Focus.DimPercent == 88, "another process's same-length save is read, not this process's own last write");
    SettingsStore.Save(SettingsStore.Load());
    Thread.Sleep(20);
    File.WriteAllText(SettingsStore.Path_, File.ReadAllText(SettingsStore.Path_).Replace("\"dimPercent\": 88", "\"dimPercent\": 89"));
    Check(SettingsStore.Load().Global.Focus.DimPercent == 89, "a same-length edit in place (an editor) is read too");
    var back = SettingsStore.Load(); back.Global.Focus.DimPercent = 45; SettingsStore.Save(back);
    var reset = service.Execute(Request("taskbar.reset"));
    Check(reset["ok"]!.GetValue<bool>() && SettingsStore.Load().Global.Focus.DimPercent == 45, "taskbar reset preserves unrelated focus settings");
    string revision = SettingsDocument.Revision(SettingsDocument.Read());
    service.Execute(Request("focus.set", new() { ["dimPercent"] = 46 }));
    var conflict = service.Execute(Request("focus.set", new() { ["dimPercent"] = 10, ["revision"] = revision }));
    Check(!conflict["ok"]!.GetValue<bool>() && SettingsStore.Load().Global.Focus.DimPercent == 46, "revision conflict does not overwrite newer settings");
    var settings = SettingsStore.Load(); settings.For("FAKE-panel").Alias = "office"; SettingsStore.Save(settings);
    good = service.Execute(Request("settings.set", new() { ["path"] = "/monitors/FAKE-panel/isOled", ["value"] = true }));
    if (good["ok"]!.GetValue<bool>() != true) Console.WriteLine(good.ToJsonString());
    Check(good["ok"]!.GetValue<bool>() && SettingsStore.Load().For("FAKE-panel").IsOled == true, "nullable settings are addressable even when previously unset");
    good = service.Execute(Request("settings.set", new() { ["path"] = "/monitors/FAKE-panel/isOled", ["value"] = null }));
    Check(good["ok"]!.GetValue<bool>() && SettingsStore.Load().For("FAKE-panel").IsOled is null, "nullable settings can return to automatic detection");
    var visited = new List<string>();
    PlannedOperation Op(int phase, string name, string? monitor, bool success = true) => new(phase, name, monitor, () => { visited.Add(name); return success; });
    var outcome = OperationSequence.Execute([Op(100, "power", "A"), Op(20, "mode", "A"), Op(60, "brightness", "A")], true);
    Check(visited.Count == 0 && outcome.Select(o => o.Name).SequenceEqual(["mode", "brightness", "power"]), "dry run orders operations without invoking adapters");
    outcome = OperationSequence.Execute([Op(20, "mode", "A", false), Op(40, "scale", "B"), Op(100, "power", "A")], false);
    Check(visited.SequenceEqual(["mode"]) && outcome.Count(o => o.State == "skipped") == 2, "mode failure blocks dependent desk operations");
    visited.Clear();
    outcome = OperationSequence.Execute([Op(60, "brightness", "A", false), Op(60, "brightness-B", "B"), Op(100, "power", "A")], false);
    Check(visited.SequenceEqual(["brightness", "brightness-B"]) && outcome[^1].State == "skipped", "monitor failure blocks its power step while other monitors continue");
    var calibration = new MonitorSettings { BrightnessFloor = 20, BrightnessCeiling = 80, BrightnessBaseline = 100 };
    Check(UnisonResume.Target(calibration, true, 50) == 50 && UnisonResume.Target(calibration, false, 50) == 50
        && UnisonResume.Target(calibration, true, 0) == 20 && UnisonResume.Target(calibration, false, 0) == 0,
        "calibration mapping and zero brightness remain distinct");
    Check(UnisonResume.Enable(0, [30], [80], 0).Level == 0, "calibrated unison resumes its zero endpoint");
    foreach (var panelRange in new[] { new MonitorSettings { BrightnessFloor = 25, BrightnessCeiling = 100 },
                                       new MonitorSettings { BrightnessFloor = 0, BrightnessCeiling = 100 },
                                       new MonitorSettings { BrightnessFloor = 10, BrightnessCeiling = 60 },
                                       new MonitorSettings { BrightnessBaseline = 70 } })
    {
        bool calibrated = panelRange.HasBrightnessRange;
        bool stable = Enumerable.Range(0, 101).All(level =>
        {
            int at = UnisonResume.Target(panelRange, calibrated, level);
            return UnisonResume.Target(panelRange, calibrated, UnisonResume.LevelFor(panelRange, calibrated, at)) == at;
        });
        bool contained = Enumerable.Range(0, 101).All(windows =>
        {
            int held = UnisonResume.Target(panelRange, calibrated, UnisonResume.LevelFor(panelRange, calibrated, windows));
            return calibrated ? held >= panelRange.BrightnessFloor && held <= panelRange.BrightnessCeiling : held <= panelRange.BrightnessBaseline;
        });
        Check(stable && contained, $"Windows brightness reads back as a level that keeps the built-in panel in range ({panelRange.BrightnessFloor}..{panelRange.BrightnessCeiling}, baseline {panelRange.BrightnessBaseline})");
    }
    var floorAt25 = new MonitorSettings { BrightnessFloor = 25, BrightnessCeiling = 100 };
    Check(UnisonResume.LevelFor(floorAt25, true, 10) == 0 && UnisonResume.LevelFor(floorAt25, true, 100) == 100
        && UnisonResume.LevelFor(floorAt25, true, 62) == 49, "a brightness key below the floor reads as unison zero");
    var panel = new QuickPanelSettings { Animate = false };
    panel.ResetToDefaults();
    Check(panel.Animate, "quick panel reset restores animation preference");
    var bound = SettingsStore.Load();
    var monitorObject = bound.For("FAKE-panel");
    var incoming = SettingsStore.Load();
    incoming.For("FAKE-panel").OledWakeOnPointerReturn = true;
    SettingsStore.RefreshInPlace(bound, incoming);
    Check(ReferenceEquals(monitorObject, bound.For("FAKE-panel")) && monitorObject.OledWakeOnPointerReturn,
        "external settings sync preserves the objects used by display bindings");
    good = service.Execute(Request("taskbar.set", new() { ["opacity"] = 0 }));
    Check(good["ok"]!.GetValue<bool>() && SettingsStore.Load().Global.TaskbarOpacity == 0, "taskbar zero opacity is accepted by the shared API");
    foreach (var malformed in new JsonObject[] {
        new() { ["command"] = "settings.get", ["args"] = 3 },
        new() { ["command"] = "settings.get", ["version"] = "one" },
        Request("focus.set", new() { ["enabled"] = "yes" }),
        Request("display.set", new() { ["coalesce"] = "yes" }),
    }) Check(service.Execute(malformed)["exitCode"]!.GetValue<int>() == 2, "malformed API values fail as invalid requests");
    invalid = (JsonObject)SettingsDocument.Read().DeepClone();
    invalid["global"]!["quickPanel"]!["sections"] = null;
    Check(service.Execute(Request("settings.validate", new() { ["document"] = invalid }))["exitCode"]!.GetValue<int>() == 2,
        "null quick-panel collections are rejected before use");
    Check(await ControlTerminal.RunAsync(["engine", "start", "--dry-run", "--json"]) == 0
        && await ControlTerminal.RunAsync(["engine", "stop", "--dry-run", "--json"]) == 0, "engine dry runs are accepted without process control");
    Check(await ControlTerminal.RunAsync(["engine", "start", "typo"]) == 2, "extra engine arguments are rejected before execution");
    Check(await ControlTerminal.RunAsync(["engine", "start", "--dry-run=maybe"]) == 2, "invalid dry-run flags fail without starting the engine");
    Check(await ControlTerminal.RunAsync(["scripts", "run", "missing.ps1", "--dry-run"]) == 2, "unsupported script dry runs cannot execute a script");
    using (var reader = new FileStream(SettingsStore.Path_, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        // A reader that does not share delete - what File.ReadAllText does -
        // held the file while the app saved, and the rename threw.
        var release = Task.Delay(120).ContinueWith(_ => reader.Dispose());
        var held = SettingsStore.Load(); held.Global.OledCare.DimPercent = 44;
        SettingsStore.Save(held);
        await release;
    }
    Check(SettingsStore.Load().Global.OledCare.DimPercent == 44, "a save waits out a reader holding the file instead of throwing");
    using (new FileStream(SettingsStore.Path_, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) _ = SettingsStore.Load();
    Check(!File.Exists(SettingsStore.Path_ + ".bad") && SettingsStore.Load().Global.OledCare.DimPercent == 44,
        "a settings file that cannot be opened is not quarantined as corrupt");
    // ---- device library: layering, links, validation ----
    string shipped = Path.Combine(scratch, "lib-shipped"), local = Path.Combine(scratch, "lib-local");
    Directory.CreateDirectory(shipped); Directory.CreateDirectory(local);
    // The shipped library in its repository layout; the local folder flat, as
    // DeviceLibrary.PathFor writes it.
    void Write(string folder, DispCtrl.Core.Devices.DeviceDefinition d)
    {
        string path = folder == shipped
            ? DispCtrl.Core.Devices.DeviceLayout.DefinitionPath(folder, d.Target)
            : Path.Combine(folder, (d.Target == "*" ? "common" : d.Target) + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(d, DispCtrl.Core.Devices.DeviceJsonContext.Default.DeviceDefinition));
    }
    DispCtrl.Core.Devices.DefinedControl Named(string code, string name, bool writable = false) =>
        new() { Code = code, Name = name, Kind = writable ? "range" : "information", Writable = writable };
    Write(shipped, new() { Target = "*", Controls = [Named("0xF0", "Common F0")] });
    Write(shipped, new() { Target = "DEL", Controls = [Named("0xF0", "Dell F0"), Named("0xE1", "Dell E1")] });
    Write(shipped, new() { Target = "DEL-A233", Extends = ["DEL-A234"], Controls = [Named("0xE3", "Sibling E3")] });
    Write(shipped, new() { Target = "DEL-A234", Extends = ["DEL-A233"], Controls = [Named("0xE1", "Model E1")] });
    Write(local, new() { Target = "DEL-A234", Controls = [Named("0xE1", "Local E1", writable: true)] });
    DispCtrl.Core.Devices.DeviceLibrary.FoldersOverride = (shipped, local);
    try
    {
        var resolved = DispCtrl.Core.Devices.DeviceLibrary.Resolve("DEL-A234");
        Check(resolved[0xF0].Definition.Name == "Dell F0" && resolved[0xE3].Definition.Name == "Sibling E3"
            && resolved[0xE1].Definition.Name == "Local E1" && resolved[0xE1].Definition.Writable,
            "definitions layer every monitor, brand, links, model, then local, and survive a link loop");
        Check(DispCtrl.Core.Devices.DeviceLibrary.Resolve("LGD-0001").Keys.SequenceEqual(new byte[] { 0xF0 })
            && DispCtrl.Core.Devices.DeviceLibrary.Resolve("LGD-0001")[0xF0].Definition.Name == "Common F0",
            "another brand gets only what applies to every monitor");
        DispCtrl.Core.Devices.DeviceLibrary.Map("DEL-A234", Named("0xE4", "Mapped here"));
        Check(DispCtrl.Core.Devices.DeviceLibrary.Resolve("DEL-A234").ContainsKey(0xE4)
            && DispCtrl.Core.Devices.DeviceLibrary.Unmap("DEL-A234", 0xE4)
            && !DispCtrl.Core.Devices.DeviceLibrary.Resolve("DEL-A234").ContainsKey(0xE4), "a local mapping can be made and removed");
    }
    finally { DispCtrl.Core.Devices.DeviceLibrary.FoldersOverride = null; }
    var unsafeDefinition = new DispCtrl.Core.Devices.DeviceDefinition
    {
        Target = "DEL-A234",
        Controls = [Named("0xE2", "Seen on C:\\Users\\someone"), Named("0xE2", "twice"), Named("0xE5", "SN 9XYZ7K1A9"),
            new() { Code = "0xE6", Name = "Writable choice without values", Kind = "choice", Writable = true },
            new() { Code = "E7", Name = "bad code" }],
    };
    var problems = DispCtrl.Core.Devices.DeviceDefinitions.Validate(unsafeDefinition);
    Check(problems.Any(p => p.Contains("path")) && problems.Any(p => p.Contains("twice")) && problems.Any(p => p.Contains("serial"))
        && problems.Any(p => p.Contains("list its values")) && problems.Any(p => p.Contains("0x00 to 0xFF")),
        "definitions refuse paths, serials, duplicates, unsafe writable choices and bad codes");
    Check(DispCtrl.Core.Devices.DeviceDefinitions.KeyFor("Input source (DP 1.4)") == "input-source-dp-1-4"
        && DispCtrl.Core.Devices.DeviceDefinitions.IsModel("DEL-A234") && !DispCtrl.Core.Devices.DeviceDefinitions.IsModel("DEL-A234-9XYZ7K1"),
        "keys are words joined by dashes, and a token is never a model");
    DispCtrl.Core.Devices.DeviceHistory.PathOverride = Path.Combine(scratch, "history.json");
    try
    {
        DispCtrl.Core.Devices.DeviceHistory.Listed("DEL-A234", "(vcp(E2))", [new(0xE2, "Manufacturer-specific control E2", "Information", [], 0)]);
        DispCtrl.Core.Devices.DeviceHistory.Listed("DEL-A234", "(vcp(E2))", [new(0xE2, "Manufacturer-specific control E2", "Information", [], 11)]);
        DispCtrl.Core.Devices.DeviceHistory.Listed("DEL-A234-9XYZ7K1", "", []);
        var history = DispCtrl.Core.Devices.DeviceHistory.Load();
        Check(history.Models.Count == 1 && history.Models["DEL-A234"].Codes["0xE2"].Observed.SequenceEqual([0, 11]),
            "history keeps each value a code was seen at, and never records a unit token as a model");
        Check(DispCtrl.Core.Devices.DeviceHistoryEdits.Forget("DEL-A234"), "removing a device clears its saved history");
        DispCtrl.Core.Devices.DeviceHistory.Seen("DEL-A234", "Dell", "DisplayPort", false, 527, 296);
        DispCtrl.Core.Devices.DeviceHistory.Listed("DEL-A234", "(vcp(E2))", [new(0xE2, "Unknown", "Information", [], 11)]);
        Check(!DispCtrl.Core.Devices.DeviceHistory.Load().Models.ContainsKey("DEL-A234")
            && !DispCtrl.Core.Devices.DeviceHistoryEdits.NeedsReading("DEL-A234"),
            "automatic sightings and code reads cannot restore a removed device");
        DispCtrl.Core.Devices.DeviceHistoryEdits.Remember(["DEL-A234"]);
        Check(DispCtrl.Core.Devices.DeviceHistoryEdits.NeedsReading("DEL-A234"), "explicit sync allows a removed model to be discovered again");
        DispCtrl.Core.Devices.DeviceHistory.Seen("DEL-A234", "Dell", "DisplayPort", false, 527, 296);
        using (var heldHistory = new FileStream(DispCtrl.Core.Devices.DeviceHistory.PathOverride!, FileMode.Open, FileAccess.Read, FileShare.None))
            DispCtrl.Core.Devices.DeviceHistory.Seen("SDC-4154", "Built-in", "Internal", true, 302, 189);
        Check(DispCtrl.Core.Devices.DeviceHistory.Load().Models.ContainsKey("DEL-A234"),
            "an unreadable history cannot be overwritten with an empty library during discovery");
    }
    finally { DispCtrl.Core.Devices.DeviceHistory.PathOverride = null; }
    // ---- quick panel folding and hotkey defaults ----
    var folding = new QuickPanelSettings();
    Check(folding.IsCollapsed("oledCare") && folding.IsCollapsed("focus") && !folding.IsCollapsed("unison"),
        "detail sections start folded and everyday ones open");
    folding.SetCollapsed("oledCare", false); folding.SetCollapsed("unison", true);
    Check(!folding.IsCollapsed("oledCare") && folding.IsCollapsed("unison")
        && folding.Expanded.SequenceEqual(["oledCare"]) && folding.Collapsed.SequenceEqual(["unison"]),
        "a fold is remembered only where it departs from the default");
    folding.SetCollapsed("oledCare", true);
    Check(folding.IsCollapsed("oledCare") && folding.Expanded.Count == 0, "folding a default-folded section again leaves nothing stored");
    var fresh = new DispCtrlSettings();
    fresh.Hotkeys.Add(new Hotkey { Modifiers = 3, Key = 'N', Action = HotkeyAction.Identify });
    bool offered = Hotkey.OfferDefaults(fresh);
    int afterFirst = fresh.Hotkeys.Count;
    fresh.Hotkeys.RemoveAt(fresh.Hotkeys.Count - 1);
    Check(offered && afterFirst == Hotkey.Defaults().Count && !Hotkey.OfferDefaults(fresh) && fresh.Hotkeys.Count == afterFirst - 1
        && fresh.Hotkeys[0].Action == HotkeyAction.Identify,
        "hotkey defaults are offered once, never over a binding, and a removed one stays removed");
    Check(Hotkey.Defaults().All(h => Hotkey.TryParse(h.Describe(), out uint k, out uint m) && k == h.Key && m == h.Modifiers),
        "every default shortcut reads back from how the page writes it");
    Check(Hotkey.Defaults().Count(h => h.Enabled) == 8 && Hotkey.Defaults().All(h => h.IsComplete),
        "eight default hotkeys are enabled and the remaining shortcuts are ready to enable");
    var upgraded = new DispCtrlSettings();
    upgraded.Global.HotkeyDefaultsOffered = true;
    upgraded.Hotkeys.Add(new Hotkey { Modifiers = 3, Key = 'U', Action = HotkeyAction.Identify });
    Check(Hotkey.OfferDefaults(upgraded) && upgraded.Hotkeys[0].Action == HotkeyAction.Identify
        && upgraded.Hotkeys.Skip(1).All(h => h.Enabled == (h.Action is HotkeyAction.DisplaysOffToggle or HotkeyAction.RestoreDisplays or HotkeyAction.PinWindow or HotkeyAction.GatherWindows))
        && upgraded.Hotkeys.Any(h => h.Action == HotkeyAction.RestoreDisplays) && !Hotkey.OfferDefaults(upgraded),
        "upgrading hotkeys keeps existing bindings, offers new actions once, and only Turn off displays, Restore, Pin and Gather switched on");
    var fromThree = new DispCtrlSettings();
    fromThree.Global.HotkeyDefaultsVersion = 3;
    Check(Hotkey.OfferDefaults(fromThree) && fromThree.Hotkeys.Count == 3 && fromThree.Hotkeys[0].Action == HotkeyAction.RestoreDisplays
        && fromThree.Hotkeys.All(h => h.Enabled && h.Action is HotkeyAction.RestoreDisplays or HotkeyAction.PinWindow or HotkeyAction.GatherWindows),
        "a desk on version 3 is offered restore, then pin and gather, switched on");
    var messy = new DispCtrlSettings();
    messy.Global.Awake.DisplaysOffUtc = DateTimeOffset.UtcNow;
    messy.Global.Focus.Enabled = messy.Global.NightLight.Enabled = messy.Global.OledCare.Enabled = true;
    messy.Global.TaskbarOpacity = 20;
    messy.For("panel").SoftwareBrightness = 30;
    messy.For("panel").HideTaskbar = true;
    messy.For("panel").Alias = "Desk";
    int bindings = messy.Hotkeys.Count;
    messy.RestoreVisibility();
    Check(messy.Global.Awake.DisplaysOffUtc is null && !messy.Global.Focus.Enabled && !messy.Global.NightLight.Enabled
        && !messy.Global.OledCare.Enabled && messy.Global.TaskbarOpacity == 100 && messy.For("panel").SoftwareBrightness == 100
        && !messy.For("panel").HideTaskbar && messy.For("panel").Alias == "Desk" && messy.Hotkeys.Count == bindings,
        "restoring displays undoes everything that darkens, tints or hides a screen, and nothing else");
    messy.RestoreVisibility();
    Check(messy.Global.BeforeRestore is { } kept && kept.Monitors.ContainsKey("panel") && kept.TaskbarOpacity == 20,
        "pressing the way back again with nothing left to switch off keeps the record of the first press");
    var since = new DispCtrlSettings();
    since.For("panel").HideTaskbar = true;
    since.RestoreVisibility();
    since.Global.NightLight.Enabled = true;
    Check(since.UndoRestoreVisibility() && since.For("panel").HideTaskbar && since.Global.NightLight.Enabled,
        "undo only switches back on: night light switched on after the way back stays on");
    var stale = new DispCtrlSettings();
    stale.Global.BeforeRestore = new VisibilitySnapshot { TakenUtc = DateTimeOffset.UtcNow.AddHours(-1), NightLight = true };
    stale.RestoreVisibility();
    Check(stale.Global.BeforeRestore is null, "an old record is dropped, not kept for a later undo");
    messy.For("other").HideTaskbar = false;
    Check(messy.UndoRestoreVisibility() && messy.Global.Focus.Enabled && messy.Global.NightLight.Enabled && messy.Global.OledCare.Enabled
        && messy.Global.TaskbarOpacity == 20 && messy.For("panel").SoftwareBrightness == 30 && messy.For("panel").HideTaskbar
        && !messy.For("other").HideTaskbar && messy.Global.Awake.DisplaysOffUtc is null && messy.Global.BeforeRestore is null
        && !messy.UndoRestoreVisibility(),
        "undoing the way back puts back what it switched off, once, and not displays off");
    var fromTwo = new DispCtrlSettings();
    fromTwo.Global.HotkeyDefaultsVersion = 2;
    fromTwo.Hotkeys.Add(new Hotkey { Modifiers = 3, Key = 'L', Action = HotkeyAction.Identify });
    Check(Hotkey.OfferDefaults(fromTwo) && fromTwo.Hotkeys.Count == 4
        && !fromTwo.Hotkeys.Any(h => h.Action == HotkeyAction.DisplaysOffToggle)
        && fromTwo.Hotkeys.Any(h => h.Action == HotkeyAction.RestoreDisplays),
        "a version 3 default is never offered over a combination something else holds, nor are version 2's again");
    var resetAll = new DispCtrlSettings();
    resetAll.Global.QuickPanel.Simple = !new QuickPanelSettings().Simple;
    resetAll.Global.UnisonFollowsWindows = !new GlobalSettings().UnisonFollowsWindows;
    resetAll.Global.NightLight.Enabled = true;
    resetAll.For("test-panel").Alias = "Desk";
    resetAll.Global.TrayPromotedFor.Add(@"C:\Programs\DispCtrl\DispCtrl.Engine.exe");
    resetAll.For("test-panel").SoftwareBrightness = 25;
    resetAll.For("test-panel").OledRestUntilUtc = DateTimeOffset.UtcNow.AddMinutes(10);
    resetAll.ResetAll();
    Check(resetAll.Global.UnisonFollowsWindows == new GlobalSettings().UnisonFollowsWindows && !resetAll.Global.NightLight.Enabled
        && resetAll.Global.QuickPanel.Simple == new QuickPanelSettings().Simple
        && resetAll.For("test-panel").SoftwareBrightness == new MonitorSettings().SoftwareBrightness
        && resetAll.For("test-panel").OledRestUntilUtc is null && resetAll.For("test-panel").Alias == "Desk"
        && resetAll.Hotkeys.Count == Hotkey.Defaults().Count && resetAll.Global.TrayPromotedFor.Count == 1,
        "reset all restores panel, bridge, colour, rest and hotkeys while retaining monitor names and tray bookkeeping");

    // A report's text, link and clipboard fallback are all publishable surfaces.
    string[] reportSecrets = ["SECRET1234", "DEL-A234-SECRET1234"];
    var report = ProblemReports.Prepare("DEL-A234-SECRET1234 stopped responding", "Try SECRET1234",
        "0.1.0", "Windows 11", "DEL-A234", "Unison off", @"C:\Users\Sample Person\private\engine.log", reportSecrets);
    string published = report.Text + System.Net.WebUtility.UrlDecode(report.Url.AbsoluteUri) + report.Paste;
    Check(reportSecrets.All(s => !published.Contains(s)) && !published.Contains("Sample Person")
        && !published.Contains("DEL-A234-[removed]") && report.Paste is null,
        "short reports prefill fully and scrub every publishing surface, longest identities first");
    string largeLog = string.Concat(Enumerable.Repeat("Long diagnostic line.\n", 500));
    report = ProblemReports.Prepare("A problem", "One step", "0.1.0", "Windows 11", "A panel", "Unison off", largeLog, []);
    Check(report.Url.AbsoluteUri.Length <= 7000 && report.Paste == largeLog.Trim()
        && report.Text.Contains(largeLog.Trim()), "long logs move to the paste field without losing the full preview");
    report = ProblemReports.Prepare(new string('界', 12000), new string('x', 12000), "0.1.0", "Windows 11", "A panel", "", "log", []);
    Check(report.Url.AbsoluteUri.Length <= 7000 && report.Paste == report.Text && report.Text.Contains(new string('界', 12000)),
        "long Unicode descriptions use a bounded issue link and retain the complete report for pasting");
    var reportSettings = SettingsStore.Load();
    reportSettings.For("DEL-A234-DISCONNECTED123").Alias = "old-monitor";
    SettingsStore.Save(reportSettings);
    File.WriteAllText(SettingsStore.LogPath, "DEL-A234-DISCONNECTED123 serial DISCONNECTED123");
    var reportReply = service.Execute(Request("report", new() { ["what"] = "A problem", ["steps"] = "One step" }));
    Check(reportReply["ok"]!.GetValue<bool>() && !reportReply["data"]!.ToJsonString().Contains("DISCONNECTED123"),
        "report API scrubs identities from saved but disconnected monitors in old logs");
    Check(service.Execute(Request("report", new() { ["what"] = true }))["exitCode"]!.GetValue<int>() == 2
        && service.Execute(Request("report", new() { ["typo"] = true }))["exitCode"]!.GetValue<int>() == 2,
        "report API rejects invalid descriptions and unknown options");
    MappingShare SampleShare(string model, string extra = "") => new(model, model,
        $"### {model}\n\nDevice key: `{model}`\n\n{extra}\n### Mappings\n\n```json\n"
        + new JsonObject { ["schema"] = 1, ["kind"] = "dispctrl-device-mapping", ["model"] = model, ["definitions"] = new JsonArray() }.ToJsonString()
        + "\n```\n", "", new Uri("https://github.com/"), true, null);
    var combined = DeviceContribution.CombineMappings([SampleShare("DEL-A234"), SampleShare("SDC-4154")]);
    Check(combined.Prefilled && combined.Paste is null && combined.Url.AbsoluteUri.Length <= 7000
        && System.Net.WebUtility.UrlDecode(combined.Url.AbsoluteUri).Contains("SDC-4154")
        && combined.Body.Split("dispctrl-device-mapping").Length == 3,
        "small combined shares prefill both models with one payload per model");
    combined = DeviceContribution.CombineMappings([SampleShare("DEL-A234", new string('界', 9000)), SampleShare("SDC-4154")]);
    Check(!combined.Prefilled && combined.Paste == combined.Body && combined.Url.AbsoluteUri.Length <= 7000
        && !System.Net.WebUtility.UrlDecode(combined.Url.AbsoluteUri).Contains("```json"),
        "large combined shares retain the full body and avoid duplicate partial JSON in the issue");
    Check(service.Execute(Request("devices.share", new() { ["all"] = true, ["model"] = "DEL-A234" }))["exitCode"]!.GetValue<int>() == 2,
        "share all rejects an ambiguous single-model selector");
    Check(service.Execute(Request("devices.contribute", new() { ["all"] = true, ["model"] = "DEL-A234" }))["exitCode"]!.GetValue<int>() == 2,
        "contribute is the same command under its new name");
    DispCtrl.Core.Devices.DeviceHistory.Listed("TST-0101", "(vcp(E2))", [new(0xE2, "Unknown", "Information", [0, 1, 2], 2)]);
    var mapped = service.Execute(Request("devices.map", new() { ["model"] = "TST-0101", ["code"] = "0xE2", ["name"] = "Test range",
        ["kind"] = "range", ["maximum"] = 42, ["notes"] = "Documented test range" }));
    var codesReply = service.Execute(Request("devices.show", new() { ["model"] = "TST-0101", ["history"] = true }));
    var mappedCode = codesReply["data"]!["codes"]![0]!;
    Check(mapped["ok"]!.GetValue<bool>() && mappedCode["maximum"]!.GetValue<int>() == 42
        && mappedCode["notes"]!.GetValue<string>() == "Documented test range"
        && !mappedCode["writable"]!.GetValue<bool>() && mappedCode["listed"]!.AsArray().Count == 3,
        "mapped unknown codes retain notes, range limits and unnamed values and stay read-only by default");
    var original = Console.Out;
    var captured = new StringWriter();
    Console.SetOut(captured);
    int refused;
    try { refused = await ControlTerminal.RunAsync(["settings", "validate", "--json"]); }
    finally { Console.SetOut(original); }
    var envelope = JsonNode.Parse(captured.ToString())!;
    Check(refused == 2 && envelope["ok"]!.GetValue<bool>() == false && envelope["exitCode"]!.GetValue<int>() == 2
        && envelope["error"]!["message"] is not null, "a refusal before the broker still answers --json in the result envelope");
    captured = new StringWriter();
    Console.SetOut(captured);
    int reportExit;
    try { reportExit = await ControlTerminal.RunAsync(["report", "--what", "on", "--steps", "123", "--local", "--json"]); }
    finally { Console.SetOut(original); }
    envelope = JsonNode.Parse(captured.ToString())!;
    Check(reportExit == 0 && envelope["command"]!.GetValue<string>() == "report"
        && envelope["data"]!["body"]!.GetValue<string>().Contains("### What happened\n\non\n\n### Steps\n\n123"),
        "report CLI keeps boolean-like and numeric descriptions as text");
    var deferred = service.Execute(Request("apply", new() { ["dryRun"] = true, ["document"] = new JsonObject
        { ["version"] = 1, ["topology"] = "extend", ["displays"] = new JsonArray(new JsonObject { ["monitor"] = "NOT-ATTACHED-0000", ["brightness"] = 50 }) } }));
    var plannedSteps = deferred["data"]!["steps"]!.AsArray().Select(s => s!["step"]!.GetValue<string>()).ToArray();
    Check(deferred["exitCode"]!.GetValue<int>() == 0 && plannedSteps.Take(2).SequenceEqual(["topology", "rediscover"])
        && deferred["data"]!["missingMonitors"]![0]!["state"]!.GetValue<string>() == "deferred",
        "a dry run defers monitors that the topology change could switch on");
    var absentMonitor = service.Execute(Request("apply", new() { ["dryRun"] = true, ["document"] = new JsonObject
        { ["version"] = 1, ["displays"] = new JsonArray(new JsonObject { ["monitor"] = "NOT-ATTACHED-0000", ["brightness"] = 50 }) } }));
    Check(absentMonitor["exitCode"]!.GetValue<int>() == 2, "without a topology change a missing monitor still fails preflight");
    using var server = new ControlServer(_ => { });
    await Task.Delay(100);
    var client = new ControlClient();
    var reply = await client.ExecuteAsync(Request("focus.get"));
    Check(reply["ok"]!.GetValue<bool>() && reply["data"]!["value"]!["dimPercent"]!.GetValue<int>() == 46, "broker round-trip reads shared state");
    var writes = await Task.WhenAll(client.ExecuteAsync(Request("oled.set", new() { ["dimPercent"] = 55 })),
        client.ExecuteAsync(Request("focus.set", new() { ["dimPercent"] = 47 })));
    Check(writes.All(r => r["ok"]!.GetValue<bool>()) && SettingsStore.Load().Global.Focus.DimPercent == 47
        && SettingsStore.Load().Global.OledCare.DimPercent == 55, "concurrent broker writes preserve both groups");
    var offOn = service.Execute(Request("awake.displays-off", new() { ["enabled"] = true }));
    DateTimeOffset? offAsked = SettingsStore.Load().Global.Awake.DisplaysOffUtc;
    var offOff = service.Execute(Request("awake.displays-off", new() { ["enabled"] = false }));
    var offBad = service.Execute(Request("awake.displays-off", new() { ["enabled"] = "soon" }));
    Check(offOn["ok"]!.GetValue<bool>() && offAsked is { } asked && DateTimeOffset.UtcNow - asked < TimeSpan.FromMinutes(1)
        && offOff["ok"]!.GetValue<bool>() && SettingsStore.Load().Global.Awake.DisplaysOffUtc is null
        && offBad["exitCode"]!.GetValue<int>() == 2,
        "awake displays-off records a request, clears it, and refuses anything but on or off");
    var hiding = SettingsStore.Load(); hiding.For("FAKE-panel").HideTaskbar = true; SettingsStore.Save(hiding);
    var restoreNow = service.Execute(Request("restore.now"));
    bool cleared = !SettingsStore.Load().For("FAKE-panel").HideTaskbar;
    var restoreGet = service.Execute(Request("restore.get"));
    var restoreUndo = service.Execute(Request("restore.undo"));
    var restoreAgain = service.Execute(Request("restore.undo"));
    Check(restoreNow["ok"]!.GetValue<bool>() && cleared && restoreGet["data"]?["recorded"]?.GetValue<bool>() == true
        && restoreUndo["ok"]!.GetValue<bool>() && SettingsStore.Load().For("FAKE-panel").HideTaskbar
        && SettingsStore.Load().Global.BeforeRestore is null && restoreAgain["exitCode"]!.GetValue<int>() == 1,
        "restore now and undo round-trip through the settings file, and a second undo is refused");
    var offTarget = service.Execute(Request("awake.set", new() { ["displaysOffTarget"] = "exceptPointer" }));
    Check(offTarget["ok"]!.GetValue<bool>() && SettingsStore.Load().Global.Awake.DisplaysOffTarget == DisplaysOffTarget.ExceptPointer,
        "which displays turn off is set by name");
    using var oversized = new MemoryStream(BitConverter.GetBytes(ControlTransport.MaxBytes + 1));
    bool rejected = false;
    try { await ControlTransport.ReadAsync(oversized, default); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "oversized protocol frames rejected before allocation");
    var beforeReset = SettingsStore.Load();
    beforeReset.For("FAKE-panel").Alias = "office";
    beforeReset.Global.QuickPanel.Simple = !new QuickPanelSettings().Simple;
    SettingsStore.Save(beforeReset);
    string beforeResetJson = File.ReadAllText(SettingsStore.Path_);
    var resetDry = service.Execute(Request("settings.reset", new() { ["dryRun"] = true }));
    Check(resetDry["ok"]!.GetValue<bool>() && File.ReadAllText(SettingsStore.Path_) == beforeResetJson,
        "reset all dry-run leaves settings untouched");
    var resetReply = service.Execute(Request("settings.reset"));
    var afterReset = SettingsStore.Load();
    Check(resetReply["ok"]!.GetValue<bool>() && afterReset.For("FAKE-panel").Alias == "office"
        && afterReset.Hotkeys.Count(h => h.Enabled) == 8 && afterReset.Hotkeys.Count == 17
        && afterReset.Global.QuickPanel.Simple == new QuickPanelSettings().Simple && afterReset.Global.Focus.DimPercent == new FocusSettings().DimPercent,
        "CLI reset all matches the app: all groups and default hotkeys reset while monitor names survive");
    // A client holding settings while the file is broken by hand: its save used
    // to throw on the merge and could never write the file back.
    var holder = SettingsStore.Load();
    File.WriteAllText(SettingsStore.Path_, "{ not json");
    holder.Global.TaskbarOpacity = 41;
    bool savedOverCorrupt = true;
    try { SettingsStore.Save(holder); } catch (System.Text.Json.JsonException) { savedOverCorrupt = false; }
    Check(savedOverCorrupt && SettingsStore.Load().Global.TaskbarOpacity == 41,
        "a save replaces a settings file that no longer parses");

    // ---- a file from a newer DispCtrl: read what can be read, lose nothing ----
    {
        var future = JsonNode.Parse(File.ReadAllText(SettingsStore.Path_))!.AsObject();
        future["hotkeys"] = new JsonArray(
            new JsonObject { ["key"] = 0x21, ["modifiers"] = 3, ["action"] = "unisonUp", ["step"] = 5, ["enabled"] = true },
            new JsonObject { ["key"] = 0x54, ["modifiers"] = 3, ["action"] = "summonTheFuture", ["enabled"] = true });
        future["global"]!["quickPanel"]!["trayWheel"] = "everyOtherDisplay";
        future["global"]!["futureSection"] = new JsonObject { ["enabled"] = true, ["level"] = 7 };
        future["global"]!["taskbarOpacity"] = 57;
        File.WriteAllText(SettingsStore.Path_, future.ToJsonString());
        File.Delete(SettingsStore.Path_ + ".bad");

        DispCtrlSettings older = SettingsStore.Load();
        Check(!File.Exists(SettingsStore.Path_ + ".bad") && older.Global.TaskbarOpacity == 57
            && older.Hotkeys.Count == 1 && older.Hotkeys[0].Action == HotkeyAction.UnisonUp
            && older.Global.QuickPanel.TrayWheel == TrayWheelTarget.Off
            && SettingsStore.SetAside.Count == 2,
            "a newer file's unknown hotkey action and choice are set aside, and everything else loads - never quarantined");

        older.Global.Focus.DimPercent = 33;
        SettingsStore.Save(older);
        var after = JsonNode.Parse(File.ReadAllText(SettingsStore.Path_))!;
        Check(after["global"]!["focus"]!["dimPercent"]!.GetValue<int>() == 33
            && after["hotkeys"]!.AsArray().Count == 2
            && after["hotkeys"]![1]!["action"]!.GetValue<string>() == "summonTheFuture"
            && after["global"]!["quickPanel"]!["trayWheel"]!.GetValue<string>() == "everyOtherDisplay"
            && after["global"]!["futureSection"]!["level"]!.GetValue<int>() == 7,
            "saving from the older build keeps what it set aside and what it never knew, for the newer build");
        Check(SettingsStore.Lenient("{ not json", out _) is null, "a file that is not JSON at all is still not guessed at");

        // Put the scratch file back to what the checks below expect.
        after["hotkeys"] = new JsonArray();
        after["global"]!["quickPanel"]!["trayWheel"] = "off";
        after["global"]!.AsObject().Remove("futureSection");
        File.WriteAllText(SettingsStore.Path_, after.ToJsonString());
    }

    // ---- updates: opt-in, never automatic by default, and no network here ----
    {
        int Code(JsonObject reply) => reply["exitCode"]!.GetValue<int>();
        Check(!SettingsStore.Load().Global.Updates.CheckAutomatically, "update checking starts off: no network request until asked");
        var on = service.Execute(Request("update.set", new() { ["checkAutomatically"] = true }));
        Check(on["ok"]!.GetValue<bool>() && SettingsStore.Load().Global.Updates.CheckAutomatically, "update set switches the daily check on");
        Check(Code(service.Execute(Request("update.set", new() { ["latestVersion"] = "9.9.9" }))) == 2
            && Code(service.Execute(Request("update.set", new() { ["checkAutomatically"] = true, ["monitor"] = "1" }))) == 2,
            "only the switch can be set: what was found is the check's to record");
        var found = SettingsStore.Load(); found.Global.Updates.LatestVersion = "99.0.0";
        found.Global.Updates.LatestUrl = "https://evil.example/setup.exe"; SettingsStore.Save(found);
        var get = service.Execute(Request("update.get"));
        Check(get["data"]?["available"]?["latest"]?.GetValue<string>() == "99.0.0"
            && get["data"]?["available"]?["url"]?.GetValue<string>() == UpdateCheck.ReleasesPage,
            "a newer release found is offered, and only ever with a link to the project's own releases");
        var skip = service.Execute(Request("update.skip"));
        Check(skip["ok"]!.GetValue<bool>() && service.Execute(Request("update.get"))["data"]?["available"] is null,
            "Not now hides that release");
        var later = SettingsStore.Load(); later.Global.Updates.LatestVersion = "99.1.0"; SettingsStore.Save(later);
        Check(service.Execute(Request("update.get"))["data"]?["available"]?["latest"]?.GetValue<string>() == "99.1.0",
            "a later release is offered again after Not now");
        Check(service.Execute(Request("update.check", new() { ["dryRun"] = true }))["data"]?["state"]?.GetValue<string>() == "validated",
            "update check validates without a request when dry");
        var updateReset = service.Execute(Request("update.reset"));
        var updatesAfter = SettingsStore.Load().Global.Updates;
        Check(updateReset["ok"]!.GetValue<bool>() && !updatesAfter.CheckAutomatically && updatesAfter.LatestVersion.Length == 0,
            "update reset switches checking off and forgets what was found");
    }

    // ---- pinning, placement, the DDC/CI guard, unison exclusion, tray wheel, theme schedule ----
    int Exit(JsonObject reply) => reply["exitCode"]!.GetValue<int>();
    var pinSet = service.Execute(Request("pin.set", new() { ["borderColour"] = "#FF8800", ["borderThickness"] = 4, ["clearInOledCare"] = true }));
    var pinNow = SettingsStore.Load().Global.Pin;
    Check(pinSet["ok"]!.GetValue<bool>() && pinNow.BorderColour == "#FF8800" && pinNow.BorderThickness == 4 && pinNow.ClearInOledCare,
        "pin border and dimming options set through the shared API");
    Check(Exit(service.Execute(Request("pin.set", new() { ["borderColour"] = "orange" }))) == 2
        && Exit(service.Execute(Request("pin.set", new() { ["borderThickness"] = 40 }))) == 2
        && Exit(service.Execute(Request("pin.set", new() { ["borderOpacity"] = 5 }))) == 2,
        "a border colour that is not #RRGGBB, and out-of-range sizes, are refused");
    Check(Exit(service.Execute(Request("pin.set", new() { ["monitor"] = "1", ["border"] = true }))) == 2,
        "pin settings refuse a display: they are about windows");
    var stepAside = service.Execute(Request("pin.set", new() { ["stepAsideForFullscreen"] = false }));
    Check(stepAside["ok"]!.GetValue<bool>() && !SettingsStore.Load().Global.Pin.StepAsideForFullscreen,
        "pinned windows can be told to stay over fullscreen ones");

    // Focus mode's one choice over its two pointer switches.
    var clearPointer = service.Execute(Request("focus.set", new() { ["keepClear"] = "pointer" }));
    var focusPointer = SettingsStore.Load().Global.Focus;
    var clearFocused = service.Execute(Request("focus.set", new() { ["keepClear"] = "Focused" }));
    var focusFocused = SettingsStore.Load().Global.Focus;
    var clearBoth = service.Execute(Request("focus.set", new() { ["keepClear"] = "both", ["dimPercent"] = 44 }));
    var focusBoth = SettingsStore.Load().Global.Focus;
    Check(clearPointer["ok"]!.GetValue<bool>() && focusPointer.Clear == FocusClear.Pointer
        && clearFocused["ok"]!.GetValue<bool>() && focusFocused.Clear == FocusClear.Focused
        && clearBoth["ok"]!.GetValue<bool>() && focusBoth.Clear == FocusClear.Both && focusBoth.DimPercent == 44,
        "focus set --keep-clear writes both switches, in any case, beside other settings");
    Check(Exit(service.Execute(Request("focus.set", new() { ["keepClear"] = "hovered" }))) == 2
        && Exit(service.Execute(Request("focus.set", new() { ["keepClear"] = "7" }))) == 2,
        "a keep-clear choice that is not one of the three is refused");

    // OLED care per display, and the apps that keep a display awake.
    var perDisplay = service.Execute(Request("oled.set", new() { ["perDisplayActivity"] = true, ["excludedApps"] = "vlc.exe, mpv" }));
    var careNow = SettingsStore.Load().Global.OledCare;
    Check(perDisplay["ok"]!.GetValue<bool>() && careNow.PerDisplayActivity && careNow.Exclusions().SetEquals(["vlc", "mpv"]),
        "OLED care rests each display on its own and keeps listed apps' displays awake");
    Check(Exit(service.Execute(Request("oled.set", new() { ["excludedApps"] = 5 }))) == 2,
        "an OLED exception list that is not text is refused");
    var careGet = service.Execute(Request("oled.get"));
    Check(careGet["data"]?["value"]?["perDisplayActivity"]?.GetValue<bool>() == true
        && SettingsDocument.Schema().ToJsonString().Contains("perDisplayActivity")
        && SettingsDocument.Schema().ToJsonString().Contains("stepAsideForFullscreen"),
        "oled get and the schema carry the new options");
    service.Execute(Request("oled.set", new() { ["perDisplayActivity"] = false, ["excludedApps"] = "" }));

    Check(Exit(service.Execute(Request("pin.on", new() { ["window"] = "no-such-window-" + Guid.NewGuid().ToString("N") }))) == 2,
        "pinning a window that is not open is refused as asked wrongly");
    Check(Exit(service.Execute(Request("pin.off", new() { ["all"] = true, ["window"] = "x" }))) == 2,
        "pin off --all takes nothing else");
    var pinReset = service.Execute(Request("pin.reset"));
    Check(pinReset["ok"]!.GetValue<bool>() && SettingsStore.Load().Global.Pin.BorderThickness == new PinSettings().BorderThickness,
        "pin reset restores the shipped border");
    var pinList = service.Execute(Request("pin.list"));
    Check(pinList["ok"]!.GetValue<bool>() && pinList["data"]!["windows"] is JsonArray, "pin list answers with the open windows");

    // No return-windows here: switching it on hands Windows' own window memory
    // over, which is a registry value of the account running the check.
    var placementSet = service.Execute(Request("placement.set", new() { ["newWindowsOnActive"] = true, ["active"] = "activeWindow", ["keepSize"] = false }));
    var placementNow = SettingsStore.Load().Global.Placement;
    Check(placementSet["ok"]!.GetValue<bool>() && placementNow.NewWindowsOnActive && placementNow.Active == ActiveDisplay.ActiveWindow && !placementNow.KeepSize,
        "placement options set by name");
    Check(Exit(service.Execute(Request("placement.set", new() { ["active"] = "sideways" }))) == 2, "an unknown active display is refused");
    Check(Exit(service.Execute(Request("placement.gather", new() { ["to"] = "99" }))) == 2, "gathering onto a display that is not connected is refused");
    Check(Exit(service.Execute(Request("placement.gather", new() { ["into"] = "1" }))) == 2, "gather refuses options it does not take");
    service.Execute(Request("placement.reset"));
    Check(!SettingsStore.Load().Global.Placement.NewWindowsOnActive, "placement reset restores the defaults");

    var withBlock = SettingsDocument.Read();
    withBlock["global"]!["ddcGuard"]!["blocked"] = new JsonArray(new JsonObject
    {
        ["token"] = "FAKE-crash-1", ["model"] = "FAK-0001", ["label"] = "Fake", ["sinceUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["reason"] = "test",
    });
    Check(service.Execute(Request("settings.import", new() { ["document"] = withBlock }))["ok"]!.GetValue<bool>(), "a blocked monitor imports");
    service.Execute(Request("ddc.set", new() { ["guard"] = false }));
    Check(!SettingsStore.Load().Global.DdcGuard.Enabled, "the guard can be switched off");
    service.Execute(Request("ddc.reset"));
    var guardNow = SettingsStore.Load().Global.DdcGuard;
    Check(guardNow.Enabled && guardNow.Blocked.Count == 1, "ddc reset turns the guard on and forgets no block");
    Check(Exit(service.Execute(Request("ddc.allow", new() { ["model"] = "NOT-0000" }))) == 2, "allowing a monitor that is not blocked is refused");
    // The guard's own check, as every DDC/CI conversation asks it, against the
    // same file: a first call must already see the block.
    var fakeMonitor = new DispCtrl.Core.Displays.DisplayInfo
    {
        Key = new DispCtrl.Core.Displays.DisplayKey(@"\\?\DISPLAY#FAK0001#guard", "FAK-0001", "GUARD1"),
        GdiName = @"\\.\DISPLAY9", FriendlyName = "Fake", Connector = DispCtrl.Core.Displays.ConnectorKind.Hdmi, IsPrimary = false,
        Bounds = new(0, 0, 1920, 1080), WorkArea = new(0, 0, 1920, 1040), RefreshHz = 60, BitsPerPixel = 32, Dpi = 96,
    };
    var guardDocument = SettingsDocument.Read();
    guardDocument["global"]!["ddcGuard"]!["blocked"]!.AsArray().Add(new JsonObject
    {
        ["token"] = fakeMonitor.Token, ["model"] = "FAK-0001", ["label"] = "Fake", ["sinceUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["reason"] = "test",
    });
    service.Execute(Request("settings.import", new() { ["document"] = guardDocument }));
    DispCtrl.Display.DdcGuard.Invalidate();
    Check(DispCtrl.Display.DdcGuard.IsBlocked(fakeMonitor), "a blocked monitor is refused from the first conversation, not a second later");
    var allowed = service.Execute(Request("ddc.allow", new() { ["model"] = "FAK-0001" }));
    Check(allowed["ok"]!.GetValue<bool>() && SettingsStore.Load().Global.DdcGuard.Blocked.Count == 0, "a blocked monitor is allowed again by its model");
    Check(!DispCtrl.Display.DdcGuard.IsBlocked(fakeMonitor), "once allowed, the monitor is talked to again at once");
    var ddcGet = service.Execute(Request("ddc.get"));
    Check(ddcGet["ok"]!.GetValue<bool>() && ddcGet["data"]!["guard"]!.GetValue<bool>(), "ddc get reports the guard");
    Check(Exit(service.Execute(Request("ddc.probe", new() { ["monitor"] = "1", ["save"] = true, ["clear"] = true }))) == 2,
        "a probe cannot save and clear at once");
    var badProbe = (JsonObject)SettingsDocument.Read().DeepClone();
    badProbe["monitors"]!["FAKE-panel"]!["probedCodes"] = new JsonArray("1G");
    Check(Exit(service.Execute(Request("settings.import", new() { ["document"] = badProbe }))) == 2, "probed codes that are not hex are refused");

    Check(Exit(service.Execute(Request("unison.set", new() { ["include"] = false }))) == 2, "leaving a display out of unison needs the display");

    var wheel = service.Execute(Request("tray.set", new() { ["trayWheel"] = "all", ["wheelStep"] = 10, ["wheelOnSliders"] = true }));
    var panelNow = SettingsStore.Load().Global.QuickPanel;
    Check(wheel["ok"]!.GetValue<bool>() && panelNow.TrayWheel == TrayWheelTarget.All && panelNow.WheelStep == 10 && panelNow.WheelOnSliders,
        "the tray wheel is set by name");
    Check(Exit(service.Execute(Request("tray.set", new() { ["wheelStep"] = 30 }))) == 2, "a wheel step past 25 is refused");
    // What the terminal hands over for "--tray-wheel off": it reads on and off as switches.
    var wheelOff = service.Execute(Request("tray.set", new() { ["trayWheel"] = false }));
    Check(wheelOff["ok"]!.GetValue<bool>() && SettingsStore.Load().Global.QuickPanel.TrayWheel == TrayWheelTarget.Off,
        "--tray-wheel off switches the wheel off rather than failing");

    service.Execute(Request("settings.set", new() { ["path"] = "/global/nightLight/themeAppliedUtc", ["value"] = DateTimeOffset.UtcNow.ToString("O") }));
    var theme = service.Execute(Request("nightlight.set", new() { ["darkModeOnSchedule"] = true, ["scheduled"] = true }));
    var nightNow = SettingsStore.Load().Global.NightLight;
    Check(theme["ok"]!.GetValue<bool>() && nightNow.DarkModeOnSchedule && nightNow.ThemeAppliedUtc is null,
        "switching dark mode on the schedule on applies it at the next look");

    var actions = service.Execute(Request("hotkeys.list"))["data"]!["actions"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
    Check(actions.Contains("pin-window") && actions.Contains("unpin-all-windows") && actions.Contains("gather-windows"),
        "the new actions are named on the command line");
    var gatherKey = service.Execute(Request("hotkeys.add", new() { ["keys"] = "Ctrl+Alt+Shift+G", ["action"] = "gather-windows", ["display"] = 2 }));
    Check(gatherKey["ok"]!.GetValue<bool>() && gatherKey["data"]!["does"]!.GetValue<string>().Contains("display 2"), "a gather shortcut can name its display");
    string schema = service.Execute(Request("settings.schema"))["data"]!["schema"]!.ToJsonString();
    Check(schema.Contains("inUnison") && schema.Contains("probedCodes") && schema.Contains("trayWheel") && schema.Contains("darkModeOnSchedule")
        && schema.Contains("clearInFocus") && schema.Contains("newWindowsOnActive"), "the schema describes every new setting");
    Console.WriteLine($"{checks} control checks passed.");
}
finally
{
    if (Directory.Exists(scratch) && Path.GetFileName(scratch).StartsWith("DispCtrl-controlcheck-", StringComparison.Ordinal)
        && Path.GetDirectoryName(scratch) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)) Directory.Delete(scratch, true);
}
