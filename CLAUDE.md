# Umbra

Per-monitor display management for Windows 11. A resident engine plus an
on-demand WinUI 3 panel.

This file is the handover: architecture, the traps already paid for, the
commands that work, and how to verify a change on real hardware. `docs/ROADMAP.md`
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
| Taskbar | hidden by Umbra | Windows' own auto-hide |
| DDC/CI | none — built-in panels have no channel | 37 VCP controls, 11 offered |

Token identities: `SDC-4154-E2387367` (internal), `DEL-A234-3QQQ2X3` (Dell).

**Leave the desk as you found it.** Brightness 68% internal / 62% Dell, night
light off, Dell contrast 75. Several bugs in this project's history were caused
by tests that changed hardware state and did not restore it.

---

## Architecture

```
Umbra.Core      no hardware writes. Display enumeration, EDID, settings,
                presets (model + diff), gamma ramps, arrangement geometry.
Umbra.Display   every call that changes something: DDC/CI, CCD writes, modes,
                wallpaper COM, power scheme. Referenced by BOTH app and engine.
Umbra.Engine    resident. Taskbar hiding, night light + software dimming,
                per-app preset rules. Native AOT intended (see debt below).
Umbra.App       WinUI 3 panel, launched on demand, exits after.
Umbra.Cli       `umbra.exe`, console subsystem. Every feature, scriptable.
```

The two processes share **only** `settings.json`. The engine watches it with a
`FileSystemWatcher` (120 ms debounce). There is no IPC.

`Umbra.Display` used to be the app's alone, on the reasoning that every call in
it is a deliberate user action. Per-app preset rules made those same calls
background work, so the engine references it too.

### State on disk

```
%LOCALAPPDATA%\Umbra\settings.json     shared, engine watches it
%LOCALAPPDATA%\Umbra\engine.log        engine's rolling log
%LOCALAPPDATA%\Umbra\displays.log      display report, rewritten whole
%LOCALAPPDATA%\Umbra\presets\*.json    one file per preset, name = file stem
```

---

## Commands

Run everything from the repo root. There is no solution file; build projects
individually, in dependency order when several changed.

```bash
dotnet build src/Umbra.Core/Umbra.Core.csproj       -c Release -v q --nologo
dotnet build src/Umbra.Display/Umbra.Display.csproj -c Release -v q --nologo
dotnet build src/Umbra.Engine/Umbra.Engine.csproj   -c Release -v q --nologo
dotnet build src/Umbra.App/Umbra.App.csproj         -c Release -v q --nologo
```

**A running process locks its DLLs and the build fails with MSB3027.** Stop both
first — and stop the engine *gracefully*, or it leaves a taskbar parked
off-screen:

```powershell
& ".\src\Umbra.Engine\bin\Release\net10.0-windows10.0.26100.0\win-x64\Umbra.Engine.exe" stop
Get-Process Umbra.App -EA SilentlyContinue | ForEach-Object { $_.Kill() }
```

The engine unwinds asynchronously; wait for the process to disappear before
building. **Killing the engine is what strands a taskbar off-screen** — only the
app may be killed outright.

Restart the engine through its scheduled task, which is how it normally runs:

```powershell
Start-ScheduledTask -TaskName 'Umbra.Engine'
```

Engine CLI: `displays`, `enable <n>`, `disable <n>`, `status`, `run [--for <s>]
[--trace]`, `stop`.

`umbra.exe` is the scriptable front end — `umbra help` lists everything. It is a
**console** subsystem app, unlike the engine: a WinExe does not block the shell
that launched it, so a script gets neither output nor an exit code. That is why
there are two binaries rather than one.

It also carries an `app.manifest` declaring PerMonitorV2. Without it the process
sees virtualised coordinates, `MonitorFromPoint` resolves the wrong monitor, and
the symptoms are baffling rather than obvious: a 200% panel reports 100%, and a
monitor with a working contrast control reports none.

```
umbra displays
umbra brightness -10 --all
umbra nightlight 60 --from 20:00 --to 07:00
umbra input "DisplayPort 1" --display 2
umbra preset apply Evening
```

Exit codes: 0 done, 1 refused, 2 asked wrongly.

### Hotkeys

Global shortcuts live in `settings.Hotkeys` and are registered by the **engine**,
because a panel that registered them would lose them on closing — the opposite
of what a global shortcut is for. The Hotkeys page only edits the list.

`RegisterHotKey` delivers `WM_HOTKEY` to the queue of the thread that
registered it, and the engine polls rather than pumping, so `HotkeyService` owns
a thread with a message pump of its own. Registration **and** unregistration
must both happen on that thread; releasing from elsewhere silently does nothing
and leaves the combination held until the process exits. `MOD_NOREPEAT` is set,
or holding a shortcut walks brightness to an end stop.

Autostart and the Start menu entry are managed by `tools/Umbra.ps1`
(`-Install`, `-Uninstall`, `-Status`, `-AddShortcut`, `-RemoveLegacy`). It is
interim; MSIX `windows.startupTask` replaces it.

### Tests

```bash
dotnet run --project tools/presetcheck/presetcheck.csproj -c Release
```

39 assertions over preset store edge cases, the diff, night-light schedules,
app-rule matching and the physical arrangement layout. Exit code is the failure
count. **Add to this rather than writing throwaway probes** — several probes in
this project's history should have been checks here.

---

## Traps already paid for

Every one of these was a real bug. Do not reintroduce them.

### WinUI

- **`SettingsExpander.Items` accepts only card-like children.** A nested
  `SettingsExpander`, or a bare `InfoBar`, does not render badly — it **takes the
  process down** when the item is realised. This has cost three crashes. Use a
  `SettingsCard` with `ContentAlignment="Vertical"`.
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
- Windows sizes its own arrangement tiles by **raw pixel count**. Umbra
  deliberately does not — see `PhysicalLayout`.
- `IDesktopWallpaper` is a **local** COM server: `CLSCTX_ALL`, not
  `CLSCTX_INPROC_SERVER`.
- The **primary taskbar cannot be moved** — `SetWindowPos` returns true and
  Explorer restores it in ~120 ms. Umbra uses Windows' own global auto-hide
  there instead.

### Presets

- The preset name **is** the file stem. Two names can sanitise to one file, so
  compare files (`PresetStore.SameFile`), not names — `Rename` used to delete the
  file it had just written.
- Capture records everything; **scope decides what is applied**. Narrowing a
  preset later must never require re-capturing.
- Brightness is captured in its own field, not as VCP `0x10`, or a preset holds
  two values for one setting.
- Drift is **listed, not flagged** — "unsaved changes" alone is a prompt nobody
  can answer. Brightness needs a two-point tolerance or DDC/CI rounding leaves a
  preset permanently dirty.
- Compare app rules **by value**. The settings watcher hands back fresh objects,
  so a reference comparison never matches: the rule re-applied every tick and
  each apply triggered the next reload.
- The engine must persist **the settings object the apply ran against**, never
  one captured at startup — that wrote its stale view back over the user's edits.

### Settings file

- Enums serialise as **names**, via `UseStringEnumConverter`. The file is
  hand-edited routinely; `"brightnessDown"` says what it does and a number
  silently means something else the moment a value is inserted into the enum.
  Before that was set, a hand-written action name made the whole file
  unparseable — it was quarantined as `settings.json.bad`, the engine fell back
  to defaults, and the symptom was hotkeys simply never firing.

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
  engine log or a small console project referencing `Umbra.Core`.
- Nested `SettingsExpander` children realise lazily: scroll the page top to
  bottom, expanding at each step, or UIA will not find inner controls.
- A capabilities sweep takes ~30 s (37 round trips at 40 ms plus reads). Wait for
  it before asserting the controls are missing.

---

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
drag/apply, presets with per-app rules, monitor capability discovery and control,
display report, identify overlays, hotplug re-discovery.

Outstanding, roughly in the order last discussed:

See `docs/FEATURES.md` for the full candidate list with effort and risk. The
short version, in recommended order:

1. ~~Brightness fallback, high-level to VCP `0x10`~~ — **done**.
2. ~~Lift the gamma clamp~~ — **done**, `GammaRange`.
3. ~~Full command line~~ — **done**, `umbra.exe`.
4. ~~Hotkeys~~ — **done**, engine-registered, with a Hotkeys page.
5. **Combined brightness** — one slider spanning hardware above a switching
   point and software dimming below it.
2. **Presets capturing everything** — identity, serial, remaining read-only
   state; apply replaces wholesale.
3. **Persistent known-monitor cache** — survive restarts, keyed on model+serial.
   Copy ddcutil's `<mfg>-<model>-<product>` convention.
4. **More fields in the display report.**
5. **Opt-in contribution toggle** — consent plus a staged anonymised bundle. Do
   not wire it to a destination that does not exist.
6. **OLED burn-in protection** — original scope, still unbuilt. The per-monitor
   `IsOled` flag exists and is what it should key off.
7. **Remember window positions** across replug.
8. MSIX packaging; Native AOT (blocked, see above); widgets; taskbar
   translucency.

## Reference implementations

Cloned outside the repo at `../refs/` (not tracked, re-clone with
`git clone --depth 1`). Read before designing anything in their territory —
`docs/FEATURES.md` lists what was taken from each and why.

| Repo | Language | Worth reading for |
|---|---|---|
| `twinkle-tray` | Electron | ambient light (per-monitor lux ranges, and a **simulated sensor** so it is testable), CLI shape, schedules, idle dimming |
| `Monitorian` | C#/WPF | closest domain; high-level/low-level brightness fallback, per-monitor ranges, per-device quirk handling |
| `MonitorControl-mac` | Swift | **shade overlay dimming** (no gamma clamp, does not fight other gamma users) and combined hardware+software brightness |
| `ColorControl` | C#/WinForms | GPU-level colour, service plus separate elevation service |

Two ideas from these are worth knowing even before implementing them:

- **A shade overlay dims further than gamma can.** Umbra's software dimming is
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
  codes. ddcutil's position, and Umbra's: record them, never write them blind.
