<#
.SYNOPSIS
    Adds or removes a virtual display for testing DispCtrl with two screens on a
    machine that has one. Off unless asked for: nothing in the build, the tests
    or the release runs it.

.DESCRIPTION
    Uses the open-source Virtual Display Driver (VirtualDrivers/Virtual-Display-Driver,
    an IddCx driver signed by the SignPath Foundation), pinned to one release and
    checked against its SHA-256. Adding and removing need administrator rights:
    the script asks once, through UAC, for the part that does.

    The display can borrow a real monitor's identity for realistic tests and
    screenshots (-Like): its EDID's size and timings are copied from a monitor
    this PC has seen, and -Name sets the name it reports. Its manufacturer and
    product code stay the driver's own (MTT-1337), so DispCtrl never takes it
    for that monitor or hands it that monitor's settings. DispCtrl shows it as
    virtual wherever it describes it, and keeps it out of the device library.

.EXAMPLE
    ./tools/VirtualDisplay.ps1 add -Like DELA234 -Name "DELL U2424H" -Mode 1920x1080 -Refresh 120 -Side left
    ./tools/VirtualDisplay.ps1 status
    ./tools/VirtualDisplay.ps1 remove
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][ValidateSet('add', 'remove', 'status')][string]$Action = 'status',
    # A monitor this PC has seen, by its PnP model as Windows records it (DELA234),
    # whose EDID gives the virtual display its size and timings.
    [string]$Like,
    # The name the virtual display reports (13 characters at most).
    [ValidateLength(1, 13)][string]$Name = 'Virtual 24in',
    [ValidatePattern('^\d+x\d+$')][string]$Mode = '1920x1080',
    [int]$Refresh = 60,
    [ValidateSet('left', 'right')][string]$Side = 'left'
)
$ErrorActionPreference = 'Stop'
$release = '25.7.23'
$zipUrl = "https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/$release/VirtualDisplayDriver-x86.Driver.Only.zip"
$zipSha = 'E24210692B442B39AF763536330CE78B423F19342B7A7792C26DE3944E418B3A'
$home_ = 'C:\VirtualDisplayDriver'   # the driver reads its settings and EDID from here
$work = Join-Path ([IO.Path]::GetTempPath()) 'dispctrl-virtual-display'
$repo = Split-Path -Parent $PSScriptRoot
$cli = @("$repo\src\DispCtrl.Cli\bin\Release\net10.0-windows10.0.26100.0\win-x64\dispctrl.exe", "$env:LOCALAPPDATA\Programs\DispCtrl\dispctrl.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1

function Device { Get-PnpDevice -Class Display -EA SilentlyContinue | Where-Object { $_.HardwareID -contains 'Root\MttVDD' } }
function DriverInf { Get-ChildItem "$env:WINDIR\INF" -Filter 'oem*.inf' | Where-Object { Select-String -Path $_.FullName -Pattern 'MttVDD' -Quiet } | Select-Object -First 1 }
function Elevated([string]$body) {
    $script = Join-Path $work 'elevated.ps1'
    $log = Join-Path $work 'elevated.log'
    "Start-Transcript -Path '$log' -Force | Out-Null`n`$ErrorActionPreference = 'Continue'`n$body`nStop-Transcript | Out-Null" | Set-Content $script -Encoding UTF8
    $p = Start-Process powershell.exe -Verb RunAs -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script -PassThru
    $p.WaitForExit()
    Get-Content $log | Where-Object { $_ -match 'successfully|added|Removed|Deleted|Failed|error|OK:' }
}
function VirtualDisplays {
    if (-not $cli) { return @() }
    @((& $cli displays list --local --json | ConvertFrom-Json).data.displays | Where-Object { $_.virtual })
}

# A real monitor's EDID with the driver's identity and the given name.
function Edid([string]$like, [string]$name) {
    $key = Get-ChildItem "HKLM:\SYSTEM\CurrentControlSet\Enum\DISPLAY\$like" -EA SilentlyContinue | Select-Object -First 1
    if (-not $key) { throw "No monitor $like has been seen on this PC (HKLM\...\Enum\DISPLAY). Leave out -Like for the driver's own EDID." }
    [byte[]]$d = (Get-ItemProperty "$($key.PSPath)\Device Parameters").EDID
    $pnp = 0; foreach ($c in 'MTT'.ToCharArray()) { $pnp = ($pnp -shl 5) -bor ([int]$c - 64) }
    $d[8] = [byte]($pnp -shr 8); $d[9] = [byte]($pnp -band 0xFF)       # manufacturer: the driver's own
    $d[10] = 0x37; $d[11] = 0x13                                          # product 0x1337, the driver's
    $d[12] = 0x24; $d[13] = 0x24; $d[14] = 0x24; $d[15] = 0x24            # a serial of its own
    foreach ($off in 54, 72, 90, 108) {
        if ($d[$off] -ne 0 -or $d[$off + 1] -ne 0) { continue }
        $text = switch ($d[$off + 3]) { 0xFC { "$name`n" } 0xFF { "VIRTUAL0001`n" } default { $null } }
        if (-not $text) { continue }
        $bytes = [Text.Encoding]::ASCII.GetBytes($text.PadRight(13).Substring(0, 13))
        [Array]::Copy($bytes, 0, $d, $off + 5, 13)
    }
    $sum = 0; for ($i = 0; $i -lt 127; $i++) { $sum += $d[$i] }; $d[127] = [byte]((256 - ($sum % 256)) % 256)
    $d
}

New-Item -ItemType Directory $work -Force | Out-Null
switch ($Action) {
    'status' {
        $dev = Device
        "Driver:  $(if (DriverInf) { (DriverInf).Name } else { 'not installed' })"
        "Device:  $(if ($dev) { "$($dev.InstanceId) ($($dev.Status))" } else { 'none' })"
        VirtualDisplays | ForEach-Object { "Display: $($_.number) $($_.name) $($_.width)x$($_.height) at $($_.x),$($_.y)" }
    }
    'add' {
        $zip = Join-Path $work 'vdd.zip'
        Invoke-WebRequest $zipUrl -OutFile $zip
        if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $zipSha) { throw "The driver download does not match its pinned SHA-256; not installed." }
        Expand-Archive $zip (Join-Path $work 'x') -Force
        $src = Join-Path $work 'x\VirtualDisplayDriver'
        if ($Like) {
            [IO.File]::WriteAllBytes((Join-Path $src 'user_edid.bin'), (Edid $Like $Name))
            $xml = Join-Path $src 'vdd_settings.xml'
            (Get-Content $xml -Raw) -replace '<CustomEdid>false</CustomEdid>', '<CustomEdid>true</CustomEdid>' | Set-Content $xml -Encoding UTF8
        }
        $create = if (Device) { "'OK: device already present'" } else { @'
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Dev {
  [StructLayout(LayoutKind.Sequential)] public struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }
  [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid g, IntPtr hwnd);
  [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern bool SetupDiCreateDeviceInfoW(IntPtr set, string name, ref Guid g, string desc, IntPtr hwnd, int flags, ref SP_DEVINFO_DATA d);
  [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern bool SetupDiSetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA d, int prop, byte[] buf, int size);
  [DllImport("setupapi.dll", SetLastError=true)] public static extern bool SetupDiCallClassInstaller(int fn, IntPtr set, ref SP_DEVINFO_DATA d);
  [DllImport("newdev.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern bool UpdateDriverForPlugAndPlayDevicesW(IntPtr hwnd, string hwid, string inf, int flags, out bool reboot);
}
"@
$display = [Guid]'4d36e968-e325-11ce-bfc1-08002be10318'
$set = [Dev]::SetupDiCreateDeviceInfoList([ref]$display, [IntPtr]::Zero)
$info = New-Object Dev+SP_DEVINFO_DATA; $info.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($info)
[void][Dev]::SetupDiCreateDeviceInfoW($set, 'Display', [ref]$display, 'Virtual Display Driver', [IntPtr]::Zero, 1, [ref]$info)
$hwid = [Text.Encoding]::Unicode.GetBytes("Root\MttVDD`0`0")
[void][Dev]::SetupDiSetDeviceRegistryPropertyW($set, [ref]$info, 1, $hwid, $hwid.Length)
"OK: registered $([Dev]::SetupDiCallClassInstaller(0x19, $set, [ref]$info))"
$reboot = $false
"OK: driver bound $([Dev]::UpdateDriverForPlugAndPlayDevicesW([IntPtr]::Zero, 'Root\MttVDD', "$src\MttVDD.inf", 1, [ref]$reboot))"
'@ }
        Elevated @"
`$src = '$src'
New-Item -ItemType Directory '$home_' -Force | Out-Null
Copy-Item "`$src\vdd_settings.xml" '$home_\' -Force
if (Test-Path "`$src\user_edid.bin") { Copy-Item "`$src\user_edid.bin" '$home_\' -Force }
pnputil.exe /add-driver "`$src\MttVDD.inf" /install
$create
`$d = Get-PnpDevice -Class Display | Where-Object { `$_.HardwareID -contains 'Root\MttVDD' }
if (`$d) { pnputil.exe /restart-device `$d.InstanceId }
"@
        if (-not $cli) { 'Added. Build dispctrl (or install DispCtrl) to arrange it from here.'; break }
        Start-Sleep 5
        & $cli topology set --mode extend --local --json | Out-Null
        Start-Sleep 5
        $v = VirtualDisplays | Select-Object -First 1
        if (-not $v) { 'Added, but Windows has not attached it yet: run status in a moment.'; break }
        & $cli display set --monitor $v.token --resolution $Mode --refresh $Refresh --local --json | Out-Null
        Start-Sleep 3
        $main = (& $cli displays list --local --json | ConvertFrom-Json).data.displays | Where-Object { $_.primary } | Select-Object -First 1
        $w = [int]($Mode -split 'x')[0]
        $x = if ($Side -eq 'left') { -$w } else { $main.width }
        & $cli display set --monitor $v.token --x $x --y 0 --local --json | Out-Null
        VirtualDisplays | ForEach-Object { "Added: $($_.number) $($_.name) $($_.width)x$($_.height) at $($_.x),$($_.y)" }
    }
    'remove' {
        $dev = Device; $inf = DriverInf
        if (-not $dev -and -not $inf) { 'Nothing to remove.'; break }
        Elevated @"
$(if ($dev) { "pnputil.exe /remove-device '$($dev.InstanceId)'" })
$(if ($inf) { "pnputil.exe /delete-driver '$($inf.Name)' /uninstall /force" })
Remove-Item '$home_' -Recurse -Force -ErrorAction SilentlyContinue
"@
    }
}
