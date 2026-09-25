# Every page, from the command line

Each control in the app, page by page, and the command that does the same.
Built by listing every two-way binding and every handler in `src/DispCtrl.App/Views`
and following each to what it changes. Anything saved in `settings.json` is also
reachable with `dispctrl settings get|set --path ...`; the commands below are the
readable way in. Add `--dry-run` to any change to validate it without doing it,
and `--json` for one-line output.

The rule going forward: a feature lands in `DispCtrl.Control` first and the page
calls it. The Devices page already works that way - it sends the same
`devices.*` requests the CLI does.

## Displays

| In the app | Command |
| --- | --- |
| Arrangement: drag, apply, make main | `display set --monitor 2 --x -1920 --y 0`, `display set --monitor 2 --primary true` |
| Identify | `display identify` |
| Detect | `displays list --refresh` |
| Multiple displays (extend, duplicate, only one) | `topology get`, `topology set --mode extend` |
| Monitor library | `devices list`, `devices show`, `devices map`, `devices contribute` (docs/DEVICE-LIBRARY.md) |
| Connect to a wireless display | `windows open --page cast` |
| Unison: on, level, Windows brightness | `unison set --enabled on --level 50 --follow-windows on` |
| Unison: calibrated, and each display's limits | `unison set --calibrated on`, `unison set --monitor 2 --floor 20 --ceiling 80` |
| Unison: follow the room's light, sensor, ends, calibration, learned levels | `ambient set --enabled on --sensor-id ID --dark-level 20 --bright-level 100 --dark-lux 5 --bright-lux 800 --learn-corrections on`, `ambient capture --as dark`, `ambient forget` |
| Night light: on, strength, schedule, follow Windows | `nightlight set --enabled on --strength 60 --scheduled on --from 20:00 --to 07:00 --follow-windows off` (`--from`/`--to` are stored as `fromMinutes`/`toMinutes`) |
| Night light: dark mode on the schedule | `nightlight set --dark-mode-on-schedule on` (applies at the next look; after that once per boundary) |
| Lift the gamma limit | `gamma set --unlocked on` |
| Windows night light, colours, colour management | `windows open --page nightlight`, `--page colors`, `--page colormanagement` |
| Dark mode | `windows set --dark-mode on` |
| Focus mode, every option, window transition | `focus set --enabled on --dim-percent 40 ...` (`focus get` lists them) |
| Focus mode: which windows stay clear | `focus set --keep-clear focused\|pointer\|both` |
| OLED care, both stages | `oled set --enabled on --dim-percent 50 --idle-minutes 5 --second-stage-enabled on ...` |
| OLED care: which displays (connected and remembered), each on its own, apps that keep a display awake | `settings set --monitor TOKEN --path isOled --value true` (and `oledProtection`), `oled set --per-display-activity on --excluded-apps "vlc.exe"` |
| Screen rest now | `oled rest --monitor 1 --minutes 5` |
| Keep awake | `awake set --mode timed --interval-hours 1 --keep-displays-on on` (`awake get` lists the fields) |
| Pin windows on top: the switch, what is pinned, unpin, unpin all | `pin set --enabled on`, `pin list`, `pin off --window 0x1A2B`, `pin off --all` |
| Pin windows on top: border, colour, width, opacity | `pin set --border on --border-colour "#FF8C00" --border-thickness 3 --border-opacity 100` (empty colour for the accent) |
| Pin windows on top: clear of focus mode and OLED dimming, fullscreen, excluded apps | `pin set --clear-in-focus on --clear-in-oled-care off --skip-fullscreen on --step-aside-for-fullscreen on --excluded-apps "game.exe"` |
| Move windows: gather onto a display | `placement gather --to 2` (`--to active` for the display in use) |
| Move windows: put back when a display returns, new windows on the display in use | `placement set --return-windows on --new-windows-on-active on` |
| Move windows: which display is in use, keep size, excluded apps | `placement set --active pointer --keep-size on --excluded-apps "obs64.exe"` |
| Windows: remember window locations, minimize on disconnect | `windows set --remember-window-locations on --minimize-on-disconnect off` |
| Settings: updates - check now, check automatically (opt-in), Not now | `update check`, `update set --check-automatically on`, `update skip`, `update get` |
| Reset focus, OLED care, keep awake | `focus reset`, `oled reset`, `awake reset` |
| Per display: brightness | `display set --monitor 2 --brightness 60` |
| Per display: in unison | `unison set --monitor 2 --include off` |
| Per display: DDC/CI off after a crash, talk to it again | `ddc get`, `ddc allow --monitor 2` |
| Per display: look for its controls (read-only probe), forget | `ddc probe --monitor 2 --save`, `ddc probe --monitor 2 --clear` |
| Per display: resolution, refresh, scale, orientation, HDR | `display set --monitor 2 --resolution 1920x1080 --refresh 120 --scale 100 --orientation 0 --hdr off` |
| Per display: wallpaper, and how it fits | `display set --monitor 2 --wallpaper C:\path.jpg`, `windows set --wallpaper-fit fill` |
| Per display: the monitor's own controls | `display controls --monitor 2`, `display control --monitor 2 --name input-source --value hdmi-1` |
| Per display: software dimming, warmth, OLED, sleep | `settings set --monitor 2 --path softwareBrightness --value 80` (and `nightLightStrength`, `isOled`, `monitorSleepMinutes` ...) |
| Per display: hide its taskbar, reclaim the space | `taskbar set --monitor 2 --hide on --reclaim-space on` |
| Per display: variable refresh, adaptive brightness, rotation | `windows set --variable-refresh on --adaptive-brightness off --auto-rotation on` |
| Per display: reset | `display reset --monitor 2`, with the monitor's own defaults `--factory --confirm` |
| Copy these details | `display get --monitor 2 --hardware` |

## Taskbar

| In the app | Command |
| --- | --- |
| Reveal: distance, delay, animation length (0 for none), polling | `taskbar set --reveal-px 2 --hide-delay-ms 400 --anim-ms 150 --idle-poll-ms 250 ...` (`taskbar get` lists them) |
| Glass, tint, rounding, opacity | `taskbar set --glass on --tint 15 --blur 6 --opacity 90` |
| Windows: auto-hide, alignment, combining, badges, flashing, widgets, task view, corner, small buttons, transparency | `windows set --auto-hide on --alignment 1 --combine-buttons 1 --show-widgets off ...` |
| Resets | `taskbar reset` |
| Windows taskbar settings | `windows open --page taskbar` |

## Quick panel

| In the app | Command |
| --- | --- |
| Show the icon | `tray set --enabled on` |
| Keep it on the taskbar | Windows' own setting, from the icon's right-click menu; not a DispCtrl setting |
| Tray menu: simple view, stop the engine, exit, close the app | `tray set --simple on`, `engine stop`; closing the app is the app's own (close its window), and exit is that plus `engine stop` |
| Simple mode, lock, stay open, animation, footer | `tray set --simple on --locked on --stay-open off --animate on --show-footer on` |
| Density, width, toggles per row, height | `tray set --density comfortable --width 360 --tile-columns 4 --fixed-height off` |
| Sections, toggles, rows, switches, and their order | `tray set --sections '["unison","tiles","displays"]'` (and `--tiles`, `--display-rows`, `--display-tiles`) |
| Your tiles | `tray set --custom-tiles '[...]'` |
| Which displays | `tray set --hidden-displays '["DEL-A234-9XYZ7K1"]'` |
| Mouse wheel over the icon, its step, on the panel's sliders | `tray set --tray-wheel all --wheel-step 5 --wheel-on-sliders off` (`off`, `main` or `all`) |
| Open it | `tray show` |
| Reset | `tray reset` |

## Hotkeys

| In the app | Command |
| --- | --- |
| The list | `hotkeys list` |
| Add | `hotkeys add --keys "Ctrl+Alt+Up" --action unison-up --step 5` (pin: `pin-window`, `unpin-all-windows`; gather: `gather-windows --display 2`, 0 for the display in use) |
| Change one | `hotkeys set --index 2 --enabled off` |
| Remove | `hotkeys remove --index 2` |
| Restore the defaults | `hotkeys reset` |

## Devices

| In the app | Command |
| --- | --- |
| Sync attached monitors (new models are read automatically by the engine) | `devices scan` |
| The list | `devices list` |
| Remove a model until the next explicit sync | `devices forget --model DEL-A234` |
| Map codes, watch | `devices show --monitor 2`, `devices probe --monitor 2` |
| Name it | `devices map --monitor 2 --code 0xE2 --name "Preset mode" --values "0x00=Standard,0x0B=ComfortView"` |
| Share with the project | `devices contribute --monitor 2 --open` |

## Settings and startup

| In the app | Command |
| --- | --- |
| Engine on and off | `engine start`, `engine stop`, `engine status` |
| Start at sign-in, keep the panel ready, open at sign-in, shortcuts | `startup set --engine on --preload-panel on --open-window off --start-menu on --desktop off` |
| Guard against the DDC/CI crash, blocked monitors | `ddc set --guard on`, `ddc get`, `ddc allow --model DEL-A234` |
| Logging | `settings set --path /global/logging --value true` |
| Reset everything | `settings reset` |
| Undo the way back | `restore undo` (and `restore now`, as Ctrl+Alt+Backspace; `restore get` shows the record) |
| Open the log and the settings folder | `diagnostics` prints both paths |
| Windows Startup apps | `windows open --page startup` |

## Help

| In the app | Command |
| --- | --- |
| Prepare problem report | `report --what "What happened" --steps "How to reproduce it"` |

## Not on the command line, and why

- **Presets.** Disabled in this build in the app and the CLI alike.
- **Other Help and About links.** Links to the repository; nothing to control.
- **The arrangement drag itself.** The CLI takes the position it would end at.
