using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RevitCli.Shared;

namespace RevitCli.Sheets;

internal static partial class SheetVerifier
{
    public static readonly string[] AvailableRules =
    {
        "numbering.scheme",
        "numbering.gap",
        "numbering.duplicate",
        "numbering.outOfRange",
        "required.missing",
        "required.viewMissing",
        "linkage.overloaded",
        "linkage.emptySheet",
    };

    public static SheetVerifyReport Verify(
        ModelSnapshot snapshot,
        LoadedSheetIndex? loadedIndex,
        string? singleRule)
    {
        var enabledRules = ResolveEnabledRules(loadedIndex?.Index, singleRule);
        var issues = new List<SheetVerifyIssue>();
        var disabledRules = AvailableRules.Except(enabledRules, StringComparer.OrdinalIgnoreCase).ToList();

        var context = new SheetVerifyContext(snapshot.Sheets, loadedIndex?.Index, enabledRules);
        if (enabledRules.Contains("numbering.duplicate", StringComparer.OrdinalIgnoreCase))
            issues.AddRange(CheckDuplicateNumbers(context));

        if (context.Scheme is not null)
        {
            if (enabledRules.Contains("numbering.scheme", StringComparer.OrdinalIgnoreCase))
                issues.AddRange(CheckNumberingScheme(context));
            if (enabledRules.Contains("numbering.outOfRange", StringComparer.OrdinalIgnoreCase))
                issues.AddRange(CheckNumberingOutOfRange(context));
            if (enabledRules.Contains("numbering.gap", StringComparer.OrdinalIgnoreCase))
                issues.AddRange(CheckNumberingGaps(context));
        }

        if (enabledRules.Contains("required.missing", StringComparer.OrdinalIgnoreCase))
            issues.AddRange(CheckRequiredSheets(context));
        if (enabledRules.Contains("required.viewMissing", StringComparer.OrdinalIgnoreCase))
            issues.AddRange(CheckRequiredViews(context));
        if (enabledRules.Contains("linkage.overloaded", StringComparer.OrdinalIgnoreCase))
            issues.AddRange(CheckOverloadedSheets(context));
        if (enabledRules.Contains("linkage.emptySheet", StringComparer.OrdinalIgnoreCase))
            issues.AddRange(CheckEmptySheets(context));

        var severityOverrides = loadedIndex?.Index.Severities ?? new Dictionary<string, string>();
        var normalized = issues
            .Select(issue => ApplySeverityOverride(issue, severityOverrides))
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.Rule, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.Message, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var exitCode = normalized.Any(issue => issue.Severity == SheetIssueSeverity.Error)
            ? 3
            : normalized.Any(issue => issue.Severity == SheetIssueSeverity.Warning)
                ? 2
                : 0;

        return new SheetVerifyReport
        {
            Command = "sheets verify",
            SchemaVersion = 1,
            ConfigSource = loadedIndex?.Path ?? "(builtin defaults)",
            Summary = new SheetVerifySummary
            {
                TotalSheets = snapshot.Sheets.Count,
                TotalPlacedViews = snapshot.Sheets.Sum(sheet => sheet.PlacedViewIds.Count),
                IssuesByRule = normalized
                    .GroupBy(issue => issue.Rule, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
                DisabledRules = disabledRules.ToArray(),
                DegradedRules = Array.Empty<string>(),
                ExitCode = exitCode,
            },
            Issues = normalized,
        };
    }

    public static void ValidateRuleName(string rule)
    {
        if (!AvailableRules.Contains(rule, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unknown sheet rule '{rule}'. Available: {string.Join(", ", AvailableRules)}");
        }
    }

    public static void ValidateIndex(SheetIndex index)
    {
        foreach (var rule in index.Severities.Keys)
        {
            ValidateRuleName(rule);
            if (!TryParseSeverity(index.Severities[rule], out _))
            {
                throw new InvalidOperationException(
                    $"Invalid severity '{index.Severities[rule]}' for rule '{rule}'. Use error, warning, or info.");
            }
        }

        if (!string.IsNullOrWhiteSpace(index.Numbering.Scheme))
        {
            _ = SheetNumberScheme.Parse(index.Numbering.Scheme!);
        }
    }

    public static SheetIndex CreateIndexFromSnapshot(ModelSnapshot snapshot)
    {
        var index = new SheetIndex();
        foreach (var sheet in snapshot.Sheets
            .Where(sheet => !string.IsNullOrWhiteSpace(sheet.Number))
            .OrderBy(sheet => sheet.Number, StringComparer.OrdinalIgnoreCase))
        {
            index.Required.Add(new RequiredSheetDeclaration
            {
                Pattern = sheet.Number,
                Description = sheet.Name,
                NeedsViews =
                {
                    new RequiredViewDeclaration
                    {
                        MinCount = sheet.PlacedViewIds.Count > 0 ? 1 : 0,
                    }
                }
            });
        }

        var maxPlacedViews = snapshot.Sheets.Count == 0
            ? 0
            : snapshot.Sheets.Max(sheet => sheet.PlacedViewIds.Count);
        index.Linkage.OverloadThreshold = Math.Max(6, maxPlacedViews);
        return index;
    }

    private static HashSet<string> ResolveEnabledRules(SheetIndex? index, string? singleRule)
    {
        if (!string.IsNullOrWhiteSpace(singleRule))
        {
            ValidateRuleName(singleRule);
            return new HashSet<string>(new[] { singleRule.Trim() }, StringComparer.OrdinalIgnoreCase);
        }

        if (index is null)
        {
            return new HashSet<string>(new[] { "numbering.duplicate" }, StringComparer.OrdinalIgnoreCase);
        }

        ValidateIndex(index);
        var rules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "numbering.duplicate",
            "required.missing",
            "required.viewMissing",
        };

        if (!string.IsNullOrWhiteSpace(index.Numbering.Scheme))
        {
            rules.Add("numbering.scheme");
            rules.Add("numbering.gap");
            rules.Add("numbering.outOfRange");
        }

        if (index.Linkage.OverloadThreshold is > 0)
            rules.Add("linkage.overloaded");

        return rules;
    }

    private static IEnumerable<SheetVerifyIssue> CheckDuplicateNumbers(SheetVerifyContext context)
    {
        return context.Sheets
            .Where(sheet => !string.IsNullOrWhiteSpace(sheet.Number))
            .GroupBy(sheet => sheet.Number, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => Issue(
                "numbering.duplicate",
                SheetIssueSeverity.Error,
                $"Sheet number {group.Key} appears {group.Count()} times.",
                group.Select(sheet => sheet.ViewId).ToArray(),
                Array.Empty<long>(),
                new Dictionary<string, object?> { ["number"] = group.Key, ["count"] = group.Count() }));
    }

    private static IEnumerable<SheetVerifyIssue> CheckNumberingScheme(SheetVerifyContext context)
    {
        foreach (var sheet in context.Sheets.Where(sheet => !string.IsNullOrWhiteSpace(sheet.Number)))
        {
            if (!context.Scheme!.TryParse(sheet.Number, out _, out _))
            {
                yield return Issue(
                    "numbering.scheme",
                    SheetIssueSeverity.Error,
                    $"Sheet number {sheet.Number} does not match scheme {context.Scheme.Raw}.",
                    new[] { sheet.ViewId },
                    Array.Empty<long>(),
                    new Dictionary<string, object?> { ["number"] = sheet.Number, ["scheme"] = context.Scheme.Raw });
            }
        }
    }

    private static IEnumerable<SheetVerifyIssue> CheckNumberingOutOfRange(SheetVerifyContext context)
    {
        var ranges = context.Index?.Numbering.Ranges ?? new List<SheetNumberRange>();
        if (ranges.Count == 0)
            yield break;

        foreach (var sheet in context.Sheets.Where(sheet => !string.IsNullOrWhiteSpace(sheet.Number)))
        {
            if (!context.Scheme!.TryParse(sheet.Number, out var floor, out var seq))
                continue;

            var matching = ranges.FirstOrDefault(range => range.Floors.Contains(floor));
            if (matching is null || seq < matching.SeqMin || seq > matching.SeqMax)
            {
                yield return Issue(
                    "numbering.outOfRange",
                    SheetIssueSeverity.Warning,
                    $"Sheet number {sheet.Number} is outside declared numbering ranges.",
                    new[] { sheet.ViewId },
                    Array.Empty<long>(),
                    new Dictionary<string, object?> { ["number"] = sheet.Number, ["floor"] = floor, ["seq"] = seq });
            }
        }
    }

    private static IEnumerable<SheetVerifyIssue> CheckNumberingGaps(SheetVerifyContext context)
    {
        var ranges = context.Index?.Numbering.Ranges ?? new List<SheetNumberRange>();
        if (ranges.Count == 0)
            yield break;

        var existing = context.Sheets
            .Select(sheet => sheet.Number)
            .Where(number => !string.IsNullOrWhiteSpace(number))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var range in ranges)
        {
            foreach (var floor in range.Floors)
            {
                for (var seq = range.SeqMin; seq <= range.SeqMax; seq++)
                {
                    var expected = context.Scheme!.Generate(floor, seq);
                    if (!existing.Contains(expected))
                    {
                        yield return Issue(
                            "numbering.gap",
                            SheetIssueSeverity.Error,
                            $"{expected} is missing from the declared sheet range.",
                            Array.Empty<long>(),
                            Array.Empty<long>(),
                            new Dictionary<string, object?> { ["expectedNumber"] = expected, ["floor"] = floor, ["seq"] = seq });
                    }
                }
            }
        }
    }

    private static IEnumerable<SheetVerifyIssue> CheckRequiredSheets(SheetVerifyContext context)
    {
        foreach (var required in context.Index?.Required ?? new List<RequiredSheetDeclaration>())
        {
            if (string.IsNullOrWhiteSpace(required.Pattern))
                continue;

            if (!context.Sheets.Any(sheet => MatchesSheet(sheet, required.Pattern)))
            {
                yield return Issue(
                    "required.missing",
                    SheetIssueSeverity.Error,
                    $"Required sheet pattern {required.Pattern} has no match.",
                    Array.Empty<long>(),
                    Array.Empty<long>(),
                    new Dictionary<string, object?> { ["pattern"] = required.Pattern, ["description"] = required.Description });
            }
        }
    }

    private static IEnumerable<SheetVerifyIssue> CheckRequiredViews(SheetVerifyContext context)
    {
        foreach (var required in context.Index?.Required ?? new List<RequiredSheetDeclaration>())
        {
            if (string.IsNullOrWhiteSpace(required.Pattern) || required.NeedsViews.Count == 0)
                continue;

            foreach (var sheet in context.Sheets.Where(sheet => MatchesSheet(sheet, required.Pattern)))
            {
                var minCount = required.NeedsViews.Sum(view => Math.Max(0, view.MinCount));
                if (sheet.PlacedViewIds.Count < minCount)
                {
                    yield return Issue(
                        "required.viewMissing",
                        SheetIssueSeverity.Error,
                        $"Sheet {sheet.Number} requires at least {minCount} placed view(s), found {sheet.PlacedViewIds.Count}.",
                        new[] { sheet.ViewId },
                        sheet.PlacedViewIds.ToArray(),
                        new Dictionary<string, object?>
                        {
                            ["pattern"] = required.Pattern,
                            ["requiredViewCount"] = minCount,
                            ["actualViewCount"] = sheet.PlacedViewIds.Count,
                        });
                }
            }
        }
    }

    private static IEnumerable<SheetVerifyIssue> CheckOverloadedSheets(SheetVerifyContext context)
    {
        var threshold = context.Index?.Linkage.OverloadThreshold;
        if (threshold is null or <= 0)
            yield break;

        foreach (var sheet in context.Sheets.Where(sheet => sheet.PlacedViewIds.Count > threshold.Value))
        {
            yield return Issue(
                "linkage.overloaded",
                SheetIssueSeverity.Warning,
                $"Sheet {sheet.Number} has {sheet.PlacedViewIds.Count} placed views, above threshold {threshold}.",
                new[] { sheet.ViewId },
                sheet.PlacedViewIds.ToArray(),
                new Dictionary<string, object?> { ["threshold"] = threshold, ["actualViewCount"] = sheet.PlacedViewIds.Count });
        }
    }

    private static IEnumerable<SheetVerifyIssue> CheckEmptySheets(SheetVerifyContext context)
    {
        foreach (var sheet in context.Sheets.Where(sheet => sheet.PlacedViewIds.Count == 0))
        {
            yield return Issue(
                "linkage.emptySheet",
                SheetIssueSeverity.Info,
                $"Sheet {sheet.Number} has no placed views.",
                new[] { sheet.ViewId },
                Array.Empty<long>(),
                new Dictionary<string, object?> { ["number"] = sheet.Number });
        }
    }

    private static SheetVerifyIssue ApplySeverityOverride(
        SheetVerifyIssue issue,
        Dictionary<string, string> overrides)
    {
        if (!overrides.TryGetValue(issue.Rule, out var overrideText)
            || !TryParseSeverity(overrideText, out var severity))
        {
            return issue;
        }

        return issue with { Severity = severity };
    }

    private static bool TryParseSeverity(string? value, out SheetIssueSeverity severity)
    {
        return Enum.TryParse(value, ignoreCase: true, out severity);
    }

    private static SheetVerifyIssue Issue(
        string rule,
        SheetIssueSeverity severity,
        string message,
        long[] sheetIds,
        long[] viewIds,
        Dictionary<string, object?> details)
    {
        return new SheetVerifyIssue
        {
            Rule = rule,
            Severity = severity,
            Message = message,
            AffectedSheetIds = sheetIds,
            AffectedViewIds = viewIds,
            Details = details,
        };
    }

    private static bool MatchesSheet(SnapshotSheet sheet, string pattern)
    {
        return MatchesPattern(sheet.Number, pattern)
               || MatchesPattern(sheet.Name, pattern);
    }

    private static bool MatchesPattern(string text, string pattern)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(pattern))
            return false;
        if (!pattern.Contains('*', StringComparison.Ordinal))
            return string.Equals(text, pattern, StringComparison.OrdinalIgnoreCase);

        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
    }

    private sealed class SheetVerifyContext
    {
        public SheetVerifyContext(
            IReadOnlyList<SnapshotSheet> sheets,
            SheetIndex? index,
            HashSet<string> enabledRules)
        {
            Sheets = sheets;
            Index = index;
            EnabledRules = enabledRules;
            Scheme = string.IsNullOrWhiteSpace(index?.Numbering.Scheme)
                ? null
                : SheetNumberScheme.Parse(index.Numbering.Scheme!);
        }

        public IReadOnlyList<SnapshotSheet> Sheets { get; }
        public SheetIndex? Index { get; }
        public HashSet<string> EnabledRules { get; }
        public SheetNumberScheme? Scheme { get; }
    }

    private sealed partial class SheetNumberScheme
    {
        private readonly string _prefix;
        private readonly string _middle;
        private readonly string _suffix;
        private readonly int _floorWidth;
        private readonly int _seqWidth;
        private readonly Regex _regex;

        private SheetNumberScheme(string raw, string prefix, string middle, string suffix, int floorWidth, int seqWidth)
        {
            Raw = raw;
            _prefix = prefix;
            _middle = middle;
            _suffix = suffix;
            _floorWidth = floorWidth;
            _seqWidth = seqWidth;
            _regex = new Regex(
                "^" + Regex.Escape(prefix) + @"(?<floor>\d{" + floorWidth + @"})"
                + Regex.Escape(middle) + @"(?<seq>\d{" + seqWidth + @"})"
                + Regex.Escape(suffix) + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public string Raw { get; }

        public static SheetNumberScheme Parse(string scheme)
        {
            var match = SchemeRegex().Match(scheme);
            if (!match.Success)
            {
                throw new InvalidOperationException(
                    $"Unsupported sheet numbering scheme '{scheme}'. Use token form like A-{{floor:01}}{{seq:02}}.");
            }

            return new SheetNumberScheme(
                scheme,
                match.Groups["prefix"].Value,
                match.Groups["middle"].Value,
                match.Groups["suffix"].Value,
                match.Groups["floorFormat"].Value.Length,
                match.Groups["seqFormat"].Value.Length);
        }

        public bool TryParse(string number, out int floor, out int seq)
        {
            floor = 0;
            seq = 0;
            var match = _regex.Match(number);
            return match.Success
                   && int.TryParse(match.Groups["floor"].Value, out floor)
                   && int.TryParse(match.Groups["seq"].Value, out seq);
        }

        public string Generate(int floor, int seq)
        {
            return _prefix
                   + floor.ToString("D" + _floorWidth)
                   + _middle
                   + seq.ToString("D" + _seqWidth)
                   + _suffix;
        }

        [GeneratedRegex(@"^(?<prefix>.*)\{floor:(?<floorFormat>\d+)\}(?<middle>.*)\{seq:(?<seqFormat>\d+)\}(?<suffix>.*)$", RegexOptions.CultureInvariant)]
        private static partial Regex SchemeRegex();
    }
}

internal sealed class SheetVerifyReport
{
    public string Command { get; set; } = "sheets verify";
    public int SchemaVersion { get; set; } = 1;
    public string ConfigSource { get; set; } = "(builtin defaults)";
    public SheetVerifySummary Summary { get; set; } = new();
    public SheetVerifyIssue[] Issues { get; set; } = Array.Empty<SheetVerifyIssue>();
}

internal sealed class SheetVerifySummary
{
    public int TotalSheets { get; set; }
    public int TotalPlacedViews { get; set; }
    public Dictionary<string, int> IssuesByRule { get; set; } = new();
    public string[] DisabledRules { get; set; } = Array.Empty<string>();
    public string[] DegradedRules { get; set; } = Array.Empty<string>();
    public int ExitCode { get; set; }
}

internal sealed record SheetVerifyIssue
{
    public string Rule { get; init; } = "";
    public SheetIssueSeverity Severity { get; init; }
    public string Message { get; init; } = "";
    public long[] AffectedSheetIds { get; init; } = Array.Empty<long>();
    public long[] AffectedViewIds { get; init; } = Array.Empty<long>();
    public Dictionary<string, object?> Details { get; init; } = new();
}

internal enum SheetIssueSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}
