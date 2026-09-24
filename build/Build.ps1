[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$Test)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    foreach ($project in @('DispCtrl.Cli','DispCtrl.Engine','DispCtrl.App')) {
        & dotnet build "src/$project/$project.csproj" -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }
    if ($Test) {
        # The same set as dev.ps1 test, less presetcheck, which needs monitors.
        $checks = @(
            @('controlcheck', @()),
            @('presetverify', @()),
            @('devicecheck', @('validate', 'devices')),
            @('devicecheck', @('selftest'))
        )
        foreach ($check in $checks) {
            & dotnet run --project "tools/$($check[0])" -c $Configuration -- @($check[1])
            if ($LASTEXITCODE -ne 0) { throw "Checks failed: $($check[0]) $($check[1] -join ' ')" }
        }
    }
} finally { Pop-Location }
