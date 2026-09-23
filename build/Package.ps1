[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DesktopDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9.-]{3,50}$')][string]$IdentityName,
    [Parameter(Mandatory)][string]$Publisher,
    [string]$PublisherDisplayName = 'Jesvi Jonathan',
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version = '0.1.0.0',
    [string]$MakeAppx
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
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
$output = Join-Path $repo ('artifacts/msix-' + $Version + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
$stage = Join-Path $output 'stage'
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
$manifest = [xml](Get-Content -LiteralPath (Join-Path $repo 'packaging/AppxManifest.xml') -Raw)
$manifest.Package.Identity.SetAttribute('Name',$IdentityName)
$manifest.Package.Identity.SetAttribute('Publisher',$Publisher)
$manifest.Package.Identity.SetAttribute('Version',$Version)
$manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
$manifest.Save((Join-Path $stage 'AppxManifest.xml'))
$package = Join-Path $output "DispCtrl-$Version-x64.msix"
& $MakeAppx pack /d $stage /p $package /o
if ($LASTEXITCODE -ne 0) { throw 'MSIX validation/packing failed.' }
Get-FileHash -LiteralPath $package -Algorithm SHA256 | ForEach-Object {
    "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))"
} | Set-Content -LiteralPath (Join-Path $output 'MSIX-SHA256SUMS.txt') -Encoding ascii
Write-Output "Unsigned MSIX: $package"
