# Devices

What real monitors report about themselves, and what their undocumented codes
mean. **[CATALOG.md](CATALOG.md)** lists every model here.

DispCtrl can only offer a control it knows a monitor has. It learns that by asking
the panel over DDC/CI, but only about the panel in front of it. Everything here
comes from someone who owned a monitor the project does not, ran DispCtrl on it,
and shared what it found.

## Layout

One folder per manufacturer, and inside it one folder per model, named for the
EDID manufacturer and product codes every unit of that model carries:

```
devices/
  CATALOG.md               every model, for people      (generated)
  index.json               every model, for tools       (generated)
  common.json              mappings for every monitor   (optional)
  schema/                  JSON Schema for definitions
  DEL/                     Dell
    brand.json             mappings for every Dell      (optional)
    A234/                  DEL-A234, the U2424H
      record.md            what the model reports about itself
      definition.json      what its codes mean
  SDC/
    4154/
      record.md
```

A model folder holds a record, a definition, or both, and nothing else. The key
(`DEL-A234`) is deliberately not the identifier DispCtrl uses internally, which
ends in the unit's serial number.

The layout is built for thousands of models:
- **Every path follows from the key.** DispCtrl reads the two or three files
  that bear on the monitor in front of it, and never lists the rest.
- **A share touches only its own model's folder,** so two pull requests never
  conflict.
- **No folder grows past what GitHub will list.** A manufacturer folder holds
  that manufacturer's models only.
- **The two generated files are rebuilt after each merge** by the
  `Device library` workflow. Do not edit them, and do not include them in a
  pull request.

Only definitions ship with DispCtrl, beside its executables in the same layout.
Records and the index stay here: the app never reads them.

## A record

`record.md` holds the connector, physical size, modes, panel technology, the
verbatim MCCS capabilities string, and every VCP code the monitor lists with its
range or permitted values. A separately labelled section records the connection
it was taken on (signal, pixel clock, density and scaling), which describes
that setup rather than every unit.

It must never hold:

- **Serial numbers.** The single most identifying thing a monitor reports.
- **Device instance paths** (`\\?\DISPLAY#...` or `5&3c9e07d1&0&UID256`),
  which are unique to one PC.
- **File paths**, which carry the owner's Windows account name.
- **Personal settings.** Brightness 62 describes an evening at someone's desk,
  not a monitor.

DispCtrl strips all four before showing you anything, `tools/presetcheck`
asserts it over the monitors actually attached, and `devicecheck validate`
refuses a record that still carries a path or an instance id.

## A definition

`definition.json` names what a model's codes do, especially the
manufacturer-specific ones the standard leaves open, which is where the
interesting controls usually hide. `brand.json` does the same for every model
of a manufacturer, and `common.json` for every monitor. DispCtrl layers them,
most specific last: every monitor, the brand, the models this one `extends`,
then the model itself. A code is writable only when its definition says so, and
only on a monitor that lists it. [schema/definition.schema.json](schema/definition.schema.json)
is the format, and [docs/DEVICE-LIBRARY.md](../docs/DEVICE-LIBRARY.md) the design.

## Contributing one

```
dispctrl devices list                    # every monitor this PC has seen
dispctrl devices show --monitor 2        # every code, and which nobody has named
dispctrl devices probe --monitor 2       # watch the unnamed ones while you use the OSD
dispctrl devices map --monitor 2 --code 0xE2 --name "Preset mode" --values "0x00=Standard,0x0B=ComfortView"
dispctrl devices share --monitor 2 --open
```

`share` opens one GitHub issue for the model, with its record and a JSON block
of your mappings. Nothing is sent until you press Submit there. The intake
workflow turns the issue into a pull request that adds the model's folder, for
a maintainer to review. In the app, the **Devices** page does the same.

There is no access token in DispCtrl and it makes no network requests. A token
shipped inside an application is a token handed to everyone who installs it,
and a submission the application makes on your behalf is not one you agreed to.

## Maintaining

```
dotnet run --project tools/devicecheck -- validate devices   # layout, rules, leaks
dotnet run --project tools/devicecheck -- index devices      # CATALOG.md and index.json
dotnet run --project tools/devicecheck -- migrate devices    # from the old flat layout
```

## What the library is for

- **Knowing which codes a model really implements,** as opposed to which it
  advertises. This Dell advertises an ambient light sensor at `0x66` and
  answers a value that is not one of the two it says it supports.
- **Naming manufacturer-specific codes,** so every owner of the model gets the
  control, not only the one who worked it out.
- **Knowing a model is OLED without asking its owner,** so burn-in protection
  can offer itself to the right panels.
