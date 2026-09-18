<#
.SYNOPSIS
    Generates DisplCtrl's application icon.

.DESCRIPTION
    Draws a monitor with one dark panel and one lit, which is what DisplCtrl is
    about: two displays treated differently. Rendered at 256px and wrapped in a
    PNG-compressed ICO, which Windows Vista and later read natively.

    Kept as a script rather than a committed binary so the icon can be
    regenerated or restyled without a drawing tool in the loop.
#>
param(
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'src\DisplCtrl.App\Assets\DisplCtrl.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc(($x + $w - $d), $y, $d, $d, 270, 90)
    $p.AddArc(($x + $w - $d), ($y + $h - $d), $d, $d, 0, 90)
    $p.AddArc($x, ($y + $h - $d), $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Outer body. Near-black, matching the OLED case DisplCtrl exists for.
$body = New-RoundedPath 20 36 216 150 18
$g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 32, 32, 36))), $body)
$g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 78, 78, 86), 4)), $body)

# Left half stays dark; right half is lit. The split is the whole idea.
$lit = New-RoundedPath 132 52 88 118 10
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point 132, 52),
    (New-Object System.Drawing.Point 220, 170),
    [System.Drawing.Color]::FromArgb(255, 200, 160, 245),
    [System.Drawing.Color]::FromArgb(255, 120, 90, 200))
$g.FillPath($brush, $lit)

$dark = New-RoundedPath 36 52 88 118 10
$g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 12, 12, 14))), $dark)

# Stand.
$g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 78, 78, 86))),
            (New-RoundedPath 104 186 48 22 6))
$g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 96, 96, 104))),
            (New-RoundedPath 72 206 112 18 9))

$g.Dispose()

# --- wrap the PNG in an ICO container -------------------------------------
$png = New-Object System.IO.MemoryStream
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$bytes = $png.ToArray()
$bmp.Dispose()

$dir = Split-Path -Parent $OutputPath
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

$fs = [System.IO.File]::Create($OutputPath)
$w = New-Object System.IO.BinaryWriter $fs

$w.Write([uint16]0)      # reserved
$w.Write([uint16]1)      # type: icon
$w.Write([uint16]1)      # image count

$w.Write([byte]0)        # width  (0 means 256)
$w.Write([byte]0)        # height (0 means 256)
$w.Write([byte]0)        # palette entries
$w.Write([byte]0)        # reserved
$w.Write([uint16]1)      # colour planes
$w.Write([uint16]32)     # bits per pixel
$w.Write([uint32]$bytes.Length)
$w.Write([uint32]22)     # offset: 6-byte header + 16-byte directory entry

$w.Write($bytes)
$w.Flush(); $w.Dispose(); $fs.Dispose()

Write-Host ("Wrote {0} ({1:N0} bytes)" -f $OutputPath, (Get-Item $OutputPath).Length)
