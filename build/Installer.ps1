[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DesktopDirectory,
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')][string]$Version = '0.1.1',
    [ValidateSet('stable','beta')][string]$Channel = 'beta',
    [string]$OutputDirectory,
    [string]$Iscc,
    # Signs the setup executable with Sign.ps1. The files inside it are whatever
    # the desktop folder holds, so sign those first (Publish.ps1 -Sign).
    [switch]$Sign
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$source = (Resolve-Path -LiteralPath $DesktopDirectory).Path
foreach ($exe in @('DispCtrl.App.exe','DispCtrl.Engine.exe','dispctrl.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $exe))) { throw "Missing published executable: $exe" }
}
if (-not $Iscc) { $Iscc = $env:DISPCTRL_ISCC }
if (-not $Iscc) {
    # The portable copy `build.cmd setup` fetches, then installed ones.
    $candidates = @(Join-Path $repo '.tools\innosetup\tools\ISCC.exe') + @(foreach ($major in 7, 6) {
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup $major\ISCC.exe")
        (Join-Path $env:ProgramFiles "Inno Setup $major\ISCC.exe")
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup $major\ISCC.exe")
    })
    $Iscc = @($candidates | Where-Object { Test-Path -LiteralPath $_ }) + @((Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source) |
        Where-Object { $_ } | Select-Object -First 1
}
if (-not $Iscc -or -not (Test-Path -LiteralPath $Iscc)) {
    throw 'Inno Setup 6 or 7 not found. Install it (winget install JRSoftware.InnoSetup), or pass -Iscc or set DISPCTRL_ISCC.'
}
if (-not $OutputDirectory) { $OutputDirectory = Split-Path -Parent $source }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
# Inno's VersionInfoVersion takes up to four numeric parts; the file name and
# the Add/Remove Programs entry keep the three the release uses.
& $Iscc /Qp "/DAppVersion=$Version" "/DChannel=$Channel" "/DSourceDir=$source" "/DRepoRoot=$repo" "/DOutputDir=$OutputDirectory" (Join-Path $repo 'build/packaging/installer/DispCtrl.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$setup = Join-Path $OutputDirectory "DispCtrl-$Version-$Channel-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw "Inno Setup reported success but $setup is missing." }
if ($Sign) { & "$PSScriptRoot/Sign.ps1" -Path $setup }
Get-FileHash -LiteralPath $setup -Algorithm SHA256 | ForEach-Object {
    "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))"
} | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SETUP-SHA256SUMS.txt') -Encoding ascii
Write-Output "Installer: $setup"
return $setup
