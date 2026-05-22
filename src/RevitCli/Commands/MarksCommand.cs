using System;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Numbering;
using RevitCli.Shared;
using YamlDotNet.Core;

namespace RevitCli.Commands;

public static class MarksCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create(RevitClient client)
    {
        var command = new Command("marks", "Plan and verify door/window Mark numbering workflows");
        command.AddCommand(CreateAssignCommand(client));
        command.AddCommand(CreateVerifyCommand(client));
        return command;
    }

    private static Command CreateAssignCommand(RevitClient client)
    {
        var categoryOpt = new Option<string>("--category", "Category to number: doors or windows") { IsRequired = true };
        var ruleOpt = new Option<string>("--rule", "Mark numbering rule YAML") { IsRequired = true };
        var planOutputOpt = new Option<string>("--plan-output", "Write frozen mark-assignment plan JSON") { IsRequired = true };
        var sortOpt = new Option<string>("--sort", () => "level,zone,type,location", "Comma-separated sort tokens");
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview only; no model writes are performed");
        var maxChangesOpt = new Option<int?>("--max-changes", "Maximum planned Mark changes");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");
        var command = new Command("assign", "Create a reviewed door/window Mark assignment plan")
        {
            categoryOpt,
            ruleOpt,
            planOutputOpt,
            sortOpt,
            dryRunOpt,
            maxChangesOpt,
            outputOpt
        };

        command.SetHandler(async (string category, string rule, string planOutput, string sort, bool dryRun, int? maxChanges, string output) =>
        {
            Environment.ExitCode = await ExecuteAssignAsync(
                client,
                category,
                rule,
                planOutput,
                sort,
                dryRun,
                maxChanges,
                output,
                Console.Out);
        }, categoryOpt, ruleOpt, planOutputOpt, sortOpt, dryRunOpt, maxChangesOpt, outputOpt);

        return command;
    }

    private static Command CreateVerifyCommand(RevitClient client)
    {
        var categoryOpt = new Option<string>("--category", () => "doors,windows", "Comma-separated categories: doors,windows");
        var againstOpt = new Option<string?>("--against", "Rule YAML file or glob for expected Mark patterns");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");
        var command = new Command("verify", "Verify door/window Mark uniqueness and optional rule conformance")
        {
            categoryOpt,
            againstOpt,
            outputOpt
        };

        command.SetHandler(async (string category, string? against, string output) =>
        {
            Environment.ExitCode = await ExecuteVerifyAsync(client, category, against, output, Console.Out);
        }, categoryOpt, againstOpt, outputOpt);

        return command;
    }

    public static async Task<int> ExecuteAssignAsync(
        RevitClient client,
        string category,
        string rulePath,
        string planOutputPath,
        string sort,
        bool dryRun,
        int? maxChanges,
        string outputFormat,
        TextWriter output)
    {
        var normalizedOutput = NormalizeOutput(outputFormat);
        if (normalizedOutput == null)
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, json, markdown.");
            return 1;
        }

        if (!dryRun)
        {
            await output.WriteLineAsync("Error: marks assign only creates reviewed plans. Use --dry-run, then apply the saved plan with revitcli plan apply.");
            return 1;
        }

        if (maxChanges.HasValue && maxChanges.Value < 1)
        {
            await output.WriteLineAsync("Error: --max-changes must be at least 1.");
            return 1;
        }

        string normalizedCategory;
        try
        {
            normalizedCategory = MarkNumberingPlanner.NormalizeCategory(category);
        }
        catch (InvalidOperationException ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        LoadedMarkNumberingRule rule;
        try
        {
            rule = MarkNumberingRuleStore.Load(rulePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var query = await client.QueryElementsAsync(normalizedCategory, filter: null);
        if (!query.Success)
        {
            await output.WriteLineAsync($"Error: {query.Error}");
            return 4;
        }

        MarkAssignmentPlan plan;
        try
        {
            plan = MarkNumberingPlanner.Create(
                query.Data ?? Array.Empty<ElementInfo>(),
                rule,
                normalizedCategory,
                ParseCsv(sort),
                planOutputPath);
        }
        catch (InvalidOperationException ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        if (maxChanges.HasValue && plan.Summary.ActionCount > maxChanges.Value)
        {
            await output.WriteLineAsync(
                $"Error: plan has {plan.Summary.ActionCount} change(s), exceeds --max-changes {maxChanges.Value}.");
            return 1;
        }

        MarkAssignmentPlanStore.Save(planOutputPath, plan);

        switch (normalizedOutput)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(plan, JsonOptions));
                break;
            case "markdown":
                await output.WriteLineAsync(RenderAssignmentMarkdown(plan, planOutputPath));
                break;
            default:
                await output.WriteLineAsync(RenderAssignmentTable(plan, planOutputPath));
                break;
        }

        return plan.Summary.ActionCount == 0 ? 2 : 0;
    }

    public static async Task<int> ExecuteVerifyAsync(
        RevitClient client,
        string categories,
        string? against,
        string outputFormat,
        TextWriter output)
    {
        var normalizedOutput = NormalizeOutput(outputFormat);
        if (normalizedOutput == null)
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, json, markdown.");
            return 1;
        }

        var requestedCategories = ParseCsv(categories);
        if (requestedCategories.Count == 0)
            requestedCategories.Add("doors");

        IReadOnlyDictionary<string, LoadedMarkNumberingRule> rules;
        try
        {
            rules = string.IsNullOrWhiteSpace(against)
                ? new Dictionary<string, LoadedMarkNumberingRule>(StringComparer.OrdinalIgnoreCase)
                : MarkNumberingRuleStore.LoadMany(against!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var elements = new Dictionary<string, IReadOnlyList<ElementInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in requestedCategories)
        {
            string normalizedCategory;
            try
            {
                normalizedCategory = MarkNumberingPlanner.NormalizeCategory(category);
            }
            catch (InvalidOperationException ex)
            {
                await output.WriteLineAsync($"Error: {ex.Message}");
                return 1;
            }

            var query = await client.QueryElementsAsync(normalizedCategory, filter: null);
            if (!query.Success)
            {
                await output.WriteLineAsync($"Error: {query.Error}");
                return 4;
            }

            elements[normalizedCategory] = query.Data ?? Array.Empty<ElementInfo>();
        }

        MarkVerifyReport report;
        try
        {
            report = MarkNumberingPlanner.Verify(elements, rules);
        }
        catch (InvalidOperationException ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        switch (normalizedOutput)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonOptions));
                break;
            case "markdown":
                await output.WriteLineAsync(RenderVerifyMarkdown(report));
                break;
            default:
                await output.WriteLineAsync(RenderVerifyTable(report));
                break;
        }

        return report.ErrorCount > 0 ? 2 : 0;
    }

    private static List<string> ParseCsv(string? value) =>
        (value ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

    private static string RenderAssignmentTable(MarkAssignmentPlan plan, string planPath)
    {
        var writer = new StringWriter();
        writer.WriteLine($"Mark assignment dry-run plan: {Path.GetFullPath(planPath)}");
        writer.WriteLine($"Category: {plan.Category}; elements: {plan.Summary.ElementCount}; actions: {plan.Summary.ActionCount}; skipped: {plan.Summary.SkippedCount}");
        foreach (var action in plan.Actions)
            writer.WriteLine($"  [{action.ElementId}] {action.ElementName}: \"{action.OldMark}\" -> \"{action.NewMark}\"");
        foreach (var skipped in plan.Skipped)
            writer.WriteLine($"  skipped [{skipped.ElementId}] {skipped.ElementName}: {skipped.Message}");
        writer.WriteLine($"Review: {plan.Commands.Show}");
        writer.WriteLine($"Dry-run apply: {plan.Commands.DryRunApply}");
        writer.WriteLine($"Apply: {plan.Commands.Apply}");
        return writer.ToString().TrimEnd();
    }

    private static string RenderAssignmentMarkdown(MarkAssignmentPlan plan, string planPath)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Mark Assignment Plan");
        writer.WriteLine();
        writer.WriteLine($"- Plan: `{EscapeInline(Path.GetFullPath(planPath))}`");
        writer.WriteLine($"- Schema: `{plan.SchemaVersion}`");
        writer.WriteLine($"- Category: `{plan.Category}`");
        writer.WriteLine($"- Rule: `{EscapeInline(plan.RulePath)}`");
        writer.WriteLine($"- Actions: `{plan.Summary.ActionCount}`");
        writer.WriteLine($"- Skipped: `{plan.Summary.SkippedCount}`");
        writer.WriteLine();
        writer.WriteLine("| Element | Old Mark | New Mark | Sort |");
        writer.WriteLine("| --- | --- | --- | --- |");
        foreach (var action in plan.Actions)
            writer.WriteLine($"| {EscapeTable($"{action.ElementName} [{action.ElementId}]")} | {EscapeTable(action.OldMark)} | {EscapeTable(action.NewMark)} | {EscapeTable(action.SortKey)} |");
        writer.WriteLine();
        writer.WriteLine("## Commands");
        writer.WriteLine();
        writer.WriteLine($"- Review: `{EscapeInline(plan.Commands.Show)}`");
        writer.WriteLine($"- Dry-run apply: `{EscapeInline(plan.Commands.DryRunApply)}`");
        writer.WriteLine($"- Apply: `{EscapeInline(plan.Commands.Apply)}`");
        return writer.ToString().TrimEnd();
    }

    private static string RenderVerifyTable(MarkVerifyReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine($"Mark verify report ({report.SchemaVersion}): errors={report.ErrorCount}, warnings={report.WarningCount}");
        foreach (var category in report.Categories)
            writer.WriteLine($"  {category.Category}: elements={category.ElementCount}, missing={category.MissingMarkCount}, duplicates={category.DuplicateMarkCount}, mismatches={category.RuleMismatchCount}");
        foreach (var issue in report.Issues.Take(50))
            writer.WriteLine($"  [{issue.Severity}] {issue.Code} {issue.Category}:{issue.ElementId} {issue.ElementName}: {issue.Message}");
        if (report.Issues.Count > 50)
            writer.WriteLine($"  ... and {report.Issues.Count - 50} more issue(s).");
        return writer.ToString().TrimEnd();
    }

    private static string RenderVerifyMarkdown(MarkVerifyReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Mark Verify Report");
        writer.WriteLine();
        writer.WriteLine($"- Schema: `{report.SchemaVersion}`");
        writer.WriteLine($"- Errors: `{report.ErrorCount}`");
        writer.WriteLine($"- Warnings: `{report.WarningCount}`");
        writer.WriteLine();
        writer.WriteLine("| Category | Elements | Missing | Duplicates | Rule mismatches | Planned actions |");
        writer.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var category in report.Categories)
            writer.WriteLine($"| `{category.Category}` | {category.ElementCount} | {category.MissingMarkCount} | {category.DuplicateMarkCount} | {category.RuleMismatchCount} | {category.PlannedActionCount} |");
        if (report.Issues.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Issues");
            writer.WriteLine();
            writer.WriteLine("| Severity | Code | Element | Current | Expected | Message |");
            writer.WriteLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var issue in report.Issues.Take(100))
                writer.WriteLine($"| `{issue.Severity}` | `{issue.Code}` | {EscapeTable($"{issue.ElementName} [{issue.ElementId}]")} | {EscapeTable(issue.CurrentMark)} | {EscapeTable(issue.ExpectedMark)} | {EscapeTable(issue.Message)} |");
        }
        return writer.ToString().TrimEnd();
    }

    private static string? NormalizeOutput(string? outputFormat)
    {
        var normalized = string.IsNullOrWhiteSpace(outputFormat)
            ? "table"
            : outputFormat.Trim().ToLowerInvariant();
        return normalized is "table" or "json" or "markdown" ? normalized : null;
    }

    private static string EscapeInline(string? value) =>
        (value ?? "").Replace("`", "'", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string EscapeTable(string? value) =>
        (value ?? "").Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal);
}
