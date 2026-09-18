using Umbra.Core.Displays;
using Umbra.Core.Settings;
using Umbra.Display;
using Umbra.Display.Cli;
using Umbra.Engine.Color;
using Umbra.Engine.Input;
using Umbra.Engine.Presets;
using Umbra.Engine.Taskbar;
using Windows.Win32;
using Windows.Win32.UI.HiDpi;

namespace Umbra.Engine;

internal static class Program
{
    /// <summary>
    /// <c>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2</c>. Win32 defines the
    /// well-known contexts as macros, not constants, so they do not survive
    /// into the metadata CsWin32 generates from — the sentinel is spelled out
    /// here rather than invented.
    /// </summary>
    private const nint PerMonitorAwareV2 = -4;

    /// <summary>
    /// Name of the graceful-shutdown event the running engine waits on.
    /// </summary>
    /// <remarks>
    /// A background engine needs a way to be stopped that is not "kill the
    /// process": killing it skips the restore path and strands a taskbar
    /// off-screen, recoverable only by restarting explorer.
    /// <para>
    /// <c>Local\</c> scopes it to the logon session, matching the engine's
    /// single-instance mutex.
    /// </para>
    /// </remarks>
    private const string StopEventName = @"Local\Umbra.Engine.Stop";

    private static int Main(string[] args)
    {
        TryAttachConsole();

        // Every coordinate this process reads or writes is physical-pixel, so
        // awareness has to be set before the first display call. The manifest
        // declares it too; this is the belt to that braces, for the case where
        // the engine is launched in a context that ignores the manifest.
        _ = PInvoke.SetProcessDpiAwarenessContext((DPI_AWARENESS_CONTEXT)PerMonitorAwareV2);

        string command = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
        string? arg = args.Length > 1 ? args[1] : null;

        return command switch
        {
            "displays" or "list" => ListDisplays(),
            "enable" => SetHide(arg, true),
            "disable" => SetHide(arg, false),
            "status" => Status(),
            "run" => Run(ParseDuration(args), HasFlag(args, "--trace")),
            "stop" => Stop(),
            "help" or "--help" or "-h" or "/?" => Usage(0),

            // Everything else is a display command. Kept in one place rather
            // than spread through this switch: they share targeting, output and
            // exit-code conventions that only make sense together.
            _ => CommandLine.Run(command, args.Length > 1 ? args[1..] : []),
        };
    }

    /// <summary>
    /// Attaches to the launching console, if there is one, so a WinExe can
    /// still behave like a CLI.
    /// </summary>
    /// <remarks>
    /// Started from Task Scheduler or Explorer there is no parent console, the
    /// attach fails, and the engine runs silently — which is the point of
    /// building it as a WinExe. .NET caches its console streams on first use,
    /// so they are replaced explicitly after attaching.
    /// </remarks>
    private static void TryAttachConsole()
    {
        const uint AttachParentProcess = 0xFFFFFFFF;

        try
        {
            if (!PInvoke.AttachConsole(AttachParentProcess)) return;

            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch (IOException)
        {
            // No usable console. Everything still goes to the log file.
        }
    }

    /// <summary>
    /// Watches the settings file so the app's changes take effect without
    /// restarting the engine.
    /// </summary>
    /// <remarks>
    /// The file is the interface between the two processes, which keeps the app
    /// from needing to talk to the engine directly.
    /// <para>
    /// Debounced, for two reasons: a single logical save raises several events,
    /// and <see cref="SettingsStore.Save"/> writes to a temp file and moves it
    /// into place, so the watcher must also settle before reading or it will
    /// catch the moment between delete and rename.
    /// </para>
    /// </remarks>
    private static FileSystemWatcher WatchSettings(TaskbarManager manager, NightLightService nightLight,
                                                   AppRuleService appRules, HotkeyService hotkeys)
    {
        Directory.CreateDirectory(SettingsStore.Directory);

        var debounce = new Timer(_ =>
        {
            try
            {
                // Loaded once and handed to both, so the two cannot end up
                // acting on different versions of the same save.
                UmbraSettings reloaded = SettingsStore.Load();
                manager.ApplySettings(reloaded);
                nightLight.Update(reloaded);
                appRules.Update(reloaded);
                hotkeys.Update(reloaded);
            }
            catch (Exception ex)
            {
                Log.Write($"settings reload failed: {ex.Message}");
            }
        });

        var watcher = new FileSystemWatcher(SettingsStore.Directory)
        {
            Filter = "settings.json",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };

        // Short enough that a toggle in the app feels immediate, long enough to
        // coalesce the several events one logical save produces.
        void Bump(object? _, FileSystemEventArgs __) =>
            debounce.Change(120, Timeout.Infinite);

        watcher.Changed += Bump;
        watcher.Created += Bump;
        watcher.Renamed += (s, e) => Bump(s, e);
        watcher.EnableRaisingEvents = true;

        return watcher;
    }

    private static int Stop()
    {
        if (!EventWaitHandle.TryOpenExisting(StopEventName, out EventWaitHandle? stop))
        {
            Console.Error.WriteLine("no Umbra engine is running.");
            return 1;
        }

        using (stop) stop.Set();
        Console.WriteLine("stop requested - the engine will restore the taskbars and exit.");
        return 0;
    }

    /// <summary>
    /// Reads <c>--for &lt;seconds&gt;</c>, or null to run until stopped.
    /// </summary>
    /// <remarks>
    /// A bounded run restores the taskbars on its own, which makes trying a
    /// configuration safe: nothing can leave a bar stranded off-screen, even if
    /// the run is never interrupted.
    /// </remarks>
    private static bool HasFlag(string[] args, string name)
    {
        foreach (string a in args)
            if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static TimeSpan? ParseDuration(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--for", StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(args[i + 1], out int seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);
        }
        return null;
    }

    private static int Usage(int code, string? error = null)
    {
        TextWriter w = code == 0 ? Console.Out : Console.Error;
        if (error is not null) w.WriteLine(error);
        w.WriteLine("""
            Umbra

            Engine
              displays              list attached monitors
              status                show what is configured
              enable  <n|token>     hide the taskbar on that monitor
              disable <n|token>     stop hiding it
              run [--for <s>]       run the engine (Ctrl+C restores everything)
              stop                  ask a running engine to restore and exit

            Brightness and colour
              brightness [<n>|+n|-n]   read or set; an offset is relative
              dim [<n>]                software dimming, for panels with no control
              unison [on|off|<n>]      one level across every display
              nightlight [on|off|<n>] [--from 20:00 --to 07:00] [--no-schedule]

            The monitor's own settings
              contrast [<n>]           ─┐
              volume [<n>]              │ whatever the monitor reports
              sharpness [<n>]          ─┘
              input [<name>]           switch source, by name: "HDMI 1"
              power <on|standby|off>   the monitor's own power state
              vcp <code> [<value>]     any allow-listed VCP code, e.g. vcp 0x14

            Layout
              topology <extend|duplicate|internal|external>
              resolution [<WxH>]
              refresh [<hz>]
              primary [<n|token>]

            Presets
              preset list
              preset apply  <name>
              preset save   <name>
              preset delete <name>

              report                write the display report and print its path

            Every display command takes --display <n|token> or --all.
            Without one it reads rather than writes, and reports every display.
            <n> is the number shown by `displays`: built-in first, then left to
            right, the same numbering the panel uses.

            Exit codes: 0 done, 1 refused, 2 asked wrongly.

            --for <s> runs for that many seconds then restores by itself,
            which is the safe way to trial a configuration.
            """);
        return code;
    }

    // ------------------------------------------------------------- commands --

    private static int ListDisplays()
    {
        List<DisplayInfo> displays = DisplayRegistry.Enumerate();
        if (displays.Count == 0)
        {
            Console.Error.WriteLine("no displays resolved");
            return 1;
        }

        UmbraSettings settings = SettingsStore.Load();

        for (int i = 0; i < displays.Count; i++)
        {
            DisplayInfo d = displays[i];
            string token = d.Token;
            bool hiding = settings.Monitors.TryGetValue(token, out MonitorSettings? ms) && ms.HideTaskbar;

            Console.WriteLine($"[{i + 1}] {d.Label} {(d.IsPrimary ? "(primary)" : "(secondary)")}");
            Console.WriteLine($"    {d.Bounds.Width}x{d.Bounds.Height} @ {d.RefreshHz}Hz, {d.Scale * 100:0}% scaling, {d.Connector}");
            Console.WriteLine($"    brightness  : {(d.IsInternal ? "WMI (internal panel, no DDC/CI)" : "DDC/CI")}");
            Console.WriteLine($"    work area   : {d.WorkArea}{(d.WorkArea == d.Bounds ? "  (full - no bar reserved)" : "")}");
            Console.WriteLine($"    taskbar     : {(hiding ? "HIDDEN by Umbra" : "normal")}");
            Console.WriteLine($"    settings key: {token}");
            Console.WriteLine();
        }

        return 0;
    }

    private static int Status()
    {
        UmbraSettings settings = SettingsStore.Load();
        Console.WriteLine($"settings: {SettingsStore.Path_}");
        Console.WriteLine($"log     : {Log.Path}");
        Console.WriteLine();

        if (settings.Monitors.Count == 0)
        {
            Console.WriteLine("nothing configured yet.");
            Console.WriteLine("run `displays` to see your monitors, then `enable <n>`.");
            return 0;
        }

        List<DisplayInfo> attached = DisplayRegistry.Enumerate();

        foreach ((string token, MonitorSettings ms) in settings.Monitors)
        {
            bool present = attached.Exists(d => d.Token == token);
            Console.WriteLine($"{ms.Label ?? token}  [{(present ? "attached" : "not attached")}]");
            Console.WriteLine($"    hide taskbar    : {ms.HideTaskbar}");
            Console.WriteLine($"    reclaim workarea: {ms.ReclaimWorkArea}");
            Console.WriteLine($"    key             : {token}");
        }

        return 0;
    }

    private static int SetHide(string? selector, bool hide)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return Usage(2, "which monitor? pass the number from `displays`.");

        List<DisplayInfo> displays = DisplayRegistry.Enumerate();
        DisplayInfo? target = Select(displays, selector);
        if (target is null)
            return Usage(2, $"no monitor matches '{selector}'.");

        UmbraSettings settings = SettingsStore.Load();
        string token = target.Token;

        MonitorSettings ms = settings.For(token);
        ms.HideTaskbar = hide;
        ms.Label = target.Label;
        SettingsStore.Save(settings);

        Console.WriteLine($"{(hide ? "hiding" : "no longer hiding")} the taskbar on {target.Label}.");

        if (hide && target.IsPrimary)
        {
            // Measured, not guessed: SetWindowPos on Shell_TrayWnd reports
            // success and explorer restores it within ~120ms. Saying so here
            // beats letting the user wonder why nothing happens.
            Console.WriteLine();
            Console.WriteLine("warning: this is your PRIMARY monitor, and Windows will not allow it.");
            Console.WriteLine("  Explorer actively restores the primary taskbar, so it snaps straight");
            Console.WriteLine("  back. Only secondary monitors' taskbars can be moved from outside");
            Console.WriteLine("  explorer. The engine will detect this and stop trying.");
            Console.WriteLine();
            Console.WriteLine("  To hide this one instead, make a different display primary in");
            Console.WriteLine("  Settings > System > Display, then enable it here.");
        }

        Console.WriteLine(hide
            ? "run `run` to start the engine."
            : "restart the engine for this to take effect.");
        return 0;
    }

    /// <summary>Resolves a 1-based index or a settings token to a display.</summary>
    private static DisplayInfo? Select(List<DisplayInfo> displays, string selector)
    {
        if (int.TryParse(selector, out int index))
            return index >= 1 && index <= displays.Count ? displays[index - 1] : null;

        return displays.Find(d =>
            string.Equals(d.Token, selector, StringComparison.OrdinalIgnoreCase));
    }

    private static int Run(TimeSpan? duration, bool trace)
    {
        // One engine at a time. A second launch exits quietly rather than
        // fighting the first over the same taskbar windows.
        //
        // Local\ rather than Global\: one engine per logon session is exactly
        // the scope wanted, and Global\ needs a privilege a plain interactive
        // token may not carry.
        using var mutex = new Mutex(initiallyOwned: true, "Local\\Umbra.Engine", out bool isNew);
        if (!isNew)
        {
            Console.Error.WriteLine("another Umbra engine is already running.");
            return 0;
        }

        UmbraSettings settings = SettingsStore.Load();
        Log.Configure(settings.Global.Logging, echo: true);

        int managed = 0;
        foreach (MonitorSettings ms in settings.Monitors.Values)
            if (ms.HideTaskbar) managed++;

        // Taskbar hiding is no longer the only reason to be resident: night
        // light has a schedule, and a schedule that only runs while a taskbar
        // is also being managed is not a schedule.
        bool dimming = false;
        foreach (MonitorSettings ms in settings.Monitors.Values)
            if (ms.SoftwareBrightness < 100) dimming = true;

        if (managed == 0 && !settings.Global.NightLight.Enabled && settings.AppRules.Count == 0
            && !dimming && settings.Hotkeys.Count == 0)
        {
            Console.Error.WriteLine(
                "nothing to do: no taskbar is managed, night light is off, nothing is software-dimmed, "
                + "and there are no app rules or hotkeys.");
            Console.Error.WriteLine("run `displays`, then `enable <n>` — or turn something on in the app.");
            return 1;
        }

        using var cts = new CancellationTokenSource();

        // Graceful shutdown for a background engine: `stop` sets this, the
        // engine unwinds through Restore() and puts the taskbars back.
        using var stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, StopEventName);
        stopEvent.Reset();
        RegisteredWaitHandle stopWait = ThreadPool.RegisterWaitForSingleObject(
            stopEvent,
            (_, _) =>
            {
                Log.Write("stop requested");
                cts.Cancel();
            },
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: true);

        Console.CancelKeyPress += (_, e) =>
        {
            // Cancel the default kill so Run can unwind and put the taskbars
            // back; otherwise Ctrl+C would strand a hidden bar.
            e.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("stopping, restoring taskbars...");
            cts.Cancel();
        };

        if (duration is { } d)
        {
            cts.CancelAfter(d);
            Console.WriteLine($"Umbra engine running for {d.TotalSeconds:0}s, then restoring. Ctrl+C to stop early.");
        }
        else
        {
            Console.WriteLine("Umbra engine running. Ctrl+C to stop and restore.");
        }

        var manager = new TaskbarManager(settings, trace);

        try
        {
            // Disposed before the manager unwinds, so the displays are back to
            // their own colour even if the taskbar restore then throws.
            // A snapshot of what is attached, written once at start. The awkward
            // display problems are all "what did the hardware say it could do at
            // the time", and by the time that is worth asking, the moment has
            // gone.
            //
            // Deliberately not awaited. The report asks every external monitor
            // for its capabilities string over DDC/CI, which is seconds of
            // blocking round trips — done inline at logon, that is seconds
            // before the taskbar starts being managed, which is the one thing
            // the engine exists for. A diagnostic must never delay the job.
            _ = Task.Run(() =>
            {
                try
                {
                    Log.Write($"display report -> {DisplayReport.Write(DisplayRegistry.Enumerate())}");
                }
                catch (Exception ex)
                {
                    Log.Write($"display report failed: {ex.Message}");
                }
            });

            using var nightLight = new NightLightService(settings);

            // Persisting from the engine is new with app rules: applying a
            // preset writes into settings as well as to the hardware, so
            // without this the next reload would undo half of what it did.
            using var appRules = new AppRuleService(settings, SettingsStore.Save);

            using var hotkeys = new HotkeyService(settings, SettingsStore.Save);

            using FileSystemWatcher watcher = WatchSettings(manager, nightLight, appRules, hotkeys);

            manager.Run(cts.Token);
            return 0;
        }
        catch (Exception ex)
        {
            // Record why it died instead of vanishing with a bare exit code.
            Log.Write($"FATAL: {ex}");
            Console.Error.WriteLine($"fatal: {ex.Message}");
            return 1;
        }
        finally
        {
            // Unregister before the event handle is disposed, or the wait can
            // fire against a closed handle during shutdown.
            stopWait.Unregister(stopEvent);
        }
    }
}
