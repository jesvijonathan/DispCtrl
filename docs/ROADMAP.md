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

### Brightness: why unison has two modes

The multiplier came first, and it is only honest near the level it was
captured at. Halving an OLED already sitting at 30% takes it somewhere
unusable; halving an external at 90% is still bright. Worse, a baseline of
zero can never be scaled back up — the bug that made unison look like it only
drove the built-in panel.

The calibrated range answers that. Both limits are asked for once, by looking
at the panels side by side and deciding how dim and how bright each should go,
because there is nothing readable off the hardware that stands in for that
judgement. The slider then interpolates per display, so 0% and 100% mean the
same thing on panels whose numbers are nothing alike.

Measured on this machine: internal 30-90, external 20-70. Slider at 0/50/100%
landed them at 30/20, 60/45, 90/70; the internal figure confirmed through WMI
independently of the app.

Equal limits are rejected rather than accepted as a fixed level — it means the
display was not touched between the two capture steps, which is a mistake far
more often than an intention, and it would pin the panel at one brightness for
every slider position. Such a display falls back to the multiplier.

### Night light: gamma ramps, and who owns them

Windows' own night light has no public API — it lives in an undocumented
CloudStore blob whose shape changes between builds. Warmth is applied instead
by rewriting each display's gamma ramp, which is what every third-party tool
does: per-display, instant, unelevated, undone by restoring the previous ramp.

Two limits found by measurement rather than documentation:

- **GDI clamps how far a ramp may stray from the identity.** On this machine
  3280K is accepted and 3050K refused, on both panels. Unlocking the rest means
  writing `GdiIcmGammaRange` under HKLM, which needs elevation and is not
  something a Store app should do to a shared machine. So the slider spans
  6500K-3300K, which is the range that actually works. A control whose top
  third silently does nothing is worse than a shorter one.

- **A display has one gamma ramp and no way to say who set it.** The panel
  originally applied warmth as a live preview while the engine also applied it.
  Whichever read second captured the other's warmth as the display's *baseline*
  and restored to that, so switching night light off left the screens tinted,
  a little further every cycle. The engine now owns the ramp outright; the panel
  only writes settings, and the engine's watcher picks them up in ~120ms, which
  is fast enough to feel live under the cursor.

Two guards follow from the second point: a baseline whose blue peak sits more
than 10% under its red is rejected as somebody else's warmth rather than
adopted, and the engine sweeps for abandoned warm ramps at startup — a ramp
outlives the process that set it, so an engine that was killed rather than
stopped would otherwise leave the displays warm with no control admitting to it.

Verified end to end: unscheduled on, schedule excluding now, schedule including
now, off, and engine stop each land both panels exactly where expected, with a
clean return to 100/100/100 every time. Kill-then-restart recovers too.

### Presets

One preset is one JSON file in `%LOCALAPPDATA%\Umbra\presets`. That is the
whole sharing story: export is a copy, import is a paste, and a user who wants
to know what a preset will do can read it. A bundle format or a registry blob
would buy nothing and cost both of those.

Three decisions worth keeping:

- **Scope, not all-or-nothing.** The useful presets are narrow — "warm and dim
  for the evening" should not drag the resolution and wallpaper along with it.
  Each preset says which of seven aspects it controls, and the two that cost a
  mode switch (arrangement, modes) are the ones worth leaving out.

- **Capture records everything; scope decides what is applied.** Narrowing or
  widening a preset later therefore never means re-capturing.

- **Drift is listed, not flagged.** "Unsaved changes" on its own is a prompt the
  user cannot answer. Knowing it is the brightness that moved and not the
  resolution is the difference between confidently pressing Save and not daring
  to. Brightness needs a two-point tolerance: DDC/CI rounds what it is asked
  for, and without it a preset would sit permanently, uselessly dirty.

Matching is on the same replug-surviving token as settings, so a preset follows
the physical panel rather than the slot.

### Per-app presets, and what they cost

A rule is "when this app is in front, use this preset". The engine polls the
foreground window once a second — a WinEvent hook would put the engine in the
message path of every activation on the machine, for no gain — and a rule only
fires once its app has held the foreground for two seconds, because applying a
preset can blank the screen and alt-tabbing past an app must not do that.

Three bugs found building it, all of the same family:

- **Rules compared by reference.** Applying writes settings, the watcher reloads
  the file, and the reload builds new `AppRule` objects — so the "already
  active" guard never matched. It re-applied every tick, and each apply
  triggered the next reload. Now compared by process and preset name.

- **A stale persist closure.** The engine captured the settings instance it
  started with; persisting that wrote its old view back over whatever the user
  had just changed, silently undoing edits made in the panel. The callback now
  takes the settings object the apply actually ran against.

- **`NotSupported_COM`.** `PublishAot` switches off built-in COM interop, and
  the built-in panel's brightness goes through WMI (`System.Management`), which
  is built-in COM. Every rule that touched the laptop screen died. Re-enabled
  with `BuiltInComInteropSupport`.

**That last one is a debt, and it is now the thing standing between the engine
and Native AOT.** Paying it off means reaching WMI through source-generated COM
interfaces, the way `Wallpaper.cs` already reaches `IDesktopWallpaper`. Leaving
`PublishAot` set keeps the publish-time warning saying so. The same change also
moved `Umbra.Display` into the engine's references: that assembly used to be
the app's alone, on the reasoning that every call in it is a deliberate user
action, and per-app rules made those same calls background work.

Verified end to end on this machine: the rule applied once on focus, reverted
once on loss, brightness 68 to 20 and back, no loop and no clobbered settings.
Foreground activation cannot be driven from a background script — Windows
refuses the focus steal — so the test matches on whatever genuinely holds the
foreground rather than trying to create it.

### The display report

`%LOCALAPPDATA%\Umbra\displays.log`, written by the engine at start and by the
Displays page on demand. Per display: identity and device path, connector,
geometry, true pixel density from the EDID against the DPI Windows renders at,
current mode, signal detail, every mode the driver reports grouped by
resolution, and what Umbra can actually drive — brightness and over which
backend, HDR, the scaling steps offered, the variable refresh range, colour
profile, whether gamma control works, wallpaper, and whether the taskbar can be
hidden there.

Written whole each time rather than appended: this is a snapshot of what is
attached now, and a growing file would bury the current answer under every
previous one. The point is that nearly every awkward display problem is a
question about what the hardware said it could do at the time, and by the time
it is worth asking, the moment has gone.

### Preset defects found in review

Six, of which four could lose data or mislead:

- **`Rename` deleted the file it had just written.** A rename between two names
  that sanitise to one stem — anything differing only in characters the
  filesystem rejects, or in trailing whitespace — wrote the new file and then
  deleted it by the old name, which resolved to the same path. The preset was
  simply gone.

- **Save could overwrite a preset nobody mentioned.** The guard compared
  display names, but two names can resolve to one file. It now asks the
  filesystem.

- **Primary was set after positions.** The desktop origin *is* the primary
  display's top-left, so promoting a display re-bases every other coordinate —
  positions written first came out shifted by the offset between the old
  primary and the new one. Primary now goes first.

- **Orientation was captured and never applied.** The field existed, the scope
  text promised it, capture always wrote zero and apply ignored it, so a
  rotated display came back the wrong way up. `DisplayInfo` now carries the
  orientation from the mode, and it is captured, applied and diffed.

The other two are responsiveness rather than correctness: the drift check and
Save both ran a capture — a DDC/CI read per external monitor plus a wallpaper
COM call — on the UI thread, which froze the window every time the bar
re-checked itself. Both are off-thread now, with overlapping drift checks
collapsed rather than queued so a debounce cannot stack a backlog against a
slow monitor. In the engine, preset applies are serialised under their own lock
rather than the settings lock, so a topology change cannot stall settings
reloads for the seconds it takes.

`tools/presetcheck` covers the awkward cases directly — colliding names, rename
onto the same file, import never overwriting, scope honoured by the diff,
brightness tolerance, absent displays, schedules crossing midnight, and app
rule matching. 24 checks.

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
