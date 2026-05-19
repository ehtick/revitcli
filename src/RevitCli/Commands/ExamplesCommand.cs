using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Output;

namespace RevitCli.Commands;

public static class ExamplesCommand
{

    private sealed record ExampleTopic(
        string Name,
        string Summary,
        string[] Commands,
        string? CodexPrompt = null);

    private static readonly ExampleTopic[] Topics =
    {
        new(
            "inspect",
            "Discover categories, parameters, schedules, sheets, local workflows, and saved plans before planning work.",
            new[]
            {
                "revitcli inspect categories",
                "revitcli inspect params doors",
                "revitcli inspect params doors --writable-only --missing-only",
                "revitcli inspect schedules",
                "revitcli inspect schedules --issues-only --output markdown",
                "revitcli inspect sheets --issues-only --output markdown",
                "revitcli inspect workflows --output markdown",
                "revitcli inspect plans --output markdown"
            },
            "Find what can be exported or checked in this model using read-only commands."),
        new(
            "sheets",
            "Find sheet blockers and dry-run export candidates.",
            new[]
            {
                "revitcli inspect sheets",
                "revitcli inspect sheets --ready-only",
                "revitcli inspect sheets --issues-only --output markdown",
                "revitcli sheets verify --output json --issues-only",
                "revitcli sheets index init",
                "revitcli export --format pdf --sheets \"A1*\" --dry-run"
            },
            "Check whether this model is ready for issue; verify sheet numbering and required sheets before export."),
        new(
            "schedule",
            "List and export schedule data for tables and deliverables.",
            new[]
            {
                "revitcli inspect schedules",
                "revitcli inspect schedules --category Doors --ready-only",
                "revitcli inspect schedules --empty-only",
                "revitcli inspect schedules --issues-only --output markdown",
                "revitcli schedule list --output markdown",
                "revitcli schedule export --name \"Door Schedule\" --output csv",
                "revitcli schedule export --name \"Door Schedule\" --output markdown",
                "revitcli schedule export --category doors --fields all --output json",
                "revitcli schedule create --category Doors --fields \"Mark,Level\" --name \"Door Review\" --dry-run --output json"
            },
            "Export the door schedule to CSV and report any missing schedule fields."),
        new(
            "set",
            "Preview and save a reviewed parameter-write plan before applying.",
            new[]
            {
                "revitcli inspect params doors",
                "revitcli inspect params doors --name \"Fire*\" --writable-only --missing-only",
                "revitcli set doors --filter \"id = 12345\" --param \"Fire Rating\" --value \"60min\" --dry-run",
                "revitcli set doors --filter \"Mark = D-01\" --param \"Fire Rating\" --value \"60min\" --plan-output .revitcli/plans/fire-rating.json",
                "revitcli plan show .revitcli/plans/fire-rating.json --output markdown",
                "revitcli plan apply .revitcli/plans/fire-rating.json --dry-run",
                "revitcli plan apply .revitcli/plans/fire-rating.json --yes --max-changes 250 --high-impact-threshold 50 --confirm-high-impact",
                "revitcli rollback .revitcli/plans/fire-rating.json.receipt.json --dry-run",
                "revitcli rollback .revitcli/plans/fire-rating.json.receipt.json --yes --max-changes 250"
            },
            "Build a reviewed plan for this parameter change; summarize it in Chinese before apply."),
        new(
            "import",
            "Write CSV data through dry-run groups and saved plans.",
            new[]
            {
                "revitcli import doors.csv --category doors --match-by Mark --dry-run",
                "revitcli import doors.csv --category doors --match-by Mark --map \"DoorMark:Mark,Rating:Fire Rating\" --plan-output .revitcli/plans/doors.json",
                "revitcli plan show .revitcli/plans/doors.json --output markdown",
                "revitcli plan apply .revitcli/plans/doors.json --yes",
                "revitcli rollback .revitcli/plans/doors.json.receipt.json --dry-run"
            },
            "Validate this CSV against the model and create a plan; do not apply until I approve."),
        new(
            "publish",
            "Run profile checks and deliverable exports with preflight.",
            new[]
            {
                "revitcli profile simulate issue",
                "revitcli check issue",
                "revitcli publish issue --dry-run",
                "revitcli publish issue",
                "revitcli deliverables verify",
                "revitcli deliverables list --output json"
            },
            "Run the pre-issue workflow as dry-run first and explain any blockers."),
        new(
            "deliverables",
            "Review delivery manifests and receipts after real exports or publishes.",
            new[]
            {
                "revitcli deliverables list",
                "revitcli deliverables stats",
                "revitcli deliverables verify",
                "revitcli deliverables verify --output json",
                "revitcli deliverables verify --output markdown",
                "revitcli deliverables bundle --dry-run --output markdown",
                "revitcli deliverables bundle --bundle-path deliverables/review-package.zip"
            },
            "Verify today's exported deliverables, then build a review package with receipts."),
        new(
            "review",
            "Summarize snapshot changes and flag suspicious model edits.",
            new[]
            {
                "revitcli snapshot --output .revitcli/snap-before.json",
                "revitcli snapshot --output .revitcli/snap-after.json",
                "revitcli diff .revitcli/snap-before.json .revitcli/snap-after.json --review",
                "revitcli diff .revitcli/snap-before.json .revitcli/snap-after.json --review --output json",
                "revitcli history diff @-2 @-1 --review"
            },
            "Review the latest model changes and tell me which ones need human attention."),
        new(
            "workbench",
            "Discover and verify the v4 terminal workbench contract before delegating tasks.",
            new[]
            {
                "revitcli workbench contract --output json",
                "revitcli workbench contract --output markdown",
                "revitcli workbench verify --output json",
                "revitcli workbench verify --output markdown",
                "revitcli workbench receipts --output json",
                "revitcli workbench paths --output json",
                "revitcli workbench exits --output json",
                "revitcli workbench extensions --output json",
                "revitcli workbench outputs --output json",
                "revitcli workbench safeguards --output json",
                "revitcli workbench project --output json",
                "revitcli workbench handoff --output markdown",
                "revitcli score --history 30d --output json",
                "revitcli examples workflow --output json",
                "revitcli workflow review .revitcli/workflows/pre-issue.yml --output markdown"
            },
            "Show the stable RevitCli workbench contract and receipt index, verify it locally, then choose the safest command path for this task."),
        new(
            "workflow",
            "Validate, simulate, run, and review reusable terminal workflow YAML.",
            new[]
            {
                "revitcli workflow init pre-issue",
                "revitcli workflow init all",
                "revitcli workflow validate",
                "revitcli workflow validate .revitcli/workflows/pre-issue.yml",
                "revitcli workflow simulate .revitcli/workflows/pre-issue.yml",
                "revitcli workflow review .revitcli/workflows/pre-issue.yml --output markdown",
                "revitcli workflow run .revitcli/workflows/pre-issue.yml --dry-run",
                "revitcli workflow run .revitcli/workflows/pre-issue.yml --yes --timeout-ms 600000",
                "revitcli workflow run .revitcli/workflows/pre-issue.yml --yes",
                "revitcli workflow simulate .revitcli/workflows/pre-issue.yml --output json",
                "revitcli workflow suggest --output yaml",
                "revitcli workflow receipts --output markdown",
                "revitcli workflow receipts --min-duration-ms 60000 --output markdown",
                "revitcli workflow receipts --sort duration --output json",
                "revitcli workflow receipts --window 24h --sort duration --output markdown",
                "revitcli workflow examples",
                "revitcli workflow examples export-package --output markdown"
            },
            "Show me the pre-issue workflow steps and risk modes before anything mutates or exports."),
        new(
            "report",
            "Generate weekly history, score, diff review, and journal summaries.",
            new[]
            {
                "revitcli report weekly",
                "revitcli report weekly --window 30d",
                "revitcli report weekly --output markdown",
                "revitcli report weekly --report .revitcli/reports/weekly.md"
            },
            "Create this week's model health report from local history and journal data."),
        new(
            "standards",
            "Validate local office standards before issue work starts.",
            new[]
            {
                "revitcli standards install ../office-standards --dry-run --output markdown",
                "revitcli standards install ../office-standards",
                "revitcli standards validate --manifest .revitcli/standards.yml",
                "revitcli standards validate --output markdown",
                "revitcli workflow validate --output markdown",
                "revitcli family validate --rules-from .revitcli/standards.yml"
            },
            "Check whether this project has the required profile, workflows, outputs, schedules, and family rules."),
        new(
            "family",
            "Review family bloat, validation findings, and purge reports before cleanup.",
            new[]
            {
                "revitcli workflow init family-cleanup",
                "revitcli workflow simulate .revitcli/workflows/family-cleanup.yml",
                "revitcli family ls --unused",
                "revitcli family validate --rules-from .revitcli/standards.yml",
                "revitcli family purge --dry-run --report .revitcli/reports/family-purge.json",
                "revitcli family purge --apply --yes --report .revitcli/reports/family-purge-applied.json"
            },
            "Preview unused family cleanup and write a purge report; do not apply until I approve."),
        new(
            "recipes",
            "Open documented Codex CLI prompt-to-command recipes.",
            new[]
            {
                "ls docs/templates/codex-recipes",
                "sed -n '1,160p' docs/templates/codex-recipes/pre-issue.md",
                "sed -n '1,160p' docs/templates/codex-recipes/standards-bootstrap.md",
                "sed -n '1,160p' docs/templates/codex-recipes/family-cleanup.md",
                "sed -n '1,160p' docs/templates/codex-recipes/release-preflight.md",
                "sed -n '1,160p' docs/templates/codex-recipes/sheet-frame-verify.md",
                "sed -n '1,160p' docs/templates/codex-recipes/weekly-review.md",
                "revitcli workflow suggest --output yaml"
            },
            "Use the local recipe templates to map my request to explicit revitcli commands; do not invent hidden steps."),
        new(
            "doctor",
            "Diagnose install, add-in, server, and live Revit-version issues.",
            new[]
            {
                "revitcli doctor --output json",
                "revitcli doctor --check-version 2026",
                "revitcli status",
                "revitcli config show",
                ".\\scripts\\smoke-revit.ps1 -Version 2026 -ElementId 12345 -Filter \"id = 12345\""
            },
            "Diagnose why RevitCli is not connecting; start with doctor and status."),
        new(
            "release",
            "Check local release files, version, CI guardrails, and smoke documentation before tagging.",
            new[]
            {
                "revitcli release verify",
                "revitcli release verify --tag v2.3.0",
                "revitcli release verify --tag v2.3.0 --output json",
                "revitcli release verify --tag v2.3.0 --output markdown",
                "revitcli doctor --check-version 2026",
                ".\\scripts\\smoke-revit.ps1 -Version 2026 -ElementId 12345 -Filter \"id = 12345\"",
                "revitcli journal verify"
            },
            "Run release preflight and summarize any version, CI, checklist, or smoke evidence gaps."),
        new(
            "journal",
            "Inspect, sign, and verify local operation history after writes or exports.",
            new[]
            {
                "revitcli journal show --limit 10",
                "revitcli journal stats",
                "revitcli journal review",
                "revitcli journal review --output markdown",
                "revitcli journal sign",
                "revitcli journal verify",
                "revitcli history capture --source manual"
            },
            "Review today's journal by risk, operator, category, and affected element IDs.")
    };

    internal static string[] TopicNames => Topics.Select(topic => topic.Name).ToArray();
    internal static string[] OutputFormats => new[] { "table", "json", "markdown" };

    public static Command Create()
    {
        var topicArg = new Argument<string?>(
            "topic",
            () => null,
            $"Example topic: {string.Join(", ", TopicNames)}");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");

        var command = new Command("examples", "Show copy-paste examples for common architect workflows")
        {
            topicArg,
            outputOpt
        };

        command.SetHandler(async (string? topic, string outputFormat) =>
        {
            Environment.ExitCode = await ExecuteAsync(Console.Out, topic, outputFormat);
        }, topicArg, outputOpt);

        return command;
    }

    public static async Task<int> ExecuteAsync(TextWriter output, string? topic, string outputFormat = "table")
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        var selectedTopics = SelectTopics(topic, output).ToArray();
        if (selectedTopics.Length == 0)
            return 1;

        if (normalizedOutput == "json")
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(
                new ExampleRecipesEnvelope(
                    "example-recipes.v1",
                    string.IsNullOrWhiteSpace(topic) ? null : selectedTopics[0].Name,
                    selectedTopics.Select(ToContract).ToArray()),
                TerminalJsonOptions.CompactContract));
            return 0;
        }

        if (normalizedOutput == "markdown")
        {
            await WriteMarkdownAsync(output, topic, selectedTopics);
            return 0;
        }

        if (string.IsNullOrWhiteSpace(topic))
        {
            await output.WriteLineAsync("Available example topics:");
            foreach (var item in selectedTopics)
            {
                await output.WriteLineAsync($"  {item.Name,-10} {item.Summary}");
            }

            await output.WriteLineAsync();
            await output.WriteLineAsync("Run: revitcli examples <topic>");
            return 0;
        }

        var match = selectedTopics[0];
        await WriteTopicAsync(output, match);
        return 0;
    }

    private static ExampleTopic[] SelectTopics(string? topic, TextWriter output)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return Topics;

        var match = Topics.FirstOrDefault(item =>
            string.Equals(item.Name, topic, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            output.WriteLine($"Unknown example topic: {topic}");
            output.WriteLine($"Available: {string.Join(", ", TopicNames)}");
            return Array.Empty<ExampleTopic>();
        }

        return new[] { match };
    }

    private static async Task WriteTopicAsync(TextWriter output, ExampleTopic match)
    {
        await output.WriteLineAsync($"# {match.Name}");
        await output.WriteLineAsync(match.Summary);
        await output.WriteLineAsync();
        await output.WriteLineAsync("Commands:");
        foreach (var command in match.Commands)
        {
            await output.WriteLineAsync($"  {command}");
        }

        if (!string.IsNullOrWhiteSpace(match.CodexPrompt))
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync("Codex prompt:");
            await output.WriteLineAsync($"  {match.CodexPrompt}");
        }
    }

    private static async Task WriteMarkdownAsync(TextWriter output, string? topic, ExampleTopic[] selectedTopics)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            await output.WriteLineAsync("# RevitCli Example Recipes");
            await output.WriteLineAsync();
            await output.WriteLineAsync("| Topic | Summary |");
            await output.WriteLineAsync("|---|---|");
            foreach (var item in selectedTopics)
            {
                await output.WriteLineAsync($"| `{item.Name}` | {EscapeTableCell(item.Summary)} |");
            }

            return;
        }

        var match = selectedTopics[0];
        await output.WriteLineAsync($"# {match.Name}");
        await output.WriteLineAsync();
        await output.WriteLineAsync(match.Summary);
        await output.WriteLineAsync();
        await output.WriteLineAsync("## Commands");
        await output.WriteLineAsync();
        foreach (var command in match.Commands)
        {
            await output.WriteLineAsync($"- `{command}`");
        }

        if (!string.IsNullOrWhiteSpace(match.CodexPrompt))
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync("## Codex Prompt");
            await output.WriteLineAsync();
            await output.WriteLineAsync(match.CodexPrompt);
        }
    }

    private static string EscapeTableCell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);

    private static ExampleTopicContract ToContract(ExampleTopic topic) =>
        new(topic.Name, topic.Summary, topic.Commands, topic.CodexPrompt);

    public sealed record ExampleRecipesEnvelope(
        string SchemaVersion,
        string? Topic,
        IReadOnlyList<ExampleTopicContract> Topics);

    public sealed record ExampleTopicContract(
        string Name,
        string Summary,
        IReadOnlyList<string> Commands,
        string? CodexPrompt);
}
