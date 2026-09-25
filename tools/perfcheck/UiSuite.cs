using System.Diagnostics;
using System.Windows.Automation;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

namespace DispCtrl.PerfCheck;

/// <summary>
/// The quick panel as a person meets it: summoned from the tray icon, used,
/// dismissed. Drives only through UI Automation patterns and the panel's own
/// signals - never coordinate clicks, which once corrupted real settings.
/// </summary>
internal static class UiSuite
{
    private const string S = "ui";
    private const string WinUi = "WinUIDesktopWin32WindowClass";

    public static void Run(Context context)
    {
        Options o = context.Options;
        string exe = context.Output("DispCtrl.App", "DispCtrl.App.exe");

        // ------------------------------------------------ cold start
        StopApp();
        long t0 = Stopwatch.GetTimestamp();
        using (Process.Start(exe, "--panel")) { }
        nint panel = 0;
        _ = Bench.Until(() => (panel = PanelOf(AppProcess())) != 0 && Native.IsShown(panel), 15000, t0);
        if (panel == 0) throw new InvalidOperationException("the panel did not appear within 15 s of a cold start");
        Watch cold = Watch.Opening(panel, t0, 15000);
        context.Add(new Row(S, "cold start: panel visible", "ms", cold.Visible, cold.Visible, cold.Visible, 1, 2500));
        context.Add(new Row(S, "cold start: panel focused", "ms", cold.Focused, cold.Focused, cold.Focused, 1, 2500));
        context.Add(new Row(S, "cold start: panel settled", "ms", cold.Settled, cold.Settled, cold.Settled, 1, 3000));
        using Process app = AppProcess() ?? throw new InvalidOperationException("the app exited");
        Thread.Sleep(1500);
        EngineSuite.Resident(context, S, app, "panel open after cold start: ", wsBudget: 250, privateBudget: 250);
        var summon = new Summoner(context);
        summon.Close(panel);

        // ------------------------------------------------ open and close
        for (int i = 0; i < 2; i++) { summon.Open(panel); Thread.Sleep(700); summon.Close(panel); Thread.Sleep(500); }
        var opens = new List<Watch>(); var closes = new List<Watch>();
        for (int i = 0; i < o.N(12); i++)
        {
            opens.Add(summon.Open(panel)); Thread.Sleep(400);
            closes.Add(summon.Close(panel)); Thread.Sleep(500);
        }
        context.Report.Note(S, summon.ByTray ? "summoned through the tray icon (UI Automation Invoke)" : "tray icon not on the taskbar: summoned by the panel's signal");
        context.Add(Stats.Row(S, "open: click -> visible", "ms", [.. opens.Select(w => w.Visible)], 60));
        context.Add(Stats.Row(S, "open: click -> focused", "ms", [.. opens.Select(w => w.Focused)], 80));
        context.Add(Stats.Row(S, "open: click -> slide settled", "ms", [.. opens.Select(w => w.Settled)], 400, "slide is 260 ms by design"));
        context.Add(Stats.Row(S, "open: longest frame gap in slide", "ms", [.. opens.Select(w => w.Stall)], 34, "> 2 frames is visible"));
        context.Add(Stats.Row(S, "close: click -> hidden", "ms", [.. closes.Select(w => w.Settled)], 300, "slide is 180 ms by design"));
        context.Add(Stats.Row(S, "close: longest frame gap in slide", "ms", [.. closes.Select(w => w.Stall)], 34));

        // ------------------------------------------------ CPU and leaks per cycle
        ProcessSample before = Native.Sample(app);
        int cycles = o.N(15);
        for (int i = 0; i < cycles; i++) { summon.Open(panel); Thread.Sleep(300); summon.Close(panel); Thread.Sleep(300); }
        ProcessSample after = Native.Sample(app);
        double perCycle = after.CpuMsSince(before) / cycles;
        context.Add(new Row(S, "CPU per open + close", "ms", perCycle, perCycle, perCycle, cycles, 150, "exact, from cycle counts"));
        if (after.SwitchesSince(before) is long switches)
        {
            double each = switches / (double)cycles;
            context.Add(new Row(S, "context switches per open + close", "count", each, each, each, cycles, null));
        }
        context.Add(new Row(S, $"GDI growth over {cycles} cycles", "count", (double)after.Gdi - before.Gdi, 0, 0, 1, 2));
        context.Add(new Row(S, $"USER growth over {cycles} cycles", "count", (double)after.User - before.User, 0, 0, 1, 2));
        context.Add(new Row(S, $"handle growth over {cycles} cycles", "count", after.Handles - before.Handles, 0, 0, 1, 40));

        // ------------------------------------------------ views and controls
        summon.Open(panel); Thread.Sleep(600);
        ViewSwitch(context, panel, app);
        if (o.Suites.Contains("writes")) Slider(context, panel);
        else context.Report.Note(S, "brightness slider -> hardware needs --writes too");

        // ------------------------------------------------ idle cost
        Thread.Sleep(3000); // "untouched": after the view switch and slider tests have settled
        EngineSuite.Idle(context, S, app, o.Seconds(10), "panel open, untouched", 30, 15);
        summon.Close(panel);
        Thread.Sleep(1000);
        EngineSuite.Resident(context, S, app, "hidden, before idle trim: ", wsBudget: 250, privateBudget: 250);
        EngineSuite.Idle(context, S, app, o.Seconds(20), "panel hidden", 10, 10);
        Thread.Sleep(Math.Max(0, 11000 - o.Seconds(20) * 1000)); // the trim runs 10 s after hiding
        EngineSuite.Resident(context, S, app, "hidden, after idle trim: ", wsBudget: 40, privateBudget: 200);
        Watch trimmed = summon.Open(panel);
        context.Add(new Row(S, "first open after idle trim: focused", "ms", trimmed.Focused, trimmed.Focused, trimmed.Focused, 1, 150));
        Thread.Sleep(500); summon.Close(panel); Thread.Sleep(500);

        // ------------------------------------------------ closing by itself
        int selfClosed = 0;
        for (int i = 0; i < o.N(10); i++)
        {
            summon.Open(panel);
            if (Bench.Until(() => !Native.IsShown(panel), 2000) is not null) selfClosed++;
            else summon.Close(panel);
            Thread.Sleep(400);
        }
        context.Add(new Row(S, $"closed by itself (of {o.N(10)} opens, 2 s each)", "count", selfClosed, 0, 0, o.N(10), 0));

        MainWindow(context, app, panel);
        EnsureAppRunning(exe);
    }

    /// <summary>The panel's Simple toggle, twice per round: timed to the rebuilt, resized, settled window.</summary>
    private static void ViewSwitch(Context context, nint panel, Process app)
    {
        AutomationElement? toggle = Find(panel, "QuickPanelSimpleToggle");
        if (toggle is null) { context.Report.Note(S, "Simple toggle not found; view switch skipped"); return; }
        bool startedSimple = SettingsStore.Load().Global.QuickPanel.Simple;
        var toSimple = new List<double>(); var toFull = new List<double>(); var cpu = new List<double>();
        for (int i = 0; i < context.Options.N(4) * 2; i++)
        {
            ProcessSample before = Native.Sample(app);
            Native.Rect r0 = Native.RectOf(panel);
            long t0 = Stopwatch.GetTimestamp();
            ((InvokePattern)toggle.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            double settled = Watch.Resized(panel, r0, t0, 3000);
            cpu.Add(Native.Sample(app).CpuMsSince(before));
            (i % 2 == 0 != startedSimple ? toSimple : toFull).Add(settled);
            Thread.Sleep(400);
        }
        context.Add(Stats.Row(S, "view switch: full -> simple, settled", "ms", toSimple, 250));
        context.Add(Stats.Row(S, "view switch: simple -> full, settled", "ms", toFull, 250));
        context.Add(Stats.Row(S, "view switch: CPU per switch", "ms", cpu, 120));
    }

    /// <summary>
    /// A brightness slider moved through UI Automation, timed to the saved
    /// setting and to the monitor reporting the new value, then put back. The
    /// desk is checked against its starting brightness at the end.
    /// </summary>
    private static void Slider(Context context, nint panel)
    {
        // The full view folds its brightness section; Simple view is all sliders.
        AutomationElement? slider = FindSlider(panel);
        AutomationElement? simple = null;
        if (slider is null && Find(panel, "QuickPanelSimpleToggle") is { } toggle)
        {
            simple = toggle;
            Native.Rect r0 = Native.RectOf(panel);
            ((InvokePattern)toggle.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            _ = Watch.Resized(panel, r0, System.Diagnostics.Stopwatch.GetTimestamp(), 3000);
            slider = FindSlider(panel);
        }
        try { if (slider is not null) MoveAndRestore(context, slider); else context.Report.Note(S, "no brightness slider in either view; skipped"); }
        finally
        {
            if (simple is not null) ((InvokePattern)simple.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Thread.Sleep(400);
        }
    }

    private static void MoveAndRestore(Context context, AutomationElement slider)
    {
        var range = (RangeValuePattern)slider.GetCurrentPattern(RangeValuePattern.Pattern);
        List<DisplayInfo> displays = DisplayRegistry.Enumerate();
        int[] start = [.. displays.Select(d => (int)Brightness.Read(d).Current)];
        double original = range.Current.Value;
        double target = original >= range.Current.Minimum + 6 ? original - 5 : original + 5;
        context.Report.Note(S, $"slider '{slider.Current.Name}' {original} -> {target} -> {original}; displays at {string.Join("/", start)}");

        // Timed from the engine's own log line for the command the slider sends:
        // polling the monitors instead measured DDC/CI reads (~55 ms each), not the path.
        var log = new EngineLog();
        DateTime written = File.GetLastWriteTimeUtc(SettingsStore.Path_);
        DateTime wall = DateTime.Now;
        long t0 = Stopwatch.GetTimestamp();
        range.SetValue(target);
        double? persisted = Bench.Until(() => File.GetLastWriteTimeUtc(SettingsStore.Path_) != written, 3000, t0);
        if (log.WaitFor("display.set", 3000) is DateTime applied)
        {
            double ms = (applied - wall).TotalMilliseconds;
            context.Add(new Row(S, "brightness slider -> engine applied it", "ms", ms, ms, ms, 1, 300, "120 ms debounce by design"));
        }
        else context.Report.Note(S, "no display.set logged within 3 s of the slider moving (unison slider?)");
        if (persisted is double p) context.Add(new Row(S, "brightness slider -> settings saved", "ms", p, p, p, 1, 1500, "saves are coalesced on purpose"));

        Thread.Sleep(500);
        range.SetValue(original);
        _ = Bench.Until(() => displays.Select(d => (int)Brightness.Read(d).Current).Where((v, i) => Math.Abs(v - start[i]) >= 2).Any() == false, 5000);
        Thread.Sleep(1500);
        int[] end = [.. displays.Select(d => (int)Brightness.Read(d).Current)];
        int off = end.Where((v, i) => Math.Abs(v - start[i]) >= 2).Count();
        if (off > 0)
        {
            for (int i = 0; i < displays.Count; i++) if (Math.Abs(end[i] - start[i]) >= 2) _ = Brightness.Write(displays[i], (uint)start[i]);
            context.Report.Note(S, $"brightness not back where it was ({string.Join("/", end)}); written back to {string.Join("/", start)}");
        }
        context.Add(new Row(S, "displays not restored after the slider test", "count", off, 0, 0, 1, 0));
    }

    /// <summary>The full window, opened in the running process as the tray menu does, then closed.</summary>
    private static void MainWindow(Context context, Process app, nint panel)
    {
        HashSet<nint> existing = [.. Native.WindowsOf(app.Id, WinUi)];
        long t0 = Stopwatch.GetTimestamp();
        if (!QuickPanelSignal.ShowRunningWindow()) { context.Report.Note(S, "main window: no listener"); return; }
        nint window = 0;
        double? shown = Bench.Until(() => (window = Native.WindowsOf(app.Id, WinUi).FirstOrDefault(h => h != panel && Native.IsShown(h))) != 0, 10000, t0);
        if (shown is not double ms) { context.Report.Note(S, "main window did not appear within 10 s"); return; }
        context.Add(new Row(S, existing.Contains(window) ? "main window: shown again" : "main window: first open in a warm process", "ms", ms, ms, ms, 1, 1500));
        Thread.Sleep(3000);
        EngineSuite.Resident(context, S, app, "main window open: ", wsBudget: 400, privateBudget: 400);
        Native.Close(window);
        _ = Bench.Until(() => !Native.IsShown(window), 5000);
    }

    // ---------------------------------------------------------------- helpers

    private static Process? AppProcess()
    {
        Process[] all = Process.GetProcessesByName("DispCtrl.App");
        foreach (Process extra in all.Skip(1)) extra.Dispose();
        return all.FirstOrDefault();
    }

    private static nint PanelOf(Process? app)
    {
        if (app is null) return 0;
        using (app) return Native.WindowsOf(app.Id, WinUi, "DispCtrl").FirstOrDefault();
    }

    /// <summary>The window only: the app may be killed outright, never the engine.</summary>
    private static void StopApp()
    {
        foreach (Process p in Process.GetProcessesByName("DispCtrl.App")) using (p) { p.Kill(); p.WaitForExit(5000); }
    }

    private static void EnsureAppRunning(string exe)
    {
        if (!QuickPanelSignal.PanelIsListening()) using (Process.Start(exe, "--panel --background")) { }
    }

    private static AutomationElement? Find(nint window, string name) =>
        AutomationElement.FromHandle(window).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name));

    private static AutomationElement? FindSlider(nint window)
    {
        AutomationElementCollection sliders = AutomationElement.FromHandle(window).FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Slider));
        AutomationElement? unison = null;
        foreach (AutomationElement s in sliders)
        {
            if (!s.Current.IsEnabled) continue;
            if (s.Current.Name.StartsWith("Brightness ", StringComparison.Ordinal)) return s;
            if (s.Current.Name == "QuickUnisonLevel") unison = s;
        }
        return unison;
    }

    /// <summary>Opens and closes the panel the way a person does: the tray icon, else the panel's signal.</summary>
    private sealed class Summoner
    {
        private readonly InvokePattern? _tray;
        public bool ByTray => _tray is not null;

        public Summoner(Context context)
        {
            try
            {
                AutomationElement? bar = AutomationElement.RootElement.FindFirst(TreeScope.Children,
                    new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"));
                AutomationElementCollection? icons = bar?.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "NotifyItemIcon"));
                foreach (AutomationElement icon in icons?.Cast<AutomationElement>() ?? [])
                    if (icon.Current.Name.StartsWith("DispCtrl", StringComparison.Ordinal)) { _tray = (InvokePattern)icon.GetCurrentPattern(InvokePattern.Pattern); break; }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException) { context.Report.Note(S, "tray icon: " + ex.Message); }
        }

        private void Click() { if (_tray is not null) _tray.Invoke(); else QuickPanelSignal.Summon(); }

        public Watch Open(nint panel)
        {
            if (Native.IsShown(panel)) Close(panel);
            long t0 = Stopwatch.GetTimestamp();
            Click();
            return Watch.Opening(panel, t0, 3000);
        }

        public Watch Close(nint panel)
        {
            long t0 = Stopwatch.GetTimestamp();
            if (!Native.IsShown(panel)) return new Watch(0, 0, 0, 0);
            Click();
            return Watch.Closing(panel, t0, 3000);
        }
    }
}

/// <summary>A window watched at ~1 ms: when it showed, took focus, stopped moving, and its longest stall.</summary>
internal readonly record struct Watch(double Visible, double Focused, double Settled, double Stall)
{
    public static Watch Opening(nint hwnd, long t0, int timeoutMs)
    {
        double visible = -1, focused = -1, lastMove = -1, stall = 0;
        int top = int.MinValue;
        while (Stopwatch.GetElapsedTime(t0).TotalMilliseconds < timeoutMs)
        {
            double now = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            if (Native.IsShown(hwnd))
            {
                if (visible < 0) visible = now;
                if (focused < 0 && Native.IsForeground(hwnd)) focused = now;
                int y = Native.RectOf(hwnd).Top;
                if (y != top)
                {
                    if (top != int.MinValue && lastMove >= 0) stall = Math.Max(stall, now - lastMove);
                    top = y; lastMove = now;
                }
                if (focused >= 0 && now - lastMove > 150) return new Watch(visible, focused, lastMove, stall);
            }
            Thread.Sleep(1);
        }
        return new Watch(visible, focused, timeoutMs, stall);
    }

    public static Watch Closing(nint hwnd, long t0, int timeoutMs)
    {
        double lastMove = -1, stall = 0;
        int top = int.MinValue;
        while (Stopwatch.GetElapsedTime(t0).TotalMilliseconds < timeoutMs)
        {
            double now = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            if (!Native.IsShown(hwnd)) return new Watch(0, 0, now, stall);
            int y = Native.RectOf(hwnd).Top;
            if (y != top)
            {
                if (top != int.MinValue && lastMove >= 0) stall = Math.Max(stall, now - lastMove);
                top = y; lastMove = now;
            }
            Thread.Sleep(1);
        }
        return new Watch(0, 0, timeoutMs, stall);
    }

    /// <summary>Time until the window's rectangle has changed from <paramref name="from"/> and then held for 150 ms.</summary>
    public static double Resized(nint hwnd, Native.Rect from, long t0, int timeoutMs)
    {
        Native.Rect last = from; double changed = -1;
        while (Stopwatch.GetElapsedTime(t0).TotalMilliseconds < timeoutMs)
        {
            double now = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            Native.Rect r = Native.RectOf(hwnd);
            if (!r.Equals(last)) { last = r; changed = now; }
            if (changed >= 0 && now - changed > 150) return changed;
            Thread.Sleep(1);
        }
        return changed < 0 ? timeoutMs : changed;
    }
}
