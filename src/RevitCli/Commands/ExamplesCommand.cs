using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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
            "Discover categories, parameters, schedules, and sheets before planning work.",
            new[]
            {
                "revitcli inspect categories",
                "revitcli inspect params doors",
                "revitcli inspect schedules",
                "revitcli inspect sheets --issues-only"
            },
            "Find what can be exported or checked in this model using read-only commands."),
        new(
            "sheets",
            "Find sheet blockers and dry-run export candidates.",
            new[]
            {
                "revitcli inspect sheets",
                "revitcli inspect sheets --ready-only",
                "revitcli inspect sheets --issues-only",
                "revitcli export --format pdf --sheets \"A1*\" --dry-run"
            },
            "Check whether this model is ready for issue; do not write files yet."),
        new(
            "schedule",
            "List and export schedule data for tables and deliverables.",
            new[]
            {
                "revitcli inspect schedules",
                "revitcli schedule list",
                "revitcli schedule export --name \"Door Schedule\" --output csv",
                "revitcli schedule export --category doors --fields all --output json"
            },
            "Export the door schedule to CSV and report any missing schedule fields."),
        new(
            "set",
            "Preview and save a reviewed parameter-write plan before applying.",
            new[]
            {
                "revitcli inspect params doors",
                "revitcli set doors --filter \"id = 12345\" --param \"Fire Rating\" --value \"60min\" --dry-run",
                "revitcli set doors --filter \"Mark = D-01\" --param \"Fire Rating\" --value \"60min\" --plan-output .revitcli/plans/fire-rating.json",
                "revitcli plan show .revitcli/plans/fire-rating.json",
                "revitcli plan apply .revitcli/plans/fire-rating.json --dry-run"
            },
            "Build a reviewed plan for this parameter change; summarize it before apply."),
        new(
            "import",
            "Write CSV data through dry-run groups and saved plans.",
            new[]
            {
                "revitcli import doors.csv --category doors --match-by Mark --dry-run",
                "revitcli import doors.csv --category doors --match-by Mark --map \"DoorMark:Mark,Rating:Fire Rating\" --plan-output .revitcli/plans/doors.json",
                "revitcli plan show .revitcli/plans/doors.json",
                "revitcli plan apply .revitcli/plans/doors.json --yes"
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
                "revitcli publish issue"
            },
            "Run the pre-issue workflow as dry-run first and explain any blockers."),
        new(
            "doctor",
            "Diagnose install, add-in, server, and live Revit-version issues.",
            new[]
            {
                "revitcli doctor --check-version 2026",
                "revitcli status",
                "revitcli config show",
                ".\\scripts\\smoke-revit.ps1 -Version 2026 -ElementId 12345 -Filter \"id = 12345\""
            },
            "Diagnose why RevitCli is not connecting; start with doctor and status."),
        new(
            "journal",
            "Sign and verify local operation history after writes or exports.",
            new[]
            {
                "revitcli journal sign",
                "revitcli journal verify",
                "revitcli history capture --source manual",
                "revitcli history list"
            },
            "Verify the journal after today's smoke and summarize the changed operations.")
    };

    internal static string[] TopicNames => Topics.Select(topic => topic.Name).ToArray();

    public static Command Create()
    {
        var topicArg = new Argument<string?>(
            "topic",
            () => null,
            $"Example topic: {string.Join(", ", TopicNames)}");

        var command = new Command("examples", "Show copy-paste examples for common architect workflows")
        {
            topicArg
        };

        command.SetHandler(async (string? topic) =>
        {
            Environment.ExitCode = await ExecuteAsync(Console.Out, topic);
        }, topicArg);

        return command;
    }

    public static async Task<int> ExecuteAsync(TextWriter output, string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            await output.WriteLineAsync("Available example topics:");
            foreach (var item in Topics)
            {
                await output.WriteLineAsync($"  {item.Name,-10} {item.Summary}");
            }

            await output.WriteLineAsync();
            await output.WriteLineAsync("Run: revitcli examples <topic>");
            return 0;
        }

        var match = Topics.FirstOrDefault(item =>
            string.Equals(item.Name, topic, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            await output.WriteLineAsync($"Unknown example topic: {topic}");
            await output.WriteLineAsync($"Available: {string.Join(", ", TopicNames)}");
            return 1;
        }

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

        return 0;
    }
}
