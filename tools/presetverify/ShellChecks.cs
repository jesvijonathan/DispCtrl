using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;

internal static class ShellChecks
{
    public static void Run(Action<bool, string> check)
    {
        // A monitor stacked above the laptop: its bar hid onto the laptop's screen.
        var upper = new DisplayRect(0, -1080, 1920, 0);
        var laptop = new DisplayRect(0, 0, 1440, 900);
        var (blocked, beyond) = TaskbarParking.Plan(upper, TaskbarSide.Bottom, 48, [upper, laptop]);
        check(blocked && beyond == 900, "a bar with a monitor below it hides past the whole desktop");

        (blocked, beyond) = TaskbarParking.Plan(laptop, TaskbarSide.Bottom, 48, [upper, laptop]);
        check(!blocked && beyond == 900, "a bar with nothing below it hides just past its own edge");

        // Side by side, as on the author's desk: nothing stacked, nothing blocked.
        var dell = new DisplayRect(0, 0, 1920, 1080);
        var right = new DisplayRect(1920, 0, 3360, 900);
        check(!TaskbarParking.Plan(dell, TaskbarSide.Bottom, 48, [dell, right]).Blocked
            && !TaskbarParking.Plan(right, TaskbarSide.Bottom, 48, [dell, right]).Blocked,
            "side-by-side monitors never block each other's bars");

        // A corner that only touches is not in the way.
        var diagonal = new DisplayRect(1920, 1080, 3840, 2160);
        check(!TaskbarParking.Plan(dell, TaskbarSide.Bottom, 48, [dell, diagonal]).Blocked,
            "a monitor meeting only at a corner does not block");

        // Windows paths and known folders; build.sh runs these checks on Linux too.
        if (!OperatingSystem.IsWindows()) return;
        string packaged = @"{6D809377-6AF0-444B-8957-A3773F02200E}\WindowsApps\JustVStudio.DispCtrl_0.1.0.0_x64__x\DispCtrl.Engine.exe";
        check(string.Equals(TrayIconPromotion.Expand(packaged),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"WindowsApps\JustVStudio.DispCtrl_0.1.0.0_x64__x\DispCtrl.Engine.exe"), StringComparison.OrdinalIgnoreCase),
            "a Store engine's tray record resolves to its real path");
        check(TrayIconPromotion.Expand(@"C:\Tools\DispCtrl.Engine.exe") == @"C:\Tools\DispCtrl.Engine.exe",
            "an ordinary tray record path is left as it is");
    }
}
