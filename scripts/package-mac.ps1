<#
.SYNOPSIS
    Builds and packages the macOS menu-bar app (AI.ChatRTLFixer.Mac) as a
    double-clickable .app bundle, zipped per architecture.

.DESCRIPTION
    Publishes self-contained builds for osx-arm64 (Apple Silicon) and osx-x64
    (Intel), assembles each into a minimal .app bundle (Info.plist +
    Contents/MacOS), and zips them.

    IMPORTANT — cross-compiled from Windows:
      - This script can only be run here; it has never been executed or
        launched on a real Mac, because this environment has no macOS
        machine or Xcode. `dotnet publish` for osx-arm64/osx-x64 is
        officially supported cross-OS (pure managed output, no native
        toolchain step), so the binaries themselves should be valid — but
        that has NOT been verified by actually running them.
      - No code signing or notarization: neither is possible without a Mac
        (codesign/notarytool are Xcode-only tools) and there is no Apple
        Developer ID yet. First launch on a real Mac will show Gatekeeper's
        "unidentified developer" warning; the user must right-click > Open
        (or System Settings > Privacy & Security > Open Anyway) once.
      - Windows has no concept of a Unix executable bit, so a plain zip of
        these files would extract as non-executable on macOS. This script
        manually stamps the Unix file-mode bits into the zip's external
        attributes (0755 for the main binary, 0644 elsewhere) so `open` /
        double-click works after unzip without an extra `chmod`.
#>
param(
    [switch] $SkipPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$version = "1.0.2"
$bundleName = "AI Chat RTL Fixer.app"
$executableName = "AI.ChatRTLFixer.Mac"
$bundleId = "com.aichatrtlfixer.mac"
$project = Join-Path $root "src\AI.ChatRTLFixer.Mac\AI.ChatRTLFixer.Mac.csproj"
$distRoot = Join-Path $root "dist\mac"

function New-InfoPlist([string]$path) {
    $content = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>AI Chat RTL Fixer</string>
    <key>CFBundleDisplayName</key>
    <string>AI Chat RTL Fixer</string>
    <key>CFBundleIdentifier</key>
    <string>$bundleId</string>
    <key>CFBundleVersion</key>
    <string>$version</string>
    <key>CFBundleShortVersionString</key>
    <string>$version</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>$executableName</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>LSUIElement</key>
    <true/>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
"@
    # Windows PowerShell 5.1 has no utf8NoBOM Set-Content encoding, and a BOM
    # at the top of Info.plist would break macOS's plist parser.
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($path, $content, $utf8NoBom)
}

# Zips a directory into a macOS-valid archive: paths use '/', and the Unix
# executable bit is stamped into each entry's external attributes since
# Windows has no such bit to preserve.
function New-MacZip([string]$sourceDir, [string]$zipPath, [string]$executableRelativePath) {
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $files = Get-ChildItem -Path $sourceDir -Recurse -File
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($sourceDir.Length + 1).Replace('\', '/')
            $entry = $zip.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
            $entryStream = $entry.Open()
            $fileStream = [System.IO.File]::OpenRead($file.FullName)
            try { $fileStream.CopyTo($entryStream) }
            finally { $fileStream.Dispose(); $entryStream.Dispose() }

            $isExecutable = $relative -eq $executableRelativePath
            # High 16 bits of ExternalAttributes = Unix mode. S_IFREG (0100000)
            # plus 0755 (rwxr-xr-x) or 0644 (rw-r--r--).
            $mode = if ($isExecutable) { 0x81ED } else { 0x81A4 }
            $entry.ExternalAttributes = ($mode -shl 16)
        }
    }
    finally { $zip.Dispose() }

    Set-ZipUnixHost -zipPath $zipPath
}

# .NET's ZipArchive stamps "version made by" with the host OS it ran on
# (Windows/FAT = 0), so unzip tools on macOS ignore the Unix-mode bits we
# just set in ExternalAttributes — that field's meaning is host-dependent
# per the ZIP spec. This walks the central directory (after the archive is
# closed, so offsets are final) and rewrites the host-OS byte of each
# "version made by" field to 3 (Unix), the same patch cross-platform zip
# tools apply when building macOS-bound archives on a non-Unix host.
function Set-ZipUnixHost([string]$zipPath) {
    $bytes = [System.IO.File]::ReadAllBytes($zipPath)

    # End Of Central Directory record is fixed-size (22 bytes) when the
    # archive has no trailing comment, which is true for archives .NET
    # itself created (as this one was, immediately above).
    $eocd = $bytes.Length - 22
    if ($eocd -lt 0 -or [BitConverter]::ToUInt32($bytes, $eocd) -ne 0x06054b50) {
        throw "Unexpected zip layout: End Of Central Directory record not found for $zipPath"
    }
    $entryCount = [BitConverter]::ToUInt16($bytes, $eocd + 10)
    $cdOffset = [BitConverter]::ToUInt32($bytes, $eocd + 16)

    $pos = [int]$cdOffset
    for ($i = 0; $i -lt $entryCount; $i++) {
        if ([BitConverter]::ToUInt32($bytes, $pos) -ne 0x02014b50) {
            throw "Unexpected zip layout: central directory entry $i has a bad signature in $zipPath"
        }
        $bytes[$pos + 5] = 3  # host OS byte of "version made by" -> Unix
        $nameLen = [BitConverter]::ToUInt16($bytes, $pos + 28)
        $extraLen = [BitConverter]::ToUInt16($bytes, $pos + 30)
        $commentLen = [BitConverter]::ToUInt16($bytes, $pos + 32)
        $pos += 46 + $nameLen + $extraLen + $commentLen
    }

    [System.IO.File]::WriteAllBytes($zipPath, $bytes)
}

$targets = @(
    @{ Rid = "osx-arm64"; Label = "apple-silicon" },
    @{ Rid = "osx-x64";   Label = "intel" }
)

New-Item -ItemType Directory -Force -Path $distRoot | Out-Null

foreach ($target in $targets) {
    $rid = $target.Rid
    $label = $target.Label
    $publishDir = Join-Path $distRoot "publish-$rid"
    $bundleRoot = Join-Path $distRoot "bundle-$rid"
    $appDir = Join-Path $bundleRoot $bundleName

    if (-not $SkipPublish) {
        Write-Output "Publishing $rid (self-contained)..."
        dotnet publish $project -c Release -r $rid --self-contained true -o $publishDir `
            /p:PublishSingleFile=false /p:IncludeNativeLibrariesForSelfExtract=false
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }
    }

    if (Test-Path $bundleRoot) { Remove-Item $bundleRoot -Recurse -Force }
    $macosDir = Join-Path $appDir "Contents\MacOS"
    $resourcesDir = Join-Path $appDir "Contents\Resources"
    New-Item -ItemType Directory -Force -Path $macosDir | Out-Null
    New-Item -ItemType Directory -Force -Path $resourcesDir | Out-Null

    Copy-Item -Path (Join-Path $publishDir '*') -Destination $macosDir -Recurse -Force
    New-InfoPlist -path (Join-Path $appDir "Contents\Info.plist")

    $zipPath = Join-Path $distRoot "AIChatRTLFixer-$version-macos-$label.zip"
    Write-Output "Packaging $zipPath ..."
    New-MacZip -sourceDir $bundleRoot -zipPath $zipPath -executableRelativePath "$bundleName/Contents/MacOS/$executableName"

    $hash = Get-FileHash -Algorithm SHA256 $zipPath
    $hashLine = "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $zipPath)
    Set-Content -Path "$zipPath.sha256" -Value $hashLine -Encoding ascii
    Write-Output "Packaged: $zipPath"
    Write-Output "SHA-256: $($hash.Hash.ToLowerInvariant())"
}

Write-Output ""
Write-Output "Done. Both zips are UNSIGNED and UNNOTARIZED (no Apple Developer ID configured)."
Write-Output "On first launch, macOS Gatekeeper will block them; users must right-click the app > Open once,"
Write-Output "or run: xattr -cr '/path/to/AI Chat RTL Fixer.app'"
