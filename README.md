<div align="center">

<img src="docs/assets/logo.png" alt="DispCtrl" width="104" height="104">

# DispCtrl

**Every monitor on your desk, controlled as one.**

Brightness in step across every screen, night light and dimming per display, a taskbar that hides where you want it, OLED care, and a command line for all of it. For Windows 11.

[![Build](https://img.shields.io/github/actions/workflow/status/jesvijonathan/Display-Control/build.yml?branch=master&label=build&logo=github)](https://github.com/jesvijonathan/Display-Control/actions/workflows/build.yml) [![Release](https://img.shields.io/github/v/release/jesvijonathan/Display-Control?include_prereleases&label=release&color=8b5cf6)](https://github.com/jesvijonathan/Display-Control/releases) [![Downloads](https://img.shields.io/github/downloads/jesvijonathan/Display-Control/total?color=8b5cf6)](https://github.com/jesvijonathan/Display-Control/releases) [![Windows 11](https://img.shields.io/badge/Windows-11-0078D4?logo=windows11&logoColor=white)](#install) [![Licence: MIT](https://img.shields.io/badge/licence-MIT-green)](LICENSE) [![Sponsor](https://img.shields.io/badge/sponsor-%E2%9D%A4-db61a2?logo=githubsponsors&logoColor=white)](https://github.com/sponsors/jesvijonathan)

[**Download**](https://github.com/jesvijonathan/Display-Control/releases) · [Features](#features) · [Install](#install) · [Command line](#command-line) · [Build from source](#build-from-source) · [Contribute](#contributing) · [Sponsor](#support-the-project)

<br>

<img src="docs/assets/hero.png" alt="The DispCtrl window with the quick panel open in front of it" width="100%">

</div>

## Features

<table>
<tr>
<td width="50%" valign="top">

### Brightness
- **Unison**: one slider for every display, each within its own calibrated range
- **Replaces Windows brightness**: the brightness keys and Quick Settings move every display
- Hardware brightness over DDC/CI and WMI, software dimming below it

### Taskbar and desktop
- Hide the taskbar per monitor, keep it on the others
- A wallpaper per display; arrangement drawn at true physical size
- Extend, duplicate and single-screen modes; identify and detect

### Your monitor's own controls
- Contrast, input, picture mode, volume and more, over DDC/CI
- Only the values the monitor accepts; nothing written blind
- A device library that learns unnamed codes, and shares them, laptop panels included

</td>
<td width="50%" valign="top">

### Colour and comfort
- Night light per display or in unison, scheduled or following Windows
- Warmer and dimmer than Windows allows, down to 1900K
- Focus mode, keep-awake and dark mode

### OLED care
- Two-stage idle dimming, paused for full-screen video and games
- Knows OLED panels from the monitor or the device library; pairs with taskbar hiding

### Automation
- A quick panel on the tray icon: tiles, sliders, a row per display
- Global hotkeys that work with the window closed
- `dispctrl.exe`: every option, with JSON output

</td>
</tr>
</table>

## A quick tour

<div align="center">
<img src="docs/assets/demo.gif" alt="A tour of the DispCtrl window" width="880">
</div>

<table>
<tr>
<td width="40%" valign="top" align="center">
<img src="docs/assets/screenshots/quick-panel.png" alt="The quick panel" width="320">
</td>
<td valign="top">

### The quick panel

Click the tray icon and it rises from the taskbar: one slider for every display, a switch that hands Windows' brightness keys to it, and toggles for night light, dark mode, focus, keep-awake, the taskbar and OLED care. Each toggle's arrow opens its options.

Below them, a row per display with its brightness, switches and modes. Sections fold, reorder and hide, and your own tiles can run any `dispctrl` command or open a program. Simple mode keeps it to brightness alone.

### Brightness that agrees with itself

Capture the dimmest and brightest you want on each display once, and the unison slider runs every screen between its own limits.

<img src="docs/assets/screenshots/unison.png" alt="Unison brightness settings" width="100%">

</td>
</tr>
</table>

## Install

Download from [**Releases**](https://github.com/jesvijonathan/Display-Control/releases). Windows 11, x64; nothing else to install.

| Package | What you get |
|---|---|
| **`...-setup.exe`** (recommended) | Per-user install, no administrator rights. Starts at sign-in, adds a desktop shortcut and `dispctrl` to your `PATH`. Updates in place and keeps your settings; can reset or repair on the way. |
| `...-desktop.zip` | Portable: unzip anywhere and run `DispCtrl.App.exe` |
| `...-cli.zip` | `dispctrl.exe` and the engine, without the window |
| winget, Microsoft Store | With the first stable release: `winget install JesviJonathan.DispCtrl` |

Settings live in `%LOCALAPPDATA%\DispCtrl`. Uninstalling stops the engine cleanly, puts back any taskbar it hid, and asks whether to keep your settings. Releases are not code-signed yet, so SmartScreen may ask once; each release lists SHA-256 checksums to verify against.

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
<img src="docs/assets/cli.png" alt="dispctrl output in PowerShell" width="820">
</div>

## Privacy

DispCtrl makes **no network calls**: no telemetry, no update check, no account. The only things that can leave your machine are a monitor record or a problem report, and only when you send one: DispCtrl removes serial numbers, device paths and your account name, shows you the exact text, and opens a prefilled GitHub issue in your own browser for you to submit.

## Screenshots

<table>
<tr>
<td width="50%" valign="top">

**Displays**
<img src="docs/assets/screenshots/displays.png" alt="Displays page">

**Hotkeys**
<img src="docs/assets/screenshots/hotkeys.png" alt="Hotkeys page">

**Quick panel settings**
<img src="docs/assets/screenshots/quick-panel-settings.png" alt="Quick panel page">

**Settings**
<img src="docs/assets/screenshots/settings.png" alt="Settings page">

</td>
<td width="50%" valign="top">

**Each display**
<img src="docs/assets/screenshots/display-settings.png" alt="A display's settings">

**Devices**
<img src="docs/assets/screenshots/devices.png" alt="Devices page">

**Taskbar**
<img src="docs/assets/screenshots/taskbar.png" alt="Taskbar page">

</td>
</tr>
</table>

## Build from source

```powershell
git clone https://github.com/jesvijonathan/Display-Control.git
cd Display-Control
.\build.cmd setup -Install   # checks the machine; offers .NET 10 and MinGW through winget
.\build.cmd build            # builds; stops and restarts the engine for you
.\build.cmd test             # the hardware-free checks
.\build.cmd run app          # or: engine, panel, cli <arguments>
```

Double-click `build.cmd` for a menu, or use `./build.sh` on Linux, macOS or WSL for everything except the window. [docs/DEVELOPING.md](docs/DEVELOPING.md) covers every option, [docs/RELEASING.md](docs/RELEASING.md) the release process.

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
```

## Contributing

- **Send your monitor**: **Devices > Share** in the app, or `dispctrl devices share --monitor 2 --open`. It is the fastest way to make DispCtrl work on hardware other than the author's.
- **Report a problem**: **Help > Report a problem** in the app, or `dispctrl report --what "..."`, which gathers the diagnostics for you.
- **Send code**: see [CONTRIBUTING](.github/CONTRIBUTING.md).

Security issues go through [SECURITY](.github/SECURITY.md), not public issues.

## Support the project

DispCtrl is free and built in spare time. [**Sponsoring on GitHub**](https://github.com/sponsors/jesvijonathan) pays for development and for test hardware, and every monitor record you send helps as much.

## Licence

[MIT](LICENSE) © 2026 [Jesvi Jonathan](https://www.jesvi.net) · [jesvi22j@gmail.com](mailto:jesvi22j@gmail.com) · [GitHub](https://github.com/jesvijonathan)
