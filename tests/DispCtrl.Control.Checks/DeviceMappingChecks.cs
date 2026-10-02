using System.Text.Json.Nodes;
using DispCtrl.Control;
using DispCtrl.Core.Devices;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display.Ddc;
using DispCtrl.Display.Devices;

internal static class DeviceMappingChecks
{
    public static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(SettingsStore.Directory, "mapper-checks");
        DeviceLibrary.FoldersOverride = (Path.Combine(root, "shipped"), Path.Combine(root, "local"));
        DeviceHistory.PathOverride = Path.Combine(root, "history.json");
        try
        {
            DisplayInfo Display(string model) => new()
            {
                Key = new("synthetic-" + model, model, ""), GdiName = "synthetic", FriendlyName = "Test monitor",
                Connector = ConnectorKind.DisplayPort, IsPrimary = false, Bounds = new(), WorkArea = new(),
                RefreshHz = 60, BitsPerPixel = 32, Dpi = 96,
            };
            DefinedControl Choice(string name, bool writable = true) => new()
            {
                Code = "0xE2", Name = name, Kind = "choice", Writable = writable,
                Values = [new() { Value = "0x00", Name = "Standard" }, new() { Value = "0x0B", Name = "Comfort, reading" }],
            };
            var raw = MonitorCapabilities.Parse("(vcp(12 E2(00 0B) E3 E4))");
            raw.Controls.Single(c => c.Code == 0x12).Maximum = 100;
            raw.Controls.Single(c => c.Code == 0xE2).Current = 11;
            raw.Controls.Single(c => c.Code == 0xE3).Maximum = 80;
            raw.Controls.Single(c => c.Code == 0xE3).Current = 40;
            foreach (string brand in new[] { "DEL", "SAM", "ACR" })
            {
                DeviceLibrary.Map(brand, Choice(brand + " picture mode"));
                var mapped = DeviceControls.Apply(Display(brand + "-1234"), raw).Controls.Single(c => c.Code == 0xE2);
                check(mapped.Settable && mapped.Kind == VcpKind.Discrete && mapped.CurrentOption?.Name == "Comfort, reading",
                    brand + " brand mappings become usable named choices");
            }
            check(raw.Controls.Single(c => c.Code == 0xE2).MappedDefinition is null, "mapping projection preserves raw discovery evidence");
            check(DeviceControls.Apply(Display("BNQ-1234"), raw) == raw, "brand mappings do not affect other manufacturers");
            DeviceLibrary.Map("DEL-1234", Choice("This model only", writable: false));
            var disabled = DeviceControls.Apply(Display("DEL-1234"), raw).Controls.Single(c => c.Code == 0xE2);
            check(disabled.Name == "This model only" && !disabled.Settable, "a model override wins over its writable brand mapping");
            DeviceLibrary.Unmap("DEL-1234", 0xE2);
            var enabled = DeviceControls.Apply(Display("DEL-1234"), raw).Controls.Single(c => c.Code == 0xE2);
            check(DeviceControls.ValidateWrite(enabled, null, 11) is not null
                && DeviceControls.ValidateWrite(enabled, Choice("Off", false), 11) is not null
                && DeviceControls.ValidateWrite(enabled, Choice("On"), 12) is not null,
                "removed, disabled and narrowed mappings reject stale writes for every brand");
            DeviceLibrary.Map("DEL-1234", new() { Code = "0xE3", Name = "Adjustment", Kind = "range", Maximum = 100, Writable = true });
            DeviceLibrary.Map("DEL-1234", new() { Code = "0xE4", Name = "Run feature", Kind = "action", Writable = true });
            DeviceLibrary.Map("DEL-1234", new() { Code = "0xE5", Name = "Absent feature", Kind = "action", Writable = true });
            var controls = DeviceControls.Apply(Display("DEL-1234"), raw).Controls;
            check(controls.Single(c => c.Code == 0xE3) is { Settable: true, Maximum: 80, Kind: VcpKind.Continuous }
                && controls.Single(c => c.Code == 0xE4) is { Settable: true, IsAction: true },
                "mapped sliders respect the monitor's maximum and mapped buttons are available");
            check(!controls.Any(c => c.Code == 0xE5), "a general mapping cannot invent an unadvertised control");
            var wide = Choice("Full word"); wide.Values = [new() { Value = "0x1234", Name = "Vendor mode" }];
            var wideControl = DeviceControls.Apply(new VcpControl(0xE2, "Unknown", VcpKind.Information, []) { Current = 0x1234 }, wide);
            check(wideControl.CurrentOption?.Value == 0x1234 && DeviceControls.ValidateWrite(wideControl, wide, 0x1234) is null,
                "manufacturer choice values retain all 16 bits");
            var unavailable = DeviceControls.Apply(raw.Controls.Single(c => c.Code == 0xE2), wide);
            check(!unavailable.Values.Any(v => v.Value == 0x1234)
                && unavailable.Values.Select(v => v.Value).SequenceEqual(new uint[] { 0x00, 0x0B })
                && DeviceControls.ValidateWrite(unavailable, wide, 0x1234) is not null
                && DeviceControls.ValidateWrite(unavailable, wide, 0x0B) is null,
                "brand choices absent from the monitor's advertised options cannot be used; listed values stay, by number");
            var modelChoice = DeviceControls.Apply(raw.Controls.Single(c => c.Code == 0xE2), wide, ownModel: true);
            check(modelChoice.Settable && modelChoice.Values[0].Value == 0x1234 && modelChoice.Values.Count == 3,
                "a model's own confirmed choices survive a stale advertised menu after merging the mapper");
            var renamedChoice = Choice("A different meaning");
            check(DeviceControls.ValidateWrite(enabled, renamedChoice, 11) is not null,
                "changing a mapping's meaning invalidates cached widgets even if the numeric choices match");
            var rangeDefinition = new DefinedControl { Code = "0xE2", Name = "Range", Kind = "range", Maximum = 100, Writable = true };
            var range = DeviceControls.Apply(raw.Controls.Single(c => c.Code == 0xE2), rangeDefinition);
            check(range.Values.Count == 0 && range.Settable && DeviceControls.ValidateWrite(range, rangeDefinition, 101) is not null,
                "mapping an advertised menu as a range clears menu options and enforces its limit");
            var zeroRange = DeviceControls.Apply(new VcpControl(0xE2, "Zero range", VcpKind.Continuous, []) { Maximum = 0 }, rangeDefinition);
            check(zeroRange.Maximum == 0 && !zeroRange.Settable && DeviceControls.ValidateWrite(zeroRange, rangeDefinition, 1) is not null,
                "a mapping cannot expand a monitor's reported zero maximum");
            var actionDefinition = new DefinedControl { Code = "0xE2", Name = "Button", Kind = "action", Writable = true };
            check(DeviceControls.Apply(raw.Controls.Single(c => c.Code == 0xE2), actionDefinition) is { IsAction: true, Values.Count: 0 }
                && DeviceControls.ValidateWrite(enabled, actionDefinition, 1) is not null,
                "mapped actions clear menu options and stale menu writes are rejected");
            DeviceLibrary.Map("DEL-1234", new() { Code = "0x12", Name = "Contrast", Kind = "range", Writable = false });
            check(!DeviceControls.Apply(Display("DEL-1234"), raw).Controls.Single(c => c.Code == 0x12).Settable,
                "a read-only mapping cannot be bypassed by the standard allow list");

            DeviceObserver.Listed(Display("DEL-1234"), raw.Raw, raw.Controls);
            raw.Controls.Single(c => c.Code == 0xE2).Current = 0;
            DeviceObserver.Listed(Display("DEL-1234"), raw.Raw, raw.Controls);
            var service = new ControlService();
            JsonObject Request(string command, JsonObject args) => new() { ["version"] = 1, ["command"] = command, ["args"] = args };
            var shown = service.Execute(Request("devices.show", new() { ["model"] = "DEL-1234", ["history"] = true }));
            check(shown["ok"]!.GetValue<bool>() && shown["data"]!["codes"]!.AsArray()
                .Single(c => c!["code"]!.GetValue<string>() == "0xE3")!["reportedMaximum"]!.GetValue<int>() == 80,
                "scanned range limits are available to the mapper without another hardware read");
            var share = DeviceContribution.PrepareMapping("DEL-1234");
            int start = share.Body.IndexOf("```json\n", StringComparison.Ordinal) + 8;
            var payload = JsonNode.Parse(share.Body[start..share.Body.IndexOf("\n```", start, StringComparison.Ordinal)])!;
            var observed = payload["observed"]!["codes"]!["0xE2"]!["observed"]!.AsArray();
            check(observed.Select(v => v!.GetValue<int>()).SequenceEqual([0, 11]),
                "watch observations remain in contributions after the manufacturer code has been named");
            check(payload["definitions"]!.AsArray().Single(d => d!["target"]!.GetValue<string>() == "DEL-1234")!["controls"]!.AsArray()
                .Any(c => c!["code"]!.GetValue<string>() == "0xE2"),
                "brand mappings are also included as model-scoped review candidates");

            var saved = service.Execute(Request("devices.map", new()
            {
                ["model"] = "SAM-1234", ["code"] = "0xE2", ["name"] = "Picture mode", ["writable"] = true,
                ["values"] = new JsonArray((JsonNode)new JsonObject { ["value"] = "0x0B", ["name"] = "Comfort, reading" }),
            }));
            check(saved["ok"]!.GetValue<bool>() && DeviceLibrary.Local("SAM-1234")!.Controls[0].Values[0].Name == "Comfort, reading",
                "structured mapper values preserve punctuation without comma-separated syntax");
            saved = service.Execute(Request("devices.map", new() { ["model"] = "SAM-1234", ["code"] = "0xE2", ["name"] = "Renamed" }));
            check(saved["ok"]!.GetValue<bool>() && DeviceLibrary.Local("SAM-1234")!.Controls[0] is { Writable: true, Kind: "choice", Values.Count: 1 },
                "renaming a mapping preserves its values, type and enabled state");
            saved = service.Execute(Request("devices.map", new() { ["model"] = "SAM-1234", ["code"] = "0xE2", ["name"] = "Brand name", ["scope"] = "brand" }));
            check(saved["ok"]!.GetValue<bool>() && DeviceLibrary.Local("SAM")!.Controls[0] is { Writable: true, Kind: "choice", Values.Count: 2 },
                "editing a brand mapping preserves the existing brand's choices");
            DeviceLibrary.Map("ACR-5678", new() { Code = "0xE6", Name = "Feature", Kind = "choice", Values = [new() { Value = "1", Name = "On" }] });
            saved = service.Execute(Request("devices.map", new() { ["model"] = "ACR-5678", ["code"] = "0xE6", ["name"] = "Feature", ["scope"] = "brand" }));
            check(saved["ok"]!.GetValue<bool>() && DeviceLibrary.Local("ACR")!.Controls.Any(c => c.Code == "0xE6" && c.Values.Count == 1),
                "promoting a model mapping to a new brand mapping retains its options");
            var probedDisplay = Display("BNQ-4321");
            var probed = MonitorCapabilities.Parse("(type(probed)vcp(12))");
            probed.Controls[0].Current = 30; probed.Controls[0].Maximum = 100;
            DeviceObserver.Listed(probedDisplay, probed.Raw, probed.Controls);
            var probeHistory = DeviceHistory.Load().Models["BNQ-4321"];
            check(probeHistory.Capabilities is null && probeHistory.Codes["0x12"] is { Probed: true, Maximum: 100 }
                && probeHistory.Codes["0x12"].ListedValues.Count == 0,
                "probe discoveries are saved without fabricating advertised capabilities or choice values");
            check(DeviceContribution.PrepareMapping("BNQ-4321").Body.Contains("\"discovery\":\"probed\""),
                "contributions identify controls learned from read-only probes");
            DeviceObserver.Listed(probedDisplay, "(vcp(12))", probed.Controls);
            DeviceObserver.Listed(probedDisplay, probed.Raw, probed.Controls);
            check(DeviceHistory.Load().Models["BNQ-4321"] is { Capabilities: "(vcp(12))" } advertised
                && !advertised.Codes["0x12"].Probed,
                "a later advertised read upgrades probe evidence and a probe cannot downgrade it");

            saved = service.Execute(Request("devices.map", new()
            {
                ["model"] = "GSM-1234", ["code"] = "0x60", ["name"] = "Input source", ["kind"] = "choice",
                ["writable"] = true, ["transport"] = "lg-input",
                ["values"] = new JsonArray((JsonNode)new JsonObject { ["value"] = "0x90", ["name"] = "HDMI" }),
            }));
            check(saved["ok"]!.GetValue<bool>() && DeviceLibrary.Local("GSM-1234") is { Schema: 2, Controls.Count: 1 } lg
                && lg.Controls[0].DdcWrite is { SourceAddress: "0x50", Code: "0xF4" },
                "the named LG transport selects the alternate method and versioned schema");
            var rejected = service.Execute(Request("devices.map", new()
            { ["model"] = "GSM-1234", ["code"] = "0x60", ["name"] = "Input", ["scope"] = "brand" }));
            check(!rejected["ok"]!.GetValue<bool>(), "an LG alternate transport cannot be promoted to a brand mapping");
            saved = service.Execute(Request("devices.map", new()
            { ["model"] = "GSM-1234", ["code"] = "0x60", ["name"] = "Input", ["transport"] = "standard" }));
            check(saved["ok"]!.GetValue<bool>() && DeviceLibrary.Local("GSM-1234")!.Controls[0].DdcWrite is null,
                "choosing standard input explicitly clears an old alternate transport");

            // Both caches must notice another process's save, which is a rename
            // over the file (a new creation time) or an in-place rewrite.
            DeviceLibrary.Map("TST-0100", new() { Code = "0xE7", Name = "Before", Kind = "information" });
            check(DeviceLibrary.Resolve("TST-0100")[0xE7].Definition.Name == "Before", "a local mapping resolves");
            string mappingFile = DeviceLibrary.PathFor("TST-0100");
            string outside = mappingFile + ".outside";
            File.WriteAllText(outside, File.ReadAllText(mappingFile).Replace("\"Before\"", "\"Later\""));
            File.Move(outside, mappingFile, overwrite: true);
            check(DeviceLibrary.Resolve("TST-0100")[0xE7].Definition.Name == "Later",
                "a mapping saved by another process is read again, not served from the cache");
            File.Delete(mappingFile);
            check(!DeviceLibrary.Resolve("TST-0100").ContainsKey(0xE7), "a removed mapping file stops applying at once");

            var memo = Display("TST-0200");
            var reading = MonitorCapabilities.Parse("(vcp(E8))");
            reading.Controls[0].Current = 7;
            DeviceObserver.Listed(memo, reading.Raw, reading.Controls);
            DeviceObserver.Listed(memo, reading.Raw, reading.Controls);
            check(DeviceHistory.Load().Models["TST-0200"].Codes["0xE8"].Observed.SequenceEqual([7]), "a repeated reading is recorded once");
            string historyFile = DeviceHistory.PathOverride!;
            File.WriteAllText(historyFile + ".outside", "{\"schema\":1}");
            File.Move(historyFile + ".outside", historyFile, overwrite: true);
            DeviceObserver.Listed(memo, reading.Raw, reading.Controls);
            check(DeviceHistory.Load().Models.TryGetValue("TST-0200", out SeenModel? again) && again.Codes["0xE8"].Observed.SequenceEqual([7]),
                "a history rewritten by another process is recorded into again, not skipped as already known");
        }
        finally { DeviceLibrary.FoldersOverride = null; DeviceHistory.PathOverride = null; }
    }
}
