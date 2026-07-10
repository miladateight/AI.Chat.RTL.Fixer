<#
.SYNOPSIS
    Generates all derived branding assets for AI Chat RTL Fixer from source PNGs.

.DESCRIPTION
    Source of truth (checked into the repo):
        assets/branding/app-logo.png    - the application logo (square, >= 512x512 recommended)
        assets/branding/brand-logo.png  - the "Milad AT8" brand mark

    Generated (do not edit by hand, produced by this script):
        assets/branding/app-logo.ico          - multi-size icon for the exe, Start Menu, tray, setup
        assets/branding/installer-sidebar.bmp - Inno Setup WizardImageFile (164x314, 24-bit)
        assets/branding/installer-small.bmp   - Inno Setup WizardSmallImageFile (55x55, 24-bit)

    If app-logo.png is missing, brand-logo.png is used as a STAND-IN so the build
    pipeline stays verifiable, and a loud warning is printed. Drop the real
    app-logo.png in and re-run to finalize.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$branding = Join-Path $root "assets\branding"

$appLogo = Join-Path $branding "app-logo.png"
$brandLogo = Join-Path $branding "brand-logo.png"

# Two distinct sources, two distinct roles:
#   app-logo.png   -> the app ICON (exe, tray, Start Menu, Setup.exe icon)
#   brand-logo.png -> the images shown DURING installation (wizard sidebar/header)
if (-not (Test-Path $brandLogo)) {
    throw "Brand logo is required for the installer wizard images: $brandLogo"
}

$iconSource = $appLogo
$usingStandIn = $false
if (-not (Test-Path $appLogo)) {
    $iconSource = $brandLogo
    $usingStandIn = $true
    Write-Warning "==================================================================="
    Write-Warning " assets/branding/app-logo.png is MISSING (the app's own chat-bubble logo)."
    Write-Warning " The APP ICON is being built from the brand logo as a STAND-IN."
    Write-Warning " Installer wizard images already use the real brand logo and are final."
    Write-Warning " Drop the real app-logo.png in and re-run to finalize the app icon."
    Write-Warning "==================================================================="
}

Write-Output "App icon source     : $iconSource"
Write-Output "Wizard image source : $brandLogo"

# Make the SOLID background around the logo transparent, so an app tile that
# was authored on a white canvas does not show a white box in the tray/taskbar.
# Uses a flood fill seeded from the image border, so white *inside* the logo
# (e.g. text lines, speech bubble) is preserved - only the outer background
# reachable from the edges is cleared.
function Remove-BorderBackground {
    param([System.Drawing.Bitmap] $Bmp, [int] $Threshold = 235)

    $w = $Bmp.Width; $h = $Bmp.Height
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $Bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadWrite, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $buf = New-Object 'byte[]' ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)

    $visited = New-Object 'bool[]' ($w * $h)
    $stack = New-Object System.Collections.Generic.Stack[int]

    $pushIf = {
        param([int]$x, [int]$y)
        if ($x -lt 0 -or $y -lt 0 -or $x -ge $w -or $y -ge $h) { return }
        $i = $y * $w + $x
        if ($visited[$i]) { return }
        $off = $y * $stride + $x * 4
        if ($buf[$off] -ge $Threshold -and $buf[$off + 1] -ge $Threshold -and $buf[$off + 2] -ge $Threshold) {
            $visited[$i] = $true
            $stack.Push($i)
        }
    }

    for ($x = 0; $x -lt $w; $x++) { & $pushIf $x 0; & $pushIf $x ($h - 1) }
    for ($y = 0; $y -lt $h; $y++) { & $pushIf 0 $y; & $pushIf ($w - 1) $y }

    while ($stack.Count -gt 0) {
        $i = $stack.Pop()
        $y = [int][math]::Floor($i / $w); $x = $i - $y * $w
        $buf[$y * $stride + $x * 4 + 3] = 0
        & $pushIf ($x - 1) $y; & $pushIf ($x + 1) $y; & $pushIf $x ($y - 1); & $pushIf $x ($y + 1)
    }

    [System.Runtime.InteropServices.Marshal]::Copy($buf, 0, $data.Scan0, $buf.Length)
    $Bmp.UnlockBits($data)
}

function New-ScaledBitmap {
    param([System.Drawing.Image] $Image, [int] $Width, [int] $Height)
    $bmp = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($Image, 0, 0, $Width, $Height)
    $g.Dispose()
    return $bmp
}

# ---- Multi-size ICO (PNG-compressed entries, Vista+) ----------------------
function Write-Ico {
    param([System.Drawing.Image] $Image, [string] $OutPath, [int[]] $Sizes)

    $pngBlobs = @()
    foreach ($s in $Sizes) {
        $bmp = New-ScaledBitmap -Image $Image -Width $s -Height $s
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $pngBlobs += ,@{ Size = $s; Bytes = $ms.ToArray() }
        $ms.Dispose()
    }

    $fs = [System.IO.File]::Create($OutPath)
    $bw = New-Object System.IO.BinaryWriter($fs)
    # ICONDIR
    $bw.Write([UInt16]0)              # reserved
    $bw.Write([UInt16]1)              # type = icon
    $bw.Write([UInt16]$pngBlobs.Count)
    $offset = 6 + (16 * $pngBlobs.Count)
    foreach ($b in $pngBlobs) {
        $dim = if ($b.Size -ge 256) { 0 } else { $b.Size }  # 0 means 256
        $bw.Write([Byte]$dim)         # width
        $bw.Write([Byte]$dim)         # height
        $bw.Write([Byte]0)            # palette
        $bw.Write([Byte]0)            # reserved
        $bw.Write([UInt16]1)          # color planes
        $bw.Write([UInt16]32)         # bpp
        $bw.Write([UInt32]$b.Bytes.Length)
        $bw.Write([UInt32]$offset)
        $offset += $b.Bytes.Length
    }
    foreach ($b in $pngBlobs) { $bw.Write($b.Bytes) }
    $bw.Flush(); $bw.Close(); $fs.Close()
    Write-Output "  wrote $OutPath ($($pngBlobs.Count) sizes)"
}

# ---- 24-bit BMP wizard image, logo centered on a white canvas -------------
function Write-WizardBmp {
    param([System.Drawing.Image] $Image, [string] $OutPath, [int] $Width, [int] $Height, [double] $Fill)
    $canvas = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.Clear([System.Drawing.Color]::White)

    $box = [Math]::Min($Width, $Height) * $Fill
    $scale = $box / [Math]::Max($Image.Width, $Image.Height)
    $w = [int]($Image.Width * $scale)
    $h = [int]($Image.Height * $scale)
    $x = [int](($Width - $w) / 2)
    $y = [int](($Height - $h) / 2)
    $g.DrawImage($Image, $x, $y, $w, $h)
    $g.Dispose()
    $canvas.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $canvas.Dispose()
    Write-Output "  wrote $OutPath ($Width x $Height)"
}

# App icon (exe / tray / Start Menu / Setup.exe) from the application logo.
# Any solid white background around the tile is made transparent first.
$iconImg = New-Object System.Drawing.Bitmap($iconSource)
try {
    Remove-BorderBackground -Bmp $iconImg -Threshold 235
    Write-Ico -Image $iconImg -OutPath (Join-Path $branding "app-logo.ico") -Sizes @(16,24,32,48,64,128,256)
}
finally {
    $iconImg.Dispose()
}

# Installer wizard images (shown DURING installation) from the brand logo.
$brandImg = [System.Drawing.Image]::FromFile($brandLogo)
try {
    Write-WizardBmp -Image $brandImg -OutPath (Join-Path $branding "installer-sidebar.bmp") -Width 164 -Height 314 -Fill 0.80
    Write-WizardBmp -Image $brandImg -OutPath (Join-Path $branding "installer-small.bmp") -Width 55 -Height 55 -Fill 0.90
}
finally {
    $brandImg.Dispose()
}

if ($usingStandIn) {
    Write-Warning "App icon is a STAND-IN (brand logo). Add app-logo.png and re-run for the final app icon."
}
Write-Output "Branding assets generated in: $branding"
