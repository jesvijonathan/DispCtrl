<#
.SYNOPSIS
    One entry point for working on DispCtrl: check the machine, set it up, build,
    test, run and package. Run with no command for a menu (build.cmd does that
    on a double-click).

.EXAMPLE
    ./build/dev.ps1 doctor              # what is installed, what is missing
    ./build/dev.ps1 setup -Install      # fetch the missing tools (asks winget for system ones)
    ./build/dev.ps1 build               # stops the engine gracefully, builds, restarts it (-Rebuild: from clean)
    ./build/dev.ps1 test -Hardware      # hardware-free checks, plus the ones that read monitors
    ./build/dev.ps1 run engine          # or: app, panel, cli <arguments>
    ./build/dev.ps1 release             # zips, installer, MSIX and notes in artifacts/
    ./build/dev.ps1 options -Configuration Debug -Channel stable -NoNative
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('menu','doctor','setup','build','test','run','publish','installer','package','release','clean','options')]
    [string]$Command = 'menu',
    [Parameter(Position = 1)][string]$Target,
    [ValidateSet('Debug','Release')][string]$Configuration,
    [ValidateSet('beta','stable')][string]$Channel,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    # setup: allow winget to install the .NET SDK and MinGW-w64 system-wide.
    [switch]$Install,
    # test: also run presetcheck, which reads the monitors actually attached.
    [switch]$Hardware,
    # Build without the native taskbar-glass helper (no MinGW needed); remembered.
    [switch]$NoNative,
    # Build the native helper again after -NoNative; remembered.
    [switch]$Native,
    # build: leave the engine stopped afterwards.
    [switch]$NoRestart,
    # publish/installer/package/release: sign (needs DISPCTRL_SIGN_PFX).
    [switch]$Sign,
    # clean: do not ask.
    [switch]$Yes,
    # build: remove every bin and obj folder first, for a build with nothing left over.
    [switch]$Rebuild,
    # publish/release: keep earlier builds in artifacts instead of clearing them.
    [switch]$Keep,
    # build/publish/release: open the output folder in Explorer when done.
    [switch]$Open,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$localFile = Join-Path $PSScriptRoot 'local.json'
$toolsDir = Join-Path $repo '.tools'
$framework = 'net10.0-windows10.0.26100.0'

# ---------------------------------------------------------------- options

function Read-Options {
    $defaults = [ordered]@{ configuration = 'Release'; channel = 'beta'; version = ''; cxx = ''; iscc = ''; skipNative = $false }
    if (Test-Path -LiteralPath $localFile) {
        try {
            $saved = Get-Content -LiteralPath $localFile -Raw | ConvertFrom-Json
            foreach ($p in $saved.PSObject.Properties) { if ($defaults.Contains($p.Name)) { $defaults[$p.Name] = $p.Value } }
        } catch { Write-Warning "Ignoring unreadable $localFile." }
    }
    if (-not $defaults.version) {
        $defaults.version = ([xml](Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw)).Project.PropertyGroup.DispCtrlVersion |
            Where-Object { $_ } | Select-Object -First 1
    }
    $defaults
}

function Save-Options($options) {
    [pscustomobject]$options | ConvertTo-Json | Set-Content -LiteralPath $localFile -Encoding utf8
}

$options = Read-Options
if ($Configuration) { $options.configuration = $Configuration }
if ($Channel) { $options.channel = $Channel }
if ($Version) { $options.version = $Version }
if ($NoNative) { $options.skipNative = $true }
if ($Native) { $options.skipNative = $false }

# ---------------------------------------------------------------- tools

function First-Existing([string[]]$paths) {
    foreach ($p in $paths) { if ($p -and (Test-Path -LiteralPath $p)) { return (Resolve-Path -LiteralPath $p).Path } }
    return $null
}

function Find-Cxx {
    $onPath = Get-Command g++.exe -ErrorAction SilentlyContinue
    $winget = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages') -Directory -Filter 'BrechtSanders.WinLibs*' -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'mingw64\bin\g++.exe' }
    First-Existing (@($env:DISPCTRL_CXX, $options.cxx, $(if ($onPath) { $onPath.Source }), 'C:\toolchains\mingw64\bin\g++.exe',
        (Join-Path $toolsDir 'mingw64\bin\g++.exe'), 'C:\msys64\ucrt64\bin\g++.exe') + @($winget))
}

function Find-Iscc {
    $candidates = foreach ($major in 7, 6) {
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup $major\ISCC.exe")
        (Join-Path $env:ProgramFiles "Inno Setup $major\ISCC.exe")
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup $major\ISCC.exe")
    }
    First-Existing (@($env:DISPCTRL_ISCC, $options.iscc, (Join-Path $toolsDir 'innosetup\tools\ISCC.exe')) + @($candidates))
}

function Find-SdkTool([string]$name) {
    $roots = @((Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'), (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools'))
    $found = foreach ($root in $roots) {
        if (Test-Path -LiteralPath $root) { Get-ChildItem -LiteralPath $root -Filter $name -Recurse -ErrorAction SilentlyContinue | Where-Object FullName -match '[\\/]x64[\\/]' }
    }
    ($found | Sort-Object FullName -Descending | Select-Object -First 1).FullName
}

function Get-Tools {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    $sdks = if ($dotnet) { @(& dotnet --list-sdks 2>$null) } else { @() }
    [ordered]@{
        windows  = [Environment]::OSVersion.Version
        dotnet   = $(if ($dotnet) { $dotnet.Source })
        sdk10    = ($sdks | Where-Object { $_ -match '^10\.' } | Select-Object -Last 1)
        pwsh     = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
        git      = (Get-Command git -ErrorAction SilentlyContinue).Source
        winget   = (Get-Command winget -ErrorAction SilentlyContinue).Source
        cxx      = Find-Cxx
        makeappx = Find-SdkTool 'makeappx.exe'
        signtool = Find-SdkTool 'signtool.exe'
        iscc     = Find-Iscc
    }
}

function Use-Tools($tools) {
    # Environment variables are MSBuild properties too, so this reaches the
    # engine project's SkipTaskbarGlass switch through Build.ps1 and Publish.ps1.
    if ($tools.cxx) { $env:DISPCTRL_CXX = $tools.cxx }
    if ($tools.iscc) { $env:DISPCTRL_ISCC = $tools.iscc }
    if ($options.skipNative) { $env:SkipTaskbarGlass = 'true' } else { Remove-Item Env:SkipTaskbarGlass -ErrorAction SilentlyContinue }
}

function Write-Check([string]$name, $ok, [string]$detail, [string]$fix, [switch]$Optional) {
    $mark = if ($ok) { '[ok]  ' } elseif ($Optional) { '[--]  ' } else { '[!!]  ' }
    $colour = if ($ok) { 'Green' } elseif ($Optional) { 'DarkYellow' } else { 'Red' }
    Write-Host $mark -ForegroundColor $colour -NoNewline
    Write-Host ('{0,-22} {1}' -f $name, $detail)
    if (-not $ok -and $fix) { Write-Host ('      {0}' -f $fix) -ForegroundColor DarkGray }
}

function Invoke-Doctor {
    $t = Get-Tools
    Write-Host "`nDispCtrl developer check  ($repo)`n"
    Write-Check 'Windows 11' ($t.windows.Build -ge 22000) "build $($t.windows.Build)" 'DispCtrl targets Windows 11; build.sh compiles the libraries elsewhere.'
    Write-Check '.NET 10 SDK' ([bool]$t.sdk10) $(if ($t.sdk10) { $t.sdk10 } else { 'not found' }) 'dev.ps1 setup -Install, or https://dotnet.microsoft.com/download'
    Write-Check 'MinGW-w64 g++' ([bool]$t.cxx -or $options.skipNative) $(if ($t.cxx) { $t.cxx } elseif ($options.skipNative) { 'not needed: native helper off (-NoNative)' } else { 'not found' }) 'dev.ps1 setup -Install (WinLibs), or set DISPCTRL_CXX, or build with -NoNative'
    Write-Check 'PowerShell 7' ([bool]$t.pwsh) $(if ($t.pwsh) { $t.pwsh } else { 'not found' }) 'winget install Microsoft.PowerShell' -Optional
    Write-Check 'git' ([bool]$t.git) $(if ($t.git) { $t.git } else { 'not found' }) 'winget install Git.Git' -Optional
    Write-Check 'MakeAppx (MSIX)' ([bool]$t.makeappx) $(if ($t.makeappx) { $t.makeappx } else { 'not found' }) 'dev.ps1 setup restores it from NuGet' -Optional
    Write-Check 'signtool' ([bool]$t.signtool) $(if ($t.signtool) { $t.signtool } else { 'not found' }) 'dev.ps1 setup restores it from NuGet' -Optional
    Write-Check 'Inno Setup (installer)' ([bool]$t.iscc) $(if ($t.iscc) { $t.iscc } else { 'not found' }) 'dev.ps1 setup fetches a portable copy into .tools' -Optional
    $engine = @(Get-RepoProcess 'DispCtrl.Engine')
    Write-Host ''
    Write-Host ("Options: {0}, {1} {2}, native helper {3}  (change with: dev.ps1 options ...)" -f $options.configuration, $options.channel, $options.version, $(if ($options.skipNative) { 'off' } else { 'on' }))
    if ($engine) { Write-Host "Engine from this repository is running (pid $($engine[0].Id)); build stops and restarts it." }
    $t
}

# ---------------------------------------------------------------- setup

function Save-NuGetTool([string]$package, [string]$folder) {
    $target = Join-Path $toolsDir $folder
    if (Test-Path -LiteralPath $target) { return $target }
    New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
    $zip = Join-Path $toolsDir "$folder.zip"
    Write-Host "Fetching $package into .tools\$folder ..."
    Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/$package" -OutFile $zip -UseBasicParsing
    Expand-Archive -LiteralPath $zip -DestinationPath $target -Force
    Remove-Item -LiteralPath $zip
    $target
}

function Invoke-Winget([string]$id) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) { throw "winget is not available; install $id by hand." }
    Write-Host "winget install $id"
    & winget install --id $id --exact --source winget --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) { Write-Warning "winget returned $LASTEXITCODE for $id." }
}

function Invoke-Setup {
    $t = Get-Tools
    if (-not $t.sdk10) {
        if ($Install) { Invoke-Winget 'Microsoft.DotNet.SDK.10' } else { Write-Warning '.NET 10 SDK missing: rerun with -Install, or install it from https://dotnet.microsoft.com/download.' }
    }
    if (-not $t.cxx -and -not $options.skipNative) {
        if ($Install) { Invoke-Winget 'BrechtSanders.WinLibs.POSIX.UCRT' }
        else { Write-Warning 'MinGW-w64 missing: rerun with -Install, set DISPCTRL_CXX, or build without the glass helper using -NoNative.' }
    }
    if (-not $t.iscc) { $null = Save-NuGetTool 'Tools.InnoSetup' 'innosetup' }
    # A new install lands on PATH only for new shells; look again rather than
    # telling the person to reopen the window.
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
    $t = Get-Tools
    if ($t.sdk10) {
        Write-Host 'Restoring packages (this also brings MakeAppx and signtool) ...'
        foreach ($project in Get-ChildItem (Join-Path $repo 'src'), (Join-Path $repo 'tools') -Recurse -Filter *.csproj) {
            & dotnet restore $project.FullName --nologo -v q
            if ($LASTEXITCODE -ne 0) { throw "Restore failed: $($project.Name)" }
        }
        $t = Get-Tools
    }
    $options.cxx = [string]$t.cxx
    $options.iscc = [string]$t.iscc
    Save-Options $options
    Write-Host "Saved tool locations to build\local.json."
    $null = Invoke-Doctor
}

# ---------------------------------------------------------------- processes

function Get-RepoProcess([string]$name) {
    # The whole repository: a copy left running from artifacts\ blocks a clean
    # as surely as one from a bin folder.
    $root = $repo.TrimEnd('\') + '\'
    @(Get-Process $name -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) })
}

function Stop-RepoProcesses {
    # The engine gracefully: killed, it leaves a hidden taskbar parked off-screen.
    # The window may be killed outright. Returns the engine path it stopped.
    $stopped = $null
    foreach ($engine in Get-RepoProcess 'DispCtrl.Engine') {
        # Read before stopping: Path comes from the main module, and an exited
        # process has none, so afterwards it is empty and nothing restarts.
        $path = $engine.Path
        Write-Host "Stopping the engine (pid $($engine.Id)) ..."
        Start-Process -FilePath $path -ArgumentList 'stop' -Wait -WindowStyle Hidden
        if (-not $engine.WaitForExit(20000)) { throw 'The engine did not stop within 20 s; quit it from the tray icon, then build again.' }
        $stopped = $path
    }
    Get-RepoProcess 'DispCtrl.App' | Stop-Process -Force
    Get-RepoProcess 'dispctrl' | Stop-Process -Force
    $stopped
}

function Start-Engine([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { Write-Warning "No engine at $path."; return }
    $task = $null
    try { $task = Get-ScheduledTask -TaskName 'DispCtrl.Engine' -ErrorAction Stop } catch { }
    if ($task -and $task.Actions[0].Execute.Trim('"') -eq $path) {
        Start-ScheduledTask -TaskName 'DispCtrl.Engine'
        Write-Host 'Engine restarted through its sign-in task.'
    } else {
        Start-Process -FilePath $path -ArgumentList 'run'
        Write-Host "Engine started: $path"
    }
}

function Output-Of([string]$project, [string]$exe) {
    Join-Path $repo "src\$project\bin\$($options.configuration)\$framework\win-x64\$exe"
}

# ---------------------------------------------------------------- commands

function Assert-Ready($tools) {
    if (-not $tools.sdk10) { throw '.NET 10 SDK missing. Run: dev.ps1 setup -Install' }
    if (-not $tools.cxx -and -not $options.skipNative) { throw 'MinGW-w64 g++ missing. Run: dev.ps1 setup -Install, or build with -NoNative.' }
    Use-Tools $tools
}

function Remove-BuildOutput {
    # bin and obj of every project: what MSBuild never removes by itself.
    $folders = @(Get-ChildItem (Join-Path $repo 'src'), (Join-Path $repo 'tools') -Directory -Recurse -Include bin, obj -ErrorAction SilentlyContinue |
        Where-Object { $_.Parent.GetFiles('*.csproj').Count -gt 0 })
    foreach ($folder in $folders) { Remove-Item -LiteralPath $folder.FullName -Recurse -Force }
    Write-Host "Removed $($folders.Count) bin/obj folder(s)."
}

function Write-Outputs([string]$title, [string[]]$paths, [string]$folder) {
    Write-Host ''
    Write-Host $title -ForegroundColor Green
    foreach ($path in $paths) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $item = Get-Item -LiteralPath $path
            Write-Host ('  {0,-46} {1,8:N1} MB   {2}' -f $item.Name, ($item.Length / 1MB), $item.DirectoryName)
        } else { Write-Host "  missing: $path" -ForegroundColor Yellow }
    }
    if ($folder) {
        Write-Host "  Folder: $folder"
        if ($Open) { Start-Process explorer.exe $folder }
    }
}

function Invoke-Build {
    Assert-Ready (Get-Tools)
    $was = Stop-RepoProcesses
    try {
        if ($Rebuild) { Remove-BuildOutput }
        & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $options.configuration
    } finally {
        if ($was -and -not $NoRestart) { Start-Engine (Output-Of 'DispCtrl.Engine' 'DispCtrl.Engine.exe') }
    }
    $app = Output-Of 'DispCtrl.App' 'DispCtrl.App.exe'
    Write-Outputs "Built ($($options.configuration)):" @($app, (Output-Of 'DispCtrl.Engine' 'DispCtrl.Engine.exe'), (Output-Of 'DispCtrl.Cli' 'dispctrl.exe')) (Split-Path -Parent $app)
}

function Invoke-Test {
    Assert-Ready (Get-Tools)
    Push-Location $repo
    try {
        $checks = @(
            @('controlcheck', @()),
            @('presetverify', @()),
            # The index is regenerated after each merge, so a new device folder
            # is valid without it: only the layout and the rules are checked.
            @('devicecheck', @('validate', 'devices')),
            @('devicecheck', @('selftest'))
        )
        if ($Hardware) { $checks += , @('presetcheck', @()) }
        $failed = @()
        foreach ($check in $checks) {
            Write-Host "`n== $($check[0]) $($check[1] -join ' ')" -ForegroundColor Cyan
            & dotnet run --project "tools/$($check[0])" -c $options.configuration -- @($check[1])
            if ($LASTEXITCODE -ne 0) { $failed += "$($check[0]) (exit $LASTEXITCODE)" }
        }
        if ($failed) { throw "Failed: $($failed -join ', ')" }
        Write-Host "`nAll checks passed." -ForegroundColor Green
        if (-not $Hardware) { Write-Host 'presetcheck reads the attached monitors; add -Hardware to run it too.' }
    } finally { Pop-Location }
}

function Invoke-Run {
    $what = if ($Target) { $Target } else { 'app' }
    switch ($what) {
        'engine' {
            if (Get-RepoProcess 'DispCtrl.Engine') { Write-Host 'The engine is already running.'; return }
            Start-Engine (Output-Of 'DispCtrl.Engine' 'DispCtrl.Engine.exe')
        }
        'app' { Start-Process -FilePath (Output-Of 'DispCtrl.App' 'DispCtrl.App.exe') }
        'panel' { Start-Process -FilePath (Output-Of 'DispCtrl.App' 'DispCtrl.App.exe') -ArgumentList '--panel' }
        'cli' {
            & (Output-Of 'DispCtrl.Cli' 'dispctrl.exe') @Rest
            exit $LASTEXITCODE
        }
        default { throw "run what? engine, app, panel or cli <arguments>" }
    }
}

function Invoke-Publish {
    Assert-Ready (Get-Tools)
    if ($options.skipNative) { Write-Warning 'Publishing without the taskbar-glass helper (-NoNative): the engine will have no glass.' }
    # Publish runs the tests, which build into the same bin folders the engine
    # runs from: stopped gracefully first, as for a build, or the build fails
    # on a locked file.
    $was = Stop-RepoProcesses
    try { $null = & (Join-Path $PSScriptRoot 'Publish.ps1') -Version $options.version -Channel $options.channel -Sign:$Sign -KeepOld:$Keep | Out-Host }
    finally { if ($was -and -not $NoRestart) { Start-Engine (Output-Of 'DispCtrl.Engine' 'DispCtrl.Engine.exe') } }
    $bundle = Bundle-Path
    if (-not (Test-Path -LiteralPath (Join-Path $bundle 'desktop'))) { throw 'Publish produced no bundle.' }
    $bundle
}

function Bundle-Path { Join-Path $repo "artifacts\$($options.channel)-$($options.version)" }

function Latest-Bundle {
    $bundle = Bundle-Path
    if (Test-Path -LiteralPath (Join-Path $bundle 'desktop')) { return $bundle }
    Write-Host 'No published bundle for these options yet; publishing first.'
    Invoke-Publish
}

function Invoke-Installer([string]$bundle) {
    $tools = Get-Tools
    if (-not $tools.iscc) { throw 'Inno Setup missing. Run: dev.ps1 setup' }
    Use-Tools $tools
    if (-not $bundle) { $bundle = Latest-Bundle }
    & (Join-Path $PSScriptRoot 'Installer.ps1') -DesktopDirectory (Join-Path $bundle 'desktop') -Version $options.version -Channel $options.channel -Iscc $tools.iscc -OutputDirectory $bundle -Sign:$Sign
}

function Invoke-Package([string]$bundle) {
    if (-not $bundle) { $bundle = Latest-Bundle }
    # The Store identity in the manifest template, unless the environment names another.
    $identity = @{}
    if ($env:STORE_IDENTITY) { $identity.IdentityName = $env:STORE_IDENTITY }
    if ($env:STORE_PUBLISHER) { $identity.Publisher = $env:STORE_PUBLISHER }
    & (Join-Path $PSScriptRoot 'Package.ps1') -DesktopDirectory (Join-Path $bundle 'desktop') @identity -Version "$($options.version).0" -Sign:$Sign
}

function Invoke-Release {
    $bundle = Invoke-Publish
    Invoke-Installer $bundle
    Invoke-Package $bundle
    $notes = Join-Path $bundle 'RELEASE-NOTES.md'
    & (Join-Path $PSScriptRoot 'ReleaseNotes.ps1') -Version $options.version -ArtifactsDirectory $bundle | Set-Content -LiteralPath $notes -Encoding utf8
    $files = @(Get-ChildItem -LiteralPath $bundle -File | Sort-Object Name | ForEach-Object FullName)
    Write-Outputs "Release $($options.version) ($($options.channel)):" $files $bundle
}

function Invoke-Clean {
    $targets = @(Get-ChildItem (Join-Path $repo 'src'), (Join-Path $repo 'tools') -Directory -Recurse -Include bin, obj -ErrorAction SilentlyContinue |
        Where-Object { $_.Parent.GetFiles('*.csproj').Count -gt 0 })
    $artifacts = Join-Path $repo 'artifacts'
    Write-Host "Removes $($targets.Count) bin/obj folders and $artifacts. Keeps .tools and build\local.json."
    if (-not $Yes) {
        $answer = Read-Host 'Continue? [y/N]'
        if ($answer -notmatch '^(y|yes)$') { return }
    }
    $was = Stop-RepoProcesses
    if ($was) { Write-Warning 'The engine was stopped; it has nothing to run until the next build.' }
    $held = @()
    foreach ($folder in $targets) { $held += @(Remove-Tree $folder.FullName) }
    if (Test-Path -LiteralPath $artifacts) { $held += @(Remove-Tree $artifacts) }
    if ($held.Count -gt 0) {
        Write-Warning ("Windows Explorer still has the taskbar glass helper loaded from the build output, so it stays until Explorer restarts (Taskbar page > Restart Windows Explorer, or sign out):`n  " + ($held -join "`n  "))
        Write-Host 'Clean, apart from that.'
    }
    else { Write-Host 'Clean.' }
}

# Removes a folder, and returns the taskbar glass helpers it could not: engines
# before the helper was copied out of bin handed Explorer the file in bin, and
# Explorer keeps a helper mapped until it restarts. Anything else still locked
# is a real failure.
function Remove-Tree([string]$path) {
    try { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop; return @() }
    catch {
        foreach ($file in @(Get-ChildItem -LiteralPath $path -Recurse -File -Force -ErrorAction SilentlyContinue)) {
            try { Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop } catch { }
        }
        $left = @(Get-ChildItem -LiteralPath $path -Recurse -File -Force -ErrorAction SilentlyContinue)
        $other = @($left | Where-Object { $_.Name -notlike 'DispCtrl.TaskbarGlass.*.dll' })
        if ($other.Count -gt 0) { throw "Could not remove $($other[0].FullName): it is in use." }
        # Empty folders go deepest first; the ones holding a helper stay.
        Get-ChildItem -LiteralPath $path -Recurse -Directory -Force -ErrorAction SilentlyContinue |
            Sort-Object { $_.FullName.Length } -Descending |
            Where-Object { -not (Get-ChildItem -LiteralPath $_.FullName -Force) } |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
        return @($left | ForEach-Object FullName)
    }
}

function Invoke-Options {
    Save-Options $options
    Write-Host ("Saved: configuration {0}, channel {1}, version {2}, native helper {3}." -f $options.configuration, $options.channel, $options.version, $(if ($options.skipNative) { 'off' } else { 'on' }))
}

# One build at a time on this machine. Two runs share every obj folder: one
# cleans or rewrites what the other is compiling, and both fail with errors
# that point anywhere but here (a missing R2R file, a locked obj).
function Use-BuildLock([scriptblock]$work) {
    $lock = [Threading.Mutex]::new($false, 'Local\DispCtrl.Build')
    $held = $false
    try { $held = $lock.WaitOne(0) } catch [Threading.AbandonedMutexException] { $held = $true }
    if (-not $held) { $lock.Dispose(); throw 'Another DispCtrl build is running on this PC. Wait for it to finish, then try again.' }
    try { & $work } finally { $lock.ReleaseMutex(); $lock.Dispose() }
}

function Invoke-Menu {
    # Grouped by what a person is trying to do, not by build stage. Shipping is
    # one choice: it asks the channel and version, starts from clean, and ends
    # by naming the files to upload.
    $items = [ordered]@{
        '1' = @('Build', 'compile the CLI, engine and app', { Invoke-Build })
        '2' = @('Test', 'run every check', { Invoke-Test })
        '3' = @('Open the app', '', { $script:Target = 'app'; Invoke-Run })
        '4' = @('Release', 'clean build, tests, installer, zips and MSIX, ready to upload', { Invoke-MenuRelease })
        '5' = @('Start the engine', '', { $script:Target = 'engine'; Invoke-Run })
        '6' = @('Open the quick panel', '', { $script:Target = 'panel'; Invoke-Run })
        '7' = @('Clean', 'remove every bin, obj and artifacts folder', { Invoke-Clean })
        '8' = @('Check this machine', 'and set up missing tools', { if (-not (Invoke-Doctor)) { $script:Install = (Read-Host 'Fetch the missing tools now? [y/N]') -match '^(y|yes)$'; Invoke-Setup } })
        '9' = @('Settings', 'Debug or Release, beta or stable, native helper', { Invoke-MenuSettings })
    }
    $groups = [ordered]@{ 'Everyday' = '1','2','3'; 'Ship' = ,'4'; 'More' = '5','6','7','8','9' }
    while ($true) {
        Write-Host ("`nDispCtrl  ·  {0}  ·  {1} {2}" -f $options.configuration, $options.channel, $options.version) -ForegroundColor Cyan
        foreach ($group in $groups.Keys) {
            Write-Host "  $group" -ForegroundColor DarkGray
            foreach ($key in $groups[$group]) {
                $item = $items[$key]
                Write-Host ('    {0}  {1,-22}{2}' -f $key, $item[0], $item[1])
            }
        }
        Write-Host '    q  Quit'
        $choice = Read-Host 'Choose'
        if ($choice -match '^(q|quit|exit|)$') { return }
        if (-not $items.Contains($choice)) { Write-Host 'Not an option.'; continue }
        try { if ($choice -in '3', '5', '6', '9') { & $items[$choice][2] } else { Use-BuildLock $items[$choice][2] } }
        catch { Write-Host "`n$($_.Exception.Message)" -ForegroundColor Red }
    }
}

function Invoke-MenuRelease {
    $channel = Read-Host "Channel: stable or beta? [stable]"
    $options.channel = if ($channel -match '^b') { 'beta' } else { 'stable' }
    $version = Read-Host "Version? [$($options.version)]"
    if ($version) {
        if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'A version is three numbers: 0.1.0.' }
        $options.version = $version
    }
    $options.configuration = 'Release'
    Write-Host ("`nReleasing {0} {1} from clean. This takes a few minutes." -f $options.channel, $options.version) -ForegroundColor Cyan
    $engine = @(Get-RepoProcess 'DispCtrl.Engine').Count -gt 0
    $script:Yes = $true
    Invoke-Clean
    Invoke-Release
    if ($engine -and -not @(Get-RepoProcess 'DispCtrl.Engine').Count) { Start-Engine (Output-Of 'DispCtrl.Engine' 'DispCtrl.Engine.exe') }
    $bundle = Bundle-Path
    Write-Host "`nReady to upload:" -ForegroundColor Green
    foreach ($pattern in '*.msix', '*-setup.exe', '*.zip') {
        Get-ChildItem -LiteralPath $bundle -Filter $pattern -File | ForEach-Object {
            $what = switch -Wildcard ($_.Name) { '*.msix' { 'Microsoft Store (Partner Center > Packages)' } '*setup.exe' { 'GitHub release: installer' } default { 'GitHub release: portable' } }
            Write-Host ('  {0,-46} {1}' -f $_.Name, $what)
        }
    }
    Write-Host "  in $bundle"
}

function Invoke-MenuSettings {
    while ($true) {
        Write-Host ''
        Write-Host ("  1  Configuration   {0}" -f $options.configuration)
        Write-Host ("  2  Channel         {0}" -f $options.channel)
        Write-Host ("  3  Native helper   {0}" -f $(if ($options.skipNative) { 'off' } else { 'on' }))
        Write-Host '  b  Back'
        switch (Read-Host 'Change') {
            '1' { $options.configuration = if ($options.configuration -eq 'Release') { 'Debug' } else { 'Release' }; Invoke-Options }
            '2' { $options.channel = if ($options.channel -eq 'beta') { 'stable' } else { 'beta' }; Invoke-Options }
            '3' { $options.skipNative = -not $options.skipNative; Invoke-Options }
            default { return }
        }
    }
}

if ($Command -in 'build', 'test', 'publish', 'installer', 'package', 'release', 'clean') {
    Use-BuildLock {
        switch ($Command) {
            'build'     { Invoke-Build }
            'test'      { Invoke-Test }
            'publish'   { Invoke-Publish }
            'installer' { Invoke-Installer $null }
            'package'   { Invoke-Package $null }
            'release'   { Invoke-Release }
            'clean'     { Invoke-Clean }
        }
    }
    return
}

switch ($Command) {
    'menu'      { Invoke-Menu }
    'doctor'    { $null = Invoke-Doctor }
    'setup'     { Invoke-Setup }
    'build'     { Invoke-Build }
    'test'      { Invoke-Test }
    'run'       { Invoke-Run }
    'publish'   { Invoke-Publish }
    'installer' { Invoke-Installer $null }
    'package'   { Invoke-Package $null }
    'release'   { Invoke-Release }
    'clean'     { Invoke-Clean }
    'options'   { Invoke-Options }
}
