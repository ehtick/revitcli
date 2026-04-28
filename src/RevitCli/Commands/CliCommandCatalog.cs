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
        ("config", "View or modify CLI configuration"),
        ("audit", "Run model checking rules against the Revit model"),
        ("completions", "Generate shell completion script"),
        ("batch", "Execute commands from a JSON batch file"),
        ("doctor", "Check RevitCli setup and diagnose issues"),
        ("check", "Run project checks from .revitcli.yml profile"),
        ("fix", "Plan or apply profile-driven parameter fixes"),
        ("rollback", "Restore parameters changed by a fix baseline"),
        ("publish", "Run export pipeline from .revitcli.yml profile"),
        ("init", "Create a .revitcli.yml profile from a template"),
        ("score", "Calculate model health score (0-100)"),
        ("coverage", "Show parameter fill rates by category"),
        ("schedule", "Manage and export Revit schedules"),
        ("diff", "Diff two snapshot JSON files"),
        ("snapshot", "Capture model's semantic state as JSON"),
        ("interactive", "Enter interactive REPL mode"),
        ("import", "Batch-write Revit element parameters from a CSV file"),
        ("history", "Manage local snapshot history (init/capture/list/prune/diff/trend)"),
        ("mcp", "Run RevitCli as a Model Context Protocol server"),
        ("ci", "CI integration helpers (detect provider, emit workflow templates)"),
        ("profile", "Validate, resolve, diff, and install .revitcli.yml profiles"),
        ("family", "Manage Revit families (list)"),
        ("dashboard", "Serve or package the RevitCli web dashboard (v2.0)")
    };

    internal static readonly (string Command, string Description)[] InteractiveHelpEntries =
    {
        ("status", "Check if Revit plugin is online"),
        ("query <category>", "Query elements (--filter, --id, --output)"),
        ("export --format <fmt>", "Export sheets (--sheets, --output-dir)"),
        ("set <category>", "Modify parameters (--param, --value, --dry-run)"),
        ("config show/set", "View or modify configuration"),
        ("audit", "Run model checking rules (--rules, --list)"),
        ("check [name]", "Run project checks from .revitcli.yml profile"),
        ("publish [name]", "Run export pipeline from .revitcli.yml profile"),
        ("rollback <baseline>", "Restore parameters changed by a fix baseline"),
        ("score", "Calculate model health score (0-100)"),
        ("coverage", "Show parameter fill rates by category"),
        ("schedule list", "List existing schedules in the model"),
        ("schedule export", "Export schedule data (--category, --name, --fields, --output)"),
        ("schedule create", "Create a ViewSchedule (--category, --fields, --name)"),
        ("import <file> --category <cat> --match-by <param>", "Batch-write params from CSV (--dry-run, --on-missing, --on-duplicate)"),
        ("history capture", "Append a snapshot to .revitcli/history/ (--source, --exclude-fixes)"),
        ("history list", "List recent snapshots (--include-fixes, --limit)"),
        ("history prune --keep <duration|count>", "Drop old snapshots (--dry-run, --apply)"),
        ("history diff <fromRef> <toRef>", "Diff two history snapshots (--output table|json|markdown)"),
        ("history trend [--metric <name>]", "ASCII sparkline of a metric over time (--window, --width)"),
        ("score --history <duration>", "Per-day score time series across the history window"),
        ("mcp serve", "Start MCP stdio server (for Claude Desktop / Cursor)"),
        ("ci doctor", "Detect CI provider and print a workflow template"),
        ("profile validate", "Schema/reference checker for .revitcli.yml"),
        ("profile show --resolve", "Print merged effective profile (yaml|json)"),
        ("profile diff <a> <b>", "Structural diff between two profiles (table|json|markdown)"),
        ("profile install <git-url>", "Shallow-clone a remote profile bundle (--ref, --subpath, --target, --force)"),
        ("family ls", "List families in the active document (--unused, --category, --output)"),
        ("dashboard serve [--port 8080]", "Serve the prebuilt dashboard on localhost"),
        ("dashboard build --output ./public", "Copy the prebuilt dashboard + inject history into a deploy folder"),
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

        if (includeBatchCommand)
            root.AddCommand(BatchCommand.Create(client, config));

        if (includeInteractiveCommand)
            root.AddCommand(InteractiveCommand.Create(client, config));

        return root;
    }
}
