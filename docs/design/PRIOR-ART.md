# Prior art

Other projects in DispCtrl's territory, and what was taken from each.
[FEATURES.md](FEATURES.md) has the details per feature.

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
