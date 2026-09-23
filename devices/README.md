# Devices

What real monitors report about themselves.

DispCtrl can only offer a control it knows a monitor has. It learns that by asking
the panel over DDC/CI — but only about the panel in front of it. Every record
here comes from someone who owned a monitor this project does not, ran
`dispctrl contribute`, read what it produced, and submitted it.

## What a record is

One file per **model**, named for its EDID manufacturer and product code:

```
DEL-A234.md      Dell U2424H
SDC-4154.md      the Samsung panel in a Zenbook
```

That key is what every unit of a model carries. It is deliberately not the
identifier DispCtrl uses internally, which ends in the serial number.

A record holds the connector, the physical size, the modes, the panel
technology, the verbatim MCCS capabilities string, and every VCP code the
monitor lists with its range or its permitted values.
Controller type and low-level commands are included too. A separately labelled
observed-connection section records active signal, pixel clock, pixel density
and Windows DPI/scaling; these describe the tested setup, not every unit.

## What a record must never hold

- **Serial numbers.** The single most identifying thing a monitor reports.
- **Device instance paths** (`\\?\DISPLAY#...`), which are unique to one PC.
- **File paths**, which carry the owner's Windows account name.
- **Personal settings.** Brightness 62 describes an evening at someone's desk,
  not a monitor.

`dispctrl contribute` strips all four before showing you anything, and the checks
in `tools/presetcheck` assert it over the monitors actually attached to the
machine running them. If you are filing a record by hand, strip them yourself.

## Contributing one

```
dispctrl devices list                    # every monitor this PC has seen
dispctrl devices show --monitor 2        # every code, and which nobody has named
dispctrl devices probe --monitor 2       # watch the unnamed ones while you use the OSD
dispctrl devices map --monitor 2 --code 0xE2 --name "Preset mode" --values "0x00=Standard,0x0B=ComfortView"
dispctrl devices share --monitor 2 --open
```

`share` opens one GitHub issue for the model: its record and a JSON block with
your mappings. Nothing is sent until you press Submit there. The intake workflow
turns the issue into a pull request for review. In the app, **Devices** does the
same. The whole design is in [docs/DEVICE-LIBRARY.md](../docs/DEVICE-LIBRARY.md).

There is no access token in DispCtrl and it makes no network requests. A token
shipped inside an application is a token handed to everyone who installs it,
and a submission the application makes on your behalf is not one you agreed to.

## Definitions

`definitions/` holds what each model's codes mean - one JSON file per model
(`DEL-A234.json`), per manufacturer (`DEL.json`), or for every monitor
(`common.json`). DispCtrl ships them beside its executables and layers them:
every monitor, the brand, linked models, then the model. `index.json` is
generated from them by `tools/devicecheck index`; `schema/` has the JSON Schema.

## What they are for

- Knowing which codes a model really implements, as opposed to which it
  advertises. Monitors list controls they do not honour — this Dell advertises
  an ambient light sensor at `0x66` and answers a value that is not one of the
  two it says it supports.
- Naming manufacturer-specific codes, which the MCCS standard leaves open and
  which are where the interesting controls usually hide.
- Knowing a model is OLED without asking its owner, so burn-in protection can
  offer itself to the right panels.
