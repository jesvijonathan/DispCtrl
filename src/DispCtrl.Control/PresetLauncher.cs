using System.Diagnostics;
using DispCtrl.Core;
using DispCtrl.Core.Presets;
using DispCtrl.Core.Settings;
using DispCtrl.Display.Presets;

namespace DispCtrl.Control;

/// <summary>
/// <c>preset launch NAME PROGRAM [ARGS...] [--wait-for PROCESS] [--keep]</c>:
/// applies the preset, starts the program, and puts the desk back as it was
/// when the program exits.
/// </summary>
/// <remarks>
/// DisplayMagician's shortcut, the reason most people use it: a game on the TV
/// at 4K with HDR, then the desk back. What is put back is a capture taken just
/// before, limited to what the preset changes, so nothing it leaves alone is
/// touched on the way out. A launcher (Steam, a game's own updater) exits as
/// soon as it has started the real thing; <c>--wait-for</c> names the process to
/// wait for instead, up to two minutes for it to appear.
/// <para>
/// Runs in the calling process rather than as a request: it lasts as long as
/// the program does, and no request with a timeout can wait that long.
/// </para>
/// </remarks>
public static class PresetLauncher
{
    /// <summary>Runs a launch from the words after <c>preset launch</c>; returns the exit code.</summary>
    public static int Run(IReadOnlyList<string> words)
    {
        if (!FeatureFlags.Presets) return Fail("Presets are an unavailable beta feature in this build.", 1);
        string? waitFor = null;
        bool keep = false;
        var rest = new List<string>();
        for (int i = 0; i < words.Count; i++)
        {
            if (words[i].Equals("--wait-for", StringComparison.OrdinalIgnoreCase) && i + 1 < words.Count) { waitFor = words[++i]; continue; }
            if (words[i].Equals("--keep", StringComparison.OrdinalIgnoreCase)) { keep = true; continue; }
            rest.Add(words[i]);
        }
        if (rest.Count < 2) return Fail("preset launch needs a preset and a program: preset launch Gaming game.exe", 2);
        string name = rest[0], program = rest[1];

        Preset? preset = PresetStore.Read(PresetStore.PathFor(name));
        if (preset is null) return Fail($"There is no preset called '{name}'.", 1);

        DispCtrlSettings settings = SettingsStore.Load();
        Preset before = PresetValidation.RetainScope(
            PresetService.Capture("Before " + name, ControlService.Resolve(null), settings, windows: true), preset, settings);
        PresetResult applied = PresetService.Apply(preset, ControlService.Resolve(null), settings);
        SettingsStore.Save(PresetSettings.Merge(preset, settings, SettingsStore.Load()));
        foreach (string note in applied.Notes) Console.Error.WriteLine(note);
        if (!applied.Attempted) return Fail($"'{name}' could not be applied; {program} was not started.", 1);

        int exit;
        try
        {
            var start = new ProcessStartInfo(program) { UseShellExecute = true };
            foreach (string word in rest.Skip(2)) start.ArgumentList.Add(word);
            using var process = Process.Start(start);
            Console.WriteLine($"Applied '{name}', started {program}{(keep ? "." : "; the desk comes back when it exits.")}");
            if (keep) return 0;
            process?.WaitForExit();
            if (waitFor is not null) WaitForProcess(waitFor);
            exit = 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"{program} did not start: {ex.Message}");
            exit = 1;
        }

        DispCtrlSettings now = SettingsStore.Load();
        PresetResult restored = PresetService.Apply(before, ControlService.Resolve(null), now);
        SettingsStore.Save(PresetSettings.Merge(before, now, SettingsStore.Load()));
        foreach (string note in restored.Notes) Console.Error.WriteLine(note);
        Console.WriteLine(restored.Ok ? "The desk is back as it was." : "The desk was not fully put back.");
        return restored.Ok ? exit : 1;
    }

    /// <summary>Waits for a named process to appear (two minutes at most), then for every instance of it to exit.</summary>
    private static void WaitForProcess(string image)
    {
        string stem = Path.GetFileNameWithoutExtension(image);
        var appear = Stopwatch.StartNew();
        while (Process.GetProcessesByName(stem).Length == 0)
        {
            if (appear.Elapsed > TimeSpan.FromMinutes(2)) return;
            Thread.Sleep(1000);
        }
        while (true)
        {
            Process[] running = Process.GetProcessesByName(stem);
            if (running.Length == 0) return;
            foreach (var p in running) using (p) p.WaitForExit();
        }
    }

    private static int Fail(string message, int exit)
    {
        Console.Error.WriteLine(message);
        return exit;
    }
}
