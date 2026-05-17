using System.Text.Json;
using RevitCli.Commands;

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
        Assert.Equal(Path.GetFullPath(receiptPath), root.GetProperty("receiptPath").GetString());
        Assert.Equal(Environment.UserName, root.GetProperty("operator").GetString());
        Assert.Equal(Environment.MachineName, root.GetProperty("machine").GetString());
        Assert.Contains("revitcli workflow run", root.GetProperty("command").GetString() ?? "");
        Assert.Equal(3, root.GetProperty("steps").GetArrayLength());
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
            outputFormat: "table",
            output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Workflow receipts", text);
        Assert.Contains("Receipts: 1 of 1", text);
        Assert.Contains("OK", text);
        Assert.Contains("pre-issue", text);
        Assert.Contains("steps=3", text);
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
        Assert.Equal(1, receipt.GetProperty("failedStepCount").GetInt32());
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

    private static void WriteWorkflow(string path, string yaml)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, yaml);
    }

    private void WriteJournal(params string[] lines)
    {
        var journalDir = Path.Combine(_root, ".revitcli");
        Directory.CreateDirectory(journalDir);
        File.WriteAllLines(Path.Combine(journalDir, "journal.jsonl"), lines);
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
