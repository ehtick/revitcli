using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using RevitCli.Fix;

namespace RevitCli.Plans;

internal sealed class FixPlanFile
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "fix";

    [JsonPropertyName("createdAtUtc")]
    public string CreatedAtUtc { get; set; } = "";

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; set; } = "";

    [JsonPropertyName("summary")]
    public FixPlanSummary Summary { get; set; } = new();

    [JsonPropertyName("actions")]
    public List<FixAction> Actions { get; set; } = new();

    [JsonPropertyName("skipped")]
    public List<FixSkippedIssue> Skipped { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    [JsonPropertyName("commands")]
    public SetPlanCommands Commands { get; set; } = new();

    internal static FixPlanFile Create(
        FixPlan plan,
        string? profilePath,
        IReadOnlyList<string>? rules,
        string? severity,
        string planPath)
    {
        var actions = (plan.Actions ?? []).Where(action => action is not null).ToList();
        var skipped = (plan.Skipped ?? []).Where(skippedIssue => skippedIssue is not null).ToList();
        var normalizedPlanPath = Path.GetFullPath(planPath);

        return new FixPlanFile
        {
            CreatedAtUtc = DateTime.UtcNow.ToString("o"),
            CreatedBy = Environment.UserName,
            Summary = new FixPlanSummary
            {
                Operation = "fix",
                CheckName = plan.CheckName,
                ProfilePath = string.IsNullOrWhiteSpace(profilePath) ? null : Path.GetFullPath(profilePath),
                ActionCount = actions.Count,
                SkippedCount = skipped.Count,
                InferredCount = actions.Count(action => action.Inferred),
                Rules = (rules ?? Array.Empty<string>())
                    .Where(rule => !string.IsNullOrWhiteSpace(rule))
                    .ToList(),
                Severity = string.IsNullOrWhiteSpace(severity) ? null : severity
            },
            Actions = actions,
            Skipped = skipped,
            Warnings = (plan.Warnings ?? []).Where(warning => warning is not null).ToList(),
            Commands = new SetPlanCommands
            {
                Show = $"revitcli plan show {QuoteArgument(normalizedPlanPath)}",
                Apply = $"revitcli plan apply {QuoteArgument(normalizedPlanPath)} --yes",
                DryRunApply = $"revitcli plan apply {QuoteArgument(normalizedPlanPath)} --dry-run"
            }
        };
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}

internal sealed class FixPlanSummary
{
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = "fix";

    [JsonPropertyName("checkName")]
    public string CheckName { get; set; } = "default";

    [JsonPropertyName("profilePath")]
    public string? ProfilePath { get; set; }

    [JsonPropertyName("actionCount")]
    public int ActionCount { get; set; }

    [JsonPropertyName("skippedCount")]
    public int SkippedCount { get; set; }

    [JsonPropertyName("inferredCount")]
    public int InferredCount { get; set; }

    [JsonPropertyName("rules")]
    public List<string> Rules { get; set; } = new();

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }
}
