<#
.SYNOPSIS
    Publishes the AI Chat RTL Fixer tray app as two portable outputs under dist/.

.DESCRIPTION
    Produces:
        dist/portable-framework-dependent/   (small; needs .NET 8 Desktop Runtime)
        dist/portable-self-contained-win-x64/ (larger; no runtime prerequisite)

    Pass -SelfContainedOnly or -FrameworkDependentOnly to build just one.
#>
param(
    [switch] $SelfContainedOnly,
    [switch] $FrameworkDependentOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$dotnet = "dotnet"
$proj = ".\src\AI.ChatRTLFixer.Tray\AI.ChatRTLFixer.Tray.csproj"
$distRoot = Join-Path $root "dist"

function Invoke-Publish {
    param([string] $Name, [bool] $SelfContained, [bool] $Compress)

    $outDir = Join-Path $distRoot $Name
    if (Test-Path $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    Write-Output "Publishing $Name (self-contained=$SelfContained)..."
    & $dotnet publish $proj `
        --configuration Release `
        --runtime win-x64 `
        --self-contained:$SelfContained `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=$Compress `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:SatelliteResourceLanguages=en `
        --output $outDir
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $Name" }

    Get-ChildItem -LiteralPath $outDir -Recurse -Filter "*.pdb" -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    # Bundle the font OFL license and a QuickStart next to the exe.
    Copy-Item (Join-Path $root "assets\fonts\OFL.txt") (Join-Path $outDir "Vazirmatn-OFL.txt") -Force
    Set-Content -Path (Join-Path $outDir "QuickStart.txt") -Encoding utf8 -Value @"
AI Chat RTL Fixer - portable build ($Name)

Quick start:
1. Run AI.ChatRTLFixer.Tray.exe (no install needed).
2. A tray icon appears in the notification area (bottom-right).
3. Right-click the tray icon for the menu; double-click to open Settings.
4. In Settings choose per-app enable, font and copy mode.
5. To exit: right-click the tray icon -> Exit.

Settings: %APPDATA%\AIChatRTLFixer\settings.json
Logs:     %APPDATA%\AIChatRTLFixer\logs\rtlfixer.log

Note: app-profile status is not "Stable" yet. Detection is separate from a
verified fix. See docs/TESTPLAN.md to verify a profile against a real install.
"@

    $exe = Join-Path $outDir "AI.ChatRTLFixer.Tray.exe"
    if (-not (Test-Path $exe)) { throw "expected exe not produced: $exe" }
    $size = "{0:N1} MB" -f ((Get-Item $exe).Length / 1MB)
    Write-Output "  -> $exe ($size)"
}

if (-not (Test-Path $distRoot)) { New-Item -ItemType Directory -Force -Path $distRoot | Out-Null }

if (-not $SelfContainedOnly) {
    Invoke-Publish -Name "portable-framework-dependent" -SelfContained $false -Compress $false
}
if (-not $FrameworkDependentOnly) {
    Invoke-Publish -Name "portable-self-contained-win-x64" -SelfContained $true -Compress $true
}

Write-Output "Done. Portable outputs are under: $distRoot"
