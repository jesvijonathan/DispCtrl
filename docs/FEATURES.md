# Features

Everything DispCtrl does, page by page, in the order the app shows it. Ranges
and choices are the ones the app offers; defaults are a new install's. Every
stored value, including the few with no switch, is in the
[settings reference](SETTINGS.md); the command behind each control is in
[command coverage](CLI-COVERAGE.md).

- [The window](#the-window)
- [Displays](#displays)
- [Brightness](#brightness)
- [Screen care](#screen-care)
- [Windows](#windows)
- [Taskbar](#taskbar)
- [Presets (beta)](#presets-beta)
- [Quick panel and tray icon](#quick-panel-and-tray-icon)
- [Hotkeys, features and triggers](#hotkeys-features-and-triggers)
- [Devices](#devices)
- [Miscellaneous](#miscellaneous)
- [Settings](#settings)
- [Help and About](#help-and-about)
- [Command line](#command-line)
- [What happens without you](#what-happens-without-you)

## The window

- A page per area, in the navigation pane on the left.
- **The preset in use** sits in the title bar (presets are beta). Its menu
  chooses which preset the desk is compared with - choosing changes nothing -
  then **Apply** puts the displays back to it, **Save** updates it from them,
  and the last entry creates a new one.
- When the displays have moved away from it, a one-line banner says how many
  things changed (click the count to see them), with **Discard** (put the
  displays back), **Save** (update the preset) and a close button that hides
  it until the displays change again. Only differences DispCtrl can put right
  count, so an unplugged display or a wallpaper file that no longer exists
  never keeps it up.
- Only one window runs; opening DispCtrl again brings it forward.
- Now and then - from the fifth time the window opens, never on a first run -
  a one-line banner asks for a star on GitHub or a donation. Either ends it;
  closing it asks once more months later, then never.

## Displays

The desk as a whole, then one card per display.

- **Arrange displays**
  - Every display drawn at its real physical size, or **By resolution** as
    Windows draws it.
  - Drag a display: it moves only between the positions Windows accepts,
    shown as dashed outlines and ranked by real distance.
  - **Identify** shows each display's number on it; **Detect** also finds a
    monitor that is connected but asleep. **Reset**, **Apply**.
- **Multiple displays**: extend, duplicate, built-in only, external only.
- **Monitor library**: a link to the Devices page.
- **Connect to a wireless display**: Windows' Miracast pairing.
- **Each display** (one card each; with only one display attached, its card starts open)
  - Wallpaper (**Change**) and how it fits: Fill, Fit, Stretch, Tile, Centre,
    Span.
  - Brightness 0-100, or software brightness where the display has no
    hardware control. **In unison**: off leaves this display out of unison.
  - Night light: this display's own warmth, when night light runs per display.
  - HDR, scale, resolution, refresh rate, orientation.
  - **Make this my main display**.
  - **Hide the taskbar** (secondary displays; the main one uses Windows'
    auto-hide, on the Taskbar page).
  - **The monitor's own controls**: **Show controls**, then a slider or a
    choice for each control the monitor offers over DDC/CI; **Learn a
    setting** names an unnamed one by watching the monitor's menu.
  - **Advanced options**
    - Software brightness, adaptive brightness, rotate with the device.
    - **OLED panel**, and whether focus mode may dim this display.
    - **OLED burn-in protection** for this panel; **Run screen rest** for
      1-30 minutes; **Wake when the pointer returns**.
    - **Monitor sleep** after 1-240 idle minutes, through the monitor's own
      power control (external monitors).
    - Variable refresh rate, colour profile (**Manage**), **Reclaim the work
      area** while its taskbar is hidden.
    - **Look for its controls**: **Probe (read-only)** asks a monitor that
      does not say what it supports about every code DispCtrl knows;
      **Forget** drops what was found.
    - **Also reported**: read-only answers such as firmware level and hours in
      use.
    - A notice and **Talk to it again** when the DDC/CI guard has stopped
      talking to this monitor.
    - **Reset this display**: DispCtrl's own settings for it, not Windows'.

## Brightness

Brightness and colour for the whole desk. Each display's own level is on its
card under Displays.

- **Unison brightness**: one slider for every display.
  - **Replace Windows brightness**: Quick Settings' slider and the brightness
    keys move every display, the built-in panel kept inside its own range.
  - **Use captured limits**: run each display between the dimmest and
    brightest it should go. **Calibrate again** walks each display to the end
    being captured; **Cancel** puts them back.
  - **Each display now**: every display's current level.
- **Follow the room's light** (where Windows has a light sensor)
  - Light sensor, the level **In the dark** and **In bright light**.
  - **Calibrate the sensor**: **This is dark**, **This is bright**, with the
    reading live.
  - **Learn from my adjustments**: moving unison while following is
    remembered for that light; **Forget**.
- **Night light**: warmer colours on every display.
  - **Use Windows' night light**: one switch with Windows' own.
  - Strength 5-100; **Schedule** with hours (past midnight is fine).
  - **Advanced options**: **Dark mode on the schedule** (dark when it starts,
    light when it ends, once each); **Gamma range**, which lifts Windows'
    limit so warmth reaches 1900 K and dimming near black (asks for
    administrator once).
  - **Right now**: what is applied.
- **Dark mode**: Windows' app and system theme together.

## Screen care

- **Focus mode**: the active window stays clear and the rest dims.
  - Background dimming 0-100; which windows stay clear (the focused one, the
    one under the pointer, or both); **Dim other monitors**.
  - **Advanced options**: delay after switching windows (0-10000 ms), fade
    (0-2000 ms), transition (none, fade the brightness, slide the shape), one
    clear window per display, OLED displays only, match the dimming to
    brightness, follow new and activated windows, keep the taskbar area
    clear, pause for fullscreen windows, excluded apps, **Reset focus mode**.
- **OLED idle protection**: rests OLED panels nobody is using.
  - **Which displays**: every monitor this PC has seen, connected ones first,
    each marked OLED and protected or not, with its own timing if you like
    (rest after, dim to, turn off after).
  - Dim after inactivity (1-120 minutes) and idle dimming 0-100, previewed for
    two seconds.
  - **Third stage: turn the display off** after further minutes.
  - Pause during fullscreen content; pause while a video plays on that
    display.
  - **Advanced options**: each display rests on its own; keep these apps'
    displays awake; second-stage dimming and when; the third stage's backlight
    and whether the computer stays active; fade; **Reset**.
- **Keep awake**: stop the computer sleeping without changing the power plan.
  - **Turn off displays**: **Turn off now** blacks the displays out while the
    computer runs; any input brings them back.
  - **Stay active**: the screen stays on and chat apps never show you as Away.
  - Mode: the power plan, indefinitely, for a time interval (up to 168 hours),
    or until a date and time.
  - **Advanced options**: how dark the displays go (50-100), the backlight
    down too, which displays go off (all, all but the main one, all but the
    pointer's, all but the active window's, only the main one), the delay,
    wake only by the pointer, hide the pointer, lock when they wake, keep
    displays on.

## Windows

- **Pin windows on top**: a window stays above every other, marked with a
  border.
  - **Pinned now**: each pinned window with **Unpin**, **Look again**,
    **Unpin all**. Ctrl+Alt+P pins the window in front.
  - **Advanced options**: border, colour, width (1-16), opacity (20-100%);
    keep pinned windows clear of focus mode and, if you like, of OLED idle
    dimming; never pin a fullscreen window; step aside for fullscreen; never
    pin these apps.
- **Move windows between displays**
  - **Gather every window** onto a display, maximized, fullscreen and
    minimized ones included. Ctrl+Alt+G gathers onto the display in use.
  - **Put windows back when a display returns**: each goes back where it was,
    unless somebody has moved it since.
  - **Open new windows on the display in use**.
  - **Advanced options**: Windows' own window memory and minimize-on-
    disconnect switches, which display is in use (the pointer's or the active
    window's), keep a window's size to the eye across scales, never move these
    apps.

## Taskbar

How the auto-hiding taskbars behave. Which displays hide theirs is on each
display's card.

- **Appearance**
  - **Taskbar surface**: Windows transparency; **Taskbar glass** with its look
    (blur, clear, opaque, acrylic), colour (accent or your own), border, blur
    radius and tint; **Restart Windows Explorer**; taskbar opacity.
  - **Windows taskbar features**: smaller buttons, alignment, combining
    buttons, Task View, Widgets, badges, flashing, the show-desktop corner.
- **Reveal behaviour**: hide delay (0-1500 ms), animate or instant, slide
  duration, edge sensitivity, **Restore defaults**.
- **Advanced**: how often the engine looks at the cursor (arm distance and the
  idle, far, armed and shown intervals).
- **Refresh the taskbar**: restarts Windows Explorer, so DispCtrl applies
  hiding, glass and opacity again. Asks first: open File Explorer windows
  close.
- The glass looks: Blur softens what is behind; Clear shows it sharp; Opaque
  is a solid colour; Acrylic is the same blur with the colours behind about
  40% more vivid.
- **Primary taskbar**: **Use Windows' own auto-hide**, the only way to hide the
  main display's taskbar.

## Presets (beta)

- **New preset**: everything DispCtrl can set, for the whole desk or one
  display. **Import**, and the presets folder.
- **Saved presets**: every preset in a list - what it holds, how many of its
  displays are attached, when it was saved - with **Apply** on each row. The
  one in use is marked, with how many settings have changed since.
- Each row's **…** menu: **Update from the displays**; **Apply when this desk
  is connected** (a desk profile: applies by itself when exactly its displays
  connect, windows put back where they were); **Rename**; **Map displays**;
  **View or edit JSON**; **Export**; **Delete**. These act on that preset
  without making it the one in use.
- What has changed since the preset in use was saved, named as such, with
  **Discard** and **Save to the preset**.
- **What it restores** (in **New preset** and each row's menu): layout,
  brightness, night light, wallpaper, monitor controls, taskbar, windows.
  Anything unticked is left as it is when the preset applies and is never
  counted as a change - a layout preset that leaves brightness to you, say.
  The row says what it leaves alone.
- **Say when the displays change from the preset in use**: off, the banner and
  the title bar's dot go; the page still lists the changes. Brightness moved
  by the room's light is never counted as a change.
- A name already taken is refused when saving or renaming; an import with a
  taken name is numbered, never overwrites.
- **App rules**: apply a preset while an app stays in front, after an
  activation delay; then return to another preset, restore the previous setup,
  or keep it. The first matching enabled rule wins.
- `dispctrl preset launch NAME PROGRAM --wait-for game.exe` applies a preset,
  runs the program and puts the desk back when it exits.
- See [Presets and desk profiles](PRESETS.md).

## Quick panel and tray icon

The panel opens from DispCtrl's icon in the notification area, rising from the
taskbar, and closes when you click elsewhere unless it is set to stay open.

- **Title bar**: the DispCtrl title drags it; buttons for simple mode (the
  dot), density, stay open and customise.
- **Simple mode**: one slider for every display with the unison switch, then
  one per display.
- **Sections**, each foldable, in your order (Quick toggles starts open, and
  a section switched on from the Quick panel page arrives open): Unison brightness, Brightness,
  Quick toggles, Displays, Night light, Taskbar, Focus, OLED care, Presets (a
  list of presets with **Apply**, and whether the displays still match),
  Display mode, Windows.
- **Quick toggles** (an arrow, or a right-click, opens a tile's options):
  Night light, Dark mode, Focus, Keep awake, Stay active, Taskbar, OLED care,
  Project, Displays off, Detect, Unison, Identify, Cast, Room light, Way back,
  Rest OLED, Engine, Pin on top, Gather, Put back, New here, and your own
  tiles.
- **Rows on each display**: Brightness, Switches, Resolution, Scale, Refresh
  rate, Software dimming, Orientation, Monitor controls, Warmth, Input source.
- **Switches on each display**: Hide taskbar, HDR, Make main, Focus dimming,
  OLED care, Rest now, Monitor sleep, Variable refresh, Adaptive brightness,
  Auto-rotate, Identify, In unison, Gather here - each only where the display
  supports it.
- **Quick panel page**
  - Simple mode; show the icon (and **Open it now**); keep it on the taskbar.
  - **What it shows**: sections, quick toggles, rows and switches on each
    display - drag to reorder, show or hide; the all-display-settings button.
  - **Your tiles**: run a `dispctrl` command, or open a program, script,
    document or address, with a name, a symbol and hover text.
  - **Which displays** the panel leaves out.
  - **Look and behaviour**: the icon (brightness, display or DispCtrl's logo),
    its colour, bolder while Keep awake or Stay active is on; density, width,
    toggles per row, height; animation, stay open, lock its position; the
    mouse wheel over the icon (off, the main display, every display) and on
    the panel's sliders, and its step.
  - **Reset the quick panel**.
- **Tray icon**: drawn like Windows' own, white or black to match the taskbar.
  The wheel over it moves brightness. **Right-click**: Open DispCtrl, Simple
  view, Keep on the taskbar, Hide this icon, Stop the engine, Exit DispCtrl.

## Hotkeys, features and triggers

Four tabs. Shortcuts are registered by the engine, so they work with the
window closed.

- **Shortcuts**: the keys, what it does, and its step, display, preset,
  control, feature, arrangement or arguments as the action needs.
  - Actions: unison up, down and on or off; brightness up and down for one
    display; software dimming; night light on or off, warmer, cooler; room
    light; focus; OLED care and rest now; keep awake; stay active; turn the
    displays off; put every display back; dark mode; taskbar and its glass;
    contrast, volume and mute; next input; any monitor control (set, next or
    previous value, up, down); HDR, variable refresh, make main; show the
    display numbers; the quick panel; the four Win+P arrangements; pin, unpin
    all, gather, put back, new windows here; move the window to the next,
    previous or a given display, or span it; apply a preset; run a feature;
    run a `dispctrl` command; open a program.
  - On by default: Ctrl+Alt+Page Up / Page Down (unison by 5), Ctrl+Alt+D
    (quick panel), N (night light), L (turn off displays), Backspace (put every
    display back), P (pin), G (gather).
  - Set up but off: Ctrl+Alt+U, F, K, I, M, T, O; Ctrl+Alt+Shift+Page Up /
    Page Down (contrast); Ctrl+Alt+1-4 (arrangements); Ctrl+Alt+] and [ (move
    the window), S (span it).
  - Never the arrow keys, which some graphics drivers take for rotation. Each
    shortcut shows whether it is registered or taken, and the page asks before
    the way-back shortcut is switched off. **Restore the defaults**.
- **Features**: a named list of steps, run from a shortcut, a trigger, a tile
  or `dispctrl features run`. Each step is a row: set a monitor control, run a
  DispCtrl command, open a program, file or link, run a script and wait, or
  wait - chosen from a list, with **Add a step**, move up and down, and
  remove. **Edit as text** writes the same steps as lines. **Run**, **Test**
  (checks every step, changes nothing), **Save** (lit once something
  changed), **Delete**.
- **Triggers**: when a display connects or disconnects, an app comes to the
  front or leaves it, you go idle or come back, the session locks or unlocks,
  the power source changes, the computer resumes, or at a time of day - run a
  feature.
- **Windows' own**: the display shortcuts Windows keeps (Win+P,
  Win+Shift+arrow, Win+Alt+B, Win+A, Win+K, Win+Ctrl+C,
  Win+Ctrl+Shift+B), listed so you need not bind over them.

## Devices

What each monitor's controls are, and sharing that with the project.

- Every monitor this PC has seen, read by itself the first time it is plugged
  in. **Scan**, **Contribute all**, **How mapping works**.
- For each monitor: every code it answers - standard, named by the library, or
  not yet named - with its kind and range; watch the unnamed codes live while
  you use the monitor's menu.
- **Name a code**: what it does, kind (range, choice, action, information),
  maximum, values, notes, and whom it applies to (this model, the brand, every
  monitor). Only a code somebody has written and watched is ever written.
- Say what a panel is (OLED, LCD), built-in panels too.
- **Contribute**: one prefilled GitHub issue with the record and your names,
  serials, device paths, file paths and account name removed; copied to the
  clipboard when too long for a link.
- See [Device library](DEVICE-LIBRARY.md) and
  [Custom controls and features](CUSTOM-CONTROLS.md).

## Settings

DispCtrl itself.

- **Engine**: start at sign-in, keep the quick panel ready, open DispCtrl at
  sign-in, Start menu and desktop shortcuts; the engine's state, settings file,
  log and executable.
- **Updates and maintenance**: **Check for updates**, and automatically once a
  day (on unless switched off; never for a Store install, which the Store
  updates); **Repair**; clear logs
  and cached data; **Undo the way back**; **Reset everything**.
- **Advanced**: write a log; guard against the DDC/CI crash, and monitors it
  has blocked (**Allow again**); allow raw DDC/CI writes.

## Miscellaneous

Extras that are about Windows rather than the displays.

- **Sign-in and lock screen**: the switches people change with regedit, often
  on a company laptop - sign in without Ctrl+Alt+Del, lock screen picture,
  skip the lock screen, no blur behind the sign-in box, lock after inactivity,
  lock when your phone leaves, no tips on the lock screen. Each shows what
  Windows has now; machine-wide ones ask for administrator permission, and
  **Put back** restores what was there before.
- **Tools**: refresh the taskbar (restart Windows Explorer), put every display
  back, Windows' display settings, colour management.

## Help and About

- **Help**: source code, **Report a problem** (a scrubbed report you review
  before a prefilled issue opens), request a feature, contribute code; fixes
  for a taskbar stuck off-screen, a main taskbar that will not hide, a monitor
  that forgot its settings.
- **About**: what DispCtrl is for, its author, ways to support it, and the
  version, runtime, Windows build and components.

## Command line

`dispctrl.exe` reaches every feature above. `dispctrl help` lists the topics -
displays, controls, brightness, nightlight, care, windows, taskbar, presets,
automation, devices, system, scripting - and `dispctrl help TOPIC` or
`dispctrl COMMAND --help` shows one. See [Command line](CLI.md).

## What happens without you

- The engine puts every hidden taskbar back when it stops, and even when it
  crashes.
- A monitor plugged in is found within moments; one plugged back in is brought
  back in step with unison once it wakes, and its windows go back to it.
- A night light or dimming left behind by a crash is found and cleared at
  start.
- Taskbar glass is re-applied when Explorer restarts.
- The engine sleeps between checks: about 16 ms of processor time a minute
  when idle. OLED care stops watching while the PC is locked. The hidden quick
  panel gives its memory back ten seconds after closing.
- Settings are saved atomically and merged between the app, the engine and the
  command line. A file written by a newer DispCtrl still loads, and what this
  version cannot read is kept for the newer one.
- A monitor whose capabilities read took Windows down is not read again until
  you allow it.
- The installer stops the engine cleanly before updating and keeps your
  settings; uninstalling puts every taskbar back.
