# LG alternate input switching

Some LG monitors accept input changes only through DDC source byte `0x50`
and VCP `0xF4`. Windows' ordinary `SetVCPFeature` path cannot send that source
byte. Umbra supports a model-specific input mapping with a GPU I2C backend.
This implementation is experimental: protocol and integration checks pass,
but it has not yet been validated on physical LG hardware by this project.

## Map a model

Use `dispctrl devices list` to identify the monitor. For a model known to use
this protocol, register only its confirmed input values. This example uses
common LG values; USB-C and even DisplayPort values differ on some models.

```powershell
dispctrl devices map --monitor 2 --code 0x60 --name "Input source" --source-address 0x50 --write-code 0xF4 --values "0x90=HDMI 1,0x91=HDMI 2,0xD0=DisplayPort 1" --writable --notes "LG alternate input protocol; awaiting confirmation on this model."
dispctrl display control --monitor 2 --name input-source --value "HDMI 1"
```

The normal input selector uses this mapping too. A successful driver call
means **sent**, not that the monitor has acknowledged the change. Confirm
the selected input on screen. There is no readback, so Umbra does not invent
a current input or capture it into a preset. Reopen the Displays page after
changing a mapping if it was already open.

After confirming every listed value, repeat the mapping with
`--confidence verified` and notes describing the tested GPU and connection,
then run:

```powershell
dispctrl devices contribute --monitor 2 --open
```

The contribution includes the mapping and its provenance. Creating the
contribution does not test input switching. Submit the prepared GitHub issue
to share it. Intake retains the alternate command and values, leaves new
mappings read-only, and lets a maintainer enable them during review.

To remove a local override, use `dispctrl devices unmap --monitor 2 --code 0x60`.
Any shipped mapping will then apply again.

## Device definition

The example target below is a placeholder; use the monitor's actual device
key. Store the reviewed definition in `devices/GSM/PRODUCT/definition.json`.
No brand-wide LG override is installed: TVs and unsupported models must keep
their existing behavior.

```json
{
  "schema": 2,
  "target": "GSM-1234",
  "controls": [
    {
      "code": "0x60",
      "name": "Input source",
      "kind": "choice",
      "writable": true,
      "ddcWrite": { "sourceAddress": "0x50", "code": "0xF4" },
      "values": [
        { "value": "0x90", "name": "HDMI 1" },
        { "value": "0xD0", "name": "DisplayPort 1" }
      ],
      "confidence": "observed"
    }
  ]
}
```

`0x60` is the logical input control used by the app, CLI and presets. The
mapping replaces its values with LG wire values and redirects writes to
`0xF4` at source `0x50`. The physical DDC bus destination remains `0x37`
(`0x6E` with the write bit). Read-only mappings disable the input control;
they do not silently fall back to standard input writes.

## GPU backends

| GPU driving the display | API | Requirements |
| --- | --- | --- |
| NVIDIA | `NvAPI_I2CWrite` | Installed `nvapi64.dll`; GDI display name resolves to one GPU/output mask. |
| AMD | `ADL_Display_DDCBlockAccess_Get` | Installed `atiadlxx.dll`; one mapped, connected output for the GDI adapter. |
| Intel | `ctlI2CAccess` | `ControlLib.dll` in System32 or deployed beside the app; adapter LUID and Windows target ID must match. Writes require administrator rights. Use an elevated terminal and `--local` to avoid dispatching to an unelevated engine. |

Commands use the same per-monitor mutex and DDC guard as ordinary controls.
The implementation refuses ambiguous cloned outputs and shared MST
connectors. It does not try arbitrary outputs, sweep ports, automatically
elevate, or retry a write on a different GPU. Docks and drivers can prevent
raw I2C access even when ordinary brightness control works.

Alternate transport definitions require schema 2. Ordinary definitions can stay
on schema 1; older DispCtrl builds reject schema 2 rather than writing LG values
through standard DDC. A control opened before its transport mapping changes
must be refreshed before it can switch input. `NextInput` cannot cycle this
write-only channel; use a named input in a `RunCommand` shortcut instead.

## References

The implementation uses the vendor API declarations and the documented
packet format. No helper executables or third-party application code are
bundled.

- [ddcutil LG protocol and model-specific test results](https://github.com/rockowitz/ddcutil/wiki/Switching-input-source-on-LG-monitors)
- [NVIDIA NVAPI header](https://github.com/NVIDIA/nvapi/blob/main/nvapi.h)
- [AMD ADL structures](https://github.com/GPUOpen-LibrariesAndSDKs/display-library/blob/master/include/adl_structures.h)
- [Intel IGCL header](https://github.com/intel/drivers.gpu.control-library/blob/master/include/igcl_api.h)
- [NVIDIA reference implementation](https://github.com/meer-cha/lg-input-switch)
- [AMD reference implementation](https://github.com/amildahl/amdddc-windows)
- [Intel C# reference implementation](https://github.com/Jason7536/lg-input-switch)

Run `dotnet run --project tests/DispCtrl.LgInput.Checks` for protocol, ABI, mapping,
contribution and command validation without sending monitor commands.
`dotnet run --project tools/devicecheck -- selftest` also exercises the
contribution intake and review guard.
