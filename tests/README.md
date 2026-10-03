# Tests

Every suite is a small console program: it prints one line per check and exits
with the number that failed, so a script needs nothing but the exit code. There
is no test framework, on purpose - several checks drive real Windows state
(monitors, Explorer's taskbars) in an order that matters, and a runner that
parallelises or reorders them would break exactly those.

| Suite | Needs | What it covers |
|---|---|---|
| `DispCtrl.Core.Checks` | nothing (runs on Linux too) | Parsing, geometry, the settings merge, presets, desk profiles, window placement, the taskbar parking plan |
| `DispCtrl.Control.Checks` | Windows, no monitors | The JSON command API and terminal against a scratch settings folder |
| `DispCtrl.LgInput.Checks` | Windows, no monitors | LG alternate input switching; sends no hardware commands |
| `DispCtrl.Hardware.Checks` | the monitors attached | EDID, capabilities, presets captured from the desk, and the redaction of device records end to end |
| `DispCtrl.Taskbar.Checks` | a secondary taskbar, by hand | Taskbar hide and reveal over a maximized window; see its README |
| `DispCtrl.TaskbarGlass.Checks` | the glass helper built, the engine stopped, by hand | The taskbar-glass helper attached to Explorer, each look rendered and screenshotted |

Run them from the repository root:

```
build.cmd test              Core, Control, LgInput, and devicecheck
build.cmd test -Hardware    the same, plus Hardware
./build.cmd test             Core and devicecheck, on Linux, macOS or WSL
```

or one at a time with `dotnet run --project tests/<Suite> -c Release`.

`tools/devicecheck` (the device library's validator and intake) and
`tools/perfcheck` (the performance budgets, `build.cmd perf`) are tools rather
than suites: they also run in CI and by hand for other jobs.

## Adding a check

Add it to the suite whose "needs" column matches, rather than writing a
throwaway probe. Prefer the hardware-free suites: a rule that can be made pure
(geometry, parsing, a decision taken from numbers) belongs in
`DispCtrl.Core.Checks`, where CI runs it on every push. Synthetic pointer input
does not reach a WinUI canvas, so anything a drag does has to be pure geometry
to be covered at all.
