[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DesktopDirectory,
    # Both default to the Store identity in build/packaging/AppxManifest.xml.
    [ValidatePattern('^[A-Za-z0-9.-]{3,50}$')][string]$IdentityName,
    [string]$Publisher,
    [string]$PublisherDisplayName = 'JustVStudio',
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version,
    [string]$MakeAppx,
    # Signs with Sign.ps1. The certificate subject must equal -Publisher.
    [switch]$Sign,
    # Where the MSIX goes; by default beside the desktop folder, in the same
    # bundle as the zips and the installer.
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    $shipping = ([xml](Get-Content (Join-Path $repo 'Directory.Build.props') -Raw)).Project.PropertyGroup.DispCtrlVersion | Where-Object { $_ } | Select-Object -First 1
    $Version = "$shipping.0"
}
$template = [xml](Get-Content -LiteralPath (Join-Path $repo 'build/packaging/AppxManifest.xml') -Raw)
if (-not $IdentityName) { $IdentityName = $template.Package.Identity.Name }
if (-not $Publisher) { $Publisher = $template.Package.Identity.Publisher }
$source = (Resolve-Path -LiteralPath $DesktopDirectory).Path
foreach ($exe in @('DispCtrl.App.exe','DispCtrl.Engine.exe','dispctrl.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $exe))) { throw "Missing published executable: $exe" }
}
if (-not $Publisher.StartsWith('CN=')) { throw 'Publisher must match the certificate or Partner Center identity (CN=...). ' }
foreach ($part in $Version.Split('.')) { if ([int]$part -gt 65535) { throw 'Each MSIX version component must be 0..65535.' } }
if (-not $MakeAppx) {
    $roots = @((Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'), (Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools'))
    $MakeAppx = @($roots | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object {
        Get-ChildItem -LiteralPath $_ -Filter makeappx.exe -Recurse | Where-Object FullName -match '[\\/]x64[\\/]'
    } | Sort-Object FullName -Descending | Select-Object -First 1).FullName
}
if (-not $MakeAppx -or -not (Test-Path -LiteralPath $MakeAppx)) { throw 'Install Windows SDK build tools or pass -MakeAppx.' }
$output = if ($OutputDirectory) { $OutputDirectory } else { Split-Path -Parent $source }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$output = (Resolve-Path -LiteralPath $output).Path
# A full copy of the desktop folder, so it is staged in temp and removed after:
# left in artifacts it was a few hundred MB per run.
$stage = Join-Path ([IO.Path]::GetTempPath()) ('dispctrl-msix-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $stage -Recurse -Force
$assets = Join-Path $stage 'PackageAssets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::new((Join-Path $repo 'src/DispCtrl.App/Assets/DispCtrl.ico'),256,256)
$original = $icon.ToBitmap()
function Save-Tile([int]$size, [string]$name) {
    $bitmap = [System.Drawing.Bitmap]::new($size,$size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($original,0,0,$size,$size)
        $bitmap.Save((Join-Path $assets $name),[System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
try {
    # Each tile at each scale; the manifest names the unqualified file.
    foreach ($tile in @(@('Logo44',44),@('Logo50',50),@('Logo150',150))) {
        foreach ($scale in @(100,125,150,200,400)) { Save-Tile ([math]::Round($tile[1] * $scale / 100)) "$($tile[0]).scale-$scale.png" }
    }
    # The taskbar, Alt+Tab and the title bar ask for a target size. With no
    # unplated variant Windows draws the icon on a solid plate, which is how a
    # pinned DispCtrl showed a square behind it while Start did not.
    foreach ($size in @(16,20,24,30,32,36,40,48,60,64,72,80,96,256)) {
        foreach ($form in @('','_altform-unplated','_altform-lightunplated')) { Save-Tile $size "Logo44.targetsize-$size$form.png" }
    }
} finally { $original.Dispose(); $icon.Dispose() }
$manifest = $template
$manifest.Package.Identity.SetAttribute('Name',$IdentityName)
$manifest.Package.Identity.SetAttribute('Publisher',$Publisher)
$manifest.Package.Identity.SetAttribute('Version',$Version)
$manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
$manifest.Save((Join-Path $stage 'AppxManifest.xml'))

# The qualified tiles are found through resources.pri. WinUI loads that in
# preference to DispCtrl.App.pri, so it has to carry the app's own resources
# too: makepri's PRI indexer merges DispCtrl.App.pri in, under the package's
# map name, as a Visual Studio MSIX build lays it out. Built from a copy of
# only the tiles and the app's PRI, so the folder indexer does not take every
# DLL in the package for a resource.
$makePri = Join-Path (Split-Path -Parent $MakeAppx) 'makepri.exe'
if (-not (Test-Path -LiteralPath $makePri)) { throw "makepri.exe is not beside $MakeAppx." }
$priRoot = Join-Path $stage '.pri-root'
New-Item -ItemType Directory -Path $priRoot -Force | Out-Null
Copy-Item -LiteralPath $assets -Destination $priRoot -Recurse
Copy-Item -LiteralPath (Join-Path $stage 'DispCtrl.App.pri') -Destination $priRoot
$priConfig = Join-Path $priRoot 'priconfig.xml'
& $makePri createconfig /cf $priConfig /dq lang-en-US /pv 10.0.0 /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makepri createconfig failed.' }
& $makePri new /pr $priRoot /cf $priConfig /mn (Join-Path $stage 'AppxManifest.xml') /of (Join-Path $stage 'resources.pri') /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makepri could not build resources.pri.' }
Remove-Item -LiteralPath $priRoot -Recurse -Force
$package = Join-Path $output "DispCtrl-$Version-x64.msix"
try { & $MakeAppx pack /d $stage /p $package /o }
finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
if ($LASTEXITCODE -ne 0) { throw 'MSIX validation/packing failed.' }
if ($Sign) { & "$PSScriptRoot/Sign.ps1" -Path $package }
Get-FileHash -LiteralPath $package -Algorithm SHA256 | ForEach-Object {
    "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))"
} | Set-Content -LiteralPath (Join-Path $output 'MSIX-SHA256SUMS.txt') -Encoding ascii
Write-Output "$(if ($Sign) { 'Signed' } else { 'Unsigned' }) MSIX: $package ($IdentityName, $Publisher)"
return $package
