using System.Text.Json;
using RevitCli.Commands;

namespace RevitCli.Tests.Commands;

[Collection("Sequential")]
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
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench contract --contract workbench-contract.v2") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench verify --contract workbench-contract.v2") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench receipts") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench paths") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench exits") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench extensions") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench outputs") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench safeguards") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench project") &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "workbench handoff"));
        Assert.Contains(commands, command =>
            command.GetProperty("name").GetString() == "release" &&
            command.GetProperty("supportsJson").GetBoolean() &&
            command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "release verify --strict"));
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
        var ledger = commands.Single(command => command.GetProperty("name").GetString() == "ledger");
        Assert.Equal("local-write", ledger.GetProperty("risk").GetString());
        Assert.Contains(
            ledger.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "ledger append");
        Assert.Contains(
            ledger.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "ledger query");
        Assert.Contains(
            ledger.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "ledger validate");
        Assert.Contains(
            ledger.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "ledger stats");
        Assert.Contains(
            ledger.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "ledger timeline");
        Assert.Contains("operations.jsonl", ledger.GetProperty("receipt").GetString()!);
        var schedule = commands.Single(command => command.GetProperty("name").GetString() == "schedule");
        Assert.Equal("mixed", schedule.GetProperty("risk").GetString());
        Assert.Contains(
            schedule.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "schedule create");
        Assert.Contains("schedule-create", schedule.GetProperty("receipt").GetString()!);
        var rooms = commands.Single(command => command.GetProperty("name").GetString() == "rooms");
        Assert.Equal("write", rooms.GetProperty("risk").GetString());
        Assert.Contains(
            rooms.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "rooms renumber");
        Assert.Contains("room-numbering-plan.v1", rooms.GetProperty("receipt").GetString()!);
        var marks = commands.Single(command => command.GetProperty("name").GetString() == "marks");
        Assert.Equal("mixed", marks.GetProperty("risk").GetString());
        Assert.Contains(
            marks.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "marks assign");
        Assert.Contains(
            marks.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "marks verify");
        Assert.Contains("mark-assignment-plan.v1", marks.GetProperty("receipt").GetString()!);
        var schedules = commands.Single(command => command.GetProperty("name").GetString() == "schedules");
        Assert.Equal("mixed", schedules.GetProperty("risk").GetString());
        Assert.Contains(
            schedules.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "schedules ensure");
        Assert.Contains(
            schedules.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "schedules batch-export");
        Assert.Contains(
            schedules.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "schedules compare");
        Assert.Contains("schedule-export-manifest.v1", schedules.GetProperty("receipt").GetString()!);
        var views = commands.Single(command => command.GetProperty("name").GetString() == "views");
        Assert.Equal("mixed", views.GetProperty("risk").GetString());
        Assert.Contains(
            views.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "views audit");
        Assert.Contains(
            views.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "views template-apply");
        Assert.Contains(
            views.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "views clone-set");
        Assert.Contains("view-template-plan.v1", views.GetProperty("receipt").GetString()!);
        var links = commands.Single(command => command.GetProperty("name").GetString() == "links");
        Assert.Equal("mixed", links.GetProperty("risk").GetString());
        Assert.Contains(
            links.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "links audit");
        Assert.Contains(
            links.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "links repair");
        Assert.Contains("link-repair-plan.v1", links.GetProperty("receipt").GetString()!);
        var model = commands.Single(command => command.GetProperty("name").GetString() == "model");
        Assert.Equal("mixed", model.GetProperty("risk").GetString());
        Assert.Contains(
            model.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "model map-check");
        Assert.Contains(
            model.GetProperty("commandPaths").EnumerateArray(),
            path => path.GetString() == "model map-fix");
        Assert.Contains("model-map-fix-plan.v1", model.GetProperty("receipt").GetString()!);
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
    public async Task Contract_Json_WithContractV2_PrintsV2Envelope()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteContractAsync(output, "json", "workbench-contract.v2");

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal("workbench-contract.v2", root.GetProperty("schemaVersion").GetString());
        Assert.Contains(
            root.GetProperty("commands").EnumerateArray(),
            command => command.GetProperty("name").GetString() == "issue" &&
                command.GetProperty("commandPaths").EnumerateArray().Any(path => path.GetString() == "issue package"));
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
        Assert.Contains("`revitcli workflow registry --output json`", text);
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
    public async Task Contract_UnknownContract_ReturnsFailureBeforeWritingContract()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteContractAsync(output, "json", "workbench-contract.v3");

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --contract must be 'workbench-contract.v1' or 'workbench-contract.v2'." + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Verify_Json_PrintsPassingVerificationEnvelope()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(output, "json", FindRepositoryRoot());

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
            check.GetProperty("id").GetString() == "sheet-renumber-dry-run-required" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "room-renumber-dry-run-required" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "mark-assignment-dry-run-required" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "schedule-spec-schema" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "schedule-export-traceable" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("SHA256", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("profile", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("model/document identity", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "schedule-diff-traceable" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("before/after", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "schedule-ensure-rollback" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "view-mutation-plan-ids-frozen" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "view-clone-no-name-collision" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "view-rollback-guards-placed-views" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "linkRepairNoCoordinateMove" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "modelMapWritableProbe" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "coordinationReceiptPaths" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "contractV2Compat" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "issueNoHiddenMutation" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "issuePackageTraceability" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("file hashes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "v5FaultInjectionCoverage" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("missing profiles", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("missing receipts", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("tampered receipts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "v5RealSmokeDisclosure" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("not-live-verified", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("read-only dry-run", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "v5RcBoundaryDisclosure" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("claimed live Revit years", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("experimental", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "v51SheetReleasePilotGate" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("production pilot gated", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("100/300/1000", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "v52SchedulePackagePilotGate" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("schedule/package closure", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("live smoke gaps", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "v53NumberingControlledApplyPilotGate" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("reserved/hold", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("live Revit smoke gaps", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "v54StandardsRuntimePackGate" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("standards runtime pack", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("standards install dry-run", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("populated-target final file-tree snapshot evidence unchanged", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("sheet map", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("numbering rule", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "v55ViewCoordinationHygieneGate" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("audit-first", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("no coordinate moves", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("worksharing gaps", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "dashboardOptional" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "sheet-receipt-rollback-shape" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("rollback actions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check =>
            check.GetProperty("id").GetString() == "numbering-receipt-rollback-shape" &&
            check.GetProperty("status").GetString() == "pass" &&
            check.GetProperty("evidence").GetString()!.Contains("rule provenance", StringComparison.OrdinalIgnoreCase) &&
            check.GetProperty("evidence").GetString()!.Contains("rollback actions", StringComparison.OrdinalIgnoreCase));
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
    public async Task Verify_Json_WithContractV2_PrintsV2Envelope()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
            output,
            "json",
            projectDirectory: FindRepositoryRoot(),
            contractSchema: "workbench-contract.v2");

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal("workbench-verify-report.v2", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("workbench-contract.v2", root.GetProperty("contractSchema").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Contains(
            root.GetProperty("checks").EnumerateArray(),
            check => check.GetProperty("id").GetString() == "issuePackageTraceability" &&
                check.GetProperty("status").GetString() == "pass");
        Assert.Contains(
            root.GetProperty("checks").EnumerateArray(),
            check => check.GetProperty("id").GetString() == "schedule-diff-traceable" &&
                check.GetProperty("status").GetString() == "pass");
        Assert.Contains(
            root.GetProperty("checks").EnumerateArray(),
            check => check.GetProperty("id").GetString() == "v5FaultInjectionCoverage" &&
                check.GetProperty("status").GetString() == "pass");
        Assert.Contains(
            root.GetProperty("checks").EnumerateArray(),
            check => check.GetProperty("id").GetString() == "v5RealSmokeDisclosure" &&
                check.GetProperty("status").GetString() == "pass" &&
                check.GetProperty("evidence").GetString()!.Contains("not-live-verified", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("read-only dry-run", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            root.GetProperty("checks").EnumerateArray(),
            check => check.GetProperty("id").GetString() == "v5RcBoundaryDisclosure" &&
                check.GetProperty("status").GetString() == "pass" &&
                check.GetProperty("evidence").GetString()!.Contains("strict release gate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            root.GetProperty("checks").EnumerateArray(),
            check => check.GetProperty("id").GetString() == "v54StandardsRuntimePackGate" &&
                check.GetProperty("status").GetString() == "pass" &&
                check.GetProperty("evidence").GetString()!.Contains("office-standard", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("approved install", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            root.GetProperty("checks").EnumerateArray(),
            check => check.GetProperty("id").GetString() == "v55ViewCoordinationHygieneGate" &&
                check.GetProperty("status").GetString() == "pass" &&
                check.GetProperty("evidence").GetString()!.Contains("writable probe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            root.GetProperty("checks").EnumerateArray(),
            check => check.GetProperty("id").GetString() == "v56TeamPilotPackGate" &&
                check.GetProperty("status").GetString() == "pass" &&
                check.GetProperty("evidence").GetString()!.Contains("receipt retention", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("without SaaS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            root.GetProperty("checks").EnumerateArray(),
            check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                check.GetProperty("status").GetString() == "pass" &&
                check.GetProperty("evidence").GetString()!.Contains("deterministic receipts", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("workflow registry runtime emits workflow-registry.v1", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("JSON/table/Markdown output semantic parity", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("final file-tree snapshot evidence", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("event-level no-write evidence", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("journal verify", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("history list JSON/table outputs", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("journal verify JSON/table validity/root-hash parity", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("history-list.v1 JSON count consistency and table row-order parity", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("table summary and Markdown detail parity for supported command-spine paths", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("rollback dry-run", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("history-list.v1 execution", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("rollback safe preview execution", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("rollback dry-run request enforcement", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("event-level no-write evidence", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("ledger query/validate runtime emits ledger-query.v1 and ledger-validate.v1", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("deterministic timestamp/source/path/line ordering", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("JSON/table/Markdown output semantic parity", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("source readability", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("artifact link", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("query invalid-timestamp filtering, final file-tree snapshot evidence, and event-level no-write evidence", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("ledger stats runtime emits ledger-stats.v1", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("exact counters operationCount=3 issueCount=3 errorIssueCount=3 unreadableReceiptCount=2", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("exact source/action/category/operator/receipt-status/issue-source/issue-severity sets", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("malformed journal, delivery, and workflow artifacts, final file-tree snapshot evidence, and event-level no-write evidence", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("ledger timeline runtime emits ledger-timeline.v1", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("ledger replay preview emits ledger-replay.v1", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("exact day-bucket source/action/category/operator/receipt-status counts", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("explicit UTC offset warning preservation under since/until/window time filters, final file-tree snapshot evidence, and event-level no-write evidence", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("category", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("operator", StringComparison.OrdinalIgnoreCase) &&
                check.GetProperty("evidence").GetString()!.Contains("without SaaS", StringComparison.OrdinalIgnoreCase));
        var v60Check = root.GetProperty("checks").EnumerateArray()
            .Single(check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate");
        var runtimeEvidence = v60Check.GetProperty("runtimeEvidence");
        var runtimeEvidenceFields = runtimeEvidence.EnumerateObject()
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
        ], runtimeEvidenceFields);
        Assert.True(runtimeEvidence.GetProperty("commandSpine").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("commandSpineOutputParity").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("commandSpineNoWrites").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("standardsValidate").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("issuePreflight").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("issuePackageDryRun").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("deliverablesVerify").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("journalVerify").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("historyList").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("historyListCountConsistency").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("historyListRowOrder").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("rollbackDryRun").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("rollbackDryRunPreview").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("rollbackNoMutatingSetRequest").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("workflowRegistry").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("ledgerAppend").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("ledgerQueryValidate").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("ledgerReplay").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("ledgerStats").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("ledgerTimeline").GetBoolean());
        Assert.True(runtimeEvidence.GetProperty("allRuntimeChecksPass").GetBoolean());
        var historyEvidence = runtimeEvidence.GetProperty("historyListEvidence");
        Assert.Equal(1, historyEvidence.GetProperty("jsonEntryCount").GetInt32());
        Assert.Equal(0, historyEvidence.GetProperty("jsonHiddenCount").GetInt32());
        Assert.Equal(1, historyEvidence.GetProperty("jsonReturnedCount").GetInt32());
        Assert.Equal(1, historyEvidence.GetProperty("tableRowCount").GetInt32());
        Assert.True(historyEvidence.GetProperty("countConsistency").GetBoolean());
        Assert.True(historyEvidence.GetProperty("idOrderMatch").GetBoolean());
        Assert.True(historyEvidence.GetProperty("headerMatched").GetBoolean());
        var rollbackEvidence = runtimeEvidence.GetProperty("rollbackDryRunEvidence");
        Assert.Equal(1, rollbackEvidence.GetProperty("actionCount").GetInt32());
        Assert.Equal(0, rollbackEvidence.GetProperty("conflictCount").GetInt32());
        Assert.Equal(0, rollbackEvidence.GetProperty("errorCount").GetInt32());
        Assert.Contains("rollback", rollbackEvidence.GetProperty("safeApplyCommand").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(rollbackEvidence.GetProperty("safeApplyEmitted").GetBoolean());
        Assert.True(rollbackEvidence.GetProperty("dryRunPreviewOnly").GetBoolean());
        Assert.True(rollbackEvidence.GetProperty("sawDryRunSetPreview").GetBoolean());
        Assert.False(rollbackEvidence.GetProperty("sawMutatingSetRequest").GetBoolean());
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_IgnoresUnrelatedEmptyDocsDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-unrelated-docs-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            Directory.SetCurrentDirectory(root);
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: null,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(0, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v5RealSmokeDisclosure" &&
                    check.GetProperty("status").GetString() == "pass" &&
                    !check.GetProperty("evidence").GetString()!.Contains("missing and not disclosed", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v5RcBoundaryDisclosure" &&
                    check.GetProperty("status").GetString() == "pass" &&
                    !check.GetProperty("evidence").GetString()!.Contains("missing or unreadable", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_UsesProvidedProjectDirectoryForV55GapReport()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v55-dir-gap-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "docs", "release-checklist.md"), "# Release Checklist\n");
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v55ViewCoordinationHygieneGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("docs/smoke/v5.5/gap-report.md is missing", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_UsesProvidedProjectDirectoryForV52ToV54GapReports()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v52-v54-dir-gap-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            var docsRoot = Path.Combine(root, "docs");
            Directory.CreateDirectory(docsRoot);
            File.WriteAllText(Path.Combine(docsRoot, "roadmap-v5-v6.md"), "# v5-v6\n");
            File.WriteAllText(
                Path.Combine(docsRoot, "v5-rc-readiness.md"),
                """
Current status:
Claimed live Revit years
Stable P0 Commands
Experimental / Deferred Commands
not live verified
v5RealSmokeDisclosure
issuePackageTraceability
v5FaultInjectionCoverage
release verify --strict
MCP, SaaS, or built-in LLM parser
""");
            Directory.CreateDirectory(Path.Combine(docsRoot, "smoke", "v5.0"));
            File.WriteAllText(
                Path.Combine(docsRoot, "smoke", "v5.0", "gap-report.md"),
                """
| Revit 2024 | not live verified |
| Revit 2025 | not live verified |
| Revit 2026 | not live verified |
""");
            Directory.CreateDirectory(Path.Combine(docsRoot, "smoke", "v5.1"));
            File.WriteAllText(
                Path.Combine(docsRoot, "smoke", "v5.1", "gap-report.md"),
                """
v5.1 sheet release control
production pilot gated
100 sheet
300 sheet
1000 sheet
not live verified
Revit 2026
dry-run/plan/receipt/rollback
journal verify
Post-rollback evidence
""");
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            var checks = document.RootElement.GetProperty("checks").EnumerateArray().ToArray();
            Assert.Contains(checks, check =>
                check.GetProperty("id").GetString() == "v52SchedulePackagePilotGate" &&
                check.GetProperty("status").GetString() == "fail" &&
                check.GetProperty("evidence").GetString()!.Contains("docs/smoke/v5.2/gap-report.md is missing", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(checks, check =>
                check.GetProperty("id").GetString() == "v53NumberingControlledApplyPilotGate" &&
                check.GetProperty("status").GetString() == "fail" &&
                check.GetProperty("evidence").GetString()!.Contains("docs/smoke/v5.3/gap-report.md is missing", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(checks, check =>
                check.GetProperty("id").GetString() == "v54StandardsRuntimePackGate" &&
                check.GetProperty("status").GetString() == "fail" &&
                check.GetProperty("evidence").GetString()!.Contains("docs/smoke/v5.4/gap-report.md is missing", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV56GapReportIsMissingForReleaseRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v56-missing-gap-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            File.Delete(Path.Combine(root, "docs", "smoke", "v5.6", "gap-report.md"));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v56TeamPilotPackGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("docs/smoke/v5.6/gap-report.md is missing", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV56SupportTemplateIsMissingForReleaseRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v56-missing-template-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            File.Delete(Path.Combine(root, "docs", "smoke", "v5.6", "support-error-report-template.md"));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v56TeamPilotPackGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("support-error-report", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60ContractIsMissingForReleaseRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-contract-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            File.Delete(Path.Combine(root, "docs", "v6-local-bimops-contract.md"));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("docs/v6-local-bimops-contract.md is missing", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60OfficePilotGapIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-office-pilot-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var gapPath = Path.Combine(root, "docs", "smoke", "v6.0", "gap-report.md");
            File.WriteAllText(
                gapPath,
                File.ReadAllText(gapPath).Replace("office rollout pilots", "rollout pilots", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("office rollout pilots", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60PilotEvidenceTemplateIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-pilot-template-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            File.Delete(Path.Combine(root, "docs", "smoke", "v6.0", "pilot-evidence-template.md"));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("pilot-evidence-template.md", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60PilotEvidenceSignoffIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-pilot-signoff-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var templatePath = Path.Combine(root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
            File.WriteAllText(
                templatePath,
                File.ReadAllText(templatePath).Replace("BIM manager signoff", "manager approval", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("BIM manager signoff", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60PilotEvidenceStatusCommandIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-pilot-status-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var templatePath = Path.Combine(root, "docs", "smoke", "v6.0", "pilot-evidence-template.md");
            File.WriteAllText(
                templatePath,
                File.ReadAllText(templatePath).Replace("status --output json", "status proof", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("status --output json", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60OfficeRolloutStatusOverclaims()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-office-rollout-overclaim-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var statusPath = Path.Combine(root, "docs", "smoke", "v6.0", "office-rollout-status.json");
            File.WriteAllText(
                statusPath,
                File.ReadAllText(statusPath).Replace("\"officeRolloutCompletion\": false", "\"officeRolloutCompletion\": true", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("office-rollout-status.json", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_AllowsV60OfficeRolloutThresholdWithPerPilotEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-office-rollout-complete-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var statusPath = Path.Combine(root, "docs", "smoke", "v6.0", "office-rollout-status.json");
            File.WriteAllText(
                statusPath,
                File.ReadAllText(statusPath)
                    .Replace("\"completedOfficePilotCount\": 0", "\"completedOfficePilotCount\": 2", StringComparison.Ordinal)
                    .Replace("\"completedPilotIds\": []", "\"completedPilotIds\": [\"pilot-01\", \"pilot-02\"]", StringComparison.Ordinal)
                    .Replace("\"completedPilots\": []", "\"completedPilots\": [" + CompletedPilotEvidenceJson("pilot-01") + ", " + CompletedPilotEvidenceJson("pilot-02") + "]", StringComparison.Ordinal)
                    .Replace("\"officeRolloutCompletion\": false", "\"officeRolloutCompletion\": true", StringComparison.Ordinal)
                    .Replace("\"productionSupportClaim\": false", "\"productionSupportClaim\": true", StringComparison.Ordinal));
            WriteCompletedPilotEvidencePackets(root, "pilot-01", "pilot-02");
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(0, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "pass");
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60OfficeRolloutEvidencePacketMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-office-rollout-packet-missing-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var statusPath = Path.Combine(root, "docs", "smoke", "v6.0", "office-rollout-status.json");
            File.WriteAllText(
                statusPath,
                File.ReadAllText(statusPath)
                    .Replace("\"completedOfficePilotCount\": 0", "\"completedOfficePilotCount\": 2", StringComparison.Ordinal)
                    .Replace("\"completedPilotIds\": []", "\"completedPilotIds\": [\"pilot-01\", \"pilot-02\"]", StringComparison.Ordinal)
                    .Replace("\"completedPilots\": []", "\"completedPilots\": [" + CompletedPilotEvidenceJson("pilot-01") + ", " + CompletedPilotEvidenceJson("pilot-02") + "]", StringComparison.Ordinal)
                    .Replace("\"officeRolloutCompletion\": false", "\"officeRolloutCompletion\": true", StringComparison.Ordinal)
                    .Replace("\"productionSupportClaim\": false", "\"productionSupportClaim\": true", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("completedPilots", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60OfficeRolloutEvidencePacketPilotIdMismatch()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-office-rollout-packet-id-mismatch-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var statusPath = Path.Combine(root, "docs", "smoke", "v6.0", "office-rollout-status.json");
            File.WriteAllText(
                statusPath,
                File.ReadAllText(statusPath)
                    .Replace("\"completedOfficePilotCount\": 0", "\"completedOfficePilotCount\": 2", StringComparison.Ordinal)
                    .Replace("\"completedPilotIds\": []", "\"completedPilotIds\": [\"pilot-01\", \"pilot-02\"]", StringComparison.Ordinal)
                    .Replace("\"completedPilots\": []", "\"completedPilots\": [" + CompletedPilotEvidenceJson("pilot-01") + ", " + CompletedPilotEvidenceJson("pilot-02") + "]", StringComparison.Ordinal)
                    .Replace("\"officeRolloutCompletion\": false", "\"officeRolloutCompletion\": true", StringComparison.Ordinal)
                    .Replace("\"productionSupportClaim\": false", "\"productionSupportClaim\": true", StringComparison.Ordinal));
            WriteCompletedPilotEvidencePackets(root, "pilot-01");
            var mismatchedPacketPath = Path.Combine(root, "docs", "smoke", "v6.0", "pilot-02.md");
            Directory.CreateDirectory(Path.GetDirectoryName(mismatchedPacketPath)!);
            File.WriteAllText(mismatchedPacketPath, CompletedPilotEvidencePacketContent("pilot-03"));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("completedPilots", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60OfficeRolloutPilotIdsMismatchEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-office-rollout-id-mismatch-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var statusPath = Path.Combine(root, "docs", "smoke", "v6.0", "office-rollout-status.json");
            File.WriteAllText(
                statusPath,
                File.ReadAllText(statusPath)
                    .Replace("\"completedOfficePilotCount\": 0", "\"completedOfficePilotCount\": 2", StringComparison.Ordinal)
                    .Replace("\"completedPilotIds\": []", "\"completedPilotIds\": [\"pilot-01\", \"pilot-02\"]", StringComparison.Ordinal)
                    .Replace("\"completedPilots\": []", "\"completedPilots\": [" + CompletedPilotEvidenceJson("pilot-01") + ", " + CompletedPilotEvidenceJson("pilot-03") + "]", StringComparison.Ordinal)
                    .Replace("\"officeRolloutCompletion\": false", "\"officeRolloutCompletion\": true", StringComparison.Ordinal)
                    .Replace("\"productionSupportClaim\": false", "\"productionSupportClaim\": true", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("completedPilots", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60OfficeRolloutUsesLocalEvidencePacketPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-office-rollout-local-path-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var statusPath = Path.Combine(root, "docs", "smoke", "v6.0", "office-rollout-status.json");
            File.WriteAllText(
                statusPath,
                File.ReadAllText(statusPath)
                    .Replace("\"completedOfficePilotCount\": 0", "\"completedOfficePilotCount\": 2", StringComparison.Ordinal)
                    .Replace("\"completedPilotIds\": []", "\"completedPilotIds\": [\"pilot-01\", \"pilot-02\"]", StringComparison.Ordinal)
                    .Replace("\"completedPilots\": []", "\"completedPilots\": [" + CompletedPilotEvidenceJson("pilot-01") + ", " + CompletedPilotEvidenceJson("pilot-02", @"C:\Users\Lenovo\pilot-02.md") + "]", StringComparison.Ordinal)
                    .Replace("\"officeRolloutCompletion\": false", "\"officeRolloutCompletion\": true", StringComparison.Ordinal)
                    .Replace("\"productionSupportClaim\": false", "\"productionSupportClaim\": true", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("completedPilots", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60AuditSpineParityDetailsAreMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-audit-spine-parity-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var gapPath = Path.Combine(root, "docs", "smoke", "v6.0", "gap-report.md");
            File.WriteAllText(
                gapPath,
                File.ReadAllText(gapPath)
                    .Replace("journal verify JSON/table validity/root-hash parity", "journal verify JSON/table parity", StringComparison.Ordinal)
                    .Replace("history-list.v1 JSON count consistency and table row-order parity", "history list JSON/table outputs", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("journal verify JSON/table validity/root-hash parity", StringComparison.OrdinalIgnoreCase) &&
                    check.GetProperty("evidence").GetString()!.Contains("history-list.v1 JSON count consistency and table row-order parity", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_AcceptsSemanticV60AuditSpineParityWording()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-semantic-audit-spine-parity-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var gapPath = Path.Combine(root, "docs", "smoke", "v6.0", "gap-report.md");
            File.WriteAllText(
                gapPath,
                File.ReadAllText(gapPath)
                    .Replace("journal verify JSON/table validity/root-hash parity", "journal verify JSON/table parity with validity and root-hash evidence", StringComparison.Ordinal)
                    .Replace("history-list.v1 JSON count consistency and table row-order parity", "history-list.v1 uses JSON count consistency with table row-order parity", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(0, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("docs/smoke/v5.5/gap-report.md", "MCP orchestration", "MCP orchestration and requires MCP")]
    [InlineData("docs/smoke/v5.6/gap-report.md", "no MCP", "no MCP but requires MCP")]
    [InlineData("docs/v6-local-bimops-contract.md", "no MCP", "no MCP but requires MCP")]
    [InlineData("docs/smoke/v6.0/gap-report.md", "no database", "no database but uses database-backed storage")]
    public async Task Verify_Json_WithContractV2_FailsWhenBoundaryContradictsNonGoals(
        string relativePath,
        string requiredPhrase,
        string replacement)
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-boundary-contradiction-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var text = File.ReadAllText(path);
            Assert.Contains(requiredPhrase, text, StringComparison.Ordinal);
            File.WriteAllText(path, text.Replace(requiredPhrase, replacement, StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("contradictory", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60LedgerQueryOrderingDocIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-ledger-query-ordering-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var smokePath = Path.Combine(root, "docs", "smoke", "v6.0", "ledger-query.md");
            File.WriteAllText(
                smokePath,
                File.ReadAllText(smokePath).Replace("timestamp/source/path/line ordering", "deterministic sorting", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("timestamp/source/path/line ordering", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("docs/smoke/v6.0/ledger-query.md")]
    [InlineData("docs/smoke/v6.0/ledger-validate.md")]
    public async Task Verify_Json_WithContractV2_FailsWhenV60LedgerFinalSnapshotDocIsMissing(string relativeSmokePath)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"revitcli-workbench-v60-missing-ledger-final-snapshot-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var smokePath = Path.Combine(root, relativeSmokePath.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllText(
                smokePath,
                File.ReadAllText(smokePath).Replace("final file-tree snapshot evidence", "final snapshot evidence", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("final file-tree snapshot evidence", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60LedgerQueryOutputParityDocIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-ledger-query-output-parity-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var smokePath = Path.Combine(root, "docs", "smoke", "v6.0", "ledger-query.md");
            File.WriteAllText(
                smokePath,
                File.ReadAllText(smokePath).Replace("JSON/table/Markdown output semantic parity", "output-format coverage", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("JSON/table/Markdown output semantic parity", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("JSON/table/Markdown output semantic parity", "output-format coverage")]
    [InlineData("final file-tree snapshot evidence", "final snapshot evidence")]
    [InlineData("event-level no-write evidence", "no-write evidence")]
    public async Task Verify_Json_WithContractV2_FailsWhenV60WorkflowRegistryRuntimeEvidenceDocIsMissing(
        string requiredPhrase,
        string replacement)
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-workflow-registry-evidence-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var smokePath = Path.Combine(root, "docs", "smoke", "v6.0", "workflow-registry.md");
            File.WriteAllText(
                smokePath,
                File.ReadAllText(smokePath).Replace(requiredPhrase, replacement, StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains(requiredPhrase, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60LedgerValidateSourceReadabilityDocIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-ledger-validate-source-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var smokePath = Path.Combine(root, "docs", "smoke", "v6.0", "ledger-validate.md");
            File.WriteAllText(
                smokePath,
                File.ReadAllText(smokePath).Replace("source readability", "source checks", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("source readability", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60LedgerStatsOperationCountsDocIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-ledger-stats-counts-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var smokePath = Path.Combine(root, "docs", "smoke", "v6.0", "ledger-stats.md");
            File.WriteAllText(
                smokePath,
                File.ReadAllText(smokePath).Replace("operation counts", "operation summaries", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("operation counts", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60LedgerStatsSourceCountsDocIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-ledger-stats-source-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var smokePath = Path.Combine(root, "docs", "smoke", "v6.0", "ledger-stats.md");
            File.WriteAllText(
                smokePath,
                File.ReadAllText(smokePath).Replace("source counts", "source summaries", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("source counts", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60LedgerStatsIssueSeverityDocIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-ledger-stats-issues-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var smokePath = Path.Combine(root, "docs", "smoke", "v6.0", "ledger-stats.md");
            File.WriteAllText(
                smokePath,
                File.ReadAllText(smokePath).Replace("issue severity", "issue type", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("issue severity", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60LedgerStatsMalformedDocIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-ledger-stats-malformed-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var smokePath = Path.Combine(root, "docs", "smoke", "v6.0", "ledger-stats.md");
            File.WriteAllText(
                smokePath,
                File.ReadAllText(smokePath).Replace("malformed journal, delivery manifest, and workflow receipt artifacts", "malformed local artifacts", StringComparison.OrdinalIgnoreCase));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("malformed", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60LedgerTimelineUnbucketedDocIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-ledger-timeline-unbucketed-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var smokePath = Path.Combine(root, "docs", "smoke", "v6.0", "ledger-timeline.md");
            File.WriteAllText(
                smokePath,
                File.ReadAllText(smokePath).Replace("unbucketed timestamp", "timestamp warning", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("unbucketed timestamp", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60LedgerTimelineCategoryDocIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-ledger-timeline-category-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "office-standard"), Path.Combine(root, "profiles", "office-standard"));
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "profiles", "team-pilot"), Path.Combine(root, "profiles", "team-pilot"));
            var smokePath = Path.Combine(root, "docs", "smoke", "v6.0", "ledger-timeline.md");
            File.WriteAllText(
                smokePath,
                File.ReadAllText(smokePath).Replace("category counts per bucket", "category filter support", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(FindRepositoryRoot());
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: root,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("category counts per bucket", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV60DeliverablesOutputParityDocIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v60-missing-deliverables-parity-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            CopyDirectory(Path.Combine(FindRepositoryRoot(), "docs"), Path.Combine(root, "docs"));
            var smokePath = Path.Combine(root, "docs", "smoke", "v6.0", "deliverables-verify.md");
            File.WriteAllText(
                smokePath,
                File.ReadAllText(smokePath)
                    .Replace("Kinds and Outcomes counts", "summary counts", StringComparison.Ordinal)
                    .Replace("table and Markdown", "human output", StringComparison.Ordinal));
            Directory.SetCurrentDirectory(root);
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: null,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v60LocalBimOpsContractGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("Kinds", StringComparison.OrdinalIgnoreCase) &&
                    check.GetProperty("evidence").GetString()!.Contains("Outcomes", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV52GapReportIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v52-missing-gap-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            var docsRoot = Path.Combine(root, "docs");
            Directory.CreateDirectory(docsRoot);
            File.WriteAllText(Path.Combine(docsRoot, "roadmap-v5-v6.md"), "# v5-v6\n");
            File.WriteAllText(
                Path.Combine(docsRoot, "v5-rc-readiness.md"),
                """
Current status:
Claimed live Revit years
Stable P0 Commands
Experimental / Deferred Commands
not live verified
v5RealSmokeDisclosure
issuePackageTraceability
v5FaultInjectionCoverage
release verify --strict
MCP, SaaS, or built-in LLM parser
""");
            Directory.CreateDirectory(Path.Combine(docsRoot, "smoke", "v5.0"));
            File.WriteAllText(
                Path.Combine(docsRoot, "smoke", "v5.0", "gap-report.md"),
                """
| Revit 2024 | not live verified |
| Revit 2025 | not live verified |
| Revit 2026 | not live verified |
""");
            Directory.CreateDirectory(Path.Combine(docsRoot, "smoke", "v5.1"));
            File.WriteAllText(
                Path.Combine(docsRoot, "smoke", "v5.1", "gap-report.md"),
                """
v5.1 sheet release control
production pilot gated
100 sheet
300 sheet
1000 sheet
not live verified
Revit 2026
dry-run/plan/receipt/rollback
journal verify
Post-rollback evidence
""");
            Directory.SetCurrentDirectory(root);
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: null,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v52SchedulePackagePilotGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("missing or unreadable", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV53GapReportIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v53-missing-gap-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            var docsRoot = Path.Combine(root, "docs");
            Directory.CreateDirectory(docsRoot);
            File.WriteAllText(Path.Combine(docsRoot, "roadmap-v5-v6.md"), "# v5-v6\n");
            File.WriteAllText(
                Path.Combine(docsRoot, "v5-rc-readiness.md"),
                """
Current status:
Claimed live Revit years
Stable P0 Commands
Experimental / Deferred Commands
not live verified
v5RealSmokeDisclosure
issuePackageTraceability
v5FaultInjectionCoverage
release verify --strict
MCP, SaaS, or built-in LLM parser
""");
            Directory.CreateDirectory(Path.Combine(docsRoot, "smoke", "v5.0"));
            File.WriteAllText(
                Path.Combine(docsRoot, "smoke", "v5.0", "gap-report.md"),
                """
| Revit 2024 | not live verified |
| Revit 2025 | not live verified |
| Revit 2026 | not live verified |
""");
            Directory.CreateDirectory(Path.Combine(docsRoot, "smoke", "v5.1"));
            File.WriteAllText(
                Path.Combine(docsRoot, "smoke", "v5.1", "gap-report.md"),
                """
v5.1 sheet release control
production pilot gated
100 sheet
300 sheet
1000 sheet
not live verified
Revit 2026
dry-run/plan/receipt/rollback
journal verify
Post-rollback evidence
""");
            Directory.CreateDirectory(Path.Combine(docsRoot, "smoke", "v5.2"));
            File.WriteAllText(
                Path.Combine(docsRoot, "smoke", "v5.2", "gap-report.md"),
                """
v5.2 schedule deliverable closure
schedule/package-only
explicit go-forward decision
not live verified
schedules batch-export
schedules compare
deliverables bundle
issue package
journal verify
""");
            Directory.SetCurrentDirectory(root);
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: null,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v53NumberingControlledApplyPilotGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("missing or unreadable", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Verify_Json_WithContractV2_FailsWhenV54GapReportIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-v54-missing-gap-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            var docsRoot = Path.Combine(root, "docs");
            Directory.CreateDirectory(docsRoot);
            File.WriteAllText(Path.Combine(docsRoot, "roadmap-v5-v6.md"), "# v5-v6\n");
            File.WriteAllText(
                Path.Combine(docsRoot, "v5-rc-readiness.md"),
                """
Current status:
Claimed live Revit years
Stable P0 Commands
Experimental / Deferred Commands
not live verified
v5RealSmokeDisclosure
issuePackageTraceability
v5FaultInjectionCoverage
release verify --strict
MCP, SaaS, or built-in LLM parser
""");
            Directory.CreateDirectory(Path.Combine(docsRoot, "smoke", "v5.0"));
            File.WriteAllText(
                Path.Combine(docsRoot, "smoke", "v5.0", "gap-report.md"),
                """
| Revit 2024 | not live verified |
| Revit 2025 | not live verified |
| Revit 2026 | not live verified |
""");
            Directory.CreateDirectory(Path.Combine(docsRoot, "smoke", "v5.1"));
            File.WriteAllText(
                Path.Combine(docsRoot, "smoke", "v5.1", "gap-report.md"),
                """
v5.1 sheet release control
production pilot gated
100 sheet
300 sheet
1000 sheet
not live verified
Revit 2026
dry-run/plan/receipt/rollback
journal verify
Post-rollback evidence
""");
            Directory.CreateDirectory(Path.Combine(docsRoot, "smoke", "v5.2"));
            File.WriteAllText(
                Path.Combine(docsRoot, "smoke", "v5.2", "gap-report.md"),
                """
v5.2 schedule deliverable closure
schedule/package-only
explicit go-forward decision
not live verified
schedules batch-export
schedules compare
deliverables bundle
issue package
journal verify
""");
            Directory.CreateDirectory(Path.Combine(docsRoot, "smoke", "v5.3"));
            File.WriteAllText(
                Path.Combine(docsRoot, "smoke", "v5.3", "gap-report.md"),
                """
v5.3 numbering controlled apply
explicit go-forward decision
reserved numbers
hold numbers
duplicate-target failure
not live verified
plan apply
rollback
journal verify
""");
            Directory.SetCurrentDirectory(root);
            var output = new StringWriter();

            var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(
                output,
                "json",
                projectDirectory: null,
                contractSchema: "workbench-contract.v2");

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains(
                document.RootElement.GetProperty("checks").EnumerateArray(),
                check => check.GetProperty("id").GetString() == "v54StandardsRuntimePackGate" &&
                    check.GetProperty("status").GetString() == "fail" &&
                    check.GetProperty("evidence").GetString()!.Contains("missing or unreadable", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
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
    public async Task Verify_UnknownContract_ReturnsFailureBeforeWritingVerification()
    {
        var output = new StringWriter();

        var exitCode = await WorkbenchCommand.ExecuteVerifyAsync(output, "json", contractSchema: "workbench-contract.v3");

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --contract must be 'workbench-contract.v1' or 'workbench-contract.v2'." + Environment.NewLine, output.ToString());
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
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "rooms renumber" &&
            path.GetProperty("command").GetString() == "rooms" &&
            path.GetProperty("dryRun").GetString()!.Contains("required", StringComparison.OrdinalIgnoreCase) &&
            path.GetProperty("receipt").GetString()!.Contains("room-numbering-plan.v1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "marks assign" &&
            path.GetProperty("command").GetString() == "marks" &&
            path.GetProperty("dryRun").GetString()!.Contains("required", StringComparison.OrdinalIgnoreCase) &&
            path.GetProperty("receipt").GetString()!.Contains("mark-assignment-plan.v1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "marks verify" &&
            path.GetProperty("command").GetString() == "marks" &&
            path.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "links repair" &&
            path.GetProperty("command").GetString() == "links" &&
            path.GetProperty("dryRun").GetString()!.Contains("required", StringComparison.OrdinalIgnoreCase) &&
            path.GetProperty("receipt").GetString()!.Contains("link-repair-plan.v1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paths, path =>
            path.GetProperty("path").GetString() == "model map-fix" &&
            path.GetProperty("command").GetString() == "model" &&
            path.GetProperty("dryRun").GetString()!.Contains("required", StringComparison.OrdinalIgnoreCase) &&
            path.GetProperty("receipt").GetString()!.Contains("model-map-fix-plan.v1", StringComparison.OrdinalIgnoreCase));
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
            extension.GetProperty("validationCommand").GetString()!.Contains("--manifest .revitcli/standards.yml", StringComparison.OrdinalIgnoreCase) &&
            extension.GetProperty("validationCommand").GetString()!.Contains("--dir profiles/office-standard", StringComparison.OrdinalIgnoreCase) &&
            extension.GetProperty("dryRunCommand").GetString()!.Contains("profiles/office-standard", StringComparison.OrdinalIgnoreCase) &&
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
            contract.GetProperty("commandPath").GetString() == "workbench contract --contract workbench-contract.v2" &&
            contract.GetProperty("jsonSchema").GetString() == "workbench-contract.v2" &&
            contract.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "workbench verify --contract workbench-contract.v2" &&
            contract.GetProperty("jsonSchema").GetString() == "workbench-verify-report.v2" &&
            contract.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "issue package" &&
            contract.GetProperty("jsonSchema").GetString() == "issue-package-receipt.v1" &&
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
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "rooms renumber" &&
            contract.GetProperty("jsonSchema").GetString() == "room-numbering-plan.v1" &&
            contract.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "marks assign" &&
            contract.GetProperty("jsonSchema").GetString() == "mark-assignment-plan.v1" &&
            contract.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "marks verify" &&
            contract.GetProperty("jsonSchema").GetString() == "mark-verify-report.v1" &&
            contract.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "links audit" &&
            contract.GetProperty("jsonSchema").GetString() == "link-audit-report.v1" &&
            contract.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "model map-fix" &&
            contract.GetProperty("jsonSchema").GetString() == "model-map-fix-plan.v1" &&
            contract.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "ledger query" &&
            contract.GetProperty("jsonSchema").GetString() == "ledger-query.v1" &&
            contract.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "ledger replay" &&
            contract.GetProperty("jsonSchema").GetString() == "ledger-replay.v1" &&
            contract.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "ledger append" &&
            contract.GetProperty("jsonSchema").GetString() == "ledger-append.v1" &&
            contract.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "ledger validate" &&
            contract.GetProperty("jsonSchema").GetString() == "ledger-validate.v1" &&
            contract.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "ledger stats" &&
            contract.GetProperty("jsonSchema").GetString() == "ledger-stats.v1" &&
            contract.GetProperty("supportsMarkdown").GetBoolean());
        Assert.Contains(outputs, contract =>
            contract.GetProperty("commandPath").GetString() == "ledger timeline" &&
            contract.GetProperty("jsonSchema").GetString() == "ledger-timeline.v1" &&
            contract.GetProperty("supportsMarkdown").GetBoolean());
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
        Assert.Contains(safeguards, safeguard =>
            safeguard.GetProperty("name").GetString() == "sheet-issue-meta" &&
            safeguard.GetProperty("dryRunCommand").GetString()!.Contains("sheets issue-meta", StringComparison.OrdinalIgnoreCase) &&
            safeguard.GetProperty("dryRunCommand").GetString()!.Contains("--plan-output", StringComparison.OrdinalIgnoreCase) &&
            safeguard.GetProperty("receipt").GetString()!.Contains("sheet-issue-plan.v1", StringComparison.OrdinalIgnoreCase) &&
            safeguard.GetProperty("receipt").GetString()!.Contains("plan-receipt.v1", StringComparison.OrdinalIgnoreCase) &&
            safeguard.GetProperty("reviewCommand").GetString()!.Contains("plan show", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(safeguards, safeguard =>
            safeguard.GetProperty("name").GetString() == "sheet-renumber" &&
            safeguard.GetProperty("dryRunCommand").GetString()!.Contains("--plan-output", StringComparison.OrdinalIgnoreCase) &&
            safeguard.GetProperty("receipt").GetString()!.Contains("plan-receipt.v1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(safeguards, safeguard =>
            safeguard.GetProperty("name").GetString() == "rooms-renumber" &&
            safeguard.GetProperty("dryRunCommand").GetString()!.Contains("rooms renumber", StringComparison.OrdinalIgnoreCase) &&
            safeguard.GetProperty("dryRunCommand").GetString()!.Contains("--plan-output", StringComparison.OrdinalIgnoreCase) &&
            safeguard.GetProperty("receipt").GetString()!.Contains("room-numbering-plan.v1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(safeguards, safeguard =>
            safeguard.GetProperty("name").GetString() == "marks-assign" &&
            safeguard.GetProperty("dryRunCommand").GetString()!.Contains("marks assign", StringComparison.OrdinalIgnoreCase) &&
            safeguard.GetProperty("dryRunCommand").GetString()!.Contains("--plan-output", StringComparison.OrdinalIgnoreCase) &&
            safeguard.GetProperty("receipt").GetString()!.Contains("mark-assignment-plan.v1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(safeguards, safeguard =>
            safeguard.GetProperty("name").GetString() == "links-repair" &&
            safeguard.GetProperty("dryRunCommand").GetString()!.Contains("links repair", StringComparison.OrdinalIgnoreCase) &&
            safeguard.GetProperty("dryRunCommand").GetString()!.Contains("--plan-output", StringComparison.OrdinalIgnoreCase) &&
            safeguard.GetProperty("reviewCommand").GetString()!.Contains("links audit", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(safeguards, safeguard =>
            safeguard.GetProperty("name").GetString() == "model-map-fix" &&
            safeguard.GetProperty("dryRunCommand").GetString()!.Contains("model map-fix", StringComparison.OrdinalIgnoreCase) &&
            safeguard.GetProperty("dryRunCommand").GetString()!.Contains("--plan-output", StringComparison.OrdinalIgnoreCase) &&
            safeguard.GetProperty("reviewCommand").GetString()!.Contains("model map-check", StringComparison.OrdinalIgnoreCase));
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
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-{Guid.NewGuid():N} $(touch hacked)'");
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
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-{Guid.NewGuid():N} $(touch hacked)'");
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
                action.GetProperty("workingDirectory").GetString() == Path.GetFullPath(root));
            Assert.Contains(readinessActions, action =>
                action.GetProperty("phase").GetString() == "draft-knowledge-report" &&
                action.GetProperty("commandLine").GetString()!.Contains("revitcli report knowledge", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(readinessActions, action =>
                action.GetProperty("phase").GetString() == "review-saved-plans" &&
                action.GetProperty("artifact").GetString() == "plans" &&
                action.GetProperty("commandLine").GetString()!.Contains("revitcli inspect plans", StringComparison.OrdinalIgnoreCase));

            var commands = rootElement.GetProperty("commands").EnumerateArray().ToArray();
            Assert.Contains(commands, command =>
                command.GetProperty("phase").GetString() == "verify" &&
                command.GetProperty("commandLine").GetString()!.Contains("revitcli workbench verify", StringComparison.OrdinalIgnoreCase) &&
                command.GetProperty("workingDirectory").GetString() == Path.GetFullPath(root));
            Assert.Contains(commands, command =>
                command.GetProperty("phase").GetString() == "project" &&
                command.GetProperty("commandLine").GetString()!.Contains("revitcli workbench project", StringComparison.OrdinalIgnoreCase) &&
                command.GetProperty("workingDirectory").GetString() == Path.GetFullPath(root));
            Assert.Contains(commands, command =>
                command.GetProperty("phase").GetString() == "workflow-review" &&
                command.GetProperty("commandLine").GetString()!.Contains("workflow review", StringComparison.OrdinalIgnoreCase) &&
                command.GetProperty("workingDirectory").GetString() == Path.GetFullPath(root));
            Assert.Contains(commands, command =>
                command.GetProperty("phase").GetString() == "workflow-discovery" &&
                command.GetProperty("commandLine").GetString()!.Contains("revitcli inspect workflows", StringComparison.OrdinalIgnoreCase) &&
                command.GetProperty("workingDirectory").GetString() == Path.GetFullPath(root));
            Assert.Contains(commands, command =>
                command.GetProperty("phase").GetString() == "plan-discovery" &&
                command.GetProperty("commandLine").GetString()!.Contains("revitcli inspect plans", StringComparison.OrdinalIgnoreCase) &&
                command.GetProperty("workingDirectory").GetString() == Path.GetFullPath(root));
            Assert.All(commands, command =>
                Assert.Equal(Path.GetFullPath(root), command.GetProperty("workingDirectory").GetString()));
            Assert.All(
                readinessActions.Select(action => action.GetProperty("commandLine").GetString()!)
                    .Concat(commands.Select(command => command.GetProperty("commandLine").GetString()!)),
                line =>
                {
                    Assert.DoesNotContain(Path.GetFullPath(root), line, StringComparison.Ordinal);
                    Assert.DoesNotContain("$(touch hacked)", line, StringComparison.Ordinal);
                });
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
            Assert.Contains("Working directory", text);
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
            Assert.Contains("| Phase | Command | Working directory | Purpose |", text);
            Assert.Contains("| `project` | `revitcli workbench project --dir", text);
            Assert.Contains("| `workflow-discovery` | `revitcli inspect workflows --dir", text);
            Assert.Contains("| `plan-discovery` | `revitcli inspect plans --dir", text);
            Assert.Contains($"`{Path.GetFullPath(root)}`", text);
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

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "RevitCli", "RevitCli.csproj")) &&
                Directory.Exists(Path.Combine(dir.FullName, "docs")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
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
        {
            var path = Path.Combine(root, "docs", "smoke", "v6.0", $"{pilotId}.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, CompletedPilotEvidencePacketContent(pilotId));
        }
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
}
