using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Shared;
using RevitCli.Sheets;
using YamlDotNet.Core;

namespace RevitCli.Commands;

public static class SheetsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static Command Create(RevitClient client)
    {
        var command = new Command("sheets", "Verify and manage local sheet-frame expectations");
        command.AddCommand(CreateVerifyCommand(client));
        command.AddCommand(CreateIssueMetaCommand(client));
        command.AddCommand(CreateRenumberCommand(client));
        command.AddCommand(CreateIndexCommand(client));
        return command;
    }

    private static Command CreateIssueMetaCommand(RevitClient client)
    {
        var selectorOpt = new Option<string>("--selector", () => "all", "Sheet selector: all, sheet number/name text, or glob");
        var issueCodeOpt = new Option<string>("--issue-code", "Issue/revision code to write") { IsRequired = true };
        var issueDateOpt = new Option<string>("--issue-date", "Issue date to write") { IsRequired = true };
        var planOutputOpt = new Option<string>("--plan-output", "Write the frozen sheet issue plan JSON") { IsRequired = true };
        var paramMapOpt = new Option<string?>("--param-map", "Titleblock parameter map YAML path");
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview only; no model writes are performed");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");

        var command = new Command("issue-meta", "Plan sheet issue metadata updates without writing the model")
        {
            selectorOpt, issueCodeOpt, issueDateOpt, planOutputOpt, paramMapOpt, dryRunOpt, outputOpt
        };
        command.SetHandler(async (selector, issueCode, issueDate, planOutput, paramMap, dryRun, output) =>
        {
            Environment.ExitCode = await ExecuteIssueMetaAsync(
                client, selector, issueCode, issueDate, planOutput, paramMap, dryRun, output, Console.Out);
        }, selectorOpt, issueCodeOpt, issueDateOpt, planOutputOpt, paramMapOpt, dryRunOpt, outputOpt);
        return command;
    }

    private static Command CreateRenumberCommand(RevitClient client)
    {
        var ruleOpt = new Option<string>("--rule", "Sheet numbering rule YAML path") { IsRequired = true };
        var planOutputOpt = new Option<string>("--plan-output", "Write the frozen sheet renumber plan JSON") { IsRequired = true };
        var selectorOpt = new Option<string>("--selector", () => "all", "Sheet selector: all, sheet number/name text, or glob");
        var maxChangesOpt = new Option<int?>("--max-changes", "Maximum planned sheet number changes");
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview only; no model writes are performed");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");

        var command = new Command("renumber", "Plan sheet renumbering from a numbering rule without writing the model")
        {
            ruleOpt, planOutputOpt, selectorOpt, maxChangesOpt, dryRunOpt, outputOpt
        };
        command.SetHandler(async (rule, planOutput, selector, maxChanges, dryRun, output) =>
        {
            Environment.ExitCode = await ExecuteRenumberAsync(
                client, rule, planOutput, selector, maxChanges, dryRun, output, Console.Out);
        }, ruleOpt, planOutputOpt, selectorOpt, maxChangesOpt, dryRunOpt, outputOpt);
        return command;
    }

    private static Command CreateVerifyCommand(RevitClient client)
    {
        var againstOpt = new Option<string?>("--against", "Sheet index YAML path");
        var ruleOpt = new Option<string?>("--rule", "Run a single sheet rule");
        var issuesOnlyOpt = new Option<bool>("--issues-only", "Only render warning/error issues");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");

        var command = new Command("verify", "Verify sheet numbering and required sheet expectations")
        {
            againstOpt, ruleOpt, issuesOnlyOpt, outputOpt
        };
        command.SetHandler(async (against, rule, issuesOnly, output) =>
        {
            Environment.ExitCode = await ExecuteVerifyAsync(client, against, rule, issuesOnly, output, Console.Out);
        }, againstOpt, ruleOpt, issuesOnlyOpt, outputOpt);
        return command;
    }

    private static Command CreateIndexCommand(RevitClient client)
    {
        var command = new Command("index", "Create or inspect .revitcli/sheets/index.yml");
        command.AddCommand(CreateIndexInitCommand(client));
        command.AddCommand(CreateIndexShowCommand());
        return command;
    }

    private static Command CreateIndexInitCommand(RevitClient client)
    {
        var pathOpt = new Option<string?>("--path", "Output path (default: .revitcli/sheets/index.yml)");
        var forceOpt = new Option<bool>("--force", "Overwrite an existing index file");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, yaml");

        var command = new Command("init", "Bootstrap a local sheet index from the active model")
        {
            pathOpt, forceOpt, outputOpt
        };
        command.SetHandler(async (path, force, output) =>
        {
            Environment.ExitCode = await ExecuteIndexInitAsync(client, path, force, output, Console.Out);
        }, pathOpt, forceOpt, outputOpt);
        return command;
    }

    private static Command CreateIndexShowCommand()
    {
        var pathOpt = new Option<string?>("--path", "Sheet index path (default: .revitcli/sheets/index.yml)");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, yaml");

        var command = new Command("show", "Show the declared sheet index");
        command.SetHandler(async (path, output) =>
        {
            Environment.ExitCode = await ExecuteIndexShowAsync(path, output, Console.Out);
        }, pathOpt, outputOpt);
        command.AddOption(pathOpt);
        command.AddOption(outputOpt);
        return command;
    }

    public static async Task<int> ExecuteVerifyAsync(
        RevitClient client,
        string? againstPath,
        string? rule,
        bool issuesOnly,
        string outputFormat,
        TextWriter output)
    {
        var normalizedOutput = NormalizeOutput(outputFormat, "table", "json", "markdown");
        if (normalizedOutput is null)
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, json, markdown.");
            return 1;
        }

        LoadedSheetIndex? loadedIndex;
        try
        {
            loadedIndex = LoadIndex(againstPath);
            if (!string.IsNullOrWhiteSpace(rule))
                SheetVerifier.ValidateRuleName(rule);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var snapshotResult = await client.CaptureSnapshotAsync(new SnapshotRequest
        {
            IncludeCategories = Array.Empty<string>().ToList(),
            IncludeSheets = true,
            IncludeSchedules = false,
            SummaryOnly = false,
        });

        if (!snapshotResult.Success)
        {
            await output.WriteLineAsync($"Error: {snapshotResult.Error}");
            return 4;
        }

        var report = SheetVerifier.Verify(snapshotResult.Data ?? new ModelSnapshot(), loadedIndex, rule);
        var rendered = issuesOnly ? FilterInfoIssues(report) : report;

        switch (normalizedOutput)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(rendered, JsonOptions));
                break;
            case "markdown":
                await output.WriteLineAsync(RenderMarkdown(rendered));
                break;
            default:
                await output.WriteLineAsync(RenderTable(rendered));
                break;
        }

        return report.Summary.ExitCode;
    }

    public static async Task<int> ExecuteIssueMetaAsync(
        RevitClient client,
        string selector,
        string issueCode,
        string issueDate,
        string planOutputPath,
        string? paramMapPath,
        bool dryRun,
        string outputFormat,
        TextWriter output)
    {
        var normalizedOutput = NormalizeOutput(outputFormat, "table", "json", "markdown");
        if (normalizedOutput is null)
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, json, markdown.");
            return 1;
        }

        if (!dryRun)
        {
            await output.WriteLineAsync("Error: sheets issue-meta only creates reviewed plans. Use --dry-run, then apply the saved plan with revitcli plan apply.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(issueCode))
        {
            await output.WriteLineAsync("Error: --issue-code is required.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(issueDate))
        {
            await output.WriteLineAsync("Error: --issue-date is required.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(planOutputPath))
        {
            await output.WriteLineAsync("Error: --plan-output is required.");
            return 1;
        }

        LoadedSheetIssueParamMap paramMap;
        try
        {
            paramMap = SheetIssueParamMapStore.LoadOrDefault(paramMapPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var snapshotResult = await client.CaptureSnapshotAsync(new SnapshotRequest
        {
            IncludeCategories = Array.Empty<string>().ToList(),
            IncludeSheets = true,
            IncludeSchedules = false,
            SummaryOnly = false,
        });

        if (!snapshotResult.Success)
        {
            await output.WriteLineAsync($"Error: {snapshotResult.Error}");
            return 4;
        }

        var plan = SheetIssuePlanner.Create(
            snapshotResult.Data ?? new ModelSnapshot(),
            selector,
            issueCode,
            issueDate,
            paramMap,
            planOutputPath);

        SheetIssuePlanStore.Save(planOutputPath, plan);

        switch (normalizedOutput)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(plan, JsonOptions));
                break;
            case "markdown":
                await output.WriteLineAsync(RenderIssuePlanMarkdown(plan, planOutputPath));
                break;
            default:
                await output.WriteLineAsync(RenderIssuePlanTable(plan, planOutputPath));
                break;
        }

        return plan.Summary.ActionCount == 0 ? 2 : 0;
    }

    public static async Task<int> ExecuteRenumberAsync(
        RevitClient client,
        string rulePath,
        string planOutputPath,
        string selector,
        int? maxChanges,
        bool dryRun,
        string outputFormat,
        TextWriter output)
    {
        var normalizedOutput = NormalizeOutput(outputFormat, "table", "json", "markdown");
        if (normalizedOutput is null)
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, json, markdown.");
            return 1;
        }

        if (!dryRun)
        {
            await output.WriteLineAsync("Error: sheets renumber only creates reviewed plans. Use --dry-run, then apply the saved plan with revitcli plan apply.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(rulePath))
        {
            await output.WriteLineAsync("Error: --rule is required.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(planOutputPath))
        {
            await output.WriteLineAsync("Error: --plan-output is required.");
            return 1;
        }

        LoadedSheetIndex rule;
        try
        {
            rule = SheetIndexStore.Load(rulePath);
            SheetRenumberPlanner.ValidateRule(rule.Index);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var snapshotResult = await client.CaptureSnapshotAsync(new SnapshotRequest
        {
            IncludeCategories = Array.Empty<string>().ToList(),
            IncludeSheets = true,
            IncludeSchedules = false,
            SummaryOnly = false,
        });

        if (!snapshotResult.Success)
        {
            await output.WriteLineAsync($"Error: {snapshotResult.Error}");
            return 4;
        }

        SheetRenumberPlan plan;
        try
        {
            plan = SheetRenumberPlanner.Create(
                snapshotResult.Data ?? new ModelSnapshot(),
                selector,
                rule,
                planOutputPath);
        }
        catch (InvalidOperationException ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        if (maxChanges.HasValue && maxChanges.Value < 1)
        {
            await output.WriteLineAsync("Error: --max-changes must be at least 1.");
            return 1;
        }

        if (maxChanges.HasValue && plan.Summary.ActionCount > maxChanges.Value)
        {
            await output.WriteLineAsync(
                $"Error: plan has {plan.Summary.ActionCount} change(s), exceeds --max-changes {maxChanges.Value}.");
            return 1;
        }

        SheetRenumberPlanStore.Save(planOutputPath, plan);

        switch (normalizedOutput)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(plan, JsonOptions));
                break;
            case "markdown":
                await output.WriteLineAsync(RenderRenumberPlanMarkdown(plan, planOutputPath));
                break;
            default:
                await output.WriteLineAsync(RenderRenumberPlanTable(plan, planOutputPath));
                break;
        }

        return plan.Summary.ActionCount == 0 ? 2 : 0;
    }

    public static async Task<int> ExecuteIndexInitAsync(
        RevitClient client,
        string? path,
        bool force,
        string outputFormat,
        TextWriter output)
    {
        var normalizedOutput = NormalizeOutput(outputFormat, "table", "yaml");
        if (normalizedOutput is null)
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, yaml.");
            return 1;
        }

        var target = SheetIndexStore.ResolvePath(path);
        if (File.Exists(target) && !force)
        {
            await output.WriteLineAsync($"Error: sheet index already exists: {target}. Use --force to overwrite.");
            return 1;
        }

        var snapshotResult = await client.CaptureSnapshotAsync(new SnapshotRequest
        {
            IncludeCategories = Array.Empty<string>().ToList(),
            IncludeSheets = true,
            IncludeSchedules = false,
            SummaryOnly = false,
        });

        if (!snapshotResult.Success)
        {
            await output.WriteLineAsync($"Error: {snapshotResult.Error}");
            return 4;
        }

        var index = SheetVerifier.CreateIndexFromSnapshot(snapshotResult.Data ?? new ModelSnapshot());
        var yaml = SheetIndexStore.ToYaml(index);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await output.WriteLineAsync($"Writing sheet index: {target}");
        await File.WriteAllTextAsync(target, yaml);

        if (normalizedOutput == "yaml")
        {
            await output.WriteLineAsync(yaml.TrimEnd());
        }
        else
        {
            await output.WriteLineAsync($"Created sheet index with {index.Required.Count} required sheet declaration(s).");
            await output.WriteLineAsync($"Review and edit before using: revitcli sheets verify --against {Quote(target)}");
        }

        return 0;
    }

    public static async Task<int> ExecuteIndexShowAsync(
        string? path,
        string outputFormat,
        TextWriter output)
    {
        var normalizedOutput = NormalizeOutput(outputFormat, "table", "json", "yaml");
        if (normalizedOutput is null)
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, json, yaml.");
            return 1;
        }

        LoadedSheetIndex loaded;
        try
        {
            loaded = SheetIndexStore.Load(SheetIndexStore.ResolvePath(path));
            SheetVerifier.ValidateIndex(loaded.Index);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        switch (normalizedOutput)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(new { path = loaded.Path, index = loaded.Index }, JsonOptions));
                break;
            case "yaml":
                await output.WriteLineAsync(SheetIndexStore.ToYaml(loaded.Index).TrimEnd());
                break;
            default:
                await output.WriteLineAsync(RenderIndexTable(loaded));
                break;
        }

        return 0;
    }

    private static LoadedSheetIndex? LoadIndex(string? againstPath)
    {
        if (!string.IsNullOrWhiteSpace(againstPath))
        {
            var full = Path.GetFullPath(againstPath);
            if (!File.Exists(full))
                throw new FileNotFoundException($"Sheet index not found: {full}", full);
            return SheetIndexStore.Load(full);
        }

        return SheetIndexStore.TryLoadDefault();
    }

    private static SheetVerifyReport FilterInfoIssues(SheetVerifyReport report)
    {
        return new SheetVerifyReport
        {
            Command = report.Command,
            SchemaVersion = report.SchemaVersion,
            ConfigSource = report.ConfigSource,
            Summary = report.Summary,
            Issues = report.Issues.Where(issue => issue.Severity != SheetIssueSeverity.Info).ToArray(),
        };
    }

    private static string RenderTable(SheetVerifyReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine("Sheet verification");
        writer.WriteLine($"Config: {report.ConfigSource}");
        writer.WriteLine($"Sheets: {report.Summary.TotalSheets}, placed views: {report.Summary.TotalPlacedViews}, exit: {report.Summary.ExitCode}");
        writer.WriteLine();

        if (report.Issues.Length == 0)
        {
            writer.WriteLine("No sheet issues found.");
            return writer.ToString().TrimEnd();
        }

        writer.WriteLine($"{"Severity",-8} {"Rule",-24} Message");
        writer.WriteLine(new string('-', 110));
        foreach (var issue in report.Issues)
        {
            writer.WriteLine($"{issue.Severity,-8} {Trim(issue.Rule, 24),-24} {issue.Message}");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderMarkdown(SheetVerifyReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Sheet Verification");
        writer.WriteLine();
        writer.WriteLine($"- Config: `{report.ConfigSource}`");
        writer.WriteLine($"- Sheets: {report.Summary.TotalSheets}");
        writer.WriteLine($"- Placed views: {report.Summary.TotalPlacedViews}");
        writer.WriteLine($"- Exit code: {report.Summary.ExitCode}");
        writer.WriteLine();

        if (report.Issues.Length == 0)
        {
            writer.WriteLine("No sheet issues found.");
            return writer.ToString().TrimEnd();
        }

        writer.WriteLine("| Severity | Rule | Message |");
        writer.WriteLine("| --- | --- | --- |");
        foreach (var issue in report.Issues)
        {
            writer.WriteLine($"| {issue.Severity} | `{issue.Rule}` | {issue.Message.Replace("|", "\\|", StringComparison.Ordinal)} |");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderIndexTable(LoadedSheetIndex loaded)
    {
        var writer = new StringWriter();
        writer.WriteLine("Sheet index");
        writer.WriteLine($"Path: {loaded.Path}");
        writer.WriteLine($"Name: {loaded.Index.Name}");
        writer.WriteLine($"Required sheets: {loaded.Index.Required.Count}");
        if (!string.IsNullOrWhiteSpace(loaded.Index.Numbering.Scheme))
            writer.WriteLine($"Numbering scheme: {loaded.Index.Numbering.Scheme}");
        if (loaded.Index.Linkage.OverloadThreshold is > 0)
            writer.WriteLine($"Overload threshold: {loaded.Index.Linkage.OverloadThreshold}");

        if (loaded.Index.Required.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine($"{"Pattern",-18} {"MinViews",-9} Description");
            writer.WriteLine(new string('-', 80));
            foreach (var required in loaded.Index.Required)
            {
                var minViews = required.NeedsViews.Sum(view => Math.Max(0, view.MinCount));
                writer.WriteLine($"{Trim(required.Pattern, 18),-18} {minViews,-9} {required.Description ?? ""}");
            }
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderIssuePlanTable(SheetIssuePlan plan, string planOutputPath)
    {
        var writer = new StringWriter();
        writer.WriteLine("Sheet issue metadata dry-run plan");
        writer.WriteLine($"Plan: {Path.GetFullPath(planOutputPath)}");
        writer.WriteLine($"Selector: {plan.Selector}");
        writer.WriteLine($"Issue: {plan.IssueCode} / {plan.IssueDate}");
        writer.WriteLine($"Actions: {plan.Summary.ActionCount}, skipped: {plan.Summary.SkippedCount}, matched sheets: {plan.Summary.MatchedSheets}");
        writer.WriteLine();

        if (plan.Actions.Count == 0)
        {
            writer.WriteLine("No sheet metadata changes planned.");
        }
        else
        {
            writer.WriteLine($"{"Sheet",-18} {"Parameter",-24} Old -> New");
            writer.WriteLine(new string('-', 100));
            foreach (var action in plan.Actions.Take(20))
            {
                writer.WriteLine(
                    $"{Trim($"{action.SheetNumber} {action.SheetName}".Trim(), 18),-18} {Trim(action.Parameter, 24),-24} \"{action.OldValue}\" -> \"{action.NewValue}\"");
            }

            if (plan.Actions.Count > 20)
                writer.WriteLine($"... and {plan.Actions.Count - 20} more action(s).");
        }

        writer.WriteLine();
        writer.WriteLine($"Review: {plan.Commands.Show}");
        return writer.ToString().TrimEnd();
    }

    private static string RenderIssuePlanMarkdown(SheetIssuePlan plan, string planOutputPath)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Sheet Issue Metadata Plan");
        writer.WriteLine();
        writer.WriteLine("- Schema: `sheet-issue-plan.v1`");
        writer.WriteLine($"- Plan: `{EscapeInlineCode(Path.GetFullPath(planOutputPath))}`");
        writer.WriteLine($"- Selector: `{EscapeInlineCode(plan.Selector)}`");
        writer.WriteLine($"- Issue code: `{EscapeInlineCode(plan.IssueCode)}`");
        writer.WriteLine($"- Issue date: `{EscapeInlineCode(plan.IssueDate)}`");
        writer.WriteLine($"- Actions: `{plan.Summary.ActionCount}`");
        writer.WriteLine($"- Skipped: `{plan.Summary.SkippedCount}`");
        writer.WriteLine();

        if (plan.Actions.Count > 0)
        {
            writer.WriteLine("| Sheet | Parameter | Old | New |");
            writer.WriteLine("| --- | --- | --- | --- |");
            foreach (var action in plan.Actions.Take(30))
            {
                writer.WriteLine(
                    $"| {EscapeTableCell($"{action.SheetNumber} {action.SheetName}".Trim())} | `{EscapeInlineCode(action.Parameter)}` | {EscapeTableCell(action.OldValue)} | {EscapeTableCell(action.NewValue)} |");
            }

            writer.WriteLine();
        }

        if (plan.Skipped.Count > 0)
        {
            writer.WriteLine("## Skipped");
            writer.WriteLine();
            writer.WriteLine("| Sheet | Key | Reason |");
            writer.WriteLine("| --- | --- | --- |");
            foreach (var skipped in plan.Skipped.Take(30))
            {
                writer.WriteLine(
                    $"| {EscapeTableCell($"{skipped.SheetNumber} {skipped.SheetName}".Trim())} | `{EscapeInlineCode(skipped.Key)}` | {EscapeTableCell(skipped.Message)} |");
            }
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderRenumberPlanTable(SheetRenumberPlan plan, string planOutputPath)
    {
        var writer = new StringWriter();
        writer.WriteLine("Sheet renumber dry-run plan");
        writer.WriteLine($"Plan: {Path.GetFullPath(planOutputPath)}");
        writer.WriteLine($"Rule: {plan.RulePath}");
        writer.WriteLine($"Selector: {plan.Selector}");
        writer.WriteLine($"Actions: {plan.Summary.ActionCount}, skipped: {plan.Summary.SkippedCount}, matched sheets: {plan.Summary.MatchedSheets}");
        writer.WriteLine();

        if (plan.Actions.Count == 0)
        {
            writer.WriteLine("No sheet renumber changes planned.");
        }
        else
        {
            writer.WriteLine($"{"Sheet",-18} Old -> New");
            writer.WriteLine(new string('-', 80));
            foreach (var action in plan.Actions.Take(20))
            {
                writer.WriteLine(
                    $"{Trim($"{action.SheetNumber} {action.SheetName}".Trim(), 18),-18} \"{action.OldNumber}\" -> \"{action.NewNumber}\"");
            }

            if (plan.Actions.Count > 20)
                writer.WriteLine($"... and {plan.Actions.Count - 20} more action(s).");
        }

        writer.WriteLine();
        writer.WriteLine($"Review: {plan.Commands.Show}");
        return writer.ToString().TrimEnd();
    }

    private static string RenderRenumberPlanMarkdown(SheetRenumberPlan plan, string planOutputPath)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Sheet Renumber Plan");
        writer.WriteLine();
        writer.WriteLine("- Schema: `sheet-renumber-plan.v1`");
        writer.WriteLine($"- Plan: `{EscapeInlineCode(Path.GetFullPath(planOutputPath))}`");
        writer.WriteLine($"- Rule: `{EscapeInlineCode(plan.RulePath)}`");
        writer.WriteLine($"- Selector: `{EscapeInlineCode(plan.Selector)}`");
        writer.WriteLine($"- Actions: `{plan.Summary.ActionCount}`");
        writer.WriteLine($"- Skipped: `{plan.Summary.SkippedCount}`");
        writer.WriteLine();

        if (plan.Actions.Count > 0)
        {
            writer.WriteLine("| Sheet | Old number | New number |");
            writer.WriteLine("| --- | --- | --- |");
            foreach (var action in plan.Actions.Take(30))
            {
                writer.WriteLine(
                    $"| {EscapeTableCell($"{action.SheetNumber} {action.SheetName}".Trim())} | {EscapeTableCell(action.OldNumber)} | {EscapeTableCell(action.NewNumber)} |");
            }

            writer.WriteLine();
        }

        if (plan.Skipped.Count > 0)
        {
            writer.WriteLine("## Skipped");
            writer.WriteLine();
            writer.WriteLine("| Sheet | Reason | Message |");
            writer.WriteLine("| --- | --- | --- |");
            foreach (var skipped in plan.Skipped.Take(30))
            {
                writer.WriteLine(
                    $"| {EscapeTableCell($"{skipped.SheetNumber} {skipped.SheetName}".Trim())} | `{EscapeInlineCode(skipped.Reason)}` | {EscapeTableCell(skipped.Message)} |");
            }
        }

        return writer.ToString().TrimEnd();
    }

    private static string? NormalizeOutput(string? output, params string[] allowed)
    {
        var normalized = (output ?? allowed[0]).Trim().ToLowerInvariant();
        return allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? normalized : null;
    }

    private static string Trim(string value, int max)
    {
        if (value.Length <= max)
            return value;

        return value[..Math.Max(0, max - 3)] + "...";
    }

    private static string EscapeTableCell(string value) =>
        (value ?? "").Replace("|", "\\|", StringComparison.Ordinal);

    private static string EscapeInlineCode(string value) =>
        (value ?? "").Replace("`", "\\`", StringComparison.Ordinal);

    private static string Quote(string path) => $"\"{path.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
