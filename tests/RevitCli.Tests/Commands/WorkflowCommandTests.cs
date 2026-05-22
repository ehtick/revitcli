using System.Text.Json;
using RevitCli.Commands;
using RevitCli.Workflows;

namespace RevitCli.Tests.Commands;

public sealed class WorkflowCommandTests : IDisposable
{
    private readonly string _root;

    public WorkflowCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-workflow-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task Init_NoTemplate_ListsBuiltInTemplates()
    {
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteInitAsync(null, _root, force: false, output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Available workflow templates:", text);
        Assert.Contains("pre-issue", text);
        Assert.Contains("weekly-health", text);
        Assert.Contains("export-package", text);
        Assert.Contains("family-cleanup", text);
    }

    [Fact]
    public async Task Init_Template_CreatesWorkflowFile()
    {
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteInitAsync("pre-issue", _root, force: false, output);

        var target = Path.Combine(_root, ".revitcli", "workflows", "pre-issue.yml");
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(target));
        Assert.Contains("Created .revitcli", output.ToString());

        var validateOutput = new StringWriter();
        var validateExit = await WorkflowCommand.ExecuteValidateAsync(target, null, "table", validateOutput);
        Assert.Equal(0, validateExit);
        Assert.Contains("OK pre-issue", validateOutput.ToString());
    }

    [Fact]
    public async Task Init_All_CreatesBuiltInWorkflowPack()
    {
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteInitAsync("all", _root, force: false, output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(_root, ".revitcli", "workflows", "pre-issue.yml")));
        Assert.True(File.Exists(Path.Combine(_root, ".revitcli", "workflows", "weekly-health.yml")));
        Assert.True(File.Exists(Path.Combine(_root, ".revitcli", "workflows", "export-package.yml")));
        Assert.True(File.Exists(Path.Combine(_root, ".revitcli", "workflows", "family-cleanup.yml")));

        var validateOutput = new StringWriter();
        var validateExit = await WorkflowCommand.ExecuteValidateAsync(null, _root, "table", validateOutput);
        Assert.Equal(0, validateExit);
        Assert.Contains("OK pre-issue", validateOutput.ToString());
        Assert.Contains("OK weekly-health", validateOutput.ToString());
        Assert.Contains("OK export-package", validateOutput.ToString());
        Assert.Contains("OK family-cleanup", validateOutput.ToString());
    }

    [Fact]
    public async Task Init_ExistingFileRequiresForce()
    {
        var firstOutput = new StringWriter();
        await WorkflowCommand.ExecuteInitAsync("pre-issue", _root, force: false, firstOutput);
        var secondOutput = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteInitAsync("pre-issue", _root, force: false, secondOutput);

        Assert.Equal(1, exitCode);
        Assert.Contains("--force", secondOutput.ToString());
    }

    [Fact]
    public async Task Validate_DefaultDirectory_ValidWorkflowReturnsZero()
    {
        var workflowDir = Path.Combine(_root, ".revitcli", "workflows");
        Directory.CreateDirectory(workflowDir);
        WriteWorkflow(Path.Combine(workflowDir, "pre-issue.yml"), ValidWorkflowYaml());
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteValidateAsync(null, _root, "table", output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Workflow validation", text);
        Assert.Contains("OK pre-issue", text);
        Assert.Contains("steps=3", text);
    }

    [Fact]
    public async Task Validate_KnowledgeAndWorkflowDiscoveryCommands_ReturnsZero()
    {
        var path = Path.Combine(_root, "knowledge.yml");
        WriteWorkflow(path, """
name: knowledge-handoff
steps:
  - run: revitcli report knowledge --output markdown
    mode: read-only
  - run: revitcli inspect workflows --output markdown
    mode: read-only
  - run: revitcli inspect plans --output markdown
    mode: read-only
""");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteValidateAsync(path, null, "table", output);

        Assert.Equal(0, exitCode);
        Assert.Contains("OK knowledge-handoff", output.ToString());
    }

    [Fact]
    public async Task Validate_WorkbenchAndWorkflowReviewCommands_ReturnsZero()
    {
        var path = Path.Combine(_root, "workbench-handoff.yml");
        WriteWorkflow(path, """
name: workbench-handoff
steps:
  - run: revitcli workbench verify --output json
    mode: read-only
  - run: revitcli workbench handoff --output markdown
    mode: read-only
  - run: revitcli workflow review .revitcli/workflows/pre-issue.yml --output markdown
    mode: read-only
""");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteValidateAsync(path, null, "table", output);

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        Assert.Contains("OK workbench-handoff", text);
        Assert.DoesNotContain("WARNING", text);
    }

    [Fact]
    public async Task Validate_IssueClosureCommands_ReturnsZero()
    {
        var path = Path.Combine(_root, "issue-closure.yml");
        WriteWorkflow(path, """
name: issue-closure
steps:
  - run: revitcli issue preflight --profile .revitcli/issue.yml --output markdown --fail-on warning
    mode: read-only
  - run: revitcli issue diff --from .revitcli/history/baseline.json --to current --review --output markdown
    mode: read-only
  - run: revitcli issue package --profile .revitcli/issue.yml --bundle-path deliverables/issue.zip --dry-run --include-receipts true --output markdown
    mode: dry-run
""");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteValidateAsync(path, null, "table", output);

        Assert.Equal(0, exitCode);
        Assert.Contains("OK issue-closure", output.ToString());
    }

    [Fact]
    public async Task Validate_UnknownWorkbenchSubcommand_ReturnsFailure()
    {
        var path = Path.Combine(_root, "bad.yml");
        WriteWorkflow(path, """
name: bad
steps:
  - run: revitcli workbench made-up
    mode: read-only
""");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteValidateAsync(path, null, "table", output);

        var text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("unknown RevitCli command 'workbench made-up'", text);
        Assert.Contains("existing CLI commands", text);
    }

    [Fact]
    public async Task Validate_MissingMode_ReturnsFailureWithPath()
    {
        var path = Path.Combine(_root, "bad.yml");
        WriteWorkflow(path, """
name: bad
steps:
  - run: revitcli check issue
""");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteValidateAsync(path, null, "table", output);

        Assert.Equal(1, exitCode);
        Assert.Contains("steps[0].mode", output.ToString());
    }

    [Fact]
    public async Task Validate_UnknownTopLevelCommand_ReturnsFailure()
    {
        var path = Path.Combine(_root, "bad.yml");
        WriteWorkflow(path, """
name: bad
steps:
  - run: revitcli made-up --dry-run
    mode: dry-run
""");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteValidateAsync(path, null, "table", output);

        var text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("unknown RevitCli command 'made-up'", text);
        Assert.Contains("existing CLI commands", text);
    }

    [Fact]
    public async Task Validate_UnknownGroupedSubcommand_ReturnsFailure()
    {
        var path = Path.Combine(_root, "bad.yml");
        WriteWorkflow(path, """
name: bad
steps:
  - run: revitcli workflow made-up
    mode: read-only
""");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteValidateAsync(path, null, "table", output);

        var text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("unknown RevitCli command 'workflow made-up'", text);
        Assert.Contains("existing CLI commands", text);
    }

    [Fact]
    public async Task Validate_UnknownNestedSubcommand_ReturnsFailure()
    {
        var path = Path.Combine(_root, "bad.yml");
        WriteWorkflow(path, """
name: bad
steps:
  - run: revitcli sheets index delete
    mode: read-only
""");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteValidateAsync(path, null, "table", output);

        var text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("unknown RevitCli command 'sheets index delete'", text);
        Assert.Contains("existing CLI commands", text);
    }

    [Fact]
    public async Task Validate_MutatingWithoutApproval_ReturnsFailure()
    {
        var path = Path.Combine(_root, "bad.yml");
        WriteWorkflow(path, """
name: bad
steps:
  - run: revitcli history capture --source manual
    mode: mutating
""");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteValidateAsync(path, null, "table", output);

        Assert.Equal(1, exitCode);
        Assert.Contains("steps[0].requiresApproval", output.ToString());
        Assert.Contains("must declare requiresApproval: true", output.ToString());
    }

    [Fact]
    public async Task Validate_JsonOutput_IncludesIssues()
    {
        var path = Path.Combine(_root, "bad.yml");
        WriteWorkflow(path, """
name: bad
steps:
  - run: powershell ./unsafe.ps1
    mode: read-only
""");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteValidateAsync(path, null, "json", output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var first = json.RootElement.EnumerateArray().Single();
        Assert.Equal("bad", first.GetProperty("name").GetString());
        Assert.Contains(
            first.GetProperty("issues").EnumerateArray(),
            issue => issue.GetProperty("path").GetString() == "steps[0].run");
    }

    [Fact]
    public async Task Validate_MarkdownOutput_PrintsHandoffReport()
    {
        var path = Path.Combine(_root, "pre-issue.yml");
        WriteWorkflow(path, ValidWorkflowYaml());
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteValidateAsync(path, null, "markdown", output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# Workflow Validation", text);
        Assert.Contains("## pre-issue", text);
        Assert.Contains("- Status: `OK`", text);
        Assert.Contains("- Issues:", text);
    }

    [Fact]
    public async Task Validate_UnknownOutput_ReturnsFailureBeforeDiscovery()
    {
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteValidateAsync(
            "/path/that/does/not/exist",
            null,
            "yaml",
            output);

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Simulate_ValidWorkflow_PrintsPlanWithoutRunning()
    {
        var path = Path.Combine(_root, "pre-issue.yml");
        WriteWorkflow(path, ValidWorkflowYaml());
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteSimulateAsync(path, null, "table", output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Workflow simulation: pre-issue", text);
        Assert.Contains("Can run: yes", text);
        Assert.Contains("[dry-run]", text);
        Assert.Contains("revitcli publish issue --dry-run", text);
    }

    [Fact]
    public async Task Simulate_JsonOutput_IncludesModeCounts()
    {
        var path = Path.Combine(_root, "pre-issue.yml");
        WriteWorkflow(path, ValidWorkflowYaml());
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteSimulateAsync(path, null, "json", output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.True(root.GetProperty("canRun").GetBoolean());
        Assert.Equal(3, root.GetProperty("stepCount").GetInt32());
        Assert.Equal(1, root.GetProperty("modeCounts").GetProperty("read-only").GetInt32());
        Assert.Equal(1, root.GetProperty("modeCounts").GetProperty("dry-run").GetInt32());
        Assert.Equal(1, root.GetProperty("modeCounts").GetProperty("mutating").GetInt32());
    }

    [Fact]
    public async Task Simulate_MarkdownOutput_PrintsReviewablePlan()
    {
        var path = Path.Combine(_root, "pre-issue.yml");
        WriteWorkflow(path, ValidWorkflowYaml());
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteSimulateAsync(path, null, "markdown", output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# Workflow Simulation: pre-issue", text);
        Assert.Contains("## Plan", text);
        Assert.Contains("approval required", text);
        Assert.Contains("revitcli publish issue --dry-run", text);
    }

    [Fact]
    public async Task Simulate_UnknownOutput_ReturnsFailureBeforeLoadingWorkflow()
    {
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteSimulateAsync(
            "/path/that/does/not/exist.yml",
            null,
            "yaml",
            output);

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Review_JsonOutput_IncludesApprovalAndEvidenceHints()
    {
        var path = Path.Combine(_root, "pre-issue.yml");
        WriteWorkflow(path, ValidWorkflowYaml());
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReviewAsync(path, null, "json", output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("workflow-review.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("canRun").GetBoolean());
        Assert.Equal(Path.GetFullPath(_root), root.GetProperty("projectDirectory").GetString());
        Assert.Equal(1, root.GetProperty("mutatingStepCount").GetInt32());
        Assert.Equal(1, root.GetProperty("approvalRequiredCount").GetInt32());
        var artifactReadiness = root.GetProperty("artifactReadiness").EnumerateArray().ToArray();
        Assert.Contains(artifactReadiness, artifact =>
            artifact.GetProperty("name").GetString() == "profile" &&
            artifact.GetProperty("status").GetString() == "missing" &&
            artifact.GetProperty("relativePath").GetString() == ".revitcli.yml" &&
            artifact.GetProperty("reviewCommand").GetString() == "revitcli profile validate" &&
            artifact.GetProperty("matchedSteps").EnumerateArray().Select(step => step.GetInt32()).SequenceEqual(new[] { 1, 2 }));
        Assert.Contains(artifactReadiness, artifact =>
            artifact.GetProperty("name").GetString() == "history" &&
            artifact.GetProperty("status").GetString() == "missing" &&
            artifact.GetProperty("matchedSteps").EnumerateArray().Select(step => step.GetInt32()).SequenceEqual(new[] { 3 }));
        var preRunCommands = root.GetProperty("preRunHandoffCommands").EnumerateArray().ToArray();
        Assert.Contains(
            preRunCommands,
            command => command.GetString() == "revitcli workbench verify --output json");
        Assert.Contains(
            preRunCommands,
            command => command.GetString() == "revitcli workbench handoff --output markdown");
        Assert.Contains(
            root.GetProperty("recommendedCommands").EnumerateArray(),
            command => command.GetString()!.Contains("workflow run") &&
                       command.GetString()!.Contains("--dry-run"));
        var postRunCommands = root.GetProperty("postRunReceiptCommands").EnumerateArray().ToArray();
        Assert.Contains(
            postRunCommands,
            command => command.GetString()!.Contains("workflow receipts --name \"pre-issue\"") &&
                       command.GetString()!.Contains("--output markdown"));
        Assert.Contains(
            postRunCommands,
            command => command.GetString()!.Contains("--failed-only"));
        Assert.Contains(
            postRunCommands,
            command => command.GetString()!.Contains("--window 24h"));
        Assert.Contains(
            postRunCommands,
            command => command.GetString()!.Contains("--min-duration-ms 60000") &&
                       command.GetString()!.Contains("--sort duration"));
        Assert.Contains(
            root.GetProperty("evidence").EnumerateArray(),
            evidence => evidence.GetString() == "publish dry-run result");
        Assert.Contains(
            root.GetProperty("handoffNotes").EnumerateArray(),
            note => note.GetString()!.Contains("inferred project artifact", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Review_MarkdownOutput_PrintsHandoffNotes()
    {
        var path = Path.Combine(_root, "pre-issue.yml");
        WriteWorkflow(path, ValidWorkflowYaml());
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReviewAsync(path, null, "markdown", output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# Workflow Review: pre-issue", text);
        Assert.Contains("## Pre-run Handoff", text);
        Assert.Contains("revitcli workbench verify --output json", text);
        Assert.Contains("revitcli workbench handoff --output markdown", text);
        Assert.Contains("## Project Artifact Readiness", text);
        Assert.Contains("| `profile` | `missing` |", text);
        Assert.Contains("| `history` | `missing` |", text);
        Assert.Contains("## Recommended Commands", text);
        Assert.Contains("revitcli workflow run", text);
        Assert.Contains("--dry-run --output markdown", text);
        Assert.Contains("## Post-run Receipt Triage", text);
        Assert.Contains("revitcli workflow receipts --name \"pre-issue\" --failed-only --output markdown", text);
        Assert.Contains("revitcli workflow receipts --name \"pre-issue\" --min-duration-ms 60000 --sort duration --output markdown", text);
        Assert.Contains("## Evidence", text);
        Assert.Contains("publish dry-run result", text);
        Assert.Contains("## Handoff Notes", text);
    }

    [Fact]
    public async Task Review_WithBaseDirectory_CarriesDirIntoPreRunHandoffCommands()
    {
        var workflowDir = Path.Combine(_root, ".revitcli", "workflows");
        Directory.CreateDirectory(workflowDir);
        File.WriteAllText(Path.Combine(_root, ".revitcli.yml"), "project: test" + Environment.NewLine);
        WriteWorkflow(Path.Combine(workflowDir, "pre-issue.yml"), ValidWorkflowYaml());
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReviewAsync(
            ".revitcli/workflows/pre-issue.yml",
            _root,
            "json",
            output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var preRunCommands = json.RootElement.GetProperty("preRunHandoffCommands").EnumerateArray().ToArray();
        Assert.Contains(
            preRunCommands,
            command => command.GetString() == $"revitcli workbench verify --dir \"{Path.GetFullPath(_root)}\" --output json");
        Assert.Contains(
            preRunCommands,
            command => command.GetString() == $"revitcli workbench handoff --dir \"{Path.GetFullPath(_root)}\" --output markdown");
        Assert.Contains(
            preRunCommands,
            command => command.GetString() == $"revitcli inspect workflows --dir \"{Path.GetFullPath(_root)}\" --output markdown");

        var artifacts = json.RootElement.GetProperty("artifactReadiness").EnumerateArray().ToArray();
        Assert.Contains(artifacts, artifact =>
            artifact.GetProperty("name").GetString() == "profile" &&
            artifact.GetProperty("status").GetString() == "present" &&
            artifact.GetProperty("workingDirectory").GetString() == Path.GetFullPath(_root));
        Assert.Contains(artifacts, artifact =>
            artifact.GetProperty("name").GetString() == "history" &&
            artifact.GetProperty("reviewCommand").GetString()!.Contains(
                Path.Combine(Path.GetFullPath(_root), ".revitcli", "history"),
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Review_PlanStep_UsesInspectPlansReadinessCommand()
    {
        var workflowDir = Path.Combine(_root, ".revitcli", "workflows");
        Directory.CreateDirectory(workflowDir);
        WriteWorkflow(Path.Combine(workflowDir, "plan-apply.yml"), PlanWorkflowYaml());
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReviewAsync(
            ".revitcli/workflows/plan-apply.yml",
            _root,
            "json",
            output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var artifacts = json.RootElement.GetProperty("artifactReadiness").EnumerateArray().ToArray();
        Assert.Contains(artifacts, artifact =>
            artifact.GetProperty("name").GetString() == "plans" &&
            artifact.GetProperty("status").GetString() == "missing" &&
            artifact.GetProperty("relativePath").GetString() == ".revitcli/plans" &&
            artifact.GetProperty("reviewCommand").GetString() == $"revitcli inspect plans --dir \"{Path.GetFullPath(_root)}\" --output markdown" &&
            artifact.GetProperty("matchedSteps").EnumerateArray().Select(step => step.GetInt32()).SequenceEqual(new[] { 1, 2 }));
    }

    [Fact]
    public async Task Review_InvalidWorkflow_ReturnsFailureWithIssues()
    {
        var path = Path.Combine(_root, "bad.yml");
        WriteWorkflow(path, """
name: bad
steps:
  - run: revitcli made-up --dry-run
    mode: dry-run
""");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReviewAsync(path, null, "json", output);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("canRun").GetBoolean());
        Assert.Contains(
            root.GetProperty("issues").EnumerateArray(),
            issue => issue.GetProperty("message").GetString()!.Contains("unknown RevitCli command"));
    }

    [Fact]
    public async Task Review_UnknownOutput_ReturnsFailure()
    {
        var path = Path.Combine(_root, "pre-issue.yml");
        WriteWorkflow(path, ValidWorkflowYaml());
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReviewAsync(path, null, "yaml", output);

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Validate_ShellOperator_ReturnsFailure()
    {
        var path = Path.Combine(_root, "bad.yml");
        WriteWorkflow(path, """
name: bad
steps:
  - run: revitcli status && revitcli doctor
    mode: read-only
""");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteValidateAsync(path, null, "table", output);

        Assert.Equal(1, exitCode);
        Assert.Contains("shell operators", output.ToString());
    }

    [Fact]
    public async Task Suggest_RepeatedCommandSequence_PrintsWorkflowYaml()
    {
        WriteJournal(
            """{"command":"revitcli check issue","timestamp":"2026-05-01T10:00:00Z"}""",
            """{"command":"revitcli publish issue --dry-run","timestamp":"2026-05-01T10:01:00Z"}""",
            """{"command":"revitcli journal verify","timestamp":"2026-05-01T10:02:00Z"}""",
            """{"command":"revitcli check issue","timestamp":"2026-05-02T10:00:00Z"}""",
            """{"command":"revitcli publish issue --dry-run","timestamp":"2026-05-02T10:01:00Z"}""",
            """{"command":"revitcli journal verify","timestamp":"2026-05-02T10:02:00Z"}""");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteSuggestAsync(
            _root,
            journalPath: null,
            minCount: 2,
            maxSteps: 3,
            limit: 3,
            outputFormat: "yaml",
            output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("version: 1", text);
        Assert.Contains("name: suggested-workflow-1", text);
        Assert.Contains("run: 'revitcli check issue'", text);
        Assert.Contains("mode: dry-run", text);
        Assert.False(File.Exists(Path.Combine(_root, ".revitcli", "workflows", "suggested-workflow-1.yml")));
    }

    [Fact]
    public async Task Suggest_JsonOutput_MarksMutatingStepsForApproval()
    {
        WriteJournal(
            """{"command":"revitcli publish issue","timestamp":"2026-05-01T10:00:00Z"}""",
            """{"command":"revitcli journal verify","timestamp":"2026-05-01T10:01:00Z"}""",
            """{"command":"revitcli publish issue","timestamp":"2026-05-02T10:00:00Z"}""",
            """{"command":"revitcli journal verify","timestamp":"2026-05-02T10:01:00Z"}""");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteSuggestAsync(
            _root,
            journalPath: null,
            minCount: 2,
            maxSteps: 2,
            limit: 1,
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(4, json.RootElement.GetProperty("commandEntryCount").GetInt32());
        var suggestion = json.RootElement.GetProperty("suggestions").EnumerateArray().Single();
        var firstStep = suggestion.GetProperty("steps").EnumerateArray().First();
        Assert.Equal("mutating", firstStep.GetProperty("mode").GetString());
        Assert.True(firstStep.GetProperty("requiresApproval").GetBoolean());
    }

    [Fact]
    public async Task Suggest_NoExplicitCommandEntries_PrintsNoSuggestion()
    {
        WriteJournal(
            """{"action":"set","timestamp":"2026-05-01T10:00:00Z","param":"Mark"}""",
            """{"action":"publish","timestamp":"2026-05-01T10:01:00Z","pipeline":"issue"}""");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteSuggestAsync(
            _root,
            journalPath: null,
            minCount: 2,
            maxSteps: 3,
            limit: 3,
            outputFormat: "table",
            output);

        Assert.Equal(0, exitCode);
        Assert.Contains("No repeated journal command sequences found", output.ToString());
    }

    [Fact]
    public async Task Examples_Table_PrintsArchitectPromptsAndCommands()
    {
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteExamplesAsync(null, "table", output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Workflow acceptance examples", text);
        Assert.Contains("pre-issue", text);
        Assert.Contains("export-package", text);
        Assert.Contains("weekly-health", text);
        Assert.Contains("family-cleanup", text);
        Assert.Contains("帮我做出图前检查", text);
        Assert.Contains("revitcli inspect schedules --issues-only", text);
        Assert.Contains("revitcli workflow run .revitcli/workflows/pre-issue.yml --dry-run", text);
        Assert.Contains("revitcli family purge --dry-run --report .revitcli/reports/family-purge.json", text);
        Assert.Contains("Evidence:", text);
    }

    [Fact]
    public async Task Examples_Json_FiltersByWorkflowTemplate()
    {
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteExamplesAsync("export-package", "json", output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var example = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal("export-package", example.GetProperty("workflow").GetString());
        Assert.Contains(
            example.GetProperty("previewCommands").EnumerateArray(),
            command => command.GetString() == "revitcli inspect sheets --ready-only");
        Assert.Contains(
            example.GetProperty("approvalCommands").EnumerateArray(),
            command => command.GetString() == "revitcli deliverables bundle --dry-run --output markdown");
        Assert.Contains(
            example.GetProperty("approvalCommands").EnumerateArray(),
            command => command.GetString() == "revitcli workflow receipts --output markdown");
    }

    [Fact]
    public async Task Examples_Markdown_PrintsHandoffSections()
    {
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteExamplesAsync("weekly-health", "markdown", output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# Workflow Acceptance Examples", text);
        Assert.Contains("## weekly-health", text);
        Assert.Contains("Preview commands:", text);
        Assert.Contains("Approval commands:", text);
        Assert.Contains("revitcli journal review --output markdown", text);
    }

    [Fact]
    public async Task Examples_UnknownTemplate_ReturnsFailure()
    {
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteExamplesAsync("missing", "table", output);

        Assert.Equal(1, exitCode);
        Assert.Contains("unknown workflow example", output.ToString());
    }

    [Fact]
    public async Task Run_DryRun_PrintsPlanAndDoesNotInvokeRunner()
    {
        var path = Path.Combine(_root, "pre-issue.yml");
        WriteWorkflow(path, ValidWorkflowYaml());
        var output = new StringWriter();
        var invoked = false;

        var exitCode = await WorkflowCommand.ExecuteRunAsync(
            path,
            null,
            dryRun: true,
            yes: false,
            continueOnError: false,
            outputFormat: "table",
            output,
            runner: (_, _) =>
            {
                invoked = true;
                return Task.FromResult(0);
            });

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.False(invoked);
        Assert.Contains("Mode: dry-run", text);
        Assert.Contains("PLANNED [mutating]", text);
        Assert.Empty(WorkflowReceipts());
    }

    [Fact]
    public async Task Run_DryRunMarkdown_PrintsReviewableStepReport()
    {
        var path = Path.Combine(_root, "pre-issue.yml");
        WriteWorkflow(path, ValidWorkflowYaml());
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteRunAsync(
            path,
            null,
            dryRun: true,
            yes: false,
            continueOnError: false,
            outputFormat: "markdown",
            output,
            runner: (_, _) => Task.FromResult(0));

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# Workflow Run: pre-issue", text);
        Assert.Contains("- Mode: dry-run", text);
        Assert.Contains("## Steps", text);
        Assert.Contains("`PLANNED` `mutating`", text);
        Assert.Empty(WorkflowReceipts());
    }

    [Fact]
    public async Task Run_UnknownOutput_ReturnsFailureBeforeRunner()
    {
        var path = Path.Combine(_root, "pre-issue.yml");
        WriteWorkflow(path, ValidWorkflowYaml());
        var output = new StringWriter();
        var invoked = false;

        var exitCode = await WorkflowCommand.ExecuteRunAsync(
            path,
            null,
            dryRun: false,
            yes: true,
            continueOnError: false,
            outputFormat: "yaml",
            output,
            runner: (_, _) =>
            {
                invoked = true;
                return Task.FromResult(0);
            });

        Assert.Equal(1, exitCode);
        Assert.False(invoked);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, output.ToString());
        Assert.Empty(WorkflowReceipts());
    }

    [Fact]
    public async Task Run_NegativeTimeout_ReturnsFailureBeforeLoadingWorkflow()
    {
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteRunAsync(
            "/path/that/does/not/exist.yml",
            null,
            dryRun: false,
            yes: false,
            continueOnError: false,
            outputFormat: "table",
            output,
            timeoutMs: -1,
            runner: (_, _) => Task.FromResult(0));

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --timeout-ms must be at least 0." + Environment.NewLine, output.ToString());
        Assert.Empty(WorkflowReceipts());
    }

    [Fact]
    public async Task Run_TimeoutAboveMaximum_ReturnsFailureBeforeLoadingWorkflow()
    {
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteRunAsync(
            "/path/that/does/not/exist.yml",
            null,
            dryRun: false,
            yes: false,
            continueOnError: false,
            outputFormat: "table",
            output,
            timeoutMs: long.MaxValue,
            runner: (_, _) => Task.FromResult(0));

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --timeout-ms must be no more than 2147483647." + Environment.NewLine, output.ToString());
        Assert.Empty(WorkflowReceipts());
    }

    [Fact]
    public async Task Run_MutatingWithoutYes_ReturnsFailureBeforeRunner()
    {
        var path = Path.Combine(_root, "pre-issue.yml");
        WriteWorkflow(path, ValidWorkflowYaml());
        var output = new StringWriter();
        var invoked = false;

        var exitCode = await WorkflowCommand.ExecuteRunAsync(
            path,
            null,
            dryRun: false,
            yes: false,
            continueOnError: false,
            outputFormat: "table",
            output,
            runner: (_, _) =>
            {
                invoked = true;
                return Task.FromResult(0);
            });

        Assert.Equal(1, exitCode);
        Assert.False(invoked);
        Assert.Contains("run.--yes", output.ToString());
    }

    [Fact]
    public async Task Run_UnknownTopLevelCommand_ReturnsFailureBeforeRunner()
    {
        var path = Path.Combine(_root, "bad.yml");
        WriteWorkflow(path, """
name: bad
steps:
  - run: revitcli made-up --dry-run
    mode: dry-run
""");
        var output = new StringWriter();
        var invoked = false;

        var exitCode = await WorkflowCommand.ExecuteRunAsync(
            path,
            null,
            dryRun: false,
            yes: true,
            continueOnError: false,
            outputFormat: "table",
            output,
            runner: (_, _) =>
            {
                invoked = true;
                return Task.FromResult(0);
            });

        Assert.Equal(1, exitCode);
        Assert.False(invoked);
        Assert.Contains("unknown RevitCli command 'made-up'", output.ToString());
    }

    [Fact]
    public async Task Run_WithYes_InvokesStepsInOrder()
    {
        var path = Path.Combine(_root, "pre-issue.yml");
        WriteWorkflow(path, ValidWorkflowYaml());
        var output = new StringWriter();
        var invoked = new List<int>();

        var exitCode = await WorkflowCommand.ExecuteRunAsync(
            path,
            null,
            dryRun: false,
            yes: true,
            continueOnError: false,
            outputFormat: "table",
            output,
            runner: (step, _) =>
            {
                invoked.Add(step.Index);
                return Task.FromResult(0);
            });

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { 1, 2, 3 }, invoked);
        var text = output.ToString();
        Assert.Contains("OK [mutating]", text);
        Assert.Contains("Receipt:", text);

        var receiptPath = Assert.Single(WorkflowReceipts());
        using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
        var root = receipt.RootElement;
        Assert.Equal("workflow-run-receipt.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("workflow.run", root.GetProperty("action").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.False(root.GetProperty("dryRun").GetBoolean());
        Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
        Assert.True(root.GetProperty("durationMs").GetInt64() >= 0);
        Assert.Equal(Path.GetFullPath(receiptPath), root.GetProperty("receiptPath").GetString());
        Assert.Equal(Environment.UserName, root.GetProperty("operator").GetString());
        Assert.Equal(Environment.MachineName, root.GetProperty("machine").GetString());
        Assert.Contains("revitcli workflow run", root.GetProperty("command").GetString() ?? "");
        var steps = root.GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal(3, steps.Length);
        Assert.All(steps, step =>
        {
            Assert.True(step.TryGetProperty("startedAtUtc", out _));
            Assert.True(step.TryGetProperty("completedAtUtc", out _));
            Assert.True(step.GetProperty("durationMs").GetInt64() >= 0);
        });
    }

    [Fact]
    public async Task Receipts_Table_ListsWorkflowRunReceipts()
    {
        var path = Path.Combine(_root, "pre-issue.yml");
        WriteWorkflow(path, ValidWorkflowYaml());
        await WorkflowCommand.ExecuteRunAsync(
            path,
            null,
            dryRun: false,
            yes: true,
            continueOnError: false,
            outputFormat: "table",
            new StringWriter(),
            runner: (_, _) => Task.FromResult(0));
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReceiptsAsync(
            _root,
            limit: 10,
            failedOnly: false,
            nameFilter: null,
            outputFormat: "table",
            output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Workflow receipts", text);
        Assert.Contains("Receipts: 1 of 1", text);
        Assert.Contains("OK", text);
        Assert.Contains("pre-issue", text);
        Assert.Contains("steps=3", text);
        Assert.Contains("dur=", text);
    }

    [Fact]
    public async Task Receipts_Json_FiltersFailedRuns()
    {
        var successPath = Path.Combine(_root, "pre-issue.yml");
        WriteWorkflow(successPath, ValidWorkflowYaml());
        await WorkflowCommand.ExecuteRunAsync(
            successPath,
            null,
            dryRun: false,
            yes: true,
            continueOnError: false,
            outputFormat: "table",
            new StringWriter(),
            runner: (_, _) => Task.FromResult(0));

        var failedPath = Path.Combine(_root, "readonly.yml");
        WriteWorkflow(failedPath, ReadOnlyWorkflowYaml());
        await WorkflowCommand.ExecuteRunAsync(
            failedPath,
            null,
            dryRun: false,
            yes: false,
            continueOnError: false,
            outputFormat: "table",
            new StringWriter(),
            runner: (step, _) => Task.FromResult(step.Index == 2 ? 7 : 0));
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReceiptsAsync(
            _root,
            limit: 10,
            failedOnly: true,
            nameFilter: null,
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("workflow-receipts.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("failedOnly").GetBoolean());
        Assert.Equal(1, root.GetProperty("receiptCount").GetInt32());
        var receipt = Assert.Single(root.GetProperty("receipts").EnumerateArray());
        Assert.Equal("readonly", receipt.GetProperty("name").GetString());
        Assert.False(receipt.GetProperty("success").GetBoolean());
        Assert.Equal(7, receipt.GetProperty("exitCode").GetInt32());
        Assert.True(receipt.GetProperty("durationMs").GetInt64() >= 0);
        Assert.Equal(1, receipt.GetProperty("failedStepCount").GetInt32());
    }

    [Fact]
    public async Task Receipts_Json_FiltersByWorkflowName()
    {
        var preIssuePath = Path.Combine(_root, "pre-issue.yml");
        WriteWorkflow(preIssuePath, ValidWorkflowYaml());
        await WorkflowCommand.ExecuteRunAsync(
            preIssuePath,
            null,
            dryRun: false,
            yes: true,
            continueOnError: false,
            outputFormat: "table",
            new StringWriter(),
            runner: (_, _) => Task.FromResult(0));

        var readonlyPath = Path.Combine(_root, "readonly.yml");
        WriteWorkflow(readonlyPath, ReadOnlyWorkflowYaml());
        await WorkflowCommand.ExecuteRunAsync(
            readonlyPath,
            null,
            dryRun: false,
            yes: true,
            continueOnError: false,
            outputFormat: "table",
            new StringWriter(),
            runner: (_, _) => Task.FromResult(0));
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReceiptsAsync(
            _root,
            limit: 10,
            failedOnly: false,
            nameFilter: "pre-issue",
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("workflow-receipts.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("pre-issue", root.GetProperty("nameFilter").GetString());
        Assert.Equal(1, root.GetProperty("receiptCount").GetInt32());
        var receipt = Assert.Single(root.GetProperty("receipts").EnumerateArray());
        Assert.Equal("pre-issue", receipt.GetProperty("name").GetString());
        Assert.True(receipt.GetProperty("durationMs").GetInt64() >= 0);
    }

    [Fact]
    public async Task Receipts_Table_ShowsWorkflowNameFilter()
    {
        var preIssuePath = Path.Combine(_root, "pre-issue.yml");
        WriteWorkflow(preIssuePath, ValidWorkflowYaml());
        await WorkflowCommand.ExecuteRunAsync(
            preIssuePath,
            null,
            dryRun: false,
            yes: true,
            continueOnError: false,
            outputFormat: "table",
            new StringWriter(),
            runner: (_, _) => Task.FromResult(0));

        var readonlyPath = Path.Combine(_root, "readonly.yml");
        WriteWorkflow(readonlyPath, ReadOnlyWorkflowYaml());
        await WorkflowCommand.ExecuteRunAsync(
            readonlyPath,
            null,
            dryRun: false,
            yes: true,
            continueOnError: false,
            outputFormat: "table",
            new StringWriter(),
            runner: (_, _) => Task.FromResult(0));
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReceiptsAsync(
            _root,
            limit: 10,
            failedOnly: false,
            nameFilter: "pre-issue",
            outputFormat: "table",
            output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Workflow: pre-issue", text);
        Assert.Contains("Receipts: 1 of 1", text);
        Assert.Contains("pre-issue", text);
        Assert.DoesNotContain("readonly", text);
    }

    [Fact]
    public async Task Receipts_Json_FiltersByMinimumDuration()
    {
        WriteWorkflowReceipt("fast", durationMs: 50);
        WriteWorkflowReceipt("slow", durationMs: 5_000);
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReceiptsAsync(
            _root,
            limit: 10,
            failedOnly: false,
            nameFilter: null,
            outputFormat: "json",
            output,
            minDurationMs: 1_000);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("workflow-receipts.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(1_000, root.GetProperty("minDurationMs").GetInt64());
        Assert.Equal(1, root.GetProperty("receiptCount").GetInt32());
        var receipt = Assert.Single(root.GetProperty("receipts").EnumerateArray());
        Assert.Equal("slow", receipt.GetProperty("name").GetString());
        Assert.Equal(5_000, receipt.GetProperty("durationMs").GetInt64());
    }

    [Fact]
    public async Task Receipts_Table_ShowsMinimumDurationFilter()
    {
        WriteWorkflowReceipt("fast", durationMs: 50);
        WriteWorkflowReceipt("slow", durationMs: 5_000);
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReceiptsAsync(
            _root,
            limit: 10,
            failedOnly: false,
            nameFilter: null,
            outputFormat: "table",
            output,
            minDurationMs: 1_000);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Min duration: 1000ms", text);
        Assert.Contains("Sort: completed", text);
        Assert.Contains("slow", text);
        Assert.DoesNotContain("fast", text);
    }

    [Fact]
    public async Task Receipts_Json_SortsByDurationDescending()
    {
        WriteWorkflowReceipt("fast", durationMs: 50);
        WriteWorkflowReceipt("medium", durationMs: 2_000);
        WriteWorkflowReceipt("slow", durationMs: 5_000);
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReceiptsAsync(
            _root,
            limit: 10,
            failedOnly: false,
            nameFilter: null,
            outputFormat: "json",
            output,
            sort: "duration");

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("duration", root.GetProperty("sort").GetString());
        var receipts = root.GetProperty("receipts").EnumerateArray().ToArray();
        Assert.Equal(new[] { "slow", "medium", "fast" }, receipts.Select(receipt =>
            receipt.GetProperty("name").GetString()).ToArray());
    }

    [Fact]
    public async Task Receipts_Json_FiltersByRecentWindow()
    {
        WriteWorkflowReceipt(
            "old",
            durationMs: 500,
            completedAtUtc: "2026-05-17T00:00:00Z");
        WriteWorkflowReceipt(
            "recent",
            durationMs: 1_000,
            completedAtUtc: "2026-05-18T11:30:00Z");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReceiptsAsync(
            _root,
            limit: 10,
            failedOnly: false,
            nameFilter: null,
            outputFormat: "json",
            output,
            window: "2h",
            nowUtc: DateTimeOffset.Parse("2026-05-18T12:00:00Z"));

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("2h", root.GetProperty("window").GetString());
        Assert.Equal("2026-05-18T10:00:00.0000000+00:00", root.GetProperty("sinceUtc").GetString());
        Assert.Equal(1, root.GetProperty("receiptCount").GetInt32());
        var receipt = Assert.Single(root.GetProperty("receipts").EnumerateArray());
        Assert.Equal("recent", receipt.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Receipts_Table_ShowsWindowFilter()
    {
        WriteWorkflowReceipt(
            "recent",
            durationMs: 1_000,
            completedAtUtc: "2026-05-18T11:30:00Z");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReceiptsAsync(
            _root,
            limit: 10,
            failedOnly: false,
            nameFilter: null,
            outputFormat: "table",
            output,
            window: "2h",
            nowUtc: DateTimeOffset.Parse("2026-05-18T12:00:00Z"));

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Window: 2h since 2026-05-18T10:00:00.0000000+00:00", text);
        Assert.Contains("recent", text);
    }

    [Fact]
    public async Task Receipts_NegativeMinimumDurationReturnsFailure()
    {
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReceiptsAsync(
            _root,
            limit: 10,
            failedOnly: false,
            nameFilter: null,
            outputFormat: "table",
            output,
            minDurationMs: -1);

        Assert.Equal(1, exitCode);
        Assert.Contains("--min-duration-ms", output.ToString());
    }

    [Fact]
    public async Task Receipts_UnknownSortReturnsFailure()
    {
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReceiptsAsync(
            _root,
            limit: 10,
            failedOnly: false,
            nameFilter: null,
            outputFormat: "table",
            output,
            sort: "started");

        Assert.Equal(1, exitCode);
        Assert.Contains("--sort", output.ToString());
    }

    [Fact]
    public async Task Receipts_InvalidWindowReturnsFailure()
    {
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReceiptsAsync(
            _root,
            limit: 10,
            failedOnly: false,
            nameFilter: null,
            outputFormat: "table",
            output,
            window: "forever");

        Assert.Equal(1, exitCode);
        Assert.Contains("Invalid window", output.ToString());
    }

    [Fact]
    public async Task Receipts_Markdown_InvalidReceiptReturnsFailure()
    {
        var receiptDir = Path.Combine(_root, ".revitcli", "workflows", "receipts");
        Directory.CreateDirectory(receiptDir);
        File.WriteAllText(Path.Combine(receiptDir, "bad.json"), "{not json");
        var output = new StringWriter();

        var exitCode = await WorkflowCommand.ExecuteReceiptsAsync(
            _root,
            limit: 10,
            failedOnly: false,
            nameFilter: null,
            outputFormat: "markdown",
            output);

        var text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("# Workflow Receipts", text);
        Assert.Contains("- Status: `FAIL`", text);
        Assert.Contains("## Issues", text);
        Assert.Contains("`ERROR`", text);
        Assert.Contains("not readable JSON", text);
    }

    [Fact]
    public async Task Run_StopsOnFailureByDefault()
    {
        var path = Path.Combine(_root, "readonly.yml");
        WriteWorkflow(path, ReadOnlyWorkflowYaml());
        var output = new StringWriter();
        var invoked = new List<int>();

        var exitCode = await WorkflowCommand.ExecuteRunAsync(
            path,
            null,
            dryRun: false,
            yes: false,
            continueOnError: false,
            outputFormat: "table",
            output,
            runner: (step, _) =>
            {
                invoked.Add(step.Index);
                return Task.FromResult(step.Index == 2 ? 7 : 0);
            });

        var text = output.ToString();
        Assert.Equal(7, exitCode);
        Assert.Equal(new[] { 1, 2 }, invoked);
        Assert.Contains("FAILED [read-only]", text);
        Assert.Contains("SKIPPED [read-only]", text);

        var receiptPath = Assert.Single(WorkflowReceipts());
        using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
        Assert.False(receipt.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(7, receipt.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Contains(
            receipt.RootElement.GetProperty("steps").EnumerateArray(),
            step => step.GetProperty("status").GetString() == "skipped");
    }

    [Fact]
    public async Task Run_TimeoutMarksStepAndSkipsRemainingSteps()
    {
        var path = Path.Combine(_root, "readonly.yml");
        WriteWorkflow(path, ReadOnlyWorkflowYaml());
        var output = new StringWriter();
        var invoked = new List<int>();

        var exitCode = await WorkflowCommand.ExecuteRunAsync(
            path,
            null,
            dryRun: false,
            yes: false,
            continueOnError: false,
            outputFormat: "json",
            output,
            timeoutMs: 1,
            runner: async (step, _) =>
            {
                invoked.Add(step.Index);
                await Task.Delay(50);
                return 0;
            });

        Assert.Equal(124, exitCode);
        Assert.Equal(new[] { 1 }, invoked);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(124, root.GetProperty("exitCode").GetInt32());
        Assert.Equal(1, root.GetProperty("timeoutMs").GetInt64());
        var steps = root.GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal("timed-out", steps[0].GetProperty("status").GetString());
        Assert.True(steps[0].GetProperty("timedOut").GetBoolean());
        Assert.Equal(124, steps[0].GetProperty("exitCode").GetInt32());
        Assert.Contains(steps.Skip(1), step => step.GetProperty("status").GetString() == "skipped");
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("path").GetString() == "steps[1].timeout");

        var receiptPath = Assert.Single(WorkflowReceipts());
        using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
        Assert.Equal(1, receipt.RootElement.GetProperty("timeoutMs").GetInt64());
        Assert.Contains(
            receipt.RootElement.GetProperty("steps").EnumerateArray(),
            step => step.GetProperty("status").GetString() == "timed-out");
    }

    [Fact]
    public async Task Run_ContinueOnError_InvokesRemainingSteps()
    {
        var path = Path.Combine(_root, "readonly.yml");
        WriteWorkflow(path, ReadOnlyWorkflowYaml());
        var output = new StringWriter();
        var invoked = new List<int>();

        var exitCode = await WorkflowCommand.ExecuteRunAsync(
            path,
            null,
            dryRun: false,
            yes: false,
            continueOnError: true,
            outputFormat: "json",
            output,
            runner: (step, _) =>
            {
                invoked.Add(step.Index);
                return Task.FromResult(step.Index == 2 ? 7 : 0);
            });

        Assert.Equal(7, exitCode);
        Assert.Equal(new[] { 1, 2, 3 }, invoked);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(7, json.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Contains(
            json.RootElement.GetProperty("steps").EnumerateArray(),
            step => step.GetProperty("status").GetString() == "failed");
    }

    [Fact]
    public async Task RunProcess_DrainsStdoutAndStderrConcurrently()
    {
        var command = CreateStdioPressureCommand();
        var step = new WorkflowStepSimulation(
            1,
            "stdio pressure",
            "read-only",
            command,
            RequiresApproval: false);
        var output = new StringWriter();

        var runTask = WorkflowCommand.RunProcessAsync(step, output);
        var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(runTask, completed);
        Assert.Equal(0, await runTask);
        var text = output.ToString();
        Assert.Contains("stdout-ready", text);
        Assert.Contains("stderr-ready", text);
    }

    private static void WriteWorkflow(string path, string yaml)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, yaml);
    }

    private string CreateStdioPressureCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(_root, "stdio-pressure.cmd");
            File.WriteAllText(path, """
@echo off
echo stdout-ready
for /L %%i in (1,1,12000) do echo stderr-line-%%i 1>&2
echo stderr-ready 1>&2
""");
            return $"cmd /d /c {QuoteCommandArgument(path)}";
        }

        var scriptPath = Path.Combine(_root, "stdio-pressure.sh");
        File.WriteAllText(scriptPath, """
#!/bin/sh
printf '%s\n' stdout-ready
i=0
while [ "$i" -lt 12000 ]; do
  i=$((i + 1))
  printf 'stderr-line-%s\n' "$i" >&2
done
printf '%s\n' stderr-ready >&2
""");
        return $"/bin/sh {QuoteCommandArgument(scriptPath)}";
    }

    private static string QuoteCommandArgument(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private void WriteJournal(params string[] lines)
    {
        var journalDir = Path.Combine(_root, ".revitcli");
        Directory.CreateDirectory(journalDir);
        File.WriteAllLines(Path.Combine(journalDir, "journal.jsonl"), lines);
    }

    private void WriteWorkflowReceipt(
        string name,
        long durationMs,
        string completedAtUtc = "2026-05-18T00:00:05Z")
    {
        var receiptDir = Path.Combine(_root, ".revitcli", "workflows", "receipts");
        Directory.CreateDirectory(receiptDir);
        var receiptPath = Path.Combine(receiptDir, $"{name}.json");
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(new
        {
            schemaVersion = "workflow-run-receipt.v1",
            action = "workflow.run",
            path = Path.Combine(_root, $"{name}.yml"),
            name,
            command = $"revitcli workflow run {name}.yml",
            startedAtUtc = "2026-05-18T00:00:00Z",
            completedAtUtc,
            durationMs,
            dryRun = false,
            success = true,
            canRun = true,
            exitCode = 0,
            issues = Array.Empty<object>(),
            steps = Array.Empty<object>()
        }));
    }

    private string[] WorkflowReceipts()
    {
        var receiptDir = Path.Combine(_root, ".revitcli", "workflows", "receipts");
        return Directory.Exists(receiptDir)
            ? Directory.GetFiles(receiptDir, "*.json")
            : Array.Empty<string>();
    }

    private static string ValidWorkflowYaml() =>
        """
name: pre-issue
description: Pre-issue dry-run checklist
steps:
  - name: simulate profile
    run: revitcli profile simulate issue
    mode: read-only
  - name: dry-run publish
    run: revitcli publish issue --dry-run
    mode: dry-run
  - name: capture history
    run: revitcli history capture --source pre-issue
    mode: mutating
    requiresApproval: true
""";

    private static string PlanWorkflowYaml() =>
        """
name: plan-apply
steps:
  - name: preview plan
    run: revitcli plan apply .revitcli/plans/doors.json --dry-run
    mode: dry-run
  - name: apply plan
    run: revitcli plan apply .revitcli/plans/doors.json --yes
    mode: mutating
    requiresApproval: true
""";

    private static string ReadOnlyWorkflowYaml() =>
        """
name: readonly
steps:
  - run: revitcli status
    mode: read-only
  - run: revitcli doctor
    mode: read-only
  - run: revitcli examples workflow
    mode: read-only
""";
}
