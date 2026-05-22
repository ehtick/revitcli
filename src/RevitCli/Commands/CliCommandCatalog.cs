using System.CommandLine;
using System.Linq;
using RevitCli.Client;
using RevitCli.Config;

namespace RevitCli.Commands;

internal static class CliCommandCatalog
{
    internal static readonly (string Name, string Description)[] TopLevelCommands =
    {
        ("status", "Check if Revit plugin is online"),
        ("query", "Query elements from the Revit model"),
        ("export", "Export sheets or views from the Revit model"),
        ("set", "Modify element parameters in the Revit model"),
        ("plan", "Review and apply saved mutation plans"),
        ("config", "View or modify CLI configuration"),
        ("audit", "Run model checking rules against the Revit model"),
        ("completions", "Generate shell completion script"),
        ("batch", "Execute commands from a JSON batch file"),
        ("doctor", "Check RevitCli setup and diagnose issues"),
        ("check", "Run project checks from .revitcli.yml profile"),
        ("fix", "Plan or apply profile-driven parameter fixes"),
        ("rollback", "Restore parameters from a fix baseline or plan receipt"),
        ("publish", "Run export pipeline from .revitcli.yml profile"),
        ("init", "Create a .revitcli.yml profile from a template"),
        ("score", "Calculate model health score (0-100)"),
        ("coverage", "Show parameter fill rates by category"),
        ("inspect", "Discover model data for safe terminal workflows"),
        ("examples", "Show copy-paste examples for common architect workflows"),
        ("workbench", "Show stable terminal workbench contract for Codex CLI"),
        ("workflow", "Create, validate, run, and review terminal workflow YAML files"),
        ("report", "Generate local project reports from history and journal data"),
        ("deliverables", "Review local delivery plans, manifests, and receipts"),
        ("issue", "Run issue preflight, diff, and package contracts"),
        ("standards", "Install and validate local office standards requirements"),
        ("release", "Verify local release readiness and CI guardrails"),
        ("sheets", "Verify sheet numbering and local sheet-frame expectations"),
        ("rooms", "Plan and review room numbering workflows"),
        ("marks", "Plan and verify door/window Mark numbering workflows"),
        ("schedules", "Ensure, batch-export, and compare versioned schedule specs"),
        ("views", "Audit, template, and clone view sets"),
        ("links", "Audit and plan safe coordination link repairs"),
        ("model", "Audit and plan safe model mapping fixes"),
        ("schedule", "Manage and export Revit schedules"),
        ("diff", "Diff and review two snapshot JSON files"),
        ("snapshot", "Capture model's semantic state as JSON"),
        ("interactive", "Enter interactive REPL mode"),
        ("import", "Batch-write Revit element parameters from a CSV file"),
        ("history", "Manage local snapshot history (init/capture/list/prune/diff/trend)"),
        ("ci", "CI integration helpers (detect provider, emit workflow templates)"),
        ("profile", "Validate, resolve, diff, and install .revitcli.yml profiles"),
        ("family", "Manage Revit families (list, validate, purge, export)"),
        ("dashboard", "Serve or package the RevitCli web dashboard (v2.0)"),
        ("journal", "Inspect, sign, and verify the operation journal")
    };

    internal static readonly (string Command, string Description)[] InteractiveHelpEntries =
    {
        ("status", "Check if Revit plugin is online"),
        ("query <category>", "Query elements (--filter, --id, --output)"),
        ("export --format <fmt>", "Export sheets (--sheets, --output-dir)"),
        ("set <category>", "Modify parameters (--param, --value, --dry-run)"),
        ("plan show/apply", "Review or apply saved mutation plans"),
        ("config show/set", "View or modify configuration"),
        ("audit", "Run model checking rules (--rules, --list)"),
        ("check [name]", "Run project checks from .revitcli.yml profile"),
        ("publish [name]", "Run export pipeline from .revitcli.yml profile"),
        ("rollback <artifact>", "Restore parameters from a fix baseline or plan receipt"),
        ("score", "Calculate model health score (0-100)"),
        ("coverage", "Show parameter fill rates by category"),
        ("inspect categories", "List common categories and next discovery commands"),
        ("inspect params <category>", "List parameters seen in a category"),
        ("inspect sheets", "List sheets with export dry-run commands"),
        ("inspect schedules", "List schedules with filters, readiness, and export commands"),
        ("inspect workflows", "List local workflow YAML files and next review commands"),
        ("inspect plans", "List saved mutation plans with dry-run apply and rollback commands"),
        ("examples <topic>", "Show copy-paste commands for common workflows"),
        ("workbench contract", "Show stable command, output, dry-run, receipt, and exit-code contract"),
        ("workbench verify", "Verify terminal workbench command contract and recipe readiness"),
        ("workbench receipts", "Show receipt schemas, paths, dry-runs, and review commands"),
        ("workbench paths", "Show flat callable command paths with risk and output contracts"),
        ("workbench exits", "Show predictable exit-code notes for callable command paths"),
        ("workbench extensions", "Show terminal-first extension points and validation commands"),
        ("workbench outputs", "Show readable table and compact JSON output contracts"),
        ("workbench safeguards", "Show dry-run, approval, receipt, and review paths for risky commands"),
        ("workbench project", "Inspect local workbench project artifacts and review commands"),
        ("workbench handoff", "Print a local terminal handoff summary for Codex CLI"),
        ("workflow init <template>", "Create .revitcli/workflows YAML from built-in templates"),
        ("workflow validate [file]", "Validate .revitcli/workflows YAML without running commands"),
        ("workflow simulate <file>", "Print workflow steps and risk modes without running commands"),
        ("workflow review <file>", "Review workflow readiness, approval gates, and handoff evidence"),
        ("workflow run <file>", "Run workflow steps (--dry-run, --yes, --continue-on-error)"),
        ("workflow suggest", "Suggest workflow YAML from repeated journal command sequences"),
        ("workflow examples", "Show architect prompts and acceptance command paths for workflow templates"),
        ("workflow receipts", "Review workflow-run receipts (--failed-only, --output table|json|markdown)"),
        ("report weekly", "Generate weekly history/score/diff/journal report"),
        ("report knowledge", "Summarize reusable local project knowledge from RevitCli artifacts"),
        ("deliverables list", "List delivery manifest entries and receipt status"),
        ("deliverables stats", "Summarize delivery manifest kinds, outcomes, and receipt status"),
        ("deliverables verify", "Verify delivery manifest entries point to readable receipts"),
        ("deliverables plan", "Plan profile deliverable exports with baseline and risk evidence"),
        ("deliverables bundle", "Package manifest receipts and output files into a zip with a bundle receipt"),
        ("issue preflight", "Review issue readiness and hidden mutation risks"),
        ("issue diff", "Create issue-scoped snapshot diff reports"),
        ("issue package", "Package deliverables, receipts, hashes, and journal evidence"),
        ("standards install <path-or-git-url>", "Install approved standards files into the local project"),
        ("standards validate", "Validate required profiles, workflows, outputs, schedules, and family rules"),
        ("release verify", "Check release files, version, tag, and CI guardrails before tagging"),
        ("sheets verify", "Verify sheet numbering, required sheets, and placed-view counts"),
        ("sheets issue-meta", "Plan sheet issue metadata updates with frozen sheet ids"),
        ("sheets renumber", "Plan sheet number updates from numbering rules with frozen sheet ids"),
        ("sheets index init", "Create .revitcli/sheets/index.yml from current sheets"),
        ("sheets index show", "Show the local sheet index declaration"),
        ("rooms renumber", "Plan room Number updates from numbering rules with frozen room ids"),
        ("marks assign", "Plan door/window Mark updates from numbering rules with frozen element ids"),
        ("marks verify", "Verify duplicate, missing, and rule-mismatched door/window Marks"),
        ("schedules ensure", "Plan missing or drifted schedules from schedule-spec.v1 YAML"),
        ("schedules batch-export", "Export a schedule spec set and write a schedule-export-manifest.v1"),
        ("schedules compare", "Compare two schedule CSV export directories"),
        ("views audit", "Audit view templates, naming, and browser standards"),
        ("views template-apply", "Plan view template assignment changes with frozen view ids"),
        ("views clone-set", "Plan a cloned view set with collision checks"),
        ("links audit", "Audit Revit link paths, load status, and coordinate fingerprints"),
        ("links repair", "Plan link path/load repairs without moving coordinates"),
        ("model map-check", "Audit element workset and phase mappings"),
        ("model map-fix", "Plan workset and phase mapping fixes with writable probes"),
        ("schedule list", "List existing schedules in the model"),
        ("schedule export", "Export schedule data (--category, --name, --fields, --output)"),
        ("schedule create", "Preview or create a ViewSchedule (--dry-run, --output, --receipt-dir)"),
        ("import <file> --category <cat> --match-by <param>", "Batch-write params from CSV (--dry-run, --on-missing, --on-duplicate)"),
        ("history capture", "Append a snapshot to .revitcli/history/ (--source, --exclude-fixes)"),
        ("history list", "List recent snapshots (--include-fixes, --limit)"),
        ("history prune --keep <duration|count>", "Drop old snapshots (--dry-run, --apply)"),
        ("diff <from> <to>", "Diff two snapshot JSON files (--review, --output table|json|markdown)"),
        ("history diff <fromRef> <toRef>", "Diff two history snapshots (--review, --output table|json|markdown)"),
        ("history trend [--metric <name>]", "ASCII sparkline of a metric over time (--window, --width)"),
        ("score --history <duration>", "Per-day score time series across the history window (--output table|json|markdown)"),
        ("ci doctor", "Detect CI provider and print a workflow template"),
        ("profile validate", "Schema/reference checker for .revitcli.yml"),
        ("profile show --resolve", "Print merged effective profile (yaml|json)"),
        ("profile diff <a> <b>", "Structural diff between two profiles (table|json|markdown)"),
        ("profile install <git-url>", "Shallow-clone a remote profile bundle (--ref, --subpath, --target, --force)"),
        ("family ls", "List families in the active document (--unused, --category, --output)"),
        ("family validate", "Validate families (--rules, --rules-from, --output, --fail-on)"),
        ("dashboard serve [--port 8080]", "Serve the prebuilt dashboard on localhost"),
        ("dashboard build --output ./public", "Copy the prebuilt dashboard + inject history into a deploy folder"),
        ("journal show", "Show recent .revitcli/journal.jsonl entries"),
        ("journal stats", "Summarize journal entries by action"),
        ("journal review", "Review journal activity by risk, operator, category, and affected elements"),
        ("journal sign", "Sign .revitcli/journal.jsonl into journal.jsonl.sig"),
        ("journal verify", "Verify journal.jsonl against journal.jsonl.sig"),
        ("init <template>", "Create .revitcli.yml from starter template"),
        ("doctor", "Check setup, server discovery, and connectivity"),
        ("batch <file>", "Execute commands from a JSON batch file"),
        ("completions <shell>", "Generate shell completion script"),
        ("help", "Show this command list"),
        ("clear", "Clear the screen"),
        ("exit", "Exit interactive mode")
    };

    internal static readonly string[] Shells = { "bash", "zsh", "powershell" };
    internal static readonly string[] ConfigSubcommands = { "show", "set" };

    internal static string[] TopLevelCommandNames =>
        TopLevelCommands.Select(command => command.Name).ToArray();

    internal static RootCommand CreateRootCommand(
        RevitClient client,
        CliConfig config,
        bool includeInteractiveCommand,
        bool includeBatchCommand)
    {
        var root = new RootCommand("RevitCli - Command-line interface for Autodesk Revit");
        root.AddCommand(StatusCommand.Create(client));
        root.AddCommand(QueryCommand.Create(client, config));
        root.AddCommand(ExportCommand.Create(client, config));
        root.AddCommand(SetCommand.Create(client));
        root.AddCommand(PlanCommand.Create(client));
        root.AddCommand(ConfigCommand.Create());
        root.AddCommand(AuditCommand.Create(client));
        root.AddCommand(CompletionsCommand.Create());
        root.AddCommand(DoctorCommand.Create(client, config));
        root.AddCommand(CheckCommand.Create(client));
        root.AddCommand(FixCommand.Create(client));
        root.AddCommand(RollbackCommand.Create(client));
        root.AddCommand(PublishCommand.Create(client));
        root.AddCommand(InitCommand.Create());
        root.AddCommand(ScoreCommand.Create(client));
        root.AddCommand(CoverageCommand.Create(client));
        root.AddCommand(InspectCommand.Create(client));
        root.AddCommand(ExamplesCommand.Create());
        root.AddCommand(WorkbenchCommand.Create());
        root.AddCommand(WorkflowCommand.Create());
        root.AddCommand(ReportCommand.Create());
        root.AddCommand(DeliverablesCommand.Create());
        root.AddCommand(IssueCommand.Create(client));
        root.AddCommand(StandardsCommand.Create());
        root.AddCommand(ReleaseCommand.Create());
        root.AddCommand(SheetsCommand.Create(client));
        root.AddCommand(RoomsCommand.Create(client));
        root.AddCommand(MarksCommand.Create(client));
        root.AddCommand(SchedulesCommand.Create(client));
        root.AddCommand(ViewsCommand.Create(client));
        root.AddCommand(LinksCommand.Create(client));
        root.AddCommand(ModelCommand.Create(client));
        root.AddCommand(ScheduleCommand.Create(client));
        root.AddCommand(DiffCommand.Create());
        root.AddCommand(SnapshotCommand.Create(client));

        root.AddCommand(ImportCommand.Create(client));
        root.AddCommand(HistoryCommand.Create(client));
        root.AddCommand(McpCommand.Create(client));
        root.AddCommand(CiCommand.Create());
        root.AddCommand(ProfileCommand.Create());
        root.AddCommand(FamilyCommand.Create(client));
        root.AddCommand(DashboardCommand.Create());
        root.AddCommand(JournalCommand.Create());

        if (includeBatchCommand)
            root.AddCommand(BatchCommand.Create(client, config));

        if (includeInteractiveCommand)
            root.AddCommand(InteractiveCommand.Create(client, config));

        return root;
    }
}
