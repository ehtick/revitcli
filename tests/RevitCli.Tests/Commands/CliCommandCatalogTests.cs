using System.CommandLine;
using System.Linq;
using System.Net.Http;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Config;
using RevitCli.Tests.Client;
using Xunit;

namespace RevitCli.Tests.Commands;

public class CliCommandCatalogTests
{
    private static RevitClient CreateClient()
    {
        var handler = new FakeHttpHandler("{}");
        return new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
    }

    [Fact]
    public void MainRoot_IncludesInteractiveAndBatchCommands()
    {
        var root = CliCommandCatalog.CreateRootCommand(
            CreateClient(),
            new CliConfig(),
            includeInteractiveCommand: true,
            includeBatchCommand: true);

        var names = root.Subcommands.Select(command => command.Name).ToArray();

        Assert.Contains("interactive", names);
        Assert.Contains("batch", names);
        Assert.Contains("completions", names);
        Assert.Contains("doctor", names);
        Assert.Contains("fix", names);
        Assert.Contains("inspect", names);
        Assert.Contains("examples", names);
        Assert.Contains("workbench", names);
        Assert.Contains("workflow", names);
        Assert.Contains("report", names);
        Assert.Contains("deliverables", names);
        Assert.Contains("standards", names);
        Assert.Contains("release", names);
        Assert.Contains("sheets", names);
        Assert.Contains("views", names);
        Assert.Contains("links", names);
        Assert.Contains("model", names);
        Assert.Contains("rollback", names);
        Assert.Contains("journal", names);
        Assert.Contains("mcp", names);
    }

    [Fact]
    public void InteractiveRoot_ExcludesSelfButKeepsBatchAndCompletions()
    {
        var root = CliCommandCatalog.CreateRootCommand(
            CreateClient(),
            new CliConfig(),
            includeInteractiveCommand: false,
            includeBatchCommand: true);

        var names = root.Subcommands.Select(command => command.Name).ToArray();

        Assert.DoesNotContain("interactive", names);
        Assert.Contains("batch", names);
        Assert.Contains("completions", names);
    }

    [Fact]
    public void BatchRoot_ExcludesRecursiveEntryPoints()
    {
        var root = CliCommandCatalog.CreateRootCommand(
            CreateClient(),
            new CliConfig(),
            includeInteractiveCommand: false,
            includeBatchCommand: false);

        var names = root.Subcommands.Select(command => command.Name).ToArray();

        Assert.DoesNotContain("interactive", names);
        Assert.DoesNotContain("batch", names);
        Assert.Contains("completions", names);
        Assert.Contains("fix", names);
        Assert.Contains("rollback", names);
        Assert.Contains("deliverables", names);
        Assert.Contains("release", names);
        Assert.Contains("sheets", names);
        Assert.Contains("views", names);
        Assert.Contains("links", names);
        Assert.Contains("model", names);
        Assert.Contains("journal", names);
    }

    [Fact]
    public void TopLevelCommands_Contains_SnapshotAndDiff()
    {
        var names = CliCommandCatalog.TopLevelCommandNames;
        Assert.Contains("fix", names);
        Assert.Contains("rollback", names);
        Assert.Contains("snapshot", names);
        Assert.Contains("diff", names);
        Assert.Contains("inspect", names);
        Assert.Contains("examples", names);
        Assert.Contains("workbench", names);
        Assert.Contains("workflow", names);
        Assert.Contains("report", names);
        Assert.Contains("deliverables", names);
        Assert.Contains("standards", names);
        Assert.Contains("release", names);
        Assert.Contains("sheets", names);
        Assert.Contains("views", names);
        Assert.Contains("links", names);
        Assert.Contains("model", names);
        Assert.Contains("journal", names);
        Assert.DoesNotContain("mcp", names);
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkbenchContract()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workbench contract" &&
                     entry.Description.Contains("exit-code"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkbenchVerify()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workbench verify" &&
                     entry.Description.Contains("recipe"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeReleasePilotScaffold()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "release pilot scaffold" &&
                     entry.Description.Contains("evidence packet scaffold"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeReleasePilotValidate()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "release pilot validate" &&
                     entry.Description.Contains("evidence packet"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeReleasePilotRegister()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "release pilot register" &&
                     entry.Description.Contains("rollout status"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeReleasePilotStatus()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "release pilot status" &&
                     entry.Description.Contains("rollout evidence status"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkbenchReceipts()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workbench receipts" &&
                     entry.Description.Contains("schema"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkbenchPaths()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workbench paths" &&
                     entry.Description.Contains("callable"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkbenchExits()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workbench exits" &&
                     entry.Description.Contains("exit-code"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkbenchExtensions()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workbench extensions" &&
                     entry.Description.Contains("extension"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkbenchOutputs()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workbench outputs" &&
                     entry.Description.Contains("JSON"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkbenchSafeguards()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workbench safeguards" &&
                     entry.Description.Contains("dry-run"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkbenchProject()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workbench project" &&
                     entry.Description.Contains("artifacts"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkbenchHandoff()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workbench handoff" &&
                     entry.Description.Contains("handoff"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeInspectWorkflows()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "inspect workflows" &&
                     entry.Description.Contains("workflow YAML"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeInspectPlans()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "inspect plans" &&
                     entry.Description.Contains("saved mutation plans", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeCoordinationCommands()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "links audit" &&
                     entry.Description.Contains("coordinate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "model map-fix" &&
                     entry.Description.Contains("writable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RootCommand_KeepsMcpAsHiddenCompatibilityCommand()
    {
        var root = CliCommandCatalog.CreateRootCommand(
            CreateClient(),
            new CliConfig(),
            includeInteractiveCommand: true,
            includeBatchCommand: true);

        var mcp = root.Subcommands.Single(command => command.Name == "mcp");
        Assert.True(mcp.IsHidden);
        var serve = Assert.Single(mcp.Subcommands);
        Assert.Equal("serve", serve.Name);
        Assert.True(serve.IsHidden);
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeExamples()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "examples <topic>" &&
                     entry.Description == "Show copy-paste commands for common workflows");
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeRollback()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "rollback <artifact>" &&
                     entry.Description == "Restore parameters from a fix baseline or plan receipt");
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeDeliverablesBundle()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "deliverables bundle" &&
                     entry.Description.Contains("zip"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeDiffReview()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "diff <from> <to>" &&
                     entry.Description.Contains("--review"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkflowSimulation()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workflow simulate <file>" &&
                     entry.Description.Contains("risk modes"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkflowReview()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workflow review <file>" &&
                     entry.Description.Contains("handoff evidence"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkflowInit()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workflow init <template>" &&
                     entry.Description.Contains("built-in templates"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkflowRun()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workflow run <file>" &&
                     entry.Description.Contains("--yes"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkflowSuggest()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workflow suggest" &&
                     entry.Description.Contains("journal"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkflowExamples()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workflow examples" &&
                     entry.Description.Contains("architect prompts"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeWorkflowReceipts()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "workflow receipts" &&
                     entry.Description.Contains("receipts"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeJournalReview()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "journal review" &&
                     entry.Description.Contains("risk"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeReportWeekly()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "report weekly" &&
                     entry.Description.Contains("journal"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeDeliverablesVerify()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "deliverables verify" &&
                     entry.Description.Contains("receipts"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeFamilyValidateRulesFrom()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "family validate" &&
                     entry.Description.Contains("--rules-from"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeStandardsValidate()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "standards validate" &&
                     entry.Description.Contains("workflows"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeReleaseVerify()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "release verify" &&
                     entry.Description.Contains("version"));
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeSheetsVerify()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "sheets verify" &&
                     entry.Description.Contains("numbering"));
    }
}
