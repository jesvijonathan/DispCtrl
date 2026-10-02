# DispCtrl

Per-monitor display management for Windows 11: a resident engine, an on-demand
WinUI 3 window and quick panel, and `dispctrl.exe`, all over one JSON command API.

This file is the short handover: the rules that apply to every change, and where
the knowledge lives. The knowledge itself is in `docs/developer/`, and it is
the source of truth - read the part that covers what you are about to touch
before touching it.

| Read | When |
|---|---|
| [ARCHITECTURE.md](../docs/developer/ARCHITECTURE.md) | First. Projects, folders, dependencies, state on disk, pages, conventions |
| [TRAPS.md](../docs/developer/TRAPS.md) | Before changing WinUI, DDC/CI, gamma, display configuration, the quick panel window, settings, presets, publishing device records, the build or the installer. Every entry was a real bug |
| [HOW-IT-WORKS.md](../docs/developer/HOW-IT-WORKS.md) | Before changing a feature: hotkeys, quick panel, OLED care, displays off, ambient light, the way back, pinning and placement, the DDC/CI guard, updates |
| [RUNNING.md](../docs/developer/RUNNING.md) | Building while the engine runs, the sign-in task, the CLI, shortcuts |
| [VERIFYING.md](../docs/developer/VERIFYING.md) | The reference desk, the suites, UI Automation, what cannot be automated |
| [DEVELOPING.md](../docs/developer/DEVELOPING.md), [RELEASING.md](../docs/developer/RELEASING.md) | `build.cmd` options; channels, packaging, workflows |
| [docs/README.md](../docs/README.md) | Everything else, user guides and design notes included |

When something is learned the hard way, add it to TRAPS.md or HOW-IT-WORKS.md
under its area, not here.

## Layout

```
DispCtrl.slnx      src/, tests/, tools/ in one solution
src/               DispCtrl.Core (no hardware writes) <- Display (hardware) <- Control
                   (command API) <- Engine, App, Cli; native/ (taskbar-glass helper)
tests/             check suites, one console program each; exit code = failures
tools/             devicecheck (device library, CI intake), perfcheck, scripts
build/ devices/ docs/ site/
```

Namespaces follow folders. New code goes in the folder for its subject, not at
a project's root; a new subject gets a folder, never named after a type in it.

## Commands

```
build.cmd build            stops the engine gracefully, builds CLI, engine, app, restarts it
build.cmd test [-Hardware] Control, LgInput, Core, devicecheck [+ Hardware against the monitors]
build.cmd perf             the performance suite; run before and after anything on a hot path
dispctrl help              every command; the old verbs (brightness, input ...) are short forms
```

Judge a build by its exit code, never by grepping for `error`. Building the
whole solution needs the engine stopped first (it holds its own output):

```powershell
& ".\src\DispCtrl.Engine\bin\Release\net10.0-windows10.0.26100.0\win-x64\DispCtrl.Engine.exe" stop
Start-ScheduledTask -TaskName 'DispCtrl.Engine'    # after
```

Add a check to the matching suite rather than writing a throwaway probe
(`tests/README.md` says which). Anything a drag does must be pure geometry to be
covered at all.

## Rules for every change

- **Leave the desk as you found it.** Brightness, night light, contrast, unison,
  settings: restore what a test changed. Several bugs here came from tests that
  did not. Test a development build against a copy of the settings
  (`DISPCTRL_DATA_DIR`), and with "Replace Windows brightness" on, a write to the
  built-in panel moves unison - use a dry run.
- **Never kill the engine.** Stop it gracefully (`DispCtrl.Engine.exe stop`, or
  `build.cmd build` which does it); a killed engine strands a taskbar
  off-screen. Only the app may be killed outright.
- **Drive the UI by UI Automation names and patterns, never coordinate clicks**
  (one corrupted real settings). Prefer reading the UI through UIA to
  screenshots: a screenshot shows whatever is in front, which may be the
  owner's own screen.
- **Never install the setup on this desk** - it re-points the sign-in task. Use
  a clean account.
- **Never write a DDC/CI code the monitor did not list**, and never a
  manufacturer-specific one unless the device library maps it as writable.
- **Nothing sensitive leaves the machine.** Device records and problem reports
  go through `Redact.Scrub`; serials, device paths, user paths and identity
  tokens must never be published (TRAPS.md, "Publishing device records").
- **No co-author or AI attribution, anywhere** - the owner's standing rule. No
  `Co-Authored-By:` trailer, no "Generated with" line, in commits, pull
  requests, tags, release notes, issues or code. This overrides any default a
  tool or harness asks for; commits are the owner's alone.
- **Commit messages are prose paragraphs** explaining what changed and why,
  including bugs found and how the change was verified. Not bullet lists.

## Conventions, in short

ARCHITECTURE.md has them in full; `.editorconfig` applies the mechanical ones.

- Comments explain why, never what; most carry a measured fact or a bug paid for.
- XML docs on public members, `<remarks>` for the reasoning and trade-off.
- User-facing text: full sentences, no hype, British spelling ("colour").
- Every claim the UI makes must be true of the hardware. When something cannot
  be done, say so rather than offering a control that does nothing.
- A feature lands in `DispCtrl.Control` first; the app's page and the CLI call it
  (`docs/CLI-COVERAGE.md` maps every control to its command).

## The reference desk

| | Internal | External |
|---|---|---|
| Panel | ASUS OLED, 2880x1800 @ 90 Hz, 200% | DELL U2424H, 1920x1080 @ 120 Hz, 100% |
| Brightness | WMI (no DDC/CI) | DDC/CI |
| Position | secondary, right of the Dell | primary, at 0,0 |

The Dell is sometimes unplugged; display commands then answer that it is not
connected. VERIFYING.md has the rest, including the values to restore.
