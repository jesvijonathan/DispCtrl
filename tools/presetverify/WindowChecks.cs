using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

/// <summary>
/// Pinning, gathering and returning windows, the theme schedule, the DDC/CI
/// guard and unison exclusion: every rule that decides something, apart from
/// the calls that act on it, which can only be watched.
/// </summary>
internal static class WindowChecks
{
    public static void Run(Action<bool, string> check)
    {
        Geometry(check);
        Workspace(check);
        Theme(check);
        Guard(check);
        Hotkeys(check);
        Panel(check);
        Probed(check);
        Settings(check);
        Activity(check);
        FocusChoice(check);
        Updates(check);
    }

    // The author's desk: a Dell at 100% on the left, primary, and the laptop at
    // 200% on its right. Work areas lose a 48 px taskbar at the bottom.
    private static readonly DisplayRect DellWork = new(0, 0, 1920, 1032);
    private static readonly DisplayRect LaptopWork = new(1920, 0, 4800, 1800);

    private static void Geometry(Action<bool, string> check)
    {
        var window = new DisplayRect(460, 216, 1460, 816); // 1000x600, centred on the Dell
        DisplayRect carried = WindowGeometry.Carry(window, DellWork, LaptopWork, 2.0);
        check(carried.Width == 2000 && carried.Height == 1200, "a gathered window keeps its size to the eye across scales");
        int centreX = (carried.Left + carried.Right) / 2, centreY = (carried.Top + carried.Bottom) / 2;
        check(Math.Abs(centreX - 3360) <= 1 && Math.Abs(centreY - 900) <= 1, "a centred window lands centred");

        DisplayRect pixels = WindowGeometry.Carry(window, DellWork, LaptopWork, 1.0);
        check(pixels.Width == 1000 && pixels.Height == 600, "keeping pixels keeps the pixel size");

        var huge = new DisplayRect(0, 0, 1920, 1032);
        DisplayRect clamped = WindowGeometry.Carry(huge, DellWork, LaptopWork, 2.0);
        check(clamped == LaptopWork, "a window larger than the target is clamped to its work area");

        var right = new DisplayRect(1500, 100, 1900, 500);
        DisplayRect onRight = WindowGeometry.Carry(right, DellWork, LaptopWork, 1.0);
        check(onRight.Right <= LaptopWork.Right && onRight.Left > (LaptopWork.Left + LaptopWork.Right) / 2,
            "a window on the right of one display lands on the right of the other, and inside it");

        var offEdge = new DisplayRect(-300, -50, 200, 250);
        DisplayRect inside = WindowGeometry.Carry(offEdge, DellWork, LaptopWork, 1.0);
        check(inside.Left >= LaptopWork.Left && inside.Top >= LaptopWork.Top, "a window hanging off its display lands wholly inside the target");

        // Putting back.
        WindowSpot spot = WindowGeometry.Spot("DEL-A234-X", window, DellWork, 96, WindowShow.Normal);
        check(WindowGeometry.Place(spot, DellWork, 96) == window, "a window returns exactly where it was to an unchanged display");
        var moved = new DisplayRect(-1920, 0, 0, 1032); // the Dell now arranged to the left of something else
        DisplayRect followed = WindowGeometry.Place(spot, moved, 96);
        check(followed == new DisplayRect(460 - 1920, 216, 1460 - 1920, 816), "a display that moved in the arrangement takes its windows with it");
        var smaller = new DisplayRect(0, 0, 1280, 672);
        DisplayRect shrunk = WindowGeometry.Place(spot, smaller, 96);
        check(shrunk.Right <= 1280 && shrunk.Bottom <= 672 && shrunk.Left >= 0, "a display back at a lower resolution holds its window inside it");

        var placed = new DisplayRect(2000, 100, 3000, 700);
        check(WindowGeometry.Untouched(placed, WindowShow.Normal, new DisplayRect(2002, 101, 3001, 700), WindowShow.Normal),
            "an app nudging itself after a DPI change still counts as untouched");
        check(!WindowGeometry.Untouched(placed, WindowShow.Normal, new DisplayRect(2100, 100, 3100, 700), WindowShow.Normal),
            "a window somebody moved is left where they put it");
        check(!WindowGeometry.Untouched(placed, WindowShow.Normal, placed, WindowShow.Maximized),
            "a window somebody maximized since is left alone");
        check(WindowGeometry.Untouched(placed, WindowShow.Minimized, new DisplayRect(-32000, -32000, -31840, -31972), WindowShow.Minimized),
            "a window Windows minimized on disconnect and nobody restored goes back");

        IReadOnlyList<DisplayRect> both = [new(0, 0, 1920, 1080), new(1920, 0, 4800, 1800)];
        check(WindowGeometry.MostlyOn(new DisplayRect(1800, 0, 2400, 400), both) == 1, "a window straddling two displays belongs to the one it mostly covers");
        check(WindowGeometry.MostlyOn(new DisplayRect(-500, -500, -100, -100), both) == -1, "a window on no display belongs to none");
    }

    private static void Workspace(Action<bool, string> check)
    {
        // A taskbar docked at the top: workspace (0,0) is 48 px below the screen's.
        DisplayInfo top = Fake(new DisplayRect(0, 0, 1920, 1080), new DisplayRect(0, 48, 1920, 1080));
        var screen = new DisplayRect(100, 148, 900, 748);
        DisplayRect workspace = DispCtrl.Display.Placement.WindowMover.ToWorkspace(screen, top);
        check(workspace == new DisplayRect(100, 100, 900, 700), "placements are converted to workspace coordinates below a top taskbar");
        check(DispCtrl.Display.Placement.WindowMover.FromWorkspace(workspace, top) == screen, "workspace and screen coordinates round-trip");
        DisplayInfo bottom = Fake(new DisplayRect(0, 0, 1920, 1080), new DisplayRect(0, 0, 1920, 1032));
        check(DispCtrl.Display.Placement.WindowMover.ToWorkspace(screen, bottom) == screen, "with the taskbar at the bottom the two are the same");
    }

    private static DisplayInfo Fake(DisplayRect bounds, DisplayRect work) => new()
    {
        Key = new DisplayKey(@"\\?\DISPLAY#TST0001#1", "TST-0001", "SERIAL"),
        GdiName = @"\\.\DISPLAY9", FriendlyName = "Test", Connector = ConnectorKind.Hdmi, IsPrimary = true,
        Bounds = bounds, WorkArea = work, RefreshHz = 60, BitsPerPixel = 32, Dpi = 96,
    };

    private static void Theme(Action<bool, string> check)
    {
        var n = new NightLightSettings { Scheduled = true, FromMinutes = 20 * 60, ToMinutes = 7 * 60, DarkModeOnSchedule = true };
        var evening = new DateTime(2026, 9, 25, 21, 0, 0);
        check(n.ThemeDue(evening) == true, "switched on in the evening, the schedule asks for dark mode at once");
        n.ThemeAppliedUtc = new DateTimeOffset(evening).ToUniversalTime();
        check(n.ThemeDue(evening.AddHours(2)) is null, "once applied, nothing more until the next boundary - a theme chosen by hand holds");
        check(n.ThemeDue(new DateTime(2026, 9, 26, 7, 30, 0)) == false, "the morning boundary asks for light mode");
        check(n.ThemeDue(new DateTime(2026, 9, 26, 1, 0, 0)) is null, "past midnight is still the same dark period");
        check(n.LastBoundary(new DateTime(2026, 9, 26, 1, 0, 0)) == new DateTime(2026, 9, 25, 20, 0, 0), "the last boundary crosses midnight");
        n.ThemeAppliedUtc = null;
        check(n.ThemeDue(new DateTime(2026, 9, 26, 12, 0, 0)) == false, "a computer asleep through a boundary catches up at the first look");
        n.DarkModeOnSchedule = false;
        check(n.ThemeDue(evening) is null, "off, the schedule never touches the theme");
        n.DarkModeOnSchedule = true; n.Scheduled = false;
        check(n.ThemeDue(evening) is null, "without a schedule there is nothing to follow");
        n.Scheduled = true; n.ToMinutes = n.FromMinutes;
        check(n.ThemeDue(evening) is null, "a schedule of no length changes nothing");
        var warm = new NightLightSettings { Enabled = false, Scheduled = true, FromMinutes = 20 * 60, ToMinutes = 7 * 60 };
        check(warm.InScheduledHours(evening) && !warm.ActiveAt(evening), "the theme follows the hours even with the warmth itself off");
    }

    private static void Guard(Action<bool, string> check)
    {
        DateTimeOffset boot = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);
        check(DdcGuard.Judge(boot, boot.AddSeconds(1), writerAlive: true) == MarkerVerdict.Live, "a read still in progress is left alone");
        check(DdcGuard.Judge(boot, boot.AddSeconds(-1), writerAlive: false) == MarkerVerdict.Stale, "a process killed mid-read in this boot is not a crash");
        check(DdcGuard.Judge(boot, boot.AddMinutes(3), writerAlive: false) == MarkerVerdict.Crashed, "a mark from an earlier boot means Windows went down during the read");
        check(DdcGuard.Judge(boot, boot.AddSeconds(40), writerAlive: false) == MarkerVerdict.Stale, "tick-count drift within one boot is not taken for a reboot");
        var settings = new DdcGuardSettings { Blocked = [new DdcBlock { Token = "DEL-A234-XYZ" }] };
        check(settings.IsBlocked("del-a234-xyz") && !settings.IsBlocked("SDC-4154-1"), "blocks are by the unit's token");
        settings.Enabled = false;
        check(!settings.IsBlocked("DEL-A234-XYZ"), "with the guard off nothing is blocked");
    }

    private static void Hotkeys(Action<bool, string> check)
    {
        var fresh = new DispCtrlSettings();
        Hotkey.OfferDefaults(fresh);
        check(fresh.Hotkeys.Any(h => h.Action == HotkeyAction.PinWindow && h.Enabled && h.Key == 'P' && h.Modifiers == 3)
            && fresh.Hotkeys.Any(h => h.Action == HotkeyAction.GatherWindows && h.Enabled && h.Key == 'G'),
            "a new desk gets pin and gather on Ctrl+Alt+P and G");

        var older = new DispCtrlSettings();
        older.Hotkeys = Hotkey.Defaults().Where(h => h.Action is not (HotkeyAction.PinWindow or HotkeyAction.GatherWindows)).ToList();
        older.Global.HotkeyDefaultsVersion = 4;
        check(Hotkey.OfferDefaults(older) && older.Hotkeys.Count(h => h.Action is HotkeyAction.PinWindow or HotkeyAction.GatherWindows && h.Enabled) == 2,
            "a desk set up before version 5 is offered pin and gather, switched on");

        var taken = new DispCtrlSettings();
        taken.Hotkeys = [new Hotkey { Key = 'P', Modifiers = 3, Action = HotkeyAction.FocusToggle }];
        taken.Global.HotkeyDefaultsVersion = 4;
        Hotkey.OfferDefaults(taken);
        check(!taken.Hotkeys.Any(h => h.Action == HotkeyAction.PinWindow), "pin is never offered over a combination something else holds");
        check(Hotkey.OfferDefaults(taken) == false, "the offer is made once");
        check(new Hotkey { Action = HotkeyAction.GatherWindows, Display = 2 }.DescribeAction().Contains("display 2"),
            "a gather shortcut says which display it gathers onto");
    }

    private static void Panel(Action<bool, string> check)
    {
        var saved = QuickPanelCatalog.Defaults(QuickPanelGroup.Tiles).Where(t => t.Id is not ("pin" or "gather")).ToList();
        foreach (var item in saved) item.Visible = true;
        var whole = QuickPanelCatalog.Normalise(QuickPanelGroup.Tiles, saved);
        check(whole.Any(i => i.Id == "pin" && !i.Visible) && whole.Any(i => i.Id == "gather" && !i.Visible),
            "a panel saved before pin and gather gets them hidden, never grown");
        // The owner's call: nothing about windows shows until it is asked for.
        check(QuickPanelCatalog.Defaults(QuickPanelGroup.Tiles).Where(t => t.Id is "pin" or "gather" or "returnWindows" or "newWindows").All(t => !t.Visible)
              && QuickPanelCatalog.Defaults(QuickPanelGroup.Sections).Any(t => t.Id == "windows" && !t.Visible),
            "a new panel shows no window tiles and no Windows section until they are switched on");
        check(new QuickPanelSettings().IsCollapsed("windows"), "the Windows section starts folded");
        check(QuickPanelCatalog.Find(QuickPanelGroup.DisplayTiles, "unison") is not null, "each display's strip can hold its unison switch");
    }

    private static void Probed(Action<bool, string> check)
    {
        MonitorCapability parsed = MonitorCapabilities.Parse("(type(probed)vcp(10 12 60))");
        check(parsed.Controls.Count == 3 && parsed.Controls.Any(c => c.Code == 0x60 && c.Values.Count > 0),
            "a stand-in built from probed codes parses, and input source falls back to the standard's values");
        check(MonitorCapabilities.IsProbed("(type(probed)vcp(10))") && !MonitorCapabilities.IsProbed("(prot(monitor)type(LCD)vcp(10))"),
            "a stand-in is told apart from a monitor's own string, so it never reaches the device library");
        var input = parsed.Controls.First(c => c.Code == 0x60);
        input.Current = 0x77;
        check(!input.Settable, "a probed discrete control whose reading is not one of its values is not offered");
        input.Current = 0x11;
        check(input.Settable, "a probed control reading a listed value is offered, through the allow list");
    }

    private static void Settings(Action<bool, string> check)
    {
        var monitor = new MonitorSettings();
        check(monitor.InUnison && monitor.ProbedCodes is null, "a display is in unison until it is left out");
        monitor.InUnison = false; monitor.ProbedCodes = ["10"];
        monitor.ResetToDefaults();
        check(monitor.InUnison && monitor.ProbedCodes is null, "resetting a display puts it back in unison and forgets a probe");

        var global = new GlobalSettings();
        global.Placement.ReturnWindows = true; global.Placement.TookOverWindowsMemory = true;
        global.DdcGuard.Enabled = false; global.DdcGuard.Blocked.Add(new DdcBlock { Token = "X" });
        global.Pin.BorderThickness = 9;
        global.ResetToDefaults();
        check(!global.Placement.ReturnWindows && global.Placement.TookOverWindowsMemory,
            "reset all keeps the record that hands Windows its window memory back");
        check(global.DdcGuard.Enabled && global.DdcGuard.Blocked.Count == 1, "reset all puts the guard back on and keeps the monitors it blocked");
        check(global.Pin.BorderThickness == new PinSettings().BorderThickness, "reset all resets the pin border");
        check(new PinSettings().Exclusions().Count == 0 && new PinSettings { ExcludedApps = "vlc.exe, OBS64" }.Exclusions().SetEquals(["vlc", "obs64"]),
            "excluded apps are read with or without .exe, in any case");
    }
    private static void Activity(Action<bool, string> check)
    {
        var dell = new DisplayRect(0, 0, 1920, 1080);
        var laptop = new DisplayRect(1920, 0, 4800, 1800);
        var onDell = new DisplayRect(100, 100, 900, 700);
        var straddling = new DisplayRect(1500, 100, 2500, 700); // 420 px on the Dell, 580 on the laptop

        // Moving the pointer counts where it is.
        check(DisplayActivity.Used(dell, true, 500, 500, true, null, false), "moving the pointer on a display uses it");
        check(!DisplayActivity.Used(laptop, true, 500, 500, true, null, false), "moving the pointer on one display does not use another");
        // Typing counts where the window in front is.
        check(DisplayActivity.Used(dell, false, 3000, 500, true, onDell, false), "typing into a window uses its display, wherever the pointer is");
        check(!DisplayActivity.Used(laptop, false, 3000, 500, true, onDell, false), "typing into a window on another display does not use the pointer's");
        check(DisplayActivity.Used(laptop, false, 3000, 500, true, straddling, false)
              && !DisplayActivity.Used(dell, false, 3000, 500, true, straddling, false),
            "a window across two displays counts for the one holding most of it");
        var overhanging = new DisplayRect(-8, -8, 1928, 1088); // maximized, its frame a few pixels over the edge
        check(DisplayActivity.Used(dell, false, 3000, 500, true, overhanging, false)
              && !DisplayActivity.Used(laptop, false, 3000, 500, true, overhanging, false),
            "a maximized window overhanging its neighbour counts only for its own display");
        check(DisplayActivity.Used(laptop, false, 3000, 500, true, null, false), "with nothing in front, input counts for the pointer's display");
        // Typing while the mouse wanders over another display keeps both awake.
        check(DisplayActivity.Used(dell, true, 3000, 500, true, onDell, false) && DisplayActivity.Used(laptop, true, 3000, 500, true, onDell, false),
            "typing on one display while the pointer moves on another keeps both awake");
        check(!DisplayActivity.Used(dell, false, 500, 500, false, onDell, false), "no input and a still pointer uses nothing");
        // Wake when the pointer returns: only the pointer counts.
        check(!DisplayActivity.Used(dell, false, 500, 500, true, onDell, true), "a pointer-only display ignores typing");
        check(DisplayActivity.Used(dell, true, 500, 500, true, onDell, true), "a pointer-only display wakes for the pointer");
        check(DisplayActivity.Used(dell, false, 500, 500, true, new DisplayRect(0, 0, 0, 0), false),
            "an empty front window falls back to the pointer's display");

        var clock = new DisplayActivity();
        check(clock.Update(1000, false) == 0, "a display's clock starts at zero");
        check(clock.Update(61_000, false) == 60_000, "an unused display's clock runs");
        check(clock.Update(62_000, true) == 0, "using a display starts its clock again");
        check(clock.Update(500, false) == 0, "a clock that goes backwards is restarted, not wrapped");
        clock.Update(100_000, false);
        clock.Reset(200_000);
        check(clock.Update(200_500, false) == 500, "reset starts the clock at that moment");

        // Switching per-display rest off again must not find an old rest waiting.
        var idle = new OledIdleState();
        var panel = new DisplayRect(1920, 0, 4800, 1800);
        idle.Update(true, true, 0, true, 0, 1, true, 100, 100, panel);
        idle.Update(true, true, 120_000, true, 120_000, 1, true, 100, 100, panel); // rested, pointer elsewhere
        idle.Update(false, false, 3_600_000, true, 0, 1, true, 100, 100, panel);  // per-display mode meanwhile
        check(idle.Update(true, true, 3_601_000, true, 1_000, 1, true, 100, 100, panel) == 1_000,
            "the ordinary idle clock starts fresh after per-display rest, not from a rest an hour old");

        var care = new OledCareSettings();
        check(!care.PerDisplayActivity && care.Exclusions().Count == 0, "per-display rest and exceptions start off");
        care.ExcludedApps = "VLC.exe; mpv\nobs64.EXE";
        check(care.Exclusions().SetEquals(["vlc", "mpv", "obs64"]), "OLED exceptions are read with or without .exe, in any case");

        var monitor = new MonitorSettings { LastSeenUtc = DateTimeOffset.UnixEpoch };
        monitor.ResetToDefaults();
        check(monitor.LastSeenUtc == DateTimeOffset.UnixEpoch, "resetting a display keeps when it was last seen");
    }

    private static void FocusChoice(Action<bool, string> check)
    {
        // The shipped defaults are the owner's desk: both switches on, which the
        // engine treats as both windows clear, the pointer's leading.
        var focus = new FocusSettings();
        check(focus.Clear == FocusClear.Both, "the shipped focus defaults read as both windows clear");
        focus.Clear = FocusClear.Focused;
        check(!focus.FollowMouse && !focus.KeepHoveredClear, "the focused window alone clears both switches");
        focus.Clear = FocusClear.Pointer;
        check(focus.FollowMouse && !focus.KeepHoveredClear, "the pointer's window instead is Follow the mouse alone");
        focus.Clear = FocusClear.Both;
        check(focus.KeepHoveredClear && focus.Clear == FocusClear.Both, "both is Keep the hovered window clear");
        focus.FollowMouse = false; focus.KeepHoveredClear = true;
        check(focus.Clear == FocusClear.Both, "keep-hovered without follow reads as both");
    }
    private static void Updates(Action<bool, string> check)
    {
        check(UpdateVersion.IsNewer("v0.1.6", "0.1.5", "stable") && UpdateVersion.IsNewer("0.2.0", "0.1.9", "stable")
              && UpdateVersion.IsNewer("v1.0", "0.9.9", "stable"), "a higher release is newer, with or without its v");
        check(!UpdateVersion.IsNewer("v0.1.5", "0.1.5", "stable") && !UpdateVersion.IsNewer("v0.1.4", "0.1.5", "stable"),
            "the same or an older release is not");
        check(UpdateVersion.IsNewer("v0.1.6", "0.1.6", "beta") && !UpdateVersion.IsNewer("v0.1.6-beta.2", "0.1.6", "beta"),
            "the stable release is newer than its own beta; another beta of it is not");
        check(!UpdateVersion.IsNewer("v0.1.6", "0.1.6", "dev"), "a build from source is not told to install what it was built as");
        check(!UpdateVersion.IsNewer("latest", "0.1.5", "stable") && !UpdateVersion.IsNewer("", "0.1.5", "stable")
              && !UpdateVersion.IsNewer(null, "0.1.5", "stable") && !UpdateVersion.IsNewer("v9.9.9", "garbage", "stable"),
            "an answer that is not a version announces nothing");
        check(UpdateVersion.Display("v0.1.6-beta.2") == "0.1.6" && UpdateVersion.Display("v1.2") == "1.2.0" && UpdateVersion.Display("x") == "",
            "a tag is shown as its version");

        var updates = new UpdateSettings();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        check(!updates.Due(now), "nothing is checked until somebody switches checking on");
        updates.CheckAutomatically = true;
        check(updates.Due(now), "switched on, the first check is due at once");
        updates.CheckedUtc = now.AddHours(-3);
        check(!updates.Due(now), "a check three hours ago is recent enough");
        updates.CheckedUtc = now.AddHours(-25);
        check(updates.Due(now), "a day later, the next one is due");
        updates.CheckedUtc = now.AddDays(3);
        check(updates.Due(now), "a check dated in the future (a clock put back) does not stop checking for good");

        var global = new GlobalSettings();
        global.Updates.CheckAutomatically = true;
        global.ResetToDefaults();
        check(!global.Updates.CheckAutomatically, "reset all goes back to no network requests");
    }
}
