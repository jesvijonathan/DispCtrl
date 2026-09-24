using System.Text.Json.Nodes;
using DispCtrl.Core.Devices;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

namespace DispCtrl.Control;

/// <summary>
/// Repair and clean-up, for a copy that misbehaves after an update or an
/// interrupted install: the same steps the installer's Repair options take.
/// </summary>
/// <remarks>
/// Neither touches a setting. Repair puts back what DispCtrl registers outside
/// its own folder; clear-cache removes what DispCtrl rebuilds by itself. Reset
/// is <c>settings reset</c>, and is separate on purpose.
/// </remarks>
public sealed partial class ControlService
{
    private static JsonNode MaintenanceCommand(string action, JsonObject args)
    {
        foreach (var pair in args)
            if (pair.Key is not "dryRun") throw new ArgumentException("maintenance takes no options besides --dry-run.");
        bool dryRun = Flag(args, "dryRun");
        return action switch
        {
            "repair" => Repair(dryRun),
            "clear-cache" => ClearCache(dryRun),
            _ => throw new ArgumentException("maintenance actions: repair, clear-cache."),
        };
    }

    private static JsonObject Repair(bool dryRun)
    {
        var done = new JsonArray();
        var notes = new JsonArray();
        void Did(string what) => done.Add((JsonNode?)JsonValue.Create(what));
        void Note(string what) => notes.Add((JsonNode?)JsonValue.Create(what));

        string engine = Path.Combine(AppContext.BaseDirectory, "DispCtrl.Engine.exe");
        string app = Path.Combine(AppContext.BaseDirectory, "DispCtrl.App.exe");

        // The sign-in task: re-pointed only when the engine it starts is gone,
        // which is what an uninstall of another copy, or a moved folder, leaves.
        if (!StartupIntegration.IsPackaged)
        {
            string? target = StartupIntegration.EngineTaskTarget();
            if (target is not null && !File.Exists(target) && File.Exists(engine))
            {
                if (!dryRun) StartupIntegration.RegisterEngineTask(engine);
                Did($"The sign-in task started an engine that no longer exists; it now starts {engine}.");
            }
            else if (target is not null && !string.Equals(Path.GetFullPath(target), Path.GetFullPath(engine), StringComparison.OrdinalIgnoreCase))
                Note($"The sign-in task starts another copy of DispCtrl ({target}); it was left alone.");

            if (File.Exists(app) && !StartupIntegration.HasStartMenuShortcut)
            {
                if (!dryRun) StartupIntegration.SetStartMenuShortcut(true);
                Did("Put the Start menu shortcut back.");
            }
        }

        // A settings file that could not be parsed was set aside, and DispCtrl
        // has been running on defaults since; the person should know.
        string bad = SettingsStore.Path_ + ".bad";
        if (File.Exists(bad)) Note($"An unreadable settings file was set aside as {bad}. Your current settings are the ones in use.");

        // Rewritten by the engine each time it registers shortcuts; a stale one
        // would show every shortcut as working or taken from the last run.
        if (File.Exists(HotkeyStatus.PathOnDisk))
        {
            if (!dryRun) TryDelete(HotkeyStatus.PathOnDisk);
            Did("Cleared the shortcut status; the engine writes it again when it starts.");
        }

        if (done.Count == 0) Did("Nothing needed repairing.");
        return new JsonObject
        {
            ["state"] = dryRun ? "planned" : "repaired", ["done"] = done, ["notes"] = notes,
            ["next"] = "Restart the engine to finish: dispctrl engine stop, then dispctrl engine start. The app's Repair button does this for you.",
        };
    }

    /// <summary>Logs and what the engine learns again by itself; never settings, presets or named codes.</summary>
    private static JsonObject ClearCache(bool dryRun)
    {
        string root = SettingsStore.Directory;
        var removed = new JsonArray();
        long bytes = 0;
        IEnumerable<string> Files()
        {
            if (!Directory.Exists(root)) yield break;
            foreach (string f in Directory.EnumerateFiles(root, "*.log*")) yield return f;
            // Not the shortcut status: the engine rewrites it only when it
            // registers, so clearing it would show every shortcut as waiting.
            if (File.Exists(DeviceHistory.PathOnDisk)) yield return DeviceHistory.PathOnDisk;
            string devices = Path.Combine(root, "devices");
            if (Directory.Exists(devices))
                foreach (string f in Directory.EnumerateFiles(devices, "*.md")) yield return f;
            string outbox = Path.Combine(devices, "outbox");
            if (Directory.Exists(outbox))
                foreach (string f in Directory.EnumerateFiles(outbox)) yield return f;
        }
        foreach (string file in Files().ToList())
        {
            long size = new FileInfo(file).Length;
            if (!dryRun && !TryDelete(file)) continue;
            bytes += size;
            removed.Add((JsonNode?)JsonValue.Create(Path.GetRelativePath(root, file)));
        }
        return new JsonObject
        {
            ["state"] = dryRun ? "planned" : "cleared", ["folder"] = root, ["removed"] = removed,
            ["kilobytes"] = Math.Round(bytes / 1024.0, 1),
            ["kept"] = "settings.json, presets and the codes you named (devices\\definitions)",
            ["note"] = "The engine reads each attached monitor again the next time it starts or one is plugged in.",
        };
    }

    private static bool TryDelete(string path)
    {
        try { File.Delete(path); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
