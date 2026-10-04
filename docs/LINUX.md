# DispCtrl for Linux

An early, X11-only Linux client: per-monitor brightness (DDC/CI monitors and
the internal panel's backlight) and gamma/colour-temperature control, as a
command line (`dispctrl-linux`) and a minimal desktop app
(`dispctrl-linux-gui`). It shares no code with the Windows app or engine —
see [`docs/design/LINUX-PORT.md`](design/LINUX-PORT.md) for why, and for the
much larger scope (taskbar-equivalent behaviour, hotkeys, night light,
presets, Wayland, Snap packaging) still ahead of it.

**What works today**: reading and setting external-monitor brightness over
DDC/CI, reading and setting the internal panel's brightness over
`/sys/class/backlight`, and reading display layout plus setting gamma ramps
and a software-dimming scalar over XRandR — on an X11 session (including
XWayland) only. The same commands are also reachable through a resident
process (`dispctrl-linux engine`) over a Unix domain socket, with a systemd
`--user` unit to run it — see "Engine" below.

**What does not exist yet**: taskbar hiding, global hotkeys, night light
scheduling, presets, per-app rules, OLED care, the device library, a
Wayland-native backend, and an actual Snap Store submission (a config sketch
exists; it has not been built or uploaded). Nothing here claims otherwise —
see "Every user-facing claim must be true of the hardware" in
`.claude/CLAUDE.md` / `.github/copilot-instructions.md`, which this follows
too.

## Build and run

```bash
dotnet build src/DispCtrl.Linux/DispCtrl.Linux.csproj -c Release
dotnet build src/DispCtrl.Linux.Gui/DispCtrl.Linux.Gui.csproj -c Release

dotnet run --project src/DispCtrl.Linux/DispCtrl.Linux.csproj -- doctor
dotnet run --project src/DispCtrl.Linux/DispCtrl.Linux.csproj -- displays
dotnet run --project src/DispCtrl.Linux.Gui/DispCtrl.Linux.Gui.csproj
```

Or through `build.sh`:

```bash
./build.sh linux-build
./build.sh linux-run cli displays
./build.sh linux-run gui
```

## Requirements

- An X11 session (Xorg, or a Wayland compositor's XWayland with the outputs
  actually present to XRandR — a pure Wayland session has none). `doctor`
  reports `XDG_SESSION_TYPE` and warns if it is not `x11`.
- `xrandr` (`x11-xserver-utils` on Debian/Ubuntu) for display layout and
  gamma.
- `ddcutil` (and `i2c-dev` loaded — most distributions load it automatically
  when `/dev/i2c-*` nodes exist) for external monitors' DDC/CI brightness. A
  monitor with no DDC/CI channel — every laptop's own panel — will never
  appear there; that is correct, not a bug (`ddcutil detect` reports it as
  `Invalid display`, same as on the Windows side's built-in panels).

## Backlight write permission

Writing `/sys/class/backlight/*/brightness` needs root, or a udev rule
granting the desktop session's user write access. Without one,
`dispctrl-linux brightness ... --backlight ...` and the GUI's matching
slider correctly refuse (exit code 1) rather than fail silently. A rule that
grants it, for a session in the `video` group:

```
# /etc/udev/rules.d/90-backlight.rules
SUBSYSTEM=="backlight", RUN+="/bin/chgrp video $sys$devpath/brightness", RUN+="/bin/chmod g+w $sys$devpath/brightness"
```

## Commands

```
doctor                                    check for xrandr, ddcutil, backlight, i2c
displays                                   list XRandR outputs, ddcutil monitors, backlight devices
brightness <0-100> --backlight <name>     write /sys/class/backlight/<name>/brightness
brightness <0-100> --ddc <display-num>     write VCP 0x10 (brightness) via ddcutil
brightness <0.0-1.0> --xrandr <output>     software dimming via xrandr --brightness (a gamma scalar, not hardware)
gamma <r> <g> <b> --xrandr <output>        set a gamma ramp scalar per channel, e.g. 1.0 0.9 0.8
```

Exit codes follow `dispctrl.exe`'s convention: 0 done, 1 refused, 2 asked
wrongly.

## Engine

`dispctrl-linux engine` runs the same commands as the CLI, but resident: it
listens on a Unix domain socket (`$XDG_RUNTIME_DIR/dispctrl-linux.sock`,
falling back to `/tmp` if that variable is unset) for newline-delimited JSON
requests and replies with newline-delimited JSON:

```
$ echo '{"args":["displays"]}' | socat - UNIX-CONNECT:$XDG_RUNTIME_DIR/dispctrl-linux.sock
{"exitCode":0,"stdout":"-- XRandR outputs --\n...","stderr":""}
```

It is the "Unix socket broker" from
[`docs/design/LINUX-PORT.md`](design/LINUX-PORT.md)'s "Engine and IPC"
section: a real, working broker for the commands the CLI already has, run
through the exact same code so there is one implementation, not two -
verified above via a raw Python socket client against real hardware
(`displays`, a malformed request, an unknown command and a missing target
all returned the correct exit code and JSON shape). It is **not** yet the
rest of `DispCtrl.Engine`'s job: no state reconciliation, no scheduling, no
presets, and nothing in this repository talks to it as a client yet (the CLI
and GUI both still call the same backends directly, in-process).

[`packaging/linux/systemd/dispctrl-linux-engine.service`](../packaging/linux/systemd/dispctrl-linux-engine.service)
is a systemd `--user` unit for it, giving "start at login, restart on
failure" for free. Verified end-to-end on this machine: installed with
`systemctl --user enable --now`, queried live over the socket, and
confirmed to auto-restart (new PID within about a second) after `kill -9`
on the running process.

## Snap packaging

[`packaging/linux/snap/snapcraft.yaml`](../packaging/linux/snap/snapcraft.yaml)
is a config sketch for Snap Store packaging - **not built or uploaded**;
`snapcraft` was not run this session. It documents the real blocker before
a strict-confinement build could work: no stock Snap interface grants
`/dev/i2c-*` write access (`hardware-observe` is read-only), so DDC/CI
brightness needs either a new/custom interface, `raw-usb`, or a
classic/devmode build in the meantime. See the file's own comments for the
rest of what it does not yet solve (Wayland, wiring the systemd unit up as
a snap daemon).

## Verified on real hardware (2026-09-27)

Ubuntu 24.04, X11 session, AMD internal panel (`eDP`, no DDC/CI) plus one
external monitor over DisplayPort — a Dell P2723DE (`DP-1-0` in XRandR,
`Display 1` in ddcutil, `/dev/i2c-5`).

- `displays`: XRandR output parsing and ddcutil detection both match raw
  tool output exactly.
- `brightness <0-100> --ddc 1`: full round-trip against the real Dell —
  read 70, wrote 55, confirmed 55 both on the panel and via `ddcutil`,
  restored to 70, reconfirmed.
- The GUI: renders the same data (a slider per DDC/CI monitor, per backlight
  device, per XRandR output), refuses the same way the CLI does on a
  permission error, and does **not** write a slider's own starting value back
  to the hardware on launch (see "Two bugs found by testing", below).
- Every error path (bad args, missing targets, nonexistent display,
  permission-denied backlight write) refused with the correct exit code.

### Two bugs found by testing against hardware, not fixtures or assumptions

- **`ddcutil getvcp`'s default output is prose, not its `--brief` form.**
  Without `--brief`, ddcutil prints
  `VCP code 0x10 (Brightness ): current value = 70, max value = 100`, which
  the parser never matched — every read would have silently returned
  nothing. Fixed by always passing `--brief`.
- **`ddcutil detect --brief` emits an `Invalid display` block for a bus with
  no working DDC/CI** (this desk's internal eDP panel: it has an EDID, but
  `DDC communication failed`). The parser only treated `Display N` as a new
  block boundary, so the eDP block's fields overwrote the preceding real
  monitor's model, serial and I2C bus in place. Fixed by treating
  `Invalid display` as a boundary that discards rather than merges.
- **The GUI's slider wrote the hardware's own reported value straight back
  to it on every launch** — the same trap `.claude/CLAUDE.md` documents for
  WinUI's two-way sliders ("every such binding needs a `_xxxReady` gate").
  `MonitorRowViewModel` now takes its initial value through a constructor
  parameter that bypasses the property setter, so only a real user-driven
  change calls the write path.

Both parsing bugs are exactly what `presetcheck`'s "verify against monitors
actually attached, not fixtures" rule exists to catch on the Windows side,
and the slider bug is exactly what its WinUI traps section warns about for
two-way bindings — the same discipline paid off here immediately.
