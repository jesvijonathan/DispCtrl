using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;
using DispCtrl.Display.Presets;

namespace DispCtrl.PerfCheck;

/// <summary>
/// Reads the attached monitors and changes nothing. DDC/CI shares one channel
/// per monitor with the engine and the app through a named mutex, so these
/// timings include waiting for them: that wait is part of what a user feels.
/// </summary>
internal static class DisplaySuite
{
    private const string S = "displays";

    public static void Run(Context context)
    {
        Options o = context.Options;
        context.Add(Bench.Time(S, "cheap signature (hot-plug check)", o.N(200), 10, 1.0, () => DisplayRegistry.CheapSignature()));
        context.Add(Bench.Time(S, "enumerate displays", o.N(30), 2, 60.0, () => DisplayRegistry.Enumerate()));
        context.Add(Bench.Time(S, "topology read", o.N(30), 2, 20.0, () => DisplayRegistry.Topology()));
        context.Add(Bench.Time(S, "wallpaper fit read", o.N(20), 2, 30.0, () => Wallpaper.ReadFit()));

        List<DisplayInfo> displays = DisplayRegistry.Enumerate();
        context.Report.Note(S, $"{displays.Count} display(s): {string.Join(", ", displays.Select(d => d.IsInternal ? "internal" : "external"))}");
        foreach (DisplayInfo d in displays)
        {
            string label = d.IsInternal ? "internal" : "external " + d.Key.Model;
            // Internal is WMI, a few ms; external is DDC/CI at 40 ms between messages.
            context.Add(Bench.Time(S, $"{label}: brightness read", o.N(8), 1, d.IsInternal ? 40.0 : 150.0, () => Brightness.Read(d)));
            context.Add(Bench.Time(S, $"{label}: HDR read", o.N(10), 1, 20.0, () => AdvancedDisplay.ReadHdr(d)));
            context.Add(Bench.Time(S, $"{label}: scaling read", o.N(10), 1, 20.0, () => AdvancedDisplay.ReadScaling(d)));
            context.Add(Bench.Time(S, $"{label}: details read", o.N(10), 1, 40.0, () => DisplayDetails.Read(d)));
            if (d.IsInternal) continue;
            context.Add(Bench.Time(S, $"{label}: capabilities (first read)", 1, 0, null, () => MonitorCapabilities.ReadForUi(d), "cold unless the cache is warm"));
            context.Add(Bench.Time(S, $"{label}: capabilities (cached)", o.N(20), 1, 5.0, () => MonitorCapabilities.ReadForUi(d)));
        }

        DispCtrlSettings settings = SettingsStore.Load();
        context.Add(Bench.Time(S, "preset capture (cold)", 1, 0, null, () => PresetService.Capture("Benchmark", displays, settings)));
        context.Add(Bench.Time(S, "preset capture (cached, drift check)", o.N(10), 1, 60.0, () => PresetService.Capture("Benchmark", displays, settings, useCache: true)));
    }
}
