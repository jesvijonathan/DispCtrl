<#
.SYNOPSIS
    Puts DisplCtrl in the Start menu, pointing at the build output.

.DESCRIPTION
    The shortcut points straight at bin\<configuration>, not at a copy. That
    path does not change when the project is rebuilt, so the shortcut always
    launches whatever was built last — which is the whole point of it. Copying
    the exe somewhere "safe" would freeze it at today's build and quietly go
    stale, which is the problem this is meant to solve.

    Release by default, because that is the build worth running. Pass
    -Configuration Debug while working on something.

.PARAMETER Configuration
    Release or Debug. Release by default.

.PARAMETER AddToPath
    Also puts dispctrl.exe on the user PATH, so `dispctrl brightness 60` works
    from any shell. User scope only; no elevation, no machine-wide change.

.PARAMETER Remove
    Takes the shortcuts away again.

.EXAMPLE
    .\tools\Install-Shortcuts.ps1
    .\tools\Install-Shortcuts.ps1 -Configuration Debug -AddToPath
    .\tools\Install-Shortcuts.ps1 -Remove
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [switch]$AddToPath,
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'

$root      = Split-Path -Parent $PSScriptRoot
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$appLink   = Join-Path $startMenu 'DisplCtrl.lnk'
$engineLink = Join-Path $startMenu 'DisplCtrl Engine.lnk'

if ($Remove) {
    foreach ($link in @($appLink, $engineLink)) {
        if (Test-Path $link) { Remove-Item $link -Force; Write-Host "removed $link" }
    }
    Write-Host 'Unpin from the taskbar by right-clicking the button there.'
    return
}

# The target framework moniker is in the path and changes with the SDK, so it
# is discovered rather than written down.
function Find-Exe([string]$project, [string]$name) {
    $dir = Join-Path $root "src\$project\bin\$Configuration"
    if (-not (Test-Path $dir)) { return $null }

    return Get-ChildItem -Path $dir -Filter $name -Recurse -File |
           Sort-Object LastWriteTime -Descending |
           Select-Object -First 1 -ExpandProperty FullName
}

$appExe    = Find-Exe 'DisplCtrl.App'    'DisplCtrl.App.exe'
$engineExe = Find-Exe 'DisplCtrl.Engine' 'DisplCtrl.Engine.exe'
$cliExe    = Find-Exe 'DisplCtrl.Cli'    'dispctrl.exe'

if (-not $appExe) {
    Write-Error "No $Configuration build of the app. Run: dotnet build src\DisplCtrl.App -c $Configuration"
}

$icon = Join-Path $root 'src\DisplCtrl.App\Assets\DisplCtrl.ico'
$shell = New-Object -ComObject WScript.Shell

function New-Shortcut([string]$path, [string]$target, [string]$arguments, [string]$description) {
    $link = $shell.CreateShortcut($path)
    $link.TargetPath       = $target
    $link.Arguments        = $arguments
    $link.WorkingDirectory = Split-Path -Parent $target
    $link.Description      = $description
    if (Test-Path $icon) { $link.IconLocation = $icon }
    $link.Save()

    Write-Host "  $([System.IO.Path]::GetFileName($path))  ->  $target"
}

Write-Host "Start menu:"
New-Shortcut $appLink $appExe '' 'Monitor brightness, arrangement, night light and presets'

if ($engineExe) {
    # Separate entry because the engine is the resident half and is sometimes
    # wanted on its own — after it has been stopped, or at sign-in.
    New-Shortcut $engineLink $engineExe 'run' 'The DisplCtrl background engine'
}

# ---------------------------------------------------------------- taskbar --
# Windows removed the "Pin to taskbar" verb in Windows 10 1903 and it has not
# come back: pinning is a deliberate user gesture and applications are not
# allowed to do it for themselves. Anything that still manages it is forging
# shell state, which breaks on updates and is indistinguishable from what
# unwanted software does. So this reports honestly rather than trying.
$verbs = @()
try {
    $folder = (New-Object -ComObject Shell.Application).Namespace($startMenu)
    $item   = $folder.ParseName('DisplCtrl.lnk')
    $verbs  = $item.Verbs() | ForEach-Object { $_.Name -replace '&', '' }
} catch { }

$pin = $verbs | Where-Object { $_ -match 'taskbar' }

Write-Host ""
if ($pin) {
    Write-Host "Taskbar: the shell still offers '$pin' — pin it from the Start menu entry."
} else {
    Write-Host "Taskbar: Windows does not let an application pin itself, and has not since"
    Write-Host "         Windows 10 1903. Open Start, find DisplCtrl, right-click it and"
    Write-Host "         choose Pin to taskbar. It only has to be done once — the shortcut"
    Write-Host "         points at the build output, so it keeps launching the latest build."
}

# ------------------------------------------------------------------- PATH --
if ($AddToPath) {
    if (-not $cliExe) {
        Write-Warning "No $Configuration build of the command line; PATH left alone."
    } else {
        $dir = Split-Path -Parent $cliExe
        $current = [Environment]::GetEnvironmentVariable('Path', 'User')

        if ($current -split ';' -contains $dir) {
            Write-Host ""
            Write-Host "PATH: already has $dir"
        } else {
            $updated = if ([string]::IsNullOrWhiteSpace($current)) { $dir } else { "$current;$dir" }
            [Environment]::SetEnvironmentVariable('Path', $updated, 'User')

            Write-Host ""
            Write-Host "PATH: added $dir"
            Write-Host "      Open a new shell, then: dispctrl displays"
        }
    }
}

Write-Host ""
Write-Host "Shortcuts point at bin\$Configuration, so rebuilding is all it takes to update them."
