# Documentation

## Using DispCtrl

| Guide | Contents |
|---|---|
| [Features](FEATURES.md) | Every page, option and shortcut, in the order the app shows them |
| [Settings reference](SETTINGS.md) | Every stored setting, its default, its values and what it does |
| [Command line](CLI.md) | Every command, its options, JSON output and exit codes; the short forms |
| [Command coverage](CLI-COVERAGE.md) | Every control in the app, page by page, and the command behind it |
| [Presets and desk profiles](PRESETS.md) | Capturing a whole desk and applying it, by hand or when its displays connect |
| [Custom controls and features](CUSTOM-CONTROLS.md) | Mapped monitor controls, named features, quick panel tiles |
| [LG input switching](LG-INPUT-SWITCHING.md) | The alternate DDC/CI input path LG monitors need |
| [Device library](DEVICE-LIBRARY.md) | Monitor definitions, naming undocumented codes, sharing a record |
| [Examples](examples) | Scripts for display events, layouts and requests |
| [Changelog](CHANGELOG.md) | What changed in each release |

## For developers

Start with the architecture, then read the part of the traps and the feature
notes that covers what you are changing.

| Guide | Contents |
|---|---|
| [Architecture](developer/ARCHITECTURE.md) | The projects, what each may do, state on disk, pages, conventions |
| [Developing](developer/DEVELOPING.md) | Setting up a machine, `build.cmd` and `build/build.sh`, editors, before a pull request |
| [Running against a live desk](developer/RUNNING.md) | Stopping and restarting the engine, the sign-in task, the CLI, shortcuts |
| [How each feature works](developer/HOW-IT-WORKS.md) | Feature by feature: what it does to the hardware and why |
| [Traps already paid for](developer/TRAPS.md) | Every mistake that has already cost a crash or a setting; do not repeat them |
| [Testing and verifying](developer/VERIFYING.md) | The suites, the reference desk, UI Automation, what cannot be automated |
| [Control architecture](developer/CONTROL-ARCHITECTURE.md) | The command API, the broker and ordered apply |
| [Quick panel](developer/QUICK-PANEL.md) | Adding a tile, a row or a switch |
| [Performance](developer/PERFORMANCE.md) | The performance suite, its budgets, and what it has found |
| [Releasing](developer/RELEASING.md) | Channels, packaging, the workflows and their secrets |
| [Tests](../tests/README.md) | What each suite needs and covers |

## Design notes

Reasoning behind decisions, kept for whoever revisits them:
[status](design/STATUS.md), [roadmap](design/ROADMAP.md),
[feature candidates](design/FEATURES.md), [prior art](design/PRIOR-ART.md),
[implementation checklist](design/IMPLEMENTATION-CHECKLIST.md),
[desk plan](design/DESK-PLAN.md), [display performance](design/PERFORMANCE.md).
