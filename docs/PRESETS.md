# Display presets

**Status: shelved beta.** Normal builds disable presets entirely: no navigation tab,
sticky controls, preset shortcuts, foreground-app rules, CLI operations or report
collection. Existing preset files and stored rules remain intact but inactive.
This is a build-time gate, not a setting users can accidentally re-enable.

For future development only, build all components with `-p:EnableBetaPresets=true`.
Use a separate output directory for beta builds and rebuild all components when
switching modes. The UI labels the enabled feature as Beta. The implementation
below is retained for that work; it is not available in normal releases.

The sticky display-page controls remain the quick way to save and restore a selected setup. The Presets page manages capture, files, monitor mapping and foreground-app rules.

## Capture and restore

Choose **Whole desk** or one display under **Capture a setup**. A whole-desk capture includes active displays, layout/topology, display modes, scaling, HDR, brightness, software dimming, warmth and calibration, wallpaper paths and fit, taskbar settings, VRR where available, custom monitor names, and readable, writable DDC/CI controls. Monitor-only captures contain one monitor and set `includeGlobal` and `includeLayout` to `false`.

**Update from displays** preserves the preset's monitor selection and scope. Disconnected monitors retain their saved values. Apply restores the recorded values, refreshes display handles after topology/mode changes, and checks readable state afterwards. Failures and missing displays produce an incomplete-restoration report; successful individual changes are retained. Applying is serialized across the panel, engine and CLI.

Monitor-only snapshots keep shared warmth/unison switches unchanged. Their per-monitor values take effect under the existing shared configuration. Resolution changes can still cause Windows to adjust the desktop geometry.

Only state exposed by the app's supported control APIs can be restored. Serial numbers, physical dimensions, DPI information and the colour-profile description are identification/reference fields, not hardware writes. Unreadable brightness uses `-1`; unrecorded scaling uses `0`. Wallpaper files and ICC profiles are not embedded. A JSON file does not contain drivers, monitor firmware state, hotkeys, or the app-rule configuration itself.

## Read, edit and share

Each preset is an indented JSON file in `%LOCALAPPDATA%\DisplCtrl\presets`. **View / edit JSON** validates before saving; saving a file does not apply it. Comments and trailing commas are accepted, and saving normalizes formatting. Reopen the Presets page to load external file edits. Use Rename for a new filename so app-rule references update too.

Schema version 3 adds:

```json
{
  "version": 3,
  "name": "Evening",
  "description": "My saved display setup",
  "includeGlobal": false,
  "includeLayout": false,
  "captureNotes": [],
  "monitors": {}
}
```

This is a structural example, not a complete display snapshot. Start by capturing a setup to obtain actual values and identity tokens. Edit recorded fields in that file; omitted non-nullable fields use their documented model defaults, so deleting a field is not a general-purpose way to exclude it. `includeGlobal` and `includeLayout` exclude those groups explicitly. Older version 1/2 files keep whole-desk scope. Unsupported future versions, invalid values and null state objects are rejected.

Export sends the JSON file. Import preserves existing presets by choosing a numbered name on collision. On another machine, **Map displays** explicitly assigns saved monitor identities to attached monitors; targets must be unique. No monitor is automatically substituted by a similar name. Review resolutions, hardware controls and wallpaper paths before applying on different hardware. Unsupported values are reported by Apply.

## App rules

Rules match an executable basename, with or without `.exe`, case-insensitively. Choose an existing preset, an activation delay (0.5–60 seconds), and whether to restore the previous setup after leaving the app. New rules default to restoring the previous setup. Alternatively choose a named return preset, or leave it blank to keep the applied setup. First matching enabled rule wins.

The resident engine must be running. Transitions are serial; short foreground changes are debounced. When switching directly between two matching apps, the previous rule is restored before the next baseline is captured. Removing/disabling an active rule also releases it. Graceful engine shutdown restores the active baseline; an unexpected crash cannot restore an in-memory snapshot. Renaming updates named rule references; deleting a preset disables rules that apply it.

## Verification

`dotnet run --project tools/presetverify -c Release` checks parsing, schema/range validation, scoped drift, scope-preserving recapture, readable reports and rule serialization without touching user settings or hardware.
