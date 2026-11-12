[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$branding = Join-Path $root 'assets\branding'
$sourcePath = Join-Path $branding 'autoclicker-icon-source.png'
if (-not (Test-Path -LiteralPath $sourcePath)) { throw "Canonical icon source not found: $sourcePath" }

function Save-PngAtSize([System.Drawing.Bitmap]$Source, [int]$Size, [string]$Path) {
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($Source, [System.Drawing.Rectangle]::new(0, 0, $Size, $Size))
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$inputBitmap = [System.Drawing.Bitmap]::new($sourcePath)
$master = $null
try {
    if ($inputBitmap.Width -ne $inputBitmap.Height) { throw "Icon source must be square; found $($inputBitmap.Width)x$($inputBitmap.Height)." }

    $minX = $inputBitmap.Width; $minY = $inputBitmap.Height; $maxX = -1; $maxY = -1
    for ($y = 0; $y -lt $inputBitmap.Height; $y++) {
        for ($x = 0; $x -lt $inputBitmap.Width; $x++) {
            if ($inputBitmap.GetPixel($x, $y).A -gt 8) {
                if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt $minX -or $maxY -lt $minY) { throw 'Icon source has no visible pixels.' }

    $visibleWidth = $maxX - $minX + 1
    $visibleHeight = $maxY - $minY + 1
    $canvasSize = [int][Math]::Ceiling([Math]::Max($visibleWidth, $visibleHeight) / 0.82)
    if (($canvasSize % 2) -ne 0) { $canvasSize++ }
    $contentCenterX = ($minX + $maxX + 1) / 2.0
    $contentCenterY = ($minY + $maxY + 1) / 2.0
    $drawX = [int][Math]::Round($canvasSize / 2.0 - $contentCenterX)
    $drawY = [int][Math]::Round($canvasSize / 2.0 - $contentCenterY)

    $square = [System.Drawing.Bitmap]::new($canvasSize, $canvasSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $square.SetResolution(96, 96)
    $graphics = [System.Drawing.Graphics]::FromImage($square)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($inputBitmap, $drawX, $drawY, $inputBitmap.Width, $inputBitmap.Height)
    } finally { $graphics.Dispose() }

    $masterPath = Join-Path $branding 'autoclicker-icon-1024.png'
    Save-PngAtSize $square 1024 $masterPath
    $square.Dispose()

    $master = [System.Drawing.Bitmap]::new($masterPath)
    $smallPath = Join-Path $branding 'autoclicker-icon-64.png'
    Save-PngAtSize $master 64 $smallPath

    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $pngs = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($size in $sizes) {
        $temporary = Join-Path ([IO.Path]::GetTempPath()) ("autoclicker-icon-$size-" + [guid]::NewGuid().ToString('N') + '.png')
        try {
            Save-PngAtSize $master $size $temporary
            $pngs.Add([IO.File]::ReadAllBytes($temporary))
        } finally {
            if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
        }
    }

    $iconPath = Join-Path $branding 'autoclicker.ico'
    $stream = [IO.File]::Create($iconPath)
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $size = $sizes[$index]; $bytes = $pngs[$index]
            $writer.Write([byte]($size % 256)); $writer.Write([byte]($size % 256))
            $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([UInt16]1); $writer.Write([UInt16]32)
            $writer.Write([UInt32]$bytes.Length); $writer.Write([UInt32]$offset)
            $offset += $bytes.Length
        }
        foreach ($bytes in $pngs) { $writer.Write($bytes) }
    } finally { $writer.Dispose(); $stream.Dispose() }

    Write-Host "Created $masterPath, $smallPath and $iconPath from source bounds $minX,$minY-$maxX,$maxY with a centered 1024px master."
    Write-Host "ICO frames: $($sizes -join ', ') px."
} finally {
    if ($null -ne $master) { $master.Dispose() }
    $inputBitmap.Dispose()
}
