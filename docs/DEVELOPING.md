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
git clone https://github.com/jesvijonathan/Display-Control.git
cd Display-Control
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
.\build.cmd doctor | setup [-Install] | build | test [-Hardware] | run engine|app|panel|cli <args>
            publish | installer | package | release | clean [-Yes] | options
Options:    -Configuration Debug|Release   -Channel beta|stable   -Version 0.2.0
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

- **VS Code:** `.vscode/tasks.json` runs the same script. *Run Build Task*
  builds, *Run Test Task* tests, and the other `DispCtrl:` tasks cover the rest.
- **Visual Studio / Rider:** open any `src/*/*.csproj`. There is no solution
  file, by choice: build the projects that changed, in dependency order.
  [CLAUDE.md](../CLAUDE.md) has the order and the traps.

## Before a pull request

```powershell
.\build.cmd build
.\build.cmd test -Hardware
```

Leave the desk as you found it: any brightness, night light or monitor setting
a manual test changed must be put back. See [CONTRIBUTING.md](../CONTRIBUTING.md).
