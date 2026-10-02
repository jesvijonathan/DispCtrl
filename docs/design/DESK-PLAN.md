# Desks, profiles and the parity plan

The owner's brief, 2026-10-02: arranging and remembering three or more
displays, settings that follow each display, DisplayFusion's and
DisplayMagician's features where they touch a display app (done better), an
OLED care stage that turns displays off, one settings model where a display
either follows the common value or has its own, and the things a company
laptop's owner sets with regedit. "No too much common/redundant features":
each item below says what existing piece it extends instead of adding a new one.

Reference reading: `../refs/DisplayMagician` (profiles, CCD capture and
re-apply, adapter-ID patching), `../refs/PowerToys` (FancyZones, Always On
Top), `../refs/Monitorian`, `../refs/twinkle-tray`.

## 1. Bugs reported from use - done (86717a7)

- **Settings lost when displays change.** A serial-less panel's token hashed
  its whole device path, which Windows renumbers; placeholder serials fused
  monitors. Token is now model + target UID; `MonitorAdoption` moves a former
  token's settings to the display that lost them.
- **Three or more displays.** Validity is one connected desktop; a drop
  closes the gaps it leaves (`ArrangementSolver.Close`).

## 2. Desk profiles - finish Presets, do not add a second concept

**Done** (behind the presets beta gate): recognised by monitor set, applied on
connect by the engine, `preset launch` with `--wait-for`, former tokens
resolved. Lifting the gate is the owner's call.

DisplayMagician's whole product is a saved desk applied on demand. DispCtrl
already has it, shelved: presets capture topology, modes, scaling, HDR,
brightness, warmth, calibration, wallpaper, taskbar, VRR and DDC/CI controls,
apply serially, map displays across machines, and run on app rules. What
DisplayMagician has that presets lack:

| Missing | Plan |
|---|---|
| A desk recognised by its monitor set | `DeskFingerprint`: sorted tokens of attached displays (FormerTokens included). A preset may name the desk it belongs to. |
| Apply when that desk connects | Preset option "Apply when this desk is connected", run on the engine's settled change. Off by default. |
| Launch an app or game with a profile, restore after | Exists as app rules (foreground). Add "start this program with the preset applied, restore when it exits" (DisplayMagician's shortcut). |
| Adapter IDs change after reboot | Presets store tokens, not LUIDs; CCD paths are rebuilt from the live query on apply. Keep it that way (DisplayMagician patches LUIDs because it stores raw CCD). |
| Audio device per profile | Out of scope unless asked: not a display. |

Then lift the beta gate: the feature that was shelved becomes the desk
memory. Window positions per desk come from Placement (Z1 already snapshots
per display fingerprint).

**Custom and professional rigs.** Arrangement and presets carry any mode the
driver lists. Custom resolutions are the GPU driver's (NVAPI
`NvAPI_DISP_TryCustomDisplay`, AMD ADL custom modes, Intel IGCL) or an EDID
override (CRU's registry route, needs a driver restart). Plan: list every
mode including scaled and DSR/VSR ones, show which are native, and add custom
modes through the vendor API where present - research item, behind a
confirmation, never on the dev desk without a way back (the mode is tested
for 15 s and reverted unless kept, as Windows does).

## 3. One settings model: common, or this display's own

**Done** for OLED care (stages and levels), night light warmth and focus
level: each display follows the common value until given its own, with one
resolver per feature (`OledCareSettings.For`, `FocusSettings.DimFor`,
`NightLightStrengthFor`) and "Same as all displays" in the app.

Today per-display and global settings live in different shapes: OLED care's
times and levels are global with a per-display switch, night light has a
global strength and per-display ranges, focus dimming a per-display opt-out.

- `MonitorSettings.Own` - a set of nullable overrides; null means "same as
  every display". First: OLED care (idle minutes, both stages, stage 3 below),
  night light strength, focus dim, software dimming, taskbar reveal.
- One resolver, `DispCtrlSettings.Effective(token)`, used by the engine and
  the app - the same rule as `NightLight.KelvinFor` already keeps in one place.
- UI: each per-display row shows "Same as all displays" with a link toggle;
  unlinking copies the common value as the start. The Displays page lists
  which displays depart from the common settings.
- Migration: a per-display value equal to the common one becomes null.

## 4. OLED care: a third stage

**Done** with the first piece of section 3: `MonitorSettings.OledCare`
(`OledCareOverride`, null values follow the common settings) resolved by
`OledCareSettings.For` in the engine and the app.

Stages today: dim at N minutes, dim further at M. Add stage 3 at K minutes:

- **Turn the display off** - the Displays-off machinery (overlay at
  `DisplaysOffPercent`), scoped to the resting display, not a new overlay.
- **Backlight / power off** - external monitors through MCCS power mode
  (`MonitorSleep*` exists per display; folded in here rather than kept as a
  separate feature), the built-in panel through brightness 0 where the panel
  allows it.
- **Keep active** toggle: off lets Windows sleep after stage 3 (DispCtrl stops
  holding Keep awake / Stay active for it); on keeps the machine awake.
- Its own expander on the Displays page and in the panel's OLED section;
  per-display through section 3.

## 5. Window management (DisplayFusion), where displays matter

**Done**, but for window layout per desk: `placement move --to next|previous|N`
and `placement span`, with the window in front by default, and four hotkey
actions (defaults version 7, off). Not tested live on two displays here: the
desk had only the laptop attached.

Already: pin on top, gather, put windows back after replug, new windows on
the display in use. Add, as hotkey actions and panel tiles, not new pages:

- Move the active window to the next / previous display, keeping its
  relative size and position (DPI-aware: move, then size).
- Span the active window across all displays, or a chosen pair.
- Window layout per desk: Placement's snapshot saved into the desk profile.

Not taken: title-bar buttons (injecting into other apps' frames), a second
taskbar implementation, FancyZones (PowerToys does it).

Window layout per desk: **done** (5280d7e). The doubled size first seen in its
test did not come back in two later runs that sampled the window every 50 ms
through the apply (placed at 1000 x 700 and held for 15 s): the first run
moved Notepad within seconds of its start, while it was still restoring its
own remembered size, which then landed after the preset's.

## 6. Triggers run features

**Done**: `Trigger` (Core), `TriggerService` (engine), `triggers` commands and
the Hotkeys page. Preset app rules stay as they are while presets are behind
the beta gate; folding them into triggers comes with lifting it.

Custom features (named step lists) exist; app rules exist for presets.
Generalise once: a trigger is an event plus a feature to run.

Events: display connected / disconnected, desk recognised, app in front /
started / exited, idle for N / back, session lock / unlock, on battery / on
AC, resume, a time of day. Actions: any feature, so any `dispctrl` command.
App rules become triggers of kind "app in front"; the preset rules page
migrates to it.

## 7. Company laptop (Misc)

**Done**: `MachinePolicies` (Core), `machine get|set|undo`, and Settings >
Company laptop switches. Ctrl+Alt+Del is read and written in both
locations Windows honours (`Policies\System` first, then `Winlogon`), since
regedit guides use either - this desk had it in Winlogon. The lock screen
picture uses PersonalizationCSP.

Machine-wide policies, each a switch that asks for elevation when changed,
reads the current value back, and says when Group Policy owns it (a domain
policy reapplies over a local write; DispCtrl says so rather than pretending):

| Switch | Where |
|---|---|
| Don't require Ctrl+Alt+Del to sign in | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System` `DisableCAD` |
| Lock screen picture | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP` `LockScreenImagePath`/`Url`/`Status` (works on Pro, unlike the Enterprise-only policy) |
| No blur behind the sign-in screen | `HKLM\SOFTWARE\Policies\Microsoft\Windows\System` `DisableAcrylicBackgroundOnLogon` |
| Lock after N minutes idle | `HKLM\...\Policies\System` `InactivityTimeoutSecs` |
| Dynamic lock (phone away) | `HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon` `EnableGoodbye` |
| Hide tips and Spotlight on the lock screen | `HKCU\...\ContentDeliveryManager` `RotatingLockScreenOverlayEnabled`, `SubscribedContent-338387Enabled` |

Every write recorded so Settings > Reset can put back what was there. A
company's IT policy may forbid some of these; the page says the machine's
owner decides.

## 8. From the Reddit thread

Pending: Reddit is blocked from this machine's tools. Save the thread
(`.json`) into `.notes/` and each reported problem is triaged here.

## Order

2 (bugs first, then the profile memory people asked for), 3 (the settings
model every later section builds on), 4, 7 (small and self-contained), 5,
6. Each lands with its checks in DispCtrl.Core.Checks or DispCtrl.Control.Checks and a hardware
verification line in its commit.
