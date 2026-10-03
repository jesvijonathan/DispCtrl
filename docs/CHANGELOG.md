# Changelog

All notable changes to DispCtrl are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). The release workflow publishes the
section matching the tag as the release notes, so write entries for people
using DispCtrl, not for people reading the diff.

## [Unreleased]

### Added
- Presets are switched on (still labelled Beta): save and apply the whole desk, apply automatically when a desk's displays connect, launch a program with a preset and get the desk back after, and per-app rules. They were built but left out of earlier releases.
- Taskbar glass looks, as TranslucentTB offers them: blur, clear, opaque and acrylic, tinted with a colour of your own or Windows' accent colour, and the thin border along the taskbar's top shown or hidden. Off is Windows' normal taskbar. On the Taskbar page and `dispctrl taskbar set --look acrylic --accent on --border off`.
- OLED care can pause while a video plays on a display - in a browser or a player, fullscreen or in a window - as Windows' media sessions report it; music playing alone still lets the displays rest. On by default, beside "Pause during fullscreen content".
- Each display's night light warmth and focus dimming level can follow the common setting or be its own, with "Same as all displays" on the display's card and `common` on the command line (`dispctrl focus set --monitor 2 --dim-percent common`). Resetting a display puts all of its own values back.
- Triggers: when something happens, run a custom feature - a display connected or disconnected, an app coming to the front or leaving it, being away and coming back, the computer locking or unlocking, unplugging or plugging in, waking, or a time of day. On the Hotkeys page and `dispctrl triggers`; the engine runs them, looking only as often as the triggers in use need.
- Move the window in front to the next, previous or a chosen display, keeping its place and size in proportion across displays at different scales, and stretch it across every display: shortcuts (offered switched off on Ctrl+Alt+], [ and S) and `dispctrl placement move --to next` / `placement span`.
- Company laptop switches, under Settings and `dispctrl machine`: sign in without Ctrl+Alt+Del, your own lock screen picture, skip the lock screen, no blur behind the sign-in box, lock after inactivity, dynamic lock, and no tips on the lock screen. Each reads back what Windows has, asks for administrator permission only when a machine-wide one is changed, says when an organisation manages the computer, and can be put back as it was.
- OLED care's third stage: after the dim stages a display goes black as if off, with its backlight down too, and "Keep the computer active" off lets Windows sleep and lock on its own timers once nobody is there. Each display can follow the common timing or have its own (rest after, dim level, when it turns off), from the OLED care list or `dispctrl oled set --monitor 2 --idle-minutes 10`; `common` puts a value back.
- A Brightness section for the full quick panel: simple mode's sliders - all displays, then each one - as a section, to show in place of Unison brightness and Displays. Hidden until switched on from the Quick panel page.
- A guided mapper for all monitor brands: scans list new codes, watching saves observations for contributions, and mapped choices, sliders and buttons appear on Displays. Mapping values have individual name fields and model/brand/all scopes.
- Shortcuts for the four arrangements Win+P offers - extend, duplicate, PC screen only, second screen only - on Ctrl+Alt+1 to 4. Offered switched off, because each one reconfigures the display stack and a mistyped digit is an expensive accident.
- Shortcuts for the rest of what DispCtrl does: follow the room's light, software dimming, monitor volume and mute, HDR, variable refresh rate, make a display the main one, put windows back, and open new windows on the display in use.
- Shortcuts that run something you wrote: a `dispctrl` command, or any program, file or link, the way a custom quick-panel tile does.
- Experimental LG alternate input switching through NVIDIA, AMD and Intel GPU APIs, enabled only by a model-specific device mapping. Mapping contributions retain the transport metadata; hardware confirmation is still required.

### Changed
- The app is easier to find your way around. Brightness (unison, the light sensor, night light, dark mode), Screen care (focus mode, OLED protection, keep awake and turning the displays off) and Windows (pinning, gathering, putting back) have pages of their own instead of sharing the Displays page with every display. Each feature and each display shows the settings most people change first, with the rest under "Advanced options". Settings ends with an Advanced section, the quick panel's options are grouped, and the taskbar glass options show only while glass is on.
- Naming a monitor's own controls is much simpler. The Devices page shows one monitor at a time and offers the quickest ways first: Learn a setting (change it with the monitor's buttons and DispCtrl finds the code), borrowing the names a sibling model already has in the library (`dispctrl devices similar`), or naming a code by hand. Its codes are filtered to the ones left to name and grouped into the standard and the manufacturer's own range. A laptop's built-in panel can be marked as OLED or not.
- The Hotkeys page shows shortcuts, features, triggers and Windows' own keys on tabs of their own; choosing what a shortcut does is a group, then the action, and its display is chosen by name.
- The quick panel keeps up with the features it switches: OLED care's section and tile include the third stage and pausing for video, the taskbar tile includes the glass looks, colour and border, and there are new tiles for following the room's light and for the way back (Ctrl+Alt+Backspace). The section that was "Simple brightness" is "Brightness", and the Quick panel page leads with what the panel shows.
- The preset in use is shown in the title bar on every page, with a dot when the displays have moved from it; clicking it switches, applies, saves or starts a preset. When the displays differ from it, one line across the top of the window says so, with Save, Discard and the list of changes. The bar that sat above the engine status on the Displays page is gone.
- The command line has one implementation: the first version's short commands (`brightness -10 --all`, `input "HDMI 1" --display 2`, `nightlight 60 --from 20:00 --to 07:00` and the rest) still work, and now answer like every other command. Presets are commands of their own, `dispctrl preset list|save|apply|diff|delete|desk|launch`.
- Settings that were grouped or named misleadingly say what they hold: the quick panel's options are the icon, size, opening and position, and the mouse wheel; the taskbar glass's "corner rounding" is its blur, and its tint is the colour's strength in every look.
- The download you unzip and run without installing is named `...-portable.zip` (it was `...-desktop.zip`).
- `dispctrl help` is organised by topic - displays, controls, brightness, night light, screen care, windows, taskbar, presets, automation, devices, system, scripting - and `dispctrl help TOPIC`, `dispctrl help COMMAND` or `COMMAND --help` shows one. It now lists every command; several (`ddc reset`, `features get`, `triggers get`, `devices share`) were missing.
- The quick panel's Presets section is a list of presets with an Apply button, and says whether the displays still match the one chosen.
- The Presets page is a list: every preset on its own row, with what it holds, how many of its displays are attached and when it was saved, Apply on the row and the rest in its menu. The preset in use is marked, and its changes are shown under a heading that names it. New preset opens a small dialog instead of a form that sat on the page.
- Features on the Hotkeys page are put together with a form: each step a row - set a monitor control, run a DispCtrl command, open a program, run a script, wait - chosen from a list, moved up and down, removed. Edit as text still writes them as lines, and Save lights only when something has changed.
- The sign-in and lock screen switches moved from Settings to a new Miscellaneous page, with tools beside them: refresh the taskbar, put every display back, and Windows' display and colour settings.
- The Taskbar page can refresh the taskbar: Windows Explorer restarts and DispCtrl applies hiding, glass and opacity again (`dispctrl maintenance restart-explorer`). The Look setting says what Acrylic is: the same blur, with the colours behind about 40% more vivid.
- Checking for updates once a day is on by default; it was off until switched on. It sends the version number and nothing else, never downloads, and is one switch to turn off. A settings file from an earlier version keeps its choice.
- With only one display attached, its card on the Displays page starts open. The quick panel's Quick toggles list starts open on the Quick panel page, and a quick panel section switched on there arrives unfolded.
- A preset can leave parts of the desk alone. "What it restores", in New preset and on each preset's menu, ticks layout, brightness, night light, wallpaper, monitor controls, taskbar and windows; anything unticked is not touched when it applies and never shows as a change, and the preset's row says what it leaves alone (`dispctrl preset set NAME --skip brightness`). Brightness moved by the room's light no longer counts as a change, and the change notice can be switched off on the Presets page.
- Now and then, from the fifth time the window is opened, a one-line banner asks for a star on GitHub or a donation. Either ends it; closed, it asks once more after ninety days, then never.
- The documentation is reorganised: the README is a front page, with every feature page by page in `docs/FEATURES.md` and every stored setting, its default and what it does in `docs/SETTINGS.md`.
- The tray icon's right-click menu no longer offers Quick panel, which a left click already opens, or Close the app; Exit DispCtrl remains.

### Fixed
- Identify's numbers no longer jump to the main display a moment after appearing: "Open new windows on the display in use" was moving them. The numbers also no longer take focus.
- Closing the support banner without a star or a donation plays out a little, the dots typing themselves out, before it fades.
- `build.sh test` passes on Linux, and CI now runs it there for every change.
- Discard on the preset banner now clears it. The app compared the desk with its settings from before the preset was applied, and counted differences no apply can put right: a layout that needs an unplugged display, a wallpaper whose file has gone, and a display's name, which the app itself rewrites.
- `dispctrl awake set --mode on` and `--mode off` work, as the help said they would.
- A pinned DispCtrl on the taskbar no longer shows a solid square behind its icon. The icon file had one 256 px image, which Explorer shrank badly for pinned items; it now carries every size Windows asks for with real transparency. The Store package gains the target-size "unplated" tile images the taskbar looks for, and a resources.pri that merges the app's own resources with them.
- A new quick panel opens with only the brightness sections unfolded; every other section and each display's own block start folded.
- Parsec, spacedesk and other virtual displays, USB display adapters and Miracast are recognised and named as such (they showed as "Unknown").
- With PowerToys FancyZones set to move new windows to their last zone, "Open new windows on the display in use" leaves new windows to it instead of both moving them.
- Settings shows the memory the engine uses on its own (about 16 MB) rather than its working set, which counted the libraries every process shares; About shows the size on disk, DispCtrl itself against the runtimes it carries.
- `DispCtrl.App.exe --page brightness` (or displays, care, windows, taskbar, presets, quickpanel, hotkeys, devices, misc, settings, help, about) opens the window on that page.
- New screenshots, demo and hero image throughout the README and the website.
- Downloads are far smaller: the portable zip is about 46 MB (was 74), the command-line zip 19 MB (was 46) and the Store package 47 MB (was 78). The app, the engine and the command line now share one trimmed copy of .NET. The background engine also uses less memory, about 48 MB instead of 73.
- Importing a preset whose name is longer than a file name may be, when that name is taken, numbers it instead of failing as not a preset.
- Presets: applying one no longer undoes changes made elsewhere while it applies; a monitor whose brightness range is not 0 to 100 can be saved in a preset; very long names and names like CON save; a desk profile no longer applies with no display attached, and is not missed when the displays change while another preset applies; an app rule written as a program's full path matches it.
- The engine wakes about half as often when idle: opening new windows on the display in use now listens only for real windows, and app rules are checked only when there are some.
- The DispCtrl window, left on a monitor that was then unplugged, flickered into view and vanished when opened again or clicked on the taskbar. It now comes back onto a display that is attached.
- A display's night light slider no longer turns into a setting of its own as the page draws it: a display following the common warmth kept following it only until its card was first opened.
- Hotkey action fields now update when the action changes. Slow shortcut actions are serialized off the keyboard pump; volume, mute, contrast and input commands read only the requested control.
- LG mappings require device schema 2 so older builds cannot interpret their wire values as standard DDC. Stale input controls reject writes after their transport mapping changes.

## [0.1.5] - 2026-09-26

### Added
- Pin any window on top - Ctrl+Alt+P, the quick panel or `dispctrl pin`. A coloured border marks it, focus mode leaves it clear, and it steps aside while a film or game is fullscreen in front of it.
- Gather every window onto one display - Ctrl+Alt+G, the quick panel or `dispctrl placement gather` - maximized, fullscreen and minimized windows included. Windows can also go back to a monitor when it returns, and new windows can open on the display you are using.
- OLED care can rest each display on its own while you work on another, keeps a display awake while an app you list is showing on it, and lists every monitor it has seen, connected ones first, so one can be marked OLED even while it is unplugged.
- Leave a display out of unison brightness; turn the mouse wheel over the tray icon to change brightness; switch Windows to dark mode on night light's schedule.
- A guard that stops DispCtrl talking to a monitor whose capabilities read crashed Windows, and a read-only probe for monitors that do not say what they support.
- An opt-in check for new versions (Settings > Updates): off until you switch it on, and never for the Microsoft Store version, which updates itself.
- The installer can install for everybody on the PC as well as just for you.

### Changed
- Focus mode's two pointer switches are one choice: keep the focused window clear, the window under the pointer, or both.
- A settings file written by a newer DispCtrl now loads in an older one: what it cannot read is set aside and kept, instead of every setting going back to its default.
- A display's card fills in at once; its colour profile, which Windows can take seconds to report, arrives on its own.
- The quick panel's new window tiles and Windows section start hidden; add them from the Quick panel page.

### Fixed
- Dragging the unison slider was jumpy and could end a step away from where you let go: the link to Windows' brightness took the slider's own changes for the brightness keys.
- OLED care's rest and Turn off displays now cover the taskbar too. It could come up over them and stay undimmed.
- Scrolling an expanded display card to the bottom looped and never got there.
- A display's wallpaper preview stayed empty when its wallpaper file had been replaced - ASUS OLED Shifter does this every few minutes. It now falls back to Windows' own copy of that display's wallpaper.
- With Stay active on, OLED care's dimming never started.
- Pinning refused a maximized window on a display whose taskbar DispCtrl hides, calling it fullscreen.

## [0.1.4] - 2026-09-25

These changes also cover 0.1.3, whose entries were not split out.

### Changed
- A toggle in the app or quick panel now reaches the engine in about 10 ms instead of 135, and a brightness slider reaches the monitor as it moves instead of waiting until you stop.
- The engine starts and takes effect sooner at sign-in: it answers the app and command line almost at once, no longer waits for OLED care's overlays, the brightness keys' listener or the monitor history before carrying on, and applies taskbar glass and hiding the moment Explorer's taskbar appears.
- Downloads are about 30% smaller (the desktop zip is ~74 MB, was ~104): parts of the Windows App SDK that DispCtrl never uses are no longer shipped.
- Beta and test builds show their version in the title, and monitor shares include the version they came from.
- Stable builds no longer log routine activity, only problems.
- Releases can update and commit the version automatically before tagging. A test channel creates numbered GitHub prereleases alongside beta and stable releases; packaging scripts use the project version by default.
- With one display connected, unison steps aside: the quick panel's unison section shows that display's own brightness, simple view drops "All displays", and the Displays page says why its unison slider is idle. Display mode and "Multiple displays" are greyed until a second display is connected. Nothing is switched off, and unison carries on from where it was left when another display arrives.
- Replace Windows brightness no longer pulls the laptop's screen back inside its calibrated range while it is the only display, so the brightness keys reach the whole range.

### Fixed
- The quick panel could open on another display, or halfway along the screen wherever the pointer was. It now always opens beside the notification area, like Windows' own flyouts.
- The quick panel opened with a grey background until clicked, and could be seen sliding under a translucent taskbar.
- Unplugging the main monitor left the taskbar in a mess: the laptop's screen became the primary display, and DispCtrl kept trying to hide a taskbar Windows always puts back, and took the whole screen for windows under it. A display set to hide its taskbar now leaves it to Windows while it is the primary display, and hides it again once it is not.
- A monitor plugged in or reconnected often showed no brightness or controls until Rescan was pressed, because it was read before it could answer. DispCtrl now waits for the displays to settle, reads a new monitor again a few seconds later if it did not answer, and updates the Displays page, its arrangement diagram and an open quick panel by itself.
- A loose cable that drops a monitor for a moment no longer sets off a round of taskbar and brightness changes: a change counts only once the displays have stayed the same for a second and a half.
- Night light reaches a newly connected monitor straight away, instead of up to 20 seconds later.
- `build clean` failed with "access denied" on the taskbar glass helper, because Windows Explorer had loaded it straight from the build folder. The engine now gives Explorer its own copy in DispCtrl's data folder, and clean skips, with a note, a helper an older engine left loaded until Explorer restarts.
- Taskbar glass did nothing after switching between the installer or a zip and the Microsoft Store version until Windows Explorer was restarted: Explorer still held the other build's glass helper. The engine now retires that helper and attaches its own, as long as no other DispCtrl engine is running.
- Following the room's light kept the engine waking about four times a second in a still room, because a light sensor's readings jitter. Readings too small to count are now noted without waking it at all.

## [0.1.2] - 2026-09-25

### Added
- `dispctrl ambient get|set|reset|capture|forget`: every setting for following the room's light, what the sensor reads now, and calibration - cover the sensor and `capture --as dark`, light it and `capture --as bright`.
- The tray icon's right-click menu can switch the quick panel between simple and full view, stop the engine, close the app while the engine carries on, or exit DispCtrl altogether - the app, the quick panel and the engine.
- Following the room's light learns: a unison level set by hand while it follows - the slider, a hotkey, the brightness keys - is remembered for that light. `dispctrl ambient forget` starts again.

### Changed
- The Displays page's light sensor section sits last under Unison brightness, with its options folded under a chevron: the sensor, the levels for a dark room and bright light, calibration ("This is dark", "This is bright") with the sensor's reading shown live, and learning from your adjustments. Switching it on switches unison brightness on too, since that is what it drives.

### Fixed
- In the Microsoft Store version, "open the log" and the other folder buttons opened nothing, and no DispCtrl folder appeared in `%LOCALAPPDATA%`. The data was always kept, in the app's own folder under `%LOCALAPPDATA%\Packages`; the app now uses and shows that real folder.
- Taskbar glass did nothing in the Microsoft Store version: Windows would not let Explorer load its helper from the Store's install folder.
- Following the room's light barely moved when the sensor was covered, never reached full brightness under a torch, and was slow to react: it stopped partway after the sensor's last reading, and a light that kept changing kept restarting its wait. It now reaches the new level within a few seconds, walks there in steps rather than jumping, and still ignores a hand passing the sensor.
- Following the room's light no longer fights Replace Windows brightness over the built-in panel.
- The simple quick panel lines up: each name, slider and value on the same edges as the title.
- The quick panel takes focus when it opens from the tray icon, instead of opening inactive with a grey background until clicked.

## [0.1.1] - 2026-09-24

### Added
- **Follow the room's light**: unison brightness can follow an ambient light sensor, from a level for a dark room to one for bright light, smoothed so passing shadows do not move it. Under Unison brightness, with the sensor to follow.
- **Undo the way back**: Settings > Undo the way back, or `dispctrl restore undo`, switches back on what Ctrl+Alt+Backspace last turned off - each monitor's taskbar hiding and dimming, night light, focus, OLED care and taskbar opacity. `dispctrl restore now` does what the shortcut does.
- **Ctrl+Alt+Backspace puts every display back**: it undoes displays off, dimming, night light, taskbar hiding and opacity, keeping every other setting. On by default, and the Hotkeys page asks before it is switched off or removed.
- The device library counts how many times each model has been contributed, by issue number only; the catalogue shows it.
- Turn off displays shows Keep awake and Stay active beside it - the same switches as everywhere else, so what is on stays on and chat apps keep showing you as available while the displays are off.
- Turn off displays can also turn each display's real backlight down, at 90% darkness and more, and puts it back as the display wakes.
- Turn off displays can lock the computer when they wake: whoever brings them back meets the lock screen, however they do it.
- Turn off displays parks the pointer in a corner of a display that went off, and puts it back afterwards unless the mouse woke them.
- **Turn off displays**: blacks every display out, as if switched off, while the computer keeps running and stays awake. Ctrl+Alt+L, the quick panel tile, the Keep awake section, or `dispctrl awake displays-off --enabled on`; any input brings them back. Choose how dark, which displays (all, all but the main one, all but the one with the pointer or the active window, or only the main one), the delay, and whether only the pointer wakes a display.
- **By resolution** on the arrangement: draws each display by its pixel count, as Windows does, instead of by its real size.

### Changed
- The Devices page offers to **contribute** a monitor rather than share it, and the command is `dispctrl devices contribute`; `devices share` still works.
- New installs start with the author's own setup: unison and Replace Windows brightness on, focus mode following the mouse with each display keeping its own window, OLED care's two-stage rest, taskbar glass and reveal timing, the quick panel's layout, and no logging.
- Only one DispCtrl window runs at a time: opening it again brings the running one forward, restored if it was minimized.
- The engine starts whenever DispCtrl is opened, and a Store install starts it at sign-in by default.
- The quick panel opens in simple mode on a new install; the dot in its title bar switches to the full panel.

### Fixed
- Focus mode kept a vanished notification (Teams, for one) clear in the dim until the next click.
- Switching displays off from the quick panel left the tile's tooltip lit on top of the black.
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

[Unreleased]: https://github.com/jesvijonathan/DispCtrl/compare/v0.1.5...HEAD
[0.1.5]: https://github.com/jesvijonathan/DispCtrl/compare/v0.1.4...v0.1.5
[0.1.4]: https://github.com/jesvijonathan/DispCtrl/compare/v0.1.2...v0.1.4
[0.1.2]: https://github.com/jesvijonathan/DispCtrl/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/jesvijonathan/DispCtrl/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/jesvijonathan/DispCtrl/releases/tag/v0.1.0
