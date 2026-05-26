using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Output;
using RevitCli.Shared;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RevitCli.Commands;

public static class LinksCommand
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static Command Create(RevitClient client)
    {
        var command = new Command("links", "Audit and plan safe coordination link repairs");
        command.AddCommand(CreateAuditCommand(client));
        command.AddCommand(CreateRepairCommand(client));
        return command;
    }

    private static Command CreateAuditCommand(RevitClient client)
    {
        var rulesOpt = new Option<string>("--rules", "Link audit rules YAML") { IsRequired = true };
        var checkOpt = new Option<string>("--check", () => "paths,loaded,coordinates", "Comma-separated checks: paths,loaded,coordinates");
        var outputOpt = new Option<string>("--output", () => "markdown", "Output format: table|json|markdown");
        var command = new Command("audit", "Audit Revit links for paths, load status, and coordinate fingerprints")
        {
            rulesOpt,
            checkOpt,
            outputOpt
        };

        command.SetHandler(async (string rules, string check, string output) =>
        {
            Environment.ExitCode = await ExecuteAuditAsync(client, rules, check, output, Console.Out);
        }, rulesOpt, checkOpt, outputOpt);
        return command;
    }

    private static Command CreateRepairCommand(RevitClient client)
    {
        var mapOpt = new Option<string>("--map", "Link path map YAML") { IsRequired = true };
        var planOutputOpt = new Option<string>("--plan-output", "Write link-repair-plan JSON") { IsRequired = true };
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview only; no link paths or loaded states are changed");
        var maxChangesOpt = new Option<int>("--max-changes", () => 20, "Maximum link repair actions allowed in the plan");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");
        var command = new Command("repair", "Create a reviewed plan for link path/load repairs")
        {
            mapOpt,
            planOutputOpt,
            dryRunOpt,
            maxChangesOpt,
            outputOpt
        };

        command.SetHandler(async (string map, string planOutput, bool dryRun, int maxChanges, string output) =>
        {
            Environment.ExitCode = await ExecuteRepairAsync(client, map, planOutput, dryRun, maxChanges, output, Console.Out);
        }, mapOpt, planOutputOpt, dryRunOpt, maxChangesOpt, outputOpt);
        return command;
    }

    public static async Task<int> ExecuteAuditAsync(
        RevitClient client,
        string rulesPath,
        string checks,
        string outputFormat,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        LinkAuditRules rules;
        try
        {
            rules = LoadRules(rulesPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var links = await ListLinksAsync(client, output);
        if (links == null)
            return 4;

        var report = CreateAuditReport(rules, links, ParseChecks(checks), rulesPath);
        await output.WriteLineAsync(Render(report, normalizedOutput));
        return report.ErrorCount > 0 ? 2 : 0;
    }

    public static async Task<int> ExecuteRepairAsync(
        RevitClient client,
        string mapPath,
        string planOutputPath,
        bool dryRun,
        int maxChanges,
        string outputFormat,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        if (!dryRun)
        {
            await output.WriteLineAsync("Error: links repair only creates reviewed plans. Use --dry-run and review the plan before a future apply path.");
            return 1;
        }

        LinkPathMap map;
        try
        {
            map = LoadPathMap(mapPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var links = await ListLinksAsync(client, output);
        if (links == null)
            return 4;

        var plan = CreateRepairPlan(map, links, mapPath, planOutputPath, maxChanges);
        SaveJson(planOutputPath, plan);
        await output.WriteLineAsync(Render(plan, normalizedOutput));
        if (plan.Issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)))
            return 1;
        return plan.Summary.ActionCount == 0 ? 2 : 0;
    }

    private static async Task<LinkInfo[]?> ListLinksAsync(RevitClient client, TextWriter output)
    {
        var result = await client.ListLinksAsync();
        if (result.Success)
            return result.Data ?? Array.Empty<LinkInfo>();

        await output.WriteLineAsync($"Error: {result.Error}");
        return null;
    }

    private static LinkAuditRules LoadRules(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException($"Link audit rules not found: {full}");

        var rules = Yaml.Deserialize<LinkAuditRules>(File.ReadAllText(full))
            ?? throw new InvalidOperationException($"Failed to parse link audit rules: {full}");
        if (!string.Equals(rules.SchemaVersion, "link-rules.v1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(rules.SchemaVersion, "1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported link audit schemaVersion '{rules.SchemaVersion}'.");
        }

        return rules;
    }

    private static LinkPathMap LoadPathMap(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException($"Link path map not found: {full}");

        var map = Yaml.Deserialize<LinkPathMap>(File.ReadAllText(full))
            ?? throw new InvalidOperationException($"Failed to parse link path map: {full}");
        if (!string.Equals(map.SchemaVersion, "link-path-map.v1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(map.SchemaVersion, "1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported link path map schemaVersion '{map.SchemaVersion}'.");
        }

        return map;
    }

    private static LinkAuditReport CreateAuditReport(
        LinkAuditRules rules,
        IReadOnlyList<LinkInfo> links,
        IReadOnlySet<string> checks,
        string rulesPath)
    {
        var issues = new List<LinkAuditIssue>();
        foreach (var rule in rules.Links)
        {
            var matches = links
                .Where(link => MatchesSelector(link.Name, rule.Name) || MatchesSelector(link.TypeName, rule.Name))
                .ToArray();
            if (matches.Length == 0)
            {
                if (rule.Required)
                    issues.Add(new LinkAuditIssue("error", "link-missing", 0, rule.Name, $"Required link '{rule.Name}' is missing."));
                continue;
            }

            foreach (var link in matches)
            {
                if (checks.Contains("paths") && !string.IsNullOrWhiteSpace(rule.Path) &&
                    !PathEquals(link.Path, rule.Path))
                {
                    issues.Add(new LinkAuditIssue("error", "link-path-mismatch", link.Id, link.Name, $"Expected path '{rule.Path}', found '{link.Path}'."));
                }

                if (checks.Contains("paths") && !link.IsCloud && !link.PathExists)
                    issues.Add(new LinkAuditIssue("error", "link-path-missing", link.Id, link.Name, $"Link path does not exist: '{link.Path}'."));

                if (checks.Contains("loaded") && rule.MustBeLoaded && !link.IsLoaded)
                    issues.Add(new LinkAuditIssue("error", "link-not-loaded", link.Id, link.Name, "Link is required to be loaded but is not loaded."));

                if (checks.Contains("coordinates") && !string.IsNullOrWhiteSpace(rule.CoordinateFingerprint) &&
                    !string.Equals(link.TransformFingerprint, rule.CoordinateFingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new LinkAuditIssue("warning", "link-coordinate-drift", link.Id, link.Name, "Link coordinate fingerprint differs from rules."));
                }
            }
        }

        foreach (var link in links.Where(link => !rules.Links.Any(rule => MatchesSelector(link.Name, rule.Name) || MatchesSelector(link.TypeName, rule.Name))))
            issues.Add(new LinkAuditIssue("warning", "link-unmapped", link.Id, link.Name, "Link is not covered by link audit rules."));

        var entries = links
            .OrderBy(link => link.Name, StringComparer.OrdinalIgnoreCase)
            .Select(link => new LinkAuditEntry(
                link.Id,
                link.Name,
                link.TypeName,
                link.Path,
                link.IsLoaded,
                link.PathExists,
                link.LinkedFileStatus,
                link.WorksetName,
                link.TransformFingerprint))
            .ToArray();

        return new LinkAuditReport(
            "link-audit-report.v1",
            DateTime.UtcNow.ToString("o"),
            Path.GetFullPath(rulesPath),
            checks.OrderBy(check => check, StringComparer.OrdinalIgnoreCase).ToArray(),
            links.Count,
            issues.Count(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)),
            issues.Count(issue => string.Equals(issue.Severity, "warning", StringComparison.OrdinalIgnoreCase)),
            issues,
            entries);
    }

    private static LinkRepairPlan CreateRepairPlan(
        LinkPathMap map,
        IReadOnlyList<LinkInfo> links,
        string mapPath,
        string planOutputPath,
        int maxChanges)
    {
        var issues = new List<LinkRepairIssue>();
        var actions = new List<LinkRepairAction>();
        foreach (var rule in map.Links)
        {
            var matches = links
                .Where(item => MatchesSelector(item.Name, rule.Name) || MatchesSelector(item.TypeName, rule.Name))
                .ToArray();
            if (matches.Length == 0)
            {
                issues.Add(new LinkRepairIssue("error", "link-missing", 0, rule.Name, $"Mapped link '{rule.Name}' was not found."));
                continue;
            }

            var typeGroups = matches
                .GroupBy(LinkTypeKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (typeGroups.Length > 1)
            {
                issues.Add(new LinkRepairIssue("error", "link-match-ambiguous", 0, rule.Name, $"Mapped link '{rule.Name}' matched {typeGroups.Length} link types."));
                continue;
            }

            var groupLinks = typeGroups[0].OrderBy(item => item.Id).ToArray();
            var link = groupLinks[0];
            var instanceIds = groupLinks.Select(item => item.Id).Distinct().OrderBy(id => id).ToArray();
            var linkTypeId = link.LinkTypeId ?? link.Id;
            var newPath = string.IsNullOrWhiteSpace(rule.NewPath) ? link.Path : rule.NewPath!;
            var loadState = rule.Load ?? link.IsLoaded;
            var changesPath = !PathEquals(link.Path, newPath);
            var changesLoad = rule.Load.HasValue && loadState != link.IsLoaded;
            if (!changesPath && !changesLoad)
                continue;

            (bool Exists, string? LastWriteTimeUtc, long? SizeBytes) newEvidence = changesPath
                ? ProbePath(newPath)
                : (link.PathExists, link.LastWriteTimeUtc, link.SizeBytes);
            var newPathIsExternallyResolved = IsExternallyResolvedPath(newPath);
            if (!newEvidence.Exists && (changesPath || loadState) && !newPathIsExternallyResolved)
                issues.Add(new LinkRepairIssue("error", "new-path-missing", link.Id, link.Name, $"New link path does not exist: '{newPath}'."));

            actions.Add(new LinkRepairAction(
                link.Id,
                link.LinkTypeId,
                instanceIds,
                link.Name,
                link.TypeName,
                link.Path,
                newPath,
                link.IsLoaded,
                loadState,
                link.PathExists,
                newEvidence.Exists,
                link.LastWriteTimeUtc,
                newEvidence.LastWriteTimeUtc,
                link.SizeBytes,
                newEvidence.SizeBytes));
        }

        if (actions.Count > maxChanges)
        {
            issues.Add(new LinkRepairIssue(
                "error",
                "max-changes-exceeded",
                0,
                "",
                $"Plan has {actions.Count} action(s), which exceeds --max-changes {maxChanges}."));
        }

        return new LinkRepairPlan(
            "link-repair-plan.v1",
            DateTime.UtcNow.ToString("o"),
            Path.GetFullPath(mapPath),
            Path.GetFullPath(planOutputPath),
            true,
            maxChanges,
            new LinkRepairSummary(links.Count, actions.Count, issues.Count, issues.Count(issue => string.Equals(issue.Code, "new-path-missing", StringComparison.OrdinalIgnoreCase))),
            actions,
            issues,
            new[]
            {
                $"revitcli plan show \"{Path.GetFullPath(planOutputPath)}\" --output markdown",
                $"revitcli plan apply \"{Path.GetFullPath(planOutputPath)}\" --dry-run",
                $"revitcli plan apply \"{Path.GetFullPath(planOutputPath)}\" --yes --max-changes {maxChanges.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                "Review old/new paths, load states, and file timestamp or size evidence before repairing links in Revit."
            });
    }

    internal static LinkRepairPlanJsonEvidence VerifyLinkRepairPlanJsonIsPathLoadOnly()
    {
        var sourceTransform = "probe-transform-should-not-leak";
        var plan = CreateRepairPlan(
            new LinkPathMap
            {
                Links =
                {
                    new LinkPathRule
                    {
                        Name = "Campus",
                        Load = false
                    }
                }
            },
            new[]
            {
                new LinkInfo
                {
                    Id = 101,
                    LinkTypeId = 201,
                    Name = "Campus",
                    TypeName = "Campus",
                    Path = "BIM 360://Hub/Project/Campus.rvt",
                    LinkedFileStatus = "Loaded",
                    IsLoaded = true,
                    PathExists = true,
                    IsCloud = true,
                    WorksetName = "Shared Levels and Grids",
                    TransformOrigin = "0,0,0",
                    TransformFingerprint = sourceTransform,
                    LastWriteTimeUtc = "2026-05-23T00:00:00.0000000Z",
                    SizeBytes = 123456
                }
            },
            "link-map.yml",
            "link-plan.json",
            maxChanges: 5);

        var json = JsonSerializer.Serialize(plan, TerminalJsonOptions.PrettyCamel);
        using var document = JsonDocument.Parse(json);
        var actions = document.RootElement.GetProperty("actions").EnumerateArray().ToArray();
        if (actions.Length != 1)
            return new LinkRepairPlanJsonEvidence(false, $"expected one link repair action in emitted JSON, found {actions.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");

        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "linkId",
            "linkTypeId",
            "instanceIds",
            "linkName",
            "typeName",
            "oldPath",
            "newPath",
            "oldLoaded",
            "newLoaded",
            "oldPathExists",
            "newPathExists",
            "oldPathLastWriteTimeUtc",
            "newPathLastWriteTimeUtc",
            "oldPathSizeBytes",
            "newPathSizeBytes",
        };
        var names = actions[0].EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        var unexpected = names
            .Where(name => !allowed.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (unexpected.Length > 0)
            return new LinkRepairPlanJsonEvidence(false, $"emitted link repair action exposes unexpected fields: {string.Join(", ", unexpected)}.");

        var missing = allowed
            .Where(name => !names.Contains(name, StringComparer.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
            return new LinkRepairPlanJsonEvidence(false, $"emitted link repair action is missing path/load evidence fields: {string.Join(", ", missing)}.");

        var forbiddenTokens = new[]
        {
            sourceTransform,
            "transform",
            "coordinate",
            "origin",
            "placement"
        };
        var leaked = forbiddenTokens
            .Where(token => json.Contains(token, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (leaked.Length > 0)
            return new LinkRepairPlanJsonEvidence(false, $"emitted link-repair-plan.v1 JSON leaks coordinate/transform evidence: {string.Join(", ", leaked)}.");

        return new LinkRepairPlanJsonEvidence(
            true,
            "emitted link-repair-plan.v1 JSON is path/load-only: it carries old/new path, load-state, and file evidence fields; source transform/coordinate values are absent.");
    }

    private static (bool Exists, string? LastWriteTimeUtc, long? SizeBytes) ProbePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, null, null);

        try
        {
            var info = new FileInfo(path);
            return info.Exists
                ? (true, info.LastWriteTimeUtc.ToString("o"), info.Length)
                : (false, null, null);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or UnauthorizedAccessException)
        {
            return (false, null, null);
        }
    }

    private static bool IsExternallyResolvedPath(string path) =>
        path.StartsWith("BIM 360://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("Autodesk Docs://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("RSN://", StringComparison.OrdinalIgnoreCase);

    private static string LinkTypeKey(LinkInfo link) =>
        link.LinkTypeId.HasValue
            ? $"id:{link.LinkTypeId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : $"type:{NormalizePath(link.TypeName)}|path:{NormalizePath(link.Path)}";

    private static IReadOnlySet<string> ParseChecks(string value)
    {
        var checks = ParseCsv(value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (checks.Count == 0)
        {
            checks.Add("paths");
            checks.Add("loaded");
            checks.Add("coordinates");
        }

        return checks;
    }

    private static List<string> ParseCsv(string? value) =>
        (value ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

    private static bool MatchesSelector(string value, string selector)
    {
        if (string.Equals(selector, "all", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(selector))
            return false;
        var pattern = "^" + Regex.Escape(selector.Trim()).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool PathEquals(string? left, string? right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string? value) =>
        (value ?? "").Trim().Replace('\\', '/').TrimEnd('/');

    private static string Render(object value, string outputFormat) =>
        outputFormat switch
        {
            "json" => JsonSerializer.Serialize(value, TerminalJsonOptions.PrettyCamel),
            "markdown" => RenderMarkdown(value),
            _ => RenderTable(value)
        };

    private static string RenderTable(object value) =>
        value switch
        {
            LinkAuditReport report => $"Link audit report ({report.SchemaVersion}): links={report.LinkCount}, errors={report.ErrorCount}, warnings={report.WarningCount}",
            LinkRepairPlan plan => $"Link repair plan ({plan.SchemaVersion}): actions={plan.Summary.ActionCount}, issues={plan.Summary.IssueCount}",
            _ => value.ToString() ?? ""
        };

    private static string RenderMarkdown(object value)
    {
        var writer = new StringWriter();
        switch (value)
        {
            case LinkAuditReport report:
                writer.WriteLine("# Link Audit Report");
                writer.WriteLine();
                writer.WriteLine($"- Schema: `{report.SchemaVersion}`");
                writer.WriteLine($"- Links: `{report.LinkCount}`");
                writer.WriteLine($"- Errors: `{report.ErrorCount}`");
                writer.WriteLine($"- Warnings: `{report.WarningCount}`");
                writer.WriteLine();
                writer.WriteLine("| Severity | Code | Link | Message |");
                writer.WriteLine("| --- | --- | --- | --- |");
                foreach (var issue in report.Issues)
                    writer.WriteLine($"| `{issue.Severity}` | `{issue.Code}` | {EscapeTable(issue.LinkName)} | {EscapeTable(issue.Message)} |");
                break;
            case LinkRepairPlan plan:
                writer.WriteLine("# Link Repair Plan");
                writer.WriteLine();
                writer.WriteLine($"- Schema: `{plan.SchemaVersion}`");
                writer.WriteLine($"- Actions: `{plan.Summary.ActionCount}`");
                writer.WriteLine($"- Issues: `{plan.Summary.IssueCount}`");
                writer.WriteLine();
                writer.WriteLine("| Link | Old path | New path | Load | New path exists |");
                writer.WriteLine("| --- | --- | --- | --- | --- |");
                foreach (var action in plan.Actions)
                    writer.WriteLine($"| {EscapeTable(action.LinkName)} | {EscapeTable(action.OldPath)} | {EscapeTable(action.NewPath)} | `{action.OldLoaded}` -> `{action.NewLoaded}` | `{action.NewPathExists.ToString().ToLowerInvariant()}` |");
                break;
        }

        return writer.ToString().TrimEnd();
    }

    private static void SaveJson<T>(string path, T value)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(full, JsonSerializer.Serialize(value, TerminalJsonOptions.PrettyCamel));
    }

    private static string EscapeTable(string? value) =>
        (value ?? "").Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);

    public sealed class LinkAuditRules
    {
        public string SchemaVersion { get; set; } = "link-rules.v1";
        public List<LinkRule> Links { get; set; } = new();
    }

    public sealed class LinkRule
    {
        public string Name { get; set; } = "";
        public string? Path { get; set; }
        public bool Required { get; set; } = true;
        public bool MustBeLoaded { get; set; } = true;
        public string? CoordinateFingerprint { get; set; }
    }

    public sealed class LinkPathMap
    {
        public string SchemaVersion { get; set; } = "link-path-map.v1";
        public List<LinkPathRule> Links { get; set; } = new();
    }

    public sealed class LinkPathRule
    {
        public string Name { get; set; } = "";
        public string? NewPath { get; set; }
        public bool? Load { get; set; }
    }

    public sealed record LinkAuditReport(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("generatedAtUtc")] string GeneratedAtUtc,
        [property: JsonPropertyName("rulesPath")] string RulesPath,
        [property: JsonPropertyName("checks")] IReadOnlyList<string> Checks,
        [property: JsonPropertyName("linkCount")] int LinkCount,
        [property: JsonPropertyName("errorCount")] int ErrorCount,
        [property: JsonPropertyName("warningCount")] int WarningCount,
        [property: JsonPropertyName("issues")] IReadOnlyList<LinkAuditIssue> Issues,
        [property: JsonPropertyName("links")] IReadOnlyList<LinkAuditEntry> Links);

    public sealed record LinkAuditIssue(
        [property: JsonPropertyName("severity")] string Severity,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("linkId")] long LinkId,
        [property: JsonPropertyName("linkName")] string LinkName,
        [property: JsonPropertyName("message")] string Message);

    public sealed record LinkAuditEntry(
        [property: JsonPropertyName("linkId")] long LinkId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("typeName")] string TypeName,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("isLoaded")] bool IsLoaded,
        [property: JsonPropertyName("pathExists")] bool PathExists,
        [property: JsonPropertyName("linkedFileStatus")] string LinkedFileStatus,
        [property: JsonPropertyName("worksetName")] string? WorksetName,
        [property: JsonPropertyName("transformFingerprint")] string TransformFingerprint);

    public sealed record LinkRepairPlan(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
        [property: JsonPropertyName("mapPath")] string MapPath,
        [property: JsonPropertyName("planPath")] string PlanPath,
        [property: JsonPropertyName("dryRun")] bool DryRun,
        [property: JsonPropertyName("maxChanges")] int MaxChanges,
        [property: JsonPropertyName("summary")] LinkRepairSummary Summary,
        [property: JsonPropertyName("actions")] IReadOnlyList<LinkRepairAction> Actions,
        [property: JsonPropertyName("issues")] IReadOnlyList<LinkRepairIssue> Issues,
        [property: JsonPropertyName("commands")] IReadOnlyList<string> Commands);

    public sealed record LinkRepairSummary(
        [property: JsonPropertyName("linkCount")] int LinkCount,
        [property: JsonPropertyName("actionCount")] int ActionCount,
        [property: JsonPropertyName("issueCount")] int IssueCount,
        [property: JsonPropertyName("newPathMissingCount")] int NewPathMissingCount);

    public sealed record LinkRepairAction(
        [property: JsonPropertyName("linkId")] long LinkId,
        [property: JsonPropertyName("linkTypeId")] long? LinkTypeId,
        [property: JsonPropertyName("instanceIds")] IReadOnlyList<long> InstanceIds,
        [property: JsonPropertyName("linkName")] string LinkName,
        [property: JsonPropertyName("typeName")] string TypeName,
        [property: JsonPropertyName("oldPath")] string OldPath,
        [property: JsonPropertyName("newPath")] string NewPath,
        [property: JsonPropertyName("oldLoaded")] bool OldLoaded,
        [property: JsonPropertyName("newLoaded")] bool NewLoaded,
        [property: JsonPropertyName("oldPathExists")] bool OldPathExists,
        [property: JsonPropertyName("newPathExists")] bool NewPathExists,
        [property: JsonPropertyName("oldPathLastWriteTimeUtc")] string? OldPathLastWriteTimeUtc,
        [property: JsonPropertyName("newPathLastWriteTimeUtc")] string? NewPathLastWriteTimeUtc,
        [property: JsonPropertyName("oldPathSizeBytes")] long? OldPathSizeBytes,
        [property: JsonPropertyName("newPathSizeBytes")] long? NewPathSizeBytes);

    public sealed record LinkRepairIssue(
        [property: JsonPropertyName("severity")] string Severity,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("linkId")] long LinkId,
        [property: JsonPropertyName("linkName")] string LinkName,
        [property: JsonPropertyName("message")] string Message);

    internal sealed record LinkRepairPlanJsonEvidence(bool Success, string Evidence);
}
