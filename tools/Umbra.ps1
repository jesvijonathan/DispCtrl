<#
.SYNOPSIS
    Installs, removes and inspects the Umbra engine's autostart.

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
    [Parameter(ParameterSetName = 'Status')][switch]$Status
)

$ErrorActionPreference = 'Stop'

$Root        = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Exe         = Join-Path $Root 'src\Umbra.Engine\bin\Release\net10.0-windows10.0.26100.0\win-x64\Umbra.Engine.exe'
$TaskName    = 'Umbra.Engine'
$LegacyTask  = 'SecondaryTaskbarAutoHide'
$LegacyProc  = 'SecondaryTaskbarAutoHide'
$LegacyDir   = Join-Path $env:LOCALAPPDATA 'SecondaryTaskbarAutoHide'

function Assert-Built {
    if (-not (Test-Path $Exe)) {
        throw "Engine not built. Run:`n  dotnet build `"$Root\src\Umbra.Engine\Umbra.Engine.csproj`" -c Release"
    }
}

function Stop-Umbra {
    # Graceful, not Stop-Process: the engine restores the taskbars on its way
    # out. Killing it would leave a bar parked off-screen.
    if (-not (Get-Process -Name 'Umbra.Engine' -ErrorAction SilentlyContinue)) { return }

    & $Exe stop | Out-Null
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 200
        if (-not (Get-Process -Name 'Umbra.Engine' -ErrorAction SilentlyContinue)) { return }
    }

    Write-Warning 'Engine did not stop on request; forcing. A taskbar may need explorer restarted.'
    Get-Process -Name 'Umbra.Engine' -ErrorAction SilentlyContinue | Stop-Process -Force
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
        Write-Host '  Left in place deliberately, as a rollback if Umbra disappoints.'
        Write-Host '  Delete whenever you like - nothing runs from there any more.'
    }
}

function Install-Umbra {
    Assert-Built
    Stop-Umbra

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

    Start-ScheduledTask -TaskName $TaskName
    Start-Sleep -Seconds 2
    Show-Status
}

function Uninstall-Umbra {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Removed task '$TaskName'."
    }
    Stop-Umbra
    Write-Host 'Engine stopped and taskbars restored.'
}

function Show-Status {
    $proc = Get-Process -Name 'Umbra.Engine' -ErrorAction SilentlyContinue
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

    $log = Join-Path $env:LOCALAPPDATA 'Umbra\engine.log'
    if (Test-Path $log) {
        Write-Host ''
        Write-Host 'Recent log:'
        Get-Content $log -Tail 10 | ForEach-Object { Write-Host "  $_" }
    }
}

if     ($Install)      { Install-Umbra }
elseif ($Uninstall)    { Uninstall-Umbra }
elseif ($RemoveLegacy) { Remove-Legacy }
else                   { Show-Status }
