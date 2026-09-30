# Custom monitor controls and features

Mappings give a monitor's VCP codes names, values and permission to write.
They work across brands; each model's meanings remain in its device definition.
Start with **Displays > Monitor controls > Learn a setting**, or **Devices >
Learn a setting**. Wait for “Watching”, change one setting using the monitor's
own menu, then select the code that changed. Name the setting and each observed
value, using **Add another value** before changing the monitor again. Choose
`range` for a numeric control or `choice` for named values.

Enable the write checkbox only when you intend to let DispCtrl change that
setting. Saving records observations; it does not prove that writes work.
For a range, enter its maximum value. Selecting another code resets write
permission; existing named values for that code are retained.
The popup reads advertised unnamed codes and does not sweep arbitrary codes.
Some monitors do not expose their menu settings through DDC/CI.

Writable mapped ranges and choices appear on Displays and in the quick panel's
monitor-controls tile. Share a mapping through **Devices > Contribute** after
checking it. See [Device library](DEVICE-LIBRARY.md) for review and contribution.

## Map and use a code from the terminal

Use the monitor number or stable token from `dispctrl devices list`. The code
and values below are examples: replace them with your monitor's observations.

```powershell
dispctrl devices map --monitor 2 --code 0xE2 --name "Picture mode" --kind choice --values "0x00=Standard,0x0C=Movie" --writable
dispctrl display control --monitor 2 --name picture-mode --value Movie
dispctrl display control --monitor 2 --name picture-mode --value next
dispctrl display control --monitor 2 --name contrast --value +5
```

Controls accept a name, key or hexadecimal code. `next` and `previous` cycle
known choices; `+N` and `-N` step numeric ranges. Cycling and relative changes
use the current reading when available. Write-only controls may cycle from the
last sent value while the mapping is unchanged; otherwise cycling starts at
the first/last known choice. Relative changes need a
current reading. For a write-only input mapping, prefer a named value.
For alternate LG input transport, see [LG input switching](LG-INPUT-SWITCHING.md).
`--monitor all` targets external monitors. Every target is validated before
any write; hardware failures still report which writes completed or remain pending.

## Custom features

Open **Hotkeys > Custom features**, add a feature, and enter one step per line:

```text
set 2 picture-mode Movie
set 2 brightness 35
wait 300
dispctrl nightlight set --enabled on
```

**Test** validates the current editor text without writing monitor settings,
starting programs or running scripts. Read commands may still read the current
state. **Run** executes the current text. **Save** makes it available to hotkeys,
tiles and the CLI. Steps run in order and stop at the first failure; completed
steps are not rolled back. A successful driver write is not proof that the
monitor accepted it.
Only one feature can execute at a time. Nested features are limited to four
levels and 1,000 total steps. Repeated clicks while an editor action is busy
do not start another action.

Other steps are `run "PATH" arguments` (opens a program, document or link) and
`script "PATH" arguments` (waits for completion, up to five minutes). Relative
script paths are looked for in the settings directory's `scripts` folder.
Blank lines and lines starting with `#` are ignored through the end of that
line, including any semicolons. Semicolons outside quotes can separate other
steps. Inside a quoted argument, double a quote to include it literally:
`dispctrl features run "My ""Cinema"" mode"`. The editor's grammar lists all forms.
Batch script paths and arguments cannot contain shell operators, quotes,
percent signs or exclamation marks; use a PowerShell script or executable for
those arguments. Script output is captured in the result, up to 4,096 characters
per stream, so it does not interfere with CLI JSON output.

```powershell
dispctrl features add --name Cinema --steps "set 2 picture-mode Movie; wait 300"
dispctrl features list
dispctrl features run Cinema --dry-run
dispctrl features run Cinema
dispctrl features remove --name Cinema
```

Choose **Run a custom feature** in a hotkey, or use **Use a saved feature** when
editing a quick-panel command tile. Control hotkeys also support set, next,
previous, increase and decrease directly. A tile can run a single control with
`display control --monitor 2 --name picture-mode --value Movie`.
If you rename a feature, update hotkeys and tiles that refer to its old name.

## Advanced raw writes

**Settings > Allow raw DDC/CI writes (advanced)** is off by default. The CLI
equivalent is `dispctrl ddc set --raw-writes on`. With it enabled, an explicit
`--raw` allows a numeric write to an unmapped code:

```powershell
dispctrl display control --monitor 2 --name 0xE9 --value 1 --raw --dry-run
```

Remove `--dry-run` only for a code and value you intend to write. Unknown codes
can change settings the monitor's menu cannot undo. Factory reset `0x04` still
requires the separate factory-reset command. A feature uses `set 2 0xE9 1 raw`.
Turn the advanced setting off again when finished. Ordinary mapped writes do
not need raw access.
