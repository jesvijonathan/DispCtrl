# DispCtrl

Per-monitor display management for Windows 11. A resident engine plus an
on-demand WinUI 3 panel.

This file is the handover: architecture, the traps already paid for, the
commands that work, and how to verify a change on real hardware. `docs/design/ROADMAP.md`
carries the longer reasoning behind individual decisions — this is the operating
manual.

---

## The machine it is built against

Two displays, and nearly every hard-won lesson below comes from one of them.

| | Internal | External |
|---|---|---|
| Panel | ASUS OLED, 2880x1800 @ 90 Hz | DELL U2424H, 1920x1080 @ 120 Hz |
| Scaling | 200% (192 DPI) | 100% (96 DPI) |
| True density | 242 PPI, 302x189 mm, 14.0" | 93 PPI, 527x296 mm, 23.8" |
| Position | secondary, right of the Dell | **primary**, at 0,0 |
| Brightness | WMI (no DDC/CI) | DDC/CI |
| Taskbar | hidden by DispCtrl | Windows' own auto-hide |
| DDC/CI | none — built-in panels have no channel | 37 VCP controls, 11 offered |

Token identities: `SDC-4154-0A1B2C3D` (internal), `DEL-A234-9XYZ7K1` (Dell).

**Leave the desk as you found it.** Brightness 68% internal / 62% Dell, night
light off, Dell contrast 75. Several bugs in this project's history were caused
by tests that changed hardware state and did not restore it.

---

## Architecture

```
DispCtrl.Core      no hardware writes. Display enumeration, EDID, settings,
                presets (model + diff), gamma ramps, arrangement geometry.
DispCtrl.Display   every call that changes something: DDC/CI, CCD writes, modes,
                wallpaper COM, power scheme. Referenced by BOTH app and engine.
DispCtrl.Engine    resident. Taskbar hiding, night light + software dimming,
                per-app preset rules. Native AOT intended (see debt below).
DispCtrl.App       WinUI 3 panel, launched on demand, exits after.
DispCtrl.Cli       `dispctrl.exe`, console subsystem. Every feature, scriptable.
DispCtrl.Control   shared JSON command API, console frontend and named-pipe broker.
```

Clients share atomic, merge-aware `settings.json`; the engine watches it with a
`FileSystemWatcher` (120 ms debounce). The engine also hosts a user/session-scoped
named-pipe command broker. The CLI falls back to local execution when the broker
is absent. See `docs/CLI.md` and `docs/design/IMPLEMENTATION-CHECKLIST.md` for coverage and
remaining migration work; older adapter paths still exist in the UI.

`DispCtrl.Display` used to be the app's alone, on the reasoning that every call in
it is a deliberate user action. Per-app preset rules made those same calls
background work, so the engine references it too.

### State on disk

```
%LOCALAPPDATA%\DispCtrl\settings.json     shared, engine watches it
%LOCALAPPDATA%\DispCtrl\engine.log        engine's rolling log
%LOCALAPPDATA%\DispCtrl\displays.log      display report, rewritten whole
%LOCALAPPDATA%\DispCtrl\presets\*.json    one file per preset, name = file stem
```

---

## Commands

Run everything from the repo root. `build.cmd` (Windows) and `build.sh`
(Linux/WSL) wrap it all: `build.cmd build` stops the engine gracefully, builds
the CLI, engine and app, and restarts the engine through its task; `test`,
`run engine|app|panel|cli`, `release`, and `doctor`/`setup` for a new machine.
`docs/DEVELOPING.md` has every option. By hand: there is no solution file; build
projects individually, in dependency order when several changed.

```bash
dotnet build src/DispCtrl.Core/DispCtrl.Core.csproj       -c Release -v q --nologo
dotnet build src/DispCtrl.Display/DispCtrl.Display.csproj -c Release -v q --nologo
dotnet build src/DispCtrl.Engine/DispCtrl.Engine.csproj   -c Release -v q --nologo
dotnet build src/DispCtrl.App/DispCtrl.App.csproj         -c Release -v q --nologo
```

**A running process locks its DLLs and the build fails with MSB3027.** Stop both
first — and stop the engine *gracefully*, or it leaves a taskbar parked
off-screen:

```powershell
& ".\src\DispCtrl.Engine\bin\Release\net10.0-windows10.0.26100.0\win-x64\DispCtrl.Engine.exe" stop
Get-Process DispCtrl.App -EA SilentlyContinue | ForEach-Object { $_.Kill() }
```

The engine unwinds asynchronously; wait for the process to disappear before
building. **Killing the engine is what strands a taskbar off-screen** — only the
app may be killed outright.

Restart the engine through its scheduled task, which is how it normally runs:

```powershell
Start-ScheduledTask -TaskName 'DispCtrl.Engine'
```

The task is `DispCtrl.Engine`: per user, a logon trigger with no delay,
priority 4 (the scheduler's default 7 is below normal), restart on failure, no
time limit and `AllowHardTerminate` off. `StartupIntegration.RegisterEngineTask`
owns it; the Settings page switch and `dispctrl startup set --engine on` both go
there. The engine migrates an old Startup-folder shortcut to it on its own and
removes the stale `Umbra.Engine` task. Started directly, it still works:

```powershell
Start-Process ".\src\DispCtrl.Engine\bin\Release\net10.0-windows10.0.26100.0\win-x64\DispCtrl.Engine.exe" run
```

**Not elevated, deliberately.** An elevated engine's tray window is cut off from
Explorer by UIPI and its command pipe from every unelevated client. The one
machine-wide write, the gamma clamp, asks for elevation itself.

Engine CLI: `displays`, `enable <n>`, `disable <n>`, `status`, `run [--for <s>]
[--trace]`, `stop`.

`dispctrl.exe` is the scriptable front end — `dispctrl help` lists everything. It is a
**console** subsystem app, unlike the engine: a WinExe does not block the shell
that launched it, so a script gets neither output nor an exit code. That is why
there are two binaries rather than one.

It also carries an `app.manifest` declaring PerMonitorV2. Without it the process
sees virtualised coordinates, `MonitorFromPoint` resolves the wrong monitor, and
the symptoms are baffling rather than obvious: a 200% panel reports 100%, and a
monitor with a working contrast control reports none.

```
dispctrl displays
dispctrl brightness -10 --all
dispctrl nightlight 60 --from 20:00 --to 07:00
dispctrl input "DisplayPort 1" --display 2
dispctrl preset apply Evening
```

Exit codes: 0 done, 1 refused, 2 asked wrongly.

### Shortcuts

```
tools\Install-Shortcuts.ps1                       # Start menu, Release
tools\Install-Shortcuts.ps1 -Configuration Debug
tools\Install-Shortcuts.ps1 -AddToPath            # dispctrl on the user PATH
tools\Install-Shortcuts.ps1 -Remove
```

The shortcut points **at `bin\<configuration>`**, not at a copy, so rebuilding
updates what it launches. Copying the exe somewhere would freeze it at that
build and go stale silently — which is exactly the confusion that prompted it
(the engine was running from Release while only Debug was being rebuilt).

Windows removed the *Pin to taskbar* verb in 10 1903 and it has not returned;
an application cannot pin itself. The script checks the shell verbs and says so
rather than pretending. Pinning is one right-click on the Start entry.

All three exes carry `Assets\DispCtrl.ico` via `<ApplicationIcon>`. Setting it
only on the shortcut would leave the taskbar button and alt-tab generic, and
the engine needs it for the tray icon's logo style.

### What a monitor will tell you

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

### Detect is not Identify

They sit next to each other on the arrangement surface and are easy to confuse,
because Detect calls Identify when it finishes.

- **Identify** flashes each display's number on it for three seconds. Instant,
  and changes nothing.
- **Detect** re-enumerates the hardware, which is seconds of DDC/CI traffic per
  panel, and is the only one that can find a monitor that is connected but
  switched off. Then it identifies, so you can see what it found.

Windows' own Display settings carries both for the same reason. The labels
cannot express the difference, so both carry tooltips.

### The device library (was: contributing a device record)

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

### Hotkeys

Global shortcuts live in `settings.Hotkeys` and are registered by the **engine**,
which also carries them out: 21 actions, from unison and night light to focus,
OLED care, keep awake, taskbar, contrast and the quick panel. A new desk is
offered fourteen defaults (`Hotkey.OfferDefaults`), five enabled: Ctrl+Alt with
Page Up/Down, N, D and L. Nine more are configured but disabled. The defaults are
versioned: upgrades offer newly added actions disabled once, preserving existing
bindings and removals. Never use the arrows, which Intel drivers take for screen
rotation. The page and `dispctrl hotkeys reset` restore
them. **Unison hotkeys write the hardware themselves** (`ApplyUnison`): they
used to save the level and nothing else, so with the app closed no display
moved. Registered
because a panel that registered them would lose them on closing — the opposite
of what a global shortcut is for. The Hotkeys page only edits the list.

`RegisterHotKey` delivers `WM_HOTKEY` to the queue of the thread that
registered it, and the engine polls rather than pumping, so `HotkeyService` owns
a thread with a message pump of its own. Registration **and** unregistration
must both happen on that thread; releasing from elsewhere silently does nothing
and leaves the combination held until the process exits. `MOD_NOREPEAT` is set,
or holding a shortcut walks brightness to an end stop.

Autostart and the Start menu entry are managed by `tools/DispCtrl.ps1`
(`-Install`, `-Uninstall`, `-Status`, `-AddShortcut`, `-RemoveLegacy`). It is
interim; MSIX `windows.startupTask` replaces it.

### Quick panel (the tray icon)

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
  new ones **hidden**. Never rename an id. `docs/QUICK-PANEL.md` is the
  contributor's guide: adding a tile is one catalogue line plus one line in
  `QuickPanelContent.Registry.cs`.
- `QuickPanelSettings.Reorder` **hides** whatever it is not handed. The page's
  remove button works by leaving an item out; keeping its old visibility made
  that button do nothing, and a `presetcheck` assertion caught it.
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
- **The icon's right-click menu**: Quick panel, Open DispCtrl, Simple view
  (`QuickPanel.Simple`, the title bar's dot), Keep on the taskbar, Hide this
  icon, Stop the engine, Exit DispCtrl, Close the app. Stop sets
  `Local\DispCtrl.Engine.Stop`, never ends the process - the engine must
  unwind to put taskbars back. Close sets `Local\DispCtrl.App.Quit`
  (`QuickPanelSignal.RequestQuit`), which the app's listener turns into a
  flushed save and `Exit()`; Exit does that, then stops the engine.
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

### Turn off displays

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

### Reports, not reporters

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

### Defaults are the owner's desk

The shipped defaults were taken from the owner's own settings on 2026-09-24:
everything global, including values inside switches that are off, and the
quick panel's order and visibility. Not taken: anything per monitor,
calibration, bookkeeping, and one-off state such as the unison level. Presets
starts folded through `QuickPanelSettings.FoldedByDefault`, never through a
default `Collapsed` list, which records only departures from it.

### Ambient light

`Color/AmbientSync` follows a Windows light sensor (`AmbientSensors`; this
desk has none, so it is untested on hardware here). The first version
was reported working badly on a laptop that has one: covering the sensor
barely dimmed, a torch never reached the top, and it was slow. Two bugs, both
now checked in presetverify:

- **A sensor reports only on change.** The old filter averaged once per
  reading, so when the light stopped changing the average stopped partway
  (a covered sensor settled at ~3/5 of the old light). `AmbientFilter` keeps
  the newest reading as the room's light, and `AmbientSync` runs a 250 ms clock
  only while a change is being weighed or the level is walking.
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

### The way back: Ctrl+Alt+Backspace

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

### Tests

```bash
dotnet run --project tools/presetcheck/presetcheck.csproj -c Release
```

Assertions over preset store edge cases, the diff, night-light schedules,
app-rule matching, the physical arrangement layout, and the drag's slot
geometry — the last of those matters because **synthetic pointer input does not
reach a WinUI canvas**, so a drag cannot be tested through automation at all.
What runs during one has to be pure geometry, or it is not covered. Exit code is the failure
count. **Add to this rather than writing throwaway probes** — several probes in
this project's history should have been checks here.

It includes the redaction checks: the scrub in isolation, then end to end over
the monitors actually attached - asserting that the text the app would publish
carries none of their serials, device paths, or the account name.

`presetcheck` needs monitors. The hardware-free suites, which CI and
`build.sh test` run, are `controlcheck` (the command API against a scratch
settings folder), `presetverify` (parsing, geometry, the settings merge) and
`devicecheck validate` / `selftest` (the device library and its intake).

---

## Traps already paid for

Every one of these was a real bug. Do not reintroduce them.

### WinUI

- **Coalesce slider saves.** A drag can change a value dozens of times per
  second; writing settings for every change wakes every engine service. Use
  `MainViewModel.PersistSoon()` for sliders and flush pending saves before a
  refresh, external settings sync, or app close.
- **Polling stops with the window.** Page `Unloaded` alone does not
  cover minimization. Stop the refresh timer on hide/minimize and restart it
  on restore; check `OverlappedPresenter.State` for both wallpaper and status
  polling. Wait for the current read before scheduling another; file
  metadata and open calls belong on a worker, and thumbnails decode directly
  from a disposed file stream rather than copying the whole image into RAM.
- **Set `AcceptsReturn` before assigning multiline `TextBox.Text`.** Assigning
  text first silently kept only the first line in the problem-report and
  device-share previews. UI Automation must read the whole preview, not just
  verify that its dialog exists.
- **`SettingsExpander.Items` accepts only card-like children.** A nested
  `SettingsExpander`, or a bare `InfoBar`, does not render badly — it **takes the
  process down** when the item is realised. This has cost three crashes. Use a
  `SettingsCard` with `ContentAlignment="Vertical"`.
- **Items added to a `SettingsExpander` from code after it is built are never
  drawn.** No error, just an empty expander. Declare a `SettingsCard` host in
  the XAML `Items` and fill a panel inside it instead (`QuickPanelPage` does).
- **UIA name searches collide.** A list item and an expander header can share a
  name ("Quick toggles"); find expanders by class
  `Microsoft.UI.Xaml.Controls.Expander` *and* name, or a script expands the
  wrong one and reports the right one missing.
- **`ItemsRepeater` virtualises.** Expanding a card near the bottom grew the
  extent, scrolled it out of the realisation window, recycled and collapsed it,
  shrank the extent — a self-feeding open/close flicker. Use `ItemsControl` with
  a plain `StackPanel` panel; there are never more than a few displays.
- **A `ComboBox` applies `SelectedItem` before its `ItemsSource` is filled**,
  finds nothing matching and renders blank. Bind `SelectedIndex` instead.
- **A two-way `Slider`/`ToggleSwitch` writes its own value to the source as it
  is realised**, before an async read has said what the value is. Indistinguish-
  able from the user acting. Every such binding needs a `_xxxReady` gate. This
  has silently: zeroed brightness, set night light to 0%, and switched a
  machine-wide power setting on.
- **`AppWindow.MoveAndResize` double-applies scale** when the move crosses to a
  monitor at a different DPI: the window is resized, *then* rescaled by the DPI
  change. `Move()` first, then `Resize()`.
- A rounded `Border` inside a square window shows chopped corners. Round the
  window with `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE)` and match
  the Border's radius to DWM's (8 DIP).
- **Even `Move()`-then-`Resize()` is not enough for a window created on one
  display and placed on another.** The rescale can arrive *after* the resize:
  the quick panel opened at 180 px instead of 360. `QuickPanelWindow` holds its
  intended rectangle for a second and restores it.
- **Never resize a window from a `SizeChanged` handler synchronously.** The
  resize lays out again inside the call; with a capped height and a scroll bar
  the content rewraps a few pixels taller and fires again, nested, until
  `InsufficientExecutionStackException` kills the process. Defer to the
  dispatcher, once per pass, and skip no-op sizes.
- **A setter bound two-way to a `TimePicker` must compare at minute
  resolution.** The picker holds whole minutes, the stored value had seconds, so
  every write-back differed: save, raise, write back, 3,430 frames deep. Any
  reload with the Displays page open killed the app (`AwakeExpirationTime`).
- A crash inside a XAML callback reaches Windows as a stowed exception
  (`0xc000027b`, `CoreMessagingXP.dll`) with no managed stack. The app now logs
  unhandled exceptions to `%LOCALAPPDATA%\DispCtrl\app-crash.log`; a stack
  overflow bypasses even that, and needs a `FirstChanceException` hook to see.

### Protection hooks

- **Keep the global location hook behind focus mode.** `EVENT_OBJECT_LOCATIONCHANGE`
  woke the protection thread about 180 times a second on an otherwise idle
  desk. OLED care alone can use its once-a-second tick to inspect the active
  window and pointer.
- **Settings reloads are not foreground changes.** Keep the exclusion set,
  hook registrations and pointer-event detection when their inputs are
  unchanged. Resetting them on unrelated saves restarts polling and the focus
  delay. `ForegroundChanged()` already calls `Tick()`; do not tick twice.

### DDC/CI

- **One channel per monitor; it will not serve two conversations.** Two callers
  do not queue — one is answered and the other fails, and the result looks
  exactly like a dead monitor. Everything goes through `DdcChannel.With`, whose
  gate is a **named mutex**, not a semaphore: engine, panel and CLI are three
  processes sharing one channel per monitor, and an in-process lock serialises
  none of that.
- **Leave 40 ms between messages.** Read back to back, the Dell answered a
  brightness read with a reply belonging to a different code (24 when the panel
  was at 62). A preset captured from that sweep wrote the wrong value to the
  hardware. `MonitorCapabilities.InterMessageMs`.
- **Never write a code the monitor did not list**, and never a manufacturer-
  specific one. `VcpControl.Settables` is an allow list of standard codes.
- **A discrete control is only offered when the current reading is one of the
  values the monitor listed.** The Dell advertises `0x66` (ambient light sensor)
  listing `0F`/`02`, then answers `A1` forever: advertised, not implemented.
- Values in a capabilities string may be run together — the Dell writes
  `66(0F02)` meaning `0F` and `02`. Parse even-length tokens as pairs.
- A VCP reply is 16 bits and **only the low byte carries a discrete value**
  (`0x1111` means input `0x11`).
- Capability strings are cached per device path — they describe the monitor, not
  its state.

- **Retry capability reads, and never cache a failure.** A capabilities string is
  around a hundred round trips and roughly one attempt in three came back empty
  on this Dell. `MonitorCapabilities` now tries three times, 150 ms apart, and
  caches only success — `GetOrAdd` was storing the failure, so one unlucky read
  made the monitor mute for the life of the process. The symptom was silent
  wrong answers: a device record saying the panel answers nothing, a preset
  capturing none of the monitor's own settings, an empty controls list.

### Gamma

- **The engine owns the ramp. The app must never write it.** Both writing it
  meant whichever read second captured the other's warmth as the display's
  *baseline*, so switching night light off left the screen permanently tinted, a
  little further every cycle.
- **Windows refuses ramps too far from identity.** Measured boundary: the
  weakest channel may fall to about **0.53** of identity, which caps warmth at
  3300K and dimming at 50%. **`GammaRange`** lifts it — one elevated write of
  `GdiIcmGammaRange` — after which the limits become 1900K and near-black.
  `NightLight.KelvinFor` and `LowestDim` read the clamp state, so every limit
  follows automatically; call `NightLight.Recheck()` after changing it.
  A translucent overlay per display was considered instead and rejected: DWM
  composites it every frame, it shows up in screen recordings, and it fights
  full-screen exclusive apps. One registry value costs nothing at runtime.
- **Warmth and software dimming share the ramp and compete.** Dimming alone
  reaches 50%; at 60% warmth only 70%; at full warmth not at all. Compose both in
  one write and compute the dim floor from the current warmth.
- A refused ramp is **not an error** — the previous ramp stays. Clamp rather than
  letting a write be refused.
- Capture each display's real ramp as the baseline, but reject one that already
  looks warmed or dimmed, or an abandoned ramp compounds forever. The engine
  sweeps for abandoned ramps at startup, for the killed-not-stopped case.

### Display configuration

- **`ChangeDisplaySettingsEx` refuses every change on this hardware.** Use the
  CCD path: `QueryDisplayConfig` → modify → `SetDisplayConfig` with
  `SDC_USE_SUPPLIED_DISPLAY_CONFIG | APPLY | SAVE_TO_DATABASE | ALLOW_CHANGES`.
- **The desktop origin *is* the primary display's top-left.** Normalise an
  arrangement against the primary, not the bounding box, or Windows rejects the
  whole thing without saying why. Negative coordinates are normal.
- **Set primary before positions**, or promoting a display re-bases every
  coordinate written before it.
- Windows requires every display flush against at least one other: no gap, no
  overlap, and a corner touch does not count. `ArrangementSolver` enforces it
  during the drag so an invalid layout is never drawn.
- Windows sizes its own arrangement tiles by **raw pixel count**. DispCtrl
  deliberately does not — see `PhysicalLayout`.
- **The arrangement drag is discrete, and has to be.** Windows takes an
  arrangement only when every display is flush against another, so the legal
  positions for one display are a countable set — each side of each neighbour,
  at each of three alignments. `ArrangementSlots` enumerates them, the drag
  moves the display to whichever the pointer is nearest, and the others are
  drawn as dashed outlines so the set is discoverable rather than learned by
  trying. Two continuous attempts came first and both failed:
  - Free movement corrected on drop meant the whole drag was spent aiming at
    positions that were going to be rejected.
  - Correcting on every move — push clear, re-attach — made the display *cling*
    to whatever it had last been pushed against, and moving it anywhere else was
    a fight with a solver answering the previous question.
- **The diagram must be frozen for the length of a drag.** Every move changes
  the bounding box; re-fitting that box to the surface recomputed the scale and
  the centring from it, and `PhysicalLayout` normalises its millimetre map to
  its own origin, so that moved too. The grab offset is measured once, at the
  old scale, and stopped meaning anything the moment the drag began: the diagram
  breathed under the pointer and the tile did not stay under the cursor.
  `ArrangeCanvas.Freeze` takes a copy on press and only the dragged tile is
  recomputed; the drop re-fits. Pinning one non-moving tile was an earlier,
  weaker fix for the same thing.
- **Rank the slots in millimetres, not desktop pixels.** A pixel is a different
  real size on each panel, so the pixel-nearest slot is not the one the eye is
  aiming at — on this desk a laptop pixel is under half the width of a Dell one.
  The surface asks `PhysicalLayout.Hang` where each slot would draw, which is
  why that method is public.
- `IDesktopWallpaper` is a **local** COM server: `CLSCTX_ALL`, not
  `CLSCTX_INPROC_SERVER`.
- The **primary taskbar cannot be moved** — `SetWindowPos` returns true and
  Explorer restores it in ~120 ms. DispCtrl uses Windows' own global auto-hide
  there instead.
- **A topology change is applied alone, then everything else is planned.**
  `dispctrl apply` used to plan the whole document against the desk before
  the topology ran, so it validated modes for displays about to vanish and
  rejected the ones about to appear. `SetDisplayConfig` also returns before the
  new monitors enumerate: `Settle` waits for two identical fingerprints.
- **Windows reports no MST topology.** Sinks behind one hub or chain are
  separate targets on the same adapter connector instance; that is what
  `SharingConnector` counts. `DISPLAYPORT_USB_TUNNEL` is the only Thunderbolt
  signal and only its positive answer means anything — a dock that converts to
  plain DisplayPort looks like DisplayPort.
- **A hidden bar must not park on a neighbour.** Parked just past its edge,
  the bar of a monitor stacked above the laptop sat along the top of the
  laptop's screen, and the next rescan's tie-break then gave it to the laptop,
  stranding it there. `TaskbarParking.Plan` sends a bar whose strip is on
  another monitor past the far side of the whole desktop; `ResolveMonitor`
  keeps a known bar with the monitor it was managed on. It first snapped
  there, which read as no animation at all; now it slides between its shown
  position and its own edge under a window region clipped to its monitor
  (`Clip`, set before each move so no frame shows on the neighbour), and parks
  only on the last frame. The rescan's `HealRegion` skips a bar mid-clip. Checked in `presetverify`, since stacking needs the desk moved.
- **A monitor keeps its own brightness while unplugged**, so one reconnected
  after unison moved came back out of step. `UnisonHotplug` writes arrivals
  only, after 1.5 s, because the DDC/CI channel is not up when the monitor
  enumerates.

### Unison calibration

- **Windows' brightness is read back through the built-in panel's own range.**
  "Replace Windows brightness" used to take the panel's raw value as the unison
  level and hold the panel at the level exactly, so with calibration on the
  built-in screen ran 0-100 while the others stayed inside their limits.
  `UnisonResume.LevelFor` is the inverse of `Target`; a value past either end is
  pulled back to it. The correction is written **after** the other displays
  move: it raises a WMI event of its own, and the loop that moves them stops for
  any newer event, so written first it stopped the Dell from following at all.

- The walkthrough drives the slider and every display to the endpoint being
  captured. **Cancel must put them back** — it used to leave the desk at its
  floors with the slider on zero. `RememberThenLowerAsync` records the levels
  first and skips the endpoint if cancelled while reading: starting it would
  take a new generation and strand the restore (and the calibration flag the
  brightness bridge checks) behind it.

### Quick panel window

- **Cloak until the first frame, then slide the window, not its contents.**
  Shown bare, the window drew as an empty grey block for two frames; sliding
  only the contents left the acrylic backdrop to appear at full size in one
  frame. `Summon` cloaks (`DWMWA_CLOAK`), waits two XAML frames, fits the height,
  then moves the whole window its own height from behind the taskbar, placed
  just below it in the topmost band so the taskbar clips it. DWM's own show
  transition is disabled. Closing does not fade: faded, an empty backdrop sank
  alone.
- **Never slot the panel after a taskbar that is not topmost.** A bar DispCtrl
  has hidden is off-screen and not topmost; ordering after it dropped the panel
  behind every ordinary window. `TaskbarOf` requires `WS_EX_TOPMOST` and an
  on-screen rectangle.
- **Topmost belongs to the presenter.** `OverlappedPresenter.IsAlwaysOnTop` set
  in the constructor of a window first shown later - the preloaded panel - never
  took, and the presenter then stripped `WS_EX_TOPMOST` from every
  `SetWindowPos`. `KeepOnTop` cycles it on each summons.
- **Rows stay attached while hidden and are rebuilt only when stale.** Rebuilt
  on every summons, each toggle that loads checked played its off-to-on colour
  transition: lit tiles flashed grey for 150 ms of every opening.
- `FrameworkElement.Parent` is null until the element reaches the live tree.
  Find a child through its container's `Content` or `Children` instead.

- **The title bar has four buttons, each with its own job**: simple mode (a
  dot, accent when on and grey when off), density, stay open, customise. A "more" menu there only repeated the
  Quick panel page and the icon's right-click menu. Only the "DispCtrl" title is
  the drag handle (`TitleRow`, with a transparent background so the whole word
  is hit-testable); a row-wide handle swallowed presses meant for the controls.
  Locking, now only on the Quick panel page and off by default, sets the title
  bar to an empty element, so the title stops dragging.
- **Detail sections start folded** (`QuickPanelSettings.FoldedByDefault`: OLED,
  focus, display mode, taskbar, night light). A separate `Expanded` list records
  the ones opened, so a section added later still starts folded. Section bodies
  sit 8 DIP in from their header; each feature's rows are built once
  (`FocusRows`, `OledRows`, `NightLightRows`) and used by both its section and
  its tile's flyout.
- Simple mode has no density button and always fits its height; a fixed height
  only left space under a handful of sliders.
- **Tiles open flyouts, not menus**, following the taskbar tile: a menu can
  hold a tick but not a slider. The glass, auto-hide and transparency tiles were
  removed - each was a switch already in the taskbar tile's flyout.

### Windows brightness bridge pacing

- **Throttle, not debounce.** Waiting for the events to stop left every other
  display still for the whole drag and then jumped it. The first event acts at
  once, then one pass per 90 ms with the newest value.
- **At the floor, key presses up were lost, and only the keys showed it.** Two
  races: the correction's own WMI echo went into the same pending slot and
  replaced the press that followed it, and the correction compared the panel with
  the *saved* level, so a press already on the panel but not yet read looked out
  of place and was undone. Now the bridge remembers its own write and drops only
  that echo, corrects only a panel outside its range, and waits for 400 ms of
  real quiet. A press that changes nothing is logged, not silent.
- **Correct the built-in panel only once the slider is still (400 ms).**
  Corrected mid-drag, the panel was pulled to its floor under the pointer while
  Windows kept moving it, and the two fought.

### Command line

- `taskbar get|set` work on fields that live loose in `/global`; unscoped, `set`
  could change unison or night light by name. `TaskbarKeys` is the allow list.
- Bare flags (`--confirm`, `--writable`, `--factory`, ...) must be listed in
  `ControlTerminal.Parse`, or the parser takes the next word as their value.
- Every `.reset` command passes the generic reset allow-list first; a reset with
  options of its own (`display.reset`) needs an entry before it.

### Settings file (sharing)

- **Read with delete sharing, and retry the rename.** A save is a rename over
  `settings.json`; `File.ReadAllText` opens without `FileShare.Delete`, so any
  reader in another process made the rename throw access denied - which crashed
  the app mid-drag of the unison slider. And a sharing violation on load was
  treated as corruption: the good file was quarantined as `.bad` and every
  client fell back to defaults. `SettingsStore.ReadShared`, `ReplaceWithRetry`,
  and a load that only quarantines on a JSON error.
- The gamma clamp state is cached for a minute, not for the process's life:
  `Recheck` only ever ran in the app, and the engine that owns the ramp kept the
  old limit until restarted.

### Presets

- **A preset holds everything and applies everything.** The `PresetScope` object
  is gone (schema version 2). A preset that silently left part of the desk alone
  was one whose behaviour you had to remember, and "why didn't it change the
  brightness" is a worse question than "why did it".
- v1 files still load: the unknown `scope` property is ignored, and fields they
  lack default to "not recorded".
- **"Not recorded" has to be distinguishable from a value.** `PresetTaskbar` is a
  nullable object and `VariableRefreshRate` is a `bool?` for exactly this reason:
  a preset saved before those were captured must not reset them to defaults on
  apply, and must not count as drift.
- Guard per field, not per display. An early return for a monitor whose mode
  could not be read also silenced its brightness, which had been read perfectly
  well.
- `PresetDiff.Describe` returns `PresetChange` records (where / what / now /
  saved), because the panel lays them out as a table. `PresetDiff.Lines` puts
  them back into prose for the command line.
- Brightness has a 2-point tolerance: DDC/CI rounds, and without it a preset is
  permanently and uselessly dirty.
- Identity fields (model, serial, connector, physical size, DPI, colour profile)
  are recorded and never applied. They make a shared file readable. Matching is
  on the token alone.

### Settings file

- Enums serialise as **names**, via `UseStringEnumConverter`. The file is
  hand-edited routinely; `"brightnessDown"` says what it does and a number
  silently means something else the moment a value is inserted into the enum.
  Before that was set, a hand-written action name made the whole file
  unparseable — it was quarantined as `settings.json.bad`, the engine fell back
  to defaults, and the symptom was hotkeys simply never firing.

### Publishing device records

Everything here is load-bearing; this is the one feature where a bug is
unrecallable.

- **`DisplayReport` is not publishable and never will be.** It carries the
  monitor serial, `\\?\DISPLAY#...` device paths, and wallpaper paths with the
  user's account name in them. `DeviceSubmission` is a separate type built by
  choosing fields, not by filtering the report. Keep it that way.
- A record carries **everything that describes the model** — the whole EDID,
  every mode with every rate, every VCP code with its kind, range and accepted
  values, and the capabilities string. What stays out is what describes a desk
  or a person: the serial, the device path, any file path, the user name, and
  every current setting. That line, not the volume, is what makes it publishable.
- **`EdidDetails` has no field for a serial number.** Deliberately: there is then
  none to forget to remove. The serial lives in `DisplayKey`, beside the identity
  token that must never be published.
- **Never publish the raw EDID blob.** `Edid.Raw` exists for the decoder. Bytes
  12-15 and descriptor 0xFF are the serial, and a hex dump of them matches none
  of the patterns `Redact.Scrub` looks for — the scrub would pass it straight
  through.
- **A full record usually overruns the prefill budget**, and that is expected
  rather than a regression: the Dell's is ~6,200 characters, which encodes well
  past 7,000. `MainViewModel.Submit` puts a single over-long record on the
  clipboard and opens the empty form, so the gap is one paste.
- **The test for a field**: would it be identical on someone else's monitor of
  the same model? Current settings fail it. Brightness 62 describes an evening
  at a desk, so controls are recorded by range, never by current value.
- **The scrub is now the whole safety margin, not a second line.** A record
  carries the full report and every preset, so it is no longer true that nothing
  sensitive can reach `Redact.Scrub`. Everything sensitive reaches it. Two leaks
  were found the day that changed, and both had been latent:
  - **An account name with a space in it split the profile-path match.**
    `C:\Users\Jesvi Jonathan\...` matched only as far as `C:\Users\Jesvi`,
    publishing the surname, the folder tree, and a device instance id baked into
    a wallpaper file name. Most Windows account names have a space. `UserPath`
    now consumes spaces and stops at end of line, quote, pipe or angle bracket.
  - **Identity tokens are not serials, and were not being scrubbed.** A panel
    with no EDID serial still has a token whose suffix is an FNV-1a hash of its
    device path - unique to that unit on that port. The laptop is that case, and
    its token went out inside the presets. `DeviceContribution.Identifiers` now
    scrubs tokens *and* serials, for **every attached panel** rather than the one
    being submitted: a preset names the whole desk, and
    `dispctrl contribute --display 2` narrows the caller's list to one.
- **A device instance id does not need its path prefix to identify a machine.**
  `5&3c9e07d1&0&UID256` on its own does it, and this laptop's wallpaper tool
  writes it into file names. `InstanceId` catches the bare form.
- **Check for fragments, not just whole strings.** Both leaks survived checks
  that looked for `Jesvi Jonathan` and the full device path, because what
  escaped was a piece of each. `presetcheck` now asserts that no *word* of the
  account name and no instance id appears, and builds a record with the list
  narrowed to one display as well as with all of them.
- **`Redact.Scrub` runs over the finished text**, not the fields, so a field
  added later cannot quietly reintroduce a leak. It removes attached panels'
  serials, device paths, `C:\Users\...` paths, bare GUIDs and the account name.
- `tools/presetcheck` asserts all of this **against the monitors actually
  attached**, not fixtures. That end-to-end check is the one that matters.
- **No token, no network call from the app.** It opens a prefilled issue in the
  browser the person is already signed into and they press Submit. A token in a
  Store app is a token given to everyone who installs it.
- **The body is plain ASCII** - the only place in DispCtrl without proper
  typography. It travels percent-encoded, where an em dash costs nine characters
  and `x` costs one. With typography the Dell's record was 6200 characters and
  overran the URL; without it, 5760 and it prefills. Budget is 7000, under the
  8k where GitHub answers 414.
- `blank_issues_enabled: true` in `.github/ISSUE_TEMPLATE/config.yml` is
  required. Turning it off sends `issues/new?body=` to the template chooser and
  silently drops the body.

### Build

- `PublishAot=true` disables built-in COM interop. The internal panel's
  brightness goes through WMI (`System.Management`), which **is** built-in COM,
  so every per-app rule touching it died with `NotSupported_COM`.
  `BuiltInComInteropSupport=true` is set to fix it, and **that is now the one
  thing between the engine and Native AOT.** Paying it off means reaching WMI
  through source-generated COM, as `Wallpaper.cs` already does for
  `IDesktopWallpaper`.
- Native AOT publish also needs the MSVC linker, which is not installed:
  `winget install Microsoft.VisualStudio.BuildTools --override "--quiet --wait --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"`
- **The taskbar glass helper's revision hashes `TaskbarGlass.cpp`, its header
  and `build.ps1` itself**, and a helper already loaded in Explorer refuses to
  attach over a different revision (`0x8007051A`, revision mismatch) until
  Explorer restarts. A one-line tidy of `build.ps1` did exactly that: glass
  stopped applying on the next engine start. Leave the script alone unless the
  helper really changes, and expect to restart Explorer when it does.
- **Explorer can leave a secondary taskbar with a region sized for the wrong
  DPI** - 2880 x 48 on a 96 px bar at 200%, measured after an Explorer restart.
  Windows clips to the region, so the lower half was never drawn.
  `TaskbarManager.HealRegion` clears a region shorter than the bar every rescan
  and when a reveal settles; the earlier repaint-on-settle did not help,
  because the pixels were clipped, not stale.
- **CsWin32's `CreateFont` cannot be called.** It marshals the byte-sized
  charset as four bytes and the runtime refuses at the first call - which ended
  the tray pump and removed the icon. Use `CreateFontIndirect` with a `LOGFONTW`.
  Read `obj/generated` before trusting any generated overload.
- **No NuGet lock files, on purpose.** Generated, they pin packages the SDK
  adds by itself - `Microsoft.DotNet.ILCompiler` (the engine's `PublishAot`)
  and `Microsoft.NET.ILLink.Tasks` (`IsAotCompatible`) - at the SDK's own
  runtime patch (10.0.12 under SDK 10.0.112, another under 10.0.203). Every
  SDK patch would then rewrite them, or fail CI in locked mode. Every
  `PackageReference` is an exact version, so restores are already
  deterministic without them.
- The engine must carry `<ApplicationIcon>` too: the tray's logo style reads it
  from the running binary, and without it drew an empty slot.

### Release and installer

- **Never let an installer kill the engine.** Inno's Restart Manager
  (`CloseApplications`) would, and a killed engine strands a hidden taskbar.
  `DispCtrl.iss` turns it off and runs `DispCtrl.Engine.exe stop` itself,
  waiting for the exit, and refuses to install over an engine that will not stop.
- **An installer or uninstaller removes the sign-in task only if it points into
  its own folder.** The first draft ran `startup set --engine off`
  unconditionally, which would have deleted a development or portable copy's
  task on this very desk. Do not install the setup on the dev machine to test
  it, for the same reason: it re-points the task. Use a clean account.
- `{tmp}` inside a Pascal `{ }` comment ends the comment - the compiler reports
  "Identifier expected" a line later. Use `//` comments in `[Code]`.
- **Do not sign the taskbar-glass helper.** Its bytes are pinned by revision;
  `Publish.ps1 -Sign` signs only `DispCtrl*` binaries.
- **Never set `AssemblyTitle` on `DispCtrl.App`.** With it set to "DispCtrl",
  a clean build crashed at start (`Cannot locate resource from
  'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'`), and so did the
  published bundle and the MSIX. Incremental builds hid it. The engine and the
  CLI carry titles safely; they have no WinUI.
- **The Store identity lives in `build/packaging/AppxManifest.xml`** (`JustVStudio.DispCtrl`,
  `CN=C082656A-...`), and `Package.ps1` packs with it by default. A package with
  the old development identity was rejected by Partner Center on upload. Only
  CI's validation build passes `DispCtrl.Development`. Run `Package.ps1` under
  pwsh 7: Windows PowerShell's `System.Drawing` cannot read the icon's 256 px frame.
- **A Store install is never started for you.** Nothing runs at install, and
  the sign-in task was declared `Enabled="false"`, so a fresh install had no
  engine - no tray icon, hotkeys, taskbar hiding or glass - until someone found
  the switch. The task is now declared enabled, the app starts the engine on
  every launch, and it enables the sign-in task once (`EngineStartupOffered`).
- **Under MSIX, new files in `%LOCALAPPDATA%` are redirected** to
  `Packages\JustVStudio.DispCtrl_*\LocalCache\Local`, while an existing
  `%LOCALAPPDATA%\DispCtrl` is written in place. Every DispCtrl process in the
  package sees the same view; anything outside it (Explorer, the glass helper,
  an unpackaged build) may not. Registry writes are not redirected - measured
  with `Invoke-CommandInDesktopPackage`.
- **Explorer cannot load anything from WindowsApps.** The glass helper is
  loaded by Explorer itself (`InitializeXamlDiagnosticsEx` hands it a path),
  and a package's files carry a conditional ACE granting execute only to
  processes whose SYSAPPID is that package: everyone else may read, not map an
  image. The Store build's glass therefore did nothing at all, with no visible
  error, while the installer and zips worked. `TaskbarGlassController.ExplorerLoadable`
  copies the helper to the package's real `LocalCacheFolder` (the user's ACL)
  and loads that. Anything else handed to another process by path from a
  Store install needs the same.
- **Explorer records a tray icon's path by known-folder id**:
  `{6D809377-...}\WindowsApps\...` for the Store engine. Compared as a plain
  path it never matched, so the Store icon was never kept on the taskbar.
  `TrayIconPromotion.Expand` resolves it.
- **Nothing asks for admin unprompted.** Only lifting the gamma range and
  auto-rotation need elevation, and each asks when the person switches it on.
  A first-launch prompt for the gamma range was tried and dropped: the lift is
  a nicety, and an unasked UAC prompt on first run costs trust (and Store
  certification questions it). The app must never require elevation: a
  packaged app cannot run elevated at all.
- **The wallpaper preview falls back to `%APPDATA%\Microsoft\Windows\Themes\TranscodedWallpaper`**,
  Windows' decoded copy, when the reported file is missing, online-only or
  undecodable (HEIC, WebP). One laptop's preview stayed empty without it.
- Switching between the installer, the Store and a development build leaves
  Explorer holding the other build's glass helper (`0x8007051A`); the Taskbar
  page offers Restart Windows Explorer when the engine reports it.
- An MSIX signs only with a certificate whose subject equals its `Publisher`.
  The release workflow compares them and leaves it unsigned rather than failing.
- **Read a process's `Path` before stopping it.** `Process.Path` comes from
  the main module; once the engine has exited it is empty. `dev.ps1 build` read
  it afterwards and so never restarted the engine it had stopped.
- `SkipTaskbarGlass=true` builds the engine without the native helper (no
  MinGW). On Linux the helper step runs `pwsh`, not `powershell.exe`: through
  WSL interop the latter reached Windows' own MinGW with Linux paths and failed
  to link. The WinUI app cannot build off Windows at all (`GenXbf.dll`).
- **A tool run on Linux must not have an apphost.** `Directory.Build.props`
  builds for win-x64, so `dotnet run` on the Linux runner tried to execute
  `devicecheck.exe` - "Exec format error" - and every shared monitor record
  failed intake. WSL hid it: it runs Windows .exe files through interop, so
  the check "passed on Linux" there. `devicecheck` and `presetverify` set
  `UseAppHost=false` off Windows; verify on Linux by confirming no `.exe` was
  built. The workflow's shell is named `bash` so `| tee` no longer hides a
  failure (the default shell has no pipefail).
- **MSBuild never deletes an output it has stopped copying.** When the device
  library moved to a folder per model, every `bin` kept the old flat
  `devices/definitions/` and the records, which must not ship.
  `Directory.Build.targets` removes the retired layout after each build and
  publish. Any future move of a copied file needs the same treatment.
- **Publish stops the engine too.** It runs the tests, which build into the
  `bin` the engine runs from; `dev.ps1 publish` stops it gracefully and
  restarts it, as `build` does.
- **Release output has one fixed place**, `artifacts/<channel>-<version>/`,
  cleared on each run. It used to be a new GUID-suffixed folder per publish and
  per MSIX, each with a full staging copy: 2 GB had accumulated.
- **The engine restores taskbars from a crash handler.** An exception on a
  timer or pool thread ends the process without unwinding through the
  manager; `AppDomain.UnhandledException` calls `TaskbarManager.EmergencyRestore`
  first. Do not remove it.
- **Idle cost is wake-ups, not work.** Measured with per-thread context
  switches (`\Thread(DispCtrl.Engine*)\Context Switches/sec`), not guessed.
  - A global `EVENT_OBJECT_LOCATIONCHANGE` hook woke the focus thread ~180 times
    a second on an idle desk; it is registered only for focus mode.
  - The taskbar loop polled at 100 ms when it managed nothing or the session was
    locked; it now sleeps to the rescan, or until settings change.
  - OLED care stops polling while locked (`WM_WTSSESSION_CHANGE`).
  - Hot-plug detection follows `WM_DISPLAYCHANGE` (`DisplayChanges`) with a
    30 s safety net.
  - The power loop sleeps to its next deadline.
  - A rest (OLED idle or manual, displays off) is ended by input, so while one
    shows the protection thread registers raw keyboard and mouse input
    (`RIDEV_INPUTSINK`) and waits for it, with a one-second safety net for a
    second stage or a pointer moved by software. It polled at 100 ms, all night.
    One key press ends displays off in well under 150 ms, measured.
  - Engine idle: ~282 ms/min before, ~16 ms/min after.
- **Slider saves are coalesced** (`PersistSoon`); a synchronous save per drag
  step also made the engine reload each time. `Persist()` flushes a pending one.
- **The hidden quick panel trims itself** 10 s after hiding: 190 MB working set
  down to ~11 MB, and the next summons shows in ~40 ms.
- **Tray icon promotion is once per engine path** (`Global.TrayPromotedFor`).
  After that, where the icon sits is the person's choice; never re-promote.
- **Repair never re-points a sign-in task another copy owns.** An installed
  copy, a portable one and a development build can all exist; `maintenance
  repair` only fixes a task whose engine is missing.
- **Repository layout**: `.claude/` this file, `.github/` community files,
  `build/packaging/` MSIX and installer, `src/native/` the glass helper,
  `docs/design/` internal notes. Session exports stay in `.notes/`, ignored.
- **Workflows** (`docs/RELEASING.md` has the table): Build and verify, Release,
  Distribute, Device library, Pull requests, Issues, Website, Housekeeping.
  Each writes a summary with links and sizes. Every action is pinned to a
  commit with its tag in a comment, every workflow starts from
  `permissions: {}` and each job asks for its own, and a checkout that pushes
  nothing sets `persist-credentials: false`. Untrusted text (issue bodies,
  titles, file names) reaches a script only through `env`. `pr.yml`'s policy
  and triage run on `pull_request_target` and must never check out the pull
  request - everything comes from the API. Lint locally with actionlint and
  shellcheck (`pip install actionlint-py shellcheck-py`); CI runs the same.
  Labels live in `.github/labels.json`. A release that the
  workflow publishes itself does not raise `released`, so Release starts
  Distribute with `gh workflow run`. Housekeeping deletes old artifacts, runs,
  caches and drafts weekly; a manual run defaults to a dry run.
- **One local build at a time.** `dev.ps1` holds `Local\DispCtrl.Build` for
  build, test, clean and release: two runs share every `obj` folder, and a
  release started while another ran failed with a missing R2R file. Publish also
  shuts the compiler server down first (CS2012 on a still-open obj file), closes
  any repository copy of the app (an installed engine can start the panel from
  `bin`), publishes the app before adding the CLI files (publish keeps a newer
  destination, which left the app with the CLI's copies and a startup crash),
  and launches the result before packaging it.
- `DispCtrlVersion` in `Directory.Build.props` is the default `Version`; a
  stable tag that disagrees with it fails `release.yml`. Bump it in the
  release commit. See `docs/RELEASING.md` for the whole procedure and the
  secrets and variables it needs.

---

## Verifying on real hardware

Screenshots and UI Automation, from **Windows PowerShell 5.1** (`powershell.exe`),
not pwsh 7 — `System.Drawing` is unavailable there.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File <script>.ps1
```

Drive controls by `AutomationProperties.Name` and a UIA pattern, **never by
coordinate clicks** — a coordinate click once corrupted real settings.

What does not work, and cost time discovering:

- **Synthetic pointer input does not reach the WinUI canvas.** `PointerPressed`
  never fires, so drag behaviour cannot be tested through automation. That is why
  `ArrangementSolver` and `PhysicalLayout` are pure geometry with checks in
  `presetcheck`.
- **Foreground cannot be stolen** from a background script — neither
  `SetForegroundWindow` nor `AppActivate`. To test per-app rules, match on
  whatever genuinely holds the foreground instead of trying to create it.
- **Gamma ramp reads fail from PowerShell** P/Invoke. Verify gamma through the
  engine log or a small console project referencing `DispCtrl.Core`.
- Nested `SettingsExpander` children realise lazily: scroll the page top to
  bottom, expanding at each step, or UIA will not find inner controls.
- A capabilities sweep takes ~30 s (37 round trips at 40 ms plus reads). Wait for
  it before asserting the controls are missing.

---

## Pages

The Help page's **Report a problem** uses the `report` control command. The
report includes scrubbed diagnostics, previews before opening GitHub, and carries
overflow in `paste` instead of exceeding the issue-link limit. Saved monitor
tokens are scrubbed too because old logs can name disconnected displays. The
CLI prints the report and link without opening a browser or touching the clipboard.

`Displays`, `Taskbar`, `Presets`, `Quick panel`, `Hotkeys`, `Devices`, `Engine`,
`Settings`, `Help`, `About`. `docs/CLI-COVERAGE.md` maps every control on every
page to its command; a new feature lands in `DispCtrl.Control` first and the
page calls it. Taskbar was split out of Settings: reveal behaviour, the four polling
intervals (which had no UI at all before, only settings.json), and Windows'
global auto-hide. What stayed in Settings is what is not about the taskbar —
logging and the reset.

Which monitors hide their taskbar stays per display, on the Displays page.

`PresetChanges` is a `UserControl`, not markup repeated twice: the docked bar and
the Presets page both show the drift sign, and two copies would disagree the
first time either changed.

## Conventions

Match what is there. It is deliberate and consistent.

- Comments explain **why**, never what. Most carry a fact that was measured or a
  bug that was paid for. Do not add narration.
- XML docs on public members; `<remarks>` for the reasoning and the trade-off.
- Prose style throughout: full sentences, no hype, British spelling in user-
  facing strings ("colour", "centre").
- Commit messages are prose paragraphs explaining what changed and why,
  including bugs found and how they were verified. Not bullet lists.
- Every user-facing claim must be true of the hardware. When something cannot be
  done — the primary taskbar, ambient light on a panel without a sensor — say so
  in the UI rather than offering a control that does nothing.

---

## State of play

Working and verified on hardware: per-monitor taskbar hiding, per-monitor
wallpaper, unison brightness (multiplier and calibrated range), night light
(unison, per-monitor, calibrated, scheduled), software dimming, arrangement
drag/apply, presets with per-app rules (shelved in release builds behind
`EnableBetaPresets` in `Directory.Build.props` until the beta is ready),
monitor capability discovery and control,
display report, identify overlays, hotplug re-discovery, device contribution
(anonymised, consent-gated), the quick panel and tray icon, Windows' brightness
slider and keys driving unison, the control API and `dispctrl` JSON surface.
`docs/design/IMPLEMENTATION-CHECKLIST.md` records how each recent item was verified and
what is still unverified. Since then CI runs on GitHub (Build and verify
passes; the Device library job failed until devicecheck stopped building a
Windows apphost on Linux) and the MSIX has been accepted by Partner Center and
installed from the Store; the installer has still only been compiled, not
installed (see "Release and installer").

Outstanding, roughly in the order last discussed:

See `docs/design/FEATURES.md` for the full candidate list with effort and risk. The
short version, in recommended order:

1. ~~Brightness fallback, high-level to VCP `0x10`~~ — **done**.
2. ~~Lift the gamma clamp~~ — **done**, `GammaRange`.
3. ~~Full command line~~ — **done**, `dispctrl.exe`.
4. ~~Hotkeys~~ — **done**, engine-registered, with a Hotkeys page.
5. **Combined brightness** — one slider spanning hardware above a switching
   point and software dimming below it.
6. ~~Presets capturing everything~~ - **done**, schema v2, scope removed.
7. **Persistent known-monitor cache** — survive restarts, keyed on model+serial.
   Copy ddcutil's `<mfg>-<model>-<product>` convention.
8. **More fields in the display report.**
9. ~~Opt-in contribution~~ - **done**, `dispctrl contribute` and the panel card.
   Records land in `devices/BRAND/PRODUCT/record.md`; `DEL/A234` and
   `SDC/4154` are seeded from this machine.
10. **OLED burn-in protection** — original scope, still unbuilt. The per-monitor
   `IsOled` flag exists and is what it should key off.
11. **Remember window positions** across replug.
12. MSIX packaging; Native AOT (blocked, see above); widgets; taskbar
   translucency.

## Reference implementations

Cloned outside the repo at `../refs/` (not tracked, re-clone with
`git clone --depth 1`). Read before designing anything in their territory —
`docs/design/FEATURES.md` lists what was taken from each and why.

| Repo | Language | Worth reading for |
|---|---|---|
| `twinkle-tray` | Electron | ambient light (per-monitor lux ranges, and a **simulated sensor** so it is testable), CLI shape, schedules, idle dimming |
| `Monitorian` | C#/WPF | closest domain; high-level/low-level brightness fallback, per-monitor ranges, per-device quirk handling |
| `MonitorControl-mac` | Swift | **shade overlay dimming** (no gamma clamp, does not fight other gamma users) and combined hardware+software brightness |
| `ColorControl` | C#/WinForms | GPU-level colour, service plus separate elevation service |

Two ideas from these are worth knowing even before implementing them:

- **A shade overlay dims further than gamma can.** DispCtrl's software dimming is
  capped at ~50% by Windows' gamma clamp; a click-through translucent window has
  no such limit. MonitorControl keeps both and picks per display.
- **Ambient light is not "one monitor's sensor drives the others".** It is one
  lux reading plus a per-monitor lux-to-brightness range. That framing makes a
  monitor without a sensor the normal case rather than a special one.

## Prior art worth knowing

- [ddcutil](https://github.com/rockowitz/ddcutil) — the most complete public VCP
  feature table (`src/vcp/vcp_feature_codes.c`), and its
  [user-defined features](https://www.ddcutil.com/udf/) format
  (`<mfg>-<model>-<product>.mccs`) is the design to copy for per-model quirks.
- [linuxhw/EDID](https://github.com/linuxhw/EDID) — ~175,000 real EDIDs by
  vendor and model.
- There is **no** comprehensive public database of manufacturer-specific VCP
  codes. ddcutil's position, and DispCtrl's: record them, never write them blind.
