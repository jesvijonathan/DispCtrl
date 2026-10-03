# Building and running against a live desk

The commands that work, and the order things have to happen in when the
engine is running and holding its own files. [DEVELOPING.md](DEVELOPING.md)
has every option of `build.cmd`; this is the operating manual around it.

## Commands

Run everything from the repo root. `build.cmd` (Windows) and `build/build.sh`
(Linux/WSL) wrap it all: `build.cmd build` stops the engine gracefully, builds
the CLI, engine and app, and restarts the engine through its task; `test`,
`perf` (the performance suite, never part of build or test), `run
engine|app|panel|cli`, `release`, and `doctor`/`setup` for a new machine.
`docs/developer/DEVELOPING.md` has every option. By hand: `DispCtrl.slnx` builds
everything (stop the engine first, below), or build projects one at a time,
in dependency order when several changed.

```bash
dotnet build src/DispCtrl.Core/DispCtrl.Core.csproj       -c Release -v q --nologo
dotnet build src/DispCtrl.Display/DispCtrl.Display.csproj -c Release -v q --nologo
dotnet build src/DispCtrl.Engine/DispCtrl.Engine.csproj   -c Release -v q --nologo
dotnet build src/DispCtrl.App/DispCtrl.App.csproj         -c Release -v q --nologo
```

**A running process locks its DLLs and the build fails with MSB3027.** Stop both
first — and stop the engine *gracefully*, or it leaves a taskbar parked
off-screen:

```powershell
& ".\src\DispCtrl.Engine\bin\Release\net10.0-windows10.0.26100.0\win-x64\DispCtrl.Engine.exe" stop
Get-Process DispCtrl.App -EA SilentlyContinue | ForEach-Object { $_.Kill() }
```

The engine unwinds asynchronously; wait for the process to disappear before
building. **Killing the engine is what strands a taskbar off-screen** — only the
app may be killed outright.

Restart the engine through its scheduled task, which is how it normally runs:

```powershell
Start-ScheduledTask -TaskName 'DispCtrl.Engine'
```

The task is `DispCtrl.Engine`: per user, a logon trigger with no delay,
priority 4 (the scheduler's default 7 is below normal), restart on failure, no
time limit and `AllowHardTerminate` off. `StartupIntegration.RegisterEngineTask`
owns it; the Settings page switch and `dispctrl startup set --engine on` both go
there. The engine migrates an old Startup-folder shortcut to it on its own and
removes the stale `Umbra.Engine` task. Started directly, it still works:

```powershell
Start-Process ".\src\DispCtrl.Engine\bin\Release\net10.0-windows10.0.26100.0\win-x64\DispCtrl.Engine.exe" run
```

**Not elevated, deliberately.** An elevated engine's tray window is cut off from
Explorer by UIPI and its command pipe from every unelevated client. The one
machine-wide write, the gamma clamp, asks for elevation itself.

Engine CLI: `status`, `run [--for <s>] [--trace]`, `stop`. Every other word
(displays, brightness, preset ...) goes to the same control terminal as
`dispctrl`; there is one command line (`DispCtrl.Control`), and the first one's
verbs are short forms rewritten by `LegacyCommands`.

`dispctrl.exe` is the scriptable front end — `dispctrl help` lists everything. It is a
**console** subsystem app, unlike the engine: a WinExe does not block the shell
that launched it, so a script gets neither output nor an exit code. That is why
there are two binaries rather than one.

It also carries an `app.manifest` declaring PerMonitorV2. Without it the process
sees virtualised coordinates, `MonitorFromPoint` resolves the wrong monitor, and
the symptoms are baffling rather than obvious: a 200% panel reports 100%, and a
monitor with a working contrast control reports none.

```
dispctrl displays
dispctrl brightness -10 --all
dispctrl nightlight 60 --from 20:00 --to 07:00
dispctrl input "DisplayPort 1" --display 2
dispctrl preset apply Evening
```

Exit codes: 0 done, 1 refused, 2 asked wrongly.

## Shortcuts

```
tools\Install-Shortcuts.ps1                       # Start menu, Release
tools\Install-Shortcuts.ps1 -Configuration Debug
tools\Install-Shortcuts.ps1 -AddToPath            # dispctrl on the user PATH
tools\Install-Shortcuts.ps1 -Remove
```

The shortcut points **at `bin\<configuration>`**, not at a copy, so rebuilding
updates what it launches. Copying the exe somewhere would freeze it at that
build and go stale silently — which is exactly the confusion that prompted it
(the engine was running from Release while only Debug was being rebuilt).

Windows removed the *Pin to taskbar* verb in 10 1903 and it has not returned;
an application cannot pin itself. The script checks the shell verbs and says so
rather than pretending. Pinning is one right-click on the Start entry.

All three exes carry `Assets\DispCtrl.ico` via `<ApplicationIcon>`. Setting it
only on the shortcut would leave the taskbar button and alt-tab generic, and
the engine needs it for the tray icon's logo style.
