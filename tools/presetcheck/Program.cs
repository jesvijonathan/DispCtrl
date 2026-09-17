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
Console.WriteLine("a preset that controls nothing");
var empty = new Preset { Name = "Nothing" };
empty.Scope.Arrangement = false;
empty.Scope.Modes = false;
empty.Scope.Brightness = false;
empty.Scope.NightLight = false;
Check("reports itself as empty", empty.Scope.IsEmpty);

Console.WriteLine();
Console.WriteLine("diff honours scope");
var saved = new Preset { Name = "S" };
saved.Scope.Wallpaper = false;
saved.Monitors["t"] = new PresetMonitor { Label = "M", Brightness = 50, WallpaperPath = "a.jpg" };

var live = new Preset { Name = "S" };
live.Monitors["t"] = new PresetMonitor { Label = "M", Brightness = 50, WallpaperPath = "b.jpg" };

Check("a wallpaper change is ignored when out of scope",
    PresetDiff.Describe(saved, live).Count == 0);

saved.Scope.Wallpaper = true;
Check("and reported when in scope", PresetDiff.Describe(saved, live).Count == 1);

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
Console.WriteLine(failures == 0 ? "all checks passed" : $"{failures} FAILED");
return failures;
