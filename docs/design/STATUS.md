# State of play

What works and has been verified on hardware, and what is outstanding, in the
order last discussed. Kept short; the reasoning lives in [ROADMAP.md](ROADMAP.md)
and [FEATURES.md](FEATURES.md), the verification in
[IMPLEMENTATION-CHECKLIST.md](IMPLEMENTATION-CHECKLIST.md).

Working and verified on hardware: per-monitor taskbar hiding, per-monitor
wallpaper, unison brightness (multiplier and calibrated range), night light
(unison, per-monitor, calibrated, scheduled), software dimming, arrangement
drag/apply, presets with per-app rules and desk profiles (on and labelled
Beta since 2026-10-02; `-p:EnableBetaPresets=false` builds without them),
monitor capability discovery and control,
display report, identify overlays, hotplug re-discovery, device contribution
(anonymised, consent-gated), the quick panel and tray icon, Windows' brightness
slider and keys driving unison, the control API and `dispctrl` JSON surface,
OLED care per display, windows pinned on top, gathering and putting windows
back (unplugged live), settings that survive a newer or older build, and the
opt-in update check.
`docs/design/IMPLEMENTATION-CHECKLIST.md` records how each recent item was verified and
what is still unverified. Since then CI runs on GitHub (Build and verify
passes; the Device library job failed until devicecheck stopped building a
Windows apphost on Linux) and the MSIX has been accepted by Partner Center and
installed from the Store; the installer has still only been compiled, not
installed (see "Release and installer").

Outstanding, roughly in the order last discussed:

See `docs/design/FEATURES.md` for the full candidate list with effort and risk,
including "Compared with PowerToys" (Power Display, FancyZones, Always On Top):
its first item, guarding the DDC/CI capabilities read against the documented
Windows kernel crash, comes before anything below. The short version, in
recommended order:

1. ~~Brightness fallback, high-level to VCP `0x10`~~ — **done**.
2. ~~Lift the gamma clamp~~ — **done**, `GammaRange`.
3. ~~Full command line~~ — **done**, `dispctrl.exe`.
4. ~~Hotkeys~~ — **done**, engine-registered, with a Hotkeys page.
5. **Combined brightness** — one slider spanning hardware above a switching
   point and software dimming below it.
6. ~~Presets capturing everything~~ - **done**, schema v2, scope removed.
7. **Persistent known-monitor cache** — survive restarts, keyed on model+serial.
   Copy ddcutil's `<mfg>-<model>-<product>` convention.
8. **More fields in the display report.**
9. ~~Opt-in contribution~~ - **done**, `dispctrl contribute` and the panel card.
   Records land in `devices/BRAND/PRODUCT/record.md`; `DEL/A234` and
   `SDC/4154` are seeded from this machine.
10. ~~OLED burn-in protection~~ - **done**: idle rest in two stages, per
   display or for the whole desk, with an exception list, manual rest, and
   focus mode beside it.
11. ~~Remember window positions across replug~~ - **done**, alongside Windows'
   own window memory (see "Windows on top, gathering, putting back").
12. MSIX packaging; Native AOT (blocked, see above); widgets; taskbar
   translucency.
