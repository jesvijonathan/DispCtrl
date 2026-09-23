# Changelog

All notable changes to DispCtrl are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). The release workflow publishes the
section matching the tag as the release notes, so write entries for people
using DispCtrl, not for people reading the diff.

## [Unreleased]

### Added
- Repair and maintenance: **Settings > Updates and maintenance** and `dispctrl maintenance repair|clear-cache` put back the sign-in task and Start menu shortcut and clear logs and cached monitor data. The installer offers the same, plus a settings reset that keeps a backup.
- Help > Report a problem and `dispctrl report`: a scrubbed diagnostic report, reviewed before a prefilled issue opens.
- Devices learn each new monitor by themselves; remove one from the list, share one or all.
- Hotkeys show whether each shortcut is working or taken by another program, warn about combinations Windows keeps, and list Windows' own display shortcuts.
- Sponsor entry in the navigation, and contact details on the About page.

### Changed
- Thirteen default shortcuts, four switched on; existing desks keep theirs and get the new ones switched off.
- The installer adds the desktop shortcut and `PATH` entry by default and asks on uninstall whether to keep your settings.
- The tray icon is kept on the taskbar the first time it appears.

### Fixed
- Reset all left most of the page showing old values, and missed the quick panel, the Windows brightness bridge and the start-up options.
- A display's Reset also factory-reset the monitor without asking; that is now an unticked option in a confirmation.
- Share opened an empty issue when a record was too long for a link.
- An exception on an engine background thread could leave a hidden taskbar off-screen.

## [0.1.0] - Unreleased

The first public beta.

### Added
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

[Unreleased]: https://github.com/jesvijonathan/Display-Control/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/jesvijonathan/Display-Control/releases/tag/v0.1.0
