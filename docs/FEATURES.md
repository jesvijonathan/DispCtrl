# Candidate features

Drawn from studying four open-source display tools alongside what Umbra already
does. Clones live outside the repo at `../refs/`:

| Repo | Language | Why it was worth reading |
|---|---|---|
| [twinkle-tray](https://github.com/xanderfrangos/twinkle-tray) | Electron/JS | ambient light model, CLI, schedules, idle dimming |
| [Monitorian](https://github.com/emoacht/Monitorian) | C#/WPF | closest domain; DDC/CI robustness, per-monitor ranges |
| [MonitorControl](https://github.com/MonitorControl/MonitorControl) | Swift/macOS | shade dimming, combined brightness |
| [ColorControl](https://github.com/Maassoft/ColorControl) | C#/WinForms | GPU-level colour, service + elevation split |

Status is honest: **done** means verified on this hardware.

---

## Already in Umbra

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
| Multi-monitor taskbars | Windows 11 draws them; Umbra hides them per monitor |

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

**The fix for Umbra's 50% dimming floor.** MonitorControl uses a click-through
translucent black window per display, alpha = 1 − brightness, rather than a gamma
ramp. It has no clamp, so it dims to near-black, and it does not fight other
software that owns the gamma table.

Umbra's software dimming is currently capped by Windows' gamma clamp — 50% at
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
fails. Some monitors answer one and not the other. Umbra uses only the high-level
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

### I. Idle dimming

Dim after N minutes of no input, restore on the first key or mouse move.
`GetLastInputInfo`, which is a cheap poll the engine already does for the cursor.

Effort: small. Risk: low. Worth pairing with B so idle can dim properly dark.

### J. Focus mode

Dim every display except the one holding the foreground window. The engine
already tracks the foreground process for app rules, so it knows.

Effort: small on top of B. Risk: low.

### K. GPU-level colour

ColorControl exposes NVIDIA/AMD dithering, colour depth and format. These are
vendor APIs (NVAPI, ADL) with real redistribution and stability questions, and
nothing about them is per-monitor in the way the rest of Umbra is.

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

Built as `DeviceSubmission` + `DeviceContribution` in `Umbra.Display/Devices`,
`umbra contribute`, and a card on the Displays page.

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
- **Keyed `<MFG>-<PRODUCT>`**, not Umbra's internal token - that ends in the
  serial.

Still open: prompting when an unknown monitor appears (must offer, never send),
and a maintainer-side path from issue to `devices/*.md`.
