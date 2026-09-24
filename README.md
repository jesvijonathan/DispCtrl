<div align="center">

<img src="site/assets/logo.png" alt="DispCtrl" width="104" height="104">

# DispCtrl

**Every monitor on your desk, controlled as one.**

One place for brightness, night light, taskbars and OLED care across your desk.<br>
A native Windows app, a quick panel in your tray, and a scriptable command line.

[![Build](https://img.shields.io/github/actions/workflow/status/jesvijonathan/DispCtrl/build.yml?branch=master&label=build&logo=github)](https://github.com/jesvijonathan/DispCtrl/actions/workflows/build.yml) [![GitHub stars](https://img.shields.io/github/stars/jesvijonathan/DispCtrl?label=stars&color=8b5cf6&logo=github)](https://github.com/jesvijonathan/DispCtrl/stargazers) [![Downloads](https://img.shields.io/github/downloads/jesvijonathan/DispCtrl/total?color=8b5cf6)](https://github.com/jesvijonathan/DispCtrl/releases) [![Windows](https://img.shields.io/badge/platform-Windows-0078D4?logo=windows11&logoColor=white)](#install) [![Licence: MIT](https://img.shields.io/badge/licence-MIT-green)](LICENSE) [![Sponsor](https://img.shields.io/badge/sponsor-%E2%9D%A4-db61a2?logo=githubsponsors&logoColor=white)](https://github.com/sponsors/jesvijonathan)

[**Download**](https://github.com/jesvijonathan/DispCtrl/releases) · [Getting started](#getting-started) · [Documentation](#documentation) · [Website](https://jesvijonathan.github.io/DispCtrl/)

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

## Install

Browse the [download options on our website](https://jesvijonathan.github.io/DispCtrl/#download), or download directly from [**Releases**](https://github.com/jesvijonathan/DispCtrl/releases). **Windows · x64 · self-contained downloads** — no separate .NET installation needed.

| Package | What you get |
|---|---|
| **`...-setup.exe`** (recommended) | Per-user install, no administrator rights. Starts at sign-in, adds a desktop shortcut and `dispctrl` to your `PATH`. Updates in place and keeps your settings; can reset or repair on the way. |
| `...-desktop.zip` | Portable: unzip anywhere and run `DispCtrl.App.exe` |
| `...-cli.zip` | `dispctrl.exe` and the engine, without the window |

Settings live in `%LOCALAPPDATA%\DispCtrl`. Uninstalling stops the engine cleanly, puts back any taskbar it hid, and asks whether to keep your settings. Unsigned builds may trigger SmartScreen; release downloads include SHA-256 checksums.

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
