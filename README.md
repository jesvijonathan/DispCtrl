<div align="center">

<img src="site/assets/logo.png" alt="DispCtrl" width="104" height="104">

# DispCtrl

**Every monitor on your desk, controlled as one.**

One place for brightness, night light, taskbars and OLED care across your desk.<br>
A native Windows app, a quick panel in your tray, and a scriptable command line.

[![Build](https://img.shields.io/github/actions/workflow/status/jesvijonathan/DispCtrl/build.yml?branch=master&label=build&logo=github)](https://github.com/jesvijonathan/DispCtrl/actions/workflows/build.yml) [![GitHub stars](https://img.shields.io/github/stars/jesvijonathan/DispCtrl?label=stars&color=8b5cf6&logo=github)](https://github.com/jesvijonathan/DispCtrl/stargazers) [![Downloads](https://img.shields.io/github/downloads/jesvijonathan/DispCtrl/total?color=8b5cf6)](https://github.com/jesvijonathan/DispCtrl/releases) [![Windows](https://img.shields.io/badge/platform-Windows-0078D4?logo=windows11&logoColor=white)](#install) [![Licence: MIT](https://img.shields.io/badge/licence-MIT-green)](LICENSE) [![Sponsor](https://img.shields.io/badge/sponsor-%E2%9D%A4-db61a2?logo=githubsponsors&logoColor=white)](https://github.com/sponsors/jesvijonathan)

[![Get it from the Microsoft Store](https://img.shields.io/badge/Microsoft_Store-8b5cf6?style=for-the-badge&logo=data:image/svg%2bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCI+PHBhdGggZD0iTTguNSA3VjUuNWEzLjUgMy41IDAgMCAxIDcgMFY3IiBmaWxsPSJub25lIiBzdHJva2U9IiNmZmYiIHN0cm9rZS13aWR0aD0iMS42Ii8+PHBhdGggZD0iTTMuNSA3aDE3bC0xIDEzLjVhMS41IDEuNSAwIDAgMS0xLjUgMS40SDZhMS41IDEuNSAwIDAgMS0xLjUtMS40eiIgZmlsbD0ibm9uZSIgc3Ryb2tlPSIjZmZmIiBzdHJva2Utd2lkdGg9IjEuNiIvPjxyZWN0IHg9IjcuNiIgeT0iMTAuMiIgd2lkdGg9IjQiIGhlaWdodD0iNCIgZmlsbD0iI0YyNTAyMiIvPjxyZWN0IHg9IjEyLjQiIHk9IjEwLjIiIHdpZHRoPSI0IiBoZWlnaHQ9IjQiIGZpbGw9IiM3RkJBMDAiLz48cmVjdCB4PSI3LjYiIHk9IjE1IiB3aWR0aD0iNCIgaGVpZ2h0PSI0IiBmaWxsPSIjMDBBNEVGIi8+PHJlY3QgeD0iMTIuNCIgeT0iMTUiIHdpZHRoPSI0IiBoZWlnaHQ9IjQiIGZpbGw9IiNGRkI5MDAiLz48L3N2Zz4=)](https://apps.microsoft.com/detail/9PNQWKNRGVR0)&nbsp;
[![Download the installer](https://img.shields.io/github/v/release/jesvijonathan/DispCtrl?style=for-the-badge&logo=github&label=Download&color=8b5cf6)](https://github.com/jesvijonathan/DispCtrl/releases/latest)&nbsp;
[![Install with winget](https://img.shields.io/badge/winget-install-8b5cf6?style=for-the-badge)](#install)

[Install](#install) · [Getting started](#getting-started) · [Documentation](#documentation) · [Website](https://jesvijonathan.github.io/DispCtrl/)

<br>

<img src="site/assets/hero.png" alt="The DispCtrl window with the quick panel open in front of it" width="100%">

</div>

## Features

<table>
<tr>
<td width="50%" valign="top">

### Brightness

- **Unison**: one slider for every display, each within its own calibrated range
- **Replace Windows brightness**: let the laptop's brightness keys and Quick Settings move every display
- Hardware brightness over DDC/CI and WMI, software dimming below it

</td>
<td width="50%" valign="top">

### Colour and comfort

- Night light per display or in unison, scheduled or following Windows
- Warmth down to 1900K with the optional expanded gamma range
- Focus mode, keep-awake, Stay active and dark mode

</td>
</tr>
<tr>
<td width="50%" valign="top">

### Taskbar and desktop

- Hide taskbars independently on secondary monitors
- A wallpaper per display; arrangement drawn at true physical size
- Extend, duplicate and single-screen modes; identify and detect

</td>
<td width="50%" valign="top">

### OLED care

- Two-stage idle dimming, paused for full-screen video and games
- Knows OLED panels from the monitor or the device library; pairs with taskbar hiding

</td>
</tr>
<tr>
<td width="50%" valign="top">

### Your monitor's own controls

- Contrast, input, picture mode, volume and more, over DDC/CI
- Controls offered according to the monitor's capabilities
- A shared device library for named controls and panel information

</td>
<td width="50%" valign="top">

### Automation

- A quick panel on the tray icon: tiles, sliders, a row per display
- Global hotkeys that work with the window closed
- `dispctrl.exe`: every option, with JSON output

</td>
</tr>
</table>

<details>
<summary><b>All features</b>: every setting, switch and command, in full</summary>

<br>

**Brightness and unison**
- Unison brightness: one slider for every display, relative to the level each is already at
- Captured limits: set how dim and how bright each display goes, and run unison between them
  - A guided calibration that drives each display to the end being captured; Cancel puts the desk back
  - Per display floor and ceiling (`dispctrl unison set --monitor 2 --floor 20 --ceiling 80`)
- Replace Windows brightness: Quick Settings' slider and the laptop's brightness keys move every display, each within its range, with DispCtrl closed
- Hardware brightness over DDC/CI (external monitors) and WMI (built-in panels), with a fallback to VCP `0x10`
- Software dimming below the hardware floor, composed with night light in one gamma write
- Unison on puts every display back at its remembered level; unison off leaves them alone
- A monitor plugged back in is brought back in step after it wakes
- Each display now: every display's level at a glance

**Following the room's light**
- Unison follows an ambient light sensor, from a level for a dark room to one for bright light
- Choose the sensor; set the dark and bright levels
- Calibrate: "This is dark" and "This is bright" take the sensor's reading now, shown live
- Learns from your adjustments: a level you set by hand while it follows is remembered for that light, and can be forgotten
- Waits out a hand passing the sensor; walks to the new level rather than jumping
- Switches Windows' own adaptive brightness off while it runs, so the two never fight

**Night light and colour**
- Night light on every display at once, or per display with its own warmth
- Strength, down to 1900 K with the expanded gamma range (one elevated write, asked for when you switch it on)
- A schedule, including hours that run past midnight
- Or use Windows' night light: one switch, both ways
- Dark mode for apps and the taskbar together
- Colour profile shown per display

**Focus mode**
- Dims everything but the active window
- Background dimming level, delay after switching windows, fade duration
- Transition between windows: fade or slide
- One clear window per display
- Keep the hovered window clear too
- OLED displays only; match the dimming to each display's brightness
- Follow the mouse; follow new and activated windows
- Dim other monitors; keep the taskbar area clear
- Pause for full-screen windows; excluded apps
- Per display: let focus mode dim it or not

**OLED care**
- Rests OLED panels after a spell without input; non-OLED panels are never touched
- Which displays; minutes before it dims; idle dimming level with a live preview
- A second stage: after more minutes, dim further
- Fade duration; pause during full-screen content
- Wake when the pointer returns, per display
- Rest now, on one display or all, for a set time (`dispctrl oled rest --monitor 2 --minutes 5`)
- OLED panels known from the monitor itself (VCP `0xB6`), the device library, or your own marking

**Keep awake, Stay active and Turn off displays**
- Keep awake: indefinitely, for a time interval, or until a date and time; optionally keep the displays on too. The Windows power plan is never changed
- Stay active: the screen stays on, and chat apps keep showing you as available
- Turn off displays (Ctrl+Alt+L): black every display out while the computer keeps running
  - Darkness level; which displays (all, all but the main one, all but the one with the pointer or the active window, only the main one)
  - A delay before they go off
  - Turn the real backlight down too, at 90% and darker
  - Wake only by the pointer, per display, or on any input
  - Hide the pointer while they are off
  - Lock the computer when they wake

**The way back**
- Ctrl+Alt+Backspace puts every display back: displays off, dimming, night light, focus, OLED care, taskbar hiding and opacity, and nothing else
- Undo the way back switches on again what it last turned off
- The Hotkeys page asks before this shortcut is switched off or removed

**Each display**
- Resolution, refresh rate (rate-only changes applied without blanking), scale, orientation
- HDR; variable refresh rate
- Make main display
- Brightness, software brightness and adaptive brightness
- Night light warmth
- Rotate with the device
- Mark as an OLED panel
- Monitor sleep
- Hide the taskbar; reclaim the work area so maximised windows fill the display
- Wallpaper per display, with the fit mode
- Reset this display's DispCtrl settings

**Your monitor's own controls (DDC/CI)**
- Offered only when the monitor lists the control, and only with the values it lists
- Written: colour temperature, brightness, contrast, colour preset, red, green and blue gain and black level, gamma, input source, volume, mute, sharpness, ambient light sensor, OSD and power-button lock, OSD language, power mode, picture mode
- Read: firmware level, MCCS version, controller type, hours in use, display technology, sub-pixel layout
- Factory reset of the monitor's own settings (`dispctrl display factory-reset`), confirmed first
- Controls a model's definition names become usable, with writable ones only where someone tested them

**Arrangement and display modes**
- An arrangement drawn at each display's real size, or by resolution as Windows draws it
- Drag a display between the positions Windows accepts, shown as outlines
- Identify: each display's number on it; Detect: find monitors that are connected but asleep
- PC screen only, duplicate, extend or second screen only
- Connect to a wireless display

**Taskbar**
- Hide the taskbar on any secondary display, sliding in when the pointer reaches its edge
- The main taskbar through Windows' own auto-hide
- Aero glass blur, with blur radius and tint
- Windows transparency; whole-bar opacity
- Smaller buttons, alignment, combine buttons and labels (separately on other taskbars)
- Task View and Widgets buttons, app badges, flashing apps, the show-desktop corner
- Reveal: hide delay, slide or snap, slide duration, edge sensitivity
- How often the engine checks the pointer, in five tiers, for a fast reveal at next to no cost

**Quick panel and tray icon**
- A panel that rises from the taskbar, like Windows' own flyouts
- Simple mode: brightness only, one slider for all displays and one for each
- Sections: unison, quick toggles, displays, night light, taskbar, focus, OLED care, display mode
- Quick toggles: night light, dark mode, focus, keep awake, stay active, taskbar, OLED care, project, displays off, detect, unison, identify, cast, rest OLED, engine
- Rows per display: brightness, switches, resolution, scale, refresh rate, software dimming, orientation, monitor controls, warmth, input source
- Switches per display: hide taskbar, HDR, make main, focus dimming, OLED care, rest now, monitor sleep, variable refresh, adaptive brightness, auto-rotate, identify
- Reorder and hide any of them; hide displays; fold sections
- Your own tiles: run a `dispctrl` command or open a program
- Density, width, toggles per row, fixed or fitted height, stay open, lock position, animation
- The tray icon: Windows' own style, taskbar white or black or your accent colour, bolder while Keep awake or Stay active is on; kept on the taskbar
- Its right-click menu: quick panel, open DispCtrl, simple view, keep on the taskbar, hide the icon, stop the engine, close the app, exit DispCtrl

**Hotkeys**
- Global shortcuts that work with the window closed, each on or off
- Actions: brightness up and down, unison up, down and on or off, contrast up and down, night light on or off, warmer and cooler, focus, OLED care, rest now, keep awake, stay active, displays off, put every display back, dark mode, taskbar, taskbar glass, next input, identify, the quick panel, apply a preset
- A step size per shortcut, and one display or all
- Shows whether each shortcut is working or taken by another program, and warns about combinations Windows keeps
- Windows' own display shortcuts listed alongside: project, move window, HDR, Quick Settings, cast, colour filters, restart the graphics driver

**Device library**
- Every monitor this PC has seen, read by itself the first time it is plugged in
- Each monitor's codes: standard, named by the library, or not yet named
- Probe: watch the unnamed codes while you use the monitor's own menu, then name them for the model, the brand or every monitor
- Say what a panel is (OLED, LCD), built-in panels included
- Contribute a monitor, or all of them, as one prefilled GitHub issue, with serials, paths and names removed first
- What the project already knows about a model is applied by itself

**Presets (beta)**
- Save the whole desk under a name and apply it again
- Shows what has drifted since it was saved
- Per-app rules: apply a preset while an app is in front, and a preset to return to
- Import, export, rename, map to other displays

**Command line**
- `dispctrl`: every feature, scriptable, with tables in a terminal and JSON when piped
- Displays, modes, capabilities, monitor controls, gamma, the device library, settings (get, set, reset, export, import, validate, schema)
- Focus, OLED care, keep awake, night light, taskbar, tray, unison, ambient light, topology, startup, hotkeys
- `apply` an ordered file of display and settings changes, with a dry run
- `watch` for display, settings and engine events, optionally running a script
- `restore now|undo`, `engine start|stop|status`, `maintenance repair|clear-cache`, `diagnostics`, `report`
- Talks to the running engine, or works on its own when the engine is not running
- Stable exit codes; an isolated configuration folder through `DISPCTRL_DATA_DIR`

**Startup and maintenance**
- Start the engine at sign-in; keep the quick panel ready; open DispCtrl at sign-in
- Start menu and desktop shortcuts; Windows' startup apps and folder
- Write a log; open the settings file and the log
- Check for updates, by hand only
- Repair the sign-in task and shortcuts; clear logs and cached monitor data
- Reset everything
- Only one DispCtrl window at a time

**Help, privacy and support**
- Report a problem: a scrubbed report with display details and recent logs, reviewed before a prefilled GitHub issue opens
- Request a feature; contribute code
- Fixes for a taskbar stuck off-screen, a primary taskbar that will not hide, a monitor that forgot its settings
- No account, no telemetry, no network calls of its own; nothing needs administrator rights
- A taskbar is always put back, even after a crash
- The installer is per user, stops the engine cleanly, and offers repair and a settings reset; uninstalling puts every taskbar back

</details>

## Install

| Where | How |
|---|---|
| **Microsoft Store** | [**Get DispCtrl from the Microsoft Store**](https://apps.microsoft.com/detail/9PNQWKNRGVR0). Installs and updates itself. |
| **winget** | `winget install 9PNQWKNRGVR0 --source msstore` installs the Store version from a terminal. |
| **GitHub** | [**Download the latest release**](https://github.com/jesvijonathan/DispCtrl/releases/latest) and pick a file below. |

Every download is **Windows · x64 · self-contained**: no separate .NET installation needed. The [website](https://jesvijonathan.github.io/DispCtrl/#download) has the same options.

| Package | What you get |
|---|---|
| **`...-setup.exe`** (recommended) | Per-user install, no administrator rights. Starts at sign-in, adds a desktop shortcut and `dispctrl` to your `PATH`. Updates in place and keeps your settings; can reset or repair on the way. |
| `...-desktop.zip` | Portable: unzip anywhere and run `DispCtrl.App.exe` |
| `...-cli.zip` | `dispctrl.exe` and the engine, without the window |

Settings live in `%LOCALAPPDATA%\DispCtrl` (the Store version keeps them in its own folder, `%LOCALAPPDATA%\Packages\JustVStudio.DispCtrl_…\LocalCache\Local\DispCtrl`). Uninstalling stops the engine cleanly, puts back any taskbar it hid, and asks whether to keep your settings. Unsigned builds may trigger SmartScreen; each release has its SHA-256 checksums attached as `*SHA256SUMS.txt`.

## Getting started

1. Open **DispCtrl** from the Start menu. The engine starts with it and puts an icon on the taskbar.
2. Click that icon for the quick panel.
3. On the **Displays** page, under **Calibrated range**, capture each screen's dimmest and brightest level.
4. Turn on **Replace Windows brightness** to have the brightness keys move every display.

Default shortcuts (change them on the Hotkeys page, where more are set up and switched off):

| Shortcut | Action |
|---|---|
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>PgUp</kbd> / <kbd>PgDn</kbd> | Every display brighter / dimmer |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>D</kbd> | Quick panel |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>N</kbd> | Night light |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>L</kbd> | Turn the displays off, or back on |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Backspace</kbd> | **Put every display back**: undoes dimming, night light, displays off and taskbar hiding |

> **A screen stuck black, dim or tinted?** Press <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Backspace</kbd>. It undoes everything DispCtrl does to how a screen looks, and keeps your other settings. Once you can see again, **Settings > Undo the way back** (or `dispctrl restore undo`) switches back on what it turned off.

### Compatibility and feature status

| Feature | What to know |
|---|---|
| External monitor controls | Require DDC/CI support. Enable it in the monitor's own menu if needed; available controls depend on the monitor and connection. |
| Laptop brightness keys | **Replace Windows brightness** follows a supported built-in panel's brightness changes and applies them across your displays. |
| Primary taskbar | Uses Windows' global auto-hide. Independent hiding applies to secondary taskbars. |
| Extended warmth and dimming | The expanded gamma range needs a one-time administrator-approved change. |
| Presets | Saving, importing, applying and app-triggered presets are disabled in default builds. The Presets page can export the current configuration as JSON. |

## A quick tour

<div align="center">
<img src="site/assets/demo.gif" alt="An animated tour of DispCtrl's display controls and settings" width="880">
</div>

<table>
<tr>
<td width="40%" align="center" valign="middle">

<img src="site/assets/screenshots/quick-panel-simple.png" alt="The quick panel in simple mode: one brightness slider for all displays and one for each" width="320"><br>
<sub>Simple mode, how it opens on a new install</sub>

<img src="site/assets/screenshots/quick-panel.png" alt="The full quick panel with brightness sliders and feature toggles" width="320"><br>
<sub>The full panel, one click on the dot away</sub>

</td>
<td width="60%" valign="middle">

### The quick panel

Click the tray icon to bring your display controls within reach.

**Brightness and quick toggles**<br>
Adjust brightness, night light, dark mode, focus, keep-awake, taskbar hiding and OLED care. Each toggle's arrow opens its options.

**Control each display**<br>
Rows below the toggles give you individual monitor controls.

**Simple mode**<br>
New installs open with just brightness: one slider for every display together, and one for each. The dot in the title bar switches to the full panel and back.

**Make it yours**<br>
Fold, reorder or hide sections. Add custom tiles to run a `dispctrl` command or open a program.

</td>
</tr>
</table>

### Brightness that agrees with itself

Capture the dimmest and brightest you want on each display once, and the unison slider runs every screen between its own limits.

<img src="site/assets/screenshots/unison.png" alt="Unison settings for calibrating each display's brightness range" width="880">

## Screenshots

[Explore the screenshot gallery on our website](https://jesvijonathan.github.io/DispCtrl/#tour).

<table>
<tr>
<td width="50%" valign="top">

**Displays**

<img src="site/assets/screenshots/displays.png" alt="Displays page with the monitor arrangement and shared controls" width="100%">

</td>
<td width="50%" valign="top">

**Each display**

<img src="site/assets/screenshots/display-settings.png" alt="Individual display settings for brightness, colour and monitor controls" width="100%">

</td>
</tr>
<tr>
<td width="50%" valign="top">

**Quick panel settings**

<img src="site/assets/screenshots/quick-panel-settings.png" alt="Quick panel settings for customising sections and tiles" width="100%">

</td>
<td width="50%" valign="top">

**Taskbar**

<img src="site/assets/screenshots/taskbar.png" alt="Taskbar appearance and reveal settings" width="100%">

</td>
</tr>
<tr>
<td width="50%" valign="top">

**Hotkeys**

<img src="site/assets/screenshots/hotkeys.png" alt="Global shortcuts with key combinations and registration status" width="100%">

</td>
<td width="50%" valign="top">

**Devices**

<img src="site/assets/screenshots/devices.png" alt="The device library with known monitors and sharing actions" width="100%">

</td>
</tr>
</table>

<details>
<summary><strong>View app settings</strong></summary>

<img src="site/assets/screenshots/settings.png" alt="App settings for startup, diagnostics and maintenance" width="880">

</details>

## Command line

Everything the app does, `dispctrl` does, from a script, a scheduled task or a macro key.

```powershell
dispctrl displays list                                        # what is attached
dispctrl unison set --level 40                                # every display, in step
dispctrl nightlight set --enabled on --from 20:00 --to 07:00
dispctrl display control --monitor 2 --name input-source --value hdmi-1
dispctrl hotkeys add --keys "Ctrl+Alt+Home" --action unison-up --step 10
dispctrl maintenance repair                                   # fix the sign-in task and shortcuts
```

`--json` prints one line of machine-readable output; exit codes are `0` done, `1` refused, `2` malformed, `4` timed out. `dispctrl help` lists everything, [docs/CLI.md](docs/CLI.md) is the reference, and [docs/examples](docs/examples) has scripts to start from.

<div align="center">
<img src="site/assets/cli.png" alt="dispctrl output in PowerShell" width="820">
</div>

## Privacy

DispCtrl's display controls run locally: **no telemetry, no automatic update checks, no account required**. Sharing a monitor record or problem report removes serial numbers, device paths and your account name, then opens a prefilled GitHub issue in your browser for you to review and submit.

Configuration exports and raw logs can contain identifying details; review them before sharing. See the [contribution guide](.github/CONTRIBUTING.md#reporting-a-bug) for report and log guidance.

## Documentation

| Guide | Contents |
|---|---|
| [Project website](https://jesvijonathan.github.io/DispCtrl/) | Features, screenshots, downloads and ways to support DispCtrl |
| [Command line](docs/CLI.md) | Commands, options, JSON output and exit codes |
| [Example scripts](docs/examples) | Display events, layouts and configuration files |
| [Quick panel](docs/QUICK-PANEL.md) | Sections, tiles and customisation |
| [Device library](docs/DEVICE-LIBRARY.md) | Monitor definitions, control mappings and contributions |
| [Development](docs/DEVELOPING.md) | Setup, builds, checks and platform requirements |
| [Releasing](docs/RELEASING.md) | Packaging and the release process |
| [Changelog](docs/CHANGELOG.md) | Changes and release history |

## Build from source

```powershell
git clone https://github.com/jesvijonathan/DispCtrl.git
cd DispCtrl
.\build.cmd setup -Install   # checks the machine; offers .NET 10 and MinGW through winget
.\build.cmd build            # builds; stops and restarts the engine for you
.\build.cmd test             # the hardware-free checks
.\build.cmd run app          # or: engine, panel, cli <arguments>
```

Double-click `build.cmd` for a menu. On Linux, macOS or WSL, `./build.sh` builds the non-UI projects and runs the checks that do not need Windows APIs; the application itself runs on Windows. [Development](docs/DEVELOPING.md) covers the options and prerequisites.

<details>
<summary><strong>Repository layout</strong></summary>

```
src/
  DispCtrl.App/        WinUI 3 window and quick panel
  DispCtrl.Engine/     resident process: tray, hotkeys, taskbar, night light
  DispCtrl.Cli/        dispctrl.exe
  DispCtrl.Control/    command API shared by all three, and the engine's pipe
  DispCtrl.Display/    everything that changes hardware: DDC/CI, CCD, gamma
  DispCtrl.Core/       settings, EDID, geometry; no hardware writes
  native/              taskbar-glass helper (C++)
devices/               the shared monitor library, a folder per model
tools/                 checks: controlcheck, presetcheck, devicecheck, ...
build/                 build, publish, installer and MSIX scripts
docs/                  CLI, developing, releasing, device library, changelog
site/                  the website (GitHub Pages) and every image the README uses
```

</details>

## Contributing

- **Send your monitor**: **Devices > Share** in the app, or `dispctrl devices contribute --monitor 2 --open`. It is the fastest way to make DispCtrl work on hardware other than the author's.
- **Report a problem**: **Help > Report a problem** in the app, or `dispctrl report --what "..."`, which gathers the diagnostics for you.
- **Send code**: see [CONTRIBUTING](.github/CONTRIBUTING.md).

Security issues go through [SECURITY](.github/SECURITY.md), not public issues.

## Support the project

<div align="center">

### Help build a better desk for everyone

DispCtrl is free and open source, built in spare time.<br>
Your support funds development and more monitors to test on.

[![Sponsor on GitHub](https://img.shields.io/badge/Sponsor_on_GitHub-db61a2?style=for-the-badge&logo=githubsponsors&logoColor=white)](https://github.com/sponsors/jesvijonathan)
[![Donate with PayPal](https://img.shields.io/badge/Donate_with_PayPal-0070ba?style=for-the-badge&logo=paypal&logoColor=white)](https://paypal.me/jesvijon)
[![Donate with UPI](https://img.shields.io/badge/Donate_with_UPI-168a5b?style=for-the-badge)](https://jesvijonathan.github.io/DispCtrl/#upi)

[Visit the support page](https://jesvijonathan.github.io/DispCtrl/#support)

**Every contribution helps.**<br>
[Star the repository](https://github.com/jesvijonathan/DispCtrl) · [Share your monitor](#contributing) · [Report a bug](https://github.com/jesvijonathan/DispCtrl/issues)

</div>

## Licence

[MIT](LICENSE) © 2026 [Jesvi Jonathan](https://www.jesvi.net) · [jesvi22j@gmail.com](mailto:jesvi22j@gmail.com) · [GitHub](https://github.com/jesvijonathan)
