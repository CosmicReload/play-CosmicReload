# Draws the installer artwork from app\logo.png: welcome.bmp (164x314, welcome/finish pages) and header.bmp
# (150x57, header of the other pages), in the launcher's colours. Run again after changing the logo.
Add-Type -AssemblyName System.Drawing
$here = $PSScriptRoot
$logo = [Drawing.Image]::FromFile((Join-Path $here '..\app\logo.png'))
$bg = [Drawing.Color]::FromArgb(11, 11, 18)
$cyan = [Drawing.Color]::FromArgb(85, 255, 255)
$pink = [Drawing.Color]::FromArgb(255, 85, 255)

function Glow($g, [float]$x, [float]$y, [float]$r, [Drawing.Color]$c) {
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($x - $r, $y - $r, $r * 2, $r * 2)
    $brush = New-Object Drawing.Drawing2D.PathGradientBrush($path)
    $brush.CenterColor = $c
    $brush.SurroundColors = @([Drawing.Color]::FromArgb(0, $c))
    $g.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()
}

function Stars($g, [int]$w, [int]$h, [int]$n, [int]$seed) {
    $rnd = New-Object Random($seed)
    for ($i = 0; $i -lt $n; $i++) {
        $b = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(40 + $rnd.Next(130), 220, 220, 255))
        $s = if ($rnd.NextDouble() -lt 0.15) { 2 } else { 1.2 }
        $g.FillEllipse($b, [float]($rnd.NextDouble() * $w), [float]($rnd.NextDouble() * $h), $s, $s)
        $b.Dispose()
    }
}

function Spaced($g, [string]$text, $font, $brush, [float]$centerX, [float]$y, [float]$spacing) {
    $fmt = [Drawing.StringFormat]::GenericTypographic
    $w = 0
    foreach ($ch in $text.ToCharArray()) { $w += $g.MeasureString([string]$ch, $font, [Drawing.PointF]::Empty, $fmt).Width + $spacing }
    $x = $centerX - ($w - $spacing) / 2
    foreach ($ch in $text.ToCharArray()) {
        $g.DrawString([string]$ch, $font, $brush, $x, $y, $fmt)
        $x += $g.MeasureString([string]$ch, $font, [Drawing.PointF]::Empty, $fmt).Width + $spacing
    }
}

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object Drawing.Bitmap($w, $h, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear($bg)
    return $bmp, $g
}

# welcome / finish page
$bmp, $g = New-Canvas 164 314
Glow $g 150 -10 150 ([Drawing.Color]::FromArgb(60, $pink))
Glow $g 0 324 150 ([Drawing.Color]::FromArgb(45, $cyan))
Stars $g 164 314 40 7
Glow $g 82 118 70 ([Drawing.Color]::FromArgb(80, $pink))
$g.DrawImage($logo, 42, 78, 80, 80)
$font = New-Object Drawing.Font('Segoe UI Black', 15, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
$cb = New-Object Drawing.SolidBrush($cyan)
$pb = New-Object Drawing.SolidBrush($pink)
Spaced $g 'COSMIC' $font $cb 82 176 3
Spaced $g 'RELOAD' $font $pb 82 196 3
$small = New-Object Drawing.Font('Segoe UI', 9, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
$mb = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(154, 154, 174))
Spaced $g 'FOR COSMIC CLIENT' $small $mb 82 226 1.6
$g.Dispose()
$bmp.Save((Join-Path $here 'welcome.bmp'), [Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()

# header of the other pages (logo on the right)
$bmp, $g = New-Canvas 150 57
Glow $g 120 28 50 ([Drawing.Color]::FromArgb(70, $pink))
Stars $g 150 57 10 3
$g.DrawImage($logo, 98, 8, 41, 41)
$g.Dispose()
$bmp.Save((Join-Path $here 'header.bmp'), [Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()
$logo.Dispose()
'Wrote welcome.bmp and header.bmp'
