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
        Assert.Contains("workflow", names);
        Assert.Contains("report", names);
        Assert.Contains("deliverables", names);
        Assert.Contains("standards", names);
        Assert.Contains("release", names);
        Assert.Contains("sheets", names);
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
        Assert.Contains("workflow", names);
        Assert.Contains("report", names);
        Assert.Contains("deliverables", names);
        Assert.Contains("standards", names);
        Assert.Contains("release", names);
        Assert.Contains("sheets", names);
        Assert.Contains("journal", names);
        Assert.DoesNotContain("mcp", names);
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
            entry => entry.Command == "rollback <baseline>" &&
                     entry.Description == "Restore parameters changed by a fix baseline");
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
