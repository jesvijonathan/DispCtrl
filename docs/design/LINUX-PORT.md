# A Linux port, for Snapstore distribution

This lays out what has to change for DispCtrl to run on Linux, in what order,
and what does not survive the move at all. The client shipped in
`src/DispCtrl.Linux*` (see `docs/LINUX.md`) covers steps 1-3 below; everything
past that is still only this plan. Written against the architecture in
`.claude/CLAUDE.md` / `.github/copilot-instructions.md` — read that first.

## Why this is a port, not a build flag

Every layer below `DispCtrl.Core` talks to a Windows-only surface: DDC/CI
through Dxva2/`Monitor_Dxva2Functions`, mode and topology changes through CCD
(`QueryDisplayConfig`/`SetDisplayConfig`), brightness for built-in panels
through WMI, gamma ramps through GDI, taskbar and window placement through
Explorer's window classes and `SetWindowPos`/WinEvent hooks, and the panel
itself through WinUI 3 (which cannot even *build* off Windows — `GenXbf.dll`).
None of that exists on Linux, under any display server. This is a rewrite of
`DispCtrl.Display`, `DispCtrl.Engine`'s hooks, and `DispCtrl.App`'s toolkit,
sharing only the parts of `DispCtrl.Core` that are pure model/geometry (presets,
arrangement solving, settings schema).

## What ports as-is

- `DispCtrl.Core`: display model, settings schema, preset diff, physical
  arrangement geometry (`PhysicalLayout`, `ArrangementSolver`), gamma-curve
  math. No hardware calls — already platform-neutral .NET.
- `DispCtrl.Control`'s JSON command shape and the `dispctrl` CLI surface — the
  command vocabulary (`displays`, `brightness`, `nightlight`, `preset apply`,
  ...) is a good fit for a Linux daemon + CLI split too. The transport (named
  pipe) does not port; see below.
- `presetcheck`'s pure-geometry assertions (arrangement, diff, schedules).
- The device library's *data* (`devices/BRAND/PRODUCT/*.json`) and its schema —
  DDC/CI VCP codes are the same standard on both platforms. `devicecheck`
  itself is already cross-platform .NET (the repo's CI already runs it on
  Linux for the intake pipeline).

## What each layer needs instead

### DDC/CI (`DispCtrl.Display`)

Linux has no Dxva2 equivalent in the kernel; DDC/CI goes over `/dev/i2c-*`
via the `i2c-dev` kernel module, exactly what **ddcutil** already wraps.
Two real choices:
- **Shell/link against ddcutil** (`libddcutil`, LGPL) rather than
  reimplementing VCP framing, retries and the 40 ms inter-message gap this
  project already had to learn the hard way — ddcutil has paid the same tax.
  A C# binding is a P/Invoke layer over `libddcutil.so`.
- Internal panels (eDP) still have **no DDC/CI channel**, matching this
  project's own machine table. Brightness there is
  `/sys/class/backlight/*/brightness`, not WMI — a sysfs write, gated by
  udev permissions (needs a udev rule or a setgid helper; there is no
  unelevated-by-default story here the way Windows session brightness is).

### Display configuration (mode, position, primary)

CCD's `SetDisplayConfig` has no analogue. Two backends, not one, because the
project cannot assume a compositor:
- **X11**: XRandR (`libxrandr`/`xrandr` protocol) — mode, position, primary,
  broadly matches the CCD model DispCtrl already targets (explicit positions,
  a primary, per-output modes).
- **Wayland**: no cross-compositor protocol for this. Each compositor exposes
  its own (GNOME Mutter's D-Bus `org.gnome.Mutter.DisplayConfig`, KDE's
  `kscreen`/KWin D-Bus, wlr-based compositors' `wlr-output-management-v1`).
  Realistically this means **three backends** behind one interface, or
  **X11-only for v1** and Wayland later — most distros still run an XWayland
  session under the hood but XRandR calls there do not reach the real outputs.

### Gamma / night light / dimming

`GdiIcmGammaRange` and the identity-ramp clamp this project spent real effort
mapping do not exist on Linux. XRandR carries per-CRTC gamma ramps directly
(`XRRSetCrtcGamma`), with no clamp to fight — likely *simpler* here, not
harder. Wayland has no ramp protocol; `wlr-gamma-control-unstable-v1` covers
wlroots compositors, GNOME and KDE again need their own path (GNOME's night
light is already built in and not controllable externally in the way this
project overrides Windows'). The shade-overlay idea already on this project's
list (from MonitorControl-mac) becomes the portable fallback everywhere a
ramp protocol is missing.

### Taskbar hiding, window pinning, gathering, hotkeys

These assume Explorer's `Shell_TrayWnd` and Win32 WinEvent hooks, both
meaningless on Linux. There is no equivalent concept of "the taskbar" across
desktop environments (GNOME has none by default, KDE's Plasma panel and
XFCE's panel are configured differently, and only some expose an auto-hide
D-Bus/config surface). Pinning windows on top and gathering are compositor
window-manager operations — the closest cross-DE mechanism is **EWMH**
(`_NET_WM_STATE_ABOVE`, `_NET_WM_STATE_STICKY`) on X11 via `libwnck`/direct
Xlib, with no Wayland equivalent at all (Wayland deliberately gives clients no
window-placement authority; only the compositor can do this, so "gather
windows" would need a GNOME Shell extension or a KWin script per DE).
Global hotkeys have the same split: X11 (`XGrabKey`) is straightforward;
Wayland needs the desktop portal's `GlobalShortcuts` interface
(`org.freedesktop.portal.GlobalShortcuts`), which is opt-in per compositor.
**This is the area with the least likely payoff** — much of it may have to
ship as "X11 only, degrades gracefully under Wayland" for a long time.

### The UI (`DispCtrl.App`)

WinUI 3 does not exist on Linux. Candidates, given the Core layer is already
.NET: **Avalonia** (closest to a drop-in replacement conceptually — XAML-like,
runs on .NET, ships on Linux/Snapstore already for other apps) is the
pragmatic choice over a GTK/Qt rewrite, which would mean leaving .NET for the
UI layer entirely. The quick-panel window behaviour (cloak-then-slide,
clip-to-taskbar-edge, notification-area anchoring) is Windows-DWM-specific and
would need a from-scratch design for Linux's tray protocols
(`org.kde.StatusNotifierItem`, which GNOME needs an extension for).

### The engine and IPC

A resident process is the right model on Linux too, but as a **systemd user
service** (`packaging/linux/systemd/dispctrl-linux-engine.service`) rather
than a scheduled task, giving equivalent "start at login, restart on
failure" semantics for free. The named-pipe command broker becomes a **Unix
domain socket** in `$XDG_RUNTIME_DIR` — implemented as `dispctrl-linux
engine`, see `docs/LINUX.md` "Engine" for the request/response shape and
its verification. It reuses `dispctrl-linux`'s own command dispatch rather
than a JSON command shape shared with the Windows engine's named pipe -
unifying the two wire formats is future work, not done here.

## Snap packaging

Once there is a Linux build at all, packaging it is the easy part:
- `snapcraft.yaml` with a `core22`/`core24` base, `dotnet` extension or a
  self-contained publish (matches how `DispCtrl.Engine`'s AOT ambitions
  already point).
- `libddcutil`, `libxrandr`, `libx11` as stage-packages.
- **`hardware-observe`** and a custom interface (or `raw-usb`/`i2c` slot) for
  `/dev/i2c-*` access — DDC/CI needs a plug users must connect explicitly
  (`snap connect`), the same trust boundary Windows solves with "never
  elevated" but Snap solves with confinement instead.
- `desktop`, `desktop-legacy` (or `wayland`) interfaces for the UI.
- A `daemon: simple` app entry for the engine service instead of the
  scheduled-task dance in `StartupIntegration`.
- Store listing, screenshots and confinement level (strict vs. classic) are
  the last step, not a blocker for anything above.

## Status

`src/DispCtrl.Linux*` covers steps 1-4 of the order below: DDC/CI wrapping
`ddcutil`, mode/gamma over XRandR, internal-panel brightness over
`/sys/class/backlight`, an Avalonia shell wired to those same backends, and
a resident Unix-socket command broker with a systemd `--user` unit. It
shares no code with `DispCtrl.Core`/`DispCtrl.Display` — both still target
`net10.0-windows10.0.26100.0`. Steps 1-2 are fully verified against real
hardware (a Dell P2723DE over DDC/CI, an AMD internal eDP panel with none),
including a full brightness round-trip through `ddcutil` and two real parsing
bugs found and fixed by testing against actual `ddcutil` output rather than
its documented format alone. The Avalonia shell (step 3) is verified against
the same hardware via screenshot, and had its own real bug found and fixed
the same way (an unwanted echo-write of brightness back to hardware on
startup, the same class of bug `.claude/CLAUDE.md` warns about for WinUI's
two-way bindings). The engine (step 4) is verified end-to-end: installed as
a real systemd `--user` service, queried live over its socket against real
hardware, and confirmed to auto-restart after a `kill -9`. A `snapcraft.yaml`
config sketch exists for step 6 but has not been built or uploaded. See
`docs/LINUX.md` for the full scope, requirements and verification report.
Everything past step 4 (taskbar/hotkeys, Wayland backends, an actual Snap
Store submission) is still only this plan.

## Recommended order

1. ~~Spike DDC/CI over `libddcutil` against the same two monitors this project
   already has ground truth for~~ — done, via the `ddcutil` CLI rather than
   `libddcutil` directly (see `docs/LINUX.md`); binding the library instead of
   shelling out remains future work.
2. ~~XRandR mode/position/primary + gamma ramps~~ — done; `dispctrl-linux`
   gives a working CLI on X11, mirroring how this project's own CLI shipped
   before the panel did.
3. ~~Avalonia shell wrapping the same backends~~ — done (`DispCtrl.Linux.Gui`);
   not yet a redesign of the WinUI app's pages, one window with a slider per
   writable brightness/dimming target.
4. ~~Engine as a systemd user service, Unix socket broker~~ — done
   (`dispctrl-linux engine`, `packaging/linux/systemd/`); the broker reuses
   the CLI's own command dispatch, but nothing in this repository speaks to
   it as a client yet, and it does no reconciliation or scheduling of its own.
5. Taskbar/pin/gather/hotkeys, X11 first, explicitly marked unsupported under
   Wayland until a per-DE path is written.
6. Snap packaging and Store submission — a `snapcraft.yaml` sketch exists
   (`packaging/linux/snap/`) but is unbuilt; its biggest open problem is that
   no stock Snap interface grants `/dev/i2c-*` write access for DDC/CI.

Steps 1–4 are done and hardware-verified; steps 5–6 are still ahead.
