<#
.SYNOPSIS
    Installs, removes and inspects the DispCtrl engine's autostart.

.DESCRIPTION
    INTERIM TOOLING. The shipping app registers autostart through the MSIX
    manifest's windows.startupTask extension, which is Store-legal and shows up
    in Settings > Apps > Startup. This script exists so the engine is usable
    day to day before the package is built, and should be deleted once it is.

    Nothing here needs administrator rights.

.PARAMETER Install
    Registers the engine's sign-in task (through dispctrl startup) and starts it.

.PARAMETER Uninstall
    Asks the engine to restore the taskbars and exit, then removes the task.

.PARAMETER RemoveLegacy
    Removes the older SecondaryTaskbarAutoHide watcher this replaces.

.PARAMETER Status
    Reports what is running and what is registered.
#>
[CmdletBinding(DefaultParameterSetName = 'Status')]
param(
    [Parameter(ParameterSetName = 'Install')][switch]$Install,
    [Parameter(ParameterSetName = 'Uninstall')][switch]$Uninstall,
    [Parameter(ParameterSetName = 'RemoveLegacy')][switch]$RemoveLegacy,
    [Parameter(ParameterSetName = 'Status')][switch]$Status,
    [Parameter(ParameterSetName = 'AddShortcut')][switch]$AddShortcut,
    [Parameter(ParameterSetName = 'RemoveShortcut')][switch]$RemoveShortcut
)

$ErrorActionPreference = 'Stop'

$Root        = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Exe         = Join-Path $Root 'src\DispCtrl.Engine\bin\Release\net10.0-windows10.0.26100.0\win-x64\DispCtrl.Engine.exe'
$TaskName    = 'DispCtrl.Engine'
$PreviousTaskName = 'Displ' + 'Ctrl.Engine'
$LegacyTask  = 'SecondaryTaskbarAutoHide'
$LegacyProc  = 'SecondaryTaskbarAutoHide'
$LegacyDir   = Join-Path $env:LOCALAPPDATA 'SecondaryTaskbarAutoHide'
$AppExe      = Join-Path $Root 'src\DispCtrl.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\DispCtrl.App.exe'
$IconPath    = Join-Path $Root 'src\DispCtrl.App\Assets\DispCtrl.ico'
$StartMenu   = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\DispCtrl.lnk'

function Add-Shortcut {
    if (-not (Test-Path $AppExe)) {
        throw "Panel not built. Run: dotnet build src\DispCtrl.App\DispCtrl.App.csproj -c Release"
    }
    if (-not (Test-Path $IconPath)) {
        & (Join-Path $Root 'tools\New-DispCtrlIcon.ps1') | Out-Null
    }

    # A .lnk is interim. The MSIX package declares its own Start entry and
    # tile, at which point this and the shortcut it writes both go away.
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($StartMenu)
    $lnk.TargetPath = $AppExe
    $lnk.WorkingDirectory = Split-Path -Parent $AppExe
    $lnk.IconLocation = "$IconPath,0"
    $lnk.Description = 'Per-monitor display management'
    $lnk.Save()

    Write-Host "Added Start menu entry: $StartMenu"
    Write-Host 'Search for "DispCtrl" in the Start menu.'
}

function Remove-Shortcut {
    if (Test-Path $StartMenu) {
        Remove-Item $StartMenu -Force
        Write-Host 'Removed the Start menu entry.'
    } else {
        Write-Host 'No Start menu entry to remove.'
    }
}

function Assert-Built {
    if (-not (Test-Path $Exe)) {
        throw "Engine not built. Run:`n  dotnet build `"$Root\src\DispCtrl.Engine\DispCtrl.Engine.csproj`" -c Release"
    }
}

function Stop-DispCtrl {
    # Graceful, not Stop-Process: the engine restores the taskbars on its way
    # out. Killing it would leave a bar parked off-screen.
    if (-not (Get-Process -Name 'DispCtrl.Engine' -ErrorAction SilentlyContinue)) { return }

    & $Exe stop | Out-Null
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 200
        if (-not (Get-Process -Name 'DispCtrl.Engine' -ErrorAction SilentlyContinue)) { return }
    }

    Write-Warning 'Engine did not stop on request; forcing. A taskbar may need explorer restarted.'
    Get-Process -Name 'DispCtrl.Engine' -ErrorAction SilentlyContinue | Stop-Process -Force
}

function Remove-Legacy {
    $task = Get-ScheduledTask -TaskName $LegacyTask -ErrorAction SilentlyContinue
    $proc = Get-Process -Name $LegacyProc -ErrorAction SilentlyContinue

    if (-not $task -and -not $proc) {
        Write-Host 'Legacy watcher: not present.'
        return
    }

    # Order matters: unregister first, so the 2-minute watchdog cannot restart
    # it in the gap between killing the process and removing the task.
    if ($task) {
        Unregister-ScheduledTask -TaskName $LegacyTask -Confirm:$false
        Write-Host "Removed legacy task '$LegacyTask'."
    }
    if ($proc) {
        $proc | Stop-Process -Force -ErrorAction SilentlyContinue
        Write-Host "Stopped legacy watcher (pid $($proc.Id))."
    }

    Start-Sleep -Milliseconds 500

    if (Test-Path $LegacyDir) {
        Write-Host ''
        Write-Host "Its files are still at:  $LegacyDir"
        Write-Host '  Left in place deliberately, as a rollback if DispCtrl disappoints.'
        Write-Host '  Delete whenever you like - nothing runs from there any more.'
    }
}

# One registration, owned by StartupIntegration.RegisterEngineTask and reached
# through the CLI, so this script, the Settings page and `dispctrl startup` can
# never register different tasks. It used to build its own here, with a 20 s
# logon delay, the scheduler's below-normal priority and a two-minute watchdog
# that restarted an engine somebody had deliberately switched off.
$Cli = Join-Path $Root 'src\DispCtrl.Cli\bin\Release\net10.0-windows10.0.26100.0\win-x64\dispctrl.exe'

function Install-DispCtrl {
    Assert-Built
    if (-not (Test-Path $Cli)) { throw "CLI not built. Run: dotnet build src\DispCtrl.Cli\DispCtrl.Cli.csproj -c Release" }
    Stop-DispCtrl

    & $Cli startup set --engine on --json | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Registering the startup task failed; run dispctrl startup set --engine on to see why.' }
    Write-Host "Registered '$TaskName' (at sign-in, no delay, normal priority, restarted on failure)."

    if (Test-Path $AppExe) { Add-Shortcut }

    Start-ScheduledTask -TaskName $TaskName
    Start-Sleep -Seconds 2
    Show-Status
}

function Uninstall-DispCtrl {
    if (Test-Path $Cli) { & $Cli startup set --engine off --json | Out-Null }
    foreach ($registeredName in @($TaskName, $PreviousTaskName)) {
        if (Get-ScheduledTask -TaskName $registeredName -ErrorAction SilentlyContinue) {
            Unregister-ScheduledTask -TaskName $registeredName -Confirm:$false
            Write-Host "Removed task '$registeredName'."
        }
    }
    Stop-DispCtrl
    Write-Host 'Engine stopped and taskbars restored.'
}

function Show-Status {
    $proc = Get-Process -Name 'DispCtrl.Engine' -ErrorAction SilentlyContinue
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

    if ($proc) {
        Write-Host ("Engine  : running (pid {0}, {1:N1} MB)" -f $proc.Id, ($proc.WorkingSet64 / 1MB))
    } else {
        Write-Host 'Engine  : NOT running'
    }

    if ($task) {
        $info = $task | Get-ScheduledTaskInfo
        Write-Host ("Task    : {0}, {1} trigger(s), last result {2}" -f `
            $task.State, $task.Triggers.Count, $info.LastTaskResult)
    } else {
        Write-Host 'Task    : NOT registered'
    }

    $legacyTask = Get-ScheduledTask -TaskName $LegacyTask -ErrorAction SilentlyContinue
    $legacyProc = Get-Process -Name $LegacyProc -ErrorAction SilentlyContinue
    if ($legacyTask -or $legacyProc) {
        Write-Warning 'The legacy SecondaryTaskbarAutoHide watcher is still present.'
        Write-Warning 'Both will fight over the same taskbar. Run with -RemoveLegacy.'
    } else {
        Write-Host 'Legacy  : removed'
    }

    Write-Host ''
    if (Test-Path $Exe) { & $Exe status }

    $log = Join-Path $env:LOCALAPPDATA 'DispCtrl\engine.log'
    if (Test-Path $log) {
        Write-Host ''
        Write-Host 'Recent log:'
        Get-Content $log -Tail 10 | ForEach-Object { Write-Host "  $_" }
    }
}

if     ($Install)        { Install-DispCtrl }
elseif ($Uninstall)      { Uninstall-DispCtrl; Remove-Shortcut }
elseif ($RemoveLegacy)   { Remove-Legacy }
elseif ($AddShortcut)    { Add-Shortcut }
elseif ($RemoveShortcut) { Remove-Shortcut }
else                     { Show-Status }
