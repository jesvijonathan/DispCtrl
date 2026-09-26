[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$prepare = Join-Path $PSScriptRoot 'Prepare-Release.ps1'
$update = Join-Path $PSScriptRoot 'Update-Version.ps1'
$source = Split-Path -Parent $PSScriptRoot
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $tempRoot ('dispctrl-release-check-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot
$passed = 0
function RunGit {
    & git @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed." }
}
function Check([bool]$ok, [string]$name) {
    if (-not $ok) { throw "FAIL: $name" }
    $script:passed++
    Write-Host "PASS: $name"
}
function Reject([scriptblock]$action, [string]$message) {
    $caught = $false
    try { & $action | Out-Null } catch {
        if ($_.Exception.Message -notlike "*$message*") { throw }
        $caught = $true
    }
    Check $caught "Reject $message"
}
Push-Location $testRoot
try {
    RunGit init --bare origin.git | Out-Host
    RunGit init -b main work | Out-Host
    Set-Location work
    RunGit config user.name 'Release check' | Out-Host
    RunGit config user.email 'release-check@example.invalid' | Out-Host
    RunGit config core.autocrlf false | Out-Host
    RunGit remote add origin (Join-Path $testRoot 'origin.git') | Out-Host
    $repo = (Get-Location).Path
    $null = New-Item -ItemType Directory -Path build/packaging -Force
    Copy-Item (Join-Path $source 'Directory.Build.props') .
    Copy-Item (Join-Path $source 'build/packaging/AppxManifest.xml') build/packaging
    & $update -Version 0.1.2 -Repository $repo
    RunGit add . | Out-Host
    RunGit commit -m Initial | Out-Host
    RunGit push origin main | Out-Host
    $initial = RunGit rev-parse HEAD
    $prepareArgs = @{ Repository = $repo; RefName = 'main'; CreateTag = $true }
    Reject { & $prepare @prepareArgs -Version '1.3' } 'not <major>'
    Reject { & $prepare @prepareArgs -Version '0.1.65536' } '0..65535'
    Reject { & $prepare @prepareArgs -Version '0.1.3' } 'declares 0.1.2'
    Check ((RunGit rev-parse HEAD) -eq $initial) 'Rejected versions do not commit'

    $release = & $prepare @prepareArgs -Version 0.1.3 -UpdateVersion
    $releaseCommit = RunGit rev-parse HEAD
    Check ($release.Version -eq '0.1.3' -and $release.Channel -eq 'stable' -and $release.Tag -eq 'v0.1.3') 'Stable release resolves'
    Check ($releaseCommit -ne $initial) 'Automatic version creates a commit'
    Check ((RunGit rev-parse v0.1.3) -eq $releaseCommit) 'Tag targets version commit'
    Check (((RunGit ls-remote --heads origin refs/heads/main) -split '\s+')[0] -eq $releaseCommit) 'Branch receives version commit'
    Check (([xml](Get-Content build/packaging/AppxManifest.xml -Raw)).Package.Identity.Version -eq '0.1.3.0') 'MSIX version stays in sync'
    Check (-not (RunGit status --porcelain)) 'Checkout stays clean'
    $null = & $prepare @prepareArgs -Version 0.1.3 -UpdateVersion
    Check ((RunGit rev-parse HEAD) -eq $releaseCommit) 'Rerun is idempotent'
    $test = & $prepare @prepareArgs -Version 0.1.3 -Channel test -TestRun 42 -UpdateVersion
    Check ($test.Tag -eq 'v0.1.3-test.42' -and $test.Channel -eq 'test') 'Test gets a numbered tag'
    $beta = & $prepare @prepareArgs -Channel beta
    Check ($beta.Tag -eq 'v0.1.3-beta') 'Blank version reads project version'
    Reject { & $prepare @prepareArgs -Version 0.1.9 -Bump patch } 'not both'
    $bumped = & $prepare @prepareArgs -Bump patch -Channel beta
    Check ($bumped.Version -eq '0.1.4' -and $bumped.Tag -eq 'v0.1.4-beta') 'Next patch is worked out from the project version'
    $bumped = & $prepare @prepareArgs -Bump minor -Channel beta
    Check ($bumped.Version -eq '0.2.0') 'Next minor resets the patch'
    $bumped = & $prepare @prepareArgs -Bump major -Channel beta
    Check ($bumped.Version -eq '1.0.0') 'Next major resets minor and patch'
    RunGit tag -a v0.1.3-beta.2 -m 'Annotated beta' | Out-Host
    RunGit push origin refs/tags/v0.1.3-beta.2 | Out-Host
    $tagged = & $prepare -Repository $repo -RefType tag -RefName v0.1.3-beta.2
    Check ($tagged.Channel -eq 'beta') 'Annotated beta tag resolves to its commit'
    $tagged = & $prepare -Repository $repo -RefType tag -RefName v0.1.3-test.42
    Check ($tagged.Channel -eq 'test') 'Pushed test tag retains its channel'
    Reject { & $prepare -Repository $repo -RefType tag -RefName v0.1.3-unknown } 'Unsupported release tag'
    Reject { & $prepare -Repository $repo -RefName main -Version 0.1.4 } 'does not exist'

    Set-Content other.txt 'A later change'
    Reject { & $prepare @prepareArgs -Version 0.1.4 -UpdateVersion } 'clean checkout'
    RunGit add other.txt | Out-Host
    RunGit commit -m Later | Out-Host
    Reject { & $prepare @prepareArgs -Version 0.1.4 -UpdateVersion } 'has moved'
    RunGit push origin main | Out-Host
    Reject { & $prepare @prepareArgs -Version 0.1.3 -UpdateVersion } 'another commit'

    # A protected branch rejects the whole push, including its proposed tag.
    $hook = Join-Path $testRoot 'origin.git/hooks/pre-receive'
    [IO.File]::WriteAllText($hook, "#!/bin/sh`nexit 1`n")
    if (-not $IsWindows) { & chmod +x $hook }
    Reject { & $prepare @prepareArgs -Version 0.1.4 -UpdateVersion } 'failed (exit'
    Check (-not (RunGit ls-remote --tags origin refs/tags/v0.1.4)) 'Rejected branch update leaves no remote tag'
    $notes = & (Join-Path $PSScriptRoot 'ReleaseNotes.ps1') -Version v0.1.3-test.42
    Check ($notes -match 'prerelease for testing' -and $notes -notmatch 'winget install') 'Test notes describe prerelease downloads'
    Write-Host "$passed release checks passed."
} finally {
    Pop-Location
    $resolved = (Resolve-Path -LiteralPath $testRoot).Path
    if ((Split-Path -Parent $resolved).TrimEnd([IO.Path]::DirectorySeparatorChar) -ne $tempRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)) {
        throw "Refusing to remove test directory outside $tempRoot"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
