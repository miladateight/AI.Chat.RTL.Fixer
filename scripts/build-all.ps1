<#
.SYNOPSIS
    Full pipeline: build, test, branding, both portable outputs, and installer.

.DESCRIPTION
    Runs everything needed for a release from a clean checkout:
      1. dotnet build (Release)
      2. dotnet test  (Release)
      3. make-branding (app-logo.ico + wizard images)
      4. publish both portable outputs (framework-dependent + self-contained)
      5. compile the Windows installer

    Pass -SkipTests to skip the test step.
#>
param(
    [switch] $SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Output "== 1/5 build =="
& dotnet build ".\AI.ChatRTLFixer.sln" --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipTests) {
    Write-Output "== 2/5 test =="
    & dotnet test ".\AI.ChatRTLFixer.sln" --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Output "== 3/5 branding =="
& "$PSScriptRoot\make-branding.ps1"

Write-Output "== 4/5 publish (both portable outputs) =="
& "$PSScriptRoot\publish.ps1"

Write-Output "== 5/5 installer =="
& "$PSScriptRoot\package-installer.ps1" -SkipPublish

Write-Output ""
Write-Output "All done. Outputs under dist\:"
Write-Output "  dist\portable-framework-dependent\"
Write-Output "  dist\portable-self-contained-win-x64\"
Write-Output "  dist\installer\AIChatRTLFixerSetup-1.0.1.exe"
