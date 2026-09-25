# Candidate features

Drawn from studying four open-source display tools alongside what DispCtrl already
does. Clones live outside the repo at `../refs/`:

| Repo | Language | Why it was worth reading |
|---|---|---|
| [twinkle-tray](https://github.com/xanderfrangos/twinkle-tray) | Electron/JS | ambient light model, CLI, schedules, idle dimming |
| [Monitorian](https://github.com/emoacht/Monitorian) | C#/WPF | closest domain; DDC/CI robustness, per-monitor ranges |
| [MonitorControl](https://github.com/MonitorControl/MonitorControl) | Swift/macOS | shade dimming, combined brightness |
| [ColorControl](https://github.com/Maassoft/ColorControl) | C#/WinForms | GPU-level colour, service + elevation split |

Status is honest: **done** means verified on this hardware.

---

## Already in DispCtrl

Several things on the original request list turned out to be built already,
because the capability-discovery work generalised them.

| Feature | Where |
|---|---|
| Monitor power state | VCP `0xD6`, offered on the Dell as On / Off (soft) / Off (hard) |
| Monitor volume + mute | VCP `0x62` / `0x8D`, in the allow list — the Dell has no speakers so it does not appear |
| Colour temperature | VCP `0x14`, offered as sRGB / 5000K / … / User 1-2 |
| Picture mode | VCP `0xDC`, offered as Standard / Movie / Games |
| Input source switching | VCP `0x60`, offered as DisplayPort 1 / HDMI 1 |
| Automatic detection of connected displays | cheap layout signature polled beside the engine status |
| Per-app profiles | `AppRuleService`, foreground-driven, 2s dwell |
| Display profiles to save and set | presets, with scope and drift tracking |
| Per-monitor brightness ranges | calibrated unison limits — Monitorian does the same thing |
| Refresh rate switching | CCD path, applied without blanking where the driver allows |
| Multi-monitor taskbars | Windows 11 draws them; DispCtrl hides them per monitor |

### Power and panel protection

- OLED idle care supports two stages: an initial dim after inactivity, followed
  by a separately timed deeper dim. The second stage is offered only when the
  first level is between 1% and 99%.
- Manual screen rest belongs to each display and always previews the actual
  full-black rest; the global dim slider remains a live preview of idle dimming.
- External monitors that expose VCP `0xD6` can enter their own low-power state
  after an independent idle timeout and wake when input resumes.
- Keep awake can follow the selected power plan, run indefinitely, run for an
  interval, or expire at a date and time. Keeping displays on is an independent
  option. These are process-scoped Windows power requests and do not rewrite the
  active power plan.

---

## Candidates

### A. Ambient light, done properly

**Twinkle Tray's model, which is better than the one first sketched here.** Not
"one monitor's sensor drives the others" but: one *lux reading* from whatever
source exists, and **each monitor maps that lux range to its own brightness
range** (`minLux`, `maxLux`, `enabled` per display). A monitor with no sensor is
not a special case — no monitor has a sensor, the sensor is a separate thing.

Sources, pluggable: Windows ambient light sensor; a monitor's own VCP `0x66`
where it genuinely implements it; and — the part that makes this testable — a
**simulated source**, which is exactly what Twinkle Tray ships as its "fake"
sensor. This machine has no ALS, so without a simulated source the feature could
not be verified at all.

Effort: moderate. Risk: low. Testable end to end via the simulated source.

### B. Shade dimming instead of gamma

**The fix for DispCtrl's 50% dimming floor.** MonitorControl uses a click-through
translucent black window per display, alpha = 1 − brightness, rather than a gamma
ramp. It has no clamp, so it dims to near-black, and it does not fight other
software that owns the gamma table.

DispCtrl's software dimming is currently capped by Windows' gamma clamp — 50% at
best, and less once night light takes part of the range. A shade removes that
entirely. Keep gamma as the default (no extra window, works over full-screen
exclusive content) and offer the shade per display where the range matters, which
is what MonitorControl's `avoidGamma` preference does.

Effort: moderate. Risk: medium — a click-through always-on-top window per display
has to behave around full-screen apps and must never swallow input.

### C. Combined brightness

One slider spanning hardware and software. Below a switching point the hardware
sits at its minimum and dimming takes over; above it, hardware scales. The user
gets a single 0–100 control that goes genuinely darker than the panel allows.

Pairs naturally with B. Effort: small once B exists.

### D. Brightness fallback, high level to low

Monitorian tries `GetMonitorBrightness` and falls back to VCP `0x10` when it
fails. Some monitors answer one and not the other. DispCtrl uses only the high-level
call, so it reports "no brightness control" on panels that would in fact answer
`0x10`.

Effort: small. Risk: low. Straightforwardly more monitors supported.

### E. Hotkeys

A Hotkeys page: global shortcuts for brightness up/down (all displays or one),
apply a named preset, toggle night light, cycle input source. `RegisterHotKey`
in the engine, which is already the resident process.

Effort: moderate. Risk: low.

### F. Command line

The engine has a CLI for taskbars only. Extend it: `preset apply <name>`,
`brightness <n> [--display <n>] [--offset]`, `nightlight on|off <strength>`,
`input <source>`, `list`. Twinkle Tray's `--MonitorNum` / `--Set` / `--Offset`
shape is the proven one.

Makes every feature scriptable, which is what the original request asked for.
Effort: small — the operations all exist.

### G. Tray icon with sliders

A tray icon whose flyout carries a brightness slider per display. This is the
whole of Twinkle Tray and Monitorian, and it is how anyone actually adjusts
brightness day to day — opening a settings window is not that.

Would mean the app, or a small part of it, becoming resident. That is a real
change to the architecture's central premise, so it deserves a decision rather
than a drive-by.

Effort: large. Risk: medium — touches the resident/on-demand split.

### H. Schedules for brightness

Night light already has a schedule. Brightness does not. Twinkle Tray's is a
simple time → level table per display, which composes with everything else.

Effort: small. The engine already evaluates a schedule each tick.

### I. Idle dimming (built)

OLED idle care uses `GetLastInputInfo`, fades into the selected level, and can
then enter a second, deeper stage after another interval. Input restores the
panel. Manual screen rest and supported monitor hardware sleep share the same
resident service without adding another polling loop.

### J. Focus mode (built)

Per-display click-through overlays keep the active, hovered, or automatically
opened window clear and smoothly dim the rest. It can follow each monitor's
last focused window, account for current brightness, pause for fullscreen apps,
and exclude selected processes.

### K. GPU-level colour

ColorControl exposes NVIDIA/AMD dithering, colour depth and format. These are
vendor APIs (NVAPI, ADL) with real redistribution and stability questions, and
nothing about them is per-monitor in the way the rest of DispCtrl is.

Listed for completeness. Recommend **not** doing it.

### L. Smooth transitions

MonitorControl animates brightness changes rather than jumping. Cosmetic, but it
is the difference between a control that feels built-in and one that does not.

Effort: small. Risk: low.

---

## Recommended order

1. **D** — more monitors work, smallest change.
2. **B + C** — removes the dimming floor, which is the biggest current limitation.
3. **A** — the ambient model, testable via a simulated source.
4. **F** — makes everything scriptable.
5. **I + J** — cheap once B lands.
6. **E**, **H**, **L**.
7. **G** — only after deciding about residency.
8. **K** — recommend against.


## Device contribution (built)

Requested as "send monitor details to repo ... we get a copy of all the monitor
the user has into our repository under devices ... like auto create a issue".

Built as `DeviceSubmission` + `DeviceContribution` in `DispCtrl.Display/Devices`,
`dispctrl contribute`, and a card on the Displays page.

What decided the shape:

- **Anonymised by construction, not by filtering.** The display report this grew
  out of contains the monitor's serial, device instance paths and wallpaper
  paths carrying the user's Windows account name. None of that can be published,
  and a filter over the report would leak the first field someone added. The
  submission is a separate type whose every field answers "would this be the
  same on another unit of this model?".
- **A second pass anyway.** `Redact.Scrub` runs over the finished markdown, so a
  field added later cannot reintroduce a leak without being both sensitive and
  shaped unlike a serial, a device path, a user path or a GUID.
- **No token, no request from the app.** It opens a prefilled issue in the
  browser; the person reads the exact text and presses Submit. A token shipped
  in a Store app is a token given to everyone who installs it, and a submission
  the app makes on someone's behalf is not consent.
- **One issue per monitor**, because a record describes a model, not a desk.
- **Keyed `<MFG>-<PRODUCT>`**, not DispCtrl's internal token - that ends in the
  serial.

Still open: prompting when an unknown monitor appears (must offer, never send),
and a maintainer-side path from issue to `devices/*.md`.


## Presets hold everything (built)

Requested as "preset should basically take a snapshot of all values.. like all
values", then "why have what this preset controls if we are going to save all
values".

The second question answers the first. Scope existed so a narrow preset could
stay narrow, but once a preset captures the whole desk, a list of what it is
allowed to touch is a second, invisible piece of state that has to be remembered
to predict what applying will do. It is gone.

Schema version 2. Added to the snapshot: software dimming, the OLED marking,
variable refresh, the taskbar's whole reveal behaviour, and per monitor the
model, serial, connector, physical size, DPI and colour profile. The last six are
recorded and never applied - they are what makes a shared file readable.

Two shapes matter:

- **Absent is not the same as default.** `PresetTaskbar?` and `bool?
  VariableRefreshRate` are nullable so a preset saved before those existed does
  not reset them on apply or show up as drift.
- **Guard per field, not per display.** The first attempt skipped a whole
  monitor whose mode could not be read, which also threw away its brightness.

Drift is now a `PresetChange` record rather than a sentence, so the panel can lay
it out as setting / now / saved. It shows as a sign with a count, and the list
is behind it in a flyout - shared between the docked bar and the Presets page by
a `UserControl`. Rename, export, import, open folder and delete collapsed into
one actions menu; delete and rename now ask in a dialog.

## Taskbar page (built)

The reveal settings, Windows' global auto-hide, and the four polling intervals -
which had never had a UI at all and were editable only by hand in settings.json.
Settings keeps what is not about the taskbar.

## Display information (built)

Twenty-nine facts in six groups, from what was ten. The gap was real: several
properties existed on the view model and were bound nowhere. `ScreenSizeText`
(inches and mm) and `PixelDensityText` (real PPI) had been written and never
placed, and `ColorProfileName` was read from Windows and never shown.

New: model, EDID manufacturer and product code, serial, screen size in inches,
pixel density, connector, whether it is the main display, DDC/CI status with the
control count, native resolution, variable refresh range, DPI with scaling,
orientation, colour depth, colour profile, position, work area, and the Windows
device name beside DispCtrl's own token.

## Compared with PowerToys (September 2026)

Three PowerToys utilities sit next to DispCtrl: **Power Display** (per-monitor
DDC/CI control), **FancyZones** (window layouts) and **Always On Top** (pinning
windows). Read from Microsoft's documentation, not run on the dev desk; recheck
before building anything that depends on how they behave.

**Status, 25 September 2026.** Built: P1 (`DdcGuard`), P3 (`MonitorSettings.InUnison`),
P4 (`Shell/TrayWheel`, and the wheel on the panel's sliders as an option), P7
(`MonitorCapabilities.Probe`, read-only, with `ProbedCodes` standing in for a
missing capabilities string), Z1 and Z4 (`Placement/PlacementService`), Z2
(`WindowMover.Gather`, and `placement move` for one window), T1-T3
(`WindowPins`, `Placement/PinService`, focus and OLED holes in `FocusService`),
and Light Switch's scheduled theme on night light's hours
(`NightLightSettings.DarkModeOnSchedule`). Open: P2, P5, P6, Z3. Z1 was exercised
by unplugging the Dell twice: it works beside Windows' own window memory,
which it no longer tries to switch off (see CLAUDE.md).
Since then: the cross-feature pass (CLAUDE.md, "Where the window features
meet the others") - pins step aside for fullscreen, gathering maximized and
borderless windows, focus mode's pointer options as one choice, and OLED care
per display with an exception list.

Power Display is the direct competitor for plain external-monitor brightness.
DispCtrl's case is everything around that - the built-in panel as a
first-class display, colour and software dimming, taskbars, OLED care, the
physical arrangement. The two window utilities are not display management, so
the items below take only the parts where knowing about displays makes
DispCtrl better at them, and recommend against cloning either.

### From Power Display

**P1. Guard the capabilities read against a kernel crash.** Microsoft
documents that reading the capabilities string of a monitor with a malformed
one can hit a Windows kernel bug and blue-screen the machine. Power Display
ships a list of monitors it never probes, notices a crash, turns itself off and
excludes the monitor next time. DispCtrl reads every external monitor's
capabilities at engine start (the display report) and when learning a model
(`UnisonHotplug`), with no protection. Shape: write a "probing <device>" marker
before each read and clear it after; a marker found at the next start means that
read took the machine down, so the monitor is excluded and the app says so. A
model known to do it goes in the device library (a definition flag, reviewed
like any other), so everybody skips it.
Effort: moderate. Risk: low. **Do first**: it is the only item here that
protects the machine rather than adding to it.

**P2. Finish presets.** Power Display ships profiles; DispCtrl's presets are
built but shelved behind `EnableBetaPresets` in every release. It is the largest
visible gap, and the work is mostly done.
Effort: moderate (the beta's open items). Risk: medium.

**P3. Leave a display out of unison.** Power Display links every monitor by
default and lets each be excluded, keeping its own slider. DispCtrl's unison
takes every display (calibrated ranges shape each one, but none can opt out). A
per-monitor `InUnison` flag, honoured by the slider, the hotkeys, the brightness
bridge and hot-plug.
Effort: small. Risk: low.

**P4. Brightness from the mouse wheel over the tray icon** (off, the primary
display, or all - with unison, all). Notification icons are not sent wheel
messages, so this needs a low-level mouse hook; install it only while the
pointer is over the icon (the icon's own mouse-move callback starts it, leaving
the icon's rectangle ends it), or it costs wake-ups all day. Mouse-wheel steps on
every brightness slider belong with it, using the same step setting.
Effort: moderate. Risk: low, if the hook is scoped as above.

**P5. Presets that follow the Windows theme** - one for dark mode, one for
light. The quick panel already switches the theme; a preset per theme needs
only P2.
Effort: small after P2. Risk: low.

**P6. Confirm the controls that can strand a monitor**: input source (a black
screen until someone presses the monitor's buttons), power state (some monitors
never wake from software) and colour preset (some keep the value after DispCtrl
closes). Once per monitor per control, remembered. Factory reset already asks.
Effort: small. Risk: low.

**P7. A read-only compatibility probe** for monitors whose capabilities string is
missing or broken: read each MCCS code DispCtrl knows, list the ones that answer,
write nothing. Power Display's compatibility mode probes too, but it can change
brightness, contrast or the input as it goes; DispCtrl's rule of never writing
an unlisted code stays. What it finds can be offered to the device library as a
share. Must come after P1.
Effort: moderate. Risk: medium (the probe itself is DDC/CI traffic a flaky
monitor can choke on).

### From FancyZones

DispCtrl should not grow a zone editor: FancyZones does it well, and it is
window management. Where displays change under windows, though, DispCtrl knows
first and knows most.

**Z1. Windows back where they were after a display returns.** Already roadmap
item 11. FancyZones keeps windows in their zones through a resolution change;
DispCtrl's `DisplayChanges.Settled` knows which display left and which arrived.
Record each top-level window's display and relative rectangle when a display
leaves; when it comes back, the windows that were on it (still open, not moved
since) go back. Per-monitor identity is the token, so the same monitor on a
different port still counts.
Effort: moderate. Risk: medium (moving other applications' windows; DPI and
minimised or maximised state need care).

**Z2. Gather windows** from a display: before turning it off, switching to one
display, or on demand ("bring everything to this display"), from a tile, a
hotkey and the command line. FancyZones rotates windows between monitors on a
shortcut; gathering is the display-shaped version of it.
Effort: small. Risk: low.

**Z3. Apply a FancyZones layout from a preset**, when PowerToys is installed:
its command line (`FancyZonesCLI set-layout <uuid> --monitor n`) makes the
integration a line per display in the preset, with no window management of our
own. A preset for a desk then sets its windows up as well as its displays.
Effort: small after P2. Risk: low; skipped quietly when FancyZones is absent.

**Z4. New windows on the active display** (FancyZones' "move newly created
windows to the current active monitor"). Worth it only as part of Z1's window
tracking. Effort: small with Z1. Risk: low.

### From Always On Top

**T1. Pin a window on top**: a hotkey (Windows' `Win+Ctrl+T` is PowerToys'; offer
the action unbound), a quick panel tile and a command, with an optional border
drawn as a layered window around it. The border follows the one pinned window
through a location hook scoped to that window's thread, never a global one (the
global location hook is the ~180 wake-ups a second paid for in focus mode).
Effort: small. Risk: low.

**T2. Pinned windows are exempt from focus mode and OLED dimming.** This is the
part PowerToys cannot do: DispCtrl dims everything but the active window, and a
video or chat pinned beside the work should stay lit. The focus service already
decides which windows are clear; a pinned window joins that set.
Effort: small with T1. Risk: low.

**T3. Excluded apps, and nothing pinned in full-screen games** - the focus
service's `PauseFullscreen` already detects that case.
Effort: small with T1.

### Recommended order

1. **P1** - protects users' machines; nothing else here does.
2. **P3**, **P6**, **Z2** - small, and each closes a real gap.
3. **P2** (then **P5**, **Z3**) - the largest visible gap, and most of it built.
4. **T1 + T2** - small, and T2 is something only DispCtrl can offer.
5. **Z1** (with **Z4**) - roadmap item 11, the most useful and the most care.
6. **P4**, **P7**.

Sources: Microsoft Learn - [Power Display](https://learn.microsoft.com/windows/powertoys/power-display),
[FancyZones](https://learn.microsoft.com/windows/powertoys/fancyzones),
[Always On Top](https://learn.microsoft.com/windows/powertoys/always-on-top).
