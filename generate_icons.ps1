param(
    [string]$SourcePng = (Join-Path $PSScriptRoot "app_icon.png"),
    [string]$TargetIco = (Join-Path $PSScriptRoot "app_icon.ico")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $SourcePng)) {
    throw "Source icon not found: $SourcePng"
}

$sizes = @(16, 32, 48, 64, 128, 256)
$pngBytesList = New-Object System.Collections.Generic.List[byte[]]

$srcImage = [System.Drawing.Image]::FromFile($SourcePng)
try {
    foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.DrawImage($srcImage, 0, 0, $size, $size)
        } finally {
            $g.Dispose()
        }

        $ms = New-Object System.IO.MemoryStream
        try {
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $pngBytesList.Add($ms.ToArray())
        } finally {
            $ms.Dispose()
            $bmp.Dispose()
        }
    }
} finally {
    $srcImage.Dispose()
}

$fs = [System.IO.File]::Create($TargetIco)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    # ICONDIR header
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$sizes.Count)

    $offset = 6 + (16 * $sizes.Count)
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]
        $data = $pngBytesList[$i]
        $dim = if ($s -ge 256) { [byte]0 } else { [byte]$s }
        $bw.Write($dim)                 # Width
        $bw.Write($dim)                 # Height
        $bw.Write([byte]0)              # Color count
        $bw.Write([byte]0)              # Reserved
        $bw.Write([uint16]1)            # Color planes
        $bw.Write([uint16]32)           # Bits per pixel
        $bw.Write([uint32]$data.Length) # Image data size
        $bw.Write([uint32]$offset)      # Image offset
        $offset += $data.Length
    }

    foreach ($data in $pngBytesList) {
        $bw.Write($data)
    }
} finally {
    $bw.Dispose()
    $fs.Dispose()
}

Write-Host "Generated multi-size icon: $TargetIco ($((Get-Item $TargetIco).Length) bytes)"

