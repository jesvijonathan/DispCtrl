param(
    # The bundle folder: the zips, the installer and the MSIX all land in it.
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][string[]]$Path
)
# Adds files to the bundle's one SHA256SUMS.txt, replacing any older line for
# the same name. Each package is built by its own script, and each wrote its
# own sums file: 0.2.0 shipped SHA256SUMS.txt, SETUP-SHA256SUMS.txt and
# MSIX-SHA256SUMS.txt for four downloads. Sorted by name, as GitHub lists the
# files under the release notes.
$ErrorActionPreference = 'Stop'
$sums = Join-Path $Directory 'SHA256SUMS.txt'
$lines = @{}
if (Test-Path -LiteralPath $sums) {
    foreach ($line in Get-Content -LiteralPath $sums) {
        if ($line -match '^([0-9a-f]{64})  (.+)$') { $lines[$Matches[2]] = $Matches[1] }
    }
}
foreach ($file in $Path) {
    $lines[[IO.Path]::GetFileName($file)] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
}
$lines.Keys | Sort-Object | ForEach-Object { "$($lines[$_])  $_" } | Set-Content -LiteralPath $sums -Encoding ascii
