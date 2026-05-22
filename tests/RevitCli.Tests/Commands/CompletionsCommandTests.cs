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
        Assert.Contains("rooms", script);
        Assert.Contains("marks", script);
        Assert.Contains("schedules", script);
        Assert.Contains("views", script);
        Assert.Contains("links", script);
        Assert.Contains("model", script);
        Assert.Contains("diff", script);
        Assert.Contains("snapshot", script);
        Assert.Contains("interactive", script);
        Assert.Contains("compgen -W \"--check-version --output\" -- \"$cur\"", script);
        Assert.Contains("compgen -W \"2024 2025 2026\" -- \"$cur\"", script);
        Assert.Contains("compgen -W \"--profile --output --report --no-save\" -- \"$cur\"", script);
        Assert.Contains("compgen -W \"table json html sarif pr-comment\" -- \"$cur\"", script);
        Assert.Contains("compgen -W \"show set\"", script);
        Assert.Contains("compgen -W \"table json\" -- \"$cur\"", script);
        Assert.Contains("categories params schedules sheets workflows plans --output --dir --include-empty --category --name --writable-only --missing-only --ready-only --empty-only --sheets --issues-only", script);
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
        Assert.Contains("init validate simulate review run suggest examples receipts --dir --journal --output --dry-run --yes --continue-on-error --timeout-ms --force --min-count --max-steps --limit --failed-only --name --min-duration-ms --sort --window", script);
        Assert.Contains("weekly knowledge --window --dir --history-dir --journal --output --report", script);
        Assert.Contains("list stats verify plan bundle --dir --profile --since --bundle-path --dry-run --force --output", script);
        Assert.Contains("install validate --manifest --dir --output --ref --subpath --force --dry-run", script);
        Assert.Contains("verify --root --output --tag --strict", script);
        Assert.Contains("verify issue-meta renumber index init show --against --rule --issues-only --output --path --force --selector --issue-code --issue-date --plan-output --param-map --dry-run --max-changes", script);
        Assert.Contains("renumber --rule --plan-output --scope --dry-run --max-changes --output", script);
        Assert.Contains("assign verify --category --rule --plan-output --sort --dry-run --max-changes --against --output", script);
        Assert.Contains("ensure batch-export compare --spec --plan-output --dry-run --mode --set --output-dir --format --manifest --from --to --keys --output", script);
        Assert.Contains("compgen -W \"create-only sync-fields\" -- \"$cur\"", script);
        Assert.Contains("audit template-apply clone-set --rules --templates --browser --selector --template --plan-output --dry-run --exclude --from-set --to-prefix --naming-rule --include-sheets --output", script);
        Assert.Contains("compgen -W \"locked\" -- \"$cur\"", script);
        Assert.Contains("list export create --category --name --fields --filter --sort --sort-desc --output --template --place-on-sheet --dry-run --receipt-dir", script);
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
        Assert.Contains("'deliverables:Review local delivery plans, manifests, and receipts'", script);
        Assert.Contains("'standards:Install and validate local office standards requirements'", script);
        Assert.Contains("'release:Verify local release readiness and CI guardrails'", script);
        Assert.Contains("'sheets:Verify sheet numbering and local sheet-frame expectations'", script);
        Assert.Contains("'rooms:Plan and review room numbering workflows'", script);
        Assert.Contains("'marks:Plan and verify door/window Mark numbering workflows'", script);
        Assert.Contains("'schedules:Ensure, batch-export, and compare versioned schedule specs'", script);
        Assert.Contains("'views:Audit, template, and clone view sets'", script);
        Assert.Contains("'links:Audit and plan safe coordination link repairs'", script);
        Assert.Contains("'model:Audit and plan safe model mapping fixes'", script);
        Assert.Contains("'interactive:Enter interactive REPL mode'", script);
        Assert.Contains("categories params schedules sheets workflows plans", script);
        Assert.Contains("--empty-only[Only zero-row schedules]", script);
        Assert.Contains("show stats review sign verify", script);
        Assert.Contains("--limit[Maximum entries to show]", script);
        Assert.Contains("--high-impact-threshold[Affected count for high-impact review]", script);
        Assert.Contains("--action[Filter entries by action]", script);
        Assert.Contains("--review[Render anomaly/notable/routine review]", script);
        Assert.Contains("--output[Output format]:format:(table json markdown)", script);
        Assert.Contains("init validate simulate review run suggest examples receipts", script);
        Assert.Contains("--name[Only show receipts for workflow name]:name:", script);
        Assert.Contains("--min-duration-ms[Only show workflow receipts at or above duration]:ms:", script);
        Assert.Contains("--timeout-ms[Maximum milliseconds per executed workflow step]:ms:", script);
        Assert.Contains("--sort[Sort workflow receipts]:sort:(completed duration)", script);
        Assert.Contains("--window[Only show workflow receipts in a recent window]:window:", script);
        Assert.Contains("weekly", script);
        Assert.Contains("list stats verify plan bundle", script);
        Assert.Contains("install validate", script);
        Assert.Contains("verify", script);
        Assert.Contains("--tag[Release tag]", script);
        Assert.Contains("--strict[Treat warnings as failures]", script);
        Assert.Contains("--issues-only[Only warning/error issues]", script);
        Assert.Contains("--path[Sheet index path]", script);
        Assert.Contains("--scope[Room scope]", script);
        Assert.Contains("--against[Rule YAML or glob]", script);
        Assert.Contains("ensure batch-export compare", script);
        Assert.Contains("--mode[Ensure mode]:mode:(create-only sync-fields)", script);
        Assert.Contains("--manifest[Write export manifest JSON]:file:_files", script);
        Assert.Contains("audit template-apply clone-set", script);
        Assert.Contains("--exclude[Exclude flags]:exclude:(locked)", script);
        Assert.Contains("--include-sheets[Plan sheet placement duplication]", script);
        Assert.Contains("list export create", script);
        Assert.Contains("--place-on-sheet[Sheet pattern]", script);
        Assert.Contains("--dry-run[Preview schedule creation without writing]", script);
        Assert.Contains("--receipt-dir[Directory for schedule-create receipts]", script);
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
        Assert.Contains("'1:rollback artifact file:_files'", rollbackBlock);
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
        Assert.Contains("'inspect' = @('categories', 'params', 'schedules', 'sheets', 'workflows', 'plans', '--output', '--dir', '--include-empty', '--category', '--name', '--writable-only', '--missing-only', '--ready-only', '--empty-only', '--sheets', '--issues-only')", script);
        Assert.Contains("$inspectOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("$checkOutputFormats = @('table', 'json', 'html', 'sarif', 'pr-comment')", script);
        Assert.Contains("'status' = @('--output')", script);
        Assert.Contains("$statusOutputFormats = @('table', 'json')", script);
        Assert.Contains("$publishOutputFormats = @('table', 'json')", script);
        Assert.Contains("$exportOutputFormats = @('table', 'json')", script);
        Assert.Contains("'examples' = 'Show copy-paste examples for common architect workflows'", script);
        Assert.Contains("'workflow' = 'Create, validate, run, and review terminal workflow YAML files'", script);
        Assert.Contains("'report' = 'Generate local project reports from history and journal data'", script);
        Assert.Contains("'deliverables' = 'Review local delivery plans, manifests, and receipts'", script);
        Assert.Contains("'standards' = 'Install and validate local office standards requirements'", script);
        Assert.Contains("'release' = 'Verify local release readiness and CI guardrails'", script);
        Assert.Contains("'sheets' = 'Verify sheet numbering and local sheet-frame expectations'", script);
        Assert.Contains("'rooms' = 'Plan and review room numbering workflows'", script);
        Assert.Contains("'marks' = 'Plan and verify door/window Mark numbering workflows'", script);
        Assert.Contains("'interactive' = 'Enter interactive REPL mode'", script);
        Assert.Contains("'rollback' = 'Restore parameters from a fix baseline or plan receipt'", script);
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
        Assert.Contains("'init', 'validate', 'simulate', 'review', 'run', 'suggest', 'examples', 'receipts', '--dir', '--journal', '--output', '--dry-run', '--yes', '--continue-on-error', '--timeout-ms', '--force', '--min-count', '--max-steps', '--limit', '--failed-only', '--name', '--min-duration-ms', '--sort', '--window'", script);
        Assert.Contains("$workflowSubcommands = @('init', 'validate', 'simulate', 'review', 'run', 'suggest', 'examples', 'receipts')", script);
        Assert.Contains("$workflowReportOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("$workflowSuggestOutputFormats = @('table', 'json', 'yaml')", script);
        Assert.Contains("'weekly', 'knowledge', '--window', '--dir', '--history-dir', '--journal', '--output', '--report'", script);
        Assert.Contains("$reportOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("'list', 'stats', 'verify', 'plan', 'bundle', '--dir', '--profile', '--since', '--bundle-path', '--dry-run', '--force', '--output'", script);
        Assert.Contains("$deliverablesOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("'install', 'validate', '--manifest', '--dir', '--output', '--ref', '--subpath', '--force', '--dry-run'", script);
        Assert.Contains("$standardsOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("'release' = @('verify', '--root', '--output', '--tag', '--strict')", script);
        Assert.Contains("$releaseOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("'sheets' = @('verify', 'issue-meta', 'renumber', 'index', 'init', 'show', '--against', '--rule', '--issues-only', '--output', '--path', '--force', '--selector', '--issue-code', '--issue-date', '--plan-output', '--param-map', '--dry-run', '--max-changes')", script);
        Assert.Contains("$sheetsOutputFormats = @('table', 'json', 'markdown', 'yaml')", script);
        Assert.Contains("'rooms' = @('renumber', '--rule', '--plan-output', '--scope', '--dry-run', '--max-changes', '--output')", script);
        Assert.Contains("$roomsOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("'marks' = @('assign', 'verify', '--category', '--rule', '--plan-output', '--sort', '--dry-run', '--max-changes', '--against', '--output')", script);
        Assert.Contains("$marksOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("'schedules' = @('ensure', 'batch-export', 'compare', '--spec', '--plan-output', '--dry-run', '--mode', '--set', '--output-dir', '--format', '--manifest', '--from', '--to', '--keys', '--output')", script);
        Assert.Contains("$schedulesSubcommands = @('ensure', 'batch-export', 'compare')", script);
        Assert.Contains("$schedulesModes = @('create-only', 'sync-fields')", script);
        Assert.Contains("'views' = @('audit', 'template-apply', 'clone-set', '--rules', '--templates', '--browser', '--selector', '--template', '--plan-output', '--dry-run', '--exclude', '--from-set', '--to-prefix', '--naming-rule', '--include-sheets', '--output')", script);
        Assert.Contains("$viewsSubcommands = @('audit', 'template-apply', 'clone-set')", script);
        Assert.Contains("$viewsExcludeValues = @('locked')", script);
        Assert.Contains("'links' = @('audit', 'repair', '--rules', '--check', '--map', '--plan-output', '--dry-run', '--max-changes', '--output')", script);
        Assert.Contains("$linksSubcommands = @('audit', 'repair')", script);
        Assert.Contains("$linkCheckValues = @('paths', 'loaded', 'coordinates', 'paths,loaded,coordinates')", script);
        Assert.Contains("'model' = @('map-check', 'map-fix', '--against', '--worksets', '--phases', '--plan-output', '--scope', '--dry-run', '--max-changes', '--output')", script);
        Assert.Contains("$modelSubcommands = @('map-check', 'map-fix')", script);
        Assert.Contains("$modelScopeValues = @('rooms', 'doors', 'walls', 'rooms,doors,walls', 'all')", script);
        Assert.Contains("$scheduleSubcommands = @('list', 'export', 'create')", script);
        Assert.Contains("$scheduleListOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("$scheduleExportOutputFormats = @('table', 'json', 'csv', 'markdown')", script);
        Assert.Contains("$scheduleCreateOutputFormats = @('table', 'json', 'markdown')", script);
        Assert.Contains("'--receipt-dir'", script);
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
        Assert.Contains("'inspect', 'sheets', 'rooms', 'marks', 'schedule'", examplesOptionsBlock);
        Assert.Contains("'--output'", examplesOptionsBlock);
        Assert.Contains("$exampleOutputFormats = @('table', 'json', 'markdown')", script);
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
    public async Task Completions_UseSubcommandSpecificWorkflowAndScheduleOutputFormats()
    {
        var bashOut = new StringWriter();
        Console.SetOut(bashOut);
        var bashExitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "bash" });
        var bash = bashOut.ToString();

        Assert.Equal(0, bashExitCode);
        var bashWorkflow = ExtractBlock(bash, "        workflow)", "        report)");
        Assert.Contains("if [ \"$subcmd\" = \"suggest\" ]; then", bashWorkflow);
        Assert.Contains("compgen -W \"table json yaml\" -- \"$cur\"", bashWorkflow);
        Assert.Contains("compgen -W \"table json markdown\" -- \"$cur\"", bashWorkflow);
        Assert.DoesNotContain("table json yaml markdown", bashWorkflow);
        var bashSchedule = ExtractBlock(bash, "        schedule)", "        family)");
        Assert.Contains("if [ \"$subcmd\" = \"list\" ]; then", bashSchedule);
        Assert.Contains("elif [ \"$subcmd\" = \"create\" ]; then", bashSchedule);
        Assert.Contains("compgen -W \"table json csv markdown\" -- \"$cur\"", bashSchedule);

        var zshOut = new StringWriter();
        Console.SetOut(zshOut);
        var zshExitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "zsh" });
        var zsh = zshOut.ToString();

        Assert.Equal(0, zshExitCode);
        var zshWorkflow = ExtractBlock(zsh, "                workflow)", "                report)");
        Assert.Contains("if [[ \"$words[3]\" == \"suggest\" ]]; then", zshWorkflow);
        Assert.Contains("--output[Output format]:format:(table json yaml)", zshWorkflow);
        Assert.Contains("--output[Output format]:format:(table json markdown)", zshWorkflow);
        Assert.DoesNotContain("--output[Output format]:format:(table json yaml markdown)", zshWorkflow);
        var zshSchedule = ExtractBlock(zsh, "                schedule)", "                family)");
        Assert.Contains("if [[ \"$words[3]\" == \"list\" ]]; then", zshSchedule);
        Assert.Contains("elif [[ \"$words[3]\" == \"create\" ]]; then", zshSchedule);
        Assert.Contains("--output[Output format]:format:(table json csv markdown)", zshSchedule);

        var pwshOut = new StringWriter();
        Console.SetOut(pwshOut);
        var pwshExitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "powershell" });
        var pwsh = pwshOut.ToString();

        Assert.Equal(0, pwshExitCode);
        var pwshWorkflow = ExtractBlock(pwsh, "        'workflow' {", "        'report' {");
        Assert.Contains("$tokens[2] -eq 'suggest'", pwshWorkflow);
        Assert.Contains("$workflowSuggestOutputFormats", pwshWorkflow);
        Assert.Contains("$workflowReportOutputFormats", pwshWorkflow);
        Assert.Contains("$workflowSubcommands", pwshWorkflow);
        Assert.DoesNotContain("$workflowOutputFormats", pwshWorkflow);
        var pwshSchedule = ExtractBlock(pwsh, "        'schedule' {", "        'family' {");
        Assert.Contains("$tokens[2] -eq 'list'", pwshSchedule);
        Assert.Contains("$tokens[2] -eq 'create'", pwshSchedule);
        Assert.Contains("$scheduleListOutputFormats", pwshSchedule);
        Assert.Contains("$scheduleCreateOutputFormats", pwshSchedule);
        Assert.Contains("$scheduleExportOutputFormats", pwshSchedule);
        Assert.DoesNotContain("$scheduleOutputFormats", pwshSchedule);
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

    [Fact]
    public async Task Completions_Include_WorkbenchContract()
    {
        var bashOut = new StringWriter();
        Console.SetOut(bashOut);
        var bashExitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "bash" });
        var bash = bashOut.ToString();

        Assert.Equal(0, bashExitCode);
        Assert.Contains("workbench)", bash);
        Assert.Contains("contract verify receipts paths exits extensions outputs safeguards project handoff --dir --output --contract", bash);
        Assert.Contains("compgen -W \"table json markdown\" -- \"$cur\"", bash);
        Assert.Contains("compgen -W \"workbench-contract.v1 workbench-contract.v2\" -- \"$cur\"", bash);

        var zshOut = new StringWriter();
        Console.SetOut(zshOut);
        var zshExitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "zsh" });
        var zsh = zshOut.ToString();

        Assert.Equal(0, zshExitCode);
        Assert.Contains("'workbench:Show stable terminal workbench contract for Codex CLI'", zsh);
        Assert.Contains("workbench)", zsh);
        Assert.Contains("_values 'subcommand' contract verify receipts paths exits extensions outputs safeguards project handoff", zsh);
        Assert.Contains("--dir[Project directory]:dir:_directories", zsh);
        Assert.Contains("--contract[Contract schema]:schema:(workbench-contract.v1 workbench-contract.v2)", zsh);
        Assert.Contains("--output[Output format]:format:(table json markdown)", zsh);

        var pwshOut = new StringWriter();
        Console.SetOut(pwshOut);
        var pwshExitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "powershell" });
        var pwsh = pwshOut.ToString();

        Assert.Equal(0, pwshExitCode);
        Assert.Contains("'workbench' = 'Show stable terminal workbench contract for Codex CLI'", pwsh);
        Assert.Contains("'workbench' = @('contract', 'verify', 'receipts', 'paths', 'exits', 'extensions', 'outputs', 'safeguards', 'project', 'handoff', '--dir', '--output', '--contract')", pwsh);
        Assert.Contains("$workbenchOutputFormats = @('table', 'json', 'markdown')", pwsh);
        Assert.Contains("$workbenchContractSchemas = @('workbench-contract.v1', 'workbench-contract.v2')", pwsh);
    }

    [Fact]
    public async Task Completions_Include_ScoreOutputFormats()
    {
        var bashOut = new StringWriter();
        Console.SetOut(bashOut);
        var bashExitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "bash" });
        var bash = bashOut.ToString();

        Assert.Equal(0, bashExitCode);
        var bashBlock = ExtractBlock(bash, "        score)", "        query)");
        Assert.Contains("--history --dir --output", bashBlock);
        Assert.Contains("compgen -W \"table json markdown\" -- \"$cur\"", bashBlock);

        var zshOut = new StringWriter();
        Console.SetOut(zshOut);
        var zshExitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "zsh" });
        var zsh = zshOut.ToString();

        Assert.Equal(0, zshExitCode);
        var zshBlock = ExtractBlock(zsh, "                score)", "                query)");
        Assert.Contains("--history[History window", zshBlock);
        Assert.Contains("--output[Output format]:format:(table json markdown)", zshBlock);

        var pwshOut = new StringWriter();
        Console.SetOut(pwshOut);
        var pwshExitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "powershell" });
        var pwsh = pwshOut.ToString();

        Assert.Equal(0, pwshExitCode);
        Assert.Contains("'score' = @('--history', '--dir', '--output')", pwsh);
        Assert.Contains("$scoreOutputFormats = @('table', 'json', 'markdown')", pwsh);
    }

    [Fact]
    public async Task Completions_Include_ExamplesOutputFormats()
    {
        var bashOut = new StringWriter();
        Console.SetOut(bashOut);
        var bashExitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "bash" });
        var bash = bashOut.ToString();

        Assert.Equal(0, bashExitCode);
        var bashBlock = ExtractBlock(bash, "        examples)", "        interactive)");
        Assert.Contains("--output", bashBlock);
        Assert.Contains("compgen -W \"table json markdown\" -- \"$cur\"", bashBlock);

        var zshOut = new StringWriter();
        Console.SetOut(zshOut);
        var zshExitCode = await CompletionsCommand.Create().InvokeAsync(new[] { "zsh" });
        var zsh = zshOut.ToString();

        Assert.Equal(0, zshExitCode);
        var zshBlock = ExtractBlock(zsh, "                examples)", "                import)");
        Assert.Contains("--output[Output format]:format:(table json markdown)", zshBlock);
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
