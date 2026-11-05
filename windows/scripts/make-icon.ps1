<#
.SYNOPSIS
  Draws the placeholder app icon: three overlapping profile dots on a dark rounded square.
  Writes src\ClaudeProfiles\app.ico with 16 to 256 px images. Replace app.ico when the real icon exists.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$here = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$out = Join-Path $here 'src\ClaudeProfiles\app.ico'

function Draw([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    # Rounded square
    $r = [single]($size * 0.22)
    $s = [single]($size - 1)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($s - 2 * $r, 0, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($s - 2 * $r, $s - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc(0, $s - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $size), ([System.Drawing.ColorTranslator]::FromHtml('#2B2F36')), ([System.Drawing.ColorTranslator]::FromHtml('#16181C'))
    $g.FillPath($bg, $path)
    # Three dots in the profile colours, the front one ringed by the background
    $d = [single]($size * 0.42)
    $ring = [single][Math]::Max(1, $size * 0.045)
    $spots = @(
        @{ X = 0.16; Y = 0.18; C = '#0F8A7E' },
        @{ X = 0.42; Y = 0.18; C = '#8B5CF6' },
        @{ X = 0.29; Y = 0.42; C = '#E8772E' })
    foreach ($p in $spots) {
        $x = [single]($size * $p.X); $y = [single]($size * $p.Y)
        $edge = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#1E2126'))
        $g.FillEllipse($edge, $x - $ring, $y - $ring, $d + 2 * $ring, $d + 2 * $ring)
        $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($p.C))
        $g.FillEllipse($fill, $x, $y, $d, $d)
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) { , (Draw $s) }
$file = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $file
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[System.IO.File]::WriteAllBytes($out, $file.ToArray())
Write-Host "Wrote $out"
