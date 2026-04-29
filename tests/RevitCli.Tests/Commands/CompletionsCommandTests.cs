using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using RevitCli.Commands;
using Xunit;

namespace RevitCli.Tests.Commands;

[Collection("Sequential")]
public class CompletionsCommandTests : IDisposable
{
    private readonly TextWriter _savedOut;
    private readonly TextWriter _savedError;

    public CompletionsCommandTests()
    {
        _savedOut = Console.Out;
        _savedError = Console.Error;
    }

    public void Dispose()
    {
        Console.SetOut(_savedOut);
        Console.SetError(_savedError);
    }

    [Fact]
    public async Task Bash_CompletionsIncludeCommandAndValueSuggestions()
    {
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        var exitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "bash" });
        var script = stdout.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("status", script);
        Assert.Contains("query", script);
        Assert.Contains("export", script);
        Assert.Contains("set", script);
        Assert.Contains("config", script);
        Assert.Contains("audit", script);
        Assert.Contains("completions", script);
        Assert.Contains("batch", script);
        Assert.Contains("doctor", script);
        Assert.Contains("check", script);
        Assert.Contains("fix", script);
        Assert.Contains("rollback", script);
        Assert.Contains("publish", script);
        Assert.Contains("init", script);
        Assert.Contains("score", script);
        Assert.Contains("coverage", script);
        Assert.Contains("schedule", script);
        Assert.Contains("diff", script);
        Assert.Contains("snapshot", script);
        Assert.Contains("interactive", script);
        Assert.Contains("compgen -W \"show set\"", script);
        Assert.Contains("defaultOutput)", script);
        Assert.Contains("compgen -f -- \"$cur\"", script);
        Assert.Contains("--profile", script);
        Assert.Contains("--rule", script);
        Assert.Contains("--severity", script);
        Assert.Contains("--dry-run", script);
        Assert.Contains("--yes", script);
        Assert.Contains("--max-changes", script);
        Assert.Contains("--baseline-output", script);
        Assert.Contains("--no-snapshot", script);
        var rollbackBlock = ExtractBlock(
            script,
            "        rollback)",
            "        status|doctor|interactive)");
        Assert.Contains("compgen -f -- \"$cur\"", rollbackBlock);
        Assert.Contains("--dry-run --yes --max-changes", rollbackBlock);
    }

    [Fact]
    public async Task Zsh_CompletionsIncludeDoctorBatchAndConfigKeys()
    {
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        var exitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "zsh" });
        var script = stdout.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("'doctor:Check RevitCli setup and diagnose issues'", script);
        Assert.Contains("'interactive:Enter interactive REPL mode'", script);
        Assert.Contains("_arguments '1:file:_files'", script);
        var rollbackBlock = ExtractBlock(
            script,
            "                rollback)",
            "                import)");
        Assert.Contains("'1:baseline file:_files'", rollbackBlock);
        Assert.Contains("--dry-run[Preview rollback without applying]", rollbackBlock);
        Assert.Contains("--yes[Confirm rollback apply in non-interactive mode]", rollbackBlock);
        Assert.Contains("--max-changes[Maximum number of rollback writes]", rollbackBlock);
    }

    [Fact]
    public async Task PowerShell_CompletionsIncludeTopLevelCommandsAndConfigSuggestions()
    {
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        var exitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "powershell" });
        var script = stdout.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("'doctor' = 'Check RevitCli setup and diagnose issues'", script);
        Assert.Contains("'interactive' = 'Enter interactive REPL mode'", script);
        Assert.Contains("'rollback' = 'Restore parameters changed by a fix baseline'", script);
        Assert.Contains("Test-Path -LiteralPath $parent", script);
        Assert.Contains("-ErrorAction SilentlyContinue", script);
        Assert.Contains(
            "$configKeys = @('serverUrl', 'defaultOutput', 'exportDir', 'Revit2024InstallDir', 'Revit2025InstallDir', 'Revit2026InstallDir')",
            script);
        Assert.Contains("New-RevitCliCompletionResults -Values $shells -ToolTip 'Shell'", script);
        var rollbackOptionsBlock = ExtractBlock(
            script,
            "        'rollback' = @(",
            "        'publish' = @(");
        Assert.Contains("'--dry-run', '--yes', '--max-changes'", rollbackOptionsBlock);
        var rollbackSwitchBlock = ExtractBlock(
            script,
            "        'rollback' {",
            "        'import' {");
        Assert.Contains("New-RevitCliFileCompletionResults -Path $wordToComplete", rollbackSwitchBlock);
    }

    [Fact]
    public async Task BashCompletions_Include_Snapshot_And_Diff()
    {
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        var exitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "bash" });
        var script = stdout.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("snapshot", script);
        Assert.Contains("diff", script);
    }

    [Fact]
    public async Task ZshCompletions_Include_Snapshot_And_Diff()
    {
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        var exitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "zsh" });
        var script = stdout.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("snapshot", script);
        Assert.Contains("diff", script);
    }

    [Fact]
    public async Task BashCompletions_PublishIncludes_SinceFlags()
    {
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        var exitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "bash" });
        var script = stdout.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("--since", script);
        Assert.Contains("--since-mode", script);
        Assert.Contains("--update-baseline", script);
    }

    [Fact]
    public async Task ZshCompletions_PublishIncludes_SinceFlags()
    {
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        var exitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "zsh" });
        var script = stdout.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("--since", script);
        Assert.Contains("--since-mode", script);
    }

    [Fact]
    public async Task BashCompletions_Include_ImportFlags()
    {
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        var exitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "bash" });
        var script = stdout.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("import)", script);
        Assert.Contains("--match-by", script);
        Assert.Contains("--on-missing", script);
        Assert.Contains("--on-duplicate", script);
        Assert.Contains("--encoding", script);
        Assert.Contains("error warn skip", script);
        Assert.Contains("error first all", script);
        Assert.Contains("auto utf-8 gbk", script);
    }

    [Fact]
    public async Task ZshCompletions_Include_ImportFlags()
    {
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        var exitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "zsh" });
        var script = stdout.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("import)", script);
        Assert.Contains("--match-by[", script);
        Assert.Contains("(error warn skip)", script);
        Assert.Contains("(auto utf-8 gbk)", script);
    }

    [Fact]
    public async Task PwshCompletions_Include_ImportFlags()
    {
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        var exitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "powershell" });
        var script = stdout.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("'import'", script);
        Assert.Contains("'--match-by'", script);
        Assert.Contains("'--encoding'", script);
        Assert.Contains("'auto', 'utf-8', 'gbk'", script);
    }

    private static string ExtractBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return text.Substring(start, end - start);
    }
}
