using System.Text.Json.Nodes;
using DispCtrl.Control;
using DispCtrl.Core.Settings;

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
    void Write(string folder, DispCtrl.Core.Devices.DeviceDefinition d) => File.WriteAllText(
        Path.Combine(folder, (d.Target == "*" ? "common" : d.Target) + ".json"),
        System.Text.Json.JsonSerializer.Serialize(d, DispCtrl.Core.Devices.DeviceJsonContext.Default.DeviceDefinition));
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
        Controls = [Named("0xE2", "Seen on C:\\Users\\someone"), Named("0xE2", "twice"), Named("0xE5", "SN 3QQQ2X3A9"),
            new() { Code = "0xE6", Name = "Writable choice without values", Kind = "choice", Writable = true },
            new() { Code = "E7", Name = "bad code" }],
    };
    var problems = DispCtrl.Core.Devices.DeviceDefinitions.Validate(unsafeDefinition);
    Check(problems.Any(p => p.Contains("path")) && problems.Any(p => p.Contains("twice")) && problems.Any(p => p.Contains("serial"))
        && problems.Any(p => p.Contains("list its values")) && problems.Any(p => p.Contains("0x00 to 0xFF")),
        "definitions refuse paths, serials, duplicates, unsafe writable choices and bad codes");
    Check(DispCtrl.Core.Devices.DeviceDefinitions.KeyFor("Input source (DP 1.4)") == "input-source-dp-1-4"
        && DispCtrl.Core.Devices.DeviceDefinitions.IsModel("DEL-A234") && !DispCtrl.Core.Devices.DeviceDefinitions.IsModel("DEL-A234-3QQQ2X3"),
        "keys are words joined by dashes, and a token is never a model");
    DispCtrl.Core.Devices.DeviceHistory.PathOverride = Path.Combine(scratch, "history.json");
    try
    {
        DispCtrl.Core.Devices.DeviceHistory.Listed("DEL-A234", "(vcp(E2))", [new(0xE2, "Manufacturer-specific control E2", "Information", [], 0)]);
        DispCtrl.Core.Devices.DeviceHistory.Listed("DEL-A234", "(vcp(E2))", [new(0xE2, "Manufacturer-specific control E2", "Information", [], 11)]);
        DispCtrl.Core.Devices.DeviceHistory.Listed("DEL-A234-3QQQ2X3", "", []);
        var history = DispCtrl.Core.Devices.DeviceHistory.Load();
        Check(history.Models.Count == 1 && history.Models["DEL-A234"].Codes["0xE2"].Observed.SequenceEqual([0, 11]),
            "history keeps each value a code was seen at, and never records a unit token as a model");
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
    var original = Console.Out;
    var captured = new StringWriter();
    Console.SetOut(captured);
    int refused;
    try { refused = await ControlTerminal.RunAsync(["settings", "validate", "--json"]); }
    finally { Console.SetOut(original); }
    var envelope = JsonNode.Parse(captured.ToString())!;
    Check(refused == 2 && envelope["ok"]!.GetValue<bool>() == false && envelope["exitCode"]!.GetValue<int>() == 2
        && envelope["error"]!["message"] is not null, "a refusal before the broker still answers --json in the result envelope");
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
    using var oversized = new MemoryStream(BitConverter.GetBytes(ControlTransport.MaxBytes + 1));
    bool rejected = false;
    try { await ControlTransport.ReadAsync(oversized, default); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "oversized protocol frames rejected before allocation");
    Console.WriteLine($"{checks} control checks passed.");
}
finally
{
    if (Directory.Exists(scratch) && Path.GetFileName(scratch).StartsWith("DispCtrl-controlcheck-", StringComparison.Ordinal)
        && Path.GetDirectoryName(scratch) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)) Directory.Delete(scratch, true);
}
