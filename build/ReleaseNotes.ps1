[CmdletBinding()]
param(
    # The tag without its v: 0.2.0 or 0.2.0-beta.1.
    [Parameter(Mandatory)][string]$Version,
    [string]$Changelog,
    # Folder searched for *SHA256SUMS.txt, whose lines are quoted in the notes.
    [string]$ArtifactsDirectory,
    # The Store listing; winget installs it through its msstore source.
    [string]$StoreId = '9PNQWKNRGVR0'
)
# Writes the release notes to stdout: the CHANGELOG section for this version,
# then how to install and how to check a download. Looks for the exact version
# first, then its numeric part (a beta ships the notes of the release it leads
# to), then Unreleased, so a tag never goes out with empty notes.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Changelog) { $Changelog = Join-Path $repo 'docs/CHANGELOG.md' }
$lines = Get-Content -LiteralPath $Changelog -Encoding utf8
function Section([string]$name) {
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^## \[(.+?)\]') {
            if ($start -ge 0) { return $lines[$start..($i - 1)] }
            if ($Matches[1] -eq $name) { $start = $i + 1 }
        } elseif ($start -ge 0 -and $lines[$i] -match '^\[.+?\]: ') {
            return $lines[$start..($i - 1)]
        }
    }
    if ($start -ge 0) { return $lines[$start..($lines.Count - 1)] }
    return $null
}
# The release workflow passes the tag itself, v and all.
$Version = $Version -replace '^v',''
$numeric = $Version -replace '-.*$',''
$body = $null
foreach ($name in @($Version, $numeric, 'Unreleased')) {
    $body = Section $name
    if ($body -and ($body -join '').Trim()) { break }
}
$notes = [Collections.Generic.List[string]]::new()
if ($body -and ($body -join '').Trim()) { $notes.AddRange([string[]](($body -join "`n").Trim() -split "`n")) }
else { $notes.Add('No changelog entry was written for this version.') }
$notes.Add('')
$notes.Add('## Install')
$notes.Add('')
$notes.Add("- **Microsoft Store**, which keeps it up to date: [DispCtrl on the Microsoft Store](https://apps.microsoft.com/detail/$StoreId)")
$notes.Add("- **winget**, the Store version from a terminal: ``winget install $StoreId --source msstore``")
$notes.Add('- **Or download a file below:**')
$notes.Add('')
$notes.Add('| File | For |')
$notes.Add('|---|---|')
$notes.Add('| `DispCtrl-*-setup.exe` | Most people. Per-user, no admin rights, starts at sign-in. |')
$notes.Add('| `DispCtrl-*-desktop.zip` | Portable: unzip anywhere and run `DispCtrl.App.exe`. |')
$notes.Add('| `DispCtrl-*-cli.zip` | `dispctrl.exe` and the engine, with no window. |')
$notes.Add('| `DispCtrl-*.msix` | The Microsoft Store package. Installs directly only when it is signed. |')
$notes.Add('')
$notes.Add('Windows 11, x64. Self-contained, so no .NET install is needed. Settings in `%LOCALAPPDATA%\DispCtrl` carry over between versions and between the installer and the zips.')
if ($ArtifactsDirectory -and (Test-Path -LiteralPath $ArtifactsDirectory)) {
    $sums = @(Get-ChildItem -LiteralPath $ArtifactsDirectory -Recurse -Filter '*SHA256SUMS.txt' | ForEach-Object { Get-Content -LiteralPath $_.FullName } | Where-Object { $_ } | Sort-Object -Unique)
    if ($sums.Count) {
        $notes.Add('')
        $notes.Add('## SHA-256')
        $notes.Add('')
        $notes.Add('```')
        $notes.AddRange([string[]]$sums)
        $notes.Add('```')
    }
}
$notes.Add('')
$notes.Add('DispCtrl is free and built in spare time. If it earns a place on your desk, [sponsoring](https://github.com/sponsors/jesvijonathan) pays for development and test hardware, and [sending your monitor](https://github.com/jesvijonathan/DispCtrl/blob/master/devices/README.md) helps just as much.')
$notes -join "`n"
