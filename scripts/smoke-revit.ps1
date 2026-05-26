#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the internal Revit real-usability smoke slice for a selected Revit version.
.DESCRIPTION
    Validates the intended baseline chain:
    doctor -> status -> query --id -> query <category> --filter ->
    set --dry-run -> set -> query confirm -> restore.

    The filter must match exactly one element and should not depend on the
    parameter being written, so the restore command can target the same element.
    Pass -V4Workbench to add the v4 terminal contract gate plus live read-only
    inspect/schedule discovery against the active Revit document.
.EXAMPLE
    .\scripts\smoke-revit.ps1 `
      -Version 2026 `
      -ElementId 12345 `
      -RevitInstallDir 'D:\revit2026\Revit 2026' `
      -Category walls `
      -Filter 'Mark = W-01' `
      -Param Comments `
      -Value 'revitcli smoke' `
      -Apply `
      -V4Workbench
#>
param(
    [ValidateSet("2024", "2025", "2026")]
    [string]$Version = "2026",

    [Parameter(Mandatory = $true)]
    [long]$ElementId,

    [string]$Category = "walls",

    [Parameter(Mandatory = $true)]
    [string]$Filter,

    [string]$Param = "Comments",

    [string]$Value = "revitcli-smoke",

    [string]$RevitCli = "revitcli",

    [string]$RevitInstallDir = "",

    [switch]$Apply,

    [switch]$FixDryRun,

    [switch]$FixApply,

    [string]$FixCheckName = "default",

    [string]$FixProfile = "",

    [string]$OutputPath = "",

    [switch]$V4Workbench,

    [string]$V4ProjectDir = "",

    [switch]$V5IssueClosure,

    [string]$V5ProjectDir = "",

    [string]$V5IssueProfile = "",

    [string]$V5IssueBundlePath = "",

    [string]$V5SheetSelector = "",

    [string]$V5IssueCode = "",

    [string]$V5IssueDate = "",

    [string]$V5SheetPlanPath = "",

    [string]$V5SheetParamMap = "",

    [switch]$V5ApplySheetIssue,

    [switch]$V5WriteIssuePackage,

    [switch]$V52SchedulePackage,

    [string]$V52ScheduleSet = "",

    [string]$V52ScheduleOutputDir = "",

    [string]$V52ScheduleManifestPath = "",

    [string]$V52ScheduleCompareBaselineDir = "",

    [string]$V52ScheduleCompareKeys = "",

    [string]$V52DeliverablesBundlePath = "",

    [switch]$V52WriteDeliverablesBundle,

    [switch]$V6LedgerReplayApply,

    [string]$V6LedgerProjectDir = ""
)

$ErrorActionPreference = "Stop"
$SemVerPattern = '^(?:v)?(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'

function Resolve-RevitInstallDir {
    param([string]$Version, [string]$OverridePath)
    if (-not [string]::IsNullOrWhiteSpace($OverridePath)) { return $OverridePath }

    $revitCliOverride = [Environment]::GetEnvironmentVariable("REVITCLI_REVIT${Version}_INSTALL_DIR")
    if (-not [string]::IsNullOrWhiteSpace($revitCliOverride)) { return $revitCliOverride }

    $autodeskOverride = [Environment]::GetEnvironmentVariable("Revit${Version}InstallDir")
    if (-not [string]::IsNullOrWhiteSpace($autodeskOverride)) { return $autodeskOverride }

    return (Join-Path $env:ProgramFiles "Autodesk\Revit $Version")
}

function Assert-FileExists {
    param([string]$Path, [string]$Label)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label missing: $Path"
    }
}

function Get-AssemblyVersion {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        return ""
    }

    try {
        $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
        if ($versionInfo.ProductVersion) {
            return $versionInfo.ProductVersion.Trim()
        }
    } catch {
        return ""
    }

    return ""
}

function Resolve-ManifestAssemblyPath {
    param([string]$ManifestPath)

    [xml]$manifestXml = Get-Content -Raw -LiteralPath $ManifestPath
    $assembly = @($manifestXml.RevitAddIns.AddIn) |
        ForEach-Object { $_.Assembly } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($assembly)) {
        return ""
    }

    if ([System.IO.Path]::IsPathRooted($assembly)) {
        return $assembly
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $ManifestPath) $assembly))
}

function Get-CliVersionMetadata {
    param([string]$Command)

    try {
        $output = & $Command --version 2>&1
        $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
    } catch {
        return [pscustomobject]@{
            Version = ""
            Error = "Failed to run '$Command --version': $($_.Exception.Message)"
        }
    }

    $text = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    if ($exitCode -ne 0) {
        return [pscustomobject]@{
            Version = ""
            Error = "'$Command --version' exited $exitCode`: $text"
        }
    }

    foreach ($line in ($text -split "`r?`n")) {
        if ($line -match '^revitcli\s+(.+)$') {
            $version = $Matches[1].Trim()
            if ($version -notmatch $SemVerPattern) {
                return [pscustomobject]@{
                    Version = ""
                    Error = "'$Command --version' returned non-SemVer version: $version"
                }
            }

            return [pscustomobject]@{
                Version = $version
                Error = ""
            }
        }
    }

    return [pscustomobject]@{
        Version = ""
        Error = "'$Command --version' did not return a 'revitcli <version>' line: $text"
    }
}

function Test-TransientRevitCliFailure {
    param([int]$ExitCode, [string]$Text)

    if ($ExitCode -eq 0) { return $false }

    return $Text -match 'HttpClient\.Timeout' -or
        $Text -match 'Communication error' -or
        $Text -match 'request was canceled' -or
        $Text -match 'TaskCanceledException'
}

function Test-RevitCliSmokeCommandCanRetry {
    param([string[]]$CommandArgs)

    if ($CommandArgs.Count -eq 0) { return $true }

    switch ($CommandArgs[0]) {
        "set" { return $CommandArgs -contains "--dry-run" }
        "fix" { return $CommandArgs -contains "--dry-run" }
        "rollback" { return $false }
        "export" { return $false }
        "publish" { return $false }
        "issue" {
            if ($CommandArgs.Count -gt 1 -and $CommandArgs[1] -eq "package") {
                return $CommandArgs -contains "--dry-run"
            }

            return $true
        }
        "plan" { return -not ($CommandArgs -contains "apply") }
        "schedule" {
            if ($CommandArgs.Count -gt 1 -and $CommandArgs[1] -eq "create") {
                return $CommandArgs -contains "--dry-run"
            }

            return $true
        }
        default { return $true }
    }
}

function Invoke-RevitCliSmoke {
    param(
        [string[]]$CommandArgs,
        [int[]]$ExpectedExitCodes = @(0),
        [int]$MaxAttempts = 2,
        [int]$RetryDelaySeconds = 2
    )

    $canRetry = Test-RevitCliSmokeCommandCanRetry -CommandArgs $CommandArgs
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $output = & $RevitCli @CommandArgs 2>&1
        $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
        $text = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        $entry = [ordered]@{
            command = "$RevitCli $($CommandArgs -join ' ')"
            attempt = $attempt
            retrySafe = $canRetry
            exitCode = $exitCode
            output = $text
        }
        $script:Steps.Add($entry) | Out-Null

        if ($ExpectedExitCodes -contains $exitCode) {
            return $text
        }

        if ($canRetry -and
            $attempt -lt $MaxAttempts -and
            (Test-TransientRevitCliFailure -ExitCode $exitCode -Text $text)) {
            Start-Sleep -Seconds $RetryDelaySeconds
            continue
        }

        throw "Command failed with exit code ${exitCode}: $($entry.command)`n$text"
    }

    throw "Command failed after $MaxAttempts attempts: $RevitCli $($CommandArgs -join ' ')"
}

function Convert-JsonArray {
    param([string]$Json, [string]$Label)
    try {
        $value = $Json | ConvertFrom-Json
    } catch {
        throw "$Label did not return valid JSON: $($_.Exception.Message)`n$Json"
    }

    if ($null -eq $value) {
        throw "$Label returned empty JSON."
    }

    if ($value -is [array]) { return ,@($value) }
    return ,@($value)
}

function Convert-JsonObject {
    param([string]$Json, [string]$Label)
    try {
        $value = $Json | ConvertFrom-Json
    } catch {
        throw "$Label did not return valid JSON: $($_.Exception.Message)`n$Json"
    }

    if ($null -eq $value) {
        throw "$Label returned empty JSON."
    }

    if ($value -is [array]) {
        throw "$Label returned a JSON array; expected an object."
    }

    return $value
}

function Normalize-SmokeVersion {
    param([string]$Version)

    if ([string]::IsNullOrWhiteSpace($Version)) {
        return ""
    }

    $trimmed = $Version.Trim()
    if ($trimmed.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
        $trimmed = $trimmed.Substring(1)
    }

    $buildIndex = $trimmed.IndexOf("+", [System.StringComparison]::Ordinal)
    if ($buildIndex -ge 0) {
        $trimmed = $trimmed.Substring(0, $buildIndex)
    }

    return $trimmed
}

function Assert-VersionMatch {
    param(
        [string]$CliVersion,
        [string]$InstalledAddinVersion,
        [string]$LiveAddinVersion
    )

    $cli = Normalize-SmokeVersion $CliVersion
    $installed = Normalize-SmokeVersion $InstalledAddinVersion
    $live = Normalize-SmokeVersion $LiveAddinVersion

    if ([string]::IsNullOrWhiteSpace($cli) -or
        [string]::IsNullOrWhiteSpace($installed) -or
        [string]::IsNullOrWhiteSpace($live)) {
        throw "CLI/add-in version evidence is incomplete: CLI='$CliVersion', installed='$InstalledAddinVersion', live='$LiveAddinVersion'."
    }

    if ($cli -ne $installed -or $cli -ne $live) {
        throw "CLI/add-in version mismatch: CLI='$CliVersion', installed='$InstalledAddinVersion', live='$LiveAddinVersion'. Restart Revit after install or reinstall the matching build before claiming smoke success."
    }
}

function Invoke-V4WorkbenchSmoke {
    param(
        [string]$ProjectDir,
        [string]$Version,
        [string]$Category,
        [long]$ElementId
    )

    $dirArgs = @()
    if (-not [string]::IsNullOrWhiteSpace($ProjectDir)) {
        $dirArgs += @("--dir", $ProjectDir)
    }

    $verifyArgs = @("workbench", "verify") + $dirArgs + @("--output", "json")
    $verifyJson = Invoke-RevitCliSmoke -CommandArgs $verifyArgs
    $verify = Convert-JsonObject $verifyJson "workbench verify"
    if ($verify.success -ne $true) {
        throw "workbench verify reported success=false."
    }

    $handoffArgs = @("workbench", "handoff") + $dirArgs + @("--output", "json")
    Convert-JsonObject (Invoke-RevitCliSmoke -CommandArgs $handoffArgs) "workbench handoff" | Out-Null

    $status = Convert-JsonObject (Invoke-RevitCliSmoke -CommandArgs @("status", "--output", "json")) "status --output json"
    if ([int]$status.revitYear -ne [int]$Version) {
        throw "status --output json reported Revit year $($status.revitYear), expected $Version."
    }
    if ([string]::IsNullOrWhiteSpace([string]$status.documentName)) {
        throw "status --output json reported no active document; open the controlled smoke model before v4 workbench smoke."
    }

    Convert-JsonArray (Invoke-RevitCliSmoke -CommandArgs @("inspect", "categories", "--output", "json")) "inspect categories" | Out-Null
    Convert-JsonArray (Invoke-RevitCliSmoke -CommandArgs @("inspect", "params", $Category, "--output", "json")) "inspect params" | Out-Null
    Convert-JsonArray (Invoke-RevitCliSmoke -CommandArgs @("inspect", "schedules", "--output", "json")) "inspect schedules" | Out-Null
    Convert-JsonArray (Invoke-RevitCliSmoke -CommandArgs @("inspect", "sheets", "--output", "json")) "inspect sheets" | Out-Null
    Convert-JsonArray (Invoke-RevitCliSmoke -CommandArgs @("schedule", "list", "--output", "json")) "schedule list" | Out-Null
    Convert-JsonObject (Invoke-RevitCliSmoke -CommandArgs @("schedule", "export", "--category", $Category, "--fields", "Name,Category,Type Name", "--output", "json")) "schedule export" | Out-Null
    Convert-JsonObject (Invoke-RevitCliSmoke -CommandArgs @("schedule", "create", "--category", $Category, "--fields", "Name", "--name", "RevitCli Smoke Preview", "--dry-run", "--output", "json")) "schedule create dry-run" | Out-Null
    Convert-JsonArray (Invoke-RevitCliSmoke -CommandArgs @("query", "--id", $ElementId.ToString(), "--output", "json")) "query target json" | Out-Null
}

function Resolve-SmokePath {
    param([string]$RootPath, [string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RootPath $Path))
}

function Invoke-V5IssueClosureSmoke {
    param(
        [string]$ProjectDir,
        [string]$IssueProfile,
        [string]$IssueBundlePath,
        [string]$SheetSelector,
        [string]$IssueCode,
        [string]$IssueDate,
        [string]$SheetPlanPath,
        [string]$SheetParamMap,
        [switch]$ApplySheetIssue,
        [switch]$WriteIssuePackage,
        [switch]$SchedulePackage,
        [string]$ScheduleSet,
        [string]$ScheduleOutputDir,
        [string]$ScheduleManifestPath,
        [string]$ScheduleCompareBaselineDir,
        [string]$ScheduleCompareKeys,
        [string]$DeliverablesBundlePath,
        [switch]$WriteDeliverablesBundle
    )

    if ([string]::IsNullOrWhiteSpace($ProjectDir)) {
        $ProjectDir = (Get-Location).Path
    } else {
        $ProjectDir = [System.IO.Path]::GetFullPath($ProjectDir)
    }

    if (-not (Test-Path -LiteralPath $ProjectDir -PathType Container)) {
        throw "v5 issue-closure project directory missing: $ProjectDir"
    }

    if ([string]::IsNullOrWhiteSpace($IssueProfile)) {
        throw "-V5IssueClosure requires -V5IssueProfile."
    }

    $IssueProfile = Resolve-SmokePath -RootPath $ProjectDir -Path $IssueProfile
    if (-not (Test-Path -LiteralPath $IssueProfile -PathType Leaf)) {
        throw "v5 issue profile missing: $IssueProfile"
    }

    if ([string]::IsNullOrWhiteSpace($IssueBundlePath)) {
        if ($WriteIssuePackage) {
            throw "-V5WriteIssuePackage requires an explicit -V5IssueBundlePath."
        }

        $IssueBundlePath = Join-Path $ProjectDir ".revitcli\smoke\v5-issue-closure.zip"
    } else {
        $IssueBundlePath = Resolve-SmokePath -RootPath $ProjectDir -Path $IssueBundlePath
    }

    $script:resolvedV5ProjectDir = $ProjectDir
    $script:resolvedV5IssueProfile = $IssueProfile
    $script:resolvedV5IssueBundlePath = $IssueBundlePath

    $verifyJson = Invoke-RevitCliSmoke -CommandArgs @(
        "workbench", "verify",
        "--contract", "workbench-contract.v2",
        "--dir", $ProjectDir,
        "--output", "json"
    )
    $verify = Convert-JsonObject $verifyJson "workbench verify v2"
    if ($verify.success -ne $true) {
        throw "workbench verify --contract workbench-contract.v2 reported success=false."
    }

    $hasSheetSelector = -not [string]::IsNullOrWhiteSpace($SheetSelector)
    $hasIssueCode = -not [string]::IsNullOrWhiteSpace($IssueCode)
    $hasIssueDate = -not [string]::IsNullOrWhiteSpace($IssueDate)
    $hasAllSheetInputs = $hasSheetSelector -and $hasIssueCode -and $hasIssueDate
    if (($hasSheetSelector -or $hasIssueCode -or $hasIssueDate) -and -not $hasAllSheetInputs) {
        throw "v5 sheet issue smoke requires -V5SheetSelector, -V5IssueCode, and -V5IssueDate together."
    }
    if ($ApplySheetIssue -and -not $hasAllSheetInputs) {
        throw "-V5ApplySheetIssue requires -V5SheetSelector, -V5IssueCode, and -V5IssueDate."
    }
    if ($WriteIssuePackage -and (Test-Path -LiteralPath $IssueBundlePath)) {
        throw "-V5WriteIssuePackage requires a disposable non-existing bundle path: $IssueBundlePath"
    }

    if ($hasAllSheetInputs) {
        if ([string]::IsNullOrWhiteSpace($SheetPlanPath)) {
            $SheetPlanPath = Join-Path $ProjectDir ".revitcli\plans\v5-sheet-issue-smoke.json"
        } else {
            $SheetPlanPath = Resolve-SmokePath -RootPath $ProjectDir -Path $SheetPlanPath
        }
        $script:resolvedV5SheetPlanPath = $SheetPlanPath

        if (-not [string]::IsNullOrWhiteSpace($SheetParamMap)) {
            $SheetParamMap = Resolve-SmokePath -RootPath $ProjectDir -Path $SheetParamMap
            if (-not (Test-Path -LiteralPath $SheetParamMap -PathType Leaf)) {
                throw "v5 sheet issue parameter map missing: $SheetParamMap"
            }
            $script:resolvedV5SheetParamMap = $SheetParamMap
        }

        $sheetPlanDir = Split-Path -Parent $SheetPlanPath
        if (-not [string]::IsNullOrWhiteSpace($sheetPlanDir)) {
            New-Item -ItemType Directory -Force -Path $sheetPlanDir | Out-Null
        }

        $sheetIssueArgs = @(
            "sheets", "issue-meta",
            "--selector", $SheetSelector,
            "--issue-code", $IssueCode,
            "--issue-date", $IssueDate,
            "--plan-output", $SheetPlanPath,
            "--dry-run",
            "--output", "json"
        )
        if (-not [string]::IsNullOrWhiteSpace($SheetParamMap)) {
            $sheetIssueArgs += @("--param-map", $SheetParamMap)
        }

        Convert-JsonObject (Invoke-RevitCliSmoke -CommandArgs $sheetIssueArgs) "sheets issue-meta dry-run" | Out-Null

        Convert-JsonObject (Invoke-RevitCliSmoke -CommandArgs @(
            "plan", "show", $SheetPlanPath, "--output", "json"
        )) "plan show sheet issue" | Out-Null

        Invoke-RevitCliSmoke -CommandArgs @(
            "plan", "apply", $SheetPlanPath,
            "--dry-run",
            "--max-changes", "500"
        ) | Out-Null
    }

    Convert-JsonObject (Invoke-RevitCliSmoke -CommandArgs @(
        "issue", "preflight",
        "--profile", $IssueProfile,
        "--output", "json"
    )) "issue preflight" | Out-Null

    Convert-JsonObject (Invoke-RevitCliSmoke -CommandArgs @(
        "issue", "package",
        "--profile", $IssueProfile,
        "--bundle-path", $IssueBundlePath,
        "--dry-run",
        "--sign-journal",
        "--include-receipts",
        "--output", "json"
    )) "issue package dry-run" | Out-Null

    if ($ApplySheetIssue) {
        Invoke-RevitCliSmoke -CommandArgs @(
            "plan", "apply", $SheetPlanPath,
            "--yes",
            "--max-changes", "500"
        ) | Out-Null

        $receiptPath = "$SheetPlanPath.receipt.json"
        if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
            throw "sheet issue apply completed but receipt was not found: $receiptPath"
        }

        Invoke-RevitCliSmoke -CommandArgs @(
            "rollback", $receiptPath,
            "--dry-run",
            "--max-changes", "500"
        ) | Out-Null
        Invoke-RevitCliSmoke -CommandArgs @(
            "rollback", $receiptPath,
            "--yes",
            "--max-changes", "500"
        ) | Out-Null
        Invoke-RevitCliSmoke -CommandArgs @("journal", "sign", "--dir", $ProjectDir) | Out-Null
        Invoke-RevitCliSmoke -CommandArgs @("journal", "verify", "--dir", $ProjectDir) | Out-Null
    }

    if ($WriteIssuePackage) {
        $package = Convert-JsonObject (Invoke-RevitCliSmoke -CommandArgs @(
            "issue", "package",
            "--profile", $IssueProfile,
            "--bundle-path", $IssueBundlePath,
            "--sign-journal",
            "--include-receipts",
            "--output", "json"
        )) "issue package write"

        if (-not (Test-Path -LiteralPath $IssueBundlePath -PathType Leaf)) {
            throw "issue package write completed but bundle was not found: $IssueBundlePath"
        }

        $receiptProperty = $package.PSObject.Properties |
            Where-Object { $_.Name -eq "receiptPath" } |
            Select-Object -First 1
        if ($null -ne $receiptProperty -and
            -not [string]::IsNullOrWhiteSpace([string]$receiptProperty.Value) -and
            -not (Test-Path -LiteralPath ([string]$receiptProperty.Value) -PathType Leaf)) {
            throw "issue package write reported a receipt that was not found: $($receiptProperty.Value)"
        }
    }

    if ($SchedulePackage) {
        Invoke-V52SchedulePackageSmoke `
            -ProjectDir $ProjectDir `
            -IssueProfile $IssueProfile `
            -IssueBundlePath $IssueBundlePath `
            -ScheduleSet $ScheduleSet `
            -ScheduleOutputDir $ScheduleOutputDir `
            -ScheduleManifestPath $ScheduleManifestPath `
            -ScheduleCompareBaselineDir $ScheduleCompareBaselineDir `
            -ScheduleCompareKeys $ScheduleCompareKeys `
            -DeliverablesBundlePath $DeliverablesBundlePath `
            -WriteDeliverablesBundle:$WriteDeliverablesBundle
    }
}

function Invoke-V52SchedulePackageSmoke {
    param(
        [string]$ProjectDir,
        [string]$IssueProfile,
        [string]$IssueBundlePath,
        [string]$ScheduleSet,
        [string]$ScheduleOutputDir,
        [string]$ScheduleManifestPath,
        [string]$ScheduleCompareBaselineDir,
        [string]$ScheduleCompareKeys,
        [string]$DeliverablesBundlePath,
        [switch]$WriteDeliverablesBundle
    )

    if ([string]::IsNullOrWhiteSpace($ScheduleSet)) {
        throw "-V52SchedulePackage requires -V52ScheduleSet."
    }
    if ([string]::IsNullOrWhiteSpace($ScheduleCompareKeys)) {
        throw "-V52ScheduleCompareKeys must include at least one key column."
    }
    if ([string]::IsNullOrWhiteSpace($ScheduleCompareBaselineDir)) {
        throw "-V52SchedulePackage requires -V52ScheduleCompareBaselineDir pointing to a baseline schedule export directory."
    }

    $ScheduleOutputDir = if ([string]::IsNullOrWhiteSpace($ScheduleOutputDir)) {
        Join-Path $ProjectDir ".revitcli\smoke\v5.2\schedules-current"
    } else {
        Resolve-SmokePath -RootPath $ProjectDir -Path $ScheduleOutputDir
    }
    $ScheduleManifestPath = if ([string]::IsNullOrWhiteSpace($ScheduleManifestPath)) {
        Join-Path $ScheduleOutputDir "schedule-export-manifest.json"
    } else {
        Resolve-SmokePath -RootPath $ProjectDir -Path $ScheduleManifestPath
    }
    $ScheduleCompareBaselineDir = Resolve-SmokePath -RootPath $ProjectDir -Path $ScheduleCompareBaselineDir
    if (-not (Test-Path -LiteralPath $ScheduleCompareBaselineDir -PathType Container)) {
        throw "-V52ScheduleCompareBaselineDir must point to an existing baseline schedule export directory: $ScheduleCompareBaselineDir"
    }
    $DeliverablesBundlePath = if ([string]::IsNullOrWhiteSpace($DeliverablesBundlePath)) {
        if ($WriteDeliverablesBundle) {
            throw "-V52WriteDeliverablesBundle requires an explicit -V52DeliverablesBundlePath."
        }

        Join-Path $ProjectDir ".revitcli\smoke\v5.2\deliverables-bundle.zip"
    } else {
        Resolve-SmokePath -RootPath $ProjectDir -Path $DeliverablesBundlePath
    }
    if ($WriteDeliverablesBundle -and (Test-Path -LiteralPath $DeliverablesBundlePath)) {
        throw "-V52WriteDeliverablesBundle requires a disposable non-existing bundle path: $DeliverablesBundlePath"
    }

    $script:resolvedV52ScheduleSet = $ScheduleSet
    $script:resolvedV52ScheduleOutputDir = $ScheduleOutputDir
    $script:resolvedV52ScheduleManifestPath = $ScheduleManifestPath
    $script:resolvedV52ScheduleCompareBaselineDir = $ScheduleCompareBaselineDir
    $script:resolvedV52DeliverablesBundlePath = $DeliverablesBundlePath

    $previousLocation = (Get-Location).Path
    try {
        Set-Location -LiteralPath $ProjectDir

        Convert-JsonObject (Invoke-RevitCliSmoke -CommandArgs @(
            "schedules", "batch-export",
            "--set", $ScheduleSet,
            "--output-dir", $ScheduleOutputDir,
            "--format", "csv",
            "--manifest", $ScheduleManifestPath,
            "--output", "json"
        )) "v5.2 schedules batch-export" | Out-Null

        Convert-JsonObject (Invoke-RevitCliSmoke -CommandArgs @(
            "schedules", "compare",
            "--from", $ScheduleCompareBaselineDir,
            "--to", $ScheduleOutputDir,
            "--keys", $ScheduleCompareKeys,
            "--output", "json"
        ) -ExpectedExitCodes @(0, 2)) "v5.2 schedules compare" | Out-Null

        Convert-JsonObject (Invoke-RevitCliSmoke -CommandArgs @(
            "deliverables", "verify",
            "--dir", $ProjectDir,
            "--output", "json"
        )) "v5.2 deliverables verify" | Out-Null

        Convert-JsonObject (Invoke-RevitCliSmoke -CommandArgs @(
            "deliverables", "bundle",
            "--dir", $ProjectDir,
            "--bundle-path", $DeliverablesBundlePath,
            "--dry-run",
            "--output", "json"
        )) "v5.2 deliverables bundle dry-run" | Out-Null

        if ($WriteDeliverablesBundle) {
            $bundle = Convert-JsonObject (Invoke-RevitCliSmoke -CommandArgs @(
                "deliverables", "bundle",
                "--dir", $ProjectDir,
                "--bundle-path", $DeliverablesBundlePath,
                "--output", "json"
            )) "v5.2 deliverables bundle write"

            if (-not (Test-Path -LiteralPath $DeliverablesBundlePath -PathType Leaf)) {
                throw "v5.2 deliverables bundle write completed but bundle was not found: $DeliverablesBundlePath"
            }

            $receiptProperty = $bundle.PSObject.Properties |
                Where-Object { $_.Name -eq "receiptPath" } |
                Select-Object -First 1
            if ($null -ne $receiptProperty -and
                -not [string]::IsNullOrWhiteSpace([string]$receiptProperty.Value) -and
                -not (Test-Path -LiteralPath ([string]$receiptProperty.Value) -PathType Leaf)) {
                throw "v5.2 deliverables bundle write reported a receipt that was not found: $($receiptProperty.Value)"
            }
        }
    } finally {
        Set-Location -LiteralPath $previousLocation
    }
}

function Get-ElementParameterProperty {
    param([object]$Element, [string]$ParameterName, [string]$Context)

    if ($null -eq $Element.parameters) {
        throw "$Context returned no parameters object for element $($Element.id)."
    }

    $property = $Element.parameters.PSObject.Properties |
        Where-Object { $_.Name -eq $ParameterName } |
        Select-Object -First 1

    if ($null -eq $property) {
        throw "$Context did not expose parameter '$ParameterName' for element $($Element.id)."
    }

    return $property
}

function Assert-DryRunPreview {
    param([string]$Text, [long]$ElementId, [string]$OldValue, [string]$NewValue)

    $idNeedle = "[$ElementId]"
    $transitionNeedle = '"' + $OldValue + '" -> "' + $NewValue + '"'
    if (-not $Text.Contains($idNeedle)) {
        throw "set --dry-run preview did not include target element id $ElementId.`n$Text"
    }
    if (-not $Text.Contains($transitionNeedle)) {
        throw "set --dry-run preview did not include expected value transition $transitionNeedle.`n$Text"
    }
}

function Test-V6LedgerReplayApplyAudit {
    param(
        [string]$ProjectDir,
        [long]$ElementId,
        [string]$ExpectedRevitVersion
    )

    $ledgerPath = Join-Path $ProjectDir ".revitcli/ledger/operations.jsonl"
    if (-not (Test-Path -LiteralPath $ledgerPath)) {
        throw "V6 ledger replay apply audit gate expected ledger file at $ledgerPath."
    }

    $records = @(Get-Content -LiteralPath $ledgerPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_ | ConvertFrom-Json })
    $audits = @($records | Where-Object { $_.action -eq "ledger.replay.apply" })
    if ($audits.Count -ne 1) {
        throw "V6 ledger replay apply audit gate expected one ledger.replay.apply audit row."
    }

    $record = $audits[0]
    if ($record.command -ne "ledger") {
        throw "V6 ledger replay apply audit gate expected command=ledger, got '$($record.command)'."
    }
    if (@($record.affectedElementIds | ForEach-Object { [string]$_ }) -notcontains [string]$ElementId) {
        throw "V6 ledger replay apply audit gate expected affected element id $ElementId."
    }
    $args = @($record.args | ForEach-Object { [string]$_ })
    foreach ($requiredArg in @("--apply", "--yes")) {
        if ($args -notcontains $requiredArg) {
            throw "V6 ledger replay apply audit gate expected replay arg '$requiredArg'."
        }
    }
    foreach ($fieldName in @("modelIdentity", "modelPath", "revitVersion")) {
        if ([string]::IsNullOrWhiteSpace([string]$record.PSObject.Properties[$fieldName].Value)) {
            throw "V6 ledger replay apply audit gate expected non-empty $fieldName."
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedRevitVersion) -and [string]$record.revitVersion -ne $ExpectedRevitVersion) {
        throw "V6 ledger replay apply audit gate expected revitVersion=$ExpectedRevitVersion, got '$($record.revitVersion)'."
    }

    return $record
}

function Invoke-V6LedgerReplayApplySmoke {
    param(
        [string]$ProjectDir,
        [long]$ElementId,
        [string]$ParameterName,
        [string]$OldValue,
        [string]$NewValue,
        [string]$ExpectedRevitVersion
    )

    if ([string]::IsNullOrWhiteSpace($ProjectDir)) {
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $ProjectDir = Join-Path ([System.IO.Path]::GetTempPath()) "revitcli-v6-ledger-replay-apply-$stamp"
    }

    $script:resolvedV6LedgerProjectDir = [System.IO.Path]::GetFullPath($ProjectDir)
    New-Item -ItemType Directory -Force -Path $script:resolvedV6LedgerProjectDir | Out-Null

    Push-Location -LiteralPath $script:resolvedV6LedgerProjectDir
    try {
        Invoke-RevitCliSmoke @(
            "set",
            "--id", $ElementId.ToString(),
            "--param", $ParameterName,
            "--value", $NewValue,
            "--yes"
        ) | Out-Null

        $restoreArgs = @("set", "--id", $ElementId.ToString(), "--param", $ParameterName)
        if ([string]::IsNullOrEmpty($OldValue)) {
            $restoreArgs += "--clear-value"
        } else {
            $restoreArgs += @("--value", $OldValue)
        }
        $restoreArgs += "--yes"
        Invoke-RevitCliSmoke $restoreArgs | Out-Null

        $previewJson = Invoke-RevitCliSmoke @(
            "ledger", "replay",
            "--source", "ledger",
            "--action", "set",
            "--limit", "1",
            "--output", "json"
        )
        $preview = Convert-JsonObject $previewJson "ledger replay preview"
        if (-not $preview.dryRun) {
            throw "ledger replay preview expected dryRun=true."
        }

        $replayJson = Invoke-RevitCliSmoke @(
            "ledger", "replay",
            "--source", "ledger",
            "--action", "set",
            "--limit", "1",
            "--apply",
            "--yes",
            "--output", "json"
        )
        $script:v6LedgerReplayApplyReport = Convert-JsonObject $replayJson "ledger replay apply"

        if ($script:v6LedgerReplayApplyReport.summary.appliedStepCount -ne 1 -or
            $script:v6LedgerReplayApplyReport.summary.failedStepCount -ne 0) {
            throw "ledger replay apply expected one applied step and zero failed steps."
        }
        if ($script:v6LedgerReplayApplyReport.steps[0].applyStatus -ne "applied") {
            throw "ledger replay apply first step was not applied."
        }

        $confirmJson = Invoke-RevitCliSmoke @("query", "--id", $ElementId.ToString(), "--output", "json")
        $confirmed = Convert-JsonArray $confirmJson "query ledger replay apply confirm"
        $confirmedParam = Get-ElementParameterProperty -Element $confirmed[0] -ParameterName $ParameterName -Context "query ledger replay apply confirm"
        if ([string]$confirmedParam.Value -ne $NewValue) {
            throw "Ledger replay apply verification failed for '$ParameterName': expected '$NewValue', got '$($confirmedParam.Value)'."
        }

        $script:v6LedgerReplayApplyAudit = Test-V6LedgerReplayApplyAudit `
            -ProjectDir $script:resolvedV6LedgerProjectDir `
            -ElementId $ElementId `
            -ExpectedRevitVersion $ExpectedRevitVersion
    } finally {
        try {
            $restoreArgs = @("set", "--id", $ElementId.ToString(), "--param", $ParameterName)
            if ([string]::IsNullOrEmpty($OldValue)) {
                $restoreArgs += "--clear-value"
            } else {
                $restoreArgs += @("--value", $OldValue)
            }
            $restoreArgs += "--yes"
            Invoke-RevitCliSmoke $restoreArgs | Out-Null
        } finally {
            Pop-Location
        }
    }
}

function Get-FixJournalPath {
    param([string]$BaselinePath)

    if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
        return ""
    }

    $fullPath = [System.IO.Path]::GetFullPath($BaselinePath)
    $directory = [System.IO.Path]::GetDirectoryName($fullPath)
    $name = [System.IO.Path]::GetFileNameWithoutExtension($fullPath)
    return Join-Path $directory "$name.fixjournal.json"
}

function New-FixBaselineOutputPath {
    param([string]$RootPath)

    $fixDirectory = Join-Path $RootPath ".revitcli"
    New-Item -ItemType Directory -Force -Path $fixDirectory | Out-Null

    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $nonce = [guid]::NewGuid().ToString("N").Substring(0, 8)
    return Join-Path $fixDirectory ("fix-baseline-{0}-{1}.json" -f $stamp, $nonce)
}

$scriptFailure = $null
$fixApplyFailure = $null
$fixRollbackFailure = $null
$fixBaselinePath = ""
$fixJournalPath = ""
$resolvedV5ProjectDir = ""
$resolvedV5IssueProfile = ""
$resolvedV5IssueBundlePath = ""
$resolvedV5SheetPlanPath = ""
$resolvedV5SheetParamMap = ""
$resolvedV52ScheduleSet = ""
$resolvedV52ScheduleOutputDir = ""
$resolvedV52ScheduleManifestPath = ""
$resolvedV52ScheduleCompareBaselineDir = ""
$resolvedV52DeliverablesBundlePath = ""
$resolvedV6LedgerProjectDir = ""
$v6LedgerReplayApplyReport = $null
$v6LedgerReplayApplyAudit = $null

$Steps = [System.Collections.Generic.List[object]]::new()
$resolvedV4ProjectDir = ""
$installDir = Resolve-RevitInstallDir -Version $Version -OverridePath $RevitInstallDir
$installDirEnvName = "REVITCLI_REVIT${Version}_INSTALL_DIR"
$previousInstallDirEnvValue = [Environment]::GetEnvironmentVariable($installDirEnvName)
$manifestPath = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$Version\RevitCli.addin"
$serverInfoPath = Join-Path $env:USERPROFILE ".revitcli\server.json"

try {
    [Environment]::SetEnvironmentVariable($installDirEnvName, $installDir)

    if (-not $V5IssueClosure -and (
        $V5ApplySheetIssue -or
        $V5WriteIssuePackage -or
        -not [string]::IsNullOrWhiteSpace($V5ProjectDir) -or
        -not [string]::IsNullOrWhiteSpace($V5IssueProfile) -or
        -not [string]::IsNullOrWhiteSpace($V5IssueBundlePath) -or
        -not [string]::IsNullOrWhiteSpace($V5SheetSelector) -or
        -not [string]::IsNullOrWhiteSpace($V5IssueCode) -or
        -not [string]::IsNullOrWhiteSpace($V5IssueDate) -or
        -not [string]::IsNullOrWhiteSpace($V5SheetPlanPath) -or
        -not [string]::IsNullOrWhiteSpace($V5SheetParamMap) -or
        $V52SchedulePackage -or
        $V52WriteDeliverablesBundle -or
        -not [string]::IsNullOrWhiteSpace($V52ScheduleSet) -or
        -not [string]::IsNullOrWhiteSpace($V52ScheduleOutputDir) -or
        -not [string]::IsNullOrWhiteSpace($V52ScheduleManifestPath) -or
        -not [string]::IsNullOrWhiteSpace($V52ScheduleCompareBaselineDir) -or
        -not [string]::IsNullOrWhiteSpace($V52ScheduleCompareKeys) -or
        -not [string]::IsNullOrWhiteSpace($V52DeliverablesBundlePath))) {
        throw "V5 issue-closure options require -V5IssueClosure."
    }
    if (-not $V6LedgerReplayApply -and -not [string]::IsNullOrWhiteSpace($V6LedgerProjectDir)) {
        throw "-V6LedgerProjectDir requires -V6LedgerReplayApply."
    }
    if ($V52WriteDeliverablesBundle -and -not $V52SchedulePackage) {
        throw "-V52WriteDeliverablesBundle requires -V52SchedulePackage."
    }

    Assert-FileExists (Join-Path $installDir "RevitAPI.dll") "Revit $Version API DLL"
    Assert-FileExists (Join-Path $installDir "RevitAPIUI.dll") "Revit $Version API UI DLL"
    Assert-FileExists $manifestPath "RevitCli $Version add-in manifest"
    Assert-FileExists $serverInfoPath "RevitCli server.json"

    $manifestAssemblyPath = Resolve-ManifestAssemblyPath $manifestPath
    Assert-FileExists $manifestAssemblyPath "RevitCli manifest assembly"
    $installedAddinVersion = Get-AssemblyVersion $manifestAssemblyPath
    $cliVersionMetadata = Get-CliVersionMetadata -Command $RevitCli
    $cliVersion = $cliVersionMetadata.Version
    $cliVersionError = $cliVersionMetadata.Error
    if (-not [string]::IsNullOrWhiteSpace($cliVersionError) -or [string]::IsNullOrWhiteSpace($cliVersion)) {
        throw "CLI version metadata unavailable: $cliVersionError"
    }

    $serverInfo = Get-Content -Raw -LiteralPath $serverInfoPath | ConvertFrom-Json
    try {
        $proc = Get-Process -Id ([int]$serverInfo.pid) -ErrorAction Stop
    } catch {
        throw "server.json is stale; pid $($serverInfo.pid) is not running. Restart Revit $Version."
    }
    if ($proc.ProcessName -notlike "*Revit*") {
        throw "server.json pid $($serverInfo.pid) belongs to '$($proc.ProcessName)', not Revit."
    }

    Invoke-RevitCliSmoke @("doctor", "--check-version", $Version) | Out-Null
    $statusText = Invoke-RevitCliSmoke @("status")
    $liveAddinVersion = ""
    foreach ($line in ($statusText -split "`r?`n")) {
        if ($line -match '^Add-in:\s+v?(.+)$') {
            $liveAddinVersion = $Matches[1].Trim()
            break
        }
    }
    Assert-VersionMatch -CliVersion $cliVersion -InstalledAddinVersion $installedAddinVersion -LiveAddinVersion $liveAddinVersion

    $idJson = Invoke-RevitCliSmoke @("query", "--id", $ElementId.ToString(), "--output", "json")
    $idElements = Convert-JsonArray $idJson "query --id"
    if ($idElements.Count -ne 1) {
        throw "query --id returned $($idElements.Count) elements; expected exactly 1."
    }

    $oldValue = $null
    $paramProperty = Get-ElementParameterProperty -Element $idElements[0] -ParameterName $Param -Context "query --id"
    if (($Apply -or $V6LedgerReplayApply) -and $null -eq $paramProperty.Value) {
        throw "Element $ElementId parameter '$Param' is null. Pick a writable text parameter whose current value is not null; empty strings are restored with --clear-value."
    }
    $oldValue = if ($null -eq $paramProperty.Value) { $null } else { [string]$paramProperty.Value }
    if (($Apply -or $V6LedgerReplayApply) -and [string]::IsNullOrEmpty($Value)) {
        throw "-Apply and -V6LedgerReplayApply require a non-empty -Value so query confirmation cannot hide a missing parameter."
    }

    $filterJson = Invoke-RevitCliSmoke @("query", $Category, "--filter", $Filter, "--output", "json")
    $filtered = Convert-JsonArray $filterJson "query filter"
    if ($filtered.Count -ne 1) {
        throw "query $Category --filter '$Filter' returned $($filtered.Count) elements; expected exactly 1."
    }
    if ([long]$filtered[0].id -ne $ElementId) {
        throw "Filter matched element $($filtered[0].id), expected $ElementId."
    }

    $dryRunText = Invoke-RevitCliSmoke @(
        "set", $Category,
        "--filter", $Filter,
        "--param", $Param,
        "--value", $Value,
        "--dry-run"
    )
    Assert-DryRunPreview -Text $dryRunText -ElementId $ElementId -OldValue $oldValue -NewValue $Value

    if ($V4Workbench) {
        $resolvedV4ProjectDir = if ([string]::IsNullOrWhiteSpace($V4ProjectDir)) {
            (Get-Location).Path
        } else {
            [System.IO.Path]::GetFullPath($V4ProjectDir)
        }
        Invoke-V4WorkbenchSmoke -ProjectDir $resolvedV4ProjectDir -Version $Version -Category $Category -ElementId $ElementId
    }

    if ($V5IssueClosure) {
        Invoke-V5IssueClosureSmoke `
            -ProjectDir $V5ProjectDir `
            -IssueProfile $V5IssueProfile `
            -IssueBundlePath $V5IssueBundlePath `
            -SheetSelector $V5SheetSelector `
            -IssueCode $V5IssueCode `
            -IssueDate $V5IssueDate `
            -SheetPlanPath $V5SheetPlanPath `
            -SheetParamMap $V5SheetParamMap `
            -ApplySheetIssue:$V5ApplySheetIssue `
            -WriteIssuePackage:$V5WriteIssuePackage `
            -SchedulePackage:$V52SchedulePackage `
            -ScheduleSet $V52ScheduleSet `
            -ScheduleOutputDir $V52ScheduleOutputDir `
            -ScheduleManifestPath $V52ScheduleManifestPath `
            -ScheduleCompareBaselineDir $V52ScheduleCompareBaselineDir `
            -ScheduleCompareKeys $V52ScheduleCompareKeys `
            -DeliverablesBundlePath $V52DeliverablesBundlePath `
            -WriteDeliverablesBundle:$V52WriteDeliverablesBundle
    }

    if (-not $Apply -and -not $FixApply -and -not $V5ApplySheetIssue -and -not $V5WriteIssuePackage -and -not $V52WriteDeliverablesBundle -and -not $V6LedgerReplayApply) {
        if ($V5IssueClosure) {
            Write-Host "Dry-run smoke completed. Re-run with -V5ApplySheetIssue, -V5WriteIssuePackage, and/or -V52WriteDeliverablesBundle on disposable controlled project copies to perform approved writes."
        } else {
            Write-Host "Dry-run smoke completed. Re-run with -Apply to perform the write/confirm/restore steps."
        }
    }

    if ($Apply) {
        $restoreNeeded = $false
        $applyFailure = $null
        $restoreFailure = $null

        try {
            Invoke-RevitCliSmoke @(
                "set", $Category,
                "--filter", $Filter,
                "--param", $Param,
                "--value", $Value,
                "--yes"
            ) | Out-Null
            $restoreNeeded = $true

            $confirmJson = Invoke-RevitCliSmoke @("query", "--id", $ElementId.ToString(), "--output", "json")
            $confirmed = Convert-JsonArray $confirmJson "query confirm"
            $newParam = Get-ElementParameterProperty -Element $confirmed[0] -ParameterName $Param -Context "query confirm"
            if ([string]$newParam.Value -ne $Value) {
                throw "Write verification failed for '$Param': expected '$Value', got '$($newParam.Value)'."
            }
        } catch {
            $applyFailure = $_
        } finally {
            if ($restoreNeeded) {
                try {
                    $restoreArgs = @("set", "--id", $ElementId.ToString(), "--param", $Param)
                    if ([string]::IsNullOrEmpty($oldValue)) {
                        $restoreArgs += "--clear-value"
                    } else {
                        $restoreArgs += @("--value", $oldValue)
                    }
                    $restoreArgs += "--yes"
                    Invoke-RevitCliSmoke $restoreArgs | Out-Null

                    $restoreJson = Invoke-RevitCliSmoke @("query", "--id", $ElementId.ToString(), "--output", "json")
                    $restored = Convert-JsonArray $restoreJson "query restore"
                    $restoredParam = Get-ElementParameterProperty -Element $restored[0] -ParameterName $Param -Context "query restore"
                    if ([string]$restoredParam.Value -ne $oldValue) {
                        throw "Restore verification failed for '$Param': expected '$oldValue', got '$($restoredParam.Value)'."
                    }
                } catch {
                    $restoreFailure = $_
                }
            }
        }

        if ($applyFailure -and $restoreFailure) {
            throw "Apply/confirm failed after write, and restore also failed. Apply error: $($applyFailure.Exception.Message). Restore error: $($restoreFailure.Exception.Message)"
        }
        if ($restoreFailure) {
            throw "Restore failed after smoke write: $($restoreFailure.Exception.Message)"
        }
        if ($applyFailure) {
            throw $applyFailure
        }
    }

    if ($V6LedgerReplayApply) {
        Invoke-V6LedgerReplayApplySmoke `
            -ProjectDir $V6LedgerProjectDir `
            -ElementId $ElementId `
            -ParameterName $Param `
            -OldValue $oldValue `
            -NewValue $Value `
            -ExpectedRevitVersion $Version
    }

    if ($FixDryRun -or $FixApply) {
        if ([string]::IsNullOrWhiteSpace($FixProfile)) {
            throw "-FixDryRun and -FixApply require a non-empty -FixProfile."
        }
    }

    if ($FixDryRun) {
        Invoke-RevitCliSmoke @("fix", $FixCheckName, "--dry-run", "--profile", $FixProfile) | Out-Null
    }

    if ($FixApply) {
        $fixBaselinePath = New-FixBaselineOutputPath -RootPath (Get-Location).Path
        $fixJournalPath = Get-FixJournalPath -BaselinePath $fixBaselinePath

        try {
            Invoke-RevitCliSmoke @(
                "fix", $FixCheckName,
                "--apply", "--yes",
                "--profile", $FixProfile,
                "--baseline-output", $fixBaselinePath
            ) | Out-Null

            if (-not (Test-Path -LiteralPath $fixBaselinePath)) {
                throw "fix apply succeeded but did not create the expected baseline file: $fixBaselinePath"
            }
        } catch {
            $fixApplyFailure = $_
        } finally {
            if (Test-Path -LiteralPath $fixBaselinePath) {
                try {
                    Invoke-RevitCliSmoke @("rollback", $fixBaselinePath, "--yes") | Out-Null
                } catch {
                    $fixRollbackFailure = $_
                }
            }
        }

        if ($null -eq $fixApplyFailure -and $null -ne $fixRollbackFailure) {
            $scriptFailure = $fixRollbackFailure
        } elseif ($null -ne $fixApplyFailure) {
            $scriptFailure = $fixApplyFailure
        }

    }
} catch {
    if ($null -eq $scriptFailure) {
        $scriptFailure = $_
    }
} finally {
    [Environment]::SetEnvironmentVariable($installDirEnvName, $previousInstallDirEnvValue)

    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $OutputPath = Join-Path (Get-Location) "revitcli-smoke-$Version-$stamp.json"
    }

    $report = [ordered]@{
        timestamp = (Get-Date).ToString("o")
        revitVersion = $Version
        revitInstallDir = $installDir
        manifestPath = $manifestPath
        manifestAssemblyPath = $manifestAssemblyPath
        serverInfoPath = $serverInfoPath
        cliVersion = $cliVersion
        cliVersionError = $cliVersionError
        installedAddinVersion = $installedAddinVersion
        liveAddinVersion = $liveAddinVersion
        elementId = $ElementId
        category = $Category
        filter = $Filter
        parameter = $Param
        oldValue = $oldValue
        testValue = $Value
        applied = [bool]$Apply
        fixDryRun = [bool]$FixDryRun
        fixApply = [bool]$FixApply
        fixCheckName = $FixCheckName
        fixProfile = $FixProfile
        fixBaselinePath = $fixBaselinePath
        fixJournalPath = $fixJournalPath
        v4Workbench = [bool]$V4Workbench
        v4ProjectDir = $resolvedV4ProjectDir
        v5IssueClosure = [bool]$V5IssueClosure
        v5ProjectDir = $resolvedV5ProjectDir
        v5IssueProfile = $resolvedV5IssueProfile
        v5IssueBundlePath = $resolvedV5IssueBundlePath
        v5SheetIssuePlanPath = $resolvedV5SheetPlanPath
        v5SheetParamMap = $resolvedV5SheetParamMap
        v5ApplySheetIssue = [bool]$V5ApplySheetIssue
        v5WriteIssuePackage = [bool]$V5WriteIssuePackage
        v52SchedulePackage = [bool]$V52SchedulePackage
        v52ScheduleSet = $resolvedV52ScheduleSet
        v52ScheduleOutputDir = $resolvedV52ScheduleOutputDir
        v52ScheduleManifestPath = $resolvedV52ScheduleManifestPath
        v52ScheduleCompareBaselineDir = $resolvedV52ScheduleCompareBaselineDir
        v52DeliverablesBundlePath = $resolvedV52DeliverablesBundlePath
        v52WriteDeliverablesBundle = [bool]$V52WriteDeliverablesBundle
        v6LedgerReplayApply = [bool]$V6LedgerReplayApply
        v6LedgerProjectDir = $resolvedV6LedgerProjectDir
        v6LedgerReplayApplySummary = if ($null -ne $v6LedgerReplayApplyReport) { $v6LedgerReplayApplyReport.summary } else { $null }
        v6LedgerReplayApplyAudit = $v6LedgerReplayApplyAudit
        fixApplyError = if ($null -ne $fixApplyFailure) { $fixApplyFailure.Exception.Message } else { "" }
        fixRollbackError = if ($null -ne $fixRollbackFailure) { $fixRollbackFailure.Exception.Message } else { "" }
        failure = if ($null -ne $scriptFailure) { $scriptFailure.Exception.Message } else { "" }
        steps = $Steps
    }

    try {
        $outputDirectory = Split-Path -Parent $OutputPath
        if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
            New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
        }
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
        Write-Host "Smoke report written to $OutputPath"
        if ($FixApply -and $null -eq $scriptFailure -and $null -eq $fixApplyFailure -and $null -eq $fixRollbackFailure) {
            Write-Host "Fix apply smoke completed. Review the report for the baseline, journal, and rollback results."
        }
        if ($V5IssueClosure -and $null -eq $scriptFailure) {
            Write-Host "V5 issue-closure smoke completed. Review the report before making any live-support claim."
        }
    } catch {
        if ($null -eq $scriptFailure) {
            $scriptFailure = $_
        } else {
            Write-Warning "Smoke report write failed: $($_.Exception.Message)"
        }
    }
}

if ($null -ne $scriptFailure) {
    throw $scriptFailure
}
