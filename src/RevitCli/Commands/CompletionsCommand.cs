using System.CommandLine;
using System.Linq;
using Spectre.Console;

namespace RevitCli.Commands;

public static class CompletionsCommand
{
    private static readonly string[] QueryOptions = { "--filter", "--id", "--output" };
    private static readonly string[] ExportOptions = { "--format", "--sheets", "--output-dir", "--dry-run" };
    private static readonly string[] SetOptions = { "--filter", "--id", "--param", "--value", "--dry-run" };
    private static readonly string[] AuditOptions = { "--rules", "--list" };
    private static readonly string[] FixOptions =
    {
        "--profile", "--rule", "--severity", "--dry-run", "--apply", "--yes",
        "--allow-inferred", "--max-changes", "--baseline-output", "--no-snapshot"
    };
    private static readonly string[] RollbackOptions = { "--dry-run", "--yes", "--max-changes" };
    private static readonly string[] PublishOptions =
        { "--profile", "--dry-run", "--since", "--since-mode", "--update-baseline" };
    private static readonly string[] SinceModes = { "content", "meta" };
    private static readonly string[] ImportOptions =
        { "--category", "--match-by", "--map", "--dry-run", "--on-missing", "--on-duplicate", "--encoding", "--batch-size" };
    private static readonly string[] OnMissingValues = { "error", "warn", "skip" };
    private static readonly string[] OnDuplicateValues = { "error", "first", "all" };
    private static readonly string[] EncodingValues = { "auto", "utf-8", "gbk" };

    public static Command Create()
    {
        var shellArg = new Argument<string>("shell", $"Shell type: {string.Join(", ", CliCommandCatalog.Shells)}");

        var command = new Command("completions", "Generate shell completion script")
        {
            shellArg
        };

        command.SetHandler((shell) =>
        {
            var script = shell.ToLowerInvariant() switch
            {
                "bash" => GenerateBash(),
                "zsh" => GenerateZsh(),
                "powershell" or "pwsh" => GeneratePowerShell(),
                _ => null
            };

            if (script == null)
            {
                AnsiConsole.MarkupLine($"[red]Unknown shell:[/] {Markup.Escape(shell)}. Use bash, zsh, or powershell.");
                return;
            }

            Console.Write(script);
        }, shellArg);

        return command;
    }

    private static string GenerateBash()
    {
        var commands = JoinWords(CliCommandCatalog.TopLevelCommandNames);
        var queryOptions = JoinWords(QueryOptions);
        var exportOptions = JoinWords(ExportOptions);
        var setOptions = JoinWords(SetOptions);
        var auditOptions = JoinWords(AuditOptions);
        var fixOptions = JoinWords(FixOptions);
        var rollbackOptions = JoinWords(RollbackOptions);
        var publishOptions = JoinWords(PublishOptions);
        var sinceModes = JoinWords(SinceModes);
        var importOptions = JoinWords(ImportOptions);
        var onMissingValues = JoinWords(OnMissingValues);
        var onDuplicateValues = JoinWords(OnDuplicateValues);
        var encodingValues = JoinWords(EncodingValues);
        var configSubcommands = JoinWords(CliCommandCatalog.ConfigSubcommands);
        var configKeys = JoinWords(ConfigCommand.ValidKeys);
        var outputFormats = JoinWords(QueryCommand.ValidOutputFormats);
        var exportFormats = JoinWords(ExportCommand.ValidFormats);
        var auditRules = JoinWords(AuditCommand.AvailableRules);
        var shells = JoinWords(CliCommandCatalog.Shells);

        return JoinLines(
            "_revitcli_completions() {",
            "    local prev cmd subcmd",
            "    local cur=\"${COMP_WORDS[COMP_CWORD]}\"",
            "    prev=\"\"",
            "    if [ $COMP_CWORD -gt 0 ]; then",
            "        prev=\"${COMP_WORDS[COMP_CWORD-1]}\"",
            "    fi",
            "    cmd=\"${COMP_WORDS[1]}\"",
            "    subcmd=\"${COMP_WORDS[2]}\"",
            "",
            "    if [ $COMP_CWORD -eq 1 ]; then",
            $"        COMPREPLY=($(compgen -W \"{commands}\" -- \"$cur\"))",
            "        return",
            "    fi",
            "",
            "    case \"$cmd\" in",
            "        query)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{outputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            $"            COMPREPLY=($(compgen -W \"{queryOptions}\" -- \"$cur\"))",
            "            ;;",
            "        export)",
            "            case \"$prev\" in",
            "                --format)",
            $"                    COMPREPLY=($(compgen -W \"{exportFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --output-dir)",
            "                    COMPREPLY=($(compgen -d -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            $"            COMPREPLY=($(compgen -W \"{exportOptions}\" -- \"$cur\"))",
            "            ;;",
            "        set)",
            $"            COMPREPLY=($(compgen -W \"{setOptions}\" -- \"$cur\"))",
            "            ;;",
            "        audit)",
            "            case \"$prev\" in",
            "                --rules)",
            $"                    COMPREPLY=($(compgen -W \"{auditRules}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            $"            COMPREPLY=($(compgen -W \"{auditOptions}\" -- \"$cur\"))",
            "            ;;",
            "        config)",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{configSubcommands}\" -- \"$cur\"))",
            "                return",
            "            fi",
            "            if [ \"$subcmd\" = \"set\" ]; then",
            "                if [ $COMP_CWORD -eq 3 ]; then",
            $"                    COMPREPLY=($(compgen -W \"{configKeys}\" -- \"$cur\"))",
            "                    return",
            "                fi",
            "                if [ $COMP_CWORD -eq 4 ]; then",
            "                    case \"${COMP_WORDS[3]}\" in",
            "                        defaultOutput)",
            $"                            COMPREPLY=($(compgen -W \"{outputFormats}\" -- \"$cur\"))",
            "                            return",
            "                            ;;",
            "                        exportDir)",
            "                            COMPREPLY=($(compgen -d -- \"$cur\"))",
            "                            return",
            "                            ;;",
            "                    esac",
            "                fi",
            "            fi",
            "            ;;",
            "        completions)",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{shells}\" -- \"$cur\"))",
            "                return",
            "            fi",
            "            ;;",
            "        batch)",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            "                COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                return",
            "            fi",
            "            ;;",
            "        publish)",
            "            case \"$prev\" in",
            "                --since|--profile)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --since-mode)",
            $"                    COMPREPLY=($(compgen -W \"{sinceModes}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            $"            COMPREPLY=($(compgen -W \"{publishOptions}\" -- \"$cur\"))",
            "            ;;",
            "        import)",
            "            case \"$prev\" in",
            "                --on-missing)",
            $"                    COMPREPLY=($(compgen -W \"{onMissingValues}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --on-duplicate)",
            $"                    COMPREPLY=($(compgen -W \"{onDuplicateValues}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --encoding)",
            $"                    COMPREPLY=($(compgen -W \"{encodingValues}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            $"            COMPREPLY=($(compgen -W \"{importOptions}\" -- \"$cur\"))",
            "            ;;",
            "        fix)",
            $"            COMPREPLY=($(compgen -W \"{fixOptions}\" -- \"$cur\"))",
            "            ;;",
            "        rollback)",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            "                case \"$cur\" in",
            "                    -*)",
            $"                        COMPREPLY=($(compgen -W \"{rollbackOptions}\" -- \"$cur\"))",
            "                        return",
            "                        ;;",
            "                    *)",
            "                        COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                        return",
            "                        ;;",
            "                esac",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{rollbackOptions}\" -- \"$cur\"))",
            "            ;;",
            "        status|doctor|interactive)",
            "            COMPREPLY=()",
            "            ;;",
            "    esac",
            "}",
            "complete -F _revitcli_completions revitcli");
    }

    private static string GenerateZsh()
    {
        var commandLines = CliCommandCatalog.TopLevelCommands
            .Select(command => $"        '{command.Name}:{command.Description}'");
        var outputFormats = JoinWords(QueryCommand.ValidOutputFormats);
        var exportFormats = JoinWords(ExportCommand.ValidFormats);
        var configSubcommands = JoinWords(CliCommandCatalog.ConfigSubcommands);
        var configKeys = JoinWords(ConfigCommand.ValidKeys);
        var shells = JoinWords(CliCommandCatalog.Shells);
        var auditRules = JoinWords(AuditCommand.AvailableRules);
        var fixOptions = JoinWords(FixOptions);

        return JoinLines(
            "#compdef revitcli",
            "",
            "_revitcli() {",
            "    local -a commands",
            "    commands=(",
            commandLines,
            "    )",
            "",
            "    _arguments -C \\",
            "        '1:command:->cmds' \\",
            "        '*::arg:->args'",
            "",
            "    case \"$state\" in",
            "        cmds)",
            "            _describe 'command' commands",
            "            ;;",
            "        args)",
            "            case $words[2] in",
            "                query)",
            "                    _arguments \\",
            "                        '--filter[Filter expression]:filter:' \\",
            "                        '--id[Element ID]:id:' \\",
            $"                        '--output[Output format]:format:({outputFormats})'",
            "                    ;;",
            "                export)",
            "                    _arguments \\",
            $"                        '--format[Export format]:format:({exportFormats})' \\",
            "                        '--sheets[Sheet patterns]:sheets:' \\",
            "                        '--output-dir[Output directory]:dir:_directories' \\",
            "                        '--dry-run[Validate without writing files]'",
            "                    ;;",
            "                set)",
            "                    _arguments \\",
            "                        '--filter[Filter expression]:filter:' \\",
            "                        '--id[Element ID]:id:' \\",
            "                        '--param[Parameter name]:param:' \\",
            "                        '--value[New value]:value:' \\",
            "                        '--dry-run[Preview changes]'",
            "                    ;;",
            "                audit)",
            "                    _arguments \\",
            $"                        '--rules[Comma-separated rules]:rules:({auditRules})' \\",
            "                        '--list[List available rules]'",
            "                    ;;",
            "                config)",
            "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {configSubcommands}",
            "                    elif [[ \"$words[3]\" == \"set\" ]]; then",
            "                        if (( CURRENT == 4 )); then",
            $"                            _values 'setting' {configKeys}",
            "                        elif (( CURRENT == 5 )); then",
            "                            case \"$words[4]\" in",
            "                                defaultOutput)",
            $"                                    _values 'format' {outputFormats}",
            "                                    ;;",
            "                                exportDir)",
            "                                    _directories",
            "                                    ;;",
            "                            esac",
            "                        fi",
            "                    fi",
            "                    ;;",
            "                completions)",
            $"                    _arguments '1:shell:({shells})'",
            "                    ;;",
            "                batch)",
            "                    _arguments '1:file:_files'",
            "                    ;;",
            "                publish)",
            "                    _arguments \\",
            "                        '--profile[Path to .revitcli.yml profile]:file:_files' \\",
            "                        '--dry-run[Preview without exporting]' \\",
            "                        '--since[Baseline snapshot JSON file]:file:_files' \\",
            "                        '--since-mode[content or meta]:mode:(content meta)' \\",
            "                        '--update-baseline[Update baseline after successful publish]'",
            "                    ;;",
            "                fix)",
            "                    _arguments \\",
            $"                        '--profile[Path to .revitcli.yml profile]:file:_files' \\",
            "                        '--rule[Filter by rule names]:rules:' \\",
            "                        '--severity[Filter by issue severity]:severity:' \\",
            "                        '--dry-run[Preview only]' \\",
            "                        '--apply[Apply generated fixes]' \\",
            "                        '--yes[Auto-confirm in non-interactive mode]' \\",
            "                        '--allow-inferred[Allow inferred fixes]' \\",
            "                        '--max-changes[Maximum number of actions]' \\",
            "                        '--baseline-output[Save baseline snapshot path]:file:_files' \\",
            "                        '--no-snapshot[Skip baseline and journal support]'",
            "                    ;;",
            "                rollback)",
            "                    _arguments \\",
            "                        '1:baseline file:_files' \\",
            "                        '--dry-run[Preview rollback without applying]' \\",
            "                        '--yes[Confirm rollback apply in non-interactive mode]' \\",
            "                        '--max-changes[Maximum number of rollback writes]'",
            "                    ;;",
            "                import)",
            "                    _arguments \\",
            "                        '1:file:_files' \\",
            "                        '--category[Revit category]:category:' \\",
            "                        '--match-by[Match-by parameter]:param:' \\",
            "                        '--map[Explicit col:Param mapping]:mapping:' \\",
            "                        '--dry-run[Preview only]' \\",
            "                        '--on-missing[Behavior on missing match]:mode:(error warn skip)' \\",
            "                        '--on-duplicate[Behavior on duplicate match]:mode:(error first all)' \\",
            "                        '--encoding[CSV encoding]:enc:(auto utf-8 gbk)' \\",
            "                        '--batch-size[Max ElementIds per SetRequest]:n:'",
            "                    ;;",
            "            esac",
            "            ;;",
            "    esac",
            "}",
            "",
            "_revitcli");
    }

    private static string GeneratePowerShell()
    {
        var commandLines = CliCommandCatalog.TopLevelCommands
            .Select(command => $"        '{command.Name}' = '{command.Description}'");
        var queryOptions = FormatPowerShellArray(QueryOptions);
        var exportOptions = FormatPowerShellArray(ExportOptions);
        var setOptions = FormatPowerShellArray(SetOptions);
        var auditOptions = FormatPowerShellArray(AuditOptions);
        var fixOptions = FormatPowerShellArray(FixOptions);
        var rollbackOptions = FormatPowerShellArray(RollbackOptions);
        var publishOptions = FormatPowerShellArray(PublishOptions);
        var sinceModes = FormatPowerShellArray(SinceModes);
        var importOptions = FormatPowerShellArray(ImportOptions);
        var onMissingValues = FormatPowerShellArray(OnMissingValues);
        var onDuplicateValues = FormatPowerShellArray(OnDuplicateValues);
        var encodingValues = FormatPowerShellArray(EncodingValues);
        var outputFormats = FormatPowerShellArray(QueryCommand.ValidOutputFormats);
        var exportFormats = FormatPowerShellArray(ExportCommand.ValidFormats);
        var configSubcommands = FormatPowerShellArray(CliCommandCatalog.ConfigSubcommands);
        var configKeys = FormatPowerShellArray(ConfigCommand.ValidKeys);
        var shells = FormatPowerShellArray(CliCommandCatalog.Shells);
        var auditRules = FormatPowerShellArray(AuditCommand.AvailableRules);

        return JoinLines(
            "Register-ArgumentCompleter -CommandName revitcli -Native -ScriptBlock {",
            "    param($wordToComplete, $commandAst, $cursorPosition)",
            "",
            "    $commands = @{",
            commandLines,
            "    }",
            "",
            "    $commandOptions = @{",
            $"        'query' = @({queryOptions})",
            $"        'export' = @({exportOptions})",
            $"        'set' = @({setOptions})",
            $"        'audit' = @({auditOptions})",
            $"        'fix' = @({fixOptions})",
            $"        'rollback' = @({rollbackOptions})",
            $"        'publish' = @({publishOptions})",
            $"        'import' = @({importOptions})",
            "    }",

            "",
            $"    $sinceModes = @({sinceModes})",
            $"    $onMissingValues = @({onMissingValues})",
            $"    $onDuplicateValues = @({onDuplicateValues})",
            $"    $encodingValues = @({encodingValues})",
            "",
            $"    $outputFormats = @({outputFormats})",
            $"    $exportFormats = @({exportFormats})",
            $"    $configSubcommands = @({configSubcommands})",
            $"    $configKeys = @({configKeys})",
            $"    $shells = @({shells})",
            $"    $auditRules = @({auditRules})",
            "",
            "    function New-RevitCliCompletionResults {",
            "        param(",
            "            [string[]]$Values,",
            "            [string]$ToolTip",
            "        )",
            "",
            "        $Values |",
            "            Where-Object { $_ -like \"$wordToComplete*\" } |",
            "            ForEach-Object {",
            "                [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $ToolTip)",
            "            }",
            "    }",
            "",
            "    function New-RevitCliFileCompletionResults {",
            "        param(",
            "            [string]$Path",
            "        )",
            "",
            "        $target = if ([string]::IsNullOrWhiteSpace($Path)) { '.' } else { $Path }",
            "        $parent = Split-Path -Path $target -Parent",
            "        if ([string]::IsNullOrWhiteSpace($parent)) {",
            "            $parent = '.'",
            "        }",
            "",
            "        $leaf = Split-Path -Path $target -Leaf",
            "        if (Test-Path -LiteralPath $parent -ErrorAction SilentlyContinue) {",
            "            try {",
            "                Get-ChildItem -LiteralPath $parent -Force -ErrorAction SilentlyContinue |",
            "                    Where-Object { $_.Name -like \"$leaf*\" } |",
            "                    ForEach-Object {",
            "                        [System.Management.Automation.CompletionResult]::new($_.FullName, $_.Name, 'ParameterValue', $_.FullName)",
            "                    }",
            "            } catch {",
            "            }",
            "        }",
            "    }",
            "",
            "    $text = $commandAst.ToString()",
            "    $tokens = $text.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)",
            "    $endsWithSpace = $text.EndsWith(' ')",
            "    $command = if ($tokens.Count -gt 1) { $tokens[1] } else { $null }",
            "    $previous = if ($endsWithSpace) {",
            "        if ($tokens.Count -gt 0) { $tokens[-1] } else { $null }",
            "    } elseif ($tokens.Count -gt 1) {",
            "        $tokens[-2]",
            "    } else {",
            "        $null",
            "    }",
            "",
            "    if (-not $command) {",
            "        $commands.GetEnumerator() | Where-Object { $_.Key -like \"$wordToComplete*\" } |",
            "            Sort-Object Key |",
            "            ForEach-Object { [System.Management.Automation.CompletionResult]::new($_.Key, $_.Key, 'ParameterValue', $_.Value) }",
            "        return",
            "    }",
            "",
            "    switch ($command) {",
            "        'query' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $outputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['query'] -ToolTip 'Option'",
            "            return",
            "        }",
            "        'export' {",
            "            if ($previous -eq '--format') {",
            "                New-RevitCliCompletionResults -Values $exportFormats -ToolTip 'Export format'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['export'] -ToolTip 'Option'",
            "            return",
            "        }",
            "        'set' {",
            "            New-RevitCliCompletionResults -Values $commandOptions['set'] -ToolTip 'Option'",
            "            return",
            "        }",
            "        'audit' {",
            "            if ($previous -eq '--rules') {",
            "                New-RevitCliCompletionResults -Values $auditRules -ToolTip 'Audit rule'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['audit'] -ToolTip 'Option'",
            "            return",
            "        }",
            "        'config' {",
            "            if ($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) {",
            "                New-RevitCliCompletionResults -Values $configSubcommands -ToolTip 'Config subcommand'",
            "                return",
            "            }",
            "",
            "            if ($tokens.Count -ge 3 -and $tokens[2] -eq 'set') {",
            "                if (($tokens.Count -eq 3 -and $endsWithSpace) -or ($tokens.Count -eq 4 -and -not $endsWithSpace)) {",
            "                    New-RevitCliCompletionResults -Values $configKeys -ToolTip 'Config key'",
            "                    return",
            "                }",
            "",
            "                if (($tokens.Count -eq 4 -and $endsWithSpace) -or ($tokens.Count -eq 5 -and -not $endsWithSpace)) {",
            "                    switch ($tokens[3]) {",
            "                        'defaultOutput' {",
            "                            New-RevitCliCompletionResults -Values $outputFormats -ToolTip 'Output format'",
            "                            return",
            "                        }",
            "                    }",
            "                }",
            "            }",
            "            return",
            "        }",
            "        'completions' {",
            "            if ($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) {",
            "                New-RevitCliCompletionResults -Values $shells -ToolTip 'Shell'",
            "                return",
            "            }",
            "            return",
            "        }",
            "        'publish' {",
            "            if ($previous -eq '--since-mode') {",
            "                New-RevitCliCompletionResults -Values $sinceModes -ToolTip 'Since mode'",
            "                return",
            "            }",
            "            New-RevitCliCompletionResults -Values $commandOptions['publish'] -ToolTip 'Option'",
            "            return",
            "        }",
            "        'fix' {",
            "            New-RevitCliCompletionResults -Values $commandOptions['fix'] -ToolTip 'Option'",
            "            return",
            "        }",
            "        'rollback' {",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['rollback'] -ToolTip 'Option'",
            "            return",
            "        }",
            "        'import' {",
            "            if ($previous -eq '--on-missing') {",
            "                New-RevitCliCompletionResults -Values $onMissingValues -ToolTip 'On-missing mode'",
            "                return",
            "            }",
            "            if ($previous -eq '--on-duplicate') {",
            "                New-RevitCliCompletionResults -Values $onDuplicateValues -ToolTip 'On-duplicate mode'",
            "                return",
            "            }",
            "            if ($previous -eq '--encoding') {",
            "                New-RevitCliCompletionResults -Values $encodingValues -ToolTip 'Encoding'",
            "                return",
            "            }",
            "            New-RevitCliCompletionResults -Values $commandOptions['import'] -ToolTip 'Option'",
            "            return",
            "        }",
            "    }",
            "}");
    }

    private static string JoinWords(IEnumerable<string> values) =>
        string.Join(" ", values);

    private static string JoinLines(params object[] parts) =>
        string.Join(
            Environment.NewLine,
            parts.SelectMany(part => part switch
            {
                string line => new[] { line },
                IEnumerable<string> lines => lines,
                _ => throw new InvalidOperationException($"Unsupported line group: {part.GetType().FullName}")
            })) + Environment.NewLine;

    private static string FormatPowerShellArray(IEnumerable<string> values) =>
        string.Join(", ", values.Select(value => $"'{value}'"));
}
