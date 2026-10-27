<#
.SYNOPSIS
    Builds Curfew's Windows icon (app.ico) from the logo PNG.

.DESCRIPTION
    Windows wants one .ico file holding the icon at several sizes, and picks the size that suits where it is shown
    (16 px in the tray, 32 px in the taskbar, 256 px in Explorer). This script makes that file from the one big PNG.

    Steps: trim the empty transparent border, make the picture square, shrink it to each size, and pack the
    sizes into an .ico. The PNG itself isn't changed.

    Run it from the repository root after changing the logo:
        powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
#>
param(
    [string]$Source = "src\Curfew.App\Assets\curfew.png",
    [string]$Output = "src\Curfew.App\Assets\app.ico"
)

Add-Type -AssemblyName System.Drawing

# The sizes inside the .ico. 16 to 48 are the ones Windows uses most; 256 is for large views.
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

# How much of the artwork's own width to leave around it after trimming, so edges aren't touching the icon's border.
$paddingFraction = 0.02

function Get-ArtworkBounds([System.Drawing.Bitmap]$bitmap) {
    # Finds the smallest rectangle containing every pixel that is not (nearly) transparent.
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bitmap.Width, $bitmap.Height
    $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $bytes = New-Object byte[] ($data.Stride * $data.Height)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    } finally { $bitmap.UnlockBits($data) }

    $minX = $bitmap.Width; $minY = $bitmap.Height; $maxX = -1; $maxY = -1
    for ($y = 0; $y -lt $bitmap.Height; $y++) {
        $row = $y * $data.Stride
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            if ($bytes[$row + $x * 4 + 3] -gt 8) {   # the 4th byte of each pixel is its opacity
                if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { return $rect }   # fully transparent: use everything
    return New-Object System.Drawing.Rectangle $minX, $minY, ($maxX - $minX + 1), ($maxY - $minY + 1)
}

function New-SquareArtwork([System.Drawing.Bitmap]$bitmap, [System.Drawing.Rectangle]$bounds) {
    # Puts the trimmed artwork in the middle of a transparent square, with a little room around it.
    $side = [int]([Math]::Max($bounds.Width, $bounds.Height) * (1 + 2 * $paddingFraction))
    $square = New-Object System.Drawing.Bitmap $side, $side, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($square)
    $g.Clear([System.Drawing.Color]::Transparent)
    $x = [int](($side - $bounds.Width) / 2); $y = [int](($side - $bounds.Height) / 2)
    $g.DrawImage($bitmap, (New-Object System.Drawing.Rectangle $x, $y, $bounds.Width, $bounds.Height), $bounds, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    return $square
}

function Resize([System.Drawing.Bitmap]$bitmap, [int]$size) {
    # Shrinks in steps of at most half, then once more to the exact size. Shrinking a 1000 px picture to 16 px in one
    # go makes thin lines flicker; halving first keeps the tick marks clean.
    $current = $bitmap
    while ($current.Width / 2 -gt $size) {
        $next = New-Object System.Drawing.Bitmap ([int]($current.Width / 2)), ([int]($current.Height / 2)), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($next)
        $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.SmoothingMode = 'HighQuality'; $g.CompositingMode = 'SourceCopy'
        $g.DrawImage($current, 0, 0, $next.Width, $next.Height); $g.Dispose()
        if ($current -ne $bitmap) { $current.Dispose() }
        $current = $next
    }
    $final = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($final)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.SmoothingMode = 'HighQuality'; $g.CompositingMode = 'SourceCopy'
    $g.DrawImage($current, 0, 0, $size, $size); $g.Dispose()
    if ($current -ne $bitmap) { $current.Dispose() }
    return $final
}

$sourcePath = (Resolve-Path $Source).Path
$original = [System.Drawing.Bitmap]::FromFile($sourcePath)
$bounds = Get-ArtworkBounds $original
$square = New-SquareArtwork $original $bounds
Write-Host "Logo is $($original.Width) x $($original.Height); artwork occupies $($bounds.Width) x $($bounds.Height) of it."

$images = foreach ($size in $sizes) {
    $scaled = Resize $square $size
    $stream = New-Object System.IO.MemoryStream
    $scaled.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)   # modern .ico files hold each size as a PNG
    $scaled.Dispose()
    , $stream.ToArray()
}
$square.Dispose(); $original.Dispose()

# An .ico file is: a small header, one 16-byte entry per size (size, where its data starts, how long it is), then the data.
$outputPath = Join-Path (Get-Location) $Output
$file = [System.IO.File]::Create($outputPath)
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]; $length = $images[$i].Length
    $dimension = if ($size -ge 256) { 0 } else { $size }   # 0 means 256 in this format
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$length); $writer.Write([uint32]$offset)
    $offset += $length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Close(); $file.Close()

Write-Host ("Wrote {0} ({1:N0} bytes, sizes: {2})" -f $Output, (Get-Item $outputPath).Length, ($sizes -join ', '))
