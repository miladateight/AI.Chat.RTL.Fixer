<#
.SYNOPSIS
    Builds a single universal (Apple Silicon + Intel) .pkg installer for the
    macOS menu-bar app (AI.ChatRTLFixer.Mac).

.DESCRIPTION
    Publishes self-contained builds for osx-arm64 and osx-x64, merges every
    native binary between them into one universal (fat) copy via `lipo`,
    assembles a single .app bundle from that merge, ad-hoc signs it, and
    packages it as one .pkg — so there's one download that installs
    correctly on either kind of Mac, not a separate file per architecture.

    IMPORTANT — packaging steps only run on macOS:
      - `dotnet publish` for osx-arm64/osx-x64 works fine cross-OS (pure
        managed output), so this script runs on Windows too — but `lipo`,
        `codesign` and `pkgbuild` are macOS-only tools. On Windows this
        script only produces the two separate publish folders for a quick
        compile sanity check; the actual universal .pkg is only ever
        produced when this same script runs on a real Mac (the
        build-macos.yml CI job).
      - Ad-hoc signing is free and needs no Apple account, but it is NOT
        notarization: first launch on a real Mac will still show
        Gatekeeper's "unidentified developer" warning until the app is
        signed with a paid Developer ID and notarized. Users must right-
        click > Open (or System Settings > Privacy & Security > Open
        Anyway) once.
#>
param(
    [switch] $SkipPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Read the version from Directory.Build.props, the single place it is declared.
# Hardcoding it here meant a bumped release still stamped the previous version
# into Info.plist and into the .pkg filename.
$propsPath = Join-Path $root "Directory.Build.props"
$versionMatch = Select-String -Path $propsPath -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
if (-not $versionMatch) { throw "Could not read <Version> from $propsPath." }
$version = $versionMatch.Matches[0].Groups[1].Value
Write-Output "Packaging version: $version"
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

# Combines the osx-arm64 and osx-x64 publish outputs into one universal
# folder. Native Mach-O files (the apphost, CoreCLR, SkiaSharp,
# AvaloniaNative, ...) are merged with `lipo` into a single fat binary that
# runs on either architecture; everything else (managed IL DLLs, JSON,
# resources) is architecture-neutral and identical between the two publishes,
# so it's just copied once.
function Merge-Universal([string]$armDir, [string]$x64Dir, [string]$outDir) {
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $armFiles = Get-ChildItem -Path $armDir -Recurse -File
    foreach ($file in $armFiles) {
        $relative = $file.FullName.Substring($armDir.Length + 1)
        $x64Path = Join-Path $x64Dir $relative
        $outPath = Join-Path $outDir $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $outPath) | Out-Null

        if (Test-Path $x64Path) {
            # lipo fails on non-Mach-O input (managed DLLs, JSON, etc.) —
            # that failure is the expected, common case here, not an error;
            # those files are identical between RIDs, so just copy one.
            & lipo -create $file.FullName $x64Path -output $outPath 2>$null
            if ($LASTEXITCODE -ne 0) {
                Copy-Item $file.FullName $outPath -Force
            }
        }
        else {
            Copy-Item $file.FullName $outPath -Force
        }
    }

    # Anything present only on the x64 side (shouldn't normally happen for a
    # like-for-like self-contained publish, but don't silently drop it).
    $x64Files = Get-ChildItem -Path $x64Dir -Recurse -File
    foreach ($file in $x64Files) {
        $relative = $file.FullName.Substring($x64Dir.Length + 1)
        $outPath = Join-Path $outDir $relative
        if (-not (Test-Path $outPath)) {
            New-Item -ItemType Directory -Force -Path (Split-Path $outPath) | Out-Null
            Copy-Item $file.FullName $outPath -Force
        }
    }
}

$armPublishDir = Join-Path $distRoot "publish-osx-arm64"
$x64PublishDir = Join-Path $distRoot "publish-osx-x64"

New-Item -ItemType Directory -Force -Path $distRoot | Out-Null

if (-not $SkipPublish) {
    foreach ($pair in @(@{ Rid = "osx-arm64"; Out = $armPublishDir }, @{ Rid = "osx-x64"; Out = $x64PublishDir })) {
        Write-Output "Publishing $($pair.Rid) (self-contained)..."
        dotnet publish $project -c Release -r $pair.Rid --self-contained true -o $pair.Out `
            /p:PublishSingleFile=false /p:IncludeNativeLibrariesForSelfExtract=false
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($pair.Rid)" }
    }
}

# `lipo` (and codesign/pkgbuild below) only exist on macOS. This is the
# reliable signal for "am I actually able to build the universal package
# here" — checking the tool directly instead of sniffing the OS avoids the
# kind of silent-skip bug a $PSVersionTable check caused before.
$lipoCmd = Get-Command lipo -ErrorAction SilentlyContinue
if (-not $lipoCmd) {
    Write-Output "lipo not found on PATH - publishing only (expected when running on Windows)."
    Write-Output "Publish output: $armPublishDir and $x64PublishDir"
    return
}

$bundleRoot = Join-Path $distRoot "bundle-universal"
$appDir = Join-Path $bundleRoot $bundleName
$macosDir = Join-Path $appDir "Contents\MacOS"
$resourcesDir = Join-Path $appDir "Contents\Resources"

Write-Output "Merging osx-arm64 + osx-x64 into a universal build..."
Merge-Universal -armDir $armPublishDir -x64Dir $x64PublishDir -outDir $macosDir
New-Item -ItemType Directory -Force -Path $resourcesDir | Out-Null
New-InfoPlist -path (Join-Path $appDir "Contents\Info.plist")

# A self-contained .NET publish bundles native libraries (CoreCLR,
# SkiaSharp, HarfBuzzSharp, AvaloniaNative) with no unified signature
# covering the app as a whole. On Apple Silicon the kernel refuses to
# execute code with no valid signature at all — separate from and stricter
# than Gatekeeper's "unidentified developer" prompt. `--deep` signs every
# embedded binary and folds them under one ad-hoc signature ("-" =
# self-signed, no Apple Developer ID needed and free).
$codesignCmd = Get-Command codesign -ErrorAction SilentlyContinue
if ($codesignCmd) {
    Write-Output "Ad-hoc signing $appDir ..."
    & codesign --force --deep --sign - $appDir
    if ($LASTEXITCODE -ne 0) { throw "codesign failed for $appDir" }
    & codesign --verify --deep --strict $appDir
    if ($LASTEXITCODE -ne 0) { throw "codesign verification failed for $appDir" }
    Write-Output "Signature verified for $appDir."
}

$pkgbuildCmd = Get-Command pkgbuild -ErrorAction SilentlyContinue
if ($pkgbuildCmd) {
    $pkgPath = Join-Path $distRoot "AIChatRTLFixer-$version-macos.pkg"
    Write-Output "Building $pkgPath ..."
    & pkgbuild --root $bundleRoot --identifier $bundleId --version $version --install-location /Applications $pkgPath
    if ($LASTEXITCODE -ne 0) { throw "pkgbuild failed" }

    $hash = Get-FileHash -Algorithm SHA256 $pkgPath
    Write-Output "Packaged: $pkgPath"
    Write-Output "SHA-256: $($hash.Hash.ToLowerInvariant())"
}

Write-Output ""
Write-Output "Done. Universal (arm64 + x64) build, ad-hoc signed; still UNNOTARIZED (no Apple Developer ID configured)."
Write-Output "On first launch, macOS Gatekeeper will block it; users must right-click the .pkg > Open once,"
Write-Output "or allow it via System Settings > Privacy & Security > Open Anyway."
