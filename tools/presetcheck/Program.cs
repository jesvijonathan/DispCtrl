using DisplCtrl.Core.Displays;
using DisplCtrl.Display.Devices;
using DisplCtrl.Core.Presets;
using DisplCtrl.Core.Settings;
using DisplCtrl.Core;

// Exercises the preset store's edge cases directly, in a scratch folder, so the
// awkward name cases can be checked without a real desk in the way.
int failures = 0;

void Check(string what, bool ok)
{
    Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}");
    if (!ok) failures++;
}

string dir = PresetStore.Directory;
Console.WriteLine($"presets folder: {dir}");
Console.WriteLine();

// A name that cannot be a file name, and one that sanitises to the same thing.
Console.WriteLine("names that collide on disk");
string awkward = "Work/Home";
string sanitised = "Work_Home";
Check("the two names resolve to one file", PresetStore.SameFile(awkward, sanitised));
Check("trailing space collides too", PresetStore.SameFile("Desk", "Desk "));
Check("different names do not collide", !PresetStore.SameFile("Desk", "Dock"));

Console.WriteLine();
Console.WriteLine("rename onto the same file must not delete it");
var p = new Preset { Name = awkward };
p.Monitors["token-a"] = new PresetMonitor { Label = "Test", Width = 1920, Height = 1080 };
PresetStore.Save(p);
Check("saved", PresetStore.Exists(awkward));

PresetStore.Rename(awkward, sanitised);
Check("survives a rename onto its own file", PresetStore.Exists(sanitised));
Check("and still parses", PresetStore.Read(PresetStore.PathFor(sanitised)) is not null);

Preset? back = PresetStore.Read(PresetStore.PathFor(sanitised));
Check("keeps its monitor", back is not null && back.Monitors.Count == 1);

PresetStore.Delete(sanitised);
Check("deleted", !PresetStore.Exists(sanitised));

Console.WriteLine();
Console.WriteLine("import never overwrites");
var original = new Preset { Name = "Shared" };
PresetStore.Save(original);

string temp = Path.Combine(Path.GetTempPath(), "shared-preset.json");
PresetStore.Export(new Preset { Name = "Shared", Description = "from elsewhere" }, temp);

string? landed = PresetStore.Import(temp);
Check("imported under a different name", landed is not null && landed != "Shared");
Check("the original is untouched",
    PresetStore.Read(PresetStore.PathFor("Shared"))?.Description is null);

if (landed is not null) PresetStore.Delete(landed);
PresetStore.Delete("Shared");
File.Delete(temp);

Console.WriteLine();
Console.WriteLine("a preset holds everything, so everything is compared");

// The scope object is gone. What replaced it is the rule that a preset carries
// the whole desk, so these assert that nothing is quietly skipped any more.
var saved = new Preset { Name = "S" };
saved.Monitors["t"] = new PresetMonitor { Label = "M", Brightness = 50, WallpaperPath = "a.jpg", Width = 1920, Height = 1080 };

var live = new Preset { Name = "S" };
live.Monitors["t"] = new PresetMonitor { Label = "M", Brightness = 50, WallpaperPath = "b.jpg", Width = 1920, Height = 1080 };

Check("a wallpaper change is reported", PresetDiff.Describe(saved, live).Count == 1);

live.Monitors["t"].SoftwareBrightness = 60;
Check("so is software dimming", PresetDiff.Describe(saved, live).Count == 2);

live.Monitors["t"].HideTaskbar = true;
Check("so is taskbar hiding", PresetDiff.Describe(saved, live).Count == 3);

// The taskbar block is absent on a preset saved before it was captured, and
// absent must not read as "wants the defaults".
var noBar = new Preset { Name = "Old" };
var withBar = new Preset { Name = "New" };
withBar.Global.Taskbar = new PresetTaskbar { HideDelayMs = 900 };
Check("an old preset says nothing about the taskbar", PresetDiff.Describe(noBar, withBar).Count == 0);

var barA = new Preset { Name = "A" };
barA.Global.Taskbar = new PresetTaskbar { HideDelayMs = 350 };
var barB = new Preset { Name = "B" };
barB.Global.Taskbar = new PresetTaskbar { HideDelayMs = 900 };
Check("two presets that both have one are compared", PresetDiff.Describe(barA, barB).Count == 1);

// Same for variable refresh, which is null on a machine with nothing capable.
var vrrNull = new Preset { Name = "A" };
var vrrOn = new Preset { Name = "B" };
vrrOn.Global.VariableRefreshRate = true;
Check("unknown variable refresh is not drift", PresetDiff.Describe(vrrNull, vrrOn).Count == 0);

vrrNull.Global.VariableRefreshRate = false;
Check("known variable refresh is", PresetDiff.Describe(vrrNull, vrrOn).Count == 1);

// A change has to be readable on its own, since it is shown in a flyout with
// no surrounding prose to lean on.
var readable = PresetDiff.Describe(barA, barB)[0];
Check("a change names the setting", readable.Setting.Contains("delay", StringComparison.OrdinalIgnoreCase));
Check("a change carries both values", readable.Now.Length > 0 && readable.Saved.Length > 0);
Check("and reads as a sentence for the command line", readable.Line.Contains("preset has"));

Console.WriteLine();
Console.WriteLine("brightness tolerance");
var a = new Preset { Name = "S" };
a.Monitors["t"] = new PresetMonitor { Label = "M", Brightness = 50 };
var b = new Preset { Name = "S" };
b.Monitors["t"] = new PresetMonitor { Label = "M", Brightness = 52 };
Check("two points of DDC/CI rounding is not a difference", PresetDiff.Describe(a, b).Count == 0);

b.Monitors["t"].Brightness = 56;
Check("six points is", PresetDiff.Describe(a, b).Count == 1);

Console.WriteLine();
Console.WriteLine("a display in the preset but not attached");
var want = new Preset { Name = "S" };
want.Monitors["gone"] = new PresetMonitor { Label = "Unplugged", Brightness = 50 };
var now = new Preset { Name = "S" };
Check("is not reported as drift", PresetDiff.Describe(want, now).Count == 0);

Console.WriteLine();
Console.WriteLine("schedule window crossing midnight");
var n = new NightLightSettings { Enabled = true, Scheduled = true, FromMinutes = 20 * 60, ToMinutes = 7 * 60 };
Check("22:00 is inside", n.ActiveAt(new DateTime(2026, 1, 1, 22, 0, 0)));
Check("03:00 is inside", n.ActiveAt(new DateTime(2026, 1, 1, 3, 0, 0)));
Check("12:00 is outside", !n.ActiveAt(new DateTime(2026, 1, 1, 12, 0, 0)));
Check("07:00 exactly is outside", !n.ActiveAt(new DateTime(2026, 1, 1, 7, 0, 0)));
Check("20:00 exactly is inside", n.ActiveAt(new DateTime(2026, 1, 1, 20, 0, 0)));

n.FromMinutes = n.ToMinutes = 600;
Check("an empty window is never active", !n.ActiveAt(new DateTime(2026, 1, 1, 10, 0, 0)));

Console.WriteLine();
Console.WriteLine("app rule matching");
var rule = new AppRule { Process = "Chrome.exe", Preset = "P" };
Check("matches without the extension, any case", rule.Matches("chrome"));
Check("does not match a different app", !rule.Matches("chromium"));
rule.Enabled = false;
Check("a disabled rule never matches", !rule.Matches("chrome"));

var incomplete = new AppRule { Process = "x" };
Check("an incomplete rule is flagged", !incomplete.IsComplete);

Console.WriteLine();

Console.WriteLine();
Console.WriteLine("the monitor's own settings in a preset");
{
    var vcpSaved = new Preset { Name = "S" };
    vcpSaved.Monitors["t"] = new PresetMonitor
    {
        Label = "DELL",
        MonitorControls = { ["0x12"] = 75, ["0x14"] = 5 },
    };

    var vcpLive = new Preset { Name = "S" };
    vcpLive.Monitors["t"] = new PresetMonitor
    {
        Label = "DELL",
        MonitorControls = { ["0x12"] = 58, ["0x14"] = 5 },
    };

    var diffs = PresetDiff.Describe(vcpSaved, vcpLive);
    Check("a changed contrast is drift", diffs.Count == 1);
    Check("and it names the code", diffs.Count == 1 && diffs[0].Setting.Contains("0x12"));

    vcpLive.Monitors["t"].MonitorControls["0x12"] = 75;
    Check("matching settings are not", PresetDiff.Describe(vcpSaved, vcpLive).Count == 0);

    // A preset from another machine can name codes this monitor lacks.
    vcpLive.Monitors["t"].MonitorControls.Remove("0x14");
    Check("a code this monitor no longer reports is not drift",
        PresetDiff.Describe(vcpSaved, vcpLive).Count == 0);

    vcpLive.Monitors["t"].MonitorControls["0x12"] = 1;
    Check("a changed control is always reported now", PresetDiff.Describe(vcpSaved, vcpLive).Count == 1);
}

Console.WriteLine();
Console.WriteLine("gamma limits follow the clamp");
{
    // The floors are computed from the clamp, so they must move together and
    // never invert. Whichever state this machine is in, both must hold.
    bool full = NightLight.FullRange;
    Console.WriteLine($"    clamp lifted on this machine: {full}");

    int neutralFloor = NightLight.LowestDim(0);
    int warmFloor = NightLight.LowestDim(100);

    Check("dimming is limited less at neutral than at full warmth", neutralFloor <= warmFloor);
    Check("every floor is a usable percentage",
        neutralFloor is >= 1 and <= 100 && warmFloor is >= 1 and <= 100);

    double neutralK = NightLight.KelvinFor(0);
    double warmestK = NightLight.KelvinFor(100);
    Check("warmth runs from 6500K downwards", Math.Abs(neutralK - 6500) < 1 && warmestK < neutralK);
    Check("full warmth matches what the machine allows",
        Math.Abs(warmestK - NightLight.WarmestAvailableKelvin) < 1);

    Check("lifting the clamp would lower the floor, not raise it",
        full ? neutralFloor < 53 : neutralFloor >= 53);
}

Console.WriteLine();
Console.WriteLine("physical arrangement layout");
{
    // The real desk: a 23.8" Dell at 1920x1080 with a 14" 2880x1800 laptop
    // panel to its right, both top-aligned.
    var dell = new PhysicalLayout.Panel("dell", 0, 0, 1920, 1080, 527.0 / 1920, 296.0 / 1080);
    var oled = new PhysicalLayout.Panel("oled", 1920, 0, 2880, 1800, 302.0 / 2880, 189.0 / 1800);

    var laid = PhysicalLayout.Resolve([dell, oled]);
    var d = laid.First(x => x.Token == "dell");
    var o = laid.First(x => x.Token == "oled");

    Console.WriteLine($"    dell {d.Width:0} x {d.Height:0} mm at {d.X:0},{d.Y:0}");
    Console.WriteLine($"    oled {o.Width:0} x {o.Height:0} mm at {o.X:0},{o.Y:0}");

    Check("the Dell is drawn its real 527 x 296", Math.Abs(d.Width - 527) < 1 && Math.Abs(d.Height - 296) < 1);
    Check("the laptop is drawn its real 302 x 189", Math.Abs(o.Width - 302) < 1 && Math.Abs(o.Height - 189) < 1);
    Check("the laptop is the narrower of the two", o.Width < d.Width);
    Check("they touch, with no gap and no overlap", Math.Abs((d.X + d.Width) - o.X) < 0.01);
    Check("both are top aligned, as they are in pixels", Math.Abs(d.Y - o.Y) < 0.01);

    // Half way down the Dell, in pixel space, should be half way down in mm.
    var lower = oled with { Y = 540 };
    var offset = PhysicalLayout.Resolve([dell, lower]);
    var lo = offset.First(x => x.Token == "oled");
    Check("an offset neighbour keeps its proportional position",
        Math.Abs(lo.Y - (296.0 / 2)) < 1);

    // No EDID size anywhere means pixels, honestly, rather than a mixture.
    var bare = new PhysicalLayout.Panel("bare", 0, 0, 1920, 1080, 0, 0);
    var fallback = PhysicalLayout.Resolve([bare]);
    Check("a display with no reported size falls back to pixels",
        Math.Abs(fallback[0].Width - 1920) < 0.01);
}

Console.WriteLine();
Console.WriteLine("where a dragged display may land");
{
    // The same desk, as the solver sees it: pixels, and which one is primary.
    var dell = new ArrangementSolver.Panel("dell", 0, 0, 1920, 1080, true);
    var oled = new ArrangementSolver.Panel("oled", 1920, 0, 2880, 1800);

    var slots = ArrangementSlots.For([dell, oled], "oled");
    Console.WriteLine($"    {slots.Count} places for the laptop beside one monitor");

    Check("there are places to put it", slots.Count > 0);

    // Four sides, three alignments, less whatever coincides or overlaps. The
    // laptop is taller and wider than the Dell, so nothing coincides here.
    Check("every side of the neighbour is offered",
        slots.Select(s => s.Side).Distinct().Count() == 4);
    Check("each side offers more than one alignment",
        slots.Where(s => s.Side == ArrangementSlots.Side.Right).Select(s => s.Align).Distinct().Count() == 3);

    Check("no two slots are the same position",
        slots.Select(s => (s.X, s.Y)).Distinct().Count() == slots.Count);

    // The point of the whole exercise: Windows takes every one of them.
    bool allLegal = slots.All(s =>
        ArrangementSolver.IsValid([dell, oled with { X = s.X, Y = s.Y }]));
    Check("every slot is an arrangement Windows accepts", allLegal);

    // Centre alignment is what makes a small panel beside a big one look
    // deliberate rather than snapped to an edge it does not share.
    var centred = slots.First(s => s.Side == ArrangementSlots.Side.Right
                                && s.Align == ArrangementSlots.Align.Centre);
    Check("a centred slot centres it on the neighbour",
        centred.Y + (1800 / 2) == 1080 / 2);

    // A third display standing in the way removes the slot, rather than
    // offering a position that would be refused without saying why.
    var blocker = new ArrangementSolver.Panel("block", 1920, 0, 1920, 1080);
    var crowded = ArrangementSlots.For([dell, blocker, oled], "oled");

    Check("a slot occupied by a third display is not offered",
        !crowded.Any(s => s.X == 1920 && s.Y == 0));
    Check("and the rest still are", crowded.Count > 0);
    Check("all of those are legal too",
        crowded.All(s => ArrangementSolver.IsValid([dell, blocker, oled with { X = s.X, Y = s.Y }])));

    // Identical panels put three alignments in the same place; one is enough.
    var twinA = new ArrangementSolver.Panel("a", 0, 0, 1920, 1080, true);
    var twinB = new ArrangementSolver.Panel("b", 1920, 0, 1920, 1080);
    var twins = ArrangementSlots.For([twinA, twinB], "b");
    Check("identical panels collapse their three alignments into one",
        twins.Count == 4);

    // One display has nowhere to be; its position is the origin by definition.
    Check("a lone display is offered nothing",
        ArrangementSlots.For([dell], "dell").Count == 0);

    var nearest = ArrangementSlots.Nearest(slots, 1920, 0);
    Check("the nearest slot to a corner is that corner",
        nearest is { X: 1920, Y: 0 });

    // What the surface actually draws. It asks PhysicalLayout where a slot would
    // put the panel, and ranks the slots by that rather than by pixel distance,
    // because a pixel is a different real size on each display. This is the path
    // a drag runs through, and a drag cannot be tested through a pointer:
    // synthetic input does not reach a WinUI canvas.
    var dellMm = new PhysicalLayout.Panel("dell", 0, 0, 1920, 1080, 527.0 / 1920, 296.0 / 1080);
    var oledMm = new PhysicalLayout.Panel("oled", 1920, 0, 2880, 1800, 302.0 / 2880, 189.0 / 1800);
    var dellAt = PhysicalLayout.Resolve([dellMm, oledMm]).First(x => x.Token == "dell");

    bool flushInMm = slots.All(s =>
    {
        PhysicalLayout.Placed at = PhysicalLayout.Hang(dellMm, oledMm with { X = s.X, Y = s.Y }, dellAt);

        return s.Side switch
        {
            ArrangementSlots.Side.Right => Math.Abs(at.X - (dellAt.X + dellAt.Width)) < 0.01,
            ArrangementSlots.Side.Left => Math.Abs(at.X + at.Width - dellAt.X) < 0.01,
            ArrangementSlots.Side.Below => Math.Abs(at.Y - (dellAt.Y + dellAt.Height)) < 0.01,
            _ => Math.Abs(at.Y + at.Height - dellAt.Y) < 0.01,
        };
    });

    Check("every slot is drawn flush in millimetres as well as in pixels", flushInMm);

    // Drawn at the panel's real size wherever it lands, not the neighbour's.
    bool realSize = slots.All(s =>
    {
        PhysicalLayout.Placed at = PhysicalLayout.Hang(dellMm, oledMm with { X = s.X, Y = s.Y }, dellAt);
        return Math.Abs(at.Width - 302) < 1 && Math.Abs(at.Height - 189) < 1;
    });

    Check("and at its real size in every one of them", realSize);

    bool alignedInMm = slots.All(s =>
    {
        var at = PhysicalLayout.Hang(dellMm, oledMm with { X = s.X, Y = s.Y }, dellAt);
        bool vertical = s.Side is ArrangementSlots.Side.Left or ArrangementSlots.Side.Right;
        double offset = vertical ? at.Y - dellAt.Y : at.X - dellAt.X;
        double remaining = vertical ? dellAt.Height - at.Height : dellAt.Width - at.Width;
        double expected = s.Align switch
        {
            ArrangementSlots.Align.Start => 0,
            ArrangementSlots.Align.Centre => remaining / 2,
            _ => remaining,
        };
        return Math.Abs(offset - expected) < 0.01;
    });
    Check("unequal-density panels preserve top, centre and bottom alignment physically", alignedInMm);
    Check("physical aspect ratios are preserved",
        Math.Abs(dellAt.Width / dellAt.Height - 527.0 / 296) < 0.0001);

}

Console.WriteLine();
Console.WriteLine("EDID, decoded from the panels actually attached");
{
    foreach (DisplayInfo d in DisplayRegistry.Enumerate())
    {
        EdidDetails edid = EdidReader.Describe(d.Key.DevicePath);

        Console.WriteLine($"  {d.Label}");

        if (!edid.Present)
        {
            Check($"{d.Label} has a readable EDID", false);
            continue;
        }

        Console.WriteLine($"    {edid.ManufacturerCode} ({edid.ManufacturerName}) {edid.ProductCode}"
            + $", EDID {edid.Version}, {edid.Bytes} bytes, made {edid.Made}");
        Console.WriteLine($"    {(edid.Digital ? $"digital, {edid.BitsPerColour}-bit, {edid.Interface}" : "analogue")}"
            + $", {edid.WidthCm} x {edid.HeightCm} cm, gamma {edid.Gamma:0.00}");
        Console.WriteLine($"    {edid.ColourEncodings}; white {edid.White}, red {edid.Red}");
        Console.WriteLine($"    {edid.EstablishedTimings.Count} established, {edid.StandardTimings.Count} standard, "
            + $"{edid.DetailedTimings.Count} detailed, {edid.Extensions} extension block(s)");

        foreach (string t in edid.DetailedTimings) Console.WriteLine($"      {t}");

        Check($"{d.Label}: the checksum adds up", edid.ChecksumValid);
        Check($"{d.Label}: the manufacturer code is three letters", edid.ManufacturerCode.Length == 3);
        Check($"{d.Label}: the code matches the one identity is keyed on",
            d.Key.Model.StartsWith(edid.ManufacturerCode, StringComparison.Ordinal));
        Check($"{d.Label}: EDID version is 1.x or better", edid.Version.Length >= 3);
        Check($"{d.Label}: it says when it was made", edid.Year is > 1990 and < 2100);

        // A decoded size that disagrees with the one the layout is drawn from
        // would put the arrangement diagram out by centimetres.
        if (d.HasPhysicalSize && edid.WidthCm > 0)
            Check($"{d.Label}: the decoded size agrees with the one the diagram uses",
                Math.Abs((edid.WidthCm * 10) - d.PhysicalWidthMm) <= 10);

        // The preferred timing is the panel's native mode, so it should be at
        // least as large as whatever Windows has it running at.
        if (edid.DetailedTimings.Count > 0)
            Check($"{d.Label}: the first detailed timing is marked preferred",
                edid.DetailedTimings[0].Contains("(preferred)", StringComparison.Ordinal));

        Check($"{d.Label}: chromaticity is inside the CIE diagram",
            edid.White.X is > 0 and < 1 && edid.White.Y is > 0 and < 1);
    }
}

// ---------------------------------------------------------------- redaction --
// The part that has to be right. Everything below asks the same question: can
// anything that identifies this machine or this person reach a public issue?

Console.WriteLine();
Console.WriteLine("redaction: what must never be published");

const string Serial = "3QQQ2X3";
string[] serials = [Serial];

Check("a serial is removed", !Redact.Scrub($"Serial {Serial} here", serials).Contains(Serial));
Check("a serial is removed whatever its case",
    !Redact.Scrub($"serial {Serial.ToLowerInvariant()}", serials).Contains(Serial, StringComparison.OrdinalIgnoreCase));

Check("a device path is removed",
    !Redact.Scrub(@"path \\?\DISPLAY#SDC4154#5&1af48b2f&0&UID256#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}")
        .Contains("SDC4154#5"));

Check("a user profile path is removed",
    !Redact.Scrub(@"wallpaper C:\Users\Jesvi Jonathan\Pictures\a.jpg").Contains("Users"));

// The one that got through. An account name with a space in it used to end the
// match at the forename, leaving the surname, the folder tree and whatever the
// file name carried. Most Windows account names have a space in them.
Check("a profile path survives no part of a spaced account name",
    Redact.Scrub(@"wallpaper C:\Users\Ada Lovelace\Pictures\a.jpg") is string spaced
    && !spaced.Contains("Lovelace") && !spaced.Contains("Pictures") && !spaced.Contains(".jpg"));

// And the fragment it was hiding: an instance id does not need its path prefix
// to identify one panel on one port of one machine.
Check("a bare device instance id is removed",
    !Redact.Scrub("Shift-SDC4154#5&1af48b2f&0&UID256-2.jpg").Contains("1af48b2f"));

Check("an instance id inside a file name is removed",
    !Redact.Scrub(@"C:\x\Shift-DELA234#5&1af48b2f&0&UID257#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}-2.jpg")
        .Contains("UID257"));

Check("a bare GUID is removed",
    !Redact.Scrub("{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}").Contains("e6f07b5f"));

Check("the account name is removed",
    !Redact.Scrub($"signed in as {Environment.UserName}")
        .Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));

// Over-redaction would be its own failure: a record scrubbed into uselessness
// teaches nobody anything about the monitor.
const string Capabilities = "(prot(monitor)type(LCD)model(U2424H)cmds(01 02 03 07 0C E3 F3)"
    + "vcp(02 04 05 08 10 12 14(01 04 05 06 08 09 0B 0C) 16 18 1A 52 60(0F 11) 87 AC AE B2 B6)mccs_ver(2.1))";

Check("a capabilities string survives untouched", Redact.Scrub(Capabilities, serials) == Capabilities);
Check("and reads as clean", Redact.IsClean(Capabilities, serials));
Check("ordinary text is left alone", Redact.Scrub("DEL U2424H, 527 x 296 mm") == "DEL U2424H, 527 x 296 mm");

Check("a key splits into maker and product", Redact.Manufacturer("DEL-A234") == "DEL" && Redact.Product("DEL-A234") == "A234");
Check("a key with no product does not invent one", Redact.Product("SDC") == "");

Console.WriteLine();
Console.WriteLine("redaction: end to end, over the monitors actually attached");

// The assertion that protects the person using this. Everything above tests the
// scrub in isolation; this one asks whether the text the application would
// really publish carries anything it should not.
List<DisplayInfo> attached = DisplayRegistry.Enumerate();
var secrets = new List<string>();

foreach (DisplayInfo d in attached)
{
    if (d.Key.HasSerial) secrets.Add(d.Key.Serial);
    secrets.Add(d.Key.DevicePath);

    // The token too, and for every panel rather than the one being submitted.
    // A record carries the presets, a preset names every display on the desk,
    // and a panel with no EDID serial still has a token whose suffix is a hash
    // of its device path - unique to that unit on that port, and invisible to a
    // check that only looks for serials. That is exactly what leaked.
    secrets.Add(d.Token);
}

secrets.Add(Environment.UserName);

// A record is built for one display at a time, and the narrowed list is what
// `dispctrl contribute --display 2` passes. Submitting one monitor must not
// publish the other one's identifiers, so both forms are checked.
foreach (DisplayInfo d in attached)
{
    Contribution narrowed = DeviceContribution.Prepare(d, [d]);

    foreach (string secret in secrets)
        if (secret.Length >= 4)
            Check($"{narrowed.Key}, built for one display alone, does not carry \"{Shorten(secret)}\"",
                !narrowed.Body.Contains(secret, StringComparison.OrdinalIgnoreCase));
}

foreach (DisplayInfo d in attached)
{
    Contribution c = DeviceContribution.Prepare(d, attached);
    Console.WriteLine($"    {c.Title}: {c.Body.Length} characters, {(c.Prefilled ? "prefills" : "needs pasting")}");

    foreach (string secret in secrets)
        if (secret.Length >= 4)
            Check($"{c.Key} does not carry \"{Shorten(secret)}\"",
                !c.Body.Contains(secret, StringComparison.OrdinalIgnoreCase));

    // What the whole change was for: a record now carries the report and the
    // presets, so it has to actually contain them.
    // Fragments, not only whole strings. Both leaks that got this far survived
    // as pieces of something the check was looking for in one piece.
    foreach (string word in Environment.UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        if (word.Length >= 4)
            Check($"{c.Key} does not carry \"{word}\", a part of the account name",
                !c.Body.Contains(word, StringComparison.OrdinalIgnoreCase));

    Check($"{c.Key} carries no device instance id", !c.Body.Contains("UID2", StringComparison.OrdinalIgnoreCase));

    Check($"{c.Key} carries the full report for its display", c.Body.Contains("Current mode"));
    // Presets are behind a build flag, and the record follows it: with the flag
    // off they are absent by design, not missing by accident. Asserting the
    // published shape either way is what keeps this honest when the flag moves.
    Check($"{c.Key} matches the preset flag",
        FeatureFlags.Presets
            ? c.Body.Contains("preset(s) saved") || c.Body.Contains("No presets are saved")
            : !c.Body.Contains("preset(s) saved") && !c.Body.Contains("Presets saved on this machine"));

    Check($"{c.Key} says something about the monitor", c.Body.Length > 200);
    Check($"{c.Key} is filed under the model, not the unit", !c.Key.Contains('_') && c.Key.Length <= 12);
    Check($"{c.Key} is plain ASCII, so the URL stays short", c.Body.All(char.IsAscii));
}

Console.WriteLine();
Console.WriteLine("the desk-wide record, which is what Submit actually opens");
{
    Contribution desk = DeviceContribution.PrepareDesk(attached);

    Console.WriteLine($"    title: {desk.Title}");
    Console.WriteLine($"    {desk.Body.Length} characters, {(desk.Prefilled ? "prefills" : "needs pasting")}");

    // The title is the index entry for the issue, so every distinct display has
    // to be in it - that was the complaint that prompted the desk-wide form.
    foreach (DisplayInfo d in attached)
    {
        string name = string.IsNullOrWhiteSpace(d.Label) ? d.Key.Model : d.Label;
        Check($"the title names {name}", desk.Title.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    // The whole file, not a per-display slice of it. Asked for by name.
    Check("it carries displays.log entire", desk.Body.Contains("DisplCtrl display report"));
    Check("including every display's section",
        desk.Body.Split("Modes the driver reports").Length - 1 >= attached.Count);
    Check("and the presets, when the flag is on",
        FeatureFlags.Presets
            ? desk.Body.Contains("preset(s) saved") || desk.Body.Contains("No presets are saved")
            : !desk.Body.Contains("preset(s) saved"));

    // A built-in panel has no name of its own; the machine's model is what
    // anyone would search for.
    bool anyInternal = attached.Any(d => d.IsInternal);
    MachineInfo machine = MachineInfo.Read();

    if (anyInternal && machine.Present)
        Check($"a built-in panel is tied to the machine ({machine.Model})",
            desk.Body.Contains(machine.Model, StringComparison.OrdinalIgnoreCase));

    Check("it is plain ASCII", desk.Body.All(char.IsAscii));

    foreach (string secret in secrets)
        if (secret.Length >= 4)
            Check($"the desk record does not carry \"{Shorten(secret)}\"",
                !desk.Body.Contains(secret, StringComparison.OrdinalIgnoreCase));

    foreach (string word in Environment.UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        if (word.Length >= 4)
            Check($"the desk record does not carry \"{word}\"",
                !desk.Body.Contains(word, StringComparison.OrdinalIgnoreCase));

    Check("the desk record carries no device instance id",
        !desk.Body.Contains("UID2", StringComparison.OrdinalIgnoreCase));

    // It will not prefill, and the panel relies on knowing that to put the text
    // on the clipboard instead. A record that quietly started fitting would
    // leave that path untested rather than broken, so this is worth asserting.
    Check("it is too long to prefill, as expected", !desk.Prefilled);
}


static string Shorten(string s) => s.Length <= 24 ? s : s[..24] + "...";

// ---- Windows' own night light ----------------------------------------------
//
// The scale was measured rather than documented: the slider in Settings was
// swept through all 101 positions and the stored value read back at each. These
// assert the curve that sweep produced, so a wrong constant is caught here
// instead of by a desk that ends up the wrong colour. Nothing here writes: the
// live read is allowed to be unavailable, because a Windows build that moves
// the data should fail the feature, not the test run.
{
    Console.WriteLine();
    Console.WriteLine("Windows night light");

    Check("neutral at zero", WindowsNightLight.KelvinFor(0) == 6500);
    Check("1200K at full", WindowsNightLight.KelvinFor(100) == 1200);
    Check("53K a step", WindowsNightLight.KelvinFor(50) == 6500 - (53 * 50));

    bool monotonic = true;
    for (int i = 1; i <= 100; i++)
        if (WindowsNightLight.KelvinFor(i) >= WindowsNightLight.KelvinFor(i - 1)) monotonic = false;

    Check("warmer at every step", monotonic);

    // Out of range is clamped rather than extrapolated: a strength of -5 must
    // not read as bluer than neutral, which no display can be asked for.
    Check("clamped below", WindowsNightLight.KelvinFor(-5) == 6500);
    Check("clamped above", WindowsNightLight.KelvinFor(400) == 1200);

    WindowsNightLightState? reported = WindowsNightLight.Read();
    Check("the live state reads, or says it cannot",
        reported is null || (reported.Value.Strength >= 0 && reported.Value.Strength <= 100));

    // Dark mode is a plain registry value with none of the night light's
    // encoding, so there is little to get wrong beyond reading the wrong one of
    // the two. Asserting it answers at all catches a renamed value, which is the
    // way this would actually break.
    Check("dark mode answers", WindowsTheme.IsDark is not null);
}

// ---- focus dimming and OLED screen rest ------------------------------------
//
// Synthetic pointer input never reaches an overlay, and a check that blacked a
// panel would be one nobody runs twice, so what is asserted here is the
// arithmetic the engine decides with rather than the dimming itself.
{
    Console.WriteLine();
    Console.WriteLine("Focus and OLED rest");

    const long grace = FocusGeometry.ManualRestGraceMs;

    Check("no rest when none was asked for",
        !FocusGeometry.RestingByHand(false, 60_000, 0, true));

    // The press that starts a rest is input, so it must not end it.
    Check("the starting press does not end it",
        FocusGeometry.RestingByHand(true, 0, 0, true));
    Check("still resting just inside the grace",
        FocusGeometry.RestingByHand(true, grace - 1, grace - 1, true));

    // Left alone, idle time keeps pace with the rest, so it stays black.
    Check("an untouched rest keeps going",
        FocusGeometry.RestingByHand(true, 300_000, 300_000, true));

    // Input well after the start leaves idle time trailing the rest.
    Check("input after the grace ends it",
        !FocusGeometry.RestingByHand(true, 60_000, 0, true));
    Check("a nudge mid-rest ends it",
        !FocusGeometry.RestingByHand(true, 60_000, 60_000 - grace - 1, true));

    // Without a usable idle reading, the rest is honoured rather than dropped:
    // the duration still bounds it, so the failure cannot leave a stuck screen.
    Check("an unreadable idle timer keeps the rest",
        FocusGeometry.RestingByHand(true, 300_000, 0, false));

    // The idle rest, which is the one that blacks a panel nobody asked it to.
    const long minute = 60_000;
    Check("idle rest waits for the whole spell",
        !FocusGeometry.RestingWhenIdle(true, true, 4 * minute, 5, false, false));
    Check("and comes on once it is up",
        FocusGeometry.RestingWhenIdle(true, true, 5 * minute, 5, false, false));

    Check("switched off it never rests",
        !FocusGeometry.RestingWhenIdle(false, true, 60 * minute, 5, false, false));

    // Fails closed: a broken idle clock must not black a screen being watched.
    Check("an unreadable idle clock rests nothing",
        !FocusGeometry.RestingWhenIdle(true, false, 60 * minute, 5, false, false));

    Check("a suspended machine rests nothing",
        !FocusGeometry.RestingWhenIdle(true, true, 60 * minute, 5, true, false));
    Check("fullscreen holds it off",
        !FocusGeometry.RestingWhenIdle(true, true, 60 * minute, 5, false, true));

    // The minutes come from a hand-edited file.
    Check("nonsense minutes are clamped, not trusted",
        FocusGeometry.RestingWhenIdle(true, true, 2 * minute, -5, false, false)
        && !FocusGeometry.RestingWhenIdle(true, true, 100 * minute, 9999, false, false));

    Check("no dimming at zero", FocusGeometry.Alpha(0) == 0);
    Check("black at full", FocusGeometry.Alpha(100) == 255);
    Check("dimming is clamped", FocusGeometry.Alpha(400) == 255 && FocusGeometry.Alpha(-5) == 0);

    var monitor = new DisplayRect(0, 0, 1920, 1080);
    Check("a maximised window covers its monitor",
        FocusGeometry.Covers(new DisplayRect(0, 0, 1920, 1080), monitor));
    Check("a window short of the edge does not",
        !FocusGeometry.Covers(new DisplayRect(0, 0, 1920, 1000), monitor));

    // A window on the next monitor along must not count as being on this one,
    // or the focus hole would be cut out of the wrong panel.
    DisplayRect off = FocusGeometry.Intersect(new DisplayRect(2000, 0, 2600, 400), monitor);
    Check("a window elsewhere intersects nothing", off.Width == 0 || off.Height == 0);

    DisplayRect overlap = FocusGeometry.Intersect(new DisplayRect(-100, -100, 300, 300), monitor);
    Check("an overlapping window is clipped to the monitor",
        overlap.Left == 0 && overlap.Top == 0 && overlap.Right == 300 && overlap.Bottom == 300);

    // The hole has to cover where a dragged window was as well as where it is,
    // or the dim flickers along the edge it just left.
    var was = new DisplayRect(100, 100, 500, 400);
    var isNow = new DisplayRect(160, 100, 560, 400);
    DisplayRect swept = FocusGeometry.Sweep(was, isNow, 300);
    Check("a drag sweeps from where it was", swept.Left == 100 && swept.Right == 560);
    Check("and keeps the other edges", swept.Top == 100 && swept.Bottom == 400);

    Check("standing still sweeps nothing extra",
        FocusGeometry.Sweep(isNow, isNow, 300) == isNow);

    // A snap across the desk is a jump, not a drag: sweeping it would undim a
    // band the width of the screen for a frame.
    Check("a jump is not swept",
        FocusGeometry.Sweep(was, new DisplayRect(1500, 100, 1900, 400), 300) == new DisplayRect(1500, 100, 1900, 400));

    Check("a first frame with no history is left alone",
        FocusGeometry.Sweep(default, isNow, 300) == isNow);

    // One overlay alpha is not one amount of dimming: it is far heavier over a
    // panel already running dim, so the figure is scaled by where the panel sits
    // between its own limits.
    Check("a panel at full brightness keeps the whole dim",
        FocusGeometry.ScaledDim(80, 100) == 80);
    Check("a panel at its floor keeps half",
        FocusGeometry.ScaledDim(80, 0) == 40);
    Check("and half way sits between",
        FocusGeometry.ScaledDim(80, 50) is > 40 and < 80);
    Check("scaling never inverts the order",
        FocusGeometry.ScaledDim(80, 20) <= FocusGeometry.ScaledDim(80, 80));
    Check("no dim stays no dim", FocusGeometry.ScaledDim(0, 100) == 0);

    // Switching windows moves the hole without changing the dim, so the slide is
    // the only thing the fade setting can act on there.
    var fromWin = new DisplayRect(100, 100, 500, 400);
    var toWin = new DisplayRect(900, 100, 1300, 400);
    Check("a slide starts at the old window",
        FocusGeometry.Between(fromWin, toWin, 0, 300) == fromWin);
    Check("and finishes on the new one",
        FocusGeometry.Between(fromWin, toWin, 300, 300) == toWin);
    Check("past the end it stays put",
        FocusGeometry.Between(fromWin, toWin, 5000, 300) == toWin);

    DisplayRect half = FocusGeometry.Between(fromWin, toWin, 150, 300);
    Check("half way is between the two", half.Left > 100 && half.Left < 900);
    Check("and keeps the window's size", half.Width == fromWin.Width);

    Check("no duration means no slide",
        FocusGeometry.Between(fromWin, toWin, 10, 0) == toWin);
    Check("nothing to slide from lands straight away",
        FocusGeometry.Between(default, toWin, 10, 300) == toWin);

    // Two layers compose as 1-(1-a)(1-b), so fading them independently would
    // lighten the whole surround mid-switch. The arriving layer is solved for.
    Check("nothing leaving means the layer carries the dim on its own",
        Math.Abs(FocusGeometry.Overlay(166, 0) - 166) < 1);
    Check("a layer still at full dim needs nothing from the other",
        FocusGeometry.Overlay(166, 166) < 1);

    double mid = FocusGeometry.Overlay(166, 83);
    Check("half way it takes up part of the load", mid > 0 && mid < 166);
    Check("and the two together still read as the chosen dim",
        Math.Abs((1 - ((1 - (mid / 255.0)) * (1 - (83 / 255.0)))) - (166 / 255.0)) < 0.01);

    Check("no dim needs no layer", FocusGeometry.Overlay(0, 0) < 1);
    Check("out of range brightness is clamped",
        FocusGeometry.ScaledDim(80, 400) == 80 && FocusGeometry.ScaledDim(80, -50) == 40);
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "all checks passed" : $"{failures} FAILED");
return failures;
