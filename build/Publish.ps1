[CmdletBinding()]
param(
    [ValidateSet('stable','beta')][string]$Channel = 'beta',
    [string]$Version = '0.1.1',
    [switch]$SkipTests,
    [switch]$Sign,
    # Keep earlier builds in artifacts/. By default they are removed: every
    # bundle is a few hundred MB, and a folder of stale ones is how an old
    # build ends up attached to a release.
    [switch]$KeepOld
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Version must be numeric, e.g. 0.1.0.' }
Push-Location $repo
try {
    if (-not $SkipTests) { & "$PSScriptRoot/Build.ps1" -Test }
    # One folder per channel and version, replaced on each run, so its path is
    # predictable: artifacts/beta-0.1.0 holds the zips, the installer and the MSIX.
    $artifacts = Join-Path $repo 'artifacts'
    $artifactRoot = Join-Path $artifacts ($Channel + '-' + $Version)
    if (Test-Path -LiteralPath $artifacts) {
        $old = @(Get-ChildItem -LiteralPath $artifacts -Force | Where-Object { $KeepOld -eq $false -or $_.FullName -eq $artifactRoot })
        if ($old) {
            $bytes = ($old | ForEach-Object { if ($_.PSIsContainer) { Get-ChildItem -LiteralPath $_.FullName -Recurse -File -Force | Measure-Object Length -Sum | ForEach-Object Sum } else { $_.Length } } | Measure-Object -Sum).Sum
            $old | Remove-Item -Recurse -Force
            Write-Host ('Removed {0} earlier build item(s) from artifacts ({1:N1} GB).' -f $old.Count, ($bytes / 1GB))
        }
    }
    $cli = Join-Path $artifactRoot 'cli'
    $desktop = Join-Path $artifactRoot 'desktop'
    New-Item -ItemType Directory -Path $cli,$desktop -Force | Out-Null
    # WMI currently requires built-in COM. Ship a self-contained managed engine
    # until that adapter has a verified NativeAOT replacement. ReadyToRun instead:
    # precompiled code, so the engine at sign-in, the CLI in a script and the
    # panel's first opening skip most of their JIT time.
    # The compiler server outlives the test build and can still hold an obj
    # file when the first publish starts: CS2012, 'being used by another process'.
    & dotnet build-server shutdown *> $null
    foreach ($project in @('DispCtrl.Cli','DispCtrl.Engine')) {
        & dotnet publish "src/$project/$project.csproj" -c Release -r win-x64 --self-contained true -p:PublishAot=false -p:PublishTrimmed=false -p:PublishReadyToRun=true -p:Version=$Version -o $cli
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
    }
    # The app first, into an empty folder, and only then whatever the CLI and
    # engine add that the app does not already have. The other way round, the
    # app's publish skipped shared files the CLI's publish had just written
    # (it keeps a newer destination), the app ran with the CLI's copies, and it
    # crashed at start: "Cannot locate resource ...themeresources.xaml".
    #
    # The app's intermediates go first. MSBuild reuses a stale generated PRI and
    # compiled XAML across incremental builds, and the app then dies at start
    # with that same missing-themeresources parse error - in bin as well as in
    # the published folder. Deleting obj is the only thing that clears it;
    # dotnet clean leaves the PRI behind.
    Remove-Item -Recurse -Force 'src/DispCtrl.App/obj' -ErrorAction SilentlyContinue
    # An engine elsewhere on this PC - an installed copy - can start the quick
    # panel from the path this repository's app last recorded, which is its
    # bin folder. That locks DispCtrl.App.exe and the publish fails (MSB3027).
    # The app may be closed outright; only the engine must be stopped gently.
    Get-Process DispCtrl.App -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase) } |
        Stop-Process -Force -ErrorAction SilentlyContinue
    & dotnet publish src/DispCtrl.App/DispCtrl.App.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishAot=false -p:PublishTrimmed=false -p:PublishReadyToRun=true -p:Version=$Version -o $desktop
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
    Get-ChildItem -LiteralPath $cli -Recurse -File | ForEach-Object {
        $target = Join-Path $desktop $_.FullName.Substring($cli.Length).TrimStart([char]92, [char]47)
        if (Test-Path -LiteralPath $target) { return }
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target
    }
    # A release that cannot start must not be packaged: launch it and watch.
    # Skipped on CI runners, which may have no interactive desktop.
    if (-not $env:CI) {
        # Only one DispCtrl app runs at a time: a second launch hands its window
        # to the one already running and exits with code 0. So any other copy -
        # the Store's, an installed one - has to be out of the way first, or the
        # probe reports that handover as a failure to start. The app may be
        # closed outright; the engine is left alone.
        $others = @(Get-Process DispCtrl.App -ErrorAction SilentlyContinue)
        if ($others.Count -gt 0) {
            Write-Host "  Closing the DispCtrl app already running ($(@($others | ForEach-Object { $_.Path } | Select-Object -Unique) -join ', ')) so the published one can be started on its own."
            $others | Stop-Process -Force -ErrorAction SilentlyContinue
            $others | ForEach-Object { $_.WaitForExit(5000) | Out-Null }
        }
        $probe = Start-Process -FilePath (Join-Path $desktop 'DispCtrl.App.exe') -PassThru
        Start-Sleep -Seconds 8
        if ($probe.HasExited) {
            $still = @(Get-Process DispCtrl.App -ErrorAction SilentlyContinue)
            if ($probe.ExitCode -eq 0 -and $still.Count -gt 0) {
                throw "The published app handed over to another DispCtrl that started meanwhile ($(@($still | ForEach-Object { $_.Path } | Select-Object -Unique) -join ', ')) and exited. Close it and run the release again."
            }
            throw "The published app exits at start (code $($probe.ExitCode)); see app-crash.log in %LOCALAPPDATA%\DispCtrl."
        }
        Stop-Process -Id $probe.Id -Force -ErrorAction SilentlyContinue
    }
    if ($Sign) {
        # DispCtrl's own binaries only. The runtime's are Microsoft-signed
        # already, and the taskbar-glass helper is pinned by revision: Explorer
        # keeps the one it loaded, so its bytes must not change after the build.
        $own = foreach ($folder in @($cli,$desktop)) {
            Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Name -like 'DispCtrl*.exe' -or $_.Name -eq 'dispctrl.exe' -or $_.Name -like 'DispCtrl.*.dll' }
        }
        & "$PSScriptRoot/Sign.ps1" -Path @($own.FullName)
    }
    # A source download from GitHub (Download ZIP) has no .git: record that
    # rather than letting git print a fatal error per bundle.
    $commit = 'unknown'; $modified = $null
    if (Test-Path -LiteralPath (Join-Path $repo '.git')) {
        $commit = (& git rev-parse HEAD 2>$null)
        $modified = [bool](& git status --porcelain 2>$null)
    }
    foreach ($folder in @($cli,$desktop)) {
        # The Markdown and the examples; not docs/design (internal notes), and
        # not the images, which live with the website in site/assets.
        New-Item -ItemType Directory -Path (Join-Path $folder 'docs') -Force | Out-Null
        Copy-Item -Path (Join-Path $repo 'docs/*.md') -Destination (Join-Path $folder 'docs')
        Copy-Item -LiteralPath (Join-Path $repo 'docs/examples') -Destination (Join-Path $folder 'examples') -Recurse
        if (Test-Path (Join-Path $repo 'LICENSE')) { Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $folder }
        $manifest = [ordered]@{ product='DispCtrl'; version=$Version; channel=$Channel; architecture='x64'; commit=$commit; modifiedSources=$modified; builtUtc=[DateTimeOffset]::UtcNow.ToString('O'); nativeAot=$false; readyToRun=$true }
        $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'build-info.json') -Encoding utf8
    }
    foreach ($kind in @('cli','desktop')) {
        Compress-Archive -Path (Join-Path $artifactRoot "$kind/*") -DestinationPath (Join-Path $artifactRoot "DispCtrl-$Version-$Channel-win-x64-$kind.zip") -CompressionLevel Optimal
    }
    Get-ChildItem -LiteralPath $artifactRoot -Filter '*.zip' | Get-FileHash -Algorithm SHA256 |
        ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))" } |
        Set-Content -LiteralPath (Join-Path $artifactRoot 'SHA256SUMS.txt') -Encoding ascii
    Write-Host "Artifacts: $artifactRoot"
    return $artifactRoot
} finally { Pop-Location }
