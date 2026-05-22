using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using RevitCli.Shared;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RevitCli.Sheets;

internal static class SheetIssuePlanner
{
    public static SheetIssuePlan Create(
        ModelSnapshot snapshot,
        string selector,
        string issueCode,
        string issueDate,
        LoadedSheetIssueParamMap paramMap,
        string planPath)
    {
        var normalizedSelector = string.IsNullOrWhiteSpace(selector) ? "all" : selector.Trim();
        var targets = new[]
        {
            new SheetIssueTargetParameter("issueCode", paramMap.Map.IssueCode),
            new SheetIssueTargetParameter("issueDate", paramMap.Map.IssueDate),
        };
        var actions = new List<SheetIssuePlanAction>();
        var skipped = new List<SheetIssuePlanSkipped>();

        foreach (var sheet in snapshot.Sheets
                     .Where(sheet => MatchesSelector(sheet, normalizedSelector))
                     .OrderBy(sheet => sheet.Number, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(sheet => sheet.ViewId))
        {
            foreach (var target in targets)
            {
                var newValue = target.Key.Equals("issueCode", StringComparison.OrdinalIgnoreCase)
                    ? issueCode
                    : issueDate;

                var parameterName = ResolveParameterName(sheet.Parameters, target.Candidates);
                if (string.IsNullOrWhiteSpace(parameterName))
                {
                    skipped.Add(new SheetIssuePlanSkipped(
                        sheet.ViewId,
                        sheet.Number,
                        sheet.Name,
                        target.Key,
                        "parameter-missing",
                        $"No mapped parameter found for {target.Key}."));
                    continue;
                }

                sheet.Parameters.TryGetValue(parameterName, out var oldValue);
                if (string.Equals(oldValue ?? "", newValue, StringComparison.Ordinal))
                {
                    skipped.Add(new SheetIssuePlanSkipped(
                        sheet.ViewId,
                        sheet.Number,
                        sheet.Name,
                        target.Key,
                        "unchanged",
                        $"Parameter {parameterName} already has the requested value."));
                    continue;
                }

                actions.Add(new SheetIssuePlanAction(
                    sheet.ViewId,
                    sheet.Number,
                    sheet.Name,
                    null,
                    target.Key,
                    parameterName,
                    oldValue ?? "",
                    newValue));
            }
        }

        var fullPlanPath = Path.GetFullPath(planPath);
        var paramMapArg = string.Equals(paramMap.Path, "(builtin defaults)", StringComparison.OrdinalIgnoreCase)
            ? ""
            : $" --param-map {Quote(paramMap.Path)}";
        return new SheetIssuePlan(
            "sheet-issue-plan.v1",
            "sheet-issue",
            "sheets issue-meta",
            DateTime.UtcNow.ToString("o"),
            Environment.UserName,
            true,
            normalizedSelector,
            issueCode,
            issueDate,
            paramMap.Path,
            targets.ToArray(),
            new SheetIssueModelFingerprint(
                snapshot.Revit.Document,
                snapshot.Revit.DocumentPath,
                snapshot.Model.FileHash),
            new SheetIssuePlanSummary(
                snapshot.Sheets.Count,
                actions.Select(action => action.SheetId)
                    .Concat(skipped.Select(item => item.SheetId))
                    .Distinct()
                    .Count(),
                actions.Count,
                skipped.Count),
            actions,
            skipped,
            new SheetIssuePlanCommands(
                $"revitcli plan show {Quote(fullPlanPath)} --output markdown",
                $"revitcli sheets issue-meta --selector {Quote(normalizedSelector)} --issue-code {Quote(issueCode)} --issue-date {Quote(issueDate)} --plan-output {Quote(fullPlanPath)}{paramMapArg} --dry-run --output markdown"));
    }

    private static string ResolveParameterName(
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<string> candidates)
    {
        foreach (var candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            var match = parameters.Keys.FirstOrDefault(key => string.Equals(key, candidate, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
                return match;
        }

        return "";
    }

    private static bool MatchesSelector(SnapshotSheet sheet, string selector)
    {
        if (selector.Equals("all", StringComparison.OrdinalIgnoreCase))
            return true;

        return MatchesPattern(sheet.Number, selector) ||
               MatchesPattern(sheet.Name, selector) ||
               MatchesPattern($"{sheet.Number} {sheet.Name}".Trim(), selector);
    }

    private static bool MatchesPattern(string value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!pattern.Contains('*', StringComparison.Ordinal) &&
            !pattern.Contains('?', StringComparison.Ordinal))
        {
            return value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        return Glob(value, pattern);
    }

    private static bool Glob(string value, string pattern)
    {
        var valueIndex = 0;
        var patternIndex = 0;
        var starIndex = -1;
        var matchIndex = 0;

        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '?' ||
                 char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(value[valueIndex])))
            {
                valueIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                matchIndex = valueIndex;
            }
            else if (starIndex != -1)
            {
                patternIndex = starIndex + 1;
                valueIndex = ++matchIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            patternIndex++;

        return patternIndex == pattern.Length;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}

internal static class SheetIssueParamMapStore
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static LoadedSheetIssueParamMap LoadOrDefault(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            return Load(path);

        var defaultPath = Path.GetFullPath(Path.Combine(".revitcli", "sheets", "titleblock-map.yml"));
        return File.Exists(defaultPath)
            ? Load(defaultPath)
            : new LoadedSheetIssueParamMap("(builtin defaults)", SheetIssueParamMap.Default());
    }

    private static LoadedSheetIssueParamMap Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var map = Deserializer.Deserialize<SheetIssueParamMap>(File.ReadAllText(fullPath))
            ?? throw new InvalidOperationException($"Sheet issue parameter map is empty: {fullPath}");
        map.Normalize();
        return new LoadedSheetIssueParamMap(fullPath, map);
    }
}

internal static class SheetIssuePlanStore
{
    public static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    public static void Save(string path, SheetIssuePlan plan)
    {
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, System.Text.Json.JsonSerializer.Serialize(plan, JsonOptions));
    }

    public static SheetIssuePlan Load(string path)
    {
        var plan = System.Text.Json.JsonSerializer.Deserialize<SheetIssuePlan>(
            File.ReadAllText(path),
            JsonOptions) ?? throw new InvalidOperationException("Sheet issue plan file is empty or invalid JSON.");

        if (!string.Equals(plan.SchemaVersion, "sheet-issue-plan.v1", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported sheet issue plan schemaVersion '{plan.SchemaVersion}'.");

        if (!string.Equals(plan.Type, "sheet-issue", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported sheet issue plan type '{plan.Type}'.");

        if (plan.Summary is null)
            throw new InvalidOperationException("Sheet issue plan is missing summary.");
        if (plan.Actions is null)
            throw new InvalidOperationException("Sheet issue plan is missing actions.");
        if (plan.Skipped is null)
            throw new InvalidOperationException("Sheet issue plan is missing skipped entries.");
        if (plan.Commands is null)
            throw new InvalidOperationException("Sheet issue plan is missing commands.");
        if (plan.ModelFingerprint is null)
            throw new InvalidOperationException("Sheet issue plan is missing model fingerprint.");
        if (string.IsNullOrWhiteSpace(plan.Commands.Show))
            throw new InvalidOperationException("Sheet issue plan is missing show command.");
        if (string.IsNullOrWhiteSpace(plan.Commands.RegenerateDryRun))
            throw new InvalidOperationException("Sheet issue plan is missing regenerate dry-run command.");

        return plan;
    }
}

internal sealed record LoadedSheetIssueParamMap(string Path, SheetIssueParamMap Map);

internal sealed class SheetIssueParamMap
{
    public List<string> IssueCode { get; set; } = new();

    public List<string> IssueDate { get; set; } = new();

    public static SheetIssueParamMap Default()
    {
        var map = new SheetIssueParamMap
        {
            IssueCode = { "Sheet Issue Code", "Issue Code", "Revision", "Revision Number" },
            IssueDate = { "Sheet Issue Date", "Issue Date", "Date" },
        };
        map.Normalize();
        return map;
    }

    public void Normalize()
    {
        if (IssueCode.Count == 0)
            IssueCode.AddRange(Default().IssueCode);
        if (IssueDate.Count == 0)
            IssueDate.AddRange(Default().IssueDate);
    }
}

internal sealed record SheetIssuePlan(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
    [property: JsonPropertyName("createdBy")] string CreatedBy,
    [property: JsonPropertyName("dryRun")] bool DryRun,
    [property: JsonPropertyName("selector")] string Selector,
    [property: JsonPropertyName("issueCode")] string IssueCode,
    [property: JsonPropertyName("issueDate")] string IssueDate,
    [property: JsonPropertyName("paramMap")] string ParamMap,
    [property: JsonPropertyName("targetParameters")] IReadOnlyList<SheetIssueTargetParameter> TargetParameters,
    [property: JsonPropertyName("modelFingerprint")] SheetIssueModelFingerprint ModelFingerprint,
    [property: JsonPropertyName("summary")] SheetIssuePlanSummary Summary,
    [property: JsonPropertyName("actions")] IReadOnlyList<SheetIssuePlanAction> Actions,
    [property: JsonPropertyName("skipped")] IReadOnlyList<SheetIssuePlanSkipped> Skipped,
    [property: JsonPropertyName("commands")] SheetIssuePlanCommands Commands);

internal sealed record SheetIssueTargetParameter(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("candidates")] IReadOnlyList<string> Candidates);

internal sealed record SheetIssueModelFingerprint(
    [property: JsonPropertyName("document")] string Document,
    [property: JsonPropertyName("documentPath")] string DocumentPath,
    [property: JsonPropertyName("fileHash")] string FileHash);

internal sealed record SheetIssuePlanSummary(
    [property: JsonPropertyName("totalSheets")] int TotalSheets,
    [property: JsonPropertyName("matchedSheets")] int MatchedSheets,
    [property: JsonPropertyName("actionCount")] int ActionCount,
    [property: JsonPropertyName("skippedCount")] int SkippedCount);

internal sealed record SheetIssuePlanAction(
    [property: JsonPropertyName("sheetId")] long SheetId,
    [property: JsonPropertyName("sheetNumber")] string SheetNumber,
    [property: JsonPropertyName("sheetName")] string SheetName,
    [property: JsonPropertyName("titleblockId")] long? TitleblockId,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("parameter")] string Parameter,
    [property: JsonPropertyName("oldValue")] string OldValue,
    [property: JsonPropertyName("newValue")] string NewValue);

internal sealed record SheetIssuePlanSkipped(
    [property: JsonPropertyName("sheetId")] long SheetId,
    [property: JsonPropertyName("sheetNumber")] string SheetNumber,
    [property: JsonPropertyName("sheetName")] string SheetName,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("message")] string Message);

internal sealed record SheetIssuePlanCommands(
    [property: JsonPropertyName("show")] string Show,
    [property: JsonPropertyName("regenerateDryRun")] string RegenerateDryRun);
