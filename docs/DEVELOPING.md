# Developing DispCtrl

One script does the work on each system:

| | Windows | Linux, macOS, WSL |
|---|---|---|
| Entry point | `build.cmd` (double-click for a menu), or `build/dev.ps1` | `./build.sh` |
| Builds | everything | everything except the window |
| Runs | the app, the engine, every check | the checks that need no Windows API |
| Packages | zips, installer, MSIX | none |

## Windows: from nothing to running

```powershell
git clone https://github.com/jesvijonathan/DispCtrl.git
cd DispCtrl
.\build.cmd setup -Install     # asks winget for .NET 10 and MinGW-w64 if missing
.\build.cmd build
.\build.cmd run engine         # tray icon, hotkeys, taskbar
.\build.cmd run app            # the window
```

Or double-click `build.cmd` and choose from the menu. The menu shows the
current options on its first line and can switch them.

### What `setup` does

- **Checks the machine** (`doctor` on its own does only this): Windows 11,
  .NET 10 SDK, MinGW-w64 `g++`, PowerShell 7, git, MakeAppx, signtool and
  Inno Setup.
- **With `-Install`, and only then**, asks winget for what is missing and has
  to be installed system-wide:
  - the .NET SDK (`Microsoft.DotNet.SDK.10`);
  - MinGW-w64 (`BrechtSanders.WinLibs.POSIX.UCRT`).
- **Fetches a portable Inno Setup** into `.tools/`, from the `Tools.InnoSetup`
  NuGet package, without installing anything.
- **Restores every project.** That also brings MakeAppx and signtool, from the
  `Microsoft.Windows.SDK.BuildTools` package the app references, so the MSIX
  needs no Windows SDK install.
- **Saves the tool paths** to `build/local.json`, which is git-ignored.

MinGW is found in `DISPCTRL_CXX`, then `local.json`, then `PATH`, then
`C:\toolchains\mingw64`, `.tools\mingw64`, MSYS2's `ucrt64`, and winget's
WinLibs folder.

### Commands and options

```
.\build.cmd doctor | setup [-Install] | build | test [-Hardware] | perf [--all|--quick|...] | run engine|app|panel|cli <args>
            publish | installer | package | release | clean [-Yes] | options
Options:    -Configuration Debug|Release   -Channel beta|stable|test   -Version 0.2.0
            -NoNative / -Native   -NoRestart   -Sign   -Rebuild   -Keep   -Open
```

**`build`** stops the engine gracefully if one is running from this
repository, builds, and starts it again: through its sign-in task if that task
points at this build, directly if not. Only the window and the CLI are killed.
A killed engine leaves a hidden taskbar off-screen. Use `-NoRestart` to leave
it stopped. `-Rebuild` removes every `bin` and `obj` folder first, for a build
with nothing left over from an earlier one. Either way, it ends by printing
where the app, engine and CLI are; `-Open` also opens that folder.

A normal build also removes files an earlier layout left in the output folders
(`Directory.Build.targets`): MSBuild copies files, but never deletes the ones it
has stopped copying.

**`test`** runs `controlcheck`, `presetverify` and `devicecheck validate` (the
device library's layout, rules and privacy). `-Hardware` adds `presetcheck`, which reads the monitors actually
attached, including the redaction checks against their serials.

**`perf`** runs `tools/perfcheck` against what is already built in `bin`:
time, CPU (from cycle counts), memory and wake-ups, each against a budget,
with a report in `artifacts/perf/`. It is never part of `build` or `test`.
`perf --quick` (about a minute, reads only) after touching a hot path;
`perf --all` (about five minutes, drives the tray and panel, restarts the
engine, desk left alone) before a release; `--baseline
artifacts\perf\latest.json` to see what moved. Everything after `perf` goes
to perfcheck. [PERFORMANCE.md](PERFORMANCE.md) explains each row.

**Channels are compiled in.** `-Channel` is also passed to every publish as
`-p:DispCtrlChannel` (`BuildInfo` reads it). A build from source is `dev`.
Stable compiles out routine diagnostics - a log line per command, per settings
reload, per brightness key, and the start-up timings - and keeps errors;
every other channel logs them and shows its version in the window and panel
titles ("DispCtrl v0.1.3 beta"). perfcheck's sync and restart timings read
those diagnostic lines, so measure a dev, beta or test build. To check the
stable path compiles: `dotnet build <project> -c Release -p:DispCtrlChannel=stable`.

**`release`** runs publish, installer, MSIX and release notes into one folder,
`artifacts\<channel>-<version>` (for example `artifacts\beta-0.1.0`), and ends
by listing every file in it with its size and path. The folder is replaced on
each run, and earlier builds in `artifacts\` are removed first, unless you add
`-Keep`: each bundle is a few hundred MB, and a folder of stale ones is how an
old build ends up attached to a release. The engine is stopped and restarted
around it, as for `build`, because the tests build into the folders it runs
from. It is the same path the release workflow takes, so a
tag's failure can be reproduced before pushing.

**`-NoNative`** builds the engine without the taskbar-glass helper, for a machine
without MinGW. The engine then runs without glass, which it already handles.
The choice is remembered until `-Native`. It works through the MSBuild property
`SkipTaskbarGlass=true`, which can also be passed to `dotnet build` directly.

`-Configuration`, `-Channel`, `-Version` and `-NoNative` are saved in
`build/local.json`, so `options -Configuration Debug` changes every later
command. Environment variables win over it: `DISPCTRL_CXX`, `DISPCTRL_ISCC`,
`DISPCTRL_SIGN_PFX`, and `STORE_IDENTITY` / `STORE_PUBLISHER` for the MSIX.

## Releasing from GitHub Actions

Open **Actions > Release > Run workflow**, select a branch, and choose `stable`,
`beta` or `test`. Nothing needs typing: **Version** is a choice - `current`
releases what `Directory.Build.props` declares, `next patch`, `next minor` and
`next major` work the next number out from it (0.1.5 becomes 0.1.6, 0.2.0 or
1.0.0). An exact three-part version typed in the box below overrides it; typing
one and choosing a bump is refused. **Update version automatically** is on by default: it updates
the project version and MSIX template, commits those two files on the selected
branch, and creates the tag at that commit. Packaging defaults read the project
version, so there are no script defaults to bump separately.

Stable tags are `v0.1.3`, beta tags are `v0.1.3-beta`, and test tags include the
workflow run number, for example `v0.1.3-test.42`. Each new test run gets its own
tag; rerunning the same job reuses its tag if the branch still points at that
release commit. If the branch has moved on, run from the tag instead.
Beta and test releases are GitHub
prereleases, never Latest, and never sent to winget or the Microsoft Store.
All channels start as drafts unless **Publish immediately** is selected.
Test packages use the same application identity and settings as the other
channels; the channel does not provide a separate installation.

Version updates apply only to new releases from branches. Existing tags are
never moved or edited. To rebuild an existing release, select its tag as the
workflow ref; a branch run refuses to attach a different commit's packages to
that tag. A stable tag must already declare the matching version. If branch
protection prevents the automatic commit, prepare the update through your
normal pull request process; the atomic push will not leave a tag behind.

To prepare a version locally without committing or tagging:

```powershell
./build/Update-Version.ps1 -Version 0.1.3
```

Run `./build/Test-Release.ps1` to check release preparation using temporary local
Git repositories, without building packages or contacting GitHub.

## Linux, macOS and WSL

```bash
./build.sh setup     # .NET 10 into .tools/dotnet, no sudo; prints what else to install
./build.sh build     # Core, Display, Control, CLI, engine, and the checks
./build.sh test      # presetverify, devicecheck
```

Verified on WSL Ubuntu:
- Every project except the window compiles, with `EnableWindowsTargeting=true`.
- `presetverify` (70 checks) and `devicecheck` pass.

The binaries are still Windows x64: this is for editing, compiling and checking
changes, not for running DispCtrl.

What needs Windows, and why:
- **The window** (`DispCtrl.App`). The WinUI XAML compiler loads Windows-only
  DLLs (`GenXbf.dll`).
- **`controlcheck` and `presetcheck`.** They call the Windows display APIs:
  `QueryDisplayConfig`, DDC/CI, WMI.
- **The installer and the MSIX.** Inno Setup and MakeAppx are Windows tools.

Without ICU, the .NET SDK aborts at start-up. `build.sh` switches it to
invariant mode, which DispCtrl uses anyway. `sudo apt install libicu-dev` is
the tidier fix.

The engine's taskbar-glass helper is skipped by default. `./build.sh build
--native` builds it with `x86_64-w64-mingw32-g++` (package
`g++-mingw-w64-x86-64-posix`) and `pwsh`. That path is wired up but has not
been verified.

## Editors

- **Visual Studio / Rider:** open any `src/*/*.csproj`. There is no solution
  file, by choice: build the projects that changed, in dependency order.
  [CLAUDE.md](../.claude/CLAUDE.md) has the order and the traps.

## Before a pull request

```powershell
.\build.cmd build
.\build.cmd test -Hardware
.\build.cmd perf --quick     # when the change touches settings, the panel, DDC/CI or an engine loop
```

Leave the desk as you found it: any brightness, night light or monitor setting
a manual test changed must be put back. See [CONTRIBUTING.md](../.github/CONTRIBUTING.md).
