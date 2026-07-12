param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'AppIcon.ico')
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()

foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new(
        $size,
        $size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $scale = $size / 64.0
        $background = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 18, 25, 35))
        $accent = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 91, 157, 255))
        $ring = [System.Drawing.Pen]::new($accent, [Math]::Max(1.0, 4.0 * $scale))
        $blade = [System.Drawing.Pen]::new($accent, [Math]::Max(1.4, 7.0 * $scale))

        try {
            $ring.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $ring.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $blade.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $blade.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

            $graphics.FillEllipse($background, 2 * $scale, 2 * $scale, 60 * $scale, 60 * $scale)
            $graphics.DrawEllipse($ring, 5 * $scale, 5 * $scale, 54 * $scale, 54 * $scale)

            $center = 32 * $scale
            for ($i = 0; $i -lt 3; $i++) {
                $angle = (-90 + $i * 120) * [Math]::PI / 180
                $startX = $center + [Math]::Cos($angle) * 7 * $scale
                $startY = $center + [Math]::Sin($angle) * 7 * $scale
                $endX = $center + [Math]::Cos($angle) * 20 * $scale
                $endY = $center + [Math]::Sin($angle) * 20 * $scale
                $graphics.DrawLine($blade, $startX, $startY, $endX, $endY)
            }

            $graphics.FillEllipse($accent, 26 * $scale, 26 * $scale, 12 * $scale, 12 * $scale)
        }
        finally {
            $blade.Dispose()
            $ring.Dispose()
            $accent.Dispose()
            $background.Dispose()
        }

        $pngStream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
            $images.Add($pngStream.ToArray())
        }
        finally {
            $pngStream.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$directory = Split-Path -Parent $OutputPath
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
$stream = [System.IO.File]::Create($OutputPath)
$writer = [System.IO.BinaryWriter]::new($stream)

try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)

    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }

    foreach ($image in $images) {
        $writer.Write($image)
    }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Output "Generated $OutputPath with $($sizes.Count) image sizes."
