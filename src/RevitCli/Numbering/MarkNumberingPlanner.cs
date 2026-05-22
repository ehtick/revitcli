using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RevitCli.Output;
using RevitCli.Plans;
using RevitCli.Shared;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RevitCli.Numbering;

internal static class MarkNumberingPlanner
{
    public const string DefaultParameter = "Mark";

    public static MarkAssignmentPlan Create(
        IReadOnlyList<ElementInfo> elements,
        LoadedMarkNumberingRule rule,
        string category,
        IReadOnlyList<string> sort,
        string planPath)
    {
        ValidateRule(rule.Rule);
        var normalizedCategory = NormalizeCategory(category);
        var normalizedSort = sort.Count == 0 ? new[] { "level", "zone", "type", "location" } : sort.ToArray();
        var parameter = string.IsNullOrWhiteSpace(rule.Rule.Parameter)
            ? DefaultParameter
            : rule.Rule.Parameter.Trim();
        var selected = elements
            .OrderBy(element => SortKey(element, rule.Rule, normalizedSort), StringComparer.OrdinalIgnoreCase)
            .ThenBy(element => element.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(element => element.Id)
            .ToList();
        var holdersByMark = elements
            .Select(element => new { Element = element, Mark = GetParameter(element, parameter) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Mark))
            .GroupBy(item => item.Mark, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Element).ToList(), StringComparer.OrdinalIgnoreCase);

        var actions = new List<MarkAssignmentPlanAction>();
        var skipped = new List<MarkAssignmentPlanSkipped>();
        var conflicts = new List<string>();
        var sequence = rule.Rule.Start;

        foreach (var element in selected)
        {
            if (!HasParameter(element, parameter))
            {
                skipped.Add(Skip(element, "", "missing-parameter", $"Element does not expose writable-looking parameter {parameter}."));
                continue;
            }

            var current = GetParameter(element, parameter);
            var target = RenderScheme(rule.Rule, element, sequence);
            sequence++;

            if (string.IsNullOrWhiteSpace(target))
            {
                skipped.Add(Skip(element, current, "empty-target", "Mark rule produced an empty target Mark."));
                continue;
            }

            if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(Skip(element, current, "unchanged", "Element already has the generated Mark."));
                continue;
            }

            if (holdersByMark.TryGetValue(target, out var holders) &&
                holders.Any(holder => holder.Id != element.Id))
            {
                conflicts.Add(
                    $"{target} is already used by element(s): {string.Join(", ", holders.Where(holder => holder.Id != element.Id).Select(holder => $"{holder.Name} [{holder.Id}]"))}");
                continue;
            }

            actions.Add(new MarkAssignmentPlanAction(
                element.Id,
                element.Name,
                normalizedCategory,
                parameter,
                current,
                target,
                SortKey(element, rule.Rule, normalizedSort)));
        }

        var duplicateTargets = actions
            .GroupBy(action => action.NewMark, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        foreach (var duplicate in duplicateTargets)
            conflicts.Add($"target Mark {duplicate} appears more than once in the plan");

        if (conflicts.Count > 0)
        {
            var preview = string.Join("; ", conflicts.Take(5));
            if (conflicts.Count > 5)
                preview += $"; and {conflicts.Count - 5} more";
            throw new InvalidOperationException($"Mark assignment would overwrite existing Marks: {preview}");
        }

        var fullPlanPath = Path.GetFullPath(planPath);
        return new MarkAssignmentPlan(
            "mark-assignment-plan.v1",
            "mark-assignment",
            "marks assign",
            DateTime.UtcNow.ToString("o"),
            Environment.UserName,
            true,
            normalizedCategory,
            Path.GetFullPath(rule.Path),
            parameter,
            normalizedSort,
            new MarkAssignmentPlanSummary(elements.Count, selected.Count, actions.Count, skipped.Count),
            actions,
            skipped,
            new SetPlanCommands
            {
                Show = $"revitcli plan show {Quote(fullPlanPath)} --output markdown",
                DryRunApply = $"revitcli plan apply {Quote(fullPlanPath)} --dry-run",
                Apply = $"revitcli plan apply {Quote(fullPlanPath)} --yes"
            });
    }

    public static MarkVerifyReport Verify(
        IReadOnlyDictionary<string, IReadOnlyList<ElementInfo>> elementsByCategory,
        IReadOnlyDictionary<string, LoadedMarkNumberingRule> rulesByCategory)
    {
        var issues = new List<MarkVerifyIssue>();
        var categories = new List<MarkVerifyCategorySummary>();

        foreach (var (category, elements) in elementsByCategory.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var normalizedCategory = NormalizeCategory(category);
            var rule = rulesByCategory.TryGetValue(normalizedCategory, out var loadedRule) ? loadedRule : null;
            var parameter = rule == null || string.IsNullOrWhiteSpace(rule.Rule.Parameter)
                ? DefaultParameter
                : rule.Rule.Parameter.Trim();
            var actionCandidates = 0;
            var missing = 0;
            var duplicateCount = 0;
            var mismatchCount = 0;

            foreach (var element in elements)
            {
                if (!HasParameter(element, parameter) || string.IsNullOrWhiteSpace(GetParameter(element, parameter)))
                {
                    missing++;
                    issues.Add(new MarkVerifyIssue(
                        "warning",
                        "missing-mark",
                        normalizedCategory,
                        element.Id,
                        element.Name,
                        "",
                        "",
                        "Element has no Mark value."));
                }
            }

            foreach (var group in elements
                         .Select(element => new { Element = element, Mark = GetParameter(element, parameter) })
                         .Where(item => !string.IsNullOrWhiteSpace(item.Mark))
                         .GroupBy(item => item.Mark, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1))
            {
                duplicateCount += group.Count();
                foreach (var item in group)
                {
                    issues.Add(new MarkVerifyIssue(
                        "error",
                        "duplicate-mark",
                        normalizedCategory,
                        item.Element.Id,
                        item.Element.Name,
                        item.Mark,
                        "",
                        $"Mark {group.Key} is used by {group.Count()} element(s)."));
                }
            }

            if (rule != null)
            {
                try
                {
                    var planned = Create(elements, rule, normalizedCategory, rule.Rule.Sort, "verify.plan.json");
                    actionCandidates = planned.Actions.Count;
                    var expectedById = planned.Actions.ToDictionary(action => action.ElementId);
                    foreach (var element in elements)
                    {
                        if (!expectedById.TryGetValue(element.Id, out var expected))
                            continue;
                        mismatchCount++;
                        issues.Add(new MarkVerifyIssue(
                            "warning",
                            "rule-mismatch",
                            normalizedCategory,
                            element.Id,
                            element.Name,
                            expected.OldMark,
                            expected.NewMark,
                            "Current Mark does not match the numbering rule."));
                    }
                }
                catch (InvalidOperationException ex)
                {
                    mismatchCount++;
                    issues.Add(new MarkVerifyIssue(
                        "error",
                        "rule-conflict",
                        normalizedCategory,
                        0,
                        "",
                        "",
                        "",
                        ex.Message));
                }
            }

            categories.Add(new MarkVerifyCategorySummary(
                normalizedCategory,
                elements.Count,
                missing,
                duplicateCount,
                mismatchCount,
                actionCandidates));
        }

        return new MarkVerifyReport(
            "mark-verify-report.v1",
            DateTime.UtcNow.ToString("o"),
            categories,
            issues,
            issues.Count(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)),
            issues.Count(issue => issue.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase)));
    }

    public static void ValidateRule(MarkNumberingRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Scheme))
            throw new InvalidOperationException("Mark assignment requires scheme.");
        if (rule.Start < 0)
            throw new InvalidOperationException("Mark assignment start must be zero or greater.");
    }

    internal static List<ImportGroup> CreateGroups(MarkAssignmentPlan plan)
    {
        return plan.Actions
            .Where(action => action.ElementId > 0)
            .GroupBy(
                action => new { action.Parameter, Value = action.NewMark },
                (key, actions) => new ImportGroup
                {
                    Param = key.Parameter,
                    Value = key.Value,
                    ElementIds = actions.Select(action => action.ElementId).Distinct().OrderBy(id => id).ToList()
                })
            .OrderBy(group => group.Param, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static string NormalizeCategory(string category)
    {
        var normalized = (category ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "door" => "doors",
            "window" => "windows",
            "doors" or "windows" => normalized,
            _ => throw new InvalidOperationException("Marks category must be doors or windows.")
        };
    }

    internal static string GetParameter(ElementInfo element, string parameter)
    {
        var key = element.Parameters.Keys.FirstOrDefault(candidate =>
            string.Equals(candidate, parameter, StringComparison.OrdinalIgnoreCase));
        return key == null ? "" : element.Parameters[key] ?? "";
    }

    private static MarkAssignmentPlanSkipped Skip(ElementInfo element, string oldMark, string reason, string message) =>
        new(element.Id, element.Name, element.Category, oldMark, reason, message);

    private static bool HasParameter(ElementInfo element, string parameter) =>
        element.Parameters.Keys.Any(candidate => string.Equals(candidate, parameter, StringComparison.OrdinalIgnoreCase));

    private static string SortKey(ElementInfo element, MarkNumberingRule rule, IReadOnlyList<string> sort) =>
        string.Join("|", sort.Select(token => ResolveToken(element, rule, token)));

    private static string RenderScheme(MarkNumberingRule rule, ElementInfo element, int sequence)
    {
        return Regex.Replace(rule.Scheme, @"\{([^{}]+)\}", match =>
        {
            var token = match.Groups[1].Value.Trim();
            if (token.StartsWith("seq", StringComparison.OrdinalIgnoreCase))
                return FormatSequence(sequence, token);
            return ResolveToken(element, rule, token);
        });
    }

    private static string FormatSequence(int sequence, string token)
    {
        var parts = token.Split(':', 2);
        if (parts.Length == 2 && int.TryParse(parts[1], out var width) && width > 0)
            return sequence.ToString("D" + width);
        return sequence.ToString();
    }

    private static string ResolveToken(ElementInfo element, MarkNumberingRule rule, string token)
    {
        if (token.Equals("name", StringComparison.OrdinalIgnoreCase))
            return element.Name;
        if (token.Equals("type", StringComparison.OrdinalIgnoreCase))
            return element.TypeName;
        if (token.Equals("id", StringComparison.OrdinalIgnoreCase))
            return element.Id.ToString();

        var parameter = rule.Tokens.TryGetValue(token, out var mapped)
            ? mapped
            : token switch
            {
                var value when value.Equals("level", StringComparison.OrdinalIgnoreCase) => "Level",
                var value when value.Equals("zone", StringComparison.OrdinalIgnoreCase) => "Zone",
                var value when value.Equals("location", StringComparison.OrdinalIgnoreCase) => "Location",
                var value when value.Equals("fromRoom", StringComparison.OrdinalIgnoreCase) => "From Room",
                var value when value.Equals("toRoom", StringComparison.OrdinalIgnoreCase) => "To Room",
                _ => token
            };
        return GetParameter(element, parameter);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}

internal static class MarkNumberingRuleStore
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static LoadedMarkNumberingRule Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Mark numbering rule not found: {fullPath}");
        var rule = Deserializer.Deserialize<MarkNumberingRule>(File.ReadAllText(fullPath))
            ?? throw new InvalidOperationException($"Failed to parse mark numbering rule: {fullPath}");
        MarkNumberingPlanner.ValidateRule(rule);
        return new LoadedMarkNumberingRule(fullPath, rule);
    }

    public static IReadOnlyDictionary<string, LoadedMarkNumberingRule> LoadMany(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return new Dictionary<string, LoadedMarkNumberingRule>(StringComparer.OrdinalIgnoreCase);

        var files = ExpandFiles(pattern);
        var result = new Dictionary<string, LoadedMarkNumberingRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var loaded = Load(file);
            if (!string.IsNullOrWhiteSpace(loaded.Rule.Category))
                result[MarkNumberingPlanner.NormalizeCategory(loaded.Rule.Category)] = loaded;
            else
            {
                var stem = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                if (stem.Contains("door", StringComparison.OrdinalIgnoreCase))
                    result["doors"] = loaded;
                else if (stem.Contains("window", StringComparison.OrdinalIgnoreCase))
                    result["windows"] = loaded;
            }
        }

        return result;
    }

    private static IReadOnlyList<string> ExpandFiles(string pattern)
    {
        var full = Path.GetFullPath(pattern);
        if (!pattern.Contains('*', StringComparison.Ordinal) && !pattern.Contains('?', StringComparison.Ordinal))
            return new[] { full };
        var directory = Path.GetDirectoryName(full);
        var filePattern = Path.GetFileName(full);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return Array.Empty<string>();
        return Directory.GetFiles(directory, filePattern).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

internal static class MarkAssignmentPlanStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Save(string path, MarkAssignmentPlan plan)
    {
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(plan, JsonOptions));
    }

    public static MarkAssignmentPlan Load(string path)
    {
        var plan = JsonSerializer.Deserialize<MarkAssignmentPlan>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("Mark assignment plan is empty or invalid JSON.");
        if (!string.Equals(plan.SchemaVersion, "mark-assignment-plan.v1", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported mark assignment schemaVersion '{plan.SchemaVersion}'.");
        if (!string.Equals(plan.Type, "mark-assignment", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported mark assignment plan type '{plan.Type}'.");
        return plan;
    }
}

internal sealed record LoadedMarkNumberingRule(string Path, MarkNumberingRule Rule);

public sealed class MarkNumberingRule
{
    public int SchemaVersion { get; set; } = 1;
    public string Category { get; set; } = "";
    public string Parameter { get; set; } = MarkNumberingPlanner.DefaultParameter;
    public string Scheme { get; set; } = "";
    public int Start { get; set; } = 1;
    public List<string> Sort { get; set; } = new();
    public Dictionary<string, string> Tokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record MarkAssignmentPlan(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
    [property: JsonPropertyName("createdBy")] string CreatedBy,
    [property: JsonPropertyName("dryRun")] bool DryRun,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("rulePath")] string RulePath,
    [property: JsonPropertyName("parameter")] string Parameter,
    [property: JsonPropertyName("sort")] IReadOnlyList<string> Sort,
    [property: JsonPropertyName("summary")] MarkAssignmentPlanSummary Summary,
    [property: JsonPropertyName("actions")] IReadOnlyList<MarkAssignmentPlanAction> Actions,
    [property: JsonPropertyName("skipped")] IReadOnlyList<MarkAssignmentPlanSkipped> Skipped,
    [property: JsonPropertyName("commands")] SetPlanCommands Commands);

public sealed record MarkAssignmentPlanSummary(
    [property: JsonPropertyName("elementCount")] int ElementCount,
    [property: JsonPropertyName("selectedCount")] int SelectedCount,
    [property: JsonPropertyName("actionCount")] int ActionCount,
    [property: JsonPropertyName("skippedCount")] int SkippedCount);

public sealed record MarkAssignmentPlanAction(
    [property: JsonPropertyName("elementId")] long ElementId,
    [property: JsonPropertyName("elementName")] string ElementName,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("parameter")] string Parameter,
    [property: JsonPropertyName("oldMark")] string OldMark,
    [property: JsonPropertyName("newMark")] string NewMark,
    [property: JsonPropertyName("sortKey")] string SortKey);

public sealed record MarkAssignmentPlanSkipped(
    [property: JsonPropertyName("elementId")] long ElementId,
    [property: JsonPropertyName("elementName")] string ElementName,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("oldMark")] string OldMark,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("message")] string Message);

public sealed record MarkVerifyReport(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("generatedAtUtc")] string GeneratedAtUtc,
    [property: JsonPropertyName("categories")] IReadOnlyList<MarkVerifyCategorySummary> Categories,
    [property: JsonPropertyName("issues")] IReadOnlyList<MarkVerifyIssue> Issues,
    [property: JsonPropertyName("errorCount")] int ErrorCount,
    [property: JsonPropertyName("warningCount")] int WarningCount);

public sealed record MarkVerifyCategorySummary(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("elementCount")] int ElementCount,
    [property: JsonPropertyName("missingMarkCount")] int MissingMarkCount,
    [property: JsonPropertyName("duplicateMarkCount")] int DuplicateMarkCount,
    [property: JsonPropertyName("ruleMismatchCount")] int RuleMismatchCount,
    [property: JsonPropertyName("plannedActionCount")] int PlannedActionCount);

public sealed record MarkVerifyIssue(
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("elementId")] long ElementId,
    [property: JsonPropertyName("elementName")] string ElementName,
    [property: JsonPropertyName("currentMark")] string CurrentMark,
    [property: JsonPropertyName("expectedMark")] string ExpectedMark,
    [property: JsonPropertyName("message")] string Message);
