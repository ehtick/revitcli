namespace RevitCli.Tests.Scripts;

public sealed class Revit2026SmokeScriptTests
{
    [Fact]
    public void VersionedSmokeScript_ExposesSupportedVersionSwitchAndDoctorCheck()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));

        Assert.Contains("[ValidateSet(\"2024\", \"2025\", \"2026\")]", script);
        Assert.Contains("REVITCLI_REVIT${Version}_INSTALL_DIR", script);
        Assert.Contains("Revit${Version}InstallDir", script);
        Assert.Contains("@(\"doctor\", \"--check-version\", $Version)", script);
        Assert.Contains("Autodesk\\Revit\\Addins\\$Version\\RevitCli.addin", script);
    }

    [Fact]
    public void VersionedSmokeScript_ExposesV4WorkbenchLiveDiscoveryGate()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));

        Assert.Contains("[switch]$V4Workbench", script);
        Assert.Contains("\"workbench\", \"verify\"", script);
        Assert.Contains("\"workbench\", \"handoff\"", script);
        Assert.Contains("\"inspect\", \"schedules\"", script);
        Assert.Contains("\"inspect\", \"sheets\"", script);
        Assert.Contains("\"schedule\", \"list\"", script);
        Assert.Contains("\"schedule\", \"export\"", script);
        Assert.Contains("v4Workbench = [bool]$V4Workbench", script);
    }

    [Fact]
    public void VersionedSmokeScript_ExposesV5IssueClosureGate()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));

        Assert.Contains("[switch]$V5IssueClosure", script);
        Assert.Contains("[string]$V5IssueProfile", script);
        Assert.Contains("[string]$V5SheetParamMap", script);
        Assert.Contains("[switch]$V5ApplySheetIssue", script);
        Assert.Contains("[switch]$V5WriteIssuePackage", script);
        Assert.Contains("[switch]$V52SchedulePackage", script);
        Assert.Contains("[string]$V52ScheduleSet", script);
        Assert.Contains("[string]$V52ScheduleCompareBaselineDir", script);
        Assert.Contains("[string]$V52ScheduleCompareKeys", script);
        Assert.Contains("[switch]$V52WriteDeliverablesBundle", script);
        Assert.Contains("Assert-VersionMatch", script);
        Assert.Contains("CLI/add-in version mismatch", script);
        Assert.Contains("\"workbench\", \"verify\"", script);
        Assert.Contains("\"--contract\", \"workbench-contract.v2\"", script);
        Assert.Contains("\"issue\", \"preflight\"", script);
        Assert.Contains("\"issue\", \"package\"", script);
        Assert.Contains("\"issue package write\"", script);
        Assert.Contains("\"sheets\", \"issue-meta\"", script);
        Assert.Contains("\"--param-map\", $SheetParamMap", script);
        Assert.Contains("\"plan\", \"apply\"", script);
        Assert.Contains("\"rollback\", $receiptPath", script);
        Assert.Contains("\"journal\", \"sign\", \"--dir\", $ProjectDir", script);
        Assert.Contains("\"journal\", \"verify\"", script);
        Assert.Contains("Invoke-V52SchedulePackageSmoke", script);
        Assert.Contains("\"schedules\", \"batch-export\"", script);
        Assert.Contains("\"schedules\", \"compare\"", script);
        Assert.Contains("-V52SchedulePackage requires -V52ScheduleCompareBaselineDir", script);
        Assert.Contains("-V52ScheduleCompareBaselineDir must point to an existing baseline schedule export directory", script);
        Assert.Contains("\"deliverables\", \"verify\"", script);
        Assert.Contains("\"deliverables\", \"bundle\"", script);
        Assert.Contains("v5IssueClosure = [bool]$V5IssueClosure", script);
        Assert.Contains("v5SheetParamMap = $resolvedV5SheetParamMap", script);
        Assert.Contains("v5ApplySheetIssue = [bool]$V5ApplySheetIssue", script);
        Assert.Contains("v5WriteIssuePackage = [bool]$V5WriteIssuePackage", script);
        Assert.Contains("v52SchedulePackage = [bool]$V52SchedulePackage", script);
    }

    [Fact]
    public void Legacy2026SmokeScript_UsesVersionCheckAndV4WorkbenchGate()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit2026.ps1"));

        Assert.Contains("@(\"doctor\", \"--check-version\", \"2026\")", script);
        Assert.Contains("[switch]$V4Workbench", script);
        Assert.Contains("\"workbench\", \"verify\"", script);
        Assert.Contains("\"schedule\", \"export\"", script);
        Assert.Contains("v4Workbench = [bool]$V4Workbench", script);
    }

    [Fact]
    public void WslSmokeScript_UsesInstalledWindowsCliAndDryRunOnly()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit-wsl.sh"));

        Assert.Contains("REVITCLI_WINDOWS_EXE", script);
        Assert.Contains("RevitCli.exe", script);
        Assert.Contains("jq is required", script);
        Assert.Contains("doctor --check-version 2026 --output json", script);
        Assert.Contains("status --output json", script);
        Assert.Contains("query --id", script);
        Assert.Contains("set-dry-run", script);
        Assert.Contains("summary.json", script);
        Assert.Contains("revitcli-wsl-live-smoke.v1", script);
        Assert.Contains("sourceInstalledDrift", script);
        Assert.Contains("--require-current-source", script);
        Assert.Contains("nextActions", script);
        Assert.Contains(@".\scripts\install.ps1", script);
        Assert.Contains("install-current-source.ps1", script);
        Assert.Contains("installHandoff", script);
        Assert.Contains("postRestartCommand", script);
        Assert.Contains("currentSourceInstalled", script);
        Assert.Contains("requireCurrentSource", script);
        Assert.Contains("mutatesModel: false", script);
        Assert.Contains("--dry-run", script);
        Assert.Contains("It does not pass --yes", script);
        Assert.DoesNotContain("\"--yes\"", script);
    }

    [Fact]
    public void Legacy2026SmokeScript_ExposesV5IssueClosureGate()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit2026.ps1"));

        Assert.Contains("[switch]$V5IssueClosure", script);
        Assert.Contains("[string]$V5IssueProfile", script);
        Assert.Contains("[string]$V5SheetParamMap", script);
        Assert.Contains("[switch]$V5ApplySheetIssue", script);
        Assert.Contains("[switch]$V5WriteIssuePackage", script);
        Assert.Contains("[switch]$V52SchedulePackage", script);
        Assert.Contains("[string]$V52ScheduleSet", script);
        Assert.Contains("[string]$V52ScheduleCompareBaselineDir", script);
        Assert.Contains("[string]$V52ScheduleCompareKeys", script);
        Assert.Contains("[switch]$V52WriteDeliverablesBundle", script);
        Assert.Contains("Assert-VersionMatch", script);
        Assert.Contains("CLI/add-in version mismatch", script);
        Assert.Contains("\"--contract\", \"workbench-contract.v2\"", script);
        Assert.Contains("\"issue\", \"preflight\"", script);
        Assert.Contains("\"issue\", \"package\"", script);
        Assert.Contains("\"issue package write\"", script);
        Assert.Contains("\"sheets\", \"issue-meta\"", script);
        Assert.Contains("\"--param-map\", $SheetParamMap", script);
        Assert.Contains("\"plan\", \"apply\"", script);
        Assert.Contains("\"rollback\", $receiptPath", script);
        Assert.Contains("\"journal\", \"sign\", \"--dir\", $ProjectDir", script);
        Assert.Contains("\"journal\", \"verify\"", script);
        Assert.Contains("Invoke-V52SchedulePackageSmoke", script);
        Assert.Contains("\"schedules\", \"batch-export\"", script);
        Assert.Contains("\"schedules\", \"compare\"", script);
        Assert.Contains("-V52SchedulePackage requires -V52ScheduleCompareBaselineDir", script);
        Assert.Contains("-V52ScheduleCompareBaselineDir must point to an existing baseline schedule export directory", script);
        Assert.Contains("\"deliverables\", \"verify\"", script);
        Assert.Contains("\"deliverables\", \"bundle\"", script);
        Assert.Contains("v5IssueClosure = [bool]$V5IssueClosure", script);
        Assert.Contains("v5SheetParamMap = $resolvedV5SheetParamMap", script);
        Assert.Contains("v5ApplySheetIssue = [bool]$V5ApplySheetIssue", script);
        Assert.Contains("v5WriteIssuePackage = [bool]$V5WriteIssuePackage", script);
        Assert.Contains("v52SchedulePackage = [bool]$V52SchedulePackage", script);
    }

    [Fact]
    public void SmokeScripts_RunV5PreflightBeforeApprovedV5Writes()
    {
        var versionedScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));
        var legacyScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit2026.ps1"));

        foreach (var script in new[] { versionedScript, legacyScript })
        {
            var preflightIndex = script.IndexOf("\"issue\", \"preflight\"", StringComparison.Ordinal);
            var approvedSheetApplyIndex = script.IndexOf("\"plan\", \"apply\", $SheetPlanPath,\n            \"--yes\"", StringComparison.Ordinal);
            var approvedPackageIndex = script.IndexOf("\"issue package write\"", StringComparison.Ordinal);

            Assert.True(preflightIndex >= 0, "v5 issue preflight command was not found.");
            Assert.True(approvedSheetApplyIndex >= 0, "approved v5 sheet apply command was not found.");
            Assert.True(approvedPackageIndex >= 0, "approved v5 package write command was not found.");
            Assert.True(preflightIndex < approvedSheetApplyIndex, "v5 preflight must run before sheet plan apply --yes.");
            Assert.True(preflightIndex < approvedPackageIndex, "v5 preflight must run before issue package write.");
        }
    }

    [Fact]
    public void SmokeScripts_BlockV5SubOptionsWithoutV5Gate()
    {
        var versionedScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));
        var legacyScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit2026.ps1"));

        foreach (var script in new[] { versionedScript, legacyScript })
        {
            Assert.Contains("V5 issue-closure options require -V5IssueClosure.", script);
            Assert.Contains("$V5ApplySheetIssue -or", script);
            Assert.Contains("$V5WriteIssuePackage -or", script);
            Assert.Contains("$V52SchedulePackage -or", script);
            Assert.Contains("$V52WriteDeliverablesBundle -or", script);
            Assert.Contains("-not [string]::IsNullOrWhiteSpace($V5SheetParamMap)", script);
            Assert.Contains("-not [string]::IsNullOrWhiteSpace($V52ScheduleCompareKeys)", script);
            Assert.Contains("-V5WriteIssuePackage requires a disposable non-existing bundle path", script);
            Assert.Contains("-V52WriteDeliverablesBundle requires a disposable non-existing bundle path", script);
        }
    }

    [Fact]
    public void SmokeScripts_BlockV52BundleWriteWithoutSchedulePackageGate()
    {
        var versionedScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));
        var legacyScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit2026.ps1"));

        foreach (var script in new[] { versionedScript, legacyScript })
        {
            var writeGuardIndex = script.IndexOf("if ($V52WriteDeliverablesBundle -and -not $V52SchedulePackage)", StringComparison.Ordinal);
            var writeInvocationIndex = script.IndexOf("-WriteDeliverablesBundle:$V52WriteDeliverablesBundle", StringComparison.Ordinal);

            Assert.True(writeGuardIndex >= 0, "approved v5.2 bundle write must require -V52SchedulePackage.");
            Assert.True(writeInvocationIndex >= 0, "approved v5.2 bundle write invocation was not found.");
            Assert.True(writeGuardIndex < writeInvocationIndex, "approved v5.2 bundle write guard must run before invoking the schedule package smoke.");
            Assert.Contains("-V52WriteDeliverablesBundle requires -V52SchedulePackage.", script);
        }
    }

    [Fact]
    public void SmokeScripts_RequireExplicitBundlePathForApprovedV5PackageWrite()
    {
        var versionedScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));
        var legacyScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit2026.ps1"));

        foreach (var script in new[] { versionedScript, legacyScript })
        {
            var writeRequiresExplicitPath = script.IndexOf(
                "if ([string]::IsNullOrWhiteSpace($IssueBundlePath)) {\n        if ($WriteIssuePackage) {\n            throw \"-V5WriteIssuePackage requires an explicit -V5IssueBundlePath.\"",
                StringComparison.Ordinal);
            var defaultPathIndex = script.IndexOf(
                "$IssueBundlePath = Join-Path $ProjectDir \".revitcli\\smoke\\v5-issue-closure.zip\"",
                StringComparison.Ordinal);
            var packageWriteIndex = script.IndexOf("\"issue package write\"", StringComparison.Ordinal);

            Assert.True(writeRequiresExplicitPath >= 0, "approved v5 package writes must require an explicit bundle path.");
            Assert.True(defaultPathIndex >= 0, "dry-run v5 package default path was not found.");
            Assert.True(packageWriteIndex >= 0, "approved v5 package write command was not found.");
            Assert.True(writeRequiresExplicitPath < defaultPathIndex, "explicit bundle-path guard must run before assigning a default dry-run path.");
            Assert.True(writeRequiresExplicitPath < packageWriteIndex, "explicit bundle-path guard must run before approved package writes.");
        }
    }

    [Fact]
    public void SmokeScripts_UseV5SpecificDryRunCompletionMessage()
    {
        var versionedScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));
        var legacyScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit2026.ps1"));

        foreach (var script in new[] { versionedScript, legacyScript })
        {
            Assert.Contains("if ($V5IssueClosure) {", script);
            Assert.Contains("Re-run with -V5ApplySheetIssue, -V5WriteIssuePackage, and/or -V52WriteDeliverablesBundle", script);
            Assert.Contains("Re-run with -Apply to perform the write/confirm/restore steps.", script);
        }
    }

    [Fact]
    public void SmokeScripts_RestoreEmptyParameterValuesWithClearValue()
    {
        var versionedScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));
        var legacyScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit2026.ps1"));

        foreach (var script in new[] { versionedScript, legacyScript })
        {
            Assert.Contains("$restoreArgs = @(\"set\", \"--id\", $ElementId.ToString(), \"--param\", $Param)", script);
            Assert.Contains("if ([string]::IsNullOrEmpty($oldValue))", script);
            Assert.Contains("$restoreArgs += \"--clear-value\"", script);
            Assert.Contains("$restoreArgs += @(\"--value\", $oldValue)", script);
            Assert.Contains("$restoreArgs += \"--yes\"", script);
            Assert.Contains("Invoke-RevitCliSmoke $restoreArgs", script);
        }
    }

    [Fact]
    public void SmokeScripts_ExposeV6LedgerReplayApplyGate()
    {
        var versionedScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));
        var legacyScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit2026.ps1"));

        foreach (var script in new[] { versionedScript, legacyScript })
        {
            Assert.Contains("[switch]$V6LedgerReplayApply", script);
            Assert.Contains("[string]$V6LedgerProjectDir", script);
            Assert.Contains("Invoke-V6LedgerReplayApplySmoke", script);
            Assert.Contains("\"ledger\", \"replay\"", script);
            Assert.Contains("\"--source\", \"ledger\"", script);
            Assert.Contains("\"--action\", \"set\"", script);
            Assert.Contains("\"--apply\"", script);
            Assert.Contains("\"--yes\"", script);
            Assert.Contains("Test-V6LedgerReplayApplyAudit", script);
            Assert.Contains(".revitcli/ledger/operations.jsonl", script);
            Assert.Contains("ledger.replay.apply", script);
            Assert.Contains("modelIdentity", script);
            Assert.Contains("modelPath", script);
            Assert.Contains("revitVersion", script);
            Assert.Contains("affectedElementIds", script);
            Assert.Contains("v6LedgerReplayApplyAudit", script);
            Assert.Contains("v6LedgerReplayApply = [bool]$V6LedgerReplayApply", script);
            Assert.Contains("v6LedgerReplayApplySummary", script);
        }
    }

    [Fact]
    public void SmokeMatrixTemplate_ExposesOptionalV5IssueClosureVariables()
    {
        var template = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "ci", "smoke-matrix-template.yml"));

        Assert.Contains("REVITCLI_V5_ISSUE_CLOSURE", template);
        Assert.Contains("REVITCLI_V5_ISSUE_PROFILE", template);
        Assert.Contains("-V5IssueClosure", template);
        Assert.Contains("-V5IssueProfile", template);
        Assert.Contains("-V5ApplySheetIssue", template);
        Assert.Contains("REVITCLI_V5_WRITE_ISSUE_PACKAGE", template);
        Assert.Contains("-V5WriteIssuePackage", template);
        Assert.Contains("REVITCLI_V5_ISSUE_BUNDLE_PATH when REVITCLI_V5_WRITE_ISSUE_PACKAGE is true", template);
        Assert.Contains("REVITCLI_V5_SHEET_PARAM_MAP", template);
        Assert.Contains("-V5SheetParamMap", template);
        Assert.Contains("REVITCLI_V52_SCHEDULE_PACKAGE", template);
        Assert.Contains("REVITCLI_V52_SCHEDULE_SET", template);
        Assert.Contains("REVITCLI_V52_SCHEDULE_COMPARE_BASELINE_DIR when REVITCLI_V52_SCHEDULE_PACKAGE is true", template);
        Assert.Contains("REVITCLI_V52_SCHEDULE_COMPARE_KEYS when REVITCLI_V52_SCHEDULE_PACKAGE is true", template);
        Assert.Contains("-V52SchedulePackage", template);
        Assert.Contains("-V52ScheduleSet", template);
        Assert.Contains("-V52ScheduleCompareBaselineDir", template);
        Assert.Contains("-V52ScheduleCompareKeys", template);
        Assert.Contains("REVITCLI_V52_DELIVERABLES_BUNDLE_PATH when REVITCLI_V52_WRITE_DELIVERABLES_BUNDLE is true", template);
    }

    [Fact]
    public void SmokeMatrixTemplate_ExposesOptionalV6LedgerReplayApplyVariables()
    {
        var template = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "ci", "smoke-matrix-template.yml"));

        Assert.Contains("REVITCLI_V6_LEDGER_REPLAY_APPLY", template);
        Assert.Contains("REVITCLI_V6_LEDGER_PROJECT_DIR", template);
        Assert.Contains("-V6LedgerReplayApply", template);
        Assert.Contains("-V6LedgerProjectDir", template);
    }

    [Fact]
    public void AddinProject_SelectsSingleTargetFrameworkPerRevitYear()
    {
        var project = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "RevitCli.Addin",
            "RevitCli.Addin.csproj"));

        Assert.Contains("$(RevitYear)' == '2024'\">net48</TargetFramework>", project);
        Assert.Contains("$(RevitYear)' != '2024'\">net8.0-windows</TargetFramework>", project);
        Assert.Contains("$(Revit2026InstallDir)</RevitInstallDir>", project);
        Assert.Contains("$(Revit2025InstallDir)</RevitInstallDir>", project);
        Assert.Contains("$(Revit2024InstallDir)</RevitInstallDir>", project);
        Assert.Contains("ValidateRevitApiReferences", project);
        Assert.Contains("Missing RevitAPI.dll", project);
        Assert.Contains("Missing RevitAPIUI.dll", project);
    }

    [Fact]
    public void VersionedSmokeScript_PreservesSingleElementJsonArrays()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));

        Assert.Contains("return ,@($value)", script);
    }

    [Fact]
    public void SmokeScripts_RetryTransientRevitCommunicationTimeouts()
    {
        var versionedScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit.ps1"));
        var legacyScript = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit2026.ps1"));

        foreach (var script in new[] { versionedScript, legacyScript })
        {
            Assert.Contains("Test-TransientRevitCliFailure", script);
            Assert.Contains("Test-RevitCliSmokeCommandCanRetry", script);
            Assert.Contains("[int]$MaxAttempts = 2", script);
            Assert.Contains("HttpClient\\.Timeout", script);
            Assert.Contains("attempt = $attempt", script);
            Assert.Contains("retrySafe = $canRetry", script);
            Assert.Contains("\"set\" { return $CommandArgs -contains \"--dry-run\" }", script);
            Assert.Contains("\"rollback\" { return $false }", script);
            Assert.Contains("\"issue\" {", script);
            Assert.Contains("if ($CommandArgs.Count -gt 1 -and $CommandArgs[1] -eq \"package\")", script);
        }
    }

    [Fact]
    public void FixApplyCompletionMessage_IsWrittenAfterReportWrite()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "smoke-revit2026.ps1"));

        var reportWrittenIndex = script.IndexOf("Smoke report written to $OutputPath", StringComparison.Ordinal);
        var completionIndex = script.IndexOf("Fix apply smoke completed. Review the report", StringComparison.Ordinal);

        Assert.True(reportWrittenIndex >= 0, "Smoke report write message was not found.");
        Assert.True(completionIndex >= 0, "Fix apply completion message was not found.");
        Assert.True(
            completionIndex > reportWrittenIndex,
            "Fix apply completion must not be printed before the smoke report is written.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "revitcli.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from the test output directory.");
    }
}
