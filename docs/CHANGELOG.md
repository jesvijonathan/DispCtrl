# Changelog

All notable changes to DispCtrl are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). The release workflow publishes the
section matching the tag as the release notes, so write entries for people
using DispCtrl, not for people reading the diff.

## [Unreleased]

### Added
- **Undo the way back**: Settings > Undo the way back, or `dispctrl restore undo`, switches back on what Ctrl+Alt+Backspace last turned off - each monitor's taskbar hiding and dimming, night light, focus, OLED care and taskbar opacity. `dispctrl restore now` does what the shortcut does.
- **Ctrl+Alt+Backspace puts every display back**: it undoes displays off, dimming, night light, taskbar hiding and opacity, keeping every other setting. On by default, and the Hotkeys page asks before it is switched off or removed.
- The device library counts how many times each model has been shared, by issue number only; the catalogue shows it.
- Turn off displays shows Keep awake and Stay active beside it - the same switches as everywhere else, so what is on stays on and chat apps keep showing you as available while the displays are off.
- Turn off displays can lock the computer when they wake: whoever brings them back meets the lock screen, however they do it.
- Turn off displays parks the pointer in a corner of a display that went off, and puts it back afterwards unless the mouse woke them.
- **Turn off displays**: blacks every display out, as if switched off, while the computer keeps running and stays awake. Ctrl+Alt+L, the quick panel tile, the Keep awake section, or `dispctrl awake displays-off --enabled on`; any input brings them back. Choose how dark, which displays (all, all but the main one, all but the one with the pointer or the active window, or only the main one), the delay, and whether only the pointer wakes a display.
- **By resolution** on the arrangement: draws each display by its pixel count, as Windows does, instead of by its real size.

### Changed
- Only one DispCtrl window runs at a time: opening it again brings the running one forward, restored if it was minimized.
- The engine starts whenever DispCtrl is opened, and a Store install starts it at sign-in by default.
- The quick panel opens in simple mode on a new install; the dot in its title bar switches to the full panel.

### Fixed
- A hidden taskbar on a monitor with another stacked against its edge appeared and disappeared instantly; it slides again, clipped to its own screen.
- A resting or turned-off display no longer keeps the engine checking for you ten times a second: it waits for the keyboard or mouse instead.
- With Stay active on, turned-off displays came back on by themselves after a minute: its pointer nudge counted as someone returning.
- Shared monitor records were never turned into pull requests: the intake on GitHub tried to run a Windows program on Linux.
- The arrangement preview showed no wallpaper when the file Windows reports was gone, online-only, or in a format the preview cannot read; it now falls back to Windows' own copy.
- A hidden taskbar on a monitor placed above another showed along the top of the lower screen.
- The Store version's tray icon was never kept on the taskbar, and the switch to keep it there was greyed out.
- When Windows Explorer still has an earlier build's taskbar glass loaded, the Taskbar page offers to restart Explorer instead of only saying so.

## [0.1.0] - 2026-09-24

The first public release.

### Added
- Repair and maintenance: **Settings > Updates and maintenance** and `dispctrl maintenance repair|clear-cache` put back the sign-in task and Start menu shortcut and clear logs and cached monitor data. The installer offers the same, plus a settings reset that keeps a backup.
- Help > Report a problem and `dispctrl report`: a scrubbed diagnostic report, reviewed before a prefilled issue opens.
- Devices learn each new monitor by themselves; remove one from the list, share one or all.
- Hotkeys show whether each shortcut is working or taken by another program, warn about combinations Windows keeps, and list Windows' own display shortcuts.
- Sponsor entry in the navigation, and contact details on the About page.
- **Stay active**: keeps the screen on and stops the lock screen and chat apps showing you as Away, with a one-pixel pointer nudge after a minute idle. A quick panel tile, a switch under Keep awake, a hotkey action and `dispctrl awake set --stay-active on`.
- The tray icon is drawn bolder while Stay active or Keep awake is on, and can use the Windows accent colour.
- The device library can say what a model's panel is, so built-in laptop panels, which have no DDC/CI, can be described too. A panel the library knows as OLED gets burn-in protection without being marked by hand. `dispctrl devices panel --monitor 1 --technology OLED` sets it; sharing carries it.
- Unison brightness across every display, each inside a calibrated range, with
  the option to replace Windows brightness: the Quick Settings slider and the
  laptop's brightness keys drive every display.
- Hardware brightness over DDC/CI and WMI, and software dimming below the
  hardware minimum.
- Night light per display or in unison, scheduled or following Windows, with an
  optional one-off lift of Windows' gamma limit for warmer and dimmer settings.
- Per-monitor taskbar hiding, per-monitor wallpaper, and a display arrangement
  drawn at physical size.
- Every DDC/CI control a monitor offers, and a device library that learns
  manufacturer-specific codes and shares them as a reviewed GitHub issue.
- Focus mode, OLED care with two-stage idle dimming, keep-awake and dark mode.
- A quick panel on the tray icon, with a simple mode, a lock, and sections that
  can be reordered, folded and hidden.
- Global hotkeys registered by the engine, with a default set.
- `dispctrl.exe`, a console CLI with JSON output covering every option in the app.
- A per-user installer, portable and CLI zips, and an MSIX for the Microsoft
  Store.

[Unreleased]: https://github.com/jesvijonathan/DispCtrl/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/jesvijonathan/DispCtrl/releases/tag/v0.1.0
