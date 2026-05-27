#Requires -Version 5.1
<#
.SYNOPSIS
    Installs the current source tree for the Revit 2026 live-smoke path.
.DESCRIPTION
    This Windows PowerShell handoff is intentionally small: it runs the normal
    source-tree installer for Revit 2026, then prints the WSL verification
    command that proves the installed Windows CLI/Add-in commit matches source
    HEAD after Revit is restarted.

    Run it from Windows PowerShell. If Revit is running, the installer updates
    the CLI immediately and stages the Add-in for the next Revit restart.
#>
param(
    [string]$Revit2026InstallDir = $(if ($env:REVITCLI_REVIT2026_INSTALL_DIR) { $env:REVITCLI_REVIT2026_INSTALL_DIR } else { "D:\revit2026\Revit 2026" }),

    [switch]$AllowRunningRevit
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$installRoot = $repoRoot

if ($repoRoot.StartsWith("\\", [System.StringComparison]::Ordinal)) {
    $snapshotRoot = Join-Path $env:LOCALAPPDATA "RevitCli\current-source-snapshot"
    Write-Host "Source tree is on a UNC path; mirroring to $snapshotRoot before Windows dotnet build ..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path (Split-Path -Parent $snapshotRoot) -Force | Out-Null

    & robocopy $repoRoot $snapshotRoot /MIR /XD ".artifacts" ".codex" "bin" "obj" /XF "*.user" /NFL /NDL /NJH /NJS /NP | Out-Host
    if ($LASTEXITCODE -gt 7) {
        throw "robocopy current source snapshot failed with exit code $LASTEXITCODE"
    }

    $installRoot = $snapshotRoot
}

Set-Location -LiteralPath $installRoot

$installArgs = @{
    RevitYears = @("2026")
    Revit2026InstallDir = $Revit2026InstallDir
    Force = $true
}
if ($AllowRunningRevit) {
    $installArgs.AllowRunningRevit = $true
}

& (Join-Path $installRoot "scripts\install.ps1") @installArgs

Write-Host ""
Write-Host "Next:" -ForegroundColor Cyan
Write-Host "  1. Restart Revit 2026 if the installer staged the Add-in." -ForegroundColor Cyan
Write-Host "  2. From WSL, run:" -ForegroundColor Cyan
Write-Host "     scripts/smoke-revit-wsl.sh --require-current-source" -ForegroundColor Cyan
