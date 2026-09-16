# Umbra — roadmap and decisions

A per-monitor display manager for Windows 11, built around OLED care on a
secondary panel. Native, minimal, MSIX-packaged for the Store.

> `Umbra` is a placeholder name. It is cheap to change now (namespaces + package
> identity) and expensive after Store submission, so decide before we submit.

## Target setup (the machine this is built against)

| | Internal | External |
|---|---|---|
| Panel | Samsung OLED `SDC-4154` | `DELL U2424H` |
| Mode | 2880x1800 @ 90Hz, 200% DPI | 1920x1080 @ 120Hz, 100% DPI |
| Role | **secondary** — taskbar hidden, black | **primary** — normal taskbar |
| Attach | Internal (eDP) | HDMI |
| Brightness | WMI only (no DDC/CI) | DDC/CI (verified: 0-100) |
| Refresh options | 60 / 90 | 50 / 59 / 60 / 100 / 119 / 120 |
| Serial in EDID | **none reported** | `3QQQ2X3` |

Hybrid GPU: RTX 3050 Laptop + AMD Radeon iGPU. The iGPU drives the internal
panel. **Anything drawing continuously must stay off the dGPU** or it wakes the
3050 and destroys battery life — this is the main constraint on the widget layer.

## Stack decisions

| Decision | Choice | Why |
|---|---|---|
| Language | C# / .NET 10 | WinUI 3 is the native UI stack regardless of language; C++/WinRT renders the identical UI for 3-5x the interop code. |
| Architecture | Split process | The 24/7 part is what must be light. Isolating it lets it be AOT with no XAML. |
| Engine | Native AOT, no XAML | AOT's weakness is XAML/reflection; the engine has neither, so it gets full AOT benefit. |
| UI | WinUI 3, on demand | Launched when you open settings, exits after. Never resident. |
| Interop | CsWin32 source generator | Hand-written `DllImport` is where interop bugs live — a wrong struct packing reads as a crash three layers away. |
| COM | `[GeneratedComInterface]` | ComWrappers, not legacy COM interop. AOT-safe. |
| Autostart | MSIX `windows.startupTask` | Store-legal, and user-visible in Settings → Apps → Startup. Replaces the Task Scheduler hack. |
| Packaging | MSIX, hand-authored manifest | Two executables in one package needs more than single-project MSIX gives. |

**Verified**: WinUI 3 builds with only the .NET 10 SDK — no Visual Studio.
**Outstanding**: Native AOT needs the MSVC linker at *publish* time. `dotnet build`
and `dotnet run` work without it, so development is unblocked.

## Knowledge carried over from the existing watcher

Hard-won in the previous session; do not regress any of these.

1. **Park the bar fully off-monitor**, at `monitor.Bottom` — not a 1px sliver.
   Native auto-hide leaves a sliver because it needs the window to catch the
   hover; reveal here is driven by cursor position, so no pixel need be lit.
   The sliver measured `#434343` across 75% of the width — a permanently-lit
   grey line, which is burn-in bait on OLED.
2. **`SPI_SETWORKAREA` with `winIni = 0`.** Broadcasting `WM_SETTINGCHANGE`
   makes explorer recompute and fight back in a flicker loop.
3. **Never adopt a bar that maps to the primary monitor.** While explorer is
   still building its taskbars a secondary bar briefly sits at a default
   position resolving to primary; acting on it hides the primary taskbar.
4. **Fingerprint the monitor layout every pass.** Taskbar window handles do not
   reliably die on replug, so handle-staleness alone misses it.
5. **Re-read geometry every pass.** The windows survive resolution, DPI and
   taskbar-size changes, so staleness never catches those either.
6. **Keep the bar out while a taskbar flyout has focus** (`Shell_TrayWnd`,
   `TopLevelWindowForOverflowXamlIsland`, `Xaml_WindowedPopupClass`, …), or
   clicking a tray icon yanks the bar away mid-interaction.
7. **Catch exceptions per loop iteration.** A transient Win32 failure during a
   display change must not kill the watcher — that is exactly when it matters.
8. **Ease-out cubic slide**, ~120fps while animating, `timeBeginPeriod(1)` held
   only during the slide. Force `HWND_TOPMOST` only on the frame it starts
   appearing; re-asserting every frame churns z-order for nothing.

### Known open issue

The current watcher exited with code 1 on 09/14 and nothing restarted it; root
cause was never found (no logging existed at the time). Mitigated with a
2-minute watchdog rather than fixed. **Umbra must not inherit this** — the
engine needs a supervisor path that does not depend on Task Scheduler.

### Measured CPU — and a corrected assumption

**The premise that the old watcher had a CPU problem was wrong.** The 85s of
CPU observed on it was *cumulative over ~17h of uptime*, i.e. roughly 0.14% of
one core — not a rate problem. Measured over a controlled 90s window, cursor
parked away from every managed edge, same job:

| | CPU (% of one core) | RSS |
|---|---|---|
| Existing watcher (.NET Framework, optimized) | **0.052%** | 20.6 MB |
| Umbra engine (.NET 10, Release, JIT) | **0.122%** | 32.1 MB |

So Umbra is currently ~2x the CPU and ~1.5x the memory, despite polling at
10 Hz where the old watcher polls at 25 Hz. It does strictly less work per
second, so this is runtime overhead — JIT, tiered compilation, GC and
finalizer threads — not the loop. **Native AOT is the lever**, and it is
untested until the MSVC toolchain exists.

Both figures are negligible in absolute terms (0.047s vs 0.109s of CPU over
90 seconds). Shorter windows were pure noise: two consecutive 30s runs put the
same old watcher at 0.104% and 0.260%. Do not tune against a window under ~60s.

The adaptive loop is kept because it is the right shape, not because it was
a measured win:

| State | Poll | Work per tick |
|---|---|---|
| Idle, cursor far from a managed edge | 100ms | `GetCursorPos` only |
| Cursor within 300px of a managed edge | 16ms | full check |
| Bar shown | 40ms | full check + sticky-class test |
| Animating | 8ms | position update |

Two performance bugs were found and fixed while measuring, both worth
remembering:

1. `Rescan` called full display enumeration every second — a CCD query plus a
   registry EDID read per monitor — to recompute an identity that only changes
   when the layout does. Now gated behind a cheap probe, with EDID memoized.
2. That cheap probe first used `EnumDisplaySettingsEx`, which queries the
   *display driver* (~3ms/call). Replaced with `EnumDisplayMonitors`, whose
   callback is handed each rectangle from data the kernel already holds.

## Build order

- [x] **0. Foundation** — stable per-monitor identity
  - [x] CCD chain: HMONITOR → GDI name → device path → friendly name → connector
  - [x] EDID parse from registry (model, serial) — AOT-safe, no WMI
  - [x] `DisplayKey` with serial-aware matching
  - [x] Verified against real hardware, cross-checked with an independent probe
- [x] **1a. Taskbar engine** — verified end to end on hardware
  - [x] Per-monitor opt-in, any edge, adaptive polling, graceful restore
  - [x] `stop` via named event — a kill would strand a bar off-screen
  - [x] Live settings reload; only bars that stop being managed are restored,
        so an unrelated change cannot flash every taskbar into view
  - [x] Gives up on a bar that will not move, with an explanation in the log
- [x] **1b. WinUI 3 panel** — NavigationView rail, Displays/Engine/Help/About/Settings
- [x] **1c. Display control** — brightness (DDC/CI + WMI), resolution, refresh rate
- [ ] **1d. OLED core** ← current
  - [ ] Per-monitor wallpaper via `IDesktopWallpaper` (`[GeneratedComInterface]`)
  - [ ] Pixel shift, OLED pixel-refresh cycle, idle blackout
  - [ ] Port the watcher into the engine with adaptive polling
  - [ ] Per-monitor opt-in (not just "all secondaries")
  - [ ] Taskbar on any edge, not only bottom
  - [ ] Per-monitor wallpaper via `IDesktopWallpaper` (`[GeneratedComInterface]`)
  - [ ] Pixel shift, OLED pixel-refresh cycle, idle blackout
  - [ ] Settings persistence keyed on `DisplayKey.ToToken()`
- [ ] **2. Packaging** — MSIX, `startupTask`, self-signed for sideload
- [ ] **3. Deeper display control** — the parts not yet built
  - [ ] HDR / SDR toggle — `DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE`
  - [ ] Colour profile — needs the WCS APIs (`WcsGetDefaultColorProfile`)
  - [ ] Input source switching — `VCP 0x60`, confirmed supported on the Dell
  - [ ] Bit depth / link rate — read-only; no public API, needs the
        DisplayPort/HDMI info the driver exposes per-vendor
  - [ ] Wireless display detail — Miracast state is not in the CCD API
  - [ ] **Per-monitor scaling.** Reading it is easy (`GetDpiForMonitor`);
        *setting* it has no public API — every tool doing it calls
        `DisplayConfigSetDeviceInfo` with the undocumented
        `DISPLAYCONFIG_DEVICE_INFO_SET_SCALING` (-4). That is exactly the kind
        of call that fails Store certification, so it stays read-only unless we
        accept sideload-only for that one feature.
- [ ] **5. Widget wallpaper layer** — `WorkerW`, Composition (not XAML),
      click-through toggle, pixel-shifted. Must stay on the iGPU.
- [ ] **6. Taskbar translucency / blur** — `SetWindowCompositionAttribute`
- [ ] **7. Store submission**

## Store risk register

Precedent matters more than policy text here — these all ship on the Store today.

| Feature | Precedent | Risk |
|---|---|---|
| Taskbar blur | TranslucentTB (C++/WinRT, WinUI 3) | Low |
| Wallpaper layer widgets | Lively Wallpaper (C#, WinUI 3) | Low |
| DDC/CI brightness | Monitorian, Twinkle Tray | Low |
| **Repositioning `Shell_SecondaryTrayWnd`** | Windhawk (not Store — injects into explorer) | **Highest.** Umbra moves the window from outside rather than injecting, which is far milder, but it is still cross-process shell manipulation and has no direct Store precedent. |

**Umbra needs no elevation anywhere.** The last candidate was WMI
`WmiSetBrightness` for the internal panel, which the docs imply needs admin —
tested unelevated on this machine and it succeeded. So the MSIX needs
`runFullTrust` (routine for packaged desktop apps) but **not** `allowElevation`
(a restricted capability requiring separate approval). Worth re-testing if the
internal panel is ever swapped, as this may be driver-dependent.
