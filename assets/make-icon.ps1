# Generates assets/app.ico - a multi-resolution icon assembled by hand, because
# System.Drawing can only save a single-size .ico.
#
# Mark: an amber tile carrying a dark striped globe. The tile is the app's accent
# colour so the icon stays visible on both light and dark taskbars, and the glyph
# drops its stripes below 24px, where they would collapse into mush.
param(
    [string]$OutFile = (Join-Path $PSScriptRoot 'app.ico')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Tile: vertical amber gradient, corner radius 22% of the canvas.
    $inset = [Math]::Max(0.0, $s * 0.02)
    $tile = New-RoundedPath $inset $inset ($s - $inset * 2) ($s - $inset * 2) ($s * 0.22)
    $rect = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(0xFF, 0xF4, 0xAD, 0x55),
        [System.Drawing.Color]::FromArgb(0xFF, 0xE0, 0x8F, 0x2E),
        90.0)
    $g.FillPath($brush, $tile)
    $brush.Dispose()

    # Glyph: dark globe.
    $ink = [System.Drawing.Color]::FromArgb(0xFF, 0x1A, 0x13, 0x06)
    $inkBrush = New-Object System.Drawing.SolidBrush($ink)
    # Small sizes get a slightly larger globe, since the tile's corner radius eats
    # proportionally more of the canvas as the pixel count drops.
    $r = if ($s -lt 24) { $s * 0.33 } else { $s * 0.30 }
    $cx = $s / 2.0
    $cy = $s / 2.0
    $g.FillEllipse($inkBrush, ($cx - $r), ($cy - $r), ($r * 2), ($r * 2))

    if ($s -lt 24) {
        # One equator only. Two bands plus a meridian turn to mush below 24px, but a
        # single cut is still enough to read as a globe rather than a dot.
        $clip = New-Object System.Drawing.Drawing2D.GraphicsPath
        $clip.AddEllipse(($cx - $r), ($cy - $r), ($r * 2), ($r * 2))
        $g.SetClip($clip)
        $band = [Math]::Max(1.0, [Math]::Round($s * 0.11))
        $amber = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0xFF, 0xEE, 0xA0, 0x44))
        $g.FillRectangle($amber, ($cx - $r), ($cy - $band / 2), ($r * 2), $band)
        $amber.Dispose()
        $g.ResetClip()
        $clip.Dispose()
    }
    else {
        # Two latitude cuts, punched back out in the tile colour. Clipped to the
        # circle so the stripes never bleed onto the tile.
        $clip = New-Object System.Drawing.Drawing2D.GraphicsPath
        $clip.AddEllipse(($cx - $r), ($cy - $r), ($r * 2), ($r * 2))
        $g.SetClip($clip)

        $band = [Math]::Max(1.0, $s * 0.055)
        $amber = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0xFF, 0xEE, 0xA0, 0x44))
        $g.FillRectangle($amber, ($cx - $r), ($cy - $r * 0.62 - $band / 2), ($r * 2), $band)
        $g.FillRectangle($amber, ($cx - $r), ($cy + $r * 0.18 - $band / 2), ($r * 2), $band)
        $amber.Dispose()

        # Meridian: a slim vertical lens, same colour, to read as a globe not a target.
        $mw = $r * 0.52
        $pen = New-Object System.Drawing.Pen(
            [System.Drawing.Color]::FromArgb(0xFF, 0xEE, 0xA0, 0x44), [float]$band)
        $g.DrawEllipse($pen, ($cx - $mw), ($cy - $r), ($mw * 2), ($r * 2))
        $pen.Dispose()

        $g.ResetClip()
        $clip.Dispose()
    }

    $inkBrush.Dispose()
    $tile.Dispose()
    $g.Dispose()
    return $bmp
}

# Render each size to an in-memory PNG. Windows has supported PNG-compressed icon
# entries since Vista, which keeps this simple and the file small.
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , @{ Size = $s; Bytes = $ms.ToArray() }
    $ms.Dispose(); $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)

# ICONDIR
$bw.Write([UInt16]0)                 # reserved
$bw.Write([UInt16]1)                 # type: 1 = icon
$bw.Write([UInt16]$pngs.Count)

# ICONDIRENTRY table, then the image blobs.
$offset = 6 + (16 * $pngs.Count)
foreach ($p in $pngs) {
    $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }   # 0 means 256 in this field
    $bw.Write([Byte]$dim)            # width
    $bw.Write([Byte]$dim)            # height
    $bw.Write([Byte]0)               # palette entries
    $bw.Write([Byte]0)               # reserved
    $bw.Write([UInt16]1)             # colour planes
    $bw.Write([UInt16]32)            # bits per pixel
    $bw.Write([UInt32]$p.Bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $p.Bytes.Length
}
foreach ($p in $pngs) { $bw.Write($p.Bytes) }

$bw.Flush()
[System.IO.File]::WriteAllBytes($OutFile, $out.ToArray())
$bw.Dispose(); $out.Dispose()

Write-Host ("Wrote {0} ({1:N0} bytes, {2} sizes: {3})" -f `
    $OutFile, (Get-Item $OutFile).Length, $pngs.Count, ($sizes -join ', ')) -ForegroundColor Green
