param(
    [string]$ProjectDir = ".",
    [string]$RevitYear = "2026",
    [string]$IssueProfile = ".revitcli/issue.yml",
    [string]$Baseline = ".revitcli/history/baseline.json",
    [string]$BundlePath = "deliverables/issue-package.zip"
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = "Stop"

function Invoke-RevitCliDemoStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string[]]$Command
    )

    Write-Host ""
    Write-Host "== $Name =="
    Write-Host ("revitcli " + ($Command -join " "))
    & revitcli @Command
    if ($LASTEXITCODE -ne 0) {
        throw "Step '$Name' failed with exit code $LASTEXITCODE."
    }
}

$ProjectRoot = Resolve-Path $ProjectDir
Push-Location $ProjectRoot
try {
    if (-not (Test-Path $IssueProfile)) {
        throw "Issue profile not found: $IssueProfile. Copy profiles/v5-issue.yml to .revitcli/issue.yml first."
    }

    Invoke-RevitCliDemoStep "doctor" @("doctor", "--check-version", $RevitYear)
    Invoke-RevitCliDemoStep "status" @("status", "--output", "json")
    Invoke-RevitCliDemoStep "workbench v2" @("workbench", "verify", "--contract", "workbench-contract.v2", "--dir", ".", "--output", "markdown")
    Invoke-RevitCliDemoStep "issue preflight" @("issue", "preflight", "--profile", $IssueProfile, "--output", "markdown", "--fail-on", "warning")

    if (Test-Path $Baseline) {
        Invoke-RevitCliDemoStep "issue diff" @("issue", "diff", "--from", $Baseline, "--to", "current", "--review", "--output", "markdown")
    } else {
        Write-Host ""
        Write-Host "== issue diff =="
        Write-Host "Skipping diff because baseline is missing: $Baseline"
        Write-Host "Create one with: revitcli snapshot --output $Baseline"
    }

    Invoke-RevitCliDemoStep "issue package dry-run" @("issue", "package", "--profile", $IssueProfile, "--bundle-path", $BundlePath, "--dry-run", "--include-receipts", "true", "--sign-journal", "--output", "markdown")
}
finally {
    Pop-Location
}
