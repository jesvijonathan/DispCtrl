# How each feature works

Feature by feature: what each one does on the hardware, why it is shaped the
way it is, and how it was verified. Read the section for the feature you are
about to change, then its entries in [TRAPS.md](TRAPS.md).

## What a monitor will tell you

Three sources, and they answer different questions:

- **EDID** - `EdidReader.Describe` decodes the whole base block: manufacturer
  (expanded through `PnpNames`), product code, build week and year, EDID version
  and checksum, digital link depth and interface, declared size, gamma, DPMS,
  colour encodings, the CIE primaries and white point, every established,
  standard and detailed timing, the range limits, and which descriptor blocks
  are present. `Edid` itself stays small and cached - it is on the engine's
  rescan path. `EdidReader` is the on-demand one.
- **DDC/CI** - `MonitorCapabilities` for the VCP codes, including the read-only
  ones the panel will not let you change but will happily answer: **firmware
  level (0xC9)**, hours in use (0xC0), controller type (0xC8), display
  technology (0xB6), sub-pixel layout (0xB2).
- **Windows** - modes, current signal, HDR, scaling, VRR, colour profile.

Two things are **not** available and must not be invented:

- **Country of manufacture.** The PNP registry records a country of
  *registration* against the three-letter code; the EDID carries none, and a
  panel built in one country by a company registered in another would be
  described wrongly either way.
- **Bit depth and digital interface on EDID 1.3.** Those sub-fields only exist
  from 1.4. Reading them off a 1.3 panel returns whatever the reserved bits
  hold, which is how this Dell first came out as "0-bit, not declared" - a claim
  about the monitor that the monitor never made. `EdidReader.Input` gates on the
  version.

## Detect is not Identify

They sit next to each other on the arrangement surface and are easy to confuse,
because Detect calls Identify when it finishes.

- **Identify** flashes each display's number on it for three seconds. Instant,
  and changes nothing.
- **Detect** re-enumerates the hardware, which is seconds of DDC/CI traffic per
  panel, and is the only one that can find a monitor that is connected but
  switched off. Then it identifies, so you can see what it found.

Windows' own Display settings carries both for the same reason. The labels
cannot express the difference, so both carry tooltips.

## The device library (was: contributing a device record)

```
dispctrl devices list | show | scan | probe | map | unmap | link | definitions | share | validate
```

`docs/DEVICE-LIBRARY.md` is the design. In short: every capabilities read
records the model's codes into `%LOCALAPPDATA%\DispCtrl\devices\history.json`
(`DeviceObserver`, no reads of its own); `devices probe` watches the codes the
standard does not name while the owner uses the monitor's menu; `devices map`
names one in a **definition** - per model (`DEL-A234`), brand (`DEL`) or every
monitor (`*`), layered in that order with `extends` links - and `devices contribute`
opens one prefilled issue with the record and the mappings. The repository is
the backend: `.github/workflows/devices.yml` turns such an issue into a
pull request through `tools/devicecheck intake` (its `intake` job), validates
every change to `devices/` (`check`) and regenerates the index after a merge
(`index`). It runs on Linux. Reviewed definitions live in `devices/BRAND/PRODUCT/`
(`DeviceLayout`; built for thousands of models, one folder each) and ship
beside every executable in that layout; records and the generated index do not
ship. The app's **Devices** page sends the same
`devices.*` requests - it replaced the Collect / View / Submit card.

- **A mapped code is writable only when its definition says so**, and only on a
  monitor that lists it. That is the single, deliberate exception to "never
  write a manufacturer-specific code": somebody wrote it and watched.
- **Record in the calling thread.** History writes on a background task were
  lost whenever a short-lived CLI process exited first.
- **"Unnamed" means not in the MCCS table** (`MonitorCapabilities.IsNamed`), not
  "not in DispCtrl's allow list": firmware level is named, just read-only.
- **A model's definition can carry `panel.technology`** (`DeviceLibrary.Panel`),
  the one fact a built-in panel can be described by: it has no DDC/CI, and
  VCP `0xB6` is the only place any display says it is OLED. The user's `IsOled`
  wins; the app then prefers `0xB6` to the library and persists the result as
  `OledDetected`. The engine cannot tell "reported LCD" from "never asked", so
  it takes `OledDetected` or the library, read once per mask
  (`FocusService.Mask.LibraryOled`), never per frame.
  Never on a brand or `*` - a maker ships both kinds. `devices panel` sets it.
- `dispctrl contribute` still works for the Markdown record alone; the docs
  point at `devices contribute`.

## Hotkeys

Global shortcuts live in `settings.Hotkeys` and are registered by the **engine**,
which also carries them out: 45 actions, from unison and night light to focus,
OLED care, keep awake, taskbar, contrast, pinning, gathering, the quick panel,
the four Win+P arrangements (`DisplayMode`, carrying `Mode`), and two that run
whatever somebody wrote (`RunCommand` through `dispctrl.exe`, `OpenProgram`
through the shell, both carrying `Command`) — the same two kinds a custom quick
panel tile has, run the same way so the words mean one thing. A new desk is
offered twenty-four defaults (`Hotkey.OfferDefaults`), eight enabled: Ctrl+Alt with
Page Up/Down, N, D, L, Backspace, P (pin) and G (gather). Sixteen more are configured but disabled - the window moves (Ctrl+Alt+], [ and S, version 7) among them -
including Ctrl+Alt+1 to 4 for extend, duplicate, PC screen only and second
screen only — off, because each reconfigures the display stack and a mistyped
digit is expensive. The defaults are
versioned: upgrades offer newly added actions disabled once, preserving existing
bindings and removals. **`OfferDefaults` judges display-mode defaults by action and
`Mode`**, or the four arrangements would collapse into one offer. Other actions ignore stale mode fields. Never use the arrows, which Intel drivers take for screen
rotation. The page and `dispctrl hotkeys reset` restore
them. **Unison hotkeys write the hardware themselves** (`ApplyUnison`): they
used to save the level and nothing else, so with the app closed no display
moved. **Topology, gather, contrast, volume, mute, input and custom-launch actions run
through one bounded worker queue off the pump**. It runs only while work is pending,
serializes slow actions and drops pending work on Restore. Targeted VCP actions
read only their own control. Registered
because a panel that registered them would lose them on closing — the opposite
of what a global shortcut is for. The Hotkeys page only edits the list.

**A ComboBox on the Hotkeys page writes its index back as it is realised,
collapsed or not**, so `SelectedModeIndex` refuses a write unless the action is
the one that owns it; without that, choosing a brightness shortcut recorded an
arrangement on it.

`RegisterHotKey` delivers `WM_HOTKEY` to the queue of the thread that
registered it, and the engine polls rather than pumping, so `HotkeyService` owns
a thread with a message pump of its own. Registration **and** unregistration
must both happen on that thread; releasing from elsewhere silently does nothing
and leaves the combination held until the process exits. `MOD_NOREPEAT` is set,
or holding a shortcut walks brightness to an end stop.

Autostart and the Start menu entry are managed by `tools/DispCtrl.ps1`
(`-Install`, `-Uninstall`, `-Status`, `-AddShortcut`, `-RemoveLegacy`). It is
interim; MSIX `windows.startupTask` replaces it.

## Quick panel (the tray icon)

The **engine** owns the notification-area icon (`Shell/TrayIconService`), for
the same reason it owns hotkeys: it is the resident process. A click sets the
named event `Local\DispCtrl.QuickPanel.Show`; whichever `DispCtrl.App` holds
`Local\DispCtrl.QuickPanel.Alive` shows `QuickPanelWindow`, or the engine starts
`DispCtrl.App.exe --panel` from the path the app records in `app.path`. That
event carries no data; the control broker separately handles structured requests.

- **What the panel shows is four ordered id lists** in `settings.Global.QuickPanel`
  (sections, quick toggles, rows per display, switches per display), plus
  `customTiles` people make. `QuickPanelCatalog` is the only place an id's
  label, glyph and hover text live; `Normalise` drops unknown ids and appends
  new ones **hidden**. Never rename an id. `docs/developer/QUICK-PANEL.md` is the
  contributor's guide: adding a tile is one catalogue line plus one line in
  `QuickPanelContent.Registry.cs`.
- `QuickPanelSettings.Reorder` **hides** whatever it is not handed. The page's
  remove button works by leaving an item out; keeping its old visibility made
  that button do nothing, and a `DispCtrl.Hardware.Checks` assertion caught it.
- **The engine passes the foreground on** (`AllowSetForegroundWindow(ASFW_ANY)`)
  before signalling the panel, from a tray click or a hotkey. Without it the
  panel's `Activate()` fails quietly, the panel is never active, never
  deactivated, and a click elsewhere does not close it. When it still is not
  foreground (a CLI summons, an older engine), `WatchOutsideIfInactive` polls
  the mouse buttons at 50 ms until a click lands outside or the panel activates.
  That permission did not always survive the trip, and the panel opened
  inactive (grey backdrop, no focus) until clicked. `TakeForeground` sends one
  empty mouse input, which makes the panel's process the last to receive input,
  and calls `SetForegroundWindow` - PowerToys' workaround (#1282) - on summons
  only, and again once uncloaked.
- **The icon's right-click menu**: Open DispCtrl, Simple view
  (`QuickPanel.Simple`, the title bar's dot), Keep on the taskbar, Hide this
  icon, Stop the engine, Exit DispCtrl. No "Quick panel" (a left click is
  that) and no "Close the app" (the owner's call, 2026-10-02). Stop sets
  `Local\DispCtrl.Engine.Stop`, never ends the process - the engine must
  unwind to put taskbars back. Exit sets `Local\DispCtrl.App.Quit`
  (`QuickPanelSignal.RequestQuit`), which the app's listener turns into a
  flushed save and `Exit()`, then stops the engine.
- The tray icon **toggles**. Clicking it while the panel is open takes focus
  first, closing the panel, and then asks for it again - so a summons within
  500 ms of a focus-loss close is treated as the same click.
- **Unison off leaves displays alone; unison on snaps them back** to their
  remembered baselines at the level the slider was left (`UnisonResume.Enable`).
  Switching it on used to record the current levels as new baselines and put the
  slider back to 100, which shrank the full scale every off-on cycle.
- **"Replace Windows brightness"** (`UnisonFollowsWindows`): the engine's
  `Color/WindowsBrightnessBridge` listens for `WmiMonitorBrightnessEvent` - the
  Quick Settings slider and the brightness keys, which only ever move the
  built-in panel - and reads the panel's value back as a unison level through
  the panel's own range (`UnisonResume.LevelFor`), then moves every other
  display there. The panel is driven like any other display; the engine writes
  it only to pull it back inside its range, after the others have moved. Events
  equal to where unison already has the panel are dropped (the app's own slider
  and that correction cause them), and settings are re-read from disk per burst,
  because the file watcher lags the event. See "Unison calibration" below.
- The panel rises from behind the taskbar and sinks back into it - see "Quick
  panel window" below. It obeys Windows' "Animation effects" setting
  (`UISettings.AnimationsEnabled`) and `quickPanel.animate`.
- Rows are **built in code** (`QuickPanelContent`): value set before handler,
  toggles on `Click`. Every row subscribes to the view model and is released on
  rebuild - the view model outlives the rows. Rows stay attached while the
  panel is hidden, so the next opening does not rebuild them.
- Support flags (HDR, scaling, brightness, the monitor's controls) arrive
  seconds after start. Rows gated on them rebuild when the gate **changes**, not
  on every notification, or a drag is torn out from under the pointer.
- Per-display warmth only applies with night light on, **not** in unison and
  **not** following Windows (`NightLightSettings.PerDisplayApplies`). Windows
  has one strength; the Displays page used to offer dead per-display sliders.
- The tray glyph is drawn from Segoe Fluent at `SM_CXSMICON`, white on a dark
  taskbar and black on a light one (`WindowsTheme.IsShellDark` - the shell's
  value, not the apps'). "Keep it on the taskbar" writes
  `HKCU\Control Panel\NotifyIconSettings\<id>\IsPromoted`, which Explorer
  applies live. `Shell_NotifyIconGetRect` cannot tell you whether it worked: a
  promoted icon and the `^` it replaced report the same rectangle. Screenshot.

## Triggers

`settings.Triggers`: an event and a custom feature to run. `TriggerService`
(engine) hears displays arriving and leaving from the settled change, and
lock, unlock and resume from `Shell.SessionEvents`, which the focus thread's
window raises - one session registration in the engine, not one per service.
The rest (app in front, away, mains power, a time) is one thread that looks
only as often as the triggers in use need and otherwise waits on settings.
Edge-triggered: the first look records state and runs nothing. A feature
still running is not started again. Verified: an at-time trigger fired its
feature through the running engine at hh:mm:00.13.

## Turn off displays

"Off" to the eye: the overlay OLED care rests with, at `DisplaysOffPercent`
(100, black), over every display - not only OLED ones, because this is about
the person leaving, not panel wear. The monitors, their brightness and every
window stay as they are, so nothing re-lays out when they come back. It lives
under Keep awake (`AwakeSettings.DisplaysOff*`), in the Displays page's Keep
awake section and the Keep awake tile's flyout, and has its own tile and a
default shortcut, Ctrl+Alt+L (Win+L is Windows'). `DisplaysOffUtc` is a
request, not a state: `FocusService` waits `DisplaysOffDelaySeconds`, picks
the displays once (`DisplaysOffTarget`, so "all but the one with the pointer"
means where the pointer is then), and writes the request back to null when
every one has woken, so the switch goes off by itself. Any input wakes them
after the manual-rest grace, or with `DisplaysOffWakeOnPointer` only the
pointer moving on each. Staying awake or active meanwhile is Keep awake's and Stay active's own
switches, shown again beside it (the owner's call: one setting, synced
everywhere, rather than a copy that applies only while the displays are off). `DisplaysOffLockOnWake` locks the session when they
come back, and **any** end locks - the first display woken, the shortcut, the
panel, the CLI, Ctrl+Alt+Backspace - or pressing Ctrl+Alt+L would be the way
round it. Captured when they go off, so changing it meanwhile does not unlock a
session already under way. Not exercised live on the dev desk: it locks it. The hotkey defaults went to version 3, and
Ctrl+Alt+L is the one upgrade offered switched on - the owner asked for it,
and it never lands on a combination something else holds. Verified here: both
displays at alpha 255 after the delay, back on when switched off.

## Reports, not reporters

The intake records each share's **issue number** against the model
(`devices/BRAND/PRODUCT/reports.json`, `{"issues": [4, 5]}`), and the index
and catalogue count them: how many owners have confirmed a model, with no name
in the repository. Commits are made as `dispctrl-intake` and titled by number;
the pull request body names only the issue. A repeat report of a known model
now makes a small pull request, which is the point - it is a confirmation.
Not shipped, like records.

**A share only adds.** The intake never changes a code, panel or link the
library already has, leaves out a share's maker-wide and every-monitor
definitions, and lands new codes read-only. A share with nothing to review
is committed straight to the default branch, after a gate: only model data
paths, no deletions, `validate` and `guard` clean, records shaped as DispCtrl
writes them (no links, no HTML beyond details/summary, a size cap, the
footer). Anything flagged becomes a pull request instead. `devicecheck guard BEFORE AFTER` lists every change
to reviewed data, and the check job runs it from the base branch's copy of
the tool: fatal for outside pull requests, a warning for the owner's.
Automatic intake (issue events, the Monday sweep) skips bots, accounts under
14 days and authors with more than three shares open.

Stay active's nudge (`PowerService.LastNudgeTick`) is input, so "any input
wakes the turned-off displays" ignores input within half a second of it.

## Defaults are the owner's desk

The shipped defaults were taken from the owner's own settings on 2026-09-24:
everything global, including values inside switches that are off, and the
quick panel's order and visibility. Not taken: anything per monitor,
calibration, bookkeeping, and one-off state such as the unison level. Presets
starts folded through `QuickPanelSettings.FoldedByDefault`, never through a
default `Collapsed` list, which records only departures from it.

## Ambient light

`Color/AmbientSync` follows a Windows light sensor (`AmbientSensors`; this
desk has none, so it is untested on hardware here). The first version
was reported working badly on a laptop that has one: covering the sensor
barely dimmed, a torch never reached the top, and it was slow. Two bugs, both
now checked in DispCtrl.Core.Checks:

- **A sensor reports only on change.** The old filter averaged once per
  reading, so when the light stopped changing the average stopped partway
  (a covered sensor settled at ~3/5 of the old light). `AmbientFilter` keeps
  the newest reading as the room's light, and `AmbientSync` runs a 250 ms clock
  only while a change is being weighed or the level is walking. A reading inside the threshold (`AmbientFilter.Ignores`)
  is observed in the sensor callback and starts no clock: waiting for the
  average to close on each jittery report ran it ~3.8 times a second in a
  still room.
- **Its wait was a debounce**, restarted by every reading, so a flickering
  torch never settled. Now Android's shape (`AutomaticBrightnessController`):
  in log lux, +15% held 1.5 s brightens, -20% held 3 s darkens, the smoothed
  value and the newest reading must agree (or a deep shadow's slow recovery
  counts as darkening), and 2 lux near darkness is noise.

The level then walks there in four steps and is saved once, at the end;
`AmbientSync.Writing` tells `WindowsBrightnessBridge` to ignore the built-in
panel's events meanwhile, or each step would read as a brightness key and be
saved, and this service would then learn it as somebody's correction.
`AmbientCurve` interpolates in log lux between `DarkLux`
and `BrightLux` (captured from the sensor: `dispctrl ambient capture --as
dark|bright`) through **learned points**: any unison change while following
(slider, hotkey, brightness keys through the bridge) is a correction, learned
for that light, newest winning, kept monotonic (wluma's idea). Report thresholds are
5% and 1 lux (both must be met). Windows' own adaptive brightness is switched
off when this is on, by the app and by the engine: two hands on one control fight.

The Displays page's section sits after "Each display now", its options
folded by a chevron `ToggleButton` over cards with bound Visibility - not a
nested expander (see WinUI traps). Switching it on switches unison on (the
engine follows only with both). The switch stays enabled while on even with no
sensor (`AmbientToggleEnabled`), or one left on could never be turned off. The
lux reading refreshes every 2 s only while the options are open and the window
visible.

**Monitors do not share their sensors.** MCCS `0x66` is only on/off
(ddcutil: 01 disabled, 02 enabled); no VCP code carries a reading. The Dell
here lists `0x66` and answers `A1`. A monitor sensor reaches DispCtrl only if
the monitor exposes it as a USB HID sensor, when it appears in the sensor list.

## The way back: Ctrl+Alt+Backspace

`RestoreDisplays` (`DispCtrlSettings.RestoreVisibility`) undoes everything that
can darken, tint or hide a screen - displays off, OLED rest and idle care,
focus, night light, software dimming, taskbar hiding and opacity - and nothing
else. It is the shortcut to give someone looking at a black screen, so it is on
by default (hotkey defaults version 4, offered switched on), the README names it,
and the Hotkeys page confirms before it is switched off or removed
(`HotkeyViewModel.SafetyNetOffRequested`). What it switches off is recorded in
`Global.BeforeRestore` (`VisibilitySnapshot`) and put back by
`UndoRestoreVisibility` - Settings > Undo the way back, or `dispctrl restore
undo`; `restore now` is the hotkey's twin. A press with nothing left to switch
off keeps an earlier record under ten minutes old, so pressing twice never
loses the first press's work, while an old one is dropped rather than undone
over later choices; displays off and a screen rest are moments, not settings, and are not
recorded. Turn off displays also parks the
pointer in a corner of a display it blacked out and puts it back when they come
on again, unless the mouse has moved it.

## Windows on top, gathering, putting back

From PowerToys' Always On Top and FancyZones, only the parts where knowing
about displays helps (`docs/design/FEATURES.md`, "Compared with PowerToys").

- **A pin is the window's own state**: `WS_EX_TOPMOST` plus the window property
  `DispCtrl.Pinned` (`Display/Placement/WindowPins`). Any process pins - the
  hotkey, the panel, `dispctrl pin` - and nothing is stored: the pin lives as
  long as the window. The property is also what stops an unpin taking topmost
  off a window that was on top by itself. Pinning refuses excluded apps,
  fullscreen windows (optional) and windows of elevated processes (UIPI; said,
  not worked around). `Local\DispCtrl.Pins.Changed` tells the engine.
- **The border is four click-through strips**, not one layered window the size
  of the pinned one (a 4K window would be a 33 MB surface for 3 px of colour).
  Regions carry the Windows 11 corner radius; a maximized or work-area-filling
  window gets its border inside, and strips are clipped to the window's display.
  `Engine/Placement/PinService` follows each window with WinEvent hooks
  **scoped to its own thread**, never a global location hook, plus a settle pass
  150 ms after the last event. Nothing hooked while nothing is pinned.
- **T2**: `FocusService` cuts pinned windows out of focus dimming
  (`Pin.ClearInFocus`, on) and, only if asked, out of OLED idle dimming
  (`ClearInOledCare`, off: a window sitting still for hours is what burns in).
  Never out of a manual rest or displays off.
- **Moving a window is move, then size** (`WindowMover.Place`): sized in the
  same call, a per-monitor-aware app's own DPI resize landed on top. Maximized
  and minimized windows go through the placement, whose rectangles are
  **workspace** coordinates - offset by the work area of the display they are
  on; with a bottom taskbar the two agree, which is how getting it wrong hides.
  Hung windows are skipped (a synchronous move would wait on them).
- **Putting back (Z1)** snapshots every window relative to its display after
  foreground, move/size-end and minimize events and every 30 s, and **only
  while the display fingerprint is the settled one**, so the moment of change
  is never recorded as where things were. A departed display's windows are
  parked with where Windows put them; on return, each still exactly there goes
  back (`WindowGeometry.Untouched`). **Windows 11's own window memory is
  left alone and works beside it.** Its switch is
  `RestorePreviousStateRecalcBehavior` (0 = on), and writing it does not take
  effect live - an unplug with it at 1 still had Windows put every visible
  window back within the 1.5 s this waits. So DispCtrl never switches it off
  (development builds did; since the value is not read live, that only left it
  set to change later);
  `Reconcile` only hands it back where `TookOverWindowsMemory` says an earlier
  build took it. Verified by unplugging the Dell twice: Windows returned the
  visible windows, DispCtrl saw them already home and left them ("moved since
  it was parked; already back where it was"), and put back the minimized ones,
  whose restore positions Windows leaves on the laptop. The per-window lines
  are diagnostic builds only.
- **New windows (Z4)** is a window-shown hook held only while the option is on;
  windows present when it came on are never new, and a window is judged 250 ms
  after it shows, once the app has placed it itself. Verified here.

## Where the window features meet the others

Every pair was walked through; these are the ones that needed code.

- **A pin's hole includes its border.** Cut to the frame alone, focus dimming
  drew over the border strips, which sit outside the window.
- **Pins step aside for fullscreen** (`Pin.StepAsideForFullscreen`, on). A
  content-fullscreen window in front (`IsContentFullscreen`, not a maximized
  one) puts the pins on its display `NOTOPMOST`, just below it, borders hidden;
  they go back on top when it leaves. Watched by one global foreground hook
  and a location hook on the **front window's thread only**, while anything is
  pinned. Pins are restored on shutdown, stepped aside or not.
- **Follow re-asserts topmost before it looks at the border.** With the border
  off it returned early, and a window an app had knocked off topmost stayed off.
- **PinService's hooks are five narrow ranges per thread**, not
  `EVENT_MIN..MAX`: that range delivered every name, value and state change the
  app raised. `Changed` fires only when a rectangle really moved.
- **Gathering a maximized window** restores it onto the target through the
  placement (`SW_SHOWNOACTIVATE`), then maximizes it there; borderless
  fullscreen is moved, then sized to the target's bounds. Both used to stay
  put, which read as "gather only moves the active window".
- **The tray wheel does nothing while unison is being calibrated**: the
  walkthrough owns the level.
- **"Fullscreen" is content fullscreen everywhere** (`AppWindows.IsFullscreen`):
  "Never pin a fullscreen window" used the bare fill test and refused a
  maximized window on the laptop, whose taskbar DispCtrl hides.
- **A minimized window keeps `WPF_RESTORETOMAXIMIZED` when it is carried**, or
  it comes back normal-sized on the new display.
- **New-window placement ignores `WS_CHILD` at once**: every control a browser
  shows raised the hook, and the seen set filled with them.
- **The quick panel re-reads what changes without a save**: the pinned list
  follows `PinnedWindows` (re-read on every summons), and `Choices` takes a
  property to watch - built once and kept while hidden, both showed what was
  true at the first opening.
- **Per-display rest keeps the ordinary idle state empty** while it runs, or
  switching it off found a rest dated from before and dimmed at once.
- **Focus mode's pointer options are one choice** (`FocusSettings.Clear`,
  `--keep-clear focused|pointer|both`), over the two stored switches.
  Follow the mouse alone clears the pointer's window *instead of* the focused
  one; Keep hovered clear clears it *as well*. Both switches on (the shipped
  default) behaves as Both, so the setter leaves Follow alone for it.
- **OLED care per display** (`OledCare.PerDisplayActivity`, off): each mask
  keeps its own clock (`DisplayActivity`). Input counts for the display holding
  half or more of the front window (or the pointer's, with nothing in front),
  and a moving pointer counts where it is as well; pointer-only for a display
  set to wake on the pointer's return. Stay active's nudge counts for none.
  `ExcludedApps`: a display showing one of them (visible, not minimized, mostly
  on it, looked up once a second with a PID-cached process name) never rests.
  The Displays page lists every monitor seen (`MonitorSettings.LastSeenUtc`,
  stamped by the engine at start and on each settled change, not reset),
  connected first. Verified live: a minute of use on the Dell only rested the
  laptop at 58% while the Dell stayed lit, the pointer on the laptop woke it,
  and `pwsh` on the exception list with a window on the laptop kept it lit
  (control run without it: rested).
- **OLED care's third stage and a display's own timing.** Stage 3 is black
  (`DimAtIdle` 100) with the backlight down through `DisplaysOffBacklight`,
  put back when the rest ends unless Displays off holds the same display.
  "Keep the computer active" off sets `PowerService.OledRestAllowsSleep`, which
  drops the execution-state hold and Stay active's nudge while a display is at
  that stage; the focus thread writes it every tick and a change wakes the
  power loop. A display's own stages are `MonitorSettings.OledCare`, resolved by
  `OledCareSettings.For` - only the stages and levels; on/off, fullscreen,
  per-display activity, exceptions and keep-active stay common. Not exercised
  live: it needs minutes of no input on a desk that is in use.
- **Idle cost, measured in cycles** (scratch desk with OLED care, Stay active
  and taskbar hiding on): all window features off ~200 Mcycles/min, all on
  ~250. The exception list was the whole of a +130 first: it described every
  window once a second; `AppWindows.ShowingFrames` tests visibility and the
  cached process name before anything else. Placement's 30 s snapshot reuses
  the display list while the cheap signature holds and re-reads only work
  areas. Pinning, the tray wheel, per-display rest and the placement hooks
  measured as nothing at idle. Memory did not move (~77 MB working set).
- **Nothing about windows shows in a new quick panel**: the Windows section
  and the pin, gather, put-back and new-window tiles are all hidden by default
  (the owner's call), and switched on from the Quick panel page.

## The DDC/CI guard and the probe

- **`DdcGuard` marks each capabilities read on disk, flushed**, and each
  process's first conversation with each monitor (Dxva2's high-level calls read
  the string themselves). A mark from an earlier boot (boot time from the tick
  count, two minutes apart) is a read Windows went down during: that monitor is
  blocked in `Global.DdcGuard.Blocked` and `DdcChannel.With` refuses every
  conversation with it until `ddc allow` / the Displays page. A mark from this
  boot whose process is gone was a kill, and is deleted. Verified by planting a
  mark: the Dell was blocked at start, read as unsupported, and allowed again.
- **The block list is read at most once a second, and only when settings.json
  moved.** Its "last checked" started at `long.MinValue`, and `now - MinValue`
  overflows negative - read as "checked a moment ago" forever, so the list never
  loaded and nothing was ever blocked. DispCtrl.Control.Checks now asks on the first call.
- **The probe reads, never writes**, every named code except MCCS's commands.
  A continuous answer with maximum 0 or the 0xFFFF filler is dropped: this Dell
  answers black levels, gamma and colour temperature that way, and on a monitor
  using probed codes they would have been sliders writing into nothing. Saved,
  the codes stand in for a missing capabilities string as `type(probed)`, which
  never reaches the device library.

## The tray wheel and the theme schedule

- **Windows sends a notification icon no wheel**, so `Shell/TrayWheel` installs
  a low-level mouse hook when the icon reports the pointer (`WM_MOUSEMOVE` via
  the tray callback) and removes it when the pointer leaves the icon's
  `Shell_NotifyIconGetRect`. The hook only counts notches (Windows drops a
  low-level hook that makes it wait ~300 ms); a worker applies them, unison
  when it covers the target. Verified: one notch 47→52, one back to 47.
- **Dark mode on the schedule** is edge-triggered (`NightLightSettings.ThemeDue`):
  once per boundary crossed, remembered in `ThemeAppliedUtc`, so a theme chosen
  by hand holds until the next boundary and a restart undoes nothing. Changing
  the hours or switching it on clears the record and applies at the next look.

## Updates

- **Store installs are updated by the Store**; nothing in DispCtrl checks for
  them (`StartupIntegration.IsPackaged`), and "Check for updates" opens the
  Store's updates page.
- **Installer and zip installs: opt-in** (the owner's rule: no network request
  DispCtrl was not asked to make). `Global.Updates.CheckAutomatically` is off by
  default and off again after Reset all; the button is an explicit ask. A check
  (`Control/UpdateCheck`) is one anonymous GET of the latest release - the
  version in the User-Agent, nothing else sent - and never downloads: a newer
  release is announced (Settings page notice, one footer line per run) with a
  link that is always under the project's own releases, whatever the answer
  says. Prereleases are never GitHub's "latest". "Not now" hides that release,
  not later ones. The app looks 30 s after start and every 6 h, asking only
  when a day has passed (`UpdateSettings.Due`). `dispctrl update
  check|get|set|skip|reset`; the CLI prints the link, never opens a browser.
- An update installs over the old one: the installer stops the engine
  gracefully and settings carry forward; a downgrade keeps working too (see
  "Settings file").
