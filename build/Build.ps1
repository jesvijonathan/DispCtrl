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
        foreach ($project in @('controlcheck','presetverify')) {
            & dotnet run --project "tools/$project" -c $Configuration
            if ($LASTEXITCODE -ne 0) { throw "Checks failed: $project" }
        }
    }
} finally { Pop-Location }
