# Traps already paid for

Every entry was a real bug. Do not reintroduce them. Grouped by area; search
for the API or file you are touching before changing it.

Every one of these was a real bug. Do not reintroduce them.

## WinUI

- **Coalesce slider saves.** A drag can change a value dozens of times per
  second; writing settings for every change wakes every engine service. Use
  `MainViewModel.PersistSoon()` for sliders and flush pending saves before a
  refresh, external settings sync, or app close.
- **Polling stops with the window.** Page `Unloaded` alone does not
  cover minimization. Stop the refresh timer on hide/minimize and restart it
  on restore; check `OverlappedPresenter.State` for both wallpaper and status
  polling. Wait for the current read before scheduling another; file
  metadata and open calls belong on a worker, and thumbnails decode directly
  from a disposed file stream rather than copying the whole image into RAM.
- **Set `AcceptsReturn` before assigning multiline `TextBox.Text`.** Assigning
  text first silently kept only the first line in the problem-report and
  device-share previews. UI Automation must read the whole preview, not just
  verify that its dialog exists.
- **`SettingsExpander.Items` accepts only card-like children.** A nested
  `SettingsExpander`, or a bare `InfoBar`, does not render badly — it **takes the
  process down** when the item is realised. This has cost three crashes. Use a
  `SettingsCard` with `ContentAlignment="Vertical"`.
- **Items added to a `SettingsExpander` from code after it is built are never
  drawn.** No error, just an empty expander. Declare a `SettingsCard` host in
  the XAML `Items` and fill a panel inside it instead (`QuickPanelPage` does).
- **UIA name searches collide.** A list item and an expander header can share a
  name ("Quick toggles"); find expanders by class
  `Microsoft.UI.Xaml.Controls.Expander` *and* name, or a script expands the
  wrong one and reports the right one missing.
- **`ItemsRepeater` virtualises.** Expanding a card near the bottom grew the
  extent, scrolled it out of the realisation window, recycled and collapsed it,
  shrank the extent — a self-feeding open/close flicker. Use `ItemsControl` with
  a plain `StackPanel` panel; there are never more than a few displays.
- **So does every `SettingsExpander`'s item list**: the toolkit lays `Items` out
  in an `ItemsRepeater`. A display's card (a 300 px overview among 70 px rows)
  looped when scrolled to the bottom and never reached it - measured through
  UIA, a jump to the end landed at 72%, then 81%. A bigger `VerticalCacheLength`
  is not enough (the cache fills over idle frames, so the end still moved);
  `ExpanderLayout` swaps in a non-virtualising stack when an expander opens
  (`Expanded="OnExpanderExpanded"` on the Displays page's expanders). Verified:
  three rounds of expand-then-jump 0.3 s later all held at 100%.
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
- **Even `Move()`-then-`Resize()` is not enough for a window created on one
  display and placed on another.** The rescale can arrive *after* the resize:
  the quick panel opened at 180 px instead of 360. `QuickPanelWindow` holds its
  intended rectangle for a second and restores it.
- **Never resize a window from a `SizeChanged` handler synchronously.** The
  resize lays out again inside the call; with a capped height and a scroll bar
  the content rewraps a few pixels taller and fires again, nested, until
  `InsufficientExecutionStackException` kills the process. Defer to the
  dispatcher, once per pass, and skip no-op sizes.
- **A setter bound two-way to a `TimePicker` must compare at minute
  resolution.** The picker holds whole minutes, the stored value had seconds, so
  every write-back differed: save, raise, write back, 3,430 frames deep. Any
  reload with the Displays page open killed the app (`AwakeExpirationTime`).
- A crash inside a XAML callback reaches Windows as a stowed exception
  (`0xc000027b`, `CoreMessagingXP.dll`) with no managed stack. The app now logs
  unhandled exceptions to `%LOCALAPPDATA%\DispCtrl\app-crash.log`; a stack
  overflow bypasses even that, and needs a `FirstChanceException` hook to see.

## Idle and focus

- **Stay active's nudge is input to Windows, not a person.** OLED idle care
  read `GetLastInputInfo`, which the nudge resets every 55 s, so with Stay
  active on neither stage ever came. `PersonIdle` discounts input within 500 ms
  of a nudge; every idle reader in `FocusService` takes its number from it.
- **Focus mode's fullscreen pause is content fullscreen** (`IsContentFullscreen`),
  as OLED care's is: an ordinary maximized window on a display whose taskbar
  DispCtrl hides covers the whole display, and a bare `Covers` paused focus
  mode for as long as it was in front - on this desk, VS Code on the laptop.

## Protection hooks

- **A rest stays above the taskbar.** The overlay was made topmost once, when
  it showed; the taskbar is topmost too and Windows raises it (a notification,
  a flashing button, a revealed bar), after which it sat undimmed over the
  rest. `KeepAboveTaskbar` walks up from the overlay on the rest's
  once-a-second tick and re-raises it only when a taskbar is above: raised by
  hand in a test, the overlay was back on top 1.6 s later. Rests and displays
  off only; focus mode leaves the taskbar alone on purpose.

- **Keep the global location hook behind focus mode.** `EVENT_OBJECT_LOCATIONCHANGE`
  woke the protection thread about 180 times a second on an otherwise idle
  desk. OLED care alone can use its once-a-second tick to inspect the active
  window and pointer.
- **Settings reloads are not foreground changes.** Keep the exclusion set,
  hook registrations and pointer-event detection when their inputs are
  unchanged. Resetting them on unrelated saves restarts polling and the focus
  delay. `ForegroundChanged()` already calls `Tick()`; do not tick twice.

## DDC/CI

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

## Gamma

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

## Display configuration

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
- Windows sizes its own arrangement tiles by **raw pixel count**. DispCtrl
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
  Explorer restores it in ~120 ms. DispCtrl uses Windows' own global auto-hide
  there instead.
- **A topology change is applied alone, then everything else is planned.**
  `dispctrl apply` used to plan the whole document against the desk before
  the topology ran, so it validated modes for displays about to vanish and
  rejected the ones about to appear. `SetDisplayConfig` also returns before the
  new monitors enumerate: `Settle` waits for two identical fingerprints.
- **Windows reports no MST topology.** Sinks behind one hub or chain are
  separate targets on the same adapter connector instance; that is what
  `SharingConnector` counts. `DISPLAYPORT_USB_TUNNEL` is the only Thunderbolt
  signal and only its positive answer means anything — a dock that converts to
  plain DisplayPort looks like DisplayPort.
- **A hidden bar must not park on a neighbour.** Parked just past its edge,
  the bar of a monitor stacked above the laptop sat along the top of the
  laptop's screen, and the next rescan's tie-break then gave it to the laptop,
  stranding it there. `TaskbarParking.Plan` sends a bar whose strip is on
  another monitor past the far side of the whole desktop; `ResolveMonitor`
  keeps a known bar with the monitor it was managed on. It first snapped
  there, which read as no animation at all; now it slides between its shown
  position and its own edge under a window region clipped to its monitor
  (`Clip`, set before each move so no frame shows on the neighbour), and parks
  only on the last frame. The rescan's `HealRegion` skips a bar mid-clip. Checked in `DispCtrl.Core.Checks`, since stacking needs the desk moved.
- **A monitor keeps its own brightness while unplugged**, so one reconnected
  after unison moved came back out of step. `UnisonHotplug` writes arrivals
  only, after 1.5 s, because the DDC/CI channel is not up when the monitor
  enumerates.
- **Hot-plug is one settled event, not every step of it.** An arrival is an
  enumeration, a mode, a moved desktop and Explorer rebuilding its bars, and a
  loose cable adds a departure and a return inside a second. The engine's
  `DisplayChanges` (woken by `WM_DISPLAYCHANGE` and resume, 30 s safety net)
  raises `Settled` with what arrived and left only once `DisplaySettle` has
  seen the same GDI fingerprint for 1.5 s; a layout that returns to where it
  was raises nothing. Taskbars, unison arrivals, night light and monitor sleep
  all hear it, and `Local\DispCtrl.Displays.Changed` tells the app. Monitorian
  and Twinkle Tray both wait out a change before rescanning.
- **Never manage the primary taskbar, even on a display set to hide it.**
  Unplug the primary monitor and the laptop becomes primary; the manager
  adopted `Shell_TrayWnd`, fought Explorer over it two dozen times, and
  reclaimed the whole work area under a bar that stayed. Only
  `Shell_SecondaryTrayWnd` is discovered; the setting sleeps while its display
  is primary. Rediscovery runs on a settled change, a changed set of bars, or a
  dead handle - it used to enumerate every second whenever it held no bar,
  which is a laptop with its monitor unplugged - and a live bar it stops
  managing is put back on its monitor as that monitor now is.
- **A token must not move.** A serial-less panel's token hashed its whole
  device path, whose middle segment Windows renumbers after a driver update,
  dock or GPU switch: the laptop came back as a stranger twice, its
  calibration and taskbar choices left under a token nothing matched. Now the
  hash is of model and target UID only (`DisplayKey.Port`), placeholder
  serials (`01010101`, all one character) are not identities, and
  `MonitorAdoption` gives an arriving display with nothing of its own the
  most recent unattached entry of its model - in the app's refresh and the
  engine's start and settled changes - recording `FormerTokens`. Two
  identical serial-less monitors arriving together adopt nothing. Verified
  here: the laptop moved to its new token with floor 24 / ceiling 79 intact.
- **Three or more displays must stay one desktop.** Slots only checked that
  the dragged display touched something, so dragging the middle of a row away
  stranded the other two and Windows refused the layout. `IsValid` now asks
  for one connected piece, and a drop runs `ArrangementSolver.Close`, sliding
  stranded pieces whole back to the primary's. `DispCtrl.Core.Checks` drops every slot
  on a 2x2 grid and a mixed-size rig of four and checks each ends valid.
- **The app reads an arrival again.** Read the moment it appears, a monitor
  answers nothing, and the card stayed without brightness or controls until
  Rescan. `LookAgainAsync` re-reads arrivals that answered nothing at 3 s and
  8 s; a hot-plug refresh keeps cached capabilities (they describe the model),
  and `DisplaysRebuilt` redraws the arrangement diagram.
- **One display is not a desk to unify.** Unison, "Multiple displays" and the
  quick panel's display mode step aside with a line saying why, and the
  brightness bridge stops holding a lone laptop inside its calibrated range.
  Nothing in settings changes: unison resumes where it was left.

## Unison calibration

- **Windows' brightness is read back through the built-in panel's own range.**
  "Replace Windows brightness" used to take the panel's raw value as the unison
  level and hold the panel at the level exactly, so with calibration on the
  built-in screen ran 0-100 while the others stayed inside their limits.
  `UnisonResume.LevelFor` is the inverse of `Target`; a value past either end is
  pulled back to it. The correction is written **after** the other displays
  move: it raises a WMI event of its own, and the loop that moves them stops for
  any newer event, so written first it stopped the Dell from following at all.

- The walkthrough drives the slider and every display to the endpoint being
  captured. **Cancel must put them back** — it used to leave the desk at its
  floors with the slider on zero. `RememberThenLowerAsync` records the levels
  first and skips the endpoint if cancelled while reading: starting it would
  take a new generation and strand the restore (and the calibration flag the
  brightness bridge checks) behind it.

## Quick panel window

- **Cloak until the first frame, then slide the window, not its contents.**
  Shown bare, the window drew as an empty grey block for two frames; sliding
  only the contents left the acrylic backdrop to appear at full size in one
  frame. `Summon` cloaks (`DWMWA_CLOAK`), waits two XAML frames, fits the height,
  then moves the whole window its own height from behind the taskbar, placed
  just below it in the topmost band so the taskbar clips it. DWM's own show
  transition is disabled. Closing does not fade: faded, an empty backdrop sank
  alone.
- **Windows' flyout curves are wrong for a slide this long.** (0.1, 0.9, 0.2, 1)
  over 260 ms moved the panel's first frame 386 px of its 1184 px travel on the
  200% laptop and the closing curve's last frame 283 px: it jumped, then
  crawled, though every frame was on time (`perfcheck ui` measured a 13 ms
  longest gap and could not see it; the window's top sampled at 1 ms could).
  Now (0.25, 0.55, 0.25, 1) over 300 ms in and (0.45, 0, 0.7, 0.6) over 200 ms
  out, under about 115 px a frame at 90 Hz, timed by `RenderingEventArgs.RenderingTime`
  from the first frame after uncloaking rather than a stopwatch read whenever
  the callback ran.
- **Tucking under the taskbar hides nothing behind a translucent one.** With
  glass or Windows' transparency the panel was seen sliding underneath, and
  could linger in the blur. `ClipAtEdge` sets a window region ending at the
  work area's edge on every frame of a slide (move-then-clip rising,
  clip-then-move sinking) and `Unclip` hands the shape back to DWM after.
- **The panel's acrylic ignores activation** (`FlyoutAcrylicBackdrop`,
  `IsInputActive` always true). The panel was foreground within ~120 ms of a
  summons, yet `DesktopAcrylicBackdrop` sometimes kept its grey inactive
  fallback until clicked - it was activated while cloaked. Windows' flyouts
  are always acrylic too.
- **The panel belongs to the notification area, not the pointer.** It opened
  on the monitor under the pointer at the pointer's x - on the wrong display,
  mid-screen - because Windows 11 has a notification area on the main taskbar
  only, and a hotkey summons from anywhere. `QuickPanelHost.TrayAnchor`:
  `Shell_TrayWnd`'s monitor, the corner nearest `TrayNotifyWnd`.
- **Never slot the panel after a taskbar that is not topmost.** A bar DispCtrl
  has hidden is off-screen and not topmost; ordering after it dropped the panel
  behind every ordinary window. `TaskbarOf` requires `WS_EX_TOPMOST` and an
  on-screen rectangle.
- **Topmost belongs to the presenter.** `OverlappedPresenter.IsAlwaysOnTop` set
  in the constructor of a window first shown later - the preloaded panel - never
  took, and the presenter then stripped `WS_EX_TOPMOST` from every
  `SetWindowPos`. `KeepOnTop` cycles it on each summons.
- **Rows stay attached while hidden and are rebuilt only when stale.** Rebuilt
  on every summons, each toggle that loads checked played its off-to-on colour
  transition: lit tiles flashed grey for 150 ms of every opening.
- `FrameworkElement.Parent` is null until the element reaches the live tree.
  Find a child through its container's `Content` or `Children` instead.

- **The title bar has four buttons, each with its own job**: simple mode (a
  dot, accent when on and grey when off), density, stay open, customise. A "more" menu there only repeated the
  Quick panel page and the icon's right-click menu. Only the "DispCtrl" title is
  the drag handle (`TitleRow`, with a transparent background so the whole word
  is hit-testable); a row-wide handle swallowed presses meant for the controls.
  Locking, now only on the Quick panel page and off by default, sets the title
  bar to an empty element, so the title stops dragging.
- **Everything but brightness starts folded** (`QuickPanelSettings.FoldsByDefault`:
  every section but Brightness, and each display's own block). A separate `Expanded` list records
  the ones opened, so a section added later still starts folded. Section bodies
  sit 8 DIP in from their header; each feature's rows are built once
  (`FocusRows`, `OledRows`, `NightLightRows`) and used by both its section and
  its tile's flyout.
- Simple mode has no density button and always fits its height; a fixed height
  only left space under a handful of sliders.
- **Tiles open flyouts, not menus**, following the taskbar tile: a menu can
  hold a tick but not a slider. The glass, auto-hide and transparency tiles were
  removed - each was a switch already in the taskbar tile's flyout.

## Windows brightness bridge pacing

- **Throttle, not debounce.** Waiting for the events to stop left every other
  display still for the whole drag and then jumped it. The first event acts at
  once, then one pass per 90 ms with the newest value.
- **At the floor, key presses up were lost, and only the keys showed it.** Two
  races: the correction's own WMI echo went into the same pending slot and
  replaced the press that followed it, and the correction compared the panel with
  the *saved* level, so a press already on the panel but not yet read looked out
  of place and was undone. Now the bridge remembers its own write and drops only
  that echo, corrects only a panel outside its range, and waits for 400 ms of
  real quiet. A press that changes nothing is logged, not silent.
- **The app's own unison slider is not Windows' slider** (`UnisonSlider`). A
  drag moves the built-in panel, whose WMI event the bridge compared with the
  *saved* level - saved at most every 300 ms - so each step between saves read
  as a brightness key: the bridge saved its own level, drove the Dell beside
  the app, and the app pulled that back into the slider. Measured with a
  60-step UIA drag: 9 takeovers and a final level of 51 for 50 before; now the
  slider sets a session-scoped event per step (held 800 ms after the last) and
  the bridge ignores events and corrections while it is set: 0 and 50.
- **Correct the built-in panel only once the slider is still (400 ms).**
  Corrected mid-drag, the panel was pulled to its floor under the pointer while
  Windows kept moving it, and the two fought.

## Command line

- `taskbar get|set` work on fields that live loose in `/global`; unscoped, `set`
  could change unison or night light by name. `TaskbarKeys` is the allow list.
- Bare flags (`--confirm`, `--writable`, `--factory`, ...) must be listed in
  `ControlTerminal.Parse`, or the parser takes the next word as their value.
- Every `.reset` command passes the generic reset allow-list first; a reset with
  options of its own (`display.reset`) needs an entry before it.
- **The terminal turns `on`/`off` into true/false** before a command sees them.
  An option whose values are words (`--tray-wheel off`) has to map the bool back
  (`GroupCommand`), or it fails as asked wrongly.

## Settings file (sharing)

- **Read with delete sharing, and retry the rename.** A save is a rename over
  `settings.json`; `File.ReadAllText` opens without `FileShare.Delete`, so any
  reader in another process made the rename throw access denied - which crashed
  the app mid-drag of the unison slider. And a sharing violation on load was
  treated as corruption: the good file was quarantined as `.bad` and every
  client fell back to defaults. `SettingsStore.ReadShared`, `ReplaceWithRetry`,
  and a load that only quarantines on a JSON error.
- **A process does not read back the file it just saved.** The first open of
  a freshly renamed `settings.json` is scanned by the antivirus: 5.6 ms against
  0.14 ms for the next open, and a load follows most saves. `SettingsStore`
  keeps its own last write and reuses it while the file's write time, creation
  time and length are unchanged; anybody else's save changes the creation time
  (rename-over) or the write time (in place). `DispCtrl.Control.Checks` covers both with a
  same-length file. Load + save went from 8.15 ms to 2.42 ms.
- **A save is reloaded on its rename, an edit after the debounce.** Every
  DispCtrl save raises exactly one `Renamed` to settings.json with the file
  already whole, so the engine and the app act on it at once (save -> engine
  applied: 127 ms -> 10 ms). An editor writing in place raises several
  `Changed` mid-write; those still wait 120 ms, because a half-written file
  read now would be quarantined as corrupt.
- **Engine start-up waits only for what it must.** The broker starts first
  (it needs only the folder's name), `FocusService` signals ready once its
  window exists and configures after, and the brightness bridge's WMI
  subscription and the device-history reads run on the pool. The engine logs
  each phase: `started N ms after launch: ...`.
- **Explorer's `TaskbarCreated` wakes the taskbar manager** (the tray window
  hears the broadcast, `TaskbarManager.ShellReady`). At sign-in the engine is
  up before Explorer's bars, and glass, hiding and opacity waited for the next
  one-second look.
- **Never `Process.GetCurrentProcess().SessionId`.** .NET snapshots every
  process on the machine to answer it (7.9 ms warm); `Session.Id` asks Windows
  (0.28 ms). Every pipe and mutex name scoped to the session uses it.
- **Windows' colour-profile call can take seconds.** `WcsGetDefaultColorProfileSize`
  took ~7 s and then failed for both displays on this desk, from PowerShell as
  well (perfcheck "details read"), and HDR, VRR, orientation and the details
  table all waited on it in one task. The card now reads the profile on its
  own, and `ColorProfile.ReadName` remembers an answer, a failure too, for a
  minute.
- The gamma clamp state is cached for a minute, not for the process's life:
  `Recheck` only ever ran in the app, and the engine that owns the ramp kept the
  old limit until restarted.

## Presets

- **A preset holds everything and applies everything it has not been told to
  skip.** The `PresetScope` object is gone (schema version 2): a preset that
  silently left part of the desk alone was one whose behaviour you had to
  remember. `Preset.Skip` (2026-10-03) brings choice back without the silence:
  the parts (`PresetPart`) are chosen by name in "What it restores", the row
  says what is left alone, and a skipped part is neither applied, merged into
  settings, nor counted as drift. Asked for because brightness - the room's
  light, a key press - kept a layout preset "changed" all day. Brightness the
  room's light moved (`BrightnessIsAutomatic`) is never drift either.
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
- **Merge field by field, and never share an object.** `PresetSettings.Merge`
  assigned night light whole: a change saved elsewhere during an apply
  (following Windows, the theme schedule) was lost, and the two settings
  objects then shared one night light. Every caller that applies must save
  through the merge - `preset apply` from the command line saved the whole
  stale copy, and the engine's changes made during the apply went with it.
- **Brightness is a percentage of the monitor's range** (`BrightnessRange.Percent`,
  written back with `FromPercent`). It was the raw value, which fails the 0-100
  validation on a monitor whose range is different, so its preset could not
  be saved.
- **A preset name is a file name.** Device names (`CON`, `NUL`, `COM1`...) get an
  underscore, since Windows 10 treats `CON.json` as a device, and stems are cut
  to 100 characters - a 300-character name failed to save.
- **A copy shares nothing**, saved windows included: the store caches presets
  and hands out copies, and a shared window list let one caller edit another's.
- **No display is not a desk.** A preset with no monitors matched an empty set,
  so a desk profile could fire with the lid shut and no monitor attached.
- **A desk change heard mid-apply is looked at again**, and an apply refused
  because another preset was applying is retried once: the desk was recorded
  before applying, so both used to leave it unconsidered until the next change.
- **App rules and triggers take a pasted path.** `AppRule.ProgramName` strips
  quotes, the folder and `.exe`; a rule written as `"C:\Games\game.exe"` never
  matched.
- **Drift is only what Discard can put right.** A layout needing a display
  that is unplugged, a wallpaper whose file has gone, and the display's name
  (`MonitorSettings.Label`, which the app rewrites from the monitor on every
  refresh) all kept the banner up after Discard. `PresetDiff` leaves them out.
- **An apply's settings reach the app through its file watcher.** The preset's
  values are merged and saved to disk, not into the app's copy, and the drift
  check run straight after compared the old values. `SyncExternalSettings`
  re-checks drift after every reload. Verified through UIA on a scratch copy:
  the banner up before Discard, gone after.
- `PresetModelChecks` (core) and `PresetStoreChecks` (control) hold all of this;
  each lists every failure in one run rather than stopping at the first.
- Identity fields (model, serial, connector, physical size, DPI, colour profile)
  are recorded and never applied. They make a shared file readable. Matching is
  on the token alone.

## Settings file

- **A file from a newer DispCtrl loads; nothing is lost** (`SettingsStore.Lenient`).
  A value this build cannot read - a hotkey action or a choice added since -
  used to throw, and the whole file went to `.bad`: every setting back to
  defaults, in the older build and in the newer one once it returned. Now each
  failing value is removed from the in-memory copy by its JSON path (a list
  entry whole, so a hotkey never runs with its action defaulted), the rest
  loads, the file is untouched, and a save merges into it, keeping what was
  set aside and every property this build never knew. The engine logs what it
  left out. Only text that is not JSON at all is still quarantined. Checked in
  DispCtrl.Control.Checks with a hand-made future file. **This protects builds from 0.1.5
  on; 0.1.4 and earlier still quarantine** - a desk that runs a newer test
  build beside the Store's 0.1.4 must not let 0.1.4 start on the same file.
- New settings need nothing else: a missing property takes its initialiser,
  and one-off upgrades are versioned where they must be (hotkey defaults).
- Enums serialise as **names**, via `UseStringEnumConverter`. The file is
  hand-edited routinely; `"brightnessDown"` says what it does and a number
  silently means something else the moment a value is inserted into the enum.
  Before that was set, a hand-written action name made the whole file
  unparseable — it was quarantined as `settings.json.bad`, the engine fell back
  to defaults, and the symptom was hotkeys simply never firing.

## Publishing device records

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
  `5&3c9e07d1&0&UID256` on its own does it, and this laptop's wallpaper tool
  writes it into file names. `InstanceId` catches the bare form.
- **Check for fragments, not just whole strings.** Both leaks survived checks
  that looked for `Jesvi Jonathan` and the full device path, because what
  escaped was a piece of each. `DispCtrl.Hardware.Checks` now asserts that no *word* of the
  account name and no instance id appears, and builds a record with the list
  narrowed to one display as well as with all of them.
- **`Redact.Scrub` runs over the finished text**, not the fields, so a field
  added later cannot quietly reintroduce a leak. It removes attached panels'
  serials, device paths, `C:\Users\...` paths, bare GUIDs and the account name.
- `tests/DispCtrl.Hardware.Checks` asserts all of this **against the monitors actually
  attached**, not fixtures. That end-to-end check is the one that matters.
- **No token, no network call from the app.** It opens a prefilled issue in the
  browser the person is already signed into and they press Submit. A token in a
  Store app is a token given to everyone who installs it. The one exception is
  the update check, and only because somebody asked for it (below).
- **The body is plain ASCII** - the only place in DispCtrl without proper
  typography. It travels percent-encoded, where an em dash costs nine characters
  and `x` costs one. With typography the Dell's record was 6200 characters and
  overran the URL; without it, 5760 and it prefills. Budget is 7000, under the
  8k where GitHub answers 414.
- `blank_issues_enabled: true` in `.github/ISSUE_TEMPLATE/config.yml` is
  required. Turning it off sends `issues/new?body=` to the template chooser and
  silently drops the body.

## Build

- `PublishAot=true` disables built-in COM interop. The internal panel's
  brightness goes through WMI (`System.Management`), which **is** built-in COM,
  so every per-app rule touching it died with `NotSupported_COM`.
  `BuiltInComInteropSupport=true` is set to fix it, and **that is now the one
  thing between the engine and Native AOT.** Paying it off means reaching WMI
  through source-generated COM, as `Wallpaper.cs` already does for
  `IDesktopWallpaper`.
- Native AOT publish also needs the MSVC linker, which is not installed:
  `winget install Microsoft.VisualStudio.BuildTools --override "--quiet --wait --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"`
- **The taskbar glass helper's revision hashes `TaskbarGlass.cpp`, its header
  and `build.ps1` itself**, and a helper already loaded in Explorer refuses to
  attach over a different revision (`0x8007051A`, revision mismatch) until
  Explorer restarts. A one-line tidy of `build.ps1` did exactly that: glass
  stopped applying on the next engine start. Leave the script alone unless the
  helper really changes, and expect to restart Explorer when it does.
- **The glass helper is configured with one 64-bit number** (`TaskbarGlass.Pack`):
  radius, tint, on, the look (blur, clear, opaque, acrylic) in bits 25-27,
  hide-the-top-border in 28, and a 0xRRGGBB colour in 32-55 behind bit 29.
  `GlassUpdate` takes it as an unsigned 64-bit value; the engine's delegate
  must match, or the colour is cut off. The border is the Rectangle named
  `BackgroundStroke`, hidden by taking its fill away and given back on
  restore. Changing this changed the helper's revision; the engine retired the
  old one in Explorer by itself here (no restart).
- **Explorer can leave a secondary taskbar with a region sized for the wrong
  DPI** - 2880 x 48 on a 96 px bar at 200%, measured after an Explorer restart.
  Windows clips to the region, so the lower half was never drawn.
  `TaskbarManager.HealRegion` clears a region shorter than the bar every rescan
  and when a reveal settles; the earlier repaint-on-settle did not help,
  because the pixels were clipped, not stale.
- **CsWin32's `CreateFont` cannot be called.** It marshals the byte-sized
  charset as four bytes and the runtime refuses at the first call - which ended
  the tray pump and removed the icon. Use `CreateFontIndirect` with a `LOGFONTW`.
  Read `obj/generated` before trusting any generated overload.
- **No NuGet lock files, on purpose.** Generated, they pin packages the SDK
  adds by itself - `Microsoft.DotNet.ILCompiler` (the engine's `PublishAot`)
  and `Microsoft.NET.ILLink.Tasks` (`IsAotCompatible`) - at the SDK's own
  runtime patch (10.0.12 under SDK 10.0.112, another under 10.0.203). Every
  SDK patch would then rewrite them, or fail CI in locked mode. Every
  `PackageReference` is an exact version, so restores are already
  deterministic without them.
- **Judge a build by its exit code, never by grepping for `: error `.** The
  XAML compiler reports `XamlCompiler error WMC0015 ...` with no colon before
  "error"; a script that grepped reported the app built when it had not, and
  the page tested was the previous build's.
- The engine must carry `<ApplicationIcon>` too: the tray's logo style reads it
  from the running binary, and without it drew an empty slot.

## Release and installer

- **A Store install has no `DispCtrl.Engine` scheduled task**; the package's
  startup task starts it. Restart it inside the package:
  `Invoke-CommandInDesktopPackage -PackageFamilyName JustVStudio.DispCtrl_5fm6x6q82qb7g -AppId App -Command '<WindowsApps path>\DispCtrl.Engine.exe' -Args 'run'`.
- **The installer offers "for me" (default, unelevated, `%LOCALAPPDATA%\Programs`)
  or "for all users" (Program Files)** - `PrivilegesRequiredOverridesAllowed=dialog`,
  `{autopf}`/`{autoprograms}`/`{autodesktop}`, and the system PATH in admin mode.
  Everything it runs is `runasoriginaluser`: the engine must never be elevated.
  The sign-in task is per person, so the installer marks its own user as
  offered (`engineStartupOffered`), and the app offers it to everybody else on
  first opening (`StartupIntegration.InstalledForAllUsers`). Compiled, not yet
  installed (a clean account or VM, never this desk).
- **Never let an installer kill the engine.** Inno's Restart Manager
  (`CloseApplications`) would, and a killed engine strands a hidden taskbar.
  `DispCtrl.iss` turns it off and runs `DispCtrl.Engine.exe stop` itself,
  waiting for the exit, and refuses to install over an engine that will not stop.
- **An installer or uninstaller removes the sign-in task only if it points into
  its own folder.** The first draft ran `startup set --engine off`
  unconditionally, which would have deleted a development or portable copy's
  task on this very desk. Do not install the setup on the dev machine to test
  it, for the same reason: it re-points the task. Use a clean account.
- `{tmp}` inside a Pascal `{ }` comment ends the comment - the compiler reports
  "Identifier expected" a line later. Use `//` comments in `[Code]`.
- **Do not sign the taskbar-glass helper.** Its bytes are pinned by revision;
  `Publish.ps1 -Sign` signs only `DispCtrl*` binaries.
- **Never set `AssemblyTitle` on `DispCtrl.App`.** With it set to "DispCtrl",
  a clean build crashed at start (`Cannot locate resource from
  'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'`), and so did the
  published bundle and the MSIX. Incremental builds hid it. The engine and the
  CLI carry titles safely; they have no WinUI.
- **The Store identity lives in `build/packaging/AppxManifest.xml`** (`JustVStudio.DispCtrl`,
  `CN=C082656A-...`), and `Package.ps1` packs with it by default. A package with
  the old development identity was rejected by Partner Center on upload. Only
  CI's validation build passes `DispCtrl.Development`. Run `Package.ps1` under
  pwsh 7: Windows PowerShell's `System.Drawing` cannot read the icon's 256 px frame.
- **A Store install is never started for you.** Nothing runs at install, and
  the sign-in task was declared `Enabled="false"`, so a fresh install had no
  engine - no tray icon, hotkeys, taskbar hiding or glass - until someone found
  the switch. The task is now declared enabled, the app starts the engine on
  every launch, and it enables the sign-in task once (`EngineStartupOffered`).
- **Under MSIX, new files in `%LOCALAPPDATA%` are redirected** to
  `Packages\JustVStudio.DispCtrl_*\LocalCache\Local`, while an existing
  `%LOCALAPPDATA%\DispCtrl` is written in place. Every DispCtrl process in the
  package sees the same view; anything outside it (Explorer, the glass helper,
  an unpackaged build) may not. Registry writes are not redirected - measured
  with `Invoke-CommandInDesktopPackage`. So `SettingsStore.Directory` resolves
  the **real** folder when packaged (`PackagedFolder`: the package's
  `LocalCache\Local\DispCtrl`, unless an earlier install's real
  `%LOCALAPPDATA%\DispCtrl` is in use): through the merged view the data was
  fine, but "open the log" handed Explorer a path that did not exist for it,
  and people found no DispCtrl folder in %LOCALAPPDATA% at all.
- **Explorer cannot load anything from WindowsApps.** The glass helper is
  loaded by Explorer itself (`InitializeXamlDiagnosticsEx` hands it a path),
  and a package's files carry a conditional ACE granting execute only to
  processes whose SYSAPPID is that package: everyone else may read, not map an
  image. The Store build's glass therefore did nothing at all, with no visible
  error, while the installer and zips worked. `TaskbarGlassController.ExplorerLoadable`
  copies the helper to the package's real `LocalCacheFolder` (the user's ACL)
  and loads that. Anything else handed to another process by path from a
  Store install needs the same.
- **Never hand Explorer a file in `bin`.** It keeps the helper mapped until it
  restarts, so `build clean` failed with access denied on
  `bin\...\taskbar-glass\DispCtrl.TaskbarGlass.*.dll`. Unpackaged builds now
  copy the helper to `%LOCALAPPDATA%\DispCtrl\taskbar-glass\` too, and
  `dev.ps1 clean` (`Remove-Tree`) leaves a held helper behind with a warning,
  failing only on anything else that is locked.
- **Explorer records a tray icon's path by known-folder id**:
  `{6D809377-...}\WindowsApps\...` for the Store engine. Compared as a plain
  path it never matched, so the Store icon was never kept on the taskbar.
  `TrayIconPromotion.Expand` resolves it.
- **Nothing asks for admin unprompted.** Only lifting the gamma range and
  auto-rotation need elevation, and each asks when the person switches it on.
  A first-launch prompt for the gamma range was tried and dropped: the lift is
  a nicety, and an unasked UAC prompt on first run costs trust (and Store
  certification questions it). The app must never require elevation: a
  packaged app cannot run elevated at all.
- **The wallpaper preview falls back to `%APPDATA%\Microsoft\Windows\Themes\TranscodedWallpaper`**,
  Windows' decoded copy, when the reported file is missing, online-only or
  undecodable (HEIC, WebP). One laptop's preview stayed empty without it.
- Switching between the installer, the Store and a development build leaves
  Explorer holding the other build's glass helper (`0x8007051A`). Attach
  refused while it was there, and the helper's own `Retire` path ran only in
  `GlassUpdate` after a successful attach, so glass stayed dead until Explorer
  restarted (seen: installer, then Store, same Explorer). The engine now sends
  one glass-off update on a mismatch, which retires the stale helper (brush
  restored, window gone), and attaches again - once per Explorer, and never
  while another `DispCtrl.Engine` runs, or two live engines would retire each
  other in turn. Restart Windows Explorer on the Taskbar page remains the way
  out if the second attach still fails. Engine-side only: touching the helper
  would change its revision and need yet another Explorer restart.
- An MSIX signs only with a certificate whose subject equals its `Publisher`.
  The release workflow compares them and leaves it unsigned rather than failing.
- **Read a process's `Path` before stopping it.** `Process.Path` comes from
  the main module; once the engine has exited it is empty. `dev.ps1 build` read
  it afterwards and so never restarted the engine it had stopped.
- `SkipTaskbarGlass=true` builds the engine without the native helper (no
  MinGW). On Linux the helper step runs `pwsh`, not `powershell.exe`: through
  WSL interop the latter reached Windows' own MinGW with Linux paths and failed
  to link. The WinUI app cannot build off Windows at all (`GenXbf.dll`).
- **A tool run on Linux must not have an apphost.** `Directory.Build.props`
  builds for win-x64, so `dotnet run` on the Linux runner tried to execute
  `devicecheck.exe` - "Exec format error" - and every shared monitor record
  failed intake. WSL hid it: it runs Windows .exe files through interop, so
  the check "passed on Linux" there. `devicecheck` and `DispCtrl.Core.Checks` set
  `UseAppHost=false` off Windows; verify on Linux by confirming no `.exe` was
  built. The workflow's shell is named `bash` so `| tee` no longer hides a
  failure (the default shell has no pipefail).
- **MSBuild never deletes an output it has stopped copying.** When the device
  library moved to a folder per model, every `bin` kept the old flat
  `devices/definitions/` and the records, which must not ship.
  `Directory.Build.targets` removes the retired layout after each build and
  publish. Any future move of a copied file needs the same treatment.
- **Publish stops the engine too.** It runs the tests, which build into the
  `bin` the engine runs from; `dev.ps1 publish` stops it gracefully and
  restarts it, as `build` does.
- **Release output has one fixed place**, `artifacts/<channel>-<version>/`,
  cleared on each run. It used to be a new GUID-suffixed folder per publish and
  per MSIX, each with a full staging copy: 2 GB had accumulated.
- **The engine restores taskbars from a crash handler.** An exception on a
  timer or pool thread ends the process without unwinding through the
  manager; `AppDomain.UnhandledException` calls `TaskbarManager.EmergencyRestore`
  first. Do not remove it.
- **Idle cost is wake-ups, not work.** Measured with per-thread context
  switches (`\Thread(DispCtrl.Engine*)\Context Switches/sec`), not guessed.
  - A global `EVENT_OBJECT_LOCATIONCHANGE` hook woke the focus thread ~180 times
    a second on an idle desk; it is registered only for focus mode.
  - The taskbar loop polled at 100 ms when it managed nothing or the session was
    locked; it now sleeps to the rescan, or until settings change.
  - OLED care stops polling while locked (`WM_WTSSESSION_CHANGE`).
  - Hot-plug detection follows `WM_DISPLAYCHANGE` (`DisplayChanges`) with a
    30 s safety net.
  - The power loop sleeps to its next deadline.
  - A rest (OLED idle or manual, displays off) is ended by input, so while one
    shows the protection thread registers raw keyboard and mouse input
    (`RIDEV_INPUTSINK`) and waits for it, with a one-second safety net for a
    second stage or a pointer moved by software. It polled at 100 ms, all night.
    One key press ends displays off in well under 150 ms, measured.
  - Engine idle: ~282 ms/min before, ~16 ms/min after.
  - New-window placement listened with a WinEvent hook on objects shown and
    uncloaked: every caret blink, tooltip and menu in every process woke the
    placement thread, 5.4 times a second with an editor open, to be thrown
    away. It hears the shell's window-created notice now
    (`RegisterShellHookWindow`, top-level unowned windows only), which needs a
    hidden top-level window - message-only windows are not sent it.
  - The app-rule service's 500 ms timer ran from start on every desk, rules
    or none, once presets were switched on: armed only while a rule exists or
    one is in force (`AppRuleService.Arm`).
  - Name a thread (`Thread.Name`) when it waits: Windows shows .NET's name as
    the thread description, which is how a wake-up per thread is pinned on a
    service without any tool installed (`GetThreadDescription` beside the
    `\Thread(DispCtrl.Engine*)\Context Switches/sec` counter).
- **Slider saves are coalesced** (`PersistSoon`); a synchronous save per drag
  step also made the engine reload each time. `Persist()` flushes a pending one.
- **The hidden quick panel trims itself** 10 s after hiding: 190 MB working set
  down to ~11 MB, and the next summons shows in ~40 ms.
- **Tray icon promotion is once per engine path** (`Global.TrayPromotedFor`).
  After that, where the icon sits is the person's choice; never re-promote.
- **Repair never re-points a sign-in task another copy owns.** An installed
  copy, a portable one and a development build can all exist; `maintenance
  repair` only fixes a task whose engine is missing.
- **Repository layout**: `.claude/` this file, `.github/` community files,
  `build/packaging/` MSIX and installer, `src/native/` the glass helper,
  `docs/design/` internal notes. Session exports stay in `.notes/`, ignored.
- **Workflows** (`docs/developer/RELEASING.md` has the table): Build and verify, Release,
  Distribute, Device library, Pull requests, Issues, Website, Housekeeping.
  Each writes a summary with links and sizes. Every action is pinned to a
  commit with its tag in a comment, every workflow starts from
  `permissions: {}` and each job asks for its own, and a checkout that pushes
  nothing sets `persist-credentials: false`. Untrusted text (issue bodies,
  titles, file names) reaches a script only through `env`. `pr.yml`'s policy
  and triage run on `pull_request_target` and must never check out the pull
  request - everything comes from the API. Lint locally with actionlint and
  shellcheck (`pip install actionlint-py shellcheck-py`); CI runs the same.
  Labels live in `.github/labels.json`. A release that the
  workflow publishes itself does not raise `released`, so Release starts
  Distribute with `gh workflow run`. Housekeeping deletes old artifacts, runs,
  caches and drafts weekly; a manual run defaults to a dry run.
- **One local build at a time.** `dev.ps1` holds `Local\DispCtrl.Build` for
  build, test, clean and release: two runs share every `obj` folder, and a
  release started while another ran failed with a missing R2R file. Publish also
  shuts the compiler server down first (CS2012 on a still-open obj file), closes
  any repository copy of the app (an installed engine can start the panel from
  `bin`), publishes the app before adding the CLI files (publish keeps a newer
  destination, which left the app with the CLI's copies and a startup crash),
  and launches the result before packaging it.
- **The channel is compiled in** (`DispCtrlChannel`: stable, beta, test; dev
  from source; `Publish.ps1` passes it). Stable defines `DISPCTRL_STABLE` and
  `BuildInfo.Diagnostics` is false: no log line per command, reload, brightness
  key or start-up phase - errors and refusals are still logged. Anything else
  shows its version in the window and panel titles (`BuildInfo.AppTitle`), and
  device shares carry `BuildInfo.Label` in their footer. perfcheck's sync and
  restart timings read those diagnostic lines: measure a non-stable build.
- **The app excludes the Windows App SDK's AI, ML, Search and Widgets** by
  `ExcludeAssets="all"` on those component packages - and on
  `Microsoft.Windows.AI.MachineLearning`, which is where onnxruntime and
  DirectML really come from. The toolkit depends on the metapackage, so it
  cannot be dropped for components. Bump those versions with the metapackage.
  **`Microsoft.Windows.SDK.NET.dll` is not ReadyToRun** (56 MB -> 25 MB; the
  panel's cold start measured the same, ~540 ms). Together: the desktop folder
  281 -> 197 MB, its zip 104 -> 74 MB.
- **The release is trimmed as one bundle per folder** (`DispCtrlBundle`, set
  by `Publish.ps1`). The app, the engine and the CLI share a folder, so
  trimming each on its own would leave three different copies of each
  framework DLL under one name; instead the app references the engine and the
  CLI (the CLI references the engine for its zip) and the trimmer keeps what
  all of them use. Partial mode: only assemblies marked trimmable are cut.
  Desktop folder 199 -> 111 MB, portable zip 74 -> 46, CLI zip 46 -> 19, MSIX
  78 -> 47; the engine's working set 73 -> 48 MB.
  - **WMI needs `System.Management` and `System.CodeDom` kept whole**
    (`TrimmerRootAssembly`). They are marked trimmable, but WMI creates its COM
    classes by reflection: cut, every brightness read on the built-in panel
    failed with "no parameterless constructor" for `WbemDefPath`. Built-in COM
    stays on (`BuiltInComInteropSupport`), or the trimmer removes it from the
    runtime the engine shares.
  - **A WinRT type the app derives from needs its interop types kept**
    (`DispCtrl.App/Trimming.xml`). `ExpanderLayout` derives from
    `NonVirtualizingLayout`; CsWinRT finds its override vtables by reflection,
    and trimmed, setting an `ItemsRepeater`'s layout failed with "Not
    implemented" - the app closed as a display card opened. Any new subclass
    of a WinUI type goes in that file.
  - **Only content is copied into a trimmed folder** (`Copy-Content`): the
    untrimmed engine publish's framework DLLs would undo the trim (123 files,
    26 MB), and its `deps.json` lists DLLs the folder does not have. Without a
    deps.json the engine loads what is in the folder.
  - The trimmer's warnings are reported, not fatal: they are about those
    reflecting assemblies. The proof is running the result - every command,
    the engine with logging, every page through UI Automation.
- **The release's start-up probe runs on a throwaway settings folder.** On the
  real one it wrote itself into `app.path`, the installed engine then started
  its quick panel from `artifacts`, and the next release failed to delete the
  locked folder.
- `DispCtrlVersion` in `Directory.Build.props` is the default `Version`; a
  stable tag that disagrees with it fails `release.yml`. The Release form's
  version defaults to **next patch**, and the workflow bumps and commits it
  itself ("Set shipping version to x.y.z", as `github-actions[bot]`, straight
  onto `master`): **fetch before pushing after a release**, or the push is
  rejected - it was, for 0.1.6. That 0.1.6 never reached GitHub: the version
  went back to 0.1.5, which is the release published on 2026-09-26, and a tag
  v0.1.6 is left only in local clones. **Write the CHANGELOG section for the
  version before releasing**: the notes are that section, and without one they
  fall back to Unreleased (the 0.1.6 draft first went out with 0.1.3/0.1.4's
  notes). A section under the wrong number is worse than none: the changelog
  carried v0.1.5's notes as [0.1.6], and the next patch release, 0.1.6, would
  have published them again instead of what is under Unreleased.
  See `docs/developer/RELEASING.md` for the whole procedure and the secrets and
  variables it needs.

---
