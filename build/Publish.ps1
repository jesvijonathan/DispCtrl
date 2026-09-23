[CmdletBinding()]
param([ValidateSet('stable','beta')][string]$Channel = 'beta', [string]$Version = '0.1.0', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Version must be numeric, e.g. 0.1.0.' }
Push-Location $repo
try {
    if (-not $SkipTests) { & "$PSScriptRoot/Build.ps1" -Test }
    $artifactRoot = Join-Path $repo ('artifacts/' + $Channel + '-' + $Version + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
    $cli = Join-Path $artifactRoot 'cli'
    $desktop = Join-Path $artifactRoot 'desktop'
    New-Item -ItemType Directory -Path $cli,$desktop -Force | Out-Null
    # WMI currently requires built-in COM. Ship a self-contained managed engine
    # until that adapter has a verified NativeAOT replacement. ReadyToRun instead:
    # precompiled code, so the engine at sign-in, the CLI in a script and the
    # panel's first opening skip most of their JIT time.
    foreach ($project in @('DispCtrl.Cli','DispCtrl.Engine')) {
        & dotnet publish "src/$project/$project.csproj" -c Release -r win-x64 --self-contained true -p:PublishAot=false -p:PublishTrimmed=false -p:PublishReadyToRun=true -p:Version=$Version -o $cli
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
    }
    Copy-Item -Path "$cli/*" -Destination $desktop -Recurse -Force
    & dotnet publish src/DispCtrl.App/DispCtrl.App.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishAot=false -p:PublishTrimmed=false -p:PublishReadyToRun=true -p:Version=$Version -o $desktop
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
    foreach ($folder in @($cli,$desktop)) {
        Copy-Item -LiteralPath (Join-Path $repo 'docs') -Destination $folder -Recurse
        Copy-Item -LiteralPath (Join-Path $repo 'examples') -Destination $folder -Recurse
        if (Test-Path (Join-Path $repo 'LICENSE')) { Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $folder }
        $manifest = [ordered]@{ product='DispCtrl'; version=$Version; channel=$Channel; architecture='x64'; commit=(& git rev-parse HEAD); modifiedSources=[bool](& git status --porcelain); builtUtc=[DateTimeOffset]::UtcNow.ToString('O'); nativeAot=$false; readyToRun=$true }
        $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'build-info.json') -Encoding utf8
    }
    foreach ($kind in @('cli','desktop')) {
        Compress-Archive -Path (Join-Path $artifactRoot "$kind/*") -DestinationPath (Join-Path $artifactRoot "DispCtrl-$Version-$Channel-win-x64-$kind.zip") -CompressionLevel Optimal
    }
    Get-ChildItem -LiteralPath $artifactRoot -Filter '*.zip' | Get-FileHash -Algorithm SHA256 |
        ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))" } |
        Set-Content -LiteralPath (Join-Path $artifactRoot 'SHA256SUMS.txt') -Encoding ascii
    Write-Output "Artifacts: $artifactRoot"
    return $artifactRoot
} finally { Pop-Location }
