using System.CommandLine;
using System.Linq;
using RevitCli.Families;
using Spectre.Console;

namespace RevitCli.Commands;

public static class CompletionsCommand
{
    private static readonly string[] StatusOptions = { "--output" };
    private static readonly string[] StatusOutputFormats = { "table", "json" };
    private static readonly string[] DoctorOptions = { "--check-version", "--output" };
    private static readonly string[] DoctorOutputFormats = { "table", "json" };
    private static readonly string[] RevitYears = { "2024", "2025", "2026" };
    private static readonly string[] CheckOptions = { "--profile", "--output", "--report", "--no-save" };
    private static readonly string[] CheckOutputFormats = { "table", "json", "html", "sarif", "pr-comment" };
    private static readonly string[] ScoreOptions = { "--history", "--dir", "--output" };
    private static readonly string[] ScoreOutputFormats = ScoreCommand.OutputFormats;
    private static readonly string[] QueryOptions = { "--filter", "--id", "--output" };
    private static readonly string[] InspectSubcommands = { "categories", "params", "schedules", "sheets", "workflows", "plans" };
    private static readonly string[] InspectOptions =
        { "--output", "--dir", "--include-empty", "--category", "--name", "--writable-only", "--missing-only", "--ready-only", "--empty-only", "--sheets", "--issues-only" };
    private static readonly string[] InspectOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] ExportOptions = { "--format", "--sheets", "--views", "--output-dir", "--dry-run", "--output" };
    private static readonly string[] ExportOutputFormats = { "table", "json" };
    private static readonly string[] SetOptions = { "--filter", "--id", "--param", "--value", "--clear-value", "--dry-run", "--yes", "--plan-output" };
    private static readonly string[] PlanSubcommands = { "show", "apply" };
    private static readonly string[] PlanOptions =
    {
        "--output", "--yes", "--dry-run", "--max-changes", "--profile",
        "--high-impact-threshold", "--confirm-high-impact", "--allow-inferred"
    };
    private static readonly string[] PlanOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] AuditOptions = { "--rules", "--list" };
    private static readonly string[] FixOptions =
    {
        "--profile", "--rule", "--severity", "--dry-run", "--apply", "--yes",
        "--allow-inferred", "--max-changes", "--baseline-output", "--no-snapshot", "--plan-output"
    };
    private static readonly string[] RollbackOptions = { "--dry-run", "--yes", "--max-changes" };
    private static readonly string[] DiffOptions = { "--output", "--report", "--categories", "--max-rows", "--review" };
    private static readonly string[] DiffOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] WorkbenchSubcommands = { "contract", "verify", "receipts", "paths", "exits", "extensions", "outputs", "safeguards", "project", "handoff" };
    private static readonly string[] WorkbenchOptions = { "contract", "verify", "receipts", "paths", "exits", "extensions", "outputs", "safeguards", "project", "handoff", "--dir", "--output", "--contract" };
    private static readonly string[] WorkbenchOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] WorkbenchContractSchemas = { "workbench-contract.v1", "workbench-contract.v2" };
    private static readonly string[] WorkflowSubcommands = { "init", "validate", "simulate", "review", "registry", "run", "suggest", "examples", "receipts" };
    private static readonly string[] WorkflowOptions =
        { "--dir", "--journal", "--output", "--dry-run", "--yes", "--continue-on-error", "--timeout-ms", "--force", "--min-count", "--max-steps", "--limit", "--failed-only", "--name", "--min-duration-ms", "--sort", "--window" };
    private static readonly string[] WorkflowReportOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] WorkflowSuggestOutputFormats = { "table", "json", "yaml" };
    private static readonly string[] ReportSubcommands = { "weekly", "knowledge" };
    private static readonly string[] ReportOptions = { "--window", "--dir", "--history-dir", "--journal", "--output", "--report" };
    private static readonly string[] ReportOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] LedgerSubcommands = { "append", "replay", "query", "validate", "stats", "timeline" };
    private static readonly string[] LedgerOptions =
        { "--dir", "--project", "--source", "--since", "--until", "--window", "--action", "--category", "--operator", "--status", "--summary", "--timestamp", "--model", "--model-path", "--revit-version", "--plan-hash", "--artifact-path", "--receipt", "--receipt-hash", "--rollback-pointer", "--evidence", "--apply", "--yes", "--receipt-status", "--limit", "--fail-on", "--bucket", "--output" };
    private static readonly string[] LedgerOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] LedgerSources = { "all", "ledger", "journal", "history", "deliveries", "workflows" };
    private static readonly string[] LedgerAppendStatuses = { "planned", "succeeded", "failed", "blocked" };
    private static readonly string[] LedgerReceiptStatuses = { "all", "valid", "missing", "unreadable" };
    private static readonly string[] LedgerFailOnValues = { "error", "warning" };
    private static readonly string[] LedgerBucketValues = { "day", "hour" };
    private static readonly string[] DeliverablesSubcommands = { "list", "stats", "verify", "plan", "bundle" };
    private static readonly string[] DeliverablesOptions = { "--dir", "--profile", "--since", "--bundle-path", "--dry-run", "--force", "--output" };
    private static readonly string[] DeliverablesOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] IssueSubcommands = { "preflight", "diff", "package" };
    private static readonly string[] IssueOptions =
        { "--profile", "--output", "--fail-on", "--from", "--to", "--review", "--report", "--max-rows", "--bundle-path", "--dry-run", "--sign-journal", "--include-receipts" };
    private static readonly string[] IssueOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] IssueFailOnValues = { "warning", "error" };
    private static readonly string[] StandardsSubcommands = { "install", "validate" };
    private static readonly string[] StandardsOptions =
        { "--manifest", "--dir", "--output", "--ref", "--subpath", "--force", "--dry-run" };
    private static readonly string[] StandardsOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] ReleaseSubcommands = { "verify" };
    private static readonly string[] ReleaseOptions = { "--root", "--output", "--tag", "--strict" };
    private static readonly string[] ReleaseOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] SheetsSubcommands = { "verify", "issue-meta", "renumber", "index", "init", "show" };
    private static readonly string[] SheetsOptions =
    {
        "--against", "--rule", "--issues-only", "--output", "--path", "--force",
        "--selector", "--issue-code", "--issue-date", "--plan-output", "--param-map", "--dry-run", "--max-changes"
    };
    private static readonly string[] SheetsOutputFormats = { "table", "json", "markdown", "yaml" };
    private static readonly string[] RoomsSubcommands = { "renumber" };
    private static readonly string[] RoomsOptions = { "--rule", "--plan-output", "--scope", "--dry-run", "--max-changes", "--output" };
    private static readonly string[] RoomsOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] MarksSubcommands = { "assign", "verify" };
    private static readonly string[] MarksOptions = { "--category", "--rule", "--plan-output", "--sort", "--dry-run", "--max-changes", "--against", "--output" };
    private static readonly string[] MarksOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] SchedulesSubcommands = { "ensure", "batch-export", "compare" };
    private static readonly string[] SchedulesOptions = { "--spec", "--plan-output", "--dry-run", "--mode", "--set", "--output-dir", "--format", "--manifest", "--from", "--to", "--keys", "--output" };
    private static readonly string[] SchedulesOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] SchedulesModes = { "create-only", "sync-fields" };
    private static readonly string[] ViewsSubcommands = { "audit", "template-apply", "clone-set" };
    private static readonly string[] ViewsOptions = { "--rules", "--templates", "--browser", "--selector", "--template", "--plan-output", "--dry-run", "--exclude", "--from-set", "--to-prefix", "--naming-rule", "--include-sheets", "--output" };
    private static readonly string[] ViewsOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] ViewsExcludeValues = { "locked" };
    private static readonly string[] LinksSubcommands = { "audit", "repair" };
    private static readonly string[] LinksOptions = { "--rules", "--check", "--map", "--plan-output", "--dry-run", "--max-changes", "--output" };
    private static readonly string[] LinksOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] LinkCheckValues = { "paths", "loaded", "coordinates", "paths,loaded,coordinates" };
    private static readonly string[] ModelSubcommands = { "map-check", "map-fix" };
    private static readonly string[] ModelOptions = { "--against", "--worksets", "--phases", "--plan-output", "--scope", "--dry-run", "--max-changes", "--output" };
    private static readonly string[] ModelOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] ModelScopeValues = { "rooms", "doors", "walls", "rooms,doors,walls", "all" };
    private static readonly string[] ScheduleSubcommands = { "list", "export", "create" };
    private static readonly string[] ScheduleOptions =
        { "--category", "--name", "--fields", "--filter", "--sort", "--sort-desc", "--output", "--template", "--place-on-sheet", "--dry-run", "--receipt-dir" };
    private static readonly string[] ScheduleListOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] ScheduleExportOutputFormats = { "table", "json", "csv", "markdown" };
    private static readonly string[] ScheduleCreateOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] FamilySubcommands = { "ls", "validate", "purge", "export" };
    private static readonly string[] FamilyOptions =
    {
        "--unused", "--category", "--rules", "--rules-from", "--output", "--fail-on",
        "--keep", "--dry-run", "--apply", "--yes", "--report", "--name", "--all", "--output-dir", "--overwrite"
    };
    private static readonly string[] FamilyOutputFormats = { "table", "json", "csv", "sarif" };
    private static readonly string[] JournalSubcommands = { "show", "stats", "review", "sign", "verify" };
    private static readonly string[] JournalOptions =
        { "--dir", "--journal", "--signature", "--key", "--until", "--limit", "--high-impact-threshold", "--action", "--category", "--operator", "--user", "--output" };
    private static readonly string[] JournalOutputFormats = { "table", "json", "markdown" };
    private static readonly string[] ExampleTopics = ExamplesCommand.TopicNames;
    private static readonly string[] ExampleOptions = ExamplesCommand.TopicNames.Concat(new[] { "--output" }).ToArray();
    private static readonly string[] ExampleOutputFormats = ExamplesCommand.OutputFormats;
    private static readonly string[] PublishOptions =
        { "--profile", "--dry-run", "--output", "--since", "--since-mode", "--update-baseline" };
    private static readonly string[] PublishOutputFormats = { "table", "json" };
    private static readonly string[] SinceModes = { "content", "meta" };
    private static readonly string[] ImportOptions =
        { "--category", "--match-by", "--map", "--dry-run", "--plan-output", "--on-missing", "--on-duplicate", "--encoding", "--batch-size" };
    private static readonly string[] OnMissingValues = { "error", "warn", "skip" };
    private static readonly string[] OnDuplicateValues = { "error", "first", "all" };
    private static readonly string[] EncodingValues = { "auto", "utf-8", "gbk" };

    internal static IReadOnlyList<string> WorkbenchCompletionSubcommands => WorkbenchSubcommands;

    internal static IReadOnlyList<string> WorkbenchCompletionOptions => WorkbenchOptions;

    internal static IReadOnlyList<string> WorkbenchCompletionOutputFormats => WorkbenchOutputFormats;

    internal static IReadOnlyList<string> InspectCompletionSubcommands => InspectSubcommands;

    internal static IReadOnlyList<string> InspectCompletionOptions => InspectOptions;

    internal static IReadOnlyList<string> InspectCompletionOutputFormats => InspectOutputFormats;

    internal static IReadOnlyList<string> WorkflowCompletionSubcommands => WorkflowSubcommands;

    internal static IReadOnlyList<string> WorkflowCompletionOptions => WorkflowOptions;

    internal static IReadOnlyList<string> WorkflowReportCompletionOutputFormats => WorkflowReportOutputFormats;

    internal static IReadOnlyList<string> WorkflowSuggestCompletionOutputFormats => WorkflowSuggestOutputFormats;

    internal static IReadOnlyList<string> ScheduleCompletionSubcommands => ScheduleSubcommands;

    internal static IReadOnlyList<string> ScheduleCompletionOptions => ScheduleOptions;

    internal static IReadOnlyList<string> ScheduleListCompletionOutputFormats => ScheduleListOutputFormats;

    internal static IReadOnlyList<string> ScheduleExportCompletionOutputFormats => ScheduleExportOutputFormats;

    internal static IReadOnlyList<string> ScheduleCreateCompletionOutputFormats => ScheduleCreateOutputFormats;

    internal static IReadOnlyList<string> RoomsCompletionSubcommands => RoomsSubcommands;

    internal static IReadOnlyList<string> RoomsCompletionOptions => RoomsOptions;

    internal static IReadOnlyList<string> RoomsCompletionOutputFormats => RoomsOutputFormats;

    internal static IReadOnlyList<string> MarksCompletionSubcommands => MarksSubcommands;

    internal static IReadOnlyList<string> MarksCompletionOptions => MarksOptions;

    internal static IReadOnlyList<string> MarksCompletionOutputFormats => MarksOutputFormats;

    internal static IReadOnlyList<string> SchedulesCompletionSubcommands => SchedulesSubcommands;

    internal static IReadOnlyList<string> SchedulesCompletionOptions => SchedulesOptions;

    internal static IReadOnlyList<string> SchedulesCompletionOutputFormats => SchedulesOutputFormats;

    internal static IReadOnlyList<string> SchedulesCompletionModes => SchedulesModes;

    internal static IReadOnlyList<string> ViewsCompletionSubcommands => ViewsSubcommands;

    internal static IReadOnlyList<string> ViewsCompletionOptions => ViewsOptions;

    internal static IReadOnlyList<string> ViewsCompletionOutputFormats => ViewsOutputFormats;

    internal static IReadOnlyList<string> ViewsCompletionExcludeValues => ViewsExcludeValues;

    internal static IReadOnlyList<string> LinksCompletionSubcommands => LinksSubcommands;

    internal static IReadOnlyList<string> LinksCompletionOptions => LinksOptions;

    internal static IReadOnlyList<string> LinksCompletionOutputFormats => LinksOutputFormats;

    internal static IReadOnlyList<string> LinksCompletionCheckValues => LinkCheckValues;

    internal static IReadOnlyList<string> ModelCompletionSubcommands => ModelSubcommands;

    internal static IReadOnlyList<string> ModelCompletionOptions => ModelOptions;

    internal static IReadOnlyList<string> ModelCompletionOutputFormats => ModelOutputFormats;

    internal static IReadOnlyList<string> ModelCompletionScopeValues => ModelScopeValues;

    internal static IReadOnlyList<string> IssueCompletionSubcommands => IssueSubcommands;

    internal static IReadOnlyList<string> IssueCompletionOptions => IssueOptions;

    internal static IReadOnlyList<string> IssueCompletionOutputFormats => IssueOutputFormats;

    internal static IReadOnlyList<string> LedgerCompletionSubcommands => LedgerSubcommands;

    internal static IReadOnlyList<string> LedgerCompletionOptions => LedgerOptions;

    internal static IReadOnlyList<string> LedgerCompletionOutputFormats => LedgerOutputFormats;

    internal static IReadOnlyList<string> LedgerCompletionSources => LedgerSources;

    internal static IReadOnlyList<string> LedgerCompletionAppendStatuses => LedgerAppendStatuses;

    internal static IReadOnlyList<string> LedgerCompletionReceiptStatuses => LedgerReceiptStatuses;

    internal static IReadOnlyList<string> LedgerCompletionFailOnValues => LedgerFailOnValues;

    internal static IReadOnlyList<string> LedgerCompletionBucketValues => LedgerBucketValues;

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
        var statusOptions = JoinWords(StatusOptions);
        var statusOutputFormats = JoinWords(StatusOutputFormats);
        var doctorOptions = JoinWords(DoctorOptions);
        var doctorOutputFormats = JoinWords(DoctorOutputFormats);
        var revitYears = JoinWords(RevitYears);
        var checkOptions = JoinWords(CheckOptions);
        var checkOutputFormats = JoinWords(CheckOutputFormats);
        var scoreOptions = JoinWords(ScoreOptions);
        var scoreOutputFormats = JoinWords(ScoreOutputFormats);
        var queryOptions = JoinWords(QueryOptions);
        var inspectWords = JoinWords(InspectSubcommands.Concat(InspectOptions));
        var inspectOptions = JoinWords(InspectOptions);
        var inspectOutputFormats = JoinWords(InspectOutputFormats);
        var exportOptions = JoinWords(ExportOptions);
        var setOptions = JoinWords(SetOptions);
        var planWords = JoinWords(PlanSubcommands.Concat(PlanOptions));
        var planOutputFormats = JoinWords(PlanOutputFormats);
        var auditOptions = JoinWords(AuditOptions);
        var fixOptions = JoinWords(FixOptions);
        var rollbackOptions = JoinWords(RollbackOptions);
        var diffOptions = JoinWords(DiffOptions);
        var diffOutputFormats = JoinWords(DiffOutputFormats);
        var workbenchWords = JoinWords(WorkbenchOptions);
        var workbenchOutputFormats = JoinWords(WorkbenchOutputFormats);
        var workflowWords = JoinWords(WorkflowSubcommands.Concat(WorkflowOptions));
        var workflowOptions = JoinWords(WorkflowOptions);
        var workflowReportOutputFormats = JoinWords(WorkflowReportOutputFormats);
        var workflowSuggestOutputFormats = JoinWords(WorkflowSuggestOutputFormats);
        var reportWords = JoinWords(ReportSubcommands.Concat(ReportOptions));
        var reportOptions = JoinWords(ReportOptions);
        var reportOutputFormats = JoinWords(ReportOutputFormats);
        var ledgerWords = JoinWords(LedgerSubcommands.Concat(LedgerOptions));
        var ledgerOptions = JoinWords(LedgerOptions);
        var ledgerOutputFormats = JoinWords(LedgerOutputFormats);
        var ledgerSources = JoinWords(LedgerSources);
        var ledgerAppendStatuses = JoinWords(LedgerAppendStatuses);
        var ledgerReceiptStatuses = JoinWords(LedgerReceiptStatuses);
        var ledgerFailOnValues = JoinWords(LedgerFailOnValues);
        var ledgerBucketValues = JoinWords(LedgerBucketValues);
        var deliverablesWords = JoinWords(DeliverablesSubcommands.Concat(DeliverablesOptions));
        var deliverablesOptions = JoinWords(DeliverablesOptions);
        var deliverablesOutputFormats = JoinWords(DeliverablesOutputFormats);
        var issueWords = JoinWords(IssueSubcommands.Concat(IssueOptions));
        var issueOptions = JoinWords(IssueOptions);
        var issueOutputFormats = JoinWords(IssueOutputFormats);
        var issueFailOnValues = JoinWords(IssueFailOnValues);
        var standardsWords = JoinWords(StandardsSubcommands.Concat(StandardsOptions));
        var standardsOptions = JoinWords(StandardsOptions);
        var standardsOutputFormats = JoinWords(StandardsOutputFormats);
        var releaseWords = JoinWords(ReleaseSubcommands.Concat(ReleaseOptions));
        var releaseOptions = JoinWords(ReleaseOptions);
        var releaseOutputFormats = JoinWords(ReleaseOutputFormats);
        var sheetsWords = JoinWords(SheetsSubcommands.Concat(SheetsOptions));
        var sheetsOptions = JoinWords(SheetsOptions);
        var sheetsOutputFormats = JoinWords(SheetsOutputFormats);
        var roomsWords = JoinWords(RoomsSubcommands.Concat(RoomsOptions));
        var roomsOptions = JoinWords(RoomsOptions);
        var roomsOutputFormats = JoinWords(RoomsOutputFormats);
        var marksWords = JoinWords(MarksSubcommands.Concat(MarksOptions));
        var marksOptions = JoinWords(MarksOptions);
        var marksOutputFormats = JoinWords(MarksOutputFormats);
        var schedulesWords = JoinWords(SchedulesSubcommands.Concat(SchedulesOptions));
        var schedulesOptions = JoinWords(SchedulesOptions);
        var schedulesOutputFormats = JoinWords(SchedulesOutputFormats);
        var schedulesModes = JoinWords(SchedulesModes);
        var viewsWords = JoinWords(ViewsSubcommands.Concat(ViewsOptions));
        var viewsOptions = JoinWords(ViewsOptions);
        var viewsOutputFormats = JoinWords(ViewsOutputFormats);
        var viewsExcludeValues = JoinWords(ViewsExcludeValues);
        var linksWords = JoinWords(LinksSubcommands.Concat(LinksOptions));
        var linksOptions = JoinWords(LinksOptions);
        var linksOutputFormats = JoinWords(LinksOutputFormats);
        var linkCheckValues = JoinWords(LinkCheckValues);
        var modelWords = JoinWords(ModelSubcommands.Concat(ModelOptions));
        var modelOptions = JoinWords(ModelOptions);
        var modelOutputFormats = JoinWords(ModelOutputFormats);
        var modelScopeValues = JoinWords(ModelScopeValues);
        var scheduleWords = JoinWords(ScheduleSubcommands.Concat(ScheduleOptions));
        var scheduleOptions = JoinWords(ScheduleOptions);
        var scheduleListOutputFormats = JoinWords(ScheduleListOutputFormats);
        var scheduleExportOutputFormats = JoinWords(ScheduleExportOutputFormats);
        var scheduleCreateOutputFormats = JoinWords(ScheduleCreateOutputFormats);
        var familyWords = JoinWords(FamilySubcommands.Concat(FamilyOptions));
        var familyOptions = JoinWords(FamilyOptions);
        var familyOutputFormats = JoinWords(FamilyOutputFormats);
        var familyRules = JoinWords(FamilyValidator.AllRuleIds);
        var journalWords = JoinWords(JournalSubcommands.Concat(JournalOptions));
        var journalOutputFormats = JoinWords(JournalOutputFormats);
        var exampleWords = JoinWords(ExampleOptions);
        var exampleOutputFormats = JoinWords(ExampleOutputFormats);
        var publishOptions = JoinWords(PublishOptions);
        var publishOutputFormats = JoinWords(PublishOutputFormats);
        var sinceModes = JoinWords(SinceModes);
        var importOptions = JoinWords(ImportOptions);
        var onMissingValues = JoinWords(OnMissingValues);
        var onDuplicateValues = JoinWords(OnDuplicateValues);
        var encodingValues = JoinWords(EncodingValues);
        var configSubcommands = JoinWords(CliCommandCatalog.ConfigSubcommands);
        var configKeys = JoinWords(ConfigCommand.ValidKeys);
        var outputFormats = JoinWords(QueryCommand.ValidOutputFormats);
        var exportFormats = JoinWords(ExportCommand.ValidFormats);
        var exportOutputFormats = JoinWords(ExportOutputFormats);
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
            "        status)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{statusOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            $"            COMPREPLY=($(compgen -W \"{statusOptions}\" -- \"$cur\"))",
            "            ;;",
            "        doctor)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{doctorOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --check-version)",
            $"                    COMPREPLY=($(compgen -W \"{revitYears}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            $"            COMPREPLY=($(compgen -W \"{doctorOptions}\" -- \"$cur\"))",
            "            ;;",
            "        check)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{checkOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --profile|--report)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            $"            COMPREPLY=($(compgen -W \"{checkOptions}\" -- \"$cur\"))",
            "            ;;",
            "        score)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{scoreOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --dir)",
            "                    COMPREPLY=($(compgen -d -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            $"            COMPREPLY=($(compgen -W \"{scoreOptions}\" -- \"$cur\"))",
            "            ;;",
            "        query)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{outputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            $"            COMPREPLY=($(compgen -W \"{queryOptions}\" -- \"$cur\"))",
        "            ;;",
            "        inspect)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{inspectOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{inspectWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{inspectOptions}\" -- \"$cur\"))",
            "            ;;",
            "        export)",
            "            case \"$prev\" in",
            "                --format)",
            $"                    COMPREPLY=($(compgen -W \"{exportFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{exportOutputFormats}\" -- \"$cur\"))",
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
            "        plan)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{planOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            $"            COMPREPLY=($(compgen -W \"{planWords}\" -- \"$cur\"))",
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
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{publishOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
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
            "        diff)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{diffOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --report)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            case \"$cur\" in",
            "                -*)",
            $"                    COMPREPLY=($(compgen -W \"{diffOptions}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                *)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            ;;",
            "        workbench)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{workbenchOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --contract)",
            $"                    COMPREPLY=($(compgen -W \"{JoinWords(WorkbenchContractSchemas)}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{workbenchWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{workbenchWords}\" -- \"$cur\"))",
            "            ;;",
            "        workflow)",
            "            case \"$prev\" in",
            "                --output)",
            "                    if [ \"$subcmd\" = \"suggest\" ]; then",
            $"                        COMPREPLY=($(compgen -W \"{workflowSuggestOutputFormats}\" -- \"$cur\"))",
            "                    else",
            $"                        COMPREPLY=($(compgen -W \"{workflowReportOutputFormats}\" -- \"$cur\"))",
            "                    fi",
            "                    return",
            "                    ;;",
            "                --dir)",
            "                    COMPREPLY=($(compgen -d -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --journal)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{workflowWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            "            case \"$cur\" in",
            "                -*)",
            $"                    COMPREPLY=($(compgen -W \"{workflowOptions}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                *)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            ;;",
            "        report)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{reportOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --dir|--history-dir)",
            "                    COMPREPLY=($(compgen -d -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --journal|--report)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{reportWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{reportOptions}\" -- \"$cur\"))",
            "            ;;",
            "        ledger)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{ledgerOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --source)",
            $"                    COMPREPLY=($(compgen -W \"{ledgerSources}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --status)",
            $"                    COMPREPLY=($(compgen -W \"{ledgerAppendStatuses}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --receipt-status)",
            $"                    COMPREPLY=($(compgen -W \"{ledgerReceiptStatuses}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
                "                --fail-on)",
            $"                    COMPREPLY=($(compgen -W \"{ledgerFailOnValues}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --bucket)",
            $"                    COMPREPLY=($(compgen -W \"{ledgerBucketValues}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
                "                --dir|--project)",
                "                    COMPREPLY=($(compgen -d -- \"$cur\"))",
                "                    return",
                "                    ;;",
                "                --model-path|--artifact-path|--receipt|--evidence)",
                "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
                "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{ledgerWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{ledgerOptions}\" -- \"$cur\"))",
            "            ;;",
            "        deliverables)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{deliverablesOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --dir)",
            "                    COMPREPLY=($(compgen -d -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --profile|--since|--bundle-path)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{deliverablesWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{deliverablesOptions}\" -- \"$cur\"))",
            "            ;;",
            "        issue)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{issueOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --fail-on)",
            $"                    COMPREPLY=($(compgen -W \"{issueFailOnValues}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --profile|--from|--to|--report|--bundle-path)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{issueWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{issueOptions}\" -- \"$cur\"))",
            "            ;;",
            "        standards)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{standardsOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --dir)",
            "                    COMPREPLY=($(compgen -d -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --manifest|--subpath)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{standardsWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{standardsOptions}\" -- \"$cur\"))",
            "            ;;",
            "        release)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{releaseOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --root)",
            "                    COMPREPLY=($(compgen -d -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{releaseWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{releaseOptions}\" -- \"$cur\"))",
            "            ;;",
            "        sheets)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{sheetsOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
                "                --against|--path|--plan-output|--param-map)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --rule)",
            "                    if [ \"$subcmd\" = \"renumber\" ]; then",
            "                        COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                        return",
            "                    fi",
            $"                    COMPREPLY=($(compgen -W \"numbering.scheme numbering.gap numbering.duplicate numbering.outOfRange required.missing required.viewMissing linkage.overloaded linkage.emptySheet\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{sheetsWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{sheetsOptions}\" -- \"$cur\"))",
            "            ;;",
            "        rooms)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{roomsOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --rule|--plan-output)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{roomsWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{roomsOptions}\" -- \"$cur\"))",
            "            ;;",
            "        marks)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{marksOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --rule|--plan-output|--against)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --category)",
            "                    COMPREPLY=($(compgen -W \"doors windows doors,windows\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{marksWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{marksOptions}\" -- \"$cur\"))",
            "            ;;",
            "        schedules)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{schedulesOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --mode)",
            $"                    COMPREPLY=($(compgen -W \"{schedulesModes}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --format)",
            "                    COMPREPLY=($(compgen -W \"csv\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --spec|--plan-output|--manifest|--from|--to|--output-dir)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{schedulesWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{schedulesOptions}\" -- \"$cur\"))",
            "            ;;",
            "        views)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{viewsOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --exclude)",
            $"                    COMPREPLY=($(compgen -W \"{viewsExcludeValues}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --rules|--plan-output)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{viewsWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{viewsOptions}\" -- \"$cur\"))",
            "            ;;",
            "        links)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{linksOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --check)",
            $"                    COMPREPLY=($(compgen -W \"{linkCheckValues}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --rules|--map|--plan-output)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{linksWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{linksOptions}\" -- \"$cur\"))",
            "            ;;",
            "        model)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{modelOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --scope)",
            $"                    COMPREPLY=($(compgen -W \"{modelScopeValues}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --against|--plan-output)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{modelWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{modelOptions}\" -- \"$cur\"))",
            "            ;;",
            "        schedule)",
            "            case \"$prev\" in",
            "                --output)",
            "                    if [ \"$subcmd\" = \"list\" ]; then",
            $"                        COMPREPLY=($(compgen -W \"{scheduleListOutputFormats}\" -- \"$cur\"))",
            "                    elif [ \"$subcmd\" = \"create\" ]; then",
            $"                        COMPREPLY=($(compgen -W \"{scheduleCreateOutputFormats}\" -- \"$cur\"))",
            "                    else",
            $"                        COMPREPLY=($(compgen -W \"{scheduleExportOutputFormats}\" -- \"$cur\"))",
            "                    fi",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{scheduleWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{scheduleOptions}\" -- \"$cur\"))",
            "            ;;",
            "        family)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{familyOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --rules)",
            $"                    COMPREPLY=($(compgen -W \"{familyRules}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --rules-from|--report|--output-dir)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            "            if [ $COMP_CWORD -eq 2 ]; then",
            $"                COMPREPLY=($(compgen -W \"{familyWords}\" -- \"$cur\"))",
            "                return",
            "            fi",
            $"            COMPREPLY=($(compgen -W \"{familyOptions}\" -- \"$cur\"))",
            "            ;;",
            "        journal)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{journalOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --journal|--signature|--key)",
            "                    COMPREPLY=($(compgen -f -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "                --dir)",
            "                    COMPREPLY=($(compgen -d -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            $"            COMPREPLY=($(compgen -W \"{journalWords}\" -- \"$cur\"))",
            "            ;;",
            "        examples)",
            "            case \"$prev\" in",
            "                --output)",
            $"                    COMPREPLY=($(compgen -W \"{exampleOutputFormats}\" -- \"$cur\"))",
            "                    return",
            "                    ;;",
            "            esac",
            $"            COMPREPLY=($(compgen -W \"{exampleWords}\" -- \"$cur\"))",
            "            ;;",
            "        interactive)",
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
        var statusOutputFormats = JoinWords(StatusOutputFormats);
        var doctorOutputFormats = JoinWords(DoctorOutputFormats);
        var revitYears = JoinWords(RevitYears);
        var checkOutputFormats = JoinWords(CheckOutputFormats);
        var scoreOutputFormats = JoinWords(ScoreOutputFormats);
        var outputFormats = JoinWords(QueryCommand.ValidOutputFormats);
        var inspectSubcommands = JoinWords(InspectSubcommands);
        var inspectOutputFormats = JoinWords(InspectOutputFormats);
        var exportFormats = JoinWords(ExportCommand.ValidFormats);
        var exportOutputFormats = JoinWords(ExportOutputFormats);
        var planSubcommands = JoinWords(PlanSubcommands);
        var planOutputFormats = JoinWords(PlanOutputFormats);
        var configSubcommands = JoinWords(CliCommandCatalog.ConfigSubcommands);
        var configKeys = JoinWords(ConfigCommand.ValidKeys);
        var shells = JoinWords(CliCommandCatalog.Shells);
        var auditRules = JoinWords(AuditCommand.AvailableRules);
        var fixOptions = JoinWords(FixOptions);
        var diffOutputFormats = JoinWords(DiffOutputFormats);
        var workbenchSubcommands = JoinWords(WorkbenchSubcommands);
        var workbenchOutputFormats = JoinWords(WorkbenchOutputFormats);
        var workflowSubcommands = JoinWords(WorkflowSubcommands);
        var workflowReportOutputFormats = JoinWords(WorkflowReportOutputFormats);
        var workflowSuggestOutputFormats = JoinWords(WorkflowSuggestOutputFormats);
        var reportSubcommands = JoinWords(ReportSubcommands);
        var reportOutputFormats = JoinWords(ReportOutputFormats);
        var ledgerSubcommands = JoinWords(LedgerSubcommands);
        var ledgerOutputFormats = JoinWords(LedgerOutputFormats);
        var ledgerSources = JoinWords(LedgerSources);
        var ledgerAppendStatuses = JoinWords(LedgerAppendStatuses);
        var ledgerReceiptStatuses = JoinWords(LedgerReceiptStatuses);
        var ledgerFailOnValues = JoinWords(LedgerFailOnValues);
        var ledgerBucketValues = JoinWords(LedgerBucketValues);
        var deliverablesSubcommands = JoinWords(DeliverablesSubcommands);
        var deliverablesOutputFormats = JoinWords(DeliverablesOutputFormats);
        var issueSubcommands = JoinWords(IssueSubcommands);
        var issueOutputFormats = JoinWords(IssueOutputFormats);
        var issueFailOnValues = JoinWords(IssueFailOnValues);
        var standardsSubcommands = JoinWords(StandardsSubcommands);
        var standardsOutputFormats = JoinWords(StandardsOutputFormats);
        var releaseSubcommands = JoinWords(ReleaseSubcommands);
        var releaseOutputFormats = JoinWords(ReleaseOutputFormats);
        var sheetsSubcommands = JoinWords(SheetsSubcommands);
        var sheetsOutputFormats = JoinWords(SheetsOutputFormats);
        var roomsSubcommands = JoinWords(RoomsSubcommands);
        var roomsOutputFormats = JoinWords(RoomsOutputFormats);
        var marksSubcommands = JoinWords(MarksSubcommands);
        var marksOutputFormats = JoinWords(MarksOutputFormats);
        var schedulesSubcommands = JoinWords(SchedulesSubcommands);
        var schedulesOutputFormats = JoinWords(SchedulesOutputFormats);
        var schedulesModes = JoinWords(SchedulesModes);
        var viewsSubcommands = JoinWords(ViewsSubcommands);
        var viewsOutputFormats = JoinWords(ViewsOutputFormats);
        var viewsExcludeValues = JoinWords(ViewsExcludeValues);
        var linksSubcommands = JoinWords(LinksSubcommands);
        var linksOutputFormats = JoinWords(LinksOutputFormats);
        var linkCheckValues = JoinWords(LinkCheckValues);
        var modelSubcommands = JoinWords(ModelSubcommands);
        var modelOutputFormats = JoinWords(ModelOutputFormats);
        var modelScopeValues = JoinWords(ModelScopeValues);
        var scheduleSubcommands = JoinWords(ScheduleSubcommands);
        var scheduleListOutputFormats = JoinWords(ScheduleListOutputFormats);
        var scheduleExportOutputFormats = JoinWords(ScheduleExportOutputFormats);
        var scheduleCreateOutputFormats = JoinWords(ScheduleCreateOutputFormats);
        var familySubcommands = JoinWords(FamilySubcommands);
        var familyOutputFormats = JoinWords(FamilyOutputFormats);
        var familyRules = JoinWords(FamilyValidator.AllRuleIds);
        var journalSubcommands = JoinWords(JournalSubcommands);
        var journalOutputFormats = JoinWords(JournalOutputFormats);
        var exampleTopics = JoinWords(ExampleTopics);
        var exampleOutputFormats = JoinWords(ExampleOutputFormats);
        var publishOutputFormats = JoinWords(PublishOutputFormats);

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
            "                status)",
            "                    _arguments \\",
            $"                        '--output[Output format]:format:({statusOutputFormats})'",
            "                    ;;",
            "                doctor)",
            "                    _arguments \\",
            $"                        '--check-version[Target Revit year]:year:({revitYears})' \\",
            $"                        '--output[Output format]:format:({doctorOutputFormats})'",
            "                    ;;",
            "                check)",
            "                    _arguments \\",
            "                        '1:check set:' \\",
            "                        '--profile[Path to .revitcli.yml profile]:file:_files' \\",
            $"                        '--output[Output format]:format:({checkOutputFormats})' \\",
            "                        '--report[Save report to file]:file:_files' \\",
            "                        '--no-save[Do not save results for diff comparison]'",
            "                    ;;",
            "                score)",
            "                    _arguments \\",
            "                        '--history[History window, e.g. 7d or 30d]:duration:' \\",
            "                        '--dir[History directory]:dir:_directories' \\",
            $"                        '--output[Output format]:format:({scoreOutputFormats})'",
            "                    ;;",
                "                query)",
                "                    _arguments \\",
                "                        '--filter[Filter expression]:filter:' \\",
                "                        '--id[Element ID]:id:' \\",
                $"                        '--output[Output format]:format:({outputFormats})'",
                "                    ;;",
            "                inspect)",
            "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {inspectSubcommands}",
            "                    else",
            "                        _arguments \\",
            "                            '2:category or file:_files' \\",
            $"                            '--output[Output format]:format:({inspectOutputFormats})' \\",
            "                            '--include-empty[Show supported empty categories]' \\",
            "                            '--category[Schedule category pattern]:category:' \\",
            "                            '--name[Schedule name pattern]:name:' \\",
            "                            '--writable-only[Only confirmed writable parameters]' \\",
            "                            '--missing-only[Only parameters with missing values]' \\",
            "                            '--ready-only[Only export-ready schedules or sheets]' \\",
            "                            '--empty-only[Only zero-row schedules]' \\",
            "                            '--sheets[Sheet number/name pattern]:pattern:' \\",
            "                            '--issues-only[Only schedules or sheets with issues]'",
            "                    fi",
            "                    ;;",
            "                export)",
            "                    _arguments \\",
            $"                        '--format[Export format]:format:({exportFormats})' \\",
            "                        '--sheets[Sheet patterns]:sheets:' \\",
            "                        '--views[View patterns]:views:' \\",
            "                        '--output-dir[Output directory]:dir:_directories' \\",
            "                        '--dry-run[Validate without writing files]' \\",
            $"                        '--output[Output format for dry-runs]:format:({exportOutputFormats})'",
            "                    ;;",
                "                set)",
                "                    _arguments \\",
                "                        '--filter[Filter expression]:filter:' \\",
                "                        '--id[Element ID]:id:' \\",
                "                        '--param[Parameter name]:param:' \\",
                "                        '--value[New value]:value:' \\",
                "                        '--dry-run[Preview changes]' \\",
                "                        '--plan-output[Write saved plan JSON]:file:_files'",
                "                    ;;",
                "                plan)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {planSubcommands}",
                "                    else",
                "                        _arguments \\",
                "                            '2:plan file:_files' \\",
            $"                            '--output[Output format]:format:({planOutputFormats})' \\",
                "                            '--yes[Confirm apply]' \\",
                "                            '--dry-run[Preview apply]' \\",
                "                            '--max-changes[Maximum writes]:n:' \\",
                "                            '--allow-inferred[Allow inferred fix actions]'",
                "                    fi",
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
            $"                        '--output[Output format for dry-runs]:format:({publishOutputFormats})' \\",
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
            "                        '--no-snapshot[Skip baseline and journal support]' \\",
            "                        '--plan-output[Write saved fix plan JSON]:file:_files'",
            "                    ;;",
                "                rollback)",
                "                    _arguments \\",
                "                        '1:rollback artifact file:_files' \\",
                "                        '--dry-run[Preview rollback without applying]' \\",
                "                        '--yes[Confirm rollback apply in non-interactive mode]' \\",
                "                        '--max-changes[Maximum number of rollback writes]'",
                "                    ;;",
                "                diff)",
                "                    _arguments \\",
                "                        '1:from snapshot:_files' \\",
                "                        '2:to snapshot:_files' \\",
                $"                        '--output[Output format]:format:({diffOutputFormats})' \\",
                "                        '--report[Write review or diff report]:file:_files' \\",
                "                        '--categories[Comma-separated category filter]:categories:' \\",
                "                        '--max-rows[Rows shown per section]:n:' \\",
                "                        '--review[Render anomaly/notable/routine review]'",
                "                    ;;",
                "                workbench)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {workbenchSubcommands}",
                "                    else",
                "                        _arguments \\",
                "                            '--dir[Project directory]:dir:_directories' \\",
                $"                            '--contract[Contract schema]:schema:({JoinWords(WorkbenchContractSchemas)})' \\",
                $"                            '--output[Output format]:format:({workbenchOutputFormats})'",
                "                    fi",
                "                    ;;",
                "                workflow)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {workflowSubcommands}",
                "                    else",
                "                        if [[ \"$words[3]\" == \"suggest\" ]]; then",
                "                            _arguments \\",
                "                                '2:workflow file:_files' \\",
                "                                '--dir[Base directory]:dir:_directories' \\",
                "                                '--journal[Journal JSONL file]:file:_files' \\",
            $"                                '--output[Output format]:format:({workflowSuggestOutputFormats})' \\",
                "                                '--dry-run[Print workflow run plan without executing steps]' \\",
                "                                '--yes[Allow approved mutating steps to run]' \\",
                "                                '--continue-on-error[Continue after a failed workflow step]' \\",
                "                                '--timeout-ms[Maximum milliseconds per executed workflow step]:ms:' \\",
                "                                '--force[Overwrite existing workflow files during init]' \\",
                "                                '--min-count[Minimum repeated sequence count]:n:' \\",
                "                                '--max-steps[Maximum suggested step count]:n:' \\",
                "                                '--limit[Maximum suggestions or receipts]:n:' \\",
                "                                '--failed-only[Only show failed workflow receipts]' \\",
                "                                '--name[Only show receipts for workflow name]:name:' \\",
                "                                '--min-duration-ms[Only show workflow receipts at or above duration]:ms:' \\",
                "                                '--sort[Sort workflow receipts]:sort:(completed duration)' \\",
                "                                '--window[Only show workflow receipts in a recent window]:window:'",
                "                        else",
                "                        _arguments \\",
                "                            '2:workflow file:_files' \\",
                "                            '--dir[Base directory]:dir:_directories' \\",
                "                            '--journal[Journal JSONL file]:file:_files' \\",
            $"                            '--output[Output format]:format:({workflowReportOutputFormats})' \\",
                "                            '--dry-run[Print workflow run plan without executing steps]' \\",
                "                            '--yes[Allow approved mutating steps to run]' \\",
                "                            '--continue-on-error[Continue after a failed workflow step]' \\",
                "                            '--timeout-ms[Maximum milliseconds per executed workflow step]:ms:' \\",
                "                            '--force[Overwrite existing workflow files during init]' \\",
                "                            '--min-count[Minimum repeated sequence count]:n:' \\",
                "                            '--max-steps[Maximum suggested step count]:n:' \\",
                "                            '--limit[Maximum suggestions or receipts]:n:' \\",
                "                            '--failed-only[Only show failed workflow receipts]' \\",
                "                            '--name[Only show receipts for workflow name]:name:' \\",
                "                            '--min-duration-ms[Only show workflow receipts at or above duration]:ms:' \\",
                "                            '--sort[Sort workflow receipts]:sort:(completed duration)' \\",
                "                            '--window[Only show workflow receipts in a recent window]:window:'",
                "                        fi",
                "                    fi",
                "                    ;;",
                "                report)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {reportSubcommands}",
                "                    else",
                "                        _arguments \\",
                "                            '--window[History window]:window:' \\",
                "                            '--dir[Project directory]:dir:_directories' \\",
                "                            '--history-dir[History directory]:dir:_directories' \\",
                "                            '--journal[Journal JSONL file]:file:_files' \\",
                $"                            '--output[Output format]:format:({reportOutputFormats})' \\",
                "                            '--report[Write report file]:file:_files'",
                "                    fi",
                "                    ;;",
                "                ledger)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {ledgerSubcommands}",
                "                    else",
                "                        _arguments \\",
                "                            '--dir[Project directory]:dir:_directories' \\",
                "                            '--project[Additional project directory]:dir:_directories' \\",
            $"                            '--source[Ledger source]:source:({ledgerSources})' \\",
                "                            '--since[Only include operations at or after ISO timestamp]:timestamp:' \\",
                "                            '--until[Only include operations at or before ISO timestamp]:timestamp:' \\",
                "                            '--window[Recent window ending now]:window:' \\",
                "                            '--action[Filter operation action]:action:' \\",
                "                            '--category[Filter operation category]:category:' \\",
                "                            '--operator[Filter operation operator]:operator:' \\",
            $"                            '--status[Append status]:status:({ledgerAppendStatuses})' \\",
                "                            '--summary[Append summary]:summary:' \\",
                "                            '--timestamp[Append ISO timestamp]:timestamp:' \\",
                "                            '--model[Model identity]:model:' \\",
                "                            '--model-path[Model path]:file:_files' \\",
                "                            '--revit-version[Revit version]:version:' \\",
                "                            '--plan-hash[Plan hash]:hash:' \\",
                "                            '--artifact-path[Artifact path]:file:_files' \\",
                "                            '--receipt[Receipt path]:file:_files' \\",
                "                            '--receipt-hash[Receipt hash]:hash:' \\",
                "                            '--rollback-pointer[Rollback pointer]:command:' \\",
                "                            '--evidence[Evidence file]:file:_files' \\",
                "                            '--yes[Append the record]' \\",
            $"                            '--receipt-status[Filter receipt status]:status:({ledgerReceiptStatuses})' \\",
                "                            '--limit[Maximum operations to return]:n:' \\",
            $"                            '--fail-on[Failure threshold]:threshold:({ledgerFailOnValues})' \\",
            $"                            '--bucket[Timeline bucket]:bucket:({ledgerBucketValues})' \\",
            $"                            '--output[Output format]:format:({ledgerOutputFormats})'",
                "                    fi",
                "                    ;;",
                "                deliverables)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {deliverablesSubcommands}",
                "                    else",
                "                        _arguments \\",
                "                            '--dir[Project directory]:dir:_directories' \\",
                "                            '--profile[Profile YAML file]:file:_files' \\",
                "                            '--since[Baseline snapshot JSON file]:file:_files' \\",
                "                            '--bundle-path[Zip file path]:file:_files' \\",
                "                            '--dry-run[Plan without writing files]' \\",
                "                            '--force[Overwrite existing bundle path]' \\",
                $"                            '--output[Output format]:format:({deliverablesOutputFormats})'",
                "                    fi",
                "                    ;;",
                "                issue)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {issueSubcommands}",
                "                    else",
                "                        _arguments \\",
                "                            '--profile[Issue profile YAML]:file:_files' \\",
                "                            '--from[Baseline snapshot JSON file]:file:_files' \\",
                "                            '--to[Current snapshot JSON file or current]:file:_files' \\",
                "                            '--review[Include grouped review evidence]' \\",
                "                            '--report[Write report file]:file:_files' \\",
                "                            '--max-rows[Maximum review rows]:count:' \\",
                "                            '--bundle-path[Issue package zip path]:file:_files' \\",
                "                            '--dry-run[Plan package without writing files]' \\",
                "                            '--sign-journal[Sign local journal before packaging]' \\",
                "                            '--include-receipts[Include child receipts]:bool:(true false)' \\",
                $"                            '--fail-on[Fail threshold]:severity:({issueFailOnValues})' \\",
                $"                            '--output[Output format]:format:({issueOutputFormats})'",
                "                    fi",
                "                    ;;",
                "                standards)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {standardsSubcommands}",
                "                    else",
                "                        _arguments \\",
                "                            '--manifest[Standards manifest file]:file:_files' \\",
                "                            '--dir[Project directory]:dir:_directories' \\",
                $"                            '--output[Output format]:format:({standardsOutputFormats})' \\",
                "                            '--ref[Git branch, tag, or commit]:ref:' \\",
                "                            '--subpath[Path inside standards source]:file:_files' \\",
                "                            '--force[Overwrite existing files]' \\",
                "                            '--dry-run[Show install plan without writing files]'",
                "                    fi",
                "                    ;;",
                "                release)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {releaseSubcommands}",
                "                    else",
                "                        _arguments \\",
                "                            '--root[Repository root]:dir:_directories' \\",
            $"                            '--output[Output format]:format:({releaseOutputFormats})' \\",
                "                            '--tag[Release tag]:tag:' \\",
                "                            '--strict[Treat warnings as failures]'",
                "                    fi",
                "                    ;;",
                "                sheets)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {sheetsSubcommands}",
                "                    else",
                "                        _arguments \\",
                "                            '--against[Sheet index YAML]:file:_files' \\",
                "                            '--rule[Sheet rule or renumber YAML]:file:_files' \\",
                "                            '--issues-only[Only warning/error issues]' \\",
            $"                            '--output[Output format]:format:({sheetsOutputFormats})' \\",
                "                            '--path[Sheet index path]:file:_files' \\",
                "                            '--force[Overwrite existing sheet index]' \\",
                "                            '--selector[Sheet selector]:selector:' \\",
                "                            '--issue-code[Issue code]:code:' \\",
                "                            '--issue-date[Issue date]:date:' \\",
                "                            '--plan-output[Write sheet plan JSON]:file:_files' \\",
                "                            '--param-map[Titleblock parameter map YAML]:file:_files' \\",
                "                            '--dry-run[Preview only]' \\",
                "                            '--max-changes[Maximum planned changes]:count:'",
                "                    fi",
                "                    ;;",
                "                rooms)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {roomsSubcommands}",
                "                    else",
                "                        _arguments \\",
                "                            '--rule[Room numbering rule YAML]:file:_files' \\",
                "                            '--plan-output[Write room numbering plan JSON]:file:_files' \\",
                "                            '--scope[Room scope]:scope:' \\",
                "                            '--dry-run[Preview only]' \\",
                "                            '--max-changes[Maximum planned changes]:count:' \\",
            $"                            '--output[Output format]:format:({roomsOutputFormats})'",
                "                    fi",
                "                    ;;",
                "                marks)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {marksSubcommands}",
                "                    else",
                "                        _arguments \\",
                "                            '--category[Element category]:category:(doors windows doors,windows)' \\",
                "                            '--rule[Mark numbering rule YAML]:file:_files' \\",
                "                            '--plan-output[Write mark assignment plan JSON]:file:_files' \\",
                "                            '--sort[Sort tokens]:sort:' \\",
                "                            '--dry-run[Preview only]' \\",
                "                            '--max-changes[Maximum planned changes]:count:' \\",
                "                            '--against[Rule YAML or glob]:file:_files' \\",
            $"                            '--output[Output format]:format:({marksOutputFormats})'",
                "                    fi",
                "                    ;;",
                "                schedules)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {schedulesSubcommands}",
                "                    else",
                "                        _arguments \\",
                "                            '--spec[Schedule spec YAML or glob]:file:_files' \\",
                "                            '--plan-output[Write schedule ensure plan JSON]:file:_files' \\",
                "                            '--dry-run[Preview only]' \\",
            $"                            '--mode[Ensure mode]:mode:({schedulesModes})' \\",
                "                            '--set[Schedule set name]:set:' \\",
                "                            '--output-dir[Export output directory]:dir:_directories' \\",
                "                            '--format[Export format]:format:(csv)' \\",
                "                            '--manifest[Write export manifest JSON]:file:_files' \\",
                "                            '--from[Baseline export directory]:dir:_directories' \\",
                "                            '--to[Current export directory]:dir:_directories' \\",
                "                            '--keys[Comma-separated key columns]:keys:' \\",
            $"                            '--output[Output format]:format:({schedulesOutputFormats})'",
                "                    fi",
                "                    ;;",
                "                views)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {viewsSubcommands}",
                "                    else",
                "                        _arguments \\",
                "                            '--rules[View standards YAML]:file:_files' \\",
                "                            '--templates[Check template assignments]' \\",
                "                            '--browser[Check browser parameters]' \\",
                "                            '--selector[View selector]:selector:' \\",
                "                            '--template[Target view template]:template:' \\",
                "                            '--plan-output[Write view plan JSON]:file:_files' \\",
                "                            '--dry-run[Preview only]' \\",
            $"                            '--exclude[Exclude flags]:exclude:({viewsExcludeValues})' \\",
                "                            '--from-set[Source view selector]:selector:' \\",
                "                            '--to-prefix[Target view prefix]:prefix:' \\",
                "                            '--naming-rule[Target naming rule]:rule:' \\",
                "                            '--include-sheets[Plan sheet placement duplication]' \\",
            $"                            '--output[Output format]:format:({viewsOutputFormats})'",
                "                    fi",
                "                    ;;",
                "                links)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {linksSubcommands}",
                "                    else",
                "                        _arguments \\",
                "                            '--rules[Link audit rules YAML]:file:_files' \\",
            $"                            '--check[Checks]:check:({linkCheckValues})' \\",
                "                            '--map[Link path map YAML]:file:_files' \\",
                "                            '--plan-output[Write link repair plan JSON]:file:_files' \\",
                "                            '--dry-run[Preview only]' \\",
                "                            '--max-changes[Maximum planned repairs]:count:' \\",
            $"                            '--output[Output format]:format:({linksOutputFormats})'",
                "                    fi",
                "                    ;;",
                "                model)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {modelSubcommands}",
                "                    else",
                "                        _arguments \\",
                "                            '--against[Model mapping YAML]:file:_files' \\",
                "                            '--worksets[Check workset mappings]' \\",
                "                            '--phases[Check phase mappings]' \\",
                "                            '--plan-output[Write model map fix plan JSON]:file:_files' \\",
            $"                            '--scope[Scope]:scope:({modelScopeValues})' \\",
                "                            '--dry-run[Preview only]' \\",
                "                            '--max-changes[Maximum planned fixes]:count:' \\",
            $"                            '--output[Output format]:format:({modelOutputFormats})'",
                "                    fi",
                "                    ;;",
                "                schedule)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {scheduleSubcommands}",
                "                    else",
                "                        if [[ \"$words[3]\" == \"list\" ]]; then",
                "                        _arguments \\",
                "                            '--category[Element category]:category:' \\",
                "                            '--name[Schedule name]:name:' \\",
                "                            '--fields[Comma-separated field names]:fields:' \\",
                "                            '--filter[Filter expression]:filter:' \\",
                "                            '--sort[Sort by field]:field:' \\",
                "                            '--sort-desc[Sort descending]' \\",
            $"                            '--output[Output format]:format:({scheduleListOutputFormats})' \\",
                "                            '--template[Schedule template name]:template:' \\",
                "                            '--place-on-sheet[Sheet pattern]:sheet:' \\",
                "                            '--dry-run[Preview schedule creation without writing]' \\",
                "                            '--receipt-dir[Directory for schedule-create receipts]:dir:_directories'",
                "                        elif [[ \"$words[3]\" == \"create\" ]]; then",
                "                        _arguments \\",
                "                            '--category[Element category]:category:' \\",
                "                            '--name[Schedule name]:name:' \\",
                "                            '--fields[Comma-separated field names]:fields:' \\",
                "                            '--filter[Filter expression]:filter:' \\",
                "                            '--sort[Sort by field]:field:' \\",
                "                            '--sort-desc[Sort descending]' \\",
            $"                            '--output[Output format]:format:({scheduleCreateOutputFormats})' \\",
                "                            '--template[Schedule template name]:template:' \\",
                "                            '--place-on-sheet[Sheet pattern]:sheet:' \\",
                "                            '--dry-run[Preview schedule creation without writing]' \\",
                "                            '--receipt-dir[Directory for schedule-create receipts]:dir:_directories'",
                "                        else",
                "                        _arguments \\",
                "                            '--category[Element category]:category:' \\",
                "                            '--name[Schedule name]:name:' \\",
                "                            '--fields[Comma-separated field names]:fields:' \\",
                "                            '--filter[Filter expression]:filter:' \\",
                "                            '--sort[Sort by field]:field:' \\",
                "                            '--sort-desc[Sort descending]' \\",
            $"                            '--output[Output format]:format:({scheduleExportOutputFormats})' \\",
                "                            '--template[Schedule template name]:template:' \\",
                "                            '--place-on-sheet[Sheet pattern]:sheet:' \\",
                "                            '--dry-run[Preview schedule creation without writing]' \\",
                "                            '--receipt-dir[Directory for schedule-create receipts]:dir:_directories'",
                "                        fi",
                "                    fi",
                "                    ;;",
                "                family)",
                "                    if (( CURRENT == 3 )); then",
            $"                        _values 'subcommand' {familySubcommands}",
                "                    else",
                "                        _arguments \\",
                "                            '--unused[Only list unused families]' \\",
                "                            '--category[Revit category]:category:' \\",
                $"                            '--rules[Comma-separated family rules]:rules:({familyRules})' \\",
                "                            '--rules-from[Standards manifest file]:file:_files' \\",
                $"                            '--output[Output format]:format:({familyOutputFormats})' \\",
                "                            '--fail-on[Failure severity]:severity:(error warning)' \\",
                "                            '--keep[Keep family name patterns]:patterns:' \\",
                "                            '--dry-run[Preview family purge or export]' \\",
                "                            '--apply[Apply family purge]' \\",
                "                            '--yes[Confirm family purge]' \\",
                "                            '--report[Write family purge JSON report]:file:_files' \\",
                "                            '--name[Family name filter]:name:' \\",
                "                            '--all[Export all loadable families]' \\",
                "                            '--output-dir[Family export directory]:dir:_directories' \\",
                "                            '--overwrite[Overwrite exported .rfa files]'",
                "                    fi",
                "                    ;;",
                "                journal)",
                "                    _arguments \\",
                $"                        '1:subcommand:({journalSubcommands})' \\",
                "                        '--dir[Project directory]:dir:_directories' \\",
                "                        '--journal[Journal JSONL file]:file:_files' \\",
                "                        '--signature[Signature file]:file:_files' \\",
                "                        '--key[HMAC key file]:file:_files' \\",
                "                        '--until[Sign entries at or before timestamp]:timestamp:' \\",
                "                        '--limit[Maximum entries to show]:n:' \\",
                "                        '--high-impact-threshold[Affected count for high-impact review]:n:' \\",
                "                        '--action[Filter entries by action]:action:' \\",
                "                        '--category[Filter entries by category]:category:' \\",
                "                        '--operator[Filter entries by operator]:operator:' \\",
                "                        '--user[Filter entries by user]:user:' \\",
                $"                        '--output[Output format]:format:({journalOutputFormats})'",
                "                    ;;",
                "                examples)",
                "                    _arguments \\",
                $"                        '1:topic:({exampleTopics})' \\",
                $"                        '--output[Output format]:format:({exampleOutputFormats})'",
                "                    ;;",
                "                import)",
            "                    _arguments \\",
            "                        '1:file:_files' \\",
            "                        '--category[Revit category]:category:' \\",
            "                        '--match-by[Match-by parameter]:param:' \\",
            "                        '--map[Explicit col:Param mapping]:mapping:' \\",
            "                        '--dry-run[Preview only]' \\",
            "                        '--plan-output[Write saved plan JSON]:file:_files' \\",
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
        var statusOptions = FormatPowerShellArray(StatusOptions);
        var doctorOptions = FormatPowerShellArray(DoctorOptions);
        var checkOptions = FormatPowerShellArray(CheckOptions);
        var scoreOptions = FormatPowerShellArray(ScoreOptions);
        var queryOptions = FormatPowerShellArray(QueryOptions);
        var inspectOptions = FormatPowerShellArray(InspectSubcommands.Concat(InspectOptions));
        var exportOptions = FormatPowerShellArray(ExportOptions);
        var setOptions = FormatPowerShellArray(SetOptions);
        var planOptions = FormatPowerShellArray(PlanSubcommands.Concat(PlanOptions));
        var auditOptions = FormatPowerShellArray(AuditOptions);
        var fixOptions = FormatPowerShellArray(FixOptions);
        var rollbackOptions = FormatPowerShellArray(RollbackOptions);
        var diffOptions = FormatPowerShellArray(DiffOptions);
        var workbenchOptions = FormatPowerShellArray(WorkbenchOptions);
        var workflowOptions = FormatPowerShellArray(WorkflowSubcommands.Concat(WorkflowOptions));
        var reportOptions = FormatPowerShellArray(ReportSubcommands.Concat(ReportOptions));
        var deliverablesOptions = FormatPowerShellArray(DeliverablesSubcommands.Concat(DeliverablesOptions));
        var issueOptions = FormatPowerShellArray(IssueSubcommands.Concat(IssueOptions));
        var standardsOptions = FormatPowerShellArray(StandardsSubcommands.Concat(StandardsOptions));
        var releaseOptions = FormatPowerShellArray(ReleaseSubcommands.Concat(ReleaseOptions));
        var sheetsOptions = FormatPowerShellArray(SheetsSubcommands.Concat(SheetsOptions));
        var roomsOptions = FormatPowerShellArray(RoomsSubcommands.Concat(RoomsOptions));
        var marksOptions = FormatPowerShellArray(MarksSubcommands.Concat(MarksOptions));
        var schedulesOptions = FormatPowerShellArray(SchedulesSubcommands.Concat(SchedulesOptions));
        var viewsOptions = FormatPowerShellArray(ViewsSubcommands.Concat(ViewsOptions));
        var scheduleOptions = FormatPowerShellArray(ScheduleSubcommands.Concat(ScheduleOptions));
        var familyOptions = FormatPowerShellArray(FamilySubcommands.Concat(FamilyOptions));
        var journalOptions = FormatPowerShellArray(JournalSubcommands.Concat(JournalOptions));
        var exampleOptions = FormatPowerShellArray(ExampleOptions);
        var exampleOutputFormats = FormatPowerShellArray(ExampleOutputFormats);
        var publishOptions = FormatPowerShellArray(PublishOptions);
        var publishOutputFormats = FormatPowerShellArray(PublishOutputFormats);
        var sinceModes = FormatPowerShellArray(SinceModes);
        var importOptions = FormatPowerShellArray(ImportOptions);
        var onMissingValues = FormatPowerShellArray(OnMissingValues);
        var onDuplicateValues = FormatPowerShellArray(OnDuplicateValues);
        var encodingValues = FormatPowerShellArray(EncodingValues);
        var statusOutputFormats = FormatPowerShellArray(StatusOutputFormats);
        var doctorOutputFormats = FormatPowerShellArray(DoctorOutputFormats);
        var revitYears = FormatPowerShellArray(RevitYears);
        var checkOutputFormats = FormatPowerShellArray(CheckOutputFormats);
        var scoreOutputFormats = FormatPowerShellArray(ScoreOutputFormats);
        var outputFormats = FormatPowerShellArray(QueryCommand.ValidOutputFormats);
        var inspectOutputFormats = FormatPowerShellArray(InspectOutputFormats);
        var planOutputFormats = FormatPowerShellArray(PlanOutputFormats);
        var exportOutputFormats = FormatPowerShellArray(ExportOutputFormats);
        var diffOutputFormats = FormatPowerShellArray(DiffOutputFormats);
        var workbenchSubcommands = FormatPowerShellArray(WorkbenchSubcommands);
        var workbenchOutputFormats = FormatPowerShellArray(WorkbenchOutputFormats);
        var workbenchContractSchemas = FormatPowerShellArray(WorkbenchContractSchemas);
        var workflowSubcommands = FormatPowerShellArray(WorkflowSubcommands);
        var workflowReportOutputFormats = FormatPowerShellArray(WorkflowReportOutputFormats);
        var workflowSuggestOutputFormats = FormatPowerShellArray(WorkflowSuggestOutputFormats);
        var reportOutputFormats = FormatPowerShellArray(ReportOutputFormats);
        var ledgerOptions = FormatPowerShellArray(LedgerSubcommands.Concat(LedgerOptions));
        var ledgerOutputFormats = FormatPowerShellArray(LedgerOutputFormats);
        var ledgerSources = FormatPowerShellArray(LedgerSources);
        var ledgerAppendStatuses = FormatPowerShellArray(LedgerAppendStatuses);
        var ledgerReceiptStatuses = FormatPowerShellArray(LedgerReceiptStatuses);
        var ledgerFailOnValues = FormatPowerShellArray(LedgerFailOnValues);
        var ledgerBucketValues = FormatPowerShellArray(LedgerBucketValues);
        var deliverablesOutputFormats = FormatPowerShellArray(DeliverablesOutputFormats);
        var issueOutputFormats = FormatPowerShellArray(IssueOutputFormats);
        var issueFailOnValues = FormatPowerShellArray(IssueFailOnValues);
        var standardsOutputFormats = FormatPowerShellArray(StandardsOutputFormats);
        var releaseOutputFormats = FormatPowerShellArray(ReleaseOutputFormats);
        var sheetsOutputFormats = FormatPowerShellArray(SheetsOutputFormats);
        var roomsOutputFormats = FormatPowerShellArray(RoomsOutputFormats);
        var marksOutputFormats = FormatPowerShellArray(MarksOutputFormats);
        var schedulesSubcommands = FormatPowerShellArray(SchedulesSubcommands);
        var schedulesOutputFormats = FormatPowerShellArray(SchedulesOutputFormats);
        var schedulesModes = FormatPowerShellArray(SchedulesModes);
        var viewsSubcommands = FormatPowerShellArray(ViewsSubcommands);
        var linksOptions = FormatPowerShellArray(LinksSubcommands.Concat(LinksOptions));
        var viewsOutputFormats = FormatPowerShellArray(ViewsOutputFormats);
        var viewsExcludeValues = FormatPowerShellArray(ViewsExcludeValues);
        var linksSubcommands = FormatPowerShellArray(LinksSubcommands);
        var linksOutputFormats = FormatPowerShellArray(LinksOutputFormats);
        var linkCheckValues = FormatPowerShellArray(LinkCheckValues);
        var modelOptions = FormatPowerShellArray(ModelSubcommands.Concat(ModelOptions));
        var modelSubcommands = FormatPowerShellArray(ModelSubcommands);
        var modelOutputFormats = FormatPowerShellArray(ModelOutputFormats);
        var modelScopeValues = FormatPowerShellArray(ModelScopeValues);
        var scheduleSubcommands = FormatPowerShellArray(ScheduleSubcommands);
        var scheduleListOutputFormats = FormatPowerShellArray(ScheduleListOutputFormats);
        var scheduleExportOutputFormats = FormatPowerShellArray(ScheduleExportOutputFormats);
        var scheduleCreateOutputFormats = FormatPowerShellArray(ScheduleCreateOutputFormats);
        var familyOutputFormats = FormatPowerShellArray(FamilyOutputFormats);
        var familyRules = FormatPowerShellArray(FamilyValidator.AllRuleIds);
        var journalOutputFormats = FormatPowerShellArray(JournalOutputFormats);
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
            $"        'status' = @({statusOptions})",
            $"        'doctor' = @({doctorOptions})",
            $"        'check' = @({checkOptions})",
            $"        'score' = @({scoreOptions})",
            $"        'query' = @({queryOptions})",
            $"        'inspect' = @({inspectOptions})",
            $"        'export' = @({exportOptions})",
            $"        'set' = @({setOptions})",
            $"        'plan' = @({planOptions})",
            $"        'audit' = @({auditOptions})",
            $"        'fix' = @({fixOptions})",
            $"        'rollback' = @({rollbackOptions})",
            $"        'diff' = @({diffOptions})",
            $"        'workbench' = @({workbenchOptions})",
            $"        'workflow' = @({workflowOptions})",
            $"        'report' = @({reportOptions})",
            $"        'ledger' = @({ledgerOptions})",
            $"        'deliverables' = @({deliverablesOptions})",
            $"        'issue' = @({issueOptions})",
            $"        'standards' = @({standardsOptions})",
            $"        'release' = @({releaseOptions})",
            $"        'sheets' = @({sheetsOptions})",
            $"        'rooms' = @({roomsOptions})",
            $"        'marks' = @({marksOptions})",
            $"        'schedules' = @({schedulesOptions})",
            $"        'views' = @({viewsOptions})",
            $"        'links' = @({linksOptions})",
            $"        'model' = @({modelOptions})",
            $"        'schedule' = @({scheduleOptions})",
            $"        'family' = @({familyOptions})",
            $"        'journal' = @({journalOptions})",
            $"        'examples' = @({exampleOptions})",
            $"        'publish' = @({publishOptions})",
            $"        'import' = @({importOptions})",
            "    }",

            "",
            $"    $sinceModes = @({sinceModes})",
            $"    $publishOutputFormats = @({publishOutputFormats})",
            $"    $onMissingValues = @({onMissingValues})",
            $"    $onDuplicateValues = @({onDuplicateValues})",
            $"    $encodingValues = @({encodingValues})",
            "",
            $"    $statusOutputFormats = @({statusOutputFormats})",
            $"    $doctorOutputFormats = @({doctorOutputFormats})",
            $"    $revitYears = @({revitYears})",
            $"    $checkOutputFormats = @({checkOutputFormats})",
            $"    $scoreOutputFormats = @({scoreOutputFormats})",
            $"    $outputFormats = @({outputFormats})",
            $"    $inspectOutputFormats = @({inspectOutputFormats})",
            $"    $planOutputFormats = @({planOutputFormats})",
            $"    $exportOutputFormats = @({exportOutputFormats})",
            $"    $diffOutputFormats = @({diffOutputFormats})",
            $"    $workbenchSubcommands = @({workbenchSubcommands})",
            $"    $workbenchOutputFormats = @({workbenchOutputFormats})",
            $"    $workbenchContractSchemas = @({workbenchContractSchemas})",
            $"    $workflowSubcommands = @({workflowSubcommands})",
            $"    $workflowReportOutputFormats = @({workflowReportOutputFormats})",
            $"    $workflowSuggestOutputFormats = @({workflowSuggestOutputFormats})",
            $"    $reportOutputFormats = @({reportOutputFormats})",
            $"    $ledgerOutputFormats = @({ledgerOutputFormats})",
            $"    $ledgerSources = @({ledgerSources})",
            $"    $ledgerAppendStatuses = @({ledgerAppendStatuses})",
            $"    $ledgerReceiptStatuses = @({ledgerReceiptStatuses})",
            $"    $ledgerFailOnValues = @({ledgerFailOnValues})",
            $"    $ledgerBucketValues = @({ledgerBucketValues})",
            $"    $deliverablesOutputFormats = @({deliverablesOutputFormats})",
            $"    $issueOutputFormats = @({issueOutputFormats})",
            $"    $issueFailOnValues = @({issueFailOnValues})",
            $"    $standardsOutputFormats = @({standardsOutputFormats})",
            $"    $releaseOutputFormats = @({releaseOutputFormats})",
            $"    $sheetsOutputFormats = @({sheetsOutputFormats})",
            $"    $roomsOutputFormats = @({roomsOutputFormats})",
            $"    $marksOutputFormats = @({marksOutputFormats})",
            $"    $schedulesSubcommands = @({schedulesSubcommands})",
            $"    $schedulesOutputFormats = @({schedulesOutputFormats})",
            $"    $schedulesModes = @({schedulesModes})",
            $"    $viewsSubcommands = @({viewsSubcommands})",
            $"    $viewsOutputFormats = @({viewsOutputFormats})",
            $"    $viewsExcludeValues = @({viewsExcludeValues})",
            $"    $linksSubcommands = @({linksSubcommands})",
            $"    $linksOutputFormats = @({linksOutputFormats})",
            $"    $linkCheckValues = @({linkCheckValues})",
            $"    $modelSubcommands = @({modelSubcommands})",
            $"    $modelOutputFormats = @({modelOutputFormats})",
            $"    $modelScopeValues = @({modelScopeValues})",
            $"    $scheduleSubcommands = @({scheduleSubcommands})",
            $"    $scheduleListOutputFormats = @({scheduleListOutputFormats})",
            $"    $scheduleExportOutputFormats = @({scheduleExportOutputFormats})",
            $"    $scheduleCreateOutputFormats = @({scheduleCreateOutputFormats})",
            $"    $familyOutputFormats = @({familyOutputFormats})",
            $"    $familyRules = @({familyRules})",
            $"    $journalOutputFormats = @({journalOutputFormats})",
            $"    $exampleOutputFormats = @({exampleOutputFormats})",
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
            "        'status' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $statusOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['status'] -ToolTip 'Option'",
            "            return",
            "        }",
            "        'doctor' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $doctorOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--check-version') {",
            "                New-RevitCliCompletionResults -Values $revitYears -ToolTip 'Revit year'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['doctor'] -ToolTip 'Option'",
            "            return",
            "        }",
            "        'check' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $checkOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--profile' -or $previous -eq '--report') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['check'] -ToolTip 'Option'",
            "            return",
            "        }",
            "        'score' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $scoreOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--dir' -or $previous -eq '--project') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['score'] -ToolTip 'Option'",
            "            return",
            "        }",
        "        'query' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $outputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['query'] -ToolTip 'Option'",
            "            return",
            "        }",
            "        'inspect' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $inspectOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            New-RevitCliCompletionResults -Values $commandOptions['inspect'] -ToolTip 'Inspect subcommand or option'",
            "            return",
            "        }",
            "        'export' {",
            "            if ($previous -eq '--format') {",
            "                New-RevitCliCompletionResults -Values $exportFormats -ToolTip 'Export format'",
            "                return",
            "            }",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $exportOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--output-dir') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
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
            "        'plan' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $planOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values @('show', 'apply') -ToolTip 'Plan subcommand'",
            "                return",
            "            }",
            "            New-RevitCliCompletionResults -Values $commandOptions['plan'] -ToolTip 'Plan option'",
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
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $publishOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
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
            "        'diff' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $diffOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--report') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if (($tokens.Count -le 3 -or ($tokens.Count -eq 4 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['diff'] -ToolTip 'Diff option'",
            "            return",
            "        }",
        "        'workbench' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $workbenchOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--contract') {",
            "                New-RevitCliCompletionResults -Values $workbenchContractSchemas -ToolTip 'Contract schema'",
            "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values $workbenchSubcommands -ToolTip 'Workbench subcommand'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['workbench'] -ToolTip 'Workbench option'",
            "            return",
            "        }",
        "        'workflow' {",
            "            if ($previous -eq '--output') {",
            "                if ($tokens.Count -ge 3 -and $tokens[2] -eq 'suggest') {",
            "                    New-RevitCliCompletionResults -Values $workflowSuggestOutputFormats -ToolTip 'Output format'",
            "                } else {",
            "                    New-RevitCliCompletionResults -Values $workflowReportOutputFormats -ToolTip 'Output format'",
            "                }",
            "                return",
            "            }",
            "            if ($previous -eq '--dir') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if ($previous -eq '--journal') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values $workflowSubcommands -ToolTip 'Workflow subcommand'",
            "                return",
            "            }",
            "            if (($tokens.Count -le 4 -or ($tokens.Count -eq 5 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['workflow'] -ToolTip 'Workflow option'",
            "            return",
            "        }",
            "        'report' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $reportOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--journal' -or $previous -eq '--report') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if ($previous -eq '--dir' -or $previous -eq '--history-dir') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values @('weekly') -ToolTip 'Report subcommand'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['report'] -ToolTip 'Report option'",
            "            return",
            "        }",
            "        'ledger' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $ledgerOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--source') {",
            "                New-RevitCliCompletionResults -Values $ledgerSources -ToolTip 'Ledger source'",
            "                return",
            "            }",
            "            if ($previous -eq '--status') {",
            "                New-RevitCliCompletionResults -Values $ledgerAppendStatuses -ToolTip 'Append status'",
            "                return",
            "            }",
            "            if ($previous -eq '--receipt-status') {",
            "                New-RevitCliCompletionResults -Values $ledgerReceiptStatuses -ToolTip 'Receipt status'",
            "                return",
            "            }",
            "            if ($previous -eq '--fail-on') {",
            "                New-RevitCliCompletionResults -Values $ledgerFailOnValues -ToolTip 'Failure threshold'",
            "                return",
            "            }",
            "            if ($previous -eq '--bucket') {",
            "                New-RevitCliCompletionResults -Values $ledgerBucketValues -ToolTip 'Timeline bucket'",
            "                return",
            "            }",
            "            if ($previous -eq '--dir') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if ($previous -eq '--model-path' -or $previous -eq '--artifact-path' -or $previous -eq '--receipt' -or $previous -eq '--evidence') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values @('append', 'replay', 'query', 'validate', 'stats', 'timeline') -ToolTip 'Ledger subcommand'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['ledger'] -ToolTip 'Ledger option'",
            "            return",
            "        }",
            "        'deliverables' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $deliverablesOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--dir' -or $previous -eq '--profile' -or $previous -eq '--since' -or $previous -eq '--bundle-path') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values @('list', 'stats', 'verify', 'plan', 'bundle') -ToolTip 'Deliverables subcommand'",
            "                return",
            "            }",
            "",
        "            New-RevitCliCompletionResults -Values $commandOptions['deliverables'] -ToolTip 'Deliverables option'",
        "            return",
        "        }",
        "        'issue' {",
        "            if ($previous -eq '--output') {",
        "                New-RevitCliCompletionResults -Values $issueOutputFormats -ToolTip 'Output format'",
        "                return",
        "            }",
        "            if ($previous -eq '--fail-on') {",
        "                New-RevitCliCompletionResults -Values $issueFailOnValues -ToolTip 'Fail threshold'",
        "                return",
        "            }",
        "            if ($previous -eq '--profile' -or $previous -eq '--from' -or $previous -eq '--to' -or $previous -eq '--report' -or $previous -eq '--bundle-path') {",
        "                New-RevitCliFileCompletionResults -Path $wordToComplete",
        "                return",
        "            }",
        "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
        "                New-RevitCliCompletionResults -Values @('preflight', 'diff', 'package') -ToolTip 'Issue subcommand'",
        "                return",
        "            }",
        "",
        "            New-RevitCliCompletionResults -Values $commandOptions['issue'] -ToolTip 'Issue option'",
        "            return",
        "        }",
        "        'standards' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $standardsOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--manifest' -or $previous -eq '--dir' -or $previous -eq '--subpath') {",
                "                New-RevitCliFileCompletionResults -Path $wordToComplete",
                "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values @('install', 'validate') -ToolTip 'Standards subcommand'",
                "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['standards'] -ToolTip 'Standards option'",
            "            return",
            "        }",
            "        'release' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $releaseOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--root') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values @('verify') -ToolTip 'Release subcommand'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['release'] -ToolTip 'Release option'",
            "            return",
            "        }",
            "        'sheets' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $sheetsOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -in @('--against', '--path', '--plan-output', '--param-map')) {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if ($previous -eq '--rule' -and $tokens.Count -ge 3 -and $tokens[2] -eq 'renumber') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if ($previous -eq '--rule') {",
            "                New-RevitCliCompletionResults -Values @('numbering.scheme', 'numbering.gap', 'numbering.duplicate', 'numbering.outOfRange', 'required.missing', 'required.viewMissing', 'linkage.overloaded', 'linkage.emptySheet') -ToolTip 'Sheet rule'",
            "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values @('verify', 'issue-meta', 'renumber', 'index') -ToolTip 'Sheets subcommand'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['sheets'] -ToolTip 'Sheets option'",
            "            return",
            "        }",
            "        'rooms' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $roomsOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--rule' -or $previous -eq '--plan-output') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values @('renumber') -ToolTip 'Rooms subcommand'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['rooms'] -ToolTip 'Rooms option'",
            "            return",
            "        }",
            "        'marks' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $marksOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--rule' -or $previous -eq '--plan-output' -or $previous -eq '--against') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if ($previous -eq '--category') {",
            "                New-RevitCliCompletionResults -Values @('doors', 'windows', 'doors,windows') -ToolTip 'Marks category'",
            "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values @('assign', 'verify') -ToolTip 'Marks subcommand'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['marks'] -ToolTip 'Marks option'",
            "            return",
            "        }",
            "        'schedules' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $schedulesOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--mode') {",
            "                New-RevitCliCompletionResults -Values $schedulesModes -ToolTip 'Schedules mode'",
            "                return",
            "            }",
            "            if ($previous -eq '--format') {",
            "                New-RevitCliCompletionResults -Values @('csv') -ToolTip 'Schedule export format'",
            "                return",
            "            }",
            "            if ($previous -eq '--spec' -or $previous -eq '--plan-output' -or $previous -eq '--manifest' -or $previous -eq '--from' -or $previous -eq '--to' -or $previous -eq '--output-dir') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values $schedulesSubcommands -ToolTip 'Schedules subcommand'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['schedules'] -ToolTip 'Schedules option'",
            "            return",
            "        }",
            "        'views' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $viewsOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--exclude') {",
            "                New-RevitCliCompletionResults -Values $viewsExcludeValues -ToolTip 'Views exclusion'",
            "                return",
            "            }",
            "            if ($previous -eq '--rules' -or $previous -eq '--plan-output') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values $viewsSubcommands -ToolTip 'Views subcommand'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['views'] -ToolTip 'Views option'",
            "            return",
            "        }",
            "        'links' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $linksOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--check') {",
            "                New-RevitCliCompletionResults -Values $linkCheckValues -ToolTip 'Link check'",
            "                return",
            "            }",
            "            if ($previous -eq '--rules' -or $previous -eq '--map' -or $previous -eq '--plan-output') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values $linksSubcommands -ToolTip 'Links subcommand'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['links'] -ToolTip 'Links option'",
            "            return",
            "        }",
            "        'model' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $modelOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--scope') {",
            "                New-RevitCliCompletionResults -Values $modelScopeValues -ToolTip 'Model map scope'",
            "                return",
            "            }",
            "            if ($previous -eq '--against' -or $previous -eq '--plan-output') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values $modelSubcommands -ToolTip 'Model subcommand'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['model'] -ToolTip 'Model option'",
            "            return",
            "        }",
        "        'schedule' {",
            "            if ($previous -eq '--output') {",
            "                if ($tokens.Count -ge 3 -and $tokens[2] -eq 'list') {",
            "                    New-RevitCliCompletionResults -Values $scheduleListOutputFormats -ToolTip 'Output format'",
            "                } elseif ($tokens.Count -ge 3 -and $tokens[2] -eq 'create') {",
            "                    New-RevitCliCompletionResults -Values $scheduleCreateOutputFormats -ToolTip 'Output format'",
            "                } else {",
            "                    New-RevitCliCompletionResults -Values $scheduleExportOutputFormats -ToolTip 'Output format'",
            "                }",
            "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values $scheduleSubcommands -ToolTip 'Schedule subcommand'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['schedule'] -ToolTip 'Schedule option'",
            "            return",
            "        }",
            "        'family' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $familyOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--rules') {",
            "                New-RevitCliCompletionResults -Values $familyRules -ToolTip 'Family rule'",
            "                return",
            "            }",
            "            if ($previous -eq '--rules-from' -or $previous -eq '--report' -or $previous -eq '--output-dir') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            if (($tokens.Count -eq 2 -or ($tokens.Count -eq 3 -and -not $endsWithSpace)) -and -not $wordToComplete.StartsWith('-')) {",
            "                New-RevitCliCompletionResults -Values @('ls', 'validate', 'purge', 'export') -ToolTip 'Family subcommand'",
            "                return",
            "            }",
            "",
            "            New-RevitCliCompletionResults -Values $commandOptions['family'] -ToolTip 'Family option'",
            "            return",
            "        }",
            "        'journal' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $journalOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            if ($previous -eq '--journal' -or $previous -eq '--signature' -or $previous -eq '--key' -or $previous -eq '--dir') {",
            "                New-RevitCliFileCompletionResults -Path $wordToComplete",
            "                return",
            "            }",
            "            New-RevitCliCompletionResults -Values $commandOptions['journal'] -ToolTip 'Journal subcommand or option'",
            "            return",
            "        }",
        "        'examples' {",
            "            if ($previous -eq '--output') {",
            "                New-RevitCliCompletionResults -Values $exampleOutputFormats -ToolTip 'Output format'",
            "                return",
            "            }",
            "            New-RevitCliCompletionResults -Values $commandOptions['examples'] -ToolTip 'Example topic'",
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
