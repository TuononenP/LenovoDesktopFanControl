param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'AppIcon.ico')
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()

# Swept turbine blade, defined in the 64x64 design space, pointing up (-Y)
# and curving to the right so rotation by 120 degrees produces a fan.
$bladePts = @(
    (33.0, 27.0),
    (41.0, 23.0),
    (46.0, 15.0),
    (43.0, 7.0),
    (35.0, 5.0),
    (28.0, 9.0),
    (27.0, 18.0),
    (30.0, 24.0)
) | ForEach-Object { [System.Drawing.PointF]::new($_[0], $_[1]) }

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
        $graphics.ScaleTransform($scale, $scale)

        $background = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 18, 25, 35))
        $accent = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 91, 157, 255))
        $accentDeep = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 56, 110, 220))
        $hub = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 232, 240, 255))
        $ring = [System.Drawing.Pen]::new($accent, 3)
        $hubRing = [System.Drawing.Pen]::new($accentDeep, 2.5)

        try {
            $ring.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $ring.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

            $graphics.FillEllipse($background, 2, 2, 60, 60)
            $graphics.DrawEllipse($ring, 4, 4, 56, 56)

            for ($i = 0; $i -lt 3; $i++) {
                $state = $graphics.Save()
                $graphics.TranslateTransform(32, 32, [System.Drawing.Drawing2D.MatrixOrder]::Prepend)
                $graphics.RotateTransform(120 * $i, [System.Drawing.Drawing2D.MatrixOrder]::Prepend)
                $graphics.TranslateTransform(-32, -32, [System.Drawing.Drawing2D.MatrixOrder]::Prepend)
                $graphics.FillClosedCurve($accent, $bladePts, [System.Drawing.Drawing2D.FillMode]::Alternate, 0.6)
                $graphics.Restore($state)
            }

            $graphics.FillEllipse($hub, 26, 26, 12, 12)
            $graphics.DrawEllipse($hubRing, 26, 26, 12, 12)
            $graphics.FillEllipse($accentDeep, 29.5, 29.5, 5, 5)
        }
        finally {
            $hubRing.Dispose()
            $ring.Dispose()
            $hub.Dispose()
            $accentDeep.Dispose()
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