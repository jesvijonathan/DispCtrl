using Umbra.Core.Displays;
using Umbra.Display.Devices;
using Umbra.Core.Presets;
using Umbra.Core.Settings;

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
}

secrets.Add(Environment.UserName);

foreach (DisplayInfo d in attached)
{
    Contribution c = DeviceContribution.Prepare(d, attached);
    Console.WriteLine($"    {c.Title}: {c.Body.Length} characters, {(c.Prefilled ? "prefills" : "needs pasting")}");

    foreach (string secret in secrets)
        if (secret.Length >= 4)
            Check($"{c.Key} does not carry \"{Shorten(secret)}\"",
                !c.Body.Contains(secret, StringComparison.OrdinalIgnoreCase));

    Check($"{c.Key} says something about the monitor", c.Body.Length > 200);
    Check($"{c.Key} is filed under the model, not the unit", !c.Key.Contains('_') && c.Key.Length <= 12);
    Check($"{c.Key} is plain ASCII, so the URL stays short", c.Body.All(char.IsAscii));
}

static string Shorten(string s) => s.Length <= 24 ? s : s[..24] + "...";

Console.WriteLine();
Console.WriteLine(failures == 0 ? "all checks passed" : $"{failures} FAILED");
return failures;
