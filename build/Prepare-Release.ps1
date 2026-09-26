[CmdletBinding()]
param(
    [string]$Version,
    # With no version given: the project's version as it is, or the next patch,
    # minor or major one - so a release never needs a number typed.
    [ValidateSet('current','patch','minor','major')][string]$Bump = 'current',
    [ValidateSet('stable','beta','test')][string]$Channel = 'stable',
    [ValidateSet('branch','tag')][string]$RefType = 'branch',
    [Parameter(Mandatory)][string]$RefName,
    [ValidatePattern('^\d*$')][string]$TestRun = '',
    [switch]$UpdateVersion,
    [switch]$CreateTag,
    [string]$Repository = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
$Version = "$Version".Trim()
# Git for Windows puts git.exe on PATH three times; take the first match only.
$gitExecutable = @(Get-Command git -CommandType Application)[0].Source
function Git {
    & $gitExecutable @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed (exit $LASTEXITCODE)." }
}
Push-Location $Repository
try {
    $declared = ([xml](Get-Content Directory.Build.props -Raw)).Project.PropertyGroup.DispCtrlVersion |
        Where-Object { $_ } | Select-Object -First 1
    if ($RefType -eq 'tag') {
        if ($RefName -cnotmatch '^v(?<version>\d+\.\d+\.\d+)(?:-(?<channel>beta|test)(?:\.\d+)?)?$') {
            throw "Unsupported release tag '$RefName'. Use v1.2.3, v1.2.3-beta[.N] or v1.2.3-test[.N]."
        }
        $Version = $Matches.version
        $Channel = if ($Matches.channel) { $Matches.channel } else { 'stable' }
        $tag = $RefName
    } else {
        # A version typed by hand wins; the bump is what happens when none is.
        if (-not $Version) {
            $Version = $declared
            if ($Bump -ne 'current') {
                if ($declared -notmatch '^(\d+)\.(\d+)\.(\d+)$') { throw "Directory.Build.props declares '$declared', which cannot be bumped." }
                [long]$major = $Matches[1]; [long]$minor = $Matches[2]; [long]$patch = $Matches[3]
                $Version = switch ($Bump) {
                    'patch' { "$major.$minor.$($patch + 1)" }
                    'minor' { "$major.$($minor + 1).0" }
                    'major' { "$($major + 1).0.0" }
                }
            }
        } elseif ($Bump -ne 'current') {
            Write-Host "Releasing $Version as typed; the '$Bump' choice applies only when no version is given."
        }
        $suffix = if ($Channel -eq 'test' -and $TestRun) { "-test.$TestRun" } elseif ($Channel -ne 'stable') { "-$Channel" } else { '' }
        $tag = "v$Version$suffix"
    }
    if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw "'$Version' is not <major>.<minor>.<patch>, for example 0.1.3."
    }
    foreach ($part in $Version.Split('.')) {
        if ([long]$part -gt 65535) { throw 'Each version component must be 0..65535 for Windows packaging.' }
    }
    if (Git status --porcelain) { throw 'Release preparation requires a clean checkout.' }
    $head = Git rev-parse HEAD
    # Query the remote even on a rerun: a local tag alone is not a release tag.
    $remoteTag = @(Git ls-remote --tags origin "refs/tags/$tag" "refs/tags/$tag^{}")
    $exists = $remoteTag.Count -gt 0
    if ($exists) {
        $peeled = @($remoteTag | Where-Object { $_ -match '\^\{\}$' })
        $tagCommit = (($remoteTag + $peeled)[-1] -split '\s+')[0]
        if ($tagCommit -ne $head) {
            throw "Tag $tag points to another commit. Run Release from that tag, or choose a new version; existing tags are never moved."
        }
    } elseif ($RefType -eq 'tag' -or -not $CreateTag) {
        throw "Tag $tag does not exist on origin. Enable Create the tag when releasing from a branch."
    }
    $commitVersion = $RefType -eq 'branch' -and -not $exists -and $UpdateVersion
    if ($RefType -eq 'branch' -and -not $exists) {
        Git check-ref-format "refs/heads/$RefName" | Out-Host
        $remoteBranch = @(Git ls-remote --heads origin "refs/heads/$RefName")
        if ($remoteBranch.Count -ne 1 -or ($remoteBranch[0] -split '\s+')[0] -ne $head) {
            throw "Branch $RefName has moved or is missing. Start a new release run from the current branch."
        }
    }
    $changed = $false
    if ($commitVersion) {
        & "$PSScriptRoot/Update-Version.ps1" -Version $Version -Repository $Repository
        if (Git status --porcelain) {
            Git add -- Directory.Build.props build/packaging/AppxManifest.xml | Out-Host
            Git -c user.name=github-actions[bot] -c user.email=41898282+github-actions[bot]@users.noreply.github.com commit -m "Set shipping version to $Version" | Out-Host
            $changed = $true
        }
    } elseif ($declared -ne $Version) {
        $message = "Directory.Build.props declares $declared but this release is $Version."
        if ($Channel -eq 'stable') {
            throw "$message Enable Update version automatically for a new branch release, or run build/Update-Version.ps1 before committing and tagging."
        }
        Write-Warning $message
    }
    if (-not $exists) {
        Git tag $tag | Out-Host
        # If a branch rule rejects the version commit, do not leave a release
        # tag behind. No force pushes, even if another run wins the race.
        $refs = @("refs/tags/$tag")
        if ($changed) { $refs += "HEAD:refs/heads/$RefName" }
        Git push --atomic origin @refs | Out-Host
    }
    [pscustomobject]@{ Version = $Version; Channel = $Channel; Tag = $tag }
} finally { Pop-Location }
