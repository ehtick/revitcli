using System;
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

public static class ViewsCommand
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static Command Create(RevitClient client)
    {
        var command = new Command("views", "Audit, template, and clone view sets");
        command.AddCommand(CreateAuditCommand(client));
        command.AddCommand(CreateTemplateApplyCommand(client));
        command.AddCommand(CreateCloneSetCommand(client));
        return command;
    }

    private static Command CreateAuditCommand(RevitClient client)
    {
        var rulesOpt = new Option<string>("--rules", "View standards YAML") { IsRequired = true };
        var templatesOpt = new Option<bool>("--templates", "Check view template assignments");
        var browserOpt = new Option<bool>("--browser", "Check browser organization parameters");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");
        var command = new Command("audit", "Audit views against local standards")
        {
            rulesOpt,
            templatesOpt,
            browserOpt,
            outputOpt
        };

        command.SetHandler(async (string rules, bool templates, bool browser, string output) =>
        {
            Environment.ExitCode = await ExecuteAuditAsync(client, rules, templates, browser, output, Console.Out);
        }, rulesOpt, templatesOpt, browserOpt, outputOpt);
        return command;
    }

    private static Command CreateTemplateApplyCommand(RevitClient client)
    {
        var selectorOpt = new Option<string>("--selector", "View name selector or 'all'") { IsRequired = true };
        var templateOpt = new Option<string>("--template", "Target view template name") { IsRequired = true };
        var planOutputOpt = new Option<string>("--plan-output", "Write view-template-plan JSON") { IsRequired = true };
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview only; no model writes are performed");
        var excludeOpt = new Option<string>("--exclude", () => "locked", "Comma-separated exclusion flags, e.g. locked");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");
        var command = new Command("template-apply", "Create a reviewed plan to apply a view template")
        {
            selectorOpt,
            templateOpt,
            planOutputOpt,
            dryRunOpt,
            excludeOpt,
            outputOpt
        };

        command.SetHandler(async (string selector, string template, string planOutput, bool dryRun, string exclude, string output) =>
        {
            Environment.ExitCode = await ExecuteTemplateApplyAsync(client, selector, template, planOutput, dryRun, exclude, output, Console.Out);
        }, selectorOpt, templateOpt, planOutputOpt, dryRunOpt, excludeOpt, outputOpt);
        return command;
    }

    private static Command CreateCloneSetCommand(RevitClient client)
    {
        var fromSetOpt = new Option<string>("--from-set", "Source view selector or set name") { IsRequired = true };
        var toPrefixOpt = new Option<string>("--to-prefix", "Target view name prefix") { IsRequired = true };
        var namingRuleOpt = new Option<string>("--naming-rule", "Target name rule, e.g. '{prefix}{name}'") { IsRequired = true };
        var planOutputOpt = new Option<string>("--plan-output", "Write view-clone-plan JSON") { IsRequired = true };
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview only; no model writes are performed");
        var includeSheetsOpt = new Option<bool>("--include-sheets", () => false, "Also plan sheet placement duplication");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");
        var command = new Command("clone-set", "Create a reviewed plan to clone a named view set")
        {
            fromSetOpt,
            toPrefixOpt,
            namingRuleOpt,
            planOutputOpt,
            dryRunOpt,
            includeSheetsOpt,
            outputOpt
        };

        command.SetHandler(async (string fromSet, string toPrefix, string namingRule, string planOutput, bool dryRun, bool includeSheets, string output) =>
        {
            Environment.ExitCode = await ExecuteCloneSetAsync(client, fromSet, toPrefix, namingRule, planOutput, dryRun, includeSheets, output, Console.Out);
        }, fromSetOpt, toPrefixOpt, namingRuleOpt, planOutputOpt, dryRunOpt, includeSheetsOpt, outputOpt);
        return command;
    }

    public static async Task<int> ExecuteAuditAsync(
        RevitClient client,
        string rulesPath,
        bool checkTemplates,
        bool checkBrowser,
        string outputFormat,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        ViewStandardsRules rules;
        try
        {
            rules = LoadRules(rulesPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var views = await ListViewsAsync(client, output);
        if (views == null)
            return 4;

        var report = CreateAuditReport(rules, views, checkTemplates, checkBrowser, rulesPath);
        await output.WriteLineAsync(Render(report, normalizedOutput));
        return report.ErrorCount > 0 ? 2 : 0;
    }

    public static async Task<int> ExecuteTemplateApplyAsync(
        RevitClient client,
        string selector,
        string templateName,
        string planOutputPath,
        bool dryRun,
        string exclude,
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
            await output.WriteLineAsync("Error: views template-apply only creates reviewed plans. Use --dry-run and review the plan before a future apply path.");
            return 1;
        }

        var views = await ListViewsAsync(client, output);
        if (views == null)
            return 4;

        var template = views.FirstOrDefault(view =>
            view.IsTemplate && string.Equals(view.Name, templateName, StringComparison.OrdinalIgnoreCase));
        if (template == null)
        {
            await output.WriteLineAsync($"Error: view template '{templateName}' was not found.");
            return 1;
        }

        var plan = CreateTemplatePlan(views, selector, template, planOutputPath, exclude);
        SaveJson(planOutputPath, plan);
        await output.WriteLineAsync(Render(plan, normalizedOutput));
        return plan.Summary.ActionCount == 0 ? 2 : 0;
    }

    public static async Task<int> ExecuteCloneSetAsync(
        RevitClient client,
        string fromSet,
        string toPrefix,
        string namingRule,
        string planOutputPath,
        bool dryRun,
        bool includeSheets,
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
            await output.WriteLineAsync("Error: views clone-set only creates reviewed plans. Use --dry-run and review the plan before a future apply path.");
            return 1;
        }

        var views = await ListViewsAsync(client, output);
        if (views == null)
            return 4;

        var plan = CreateClonePlan(views, fromSet, toPrefix, namingRule, planOutputPath, includeSheets);
        if (plan.Issues.Any(issue => issue.Severity == "error"))
        {
            await output.WriteLineAsync(Render(plan, normalizedOutput));
            return 1;
        }

        SaveJson(planOutputPath, plan);
        await output.WriteLineAsync(Render(plan, normalizedOutput));
        return plan.Summary.ActionCount == 0 ? 2 : 0;
    }

    private static async Task<ViewInfo[]?> ListViewsAsync(RevitClient client, TextWriter output)
    {
        var result = await client.ListViewsAsync();
        if (result.Success)
            return result.Data ?? Array.Empty<ViewInfo>();

        await output.WriteLineAsync($"Error: {result.Error}");
        return null;
    }

    private static ViewStandardsRules LoadRules(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException($"View standards not found: {full}");

        var rules = Yaml.Deserialize<ViewStandardsRules>(File.ReadAllText(full))
            ?? throw new InvalidOperationException($"Failed to parse view standards: {full}");
        if (!string.Equals(rules.SchemaVersion, "view-standards.v1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(rules.SchemaVersion, "1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported view standards schemaVersion '{rules.SchemaVersion}'.");
        }

        return rules;
    }

    private static ViewStandardsReport CreateAuditReport(
        ViewStandardsRules rules,
        IReadOnlyList<ViewInfo> views,
        bool checkTemplates,
        bool checkBrowser,
        string rulesPath)
    {
        var issues = new List<ViewStandardsIssue>();
        var auditableViews = views.Where(view => !view.IsTemplate).OrderBy(view => view.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var view in auditableViews)
        {
            if (rules.Naming.RejectDefaultNames && IsDefaultViewName(view.Name))
                issues.Add(Issue("warning", "default-view-name", view, $"View '{view.Name}' appears to use a default generated name."));

            if (rules.Naming.RequiredPrefixes.Count > 0 &&
                !rules.Naming.RequiredPrefixes.Any(prefix => view.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(Issue("warning", "view-prefix-missing", view, $"View name must start with one of: {string.Join(", ", rules.Naming.RequiredPrefixes)}."));
            }

            if (checkTemplates || rules.Templates.Count > 0)
            {
                var templateRule = rules.Templates.FirstOrDefault(rule => AppliesTo(rule, view));
                if (templateRule != null &&
                    !string.Equals(view.TemplateName ?? "", templateRule.Template, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(Issue("error", "template-mismatch", view, $"Expected template '{templateRule.Template}', found '{view.TemplateName ?? "(none)"}'."));
                }
            }

            if (checkBrowser || rules.Browser.RequiredParameters.Count > 0)
            {
                foreach (var parameter in rules.Browser.RequiredParameters)
                {
                    if (!view.Parameters.TryGetValue(parameter, out var value) || string.IsNullOrWhiteSpace(value))
                        issues.Add(Issue("warning", "browser-parameter-missing", view, $"Browser parameter '{parameter}' is missing or empty."));
                }
            }
        }

        return new ViewStandardsReport(
            "view-standards-report.v1",
            DateTime.UtcNow.ToString("o"),
            Path.GetFullPath(rulesPath),
            auditableViews.Length,
            views.Count(view => view.IsTemplate),
            issues.Count(issue => issue.Severity == "error"),
            issues.Count(issue => issue.Severity == "warning"),
            issues);
    }

    private static ViewTemplatePlan CreateTemplatePlan(
        IReadOnlyList<ViewInfo> views,
        string selector,
        ViewInfo template,
        string planOutputPath,
        string exclude)
    {
        var excluded = ParseCsv(exclude).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = views
            .Where(view => !view.IsTemplate)
            .Where(view => MatchesSelector(view.Name, selector) || MatchesSelector(view.ViewType, selector))
            .ToArray();
        var actions = candidates
            .Where(view => !(excluded.Contains("locked") && view.IsLocked))
            .Where(view => view.TemplateId != template.Id)
            .OrderBy(view => view.Name, StringComparer.OrdinalIgnoreCase)
            .Select(view => new ViewTemplatePlanAction(
                view.Id,
                view.Name,
                view.ViewType,
                view.TemplateId,
                view.TemplateName,
                template.Id,
                template.Name,
                view.IsPlacedOnSheet))
            .ToArray();

        return new ViewTemplatePlan(
            "view-template-plan.v1",
            DateTime.UtcNow.ToString("o"),
            selector,
            template.Name,
            Path.GetFullPath(planOutputPath),
            true,
            new ViewTemplatePlanSummary(candidates.Length, actions.Length, actions.Count(action => action.IsPlacedOnSheet)),
            actions,
            new[]
            {
                $"revitcli plan show \"{Path.GetFullPath(planOutputPath)}\" --output markdown",
                "Review frozen view ids and old/new template ids before applying in Revit."
            });
    }

    private static ViewClonePlan CreateClonePlan(
        IReadOnlyList<ViewInfo> views,
        string fromSet,
        string toPrefix,
        string namingRule,
        string planOutputPath,
        bool includeSheets)
    {
        var existingNames = views.Select(view => view.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var issues = new List<ViewClonePlanIssue>();
        var selected = views
            .Where(view => !view.IsTemplate)
            .Where(view => MatchesSelector(view.Name, fromSet) || MatchesSelector(view.ViewType, fromSet))
            .OrderBy(view => view.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var actions = new List<ViewClonePlanAction>();
        foreach (var view in selected)
        {
            var targetName = ApplyNamingRule(namingRule, toPrefix, view.Name);
            if (existingNames.Contains(targetName))
                issues.Add(new ViewClonePlanIssue("error", "target-name-exists", view.Id, view.Name, targetName, "Target view name already exists."));
            if (!targetNames.Add(targetName))
                issues.Add(new ViewClonePlanIssue("error", "target-name-duplicate", view.Id, view.Name, targetName, "Multiple source views produce the same target name."));

            actions.Add(new ViewClonePlanAction(
                view.Id,
                view.Name,
                view.ViewType,
                targetName,
                view.TemplateId,
                view.TemplateName,
                includeSheets,
                view.IsPlacedOnSheet,
                "Before rollback deletes a cloned view, verify it has not been placed on a sheet."));
        }

        return new ViewClonePlan(
            "view-clone-plan.v1",
            DateTime.UtcNow.ToString("o"),
            fromSet,
            toPrefix,
            namingRule,
            Path.GetFullPath(planOutputPath),
            true,
            new ViewClonePlanSummary(selected.Length, actions.Count, issues.Count(issue => issue.Severity == "error"), actions.Count(action => action.SourceIsPlacedOnSheet)),
            actions,
            issues);
    }

    private static ViewStandardsIssue Issue(string severity, string code, ViewInfo view, string message) =>
        new(severity, code, view.Id, view.Name, view.ViewType, message);

    private static bool AppliesTo(ViewTemplateRule rule, ViewInfo view)
    {
        var selectorMatches = string.IsNullOrWhiteSpace(rule.Selector) || MatchesSelector(view.Name, rule.Selector);
        var typeMatches = string.IsNullOrWhiteSpace(rule.ViewType) || string.Equals(view.ViewType, rule.ViewType, StringComparison.OrdinalIgnoreCase);
        return selectorMatches && typeMatches;
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

    private static bool IsDefaultViewName(string name)
    {
        var trimmed = name.Trim();
        return Regex.IsMatch(trimmed, "^(Floor Plan|Ceiling Plan|Section|Elevation|3D View|Drafting View|Legend)\\s+\\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string ApplyNamingRule(string rule, string prefix, string sourceName)
    {
        if (rule.Contains("{name}", StringComparison.OrdinalIgnoreCase) ||
            rule.Contains("{source}", StringComparison.OrdinalIgnoreCase) ||
            rule.Contains("{prefix}", StringComparison.OrdinalIgnoreCase))
        {
            return rule
                .Replace("{prefix}", prefix, StringComparison.OrdinalIgnoreCase)
                .Replace("{name}", sourceName, StringComparison.OrdinalIgnoreCase)
                .Replace("{source}", sourceName, StringComparison.OrdinalIgnoreCase)
                .Trim();
        }

        return (prefix + sourceName).Trim();
    }

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

    private static string RenderTable(object value)
    {
        return value switch
        {
            ViewStandardsReport report => $"View standards report ({report.SchemaVersion}): views={report.ViewCount}, errors={report.ErrorCount}, warnings={report.WarningCount}",
            ViewTemplatePlan plan => $"View template plan ({plan.SchemaVersion}): actions={plan.Summary.ActionCount}, placed={plan.Summary.PlacedOnSheetCount}",
            ViewClonePlan plan => $"View clone plan ({plan.SchemaVersion}): actions={plan.Summary.ActionCount}, issues={plan.Summary.IssueCount}",
            _ => value.ToString() ?? ""
        };
    }

    private static string RenderMarkdown(object value)
    {
        var writer = new StringWriter();
        switch (value)
        {
            case ViewStandardsReport report:
                writer.WriteLine("# View Standards Report");
                writer.WriteLine();
                writer.WriteLine($"- Schema: `{report.SchemaVersion}`");
                writer.WriteLine($"- Views: `{report.ViewCount}`");
                writer.WriteLine($"- Errors: `{report.ErrorCount}`");
                writer.WriteLine($"- Warnings: `{report.WarningCount}`");
                writer.WriteLine();
                writer.WriteLine("| Severity | Code | View | Type | Message |");
                writer.WriteLine("| --- | --- | --- | --- | --- |");
                foreach (var issue in report.Issues)
                    writer.WriteLine($"| `{issue.Severity}` | `{issue.Code}` | {EscapeTable(issue.ViewName)} | {EscapeTable(issue.ViewType)} | {EscapeTable(issue.Message)} |");
                break;
            case ViewTemplatePlan plan:
                writer.WriteLine("# View Template Plan");
                writer.WriteLine();
                writer.WriteLine($"- Schema: `{plan.SchemaVersion}`");
                writer.WriteLine($"- Selector: `{EscapeInline(plan.Selector)}`");
                writer.WriteLine($"- Template: `{EscapeInline(plan.TemplateName)}`");
                writer.WriteLine($"- Actions: `{plan.Summary.ActionCount}`");
                writer.WriteLine();
                writer.WriteLine("| View | Type | Old template | New template | Placed |");
                writer.WriteLine("| --- | --- | --- | --- | --- |");
                foreach (var action in plan.Actions)
                    writer.WriteLine($"| {EscapeTable(action.ViewName)} | {EscapeTable(action.ViewType)} | {EscapeTable(action.OldTemplateName ?? "(none)")} | {EscapeTable(action.NewTemplateName)} | `{action.IsPlacedOnSheet.ToString().ToLowerInvariant()}` |");
                break;
            case ViewClonePlan plan:
                writer.WriteLine("# View Clone Plan");
                writer.WriteLine();
                writer.WriteLine($"- Schema: `{plan.SchemaVersion}`");
                writer.WriteLine($"- Source set: `{EscapeInline(plan.FromSet)}`");
                writer.WriteLine($"- Actions: `{plan.Summary.ActionCount}`");
                writer.WriteLine($"- Issues: `{plan.Summary.IssueCount}`");
                writer.WriteLine();
                writer.WriteLine("| Source | Target | Type | Rollback guard |");
                writer.WriteLine("| --- | --- | --- | --- |");
                foreach (var action in plan.Actions)
                    writer.WriteLine($"| {EscapeTable(action.SourceName)} | {EscapeTable(action.TargetName)} | {EscapeTable(action.ViewType)} | {EscapeTable(action.RollbackGuard)} |");
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

    private static string EscapeInline(string? value) =>
        (value ?? "").Replace("`", "'", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string EscapeTable(string? value) =>
        EscapeInline(value).Replace("|", "\\|", StringComparison.Ordinal);

    public sealed class ViewStandardsRules
    {
        public string SchemaVersion { get; set; } = "view-standards.v1";
        public List<ViewTemplateRule> Templates { get; set; } = new();
        public ViewBrowserRule Browser { get; set; } = new();
        public ViewNamingRule Naming { get; set; } = new();
    }

    public sealed class ViewTemplateRule
    {
        public string? Selector { get; set; }
        public string? ViewType { get; set; }
        public string Template { get; set; } = "";
    }

    public sealed class ViewBrowserRule
    {
        public List<string> RequiredParameters { get; set; } = new();
    }

    public sealed class ViewNamingRule
    {
        public List<string> RequiredPrefixes { get; set; } = new();
        public bool RejectDefaultNames { get; set; }
    }

    public sealed record ViewStandardsReport(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("generatedAtUtc")] string GeneratedAtUtc,
        [property: JsonPropertyName("rulesPath")] string RulesPath,
        [property: JsonPropertyName("viewCount")] int ViewCount,
        [property: JsonPropertyName("templateCount")] int TemplateCount,
        [property: JsonPropertyName("errorCount")] int ErrorCount,
        [property: JsonPropertyName("warningCount")] int WarningCount,
        [property: JsonPropertyName("issues")] IReadOnlyList<ViewStandardsIssue> Issues);

    public sealed record ViewStandardsIssue(
        [property: JsonPropertyName("severity")] string Severity,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("viewId")] long ViewId,
        [property: JsonPropertyName("viewName")] string ViewName,
        [property: JsonPropertyName("viewType")] string ViewType,
        [property: JsonPropertyName("message")] string Message);

    public sealed record ViewTemplatePlan(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
        [property: JsonPropertyName("selector")] string Selector,
        [property: JsonPropertyName("templateName")] string TemplateName,
        [property: JsonPropertyName("planPath")] string PlanPath,
        [property: JsonPropertyName("dryRun")] bool DryRun,
        [property: JsonPropertyName("summary")] ViewTemplatePlanSummary Summary,
        [property: JsonPropertyName("actions")] IReadOnlyList<ViewTemplatePlanAction> Actions,
        [property: JsonPropertyName("commands")] IReadOnlyList<string> Commands);

    public sealed record ViewTemplatePlanSummary(
        [property: JsonPropertyName("candidateCount")] int CandidateCount,
        [property: JsonPropertyName("actionCount")] int ActionCount,
        [property: JsonPropertyName("placedOnSheetCount")] int PlacedOnSheetCount);

    public sealed record ViewTemplatePlanAction(
        [property: JsonPropertyName("viewId")] long ViewId,
        [property: JsonPropertyName("viewName")] string ViewName,
        [property: JsonPropertyName("viewType")] string ViewType,
        [property: JsonPropertyName("oldTemplateId")] long? OldTemplateId,
        [property: JsonPropertyName("oldTemplateName")] string? OldTemplateName,
        [property: JsonPropertyName("newTemplateId")] long NewTemplateId,
        [property: JsonPropertyName("newTemplateName")] string NewTemplateName,
        [property: JsonPropertyName("isPlacedOnSheet")] bool IsPlacedOnSheet);

    public sealed record ViewClonePlan(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
        [property: JsonPropertyName("fromSet")] string FromSet,
        [property: JsonPropertyName("toPrefix")] string ToPrefix,
        [property: JsonPropertyName("namingRule")] string NamingRule,
        [property: JsonPropertyName("planPath")] string PlanPath,
        [property: JsonPropertyName("dryRun")] bool DryRun,
        [property: JsonPropertyName("summary")] ViewClonePlanSummary Summary,
        [property: JsonPropertyName("actions")] IReadOnlyList<ViewClonePlanAction> Actions,
        [property: JsonPropertyName("issues")] IReadOnlyList<ViewClonePlanIssue> Issues);

    public sealed record ViewClonePlanSummary(
        [property: JsonPropertyName("sourceCount")] int SourceCount,
        [property: JsonPropertyName("actionCount")] int ActionCount,
        [property: JsonPropertyName("issueCount")] int IssueCount,
        [property: JsonPropertyName("placedSourceCount")] int PlacedSourceCount);

    public sealed record ViewClonePlanAction(
        [property: JsonPropertyName("sourceViewId")] long SourceViewId,
        [property: JsonPropertyName("sourceName")] string SourceName,
        [property: JsonPropertyName("viewType")] string ViewType,
        [property: JsonPropertyName("targetName")] string TargetName,
        [property: JsonPropertyName("sourceTemplateId")] long? SourceTemplateId,
        [property: JsonPropertyName("sourceTemplateName")] string? SourceTemplateName,
        [property: JsonPropertyName("includeSheets")] bool IncludeSheets,
        [property: JsonPropertyName("sourceIsPlacedOnSheet")] bool SourceIsPlacedOnSheet,
        [property: JsonPropertyName("rollbackGuard")] string RollbackGuard);

    public sealed record ViewClonePlanIssue(
        [property: JsonPropertyName("severity")] string Severity,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("sourceViewId")] long SourceViewId,
        [property: JsonPropertyName("sourceName")] string SourceName,
        [property: JsonPropertyName("targetName")] string TargetName,
        [property: JsonPropertyName("message")] string Message);
}
