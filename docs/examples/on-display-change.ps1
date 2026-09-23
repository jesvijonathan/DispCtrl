# Re-applies a layout whenever the set of monitors changes:
#   dispctrl watch --events displays --script ./on-display-change.ps1
# The initial event fires once at start; skip it so starting the watcher
# does not rearrange the desk.
$eventData = $env:DISPCTRL_EVENT_JSON | ConvertFrom-Json
if ($eventData.initial) { return }
$layout = Join-Path $PSScriptRoot 'hotplug-layout.json'
dispctrl apply $layout --json | Write-Output
