<#
.SYNOPSIS
    Compiles the Windows installer for AI Chat RTL Fixer with Inno Setup.

.DESCRIPTION
    Regenerates branding, ensures a self-contained portable build exists, then
    compiles installer\AI.ChatRTLFixer.iss into dist\installer\.

    Inno Setup compiler (ISCC.exe) is located in this order:
      1. .tools\InnoSetup\ISCC.exe  (bundled with the repo)
      2. iscc on PATH
      3. Program Files (x86)\Inno Setup 6\ISCC.exe
      4. the Uninstall registry (any installed Inno Setup)
#>
param(
    [switch] $SkipPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Branding + a self-contained portable build are inputs to the installer.
# (Child scripts use -ErrorActionPreference Stop and throw on failure.)
& "$PSScriptRoot\make-branding.ps1"

if (-not $SkipPublish) {
    & "$PSScriptRoot\publish.ps1" -SelfContainedOnly
}

$sourceDir = Join-Path $root "dist\portable-self-contained-win-x64"
if (-not (Test-Path (Join-Path $sourceDir "AI.ChatRTLFixer.Tray.exe"))) {
    throw "Self-contained portable build not found in $sourceDir. Run scripts\publish.ps1 first."
}

function Resolve-Iscc {
    $bundled = Join-Path $root ".tools\InnoSetup\ISCC.exe"
    if (Test-Path $bundled) { return $bundled }

    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $default = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (Test-Path $default) { return $default }

    $registryRoots = @(
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )
    $installLocation = Get-ItemProperty $registryRoots -ErrorAction SilentlyContinue |
        Where-Object {
            ([string]($_.PSObject.Properties["DisplayName"].Value)) -like "*Inno Setup*" -and
            -not [string]::IsNullOrWhiteSpace([string]($_.PSObject.Properties["InstallLocation"].Value))
        } |
        Select-Object -First 1 -ExpandProperty InstallLocation
    if (-not [string]::IsNullOrWhiteSpace($installLocation)) {
        $candidate = Join-Path $installLocation "ISCC.exe"
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

$iscc = Resolve-Iscc
if ($null -eq $iscc) {
    throw "Inno Setup compiler (ISCC.exe) was not found. Install Inno Setup 6 from https://jrsoftware.org/isdl.php, then re-run."
}
Write-Output "Using Inno Setup compiler: $iscc"

$installerOut = Join-Path $root "dist\installer"
New-Item -ItemType Directory -Force -Path $installerOut | Out-Null

& $iscc ".\installer\AI.ChatRTLFixer.iss"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$installer = Join-Path $installerOut "AIChatRTLFixerSetup-0.4.0.exe"
if (Test-Path $installer) {
    $hash = Get-FileHash -Algorithm SHA256 $installer
    $hashLine = "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $installer)
    Set-Content -Path "$installer.sha256" -Value $hashLine -Encoding ascii
    Write-Output "Packaged installer: $installer"
    Write-Output "SHA-256: $($hash.Hash.ToLowerInvariant())"
}
else {
    throw "Expected installer was not produced: $installer"
}
