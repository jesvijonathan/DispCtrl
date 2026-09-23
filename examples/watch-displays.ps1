# Runs only when explicitly selected with: dispctrl watch --script ./watch-displays.ps1
# A fresh process reads this file on each event; editing it takes effect next time.
$eventData = $env:DISPCTRL_EVENT_JSON | ConvertFrom-Json
Write-Output ($eventData | ConvertTo-Json -Depth 12 -Compress)
# Call dispctrl commands here to respond to a display/settings/engine change.
# Avoid unconditional settings writes from a settings event (feedback loop).
