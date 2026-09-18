<#
.SYNOPSIS
    Installs, removes and inspects the DisplCtrl engine's autostart.

.DESCRIPTION
    INTERIM TOOLING. The shipping app registers autostart through the MSIX
    manifest's windows.startupTask extension, which is Store-legal and shows up
    in Settings > Apps > Startup. This script exists so the engine is usable
    day to day before the package is built, and should be deleted once it is.

    Nothing here needs administrator rights.

.PARAMETER Install
    Registers a logon task plus a watchdog, and starts the engine.

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
$Exe         = Join-Path $Root 'src\DisplCtrl.Engine\bin\Release\net10.0-windows10.0.26100.0\win-x64\DisplCtrl.Engine.exe'
$TaskName    = 'DisplCtrl.Engine'
$LegacyTask  = 'SecondaryTaskbarAutoHide'
$LegacyProc  = 'SecondaryTaskbarAutoHide'
$LegacyDir   = Join-Path $env:LOCALAPPDATA 'SecondaryTaskbarAutoHide'
$AppExe      = Join-Path $Root 'src\DisplCtrl.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\DisplCtrl.App.exe'
$IconPath    = Join-Path $Root 'src\DisplCtrl.App\Assets\DisplCtrl.ico'
$StartMenu   = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\DisplCtrl.lnk'

function Add-Shortcut {
    if (-not (Test-Path $AppExe)) {
        throw "Panel not built. Run: dotnet build src\DisplCtrl.App\DisplCtrl.App.csproj -c Release"
    }
    if (-not (Test-Path $IconPath)) {
        & (Join-Path $Root 'tools\New-DisplCtrlIcon.ps1') | Out-Null
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
    Write-Host 'Search for "DisplCtrl" in the Start menu.'
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
        throw "Engine not built. Run:`n  dotnet build `"$Root\src\DisplCtrl.Engine\DisplCtrl.Engine.csproj`" -c Release"
    }
}

function Stop-DisplCtrl {
    # Graceful, not Stop-Process: the engine restores the taskbars on its way
    # out. Killing it would leave a bar parked off-screen.
    if (-not (Get-Process -Name 'DisplCtrl.Engine' -ErrorAction SilentlyContinue)) { return }

    & $Exe stop | Out-Null
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 200
        if (-not (Get-Process -Name 'DisplCtrl.Engine' -ErrorAction SilentlyContinue)) { return }
    }

    Write-Warning 'Engine did not stop on request; forcing. A taskbar may need explorer restarted.'
    Get-Process -Name 'DisplCtrl.Engine' -ErrorAction SilentlyContinue | Stop-Process -Force
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
        Write-Host '  Left in place deliberately, as a rollback if DisplCtrl disappoints.'
        Write-Host '  Delete whenever you like - nothing runs from there any more.'
    }
}

function Install-DisplCtrl {
    Assert-Built
    Stop-DisplCtrl

    $action = New-ScheduledTaskAction -Execute $Exe -Argument 'run'

    $atLogon = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    # Explorer needs a moment after logon to create the secondary taskbars.
    $atLogon.Delay = 'PT20S'

    # A logon trigger alone means one failed start leaves the engine dead until
    # the next logon - and a crash or a monitor replug does not produce a logon.
    # MultipleInstances IgnoreNew plus the engine's own mutex make a redundant
    # fire a no-op, so this costs nothing while things are healthy.
    #
    # Leaving Repetition.Duration unset is what Task Scheduler reads as "repeat
    # indefinitely"; TimeSpan::MaxValue serialises out of range and makes
    # registration fail outright.
    $watchdog = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) `
        -RepetitionInterval (New-TimeSpan -Minutes 2)
    $watchdog.Repetition.Duration = $null
    $watchdog.Repetition.StopAtDurationEnd = $false

    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0 -RestartCount 3 `
        -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew `
        -StartWhenAvailable

    $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" `
        -LogonType Interactive -RunLevel Limited

    Register-ScheduledTask -TaskName $TaskName -Action $action `
        -Trigger @($atLogon, $watchdog) -Settings $settings -Principal $principal -Force | Out-Null
    Write-Host "Registered '$TaskName' (at logon + 2-minute watchdog)."

    if (Test-Path $AppExe) { Add-Shortcut }

    Start-ScheduledTask -TaskName $TaskName
    Start-Sleep -Seconds 2
    Show-Status
}

function Uninstall-DisplCtrl {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Removed task '$TaskName'."
    }
    Stop-DisplCtrl
    Write-Host 'Engine stopped and taskbars restored.'
}

function Show-Status {
    $proc = Get-Process -Name 'DisplCtrl.Engine' -ErrorAction SilentlyContinue
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

    $log = Join-Path $env:LOCALAPPDATA 'DisplCtrl\engine.log'
    if (Test-Path $log) {
        Write-Host ''
        Write-Host 'Recent log:'
        Get-Content $log -Tail 10 | ForEach-Object { Write-Host "  $_" }
    }
}

if     ($Install)        { Install-DisplCtrl }
elseif ($Uninstall)      { Uninstall-DisplCtrl; Remove-Shortcut }
elseif ($RemoveLegacy)   { Remove-Legacy }
elseif ($AddShortcut)    { Add-Shortcut }
elseif ($RemoveShortcut) { Remove-Shortcut }
else                     { Show-Status }
