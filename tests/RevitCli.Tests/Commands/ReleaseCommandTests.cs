using System.Text.Json;
using RevitCli.Commands;
using RevitCli.Release;

namespace RevitCli.Tests.Commands;

[Collection("Sequential")]
public sealed class ReleaseCommandTests : IDisposable
{
    private readonly string _root;

    public ReleaseCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-release-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Verify_HealthyTreeJson_ReturnsSuccessAndSchema()
    {
        WriteHealthyTree(_root);
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", "v2.3.0", strict: false, output);

        Assert.True(exitCode == 0, output.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("release-verify.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("2.3.0", root.GetProperty("version").GetString());
        Assert.Equal("v2.3.0", root.GetProperty("tag").GetString());
        Assert.Equal(0, root.GetProperty("errorCount").GetInt32());
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "ci:no-addin-build" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "ci:release-verify" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "ci:workbench-verify" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "ci:workbench-v2-verify" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "smoke-script:v4-workbench" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "smoke-script-wsl:live-addin-commit" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "smoke-script-wsl:repair-handoff" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "smoke-script-wsl:restart-required" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "installer-current-source-handoff:verify" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5-rc:status" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5-rc:smoke-no-go" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5.4:office-standard-validate" &&
            check.GetProperty("status").GetString() == "ok" &&
            check.GetProperty("message").GetString()!.Contains("sheet map", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5.4:office-standard-install" &&
            check.GetProperty("status").GetString() == "ok" &&
            check.GetProperty("message").GetString()!.Contains("dry-run", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("approved install", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5.5:no-coordinate-doc" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5.5:worksharing-gap-doc" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5.5:link-repair-plan-json" &&
            check.GetProperty("status").GetString() == "ok" &&
            check.GetProperty("message").GetString()!.Contains("path/load-only", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5.5:workbench-gate" &&
            check.GetProperty("status").GetString() == "ok" &&
            check.GetProperty("message").GetString()!.Contains("scoped workbench v2 v5.5 gate passes", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("overall workbench exit", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("path").GetString()!.Contains($"--dir \"{_root}\"", StringComparison.Ordinal));
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5.6:team-policy" &&
            check.GetProperty("status").GetString() == "ok" &&
            check.GetProperty("message").GetString()!.Contains("receipt retention", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5.6:workbench-gate" &&
            check.GetProperty("status").GetString() == "ok" &&
            check.GetProperty("message").GetString()!.Contains("scoped workbench v2 v5.6 gate passes", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("overall workbench exit", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("path").GetString()!.Contains($"--dir \"{_root}\"", StringComparison.Ordinal));
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:contract-doc" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:standards-spine-smoke-doc" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:issue-spine-smoke-doc" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:deliverables-spine-smoke-doc" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:office-rollout-status-no-overclaim-json" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:local-controlled-pilot-evidence-json" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:local-controlled-pilot-ledger-validate-json" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-query-smoke-doc" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-append-smoke-doc" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-validate-smoke-doc" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-stats-smoke-doc" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-timeline-smoke-doc" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:workflow-registry-smoke-doc" &&
            check.GetProperty("status").GetString() == "ok");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:workbench-gate" &&
            check.GetProperty("status").GetString() == "ok" &&
            check.GetProperty("message").GetString()!.Contains("scoped workbench v2 v6.0 gate passes", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("overall workbench exit", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("path").GetString()!.Contains($"--dir \"{_root}\"", StringComparison.Ordinal) &&
            check.GetProperty("message").GetString()!.Contains("deterministic timestamp/source/path/line ordering", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("journal verify", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("history list JSON/table outputs", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("journal verify JSON/table validity/root-hash parity", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("history-list.v1 JSON count consistency and table row-order parity", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("table summary and Markdown detail parity for supported command-spine paths", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("rollback dry-run", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("runtimeEvidence=pass", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("standardsValidate=true", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("issuePackageDryRun=true", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("rollbackDryRun=true", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("workflowRegistry=true", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("ledgerAppend=true", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("ledgerReplay=true", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("ledgerTimeline=true", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("allRuntimeChecksPass=true", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("event-level no-write evidence", StringComparison.OrdinalIgnoreCase));
        var v60WorkbenchGate = root.GetProperty("checks").EnumerateArray()
            .Single(check => check.GetProperty("id").GetString() == "v6.0:workbench-gate");
        var releaseRuntimeEvidence = v60WorkbenchGate.GetProperty("runtimeEvidence");
        AssertRuntimeEvidenceShape(releaseRuntimeEvidence);
        Assert.True(releaseRuntimeEvidence.GetProperty("commandSpineOutputParity").GetBoolean());
        Assert.True(releaseRuntimeEvidence.GetProperty("commandSpineNoWrites").GetBoolean());
        Assert.True(releaseRuntimeEvidence.GetProperty("historyListEvidence").GetProperty("countConsistency").GetBoolean());
        Assert.True(releaseRuntimeEvidence.GetProperty("historyListEvidence").GetProperty("idOrderMatch").GetBoolean());
        Assert.Equal(1, releaseRuntimeEvidence.GetProperty("historyListEvidence").GetProperty("tableRowCount").GetInt32());
        Assert.True(releaseRuntimeEvidence.GetProperty("rollbackDryRunEvidence").GetProperty("safeApplyEmitted").GetBoolean());
        Assert.True(releaseRuntimeEvidence.GetProperty("rollbackDryRunEvidence").GetProperty("dryRunPreviewOnly").GetBoolean());
        Assert.False(releaseRuntimeEvidence.GetProperty("rollbackDryRunEvidence").GetProperty("sawMutatingSetRequest").GetBoolean());
        Assert.Contains("revitcli rollback", releaseRuntimeEvidence.GetProperty("rollbackDryRunEvidence").GetProperty("safeApplyCommand").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "release-workflow:no-addin-2025" &&
            check.GetProperty("status").GetString() == "ok");
    }

    [Fact]
    public async Task Verify_UbuntuCiMentionsAddin_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        WriteFile(".github/workflows/ci.yml", """
name: CI
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - run: dotnet test tests/RevitCli.Tests/RevitCli.Tests.csproj
      - run: dotnet build src/RevitCli.Addin
""");
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "table", null, strict: false, output);

        Assert.Equal(1, exitCode);
        Assert.Contains("ci:no-addin-build", output.ToString());
        Assert.Contains("FAIL", output.ToString());
    }

    [Fact]
    public async Task Verify_MissingCurrentSourceRevit2026Handoff_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        File.Delete(Path.Combine(_root, "scripts", "install-current-source-revit2026.ps1"));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "file:scripts/install-current-source-revit2026.ps1" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_CurrentSourceRevit2026HandoffWithoutWslGate_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var path = Path.Combine(_root, "scripts", "install-current-source-revit2026.ps1");
        File.WriteAllText(
            path,
            File.ReadAllText(path).Replace("scripts/smoke-revit-wsl.sh --require-current-source", "scripts/smoke-revit-wsl.sh", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "installer-current-source-handoff:verify" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_CurrentSourceRevit2026HandoffWithQuotedParameterSplat_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var path = Path.Combine(_root, "scripts", "install-current-source-revit2026.ps1");
        File.WriteAllText(
            path,
            File.ReadAllText(path)
                .Replace(
                    """
                    $installArgs = @{
                        RevitYears = @("2026")
                        Revit2026InstallDir = $Revit2026InstallDir
                        Force = $true
                    }
                    if ($AllowRunningRevit) {
                        $installArgs.AllowRunningRevit = $true
                    }
                    """,
                    """
                    $installArgs = @(
                        "-RevitYears", "2026",
                        "-Revit2026InstallDir", $Revit2026InstallDir,
                        "-Force"
                    )
                    if ($AllowRunningRevit) {
                        $installArgs += "-AllowRunningRevit"
                    }
                    """,
                    StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "installer-current-source-handoff:named-splat" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_CurrentSourceRevit2026HandoffWithoutUncSnapshot_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var path = Path.Combine(_root, "scripts", "install-current-source-revit2026.ps1");
        File.WriteAllText(
            path,
            File.ReadAllText(path).Replace("current-source-snapshot", "source-snapshot", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "installer-current-source-handoff:unc-snapshot" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_WslCurrentSourceSmokeWithoutLiveAddinCommit_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var path = Path.Combine(_root, "scripts", "smoke-revit-wsl.sh");
        File.WriteAllText(
            path,
            File.ReadAllText(path).Replace("liveAddinCommit", "liveCommit", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "smoke-script-wsl:live-addin-commit" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_WslCurrentSourceSmokeWithoutDriftKind_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var path = Path.Combine(_root, "scripts", "smoke-revit-wsl.sh");
        File.WriteAllText(
            path,
            File.ReadAllText(path).Replace("currentSourceDriftKind", "sourceDrift", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "smoke-script-wsl:drift-kind" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_Markdown_PrintsReviewSections()
    {
        WriteHealthyTree(_root);
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "markdown", "v2.3.0", strict: false, output);

        var text = output.ToString();
        Assert.True(exitCode == 0, output.ToString());
        Assert.Contains("# Release Verification", text);
        Assert.Contains("- Status: `PASS`", text);
        Assert.Contains("- Tag: `v2.3.0`", text);
        Assert.Contains("## Errors", text);
        Assert.Contains("- None.", text);
        Assert.Contains("## Passing Checks", text);
        Assert.Contains("| OK | ci:no-addin-build | .github/workflows/ci.yml | Ubuntu CI does not build the Windows/Revit add-in. |", text);
        Assert.Contains("| OK | ci:workbench-verify | .github/workflows/ci.yml | Ubuntu CI runs the v4 workbench contract verifier. |", text);
        Assert.Contains("| OK | ci:workbench-v2-verify | .github/workflows/ci.yml | Ubuntu CI runs the v5 workbench contract verifier. |", text);
        Assert.Contains("| OK | smoke-script:v4-workbench | scripts/smoke-revit.ps1 | Real smoke script can run the v4 workbench and live discovery gate. |", text);
        Assert.Contains("## Gate Scope", text);
        Assert.Contains("Real Revit smoke remains a separate Windows/Revit checklist gate.", text);
        Assert.Contains("release verify --strict", text);
    }

    [Fact]
    public async Task PilotScaffold_Json_WritesPublicSafePacket()
    {
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotScaffoldAsync(
            _root,
            "v6-pilot-2026-office-copy-01",
            evidencePacketPath: null,
            force: false,
            outputFormat: "json",
            output);

        Assert.True(exitCode == 0, output.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("release-pilot-scaffold.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("wrote").GetBoolean());
        Assert.False(root.GetProperty("force").GetBoolean());
        Assert.Equal("v6-pilot-2026-office-copy-01", root.GetProperty("pilotId").GetString());
        Assert.Equal("docs/smoke/v6.0/v6-pilot-2026-office-copy-01.md", root.GetProperty("evidencePacketPath").GetString());
        Assert.Contains("office-rollout-status.json", root.GetProperty("rolloutStatusHint").GetString()!, StringComparison.Ordinal);

        var packetPath = Path.Combine(_root, "docs", "smoke", "v6.0", "v6-pilot-2026-office-copy-01.md");
        Assert.True(File.Exists(packetPath));
        var packet = File.ReadAllText(packetPath);
        Assert.Contains("## Required Commands", packet);
        Assert.Contains("doctor --check-version 2026 --output json", packet);
        Assert.Contains("status --output json", packet);
        Assert.Contains("workbench verify --contract workbench-contract.v2", packet);
        Assert.Contains("release verify --strict --output json", packet);
        Assert.Contains("ledger query --source ledger --output json", packet);
        Assert.Contains("ledger validate --source ledger --output json", packet);
        Assert.Contains("ledger stats --source ledger --analytics-snapshot", packet);
        Assert.Contains("ledger timeline --source ledger --analytics-snapshot", packet);
        Assert.Contains("journal verify --output json", packet);
        Assert.Contains("## Live Operation Evidence", packet);
        Assert.Contains("Rollback result", packet);
        Assert.Contains("## User Review", packet);
        Assert.Contains("BIM manager signoff", packet);
        Assert.Contains("Project-copy owner signoff", packet);
        Assert.Contains("Support ticket review", packet);
        Assert.Contains("Multi-user rollout postmortem", packet);
        Assert.Contains("Boundary summary", packet);
    }

    [Theory]
    [InlineData("pilot with spaces", null)]
    [InlineData("pilot-01", "../pilot-01.md")]
    [InlineData("pilot-01", "C:/temp/pilot-01.md")]
    [InlineData("pilot-01", "docs/smoke/v6.0/../pilot-01.md")]
    [InlineData("pilot-01", "docs/smoke/v6.0/pilot-01.txt")]
    public async Task PilotScaffold_RejectsUnsafeInputs(string pilotId, string? evidencePacketPath)
    {
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotScaffoldAsync(
            _root,
            pilotId,
            evidencePacketPath,
            force: false,
            outputFormat: "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.False(json.RootElement.GetProperty("wrote").GetBoolean());
        Assert.False(File.Exists(Path.Combine(_root, "pilot-01.md")));
    }

    [Fact]
    public async Task PilotScaffold_RefusesExistingWithoutForce()
    {
        var relativePath = "docs/smoke/v6.0/pilot-01.md";
        var fullPath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-01.md");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "existing packet");
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotScaffoldAsync(
            _root,
            "pilot-01",
            relativePath,
            force: false,
            outputFormat: "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.False(json.RootElement.GetProperty("wrote").GetBoolean());
        Assert.Contains("already exists", json.RootElement.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("existing packet", File.ReadAllText(fullPath));
    }

    [Fact]
    public async Task PilotScaffold_ForceOverwritesExistingPacket()
    {
        var relativePath = "docs/smoke/v6.0/pilot-01.md";
        var fullPath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-01.md");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "existing packet");
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotScaffoldAsync(
            _root,
            "pilot-01",
            relativePath,
            force: true,
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.True(json.RootElement.GetProperty("wrote").GetBoolean());
        Assert.True(json.RootElement.GetProperty("force").GetBoolean());
        Assert.Contains("Pilot identifier: pilot-01", File.ReadAllText(fullPath));
    }

    [Fact]
    public async Task PilotValidate_Json_AcceptsCompletedPublicSafePacket()
    {
        WriteFile(
            "docs/smoke/v6.0/pilot-01.md",
            CompletedPilotEvidencePacketContent("pilot-01") + "\nReference: https://example.com/pilot-evidence\n");
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotValidateAsync(
            _root,
            "docs/smoke/v6.0/pilot-01.md",
            "json",
            output);

        Assert.True(exitCode == 0, output.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("release-pilot-validate.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(0, root.GetProperty("errorCount").GetInt32());
        Assert.Equal(0, root.GetProperty("warningCount").GetInt32());
        Assert.Empty(root.GetProperty("issues").EnumerateArray());
    }

    [Fact]
    public async Task PilotValidate_ScaffoldWithBlankFields_ReturnsFailure()
    {
        var scaffoldOutput = new StringWriter();
        Assert.Equal(0, await ReleaseCommand.ExecutePilotScaffoldAsync(
            _root,
            "pilot-01",
            evidencePacketPath: null,
            force: false,
            outputFormat: "json",
            scaffoldOutput));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotValidateAsync(
            _root,
            "docs/smoke/v6.0/pilot-01.md",
            "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("id").GetString() == "blank-scaffold-field" &&
            issue.GetProperty("message").GetString()!.Contains("Date/time", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PilotValidate_LocalAbsolutePath_ReturnsFailure()
    {
        WriteFile("docs/smoke/v6.0/pilot-01.md", CompletedPilotEvidencePacketContent("pilot-01") + """

        Private path that must not be checked in: C:\Users\Lenovo\receipt.json
        """);
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotValidateAsync(
            _root,
            "docs/smoke/v6.0/pilot-01.md",
            "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("id").GetString() == "public-safety-path" &&
            issue.GetProperty("lineNumber").GetInt32() > 0);
    }

    [Fact]
    public async Task PilotValidate_MissingRequiredEvidence_ReturnsFailure()
    {
        WriteFile(
            "docs/smoke/v6.0/pilot-01.md",
            CompletedPilotEvidencePacketContent("pilot-01").Replace("Rollback result", "Rollback note", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotValidateAsync(
            _root,
            "docs/smoke/v6.0/pilot-01.md",
            "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("id").GetString() == "missing-required-evidence" &&
            issue.GetProperty("message").GetString()!.Contains("Rollback result", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PilotValidate_RejectsUnsafePath()
    {
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotValidateAsync(
            _root,
            "../pilot-01.md",
            "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("id").GetString() == "path-safety");
    }

    [Fact]
    public async Task PilotRegister_DryRun_DoesNotWriteRolloutStatus()
    {
        WriteHealthyTree(_root);
        WriteFile("docs/smoke/v6.0/pilot-01.md", CompletedPilotEvidencePacketContent("pilot-01"));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotRegisterAsync(
            _root,
            "pilot-01",
            "docs/smoke/v6.0/pilot-01.md",
            yes: false,
            outputFormat: "json",
            output);

        Assert.True(exitCode == 0, output.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("release-pilot-register.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("dryRun").GetBoolean());
        Assert.False(root.GetProperty("wrote").GetBoolean());
        Assert.Equal(1, root.GetProperty("completedOfficePilotCount").GetInt32());
        Assert.Equal(2, root.GetProperty("minimumOfficePilotCount").GetInt32());
        Assert.Contains(root.GetProperty("nextActions").EnumerateArray(), action =>
            action.GetString() == "release pilot register --pilot-id pilot-01 --path docs/smoke/v6.0/pilot-01.md --yes --output json");

        using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "docs", "smoke", "v6.0", "office-rollout-status.json")));
        Assert.Equal(0, status.RootElement.GetProperty("completedOfficePilotCount").GetInt32());
        Assert.Empty(status.RootElement.GetProperty("completedPilotIds").EnumerateArray());
    }

    [Fact]
    public async Task PilotRegister_Yes_WritesCompletedPilotEntry()
    {
        WriteHealthyTree(_root);
        WriteFile("docs/smoke/v6.0/pilot-01.md", CompletedPilotEvidencePacketContent("pilot-01"));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotRegisterAsync(
            _root,
            "pilot-01",
            "docs/smoke/v6.0/pilot-01.md",
            yes: true,
            outputFormat: "json",
            output);

        Assert.True(exitCode == 0, output.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.False(json.RootElement.GetProperty("dryRun").GetBoolean());
        Assert.True(json.RootElement.GetProperty("wrote").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("nextActions").EnumerateArray(), action =>
            action.GetString() == "release pilot status --output json");
        Assert.Contains(json.RootElement.GetProperty("nextActions").EnumerateArray(), action =>
            action.GetString() == "release pilot scaffold --pilot-id <public-id> --output json");

        using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "docs", "smoke", "v6.0", "office-rollout-status.json")));
        var statusRoot = status.RootElement;
        Assert.Equal(1, statusRoot.GetProperty("completedOfficePilotCount").GetInt32());
        Assert.Equal("pilot-01", statusRoot.GetProperty("completedPilotIds")[0].GetString());
        var pilot = statusRoot.GetProperty("completedPilots")[0];
        Assert.Equal("pilot-01", pilot.GetProperty("pilotId").GetString());
        Assert.Equal("docs/smoke/v6.0/pilot-01.md", pilot.GetProperty("evidencePacketPath").GetString());
        Assert.True(pilot.GetProperty("doctor").GetBoolean());
        Assert.True(pilot.GetProperty("multiUserRolloutPostmortem").GetBoolean());
        Assert.False(statusRoot.GetProperty("officeRolloutCompletion").GetBoolean());
        Assert.False(statusRoot.GetProperty("productionSupportClaim").GetBoolean());
    }

    [Fact]
    public async Task PilotRegister_InvalidPacket_ReturnsFailureWithoutWritingStatus()
    {
        WriteHealthyTree(_root);
        Assert.Equal(0, await ReleaseCommand.ExecutePilotScaffoldAsync(
            _root,
            "pilot-01",
            evidencePacketPath: null,
            force: false,
            outputFormat: "json",
            output: new StringWriter()));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotRegisterAsync(
            _root,
            "pilot-01",
            "docs/smoke/v6.0/pilot-01.md",
            yes: true,
            outputFormat: "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.False(json.RootElement.GetProperty("wrote").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("id").GetString() == "blank-scaffold-field");
        Assert.Contains(json.RootElement.GetProperty("nextActions").EnumerateArray(), action =>
            action.GetString() == "release pilot validate --path docs/smoke/v6.0/pilot-01.md --output json");
        using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "docs", "smoke", "v6.0", "office-rollout-status.json")));
        Assert.Equal(0, status.RootElement.GetProperty("completedOfficePilotCount").GetInt32());
    }

    [Fact]
    public async Task PilotRegister_DuplicatePilot_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        WriteFile("docs/smoke/v6.0/pilot-01.md", CompletedPilotEvidencePacketContent("pilot-01"));
        Assert.Equal(0, await ReleaseCommand.ExecutePilotRegisterAsync(
            _root,
            "pilot-01",
            "docs/smoke/v6.0/pilot-01.md",
            yes: true,
            outputFormat: "json",
            output: new StringWriter()));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotRegisterAsync(
            _root,
            "pilot-01",
            "docs/smoke/v6.0/pilot-01.md",
            yes: false,
            outputFormat: "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("id").GetString() == "pilot-duplicate");
    }

    [Fact]
    public async Task PilotRegister_PacketPilotIdMismatch_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        WriteFile("docs/smoke/v6.0/pilot-01.md", CompletedPilotEvidencePacketContent("pilot-01"));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotRegisterAsync(
            _root,
            "pilot-02",
            "docs/smoke/v6.0/pilot-01.md",
            yes: true,
            outputFormat: "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.False(root.GetProperty("wrote").GetBoolean());
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("id").GetString() == "pilot-id-mismatch");
        using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "docs", "smoke", "v6.0", "office-rollout-status.json")));
        Assert.Equal(0, status.RootElement.GetProperty("completedOfficePilotCount").GetInt32());
    }

    [Fact]
    public async Task PilotStatus_Json_ReportsRemainingPilots()
    {
        WriteHealthyTree(_root);
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotStatusAsync(
            _root,
            "json",
            output);

        Assert.True(exitCode == 0, output.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("release-pilot-status.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("docs/smoke/v6.0/office-rollout-status.json", root.GetProperty("statusPath").GetString());
        Assert.Equal(2, root.GetProperty("minimumOfficePilotCount").GetInt32());
        Assert.Equal(0, root.GetProperty("completedOfficePilotCount").GetInt32());
        Assert.Equal(2, root.GetProperty("remainingOfficePilotCount").GetInt32());
        Assert.Equal(0, root.GetProperty("evidenceCompleteOfficePilotCount").GetInt32());
        Assert.Equal(2, root.GetProperty("remainingEvidenceCompleteOfficePilotCount").GetInt32());
        Assert.False(root.GetProperty("officeRolloutCompletion").GetBoolean());
        Assert.False(root.GetProperty("productionSupportClaim").GetBoolean());
        Assert.False(root.GetProperty("canClaimOfficeRollout").GetBoolean());
        Assert.Empty(root.GetProperty("completedPilots").EnumerateArray());
        Assert.Equal(0, root.GetProperty("errorCount").GetInt32());
        Assert.Empty(root.GetProperty("issues").EnumerateArray());
        var nextActions = root.GetProperty("nextActions").EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Contains("release pilot scaffold --pilot-id <public-id> --output json", nextActions);
        Assert.Contains("release pilot validate --path docs/smoke/v6.0/<public-id>.md --output json", nextActions);
        Assert.Contains("release pilot register --pilot-id <public-id> --path docs/smoke/v6.0/<public-id>.md --output json", nextActions);
        Assert.Contains("2 more completed office pilot", root.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PilotStatus_RegisteredPilot_ReportsPacketValidation()
    {
        WriteHealthyTree(_root);
        WriteFile("docs/smoke/v6.0/pilot-01.md", CompletedPilotEvidencePacketContent("pilot-01"));
        Assert.Equal(0, await ReleaseCommand.ExecutePilotRegisterAsync(
            _root,
            "pilot-01",
            "docs/smoke/v6.0/pilot-01.md",
            yes: true,
            outputFormat: "json",
            output: new StringWriter()));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotStatusAsync(
            _root,
            "json",
            output);

        Assert.True(exitCode == 0, output.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(1, root.GetProperty("completedOfficePilotCount").GetInt32());
        Assert.Equal(1, root.GetProperty("remainingOfficePilotCount").GetInt32());
        Assert.Equal(1, root.GetProperty("evidenceCompleteOfficePilotCount").GetInt32());
        Assert.Equal(1, root.GetProperty("remainingEvidenceCompleteOfficePilotCount").GetInt32());
        Assert.False(root.GetProperty("canClaimOfficeRollout").GetBoolean());
        var pilot = root.GetProperty("completedPilots")[0];
        Assert.Equal("pilot-01", pilot.GetProperty("pilotId").GetString());
        Assert.Equal("docs/smoke/v6.0/pilot-01.md", pilot.GetProperty("evidencePacketPath").GetString());
        Assert.True(pilot.GetProperty("validationSuccess").GetBoolean());
        Assert.Equal(0, pilot.GetProperty("validationErrorCount").GetInt32());
        Assert.Equal(0, pilot.GetProperty("missingEvidenceCount").GetInt32());
        Assert.Empty(pilot.GetProperty("missingEvidence").EnumerateArray());
        Assert.Empty(root.GetProperty("missingEvidenceSummary").EnumerateArray());
        Assert.Contains(
            root.GetProperty("nextActions").EnumerateArray(),
            item => item.GetString() == "release pilot scaffold --pilot-id <public-id> --output json");
    }

    [Fact]
    public async Task PilotStatus_RegisteredPilotReportsMissingEvidenceFlags()
    {
        WriteHealthyTree(_root);
        WriteFile("docs/smoke/v6.0/pilot-01.md", CompletedPilotEvidencePacketContent("pilot-01"));
        WriteFile("docs/smoke/v6.0/office-rollout-status.json", $$"""
{
  "schemaVersion": "v6-office-rollout-status.v1",
  "minimumOfficePilotCount": 2,
  "completedOfficePilotCount": 1,
  "completedPilotIds": ["pilot-01"],
  "completedPilots": [
    {
      "pilotId": "pilot-01",
      "evidencePacketPath": "docs/smoke/v6.0/pilot-01.md",
      "doctor": true,
      "status": true,
      "workbench": true,
      "release": true,
      "ledgerQuery": true,
      "ledgerValidate": true,
      "ledgerStatsAnalyticsSnapshot": true,
      "ledgerTimelineAnalyticsSnapshot": true,
      "journalVerify": true,
      "rollbackResult": false,
      "userReview": true,
      "bimManagerSignoff": false,
      "projectCopyOwnerSignoff": true,
      "supportTicketReview": true,
      "multiUserRolloutPostmortem": true
    }
  ],
  "officeRolloutCompletion": false,
  "productionSupportClaim": false,
  "requiredEvidence": {
    "doctor": true,
    "status": true,
    "workbench": true,
    "release": true,
    "ledgerQuery": true,
    "ledgerValidate": true,
    "ledgerStatsAnalyticsSnapshot": true,
    "ledgerTimelineAnalyticsSnapshot": true,
    "journalVerify": true,
    "rollbackResult": true,
    "userReview": true,
    "bimManagerSignoff": true,
    "projectCopyOwnerSignoff": true,
    "supportTicketReview": true,
    "multiUserRolloutPostmortem": true
  }
}
""");
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotStatusAsync(
            _root,
            "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(1, json.RootElement.GetProperty("completedOfficePilotCount").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("remainingOfficePilotCount").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("evidenceCompleteOfficePilotCount").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("remainingEvidenceCompleteOfficePilotCount").GetInt32());
        var pilot = json.RootElement.GetProperty("completedPilots")[0];
        Assert.True(pilot.GetProperty("validationSuccess").GetBoolean());
        Assert.Equal(2, pilot.GetProperty("missingEvidenceCount").GetInt32());
        var missing = pilot.GetProperty("missingEvidence").EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Contains("rollbackResult", missing);
        Assert.Contains("bimManagerSignoff", missing);
        var summary = json.RootElement.GetProperty("missingEvidenceSummary").EnumerateArray().ToArray();
        Assert.Equal(2, summary.Length);
        Assert.Contains(summary, item =>
            item.GetProperty("evidence").GetString() == "bimManagerSignoff" &&
            item.GetProperty("missingPilotCount").GetInt32() == 1 &&
            item.GetProperty("pilotIds")[0].GetString() == "pilot-01");
        Assert.Contains(summary, item =>
            item.GetProperty("evidence").GetString() == "rollbackResult" &&
            item.GetProperty("missingPilotCount").GetInt32() == 1 &&
            item.GetProperty("pilotIds")[0].GetString() == "pilot-01");
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("id").GetString() == "rollout-status-completed-pilot-evidence");
        Assert.Contains(json.RootElement.GetProperty("nextActions").EnumerateArray(), item =>
            item.GetString() == "complete missingEvidence for registered pilots");
    }

    [Fact]
    public async Task PilotStatus_InvalidRegisteredPacket_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        WriteFile("docs/smoke/v6.0/office-rollout-status.json", $$"""
{
  "schemaVersion": "v6-office-rollout-status.v1",
  "minimumOfficePilotCount": 2,
  "completedOfficePilotCount": 1,
  "completedPilotIds": ["pilot-01"],
  "completedPilots": [{{CompletedPilotEvidenceJson("pilot-01")}}],
  "officeRolloutCompletion": false,
  "productionSupportClaim": false,
  "requiredEvidence": {
    "doctor": true,
    "status": true,
    "workbench": true,
    "release": true,
    "ledgerQuery": true,
    "ledgerValidate": true,
    "ledgerStatsAnalyticsSnapshot": true,
    "ledgerTimelineAnalyticsSnapshot": true,
    "journalVerify": true,
    "rollbackResult": true,
    "userReview": true,
    "bimManagerSignoff": true,
    "projectCopyOwnerSignoff": true,
    "supportTicketReview": true,
    "multiUserRolloutPostmortem": true
  }
}
""");
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotStatusAsync(
            _root,
            "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(1, root.GetProperty("completedOfficePilotCount").GetInt32());
        Assert.False(root.GetProperty("completedPilots")[0].GetProperty("validationSuccess").GetBoolean());
        Assert.True(root.GetProperty("completedPilots")[0].GetProperty("validationErrorCount").GetInt32() > 0);
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("id").GetString() == "packet-missing");
    }

    [Fact]
    public async Task PilotStatus_PacketPilotIdMismatch_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        WriteFile("docs/smoke/v6.0/pilot-02.md", CompletedPilotEvidencePacketContent("pilot-01"));
        WriteFile("docs/smoke/v6.0/office-rollout-status.json", $$"""
{
  "schemaVersion": "v6-office-rollout-status.v1",
  "minimumOfficePilotCount": 2,
  "completedOfficePilotCount": 1,
  "completedPilotIds": ["pilot-02"],
  "completedPilots": [{{CompletedPilotEvidenceJson("pilot-02")}}],
  "officeRolloutCompletion": false,
  "productionSupportClaim": false,
  "requiredEvidence": {
    "doctor": true,
    "status": true,
    "workbench": true,
    "release": true,
    "ledgerQuery": true,
    "ledgerValidate": true,
    "ledgerStatsAnalyticsSnapshot": true,
    "ledgerTimelineAnalyticsSnapshot": true,
    "journalVerify": true,
    "rollbackResult": true,
    "userReview": true,
    "bimManagerSignoff": true,
    "projectCopyOwnerSignoff": true,
    "supportTicketReview": true,
    "multiUserRolloutPostmortem": true
  }
}
""");
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotStatusAsync(
            _root,
            "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.False(root.GetProperty("completedPilots")[0].GetProperty("validationSuccess").GetBoolean());
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("id").GetString() == "pilot-id-mismatch");
    }

    [Fact]
    public async Task PilotClaim_InsufficientPilots_ReturnsFailureWithoutWriting()
    {
        WriteHealthyTree(_root);
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotClaimAsync(
            _root,
            yes: true,
            productionSupport: false,
            outputFormat: "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("release-pilot-claim.v1", root.GetProperty("schemaVersion").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.False(root.GetProperty("wrote").GetBoolean());
        Assert.False(root.GetProperty("canClaimOfficeRollout").GetBoolean());
        Assert.Equal(2, root.GetProperty("remainingOfficePilotCount").GetInt32());
        Assert.Equal(0, root.GetProperty("evidenceCompleteOfficePilotCount").GetInt32());
        Assert.Equal(2, root.GetProperty("remainingEvidenceCompleteOfficePilotCount").GetInt32());
        var blockers = root.GetProperty("claimBlockers").EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Contains("completedOfficePilotCount", blockers);
        Assert.Contains("evidenceCompleteOfficePilotCount", blockers);
        Assert.Contains(root.GetProperty("nextActions").EnumerateArray(), item =>
            item.GetString() == "release pilot scaffold --pilot-id <public-id> --output json");
        using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "docs", "smoke", "v6.0", "office-rollout-status.json")));
        Assert.False(status.RootElement.GetProperty("officeRolloutCompletion").GetBoolean());
        Assert.False(status.RootElement.GetProperty("productionSupportClaim").GetBoolean());
    }

    [Fact]
    public async Task PilotClaim_IncompleteRegisteredEvidence_ReportsClaimBlockers()
    {
        WriteHealthyTree(_root);
        WriteFile("docs/smoke/v6.0/pilot-01.md", CompletedPilotEvidencePacketContent("pilot-01"));
        WriteFile("docs/smoke/v6.0/office-rollout-status.json", $$"""
{
  "schemaVersion": "v6-office-rollout-status.v1",
  "minimumOfficePilotCount": 2,
  "completedOfficePilotCount": 1,
  "completedPilotIds": ["pilot-01"],
  "completedPilots": [
    {
      "pilotId": "pilot-01",
      "evidencePacketPath": "docs/smoke/v6.0/pilot-01.md",
      "doctor": true,
      "status": true,
      "workbench": true,
      "release": true,
      "ledgerQuery": true,
      "ledgerValidate": true,
      "ledgerStatsAnalyticsSnapshot": true,
      "ledgerTimelineAnalyticsSnapshot": true,
      "journalVerify": true,
      "rollbackResult": false,
      "userReview": true,
      "bimManagerSignoff": true,
      "projectCopyOwnerSignoff": true,
      "supportTicketReview": true,
      "multiUserRolloutPostmortem": true
    }
  ],
  "officeRolloutCompletion": false,
  "productionSupportClaim": false,
  "requiredEvidence": {
    "doctor": true,
    "status": true,
    "workbench": true,
    "release": true,
    "ledgerQuery": true,
    "ledgerValidate": true,
    "ledgerStatsAnalyticsSnapshot": true,
    "ledgerTimelineAnalyticsSnapshot": true,
    "journalVerify": true,
    "rollbackResult": true,
    "userReview": true,
    "bimManagerSignoff": true,
    "projectCopyOwnerSignoff": true,
    "supportTicketReview": true,
    "multiUserRolloutPostmortem": true
  }
}
""");
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotClaimAsync(
            _root,
            yes: true,
            productionSupport: false,
            outputFormat: "json",
            output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.False(root.GetProperty("wrote").GetBoolean());
        Assert.Equal(0, root.GetProperty("evidenceCompleteOfficePilotCount").GetInt32());
        var blockers = root.GetProperty("claimBlockers").EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Contains("statusValidation", blockers);
        Assert.Contains("completedOfficePilotCount", blockers);
        Assert.Contains("evidenceCompleteOfficePilotCount", blockers);
        Assert.Contains("missingEvidence", blockers);
        Assert.Contains(root.GetProperty("nextActions").EnumerateArray(), item =>
            item.GetString() == "complete missingEvidence for registered pilots");
    }

    [Fact]
    public async Task PilotClaim_DryRun_WithCompletedPilots_DoesNotWrite()
    {
        WriteHealthyTree(_root);
        await RegisterCompletedPilotAsync("pilot-01");
        await RegisterCompletedPilotAsync("pilot-02");
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotClaimAsync(
            _root,
            yes: false,
            productionSupport: false,
            outputFormat: "json",
            output);

        Assert.True(exitCode == 0, output.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("dryRun").GetBoolean());
        Assert.False(root.GetProperty("wrote").GetBoolean());
        Assert.True(root.GetProperty("canClaimOfficeRollout").GetBoolean());
        Assert.Equal(2, root.GetProperty("evidenceCompleteOfficePilotCount").GetInt32());
        Assert.Equal(0, root.GetProperty("remainingEvidenceCompleteOfficePilotCount").GetInt32());
        Assert.Empty(root.GetProperty("claimBlockers").EnumerateArray());
        var nextActions = root.GetProperty("nextActions").EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Contains("release pilot claim --output json", nextActions);
        Assert.Contains("release pilot claim --yes --output json", nextActions);
        Assert.False(root.GetProperty("officeRolloutCompletionBefore").GetBoolean());
        Assert.True(root.GetProperty("officeRolloutCompletionAfter").GetBoolean());
        Assert.False(root.GetProperty("productionSupportClaimAfter").GetBoolean());
        Assert.Empty(root.GetProperty("claimBlockers").EnumerateArray());
        using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "docs", "smoke", "v6.0", "office-rollout-status.json")));
        Assert.False(status.RootElement.GetProperty("officeRolloutCompletion").GetBoolean());
        Assert.False(status.RootElement.GetProperty("productionSupportClaim").GetBoolean());
    }

    [Fact]
    public async Task PilotClaim_Yes_WithCompletedPilots_MarksCompletionOnly()
    {
        WriteHealthyTree(_root);
        await RegisterCompletedPilotAsync("pilot-01");
        await RegisterCompletedPilotAsync("pilot-02");
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotClaimAsync(
            _root,
            yes: true,
            productionSupport: false,
            outputFormat: "json",
            output);

        Assert.True(exitCode == 0, output.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.False(root.GetProperty("dryRun").GetBoolean());
        Assert.True(root.GetProperty("wrote").GetBoolean());
        Assert.True(root.GetProperty("officeRolloutCompletionAfter").GetBoolean());
        Assert.False(root.GetProperty("productionSupportClaimAfter").GetBoolean());
        using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "docs", "smoke", "v6.0", "office-rollout-status.json")));
        Assert.True(status.RootElement.GetProperty("officeRolloutCompletion").GetBoolean());
        Assert.False(status.RootElement.GetProperty("productionSupportClaim").GetBoolean());
    }

    [Fact]
    public async Task PilotClaim_YesWithProductionSupport_MarksBothClaims()
    {
        WriteHealthyTree(_root);
        await RegisterCompletedPilotAsync("pilot-01");
        await RegisterCompletedPilotAsync("pilot-02");
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecutePilotClaimAsync(
            _root,
            yes: true,
            productionSupport: true,
            outputFormat: "json",
            output);

        Assert.True(exitCode == 0, output.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("requestedProductionSupportClaim").GetBoolean());
        Assert.True(root.GetProperty("officeRolloutCompletionAfter").GetBoolean());
        Assert.True(root.GetProperty("productionSupportClaimAfter").GetBoolean());
        Assert.Empty(root.GetProperty("claimBlockers").EnumerateArray());
        using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "docs", "smoke", "v6.0", "office-rollout-status.json")));
        Assert.True(status.RootElement.GetProperty("officeRolloutCompletion").GetBoolean());
        Assert.True(status.RootElement.GetProperty("productionSupportClaim").GetBoolean());
    }

    [Fact]
    public async Task Verify_Strict_WithDisclosedV5NoGo_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        WriteV5NoGoDocs(_root);
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: true, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("warningCount").GetInt32() >= 2);
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5-rc:status" &&
            check.GetProperty("status").GetString() == "warning" &&
            check.GetProperty("message").GetString()!.Contains("NO-GO", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5-rc:smoke-no-go" &&
            check.GetProperty("status").GetString() == "warning" &&
            check.GetProperty("message").GetString()!.Contains("NO-GO", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_Strict_WithClaimed2026OnlyAndDisclosedOtherYears_ReturnsSuccess()
    {
        WriteHealthyTree(_root);
        File.Delete(Path.Combine(_root, "docs", "smoke", "v5.0", "revit-2024-issue-closure.md"));
        File.Delete(Path.Combine(_root, "docs", "smoke", "v5.0", "revit-2025-issue-closure.md"));
        WriteFile("docs/v5-rc-readiness.md", """
# RevitCli v5.0 RC Readiness

> Current status: GO.
> Claimed live Revit years: 2026.

## Stable P0 Commands

`workbench verify --contract workbench-contract.v2` exposes `v5RealSmokeDisclosure`, `issuePackageTraceability`, and `v5FaultInjectionCoverage`.

## Experimental / Deferred Commands

not live verified gaps remain documented in smoke reports.
Views, links, model map, dashboard, MCP, SaaS, or built-in LLM parser remain outside the v5.0 RC production claim.

Run `release verify --strict`.
""");
        WriteFile("docs/smoke/v5.0/gap-report.md", """
# RevitCli v5.0 Live Smoke Gap Report

| Revit year | v5.0 issue-closure live smoke status | Notes |
| --- | --- | --- |
| Revit 2024 | not live verified | No controlled issue-closure evidence is recorded. |
| Revit 2025 | not live verified | No controlled issue-closure evidence is recorded. |
| Revit 2026 | live verified | Evidence exists. |
""");
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: true, output);

        Assert.True(exitCode == 0, output.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(0, root.GetProperty("warningCount").GetInt32());
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5-rc:smoke-no-go" &&
            check.GetProperty("status").GetString() == "ok" &&
            check.GetProperty("message").GetString()!.Contains("Revit 2026", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_TagMismatch_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", "v2.4.0", strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var failed = json.RootElement.GetProperty("checks")
            .EnumerateArray()
            .Single(check => check.GetProperty("id").GetString() == "version:tag-match");
        Assert.Equal("error", failed.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Verify_MissingV54OfficeStandardPack_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        Directory.Delete(Path.Combine(_root, "profiles", "office-standard"), recursive: true);
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5.4:office-standard-pack" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_MissingV55GapReport_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        File.Delete(Path.Combine(_root, "docs", "smoke", "v5.5", "gap-report.md"));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5.5:view-coordination-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("Missing docs/smoke/v5.5", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingV55NoCoordinateDisclosure_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var gapPath = Path.Combine(_root, "docs", "smoke", "v5.5", "gap-report.md");
        File.WriteAllText(
            gapPath,
            File.ReadAllText(gapPath).Replace("has no coordinate moves", "does not change link files", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5.5:no-coordinate-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("no coordinate moves", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingV56TeamPolicy_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        Directory.Delete(Path.Combine(_root, "profiles", "team-pilot"), recursive: true);
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5.6:team-policy" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingV56SupportTemplate_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        File.Delete(Path.Combine(_root, "docs", "smoke", "v5.6", "support-error-report-template.md"));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5.6:team-policy" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("support-error-report", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingV56DashboardCentralDisclosure_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var gapPath = Path.Combine(_root, "docs", "smoke", "v5.6", "gap-report.md");
        File.WriteAllText(
            gapPath,
            File.ReadAllText(gapPath).Replace("no dashboard-central, and ", "", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5.6:no-dashboard-central-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("dashboard-central", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("docs/smoke/v5.5/gap-report.md", "no MCP", "no MCP but requires MCP", "v5.5:no-mcp-doc")]
    [InlineData("docs/smoke/v5.5/gap-report.md", "no built-in LLM", "no built-in LLM but uses built-in LLM", "v5.5:no-llm-doc")]
    [InlineData("docs/smoke/v5.6/gap-report.md", "No SaaS", "No SaaS but uses SaaS", "v5.6:no-saas-doc")]
    [InlineData("docs/smoke/v5.6/gap-report.md", "no dashboard-central", "no dashboard-central but requires dashboard-central", "v5.6:no-dashboard-central-doc")]
    public async Task Verify_V55V56BoundaryContradiction_ReturnsFailure(
        string relativePath,
        string requiredPhrase,
        string replacement,
        string expectedCheckId)
    {
        WriteHealthyTree(_root);
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var text = File.ReadAllText(path);
        Assert.Contains(requiredPhrase, text, StringComparison.Ordinal);
        File.WriteAllText(path, text.Replace(requiredPhrase, replacement, StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == expectedCheckId &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("contradictory wording", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("docs/smoke/v5.5/gap-report.md", "No SaaS, no MCP", "SaaS, MCP", "v5.5:no-saas-doc")]
    [InlineData("docs/smoke/v5.6/gap-report.md", "No SaaS, no MCP", "SaaS, MCP", "v5.6:no-saas-doc")]
    public async Task Verify_V55V56BareBoundaryTerm_ReturnsFailure(
        string relativePath,
        string boundaryPhrase,
        string replacement,
        string expectedCheckId)
    {
        WriteHealthyTree(_root);
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var text = File.ReadAllText(path);
        Assert.Contains(boundaryPhrase, text, StringComparison.Ordinal);
        File.WriteAllText(path, text.Replace(boundaryPhrase, replacement, StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == expectedCheckId &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("not only a bare mention", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingV60Contract_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        File.Delete(Path.Combine(_root, "docs", "v6-local-bimops-contract.md"));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:contract-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("missing", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("v6.0:deterministic-receipt-doc", "deterministic receipt", "stable receipt")]
    [InlineData("v6.0:receipt-hash-doc", "receiptHash", "receiptDigest")]
    [InlineData("v6.0:rollback-pointer-doc", "rollbackPointer", "rollbackLink")]
    [InlineData("v6.0:release-pilot-register-doc", "release pilot register", "release pilot record")]
    [InlineData("v6.0:release-pilot-status-doc", "release pilot status", "release pilot progress")]
    [InlineData("v6.0:release-pilot-claim-doc", "release pilot claim", "release pilot approve")]
    public async Task Verify_MissingV60ContractLedgerFieldPhrase_ReturnsFailure(
        string checkId,
        string requiredPhrase,
        string replacement)
    {
        WriteHealthyTree(_root);
        var contractPath = Path.Combine(_root, "docs", "v6-local-bimops-contract.md");
        File.WriteAllText(
            contractPath,
            File.ReadAllText(contractPath).Replace(requiredPhrase, replacement, StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == checkId &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains(requiredPhrase, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingV60GapReport_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        File.Delete(Path.Combine(_root, "docs", "smoke", "v6.0", "gap-report.md"));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:gap-report" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingV60OfficePilotGap_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var gapPath = Path.Combine(_root, "docs", "smoke", "v6.0", "gap-report.md");
        File.WriteAllText(
            gapPath,
            File.ReadAllText(gapPath).Replace("office rollout pilots", "rollout pilots", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:office-pilot-gap-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("office rollout pilots", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingV60PilotEvidenceTemplate_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        File.Delete(Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-evidence-template.md"));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-template" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("pilot-evidence-template.md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_IncompleteV60PilotEvidenceTemplate_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var templatePath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
        File.WriteAllText(
            templatePath,
            File.ReadAllText(templatePath).Replace("v6.0 Office Rollout Pilot Evidence Packet", "v6.0 pilot notes", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-template" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("office rollout pilot evidence packet", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingV60PilotEvidenceSignoff_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var templatePath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
        File.WriteAllText(
            templatePath,
            File.ReadAllText(templatePath).Replace("BIM manager signoff", "manager approval", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-bim-manager-signoff" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("BIM manager signoff", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingV60PilotEvidencePilotIdentifier_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var templatePath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
        File.WriteAllText(
            templatePath,
            File.ReadAllText(templatePath).Replace("Pilot identifier", "Pilot marker", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-pilot-identifier" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_MissingV60PilotEvidenceStatusCommand_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var templatePath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
        File.WriteAllText(
            templatePath,
            File.ReadAllText(templatePath).Replace("status --output json", "status proof", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-status" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_MissingV60PilotEvidenceScaffoldCommand_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var templatePath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
        File.WriteAllText(
            templatePath,
            File.ReadAllText(templatePath).Replace("release pilot scaffold", "release pilot packet", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-scaffold-command" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_MissingV60PilotEvidenceValidateCommand_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var templatePath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
        File.WriteAllText(
            templatePath,
            File.ReadAllText(templatePath).Replace("release pilot validate", "release pilot inspect", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-validate-command" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_MissingV60PilotEvidenceRegisterCommand_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var templatePath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
        File.WriteAllText(
            templatePath,
            File.ReadAllText(templatePath).Replace("release pilot register", "release pilot record", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-register-command" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_MissingV60PilotEvidenceRegisterNextActions_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var templatePath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
        File.WriteAllText(
            templatePath,
            File.ReadAllText(templatePath).Replace("register nextActions", "register next steps", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-register-next-actions" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_MissingV60PilotEvidenceRolloutStatusCommand_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var templatePath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
        File.WriteAllText(
            templatePath,
            File.ReadAllText(templatePath).Replace("release pilot status", "release pilot progress", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-rollout-status-command" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_MissingV60PilotEvidenceMissingEvidenceStatus_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var templatePath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
        File.WriteAllText(
            templatePath,
            File.ReadAllText(templatePath).Replace("missingEvidence", "missing evidence list", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-rollout-status-missing-evidence" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_MissingV60PilotEvidenceMissingEvidenceSummaryStatus_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var templatePath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
        File.WriteAllText(
            templatePath,
            File.ReadAllText(templatePath).Replace("missingEvidenceSummary", "missing evidence summary", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-rollout-status-missing-evidence-summary" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_MissingV60PilotEvidenceCompleteCounts_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var templatePath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
        File.WriteAllText(
            templatePath,
            File.ReadAllText(templatePath)
                .Replace("evidenceCompleteOfficePilotCount", "evidence complete pilot count", StringComparison.Ordinal)
                .Replace("remainingEvidenceCompleteOfficePilotCount", "remaining evidence complete pilot count", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        var checks = json.RootElement.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-rollout-status-evidence-complete-count" &&
            check.GetProperty("status").GetString() == "error");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-rollout-status-evidence-complete-remaining" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_MissingV60PilotEvidenceClaimCommand_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var templatePath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
        File.WriteAllText(
            templatePath,
            File.ReadAllText(templatePath).Replace("release pilot claim", "release pilot approve", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-claim-command" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_MissingV60PilotEvidenceClaimBlockers_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var templatePath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
        File.WriteAllText(
            templatePath,
            File.ReadAllText(templatePath).Replace("claimBlockers", "claim blockers", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-claim-blockers" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_MissingV60PilotEvidenceNextActions_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var templatePath = Path.Combine(_root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
        File.WriteAllText(
            templatePath,
            File.ReadAllText(templatePath).Replace("nextActions", "next actions", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:pilot-evidence-next-actions" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_V60OfficeRolloutStatusOverclaim_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var statusPath = Path.Combine(_root, "docs", "smoke", "v6.0", "office-rollout-status.json");
        File.WriteAllText(
            statusPath,
            File.ReadAllText(statusPath).Replace("\"officeRolloutCompletion\": false", "\"officeRolloutCompletion\": true", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:office-rollout-status-no-overclaim-json" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_V60OfficeRolloutStatusThresholdReached_ReturnsSuccess()
    {
        WriteHealthyTree(_root);
        var statusPath = Path.Combine(_root, "docs", "smoke", "v6.0", "office-rollout-status.json");
        File.WriteAllText(
            statusPath,
            File.ReadAllText(statusPath)
                .Replace("\"completedOfficePilotCount\": 0", "\"completedOfficePilotCount\": 2", StringComparison.Ordinal)
                .Replace("\"completedPilotIds\": []", "\"completedPilotIds\": [\"pilot-01\", \"pilot-02\"]", StringComparison.Ordinal)
                .Replace("\"completedPilots\": []", "\"completedPilots\": [" + CompletedPilotEvidenceJson("pilot-01") + ", " + CompletedPilotEvidenceJson("pilot-02") + "]", StringComparison.Ordinal)
                .Replace("\"officeRolloutCompletion\": false", "\"officeRolloutCompletion\": true", StringComparison.Ordinal)
                .Replace("\"productionSupportClaim\": false", "\"productionSupportClaim\": true", StringComparison.Ordinal));
        WriteCompletedPilotEvidencePackets(_root, "pilot-01", "pilot-02");
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:office-rollout-status-no-overclaim-json" &&
            check.GetProperty("status").GetString() == "ok");
    }

    [Fact]
    public async Task Verify_V60OfficeRolloutStatusMissingEvidencePacketFile_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var statusPath = Path.Combine(_root, "docs", "smoke", "v6.0", "office-rollout-status.json");
        File.WriteAllText(
            statusPath,
            File.ReadAllText(statusPath)
                .Replace("\"completedOfficePilotCount\": 0", "\"completedOfficePilotCount\": 2", StringComparison.Ordinal)
                .Replace("\"completedPilotIds\": []", "\"completedPilotIds\": [\"pilot-01\", \"pilot-02\"]", StringComparison.Ordinal)
                .Replace("\"completedPilots\": []", "\"completedPilots\": [" + CompletedPilotEvidenceJson("pilot-01") + ", " + CompletedPilotEvidenceJson("pilot-02") + "]", StringComparison.Ordinal)
                .Replace("\"officeRolloutCompletion\": false", "\"officeRolloutCompletion\": true", StringComparison.Ordinal)
                .Replace("\"productionSupportClaim\": false", "\"productionSupportClaim\": true", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:office-rollout-status-completed-pilots-json" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_V60OfficeRolloutStatusEvidencePacketPilotIdMismatch_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var statusPath = Path.Combine(_root, "docs", "smoke", "v6.0", "office-rollout-status.json");
        File.WriteAllText(
            statusPath,
            File.ReadAllText(statusPath)
                .Replace("\"completedOfficePilotCount\": 0", "\"completedOfficePilotCount\": 2", StringComparison.Ordinal)
                .Replace("\"completedPilotIds\": []", "\"completedPilotIds\": [\"pilot-01\", \"pilot-02\"]", StringComparison.Ordinal)
                .Replace("\"completedPilots\": []", "\"completedPilots\": [" + CompletedPilotEvidenceJson("pilot-01") + ", " + CompletedPilotEvidenceJson("pilot-02") + "]", StringComparison.Ordinal)
                .Replace("\"officeRolloutCompletion\": false", "\"officeRolloutCompletion\": true", StringComparison.Ordinal)
                .Replace("\"productionSupportClaim\": false", "\"productionSupportClaim\": true", StringComparison.Ordinal));
        WriteCompletedPilotEvidencePackets(_root, "pilot-01");
        WriteFile("docs/smoke/v6.0/pilot-02.md", CompletedPilotEvidencePacketContent("pilot-03"));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:office-rollout-status-completed-pilots-json" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_V60OfficeRolloutStatusMissingPerPilotEvidence_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var statusPath = Path.Combine(_root, "docs", "smoke", "v6.0", "office-rollout-status.json");
        File.WriteAllText(
            statusPath,
            File.ReadAllText(statusPath)
                .Replace("\"completedOfficePilotCount\": 0", "\"completedOfficePilotCount\": 2", StringComparison.Ordinal)
                .Replace("\"completedPilotIds\": []", "\"completedPilotIds\": [\"pilot-01\", \"pilot-02\"]", StringComparison.Ordinal)
                .Replace("\"officeRolloutCompletion\": false", "\"officeRolloutCompletion\": true", StringComparison.Ordinal)
                .Replace("\"productionSupportClaim\": false", "\"productionSupportClaim\": true", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:office-rollout-status-completed-pilots-json" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_V60OfficeRolloutStatusPilotIdsMismatchEvidence_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var statusPath = Path.Combine(_root, "docs", "smoke", "v6.0", "office-rollout-status.json");
        File.WriteAllText(
            statusPath,
            File.ReadAllText(statusPath)
                .Replace("\"completedOfficePilotCount\": 0", "\"completedOfficePilotCount\": 2", StringComparison.Ordinal)
                .Replace("\"completedPilotIds\": []", "\"completedPilotIds\": [\"pilot-01\", \"pilot-02\"]", StringComparison.Ordinal)
                .Replace("\"completedPilots\": []", "\"completedPilots\": [" + CompletedPilotEvidenceJson("pilot-01") + ", " + CompletedPilotEvidenceJson("pilot-03") + "]", StringComparison.Ordinal)
                .Replace("\"officeRolloutCompletion\": false", "\"officeRolloutCompletion\": true", StringComparison.Ordinal)
                .Replace("\"productionSupportClaim\": false", "\"productionSupportClaim\": true", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:office-rollout-status-completed-pilots-json" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_V60OfficeRolloutStatusLocalEvidencePacketPath_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var statusPath = Path.Combine(_root, "docs", "smoke", "v6.0", "office-rollout-status.json");
        File.WriteAllText(
            statusPath,
            File.ReadAllText(statusPath)
                .Replace("\"completedOfficePilotCount\": 0", "\"completedOfficePilotCount\": 2", StringComparison.Ordinal)
                .Replace("\"completedPilotIds\": []", "\"completedPilotIds\": [\"pilot-01\", \"pilot-02\"]", StringComparison.Ordinal)
                .Replace("\"completedPilots\": []", "\"completedPilots\": [" + CompletedPilotEvidenceJson("pilot-01") + ", " + CompletedPilotEvidenceJson("pilot-02", @"C:\Users\Lenovo\pilot-02.md") + "]", StringComparison.Ordinal)
                .Replace("\"officeRolloutCompletion\": false", "\"officeRolloutCompletion\": true", StringComparison.Ordinal)
                .Replace("\"productionSupportClaim\": false", "\"productionSupportClaim\": true", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:office-rollout-status-completed-pilots-json" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_MissingV60LocalControlledPilotEvidenceJson_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        File.Delete(Path.Combine(_root, "docs", "smoke", "v6.0", "local-controlled-pilot-20260525.evidence.json"));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:local-controlled-pilot-evidence-json" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("local-controlled-pilot-20260525.evidence.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_InvalidV60LocalControlledPilotLedgerValidation_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var evidencePath = Path.Combine(_root, "docs", "smoke", "v6.0", "local-controlled-pilot-20260525.evidence.json");
        File.WriteAllText(
            evidencePath,
            File.ReadAllText(evidencePath).Replace("\"valid\": true", "\"valid\": false", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:local-controlled-pilot-ledger-validate-json" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("ledgerValidate evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("\"sourceBundle\": \"docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525\"", "\"sourceBundle\": \".artifacts/live-smoke/other\"", "v6.0:local-controlled-pilot-evidence-json")]
    [InlineData("\"officeRolloutCompletion\": false", "\"officeRolloutCompletion\": true", "v6.0:local-controlled-pilot-evidence-boundary-json")]
    [InlineData("\"rootHash\": \"b915f6cf6ffea40425cb16bf51bba858339e8e00059f07455b919475968d24fe\"", "\"rootHash\": \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"", "v6.0:local-controlled-pilot-journal-json")]
    [InlineData("    \"docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525/outputs/ledger-validate.json\",\n", "", "v6.0:local-controlled-pilot-required-files-json")]
    public async Task Verify_InvalidV60LocalControlledPilotEvidenceSummary_ReturnsFailure(
        string oldText,
        string newText,
        string expectedCheckId)
    {
        WriteHealthyTree(_root);
        var evidencePath = Path.Combine(_root, "docs", "smoke", "v6.0", "local-controlled-pilot-20260525.evidence.json");
        File.WriteAllText(
            evidencePath,
            File.ReadAllText(evidencePath).Replace(oldText, newText, StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == expectedCheckId &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Verify_MissingV60LocalControlledPilotSourceBundle_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        Directory.Delete(
            Path.Combine(_root, "docs", "smoke", "v6.0", "revit2026-v6-local-controlled-pilot-20260525"),
            recursive: true);
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:local-controlled-pilot-source-bundle" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("checked-in public-safe evidence source files", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingV60AuditSpineParityDetails_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var gapPath = Path.Combine(_root, "docs", "smoke", "v6.0", "gap-report.md");
        File.WriteAllText(
            gapPath,
            File.ReadAllText(gapPath)
                .Replace("journal verify JSON/table validity/root-hash parity", "journal verify JSON/table parity", StringComparison.Ordinal)
                .Replace("history-list.v1 JSON count consistency and table row-order parity", "history list JSON/table outputs", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        var checks = json.RootElement.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "v6.0:audit-spine-journal-parity-gap-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("root-hash", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "v6.0:audit-spine-history-row-order-gap-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("row-order parity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingWorkflowRegistryScheduleManifestDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "workflow-registry.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("schedule-export-manifest.v1", "schedule manifest", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:workflow-registry-schedule-manifest-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("schedule-export-manifest.v1", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("JSON/table/Markdown output semantic parity", "output-format coverage", "v6.0:workflow-registry-output-parity-smoke-doc")]
    [InlineData("final file-tree snapshot evidence", "final snapshot evidence", "v6.0:workflow-registry-final-snapshot-smoke-doc")]
    [InlineData("event-level no-write evidence", "no-write evidence", "v6.0:workflow-registry-event-no-write-smoke-doc")]
    public async Task Verify_MissingWorkflowRegistryRuntimeEvidenceDoc_ReturnsFailure(
        string requiredPhrase,
        string replacement,
        string expectedCheckId)
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "workflow-registry.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace(requiredPhrase, replacement, StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == expectedCheckId &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains(requiredPhrase, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("docs/v6-local-bimops-contract.md", "No SaaS", "No SaaS, but may use SaaS", "v6.0:no-saas-doc")]
    [InlineData("docs/smoke/v6.0/standards-runtime.md", "does not start Revit", "does not start Revit, but may start Revit during validation", "v6.0:standards-no-revit-smoke-doc")]
    [InlineData("docs/smoke/v6.0/issue-spine.md", "dry-run no-write evidence", "dry-run no-write evidence, but may write package cache files", "v6.0:issue-no-write-smoke-doc")]
    [InlineData("docs/smoke/v6.0/issue-spine.md", "no MCP", "no MCP but may use MCP", "v6.0:issue-no-mcp-smoke-doc")]
    [InlineData("docs/smoke/v6.0/deliverables-verify.md", "no SaaS", "no SaaS but uses SaaS", "v6.0:deliverables-no-saas-smoke-doc")]
    [InlineData("docs/smoke/v6.0/deliverables-verify.md", "no Revit API", "no Revit API but uses Revit API for metadata", "v6.0:deliverables-no-revit-smoke-doc")]
    [InlineData("docs/smoke/v6.0/deliverables-verify.md", "starting Revit", "starting Revit but may start Revit", "v6.0:deliverables-no-revit-runtime-smoke-doc")]
    [InlineData("docs/smoke/v6.0/ledger-query.md", "event-level no-write evidence", "event-level no-write evidence but may write local cache files", "v6.0:ledger-query-no-write-smoke-doc")]
    [InlineData("docs/smoke/v6.0/ledger-query.md", "start Revit", "start Revit but may start Revit", "v6.0:ledger-query-no-revit-smoke-doc")]
    [InlineData("docs/smoke/v6.0/ledger-timeline.md", "no database", "no database but uses database-backed storage", "v6.0:ledger-timeline-no-db-smoke-doc")]
    [InlineData("docs/smoke/v6.0/workflow-registry.md", "does not write files", "does not write files but may write registry cache files", "v6.0:workflow-registry-no-write-smoke-doc")]
    [InlineData("docs/smoke/v6.0/workflow-registry.md", "start Revit", "start Revit but may start Revit", "v6.0:workflow-registry-no-revit-smoke-doc")]
    public async Task Verify_V60BoundaryContradiction_ReturnsFailure(
        string relativePath,
        string requiredPhrase,
        string replacement,
        string expectedCheckId)
    {
        WriteHealthyTree(_root);
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var text = File.ReadAllText(path);
        Assert.Contains(requiredPhrase, text, StringComparison.Ordinal);
        File.WriteAllText(path, text.Replace(requiredPhrase, replacement, StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == expectedCheckId &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("contradictory wording", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("docs/v6-local-bimops-contract.md", "No SaaS, no MCP, no built-in LLM, no dashboard-central workflow state, and no", "SaaS, MCP, built-in LLM, dashboard-central workflow state, and", "v6.0:no-saas-doc")]
    [InlineData("docs/smoke/v6.0/standards-runtime.md", "no Revit API, no add-in, no SaaS, no MCP, no database, no dashboard-central", "Revit API, add-in, SaaS, MCP, database, dashboard-central", "v6.0:standards-no-db-smoke-doc")]
    public async Task Verify_V60BareBoundaryTerm_ReturnsFailure(
        string relativePath,
        string boundaryPhrase,
        string replacement,
        string expectedCheckId)
    {
        WriteHealthyTree(_root);
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var text = File.ReadAllText(path);
        Assert.Contains(boundaryPhrase, text, StringComparison.Ordinal);
        File.WriteAllText(path, text.Replace(boundaryPhrase, replacement, StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == expectedCheckId &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("not only a bare mention", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_V60BoundaryContradictionInUnrelatedExample_DoesNotFail()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "workflow-registry.md");
        File.WriteAllText(
            smokePath,
            "Rejected example wording for reviewers: no SaaS but uses SaaS." +
            Environment.NewLine +
            File.ReadAllText(smokePath));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:workflow-registry-no-write-smoke-doc" &&
            check.GetProperty("status").GetString() == "ok");
    }

    [Fact]
    public async Task Verify_MissingLedgerValidateSourceReadabilityDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-validate.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("source readability, ", "", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-validate-sources-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("source readability", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingLedgerStatsOperationCountsDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-stats.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("operation counts", "operation summaries", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-stats-counts-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("operation counts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingLedgerStatsSourceCountsDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-stats.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("source counts", "source summaries", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-stats-source-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("source counts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingLedgerStatsIssueSeverityDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-stats.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("issue severity", "issue type", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-stats-issues-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("issue severity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingLedgerValidateExplicitOffsetDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-validate.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("explicit UTC offset", "timezone coercion", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-validate-timestamp-offset-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("explicit UTC offset", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingStandardsRuntimeCommandDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "standards-runtime.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("standards validate --output json", "standards check", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:standards-spine-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("standards validate --output json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingStandardsRuntimeSaasBoundaryDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "standards-runtime.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("SaaS", "cloud service", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:standards-no-saas-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("SaaS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingStandardsRuntimeFinalSnapshotDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "standards-runtime.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("final file-tree snapshot evidence", "final snapshot proof", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:standards-final-snapshot-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("final file-tree snapshot evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingIssueSpineDryRunDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "issue-spine.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("dry-run first", "package review", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:issue-dry-run-first-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("dry-run first", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingIssueSpineMcpBoundaryDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "issue-spine.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("MCP", "tool protocol", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:issue-no-mcp-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("MCP", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingDeliverablesVerifyReceiptDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "deliverables-verify.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("readable-receipt evidence", "receipt scan", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:deliverables-receipt-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("readable-receipt evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingDeliverablesVerifyOutputParityDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "deliverables-verify.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath)
                .Replace("Kinds and Outcomes counts", "summary counts", StringComparison.Ordinal)
                .Replace("table and Markdown", "human output", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:deliverables-output-parity-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("semantic evidence terms", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingDeliverablesVerifyLlmBoundaryDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "deliverables-verify.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("built-in LLM", "assistant runtime", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:deliverables-no-llm-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("built-in LLM", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingLedgerValidateTimeFilterTimestampDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-validate.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("Time filters preserve invalid timestamp warnings.", "Time filters apply normally.", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-validate-time-filter-timestamp-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("time filters preserve invalid timestamp warnings", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingLedgerValidateReceiptHashDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-validate.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("declared receipt hash values, ", "", StringComparison.OrdinalIgnoreCase));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-validate-receipt-hash-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("receipt hash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingLedgerStatsMalformedDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-stats.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("Malformed journal, delivery manifest, and workflow receipt artifacts", "Malformed local artifacts", StringComparison.OrdinalIgnoreCase));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-stats-malformed-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingLedgerTimelineUnbucketedTimestampDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-timeline.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("unbucketed timestamp", "timestamp warning", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-timeline-unbucketed-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("unbucketed timestamp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingLedgerTimelineTimeFilterTimestampDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-timeline.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("Time filters preserve unbucketed timestamp warnings.", "Time filters apply normally.", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-timeline-time-filter-timestamp-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("time filters preserve unbucketed timestamp warnings", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingLedgerTimelineCategoryDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-timeline.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("category counts per bucket", "category filter support", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-timeline-category-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("category counts per bucket", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingLedgerQueryOrderingDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-query.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("timestamp/source/path/line ordering", "deterministic sorting", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-query-ordering-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("timestamp/source/path/line ordering", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingLedgerQueryOutputParityDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-query.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("JSON/table/Markdown output semantic parity", "output-format coverage", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-query-output-parity-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("JSON/table/Markdown output semantic parity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingLedgerQueryEventNoWriteDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-query.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("event-level no-write evidence", "file-hash no-write evidence", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-query-no-write-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("event-level no-write evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingLedgerQueryFinalSnapshotDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-query.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("final file-tree snapshot evidence", "final snapshot evidence", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-query-final-snapshot-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("final file-tree snapshot evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingLedgerValidateEventNoWriteDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-validate.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("event-level no-write evidence", "file-hash no-write evidence", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-validate-no-write-evidence-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("event-level no-write evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_MissingLedgerValidateFinalSnapshotDoc_ReturnsFailure()
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", "ledger-validate.md");
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace("final file-tree snapshot evidence", "final snapshot evidence", StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:ledger-validate-final-snapshot-smoke-doc" &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains("final file-tree snapshot evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("ledger-validate.md", "validation JSON/table/Markdown semantic parity", "validation output coverage", "v6.0:ledger-validate-output-parity-smoke-doc")]
    [InlineData("ledger-stats.md", "JSON/table/Markdown stats semantic parity", "stats output coverage", "v6.0:ledger-stats-output-parity-smoke-doc")]
    [InlineData("ledger-stats.md", "event-level no-write evidence", "file-hash no-write evidence", "v6.0:ledger-stats-no-write-smoke-doc")]
    [InlineData("ledger-stats.md", "final file-tree snapshot evidence", "final snapshot evidence", "v6.0:ledger-stats-final-snapshot-smoke-doc")]
    [InlineData("ledger-timeline.md", "JSON/table/Markdown timeline semantic parity", "timeline output coverage", "v6.0:ledger-timeline-output-parity-smoke-doc")]
    [InlineData("ledger-timeline.md", "event-level no-write evidence", "file-hash no-write evidence", "v6.0:ledger-timeline-no-write-evidence-smoke-doc")]
    [InlineData("ledger-timeline.md", "final file-tree snapshot evidence", "final snapshot evidence", "v6.0:ledger-timeline-final-snapshot-smoke-doc")]
    [InlineData("ledger-timeline.md", "projectDirectories", "project directory list", "v6.0:ledger-timeline-cross-project-smoke-doc")]
    [InlineData("ledger-timeline.md", "byProject", "per-project counts", "v6.0:ledger-timeline-by-project-smoke-doc")]
    [InlineData("ledger-analytics.md", "ledger-analytics-bundle.v1", "ledger analytics bundle schema", "v6.0:ledger-analytics-schema-smoke-doc")]
    [InlineData("ledger-analytics.md", "JSON/table/Markdown output formats", "bundle output formats", "v6.0:ledger-analytics-output-parity-smoke-doc")]
    [InlineData("ledger-analytics.md", "localOnly=true", "localOnly=false", "v6.0:ledger-analytics-local-only-smoke-doc")]
    [InlineData("ledger-analytics.md", "databaseRuntime=false", "databaseRuntime=true", "v6.0:ledger-analytics-no-db-flag-smoke-doc")]
    [InlineData("ledger-analytics.md", "networkService=false", "networkService=true", "v6.0:ledger-analytics-no-network-flag-smoke-doc")]
    public async Task Verify_MissingV60LedgerSemanticEvidenceDoc_ReturnsFailure(
        string smokeFileName,
        string requiredPhrase,
        string replacement,
        string checkId)
    {
        WriteHealthyTree(_root);
        var smokePath = Path.Combine(_root, "docs", "smoke", "v6.0", smokeFileName);
        File.WriteAllText(
            smokePath,
            File.ReadAllText(smokePath).Replace(requiredPhrase, replacement, StringComparison.Ordinal));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == checkId &&
            check.GetProperty("status").GetString() == "error" &&
            check.GetProperty("message").GetString()!.Contains(requiredPhrase, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_V60WorkbenchGateIgnoresUnrelatedWorkbenchFailures()
    {
        WriteHealthyTree(_root);
        File.Delete(Path.Combine(_root, "docs", "smoke", "v5.6", "gap-report.md"));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v6.0:workbench-gate" &&
            check.GetProperty("status").GetString() == "ok" &&
            check.GetProperty("message").GetString()!.Contains("scoped workbench v2 v6.0 gate passes", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("overall workbench exit", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("scoped v6 gate status=pass", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("path").GetString()!.Contains($"--dir \"{_root}\"", StringComparison.Ordinal) &&
            check.GetProperty("message").GetString()!.Contains("runtimeEvidence=pass", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("history-list.v1 execution", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("rollback dry-run request enforcement", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_V55WorkbenchGateIgnoresLaterScopedWorkbenchFailures()
    {
        WriteHealthyTree(_root);
        File.Delete(Path.Combine(_root, "docs", "smoke", "v5.6", "gap-report.md"));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5.5:workbench-gate" &&
            check.GetProperty("status").GetString() == "ok" &&
            check.GetProperty("message").GetString()!.Contains("scoped workbench v2 v5.5 gate passes", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("overall workbench exit", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("ignored by design", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("scoped v5.5 gate status=pass", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("path").GetString()!.Contains($"--dir \"{_root}\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verify_V56WorkbenchGateIgnoresEarlierScopedWorkbenchFailures()
    {
        WriteHealthyTree(_root);
        File.Delete(Path.Combine(_root, "docs", "smoke", "v5.5", "gap-report.md"));
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "json", null, strict: false, output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "v5.6:workbench-gate" &&
            check.GetProperty("status").GetString() == "ok" &&
            check.GetProperty("message").GetString()!.Contains("scoped workbench v2 v5.6 gate passes", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("overall workbench exit", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("ignored by design", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("message").GetString()!.Contains("scoped v5.6 gate status=pass", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("path").GetString()!.Contains($"--dir \"{_root}\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/tmp/RevitCli release root O'Hare", "workbench verify --contract workbench-contract.v2 --dir \"/tmp/RevitCli release root O'Hare\" --output json")]
    [InlineData("C:\\Temp\\RevitCli Release Root", "workbench verify --contract workbench-contract.v2 --dir \"C:\\Temp\\RevitCli Release Root\" --output json")]
    [InlineData("/tmp/RevitCli \"quoted\" root", "workbench verify --contract workbench-contract.v2 --dir \"/tmp/RevitCli \\\"quoted\\\" root\" --output json")]
    public void WorkbenchVerifySource_QuotesReleaseRootForDisplay(string root, string expected)
    {
        Assert.Equal(expected, ReleaseVerifier.WorkbenchVerifySource(root));
    }

    [Fact]
    public void V60WorkbenchGateRuntimeEvidence_MissingOrFalse_DoesNotPass()
    {
        using var missing = JsonDocument.Parse("""
{
  "id": "v60LocalBimOpsContractGate",
  "status": "pass",
  "evidence": "status-only pass"
}
""");
        using var allRuntimeFalse = JsonDocument.Parse("""
{
  "id": "v60LocalBimOpsContractGate",
  "status": "pass",
  "evidence": "runtime false",
  "runtimeEvidence": {
    "commandSpine": true,
    "commandSpineOutputParity": true,
    "commandSpineNoWrites": true,
    "standardsValidate": true,
    "issuePreflight": true,
    "issuePackageDryRun": true,
    "deliverablesVerify": true,
    "journalVerify": true,
    "historyList": true,
    "historyListCountConsistency": true,
    "historyListRowOrder": true,
    "historyListEvidence": {
      "jsonEntryCount": 1,
      "jsonHiddenCount": 0,
      "jsonReturnedCount": 1,
      "tableRowCount": 1,
      "countConsistency": true,
      "idOrderMatch": true,
      "headerMatched": true
    },
    "rollbackDryRun": true,
    "rollbackDryRunPreview": true,
    "rollbackNoMutatingSetRequest": true,
    "rollbackDryRunEvidence": {
      "actionCount": 1,
      "conflictCount": 0,
      "errorCount": 0,
      "safeApplyCommand": "revitcli rollback receipt.json --approve",
      "safeApplyEmitted": true,
      "dryRunPreviewOnly": true,
      "sawDryRunSetPreview": true,
      "sawMutatingSetRequest": false
    },
    "workflowRegistry": true,
    "ledgerAppend": true,
    "ledgerQueryValidate": true,
    "ledgerReplay": true,
    "ledgerStats": true,
    "ledgerTimeline": true,
    "allRuntimeChecksPass": false
  }
}
""");
        using var statusFail = JsonDocument.Parse("""
{
  "id": "v60LocalBimOpsContractGate",
  "status": "fail",
  "evidence": "runtime true but status failed",
  "runtimeEvidence": {
    "commandSpine": true,
    "commandSpineOutputParity": true,
    "commandSpineNoWrites": true,
    "standardsValidate": true,
    "issuePreflight": true,
    "issuePackageDryRun": true,
    "deliverablesVerify": true,
    "journalVerify": true,
    "historyList": true,
    "historyListCountConsistency": true,
    "historyListRowOrder": true,
    "historyListEvidence": {
      "jsonEntryCount": 1,
      "jsonHiddenCount": 0,
      "jsonReturnedCount": 1,
      "tableRowCount": 1,
      "countConsistency": true,
      "idOrderMatch": true,
      "headerMatched": true
    },
    "rollbackDryRun": true,
    "rollbackDryRunPreview": true,
    "rollbackNoMutatingSetRequest": true,
    "rollbackDryRunEvidence": {
      "actionCount": 1,
      "conflictCount": 0,
      "errorCount": 0,
      "safeApplyCommand": "revitcli rollback receipt.json --approve",
      "safeApplyEmitted": true,
      "dryRunPreviewOnly": true,
      "sawDryRunSetPreview": true,
      "sawMutatingSetRequest": false
    },
    "workflowRegistry": true,
    "ledgerAppend": true,
    "ledgerQueryValidate": true,
    "ledgerReplay": true,
    "ledgerStats": true,
    "ledgerTimeline": true,
    "allRuntimeChecksPass": true
  }
}
""");

        Assert.False(ReleaseVerifier.EvaluateV60WorkbenchGateCheck(
            missing.RootElement,
            out var missingStatus,
            out var missingEvidence,
            out var missingRuntimeEvidence));
        Assert.Equal("pass", missingStatus);
        Assert.Equal("status-only pass", missingEvidence);
        Assert.Equal("missing", missingRuntimeEvidence);

        Assert.False(ReleaseVerifier.EvaluateV60WorkbenchGateCheck(
            allRuntimeFalse.RootElement,
            out _,
            out _,
            out var falseRuntimeEvidence));
        Assert.Contains("allRuntimeChecksPass=false", falseRuntimeEvidence, StringComparison.OrdinalIgnoreCase);

        Assert.False(ReleaseVerifier.EvaluateV60WorkbenchGateCheck(
            statusFail.RootElement,
            out var failedStatus,
            out _,
            out var trueRuntimeEvidence));
        Assert.Equal("fail", failedStatus);
        Assert.Contains("allRuntimeChecksPass=true", trueRuntimeEvidence, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("commandSpine")]
    [InlineData("commandSpineOutputParity")]
    [InlineData("commandSpineNoWrites")]
    [InlineData("standardsValidate")]
    [InlineData("issuePreflight")]
    [InlineData("issuePackageDryRun")]
    [InlineData("deliverablesVerify")]
    [InlineData("journalVerify")]
    [InlineData("historyList")]
    [InlineData("historyListCountConsistency")]
    [InlineData("historyListRowOrder")]
    [InlineData("rollbackDryRun")]
    [InlineData("rollbackDryRunPreview")]
    [InlineData("rollbackNoMutatingSetRequest")]
    [InlineData("workflowRegistry")]
    [InlineData("ledgerAppend")]
    [InlineData("ledgerQueryValidate")]
    [InlineData("ledgerReplay")]
    [InlineData("ledgerStats")]
    [InlineData("ledgerTimeline")]
    public void V60WorkbenchGateRuntimeEvidence_RequiredSubfieldFalse_DoesNotPass(string fieldName)
    {
        var json = """
{
  "id": "v60LocalBimOpsContractGate",
  "status": "pass",
  "evidence": "runtime subfield false",
  "runtimeEvidence": {
    "commandSpine": true,
    "commandSpineOutputParity": true,
    "commandSpineNoWrites": true,
    "standardsValidate": true,
    "issuePreflight": true,
    "issuePackageDryRun": true,
    "deliverablesVerify": true,
    "journalVerify": true,
    "historyList": true,
    "historyListCountConsistency": true,
    "historyListRowOrder": true,
    "historyListEvidence": {
      "jsonEntryCount": 1,
      "jsonHiddenCount": 0,
      "jsonReturnedCount": 1,
      "tableRowCount": 1,
      "countConsistency": true,
      "idOrderMatch": true,
      "headerMatched": true
    },
    "rollbackDryRun": true,
    "rollbackDryRunPreview": true,
    "rollbackNoMutatingSetRequest": true,
    "rollbackDryRunEvidence": {
      "actionCount": 1,
      "conflictCount": 0,
      "errorCount": 0,
      "safeApplyCommand": "revitcli rollback receipt.json --approve",
      "safeApplyEmitted": true,
      "dryRunPreviewOnly": true,
      "sawDryRunSetPreview": true,
      "sawMutatingSetRequest": false
    },
    "workflowRegistry": true,
    "ledgerAppend": true,
    "ledgerQueryValidate": true,
    "ledgerReplay": true,
    "ledgerStats": true,
    "ledgerTimeline": true,
    "allRuntimeChecksPass": true
  }
}
""".Replace($"\"{fieldName}\": true", $"\"{fieldName}\": false", StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);

        Assert.False(ReleaseVerifier.EvaluateV60WorkbenchGateCheck(
            document.RootElement,
            out var status,
            out _,
            out var runtimeEvidence));
        Assert.Equal("pass", status);
        Assert.Contains($"{fieldName}=false", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("commandSpine")]
    [InlineData("commandSpineOutputParity")]
    [InlineData("commandSpineNoWrites")]
    [InlineData("standardsValidate")]
    [InlineData("issuePreflight")]
    [InlineData("issuePackageDryRun")]
    [InlineData("deliverablesVerify")]
    [InlineData("journalVerify")]
    [InlineData("historyList")]
    [InlineData("historyListCountConsistency")]
    [InlineData("historyListRowOrder")]
    [InlineData("rollbackDryRun")]
    [InlineData("rollbackDryRunPreview")]
    [InlineData("rollbackNoMutatingSetRequest")]
    [InlineData("workflowRegistry")]
    [InlineData("ledgerAppend")]
    [InlineData("ledgerQueryValidate")]
    [InlineData("ledgerReplay")]
    [InlineData("ledgerStats")]
    [InlineData("ledgerTimeline")]
    public void V60WorkbenchGateRuntimeEvidence_RequiredSubfieldMissing_DoesNotPass(string fieldName)
    {
        var json = """
{
  "id": "v60LocalBimOpsContractGate",
  "status": "pass",
  "evidence": "runtime subfield missing",
  "runtimeEvidence": {
    "commandSpine": true,
    "commandSpineOutputParity": true,
    "commandSpineNoWrites": true,
    "standardsValidate": true,
    "issuePreflight": true,
    "issuePackageDryRun": true,
    "deliverablesVerify": true,
    "journalVerify": true,
    "historyList": true,
    "historyListCountConsistency": true,
    "historyListRowOrder": true,
    "historyListEvidence": {
      "jsonEntryCount": 1,
      "jsonHiddenCount": 0,
      "jsonReturnedCount": 1,
      "tableRowCount": 1,
      "countConsistency": true,
      "idOrderMatch": true,
      "headerMatched": true
    },
    "rollbackDryRun": true,
    "rollbackDryRunPreview": true,
    "rollbackNoMutatingSetRequest": true,
    "rollbackDryRunEvidence": {
      "actionCount": 1,
      "conflictCount": 0,
      "errorCount": 0,
      "safeApplyCommand": "revitcli rollback receipt.json --approve",
      "safeApplyEmitted": true,
      "dryRunPreviewOnly": true,
      "sawDryRunSetPreview": true,
      "sawMutatingSetRequest": false
    },
    "workflowRegistry": true,
    "ledgerAppend": true,
    "ledgerQueryValidate": true,
    "ledgerReplay": true,
    "ledgerStats": true,
    "ledgerTimeline": true,
    "allRuntimeChecksPass": true
  }
}
""";
        var fieldLine = $"\"{fieldName}\": true,";
        var withoutField = string.Join(
            '\n',
            json.Split('\n')
                .Where(line => !string.Equals(line.Trim().TrimEnd('\r'), fieldLine, StringComparison.Ordinal)));
        using var document = JsonDocument.Parse(withoutField);

        Assert.False(ReleaseVerifier.EvaluateV60WorkbenchGateCheck(
            document.RootElement,
            out var status,
            out _,
            out var runtimeEvidence,
            out var runtimeEvidenceMap));
        Assert.Equal("pass", status);
        Assert.NotNull(runtimeEvidenceMap);
        Assert.DoesNotContain(fieldName, runtimeEvidenceMap.Keys);
        Assert.Contains($"{fieldName}=missing", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void V60WorkbenchGateRuntimeEvidence_AllTrue_Passes()
    {
        using var document = JsonDocument.Parse("""
{
  "id": "v60LocalBimOpsContractGate",
  "status": "pass",
  "evidence": "runtime true",
  "runtimeEvidence": {
    "commandSpine": true,
    "commandSpineOutputParity": true,
    "commandSpineNoWrites": true,
    "standardsValidate": true,
    "issuePreflight": true,
    "issuePackageDryRun": true,
    "deliverablesVerify": true,
    "journalVerify": true,
    "historyList": true,
    "historyListCountConsistency": true,
    "historyListRowOrder": true,
    "historyListEvidence": {
      "jsonEntryCount": 1,
      "jsonHiddenCount": 0,
      "jsonReturnedCount": 1,
      "tableRowCount": 1,
      "countConsistency": true,
      "idOrderMatch": true,
      "headerMatched": true
    },
    "rollbackDryRun": true,
    "rollbackDryRunPreview": true,
    "rollbackNoMutatingSetRequest": true,
    "rollbackDryRunEvidence": {
      "actionCount": 1,
      "conflictCount": 0,
      "errorCount": 0,
      "safeApplyCommand": "revitcli rollback receipt.json --approve",
      "safeApplyEmitted": true,
      "dryRunPreviewOnly": true,
      "sawDryRunSetPreview": true,
      "sawMutatingSetRequest": false
    },
    "workflowRegistry": true,
    "ledgerAppend": true,
    "ledgerQueryValidate": true,
    "ledgerReplay": true,
    "ledgerStats": true,
    "ledgerTimeline": true,
    "allRuntimeChecksPass": true
  }
}
""");

        Assert.True(ReleaseVerifier.EvaluateV60WorkbenchGateCheck(
            document.RootElement,
            out var status,
            out var evidence,
            out var runtimeEvidence,
            out var runtimeEvidenceMap));
        Assert.Equal("pass", status);
        Assert.Equal("runtime true", evidence);
        Assert.NotNull(runtimeEvidenceMap);
        Assert.True((bool)runtimeEvidenceMap["commandSpineOutputParity"]!);
        Assert.True((bool)runtimeEvidenceMap["commandSpineNoWrites"]!);
        Assert.Contains("commandSpine=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("commandSpineOutputParity=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("commandSpineNoWrites=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("standardsValidate=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("issuePreflight=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("issuePackageDryRun=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deliverablesVerify=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("journalVerify=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("historyList=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("historyListCountConsistency=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("historyListRowOrder=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("historyListEvidence=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollbackDryRun=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollbackDryRunPreview=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollbackNoMutatingSetRequest=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollbackDryRunEvidence=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workflowRegistry=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ledgerAppend=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ledgerQueryValidate=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ledgerReplay=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ledgerStats=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ledgerTimeline=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allRuntimeChecksPass=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void V60WorkbenchGateRuntimeEvidence_MissingStructuredHistoryEvidence_DoesNotPass()
    {
        var json = V60RuntimeEvidenceGateJson().Replace(HistoryListEvidenceJsonBlock(), "", StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);

        Assert.False(ReleaseVerifier.EvaluateV60WorkbenchGateCheck(
            document.RootElement,
            out _,
            out _,
            out var runtimeEvidence,
            out var runtimeEvidenceMap));
        Assert.NotNull(runtimeEvidenceMap);
        Assert.DoesNotContain("historyListEvidence", runtimeEvidenceMap.Keys);
        Assert.Contains("historyListEvidence=missing", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void V60WorkbenchGateRuntimeEvidence_InvalidStructuredHistoryEvidence_DoesNotPass()
    {
        var json = V60RuntimeEvidenceGateJson().Replace("\"tableRowCount\": 1", "\"tableRowCount\": 0", StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);

        Assert.False(ReleaseVerifier.EvaluateV60WorkbenchGateCheck(
            document.RootElement,
            out _,
            out _,
            out var runtimeEvidence,
            out var runtimeEvidenceMap));
        Assert.NotNull(runtimeEvidenceMap);
        Assert.IsType<SortedDictionary<string, object?>>(runtimeEvidenceMap["historyListEvidence"]);
        Assert.Contains("historyListEvidence=false", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void V60WorkbenchGateRuntimeEvidence_InvalidStructuredRollbackEvidence_DoesNotPass()
    {
        var json = V60RuntimeEvidenceGateJson().Replace("\"sawMutatingSetRequest\": false", "\"sawMutatingSetRequest\": true", StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);

        Assert.False(ReleaseVerifier.EvaluateV60WorkbenchGateCheck(
            document.RootElement,
            out _,
            out _,
            out var runtimeEvidence,
            out var runtimeEvidenceMap));
        Assert.NotNull(runtimeEvidenceMap);
        Assert.IsType<SortedDictionary<string, object?>>(runtimeEvidenceMap["rollbackDryRunEvidence"]);
        Assert.Contains("rollbackDryRunEvidence=false", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void V60WorkbenchGateRuntimeEvidence_InvalidRollbackSafeApplyCommand_DoesNotPass()
    {
        var json = V60RuntimeEvidenceGateJson().Replace(
            "\"safeApplyCommand\": \"revitcli rollback receipt.json --approve\"",
            "\"safeApplyCommand\": \"echo ok\"",
            StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);

        Assert.False(ReleaseVerifier.EvaluateV60WorkbenchGateCheck(
            document.RootElement,
            out _,
            out _,
            out var runtimeEvidence,
            out var runtimeEvidenceMap));
        Assert.NotNull(runtimeEvidenceMap);
        var rollbackEvidence = Assert.IsType<SortedDictionary<string, object?>>(runtimeEvidenceMap["rollbackDryRunEvidence"]);
        Assert.Equal("echo ok", rollbackEvidence["safeApplyCommand"]);
        Assert.Contains("rollbackDryRunEvidence=false", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void V60WorkbenchGateRuntimeEvidence_DryRunRollbackSafeApplyCommand_DoesNotPass()
    {
        var json = V60RuntimeEvidenceGateJson().Replace(
            "\"safeApplyCommand\": \"revitcli rollback receipt.json --approve\"",
            "\"safeApplyCommand\": \"revitcli rollback receipt.json --dry-run --approve\"",
            StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);

        Assert.False(ReleaseVerifier.EvaluateV60WorkbenchGateCheck(
            document.RootElement,
            out _,
            out _,
            out var runtimeEvidence));
        Assert.Contains("rollbackDryRunEvidence=false", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void V60WorkbenchGateRuntimeEvidence_YesRollbackSafeApplyCommand_Passes()
    {
        var json = V60RuntimeEvidenceGateJson().Replace(
            "\"safeApplyCommand\": \"revitcli rollback receipt.json --approve\"",
            "\"safeApplyCommand\": \"revitcli rollback receipt.json --yes --max-changes 5\"",
            StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);

        Assert.True(ReleaseVerifier.EvaluateV60WorkbenchGateCheck(
            document.RootElement,
            out _,
            out _,
            out var runtimeEvidence));
        Assert.Contains("rollbackDryRunEvidence=true", runtimeEvidence, StringComparison.OrdinalIgnoreCase);
    }

    private static string V60RuntimeEvidenceGateJson() =>
        """
{
  "id": "v60LocalBimOpsContractGate",
  "status": "pass",
  "evidence": "runtime true",
  "runtimeEvidence": {
    "commandSpine": true,
    "commandSpineOutputParity": true,
    "commandSpineNoWrites": true,
    "standardsValidate": true,
    "issuePreflight": true,
    "issuePackageDryRun": true,
    "deliverablesVerify": true,
    "journalVerify": true,
    "historyList": true,
    "historyListCountConsistency": true,
    "historyListRowOrder": true,
    "historyListEvidence": {
      "jsonEntryCount": 1,
      "jsonHiddenCount": 0,
      "jsonReturnedCount": 1,
      "tableRowCount": 1,
      "countConsistency": true,
      "idOrderMatch": true,
      "headerMatched": true
    },
    "rollbackDryRun": true,
    "rollbackDryRunPreview": true,
    "rollbackNoMutatingSetRequest": true,
    "rollbackDryRunEvidence": {
      "actionCount": 1,
      "conflictCount": 0,
      "errorCount": 0,
      "safeApplyCommand": "revitcli rollback receipt.json --approve",
      "safeApplyEmitted": true,
      "dryRunPreviewOnly": true,
      "sawDryRunSetPreview": true,
      "sawMutatingSetRequest": false
    },
    "workflowRegistry": true,
    "ledgerAppend": true,
    "ledgerQueryValidate": true,
    "ledgerReplay": true,
    "ledgerStats": true,
    "ledgerTimeline": true,
    "allRuntimeChecksPass": true
  }
}
""";

    private static string HistoryListEvidenceJsonBlock() =>
        """
    "historyListEvidence": {
      "jsonEntryCount": 1,
      "jsonHiddenCount": 0,
      "jsonReturnedCount": 1,
      "tableRowCount": 1,
      "countConsistency": true,
      "idOrderMatch": true,
      "headerMatched": true
    },
""";

    private static void AssertRuntimeEvidenceShape(JsonElement runtimeEvidence)
    {
        var fields = runtimeEvidence.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
        [
            "allRuntimeChecksPass",
            "commandSpine",
            "commandSpineNoWrites",
            "commandSpineOutputParity",
            "deliverablesVerify",
            "historyList",
            "historyListCountConsistency",
            "historyListEvidence",
            "historyListRowOrder",
            "issuePackageDryRun",
            "issuePreflight",
            "journalVerify",
            "ledgerAnalytics",
            "ledgerAppend",
            "ledgerQueryValidate",
            "ledgerReplay",
            "ledgerStats",
            "ledgerTimeline",
            "rollbackDryRun",
            "rollbackDryRunEvidence",
            "rollbackDryRunPreview",
            "rollbackNoMutatingSetRequest",
            "standardsValidate",
            "workflowRegistry",
        ], fields);
    }

    [Fact]
    public async Task Verify_UnknownOutputFormat_ReturnsFailureBeforeReadingFiles()
    {
        var output = new StringWriter();

        var exitCode = await ReleaseCommand.ExecuteVerifyAsync(_root, "yaml", null, strict: false, output);

        Assert.Equal(1, exitCode);
        Assert.Contains("unknown output format", output.ToString());
    }

    private static void WriteHealthyTree(string root)
    {
        WriteFile(root, "Directory.Build.props", """
<Project>
  <PropertyGroup>
    <RevitCliVersion>2.3.0</RevitCliVersion>
  </PropertyGroup>
</Project>
""");
        WriteFile(root, "CHANGELOG.md", """
# Changelog

## [Unreleased]

### Added - v2.3 inspect/discover

- Release integrity work.
""");
        WriteFile(root, "README.md", """
# RevitCli

See docs/release-checklist.md before release.
Update RevitCliVersion before tagging.
Use docs/revit2026-real-smoke.md for live smoke evidence.
Use profiles/office-standard for v5.4 standards runtime pack smoke.
git tag vX.Y.Z
""");
        WriteFile(root, "docs/templates/codex-recipes/standards-bootstrap.md", """
# Standards Bootstrap

Use `profiles/office-standard` for local standards install and validate smoke.
""");
        WriteFile(root, "docs/release-checklist.md", """
# Release Checklist

Run `revitcli release verify --strict` before v5.0 RC handoff.

### v5.0 Fault Injection Evidence
""");
        WriteFile(root, "docs/architect-terminal-vision.md", "# Vision");
        WriteFile(root, "docs/revit2026-real-smoke.md", "# Smoke\n\njournal evidence with V4Workbench");
        WriteFile(root, "docs/revit-version-compatibility.md", "2024 2025 2026");
        WriteFile(root, "docs/v5-rc-readiness.md", """
# RevitCli v5.0 RC Readiness

> Current status: GO.
> Claimed live Revit years: 2024, 2025, 2026.

## Stable P0 Commands

`workbench verify --contract workbench-contract.v2` exposes `v5RealSmokeDisclosure`, `issuePackageTraceability`, and `v5FaultInjectionCoverage`.

## Experimental / Deferred Commands

not live verified gaps remain documented in smoke reports.
Views, links, model map, dashboard, MCP, SaaS, or built-in LLM parser remain outside the v5.0 RC production claim.

Run `release verify --strict`.
""");
        WriteFile(root, "docs/smoke/v5.0/revit-2024-issue-closure.md", "# Revit 2024 issue closure smoke");
        WriteFile(root, "docs/smoke/v5.0/revit-2025-issue-closure.md", "# Revit 2025 issue closure smoke");
        WriteFile(root, "docs/smoke/v5.0/revit-2026-issue-closure.md", "# Revit 2026 issue closure smoke");
        WriteFile(root, "docs/smoke/v5.1/gap-report.md", """
# v5.1 sheet release control

production pilot gated for 100 sheet, 300 sheet, and 1000 sheet fixtures.
Revit 2026 dry-run/plan/receipt/rollback and journal verify evidence is not live verified.
Post-rollback evidence remains required before production support.
""");
        WriteFile(root, "docs/smoke/v5.2/gap-report.md", """
# v5.2 schedule deliverable closure

This schedule/package-only lane records an explicit go-forward decision while live evidence is not live verified.
It covers schedules batch-export, schedules compare, deliverables bundle, issue package, and journal verify.
""");
        WriteFile(root, "docs/smoke/v5.3/gap-report.md", """
# v5.3 numbering controlled apply

This lane records an explicit go-forward decision for reserved numbers, hold numbers, duplicate-target failure, plan apply, rollback, and journal verify.
Live numbering apply evidence is not live verified.
""");
        WriteFile(root, "docs/smoke/v5.4/gap-report.md", """
# RevitCli v5.4 Standards Runtime Pack Gap Report

v5.4 Standards Runtime Pack keeps the canonical `profiles/office-standard`
pack local and offline.

| Scope | Status | Evidence |
| --- | --- | --- |
| Standards install dry-run | portable verified | `standards install profiles/office-standard --dry-run` previews local files. |
| Standards validate | portable verified | `standards validate --manifest .revitcli/standards.yml --dir profiles/office-standard`. |
| Sheet map | portable verified | The pack includes a sheet map file. |
| Numbering rules | portable verified | The pack includes numbering rules. |
| release/workbench gates | portable verified | release/workbench gates validate the pack without SaaS, MCP, dashboard, or LLM runtime. |
| Bootstrap time SLA | not benchmarked | Timed pilot is deferred. |
| Office pilot | not live verified | BIM manager validation is deferred. |

No SaaS, no MCP, no dashboard-central, or no built-in LLM runtime is introduced.
""");
        WriteFile(root, "docs/smoke/v5.5/gap-report.md", """
# RevitCli v5.5 View and Coordination Hygiene Gap Report

v5.5 View and Coordination Hygiene is audit-first.

| Scope | Status | Evidence |
| --- | --- | --- |
| View standards audit | portable verified | `views audit` reports template issues. |
| View template planning | portable verified | `views template-apply --dry-run` freezes ids. |
| View clone planning | portable verified | `views clone-set --dry-run` carries a placed-view rollback guard. |
| Link audit | portable verified | `links audit` reports coordinate drift. |
| Link repair planning | portable verified | `links repair --dry-run` has no coordinate moves. |
| Model map audit | portable verified | `model map-check` reports workset drift. |
| Model map planning | portable verified | `model map-fix --dry-run` records write-precheck evidence. |
| Worksharing locks | not live verified | Controlled Revit smoke is deferred. |
| Journal verification | not live verified | `journal verify` evidence is deferred. |

No SaaS, no MCP, no dashboard-central, or no built-in LLM runtime is introduced.
""");
        WriteFile(root, "docs/smoke/v5.6/gap-report.md", """
# RevitCli v5.6 Team Pilot Pack Gap Report

v5.6 Team Pilot Pack is local-first and terminal-first.

| Scope | Status | Evidence |
| --- | --- | --- |
| Installer bootstrap | portable documented | The installer and doctor checks start the team pilot. |
| Doctor support report | portable documented | `doctor` output is required. |
| Team policy files | portable verified | Policy files record local-only boundaries. |
| Receipt retention | portable verified | receipt retention paths include receipts and journal. |
| Training handoff | documented | training docs are required. |
| Supportable error reports | documented | supportable error reports capture command, output, and remediation. |
| Office pilots | not live verified | office pilots remain required. |

No SaaS, no MCP, no dashboard-central, and no built-in LLM runtime is introduced.
""");
        WriteFile(root, "docs/v6-local-bimops-contract.md", """
# v6.0 Local BIMOps Workbench Contract

The product phrase is BIM Release OS and the technical kernel is the Revit Model Operations Ledger.
The contract is terminal-first, local-first, deterministic, dry-run first, and requires explicit approval.

Required local behavior includes planHash, receiptHash, journalPath, rollbackPointer, checks, artifacts, deterministic receipt rules, rollback preconditions, current-value conflict checks, audit trail invariants, journal verify, standards runtime, project memory, workflow registry, workflow registry --output json, workflow-registry.v1, ledger append, ledger replay, ledger query, ledger validate, ledger stats, ledger timeline, ledger analytics, release pilot validate, release pilot register, register nextActions, release pilot status, missingEvidence, missingEvidenceSummary, evidenceCompleteOfficePilotCount, remainingEvidenceCompleteOfficePilotCount, release pilot claim, claimBlockers, nextActions, ledger-append.v1, ledger-replay.v1, ledger-query.v1, ledger-validate.v1, ledger-stats.v1, ledger-timeline.v1, and ledger-analytics-bundle.v1.

No SaaS, no MCP, no built-in LLM, no dashboard-central workflow state, and no database runtime are introduced.
""");
        WriteFile(root, "docs/smoke/v6.0/gap-report.md", """
# RevitCli v6.0 Local BIMOps Workbench Gap Report

This is a contract baseline for operations ledger behavior. It is not live verified.

The Revit Model Operations Ledger has read-only standards validate, dry-run issue package, read-only deliverables verify, append-only ledger runtime, ledger replay preview, read-only ledger query, read-only ledger validate, read-only ledger stats, read-only ledger timeline, local ledger analytics bundle, read-only workflow registry, a pilot evidence packet, and a local controlled pilot packet. Live ledger apply, live Revit ledger integration, real Revit pilots, and office rollout pilots remain future evidence. Supported command-spine paths document table summary and Markdown detail parity, including history list` JSON/table outputs.
Local audit spine docs include journal verify JSON/table validity/root-hash parity and history-list.v1 JSON count consistency and table row-order parity.
Current-source Revit proof uses scripts\install-current-source-revit2026.ps1 and scripts/smoke-revit-wsl.sh --require-current-source before claiming live add-in/source alignment.

No SaaS, no MCP, no dashboard-central workflow, no built-in LLM parser, and no database runtime are introduced.
""");
        WriteFile(root, "docs/smoke/v6.0/pilot-evidence-template.md", """
# RevitCli v6.0 Office Rollout Pilot Evidence Packet

Use this packet only for controlled project-copy pilots. It is not a production support claim.

Create packets with release pilot scaffold --pilot-id v6-pilot-2026-office-copy-01 --output json before collecting private office evidence.
Run release pilot validate --path docs/smoke/v6.0/v6-pilot-2026-office-copy-01.md --output json before listing a packet as complete.
Dry-run release pilot register --pilot-id v6-pilot-2026-office-copy-01 --path docs/smoke/v6.0/v6-pilot-2026-office-copy-01.md --output json before using --yes and inspect register nextActions.
Check release pilot status --output json after registration to report remaining office pilots, missingEvidence, missingEvidenceSummary, evidenceCompleteOfficePilotCount, remainingEvidenceCompleteOfficePilotCount, and nextActions.
Run release pilot claim --output json as a dry-run and inspect claimBlockers and nextActions before using --yes for an office rollout completion claim.
Each packet records a Pilot identifier that must match the registered pilot id.

Required commands include doctor --check-version 2026 --output json, `status --output json`, workbench verify --contract workbench-contract.v2 --dir . --output json, release verify --strict --output json, ledger query --source ledger --output json, ledger validate --source ledger --output json, ledger stats --source ledger --analytics-snapshot .revitcli/analytics/ledger-stats.json --output json, ledger timeline --source ledger --analytics-snapshot .revitcli/analytics/ledger-timeline.json --output json, and journal verify --output json.
Live evidence records Rollback result, final verification command, safe retry status, user-review notes, and go-forward decision.
BIM manager signoff, Project-copy owner signoff, Support ticket review, and Multi-user rollout postmortem are required.
Minimum office pilots: 2-3 completed office pilots before any v6.0 office rollout completion claim.

Boundary summary: no SaaS, no MCP, no dashboard-central workflow, no built-in LLM parser, no database runtime, no central production model mutation, and no production support claim without completed office rollout pilots.
""");
        WriteFile(root, "docs/smoke/v6.0/office-rollout-status.json", """
{
  "schemaVersion": "v6-office-rollout-status.v1",
  "minimumOfficePilotCount": 2,
  "completedOfficePilotCount": 0,
  "completedPilotIds": [],
  "completedPilots": [],
  "officeRolloutCompletion": false,
  "productionSupportClaim": false,
  "requiredEvidence": {
    "doctor": true,
    "status": true,
    "workbench": true,
    "release": true,
    "ledgerQuery": true,
    "ledgerValidate": true,
    "ledgerStatsAnalyticsSnapshot": true,
    "ledgerTimelineAnalyticsSnapshot": true,
    "journalVerify": true,
    "rollbackResult": true,
    "userReview": true,
    "bimManagerSignoff": true,
    "projectCopyOwnerSignoff": true,
    "supportTicketReview": true,
    "multiUserRolloutPostmortem": true
  }
}
""");
        WriteFile(root, "docs/smoke/v6.0/local-controlled-pilot-20260525.md", """
# RevitCli v6.0 Local Controlled Pilot Evidence

This local controlled pilot packet references docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525. It records ledger-validate.v1 and journal verify with isValid=true. It is not office rollout completion and not a production support claim.
""");
        WriteFile(root, "docs/smoke/v6.0/local-controlled-pilot-20260525.evidence.json", """
{
  "schemaVersion": "v6-local-controlled-pilot-evidence.v1",
  "pilotId": "v6-local-controlled-pilot-20260525",
  "sourceBundle": "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525",
  "scope": {
    "localControlledPilot": true,
    "officeRolloutCompletion": false,
    "productionSupportClaim": false
  },
  "requiredFiles": [
    "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525/outputs/doctor.json",
    "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525/outputs/status.json",
    "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525/outputs/workbench.json",
    "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525/outputs/release.json",
    "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525/outputs/ledger-query.json",
    "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525/outputs/ledger-validate.json",
    "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525/outputs/ledger-stats.json",
    "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525/outputs/ledger-timeline.json",
    "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525/outputs/journal-sign.json",
    "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525/outputs/journal-verify.json",
    "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525/project/.revitcli/ledger/operations.jsonl",
    "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525/project/.revitcli/analytics/ledger-stats.json",
    "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525/project/.revitcli/analytics/ledger-timeline.json",
    "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525/project/.revitcli/journal.jsonl.sig"
  ],
  "doctor": {
    "success": true,
    "targetRevitYear": 2026,
    "versionsMatch": true
  },
  "status": {
    "revitYear": 2026,
    "documentName": "revit_cli"
  },
  "workbench": {
    "success": true,
    "issueCount": 0
  },
  "release": {
    "success": true,
    "errorCount": 0,
    "warningCount": 0
  },
  "ledgerQuery": {
    "schemaVersion": "ledger-query.v1",
    "totalOperations": 4,
    "issueCount": 0
  },
  "ledgerValidate": {
    "schemaVersion": "ledger-validate.v1",
    "valid": true,
    "operationCount": 4,
    "issueCount": 0,
    "errorCount": 0,
    "warningCount": 0
  },
  "ledgerStats": {
    "schemaVersion": "ledger-stats.v1",
    "operationCount": 4,
    "issueCount": 0
  },
  "ledgerTimeline": {
    "schemaVersion": "ledger-timeline.v1",
    "operationCount": 4,
    "bucketCount": 1,
    "issueCount": 0
  },
  "journal": {
    "signEntryCount": 2,
    "verifyEntryCount": 2,
    "isValid": true,
    "signRootHash": "b915f6cf6ffea40425cb16bf51bba858339e8e00059f07455b919475968d24fe",
    "rootHash": "b915f6cf6ffea40425cb16bf51bba858339e8e00059f07455b919475968d24fe",
    "errors": []
  }
}
""");
        WriteLocalControlledPilotSourceBundle(root);
        WriteFile(root, "docs/smoke/v6.0/ledger-query.md", """
# RevitCli v6.0 Ledger Query Portable Smoke

This read-only ledger query emits ledger-query.v1 from journal, history, delivery manifest, and workflow receipt files.
Malformed artifacts are reported as issues. The command does not write files, start Revit, call a network service, or create a database. It uses deterministic timestamp/source/path/line ordering, documents JSON/table/Markdown output semantic parity, includes event-level no-write evidence and final file-tree snapshot evidence, and uses no database.

No SaaS, no MCP, no dashboard-central workflow, no built-in LLM parser, and no database runtime are introduced.
""");
        WriteFile(root, "docs/smoke/v6.0/ledger-append.md", """
# RevitCli v6.0 Ledger Append Portable Smoke

This append-only ledger runtime uses ledger append to emit ledger-append.v1 dry-run previews and append ledger-operation.v1 records under .revitcli/ledger/operations.jsonl only with --yes.
The dry-run default does not write files. Approved append smoke includes bounded local write evidence for only the ledger JSONL path, source ledger query/validate readback, deterministic evidence links, and JSON/table/Markdown output semantic parity. The command does not start Revit, call a network service, replay/apply ledger records, or create a database, and uses no database.

No SaaS, no MCP, no dashboard-central workflow, no built-in LLM parser, and no database runtime are introduced.
""");
        WriteFile(root, "docs/smoke/v6.0/ledger-replay.md", """
# RevitCli v6.0 Ledger Replay Preview Portable Smoke

This ledger replay preview emits ledger-replay.v1 from source ledger records. It is preview-only: dryRun is true, applySupported is false, and every step reports canApply false with a block reason.
It documents JSON/table/Markdown output semantic parity. The command does not write files, does not start Revit, does not call a network service, does not apply ledger records, and uses no database.

No SaaS, no MCP, no dashboard-central workflow, no built-in LLM parser, and no database runtime are introduced.
""");
        WriteFile(root, "docs/smoke/v6.0/standards-runtime.md", """
# RevitCli v6.0 Standards Runtime Portable Smoke

This standards runtime smoke doc exposes revitcli standards validate --output json for local standards runtime checks.
It documents table summary and Markdown detail parity for validation fields.
It includes populated-target final file-tree snapshot evidence for standards install dry-run planning.
The command is read-only, does not start Revit, does not write model data, and keeps dashboard-central, SaaS, MCP, and built-in LLM behavior out of the portable smoke scope.
Boundary summary: no Revit API, no add-in, no SaaS, no MCP, no database, no dashboard-central state, and no built-in LLM parser.
""");
        WriteFile(root, "docs/smoke/v6.0/issue-spine.md", """
# RevitCli v6.0 Issue Spine Portable Smoke

This issue spine smoke doc exposes revitcli issue preflight --profile .revitcli/issue.yml --output json and revitcli issue package --profile .revitcli/issue.yml --bundle-path deliverables/issue-package.zip --dry-run --output json.
It remains dry-run first, documents hidden mutation guards, emits issue-package-receipt.v1 evidence, includes table summary and Markdown detail parity, and includes dry-run no-write evidence.
The scope does not start Revit and excludes dashboard-central, SaaS, MCP, and built-in LLM behavior.
Boundary summary: no Revit API, no add-in, no SaaS, no MCP, no database, no dashboard-central state, and no built-in LLM parser.
""");
        WriteFile(root, "docs/smoke/v6.0/deliverables-verify.md", """
# RevitCli v6.0 Deliverables Verify Portable Smoke

This deliverables verification smoke doc exposes revitcli deliverables verify --output json.
It checks local manifest-read and readable-receipt evidence, reports missing receipts, preserves Kinds and Outcomes counts in table and Markdown, and runs without package writes.
The scope uses no Revit API, runs without starting Revit, and excludes dashboard-central, SaaS, MCP, and built-in LLM behavior.
Boundary summary: no Revit API, no add-in, no SaaS, no MCP, no database, no dashboard-central state, and no built-in LLM parser.
""");
        WriteFile(root, "docs/smoke/v6.0/ledger-validate.md", """
# RevitCli v6.0 Ledger Validate Portable Smoke

This read-only ledger validate emits ledger-validate.v1 from journal, history, delivery manifest, and workflow receipt files.
It checks source readability, artifact links, receipt status, declared receipt hash values, timestamp format, and explicit UTC offset requirements. Time filters preserve invalid timestamp warnings. It documents validation JSON/table/Markdown semantic parity. The command does not write files, start Revit, call a network service, or create a database. It includes event-level no-write evidence and final file-tree snapshot evidence, and uses no database.

No SaaS, no MCP, no dashboard-central workflow, no built-in LLM parser, and no database runtime are introduced.
""");
        WriteFile(root, "docs/smoke/v6.0/ledger-stats.md", """
# RevitCli v6.0 Ledger Stats Portable Smoke

This read-only ledger stats emits ledger-stats.v1 from journal, history, delivery manifest, and workflow receipt files.
It summarizes operation counts with source counts, action counts, category and operator counts, receipt status counts, issue source counts, and issue severity counts. Malformed journal, delivery manifest, and workflow receipt artifacts are surfaced as issue source counts and issue severity counts. It documents JSON/table/Markdown stats semantic parity. The command does not write files, start Revit, call a network service, or create a database. It includes event-level no-write evidence and final file-tree snapshot evidence, and uses no database.

No SaaS, no MCP, no dashboard-central workflow, no built-in LLM parser, and no database runtime are introduced.
""");
        WriteFile(root, "docs/smoke/v6.0/ledger-timeline.md", """
# RevitCli v6.0 Ledger Timeline Portable Smoke

This read-only ledger timeline emits ledger-timeline.v1 from journal, history, delivery manifest, and workflow receipt files.
It buckets project memory by day or hour and reports bucket, source, action, category counts per bucket, operator counts per bucket, receipt status, issue severity, JSON/table/Markdown timeline semantic parity, explicit UTC offset handling, and unbucketed timestamp warnings. Time filters preserve unbucketed timestamp warnings. Repeated --project roots emit projectDirectories and byProject counts for explicitly supplied local project roots. The command does not write files, start Revit, call a network service, or create a database. It includes event-level no-write evidence and final file-tree snapshot evidence, and uses no database.

No SaaS, no MCP, no dashboard-central workflow, no built-in LLM parser, and no database runtime are introduced.
""");
        WriteFile(root, "docs/smoke/v6.0/ledger-analytics.md", """
# RevitCli v6.0 Ledger Analytics Bundle Smoke

This local ledger analytics bundle exposes ledger analytics and emits ledger-analytics-bundle.v1.
It writes ledger-stats.v1 and ledger-timeline.v1 local snapshots. JSON/table/Markdown output formats describe the same bundle paths and operation counts. The payload declares localOnly=true, databaseRuntime=false, and networkService=false. It does not start Revit, does not call a network service, and does not create a database.

No SaaS, no MCP, no dashboard-central workflow, no built-in LLM parser, and no database runtime are introduced.
""");
        WriteFile(root, "docs/smoke/v6.0/workflow-registry.md", """
# RevitCli v6.0 Workflow Registry Portable Smoke

This read-only workflow registry emits workflow-registry.v1 from local workflow YAML files.
It indexes inputs, outputs, delivery bundle, schedule export, schedule-export-manifest.v1, publish output, read/write scope, risk level, dry-run command, approval command, rollback support, receipt schema, publish-receipt.v1, journal verify, and acceptance evidence. It documents JSON/table/Markdown output semantic parity. The command does not write files, run workflow steps, start Revit, call a network service, or create a database, and includes event-level no-write evidence plus final file-tree snapshot evidence.

Boundary summary: no SaaS, no MCP, no built-in LLM parser, no database, and no dashboard-central workflow state.
""");
        WriteOfficeStandardPack(root);
        WriteTeamPilotPack(root);
        WriteFile(root, ".github/workflows/ci.yml", """
name: CI
jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - run: dotnet restore shared/RevitCli.Shared/RevitCli.Shared.csproj
      - run: dotnet build src/RevitCli/RevitCli.csproj --no-restore
      - run: dotnet run --project src/RevitCli/RevitCli.csproj --no-build -- release verify --output json
      - run: dotnet run --project src/RevitCli/RevitCli.csproj --no-build -- workbench verify --contract workbench-contract.v2 --dir . --output json
      - run: dotnet test tests/RevitCli.Tests/RevitCli.Tests.csproj --no-build
""");
        WriteFile(root, ".github/workflows/release.yml", """
on:
  push:
    tags:
      - "v*"
jobs:
  release:
    runs-on: [self-hosted, windows, revit2026]
    steps:
      - run: |
          $installDir = $env:REVITCLI_REVIT2026_INSTALL_DIR
          dotnet publish src/RevitCli.Addin -p:RevitYear=2026 "-p:RevitInstallDir=$installDir"
      - run: Get-FileHash ./revitcli-win-x64.zip | Set-Content SHA256SUMS.txt
      - uses: softprops/action-gh-release@v2
""");
        WriteFile(root, ".github/workflows/publish.yml", """
on:
  workflow_dispatch:
    inputs:
      tag:
        required: true
        type: string
jobs:
  publish:
    steps:
      - run: dotnet pack src/RevitCli
      - run: dotnet nuget push --api-key "${{ secrets.NUGET_API_KEY }}"
""");
        WriteFile(root, "scripts/install.ps1", """
param(
  [string]$Revit2024InstallDir,
  [string]$Revit2025InstallDir,
  [string]$Revit2026InstallDir
)
$staged = 'staged'
function Test-PathListContains { }
""");
        WriteFile(root, "scripts/install-current-source-revit2026.ps1", """
param(
    [string]$Revit2026InstallDir = "D:\revit2026\Revit 2026",
    [switch]$AllowRunningRevit
)
$repoRoot = Split-Path -Parent $PSScriptRoot
$installRoot = $repoRoot
if ($repoRoot.StartsWith("\\", [System.StringComparison]::Ordinal)) {
    $snapshotRoot = Join-Path $env:LOCALAPPDATA "RevitCli\current-source-snapshot"
    & robocopy $repoRoot $snapshotRoot /MIR /XD ".artifacts" ".codex" "bin" "obj" /NFL /NDL /NJH /NJS /NP
    $installRoot = $snapshotRoot
}
$installArgs = @{
    RevitYears = @("2026")
    Revit2026InstallDir = $Revit2026InstallDir
    Force = $true
}
if ($AllowRunningRevit) {
    $installArgs.AllowRunningRevit = $true
}
& (Join-Path $installRoot "scripts\install.ps1") @installArgs
Write-Host "scripts/smoke-revit-wsl.sh --require-current-source"
""");
        WriteFile(root, "scripts/smoke-revit.ps1", "2024 2025 2026 V4Workbench workbench\", \"verify schedule\", \"export");
        WriteFile(root, "scripts/smoke-revit-wsl.sh", """
#!/usr/bin/env bash
set -euo pipefail
require_current_source=false
current_source_installed=false
currentSourceDriftKind="restart-required"
cliCommit="abc"
installedAddinCommit="abc"
liveAddinCommit="abc"
statusAddinCommit="abc"
echo "restart-required"
echo "install-required"
sourceInstalledDrift=true
postRestartCommand="scripts/smoke-revit-wsl.sh --require-current-source"
cat > install-current-source.ps1 <<'EOF'
& .\scripts\install-current-source-revit2026.ps1 -Revit2026InstallDir 'D:\revit2026\Revit 2026'
EOF
echo "currentSourceInstalled"
echo "nextActions"
echo "mutatesModel: false"
""");
    }

    private static void WriteLocalControlledPilotSourceBundle(string root)
    {
        const string bundle = "docs/smoke/v6.0/revit2026-v6-local-controlled-pilot-20260525";
        WriteFile(root, $"{bundle}/outputs/doctor.json", """{"success":true,"targetRevitYear":2026}""");
        WriteFile(root, $"{bundle}/outputs/status.json", """{"revitYear":2026,"documentName":"revit_cli"}""");
        WriteFile(root, $"{bundle}/outputs/workbench.json", """{"success":true,"issueCount":0}""");
        WriteFile(root, $"{bundle}/outputs/release.json", """{"success":true,"errorCount":0,"warningCount":0}""");
        WriteFile(root, $"{bundle}/outputs/ledger-query.json", """{"schemaVersion":"ledger-query.v1","summary":{"totalOperations":1,"issueCount":0}}""");
        WriteFile(root, $"{bundle}/outputs/ledger-validate.json", """{"schemaVersion":"ledger-validate.v1","valid":true,"summary":{"operationCount":1,"issueCount":0,"errorCount":0}}""");
        WriteFile(root, $"{bundle}/outputs/ledger-stats.json", """{"schemaVersion":"ledger-stats.v1","summary":{"operationCount":1,"issueCount":0}}""");
        WriteFile(root, $"{bundle}/outputs/ledger-timeline.json", """{"schemaVersion":"ledger-timeline.v1","summary":{"operationCount":1,"bucketCount":1,"issueCount":0}}""");
        WriteFile(root, $"{bundle}/outputs/journal-sign.json", """{"entryCount":1,"rootHash":"b915f6cf6ffea40425cb16bf51bba858339e8e00059f07455b919475968d24fe"}""");
        WriteFile(root, $"{bundle}/outputs/journal-verify.json", """{"isValid":true,"entryCount":1,"rootHash":"b915f6cf6ffea40425cb16bf51bba858339e8e00059f07455b919475968d24fe","errors":[]}""");
        WriteFile(root, $"{bundle}/project/.revitcli/ledger/operations.jsonl", "{}\n");
        WriteFile(root, $"{bundle}/project/.revitcli/analytics/ledger-stats.json", "{}\n");
        WriteFile(root, $"{bundle}/project/.revitcli/analytics/ledger-timeline.json", "{}\n");
        WriteFile(root, $"{bundle}/project/.revitcli/journal.jsonl.sig", "signature\n");
    }

    private static void WriteOfficeStandardPack(string root)
    {
        WriteFile(root, "profiles/office-standard/.revitcli/standards.yml", """
version: 1
name: office-standard
packVersion: 2026.5.0
compatibility:
  revitCli: ">=0.1.0"
  revitYears: [2024, 2025, 2026]
  notes:
    - Local-first pack; no SaaS, MCP, dashboard, or built-in LLM dependency.
required:
  profiles: [.revitcli.yml]
  workflows: [pre-issue]
  outputPaths: [deliverables]
  scheduleTemplates: [doors]
  sheetMaps:
    - .revitcli/sheets/issue-meta.yml
  numberingRules:
    - .revitcli/numbering/rooms.yml
    - .revitcli/numbering/doors.yml
  familyRules: [name-non-empty, category-known]
""");
        WriteFile(root, "profiles/office-standard/.revitcli.yml", """
version: 1
checks:
  default:
    failOn: error
schedules:
  doors:
    category: doors
    fields: [Mark]
    name: Door Schedule
""");
        WriteFile(root, "profiles/office-standard/.revitcli/workflows/pre-issue.yml", """
version: 1
name: pre-issue
steps:
  - run: revitcli standards validate --output json
    mode: read-only
""");
        WriteFile(root, "profiles/office-standard/.revitcli/sheets/issue-meta.yml", """
issueCode: [Sheet Issue Code, Issue Code]
issueDate: [Sheet Issue Date, Issue Date]
""");
        WriteFile(root, "profiles/office-standard/.revitcli/numbering/rooms.yml", """
schemaVersion: 1
parameter: Number
scheme: "R-{seq:03}"
start: 1
""");
        WriteFile(root, "profiles/office-standard/.revitcli/numbering/doors.yml", """
schemaVersion: 1
category: doors
parameter: Mark
scheme: "D-{seq:03}"
start: 1
""");
        WriteFile(root, "profiles/office-standard/deliverables/.gitkeep", "");
    }

    private static void WriteTeamPilotPack(string root)
    {
        WriteFile(root, "docs/smoke/v5.6/install-postmortem-template.md", "# install postmortem\n");
        WriteFile(root, "docs/v5-demo-and-pilot-playbook.md", "# pilot playbook\n");
        WriteFile(root, "docs/smoke/v5.6/support-error-report-template.md", "# support error report\n");
        WriteFile(root, "profiles/team-pilot/.revitcli/team-policy.yml", """
schemaVersion: team-policy.v1
name: team-pilot
boundaries:
  localFirst: true
  terminalFirst: true
  dryRunFirst: true
  noSaaS: true
  noMcp: true
  noBuiltInLlm: true
  noDashboardCentral: true
install:
  revitYears:
    - 2024
    - 2025
    - 2026
receiptRetention:
  days: 180
  maxFiles: 5000
  paths:
    - .revitcli/receipts
    - .revitcli/workflows/receipts
    - .revitcli/journal.jsonl
requiredCommands:
  - doctor --output json
  - workbench verify --contract workbench-contract.v2 --dir . --output json
  - release verify --strict --output json
  - standards validate --output json
  - journal verify --dir . --output json
support:
  installPostmortemTemplate: docs/smoke/v5.6/install-postmortem-template.md
  userInterviewChecklist: docs/v5-demo-and-pilot-playbook.md
  errorReportTemplate: docs/smoke/v5.6/support-error-report-template.md
""");
    }

    private static void WriteV5NoGoDocs(string root)
    {
        var smokeRoot = Path.Combine(root, "docs", "smoke", "v5.0");
        if (Directory.Exists(smokeRoot))
            Directory.Delete(smokeRoot, recursive: true);

        WriteFile(root, "docs/v5-rc-readiness.md", """
# RevitCli v5.0 RC Readiness

> Current status: NO-GO.

## Stable P0 Commands

`workbench verify --contract workbench-contract.v2` exposes `v5RealSmokeDisclosure`, `issuePackageTraceability`, and `v5FaultInjectionCoverage`.

## Experimental / Deferred Commands

Views, links, model map, dashboard, MCP, SaaS, or built-in LLM parser remain outside the v5.0 RC production claim.

Run `release verify --strict`.
""");
        WriteFile(root, "docs/smoke/v5.0/gap-report.md", """
# RevitCli v5.0 Live Smoke Gap Report

| Revit year | v5.0 issue-closure live smoke status | Notes |
| --- | --- | --- |
| Revit 2024 | not live verified | No controlled issue-closure evidence is recorded. |
| Revit 2025 | not live verified | No controlled issue-closure evidence is recorded. |
| Revit 2026 | not live verified | No controlled issue-closure evidence is recorded. |
""");
    }

    private void WriteFile(string relativePath, string content) =>
        WriteFile(_root, relativePath, content);

    private async Task RegisterCompletedPilotAsync(string pilotId)
    {
        WriteFile($"docs/smoke/v6.0/{pilotId}.md", CompletedPilotEvidencePacketContent(pilotId));
        var output = new StringWriter();
        var exitCode = await ReleaseCommand.ExecutePilotRegisterAsync(
            _root,
            pilotId,
            $"docs/smoke/v6.0/{pilotId}.md",
            yes: true,
            outputFormat: "json",
            output);
        Assert.True(exitCode == 0, output.ToString());
    }

    private static string CompletedPilotEvidenceJson(string pilotId, string? evidencePacketPath = null)
    {
        var serializedEvidencePacketPath = JsonSerializer.Serialize(evidencePacketPath ?? $"docs/smoke/v6.0/{pilotId}.md");
        return $$"""
        {
          "pilotId": "{{pilotId}}",
          "evidencePacketPath": {{serializedEvidencePacketPath}},
          "doctor": true,
          "status": true,
          "workbench": true,
          "release": true,
          "ledgerQuery": true,
          "ledgerValidate": true,
          "ledgerStatsAnalyticsSnapshot": true,
          "ledgerTimelineAnalyticsSnapshot": true,
          "journalVerify": true,
          "rollbackResult": true,
          "userReview": true,
          "bimManagerSignoff": true,
          "projectCopyOwnerSignoff": true,
          "supportTicketReview": true,
          "multiUserRolloutPostmortem": true
        }
        """;
    }

    private static void WriteCompletedPilotEvidencePackets(string root, params string[] pilotIds)
    {
        foreach (var pilotId in pilotIds)
            WriteFile(root, $"docs/smoke/v6.0/{pilotId}.md", CompletedPilotEvidencePacketContent(pilotId));
    }

    private static string CompletedPilotEvidencePacketContent(string pilotId) => $$"""
        # v6.0 Office Pilot {{pilotId}}

        ## Scope

        - Pilot identifier: {{pilotId}}

        ## Required Commands

        - `doctor --check-version 2026 --output json`
        - `status --output json`
        - `workbench verify --contract workbench-contract.v2 --dir . --output json`
        - `release verify --strict --output json`
        - `ledger query --source ledger --output json`
        - `ledger validate --source ledger --output json`
        - `ledger stats --source ledger --analytics-snapshot .revitcli/analytics/ledger-stats.json --output json`
        - `ledger timeline --source ledger --analytics-snapshot .revitcli/analytics/ledger-timeline.json --output json`
        - `journal verify --output json`

        ## Live Operation Evidence

        - Rollback result: passed

        ## User Review

        - BIM manager signoff: approved
        - Project-copy owner signoff: approved
        - Support ticket review: reviewed
        - Multi-user rollout postmortem: complete

        Boundary summary: no SaaS, no MCP, no dashboard-central workflow, no built-in LLM parser, no database runtime.
        """;

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
