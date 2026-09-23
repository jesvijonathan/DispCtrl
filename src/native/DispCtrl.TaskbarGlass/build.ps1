param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$sourceRoot = $PSScriptRoot
$compiler = if ($env:DISPCTRL_CXX) { $env:DISPCTRL_CXX } elseif ($env:DISPLCTRL_CXX) { $env:DISPLCTRL_CXX } elseif (Get-Command g++.exe -ErrorAction SilentlyContinue) { (Get-Command g++.exe).Source } else { 'C:\toolchains\mingw64\bin\g++.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Taskbar glass requires an x64 MinGW-w64 C++ compiler. Set DISPCTRL_CXX to g++.exe.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$source = Join-Path $sourceRoot 'TaskbarGlass.cpp'
$header = Join-Path $sourceRoot 'CompositionAbi.h'
$sha = New-Object Security.Cryptography.SHA256Managed
$revision = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes((Get-Content -LiteralPath $source,$header,$PSCommandPath -Raw) -join "`n"))).Replace('-', '')).Substring(0,16)
# Explorer pins the helper. Versioned names allow rebuild/restart without
# restarting Explorer or replacing a DLL mapped into another process.
$target = Join-Path $OutputDirectory "DispCtrl.TaskbarGlass.$revision.dll"
if (-not (Test-Path -LiteralPath $target)) {
    $buildDefine = '-DGLASS_REVISION=' + $revision
    & $compiler '-std=c++20' '-O2' '-shared' '-static' '-static-libgcc' '-static-libstdc++' '-DUNICODE' '-D_UNICODE' $buildDefine $source '-o' $target '-lole32' '-loleaut32' '-lruntimeobject' '-luuid' '-ld2d1' '-Wl,--no-insert-timestamp'
    if ($LASTEXITCODE -ne 0) { throw "Native taskbar helper build failed: $LASTEXITCODE" }
}
Set-Content -LiteralPath (Join-Path $OutputDirectory 'TaskbarGlass.version') -Value ([IO.Path]::GetFileName($target)) -NoNewline
Write-Output "Taskbar glass: $revision"
