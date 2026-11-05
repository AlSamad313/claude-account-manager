<#
.SYNOPSIS
  Draws the app icon, as scripts/make-icon.sh does for macOS: the burst from Resources/BurstMask.png on
  a rounded square in one fixed colour. Writes src\ClaudeProfiles\app.ico with 16 to 256 px images.
  The app icon cannot follow the default profile; the tray icon (ProfileUI\TrayImage.cs) does.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$here = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$repo = Split-Path -Parent $here
$out = Join-Path $here 'src\ClaudeProfiles\app.ico'
$mask = [System.Drawing.Image]::FromFile((Join-Path $repo 'Resources\BurstMask.png'))

function Draw([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    # The macOS proportions: a 10% margin, then a corner radius and burst of 18% and 58% of the whole.
    # Small sizes use less margin, so the icon is not lost in a 16 px slot.
    $inset = [single]($(if ($size -le 32) { $size * 0.04 } else { $size * 0.1 }))
    $s = [single]($size - 2 * $inset)
    $r = [single]($s * 0.225)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($inset, $inset, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($inset + $s - 2 * $r, $inset, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($inset + $s - 2 * $r, $inset + $s - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($inset, $inset + $s - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 217, 120, 87))
    $g.FillPath($fill, $path)
    $side = [single]($s * 0.725)
    $g.DrawImage($mask, [single](($size - $side) / 2), [single](($size - $side) / 2), $side, $side)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) { , (Draw $s) }
$mask.Dispose()
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
