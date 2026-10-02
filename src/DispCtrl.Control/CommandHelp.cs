namespace DispCtrl.Control;

/// <summary>dispctrl's help, by topic: an overview first, then one page per area.</summary>
/// <remarks>
/// It was one list of a hundred lines, in the order commands were added, and
/// several commands were missing from it. Each topic now holds the commands
/// for one part of the app, in the order the app's pages put them, and
/// <c>dispctrl help TOPIC</c> - or a command's own name, <c>dispctrl help oled</c> -
/// shows it. <see cref="All"/> is every topic, for the documentation and the
/// checks. Option names are the kebab-case of what <c>get</c> prints; a topic
/// lists the common ones, and <c>get</c> shows every one.
/// </remarks>
public static class CommandHelp
{
    public sealed record Topic(string Name, string Title, string Summary, string[] Commands, string Body);

    public static readonly Topic[] Topics =
    [
        new("displays", "Displays", "list, inspect and set each display; arrangement and topology",
            ["displays", "display", "topology", "gamma"], """
              displays list                         Connected monitors, numbered as Identify numbers them, with stable tokens
              display get [--monitor ID] [--hardware]   State; --hardware adds brightness, HDR, scaling and wallpaper
              display modes|capabilities --monitor ID   Supported modes, and every DDC/CI code the monitor lists
              display set --monitor ID|all [options]
                  --resolution 2560x1440 --refresh 144 --scale 125 --orientation 90 --hdr on --primary on
                  --x -1920 --y 0 --brightness 60 --brightness-by -10 --dim 70 --wallpaper FILE
                  --controls "contrast=70,input-source=hdmi-1"
              display identify                      Show each display's number on it
              display reset --monitor ID [--factory --confirm]   DispCtrl's settings for it (and the monitor's own)
              display factory-reset --monitor ID --confirm
              topology get|set --mode extend|duplicate|internal|external   As Win+P
              gamma get|set --unlocked on|off       Windows' gamma clamp: how warm and how dim the screens can go (asks for admin)
            """),
        new("controls", "The monitor's own controls", "contrast, input, picture modes and any code, over DDC/CI",
            ["control", "controls"], """
              display controls --monitor ID [--all] The monitor's controls, by name, with their values
              display control --monitor ID --name KEY [--value V]
                                                    Read or set one: --name picture-mode --value games, --name 0x12 --value 70
                                                    V may be next, previous, +5 or -5 (cycle a choice, step a range)
              display control --monitor ID --name 0xE9 --value 3 --raw
                                                    Any code, named or not (needs ddc set --raw-writes on)
              Naming codes the standard leaves unnamed: dispctrl help devices.
            """),
        new("brightness", "Brightness", "unison, calibrated ranges, the light sensor, software dimming",
            ["unison", "ambient", "brightness", "dim"], """
              unison get|set --enabled on --level 50   One level for every display
              unison set --follow-windows on        Windows' brightness slider and keys move unison
              unison set --monitor ID --floor 20 --ceiling 80   A display's calibrated range
              unison set --monitor ID --include off   Leave a display out (it keeps its own brightness)
              ambient get                           Following the room's light, and what the sensor reads now
              ambient set --enabled on --dark-level 20 --bright-level 100 --dark-lux 5 --bright-lux 800
              ambient capture --as dark|bright      Calibrate: the sensor's reading now becomes that end
              ambient forget|reset                  Drop learned levels, or every ambient setting
              display set --monitor ID --brightness 60 | --brightness-by -10 | --dim 70
            """),
        new("nightlight", "Night light and theme", "warmth, its schedule, per-display strength, dark mode",
            ["nightlight", "theme"], """
              nightlight get|set|reset              --enabled on --strength 45 --unison on --follow-windows off
                                                    --scheduled on --from-minutes 1200 --to-minutes 420 --dark-mode-on-schedule on
              nightlight set --monitor ID --strength 60   A display's own warmth (--strength common follows the shared one)
              windows set --dark-mode on            Windows' app and system theme
            """),
        new("care", "Screen care and power", "focus mode, OLED care, keep awake, turning the displays off, the way back",
            ["focus", "oled", "awake", "restore"], """
              focus get|set|reset                   --enabled on --dim-percent 65 --delay-ms 250 --fade-ms 300
                                                    --keep-clear focused|pointer|both --dim-other-monitors on --excluded-apps "obs64.exe"
              focus set --monitor ID --dim-percent 40   A display's own level (common follows the shared one)
              oled get|set|reset                    --enabled on --idle-minutes 4 --dim-percent 50
                                                    --second-stage-enabled on --second-stage-minutes 10 --second-stage-dim-percent 90
                                                    --third-stage-enabled on --third-stage-minutes 20 --third-stage-backlight on
                                                    --third-stage-keep-active off --pause-fullscreen on --pause-video on
                                                    --per-display-activity on --excluded-apps "vlc.exe"
              oled set --monitor ID --idle-minutes 10   A display's own timing (common puts it back)
              oled preview --percent 50             Two seconds of the dimming level (engine running, care on)
              oled rest --monitor ID --minutes 5    Rest a display now
              awake get|set|reset                   --mode off|indefinite|timed|expiration --stay-active on --keep-displays-on on
                                                    --interval-hours 1 --interval-minutes 30 (timed) --expiration-utc 2026-10-03T18:00Z
              awake displays-off --enabled on       Black the displays out while the computer keeps running
              awake set --displays-off-percent 100 --displays-off-target all --displays-off-wake-on-pointer off
                        --displays-off-lock-on-wake on --displays-off-backlight on
              restore now|undo|get                  Put every display back, as Ctrl+Alt+Backspace; undo puts back what it switched off
            """),
        new("windows", "Windows", "pin on top, gather, move and span, put back, new windows",
            ["pin", "placement"], """
              pin list                              Open windows, with the handle each is pinned by
              pin on|off|toggle [--window W]        W: 0x1A2B, an app's name, or part of a title; the window in front without it
              pin off --all                         Unpin every window
              pin get|set|reset                     --border on --border-colour "#FF8800" --border-thickness 3
                                                    --clear-in-focus on --clear-in-oled-care off --excluded-apps x.exe
              placement gather [--to N|active] [--from N]   Every window onto one display
              placement move [--window W] --to N|next|previous|active   One window onto a display
              placement span [--window W] [--displays 1,2]   Stretch a window across displays
              placement get|set|reset               --return-windows on --new-windows-on-active on --active pointer --keep-size on
            """),
        new("taskbar", "Taskbar and quick panel", "hiding, glass looks, reveal timing; the tray icon and quick panel",
            ["taskbar", "tray"], """
              taskbar get|set|reset                 --glass on --look blur|clear|opaque|acrylic --accent on --colour "#202020"
                                                    --border off --blur 6 --tint 15 --opacity 90
                                                    --hide-delay-ms 350 --anim-ms 180 --reveal-px 2
              taskbar set --monitor ID --hide on --reclaim-space on   Hide one display's taskbar
              windows get|set                       Windows' own taskbar: --auto-hide on --alignment 0 --show-widgets off ...
              tray get|set|reset                    --enabled on --simple off --icon brightness --icon-colour taskbar
                                                    --density comfortable --width 360 --tile-columns 4 --fixed-height off
                                                    --stay-open off --locked off --animate on --show-footer on
                                                    --tray-wheel off|main|all --wheel-step 5 --wheel-on-sliders off
              tray set --sections '["tiles","displays"]'   And --tiles, --display-rows, --display-tiles, --custom-tiles
              tray show                             Open the quick panel
            """),
        new("presets", "Presets and desk profiles (Beta)", "save the whole desk, apply it, launch with it, apply when a desk connects",
            ["preset", "presets"], """
              preset list                           Every preset, its displays, and whether it holds a layout
              preset save NAME                      The desk now, windows included (keeps an existing preset's scope)
              preset apply NAME                     Put the desk back to it
              preset diff NAME                      What differs from the desk now, and its displays not attached
              preset delete NAME
              preset desk NAME on|off               Apply by itself when exactly its displays connect
              preset launch NAME PROGRAM [ARGS] [--wait-for game.exe] [--keep]   Apply, run, put the desk back after
            """),
        new("automation", "Shortcuts, features and triggers", "hotkeys, named step lists, and running them when something happens",
            ["hotkeys", "features", "triggers"], """
              hotkeys list|add|set|remove|reset     --keys "Ctrl+Alt+PageUp" --action unison-up --step 5 --display 2
                  --action display-mode --mode extend|duplicate|internal|external
                  --action set-control --control picture-mode --value fps --display 2
                  --action next-control-value|previous-control-value --control input-source
                  --action control-up|control-down --control 0xF9 --step 2
                  --action run-feature --feature Gaming | run-command --command "topology set duplicate"
                  --action open-program --command notepad.exe --arguments notes.txt
              features list|get|add|set|remove|run  Named steps, run in order (docs/CUSTOM-CONTROLS.md)
              features add --name Gaming --steps "set 2 picture-mode fps; wait 500; dispctrl nightlight set --enabled off"
              features run Gaming [--dry-run]       Steps: set MONITOR CONTROL VALUE, dispctrl ARGS, run TARGET, script PATH, wait MS
              triggers list|get                     When this happens, run that feature
              triggers add --event EVENT --feature NAME [--match vlc.exe|2|20:00] [--minutes 10]
                  display-connected, display-disconnected, app-in-front, app-left, idle, back,
                  locked, unlocked, on-battery, on-power, resumed, at-time
              triggers set --index N [--enabled off] ...   triggers remove --index N
            """),
        new("devices", "Device library", "every monitor seen, naming its codes, sharing them",
            ["devices", "ddc"], """
              devices list                          Every monitor model seen here, and what is known of it
              devices show --monitor ID|--model KEY Every code: standard, named, or not yet named
              devices scan [--monitor ID]           Read the controls again, and save new codes for Contribute
              devices probe --monitor ID [--codes unknown|all|0xE2] [--seconds 120]   Watch codes while you use the monitor's menu
              devices map --monitor ID --code 0xE2 --name "Preset mode" [--values "0x0B=ComfortView"]
                  [--kind range|choice|action|information] [--writable] [--scope model|brand|all]
                  LG alternate input: --transport lg-input --code 0x60 --source-address 0x50 --write-code 0xF4
              devices unmap --monitor ID --code 0xE2 [--scope ...]
              devices similar --monitor ID          Known models whose names would name this one's unknown codes
              devices link --monitor ID --to DEL-A233 [--remove]   Use (or stop using) another model's names
              devices panel --monitor ID --technology OLED|LCD|none   What a panel is; built-in panels too
              devices definitions [--model KEY]     The library's layers, shipped and local
              devices forget --monitor ID|--model KEY   Off the list until the next scan
              devices contribute|share --monitor ID|--all [--open]   Everything above, as one issue to review and submit
              devices validate FILE                 Check a definition before sharing it
              ddc get|set|reset --guard on|off      The guard against a capabilities read crashing Windows
              ddc set --raw-writes on|off           Advanced: display control --raw on any code
              ddc allow --monitor ID|--model KEY    Talk to a monitor the guard blocked again
              ddc probe --monitor ID [--save|--clear]   Read-only: which known codes a monitor answers
            """),
        new("system", "Settings, startup and the engine", "the settings file, start at sign-in, updates, company laptop switches",
            ["settings", "startup", "engine", "maintenance", "update", "machine", "windows", "report", "status", "diagnostics"], """
              settings get|schema                   Every saved setting, or the generated JSON schema
              settings set --path /global/... --value JSON
              settings set --monitor ID --path alias --value office
              settings reset [--path /global/focus]   One part, or everything (as Settings > Reset everything)
              settings export [--output FILE]       Every saved setting, identities included
              settings validate|import FILE         Check, or replace, the saved settings
              startup get|set                       --engine on --preload-panel on --open-window off --start-menu on --desktop off
              engine start|stop|status              The resident engine
              maintenance repair|clear-cache        Fix the sign-in task and shortcuts; clear logs and cached data
              update check|get|set|skip|reset       The opt-in update check (never downloads)
              machine get|set|undo                  Sign-in and lock screen switches: no-ctrl-alt-del, lock-screen-picture,
                                                    no-lock-screen, sharp-sign-in, lock-after, dynamic-lock, quiet-lock-screen
              windows open --page display|nightlight|colors|taskbar|startup|power|hdr|cast|colormanagement
              status|diagnostics|commands           The engine, the displays, and every command
              report [--what TEXT] [--steps TEXT]   A scrubbed bug report, and the issue link
            """),
        new("scripting", "Scripting", "ordered documents, watching for changes, raw requests",
            ["apply", "watch", "scripts", "request"], """
              apply FILE [--dry-run]                Ordered display and settings operations (docs/examples)
              watch [--events displays,settings,engine] [--interval 500]   Print changes as they happen
              watch --script PATH.ps1               Run a script on each change
              scripts list|run FILE                 Local scripts
              request FILE|-                        Send one versioned JSON request
            """),
    ];

    private const string Common = """
        Options common to every command
          --monitor ID     A display: its number, token or alias; all for every one
          --json           The JSON envelope (the default when piped); --text keeps tables
          --dry-run        Check the request and change nothing
          --local          Run here rather than through the engine
          --timeout 30000  How long to wait for the engine, in milliseconds
        Exit codes: 0 done, 1 refused or partly done, 2 asked wrongly, 4 timed out, 130 cancelled.
        DISPCTRL_DATA_DIR points DispCtrl at another settings folder (an absolute path).
        """;

    /// <summary>What <c>dispctrl help</c> prints: one line per topic.</summary>
    public static string Overview(bool presets)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("dispctrl - every DispCtrl feature from a terminal or a script").AppendLine();
        text.AppendLine("  dispctrl help TOPIC    one of the topics below, or a command's name (dispctrl help oled)");
        text.AppendLine("  dispctrl help all      every topic").AppendLine();
        foreach (Topic t in Visible(presets))
            text.AppendLine($"  {t.Name,-12} {t.Title}: {t.Summary}");
        text.AppendLine($"  {"short",-12} The first command line's short forms: brightness -10, input \"HDMI 1\"...").AppendLine();
        text.Append(Common);
        return text.ToString();
    }

    /// <summary>One topic, found by its name or by a command it covers; null when nothing matches.</summary>
    public static string? For(string name, bool presets)
    {
        string key = name.Trim().ToLowerInvariant();
        if (key is "short" or "shortcuts-short" or "legacy") return LegacyCommands.Help;
        if (key == "all") return All(presets);
        Topic? t = Visible(presets).FirstOrDefault(t => t.Name == key) ?? Visible(presets).FirstOrDefault(t => t.Commands.Contains(key));
        return t is null ? null : Page(t);
    }

    /// <summary>Every topic, then the short forms and the common options.</summary>
    public static string All(bool presets) =>
        string.Join(Environment.NewLine, Visible(presets).Select(Page)) + Environment.NewLine + LegacyCommands.Help + Environment.NewLine + Environment.NewLine + Common;

    private static IEnumerable<Topic> Visible(bool presets) => Topics.Where(t => presets || t.Name != "presets");

    private static string Page(Topic t) => $"{t.Title}\n{t.Body.TrimEnd()}\n";
}
