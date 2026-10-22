# Erzeugt Assets\app.ico (16–256 px): oranges abgerundetes Quadrat mit Mikrofon-Symbol (Segoe MDL2 Assets).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repo = Split-Path $PSScriptRoot -Parent
$out = Join-Path $repo 'Assets\app.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function Draw([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.Clear([Drawing.Color]::Transparent)
    $r = [Math]::Max(2, [int]($s * 0.22)); $pad = [Math]::Max(0, [int]($s * 0.03)); $w = $s - 2 * $pad
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($pad, $pad, 2 * $r, 2 * $r, 180, 90); $path.AddArc($pad + $w - 2 * $r, $pad, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($pad + $w - 2 * $r, $pad + $w - 2 * $r, 2 * $r, 2 * $r, 0, 90); $path.AddArc($pad, $pad + $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $grad = New-Object Drawing.Drawing2D.LinearGradientBrush((New-Object Drawing.Point 0, 0), (New-Object Drawing.Point 0, $s),
        [Drawing.Color]::FromArgb(255, 0xF7, 0xA0, 0x3C), [Drawing.Color]::FromArgb(255, 0xD9, 0x6E, 0x10))
    $g.FillPath($grad, $path)
    $font = New-Object Drawing.Font('Segoe MDL2 Assets', [float]($s * 0.56), [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $g.DrawString([string][char]0xE720, $font, [Drawing.Brushes]::White, (New-Object Drawing.RectangleF 0, ([float]($s * 0.03)), $s, $s), $fmt)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) { , (Draw $s) }
$fs = [IO.File]::Create($out); $bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$images[$i].Length); $bw.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
"Icon geschrieben: $out ($((Get-Item $out).Length) Bytes)"
# Vorschau zum Ansehen
[IO.File]::WriteAllBytes((Join-Path $repo 'Assets\app-preview-256.png'), $images[-1])
[IO.File]::WriteAllBytes((Join-Path $repo 'Assets\app-preview-32.png'), $images[3])
