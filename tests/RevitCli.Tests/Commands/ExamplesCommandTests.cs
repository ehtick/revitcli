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
        Assert.Contains("workflow", text);
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
    public async Task Execute_InspectTopic_PrintsParamFilters()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "inspect");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("revitcli inspect params doors --writable-only --missing-only", text);
        Assert.Contains("revitcli inspect schedules --issues-only --output markdown", text);
        Assert.Contains("revitcli inspect sheets --issues-only --output markdown", text);
    }

    [Fact]
    public async Task Execute_SetTopic_PrintsFilteredParamDiscovery()
    {
        var output = new StringWriter();

        var exitCode = await ExamplesCommand.ExecuteAsync(output, "set");

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("revitcli inspect params doors --name \"Fire*\" --writable-only --missing-only", text);
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
        Assert.Contains("revitcli workflow run .revitcli/workflows/pre-issue.yml --dry-run", text);
        Assert.Contains("revitcli workflow suggest --output yaml", text);
        Assert.Contains("revitcli workflow receipts --output markdown", text);
        Assert.Contains("revitcli workflow examples export-package --output markdown", text);
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
        Assert.Contains("docs/templates/codex-recipes/pre-issue.md", text);
        Assert.Contains("docs/templates/codex-recipes/standards-bootstrap.md", text);
        Assert.Contains("docs/templates/codex-recipes/family-cleanup.md", text);
        Assert.Contains("docs/templates/codex-recipes/release-preflight.md", text);
        Assert.Contains("docs/templates/codex-recipes/sheet-frame-verify.md", text);
        Assert.Contains("revitcli workflow suggest --output yaml", text);
        Assert.Contains("Codex prompt:", text);
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
