# Implementation checklist

Checked items were verified on this desk (Internal SDC-4154 at 200%, DELL
U2424H at 100%); the note says how. Unchecked items have code but lack the
verification named beside them.

- [x] Quick panel border, consistent folding, nested controls, menu cleanup.
  Panel captured on the desk: no light frame, unison/tiles/night light fold,
  monitor controls fold inside each display, `…` menu has no duplicates.
- [x] Compositor animation, user switch and Windows reduced-motion support.
  Slide plus fade on the compositor thread; `quickPanel.animate` and Windows'
  *Animation effects* both turn it off. Summoned from the CLI with no crash log.
- [x] Responsive individual display preview preserving aspect ratio.
  UI Automation at 2200, 1100 and 760 px windows: beside the table at the first
  two, below it at 760, the same size at all three (no squash).
- [x] Unison calibration endpoints with Unison enabled or disabled.
  Live: Recalibrate moved the slider to 0 and each display to its floor. Cancel
  used to leave them there; it now restores the level and every display.
- [x] Windows brightness keys/slider: coalescing, calibration exclusion.
  Live: `WmiSetBrightness` 41 -> 71 moved unison to 71 and the Dell to its
  calibrated 71; everything restored. Reconnecting the WMI watcher after the
  WMI service restarts is not tested.
- [x] Secondary taskbar glass, clipping, reclaimed work area and mixed DPI.
  Probe on the 200% panel: 96 px bar, fully drawn, glass on both backgrounds;
  a repaint when a reveal settles covers the half-drawn bar.
- [x] Taskbar opacity 0–100 across clients (panel, page, engine, API).
- [x] Reported connection topology; honest MST/Thunderbolt detection limits.
  `tunnelled` from the driver's USB-tunnel output technology, `sharingConnector`
  for MST; only positive answers are claimed. Neither exists on this desk, so
  only the negative path has run on hardware.
- [x] Shared API and CLI parity: settings, hardware, Windows, startup, toolkit.
  Sweep of every command read-only and dry-run: all answer, settings file
  untouched. Refusals before the broker answer `--json` in the envelope.
- [x] Ordered apply, topology rediscovery, dry run, partial errors and hot-plug.
  Topology runs first and the rest is planned after rediscovery; dry runs defer
  monitors a topology could switch on (controlcheck). Unison hot-plug sync is
  built but has not seen a physical replug.
- [x] Cached inventory, invalidation, latest-value writes and event delivery.
  `watch` polls metadata; it is not a broker push stream.
- [x] External scripts, examples, public protocol, debugging and coverage matrix.
  `examples/` documents are validated by the CLI; see `docs/CLI.md`.
- [x] Quick panel simple mode; words instead of symbols on rows; header switches.
  Captured on the desk in both modes; simple mode switched from the CLI.
- [x] Windows brightness within calibrated ranges.
  Live, built-in 25..78: Windows 10 held at 25 with unison 0 and the Dell 0;
  60 gave unison 66 on both; 95 held at 78 with unison 100. Restored.
- [x] Opening animation, preload and startup task.
  Frame captures of both directions; first summons from preload 42 ms; engine
  started from the `DispCtrl.Engine` task at normal priority.
- [x] Generic monitor controls from the CLI: keys and values from the monitor.
- [x] Settings saves survive a reader holding the file (controlcheck).
- [x] Quick panel title bar without the redundant menu; position lock.
- [x] Tile flyouts with sliders and switches (night light, focus, OLED care,
  keep awake, unison, colours); redundant glass, auto-hide and transparency
  tiles removed. Night light flyout opened through UIA and captured.
- [x] Windows brightness link: throttled instead of debounced; the built-in
  panel is pulled back into range only once the slider is still.
- [x] Device library: local history, definitions (model, brand, every monitor,
  links), probe, map, share, intake workflow and validation. End to end on the
  Dell in an isolated data folder; six controlcheck assertions; devicecheck.
  The intake workflow has not run on GitHub.
- [x] Every page's controls reachable from the CLI (docs/CLI-COVERAGE.md):
  hotkeys, display reset, wallpaper fit, Windows pages, unison limits added.
- [x] Secondary taskbar drawn in full at 200%: a stale 48 px window region
  found on the 96 px bar and cleared; the engine now heals it.
- [x] Hotkeys: 21 actions, seven defaults offered once, restore from page or CLI.
  Ctrl+Alt+Page Up/Down pressed for real: unison 51 to 56 to 51 on both displays.
- [x] Quick panel sections with every option (focus, OLED, night light), detail
  sections folded by default, indented sub-settings, stretched display switches,
  simple mode without density and fitted height. Captured on the desk.
- [x] Displays page, narrow: preview first and on the table's edge; copy button
  on the caption's line. Captured with PrintWindow at 860 px.
- [ ] Portable releases, beta/stable CI, symbols and checksums.
  The CI steps ran locally end to end - build and checks, ReadyToRun publish,
  MSIX pack - and the workflows pass actionlint. Not yet run on GitHub: needs a
  push and a tag.
- [ ] MSIX packaging, identity, installation/update/startup and certification.
  `build/Package.ps1` packs and MakeAppx validates. Not installed, signed or
  certified: needs the Partner Center identity and a signing certificate.
- [ ] Per-user installer (Inno Setup), graceful engine stop on upgrade and
  uninstall, sign-in task, optional PATH. `build/Installer.ps1` compiled a
  73 MB setup from a real desktop bundle (Inno 7.1, portable `ISCC.exe`). The
  embedded stop script ran against an empty root, and its task-ownership check
  ran read-only against this desk's task: it matches the Release build and
  would not remove it for an install elsewhere. Not installed here, because
  doing so would re-point this desk's sign-in task. Needs a clean account or VM.
- [ ] Release pipeline: CHANGELOG-based draft notes, signing of binaries, setup
  and MSIX (MSIX only when the certificate subject equals the Publisher),
  winget update and gated Store submission on publish. `ReleaseNotes.ps1` ran
  locally; every workflow passes actionlint. Nothing has run on GitHub, and the
  winget and Store jobs need their accounts first (docs/RELEASING.md).
- [x] Device library laid out for thousands of models: devices/BRAND/PRODUCT/
  {record.md,definition.json}, BRAND/brand.json, common.json (DeviceLayout).
  The app reads per target and caches; only definitions ship. devicecheck
  migrate moved the repository's files; validate refuses stray files, bad
  folder names and records carrying a user path or instance id (each planted
  and caught); intake writes a new model into its own folder; index writes
  index.json and CATALOG.md, regenerated after merge by devices.yml. A synthetic
  3,000-model, 150-brand library validated in about 8 s warm; the index is
  276 KB. All checks pass; the CLI resolves DEL-A234 from its folder.
- [x] Build output: Directory.Build.targets removes the retired flat layout from
  every bin (verified in the engine, app and CLI outputs); build -Rebuild; build
  and release end by listing every output with its path. Release goes to one
  folder, artifacts/<channel>-<version>, cleared first (it removed 1.9 GB of
  stale bundles here), MSIX staged in temp and deleted. dev.ps1 publish now
  stops and restarts the engine, which the tests' build otherwise collides with.
- [x] Developer entry points: build.cmd / build/dev.ps1 (doctor, setup, build,
  test, run, publish, installer, package, release, clean, options, and a menu)
  and build.sh for Linux/WSL. On this desk: doctor and setup found every tool
  and fetched a portable Inno Setup; build stopped the engine, built with the
  glass helper at the same revision, and restarted it through its task (the
  first run did not restart it - Process.Path is empty after exit - fixed);
  test passed all four checks. Script parses under Windows PowerShell 5.1.
  WSL Ubuntu, from a clean copy: setup installed .NET into .tools, build
  compiled everything but the app with the glass helper skipped, and
  presetverify and devicecheck passed. setup -Install (winget) and build.sh
  --native are not exercised.
- [x] Licence (MIT), README, CONTRIBUTING, SECURITY, code of conduct, changelog,
  issue forms, PR template, dependabot. README claims checked against the code:
  default hotkeys, the night light schedule, the OLED and focus options, and
  presets marked as off in release builds.

Presets stay disabled; current-settings export remains available. Preserve earlier
OLED, Focus, Awake, contribution, startup, help and layout changes. Static-content
detection and hardware refresh/power features need working adapters before being
represented as supported. The licence is MIT; publication is a release input.
