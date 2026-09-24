[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DesktopDirectory,
    # Both default to the Store identity in build/packaging/AppxManifest.xml.
    [ValidatePattern('^[A-Za-z0-9.-]{3,50}$')][string]$IdentityName,
    [string]$Publisher,
    [string]$PublisherDisplayName = 'JustVStudio',
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version = '0.1.0.0',
    [string]$MakeAppx,
    # Signs with Sign.ps1. The certificate subject must equal -Publisher.
    [switch]$Sign,
    # Where the MSIX goes; by default beside the desktop folder, in the same
    # bundle as the zips and the installer.
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
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
try {
    foreach ($size in @(44,50,150)) {
        $bitmap = [System.Drawing.Bitmap]::new($size,$size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($original,0,0,$size,$size)
            $bitmap.Save((Join-Path $assets "Logo$size.png"),[System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $original.Dispose(); $icon.Dispose() }
$manifest = $template
$manifest.Package.Identity.SetAttribute('Name',$IdentityName)
$manifest.Package.Identity.SetAttribute('Publisher',$Publisher)
$manifest.Package.Identity.SetAttribute('Version',$Version)
$manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
$manifest.Save((Join-Path $stage 'AppxManifest.xml'))
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
