# Draws the CDiskCleaner app icon with System.Drawing (no image generator, no NuGet)
# and packs a real multi-size .ico (16/24/32/48/64/128/256) for <ApplicationIcon>.
#
# Design: Windows 11 style rounded tile in the app accent teal (#0F766E -> #115E59),
# a white hard-disk plinth with a slot and an LED, plus two sparkles for "clean".
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 -PreviewSheet

param(
    [string]$IcoPath = (Join-Path $PSScriptRoot '..\src\CDiskCleaner\Assets\app-icon.ico'),
    [string]$PreviewPath = (Join-Path $PSScriptRoot '..\docs\icon-preview-256.png'),
    [switch]$PreviewSheet
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$IcoPath = [System.IO.Path]::GetFullPath($IcoPath)
$PreviewPath = [System.IO.Path]::GetFullPath($PreviewPath)

foreach ($dir in @([System.IO.Path]::GetDirectoryName($IcoPath), [System.IO.Path]::GetDirectoryName($PreviewPath))) {
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
}

$MasterSize = 1024
$Sizes = @(256, 128, 64, 48, 32, 24, 16)

function New-RoundedRect {
    param([single]$X, [single]$Y, [single]$W, [single]$H, [single]$R)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $R * 2
    if ($d -gt $W) { $d = $W }
    if ($d -gt $H) { $d = $H }
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# 4-point sparkle: four cubic beziers whose control points sit in the centre.
function New-Sparkle {
    param([single]$Cx, [single]$Cy, [single]$R)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $n = New-Object System.Drawing.PointF($Cx, ($Cy - $R))
    $e = New-Object System.Drawing.PointF(($Cx + $R), $Cy)
    $s = New-Object System.Drawing.PointF($Cx, ($Cy + $R))
    $w = New-Object System.Drawing.PointF(($Cx - $R), $Cy)
    $c = New-Object System.Drawing.PointF($Cx, $Cy)
    $path.StartFigure()
    $path.AddBezier($n, $c, $c, $e)
    $path.AddBezier($e, $c, $c, $s)
    $path.AddBezier($s, $c, $c, $w)
    $path.AddBezier($w, $c, $c, $n)
    $path.CloseFigure()
    return $path
}

function New-IconMaster {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        # scale everything from the 1024 design grid
        $k = $Size / 1024.0

        # --- tile -------------------------------------------------------------
        $tile = New-RoundedRect 0 0 $Size $Size (200 * $k)
        $accent = [System.Drawing.ColorTranslator]::FromHtml('#0F766E')
        $accentDark = [System.Drawing.ColorTranslator]::FromHtml('#115E59')
        $tileBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.Point(0, 0)),
            (New-Object System.Drawing.Point(0, $Size)),
            $accent, $accentDark)
        $g.FillPath($tileBrush, $tile)
        $tileBrush.Dispose()

        # hairline highlight just inside the tile edge (polish, keeps the tile from going flat)
        $inner = New-RoundedRect (8 * $k) (8 * $k) ($Size - 16 * $k) ($Size - 16 * $k) (192 * $k)
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(38, 255, 255, 255), (7 * $k))
        $g.DrawPath($pen, $inner)
        $pen.Dispose()
        $inner.Dispose()

        # --- drive (flat Explorer-style case: rounded body + seam + LED) ------
        $bodyX = 224 * $k
        $bodyY = 336 * $k
        $bodyW = 576 * $k
        $bodyH = 424 * $k
        $drivePath = New-RoundedRect $bodyX $bodyY $bodyW $bodyH (72 * $k)
        $driveBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF(0, $bodyY)),
            (New-Object System.Drawing.PointF(0, ($bodyY + $bodyH))),
            [System.Drawing.Color]::White,
            [System.Drawing.ColorTranslator]::FromHtml('#CFE9E6'))
        $g.FillPath($driveBrush, $drivePath)
        $driveBrush.Dispose()

        # seam: a thin full-width band, the single cue that says "drive bay"
        $seamBrush = New-Object System.Drawing.SolidBrush($accent)
        $seamTop = $bodyY + ($bodyH * 0.655)
        $seamH = 28 * $k
        $g.FillRectangle($seamBrush, $bodyX, $seamTop, $bodyW, $seamH)

        # LED under the seam
        $ledBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#14B8A6'))
        $led = 34 * $k
        $ledCy = $seamTop + $seamH + (($bodyY + $bodyH - ($seamTop + $seamH)) / 2)
        $g.FillEllipse($ledBrush, (($bodyX + (104 * $k)) - $led), ($ledCy - $led), ($led * 2), ($led * 2))
        $ledBrush.Dispose()
        $seamBrush.Dispose()

        # --- sparkles ---------------------------------------------------------
        $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $g.FillPath($white, (New-Sparkle (768 * $k) (264 * $k) (148 * $k)))
        $g.FillPath($white, (New-Sparkle (288 * $k) (862 * $k) (92 * $k)))
        $white.Dispose()

        return $bmp
    }
    finally {
        $g.Dispose()
    }
}

function Resize-Bitmap {
    param([System.Drawing.Bitmap]$Source, [int]$Size)
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImage($Source, (New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)))
    }
    finally {
        $g.Dispose()
    }
    return $bmp
}

function Get-PngBytes {
    param([System.Drawing.Bitmap]$Bitmap)
    $ms = New-Object System.IO.MemoryStream
    try {
        $Bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return $ms.ToArray()
    }
    finally {
        $ms.Dispose()
    }
}

# 32bpp uncompressed DIB frame: BITMAPINFOHEADER + bottom-up BGRA + 1bpp AND mask.
function Get-DibBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $w = $Bitmap.Width
    $h = $Bitmap.Height
    $stride = $w * 4
    $maskStride = [int]([Math]::Floor((($w + 31) / 32))) * 4
    $xorSize = $stride * $h
    $andSize = $maskStride * $h

    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $pixels = New-Object byte[] ($data.Stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
        $sourceStride = $data.Stride
    }
    finally {
        $Bitmap.UnlockBits($data)
    }

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    try {
        # BITMAPINFOHEADER
        $bw.Write([int]40)
        $bw.Write([int]$w)
        $bw.Write([int]($h * 2))   # XOR + AND
        $bw.Write([int16]1)
        $bw.Write([int16]32)
        $bw.Write([int]0)          # BI_RGB
        $bw.Write([int]($xorSize + $andSize))
        $bw.Write([int]0)
        $bw.Write([int]0)
        $bw.Write([int]0)
        $bw.Write([int]0)

        for ($y = $h - 1; $y -ge 0; $y--) {
            $bw.Write($pixels, ($y * $sourceStride), $stride)
        }

        # AND mask: 1 where the pixel is fully transparent
        for ($y = $h - 1; $y -ge 0; $y--) {
            $row = New-Object byte[] $maskStride
            for ($x = 0; $x -lt $w; $x++) {
                $alpha = $pixels[($y * $sourceStride) + ($x * 4) + 3]
                if ($alpha -eq 0) {
                    $byteIndex = $x -shr 3
                    $row[$byteIndex] = $row[$byteIndex] -bor ([byte](1 -shl (7 - ($x -band 7))))
                }
            }
            $bw.Write($row)
        }
    }
    finally {
        $bw.Dispose()
        $ms.Dispose()
    }

    return $ms.ToArray()
}

function Write-Icon {
    param([hashtable]$Frames, [string]$Path)

    $ordered = @($Sizes | Where-Object { $Frames.ContainsKey($_) })
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    try {
        $bw.Write([int16]0)                 # reserved
        $bw.Write([int16]1)                 # type: icon
        $bw.Write([int16]$ordered.Count)

        # the 256x256 frame travels as PNG (the shell convention); the rest as DIB
        $payloads = @()
        foreach ($size in $ordered) {
            $bytes = if ($size -ge 256) { Get-PngBytes $Frames[$size] } else { Get-DibBytes $Frames[$size] }
            $payloads += , @{ Size = $size; Bytes = $bytes }
        }

        $offset = 6 + (16 * $ordered.Count)
        foreach ($p in $payloads) {
            $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }
            $bw.Write([byte]$dim)
            $bw.Write([byte]$dim)
            $bw.Write([byte]0)
            $bw.Write([byte]0)
            $bw.Write([int16]1)
            $bw.Write([int16]32)
            $bw.Write([int]$p.Bytes.Length)
            $bw.Write([int]$offset)
            $offset += $p.Bytes.Length
        }

        foreach ($p in $payloads) {
            $bw.Write($p.Bytes, 0, $p.Bytes.Length)
        }
    }
    finally {
        $bw.Dispose()
    }

    [System.IO.File]::WriteAllBytes($Path, $ms.ToArray())
    $ms.Dispose()
}

# --- draw once, downscale in steps so the small frames stay crisp -------------
$master = New-IconMaster $MasterSize
$frames = @{}
$previous = $master
try {
    foreach ($size in $Sizes) {
        if ($size -eq $MasterSize) {
            $frames[$size] = $master
        } elseif ($previous.Width -eq $size) {
            $frames[$size] = $previous
        } else {
            $next = Resize-Bitmap $previous $size
            $frames[$size] = $next
            $previous = $next
        }
    }

    Write-Icon $frames $IcoPath
    $frames[256].Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)

    if ($PreviewSheet) {
        $sheetPath = Join-Path ([System.IO.Path]::GetDirectoryName($PreviewPath)) 'icon-preview-sizes.png'
        $cell = 160
        $sheet = New-Object System.Drawing.Bitmap(($cell * $Sizes.Count), $cell, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $sg = [System.Drawing.Graphics]::FromImage($sheet)
        try {
            $sg.Clear([System.Drawing.ColorTranslator]::FromHtml('#F3F6F8'))
            $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $i = 0
            foreach ($size in $Sizes) {
                $scale = [int][Math]::Max(1, [Math]::Floor($cell / $size))
                if ($size -gt $cell) { $scale = 1 }
                $draw = $size * $scale
                $x = ($cell * $i) + [int](($cell - $draw) / 2)
                $y = [int](($cell - $draw) / 2)
                $sg.DrawImage($frames[$size], (New-Object System.Drawing.Rectangle($x, $y, $draw, $draw)))
                $i++
            }
        }
        finally {
            $sg.Dispose()
        }
        $sheet.Save($sheetPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $sheet.Dispose()
        Write-Host ("sizes sheet : " + $sheetPath)
    }
}
finally {
    foreach ($size in $Sizes) { if ($frames.ContainsKey($size) -and $frames[$size] -ne $master) { $frames[$size].Dispose() } }
    $master.Dispose()
}

# --- verify what was written -------------------------------------------------
$info = Get-Item -LiteralPath $IcoPath
$icon = New-Object System.Drawing.Icon($IcoPath)
Write-Host ("icon        : " + $IcoPath)
Write-Host ("bytes       : " + $info.Length)
Write-Host ("default size: " + $icon.Width + "x" + $icon.Height)
$icon.Dispose()
Write-Host ("preview     : " + $PreviewPath)
