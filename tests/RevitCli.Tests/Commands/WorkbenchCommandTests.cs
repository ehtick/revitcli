using System.Text.Json;
using RevitCli.Commands;

namespace RevitCli.Tests.Commands;

public sealed class WorkbenchCommandTests
{
    [Fact]
    public async Task Contract_Json_PrintsStableEnvelopeForCodexCli()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteContractAsync(output, "json");

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal("workbench-contract.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("RevitCli Architect Terminal BIM Workbench", root.GetProperty("product").GetString());
        Assert.True(root.TryGetProperty("generatedAt", out _));

        var commands = root.GetProperty("commands").EnumerateArray().ToArray();
        Assert.Contains(commands, command =>
            command.GetProperty("name").GetString() == "publish" &&
            command.GetProperty("supportsJson").GetBoolean() &&
            command.GetProperty("dryRun").GetString()!.Contains("required", StringComparison.OrdinalIgnoreCase) &&
            command.GetProperty("receipt").GetString()!.Contains(".revitcli/receipts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, command =>
            command.GetProperty("name").GetString() == "plan" &&
            command.GetProperty("supportsMarkdown").GetBoolean() &&
            command.GetProperty("receipt").GetString()!.Contains("plan-receipt.v1", StringComparison.OrdinalIgnoreCase) &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "plan apply"));
        Assert.Contains(commands, command =>
            command.GetProperty("name").GetString() == "workbench" &&
            command.GetProperty("supportsJson").GetBoolean() &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench verify") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench receipts") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench paths") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench exits") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench extensions") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench outputs") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench safeguards") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench project") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench handoff"));
        Assert.Contains(commands, command =>
            command.GetProperty("name").GetString() == "inspect" &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "inspect plans"));
        Assert.Contains(commands, command =>
            command.GetProperty("name").GetString() == "journal" &&
            command.GetProperty("recommendedFirstCommand").GetString() == "revitcli journal review --output json");
        Assert.Contains(commands, command =>
            command.GetProperty("name").GetString() == "examples" &&
            command.GetProperty("supportsJson").GetBoolean() &&
            command.GetProperty("recommendedFirstCommand").GetString() == "revitcli examples workflow --output json");
        Assert.Contains(commands, command =>
            command.GetProperty("name").GetString() == "score" &&
            command.GetProperty("supportsJson").GetBoolean() &&
            command.GetProperty("supportsMarkdown").GetBoolean() &&
            command.GetProperty("recommendedFirstCommand").GetString() == "revitcli score --history 30d --output json" &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "score --history"));
        var schedule = commands.Single(command => command.GetProperty("name").GetString() == "schedule");
        Assert.Equal("mixed", schedule.GetProperty("risk").GetString());
        Assert.Contains(
            schedule.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "schedule create");
        Assert.Contains("schedule-create", schedule.GetProperty("receipt").GetString()!);
    }

    [Fact]
    public async Task Contract_Table_PrintsReadableTerminalSummary()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteContractAsync(output, "table");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("RevitCli workbench contract (workbench-contract.v1)", text);
        Assert.Contains("Command      Risk         Paths", text);
        Assert.Contains("publish", text);
        Assert.Contains("Recommended first commands:", text);
        Assert.Contains("revitcli publish issue --dry-run --output json", text);
        Assert.Contains("revitcli report knowledge --output json", text);
    }

    [Fact]
    public async Task Contract_Markdown_PrintsHandoffTable()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteContractAsync(output, "markdown");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# RevitCli Workbench Contract", text);
        Assert.Contains("Schema: `workbench-contract.v1`", text);
        Assert.Contains("| Command | Risk | JSON | Markdown | Dry-run | Receipt | First command |", text);
        Assert.Contains("| `deliverables` | local-write | yes | yes | available for bundles | delivery-bundle-receipt.v1 sidecars |", text);
        Assert.Contains("`revitcli workflow review .revitcli/workflows/pre-issue.yml --output json`", text);
        Assert.Contains("## Callable Command Paths", text);
        Assert.Contains("- `revitcli workbench verify`", text);
        Assert.Contains("- `revitcli workbench receipts`", text);
        Assert.Contains("- `revitcli workbench paths`", text);
        Assert.Contains("- `revitcli workbench exits`", text);
        Assert.Contains("- `revitcli workbench extensions`", text);
        Assert.Contains("- `revitcli workbench outputs`", text);
        Assert.Contains("- `revitcli workbench safeguards`", text);
        Assert.Contains("- `revitcli workbench project`", text);
        Assert.Contains("- `revitcli workbench handoff`", text);
        Assert.Contains("- `revitcli workflow review`", text);
    }

    [Fact]
    public async Task Contract_UnknownOutput_ReturnsFailureBeforeWritingContract()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteContractAsync(output, "yaml");

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Verify_Json_PrintsPassingVerificationEnvelope()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(output, "json");

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal("workbench-verification.v1", root.GetProperty("schemaVersion").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("projectDirectory").GetString()));
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(0, root.GetProperty("issueCount").GetInt32());
        Assert.True(root.GetProperty("commandCount").GetInt32() >= 20);
        Assert.True(root.GetProperty("recipeTopicCount").GetInt32() >= 10);

        var checks = root.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "contract-root-alignment" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "callable-command-paths" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "manual-only-path-exclusion" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "llm-runtime-exclusion" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "legacy-mcp-hidden" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("hidden", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("deprecated", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "dashboard-dependency-exclusion" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "cloud-sync-exclusion" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "receipt-index-surface" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "extension-point-surface" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "output-contract-surface" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "completion-surface" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("inspect", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("output-format contracts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "safeguard-surface" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "schedule-create-safety" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("dry-run", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "project-inventory-surface" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "handoff-readiness-actions" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("next actions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "handoff-command-surface" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("plan-discovery", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("schedule-create", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "workflow-duration-telemetry" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "workflow-receipt-triage" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("recent-window", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "workflow-discovery-surface" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("workflow YAML", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "plan-discovery-surface" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("saved mutation plan", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "workflow-template-surface" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("pre-issue", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("family-cleanup", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "workflow-review-handoff" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("pre-run workbench", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("artifact readiness", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("post-run receipt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "example-recipe-surface" &&
            check.GetProperty("evidence").GetString()!.Contains("JSON/Markdown", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "model-health-terminal-surface" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("model-health-history.v1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "risky-command-safety" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "exit-code-index-surface" &&
            check.GetProperty("status").GetString() == "pass");
    }

    [Fact]
    public async Task Verify_Json_UsesProvidedProjectDirectoryForReadiness()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-verify-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(output, "json", root);

            Assert.Equal(0, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal(Path.GetFullPath(root), document.RootElement.GetProperty("projectDirectory").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Table_PrintsReadableCheckSummary()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(output, "table");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("RevitCli workbench verification (workbench-verification.v1)", text);
        Assert.Contains("Project:", text);
        Assert.Contains("Success: yes", text);
        Assert.Contains("contract-root-alignment", text);
        Assert.Contains("completion-surface", text);
        Assert.Contains("handoff-command-surface", text);
        Assert.Contains("mcp-public-exclusion", text);
    }

    [Fact]
    public async Task Verify_Markdown_PrintsHandoffCheckTable()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(output, "markdown");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# RevitCli Workbench Verification", text);
        Assert.Contains("Schema: `workbench-verification.v1`", text);
        Assert.Contains("Project: `", text);
        Assert.Contains("| Status | Check | Evidence |", text);
        Assert.Contains("| `pass` | `core-command-contract` |", text);
        Assert.Contains("| `pass` | `completion-surface` |", text);
        Assert.Contains("| `pass` | `handoff-command-surface` |", text);
    }

    [Fact]
    public async Task Verify_UnknownOutput_ReturnsFailureBeforeWritingVerification()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(output, "yaml");

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Verify_MissingDirectory_ReturnsFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-missing-{Guid.NewGuid():N}");
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(output, "json", root);

        Assert.Equal(1, exitCode);
        Assert.Contains("Error: project directory not found:", output.ToString());
        Assert.Contains(Path.GetFullPath(root), output.ToString());
    }

    [Fact]
    public async Task Receipts_Json_PrintsStableReceiptIndexForCodexCli()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteReceiptsAsync(output, "json");

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal("workbench-receipts.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("RevitCli Architect Terminal BIM Workbench", root.GetProperty("product").GetString());
        Assert.True(root.GetProperty("receiptCount").GetInt32() >= 5);

        var receipts = root.GetProperty("receipts").EnumerateArray().ToArray();
        Assert.Contains(receipts, receipt =>
            receipt.GetProperty("schemaVersion").GetString() == "export-receipt.v1" &&
            receipt.GetProperty("pathPattern").GetString() == "<outputDir>/.revitcli/receipts/export-*.json" &&
            receipt.GetProperty("dryRunCommand").GetString() == "revitcli export --format pdf --sheets \"A1*\" --dry-run --output json");
        Assert.Contains(receipts, receipt =>
            receipt.GetProperty("schemaVersion").GetString() == "workflow-run-receipt.v1" &&
            receipt.GetProperty("writesOn").GetString()!.Contains("durations", StringComparison.OrdinalIgnoreCase) &&
            receipt.GetProperty("reviewCommand").GetString() == "revitcli workflow receipts --output json");
        Assert.Contains(receipts, receipt =>
            receipt.GetProperty("schemaVersion").GetString() == "delivery-bundle-receipt.v1" &&
            receipt.GetProperty("pathPattern").GetString() == "<bundle-path>.receipt.json");
        Assert.Contains(receipts, receipt =>
            receipt.GetProperty("schemaVersion").GetString() == "schedule-create-receipt.v1" &&
            receipt.GetProperty("commandPath").GetString() == "revitcli schedule create" &&
            receipt.GetProperty("dryRunCommand").GetString()!.Contains("--dry-run", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Receipts_Table_PrintsReadableTerminalIndex()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteReceiptsAsync(output, "table");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("RevitCli workbench receipts (workbench-receipts.v1)", text);
        Assert.Contains("export-receipt.v1", text);
        Assert.Contains("workflow-run-receipt.v1", text);
        Assert.Contains("Dry-run and review commands:", text);
        Assert.Contains("revitcli deliverables verify --output json", text);
    }

    [Fact]
    public async Task Receipts_Markdown_PrintsHandoffTable()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteReceiptsAsync(output, "markdown");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# RevitCli Workbench Receipts", text);
        Assert.Contains("Schema: `workbench-receipts.v1`", text);
        Assert.Contains("| Schema | Action | Command | Writes on | Path pattern | Dry-run | Review |", text);
        Assert.Contains("| `plan-receipt.v1` | `plan.apply` | `revitcli plan apply` |", text);
        Assert.Contains("`revitcli rollback <plan-file>.receipt.json --dry-run`", text);
    }

    [Fact]
    public async Task Receipts_UnknownOutput_ReturnsFailureBeforeWritingIndex()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteReceiptsAsync(output, "yaml");

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Paths_Json_PrintsFlatCallablePathIndexForCodexCli()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecutePathsAsync(output, "json");

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal("workbench-paths.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("pathCount").GetInt32() >= 40);

        var paths = root.GetProperty("paths").EnumerateArray().ToArray();
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "workflow review" &&
            path.GetProperty("commandLine").GetString() == "revitcli workflow review" &&
            path.GetProperty("supportsJson").GetBoolean() &&
            path.GetProperty("receipt").GetString()!.Contains(".revitcli/workflows/receipts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "inspect workflows" &&
            path.GetProperty("commandLine").GetString() == "revitcli inspect workflows" &&
            path.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "inspect plans" &&
            path.GetProperty("commandLine").GetString() == "revitcli inspect plans" &&
            path.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "workbench receipts" &&
            path.GetProperty("command").GetString() == "workbench" &&
            path.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "workbench exits" &&
            path.GetProperty("command").GetString() == "workbench" &&
            path.GetProperty("exitCodeNotes").GetString()!.Contains("contract verification", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "workbench extensions" &&
            path.GetProperty("command").GetString() == "workbench" &&
            path.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "workbench outputs" &&
            path.GetProperty("command").GetString() == "workbench" &&
            path.GetProperty("supportsJson").GetBoolean());
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "workbench safeguards" &&
            path.GetProperty("command").GetString() == "workbench" &&
            path.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "workbench project" &&
            path.GetProperty("command").GetString() == "workbench" &&
            path.GetProperty("supportsJson").GetBoolean());
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "workbench handoff" &&
            path.GetProperty("command").GetString() == "workbench" &&
            path.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "plan apply" &&
            path.GetProperty("dryRun").GetString()!.Contains("required", StringComparison.OrdinalIgnoreCase) &&
            path.GetProperty("exitCodeNotes").GetString()!.Contains("validation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "score --history" &&
            path.GetProperty("command").GetString() == "score" &&
            path.GetProperty("supportsJson").GetBoolean() &&
            path.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "schedule create" &&
            path.GetProperty("command").GetString() == "schedule" &&
            path.GetProperty("dryRun").GetString()!.Contains("required", StringComparison.OrdinalIgnoreCase) &&
            path.GetProperty("receipt").GetString()!.Contains("schedule-create", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Paths_Table_PrintsReadableCallablePathIndex()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecutePathsAsync(output, "table");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("RevitCli workbench paths (workbench-paths.v1)", text);
        Assert.Contains("workflow review", text);
        Assert.Contains("workbench receipts", text);
        Assert.Contains("workbench exits", text);
        Assert.Contains("workbench extensions", text);
        Assert.Contains("workbench outputs", text);
        Assert.Contains("workbench safeguards", text);
        Assert.Contains("workbench project", text);
        Assert.Contains("workbench handoff", text);
        Assert.Contains("deliverables bundle", text);
    }

    [Fact]
    public async Task Paths_Markdown_PrintsHandoffTable()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecutePathsAsync(output, "markdown");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# RevitCli Workbench Paths", text);
        Assert.Contains("Schema: `workbench-paths.v1`", text);
        Assert.Contains("| Path | Risk | JSON | Markdown | Dry-run | Receipt | Exit notes |", text);
        Assert.Contains("| `revitcli workbench paths` | read-only | yes | yes |", text);
        Assert.Contains("| `revitcli plan apply` | write | yes | yes |", text);
    }

    [Fact]
    public async Task Exits_Json_PrintsStableExitCodeIndex()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteExitsAsync(output, "json");

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal("workbench-exit-codes.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("commandCount").GetInt32() >= 20);

        var commands = root.GetProperty("commands").EnumerateArray().ToArray();
        Assert.Contains(commands, command =>
            command.GetProperty("command").GetString() == "publish" &&
            command.GetProperty("successExitCodes").EnumerateArray().Any(code => code.GetString() == "0") &&
            command.GetProperty("notes").GetString()!.Contains("dry-run/publish", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, command =>
            command.GetProperty("command").GetString() == "score" &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "score --history"));
    }

    [Fact]
    public async Task Exits_Table_PrintsReadableExitCodeIndex()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteExitsAsync(output, "table");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("RevitCli workbench exit codes (workbench-exit-codes.v1)", text);
        Assert.Contains("publish", text);
        Assert.Contains("score", text);
    }

    [Fact]
    public async Task Exits_Markdown_PrintsHandoffTable()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteExitsAsync(output, "markdown");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# RevitCli Workbench Exit Codes", text);
        Assert.Contains("Schema: `workbench-exit-codes.v1`", text);
        Assert.Contains("| Command | Success | Failure | First command | Notes |", text);
        Assert.Contains("| `score` | `0` | `1, non-zero` | `revitcli score --history 30d --output json` |", text);
    }

    [Fact]
    public async Task Exits_UnknownOutput_ReturnsFailureBeforeWritingIndex()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteExitsAsync(output, "yaml");

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Extensions_Json_PrintsStableExtensionIndex()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteExtensionsAsync(output, "json");

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal("workbench-extensions.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("extensionCount").GetInt32() >= 5);

        var extensions = root.GetProperty("extensions").EnumerateArray().ToArray();
        Assert.Contains(extensions, extension =>
            extension.GetProperty("name").GetString() == "workflow-yaml" &&
            extension.GetProperty("validationCommand").GetString()!.Contains("workflow validate", StringComparison.OrdinalIgnoreCase) &&
            extension.GetProperty("dryRunCommand").GetString()!.Contains("workflow simulate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(extensions, extension =>
            extension.GetProperty("name").GetString() == "standards-pack" &&
            extension.GetProperty("dryRunCommand").GetString()!.Contains("--dry-run", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Extensions_Table_PrintsReadableExtensionIndex()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteExtensionsAsync(output, "table");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("RevitCli workbench extensions (workbench-extensions.v1)", text);
        Assert.Contains("workflow-yaml", text);
        Assert.Contains("Dry-run or preview commands:", text);
    }

    [Fact]
    public async Task Extensions_Markdown_PrintsHandoffTable()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteExtensionsAsync(output, "markdown");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# RevitCli Workbench Extensions", text);
        Assert.Contains("Schema: `workbench-extensions.v1`", text);
        Assert.Contains("| Extension | File pattern | Validation | Dry-run / preview | Write behavior | Notes |", text);
        Assert.Contains("| `family-rules` | `.revitcli/family-rules/*.yml` |", text);
    }

    [Fact]
    public async Task Extensions_UnknownOutput_ReturnsFailureBeforeWritingIndex()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteExtensionsAsync(output, "yaml");

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Outputs_Json_PrintsStableOutputIndex()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteOutputsAsync(output, "json");

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal("workbench-outputs.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("outputCount").GetInt32() >= 10);

        var outputs = root.GetProperty("outputs").EnumerateArray().ToArray();
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "workbench verify" &&
            contract.GetProperty("jsonSchema").GetString() == "workbench-verification.v1" &&
            contract.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "workflow review <file>" &&
            contract.GetProperty("jsonSchema").GetString() == "workflow-review.v1" &&
            contract.GetProperty("notes").GetString()!.Contains("receipt triage", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "inspect workflows" &&
            contract.GetProperty("jsonSchema").GetString() == "inspect-workflows.v1");
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "inspect plans" &&
            contract.GetProperty("jsonSchema").GetString() == "inspect-plans.v1");
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "workbench project" &&
            contract.GetProperty("jsonSchema").GetString() == "workbench-project.v1");
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "workbench handoff" &&
            contract.GetProperty("jsonSchema").GetString() == "workbench-handoff.v1");
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "schedule create" &&
            contract.GetProperty("jsonSchema").GetString() == "schedule-create.v1");
    }

    [Fact]
    public async Task Outputs_Table_PrintsReadableOutputIndex()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteOutputsAsync(output, "table");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("RevitCli workbench outputs (workbench-outputs.v1)", text);
        Assert.Contains("workbench-verification.v1", text);
        Assert.Contains("workflow-review", text);
    }

    [Fact]
    public async Task Outputs_Markdown_PrintsHandoffTable()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteOutputsAsync(output, "markdown");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# RevitCli Workbench Outputs", text);
        Assert.Contains("Schema: `workbench-outputs.v1`", text);
        Assert.Contains("| Name | Command path | Table | JSON schema | Markdown | Notes |", text);
        Assert.Contains("| `model-health-history` | `score --history <duration>` | yes | `model-health-history.v1` | yes |", text);
    }

    [Fact]
    public async Task Outputs_UnknownOutput_ReturnsFailureBeforeWritingIndex()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteOutputsAsync(output, "yaml");

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Safeguards_Json_PrintsStableSafeguardIndex()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteSafeguardsAsync(output, "json");

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal("workbench-safeguards.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("safeguardCount").GetInt32() >= 10);

        var safeguards = root.GetProperty("safeguards").EnumerateArray().ToArray();
        Assert.Contains(safeguards, safeguard =>
            safeguard.GetProperty("name").GetString() == "plan-apply" &&
            safeguard.GetProperty("dryRunCommand").GetString() == "revitcli plan apply <plan-file> --dry-run" &&
            safeguard.GetProperty("receipt").GetString() == "<plan-file>.receipt.json");
        Assert.Contains(safeguards, safeguard =>
            safeguard.GetProperty("name").GetString() == "workflow-run" &&
            safeguard.GetProperty("reviewCommand").GetString() == "revitcli workflow receipts --output markdown");
        Assert.Contains(safeguards, safeguard =>
            safeguard.GetProperty("name").GetString() == "schedule-create" &&
            safeguard.GetProperty("dryRunCommand").GetString()!.Contains("schedule create", StringComparison.OrdinalIgnoreCase) &&
            safeguard.GetProperty("receipt").GetString()!.Contains("schedule-create", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Safeguards_Table_PrintsReadableSafeguardIndex()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteSafeguardsAsync(output, "table");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("RevitCli workbench safeguards (workbench-safeguards.v1)", text);
        Assert.Contains("plan-apply", text);
        Assert.Contains("Approval and review:", text);
    }

    [Fact]
    public async Task Safeguards_Markdown_PrintsHandoffTable()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteSafeguardsAsync(output, "markdown");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# RevitCli Workbench Safeguards", text);
        Assert.Contains("Schema: `workbench-safeguards.v1`", text);
        Assert.Contains("| Name | Path | Risk | Dry-run / preview | Approval | Receipt | Review | Notes |", text);
        Assert.Contains("| `deliverables-bundle` | `deliverables bundle` | local-write |", text);
    }

    [Fact]
    public async Task Safeguards_UnknownOutput_ReturnsFailureBeforeWritingIndex()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteSafeguardsAsync(output, "yaml");

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Project_Json_PrintsLocalArtifactInventory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, ".revitcli.yml"), "project: test" + Environment.NewLine);

            var workflowsDir = Path.Combine(root, ".revitcli", "workflows");
            Directory.CreateDirectory(workflowsDir);
            File.WriteAllText(Path.Combine(workflowsDir, "pre-issue.yml"), "name: pre-issue" + Environment.NewLine);

            var workflowReceiptsDir = Path.Combine(workflowsDir, "receipts");
            Directory.CreateDirectory(workflowReceiptsDir);
            File.WriteAllText(Path.Combine(workflowReceiptsDir, "run.json"), "{}" + Environment.NewLine);

            var deliveriesDir = Path.Combine(root, ".revitcli", "deliveries");
            Directory.CreateDirectory(deliveriesDir);
            File.WriteAllText(
                Path.Combine(deliveriesDir, "manifest.jsonl"),
                "{}" + Environment.NewLine + "{}" + Environment.NewLine);

            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteProjectAsync(root, "json", output);

            Assert.Equal(0, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            var rootElement = document.RootElement;
            Assert.Equal("workbench-project.v1", rootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal(Path.GetFullPath(root), rootElement.GetProperty("projectDirectory").GetString());
            Assert.True(rootElement.GetProperty("artifactCount").GetInt32() >= 10);
            Assert.True(rootElement.GetProperty("presentCount").GetInt32() >= 4);

            var artifacts = rootElement.GetProperty("artifacts").EnumerateArray().ToArray();
            Assert.Contains(artifacts, artifact =>
                artifact.GetProperty("name").GetString() == "profile" &&
                artifact.GetProperty("status").GetString() == "present" &&
                artifact.GetProperty("count").GetInt32() == 1 &&
                artifact.GetProperty("relativePath").GetString() == ".revitcli.yml");
            Assert.Contains(artifacts, artifact =>
                artifact.GetProperty("name").GetString() == "workflows" &&
                artifact.GetProperty("status").GetString() == "present" &&
                artifact.GetProperty("count").GetInt32() == 1);
            Assert.Contains(artifacts, artifact =>
                artifact.GetProperty("name").GetString() == "workflow-receipts" &&
                artifact.GetProperty("status").GetString() == "present" &&
                artifact.GetProperty("count").GetInt32() == 1);
            Assert.Contains(artifacts, artifact =>
                artifact.GetProperty("name").GetString() == "delivery-manifest" &&
                artifact.GetProperty("status").GetString() == "present" &&
                artifact.GetProperty("count").GetInt32() == 2 &&
                artifact.GetProperty("reviewCommand").GetString() == "revitcli deliverables list --output json");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Project_Table_PrintsReadableInventory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteProjectAsync(root, "table", output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("RevitCli workbench project (workbench-project.v1)", text);
            Assert.Contains("Artifacts:", text);
            Assert.Contains(".revitcli.yml", text);
            Assert.Contains("Review commands:", text);
            Assert.Contains("revitcli report knowledge --output markdown", text);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Project_Markdown_PrintsHandoffTable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteProjectAsync(root, "markdown", output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("# RevitCli Workbench Project", text);
            Assert.Contains("Schema: `workbench-project.v1`", text);
            Assert.Contains("| Artifact | Kind | Status | Count | Path | Review | Notes |", text);
            Assert.Contains("| `profile` | file |", text);
            Assert.Contains("`revitcli profile validate`", text);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Project_UnknownOutput_ReturnsFailureBeforeReadingDirectory()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteProjectAsync("/path/that/does/not/exist", "yaml", output);

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Project_MissingDirectory_ReturnsFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-missing-{Guid.NewGuid():N}");
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteProjectAsync(root, "json", output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Error: project directory not found:", output.ToString());
        Assert.Contains(Path.GetFullPath(root), output.ToString());
    }

    [Fact]
    public async Task Handoff_Json_PrintsTerminalHandoffSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, ".revitcli.yml"), "project: test" + Environment.NewLine);
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteHandoffAsync(root, "json", output);

            Assert.Equal(0, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            var rootElement = document.RootElement;
            Assert.Equal("workbench-handoff.v1", rootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal(Path.GetFullPath(root), rootElement.GetProperty("projectDirectory").GetString());
            Assert.True(rootElement.GetProperty("success").GetBoolean());
            Assert.Equal(0, rootElement.GetProperty("issueCount").GetInt32());
            Assert.True(rootElement.GetProperty("checkCount").GetInt32() >= 20);
            Assert.True(rootElement.GetProperty("artifactCount").GetInt32() >= 10);
            Assert.True(rootElement.GetProperty("readinessActionCount").GetInt32() >= 1);
            Assert.True(rootElement.GetProperty("commandCount").GetInt32() >= 9);

            var verificationChecks = rootElement.GetProperty("verificationChecks").EnumerateArray().ToArray();
            Assert.True(verificationChecks.Length >= 20);
            Assert.Contains(verificationChecks, check =>
                check.GetProperty("id").GetString() == "completion-surface" &&
                check.GetProperty("status").GetString() == "pass");
            Assert.Contains(verificationChecks, check =>
                check.GetProperty("id").GetString() == "schedule-create-safety" &&
                check.GetProperty("evidence").GetString()!.Contains("dry-run", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(verificationChecks, check =>
                check.GetProperty("id").GetString() == "handoff-command-surface" &&
                check.GetProperty("evidence").GetString()!.Contains("plan-discovery", StringComparison.OrdinalIgnoreCase));

            var readinessActions = rootElement.GetProperty("readinessActions").EnumerateArray().ToArray();
            Assert.Contains(readinessActions, action =>
                action.GetProperty("phase").GetString() == "bootstrap-workflows" &&
                action.GetProperty("artifact").GetString() == "workflows" &&
                action.GetProperty("status").GetString() == "missing" &&
                action.GetProperty("commandLine").GetString()!.Contains("revitcli workflow init all", StringComparison.OrdinalIgnoreCase) &&
                action.GetProperty("commandLine").GetString()!.Contains($"--dir {Path.GetFullPath(root)}", StringComparison.OrdinalIgnoreCase) &&
                action.GetProperty("workingDirectory").GetString() == Path.GetFullPath(root));
            Assert.Contains(readinessActions, action =>
                action.GetProperty("phase").GetString() == "draft-knowledge-report" &&
                action.GetProperty("commandLine").GetString()!.Contains("revitcli report knowledge", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(readinessActions, action =>
                action.GetProperty("phase").GetString() == "review-saved-plans" &&
                action.GetProperty("artifact").GetString() == "plans" &&
                action.GetProperty("commandLine").GetString()!.Contains("revitcli inspect plans", StringComparison.OrdinalIgnoreCase) &&
                action.GetProperty("commandLine").GetString()!.Contains($"--dir {Path.GetFullPath(root)}", StringComparison.OrdinalIgnoreCase));

            var commands = rootElement.GetProperty("commands").EnumerateArray().ToArray();
            Assert.Contains(commands, command =>
                command.GetProperty("phase").GetString() == "verify" &&
                command.GetProperty("commandLine").GetString()!.Contains("revitcli workbench verify", StringComparison.OrdinalIgnoreCase) &&
                command.GetProperty("commandLine").GetString()!.Contains($"--dir {Path.GetFullPath(root)}", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(commands, command =>
                command.GetProperty("phase").GetString() == "project" &&
                command.GetProperty("commandLine").GetString()!.Contains("revitcli workbench project", StringComparison.OrdinalIgnoreCase) &&
                command.GetProperty("commandLine").GetString()!.Contains($"--dir {Path.GetFullPath(root)}", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(commands, command =>
                command.GetProperty("phase").GetString() == "workflow-review" &&
                command.GetProperty("commandLine").GetString()!.Contains("workflow review", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(commands, command =>
                command.GetProperty("phase").GetString() == "workflow-discovery" &&
                command.GetProperty("commandLine").GetString()!.Contains("revitcli inspect workflows", StringComparison.OrdinalIgnoreCase) &&
                command.GetProperty("commandLine").GetString()!.Contains($"--dir {Path.GetFullPath(root)}", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(commands, command =>
                command.GetProperty("phase").GetString() == "plan-discovery" &&
                command.GetProperty("commandLine").GetString()!.Contains("revitcli inspect plans", StringComparison.OrdinalIgnoreCase) &&
                command.GetProperty("commandLine").GetString()!.Contains($"--dir {Path.GetFullPath(root)}", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(commands, command =>
                command.GetProperty("phase").GetString() == "schedule-create" &&
                command.GetProperty("commandLine").GetString()!.Contains("--dry-run", StringComparison.OrdinalIgnoreCase) &&
                command.GetProperty("commandLine").GetString()!.Contains("--output json", StringComparison.OrdinalIgnoreCase));

            var notes = rootElement.GetProperty("notes").EnumerateArray().Select(note => note.GetString()).ToArray();
            Assert.Contains(notes, note => note!.Contains("dry-run", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(notes, note => note!.Contains("MCP", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Handoff_Table_PrintsReadableSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteHandoffAsync(root, "table", output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("RevitCli workbench handoff (workbench-handoff.v1)", text);
            Assert.Contains("Verification: yes", text);
            Assert.Contains("Readiness checks:", text);
            Assert.Contains("Readiness actions:", text);
            Assert.Contains("workflow init all", text);
            Assert.Contains("completion-surface", text);
            Assert.Contains("handoff-command-surface", text);
            Assert.Contains("revitcli workbench verify", text);
            Assert.Contains("--dir", text);
            Assert.Contains("schedule create", text);
            Assert.Contains("Notes:", text);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Handoff_Markdown_PrintsHandoffTable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteHandoffAsync(root, "markdown", output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("# RevitCli Workbench Handoff", text);
            Assert.Contains("Schema: `workbench-handoff.v1`", text);
            Assert.Contains("## Verification Checks", text);
            Assert.Contains("| `pass` | `completion-surface` |", text);
            Assert.Contains("| `pass` | `handoff-command-surface` |", text);
            Assert.Contains("## Readiness Actions", text);
            Assert.Contains("| `bootstrap-workflows` | `workflows` | `missing` | `revitcli workflow init all --dir", text);
            Assert.Contains("## Commands", text);
            Assert.Contains("| Phase | Command | Purpose |", text);
            Assert.Contains("| `project` | `revitcli workbench project --dir", text);
            Assert.Contains("| `workflow-discovery` | `revitcli inspect workflows --dir", text);
            Assert.Contains("| `plan-discovery` | `revitcli inspect plans --dir", text);
            Assert.Contains("| `schedule-create` | `revitcli schedule create --category Doors --fields \"Mark,Level\" --name \"Door Review\" --dry-run --output json` |", text);
            Assert.Contains("## Notes", text);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Handoff_UnknownOutput_ReturnsFailureBeforeReadingDirectory()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteHandoffAsync("/path/that/does/not/exist", "yaml", output);

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Handoff_MissingDirectory_ReturnsFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-missing-{Guid.NewGuid():N}");
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteHandoffAsync(root, "json", output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Error: project directory not found:", output.ToString());
        Assert.Contains(Path.GetFullPath(root), output.ToString());
    }

    [Fact]
    public async Task Paths_UnknownOutput_ReturnsFailureBeforeWritingIndex()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecutePathsAsync(output, "yaml");

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, output.ToString());
    }
}
