# Settings reference

Every setting DispCtrl stores in `settings.json`, with its default on a new install, the values it takes and what it does.
Most have a switch in the app; the rest can be changed with

```powershell
dispctrl settings set --path /global/oledCare/idleMinutes --value 6
dispctrl settings set --monitor 2 --path hideTaskbar --value true
```

`{monitor}` is a display's identity token (or its number or alias on the command line); `[]` is each item of a list.
The file lives in `%LOCALAPPDATA%\DispCtrl` (the Microsoft Store version keeps it in its own package folder).
Generated from the settings themselves; `DispCtrl.Control.Checks` fails when a setting is missing here.

## Shared settings

| Setting | Default | Values | What it does |
|---|---|---|---|
| `/global/focus/enabled` | off | on or off | Focus mode on or off |
| `/global/focus/dimPercent` | 64 | number | How dark the background goes, 0 unchanged to 100 black |
| `/global/focus/delayMs` | 500 | number | Milliseconds after switching windows before the background dims |
| `/global/focus/fadeMs` | 300 | number | Milliseconds for the dimming to appear and disappear, and for the slide |
| `/global/focus/oledOnly` | off | on or off | Dim only panels marked as OLED |
| `/global/focus/easeBetweenWindows` | off | on or off | Slide the clear area across instead of cutting it at the new window. |
| `/global/focus/crossFadeWindows` | on | on or off | Fade the old cut-out out and the new one in when the window changes. |
| `/global/focus/scaleWithBrightness` | on | on or off | Ease the dimming on panels that unison brightness is already running dim. |
| `/global/focus/perMonitorFocus` | on | on or off | Give every display its own clear window rather than sharing one. |
| `/global/focus/keepHoveredClear` | on | on or off | Keep the window under the pointer clear as well as the focused one. |
| `/global/focus/followMouse` | on | on or off | Keep whatever the pointer is over clear, rather than the focused window. |
| `/global/focus/prioritizeNewWindows` | on | on or off | Temporarily prefer a newly shown or activated top-level window. |
| `/global/focus/dimOtherMonitors` | on | on or off | Also dim monitors without the active window |
| `/global/focus/pauseFullscreen` | on | on or off | Stop dimming while a full-screen window is in front |
| `/global/focus/keepTaskbarVisible` | off | on or off | Leave the taskbar area at normal brightness |
| `/global/focus/excludedApps` | "" | text | Executable names separated by commas, semicolons or newlines. |
| `/global/oledCare/enabled` | off | on or off | Dim automatically after IdleMinutes without input. |
| `/global/oledCare/idleMinutes` | 4 | number | Minutes without input before OLED panels dim |
| `/global/oledCare/dimPercent` | 60 | number | How far the first stage dims, 100 is black |
| `/global/oledCare/secondStageEnabled` | on | on or off | Dim again after a longer spell |
| `/global/oledCare/secondStageMinutes` | 12 | number | Further minutes before the second stage |
| `/global/oledCare/secondStageDimPercent` | 95 | number | How far the second stage dims; never lighter than the first |
| `/global/oledCare/fadeMs` | 2000 | number | Milliseconds to fade to the idle level |
| `/global/oledCare/pauseFullscreen` | on | on or off | No idle dimming while full-screen content plays |
| `/global/oledCare/pauseVideo` | on | on or off | Keep a display awake while it shows an app playing a video, fullscreen or not. |
| `/global/oledCare/thirdStageEnabled` | off | on or off | A third stage after the second: the display goes black, as if off. |
| `/global/oledCare/thirdStageMinutes` | 20 | number | Further minutes, after the second stage, before the display turns off. |
| `/global/oledCare/thirdStageBacklight` | on | on or off | At the third stage, also turn the display's own brightness to its lowest. |
| `/global/oledCare/thirdStageKeepActive` | on | on or off | At the third stage, keep holding the computer awake as Keep awake and Stay active ask. |
| `/global/oledCare/perDisplayActivity` | off | on or off | Rest each display when it goes unused, rather than when the whole computer does. |
| `/global/oledCare/excludedApps` | "" | text | Executable names that keep the display showing them awake, separated by commas. |
| `/global/awake/mode` | "PowerPlan" | PowerPlan, Indefinite, Timed, Expiration | Keep awake: power plan (off), indefinitely, for an interval, or until a time |
| `/global/awake/keepDisplaysOn` | off | on or off | Also stop the displays timing out while keeping awake |
| `/global/awake/stayActive` | off | on or off | Keeps the screen on and the session looking attended, until switched off: no timer. See the engine's PowerService. |
| `/global/awake/displaysOffUtc` | none | text | When "Turn off displays" was asked for; null while the displays are on. |
| `/global/awake/displaysOffPercent` | 100 | number | How dark "off" is: 100 is black. |
| `/global/awake/displaysOffDelaySeconds` | 1 | number | Seconds between asking and the displays going dark. |
| `/global/awake/displaysOffTarget` | "All" | All, ExceptMain, ExceptPointer, ExceptActiveWindow, OnlyMain | Which displays Turn off displays blacks out |
| `/global/awake/displaysOffWakeOnPointer` | off | on or off | Wake a display only when the pointer moves on it, rather than on any input: a download or a render can be typed at, or a long read scrolled with the keys, without lighting every screen. |
| `/global/awake/displaysOffHidePointer` | on | on or off | Park the pointer in a corner of a display that went off, so no arrow floats on the black. |
| `/global/awake/displaysOffLockOnWake` | off | on or off | Lock the computer the moment the displays come back on. |
| `/global/awake/displaysOffBacklight` | off | on or off | Also turn each display's real backlight down, on those that allow it. |
| `/global/awake/intervalHours` | 1 | number | Hours, for keep awake for a time interval |
| `/global/awake/intervalMinutes` | 0 | number | Minutes, for keep awake for a time interval |
| `/global/awake/timedUntilUtc` | "0001-01-01T00:00:00+00:00" | text | When the current interval ends |
| `/global/awake/expirationUtc` | "2026-10-03T01:42:45.5055302+05:30" | text | Keep awake until this date and time |
| `/global/pin/enabled` | on | on or off | Whether pinning is offered at all. Switching it off unpins every window DispCtrl pinned. |
| `/global/pin/border` | on | on or off | Draw a coloured border around each pinned window. |
| `/global/pin/borderColour` | "" | text | The border's colour as #RRGGBB; empty for Windows' accent colour. |
| `/global/pin/borderThickness` | 3 | number | The border's width, in DIP. |
| `/global/pin/borderOpacity` | 100 | number | The border's opacity, 20-100. |
| `/global/pin/clearInFocus` | on | on or off | Keep pinned windows clear of focus mode's dimming. |
| `/global/pin/clearInOledCare` | off | on or off | Keep pinned windows clear of OLED idle dimming as well. |
| `/global/pin/skipFullscreen` | on | on or off | Refuse to pin a window that fills its display, such as a game. |
| `/global/pin/stepAsideForFullscreen` | on | on or off | Let a fullscreen window in front go over pinned windows on its display, for as long as it is there. |
| `/global/pin/excludedApps` | "" | text | Executable names never pinned, separated by commas. |
| `/global/placement/returnWindows` | off | on or off | Put windows back on a display when it returns, as they were when it left. |
| `/global/placement/tookOverWindowsMemory` | off | on or off | Whether an earlier build switched Windows' own window memory off, and owes it back. |
| `/global/placement/newWindowsOnActive` | off | on or off | Move a newly opened window to the display in use, when it opens somewhere else. |
| `/global/placement/active` | "Pointer" | Pointer, ActiveWindow | Which display counts as the one in use for new windows and for gathering. |
| `/global/placement/keepSize` | on | on or off | Keep a gathered window's size in real units (DIP) rather than in pixels. |
| `/global/placement/excludedApps` | "" | text | Executable names never moved by gathering, returning or new-window placement. |
| `/global/ddcGuard/enabled` | on | on or off | Watch capabilities reads and stop talking to a monitor that took Windows down. |
| `/global/ddcGuard/blocked` | empty | list | Monitors DispCtrl no longer talks to over DDC/CI, until allowed again. |
| `/global/ddcGuard/blocked[]/token` | none | text | The monitor's identity token; this unit, not every one of its model. |
| `/global/ddcGuard/blocked[]/model` | none | text | The model, as DEL-A234, so the list says what kind of monitor it is. |
| `/global/ddcGuard/blocked[]/label` | none | text | The monitor's name as Windows reported it. |
| `/global/ddcGuard/blocked[]/sinceUtc` | none | text | When the guard stopped talking to it. |
| `/global/ddcGuard/blocked[]/reason` | none | text | Why, in a sentence. |
| `/global/ddcGuard/allowRawWrites` | off | on or off | Advanced, off by default: lets display control --raw and the app read and write any VCP code, including manufacturer codes nobody has mapped. |
| `/global/updates/checkAutomatically` | on | on or off | Look for a new release at most once a day. On unless switched off. |
| `/global/updates/checkedUtc` | none | text | When a check last got an answer; bookkeeping. |
| `/global/updates/latestVersion` | "" | text | The newest release that check found, as 0.1.6; empty before any. |
| `/global/updates/latestUrl` | "" | text | Its release page. |
| `/global/updates/skippedVersion` | "" | text | A release somebody chose "Not now" for: not announced again, though a later one is. |
| `/global/quickPanel/enabled` | on | on or off | Whether the icon is in the notification area at all. |
| `/global/quickPanel/icon` | "Brightness" | Brightness, Display, AppLogo | The icon in the notification area. |
| `/global/quickPanel/iconColour` | "Taskbar" | Taskbar, Accent | The glyph's colour: the taskbar's own white or black, or Windows' accent colour. |
| `/global/quickPanel/iconShowsActive` | on | on or off | Draws the glyph bolder while Stay active or Keep awake is on, so it can be seen at a glance. |
| `/global/quickPanel/trayWheel` | "Off" | Off, Main, All | Brightness from the mouse wheel over the icon. |
| `/global/quickPanel/wheelStep` | 5 | number | How far one notch of the wheel moves brightness, in percent. |
| `/global/quickPanel/wheelOnSliders` | off | on or off | Let the wheel move the panel's sliders when the pointer is over one. |
| `/global/quickPanel/density` | "Comfortable" | Compact, Comfortable, Spacious | Toggle size and labelling |
| `/global/quickPanel/width` | 360 | number | Panel width in DIP. |
| `/global/quickPanel/tileColumns` | 4 | number | How many toggles sit side by side in the grid. |
| `/global/quickPanel/fixedHeight` | on | on or off | Keep the panel one height and scroll inside it, rather than growing to fit. |
| `/global/quickPanel/height` | 500 | number | The height when FixedHeight is on, in DIP. |
| `/global/quickPanel/stayOpen` | off | on or off | Keep the panel open when it loses focus. |
| `/global/quickPanel/simple` | on | on or off | Brightness sliders only: all displays together, then one per display. |
| `/global/quickPanel/locked` | off | on or off | Keep the panel where it opens: its title is not a drag handle. |
| `/global/quickPanel/animate` | on | on or off | Animate the panel unless Windows has disabled animation effects. |
| `/global/quickPanel/showFooter` | off | on or off | The row that opens the full app, at the bottom. |
| `/global/quickPanel/customTiles` | empty | list | Tiles somebody made, run from the panel. See QuickPanelCustomTile. |
| `/global/quickPanel/customTiles[]/id` | none | text | Always starts with Prefix, so it can never collide with a built-in id. |
| `/global/quickPanel/customTiles[]/label` | none | text | The tile's name. |
| `/global/quickPanel/customTiles[]/glyph` | none | text | A Segoe Fluent Icons code point, as a one-character string. |
| `/global/quickPanel/customTiles[]/kind` | none | Command, Program | Command runs a dispctrl command; Program opens a program, file or address. |
| `/global/quickPanel/customTiles[]/target` | none | text | The dispctrl arguments, or the program, script, document or URL. |
| `/global/quickPanel/customTiles[]/arguments` | none | text | Arguments for a Program; unused for a command. |
| `/global/quickPanel/customTiles[]/hint` | none | text | Hover text. |
| `/global/quickPanel/collapsed` | empty | list | Sections and displays folded away in the panel, by id - a section's id, or display: and a display's identity token. |
| `/global/quickPanel/expanded` | empty | list | Sections folded by default that somebody has opened. |
| `/global/quickPanel/sections` | list | list | The blocks of the full panel, in order, each shown or hidden |
| `/global/quickPanel/sections[]/id` | none | text | Which section. |
| `/global/quickPanel/sections[]/visible` | none | on or off | Whether it shows. |
| `/global/quickPanel/tiles` | list | list | The quick toggles, in order, each shown or hidden |
| `/global/quickPanel/displayRows` | list | list | The rows under each display's name, in order |
| `/global/quickPanel/displayTiles` | list | list | The switches in each display's block, in order |
| `/global/quickPanel/hiddenDisplays` | empty | list | Identity tokens of displays the panel leaves out. |
| `/global/taskbarOpacity` | 100 | number | Whole taskbar opacity, including icons. 100 leaves Explorer untouched. |
| `/global/taskbarGlassEnabled` | off | on or off | Use Explorer's compositor-backed XAML taskbar blur. |
| `/global/taskbarGlassRadius` | 48 | number | Gaussian blur radius in XAML/compositor pixels. |
| `/global/taskbarGlassTint` | 24 | number | How strongly the glass colour (accent or own) shows, in every look, 0 to 100. |
| `/global/taskbarGlassLook` | "Blur" | Blur, Clear, Opaque, Acrylic | What the taskbar's surface is, with glass on; off, it is Windows' own. |
| `/global/taskbarGlassColour` | "" | text | The tint's colour, written #RRGGBB; empty for black. |
| `/global/taskbarGlassAccent` | off | on or off | Tint with Windows' accent colour, followed when it changes, instead of TaskbarGlassColour. |
| `/global/taskbarGlassBorder` | on | on or off | Keep the thin line along the taskbar's top edge; off hides it while glass is on. |
| `/global/hideDelayMs` | 1500 | number | How long the bar stays out after the cursor leaves. |
| `/global/animMs` | 320 | number | Slide duration. 0 restores an instant snap. |
| `/global/revealPx` | 2 | number | How close to the screen edge the cursor must get to reveal. |
| `/global/armDistancePx` | 300 | number | Distance from a managed edge at which polling speeds up. |
| `/global/idlePollMs` | 100 | number | Poll interval when no managed edge is near the cursor. |
| `/global/farPollMs` | 500 | number | Longest the engine will ever wait between cursor checks. |
| `/global/armedPollMs` | 16 | number | Poll interval inside the armed band. |
| `/global/shownPollMs` | 40 | number | Poll interval while a bar is revealed. |
| `/global/logging` | off | on or off | Write a rolling log next to the settings file. |
| `/global/unisonBrightness` | on | on or off | Drive every display's brightness from one relative control. |
| `/global/unisonLevel` | 100 | number | The unison level, as a percentage of each display's own baseline. |
| `/global/unisonCalibrated` | off | on or off | Drive unison from a calibrated low/high limit per display instead of a multiplier on one captured level. |
| `/global/unisonFollowsWindows` | on | on or off | Let Windows' own brightness - the Quick Settings slider and the keyboard's brightness keys - drive the unison level. |
| `/global/preloadQuickPanel` | on | on or off | Start the quick panel hidden alongside the engine, so the first click opens it at once. |
| `/global/openWindowAtSignIn` | off | on or off | Open the DispCtrl window too when the engine starts at sign-in. |
| `/global/hotkeyDefaultsOffered` | off | on or off | Whether the default hotkeys have been added; see OfferDefaults. |
| `/global/hotkeyDefaultsVersion` | 0 | number | Which defaults version this desk has been offered; 0 before versions were counted. |
| `/global/trayPromotedFor` | empty | list | Engine locations whose tray icon was put on the taskbar once, by default. |
| `/global/lastDesk` | none | text | The set of displays the engine last saw settle, as DeskProfiles.Fingerprint. |
| `/global/machineBefore` | group | group | Registry values as Windows had them before DispCtrl's sign-in switches first changed them. |
| `/global/engineStartupOffered` | off | on or off | Whether the Store package's sign-in task has been switched on once, by default. |
| `/global/presetChangeNotice` | on | on or off | Say, across the top of the window, when the displays have changed from the preset in use. Off, the Presets page still lists the changes. |
| `/global/support/firstSeenUtc` | none | text | When the window first opened. |
| `/global/support/windowOpens` | 0 | number | How many times the window has opened. It asks from the fifth, three days after the first. |
| `/global/support/declined` | 0 | number | How many times the request was closed without a star or a donation. |
| `/global/support/declinedUtc` | none | text | When it was last closed that way; it asks once more ninety days later. |
| `/global/support/done` | off | on or off | Never ask again: somebody starred or donated, or closed it twice. |
| `/global/beforeRestore/takenUtc` | none | text | When Ctrl+Alt+Backspace recorded it. |
| `/global/beforeRestore/focus` | none | on or off | Whether focus mode was on |
| `/global/beforeRestore/oledCare` | none | on or off | Whether OLED care was on |
| `/global/beforeRestore/nightLight` | none | on or off | Whether night light was on |
| `/global/beforeRestore/taskbarOpacity` | none | number | The taskbar opacity it put back to 100 |
| `/global/beforeRestore/monitors` | none | one per display | Monitors that had taskbar hiding or software dimming on, by token. |
| `/global/beforeRestore/monitors/{monitor}/hideTaskbar` | none | on or off | Whether that monitor's taskbar was hidden |
| `/global/beforeRestore/monitors/{monitor}/softwareBrightness` | none | number | That monitor's software brightness |
| `/global/arrangeByResolution` | off | on or off | Draw the arrangement by pixel count, as Windows does, rather than by real size. |
| `/global/nightLight/enabled` | off | on or off | Warm the displays, subject to Scheduled. |
| `/global/nightLight/strength` | 5 | number | How warm, 0-100. See NightLight.KelvinFor for the range. |
| `/global/nightLight/unison` | on | on or off | Drive every display from Strength rather than each from its own. |
| `/global/nightLight/calibrated` | off | on or off | Run the unison slider between each display's captured warmth limits. |
| `/global/nightLight/followWindows` | off | on or off | Keep this toggle and Windows' own night light as one setting. |
| `/global/nightLight/scheduled` | off | on or off | Only warm between FromMinutes and ToMinutes. |
| `/global/nightLight/fromMinutes` | 1200 | number | Start of the warm period, in minutes past local midnight. |
| `/global/nightLight/toMinutes` | 420 | number | End of the warm period, in minutes past local midnight. |
| `/global/nightLight/darkModeOnSchedule` | off | on or off | Switch Windows to dark mode for the scheduled hours, and back to light after. |
| `/global/nightLight/themeAppliedUtc` | none | text | When the schedule last set the theme; null to set it at the next look. |
| `/global/ambient/enabled` | off | on or off | Unison follows the room's light |
| `/global/ambient/sensorId` | "" | text | The sensor to follow; empty for the one Windows calls its default. |
| `/global/ambient/darkLevel` | 25 | number | Unison level in a dark room |
| `/global/ambient/brightLevel` | 100 | number | Unison level in bright light |
| `/global/ambient/darkLux` | 5 | number | The light at and below which unison sits at DarkLevel. |
| `/global/ambient/brightLux` | 800 | number | The light at which unison reaches BrightLevel: about a bright office. |
| `/global/ambient/learnCorrections` | on | on or off | Learn from the unison slider, hotkeys and brightness keys while following. |
| `/global/ambient/points` | empty | list | Levels chosen by hand in a given light, newest winning; see Learn. |
| `/global/ambient/points[]/lux` | none | number | The light it was learned in |
| `/global/ambient/points[]/level` | none | number | The level chosen there |

## Each display

| Setting | Default | Values | What it does |
|---|---|---|---|
| `/monitors/{monitor}/label` | none | text | Last known friendly name. Purely so the settings file is readable — never used for matching, since names are not unique. |
| `/monitors/{monitor}/hideTaskbar` | none | on or off | Hide this monitor's taskbar, revealing it on cursor approach. |
| `/monitors/{monitor}/reclaimWorkArea` | none | on or off | Expand this monitor's work area to the full panel while its taskbar is managed. Revealing the bar overlays maximized windows without resizing them. |
| `/monitors/{monitor}/brightnessBaseline` | none | number | The brightness this display sits at when unison is at 100%. |
| `/monitors/{monitor}/brightnessFloor` | none | number | The dimmest level this display should reach when unison is calibrated. -1 means it has not been captured. |
| `/monitors/{monitor}/brightnessCeiling` | none | number | The brightest level this display should reach when unison is calibrated. -1 means it has not been captured. |
| `/monitors/{monitor}/nightLightStrength` | none | number | This display's own warmth, 0-100. -1 means it has not been set and the shared value applies. |
| `/monitors/{monitor}/nightLightFloor` | none | number | Least warmth this display should reach when unison is calibrated. |
| `/monitors/{monitor}/nightLightCeiling` | none | number | Most warmth this display should reach when unison is calibrated. |
| `/monitors/{monitor}/softwareBrightness` | none | number | Brightness for panels with no hardware control, 10-100. |
| `/monitors/{monitor}/softwareDimming` | off | on or off | Dim this display in software even though it has brightness control of its own: its slider, unison and the quick panel then move the software brightness instead of the backlight. |
| `/monitors/{monitor}/isOled` | none | on or off | Whether this panel is OLED, and where that was decided. |
| `/monitors/{monitor}/oledDetected` | none | on or off | Last reported panel technology, so the engine never polls DDC for protection. |
| `/monitors/{monitor}/oledProtection` | none | on or off | Whether OLED idle care and screen rests reach this panel |
| `/monitors/{monitor}/alias` | none | text | Optional unique script-friendly monitor name. |
| `/monitors/{monitor}/oledWakeOnPointerReturn` | none | on or off | Keep an idle panel dimmed until the pointer moves on that panel. |
| `/monitors/{monitor}/oledRestMinutes` | none | number | Length of a screen rest run from this display |
| `/monitors/{monitor}/monitorSleepEnabled` | none | on or off | Turn this external monitor off through MCCS power mode after inactivity. |
| `/monitors/{monitor}/monitorSleepMinutes` | none | number | Idle minutes before monitor sleep |
| `/monitors/{monitor}/monitorSleepState` | 4 | 2, 3, 4 or 5 | The power mode monitor sleep sends: 2 standby, 3 suspend, 4 off, 5 off (hard). Only a mode the monitor lists is sent. |
| `/monitors/{monitor}/inUnison` | none | on or off | Whether unison brightness moves this display. |
| `/monitors/{monitor}/probedCodes` | none | list | VCP codes, as hex, that a read-only probe found this monitor answering, used in place of a capabilities string it cannot give. Null when unused. |
| `/monitors/{monitor}/focusDimming` | none | on or off | Whether focus dimming touches this display at all. |
| `/monitors/{monitor}/focusDimPercent` | none | number | How far focus mode dims this display; -1 is the same as every display. |
| `/monitors/{monitor}/oledRestUntilUtc` | none | text | Temporary screen-rest request consumed by the engine. |
| `/monitors/{monitor}/lastSeenUtc` | none | text | When this monitor was last connected or left, so lists of monitors put recent ones first. |
| `/monitors/{monitor}/oledCare/idleMinutes` | none | number | This display's own OLED care: rest after this many minutes. Empty follows the shared setting. |
| `/monitors/{monitor}/oledCare/dimPercent` | none | number | This display's own OLED care: first-stage dimming. Empty follows the shared setting. |
| `/monitors/{monitor}/oledCare/secondStageEnabled` | none | on or off | This display's own OLED care: second stage on or off. Empty follows the shared setting. |
| `/monitors/{monitor}/oledCare/secondStageMinutes` | none | number | This display's own OLED care: second stage after. Empty follows the shared setting. |
| `/monitors/{monitor}/oledCare/secondStageDimPercent` | none | number | This display's own OLED care: second-stage dimming. Empty follows the shared setting. |
| `/monitors/{monitor}/oledCare/thirdStageEnabled` | none | on or off | This display's own OLED care: third stage on or off. Empty follows the shared setting. |
| `/monitors/{monitor}/oledCare/thirdStageMinutes` | none | number | This display's own OLED care: turn off after. Empty follows the shared setting. |
| `/monitors/{monitor}/oledCare/thirdStageBacklight` | none | on or off | This display's own OLED care: backlight off with the third stage. Empty follows the shared setting. |
| `/monitors/{monitor}/formerTokens` | none | list | Tokens this monitor's settings were saved under before, newest last. |

## Shortcuts

| Setting | Default | Values | What it does |
|---|---|---|---|
| `/hotkeys` | empty | list | Global keyboard shortcuts, registered by the engine. |
| `/hotkeys[]/key` | none | number | Win32 virtual-key code. |
| `/hotkeys[]/modifiers` | none | number | MOD_ALT 1, MOD_CONTROL 2, MOD_SHIFT 4, MOD_WIN 8. |
| `/hotkeys[]/action` | none | BrightnessUp, BrightnessDown, NightLightToggle, NightLightWarmer, NightLightCooler, ApplyPreset, NextInput, Identify, UnisonUp, UnisonDown, UnisonToggle, FocusToggle, OledCareToggle, OledRestNow, KeepAwakeToggle, DarkModeToggle, QuickPanel, TaskbarToggle, TaskbarGlassToggle, ContrastUp, ContrastDown, StayActiveToggle, DisplaysOffToggle, RestoreDisplays, PinWindow, UnpinAllWindows, GatherWindows, DisplayMode, AmbientToggle, ReturnWindowsToggle, NewWindowsToggle, SoftwareDimUp, SoftwareDimDown, MakePrimary, HdrToggle, VariableRefreshToggle, VolumeUp, VolumeDown, MuteToggle, RunCommand, OpenProgram, SetControl, NextControlValue, PreviousControlValue, ControlUp, ControlDown, RunFeature, MoveWindowNext, MoveWindowPrevious, MoveWindowTo, SpanWindow | What the shortcut does |
| `/hotkeys[]/display` | none | number | Which display, as the number shown in the panel. Zero means all. |
| `/hotkeys[]/preset` | none | text | Preset name, for ApplyPreset. |
| `/hotkeys[]/mode` | none | text | Which arrangement, for DisplayMode: Extend, Duplicate, InternalOnly or ExternalOnly. |
| `/hotkeys[]/command` | none | text | The dispctrl arguments for RunCommand, or what OpenProgram opens. |
| `/hotkeys[]/arguments` | none | text | Arguments for OpenProgram; unused by a command. |
| `/hotkeys[]/step` | none | number | How much a step changes, for the actions that step. |
| `/hotkeys[]/control` | none | text | The monitor control a control action works on: its key as display controls lists it (picture-mode), its name, or its code (0x15). |
| `/hotkeys[]/value` | none | text | What SetControl sets: a value's key or name, or a number. |
| `/hotkeys[]/feature` | none | text | The custom feature RunFeature runs. |
| `/hotkeys[]/enabled` | none | on or off | Whether the shortcut is registered |

## App rules

| Setting | Default | Values | What it does |
|---|---|---|---|
| `/appRules` | empty | list | Rules that switch presets when an app takes the foreground. |
| `/appRules[]/process` | none | text | Executable name, with or without the extension. Case-insensitive. |
| `/appRules[]/preset` | none | text | The preset to apply while that app is in front. |
| `/appRules[]/enabled` | none | on or off | Whether this rule is live. |
| `/appRules[]/revertTo` | none | text | Preset to return to once the app is no longer in front. Blank leaves whatever the app's preset set in place. |
| `/appRules[]/restorePrevious` | none | on or off | Restore an in-memory snapshot when leaving this app. |
| `/appRules[]/dwellSeconds` | none | number | Seconds the app must remain foreground before switching. |

## Custom features

| Setting | Default | Values | What it does |
|---|---|---|---|
| `/features` | empty | list | Custom features: named lists of steps - monitor controls, dispctrl commands, programs and scripts - run by name from the command line, a hotkey or a quick-panel tile. |
| `/features[]/name` | none | text | The feature's name, which hotkeys, tiles and triggers run it by. |
| `/features[]/description` | none | text | What it is for, in the owner's words; optional. |
| `/features[]/steps` | none | list | One step per line; see Parse. |

## Triggers

| Setting | Default | Values | What it does |
|---|---|---|---|
| `/triggers` | empty | list | When this happens, run that feature: see Trigger. |
| `/triggers[]/enabled` | none | on or off | Whether the trigger runs. |
| `/triggers[]/event` | none | DisplayConnected, DisplayDisconnected, AppInFront, AppLeft, Idle, Back, Locked, Unlocked, OnBattery, OnPower, Resumed, AtTime | What happens: DisplayConnected, DisplayDisconnected, AppInFront, AppLeft, Idle, Back, Locked, Unlocked, OnBattery, OnPower, Resumed or AtTime. |
| `/triggers[]/match` | none | text | What the event is about: an executable, a display (number, name, model or token), a time; empty for any. |
| `/triggers[]/minutes` | none | number | For Idle and Back: how long counts as away. |
| `/triggers[]/feature` | none | text | The custom feature to run, by name. |
| `/triggers[]/needsMatch` | none | on or off | Worked out from the event, not set: whether it needs a match. |
| `/triggers[]/timeOfDay` | none | text | Worked out from the match, not set: the time of an at-time trigger. |

## The file

| Setting | Default | Values | What it does |
|---|---|---|---|
| `/version` | 1 | number | Schema version, so a future format change can migrate rather than reset. |
| `/monitors` | group | one per display | Per-monitor settings keyed on DisplayKey.ToToken(). |
