using System.Text.Json.Nodes;
using DispCtrl.Control;
using DispCtrl.Core.Presets;

/// <summary>
/// Presets on disk and through the command API, in the scratch settings
/// folder: names Windows will not take, collisions, renames, imports, broken
/// files, and every preset command's refusals. Nothing here reads or writes
/// a display - every apply is a dry run or a refusal.
/// </summary>
internal static class PresetStoreChecks
{
    public static void Run(Action<bool, string> check)
    {
        Directory.CreateDirectory(PresetStore.Directory);
        foreach (string file in Directory.EnumerateFiles(PresetStore.Directory)) File.Delete(file);
        try
        {
            Names(check);
            Renames(check);
            Imports(check);
            Commands(check);
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(PresetStore.Directory)) File.Delete(file);
        }
    }

    private static Preset Named(string name)
    {
        Preset p = PresetModelSample.Desk();
        p.Name = name;
        return p;
    }

    private static bool Saves(string name)
    {
        try
        {
            PresetStore.Save(Named(name));
            return PresetStore.Exists(name) && PresetStore.Read(PresetStore.PathFor(name)) is not null;
        }
        catch (Exception) { return false; }
    }

    private static void Names(Action<bool, string> check)
    {
        string[] reserved = ["CON", "nul", "Com1", "LPT9", "aux", "PRN", "CON.backup", "COM¹"];
        string[] refused = reserved.Where(n => !Saves(n)).ToArray();
        check(refused.Length == 0, "names Windows reserves for devices save as ordinary files" + (refused.Length > 0 ? ": failed " + string.Join(", ", refused) : ""));
        check(PresetStore.Load().Count(p => p.Name.Contains("CON", StringComparison.OrdinalIgnoreCase)) >= 2,
            "and come back in the list");

        string[] awkward = ["Café 🌙 evening", "Work/Home", "a:b*c?\"d<e>f|g", "trailing dot.", "  padded  ", new string('x', 300), "..", "."];
        string[] failed = awkward.Where(n => !Saves(n)).ToArray();
        check(failed.Length == 0, "unicode, punctuation, a trailing dot, padding, dots alone and a 300-character name all save"
            + (failed.Length > 0 ? ": failed " + string.Join(", ", failed.Select(f => f.Length > 20 ? f[..20] + "..." : f)) : ""));
        check(Path.GetFileName(PresetStore.PathFor(new string('x', 300))).Length <= 120, "a very long name is cut to a file name every Windows API accepts");
        check(PresetStore.SameFile("Work/Home", "Work_Home") && PresetStore.Exists("Work_Home"),
            "two names that become one file are known to be one preset, so neither silently replaces the other");
        int before = PresetStore.Load().Count;
        File.WriteAllText(Path.Combine(PresetStore.Directory, "Broken.json"), "{ not json");
        File.WriteAllText(Path.Combine(PresetStore.Directory, "NotAPreset.json"), "[1, 2]");
        File.WriteAllText(Path.Combine(PresetStore.Directory, "notes.txt"), "ignored");
        check(PresetStore.Load().Count == before, "a broken file, a JSON file that is not a preset and other files are skipped, not fatal");
        PresetStore.Delete("Never existed");
        check(true, "deleting a preset that does not exist is not an error");
    }

    private static void Renames(Action<bool, string> check)
    {
        PresetStore.Save(Named("Desk"));
        PresetStore.Save(Named("Couch"));
        bool clash;
        try { PresetStore.Rename("Desk", "Couch"); clash = false; } catch (IOException) { clash = true; }
        check(clash && PresetStore.Exists("Desk") && PresetStore.Exists("Couch"), "renaming onto another preset is refused, and both are kept");

        PresetStore.Rename("Desk", "desk");
        string[] files = Directory.GetFiles(PresetStore.Directory, "*.json").Select(Path.GetFileNameWithoutExtension).Where(f => f!.Equals("desk", StringComparison.OrdinalIgnoreCase)).ToArray()!;
        check(files is ["desk"] && PresetStore.Load().Any(p => p.Name == "desk"),
            "changing only a name's case keeps one file, under the new case");

        PresetStore.Rename("desk", "Desk 2");
        check(PresetStore.Exists("Desk 2") && !PresetStore.Exists("desk"), "a rename moves the preset to its new name");
        PresetStore.Rename("Missing", "Anything");
        check(!PresetStore.Exists("Anything"), "renaming a preset that does not exist creates nothing");
    }

    private static void Imports(Action<bool, string> check)
    {
        string outside = Path.Combine(Path.GetTempPath(), "DispCtrl-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            PresetStore.Save(Named("Shared"));
            string theirs = Path.Combine(outside, "Shared.json");
            PresetStore.Export(Named("Shared"), theirs);
            string? first = PresetStore.Import(theirs), second = PresetStore.Import(theirs);
            check(first == "Shared (2)" && second == "Shared (3)" && PresetStore.Exists("Shared"),
                "importing a preset with a name already used keeps both, numbering the newcomer");
            string bad = Path.Combine(outside, "Bad.json");
            File.WriteAllText(bad, "{\"monitors\":{},\"version\":9}");
            check(PresetStore.Import(bad) is null && !PresetStore.Exists("Bad"), "an invalid file is not imported");
            check(PresetStore.Import(Path.Combine(outside, "missing.json")) is null, "importing a file that is not there does nothing");
        }
        finally { Directory.Delete(outside, true); }
    }

    private static void Commands(Action<bool, string> check)
    {
        var service = new ControlService();
        JsonObject Do(string command, JsonObject? args = null) =>
            service.Execute(new JsonObject { ["version"] = 1, ["command"] = command, ["args"] = args ?? new JsonObject() });
        int Exit(JsonObject result) => result["exitCode"]!.GetValue<int>();

        Preset whole = Named("Living room");
        PresetStore.Save(whole);
        Preset monitorOnly = Named("Just the TV");
        monitorOnly.IncludeLayout = false;
        PresetStore.Save(monitorOnly);

        var list = Do("preset.list");
        check(Exit(list) == 0 && list["data"]!["presets"]!.AsArray().Any(p => p!["name"]!.GetValue<string>() == "Living room"
            && p["layout"]!.GetValue<bool>() && p["displays"]!.GetValue<int>() == 2), "preset list names each preset, its displays and whether it holds a layout");
        check(Exit(Do("preset.apply", new() { ["name"] = "No such preset" })) == 1 && Exit(Do("preset.delete", new() { ["name"] = "No such preset" })) == 1
            && Exit(Do("preset.desk", new() { ["name"] = "No such preset", ["enabled"] = true })) == 1,
            "applying, deleting or marking a preset that does not exist is refused");
        check(Exit(Do("preset.apply")) == 2 && Exit(Do("preset.apply", new() { ["name"] = "  " })) == 2 && Exit(Do("preset.save", new() { ["name"] = "" })) == 2,
            "a preset command without a name is asked wrongly");
        check(Exit(Do("preset.apply", new() { ["name"] = "Living room", ["force"] = true })) == 2 && Exit(Do("preset.list", new() { ["name"] = "x" })) == 2
            && Exit(Do("preset.apply", new() { ["name"] = "Living room", ["enabled"] = true })) == 2,
            "an option a preset command does not take is refused, not ignored");
        check(Exit(Do("preset.desk", new() { ["name"] = "Just the TV", ["enabled"] = true })) == 1
            && !PresetStore.Read(PresetStore.PathFor("Just the TV"))!.ApplyWhenConnected,
            "a preset without a layout cannot be a desk profile: it describes no desk");
        check(Exit(Do("preset.desk", new() { ["name"] = "Living room" })) == 2, "marking a desk needs on or off");
        var dry = Do("preset.desk", new() { ["name"] = "Living room", ["enabled"] = true, ["dryRun"] = true });
        check(Exit(dry) == 0 && !PresetStore.Read(PresetStore.PathFor("Living room"))!.ApplyWhenConnected, "a dry run of marking a desk changes nothing");
        check(Exit(Do("preset.desk", new() { ["name"] = "Living room", ["enabled"] = true })) == 0
            && PresetStore.Read(PresetStore.PathFor("Living room"))!.ApplyWhenConnected
            && Exit(Do("preset.desk", new() { ["name"] = "Living room", ["enabled"] = false })) == 0
            && !PresetStore.Read(PresetStore.PathFor("Living room"))!.ApplyWhenConnected,
            "a whole-desk preset is marked as its desk's profile, and unmarked");
        check(Exit(Do("preset.apply", new() { ["name"] = "Living room", ["dryRun"] = true })) == 0
            && Exit(Do("preset.save", new() { ["name"] = "Brand new", ["dryRun"] = true })) == 0 && !PresetStore.Exists("Brand new"),
            "dry runs of applying and saving read nothing and write nothing");
        check(Exit(Do("preset.delete", new() { ["name"] = "Living room", ["dryRun"] = true })) == 0 && PresetStore.Exists("Living room")
            && Exit(Do("preset.delete", new() { ["name"] = "Living room" })) == 0 && !PresetStore.Exists("Living room"),
            "a dry run of deleting keeps the preset; deleting removes it");

        check(PresetLauncher.Run([]) == 2 && PresetLauncher.Run(["Just the TV"]) == 2 && PresetLauncher.Run(["No such preset", "notepad.exe"]) == 1,
            "a launch without a program is asked wrongly, and one for a missing preset is refused before anything starts");
    }
}

/// <summary>A small valid preset for store and command checks, without reading any display.</summary>
internal static class PresetModelSample
{
    public static Preset Desk()
    {
        var p = new Preset { Name = "Sample" };
        p.Monitors["DEL-A234-X"] = new PresetMonitor { Label = "DELL U2424H", Width = 1920, Height = 1080, RefreshHz = 120, X = -1920 };
        p.Monitors["SDC-4154-Y"] = new PresetMonitor { Label = "Internal", Primary = true, Width = 2880, Height = 1800 };
        return p;
    }
}
