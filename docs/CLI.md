# DispCtrl CLI and local API

`dispctrl.exe` is the console entry point. It waits for completion, writes JSON
results to stdout and returns an exit code. Add `--json` for compact output.
Use the CLI+engine ZIP for scripting without installing the native UI.

```powershell
dispctrl displays list --json
dispctrl settings set --monitor 2 --path alias --value office
dispctrl display get --monitor office --hardware --json
dispctrl display set --monitor office --resolution 1920x1080 --refresh 120 --dry-run
dispctrl display set --monitor office --brightness 65
dispctrl display controls --monitor office
dispctrl display control --monitor office --name picture-mode --value games
dispctrl display set --monitor office --controls "contrast=70,input-source=hdmi-1"
dispctrl display identify
dispctrl gamma set --unlocked on
dispctrl startup set --engine on --preload-panel on
dispctrl windows set --transparency on --alignment 1 --dry-run
dispctrl taskbar set --opacity 0 --dry-run
dispctrl taskbar set --monitor office --hide on --reclaim-space on --dry-run
dispctrl unison set --enabled on --level 50 --follow-windows on --dry-run
dispctrl oled set --monitor 1 --wake pointer-return
dispctrl tray set --animate off
dispctrl tray show
dispctrl startup get
dispctrl settings export --output settings.json
dispctrl apply examples/configuration.json --dry-run
dispctrl watch --events displays,settings,engine --json
```

Monitor numbers are temporary. Persist a token or unique alias in scripts. Alias
characters are letters, numbers, dashes and underscores; numeric aliases and
`all` are reserved. A monitor selector must resolve unambiguously.

## Problem reports

`dispctrl report --what "What happened" --steps "How to reproduce it" --json`
prepares the same report as **Help → Report a problem**. It includes the version,
Windows build, display models, relevant settings and recent engine/crash logs.
Known monitor identities and user paths are scrubbed, including identities saved
for disconnected displays. Review the result for any other personal information.

The response's `data.body` is the complete report and `data.url` opens a prefilled
GitHub issue. If `data.paste` is non-null, copy that text into the field indicated
by the issue: long logs are supplied separately, and a long description uses a
complete-report paste instead. The command does not open a browser, change the
clipboard or submit anything. The Help page offers a preview, Copy report and
Open GitHub issue; submission always happens in the browser.

## Implemented surface

| Area | Operations |
| --- | --- |
| Discovery | `status`, `diagnostics`, `commands`, `displays list`, `display get`, `display modes`, `display capabilities` |
| Windows displays | `topology get/set`; `display set` resolution, refresh, orientation, primary, x/y, scale, HDR, wallpaper |
| Monitor hardware | brightness, contrast, volume, sharpness, red/green/blue gain, colour preset, input, power, explicit `--vcp-code`/`--vcp-value`; capabilities gate writes |
| Shared policies | `focus`, `oled`, `awake`, `nightlight`, `taskbar`, `tray` each support `get/set/reset` |
| Unison | `unison get/set`: enabled, level, calibrated, follow-windows |
| Windows preferences | auto-hide, transparency, small buttons, alignment, combining, task view, widgets, badges, flashing, desktop corner, VRR, adaptive brightness, auto-rotation, dark mode |
| Startup | `startup get/set`: engine, start-menu, desktop; packaged startup uses the Windows startup task |
| Toolkit | `tray show`; all composition, density, animation and custom-tile settings via `tray`/`settings` |
| Complete saved state | `settings get/set/reset/schema/validate/import/export`, including hotkeys, monitor sleep, per-monitor OLED stage behaviour and custom scripts |
| Runtime | `engine start/stop/status`, `oled preview/rest`, `watch`, `scripts list/run`, `request` |

Use `get` to discover exact camelCase field names; option names use kebab-case.
For settings without a convenience command, use a JSON pointer:

```powershell
dispctrl settings set --monitor office --path monitorSleepMinutes --value 15
dispctrl settings set --path /global/oledCare/secondStageEnabled --value true
dispctrl settings get --path /hotkeys
```

`settings export` captures saved settings, identities and policy values; it does
not read current hardware brightness or Windows preferences. The app's current
configuration export includes hardware state. Named presets and automatic preset
application remain disabled. Legacy flat commands remain available but do not
all use the versioned result format or shared broker yet.

Every control in the app, page by page, and its command:
[CLI-COVERAGE.md](CLI-COVERAGE.md). The device library - naming the codes
manufacturers leave undocumented, and sharing them - is
[DEVICE-LIBRARY.md](DEVICE-LIBRARY.md).

```powershell
dispctrl hotkeys add --keys "Ctrl+Alt+Up" --action unison-up --step 5
dispctrl devices probe --monitor 2
dispctrl devices map --monitor 2 --code 0xE2 --name "Preset mode" --values "0x00=Standard,0x0B=ComfortView"
dispctrl windows set --wallpaper-fit fill
dispctrl unison set --monitor 2 --floor 20 --ceiling 80
```

## Monitor controls

Nothing is hardcoded per manufacturer. `display controls` asks the monitor for
its capabilities string and prints every control it lists that DispCtrl will
set - `--all` adds the read-only and manufacturer-specific ones, and a code
somebody has mapped in the device library appears under its mapped name - each with a
`key`, its VCP `code`, `kind` (`range`, `choice` or `information`), the current
value (`null` when the monitor did not answer that read) and, for a choice, the
values it accepts with their own keys:

```text
contrast         0x12  range   now 75
input-source     0x60  choice  now 17   displayport-1 hdmi-1
picture-mode     0xDC  choice  now 0    standard movie games
```

`display control --name KEY` reads one; add `--value` to set it. The name is the
key, the monitor's own wording, or the code (`0x12`); the value is a number or a
listed value's key or name. `display set --controls "key=value,..."` does
several in one ordered request. Writes are refused, with the reason, for a
control the monitor does not accept, a value it did not list, or a number above
its maximum - the same allow list the panel uses. `display factory-reset
--monitor ID --confirm` restores the monitor's own defaults.

## Everything else the app does

| App | Command |
| --- | --- |
| Identify | `display identify` (the app draws the numbers; started hidden if needed) |
| Detect | `displays list --refresh` |
| Gamma clamp (night light 1900 K, dimming to near-black) | `gamma get`, `gamma set --unlocked on` (one elevation prompt) |
| Startup | `startup set --engine on --preload-panel on --open-window off --start-menu on --desktop off` |
| Quick panel, simple mode included | `tray set --simple on`, `tray show` |
| Calibration limits | `settings set --monitor 2 --path brightnessFloor --value 20` |
| Per-display taskbar, dimming, warmth, OLED | `settings set --monitor 2 --path hideTaskbar --value true` |

## Apply and failure handling

Apply documents contain `version: 1`, optional `topology`, `displays`, `settings`
and `missingMonitor` (`fail`, or explicitly `skip`). Each display entry uses the
same fields as `display set`. Settings is a full validated settings document.

Preflight resolves connected monitors before changing anything. Topology runs
first, followed by fresh discovery, modes/orientation, primary and batched layout,
scaling, colour/brightness, policies, input and power. Mode/layout failures block
dependent desk changes; a monitor-control failure blocks later actions for that
monitor. Results identify applied, failed, skipped and planned steps. Missing
monitor skips are reported as partial completion. There is no atomic rollback.

A topology change runs on its own first. Windows is given up to five seconds to
settle (two identical monitor fingerprints 150 ms apart), and everything else in
the document is then planned against the desk that produced, so one document can
switch a display on and set its mode. If that planning fails after the topology
has changed, the result is `partial` with a failed `plan` step rather than a bare
refusal. A dry run cannot perform the topology change, so a monitor it would
switch on is reported in `missingMonitors` as `deferred` instead of failing.

Hot-plug: Windows itself restores each monitor combination's modes, positions
and scaling from its display database. With unison on, the engine also brings a
reconnected external monitor back to the unison level (after 1.5 s for its DDC/CI
channel to come up, with three attempts), because a monitor keeps its own
brightness while unplugged. Window placement across replug is not restored.

## Connection topology

`display get` reports `connection`: connector, connector instance, adapter and
target ids, plus:

- `tunnelled` is `true` when the driver reports DisplayPort carried over USB4,
  Thunderbolt or USB-C, and `null` otherwise. A dock that converts to plain
  DisplayPort before the GPU sees it is indistinguishable, so `null` is never
  written as `false`.
- `sharingConnector` counts other active DisplayPort displays on the same
  adapter connector: above zero means an MST hub or daisy chain.
  `chainPosition` stays `null`; Windows does not report the order.

`--dry-run` validates and reports without writes; it may read hardware. Policy
setters report `saved`, not confirmed hardware application. Persistent policies
need `engine start`. `--revision` protects settings edits against a newer saved
revision. `--coalesce true` is an opt-in brightness-only broker optimisation for
interactive sliders: superseded queued requests return `superseded`.

Exit codes: **0** success, **1** failed/partial, **2** invalid request, **4** timeout,
**130** cancellation. If the broker connection is lost after sending a mutation,
its outcome is unknown; inspect the state before retrying. Native calls already
in progress cannot be safely interrupted. Local execution does not impose a hard
native-driver timeout.

## Library, protocol and scripts

Reference `DispCtrl.Control` from .NET. `ControlClient.ExecuteAsync` prefers the
resident engine; `ControlService.Execute` executes locally. The app need not
spawn a CLI process. The engine also accepts `DispCtrl.Engine.exe control <args>`
using the same parser, but scripts should prefer the console executable for
ordinary shell waiting, redirection and exit-code handling.

```json
{"version":1,"id":"script-1","command":"display.get","args":{"monitor":"office"}}
```

Send this with `dispctrl request request.json` or `request -` on stdin. Responses
include version, id, command, ok, exitCode, elapsedMs, data and error. Transport is
a current-user named pipe scoped to session and data directory, with a four-byte
little-endian UTF-8 byte length and a 1 MiB limit. Requests are finite; `watch`
currently polls display/settings/engine metadata and emits NDJSON, including an
initial snapshot. It is not yet a broker push stream.

`watch --script file.ps1` starts that explicit script for each event, supplies
`DISPCTRL_EVENT_JSON`, and sends script output to stderr to preserve the JSON
stream. Script source is read anew on each invocation. Execution is bounded to
30 seconds. Nothing auto-loads DLLs or executes arbitrary files on discovery.
Keep scripts in the data directory's `scripts` folder for `scripts list`.

For isolated development, set `DISPCTRL_DATA_DIR` to an absolute directory before
starting the CLI/engine. This isolates settings, not the physical displays:
hardware writes still affect the current desktop. Run `tools/controlcheck` for
protocol, validation, concurrency and failure-sequencing checks using fake steps.
Diagnostics/log paths remain local. See [architecture](CONTROL-ARCHITECTURE.md)
and [release instructions](RELEASING.md) for unfinished migration and delivery work.
