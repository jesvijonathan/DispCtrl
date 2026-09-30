using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DispCtrl.Control;
using DispCtrl.Core.Devices;
using DispCtrl.Core.Displays;
using DispCtrl.Display;
using DispCtrl.Display.Devices;

// Pure protocol/mapping tests. Never send DDC commands or change a live display.
string scratch = Path.Combine(Path.GetTempPath(), "DispCtrl-lginputcheck-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("DISPCTRL_DATA_DIR", scratch);
DeviceLibrary.FoldersOverride = (Path.Combine(scratch, "shipped"), Path.Combine(scratch, "definitions"));
int checks = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception(name);
    Console.WriteLine("PASS " + name);
    checks++;
}
var mapping = new DefinedControl
{
    Code = "0x60", Name = "Input source", Kind = "choice", Writable = true,
    DdcWrite = new(), Values = [new() { Value = "0x90", Name = "HDMI 1" }, new() { Value = "0xD0", Name = "DisplayPort 1" }],
};
var definition = new DeviceDefinition { Schema = 2, Target = "GSM-1234", Controls = [mapping] };
var display = new DisplayInfo
{
    Key = new("synthetic-lg-path", "GSM-1234", ""), GdiName = "synthetic", FriendlyName = "Test LG",
    Connector = ConnectorKind.DisplayPort, IsPrimary = false, Bounds = new(), WorkArea = new(),
    RefreshHz = 60, BitsPerPixel = 32, Dpi = 96,
};
try
{
    Check(LgInput.Packet(0x50, 0xF4, 0xD0).SequenceEqual(new byte[] { 0x50, 0x84, 0x03, 0xF4, 0x00, 0xD0, 0x9D }), "DisplayPort packet matches the LG wire protocol including checksum");
    Check(LgInput.Packet(0x50, 0xF4, 0x90)[^1] == 0xDD, "HDMI packet checksum includes destination and alternate source");
    Check(LgInput.ResolveOutput(display, []).Display is null && LgInput.ResolveOutput(display, [display, display]).Display is null,
        "unplugged and duplicate display identities cannot select a GPU output");
    Check(LgInput.ResolveOutput(display, [display, display with { Key = new("clone", "GSM-5678", "") }]).Display is null,
        "cloned GDI outputs are refused");
    Check(LgInput.ResolveOutput(display, [display with { SharingConnector = 1 }]).Display is null,
        "shared MST connectors are refused");
    var moved = display with { GdiName = "new-live-output", TargetId = 42 };
    Check(LgInput.ResolveOutput(display, [moved]).Display == moved, "output selection uses the fresh path for the same monitor identity");
    Check(Marshal.SizeOf<RawDdc.NvI2c>() == 64 && Marshal.OffsetOf<RawDdc.NvI2c>("Data").ToInt32() == 32, "NVAPI x64 ABI has the expected pointer alignment");
    Check(Marshal.SizeOf<RawDdc.AdlAdapter>() == 1572 && Marshal.SizeOf<RawDdc.AdlDisplay>() == 552, "AMD adapter/display buffers match the Windows ADL ABI");
    Check(Marshal.SizeOf<RawDdc.CtlInit>() == 36 && Marshal.SizeOf<RawDdc.CtlAdapter>() == 320
        && Marshal.SizeOf<RawDdc.CtlEncoder>() == 112 && Marshal.SizeOf<RawDdc.CtlI2c>() == 168
        && Marshal.OffsetOf<RawDdc.CtlI2c>("Data").ToInt32() == 40, "Intel IGCL x64 ABI sizes and I2C payload offset match the header");
    Check(DeviceDefinitions.Validate(definition).Count == 0, "a model-specific LG input mapping validates");
    definition.Schema = 1;
    Check(DeviceDefinitions.Validate(definition).Count > 0, "alternate transport cannot be loaded as an older schema");
    definition.Schema = 2;
    Check(DeviceDefinitions.Validate(new DeviceDefinition { Target = "GSM-1234" }).Count == 0,
        "standard schema 1 definitions remain compatible");
    foreach (string badTarget in new[] { "*", "GSM", "DEL-1234" })
    {
        definition.Target = badTarget;
        Check(DeviceDefinitions.Validate(definition).Count > 0, "alternate writes rejected for " + badTarget);
    }
    definition.Target = "GSM-1234";
    mapping.DdcWrite!.SourceAddress = "0x51";
    Check(DeviceDefinitions.Validate(definition).Count > 0, "an incorrect wire address is rejected");
    mapping.DdcWrite.SourceAddress = "0x50";
    mapping.Values.Add(new() { Value = "0x100", Name = "Invalid" });
    Check(DeviceDefinitions.Validate(definition).Count > 0, "input values cannot be silently truncated");
    mapping.Values.RemoveAt(mapping.Values.Count - 1);
    DeviceLibrary.SaveLocal(definition);
    DeviceLibrary.Link("DEL-1234", "GSM-1234");
    Check(!DeviceLibrary.Resolve("DEL-1234").ContainsKey(0x60), "cross-brand inheritance cannot turn LG wire values into standard input writes");
    Check(DeviceLibrary.Local("GSM-1234")!.Controls[0].DdcWrite?.Code == "0xF4", "wire mapping survives serialization and validation on reload");
    var reported = MonitorCapabilities.Parse("(prot(monitor)vcp(10 60(0F 11)))");
    var applied = LgInput.Apply(display, reported);
    var input = applied.Controls.Single(c => c.Code == 0x60);
    Check(input.Settable && input.WriteOnly && input.Current == -1 && input.Values.Select(v => v.Value).SequenceEqual(new ushort[] { 0x90, 0xD0 }), "mapped input replaces standard values without inventing a current input");
    Check(reported.Controls.Single(c => c.Code == 0x60).Values[0].Value == 0x0F, "capability discovery remains unmodified");
    Check(LgInput.Apply(display, MonitorCapability.None).Controls.Single().Settable, "an explicitly mapped write-only input can be absent from advertised capabilities");
    Check(MonitorCapabilities.ReadControl(display, 0x60) is { WriteOnly: true, Current: -1 },
        "reading the mapped input requires no standard DDC capability or current-value request");
    DeviceLibrary.Unmap("GSM-1234", 0x60);
    Check(!MonitorCapabilities.Write(display, input, 0x90, out string? staleError) && staleError!.Contains("mapping changed"),
        "a removed mapping cannot send LG values through standard DDC from a stale control");
    DeviceLibrary.SaveLocal(definition);
    Check(!MonitorCapabilities.Write(display, reported.Controls.Single(c => c.Code == 0x60), 0x11, out staleError)
        && staleError!.Contains("mapping changed"), "a newly added mapping also invalidates a stale standard control");
    Check(LgInput.Apply(display with { Key = new("other", "DEL-1234", "") }, reported) == reported, "another manufacturer's input stays on the normal path");
    Check(LgInput.Mapping(display with { Connector = ConnectorKind.Internal }) is null, "internal panels cannot use raw LG input writes");
    Check(!LgInput.Write(display, mapping, 0x91).Sent, "unmapped values are refused before display discovery or GPU access");
    mapping.Writable = false;
    DeviceLibrary.SaveLocal(definition);
    Check(!LgInput.Apply(display, reported).Controls.Single(c => c.Code == 0x60).Settable
        && !LgInput.Write(display, mapping, 0x90).Sent, "read-only contributions cannot enable writes");
    mapping.Writable = true;
    DeviceLibrary.SaveLocal(definition);
    var share = DeviceContribution.PrepareMapping("GSM-1234");
    int start = share.Body.IndexOf("```json\n", StringComparison.Ordinal) + 8;
    int end = share.Body.IndexOf("\n```", start, StringComparison.Ordinal);
    var payload = JsonNode.Parse(share.Body[start..end])!;
    Check(payload["definitions"]![0]!["controls"]![0]!["ddcWrite"]!["sourceAddress"]!.GetValue<string>() == "0x50"
        && payload["observed"]!["inputSwitchMapping"]!["verification"] is not null,
        "contribution carries the mapping and distinguishes it from a switching test");
    var service = new ControlService();
    var response = service.Execute(new JsonObject
    {
        ["version"] = 1, ["command"] = "devices.map", ["args"] = new JsonObject
        {
            ["model"] = "GSM-1234", ["code"] = "0x60", ["name"] = "Input source",
            ["values"] = "0x90=HDMI 1,0xD0=DisplayPort 1", ["sourceAddress"] = "0x50", ["writeCode"] = "0xF4",
            ["writable"] = true, ["dryRun"] = true,
        },
    });
    Check(response["ok"]!.GetValue<bool>(), "devices map accepts the alternate-address options through the control API");
    response = service.Execute(new JsonObject
    {
        ["version"] = 1, ["command"] = "devices.map", ["args"] = new JsonObject
        {
            ["model"] = "GSM-2345", ["code"] = "0x60", ["name"] = "Input source",
            ["values"] = "0x90=HDMI 1", ["sourceAddress"] = "0x50", ["writeCode"] = "0xF4", ["writable"] = true,
        },
    });
    Check(response["ok"]!.GetValue<bool>() && DeviceLibrary.Local("GSM-2345")?.Schema == 2,
        "a newly saved alternate mapping uses schema 2 so older readers reject it");
    Check(await ControlTerminal.RunAsync(["devices", "map", "--model", "GSM-1234", "--code", "0x60", "--name", "Input source",
        "--source-address", "0x50", "--write-code", "0xF4", "--values", "0x90=HDMI 1,0xD0=DisplayPort 1",
        "--writable", "--dry-run", "--local"]) == 0, "CLI parses the documented alternate mapping switches");
    var custom = new DefinedControl { Code = "0xE2", Name = "Custom", Kind = "choice", Writable = true,
        Values = [new() { Value = "0x101", Name = "Wide" }] };
    DeviceLibrary.SaveLocal(new DeviceDefinition { Target = "GSM-1234", Controls = [custom] });
    var mappedUi = new VcpControl(0xE2, "Custom", VcpKind.Discrete, [new(0x101, "Wide")])
    {
        MappedWritable = true,
        MappingSnapshot = JsonSerializer.Serialize(custom, DeviceJsonContext.Default.DefinedControl),
    };
    Check(!MonitorCapabilities.Write(display, mappedUi, 1, out _), "mapped UI refuses values outside the displayed choices before touching hardware");
    custom.Writable = false;
    DeviceLibrary.SaveLocal(new DeviceDefinition { Target = "GSM-1234", Controls = [custom] });
    Check(!MonitorCapabilities.Write(display, mappedUi, 0x101, out _), "a stale mapped UI cannot write after permission is revoked");
    custom.Writable = true; custom.Values[0].Value = "0x102";
    DeviceLibrary.SaveLocal(new DeviceDefinition { Target = "GSM-1234", Controls = [custom] });
    Check(!MonitorCapabilities.Write(display, mappedUi, 0x101, out _), "a stale mapped UI cannot write after the mapping changes");
    DeviceLibrary.Unmap("GSM-1234", 0xE2);
    Check(!MonitorCapabilities.Write(display, mappedUi, 0x101, out _), "removing a mapping invalidates existing UI writers");
    var readOnlyContrast = new DefinedControl { Code = "0x12", Name = "Contrast", Kind = "range", Writable = false };
    DeviceLibrary.SaveLocal(new DeviceDefinition { Target = "GSM-1234", Controls = [readOnlyContrast] });
    Check(!MonitorCapabilities.Write(display, new VcpControl(0x12, "Contrast", VcpKind.Continuous, []), 50, out _),
        "read-only overrides also block stale standard-control widgets");
    Console.WriteLine($"{checks} LG input checks passed; no hardware commands sent.");
}
finally
{
    DeviceLibrary.FoldersOverride = null;
    if (Path.GetDirectoryName(scratch) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
        && Path.GetFileName(scratch).StartsWith("DispCtrl-lginputcheck-", StringComparison.Ordinal) && Directory.Exists(scratch))
        Directory.Delete(scratch, true);
}
