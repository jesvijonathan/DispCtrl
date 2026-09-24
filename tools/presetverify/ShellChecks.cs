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

        var room = new AmbientSettings { DarkLevel = 20, BrightLevel = 90, BrightLux = 800 };
        int[] levels = [.. new[] { 0.0, 5, 50, 300, 800, 5000 }.Select(l => AmbientCurve.Level(l, room))];
        check(levels[0] == 20 && levels[4] == 90 && levels[5] == 90,
            "ambient light runs unison from the dark level to the bright one, and no further");
        check(levels.Zip(levels.Skip(1)).All(p => p.First <= p.Second) && levels[2] > 20 && levels[3] < 90,
            "more light never means a dimmer desk, and the middle is in the middle");

        // The sensor reports only on change, so one reading has to be enough:
        // covered, it says 0 once and then nothing. The first filter stopped
        // three-fifths of the way there and the desk barely moved.
        static long Settle(AmbientFilter filter, double lux, long from)
        {
            long t = from;
            filter.Observe(lux, t);
            while (filter.Busy && t - from < 20_000) { t += 250; filter.Observe(lux, t); }
            return t - from;
        }
        var sensor = new AmbientFilter();
        check(sensor.Observe(300, 0) && Math.Abs(sensor.SettledLux - 300) < 0.5, "the first reading acts at once");
        long darkened = Settle(sensor, 0, 1000);
        check(sensor.SettledLux < 3 && AmbientCurve.Level(sensor.SettledLux, room) == 20 && darkened < 6000,
            "a covered sensor reaches the dark level from one reading, within seconds");
        long brightened = Settle(sensor, 20000, 30_000);
        check(AmbientCurve.Level(sensor.SettledLux, room) == 90 && brightened < 4000,
            "a torch on the sensor reaches the bright level, sooner than darkening does");

        // A torch flickers: readings that keep changing must not keep restarting the wait.
        var flicker = new AmbientFilter();
        flicker.Observe(100, 0);
        long at = 0;
        for (int i = 0; i < 12 && flicker.SettledLux < 1000; i++) { at += 250; flicker.Observe(i % 2 == 0 ? 15000 : 9000, at); }
        check(flicker.SettledLux > 1000, "a flickering bright light still counts");

        // A hand passing the sensor is shorter than the wait.
        var passing = new AmbientFilter();
        passing.Observe(300, 0);
        passing.Observe(5, 250); passing.Observe(5, 500); passing.Observe(5, 1500);
        passing.Observe(300, 1750); passing.Observe(300, 2000);
        Settle(passing, 300, 2250);
        check(Math.Abs(passing.SettledLux - 300) < 1, "a moment's shadow does not dim the desk");

        // Small changes are not changes; the sensor's own noise is not the room.
        var dim = new AmbientFilter(); dim.Observe(1, 0); Settle(dim, 2.5, 250);
        check(Math.Abs(dim.SettledLux - 1) < 0.01, "a lux or two near darkness is noise");
        var drift = new AmbientFilter(); drift.Observe(300, 0); Settle(drift, 320, 250);
        check(Math.Abs(drift.SettledLux - 300) < 0.5, "a few percent more light moves nothing");

        // A real sensor jitters in a still room (24, 20, 26 lx), and each wobble
        // is a report. Waiting for the average to close on every one kept the
        // engine's clock running almost all the time.
        var still = new AmbientFilter(); still.Observe(24, 0);
        var jitter = new Random(1);
        int woken = 0;
        for (long t = 250; t < 60_000; t += 250)
        {
            double lux = 24 * (1 + (jitter.NextDouble() * 2 - 1) * 0.12);
            if (!still.Ignores(lux)) woken++;
            still.Observe(lux, t);
        }
        check(woken == 0 && !still.Busy && Math.Abs(still.SettledLux - 24) < 0.5,
            "a jittering sensor in a still room never starts the clock");

        // Learning: a level chosen by hand is where the curve goes in that light,
        // the newest answer wins, and the curve never falls as the light rises.
        var taught = new AmbientSettings { DarkLevel = 20, BrightLevel = 90, BrightLux = 800 };
        check(AmbientCurve.Learn(taught, 100, 40) && AmbientCurve.Level(100, taught) == 40,
            "a correction is kept for that light");
        check(!AmbientCurve.Learn(taught, 100, 40), "the same correction twice is one knot");
        AmbientCurve.Learn(taught, 30, 60);
        check(AmbientCurve.Level(30, taught) == 60 && AmbientCurve.Level(100, taught) >= 60 && taught.Points.Count == 1,
            "a newer correction replaces an older one it contradicts");
        int[] curve = [.. new[] { 0.0, 3, 10, 30, 60, 100, 300, 800, 5000 }.Select(l => AmbientCurve.Level(l, taught))];
        check(curve.Zip(curve.Skip(1)).All(p => p.First <= p.Second), "learned or not, more light never dims the desk");
        AmbientCurve.Learn(taught, 1, 50);
        check(AmbientCurve.Level(0, taught) == 50 && AmbientCurve.Level(1, taught) == 50,
            "a correction past the dark end moves the dark end aside");
        taught.Points.Clear();
        check(AmbientCurve.Level(0, taught) == 20 && AmbientCurve.Level(800, taught) == 90, "forgetting returns to the two ends");

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
