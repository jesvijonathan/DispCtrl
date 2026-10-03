# Architecture

How DispCtrl is put together: the projects and what each may do, the state it
keeps on disk, the app's pages, and the conventions the code follows.
[HOW-IT-WORKS.md](HOW-IT-WORKS.md) goes feature by feature;
[TRAPS.md](TRAPS.md) is the list of mistakes already paid for.

## Projects

Dependencies run one way, top to bottom: nothing in Core may reference
Display, nothing in Display may reference Control, and the three front ends
(engine, app, CLI) meet only in Control and the settings file.

```
src/
  DispCtrl.Core       no hardware writes. Pure rules, the settings model, presets.
    Displays/         enumeration, identity tokens, EDID and the PNP names
    Arrangement/      the legal positions of a display and the physical layout
    Color/            night light, the gamma clamp, the ambient-light curve and filter
    Protection/       idle and activity per display, focus geometry
    Placement/        where a window sits relative to its display
    Shell/            taskbar parking, the quick panel's place
    Settings/ Presets/ Devices/ Machine/ Caching/
  DispCtrl.Display    every call that changes something.
    Ddc/              the one DDC/CI channel, its guard, capabilities, raw I2C per GPU
    Light/            brightness (WMI and DDC), adaptive brightness, light sensors
    Topology/         modes, HDR and scaling, VRR, orientation, the desktop arrangement
    Shell/            taskbar auto-hide, wallpaper COM, startup task, media sessions
    Reports/          the display report and the problem report
    Devices/ Placement/ Presets/
  DispCtrl.Control    the JSON command API: ControlService (one partial per area in
                      Operations/), the terminal, the named-pipe broker client,
                      LegacyCommands (the first CLI's verbs as short forms)
  DispCtrl.Engine     resident. Taskbars, night light and dimming, OLED care and focus,
                      hotkeys, the tray, triggers, per-app and per-desk presets
  DispCtrl.App        WinUI 3 window and quick panel, launched on demand
    Views/Pages/ Views/Controls/ Views/QuickPanel/ Views/Dialogs/ ViewModels/ Services/
  DispCtrl.Cli        dispctrl.exe, console subsystem: the control terminal, nothing else
  native/             the taskbar-glass helper Explorer loads (C++, MinGW)
tests/                check suites, one console program each (tests/README.md)
tools/                devicecheck (device library and intake), perfcheck, scripts, promo-video (the release video; never packaged)
build/                dev.ps1 behind build.cmd (at the root) and build.sh, packaging, the installer
devices/              the reviewed device library, one folder per model
docs/                 user documentation; developer/ for contributors; design/ notes
```

Namespaces follow folders (`DispCtrl.Display.Ddc`, `DispCtrl.App.Views.Pages`),
with one exception: the files in `Control/Operations/` are partials of
`ControlService` and share its namespace. No folder is named after a type in
it, or the namespace would shadow the class - hence `Light`, not `Brightness`.

Clients share atomic, merge-aware `settings.json`; the engine watches it with a
`FileSystemWatcher`: a DispCtrl save (one rename of a finished file) is reloaded
at once, an in-place edit after a 120 ms debounce (see "Settings file
(sharing)"). The engine also hosts a user/session-scoped
named-pipe command broker. The CLI falls back to local execution when the broker
is absent. See [CLI.md](../CLI.md) and
[IMPLEMENTATION-CHECKLIST.md](../design/IMPLEMENTATION-CHECKLIST.md) for coverage
and remaining migration work.

`DispCtrl.Display` used to be the app's alone, on the reasoning that every call in
it is a deliberate user action. Per-app preset rules made those same calls
background work, so the engine references it too.

## State on disk

```
%LOCALAPPDATA%\DispCtrl\settings.json     shared, engine watches it
%LOCALAPPDATA%\DispCtrl\engine.log        engine's rolling log
%LOCALAPPDATA%\DispCtrl\displays.log      display report, rewritten whole
%LOCALAPPDATA%\DispCtrl\presets\*.json    one file per preset, name = file stem
```

---

## Pages

The Help page's **Report a problem** uses the `report` control command. The
report includes scrubbed diagnostics, previews before opening GitHub, and carries
overflow in `paste` instead of exceeding the issue-link limit. Saved monitor
tokens are scrubbed too because old logs can name disconnected displays. The
CLI prints the report and link without opening a browser or touching the clipboard.

`Displays`, `Brightness`, `Screen care`, `Windows`, `Taskbar`, `Presets`,
`Quick panel`, `Hotkeys`, `Devices`, `Miscellaneous`, `Settings` (the engine's state and files among them), `Help`, `About`.
Miscellaneous (2026-10-03) holds what is about Windows rather than the
displays or DispCtrl: the sign-in and lock screen switches, moved from
Settings, and tools (refresh the taskbar, put every display back).
Brightness, Screen care and Windows were split off Displays (2026-10-02): it had
grown to 180 cards, every desk-wide feature beside the displays themselves.
Each feature leads with its essentials and folds the rest behind "Advanced
options" (`Fold`: a ToggleButton bound to `Open`, the advanced cards' Visibility
bound to it, never a nested expander); a card that already shows only when it
applies combines the two (`OledSecondStageVisibility`, `NightLightThemeVisibility`,
and on a display card the `Advanced(open, visibility)` x:Bind function). Settings
ends with an Advanced heading (the DDC/CI guard, raw writes, logging); the
Quick panel page leads with what the panel shows, then groups the rest as the
icon, size, opening, closing and position, and the mouse wheel; the taskbar glass options show only with glass on. `docs/CLI-COVERAGE.md` maps every control on every
page to its command; a new feature lands in `DispCtrl.Control` first and the
page calls it. Taskbar was split out of Settings: reveal behaviour, the four polling
intervals (which had no UI at all before, only settings.json), and Windows'
global auto-hide. What stayed in Settings is what is not about the taskbar —
logging and the reset.

Which monitors hide their taskbar stays per display, on the Displays page.

`PresetChanges` is a `UserControl`, not markup repeated twice: the docked bar and
the Presets page both show the drift sign, and two copies would disagree the
first time either changed.

## Conventions

Match what is there. It is deliberate and consistent.

- Comments explain **why**, never what. Most carry a fact that was measured or a
  bug that was paid for. Do not add narration.
- XML docs on public members; `<remarks>` for the reasoning and the trade-off.
- Prose style throughout: full sentences, no hype, British spelling in user-
  facing strings ("colour", "centre").
- Commit messages are prose paragraphs explaining what changed and why,
  including bugs found and how they were verified. Not bullet lists.
- **No co-author or AI attribution, anywhere - the owner's standing rule.**
  Never add a `Co-Authored-By:` trailer, a "Generated with" line, or any
  other attribution to an assistant or tool in commits, pull requests, tags,
  release notes, issues or code. This overrides any default attribution a
  tool or harness asks for; commits are the owner's alone.
- Every user-facing claim must be true of the hardware. When something cannot be
  done — the primary taskbar, ambient light on a panel without a sensor — say so
  in the UI rather than offering a control that does nothing.

---
