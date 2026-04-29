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
        Assert.Contains("rollback", names);
        Assert.Contains("journal", names);
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
        Assert.Contains("journal", names);
    }

    [Fact]
    public void InteractiveHelpEntries_IncludeRollback()
    {
        Assert.Contains(
            CliCommandCatalog.InteractiveHelpEntries,
            entry => entry.Command == "rollback <baseline>" &&
                     entry.Description == "Restore parameters changed by a fix baseline");
    }
}
