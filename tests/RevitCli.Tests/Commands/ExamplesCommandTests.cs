using System.Text.Json;
using RevitCli.Commands;

namespace RevitCli.Tests.Commands;

public sealed class ExamplesCommandTests
{
    [Fact]
    public async Task Execute_NoTopic_ListsAvailableTopics()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, topic: null);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Available example topics:", text);
        Assert.Contains("inspect", text);
        Assert.Contains("sheets", text);
        Assert.Contains("rooms", text);
        Assert.Contains("marks", text);
        Assert.Contains("schedules", text);
        Assert.Contains("views", text);
        Assert.Contains("links", text);
        Assert.Contains("model", text);
        Assert.Contains("workflow", text);
        Assert.Contains("workbench", text);
        Assert.Contains("issue", text);
        Assert.Contains("report", text);
        Assert.Contains("deliverables", text);
        Assert.Contains("standards", text);
        Assert.Contains("family", text);
        Assert.Contains("release", text);
        Assert.Contains("recipes", text);
        Assert.Contains("Run: revitcli examples <topic>", text);
    }

    [Fact]
    public async Task Execute_KnownTopic_PrintsCommandsAndPrompt()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "sheets");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# sheets", text);
        Assert.Contains("revitcli inspect sheets --issues-only --output markdown", text);
        Assert.Contains("revitcli sheets verify --output json --issues-only", text);
        Assert.Contains("revitcli sheets index init", text);
        Assert.Contains("revitcli export --format pdf --sheets \"A1*\" --dry-run", text);
        Assert.Contains("Codex prompt:", text);
    }

    [Fact]
    public async Task Execute_IssueTopic_PrintsClosureWorkflow()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "issue");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# issue", text);
        Assert.Contains("revitcli issue preflight --profile .revitcli/issue.yml --output markdown --fail-on warning", text);
        Assert.Contains("revitcli issue diff --from .revitcli/history/baseline.json --to current --review --output markdown", text);
        Assert.Contains("revitcli issue package --profile .revitcli/issue.yml --bundle-path deliverables/issue-package.zip --dry-run --sign-journal --include-receipts true --output markdown", text);
        Assert.Contains("revitcli workbench verify --contract workbench-contract.v2 --dir . --output json", text);
        Assert.Contains("hidden-mutation blockers", text);
    }

    [Fact]
    public async Task Execute_RoomsTopic_PrintsRenumberPlanWorkflow()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "rooms");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# rooms", text);
        Assert.Contains("revitcli inspect params rooms --writable-only --missing-only", text);
        Assert.Contains("revitcli rooms renumber --rule .revitcli/numbering/rooms.yml --scope all --plan-output .revitcli/plans/room-numbering.json --dry-run --output markdown", text);
        Assert.Contains("revitcli plan apply .revitcli/plans/room-numbering.json --yes --max-changes 500", text);
        Assert.Contains("deterministic room-numbering plan", text);
    }

    [Fact]
    public async Task Execute_MarksTopic_PrintsAssignAndVerifyWorkflow()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "marks");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# marks", text);
        Assert.Contains("revitcli marks verify --category doors,windows --output markdown", text);
        Assert.Contains("revitcli marks assign --category doors --rule .revitcli/numbering/doors.yml --plan-output .revitcli/plans/door-marks.json --dry-run --output markdown", text);
        Assert.Contains("deterministic assignment plan", text);
    }

    [Fact]
    public async Task Execute_InspectTopic_PrintsParamFilters()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "inspect");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("revitcli inspect params doors --writable-only --missing-only", text);
        Assert.Contains("revitcli inspect schedules --issues-only --output markdown", text);
        Assert.Contains("revitcli inspect sheets --issues-only --output markdown", text);
        Assert.Contains("revitcli inspect workflows --output markdown", text);
        Assert.Contains("revitcli inspect plans --output markdown", text);
    }

    [Fact]
    public async Task Execute_SetTopic_PrintsFilteredParamDiscovery()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "set");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("revitcli inspect params doors --name \"Fire*\" --writable-only --missing-only", text);
        Assert.Contains("revitcli plan apply .revitcli/plans/fire-rating.json --yes --max-changes 250 --high-impact-threshold 50 --confirm-high-impact", text);
        Assert.Contains("revitcli rollback .revitcli/plans/fire-rating.json.receipt.json --dry-run", text);
        Assert.Contains("revitcli rollback .revitcli/plans/fire-rating.json.receipt.json --yes --max-changes 250", text);
        Assert.Contains("summarize it in Chinese before apply", text);
    }

    [Fact]
    public async Task Execute_ScheduleTopic_PrintsInspectFilters()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "schedule");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("revitcli inspect schedules --category Doors --ready-only", text);
        Assert.Contains("revitcli inspect schedules --empty-only", text);
        Assert.Contains("revitcli inspect schedules --issues-only --output markdown", text);
        Assert.Contains("revitcli schedule list --output markdown", text);
        Assert.Contains("revitcli schedule export --name \"Door Schedule\" --output csv", text);
        Assert.Contains("revitcli schedule export --name \"Door Schedule\" --output markdown", text);
        Assert.Contains("revitcli schedule create --category Doors --fields \"Mark,Level\" --name \"Door Review\" --dry-run --output json", text);
    }

    [Fact]
    public async Task Execute_SchedulesTopic_PrintsEnsureExportAndCompareWorkflow()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "schedules");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# schedules", text);
        Assert.Contains("revitcli schedules ensure --spec .revitcli/schedules/issue.yml --plan-output .revitcli/plans/schedule-ensure.json --dry-run --mode create-only --output markdown", text);
        Assert.Contains("revitcli schedules batch-export --set issue --output-dir exports/schedules/current --format csv --manifest exports/schedules/current/manifest.json --output json", text);
        Assert.Contains("revitcli schedules compare --from exports/schedules/baseline --to exports/schedules/current --keys Number,Mark --output markdown", text);
    }

    [Fact]
    public async Task Execute_ViewsTopic_PrintsAuditTemplateAndCloneWorkflow()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "views");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# views", text);
        Assert.Contains("revitcli views audit --rules .revitcli/views/standards.yml --templates --browser --output markdown", text);
        Assert.Contains("revitcli views template-apply --selector \"Level*\" --template \"Architectural Plan\" --plan-output .revitcli/plans/view-template.json --dry-run --output markdown", text);
        Assert.Contains("revitcli views clone-set --from-set \"Level*\" --to-prefix \"Tender - \" --naming-rule \"{prefix}{name}\" --plan-output .revitcli/plans/view-clone.json --dry-run --output json", text);
    }

    [Fact]
    public async Task Execute_LinksTopic_PrintsAuditAndRepairWorkflow()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "links");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# links", text);
        Assert.Contains("revitcli links audit --rules .revitcli/links/rules.yml --check paths,loaded,coordinates --output markdown", text);
        Assert.Contains("revitcli links repair --map .revitcli/links/paths.yml --plan-output .revitcli/plans/link-repair.json --dry-run --max-changes 20 --output json", text);
        Assert.Contains("without coordinate moves", text);
    }

    [Fact]
    public async Task Execute_ModelTopic_PrintsMapCheckAndFixWorkflow()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "model");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# model", text);
        Assert.Contains("revitcli model map-check --against .revitcli/model-mapping.yml --worksets --phases --output markdown", text);
        Assert.Contains("revitcli model map-fix --against .revitcli/model-mapping.yml --scope rooms,doors,walls --plan-output .revitcli/plans/model-map-fix.json --dry-run --output json", text);
        Assert.Contains("write prechecks", text);
    }

    [Fact]
    public async Task Execute_JournalTopic_PrintsReviewAndIntegrityCommands()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "journal");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("revitcli journal show --limit 10", text);
        Assert.Contains("revitcli journal stats", text);
        Assert.Contains("revitcli journal review --output markdown", text);
        Assert.Contains("revitcli journal sign", text);
        Assert.Contains("revitcli journal verify", text);
    }

    [Fact]
    public async Task Execute_ReviewTopic_PrintsDiffReviewCommands()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "review");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("revitcli diff .revitcli/snap-before.json .revitcli/snap-after.json --review", text);
        Assert.Contains("revitcli history diff @-2 @-1 --review", text);
        Assert.Contains("Codex prompt:", text);
    }

    [Fact]
    public async Task Execute_WorkflowTopic_PrintsValidationAndSimulationCommands()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "workflow");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("revitcli workflow init pre-issue", text);
        Assert.Contains("revitcli workflow init all", text);
        Assert.Contains("revitcli workflow validate", text);
        Assert.Contains("revitcli workflow simulate .revitcli/workflows/pre-issue.yml", text);
        Assert.Contains("revitcli workflow review .revitcli/workflows/pre-issue.yml --output markdown", text);
        Assert.Contains("revitcli workflow run .revitcli/workflows/pre-issue.yml --dry-run", text);
        Assert.Contains("revitcli workflow suggest --output yaml", text);
        Assert.Contains("revitcli workflow receipts --output markdown", text);
        Assert.Contains("revitcli workflow receipts --min-duration-ms 60000 --output markdown", text);
        Assert.Contains("revitcli workflow receipts --sort duration --output json", text);
        Assert.Contains("revitcli workflow receipts --window 24h --sort duration --output markdown", text);
        Assert.Contains("revitcli workflow examples export-package --output markdown", text);
        Assert.Contains("Codex prompt:", text);
    }

    [Fact]
    public async Task Execute_WorkbenchTopic_PrintsContractAndVerifierCommands()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "workbench");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# workbench", text);
        Assert.Contains("revitcli workbench contract --output json", text);
        Assert.Contains("revitcli workbench verify --output markdown", text);
        Assert.Contains("revitcli workbench receipts --output json", text);
        Assert.Contains("revitcli workbench paths --output json", text);
        Assert.Contains("revitcli workbench exits --output json", text);
        Assert.Contains("revitcli workbench extensions --output json", text);
        Assert.Contains("revitcli workbench outputs --output json", text);
        Assert.Contains("revitcli workbench safeguards --output json", text);
        Assert.Contains("revitcli workbench project --output json", text);
        Assert.Contains("revitcli workbench handoff --output markdown", text);
        Assert.Contains("revitcli score --history 30d --output json", text);
        Assert.Contains("revitcli examples workflow --output json", text);
        Assert.Contains("revitcli workflow review .revitcli/workflows/pre-issue.yml --output markdown", text);
        Assert.Contains("Codex prompt:", text);
    }

    [Fact]
    public async Task Execute_ReportTopic_PrintsWeeklyReportCommands()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "report");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("revitcli report weekly", text);
        Assert.Contains("revitcli report weekly --report .revitcli/reports/weekly.md", text);
        Assert.Contains("Codex prompt:", text);
    }

    [Fact]
    public async Task Execute_DeliverablesTopic_PrintsManifestReviewCommands()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "deliverables");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("revitcli deliverables list", text);
        Assert.Contains("revitcli deliverables stats", text);
        Assert.Contains("revitcli deliverables verify --output json", text);
        Assert.Contains("revitcli deliverables verify --output markdown", text);
        Assert.Contains("revitcli deliverables bundle --dry-run --output markdown", text);
        Assert.Contains("Codex prompt:", text);
    }

    [Fact]
    public async Task Execute_StandardsTopic_PrintsValidationCommands()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "standards");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("revitcli standards install ../office-standards --dry-run", text);
        Assert.Contains("revitcli standards validate --output markdown", text);
        Assert.Contains("revitcli workflow validate", text);
        Assert.Contains("Codex prompt:", text);
    }

    [Fact]
    public async Task Execute_FamilyTopic_PrintsPurgeReportCommands()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "family");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("revitcli family ls --unused", text);
        Assert.Contains("revitcli family validate --rules-from .revitcli/standards.yml", text);
        Assert.Contains("revitcli family purge --dry-run --report .revitcli/reports/family-purge.json", text);
        Assert.Contains("Codex prompt:", text);
    }

    [Fact]
    public async Task Execute_RecipesTopic_PrintsTemplatePaths()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "recipes");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("docs/templates/codex-recipes/issue-day.md", text);
        Assert.Contains("docs/templates/codex-recipes/pre-issue.md", text);
        Assert.Contains("docs/templates/codex-recipes/standards-bootstrap.md", text);
        Assert.Contains("docs/templates/codex-recipes/family-cleanup.md", text);
        Assert.Contains("docs/templates/codex-recipes/release-preflight.md", text);
        Assert.Contains("docs/templates/codex-recipes/sheet-frame-verify.md", text);
        Assert.Contains("revitcli workflow suggest --output yaml", text);
        Assert.Contains("Codex prompt:", text);
    }

    [Fact]
    public async Task Execute_OutputJson_PrintsRecipeEnvelopeForTopic()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "workflow", "json");

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal("example-recipes.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("workflow", root.GetProperty("topic").GetString());
        var topic = Assert.Single(root.GetProperty("topics").EnumerateArray());
        Assert.Equal("workflow", topic.GetProperty("name").GetString());
        Assert.Contains(
            topic.GetProperty("commands").EnumerateArray(),
            command => command.GetString() == "revitcli workflow simulate .revitcli/workflows/pre-issue.yml --output json");
        Assert.Contains("risk modes", topic.GetProperty("codexPrompt").GetString());
    }

    [Fact]
    public async Task Execute_OutputJson_NoTopic_PrintsAllRecipeTopics()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, null, "json");

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal("example-recipes.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("topic").ValueKind);
        var topics = root.GetProperty("topics").EnumerateArray().ToArray();
        Assert.Contains(topics, topic => topic.GetProperty("name").GetString() == "publish");
        Assert.Contains(topics, topic => topic.GetProperty("name").GetString() == "journal");
        Assert.Contains(topics, topic => topic.GetProperty("name").GetString() == "rooms");
        Assert.Contains(topics, topic => topic.GetProperty("name").GetString() == "marks");
        Assert.Contains(topics, topic => topic.GetProperty("name").GetString() == "schedules");
        Assert.Contains(topics, topic => topic.GetProperty("name").GetString() == "views");
        Assert.Contains(topics, topic => topic.GetProperty("name").GetString() == "links");
        Assert.Contains(topics, topic => topic.GetProperty("name").GetString() == "model");
    }

    [Fact]
    public async Task Execute_OutputMarkdown_PrintsHandoffRecipe()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "deliverables", "markdown");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# deliverables", text);
        Assert.Contains("## Commands", text);
        Assert.Contains("- `revitcli deliverables bundle --dry-run --output markdown`", text);
        Assert.Contains("## Codex Prompt", text);
    }

    [Fact]
    public async Task Execute_UnknownOutput_ReturnsFailure()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "workflow", "yaml");

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Execute_ReleaseTopic_PrintsReleaseVerifyAndSmokeCommands()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "release");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("revitcli release verify --tag v2.3.0 --output json", text);
        Assert.Contains("revitcli release verify --tag v2.3.0 --output markdown", text);
        Assert.Contains(".\\scripts\\smoke-revit.ps1 -Version 2026", text);
        Assert.Contains("revitcli journal verify", text);
        Assert.Contains("Codex prompt:", text);
    }

    [Fact]
    public async Task Execute_UnknownTopic_ReturnsFailureAndListsTopics()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "mcp");

        var text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown example topic: mcp", text);
        Assert.Contains("Available:", text);
        Assert.Contains("inspect", text);
    }
}
