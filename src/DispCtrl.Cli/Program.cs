using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;
using DispCtrl.Display.Cli;

// Console front end. The engine owns the resident work; everything a script
// wants to ask for is here, in a process that blocks the shell and returns a
// meaningful exit code.
if (args.Length == 0) return CommandLine.Usage(null);

string verb = args[0].ToLowerInvariant();
string[] rest = args.Length > 1 ? args[1..] : [];

return verb switch
{
    "help" or "--help" or "-h" or "/?" => CommandLine.Usage(null),
    "displays" or "list" => ListDisplays(),
    "enable" => SetHide(rest, true),
    "disable" => SetHide(rest, false),
    _ => CommandLine.Run(verb, rest),
};

static int ListDisplays()
{
    List<DisplayInfo> displays = CommandLine.Sorted();
    if (displays.Count == 0)
    {
        Console.Error.WriteLine("no displays resolved");
        return 1;
    }

    DispCtrlSettings settings = SettingsStore.Load();

    for (int i = 0; i < displays.Count; i++)
    {
        DisplayInfo d = displays[i];
        MonitorSettings m = settings.For(d.Token);

        Console.WriteLine($"{i + 1}. {d.Label}");
        Console.WriteLine($"     {d.Bounds.Width} x {d.Bounds.Height} @ {d.RefreshHz} Hz"
            + $"  ·  {d.Scale * 100:0}%  ·  {d.Connector}{(d.IsPrimary ? "  ·  main" : "")}");
        Console.WriteLine($"     token {d.Token}{(m.HideTaskbar ? "  ·  taskbar hidden" : "")}");
    }

    return 0;
}

static int SetHide(string[] args, bool hide)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine($"{(hide ? "enable" : "disable")} needs a display number or token");
        return 2;
    }

    List<DisplayInfo> targets = CommandLine.Resolve(args[0], out string? error);
    if (error is not null)
    {
        Console.Error.WriteLine(error);
        return 1;
    }

    DispCtrlSettings settings = SettingsStore.Load();
    foreach (DisplayInfo d in targets)
    {
        if (d.IsPrimary && hide)
        {
            Console.Error.WriteLine(
                $"{d.Label} is the main display. Explorer restores its taskbar immediately; "
                + "use Windows' own auto-hide there instead.");
            return 1;
        }

        settings.For(d.Token).HideTaskbar = hide;
        Console.WriteLine($"{d.Label}: taskbar {(hide ? "hidden" : "shown")}");
    }

    SettingsStore.Save(settings);
    return 0;
}
