using System.Diagnostics;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Presets;
using DispCtrl.Core.Settings;
using DispCtrl.Display;
using DispCtrl.Display.Presets;
var displays = DisplayRegistry.Enumerate();
var external = displays.FirstOrDefault(d => !d.IsInternal);
void Measure(string name, Action action, int count = 1)
{
    var watch = Stopwatch.StartNew();
    for (int i = 0; i < count; i++) action();
    Console.WriteLine($"{name}: {watch.Elapsed.TotalMilliseconds:0.0} ms ({count} operation(s))");
}
if (args.Contains("--details"))
{
    Measure("topology", () => DisplayRegistry.Topology());
    Measure("wallpaper fit", () => Wallpaper.ReadFit());
    foreach (var d in displays)
    {
        string label = d.IsInternal ? "internal" : "external";
        Measure(label + " brightness", () => Brightness.Read(d));
        Measure(label + " HDR", () => AdvancedDisplay.ReadHdr(d));
        Measure(label + " scaling", () => AdvancedDisplay.ReadScaling(d));
        Measure(label + " advanced detail", () => DisplayDetails.Read(d));
        Measure(label + " wallpaper", () => Wallpaper.Read(d));
        Measure(label + " settable controls", () => MonitorCapabilities.ReadSettable(d));
    }
    return;
}
if (external is not null)
{
    Measure("Display controls, cold", () => MonitorCapabilities.ReadForUi(external));
    Measure("Display controls, repeated", () => MonitorCapabilities.ReadForUi(external));
    var modeIndex = DisplayModes.Available(external.GdiName).GroupBy(mode => (mode.Width, mode.Height))
        .ToDictionary(group => group.Key, group => group.Select(mode => mode.RefreshHz).Distinct().Order().ToArray());
    Measure("Indexed mode selections", () => modeIndex.TryGetValue(((uint)external.Bounds.Width, (uint)external.Bounds.Height), out _), 20);
}
var settings = SettingsStore.Load();
Measure("Preset capture", () => PresetService.Capture("Benchmark", displays, settings));
Measure("Preset drift capture, repeated", () => PresetService.Capture("Benchmark", displays, settings, useCache: true));
string scratch = Path.Combine(Path.GetTempPath(), $"DispCtrl-performance-{Guid.NewGuid():N}.json");
try
{
    File.WriteAllText(scratch, PresetStore.ToJson(new Preset { Monitors = new() { ["test"] = new() } }));
    Measure("Preset file reads", () => PresetStore.Read(scratch), 1000);
}
finally { File.Delete(scratch); }
