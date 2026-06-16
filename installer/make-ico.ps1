<#
.SYNOPSIS
  Builds a multi-resolution Windows .ico from a square PNG (PNG-compressed entries).
.EXAMPLE
  installer\make-ico.ps1 -Src assets\logo.png -Out src\ChurchProjection.App\Assets\app.ico
#>
param(
    [Parameter(Mandatory)] [string]$Src,
    [Parameter(Mandatory)] [string]$Out
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 256, 128, 64, 48, 32, 16
$srcPath = [string](Resolve-Path $Src).Path
$img = [System.Drawing.Image]::FromFile($srcPath)
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $g.DrawImage($img, $rect)
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , ($ms.ToArray())
    $g.Dispose(); $bmp.Dispose(); $ms.Dispose()
}
$img.Dispose()

$outFull = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Out))
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($outFull)) | Out-Null
$fs = [System.IO.File]::Create($outFull)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $pngs[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $bw.Write($data) }
$bw.Flush(); $bw.Close(); $fs.Close()
Write-Host "Wrote $outFull"
