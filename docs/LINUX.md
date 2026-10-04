# DispCtrl for Linux

Each display's brightness, software dimming and a night light with a
schedule, from a command line (`dispctrl-linux`) and a window
(`dispctrl-linux-gui`), kept by a small engine that runs with your session.

It is early, and smaller than DispCtrl for Windows: no taskbar, hotkeys,
presets, OLED care or quick panel yet. Night light and dimming need an X11
session (see [Wayland](#wayland)). What follows is everything it does today.

| | How | Needs |
|---|---|---|
| An external monitor's own brightness | DDC/CI, through ddcutil | `ddcutil`, and a monitor with DDC/CI switched on in its menu |
| A laptop panel's brightness | the kernel backlight, through systemd-logind | nothing: the active session may set it |
| Software dimming, per display | the output's gamma ramp | an X11 session |
| Night light, with a schedule | the output's gamma ramp, 6500 K to 1900 K | an X11 session; the engine for the schedule |

## Install

**Debian, Ubuntu and derivatives** - download `dispctrl-linux_<version>_amd64.deb`
from the [releases](https://github.com/jesvijonathan/DispCtrl/releases):

```bash
sudo apt install ./dispctrl-linux_*_amd64.deb
systemctl --user start dispctrl-linux-engine    # now; it starts by itself at every login after
```

**Anything else (x86-64)** - the tarball installs for your account only,
without root:

```bash
tar xzf dispctrl-linux-*-linux-x64.tar.gz
cd dispctrl-linux-*-linux-x64
./install.sh                 # to ~/.local, and starts the engine
./install.sh --uninstall     # removes it; settings stay
```

Both are self-contained: no .NET needed. Then check what it can reach:

```bash
dispctrl-linux doctor
```

For external monitors, install `ddcutil` (`sudo apt install ddcutil`); its
package lets the logged-in user use the I2C buses. Laptop panels have no DDC/CI
channel - that is correct, not a fault; their backlight is used instead.

## Use

Open **DispCtrl** from the applications menu, or:

```
dispctrl-linux displays                      outputs, DDC/CI monitors, backlights, with current levels
dispctrl-linux brightness 60 --ddc 1         a DDC/CI monitor (the number from displays)
dispctrl-linux brightness 40 --backlight amdgpu_bl2
dispctrl-linux dim 0.7 --output DP-1         software dimming, 0.1 to 1
dispctrl-linux dim off --all
dispctrl-linux nightlight 60                 strength 0-100, and on
dispctrl-linux nightlight --from 20:00 --to 07:00
dispctrl-linux nightlight off
dispctrl-linux restore                       night light off, no dimming, every ramp back to normal
dispctrl-linux status                        what is on, and what every ramp holds
```

`dispctrl-linux help` lists everything. Exit codes are those of `dispctrl.exe`
on Windows: 0 done, 1 refused, 2 asked wrongly.

**If a screen is too dark or too orange**, `dispctrl-linux restore` puts
everything back, as does the window's **Restore** button. Dimming never goes
below 10%, so the controls stay readable.

## The engine

`dispctrl-linux-engine` is a systemd user service. It follows the night light
schedule (it wakes at the boundaries rather than polling), puts a ramp back
when a mode change or a monitor plugged in resets it (within ten seconds), and
when it stops - at logout, or `systemctl --user stop dispctrl-linux-engine` -
it puts every ramp it warmed or dimmed back to normal.

The command line and the window send their commands to it when it runs, so
there is one writer of the ramps and one queue in front of each DDC/CI
monitor; without it they do the work themselves, and everything except the
schedule still works.

```
systemctl --user status dispctrl-linux-engine
journalctl --user -u dispctrl-linux-engine     what it changed, and why
dispctrl-linux engine status
```

It listens on `$XDG_RUNTIME_DIR/dispctrl-linux.sock`, readable by your account
only. A request is one line of JSON, `{"args":["nightlight","on"]}`, and the
reply is `{"exitCode":0,"stdout":"...","stderr":""}`.

Settings live in `~/.config/dispctrl-linux/settings.json`, written whole and
renamed into place; the engine sees a change at once. A hand edit is fine:
values out of range are pulled back, and a file that is not JSON is set aside
as `settings.json.bad`.

## Things that get in the way

- **GNOME's own night light** writes the same ramps; whichever writes last
  wins. Switch one off (`doctor` says when GNOME's is on).
- **Redshift, gammastep or a calibration loader** - the same. DispCtrl leaves
  a ramp alone while its own night light and dimming are off.
- **DDC/CI switched off in the monitor's menu**, or a dock or KVM that does not
  pass it through: the monitor will not appear under DDC/CI in `displays`.
- **Backlight over SSH or on a second seat**: logind only lets the active local
  session set it. There, install `/usr/lib/udev/rules.d/90-dispctrl-backlight.rules`
  (the .deb does; `./install.sh --backlight-rule` for the tarball) and join the
  video group: `sudo usermod -aG video $USER`.

## Wayland

Wayland compositors keep gamma ramps to themselves, and there is no protocol
every one of them offers, so on a pure Wayland session night light and dimming
are unavailable and say so. Brightness - DDC/CI and the backlight - works
everywhere. Log in to an X11 session ("Ubuntu on Xorg", "Plasma (X11)") for
the rest. Per-compositor support is planned
([docs/design/LINUX-PORT.md](design/LINUX-PORT.md)).

## Building from source

```bash
./build.cmd setup              # the .NET 10 SDK into .tools/, if missing
./build.cmd linux-build        # the command line, the window and the checks
./build.cmd linux-run cli doctor
./build.cmd linux-run gui
./build.cmd test               # includes DispCtrl.Linux.Checks on Linux
./build.cmd linux-package      # the .deb and the tarball, in artifacts/linux-<version>/
```

`src/DispCtrl.Linux.Core` holds the hardware (ddcutil, the backlight, XRandR
ramps through libXrandr), settings, every command and the engine's protocol;
`src/DispCtrl.Linux` is the command line and the engine; `src/DispCtrl.Linux.Gui`
the Avalonia window; `build/packaging/linux` the packages, the systemd unit and
the udev rule. `tests/DispCtrl.Linux.Checks` needs no monitors and no X server:
it points X at a display that cannot answer, so it never changes a ramp. The
release workflow builds and attaches the Linux packages after the Windows ones.
The traps already paid for are in [TRAPS.md](developer/TRAPS.md#linux-client).

## Verified on

Ubuntu 24.04, an X11 GNOME session, an AMD laptop panel (`eDP`, backlight
`amdgpu_bl2`, no DDC/CI) and a Dell P2723DE over DisplayPort (`DP-1-0`,
DDC/CI on `/dev/i2c-5`), on 2026-10-04:

- DDC/CI brightness written and read back on the Dell, and restored.
- The laptop backlight set through logind by an account in neither the video
  nor the i2c group, and restored.
- Night light at strength 60 read back from the CRTC as 1.000 / 0.784 / 0.614;
  dimming at 80% on top as 0.800 / 0.627 / 0.491 - composed, not fighting.
- The engine: a schedule window opened and closed on the minute; a ramp reset
  by `xrandr` repaired on the next pass; SIGTERM put both ramps back and removed
  the socket; a second engine refused to start; the socket was 0600.
- The window, driven through the accessibility tree: switching night light on,
  a strength sweep, dimming and Restore each reached the hardware and the
  settings; opening it wrote nothing.
- The .deb's binaries and the tarball's `install.sh` / `--uninstall` against a
  scratch home. The .deb has not been installed system-wide on this machine.
