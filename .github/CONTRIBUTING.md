# Contributing to DispCtrl

Thank you for helping. DispCtrl talks to real hardware, and every monitor behaves
a little differently, so reports from desks other than the author's are worth
more than almost any code.

## The most useful contribution: your monitor

```powershell
dispctrl devices contribute --monitor 2 --open     # or: the Devices page in the app
```

This builds a record of what your monitor is (its EDID, modes and DDC/CI codes),
with serial numbers, device paths and your account name removed. It shows you
the text and opens a prefilled issue for you to submit. Nothing is sent without
your press. If the monitor has manufacturer-specific codes you can identify,
`dispctrl devices probe` and `dispctrl devices map` name them. A laptop screen
has no codes, but you can still say what it is, which nothing on the machine
reports: `dispctrl devices panel --monitor 1 --technology OLED`. See
[docs/DEVICE-LIBRARY.md](../docs/DEVICE-LIBRARY.md).

## Reporting a bug

`dispctrl report --what "what happened"` (or **Help > Report a problem** in the
app) builds the report for you, scrubbed, and opens it as a prefilled issue.
Otherwise use the **Bug report** issue form. The most useful attachments are:

- `dispctrl diagnostics --json`;
- the last lines of `%LOCALAPPDATA%\DispCtrl\engine.log`;
- `%LOCALAPPDATA%\DispCtrl\app-crash.log` if the window closed on its own;
- your monitors: model, connection (HDMI, DisplayPort, USB-C, dock) and scaling.

Read these before you attach them. Diagnostics and the logs name your monitors
by identity token, which includes the serial number, and contain paths under
your user folder. The logs may also name the apps you use. Replace anything you
would rather not publish. `dispctrl report` and `dispctrl devices contribute` are
the outputs that are scrubbed for you.

## Changing code

1. Read [CLAUDE.md](../.claude/CLAUDE.md). It is the handover document: the architecture,
   the build commands, and a long list of traps that have already cost a crash
   or a corrupted setting. Most bugs worth fixing here were caused by one of them.
2. Set up and build. [docs/DEVELOPING.md](../docs/DEVELOPING.md) has the details:
   ```powershell
   .\build.cmd setup -Install
   .\build.cmd build
   .\build.cmd test -Hardware
   ```
   `test` alone needs no display hardware. `-Hardware` adds `presetcheck`,
   which asserts the redaction rules against the monitors actually attached.
   On Linux, `./build.sh build && ./build.sh test` covers everything but the
   window.
3. **Leave the desk as you found it.** A test that changes brightness, night light
   or a monitor setting must put it back, including when it fails.
4. Build through `build.cmd build`, which stops the engine gracefully and
   restarts it. If you build by hand, stop it with `DispCtrl.Engine.exe stop`
   first, never by killing it: a killed engine leaves a hidden taskbar off-screen.
5. Anything new should reach the command line first and the app second, through
   `DispCtrl.Control`, so it can be scripted and checked.

### Style

- Match the code around you. Comments explain **why**, usually a measured fact
  or a bug already paid for, and never narrate what the code does.
- XML docs on public members, with `<remarks>` for the reasoning.
- User-facing text is full sentences in British spelling ("colour", "centre").
  Every claim must be true of the hardware: if something cannot be done on a
  given monitor, say so rather than offering a control that does nothing.
- Warnings are errors. Keep it that way.

### What the checks do

Every pull request is labelled by area and size, built and tested on Windows,
and checked for new dependencies with known vulnerabilities. One more check,
**policy**, stays red when a pull request from outside the project touches the
build, packaging, tools, scripts or CI, or adds a binary: those are what
everyone who builds DispCtrl runs, so a maintainer reads them first and adds
`maintainer-approved`. It is not a judgement on the change, and it explains
itself in a comment. A pull request quiet for six weeks is marked stale and
closes two weeks later; reopen it any time.

### Commits and pull requests

Commit messages are prose: what changed, why, and how you verified it, including
on which hardware. The pull request template asks the same questions. Keep one
concern per pull request, and add an entry under **Unreleased** in
[CHANGELOG.md](../docs/CHANGELOG.md) for anything a user would notice.

## Licence

By contributing you agree that your contribution is licensed under the
[MIT licence](../LICENSE).
