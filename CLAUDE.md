# DisplCtrl

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
| Taskbar | hidden by DisplCtrl | Windows' own auto-hide |
| DDC/CI | none — built-in panels have no channel | 37 VCP controls, 11 offered |

Token identities: `SDC-4154-E2387367` (internal), `DEL-A234-3QQQ2X3` (Dell).

**Leave the desk as you found it.** Brightness 68% internal / 62% Dell, night
light off, Dell contrast 75. Several bugs in this project's history were caused
by tests that changed hardware state and did not restore it.

---

## Architecture

```
DisplCtrl.Core      no hardware writes. Display enumeration, EDID, settings,
                presets (model + diff), gamma ramps, arrangement geometry.
DisplCtrl.Display   every call that changes something: DDC/CI, CCD writes, modes,
                wallpaper COM, power scheme. Referenced by BOTH app and engine.
DisplCtrl.Engine    resident. Taskbar hiding, night light + software dimming,
                per-app preset rules. Native AOT intended (see debt below).
DisplCtrl.App       WinUI 3 panel, launched on demand, exits after.
DisplCtrl.Cli       `dispctrl.exe`, console subsystem. Every feature, scriptable.
```

The two processes share **only** `settings.json`. The engine watches it with a
`FileSystemWatcher` (120 ms debounce). There is no IPC.

`DisplCtrl.Display` used to be the app's alone, on the reasoning that every call in
it is a deliberate user action. Per-app preset rules made those same calls
background work, so the engine references it too.

### State on disk

```
%LOCALAPPDATA%\DisplCtrl\settings.json     shared, engine watches it
%LOCALAPPDATA%\DisplCtrl\engine.log        engine's rolling log
%LOCALAPPDATA%\DisplCtrl\displays.log      display report, rewritten whole
%LOCALAPPDATA%\DisplCtrl\presets\*.json    one file per preset, name = file stem
```

---

## Commands

Run everything from the repo root. There is no solution file; build projects
individually, in dependency order when several changed.

```bash
dotnet build src/DisplCtrl.Core/DisplCtrl.Core.csproj       -c Release -v q --nologo
dotnet build src/DisplCtrl.Display/DisplCtrl.Display.csproj -c Release -v q --nologo
dotnet build src/DisplCtrl.Engine/DisplCtrl.Engine.csproj   -c Release -v q --nologo
dotnet build src/DisplCtrl.App/DisplCtrl.App.csproj         -c Release -v q --nologo
```

**A running process locks its DLLs and the build fails with MSB3027.** Stop both
first — and stop the engine *gracefully*, or it leaves a taskbar parked
off-screen:

```powershell
& ".\src\DisplCtrl.Engine\bin\Release\net10.0-windows10.0.26100.0\win-x64\DisplCtrl.Engine.exe" stop
Get-Process DisplCtrl.App -EA SilentlyContinue | ForEach-Object { $_.Kill() }
```

The engine unwinds asynchronously; wait for the process to disappear before
building. **Killing the engine is what strands a taskbar off-screen** — only the
app may be killed outright.

Restart the engine through its scheduled task, which is how it normally runs:

```powershell
Start-ScheduledTask -TaskName 'DisplCtrl.Engine'
```

**That task does not exist on this machine and this command fails.** The rename
left the registered task called `Umbra.Engine`, still pointing at
`src\Umbra.Engine\bin\...\Umbra.Engine.exe` - a path that no longer exists. So
the engine does not come back on its own, and the only way to restart it is
directly:

```powershell
Start-Process ".\src\DisplCtrl.Engine\bin\Release\net10.0-windows10.0.26100.0\win-x64\DisplCtrl.Engine.exe" run
```

Re-registering it is `tools/DisplCtrl.ps1 -Install`, which has not been run since
the rename. Until it is, **a reboot leaves the desk with no engine**: no taskbar
hiding, no night light schedule, no per-app rules.

Engine CLI: `displays`, `enable <n>`, `disable <n>`, `status`, `run [--for <s>]
[--trace]`, `stop`.

`dispctrl.exe` is the scriptable front end — `dispctrl help` lists everything. It is a
**console** subsystem app, unlike the engine: a WinExe does not block the shell
that launched it, so a script gets neither output nor an exit code. That is why
there are two binaries rather than one.

It also carries an `app.manifest` declaring PerMonitorV2. Without it the process
sees virtualised coordinates, `MonitorFromPoint` resolves the wrong monitor, and
the symptoms are baffling rather than obvious: a 200% panel reports 100%, and a
monitor with a working contrast control reports none.

```
dispctrl displays
dispctrl brightness -10 --all
dispctrl nightlight 60 --from 20:00 --to 07:00
dispctrl input "DisplayPort 1" --display 2
dispctrl preset apply Evening
```

Exit codes: 0 done, 1 refused, 2 asked wrongly.

### Shortcuts

```
tools\Install-Shortcuts.ps1                       # Start menu, Release
tools\Install-Shortcuts.ps1 -Configuration Debug
tools\Install-Shortcuts.ps1 -AddToPath            # dispctrl on the user PATH
tools\Install-Shortcuts.ps1 -Remove
```

The shortcut points **at `bin\<configuration>`**, not at a copy, so rebuilding
updates what it launches. Copying the exe somewhere would freeze it at that
build and go stale silently — which is exactly the confusion that prompted it
(the engine was running from Release while only Debug was being rebuilt).

Windows removed the *Pin to taskbar* verb in 10 1903 and it has not returned;
an application cannot pin itself. The script checks the shell verbs and says so
rather than pretending. Pinning is one right-click on the Start entry.

Both exes carry `Assets\DisplCtrl.ico` via `<ApplicationIcon>`. Setting it only
on the shortcut would leave the taskbar button and alt-tab generic.

### What a monitor will tell you

Three sources, and they answer different questions:

- **EDID** - `EdidReader.Describe` decodes the whole base block: manufacturer
  (expanded through `PnpNames`), product code, build week and year, EDID version
  and checksum, digital link depth and interface, declared size, gamma, DPMS,
  colour encodings, the CIE primaries and white point, every established,
  standard and detailed timing, the range limits, and which descriptor blocks
  are present. `Edid` itself stays small and cached - it is on the engine's
  rescan path. `EdidReader` is the on-demand one.
- **DDC/CI** - `MonitorCapabilities` for the VCP codes, including the read-only
  ones the panel will not let you change but will happily answer: **firmware
  level (0xC9)**, hours in use (0xC0), controller type (0xC8), display
  technology (0xB6), sub-pixel layout (0xB2).
- **Windows** - modes, current signal, HDR, scaling, VRR, colour profile.

Two things are **not** available and must not be invented:

- **Country of manufacture.** The PNP registry records a country of
  *registration* against the three-letter code; the EDID carries none, and a
  panel built in one country by a company registered in another would be
  described wrongly either way.
- **Bit depth and digital interface on EDID 1.3.** Those sub-fields only exist
  from 1.4. Reading them off a 1.3 panel returns whatever the reserved bits
  hold, which is how this Dell first came out as "0-bit, not declared" - a claim
  about the monitor that the monitor never made. `EdidReader.Input` gates on the
  version.

### Detect is not Identify

They sit next to each other on the arrangement surface and are easy to confuse,
because Detect calls Identify when it finishes.

- **Identify** flashes each display's number on it for three seconds. Instant,
  and changes nothing.
- **Detect** re-enumerates the hardware, which is seconds of DDC/CI traffic per
  panel, and is the only one that can find a monitor that is connected but
  switched off. Then it identifies, so you can see what it found.

Windows' own Display settings carries both for the same reason. The labels
cannot express the difference, so both carry tooltips.

### Contributing a device record

```
dispctrl contribute                    # every monitor, printed; sends nothing
dispctrl contribute --display 2        # one of them
dispctrl contribute --display 2 --open # prefills a GitHub issue for review
```

Writes `%LOCALAPPDATA%\DisplCtrl\devices\<KEY>.md` and, with `--open`, opens
`github.com/jesvijonathan/Display-Control/issues/new` with the body filled in.
The panel has the same thing under **Displays -> Send monitor details**, one
card for the whole desk: **Collect** reads every display and writes both the
report and a record per monitor, **View** shows the exact text that would be
published, and **Submit** opens one prefilled issue per monitor. It used to be a
Review button per display inside an expander, which charged the user a press per
monitor for a distinction — one record describes one model — that the button
could not explain.

Committed records live in `devices/`, one file per model, keyed on EDID
manufacturer and product code (`DEL-A234.md`). See `devices/README.md`.

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

Autostart and the Start menu entry are managed by `tools/DisplCtrl.ps1`
(`-Install`, `-Uninstall`, `-Status`, `-AddShortcut`, `-RemoveLegacy`). It is
interim; MSIX `windows.startupTask` replaces it.

### Tests

```bash
dotnet run --project tools/presetcheck/presetcheck.csproj -c Release
```

Assertions over preset store edge cases, the diff, night-light schedules,
app-rule matching, the physical arrangement layout, and the drag's slot
geometry — the last of those matters because **synthetic pointer input does not
reach a WinUI canvas**, so a drag cannot be tested through automation at all.
What runs during one has to be pure geometry, or it is not covered. Exit code is the failure
count. **Add to this rather than writing throwaway probes** — several probes in
this project's history should have been checks here.

---

Includes the redaction checks: the scrub in isolation, then end to end over the
monitors actually attached - asserting that the text the app would publish
carries none of their serials, device paths, or the account name. 69 assertions.

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

- **Retry capability reads, and never cache a failure.** A capabilities string is
  around a hundred round trips and roughly one attempt in three came back empty
  on this Dell. `MonitorCapabilities` now tries three times, 150 ms apart, and
  caches only success — `GetOrAdd` was storing the failure, so one unlucky read
  made the monitor mute for the life of the process. The symptom was silent
  wrong answers: a device record saying the panel answers nothing, a preset
  capturing none of the monitor's own settings, an empty controls list.

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
- Windows sizes its own arrangement tiles by **raw pixel count**. DisplCtrl
  deliberately does not — see `PhysicalLayout`.
- **The arrangement drag is discrete, and has to be.** Windows takes an
  arrangement only when every display is flush against another, so the legal
  positions for one display are a countable set — each side of each neighbour,
  at each of three alignments. `ArrangementSlots` enumerates them, the drag
  moves the display to whichever the pointer is nearest, and the others are
  drawn as dashed outlines so the set is discoverable rather than learned by
  trying. Two continuous attempts came first and both failed:
  - Free movement corrected on drop meant the whole drag was spent aiming at
    positions that were going to be rejected.
  - Correcting on every move — push clear, re-attach — made the display *cling*
    to whatever it had last been pushed against, and moving it anywhere else was
    a fight with a solver answering the previous question.
- **The diagram must be frozen for the length of a drag.** Every move changes
  the bounding box; re-fitting that box to the surface recomputed the scale and
  the centring from it, and `PhysicalLayout` normalises its millimetre map to
  its own origin, so that moved too. The grab offset is measured once, at the
  old scale, and stopped meaning anything the moment the drag began: the diagram
  breathed under the pointer and the tile did not stay under the cursor.
  `ArrangeCanvas.Freeze` takes a copy on press and only the dragged tile is
  recomputed; the drop re-fits. Pinning one non-moving tile was an earlier,
  weaker fix for the same thing.
- **Rank the slots in millimetres, not desktop pixels.** A pixel is a different
  real size on each panel, so the pixel-nearest slot is not the one the eye is
  aiming at — on this desk a laptop pixel is under half the width of a Dell one.
  The surface asks `PhysicalLayout.Hang` where each slot would draw, which is
  why that method is public.
- `IDesktopWallpaper` is a **local** COM server: `CLSCTX_ALL`, not
  `CLSCTX_INPROC_SERVER`.
- The **primary taskbar cannot be moved** — `SetWindowPos` returns true and
  Explorer restores it in ~120 ms. DisplCtrl uses Windows' own global auto-hide
  there instead.

### Presets

- **A preset holds everything and applies everything.** The `PresetScope` object
  is gone (schema version 2). A preset that silently left part of the desk alone
  was one whose behaviour you had to remember, and "why didn't it change the
  brightness" is a worse question than "why did it".
- v1 files still load: the unknown `scope` property is ignored, and fields they
  lack default to "not recorded".
- **"Not recorded" has to be distinguishable from a value.** `PresetTaskbar` is a
  nullable object and `VariableRefreshRate` is a `bool?` for exactly this reason:
  a preset saved before those were captured must not reset them to defaults on
  apply, and must not count as drift.
- Guard per field, not per display. An early return for a monitor whose mode
  could not be read also silenced its brightness, which had been read perfectly
  well.
- `PresetDiff.Describe` returns `PresetChange` records (where / what / now /
  saved), because the panel lays them out as a table. `PresetDiff.Lines` puts
  them back into prose for the command line.
- Brightness has a 2-point tolerance: DDC/CI rounds, and without it a preset is
  permanently and uselessly dirty.
- Identity fields (model, serial, connector, physical size, DPI, colour profile)
  are recorded and never applied. They make a shared file readable. Matching is
  on the token alone.

### Settings file

- Enums serialise as **names**, via `UseStringEnumConverter`. The file is
  hand-edited routinely; `"brightnessDown"` says what it does and a number
  silently means something else the moment a value is inserted into the enum.
  Before that was set, a hand-written action name made the whole file
  unparseable — it was quarantined as `settings.json.bad`, the engine fell back
  to defaults, and the symptom was hotkeys simply never firing.

### Publishing device records

Everything here is load-bearing; this is the one feature where a bug is
unrecallable.

- **`DisplayReport` is not publishable and never will be.** It carries the
  monitor serial, `\\?\DISPLAY#...` device paths, and wallpaper paths with the
  user's account name in them. `DeviceSubmission` is a separate type built by
  choosing fields, not by filtering the report. Keep it that way.
- A record carries **everything that describes the model** — the whole EDID,
  every mode with every rate, every VCP code with its kind, range and accepted
  values, and the capabilities string. What stays out is what describes a desk
  or a person: the serial, the device path, any file path, the user name, and
  every current setting. That line, not the volume, is what makes it publishable.
- **`EdidDetails` has no field for a serial number.** Deliberately: there is then
  none to forget to remove. The serial lives in `DisplayKey`, beside the identity
  token that must never be published.
- **Never publish the raw EDID blob.** `Edid.Raw` exists for the decoder. Bytes
  12-15 and descriptor 0xFF are the serial, and a hex dump of them matches none
  of the patterns `Redact.Scrub` looks for — the scrub would pass it straight
  through.
- **A full record usually overruns the prefill budget**, and that is expected
  rather than a regression: the Dell's is ~6,200 characters, which encodes well
  past 7,000. `MainViewModel.Submit` puts a single over-long record on the
  clipboard and opens the empty form, so the gap is one paste.
- **The test for a field**: would it be identical on someone else's monitor of
  the same model? Current settings fail it. Brightness 62 describes an evening
  at a desk, so controls are recorded by range, never by current value.
- **The scrub is now the whole safety margin, not a second line.** A record
  carries the full report and every preset, so it is no longer true that nothing
  sensitive can reach `Redact.Scrub`. Everything sensitive reaches it. Two leaks
  were found the day that changed, and both had been latent:
  - **An account name with a space in it split the profile-path match.**
    `C:\Users\Jesvi Jonathan\...` matched only as far as `C:\Users\Jesvi`,
    publishing the surname, the folder tree, and a device instance id baked into
    a wallpaper file name. Most Windows account names have a space. `UserPath`
    now consumes spaces and stops at end of line, quote, pipe or angle bracket.
  - **Identity tokens are not serials, and were not being scrubbed.** A panel
    with no EDID serial still has a token whose suffix is an FNV-1a hash of its
    device path - unique to that unit on that port. The laptop is that case, and
    its token went out inside the presets. `DeviceContribution.Identifiers` now
    scrubs tokens *and* serials, for **every attached panel** rather than the one
    being submitted: a preset names the whole desk, and
    `dispctrl contribute --display 2` narrows the caller's list to one.
- **A device instance id does not need its path prefix to identify a machine.**
  `5&1af48b2f&0&UID256` on its own does it, and this laptop's wallpaper tool
  writes it into file names. `InstanceId` catches the bare form.
- **Check for fragments, not just whole strings.** Both leaks survived checks
  that looked for `Jesvi Jonathan` and the full device path, because what
  escaped was a piece of each. `presetcheck` now asserts that no *word* of the
  account name and no instance id appears, and builds a record with the list
  narrowed to one display as well as with all of them.
- **`Redact.Scrub` runs over the finished text**, not the fields, so a field
  added later cannot quietly reintroduce a leak. It removes attached panels'
  serials, device paths, `C:\Users\...` paths, bare GUIDs and the account name.
- `tools/presetcheck` asserts all of this **against the monitors actually
  attached**, not fixtures. That end-to-end check is the one that matters.
- **No token, no network call from the app.** It opens a prefilled issue in the
  browser the person is already signed into and they press Submit. A token in a
  Store app is a token given to everyone who installs it.
- **The body is plain ASCII** - the only place in DisplCtrl without proper
  typography. It travels percent-encoded, where an em dash costs nine characters
  and `x` costs one. With typography the Dell's record was 6200 characters and
  overran the URL; without it, 5760 and it prefills. Budget is 7000, under the
  8k where GitHub answers 414.
- `blank_issues_enabled: true` in `.github/ISSUE_TEMPLATE/config.yml` is
  required. Turning it off sends `issues/new?body=` to the template chooser and
  silently drops the body.

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
  engine log or a small console project referencing `DisplCtrl.Core`.
- Nested `SettingsExpander` children realise lazily: scroll the page top to
  bottom, expanding at each step, or UIA will not find inner controls.
- A capabilities sweep takes ~30 s (37 round trips at 40 ms plus reads). Wait for
  it before asserting the controls are missing.

---

## Pages

`Displays`, `Taskbar`, `Presets`, `Hotkeys`, `Engine`, `Settings`, `Help`,
`About`. Taskbar was split out of Settings: reveal behaviour, the four polling
intervals (which had no UI at all before, only settings.json), and Windows'
global auto-hide. What stayed in Settings is what is not about the taskbar —
logging and the reset.

Which monitors hide their taskbar stays per display, on the Displays page.

`PresetChanges` is a `UserControl`, not markup repeated twice: the docked bar and
the Presets page both show the drift sign, and two copies would disagree the
first time either changed.

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
display report, identify overlays, hotplug re-discovery, device contribution
(anonymised, consent-gated).

Outstanding, roughly in the order last discussed:

See `docs/FEATURES.md` for the full candidate list with effort and risk. The
short version, in recommended order:

1. ~~Brightness fallback, high-level to VCP `0x10`~~ — **done**.
2. ~~Lift the gamma clamp~~ — **done**, `GammaRange`.
3. ~~Full command line~~ — **done**, `dispctrl.exe`.
4. ~~Hotkeys~~ — **done**, engine-registered, with a Hotkeys page.
5. **Combined brightness** — one slider spanning hardware above a switching
   point and software dimming below it.
2. ~~Presets capturing everything~~ - **done**, schema v2, scope removed.
3. **Persistent known-monitor cache** — survive restarts, keyed on model+serial.
   Copy ddcutil's `<mfg>-<model>-<product>` convention.
4. **More fields in the display report.**
5. ~~Opt-in contribution~~ - **done**, `dispctrl contribute` and the panel card.
   Records land in `devices/`; `DEL-A234.md` and `SDC-4154.md` are seeded from
   this machine.
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

- **A shade overlay dims further than gamma can.** DisplCtrl's software dimming is
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
  codes. ddcutil's position, and DisplCtrl's: record them, never write them blind.
