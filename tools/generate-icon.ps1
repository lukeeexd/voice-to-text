# Generates src\VoiceToText\app.ico (multi-resolution) — a white microphone
# on a blue rounded square. Re-run if you want to tweak the look.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$projDir = (Resolve-Path (Join-Path $PSScriptRoot '..\src\VoiceToText')).Path
$outIco = Join-Path $projDir 'app.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded-square background with a vertical gradient.
    $margin = [Math]::Max(1, [int]($s * 0.04))
    $rect = New-Object System.Drawing.Rectangle($margin, $margin, ($s - 2 * $margin), ($s - 2 * $margin))
    $d = [int]($s * 0.44)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc(($rect.Right - $d), $rect.Y, $d, $d, 270, 90)
    $path.AddArc(($rect.Right - $d), ($rect.Bottom - $d), $d, $d, 0, 90)
    $path.AddArc($rect.X, ($rect.Bottom - $d), $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(95, 165, 250), [System.Drawing.Color]::FromArgb(35, 85, 200), 90.0)
    $g.FillPath($brush, $path)

    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $penW = [single]([Math]::Max(1.0, $s * 0.05))
    $pen = New-Object System.Drawing.Pen($white.Color, $penW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $cx = $s * 0.5
    $bodyW = $s * 0.26
    $bodyTop = $s * 0.20
    $bodyH = $s * 0.34

    # Mic capsule (rounded body).
    $bp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bx = [single]($cx - $bodyW / 2)
    $bp.AddArc($bx, [single]$bodyTop, [single]$bodyW, [single]$bodyW, 180, 180)
    $bp.AddArc($bx, [single]($bodyTop + $bodyH - $bodyW), [single]$bodyW, [single]$bodyW, 0, 180)
    $bp.CloseFigure()
    $g.FillPath($white, $bp)

    # Cradle arc, stem and base.
    $cradle = New-Object System.Drawing.RectangleF([single]($cx - $s * 0.20), [single]($s * 0.30), [single]($s * 0.40), [single]($s * 0.40))
    $g.DrawArc($pen, $cradle, 20, 140)
    $g.DrawLine($pen, [single]$cx, [single]($s * 0.70), [single]$cx, [single]($s * 0.80))
    $g.DrawLine($pen, [single]($cx - $s * 0.13), [single]($s * 0.80), [single]($cx + $s * 0.13), [single]($s * 0.80))

    $g.Dispose()
    return $bmp
}

$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , ($ms.ToArray())
    $bmp.Dispose(); $ms.Dispose()
}

$fs = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $pngs[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $bw.Write($data) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($outIco, $fs.ToArray())
$bw.Dispose()
Write-Host "Wrote $outIco ($([Math]::Round((Get-Item $outIco).Length/1KB,1)) KB, sizes: $($sizes -join ','))"
