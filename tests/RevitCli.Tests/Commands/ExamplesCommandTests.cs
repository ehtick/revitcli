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
        Assert.Contains("revitcli inspect sheets --issues-only", text);
        Assert.Contains("revitcli export --format pdf --sheets \"A1*\" --dry-run", text);
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
