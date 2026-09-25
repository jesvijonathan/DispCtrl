[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')][string]$Version,
    [string]$Repository = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
foreach ($part in $Version.Split('.')) {
    if ([long]$part -gt 65535) { throw 'Each version component must be 0..65535 for Windows packaging.' }
}

# Preserve formatting: an automatic version commit should contain only the
# shipping version and the matching four-part MSIX template version.
$propsPath = Join-Path $Repository 'Directory.Build.props'
$manifestPath = Join-Path $Repository 'build/packaging/AppxManifest.xml'
$props = [IO.File]::ReadAllText($propsPath)
$manifest = [IO.File]::ReadAllText($manifestPath)
if ([regex]::Matches($props, '<DispCtrlVersion>[^<]+</DispCtrlVersion>').Count -ne 1) {
    throw 'Expected one DispCtrlVersion in Directory.Build.props.'
}
$identity = ([xml]$manifest).Package.Identity
if (-not $identity.Version) { throw 'MSIX template is missing its Identity version.' }
$updatedProps = $props -replace '<DispCtrlVersion>[^<]+</DispCtrlVersion>', "<DispCtrlVersion>$Version</DispCtrlVersion>"
$pattern = '(<Identity\b[^>]*\bVersion=")[^"]+("[^>]*>)'
if ([regex]::Matches($manifest, $pattern).Count -ne 1) { throw 'Expected one MSIX Identity version attribute.' }
$updatedManifest = [regex]::Replace($manifest, $pattern, { param($match) $match.Groups[1].Value + "$Version.0" + $match.Groups[2].Value })
if ($updatedProps -ne $props) { [IO.File]::WriteAllText($propsPath, $updatedProps) }
if ($updatedManifest -ne $manifest) { [IO.File]::WriteAllText($manifestPath, $updatedManifest) }
Write-Host "Shipping version: $Version (MSIX $Version.0)"
