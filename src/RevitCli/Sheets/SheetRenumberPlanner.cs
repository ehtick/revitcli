using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using RevitCli.Shared;

namespace RevitCli.Sheets;

internal static class SheetRenumberPlanner
{
    public const string SheetNumberParameter = "Sheet Number";

    public static SheetRenumberPlan Create(
        ModelSnapshot snapshot,
        string selector,
        LoadedSheetIndex rule,
        string planPath)
    {
        ValidateRule(rule.Index);

        var normalizedSelector = string.IsNullOrWhiteSpace(selector) ? "all" : selector.Trim();
        var scheme = SheetNumberScheme.Parse(rule.Index.Numbering.Scheme!);
        var generated = GenerateNumbers(rule.Index.Numbering.Ranges, scheme);
        var targetSheets = snapshot.Sheets
            .Where(sheet => MatchesSelector(sheet, normalizedSelector))
            .OrderBy(sheet => sheet.Number, StringComparer.OrdinalIgnoreCase)
            .ThenBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(sheet => sheet.ViewId)
            .ToList();
        var selectedIds = targetSheets.Select(sheet => sheet.ViewId).ToHashSet();
        var selectedNumbers = targetSheets
            .Where(sheet => !string.IsNullOrWhiteSpace(sheet.Number))
            .GroupBy(sheet => sheet.Number, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var unselectedNumbers = snapshot.Sheets
            .Where(sheet => !selectedIds.Contains(sheet.ViewId) && !string.IsNullOrWhiteSpace(sheet.Number))
            .GroupBy(sheet => sheet.Number, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var actions = new List<SheetRenumberPlanAction>();
        var skipped = new List<SheetRenumberPlanSkipped>();
        var conflicts = new List<string>();

        for (var index = 0; index < targetSheets.Count; index++)
        {
            var sheet = targetSheets[index];
            if (index >= generated.Count)
            {
                skipped.Add(new SheetRenumberPlanSkipped(
                    sheet.ViewId,
                    sheet.Number,
                    sheet.Name,
                    "no-target-number",
                    "No generated sheet number remains for this selected sheet."));
                continue;
            }

            var expected = generated[index];
            if (selectedNumbers.TryGetValue(expected.Number, out var selectedHolders) &&
                selectedHolders.Any(holder => holder.ViewId != sheet.ViewId))
            {
                conflicts.Add(
                    $"{expected.Number} is still used by selected sheet(s): {string.Join(", ", selectedHolders.Where(holder => holder.ViewId != sheet.ViewId).Select(holder => $"{holder.Number} {holder.Name} [{holder.ViewId}]"))}");
                continue;
            }

            if (unselectedNumbers.TryGetValue(expected.Number, out var holders))
            {
                conflicts.Add(
                    $"{expected.Number} is already used by unselected sheet(s): {string.Join(", ", holders.Select(holder => $"{holder.Number} {holder.Name} [{holder.ViewId}]"))}");
                continue;
            }

            if (string.Equals(sheet.Number, expected.Number, StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(new SheetRenumberPlanSkipped(
                    sheet.ViewId,
                    sheet.Number,
                    sheet.Name,
                    "unchanged",
                    "Sheet already has the generated number."));
                continue;
            }

            actions.Add(new SheetRenumberPlanAction(
                sheet.ViewId,
                sheet.Number,
                sheet.Name,
                SheetNumberParameter,
                sheet.Number,
                expected.Number,
                expected.Floor,
                expected.Seq));
        }

        if (conflicts.Count > 0)
        {
            var preview = string.Join("; ", conflicts.Take(5));
            if (conflicts.Count > 5)
                preview += $"; and {conflicts.Count - 5} more";
            throw new InvalidOperationException($"Sheet renumber would overwrite existing sheet numbers: {preview}");
        }

        var fullPlanPath = Path.GetFullPath(planPath);
        var fullRulePath = Path.GetFullPath(rule.Path);
        return new SheetRenumberPlan(
            "sheet-renumber-plan.v1",
            "sheet-renumber",
            "sheets renumber",
            DateTime.UtcNow.ToString("o"),
            Environment.UserName,
            true,
            normalizedSelector,
            fullRulePath,
            SheetNumberParameter,
            new SheetIssueModelFingerprint(
                snapshot.Revit.Document,
                snapshot.Revit.DocumentPath,
                snapshot.Model.FileHash),
            new SheetRenumberPlanSummary(
                snapshot.Sheets.Count,
                targetSheets.Count,
                generated.Count,
                actions.Count,
                skipped.Count),
            actions,
            skipped,
            new SheetRenumberPlanCommands(
                $"revitcli plan show {Quote(fullPlanPath)} --output markdown",
                $"revitcli sheets renumber --rule {Quote(fullRulePath)} --selector {Quote(normalizedSelector)} --plan-output {Quote(fullPlanPath)} --dry-run --output markdown",
                $"revitcli plan apply {Quote(fullPlanPath)} --dry-run",
                $"revitcli plan apply {Quote(fullPlanPath)} --yes"));
    }

    public static void ValidateRule(SheetIndex index)
    {
        SheetVerifier.ValidateIndex(index);
        if (string.IsNullOrWhiteSpace(index.Numbering.Scheme))
            throw new InvalidOperationException("Sheet renumber requires numbering.scheme.");
        if (index.Numbering.Ranges.Count == 0)
            throw new InvalidOperationException("Sheet renumber requires at least one numbering range.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scheme = SheetNumberScheme.Parse(index.Numbering.Scheme!);
        foreach (var number in GenerateNumbers(index.Numbering.Ranges, scheme))
        {
            if (!seen.Add(number.Number))
                throw new InvalidOperationException($"Sheet renumber rule generates duplicate number '{number.Number}'.");
        }
    }

    private static List<GeneratedSheetNumber> GenerateNumbers(
        IReadOnlyList<SheetNumberRange> ranges,
        SheetNumberScheme scheme)
    {
        var generated = new List<GeneratedSheetNumber>();
        foreach (var range in ranges)
        {
            if (range.Floors.Count == 0)
                throw new InvalidOperationException("Sheet renumber ranges must declare at least one floor.");
            if (range.SeqMin > range.SeqMax)
                throw new InvalidOperationException($"Sheet renumber range seqMin {range.SeqMin} exceeds seqMax {range.SeqMax}.");

            foreach (var floor in range.Floors)
            {
                for (var seq = range.SeqMin; seq <= range.SeqMax; seq++)
                    generated.Add(new GeneratedSheetNumber(floor, seq, scheme.Generate(floor, seq)));
            }
        }

        return generated;
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

    private sealed record GeneratedSheetNumber(int Floor, int Seq, string Number);
}

internal static class SheetRenumberPlanStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Save(string path, SheetRenumberPlan plan)
    {
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, JsonSerializer.Serialize(plan, JsonOptions));
    }

    public static SheetRenumberPlan Load(string path)
    {
        var plan = JsonSerializer.Deserialize<SheetRenumberPlan>(
            File.ReadAllText(path),
            JsonOptions) ?? throw new InvalidOperationException("Sheet renumber plan file is empty or invalid JSON.");

        if (!string.Equals(plan.SchemaVersion, "sheet-renumber-plan.v1", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported sheet renumber plan schemaVersion '{plan.SchemaVersion}'.");
        if (!string.Equals(plan.Type, "sheet-renumber", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported sheet renumber plan type '{plan.Type}'.");
        if (plan.Summary is null)
            throw new InvalidOperationException("Sheet renumber plan is missing summary.");
        if (plan.Actions is null)
            throw new InvalidOperationException("Sheet renumber plan is missing actions.");
        if (plan.Skipped is null)
            throw new InvalidOperationException("Sheet renumber plan is missing skipped entries.");
        if (plan.Commands is null)
            throw new InvalidOperationException("Sheet renumber plan is missing commands.");
        if (plan.ModelFingerprint is null)
            throw new InvalidOperationException("Sheet renumber plan is missing model fingerprint.");
        if (string.IsNullOrWhiteSpace(plan.Commands.Show))
            throw new InvalidOperationException("Sheet renumber plan is missing show command.");
        if (string.IsNullOrWhiteSpace(plan.Commands.RegenerateDryRun))
            throw new InvalidOperationException("Sheet renumber plan is missing regenerate dry-run command.");

        return plan;
    }
}

internal sealed record SheetRenumberPlan(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
    [property: JsonPropertyName("createdBy")] string CreatedBy,
    [property: JsonPropertyName("dryRun")] bool DryRun,
    [property: JsonPropertyName("selector")] string Selector,
    [property: JsonPropertyName("rulePath")] string RulePath,
    [property: JsonPropertyName("parameter")] string Parameter,
    [property: JsonPropertyName("modelFingerprint")] SheetIssueModelFingerprint ModelFingerprint,
    [property: JsonPropertyName("summary")] SheetRenumberPlanSummary Summary,
    [property: JsonPropertyName("actions")] IReadOnlyList<SheetRenumberPlanAction> Actions,
    [property: JsonPropertyName("skipped")] IReadOnlyList<SheetRenumberPlanSkipped> Skipped,
    [property: JsonPropertyName("commands")] SheetRenumberPlanCommands Commands);

internal sealed record SheetRenumberPlanSummary(
    [property: JsonPropertyName("totalSheets")] int TotalSheets,
    [property: JsonPropertyName("matchedSheets")] int MatchedSheets,
    [property: JsonPropertyName("generatedNumbers")] int GeneratedNumbers,
    [property: JsonPropertyName("actionCount")] int ActionCount,
    [property: JsonPropertyName("skippedCount")] int SkippedCount);

internal sealed record SheetRenumberPlanAction(
    [property: JsonPropertyName("sheetId")] long SheetId,
    [property: JsonPropertyName("sheetNumber")] string SheetNumber,
    [property: JsonPropertyName("sheetName")] string SheetName,
    [property: JsonPropertyName("parameter")] string Parameter,
    [property: JsonPropertyName("oldNumber")] string OldNumber,
    [property: JsonPropertyName("newNumber")] string NewNumber,
    [property: JsonPropertyName("floor")] int Floor,
    [property: JsonPropertyName("seq")] int Seq);

internal sealed record SheetRenumberPlanSkipped(
    [property: JsonPropertyName("sheetId")] long SheetId,
    [property: JsonPropertyName("sheetNumber")] string SheetNumber,
    [property: JsonPropertyName("sheetName")] string SheetName,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("message")] string Message);

internal sealed record SheetRenumberPlanCommands(
    [property: JsonPropertyName("show")] string Show,
    [property: JsonPropertyName("regenerateDryRun")] string RegenerateDryRun,
    [property: JsonPropertyName("dryRunApply")] string DryRunApply,
    [property: JsonPropertyName("apply")] string Apply);
