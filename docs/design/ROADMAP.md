# DispCtrl — roadmap and decisions

A per-monitor display manager for Windows 11, built around OLED care on a
secondary panel. Native, minimal, MSIX-packaged for the Store.

> `DispCtrl` is a placeholder name. It is cheap to change now (namespaces + package
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
| Serial in EDID | **none reported** | `9XYZ7K1` |

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

One preset is one JSON file in `%LOCALAPPDATA%\DispCtrl\presets`. That is the
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
moved `DispCtrl.Display` into the engine's references: that assembly used to be
the app's alone, on the reasoning that every call in it is a deliberate user
action, and per-app rules made those same calls background work.

Verified end to end on this machine: the rule applied once on focus, reverted
once on loss, brightness 68 to 20 and back, no loop and no clobbered settings.
Foreground activation cannot be driven from a background script — Windows
refuses the focus steal — so the test matches on whatever genuinely holds the
foreground rather than trying to create it.

### The display report

`%LOCALAPPDATA%\DispCtrl\displays.log`, written by the engine at start and by the
Displays page on demand. Per display: identity and device path, connector,
geometry, true pixel density from the EDID against the DPI Windows renders at,
current mode, signal detail, every mode the driver reports grouped by
resolution, and what DispCtrl can actually drive — brightness and over which
backend, HDR, the scaling steps offered, the variable refresh range, colour
profile, whether gamma control works, wallpaper, and whether the taskbar can be
hidden there.

Written whole each time rather than appended: this is a snapshot of what is
attached now, and a growing file would bury the current answer under every
previous one. The point is that nearly every awkward display problem is a
question about what the hardware said it could do at the time, and by the time
it is worth asking, the moment has gone.

### What the monitor knows about itself

The report's real job is capability discovery, not diagnostics. Everything
Windows exposes about a display is a fraction of what the panel itself will
tell you: every DDC/CI monitor publishes an MCCS capabilities string naming the
VCP codes it implements and, for the discrete ones, the values it accepts.

Measured on the U2424H: 37 controls, of which 11 are worth offering — contrast,
colour preset (sRGB through 10000 K plus two user slots), red/green/blue gain,
sharpness, input source, OSD lock, OSD language, power mode. None of that is
reachable anywhere in Windows. The panel also reports its own firmware level,
controller type, sub-pixel layout and panel technology.

Three things this forced:

- **Discovery, not a fixed list.** The controls DispCtrl offers are built from
  what the panel reported, so it never shows a control the monitor in front of
  you does not have — and does show the ones it does.

- **An allow list for writing.** A capabilities string includes
  manufacturer-specific codes whose meaning is undocumented and differs between
  models. They are reported and never written; writing one to find out what it
  does is how a panel ends up in a state its own OSD cannot undo.

- **The low byte is the value.** A VCP reply is 16 bits and for a discrete
  control only the low byte carries meaning. This Dell answers 0x1111 for
  "input is HDMI 1"; matching the whole word finds nothing and the control
  reads as being on a setting it does not have.

The sweep is slow — 37 round trips is about 30 seconds — so the engine runs it
on a background task. Done inline it delayed taskbar management by that much at
logon, which is the one thing the engine exists for.

The file stays on the machine. It is the raw material for supporting hardware
this has never seen, and sending one should be something the user chooses,
not something that quietly already happened.

### Drawing the arrangement at real sizes

Windows draws its arrangement diagram in raw pixels, which makes a 14-inch
2880x1800 laptop panel look wider than the 24-inch monitor beside it. The
diagram's whole job is to show where things physically are, so DispCtrl draws it in
millimetres from the EDID.

The first attempt rescaled the coordinate space piecewise, interval by interval,
and got it wrong: where two panels of different density share a y interval, one
measurement has to win, and the laptop came out nearly twice its real height.
There is no mapping of a shared coordinate space that is right for both, because
a pixel is a different real size on each panel.

What works is to take only the *relationships* from pixel space. Sizes come
from the EDID and nothing else; each display is hung off a neighbour it touches,
on the side it touches, offset along that edge by the same fraction as in
pixels. Every panel is then its true size and still meets its neighbour, which
is what two monitors of different heights do on a real desk. The drag maths and
the snap threshold both convert through the dragged display's own density —
skipping that made a dense panel crawl under the cursor.

Covered in `tools/presetcheck`: true sizes, flushness, top alignment,
proportional offset, and the fall back to pixels when any display reports no
physical size.

### What nothing reports: panel technology

There is no reliable way to ask whether a panel is OLED. EDID has no field for
it. Windows exposes none. An external monitor can answer VCP B6 over DDC/CI —
the Dell says LCD (TFT) — but a built-in panel has no DDC/CI channel at all,
which is exactly the case that matters, because built-in OLEDs are what burn in.

So panel technology is reported where the monitor says, and OLED is a per-
monitor setting defaulting to what B6 said. Getting it wrong is not symmetric:
burn-in protection on an LCD is a pointless annoyance, its absence on an OLED is
permanent damage. Better to ask than to guess.

### Prior art worth knowing about

- **ddcutil** carries the most complete public VCP feature table there is
  (`src/vcp/vcp_feature_codes.c`), and its *user defined features* mechanism is
  the design worth copying for per-model quirks: a file named
  `<mfg>-<model>-<product>.mccs` naming the codes and values a particular model
  implements. That is the shape a "known models" store here should take.
- **linuxhw/EDID** is ~175,000 real EDIDs organised by vendor and model, which
  is the reference for anything to do with parsing or recognising panels.
- There is no comprehensive public database of *manufacturer-specific* VCP
  codes. ddcutil's position — record them, do not write them blind — is the
  same one taken here, and is the reason those codes are reported and never set.

### Software dimming, and what it costs

A panel that reports no brightness control can still be dimmed by scaling its
gamma ramp. It is not the same thing as turning a backlight down — the panel
emits as much light as before, the signal is just smaller, so contrast and
colour depth fall with it. The UI says so rather than presenting it as the same
control, and it is only offered where the real one is missing.

It shares the ramp with night light, which forced two things:

- **One writer, composing both.** Warmth and dimming are each a scale of the
  same ramp. Two callers each writing "their" ramp would simply overwrite one
  another, so they are applied together in a single write.

- **They compete for the range.** Windows refuses a ramp too far from the
  identity — the same clamp that caps warmth at 3300K. Measured on this
  hardware, the boundary is where the weakest channel reaches about half of
  identity: dimming alone is accepted to 50%, at 60% warmth only to 70%, and at
  full warmth not at all. So the slider's floor is computed from the current
  warmth and moves with it, and the card says why when it is not at the bottom.

Without that, the feature half-works silently: a refused ramp is not an error,
the previous one simply stays, and the slider appears to stop responding at an
arbitrary point.

### Read DDC/CI too fast and it lies

The channel gate stopped two callers colliding. It does not stop one caller
asking too quickly, and that turns out to be worse, because it does not fail.

Sweeping 37 VCP codes back to back, this Dell answered a brightness read with
24 while the panel was plainly at 62 — a reply belonging to a different
question. A preset captured from that sweep then wrote 24 back to the hardware,
so a wrong read became a wrong screen. MCCS asks for 40ms between messages and
means it; ddcutil carries per-model tuned delays for the same reason. There is
now a flat 40ms gap between reads.

Brightness is also excluded from the monitor-settings a preset captures. It has
its own field, read through the path the brightness slider uses, and carrying it
twice let one preset hold two values for one setting, free to disagree.

### Where a Windows setting belongs

Adaptive brightness and auto-rotation are Windows settings, not monitor ones,
and they were put in the page's global section on that reasoning. That was the
wrong call: both act on the built-in panel and nothing else — the ambient light
sensor drives the laptop's backlight, the accelerometer turns the laptop's
screen. They now sit on that display's card, worded so it is clear they are
Windows' and machine-wide.

Auto-rotation is settable now rather than a hand-off. There is no public API —
`SetAutoRotation` is not exported from user32 on this build — and the HKLM key
refuses a normal token, so the toggle shells out to Windows' own `reg.exe` with
the `runas` verb: one UAC prompt for that one write.

**Not** by making the app elevated. Everything else works from a normal token,
and a packaged app cannot request elevation at all, so running the panel as
administrator would trade the Store requirement for a single toggle.

### One monitor, one DDC/CI conversation

A monitor has a single DDC/CI channel and will not serve two conversations at
once. Two callers opening it together do not queue: the monitor answers one and
fails the other.

That is not theoretical — it was shipped and caught within the hour. Reading a
monitor's capabilities in parallel with reading its brightness, both reasonable
on their own, made the Dell report no brightness control *and* no capabilities
string, which reads exactly like a monitor that has stopped answering. It took a
direct probe outside the app, showing the channel perfectly healthy, to tell the
two apart.

Every DDC/CI call now goes through `DdcChannel`, which gates per device path —
one gate per monitor rather than one globally, because two monitors have two
independent channels and serialising across them would double a sweep for no
reason. The gate is patient (20s) on purpose: the thing most likely to be
holding it is a full capabilities sweep, and giving up early would put back the
failure it exists to prevent.

Worth remembering when adding anything else that talks to a monitor.

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
2-minute watchdog rather than fixed. **DispCtrl must not inherit this** — the
engine needs a supervisor path that does not depend on Task Scheduler.

### Measured CPU — and a corrected assumption

**The premise that the old watcher had a CPU problem was wrong.** The 85s of
CPU observed on it was *cumulative over ~17h of uptime*, i.e. roughly 0.14% of
one core — not a rate problem. Measured over a controlled 90s window, cursor
parked away from every managed edge, same job:

| | CPU (% of one core) | RSS |
|---|---|---|
| Existing watcher (.NET Framework, optimized) | **0.052%** | 20.6 MB |
| DispCtrl engine (.NET 10, Release, JIT) | **0.122%** | 32.1 MB |

So DispCtrl is currently ~2x the CPU and ~1.5x the memory, despite polling at
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
| **Repositioning `Shell_SecondaryTrayWnd`** | Windhawk (not Store — injects into explorer) | **Highest.** DispCtrl moves the window from outside rather than injecting, which is far milder, but it is still cross-process shell manipulation and has no direct Store precedent. |

**DispCtrl needs no elevation anywhere.** The last candidate was WMI
`WmiSetBrightness` for the internal panel, which the docs imply needs admin —
tested unelevated on this machine and it succeeded. So the MSIX needs
`runFullTrust` (routine for packaged desktop apps) but **not** `allowElevation`
(a restricted capability requiring separate approval). Worth re-testing if the
internal panel is ever swapped, as this may be driver-dependent.
