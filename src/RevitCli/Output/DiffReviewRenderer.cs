using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RevitCli.Shared;

namespace RevitCli.Output;

public static class DiffReviewRenderer
{
    private const int LargeBatchThreshold = 10;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static readonly string[] CriticalParameterTokens =
    {
        "fire",
        "egress",
        "exit",
        "life safety",
        "struct",
        "load",
        "occupancy",
        "smoke",
        "compartment"
    };

    private static readonly string[] IdentityParameterNames =
    {
        "number",
        "sheet number",
        "room number",
        "mark",
        "type mark"
    };

    public static DiffReviewReport Build(SnapshotDiff diff)
    {
        var report = new DiffReviewReport
        {
            SchemaVersion = diff.SchemaVersion,
            From = diff.From,
            To = diff.To,
            Warnings = diff.Warnings.ToList(),
            TotalChanges = CountChanges(diff)
        };

        foreach (var (category, changes) in diff.Categories.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            AddCategoryReviewGroups(report.Groups, "category", category, changes);
        }

        AddSheetReviewGroups(report.Groups, diff.Sheets);
        AddScheduleReviewGroups(report.Groups, diff.Schedules);

        report.Groups = report.Groups
            .OrderBy(group => SeverityRank(group.Severity))
            .ThenBy(group => group.Scope, StringComparer.Ordinal)
            .ThenBy(group => group.Name, StringComparer.Ordinal)
            .ThenBy(group => group.Parameter ?? "", StringComparer.Ordinal)
            .ThenBy(group => group.ChangeType, StringComparer.Ordinal)
            .ToList();

        report.HighestSeverity = report.Groups.Count == 0 ? "none" : report.Groups[0].Severity;

        foreach (var group in report.Groups)
        {
            if (!report.SeverityCounts.TryGetValue(group.Severity, out var count))
            {
                count = 0;
            }

            report.SeverityCounts[group.Severity] = count + 1;

            if (group.Severity is "anomaly" or "notable" &&
                !string.IsNullOrWhiteSpace(group.Recommendation) &&
                !report.RecommendedActions.Contains(group.Recommendation, StringComparer.Ordinal))
            {
                report.RecommendedActions.Add(group.Recommendation);
            }
        }

        return report;
    }

    public static string Render(DiffReviewReport report, string format, int maxRows)
    {
        return format?.ToLowerInvariant() switch
        {
            "json" => JsonSerializer.Serialize(report, JsonOpts),
            "markdown" or "md" => RenderMarkdown(report, maxRows),
            _ => RenderTable(report, maxRows)
        };
    }

    private static void AddCategoryReviewGroups(
        List<DiffReviewGroup> groups,
        string scope,
        string name,
        CategoryDiff changes)
    {
        if (changes.Removed.Count > 0)
        {
            AddGroup(
                groups,
                "anomaly",
                scope,
                name,
                "removed",
                null,
                changes.Removed.Count,
                changes.Removed.Select(item => item.Id),
                $"{name}: {changes.Removed.Count} removed",
                $"Verify removed {name} were intentional ({SampleText(changes.Removed.Select(item => item.Id))}).");
        }

        if (changes.Added.Count > 0)
        {
            var severity = changes.Added.Count >= LargeBatchThreshold ? "notable" : "routine";
            AddGroup(
                groups,
                severity,
                scope,
                name,
                "added",
                null,
                changes.Added.Count,
                changes.Added.Select(item => item.Id),
                $"{name}: {changes.Added.Count} added",
                severity == "notable"
                    ? $"Confirm large {name} addition before publish ({SampleText(changes.Added.Select(item => item.Id))})."
                    : null);
        }

        AddModifiedGroups(groups, scope, name, changes.Modified);
    }

    private static void AddSheetReviewGroups(List<DiffReviewGroup> groups, CategoryDiff changes)
    {
        AddNonCategoryGroups(groups, "sheet", "sheets", changes);
    }

    private static void AddScheduleReviewGroups(List<DiffReviewGroup> groups, CategoryDiff changes)
    {
        AddNonCategoryGroups(groups, "schedule", "schedules", changes);
    }

    private static void AddNonCategoryGroups(
        List<DiffReviewGroup> groups,
        string scope,
        string name,
        CategoryDiff changes)
    {
        if (changes.Removed.Count > 0)
        {
            AddGroup(
                groups,
                "anomaly",
                scope,
                name,
                "removed",
                null,
                changes.Removed.Count,
                changes.Removed.Select(item => item.Id),
                $"{name}: {changes.Removed.Count} removed",
                $"Verify removed {name} were intentional ({SampleText(changes.Removed.Select(item => item.Id))}).");
        }

        if (changes.Added.Count > 0)
        {
            AddGroup(
                groups,
                "notable",
                scope,
                name,
                "added",
                null,
                changes.Added.Count,
                changes.Added.Select(item => item.Id),
                $"{name}: {changes.Added.Count} added",
                $"Confirm added {name} belong in the current issue scope ({SampleText(changes.Added.Select(item => item.Id))}).");
        }

        AddModifiedGroups(groups, scope, name, changes.Modified);
    }

    private static void AddModifiedGroups(
        List<DiffReviewGroup> groups,
        string scope,
        string name,
        List<ModifiedItem> modified)
    {
        var hashOnly = modified.Where(item => item.Changed.Count == 0).ToList();
        if (hashOnly.Count > 0)
        {
            AddGroup(
                groups,
                "notable",
                scope,
                name,
                "modified",
                null,
                hashOnly.Count,
                hashOnly.Select(item => item.Id),
                $"{name}: {hashOnly.Count} modified with hash-only detail",
                $"Review {name} hash-only changes in the full diff ({SampleText(hashOnly.Select(item => item.Id))}).");
        }

        var byParameter = modified
            .SelectMany(item => item.Changed
                .OrderBy(change => change.Key, StringComparer.Ordinal)
                .Select(change => new ParameterChangeItem(item.Id, change.Key, change.Value)))
            .GroupBy(item => item.Parameter, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var parameterGroup in byParameter)
        {
            var items = parameterGroup.ToList();
            var lostValues = items.Where(item => HasValue(item.Change.From) && !HasValue(item.Change.To)).ToList();
            var severity = ClassifyParameterGroup(name, parameterGroup.Key, items, lostValues);
            var changeType = lostValues.Count > 0 ? "lost-values" : "modified";
            var message = lostValues.Count > 0
                ? $"{name}: {parameterGroup.Key} lost values on {lostValues.Count}"
                : $"{name}: {parameterGroup.Key} changed on {items.Count}";
            var recommendation = RecommendationForParameterGroup(name, parameterGroup.Key, severity, lostValues, items);
            var ids = lostValues.Count > 0 ? lostValues.Select(item => item.Id) : items.Select(item => item.Id);

            AddGroup(
                groups,
                severity,
                scope,
                name,
                changeType,
                parameterGroup.Key,
                lostValues.Count > 0 ? lostValues.Count : items.Count,
                ids,
                message,
                recommendation);
        }
    }

    private static string ClassifyParameterGroup(
        string name,
        string parameter,
        List<ParameterChangeItem> items,
        List<ParameterChangeItem> lostValues)
    {
        if (lostValues.Count > 0)
        {
            return "anomaly";
        }

        if (IsCriticalParameter(parameter) || IsIdentityParameter(name, parameter))
        {
            return "notable";
        }

        return items.Count >= LargeBatchThreshold ? "notable" : "routine";
    }

    private static string? RecommendationForParameterGroup(
        string name,
        string parameter,
        string severity,
        List<ParameterChangeItem> lostValues,
        List<ParameterChangeItem> items)
    {
        if (lostValues.Count > 0)
        {
            return $"Restore or confirm blank {name}.{parameter} values ({SampleText(lostValues.Select(item => item.Id))}).";
        }

        if (severity == "notable" && IsCriticalParameter(parameter))
        {
            return $"Review {name}.{parameter} changes for code or deliverable impact ({SampleText(items.Select(item => item.Id))}).";
        }

        if (severity == "notable" && IsIdentityParameter(name, parameter))
        {
            return $"Verify {name}.{parameter} identity changes before exporting ({SampleText(items.Select(item => item.Id))}).";
        }

        if (severity == "notable")
        {
            return $"Confirm batch {name}.{parameter} change scope ({SampleText(items.Select(item => item.Id))}).";
        }

        return null;
    }

    private static void AddGroup(
        List<DiffReviewGroup> groups,
        string severity,
        string scope,
        string name,
        string changeType,
        string? parameter,
        int count,
        IEnumerable<long> ids,
        string message,
        string? recommendation)
    {
        groups.Add(new DiffReviewGroup
        {
            Severity = severity,
            Scope = scope,
            Name = name,
            ChangeType = changeType,
            Parameter = parameter,
            Count = count,
            SampleIds = ids.Distinct().Take(20).ToList(),
            Message = message,
            Recommendation = recommendation
        });
    }

    private static string RenderTable(DiffReviewReport report, int maxRows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(report.TotalChanges == 0
            ? $"Review: no changes between {Label(report.From)} and {Label(report.To)}"
            : $"Review: {report.TotalChanges} changes between {Label(report.From)} and {Label(report.To)}");
        sb.AppendLine($"Highest severity: {report.HighestSeverity}");

        foreach (var warning in report.Warnings)
        {
            sb.AppendLine($"[warn] {warning}");
        }

        if (report.Groups.Count == 0)
        {
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine();
        sb.AppendLine("Summary by severity:");
        foreach (var severity in new[] { "anomaly", "notable", "routine" })
        {
            if (report.SeverityCounts.TryGetValue(severity, out var count))
            {
                sb.AppendLine($"  {severity}: {count}");
            }
        }

        foreach (var severity in new[] { "anomaly", "notable", "routine" })
        {
            var groups = report.Groups.Where(group => group.Severity == severity).ToList();
            if (groups.Count == 0)
            {
                continue;
            }

            sb.AppendLine();
            sb.AppendLine(CultureHeader(severity));
            foreach (var group in groups.Take(maxRows))
            {
                sb.AppendLine($"  - [{group.Scope}/{group.Name}] {group.ChangeType}: {group.Message}{SampleSuffix(group.SampleIds)}");
            }

            if (groups.Count > maxRows)
            {
                sb.AppendLine($"  ...and {groups.Count - maxRows} more {severity} groups");
            }
        }

        if (report.RecommendedActions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Recommended actions:");
            foreach (var action in report.RecommendedActions.Take(maxRows))
            {
                sb.AppendLine($"  - {action}");
            }

            if (report.RecommendedActions.Count > maxRows)
            {
                sb.AppendLine($"  ...and {report.RecommendedActions.Count - maxRows} more actions");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderMarkdown(DiffReviewReport report, int maxRows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Diff review");
        sb.AppendLine();
        sb.AppendLine(report.TotalChanges == 0
            ? $"No changes between `{Label(report.From)}` and `{Label(report.To)}`."
            : $"{report.TotalChanges} changes between `{Label(report.From)}` and `{Label(report.To)}`.");
        sb.AppendLine();
        sb.AppendLine($"**Highest severity:** {report.HighestSeverity}");

        foreach (var warning in report.Warnings)
        {
            sb.AppendLine();
            sb.AppendLine($"> {warning}");
        }

        foreach (var severity in new[] { "anomaly", "notable", "routine" })
        {
            var groups = report.Groups.Where(group => group.Severity == severity).ToList();
            if (groups.Count == 0)
            {
                continue;
            }

            sb.AppendLine();
            sb.AppendLine($"### {CultureHeader(severity)}");
            foreach (var group in groups.Take(maxRows))
            {
                sb.AppendLine($"- **{EscapeMd(group.Scope)}/{EscapeMd(group.Name)}** `{EscapeMd(group.ChangeType)}`: {EscapeMd(group.Message)}{EscapeMd(SampleSuffix(group.SampleIds))}");
            }

            if (groups.Count > maxRows)
            {
                sb.AppendLine($"- ...and {groups.Count - maxRows} more {severity} groups");
            }
        }

        if (report.RecommendedActions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Recommended actions");
            foreach (var action in report.RecommendedActions.Take(maxRows))
            {
                sb.AppendLine($"- {EscapeMd(action)}");
            }

            if (report.RecommendedActions.Count > maxRows)
            {
                sb.AppendLine($"- ...and {report.RecommendedActions.Count - maxRows} more actions");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static int CountChanges(SnapshotDiff diff) =>
        diff.Categories.Values.Sum(CountChanges) +
        CountChanges(diff.Sheets) +
        CountChanges(diff.Schedules);

    private static int CountChanges(CategoryDiff diff) =>
        diff.Added.Count + diff.Removed.Count + diff.Modified.Count;

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool IsCriticalParameter(string parameter) =>
        CriticalParameterTokens.Any(token =>
            parameter.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static bool IsIdentityParameter(string name, string parameter)
    {
        if (!IdentityParameterNames.Contains(parameter.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.Contains("room", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("sheet", StringComparison.OrdinalIgnoreCase) ||
               parameter.Contains("mark", StringComparison.OrdinalIgnoreCase);
    }

    private static int SeverityRank(string severity) =>
        severity switch
        {
            "anomaly" => 0,
            "notable" => 1,
            "routine" => 2,
            _ => 3
        };

    private static string CultureHeader(string severity) =>
        severity switch
        {
            "anomaly" => "Anomaly",
            "notable" => "Notable",
            "routine" => "Routine",
            _ => severity
        };

    private static string SampleSuffix(IReadOnlyList<long> ids) =>
        ids.Count == 0 ? "" : $" (sample ids: {string.Join(", ", ids.Take(5))})";

    private static string SampleText(IEnumerable<long> ids)
    {
        var sample = ids.Distinct().Take(5).ToList();
        return sample.Count == 0 ? "no sample ids" : $"sample ids: {string.Join(", ", sample)}";
    }

    private static string Label(string value) =>
        string.IsNullOrWhiteSpace(value) ? "snapshot" : value;

    private static string EscapeMd(string value) =>
        value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private sealed record ParameterChangeItem(long Id, string Parameter, ParamChange Change);
}

public sealed class DiffReviewReport
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("from")]
    public string From { get; set; } = "";

    [JsonPropertyName("to")]
    public string To { get; set; } = "";

    [JsonPropertyName("totalChanges")]
    public int TotalChanges { get; set; }

    [JsonPropertyName("highestSeverity")]
    public string HighestSeverity { get; set; } = "none";

    [JsonPropertyName("severityCounts")]
    public Dictionary<string, int> SeverityCounts { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    [JsonPropertyName("groups")]
    public List<DiffReviewGroup> Groups { get; set; } = new();

    [JsonPropertyName("recommendedActions")]
    public List<string> RecommendedActions { get; set; } = new();
}

public sealed class DiffReviewGroup
{
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "";

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("changeType")]
    public string ChangeType { get; set; } = "";

    [JsonPropertyName("parameter")]
    public string? Parameter { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("sampleIds")]
    public List<long> SampleIds { get; set; } = new();

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("recommendation")]
    public string? Recommendation { get; set; }
}
