[CmdletBinding()]
param([Parameter(Mandatory)][string[]]$Path)
# Authenticode-signs files with the certificate in DISPCTRL_SIGN_PFX (a .pfx
# path) and DISPCTRL_SIGN_PASSWORD. Kept out of parameters so a password never
# appears on a command line or in a transcript. An MSIX additionally needs the
# certificate subject to equal the manifest's Publisher.
$ErrorActionPreference = 'Stop'
$pfx = $env:DISPCTRL_SIGN_PFX
if (-not $pfx -or -not (Test-Path -LiteralPath $pfx)) { throw 'Set DISPCTRL_SIGN_PFX to the signing certificate (.pfx).' }
$signtool = $env:DISPCTRL_SIGNTOOL
if (-not $signtool) {
    $signtool = (Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin') -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object FullName -match '[\/]x64[\/]' | Sort-Object FullName -Descending | Select-Object -First 1).FullName
}
if (-not $signtool) { throw 'signtool.exe not found; install the Windows SDK or set DISPCTRL_SIGNTOOL.' }
$files = @($Path | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })
# One call for every file: the timestamp server is the slow part, and it
# rate-limits a call per file on a large bundle.
$arguments = @('sign','/fd','SHA256','/f',$pfx,'/tr','http://timestamp.digicert.com','/td','SHA256')
if ($env:DISPCTRL_SIGN_PASSWORD) { $arguments += @('/p',$env:DISPCTRL_SIGN_PASSWORD) }
& $signtool @arguments @files
if ($LASTEXITCODE -ne 0) { throw "Signing failed for: $($files -join ', ')" }
