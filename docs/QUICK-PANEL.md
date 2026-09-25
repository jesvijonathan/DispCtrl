# The quick panel

The panel that opens from DispCtrl's icon in the notification area. This page
is for anyone adding to it: where each piece lives, and what adding a tile,
a row or a switch actually takes.

## How it fits together

```
DispCtrl.Engine   Shell/TrayIconService     the icon, its menu, the click
      │  sets Local\DispCtrl.QuickPanel.Show (a named event, no data)
      ▼
DispCtrl.App      App.SummonPanel           toggles the panel open or closed
                  Views/QuickPanelWindow    the flyout: placement, fitting, folding
                  Views/QuickPanelContent*  the rows, built from the settings
                  Views/QuickPanelPage      the customisation page
                  Views/QuickPanelListEditor  the reorderable list it uses
DispCtrl.Core     Settings/QuickPanelSettings  what the panel shows - saved in
                                               settings.json → global.quickPanel
```

The engine owns the icon because it is the resident process. The app owns the
panel because it is the process with WinUI. They share nothing but the named
event and `settings.json`.

## What the settings hold

`global.quickPanel` in `settings.json`:

| Field | What it is |
|---|---|
| `sections` | The blocks, top to bottom: `unison`, `tiles`, `nightLight`, `displays`, `taskbar`, `focus`, `oledCare`, `presets`, `displayMode`, `windows` |
| `tiles` | The quick toggles, in order |
| `displayRows` | The rows under each display |
| `displayTiles` | The small switches in each display's strip |
| `customTiles` | Tiles people made - see below |
| `collapsed` | Sections and displays folded away (`taskbar`, `display:<token>`) |
| `hiddenDisplays` | Identity tokens of displays left out |
| `density`, `width`, `tileColumns`, `icon`, `stayOpen`, `showFooter`, `enabled` | Look and behaviour |
| `trayWheel`, `wheelStep`, `wheelOnSliders` | The mouse wheel over the icon (`off`, `main`, `all`), how far a notch goes, and whether it moves the panel's sliders |

Each list holds `{ "id": "...", "visible": true }` entries. The order of the
list is the order on screen. Unknown ids are dropped when read, and ids added in
a later version are appended **hidden**, so a panel someone pared down never
grows rows after an update. **Never rename an id** - a renamed id is a choice
somebody made, silently reset.

## Simple mode

`QuickPanelSettings.Simple` swaps the whole layout for brightness alone: an "All
displays" slider with the unison switch beside its name, then one slider per
shown display. It is a mode rather than a preset of the lists, so the sections,
tiles and rows are untouched and switching it off brings the full panel back
exactly. The customisation page greys only what simple mode ignores.

## Sections

Focus mode, OLED protection and night light put their switch in the header and
every option beneath it - the same rows their tiles' flyouts show, built once in
`QuickPanelContent.Registry.cs`. OLED protection, focus, display mode, taskbar,
night light and Windows start folded until opened; the rows under an open section sit a
little in from its header. A display's switches span the width in equal columns.

The Windows section (`QuickPanelContent.Windows.cs`) gathers every window onto
a display, holds the two placement switches, and lists what is pinned. It and
the pin, gather, put-back and new-window tiles are hidden until switched on
from the Quick panel page. The pin tile opens the list of open windows to
choose from: the panel is in front whenever it is clicked, so "the active
window" means nothing there - Ctrl+Alt+P is how the window in front is pinned.
A pin is the window's own state, which the hotkey or the command line change
without the panel hearing, so the flyout's list is read as it opens and the
section's follows `PinnedWindows`, which every summons re-reads.

## Rows: words, then the control

Section headers are words alone, and so are rows that choose something -
resolution, refresh rate, scale, orientation, input, a monitor's own controls:
the name in a fixed column on the left, the control on the right, every row
starting at the header's edge. Symbols are kept where they are the convention:
the brightness sun on a slider, the tiles. A switch that belongs to a whole
section sits in its header, before the chevron.

## Opening and closing

The panel opens on the monitor with Windows' notification area - the main
taskbar's - in the corner nearest it (`QuickPanelHost.TrayAnchor`), wherever
the pointer is: Windows 11 has a notification area on the main taskbar only,
and a hotkey summons from anywhere. The window is cloaked until XAML has drawn two frames and its height is fitted,
then slides its whole height out from behind the taskbar (260 ms in, 180 ms out,
Windows' flyout curves), placed just under the taskbar in the topmost band so
the taskbar clips it. A window region cut at the taskbar's edge does the
clipping for a translucent taskbar, and the acrylic stays live whether or not
the panel is active (`FlyoutAcrylicBackdrop`). Rows stay built while it is hidden; with "Keep the quick
panel ready" the engine starts it hidden at sign-in, so even the first click
shows it in about 40 ms.

## Adding a quick toggle

Two lines.

1. **Describe it** in `QuickPanelCatalog.Tiles` (`DispCtrl.Core/Settings/QuickPanelSettings.cs`):

   ```csharp
   new("hdrAll", "HDR", "\uE7A1", "HDR on every display that supports it.", false),
   ```

   Id, label, a [Segoe Fluent Icons](https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font)
   code point, the hover text, and whether a fresh panel shows it. The
   customisation page picks it up from here on its own.

2. **Say what it does** in `DeskTile` (`DispCtrl.App/Views/QuickPanelContent.Registry.cs`):

   ```csharp
   "hdrAll" => Tile(e, () => _vm.HdrEverywhere, v => _vm.HdrEverywhere = v,
       nameof(MainViewModel.HdrEverywhere)),
   ```

   `Tile` for a switch, `ActionTile` for something done rather than held.
   Pass a `MenuFlyout` as the last argument to `Tile` and it becomes a split
   tile, with the options behind its arrow and on a right-click. Return `null`
   when the tile cannot work on this machine, and it is simply left out.

A row under each display is the same pair: `QuickPanelCatalog.DisplayRows` and
`DisplayRow`. A switch in a display's strip: `QuickPanelCatalog.DisplayTiles`
and `StripItem`.

`presetcheck` asserts that every catalogue entry has a symbol, a name and hover
text, and that the default lists name each item exactly once. Run it:

```bash
dotnet run --project tools/presetcheck/presetcheck.csproj -c Release
```

## Rules the building blocks keep for you

Use the blocks in `QuickPanelContent.Blocks.cs` rather than new controls, and
these come free:

- **A control's value is set before its handler is attached.** A two-way XAML
  binding writes its default back to the source before the real value arrives,
  which is indistinguishable from somebody moving it. This project has lost a
  brightness, a night light strength and a machine-wide power setting to that.
- **Toggles listen to `Click`**, which only a person raises. `Toggled` and
  `Checked` fire for values set from code too.
- **Every subscription goes through `Watch`**, which records it. The panel
  releases them all on every rebuild and whenever it hides; the view model
  outlives the rows, and a forgotten handler keeps a whole panel alive.
- **Rebuild on a change, not on a notification.** A setter that raises on every
  step of a drag would otherwise tear the slider out from under the pointer.
  See `FollowLateReadings` for the pattern.

## Tiles people make

A custom tile runs either

- a **`dispctrl` command** - the same words typed in a terminal, run through
  `dispctrl.exe` (found beside the app, in the build tree, or on `PATH`), with
  its exit code reported in the panel: `0` done, `1` refused, `2` not understood; or
- a **program, script, document or address**, opened the way Explorer would -
  `ms-settings:display`, `https://...`, `C:\Tools\script.cmd`.

```json
{ "id": "custom-3f2a91c0", "label": "Movie", "glyph": "E768",
  "kind": "Command", "target": "preset apply Movie", "arguments": "" }
```

`glyph` may be the character or its code point in hex. The id always starts
with `custom-`, so it cannot collide with a built-in one. Custom tiles order,
hide and move like any other. Nothing runs except when its tile is clicked.

## Testing what you add

Synthetic pointer input does not reach WinUI, so a panel cannot be clicked
through by a script. What can be checked is the logic: placement, the lists,
the night-light rule, unison resuming - all in `presetcheck`. For the panel
itself, drive it through UI Automation by the `AutomationProperties.Name` every
block sets (`QuickTile <id>`, `QuickDisplay <n>`, `QuickSection <title>`), and
summon it by setting `Local\DispCtrl.QuickPanel.Show`.
