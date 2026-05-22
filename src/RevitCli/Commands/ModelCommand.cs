using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text;
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

public static class ModelCommand
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static Command Create(RevitClient client)
    {
        var command = new Command("model", "Audit and plan safe model mapping fixes");
        command.AddCommand(CreateMapCheckCommand(client));
        command.AddCommand(CreateMapFixCommand(client));
        return command;
    }

    private static Command CreateMapCheckCommand(RevitClient client)
    {
        var againstOpt = new Option<string>("--against", "Model mapping YAML") { IsRequired = true };
        var worksetsOpt = new Option<bool>("--worksets", "Check workset mappings");
        var phasesOpt = new Option<bool>("--phases", "Check phase mappings");
        var outputOpt = new Option<string>("--output", () => "json", "Output format: table|json|markdown");
        var command = new Command("map-check", "Audit element workset and phase mappings")
        {
            againstOpt,
            worksetsOpt,
            phasesOpt,
            outputOpt
        };

        command.SetHandler(async (string against, bool worksets, bool phases, string output) =>
        {
            Environment.ExitCode = await ExecuteMapCheckAsync(client, against, worksets, phases, output, Console.Out);
        }, againstOpt, worksetsOpt, phasesOpt, outputOpt);
        return command;
    }

    private static Command CreateMapFixCommand(RevitClient client)
    {
        var againstOpt = new Option<string>("--against", "Model mapping YAML") { IsRequired = true };
        var planOutputOpt = new Option<string>("--plan-output", "Write model-map-fix-plan JSON") { IsRequired = true };
        var scopeOpt = new Option<string>("--scope", () => "rooms,doors,walls", "Comma-separated categories/scopes to plan");
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview only; no workset or phase parameters are changed");
        var maxChangesOpt = new Option<int>("--max-changes", () => 200, "Maximum model map fix actions allowed in the plan");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");
        var command = new Command("map-fix", "Create a reviewed plan for workset and phase mapping fixes")
        {
            againstOpt,
            planOutputOpt,
            scopeOpt,
            dryRunOpt,
            maxChangesOpt,
            outputOpt
        };

        command.SetHandler(async (string against, string planOutput, string scope, bool dryRun, int maxChanges, string output) =>
        {
            Environment.ExitCode = await ExecuteMapFixAsync(client, against, planOutput, scope, dryRun, maxChanges, output, Console.Out);
        }, againstOpt, planOutputOpt, scopeOpt, dryRunOpt, maxChangesOpt, outputOpt);
        return command;
    }

    public static async Task<int> ExecuteMapCheckAsync(
        RevitClient client,
        string rulesPath,
        bool worksets,
        bool phases,
        string outputFormat,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        ModelMappingRules rules;
        try
        {
            rules = LoadRules(rulesPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var elements = await ListModelMapElementsAsync(client, output);
        if (elements == null)
            return 4;

        var checks = ResolveChecks(worksets, phases);
        var report = CreateMapReport(rules, elements, rulesPath, checks);
        await output.WriteLineAsync(Render(report, normalizedOutput));
        return report.ErrorCount > 0 ? 2 : 0;
    }

    public static async Task<int> ExecuteMapFixAsync(
        RevitClient client,
        string rulesPath,
        string planOutputPath,
        string scope,
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
            await output.WriteLineAsync("Error: model map-fix only creates reviewed plans. Use --dry-run and review the plan before a future apply path.");
            return 1;
        }

        ModelMappingRules rules;
        try
        {
            rules = LoadRules(rulesPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var elements = await ListModelMapElementsAsync(client, output);
        if (elements == null)
            return 4;

        var plan = CreateFixPlan(rules, elements, rulesPath, planOutputPath, scope, maxChanges);
        SaveJson(planOutputPath, plan);
        await output.WriteLineAsync(Render(plan, normalizedOutput));
        if (plan.Issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)))
            return 1;
        return plan.Summary.ActionCount == 0 ? 2 : 0;
    }

    private static async Task<ModelMapElementInfo[]?> ListModelMapElementsAsync(RevitClient client, TextWriter output)
    {
        var result = await client.ListModelMapElementsAsync();
        if (result.Success)
            return result.Data ?? Array.Empty<ModelMapElementInfo>();

        await output.WriteLineAsync($"Error: {result.Error}");
        return null;
    }

    private static ModelMappingRules LoadRules(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException($"Model mapping rules not found: {full}");

        var rules = Yaml.Deserialize<ModelMappingRules>(File.ReadAllText(full))
            ?? throw new InvalidOperationException($"Failed to parse model mapping rules: {full}");
        if (!string.Equals(rules.SchemaVersion, "model-mapping.v1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(rules.SchemaVersion, "1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported model mapping schemaVersion '{rules.SchemaVersion}'.");
        }

        return rules;
    }

    private static IReadOnlySet<string> ResolveChecks(bool worksets, bool phases)
    {
        var checks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (worksets || !phases)
            checks.Add("worksets");
        if (phases || !worksets)
            checks.Add("phases");
        return checks;
    }

    private static ModelMapReport CreateMapReport(
        ModelMappingRules rules,
        IReadOnlyList<ModelMapElementInfo> elements,
        string rulesPath,
        IReadOnlySet<string> checks)
    {
        var issues = new List<ModelMapIssue>();
        var checkedCount = 0;
        foreach (var element in elements)
        {
            var rule = FindRule(rules, element);
            if (rule == null)
                continue;

            checkedCount++;
            AddMappingIssues(issues, element, rule, checks);
        }

        return new ModelMapReport(
            "model-map-report.v1",
            DateTime.UtcNow.ToString("o"),
            Path.GetFullPath(rulesPath),
            checks.OrderBy(check => check, StringComparer.OrdinalIgnoreCase).ToArray(),
            elements.Count,
            checkedCount,
            issues.Count(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)),
            issues.Count(issue => string.Equals(issue.Severity, "warning", StringComparison.OrdinalIgnoreCase)),
            issues);
    }

    private static ModelMapFixPlan CreateFixPlan(
        ModelMappingRules rules,
        IReadOnlyList<ModelMapElementInfo> elements,
        string rulesPath,
        string planOutputPath,
        string scope,
        int maxChanges)
    {
        var scopes = ParseCsv(scope).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var issues = new List<ModelMapIssue>();
        var actions = new List<ModelMapFixAction>();
        var candidates = 0;
        foreach (var element in elements.Where(element => IsInScope(element.Category, scopes)))
        {
            var rule = FindRule(rules, element);
            if (rule == null)
                continue;

            candidates++;
            AddFixAction(actions, issues, element, "workset", element.WorksetName, rule.Workset, element.CanWriteWorkset);
            AddFixAction(actions, issues, element, "phaseCreated", element.PhaseCreated, rule.PhaseCreated, element.CanWritePhaseCreated);
            AddFixAction(actions, issues, element, "phaseDemolished", element.PhaseDemolished, rule.PhaseDemolished, element.CanWritePhaseDemolished);
        }

        if (actions.Count > maxChanges)
        {
            issues.Add(new ModelMapIssue(
                "error",
                "max-changes-exceeded",
                0,
                "",
                "",
                "plan",
                maxChanges.ToString(System.Globalization.CultureInfo.InvariantCulture),
                actions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Plan has {actions.Count} action(s), which exceeds --max-changes {maxChanges}."));
        }

        return new ModelMapFixPlan(
            "model-map-fix-plan.v1",
            DateTime.UtcNow.ToString("o"),
            Path.GetFullPath(rulesPath),
            Path.GetFullPath(planOutputPath),
            true,
            scope,
            maxChanges,
            new ModelMapFixSummary(candidates, actions.Count, issues.Count, actions.Count(action => !action.CanWrite)),
            actions,
            issues,
            new[]
            {
                $"revitcli plan show \"{Path.GetFullPath(planOutputPath)}\" --output markdown",
                $"revitcli plan apply \"{Path.GetFullPath(planOutputPath)}\" --dry-run",
                $"revitcli plan apply \"{Path.GetFullPath(planOutputPath)}\" --yes --max-changes {maxChanges.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                "Review element ids, old/new phase/workset values, and write prechecks before applying in Revit."
            });
    }

    private static void AddMappingIssues(
        List<ModelMapIssue> issues,
        ModelMapElementInfo element,
        ModelMapRule rule,
        IReadOnlySet<string> checks)
    {
        if (checks.Contains("worksets") && !string.IsNullOrWhiteSpace(rule.Workset) &&
            !ValueEquals(element.WorksetName, rule.Workset))
        {
            issues.Add(Issue(element, "workset", rule.Workset!, element.WorksetName, "workset-mismatch"));
        }

        if (checks.Contains("phases") && !string.IsNullOrWhiteSpace(rule.PhaseCreated) &&
            !ValueEquals(element.PhaseCreated, rule.PhaseCreated))
        {
            issues.Add(Issue(element, "phaseCreated", rule.PhaseCreated!, element.PhaseCreated, "phase-created-mismatch"));
        }

        if (checks.Contains("phases") && !string.IsNullOrWhiteSpace(rule.PhaseDemolished) &&
            !ValueEquals(element.PhaseDemolished, rule.PhaseDemolished))
        {
            issues.Add(Issue(element, "phaseDemolished", rule.PhaseDemolished!, element.PhaseDemolished, "phase-demolished-mismatch"));
        }
    }

    private static void AddFixAction(
        List<ModelMapFixAction> actions,
        List<ModelMapIssue> issues,
        ModelMapElementInfo element,
        string field,
        string? oldValue,
        string? newValue,
        bool canWrite)
    {
        if (string.IsNullOrWhiteSpace(newValue) || ValueEquals(oldValue, newValue))
            return;

        var targetExists = TargetExists(element, field, newValue);
        var writableProbe = ResolveWritableProbe(canWrite, targetExists);
        var writePrecheckPassed = writableProbe;
        var unwritableReason = writePrecheckPassed
            ? null
            : !targetExists
                ? $"{field} target '{newValue}' was not found in the active document."
                : $"{field} is read-only or unavailable for this element.";
        actions.Add(new ModelMapFixAction(
            element.Id,
            element.Name,
            element.Category,
            field,
            oldValue,
            newValue,
            writePrecheckPassed,
            writableProbe,
            unwritableReason));

        if (!writePrecheckPassed)
        {
            issues.Add(new ModelMapIssue(
                "error",
                targetExists ? "target-not-writable" : "target-not-found",
                element.Id,
                element.Name,
                element.Category,
                field,
                newValue,
                oldValue ?? "",
                unwritableReason!));
        }
    }

    internal static bool ResolveWritableProbe(bool canWrite, bool targetExists) =>
        canWrite && targetExists;

    private static bool TargetExists(ModelMapElementInfo element, string field, string newValue)
    {
        var values = string.Equals(field, "workset", StringComparison.OrdinalIgnoreCase)
            ? element.AvailableWorksets
            : element.AvailablePhases;
        return values.Count > 0 && values.Any(value => ValueEquals(value, newValue));
    }

    private static ModelMapIssue Issue(ModelMapElementInfo element, string field, string expected, string? actual, string code) =>
        new(
            "error",
            code,
            element.Id,
            element.Name,
            element.Category,
            field,
            expected,
            actual ?? "",
            $"{field} expected '{expected}', found '{actual ?? ""}'.");

    private static ModelMapRule? FindRule(ModelMappingRules rules, ModelMapElementInfo element) =>
        rules.Rules.FirstOrDefault(rule => RuleMatches(rule, element));

    private static bool RuleMatches(ModelMapRule rule, ModelMapElementInfo element)
    {
        if (!string.IsNullOrWhiteSpace(rule.Category) && !CategoryMatches(element.Category, rule.Category))
            return false;
        if (!string.IsNullOrWhiteSpace(rule.Scope) && !CategoryMatches(element.Category, rule.Scope))
            return false;
        if (!string.IsNullOrWhiteSpace(rule.Name) && !MatchesSelector(element.Name, rule.Name))
            return false;
        return true;
    }

    private static bool IsInScope(string category, IReadOnlySet<string> scopes) =>
        scopes.Count == 0 ||
        scopes.Contains("all") ||
        scopes.Any(scope => CategoryMatches(category, scope));

    private static bool CategoryMatches(string category, string selector)
    {
        var left = NormalizeKey(category);
        var right = NormalizeKey(selector);
        if (left.Length > 0 && right.Length > 0 &&
            (left == right || left.TrimEnd('s') == right.TrimEnd('s')))
        {
            return true;
        }

        return MatchesSelector(category, selector);
    }

    private static bool MatchesSelector(string value, string selector)
    {
        if (string.Equals(selector, "all", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(selector))
            return false;
        var pattern = "^" + Regex.Escape(selector.Trim()).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string NormalizeKey(string value)
    {
        var normalized = (value ?? "").Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private static bool ValueEquals(string? left, string? right) =>
        string.Equals((left ?? "").Trim(), (right ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private static List<string> ParseCsv(string? value) =>
        (value ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

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
            ModelMapReport report => $"Model map report ({report.SchemaVersion}): checked={report.CheckedElementCount}, errors={report.ErrorCount}, warnings={report.WarningCount}",
            ModelMapFixPlan plan => $"Model map fix plan ({plan.SchemaVersion}): actions={plan.Summary.ActionCount}, blocked={plan.Summary.BlockedCount}",
            _ => value.ToString() ?? ""
        };

    private static string RenderMarkdown(object value)
    {
        var writer = new StringWriter();
        switch (value)
        {
            case ModelMapReport report:
                writer.WriteLine("# Model Map Report");
                writer.WriteLine();
                writer.WriteLine($"- Schema: `{report.SchemaVersion}`");
                writer.WriteLine($"- Checked elements: `{report.CheckedElementCount}`");
                writer.WriteLine($"- Errors: `{report.ErrorCount}`");
                writer.WriteLine();
                writer.WriteLine("| Code | Element | Category | Field | Expected | Actual |");
                writer.WriteLine("| --- | --- | --- | --- | --- | --- |");
                foreach (var issue in report.Issues)
                    writer.WriteLine($"| `{issue.Code}` | {EscapeTable(issue.ElementName)} | {EscapeTable(issue.Category)} | `{issue.Field}` | {EscapeTable(issue.Expected)} | {EscapeTable(issue.Actual)} |");
                break;
            case ModelMapFixPlan plan:
                writer.WriteLine("# Model Map Fix Plan");
                writer.WriteLine();
                writer.WriteLine($"- Schema: `{plan.SchemaVersion}`");
                writer.WriteLine($"- Actions: `{plan.Summary.ActionCount}`");
                writer.WriteLine($"- Blocked: `{plan.Summary.BlockedCount}`");
                writer.WriteLine();
                writer.WriteLine("| Element | Category | Field | Old | New | Write Precheck |");
                writer.WriteLine("| --- | --- | --- | --- | --- | --- |");
                foreach (var action in plan.Actions)
                    writer.WriteLine($"| {EscapeTable(action.ElementName)} | {EscapeTable(action.Category)} | `{action.Field}` | {EscapeTable(action.OldValue)} | {EscapeTable(action.NewValue)} | `{action.CanWrite.ToString().ToLowerInvariant()}` |");
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

    public sealed class ModelMappingRules
    {
        public string SchemaVersion { get; set; } = "model-mapping.v1";
        public List<ModelMapRule> Rules { get; set; } = new();
    }

    public sealed class ModelMapRule
    {
        public string? Scope { get; set; }
        public string? Category { get; set; }
        public string? Name { get; set; }
        public string? Workset { get; set; }
        public string? PhaseCreated { get; set; }
        public string? PhaseDemolished { get; set; }
    }

    public sealed record ModelMapReport(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("generatedAtUtc")] string GeneratedAtUtc,
        [property: JsonPropertyName("rulesPath")] string RulesPath,
        [property: JsonPropertyName("checks")] IReadOnlyList<string> Checks,
        [property: JsonPropertyName("elementCount")] int ElementCount,
        [property: JsonPropertyName("checkedElementCount")] int CheckedElementCount,
        [property: JsonPropertyName("errorCount")] int ErrorCount,
        [property: JsonPropertyName("warningCount")] int WarningCount,
        [property: JsonPropertyName("issues")] IReadOnlyList<ModelMapIssue> Issues);

    public sealed record ModelMapIssue(
        [property: JsonPropertyName("severity")] string Severity,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("elementId")] long ElementId,
        [property: JsonPropertyName("elementName")] string ElementName,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("field")] string Field,
        [property: JsonPropertyName("expected")] string Expected,
        [property: JsonPropertyName("actual")] string Actual,
        [property: JsonPropertyName("message")] string Message);

    public sealed record ModelMapFixPlan(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
        [property: JsonPropertyName("rulesPath")] string RulesPath,
        [property: JsonPropertyName("planPath")] string PlanPath,
        [property: JsonPropertyName("dryRun")] bool DryRun,
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("maxChanges")] int MaxChanges,
        [property: JsonPropertyName("summary")] ModelMapFixSummary Summary,
        [property: JsonPropertyName("actions")] IReadOnlyList<ModelMapFixAction> Actions,
        [property: JsonPropertyName("issues")] IReadOnlyList<ModelMapIssue> Issues,
        [property: JsonPropertyName("commands")] IReadOnlyList<string> Commands);

    public sealed record ModelMapFixSummary(
        [property: JsonPropertyName("candidateCount")] int CandidateCount,
        [property: JsonPropertyName("actionCount")] int ActionCount,
        [property: JsonPropertyName("issueCount")] int IssueCount,
        [property: JsonPropertyName("blockedCount")] int BlockedCount);

    public sealed record ModelMapFixAction(
        [property: JsonPropertyName("elementId")] long ElementId,
        [property: JsonPropertyName("elementName")] string ElementName,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("field")] string Field,
        [property: JsonPropertyName("oldValue")] string? OldValue,
        [property: JsonPropertyName("newValue")] string NewValue,
        [property: JsonPropertyName("canWrite")] bool CanWrite,
        [property: JsonPropertyName("writableProbe")] bool WritableProbe,
        [property: JsonPropertyName("unwritableReason")] string? UnwritableReason);
}
