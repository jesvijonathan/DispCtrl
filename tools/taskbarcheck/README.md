# Taskbar reveal regression check

On Windows, start the rebuilt engine with Focus mode enabled and enable both
taskbar hiding and work-area reclaim on the first secondary display. This check
supports a bottom-docked, horizontal secondary taskbar.

```powershell
dotnet run --project tools/taskbarcheck/taskbarcheck.csproj -c Release
```

The test opens a temporary maximized app and checks three hide/reveal cycles:

- Both dimming layers opt out of Explorer's fullscreen detection.
- The app fills the whole reclaimed monitor, including the hidden taskbar strip.
- Revealing the taskbar preserves the app and work-area dimensions.
- The revealed taskbar is above the app and receives pointer input.

The tool uses physical pixels, closes its test window and restores the cursor
and foreground window. It does not change settings. Bottom-edge screenshots
are saved to `%TEMP%\UmbraTaskbarCheck` (or an output directory passed as the
first argument). Exit code 0 means all checks passed.

The overlay exclusion uses Microsoft's documented `NonRudeHWND` property:
https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-itaskbarlist2-markfullscreenwindow
