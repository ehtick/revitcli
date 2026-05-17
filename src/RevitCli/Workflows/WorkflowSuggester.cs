using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RevitCli.Journal;

namespace RevitCli.Workflows;

internal static class WorkflowSuggester
{
    public static WorkflowSuggestionResult Suggest(
        JournalReadResult journal,
        int minCount,
        int maxSteps,
        int limit)
    {
        minCount = Math.Max(2, minCount);
        maxSteps = Math.Clamp(maxSteps, 2, 10);
        limit = Math.Clamp(limit, 1, 20);

        var commands = journal.Entries
            .Select(ReadCommandEntry)
            .Where(entry => entry != null)
            .Cast<WorkflowCommandEntry>()
            .ToList();
        var suggestions = FindRepeatedSequences(commands, minCount, maxSteps, limit)
            .Select((sequence, index) => BuildSuggestion(sequence, index + 1))
            .ToList();

        return new WorkflowSuggestionResult(
            journal.JournalPath,
            journal.Entries.Count,
            commands.Count,
            minCount,
            maxSteps,
            suggestions);
    }

    private static IReadOnlyList<RepeatedSequence> FindRepeatedSequences(
        IReadOnlyList<WorkflowCommandEntry> commands,
        int minCount,
        int maxSteps,
        int limit)
    {
        var candidates = new List<RepeatedSequence>();
        for (var length = Math.Min(maxSteps, commands.Count); length >= 2; length--)
        {
            var grouped = new Dictionary<string, SequenceAccumulator>(StringComparer.Ordinal);
            for (var i = 0; i <= commands.Count - length; i++)
            {
                var slice = commands.Skip(i).Take(length).Select(entry => entry.Command).ToArray();
                var key = string.Join("\u001f", slice);
                if (!grouped.TryGetValue(key, out var accumulator))
                {
                    accumulator = new SequenceAccumulator(slice, commands[i].LineNumber);
                    grouped[key] = accumulator;
                }

                accumulator.Count++;
            }

            foreach (var accumulator in grouped.Values.Where(item => item.Count >= minCount))
            {
                candidates.Add(new RepeatedSequence(
                    accumulator.Commands,
                    accumulator.Count,
                    accumulator.FirstLine));
            }
        }

        return candidates
            .OrderByDescending(item => item.Count)
            .ThenByDescending(item => item.Commands.Count)
            .ThenBy(item => item.FirstLine)
            .Take(limit)
            .ToList();
    }

    private static WorkflowSuggestion BuildSuggestion(RepeatedSequence sequence, int index)
    {
        var name = $"suggested-workflow-{index}";
        var steps = sequence.Commands
            .Select((command, stepIndex) => new WorkflowSuggestedStep(
                stepIndex + 1,
                $"step {stepIndex + 1}",
                command,
                InferMode(command),
                RequiresApproval(command)))
            .ToList();

        return new WorkflowSuggestion(
            name,
            sequence.Count,
            sequence.FirstLine,
            steps,
            RenderYaml(name, sequence.Count, steps));
    }

    private static WorkflowCommandEntry? ReadCommandEntry(JournalEntrySummary entry)
    {
        try
        {
            using var doc = JsonDocument.Parse(entry.Raw);
            var root = doc.RootElement;
            var command = ReadString(root, "command")
                ?? ReadString(root, "commandLine")
                ?? ReadString(root, "run");
            if (string.IsNullOrWhiteSpace(command))
            {
                return null;
            }

            command = command.Trim();
            if (!command.Equals("revitcli", StringComparison.OrdinalIgnoreCase) &&
                !command.StartsWith("revitcli ", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new WorkflowCommandEntry(entry.LineNumber, command);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string InferMode(string command)
    {
        var words = TokenizeOrEmpty(command);
        if (words.Contains("--dry-run", StringComparer.OrdinalIgnoreCase))
        {
            return "dry-run";
        }

        if (LooksReadOnly(words))
        {
            return "read-only";
        }

        return "mutating";
    }

    private static bool RequiresApproval(string command) =>
        string.Equals(InferMode(command), "mutating", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> TokenizeOrEmpty(string command)
    {
        try
        {
            return WorkflowCommandLine.Tokenize(command);
        }
        catch (FormatException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool LooksReadOnly(IReadOnlyList<string> words)
    {
        if (words.Count < 2)
        {
            return false;
        }

        var command = words[1];
        if (IsAny(command,
                "status",
                "doctor",
                "query",
                "audit",
                "score",
                "coverage",
                "inspect",
                "examples",
                "snapshot",
                "diff",
                "ci"))
        {
            return true;
        }

        if (IsAny(command, "check", "config", "profile") ||
            (IsAny(command, "workflow") && words.Count >= 3 && IsAny(words[2], "validate", "simulate", "suggest")) ||
            (IsAny(command, "family") && words.Count >= 3 && IsAny(words[2], "ls", "validate")) ||
            (IsAny(command, "schedule") && words.Count >= 3 && IsAny(words[2], "list", "export")) ||
            (IsAny(command, "history") && words.Count >= 3 && IsAny(words[2], "list", "diff", "trend")) ||
            (IsAny(command, "journal") && words.Count >= 3 && IsAny(words[2], "show", "stats", "verify")) ||
            (IsAny(command, "report") && words.Count >= 3 && IsAny(words[2], "weekly")) ||
            (IsAny(command, "standards") && words.Count >= 3 && IsAny(words[2], "validate")) ||
            (IsAny(command, "plan") && words.Count >= 3 && IsAny(words[2], "show")))
        {
            return true;
        }

        return false;
    }

    private static string RenderYaml(
        string name,
        int count,
        IReadOnlyList<WorkflowSuggestedStep> steps)
    {
        var sb = new StringBuilder();
        sb.AppendLine("version: 1");
        sb.AppendLine($"name: {name}");
        sb.AppendLine($"description: {QuoteYaml($"Suggested from {count} repeated journal command sequences. Review before running.")}");
        sb.AppendLine("steps:");
        foreach (var step in steps)
        {
            sb.AppendLine($"  - name: {QuoteYaml(step.Name)}");
            sb.AppendLine($"    run: {QuoteYaml(step.Run)}");
            sb.AppendLine($"    mode: {step.Mode}");
            if (step.RequiresApproval)
            {
                sb.AppendLine("    requiresApproval: true");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string QuoteYaml(string value) =>
        "'" + value.Replace("'", "''") + "'";

    private static bool IsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));

    private sealed record WorkflowCommandEntry(int LineNumber, string Command);

    private sealed class SequenceAccumulator
    {
        public SequenceAccumulator(IReadOnlyList<string> commands, int firstLine)
        {
            Commands = commands;
            FirstLine = firstLine;
        }

        public IReadOnlyList<string> Commands { get; }
        public int Count { get; set; }
        public int FirstLine { get; }
    }

    private sealed record RepeatedSequence(
        IReadOnlyList<string> Commands,
        int Count,
        int FirstLine);
}

internal sealed record WorkflowSuggestionResult(
    [property: JsonPropertyName("journalPath")] string JournalPath,
    [property: JsonPropertyName("entryCount")] int EntryCount,
    [property: JsonPropertyName("commandEntryCount")] int CommandEntryCount,
    [property: JsonPropertyName("minCount")] int MinCount,
    [property: JsonPropertyName("maxSteps")] int MaxSteps,
    [property: JsonPropertyName("suggestions")] IReadOnlyList<WorkflowSuggestion> Suggestions);

internal sealed record WorkflowSuggestion(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("firstLine")] int FirstLine,
    [property: JsonPropertyName("steps")] IReadOnlyList<WorkflowSuggestedStep> Steps,
    [property: JsonPropertyName("yaml")] string Yaml);

internal sealed record WorkflowSuggestedStep(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("run")] string Run,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("requiresApproval")] bool RequiresApproval);
