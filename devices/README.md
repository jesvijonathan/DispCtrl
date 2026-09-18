# Devices

What real monitors report about themselves.

Umbra can only offer a control it knows a monitor has. It learns that by asking
the panel over DDC/CI — but only about the panel in front of it. Every record
here comes from someone who owned a monitor this project does not, ran
`umbra contribute`, read what it produced, and submitted it.

## What a record is

One file per **model**, named for its EDID manufacturer and product code:

```
DEL-A234.md      Dell U2424H
SDC-4154.md      the Samsung panel in a Zenbook
```

That key is what every unit of a model carries. It is deliberately not the
identifier Umbra uses internally, which ends in the serial number.

A record holds the connector, the physical size, the modes, the panel
technology, the verbatim MCCS capabilities string, and every VCP code the
monitor lists with its range or its permitted values.

## What a record must never hold

- **Serial numbers.** The single most identifying thing a monitor reports.
- **Device instance paths** (`\\?\DISPLAY#...`), which are unique to one PC.
- **File paths**, which carry the owner's Windows account name.
- **Current settings.** Brightness 62 describes an evening at someone's desk,
  not a monitor.

`umbra contribute` strips all four before showing you anything, and the checks
in `tools/presetcheck` assert it over the monitors actually attached to the
machine running them. If you are filing a record by hand, strip them yourself.

## Contributing one

```
umbra contribute                 # every attached monitor, printed, nothing sent
umbra contribute --display 2     # just that one
umbra contribute --display 2 --open
```

`--open` fills in a GitHub issue and opens it in your browser. Nothing is sent
until you press Submit there. The same text is saved to
`%LOCALAPPDATA%\Umbra\devices\` either way.

The panel has the same thing under **Displays → Help Umbra support more
monitors**, which shows the full text before it opens anything.

There is no access token in Umbra and it makes no network requests. A token
shipped inside an application is a token handed to everyone who installs it,
and a submission the application makes on your behalf is not one you agreed to.

## What they are for

- Knowing which codes a model really implements, as opposed to which it
  advertises. Monitors list controls they do not honour — this Dell advertises
  an ambient light sensor at `0x66` and answers a value that is not one of the
  two it says it supports.
- Naming manufacturer-specific codes, which the MCCS standard leaves open and
  which are where the interesting controls usually hide.
- Knowing a model is OLED without asking its owner, so burn-in protection can
  offer itself to the right panels.
