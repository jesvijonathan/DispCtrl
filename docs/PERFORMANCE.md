# Performance

`tools/perfcheck` measures what DispCtrl costs: time, CPU, memory and
wake-ups, from a settings load to a tray click. Every number has a budget,
and a run can be compared with an earlier one.

```
build.cmd perf                          core, displays, engine, cli (reads only)
build.cmd perf --all                    and ui, writes, restart
build.cmd perf --suites ui --quick      one suite, fewer samples
build.cmd perf --baseline artifacts\perf\latest.json
```

Or `dotnet run --project tools/perfcheck -c Release -- <options>`. It measures
the binaries already in `bin`; build first. Reports go to `artifacts/perf/`
(`latest.json` is always the newest). The exit code is the number of
measurements over budget, plus regressions against `--baseline`, plus
suites that failed.

## Suites

| Suite | Changes anything? | Measures |
|---|---|---|
| `core` | no - scratch settings folder, child process | settings load / save / merge, the file-system floor under a save, control commands, preset JSON and diff, allocations |
| `displays` | no | enumeration, topology, per-display brightness / HDR / scaling / details, capabilities cold and cached, preset capture |
| `engine` | re-saves settings unchanged | resident memory, handles, GDI/USER, idle CPU and wake-ups, broker round trips, save-to-reload sync and its CPU |
| `cli` | no | `dispctrl` start to exit, CPU and peak memory per command |
| `ui` | opens and closes the quick panel | cold start, click to visible / focused / settled, frame gaps in the slides, Simple / full view switch, CPU and leaks per cycle, idle cost open and hidden, idle trim, closing by itself, the main window |
| `writes` | writes values the hardware already holds | DDC/CI brightness write and read-back, `display.set`, dry-run `apply`; with `ui`, the brightness slider to the engine applying it (its own log) and to the saved file, then restored |
| `restart` | restarts the engine gracefully | stop, start through the sign-in task, time to broker / taskbar / hotkeys / tray icon, start-up CPU and memory, settled idle |

`ui`, `writes` and `restart` need the desk left alone for a few minutes: they
drive the tray icon through UI Automation (never coordinate clicks) and
measure the screen. Every one leaves the desk as it found it and says so in a
row (`displays not restored after the slider test`, budget 0).

## Reading it

- **Medians against budgets.** One descheduled sample is the machine, not the
  code; p95 and worst are printed for judgement.
- **CPU is from cycle counts** (`QueryProcessCycleTime`, `SystemProcessInformation`),
  not `TotalProcessorTime`, which moves in 15.6 ms scheduler ticks: an idle
  process reads 0 or 78 ms at random, and that once blamed a change for noise.
- **Wake-ups are context switches.** Idle cost is how often a process wakes,
  not how much it does when it does (see "Idle cost is wake-ups" in
  `.claude/CLAUDE.md`).
- **Measure warm.** The first run of anything freshly built is slow because
  the antivirus scans new DLLs; a one-off probe once read 64 ms where the real
  cost was 8.
- **A regression** is 25% slower *and* past a noise floor per unit (1 ms,
  4 MB, 5 wake-ups/s...), so microsecond operations do not flap.

## What it has found

| Finding | Before | After |
|---|---|---|
| Load straight after a save re-read the file this process had just written; the first open of a renamed file is scanned (5.6 ms vs 0.14 ms). `SettingsStore` keeps its own last write. | load + save 8.15 ms | 2.42 ms |
| `Process.SessionId` snapshots every process to read one number, in every process that names the pipe or a mutex. `Session.Id` asks Windows. | 7.9 ms | 0.28 ms |
| `display.set --brightness` read the range twice: four DDC/CI transactions for one change (Dell, same value). | 250 ms | 216 ms |
| The quick panel's slide moved the window with a plain `SetWindowPos` each frame. Now a bare move (no redraw, no changing message). | ~129 ms CPU per open + close | ~115 ms |
| The glass-taskbar clip asked for a repaint with every region change. | +12% per cycle | within noise |

Known and left: a `dispctrl` command is ~105 ms of CPU even ReadyToRun, about
half of it the runtime starting; the slide itself is most of a panel cycle's
CPU because WinUI renders a frame per step.
