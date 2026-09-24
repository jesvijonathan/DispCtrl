# The device library

Every monitor answers DDC/CI with a list of VCP codes. The MCCS standard names
some of them - brightness, contrast, input source. Manufacturers add their own,
from `0xE0` up and in the gaps between, and document none of them. That is
where the features live that make one monitor different from another: preset
modes, low blue light, KVM switches, uniformity compensation.

No public database of those codes exists; `ddcutil`, the most complete VCP
table there is, stops at the standard. The device library is how DispCtrl
builds one: every owner can see what their monitor lists, work out what an
unnamed code does, name it, and share that, so every owner of the same model -
or the same brand - gets the control.

## The pieces

```
monitor ──DDC/CI──▶ MonitorCapabilities ──▶ local history          (this PC)
                                        └─▶ definitions: shipped + local
                                                 │
            dispctrl devices / Devices page ─────┤ map, link, share
                                                 ▼
            GitHub issue ──intake workflow──▶ pull request ──▶ devices/BRAND/PRODUCT/
```

### Local history

`%LOCALAPPDATA%\DispCtrl\devices\history.json`. Every model this PC has seen,
keyed on the EDID model (`DEL-A234`), never the unit: no serial, no device
path. For each: when it was first and last seen, its capabilities string, and
every code it listed with the values it was read at. Written as a by-product of
reads DispCtrl makes anyway - the engine records arrivals, and every
capabilities read from the app or the CLI records its codes - so a monitor
unplugged last month can still be mapped and shared.

### Definitions

One JSON file per target, in two places:

| Where | Layout | What |
| --- | --- | --- |
| `devices/` in the repository, shipped beside every executable | `DEL/A234/definition.json`, `DEL/brand.json`, `common.json` | reviewed, everyone's |
| `%LOCALAPPDATA%\DispCtrl\devices\definitions\` | flat: `DEL-A234.json`, `DEL.json`, `common.json` | made on this PC; wins over a shipped file, code by code |

The repository's layout is `DeviceLayout`: a folder per manufacturer, then per
model, so every path follows from the key. It is built for thousands of models.
The app reads only the files that bear on the monitor in front of it, and
caches them, since the shipped library cannot change while it runs. Two shares
of different models touch different folders and cannot conflict. Records
(`record.md`) and the generated index stay in the repository and do not ship;
the app never reads them. [devices/README.md](../devices/README.md) shows the
tree.

A target is every monitor (`*`, file `common.json`), a manufacturer (`DEL`) or a
model (`DEL-A234`). For a model, definitions layer in this order, later winning
per code: every monitor, the manufacturer, whatever the model `extends` (links
are followed once each, so loops are harmless), then the model itself - shipped
before local at each step.

```json
{
  "schema": 1,
  "target": "DEL-A234",
  "name": "Dell U2424H",
  "extends": ["DEL-A233"],
  "controls": [
    {
      "code": "0xE2",
      "key": "preset-mode",
      "name": "Preset mode",
      "kind": "choice",
      "writable": true,
      "values": [ { "value": "0x00", "name": "Standard" }, { "value": "0x0B", "name": "ComfortView" } ],
      "confidence": "verified",
      "sources": ["DEL-A234"],
      "notes": "Moves with the OSD's Preset Modes item."
    }
  ]
}
```

`kind` is `range`, `choice`, `action` (a write is the command) or
`information`. `confidence` is `observed` (seen to move with the monitor's
menu), `verified` (written and seen to do what the name says) or `documented`
(from the manufacturer). `sources` accumulate as more models confirm a code.
`devices/schema/definition.schema.json` is the JSON Schema for editors; the
rules that count are `DeviceDefinitions.Validate`, which DispCtrl, the CLI and CI
all run.

### The panel

A model's definition may also say what its panel is:

```json
{
  "schema": 1,
  "target": "SDC-4154",
  "name": "Samsung Display 14.0 inch 2880 x 1800 panel",
  "panel": { "technology": "OLED", "notes": "Built into the ASUSTeK Vivobook M7400QC, which ASUS sells as an OLED laptop." },
  "controls": []
}
```

This is how a **built-in panel** joins the library. It has no DDC/CI channel,
so it has no codes to map, and nothing on the machine reports what it is made
of: VCP `0xB6` is the only place a display ever says, and only an external
monitor can answer it. Whether a panel is OLED is exactly what burn-in
protection keys off, so one owner who knows says it once, and every laptop with
that panel is covered.

Only a model can carry a panel, never a brand or `*`: a manufacturer makes both
kinds. It resolves from the model's own definition, local before shipped, then
from whatever the model `extends`. The app prefers the monitor's own `0xB6`
answer to the library, and a person's own OLED switch on the Displays page wins
over both.

```powershell
dispctrl devices panel --monitor 1 --technology OLED --notes "The laptop's specification says OLED."
dispctrl devices panel --monitor 1 --technology none     # forget it
```

### What a definition changes

- `display controls` and `display control` show a mapped code by its name,
  key and values, with where the mapping came from.
- A panel's technology decides whether the display is treated as OLED, in the
  app and in the engine's burn-in protection, when the monitor cannot say.
- A mapped code becomes **writable only when its definition says so**, and only
  on a monitor that lists it. Standard codes stay behind the existing allow
  list. This is the one place the project's rule - never write a
  manufacturer-specific code - bends, and it bends only for a code somebody has
  written and watched; `writable` is never a default.

## Mapping a code

```powershell
dispctrl devices show --monitor 2            # every code: standard, named, mapped, unnamed
dispctrl devices probe --monitor 2           # watch the unnamed ones
```

`probe` reads the unnamed codes over and over and prints each one that
changes. Change a single setting in the monitor's own menu and watch which code
moves, and to what. Then name it:

```powershell
dispctrl devices map --monitor 2 --code 0xE2 --name "Preset mode" `
  --values "0x00=Standard,0x0B=ComfortView" --notes "Moves with Preset Modes."
dispctrl devices map --monitor 2 --code 0xE2 --name "Preset mode" --scope brand   # every Dell that lists it
dispctrl devices link --monitor 2 --to DEL-A233                                     # take a sibling's mappings
dispctrl display control --monitor 2 --name preset-mode                             # read it by name
```

Only after writing a value and seeing the monitor do what the name says, add
`--writable` (and `--confidence verified`). Then
`dispctrl display control --monitor 2 --name preset-mode --value comfortview`
works like any standard control.

In the app, **Devices** does the same: *Map codes* lists every code with its
value, *Watch the unnamed codes* lights up whichever moves, and *Name it* saves
the definition.

## Sharing

```powershell
dispctrl devices share --monitor 2 --open
```

One issue per model, replacing the old collect, view, submit. The body is the
model's record (when it is attached) and one fenced JSON block: this PC's
definitions for the model, its brand and every monitor, plus what its unnamed
codes were seen to do. Observed values are included only for codes nobody has
named - they are what a mapping is worked out from; a standard control's value
is somebody's setting and stays out. The whole body goes through `Redact.Scrub`
and the ASCII fold, like every record. The app shows the exact text first.
There is no token and no request from DispCtrl: the browser opens with the body
filled in, or, when it is too long for a link, with an empty form and the body
on the clipboard.

## The backend

The repository is the backend; there is no server.

- **`.github/workflows/devices.yml`**, the **intake** job, runs on every
  opened or edited issue whose body carries the share's `dispctrl-device-mapping` marker - a
  label cannot be relied on, since GitHub drops the labels a link asks for
  when the author cannot triage. It runs `tools/devicecheck intake`, which
  validates every definition, merges it into the model's folder code by code
  (sources accumulate), adds the record for a model the repository has never
  seen after checking it for paths and instance ids, and opens a pull request
  for a maintainer. It does not touch the index, so shares never conflict. A share it cannot read gets a comment saying why. The issue body
  is passed through the environment, never into a script.
- The same workflow's **check** job runs `devicecheck validate` and the intake
  self-test on every pull request touching `devices/`: the rules DispCtrl loads a definition
  with, the layout (a file in the wrong place is one the app would never
  read), and the records' privacy. After a merge it regenerates the index and
  commits it.
- **`devices/index.json` and `devices/CATALOG.md`** list every model: one line
  per model in the JSON for tools, and a table per manufacturer for people.
  Both are generated; nobody edits them.

```powershell
dotnet run --project tools/devicecheck -- validate devices
dotnet run --project tools/devicecheck -- index devices
dotnet run --project tools/devicecheck -- intake issue-body.md devices
```

## Reviewing a share

- A `writable` flag is the part to question. Ask how it was verified.
- Prefer `--scope brand` only when two or more models confirm the same code.
- Names describe what the control does in the monitor's own words where it has
  them ("ComfortView", not "blue light filter").
- The checks refuse paths and serial-shaped text, but read the notes anyway.
