using DispCtrl.Core.Displays;
using DispCtrl.Core.Presets;
using DispCtrl.Core.Settings;
using Panel = DispCtrl.Core.Displays.ArrangementSolver.Panel;

/// <summary>
/// Desks of three and more displays, and a display keeping its settings when
/// its identity token changes: both were reported broken, and neither needs
/// hardware to check.
/// </summary>
internal static class DeskChecks
{
    public static void Run(Action<bool, string> check)
    {
        Arrangements(check);
        Identity(check);
        Adoption(check);
        Profiles(check);
    }

    private static void Arrangements(Action<bool, string> check)
    {
        // A row of three: left, primary in the middle, right.
        Panel left = new("left", -1920, 0, 1920, 1080), middle = new("middle", 0, 0, 1920, 1080, true),
            right = new("right", 1920, 0, 2560, 1440);
        check(ArrangementSolver.IsValid([left, middle, right]), "a row of three is one desktop");

        // Two pairs each flush against their partner passed the old test.
        Panel a = new("a", 0, 0, 1920, 1080, true), b = new("b", 1920, 0, 1920, 1080),
            c = new("c", 0, 3000, 1920, 1080), d = new("d", 1920, 3000, 1920, 1080);
        check(!ArrangementSolver.IsValid([a, b, c, d]), "two separate pairs are not a valid desktop");
        var joined = ArrangementSolver.Close([a, b, c, d]);
        check(ArrangementSolver.IsValid(joined) && joined.Single(p => p.Token == "a") is { X: 0, Y: 0 },
            "separate pieces are joined to the primary's, which stays at the origin");
        check(joined.Single(p => p.Token == "d").X - joined.Single(p => p.Token == "c").X == 1920
            && joined.Single(p => p.Token == "d").Y == joined.Single(p => p.Token == "c").Y,
            "a piece moves whole, so displays arranged together stay together");

        // The reported case: the middle display of a row dragged up above
        // the left one, leaving left and right touching nothing.
        Panel primaryLeft = left with { IsPrimary = true }, plainMiddle = middle with { IsPrimary = false };
        var slots = ArrangementSlots.For([primaryLeft, plainMiddle, right], "middle");
        var above = slots.First(s => s.Against == "left" && s.Side == ArrangementSlots.Side.Above);
        List<Panel> dropped = [primaryLeft, plainMiddle with { X = above.X, Y = above.Y }, right];
        check(!ArrangementSolver.IsValid(dropped), "moving the middle of three out of the row strands the third");
        var closed = ArrangementSolver.Close(dropped);
        check(ArrangementSolver.IsValid(closed), "dropping it closes the gap, as Windows' own page does");
        check(closed.Single(p => p.Token == "middle").Y < 0 && closed.Single(p => p.Token == "left") is { X: 0, Y: 0 },
            "the dropped display stays where it was put and the primary stays at the origin");

        // Four in a 2x2 grid, then every slot for every display, closed, valid.
        Panel g1 = new("g1", 0, 0, 1920, 1080, true), g2 = new("g2", 1920, 0, 1920, 1080),
            g3 = new("g3", 0, 1080, 1920, 1080), g4 = new("g4", 1920, 1080, 1920, 1080);
        List<Panel> grid = [g1, g2, g3, g4];
        bool all = true;
        int tried = 0;
        foreach (Panel moving in grid)
            foreach (var slot in ArrangementSlots.For(grid, moving.Token))
            {
                var layout = grid.Select(p => p.Token == moving.Token ? p with { X = slot.X, Y = slot.Y } : p).ToList();
                all &= ArrangementSolver.IsValid(ArrangementSolver.Close(layout));
                tried++;
            }
        check(all && tried > 20, $"every drop on a 2x2 desk of four ends valid ({tried} positions)");

        // Mixed sizes and a portrait display: a professional rig.
        List<Panel> rig = [new("wide", 0, 0, 3440, 1440, true), new("portrait", 3440, 0, 1440, 2560),
            new("laptop", -2880, 200, 2880, 1800), new("top", 0, -2160, 3840, 2160)];
        check(ArrangementSolver.IsValid(rig), "an ultrawide, a portrait panel, a laptop and a 4K above make one desktop");
        bool rigAll = true;
        foreach (Panel moving in rig)
            foreach (var slot in ArrangementSlots.For(rig, moving.Token))
            {
                var layout = rig.Select(p => p.Token == moving.Token ? p with { X = slot.X, Y = slot.Y } : p).ToList();
                rigAll &= ArrangementSolver.IsValid(ArrangementSolver.Close(layout));
            }
        check(rigAll, "every drop on a mixed-size rig of four ends valid");
    }

    private static void Identity(Action<bool, string> check)
    {
        const string before = @"\\?\DISPLAY#SDC4154#4&1b2c3d4e&0&UID8388688#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        const string after = @"\\?\DISPLAY#SDC4154#5&9f8e7d6c&0&UID8388688#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        const string otherPort = @"\\?\DISPLAY#SDC4154#5&9f8e7d6c&0&UID8388689#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        check(new DisplayKey(before, "SDC-4154", "").ToToken() == new DisplayKey(after, "SDC-4154", "").ToToken(),
            "a panel with no serial keeps its token when Windows renumbers the parent instance");
        check(new DisplayKey(after, "SDC-4154", "").ToToken() != new DisplayKey(otherPort, "SDC-4154", "").ToToken(),
            "two serial-less panels of one model on different ports stay apart");
        check(!new DisplayKey(after, "GSM-0001", "01010101").HasSerial && !new DisplayKey(after, "ACR-0001", "00000000").HasSerial
            && new DisplayKey(after, "DEL-A234", "3QQQ2X3").HasSerial,
            "placeholder serials are not identities; a real one is");
    }

    private static void Adoption(Action<bool, string> check)
    {
        var settings = new DispCtrlSettings();
        settings.For("SDC-4154-0A1B2C3D").BrightnessFloor = 24;
        settings.For("SDC-4154-0A1B2C3D").LastSeenUtc = DateTimeOffset.UtcNow.AddDays(-2);
        settings.For("SDC-4154-11111111").BrightnessFloor = 10;
        settings.For("SDC-4154-11111111").LastSeenUtc = DateTimeOffset.UtcNow.AddDays(-30);
        settings.For("SDC-4154-NEWTOKEN").Label = "Internal";
        settings.Global.QuickPanel.HiddenDisplays.Add("SDC-4154-0A1B2C3D");
        var moves = MonitorAdoption.Adopt(settings, [("SDC-4154-NEWTOKEN", "SDC-4154"), ("DEL-A234-3QQQ2X3", "DEL-A234")]);
        check(moves.Count == 1 && moves[0].From == "SDC-4154-0A1B2C3D"
            && settings.Monitors["SDC-4154-NEWTOKEN"].BrightnessFloor == 24
            && settings.Monitors["SDC-4154-NEWTOKEN"].FormerTokens.SequenceEqual(["SDC-4154-0A1B2C3D"])
            && !settings.Monitors.ContainsKey("SDC-4154-0A1B2C3D"),
            "a renumbered panel takes back its most recent settings, calibration included");
        check(settings.Global.QuickPanel.HiddenDisplays.SequenceEqual(["SDC-4154-NEWTOKEN"]),
            "the quick panel's choices about the display follow it");
        check(MonitorAdoption.Adopt(settings, [("SDC-4154-NEWTOKEN", "SDC-4154")]).Count == 0,
            "a display with settings of its own adopts nothing");

        var twins = new DispCtrlSettings();
        twins.For("ACR-0001-AAAAAAAA").BrightnessFloor = 30;
        check(MonitorAdoption.Adopt(twins, [("ACR-0001-11111111", "ACR-0001"), ("ACR-0001-22222222", "ACR-0001")]).Count == 0
            && twins.Monitors.ContainsKey("ACR-0001-AAAAAAAA"),
            "two identical serial-less monitors arriving together adopt nothing rather than guess");
    }

    private static void Profiles(Action<bool, string> check)
    {
        check(DeskProfiles.Fingerprint(["B", "A", "B"]) == DeskProfiles.Fingerprint(["A", "B"]),
            "a desk is the same set of displays whatever order they were found in");

        var settings = new DispCtrlSettings();
        settings.For("SDC-4154-NEW").FormerTokens.Add("SDC-4154-OLD");
        settings.For("DEL-A234-3QQQ2X3");
        Preset Desk(string name, bool marked, params string[] tokens)
        {
            var p = new Preset { Name = name, ApplyWhenConnected = marked };
            foreach (string t in tokens) p.Monitors[t] = new PresetMonitor { Label = t };
            return p;
        }
        string[] docked = ["SDC-4154-NEW", "DEL-A234-3QQQ2X3"];
        Preset home = Desk("Home", true, "SDC-4154-OLD", "DEL-A234-3QQQ2X3");
        check(DeskProfiles.Covers(home, docked, settings),
            "a preset saved before the laptop's token changed still recognises its desk");
        check(!DeskProfiles.Covers(home, ["SDC-4154-NEW"], settings) && !DeskProfiles.Covers(home, [.. docked, "GSM-0001-AAAA"], settings),
            "a desk is exactly its displays, no fewer and no more");
        var monitorOnly = Desk("Laptop only", true, "SDC-4154-NEW", "DEL-A234-3QQQ2X3");
        monitorOnly.IncludeLayout = false;
        check(DeskProfiles.Due([Desk("Unmarked", false, docked), monitorOnly, home, Desk("Another", true, docked)], docked, settings)?.Name == "Another",
            "the desk's marked whole-desk preset applies, the first by name; unmarked and monitor-only ones never do");
        check(DeskProfiles.Due([Desk("Unmarked", false, docked)], docked, settings) is null, "nothing applies by itself unless marked");

        Preset renamed = DeskProfiles.WithCurrentTokens(home, settings);
        check(renamed.Monitors.ContainsKey("SDC-4154-NEW") && home.Monitors.ContainsKey("SDC-4154-OLD"),
            "applying renames former tokens in a copy, leaving the saved preset as it was");
        Preset fresh = Desk("Home", false, docked);
        fresh.Monitors["SDC-4154-NEW"].Label = "fresh";
        Preset kept = PresetValidation.RetainScope(fresh, home, settings);
        check(kept.ApplyWhenConnected && kept.Monitors.Count == 2 && kept.Monitors["SDC-4154-NEW"].Label == "fresh",
            "updating a desk profile keeps it one, and updates the display under its new token");
        check(PresetStore.Parse(PresetStore.ToJson(home)).ApplyWhenConnected, "the desk switch survives the file");
    }
}
