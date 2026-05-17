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
        Assert.Contains("status)", script);
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
        Assert.Contains("examples", script);
        Assert.Contains("workflow", script);
        Assert.Contains("report", script);
        Assert.Contains("deliverables", script);
        Assert.Contains("standards", script);
        Assert.Contains("release", script);
        Assert.Contains("sheets", script);
        Assert.Contains("diff", script);
        Assert.Contains("snapshot", script);
        Assert.Contains("interactive", script);
        Assert.Contains("compgen -W \"--check-version --output\" -- \"$cur\"", script);
        Assert.Contains("compgen -W \"2024 2025 2026\" -- \"$cur\"", script);
        Assert.Contains("compgen -W \"--profile --output --report --no-save\" -- \"$cur\"", script);
        Assert.Contains("compgen -W \"table json html sarif pr-comment\" -- \"$cur\"", script);
        Assert.Contains("compgen -W \"show set\"", script);
        Assert.Contains("compgen -W \"table json\" -- \"$cur\"", script);
        Assert.Contains("categories params schedules sheets --output --include-empty --category --name --writable-only --missing-only --ready-only --empty-only --sheets --issues-only", script);
        Assert.Contains("defaultOutput)", script);
        Assert.Contains("compgen -f -- \"$cur\"", script);
        Assert.Contains("--profile", script);
        Assert.Contains("--rule", script);
        Assert.Contains("--severity", script);
        Assert.Contains("--dry-run", script);
        Assert.Contains("--format --sheets --views --output-dir --dry-run --output", script);
        Assert.Contains("--yes", script);
        Assert.Contains("--max-changes", script);
        Assert.Contains("--baseline-output", script);
        Assert.Contains("--no-snapshot", script);
        Assert.Contains("--plan-output", script);
        Assert.Contains("show stats review sign verify", script);
        Assert.Contains("--limit", script);
        Assert.Contains("--high-impact-threshold", script);
        Assert.Contains("--action", script);
        Assert.Contains("--category", script);
        Assert.Contains("--operator", script);
        Assert.Contains("--user", script);
        Assert.Contains("ls validate purge export", script);
        Assert.Contains("--rules-from", script);
        Assert.Contains("--report", script);
        Assert.Contains("--review", script);
        Assert.Contains("table json markdown", script);
        Assert.Contains("init validate simulate run suggest examples receipts --dir --journal --output --dry-run --yes --continue-on-error --force --min-count --max-steps --limit --failed-only", script);
        Assert.Contains("weekly --window --dir --history-dir --journal --output --report", script);
        Assert.Contains("list stats verify bundle --dir --bundle-path --dry-run --force --output", script);
        Assert.Contains("install validate --manifest --dir --output --ref --subpath --force --dry-run", script);
        Assert.Contains("verify --root --output --tag --strict", script);
        Assert.Contains("verify index init show --against --rule --issues-only --output --path --force", script);
        Assert.Contains("list export create --category --name --fields --filter --sort --sort-desc --output --template --place-on-sheet", script);
        Assert.Contains("compgen -W \"table json markdown\" -- \"$cur\"", script);
        Assert.Contains("compgen -W \"table json csv markdown\" -- \"$cur\"", script);
        var rollbackBlock = ExtractBlock(
            script,
            "        rollback)",
            "        diff)");
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
        Assert.Contains("--check-version[Target Revit year]:year:(2024 2025 2026)", script);
        Assert.Contains("--output[Output format]:format:(table json)", script);
        Assert.Contains("--output[Output format]:format:(table json html sarif pr-comment)", script);
        Assert.Contains("--report[Save report to file]:file:_files", script);
        Assert.Contains("'examples:Show copy-paste examples for common architect workflows'", script);
        Assert.Contains("'workflow:Create, validate, run, and review terminal workflow YAML files'", script);
        Assert.Contains("'report:Generate local project reports from history and journal data'", script);
        Assert.Contains("'deliverables:Review local delivery manifests and receipts'", script);
        Assert.Contains("'standards:Install and validate local office standards requirements'", script);
        Assert.Contains("'release:Verify local release readiness and CI guardrails'", script);
        Assert.Contains("'sheets:Verify sheet numbering and local sheet-frame expectations'", script);
        Assert.Contains("'interactive:Enter interactive REPL mode'", script);
        Assert.Contains("categories params schedules sheets", script);
        Assert.Contains("--empty-only[Only zero-row schedules]", script);
        Assert.Contains("show stats review sign verify", script);
        Assert.Contains("--limit[Maximum entries to show]", script);
        Assert.Contains("--high-impact-threshold[Affected count for high-impact review]", script);
        Assert.Contains("--action[Filter entries by action]", script);
        Assert.Contains("--review[Render anomaly/notable/routine review]", script);
        Assert.Contains("--output[Output format]:format:(table json markdown)", script);
        Assert.Contains("init validate simulate run suggest examples receipts", script);
        Assert.Contains("weekly", script);
        Assert.Contains("list stats verify bundle", script);
        Assert.Contains("install validate", script);
        Assert.Contains("verify", script);
        Assert.Contains("--tag[Release tag]", script);
        Assert.Contains("--strict[Treat warnings as failures]", script);
        Assert.Contains("--issues-only[Only warning/error issues]", script);
        Assert.Contains("--path[Sheet index path]", script);
        Assert.Contains("list export create", script);
        Assert.Contains("--place-on-sheet[Sheet pattern]", script);
        Assert.Contains("--output[Output format]:format:(table json csv markdown)", script);
        Assert.Contains("--manifest[Standards manifest file]", script);
        Assert.Contains("--dry-run[Show install plan without writing files]", script);
        Assert.Contains("--rules-from[Standards manifest file]", script);
        Assert.Contains("--report[Write family purge JSON report]:file:_files", script);
        Assert.Contains("--output[Output format]:format:(table json)", script);
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
        Assert.Contains("'doctor' = @('--check-version', '--output')", script);
        Assert.Contains("$doctorOutputFormats = @('table', 'json')", script);
        Assert.Contains("$revitYears = @('2024', '2025', '2026')", script);
        Assert.Contains("'check' = @('--profile', '--output', '--report', '--no-save')", script);
        Assert.Contains("'inspect' = @('categories', 'params', 'schedules', 'sheets', '--output', '--include-empty', '--category', '--name', '--writable-only', '--missing-only', '--ready-only', '--empty-only', '--sheets', '--issues-only')", script);
        Assert.Contains("$inspectOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("$checkOutputFormats = @('table', 'json', 'html', 'sarif', 'pr-comment')", script);
        Assert.Contains("'status' = @('--output')", script);
        Assert.Contains("$statusOutputFormats = @('table', 'json')", script);
        Assert.Contains("$publishOutputFormats = @('table', 'json')", script);
        Assert.Contains("$exportOutputFormats = @('table', 'json')", script);
        Assert.Contains("'examples' = 'Show copy-paste examples for common architect workflows'", script);
        Assert.Contains("'workflow' = 'Create, validate, run, and review terminal workflow YAML files'", script);
        Assert.Contains("'report' = 'Generate local project reports from history and journal data'", script);
        Assert.Contains("'deliverables' = 'Review local delivery manifests and receipts'", script);
        Assert.Contains("'standards' = 'Install and validate local office standards requirements'", script);
        Assert.Contains("'release' = 'Verify local release readiness and CI guardrails'", script);
        Assert.Contains("'sheets' = 'Verify sheet numbering and local sheet-frame expectations'", script);
        Assert.Contains("'interactive' = 'Enter interactive REPL mode'", script);
        Assert.Contains("'rollback' = 'Restore parameters changed by a fix baseline'", script);
        Assert.Contains("'show', 'stats', 'review', 'sign', 'verify'", script);
        Assert.Contains("'--limit'", script);
        Assert.Contains("'--high-impact-threshold'", script);
        Assert.Contains("'--action'", script);
        Assert.Contains("'--category'", script);
        Assert.Contains("'--operator'", script);
        Assert.Contains("'--user'", script);
        Assert.Contains("$journalOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("'--review'", script);
        Assert.Contains("'--plan-output'", script);
        Assert.Contains("$planOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("$diffOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("'init', 'validate', 'simulate', 'run', 'suggest', 'examples', 'receipts', '--dir', '--journal', '--output', '--dry-run', '--yes', '--continue-on-error', '--force', '--min-count', '--max-steps', '--limit', '--failed-only'", script);
        Assert.Contains("$workflowOutputFormats = @('table', 'json', 'yaml', 'markdown')", script);
        Assert.Contains("'weekly', '--window', '--dir', '--history-dir', '--journal', '--output', '--report'", script);
        Assert.Contains("$reportOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("'list', 'stats', 'verify', 'bundle', '--dir', '--bundle-path', '--dry-run', '--force', '--output'", script);
        Assert.Contains("$deliverablesOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("'install', 'validate', '--manifest', '--dir', '--output', '--ref', '--subpath', '--force', '--dry-run'", script);
        Assert.Contains("$standardsOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("'release' = @('verify', '--root', '--output', '--tag', '--strict')", script);
        Assert.Contains("$releaseOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("'sheets' = @('verify', 'index', 'init', 'show', '--against', '--rule', '--issues-only', '--output', '--path', '--force')", script);
        Assert.Contains("$sheetsOutputFormats = @('table', 'json', 'markdown', 'yaml')", script);
        Assert.Contains("$scheduleSubcommands = @('list', 'export', 'create')", script);
        Assert.Contains("$scheduleOutputFormats = @('table', 'json', 'csv', 'markdown')", script);
        Assert.Contains("'ls', 'validate', 'purge', 'export', '--unused', '--category', '--rules', '--rules-from'", script);
        Assert.Contains("'--report'", script);
        Assert.Contains("$familyRules = @('name-non-empty', 'name-no-path-chars', 'category-known', 'loadable-or-in-place')", script);
        Assert.Contains("Test-Path -LiteralPath $parent", script);
        Assert.Contains("-ErrorAction SilentlyContinue", script);
        Assert.Contains(
            "$configKeys = @('serverUrl', 'defaultOutput', 'exportDir', 'Revit2024InstallDir', 'Revit2025InstallDir', 'Revit2026InstallDir')",
            script);
        Assert.Contains("New-RevitCliCompletionResults -Values $shells -ToolTip 'Shell'", script);
        var examplesOptionsBlock = ExtractBlock(
            script,
            "        'examples' = @(",
            "        'publish' = @(");
        Assert.Contains("'inspect', 'sheets', 'schedule'", examplesOptionsBlock);
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
        Assert.Contains("compgen -W \"table json\" -- \"$cur\"", script);
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
        Assert.Contains("--output[Output format for dry-runs]:format:(table json)", script);
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
