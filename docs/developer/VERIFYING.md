# Testing and verifying on real hardware

The desk DispCtrl is built against, the check suites, and how to verify a
change on real displays without changing them for good.

## The machine it is built against

Two displays, and nearly every hard-won lesson below comes from one of them.

| | Internal | External |
|---|---|---|
| Panel | ASUS OLED, 2880x1800 @ 90 Hz | DELL U2424H, 1920x1080 @ 120 Hz |
| Scaling | 200% (192 DPI) | 100% (96 DPI) |
| True density | 242 PPI, 302x189 mm, 14.0" | 93 PPI, 527x296 mm, 23.8" |
| Position | secondary, right of the Dell | **primary**, at 0,0 |
| Brightness | WMI (no DDC/CI) | DDC/CI |
| Taskbar | hidden by DispCtrl | Windows' own auto-hide |
| DDC/CI | none — built-in panels have no channel | 37 VCP controls, 11 offered |

Token identities: `SDC-4154-D571EEB5` (internal; no serial, so model plus a
hash of its port), `DEL-A234-9XYZ7K1` (Dell). The internal one was
`0A1B2C3D`, then `E2387367`: see "A token must not move" below.

**Leave the desk as you found it.** Brightness 68% internal / 62% Dell, night
light off, Dell contrast 75. Several bugs in this project's history were caused
by tests that changed hardware state and did not restore it.

---

## Tests

```bash
dotnet run --project tests/DispCtrl.Hardware.Checks/DispCtrl.Hardware.Checks.csproj -c Release
```

Assertions over preset store edge cases, the diff, night-light schedules,
app-rule matching, the physical arrangement layout, and the drag's slot
geometry — the last of those matters because **synthetic pointer input does not
reach a WinUI canvas**, so a drag cannot be tested through automation at all.
What runs during one has to be pure geometry, or it is not covered. Exit code is the failure
count. **Add to this rather than writing throwaway probes** — several probes in
this project's history should have been checks here.

It includes the redaction checks: the scrub in isolation, then end to end over
the monitors actually attached - asserting that the text the app would publish
carries none of their serials, device paths, or the account name.

**Performance: `build.cmd perf`** (`tools/perfcheck`, guide in
`docs/developer/PERFORMANCE.md`). Budgets per measurement, reports in `artifacts/perf/`,
`--baseline` flags regressions. `--all` adds the ui, writes and restart suites,
which drive the tray and panel and need the desk left alone. Measure CPU from
cycle counts and measure warm - both have produced wrong conclusions here.
Run it before and after anything on a hot path.

`DispCtrl.Hardware.Checks` needs monitors. The hardware-free suites, which CI and
`build/build.sh test` run, are `DispCtrl.Control.Checks` (the command API against a scratch
settings folder), `DispCtrl.Core.Checks` (parsing, geometry, the settings merge) and
`devicecheck validate` / `selftest` (the device library and its intake).

---

## A virtual second display

For two-display work on a one-screen machine - unison, the arrangement,
gathering windows, screenshots - `build.cmd virtual-display add` adds a
virtual display; `status` shows it and `remove` takes the driver away again.
Off unless asked for: nothing in build, test or release runs it.

```powershell
.\build.cmd virtual-display add                  # driver defaults: "Virtual 24in", 1920x1080
.\tools\VirtualDisplay.ps1 add -Like DELA234 -Name "DELL U2424H" -Mode 1920x1080 -Refresh 120 -Side left
.\build.cmd virtual-display status
.\build.cmd virtual-display remove
```

- It is the open-source [Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver)
  (IddCx, signed by the SignPath Foundation), pinned to release 25.7.23 and
  checked against its SHA-256 before anything installs. Adding and removing
  ask for administrator rights once, through UAC.
- `-Like` copies the size and timings from a monitor this PC has seen
  (`HKLM\...\Enum\DISPLAY\<model>`), `-Name` sets the name it reports. Its
  manufacturer and product stay the driver's (`MTT-1337`), so DispCtrl never
  takes it for that monitor or gives it that monitor's settings.
- DispCtrl says it is virtual wherever it describes it - the card's "What it
  is" rows (made by, adapter, driver), the arrangement tile, `display get`'s
  `virtual` and `adapter` - because the name and connector it reports are
  whatever its driver says (this one claims HDMI). Virtual is decided by the
  adapter: a root-enumerated one (`ROOT\DISPLAY\...`) has no hardware under it.
- It has no brightness control, so it is the test for software dimming: its
  brightness slider, unison and the quick panel all dim it in software.
- It is kept out of the device library: its EDID describes no real monitor.

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
  `DispCtrl.Hardware.Checks`.
- **Foreground cannot be stolen** from a background script — neither
  `SetForegroundWindow` nor `AppActivate`. To test per-app rules, match on
  whatever genuinely holds the foreground instead of trying to create it.
- **Gamma ramp reads fail from PowerShell** P/Invoke. Verify gamma through the
  engine log or a small console project referencing `DispCtrl.Core`.
- Nested `SettingsExpander` children realise lazily: scroll the page top to
  bottom, expanding at each step, or UIA will not find inner controls.
- A capabilities sweep takes ~30 s (37 round trips at 40 ms plus reads). Wait for
  it before asserting the controls are missing.
- `DwmGetWindowAttribute(EXTENDED_FRAME_BOUNDS)` is always physical pixels;
  `GetWindowRect` is virtualised for a caller that is not per-monitor aware. A
  probe mixing them "showed" a border left behind that was exactly in place.
  Call `SetThreadDpiAwarenessContext(-4)` first.
- `SetCursorPos` does not hover a notification icon; small relative
  `mouse_event` moves do.
- PowerShell passes `$null` to a `string` parameter as `""`:
  `FindWindow($null, title)` finds nothing. Use `[NullString]::Value`.
- Start test windows under `pwsh`, so "never move these apps" can exclude
  everything else by process name without excluding the test window.
- **Test a development build against a copy of the settings**:
  `DISPCTRL_DATA_DIR` pointing at a scratch folder, `preloadQuickPanel` off
  there (app.path would start another build's app against it), and the
  running engine stopped gracefully first and started again after. Two builds
  of different versions on one file is how a desk ends up with settings only
  the newer one reads.
- `Start-Process pwsh -WindowStyle Hidden` hides the form a test script shows
  as well - the window's first show takes the process's start-up state. Start
  test windows minimized-console instead (`-WindowStyle Minimized` inside the
  command) and find them by title.
- **A screenshot shows whatever is in front, which may be the owner's own
  screen**: one taken while they were at the desk caught their browser. When
  somebody is using the machine, read the UI through UI Automation (names,
  toggle states, text) rather than capturing it.

---
