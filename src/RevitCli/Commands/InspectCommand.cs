using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Output;
using RevitCli.Plans;
using RevitCli.Shared;
using RevitCli.Workflows;

namespace RevitCli.Commands;

public static class InspectCommand
{
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly KnownCategory[] KnownCategories =
    {
        new("walls", "Walls / 墙"),
        new("doors", "Doors / 门"),
        new("windows", "Windows / 窗"),
        new("rooms", "Rooms / 房间"),
        new("floors", "Floors / 楼板"),
        new("roofs", "Roofs / 屋顶"),
        new("stairs", "Stairs / 楼梯"),
        new("columns", "Columns / 柱"),
        new("structuralcolumns", "Structural Columns / 结构柱"),
        new("ceilings", "Ceilings / 天花板"),
        new("furniture", "Furniture / 家具"),
        new("levels", "Levels / 标高")
    };

    private static readonly SheetKeyParameter[] SheetKeyParameters =
    {
        new("drawnBy", new[] { "Drawn By", "DrawnBy", "绘图", "制图" }),
        new("checkedBy", new[] { "Checked By", "CheckedBy", "Checked", "审核", "校对" }),
        new("approvedBy", new[] { "Approved By", "ApprovedBy", "Approved", "批准" }),
        new("issueDate", new[] { "Sheet Issue Date", "Issue Date", "Date", "日期" }),
        new("revision", new[] { "Current Revision", "Revision", "Revision Number", "修订" }),
        new("scale", new[] { "Scale", "比例" })
    };

    public static Command Create(RevitClient client)
    {
        var command = new Command("inspect", "Discover model data for safe terminal workflows");
        command.AddCommand(CreateCategoriesCommand(client));
        command.AddCommand(CreateParamsCommand(client));
        command.AddCommand(CreateSchedulesCommand(client));
        command.AddCommand(CreateSheetsCommand(client));
        command.AddCommand(CreateWorkflowsCommand());
        command.AddCommand(CreatePlansCommand());
        return command;
    }

    private static Command CreateCategoriesCommand(RevitClient client)
    {
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        var includeEmptyOpt = new Option<bool>("--include-empty", () => false, "Show supported categories with zero elements");
        var command = new Command("categories", "Inspect common query categories") { outputOpt, includeEmptyOpt };
        command.SetHandler(async (output, includeEmpty) =>
        {
            Environment.ExitCode = await ExecuteCategoriesAsync(client, output, includeEmpty, Console.Out);
        }, outputOpt, includeEmptyOpt);
        return command;
    }

    private static Command CreateParamsCommand(RevitClient client)
    {
        var categoryArg = new Argument<string>("category", "Element category to inspect, e.g. doors");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        var nameOpt = new Option<string?>("--name", "Filter parameters by name pattern");
        var writableOnlyOpt = new Option<bool>("--writable-only", "Only show parameters confirmed writable by metadata");
        var missingOnlyOpt = new Option<bool>("--missing-only", "Only show parameters with missing values on some elements");
        var command = new Command("params", "Inspect parameters found on elements in a category")
        {
            categoryArg,
            outputOpt,
            nameOpt,
            writableOnlyOpt,
            missingOnlyOpt
        };
        command.SetHandler(async (category, output, name, writableOnly, missingOnly) =>
        {
            Environment.ExitCode = await ExecuteParamsAsync(
                client,
                category,
                output,
                name,
                writableOnly,
                missingOnly,
                Console.Out);
        }, categoryArg, outputOpt, nameOpt, writableOnlyOpt, missingOnlyOpt);
        return command;
    }

    private static Command CreateSchedulesCommand(RevitClient client)
    {
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        var categoryOpt = new Option<string?>("--category", "Filter schedules by category pattern");
        var nameOpt = new Option<string?>("--name", "Filter schedules by name pattern");
        var readyOnlyOpt = new Option<bool>("--ready-only", "Only show schedules ready for delivery export");
        var emptyOnlyOpt = new Option<bool>("--empty-only", "Only show schedules with zero rows");
        var issuesOnlyOpt = new Option<bool>("--issues-only", "Only show schedules with export-readiness issues");
        var command = new Command("schedules", "Inspect schedules and export commands")
        {
            outputOpt,
            categoryOpt,
            nameOpt,
            readyOnlyOpt,
            emptyOnlyOpt,
            issuesOnlyOpt
        };
        command.SetHandler(async (output, category, name, readyOnly, emptyOnly, issuesOnly) =>
        {
            Environment.ExitCode = await ExecuteSchedulesAsync(
                client,
                output,
                category,
                name,
                readyOnly,
                emptyOnly,
                issuesOnly,
                Console.Out);
        }, outputOpt, categoryOpt, nameOpt, readyOnlyOpt, emptyOnlyOpt, issuesOnlyOpt);
        return command;
    }

    private static Command CreateSheetsCommand(RevitClient client)
    {
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        var sheetsOpt = new Option<string[]>(
            "--sheets",
            () => Array.Empty<string>(),
            "Filter by sheet number/name patterns (e.g. A1*, \"A101\", all)");
        var readyOnlyOpt = new Option<bool>("--ready-only", "Only show sheets that are ready export candidates");
        var issuesOnlyOpt = new Option<bool>("--issues-only", "Only show sheets with missing info or review issues");
        var command = new Command("sheets", "Inspect sheets and export dry-run commands")
        {
            outputOpt,
            sheetsOpt,
            readyOnlyOpt,
            issuesOnlyOpt
        };
        command.SetHandler(async (output, sheets, readyOnly, issuesOnly) =>
        {
            Environment.ExitCode = await ExecuteSheetsAsync(
                client,
                output,
                sheets,
                readyOnly,
                issuesOnly,
                Console.Out);
        }, outputOpt, sheetsOpt, readyOnlyOpt, issuesOnlyOpt);
        return command;
    }

    private static Command CreateWorkflowsCommand()
    {
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        var dirOpt = new Option<string?>("--dir", "Project directory containing .revitcli/workflows");
        var command = new Command("workflows", "Inspect local workflow YAML files and next commands")
        {
            outputOpt,
            dirOpt
        };
        command.SetHandler(async (output, dir) =>
        {
            Environment.ExitCode = await ExecuteWorkflowsAsync(dir, output, Console.Out);
        }, outputOpt, dirOpt);
        return command;
    }

    private static Command CreatePlansCommand()
    {
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        var dirOpt = new Option<string?>("--dir", "Project directory containing .revitcli/plans");
        var command = new Command("plans", "Inspect saved mutation plans and review commands")
        {
            outputOpt,
            dirOpt
        };
        command.SetHandler(async (output, dir) =>
        {
            Environment.ExitCode = await ExecutePlansAsync(dir, output, Console.Out);
        }, outputOpt, dirOpt);
        return command;
    }

    public static async Task<int> ExecuteSchedulesAsync(
        RevitClient client,
        string outputFormat,
        TextWriter output)
        => await ExecuteSchedulesAsync(
            client,
            outputFormat,
            categoryFilter: null,
            nameFilter: null,
            readyOnly: false,
            emptyOnly: false,
            issuesOnly: false,
            output);

    public static async Task<int> ExecuteSchedulesAsync(
        RevitClient client,
        string outputFormat,
        string? categoryFilter,
        string? nameFilter,
        bool readyOnly,
        bool emptyOnly,
        TextWriter output)
        => await ExecuteSchedulesAsync(
            client,
            outputFormat,
            categoryFilter,
            nameFilter,
            readyOnly,
            emptyOnly,
            issuesOnly: false,
            output);

    public static async Task<int> ExecuteSchedulesAsync(
        RevitClient client,
        string outputFormat,
        string? categoryFilter,
        string? nameFilter,
        bool readyOnly,
        bool emptyOnly,
        bool issuesOnly,
        TextWriter output)
    {
        if (readyOnly && emptyOnly)
        {
            await output.WriteLineAsync("Error: --ready-only and --empty-only are mutually exclusive.");
            return 1;
        }

        if (readyOnly && issuesOnly)
        {
            await output.WriteLineAsync("Error: --ready-only and --issues-only are mutually exclusive.");
            return 1;
        }

        var result = await client.ListSchedulesAsync();
        if (!result.Success)
        {
            await output.WriteLineAsync($"Error: {result.Error}");
            return 1;
        }

        var schedules = result.Data ?? Array.Empty<ScheduleInfo>();
        var items = schedules
            .OrderBy(schedule => schedule.Name, StringComparer.OrdinalIgnoreCase)
            .Select(BuildScheduleInspectItem)
            .Where(item => MatchesOptionalPattern(item.Category ?? "", categoryFilter))
            .Where(item => MatchesOptionalPattern(item.Name, nameFilter))
            .Where(item => !readyOnly || item.ExportReady)
            .Where(item => !emptyOnly || item.RowCount == 0)
            .Where(item => !issuesOnly || item.HasIssues)
            .ToArray();

        if (IsJson(outputFormat))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(items, PrettyJson));
            return 0;
        }

        if (IsMarkdown(outputFormat))
        {
            await output.WriteLineAsync(RenderSchedulesMarkdown(items));
            return 0;
        }

        if (items.Length == 0)
        {
            await output.WriteLineAsync("No schedules matched the inspect filters.");
            return 0;
        }

        await output.WriteLineAsync($"{"Name",-30} {"Category",-15} {"Fields",-8} {"Rows",-8} {"Ready",-7} {"Issues",-24} Commands");
        await output.WriteLineAsync(new string('-', 160));

        foreach (var item in items)
        {
            var ready = item.ExportReady ? "yes" : "no";
            await output.WriteLineAsync(
                $"{TrimForTable(item.Name, 30),-30} {TrimForTable(item.Category ?? "-", 15),-15} {item.FieldCount,-8} {item.RowCount,-8} {ready,-7} {TrimForTable(item.IssueSummary, 24),-24} csv: {item.CsvExportCommand} | json: {item.JsonExportCommand}");
        }

        return 0;
    }

    public static async Task<int> ExecuteCategoriesAsync(
        RevitClient client,
        string outputFormat,
        bool includeEmpty,
        TextWriter output)
    {
        var items = new List<CategoryInspectItem>();

        foreach (var category in KnownCategories)
        {
            var result = await client.QueryElementsAsync(category.Alias, null);
            if (!result.Success)
            {
                if (IsConnectionFailure(result.Error))
                {
                    await output.WriteLineAsync($"Error: {result.Error}");
                    return 1;
                }

                items.Add(new CategoryInspectItem(
                    category.Alias,
                    category.Label,
                    null,
                    0,
                    false,
                    null,
                    BuildQueryCommand(category.Alias),
                    BuildParamsCommand(category.Alias),
                    result.Error));
                continue;
            }

            var elements = result.Data ?? Array.Empty<ElementInfo>();
            if (elements.Length == 0 && !includeEmpty)
                continue;

            items.Add(new CategoryInspectItem(
                category.Alias,
                category.Label,
                elements.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Category))?.Category,
                elements.Length,
                elements.Length > 0,
                elements.FirstOrDefault()?.Id,
                BuildQueryCommand(category.Alias),
                BuildParamsCommand(category.Alias),
                null));
        }

        if (IsJson(outputFormat))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(items, PrettyJson));
            return 0;
        }

        if (IsMarkdown(outputFormat))
        {
            await output.WriteLineAsync(RenderCategoriesMarkdown(items));
            return 0;
        }

        if (items.Count == 0)
        {
            await output.WriteLineAsync("No elements found in common categories.");
            return 0;
        }

        await output.WriteLineAsync($"{"Alias",-20} {"Model category",-18} {"Count",-8} Next command");
        await output.WriteLineAsync(new string('-', 94));
        foreach (var item in items.OrderByDescending(i => i.Count).ThenBy(i => i.Alias, StringComparer.OrdinalIgnoreCase))
        {
            var modelCategory = TrimForTable(item.ModelCategory ?? item.Label, 18);
            await output.WriteLineAsync($"{item.Alias,-20} {modelCategory,-18} {item.Count,-8} {item.ParamsCommand}");
        }

        return 0;
    }

    public static async Task<int> ExecuteWorkflowsAsync(
        string? projectDirectory,
        string outputFormat,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        string projectRoot;
        try
        {
            projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(projectDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            await output.WriteLineAsync($"Error: invalid --dir: {ex.Message}");
            return 1;
        }

        if (!Directory.Exists(projectRoot))
        {
            await output.WriteLineAsync($"Error: project directory not found: {projectRoot}");
            return 1;
        }

        var report = BuildWorkflowInspectReport(projectRoot);
        if (normalizedOutput == "json")
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(report, PrettyJson));
            return 0;
        }

        if (normalizedOutput == "markdown")
        {
            await output.WriteLineAsync(RenderWorkflowsMarkdown(report));
            return 0;
        }

        await output.WriteLineAsync(RenderWorkflowsTable(report));
        return 0;
    }

    public static async Task<int> ExecutePlansAsync(
        string? projectDirectory,
        string outputFormat,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        string projectRoot;
        try
        {
            projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(projectDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            await output.WriteLineAsync($"Error: invalid --dir: {ex.Message}");
            return 1;
        }

        if (!Directory.Exists(projectRoot))
        {
            await output.WriteLineAsync($"Error: project directory not found: {projectRoot}");
            return 1;
        }

        var report = BuildPlanInspectReport(projectRoot);
        if (normalizedOutput == "json")
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(report, PrettyJson));
            return 0;
        }

        if (normalizedOutput == "markdown")
        {
            await output.WriteLineAsync(RenderPlansMarkdown(report));
            return 0;
        }

        await output.WriteLineAsync(RenderPlansTable(report));
        return 0;
    }

    public static async Task<int> ExecuteSheetsAsync(
        RevitClient client,
        string outputFormat,
        TextWriter output)
        => await ExecuteSheetsAsync(
            client,
            outputFormat,
            Array.Empty<string>(),
            readyOnly: false,
            issuesOnly: false,
            output);

    public static async Task<int> ExecuteSheetsAsync(
        RevitClient client,
        string outputFormat,
        string[] sheetPatterns,
        bool readyOnly,
        bool issuesOnly,
        TextWriter output)
    {
        var result = await client.CaptureSnapshotAsync(new SnapshotRequest
        {
            IncludeCategories = new List<string>(),
            IncludeSheets = true,
            IncludeSchedules = false,
            SummaryOnly = false
        });
        if (!result.Success)
        {
            await output.WriteLineAsync($"Error: {result.Error}");
            return 1;
        }

        var sheets = result.Data?.Sheets ?? new List<SnapshotSheet>();
        var patterns = NormalizePatterns(sheetPatterns);
        var items = sheets
            .OrderBy(sheet => sheet.Number, StringComparer.OrdinalIgnoreCase)
            .ThenBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase)
            .Select(BuildSheetInspectItem)
            .Where(item => patterns.Length == 0 || patterns.Any(pattern => MatchesSheetPattern(item, pattern)))
            .Where(item => !readyOnly || item.ExportReady)
            .Where(item => !issuesOnly || item.HasIssues)
            .ToArray();

        if (IsJson(outputFormat))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(items, PrettyJson));
            return 0;
        }

        if (IsMarkdown(outputFormat))
        {
            await output.WriteLineAsync(RenderSheetsMarkdown(items));
            return 0;
        }

        if (items.Length == 0)
        {
            await output.WriteLineAsync("No sheets matched the inspect filters.");
            return 0;
        }

        await output.WriteLineAsync($"{"Number",-14} {"Name",-28} {"Views",-7} {"Ready",-7} {"Issues",-28} Commands");
        await output.WriteLineAsync(new string('-', 172));
        foreach (var item in items)
        {
            var ready = item.ExportReady ? "yes" : "no";
            await output.WriteLineAsync(
                $"{TrimForTable(item.Number, 14),-14} {TrimForTable(item.Name, 28),-28} {item.PlacedViewCount,-7} {ready,-7} {TrimForTable(item.IssueSummary, 28),-28} export: {item.ExportCommand} | dry-run: {item.DryRunCommand}");
        }

        return 0;
    }

    public static async Task<int> ExecuteParamsAsync(
        RevitClient client,
        string category,
        string outputFormat,
        TextWriter output)
        => await ExecuteParamsAsync(
            client,
            category,
            outputFormat,
            nameFilter: null,
            writableOnly: false,
            missingOnly: false,
            output);

    public static async Task<int> ExecuteParamsAsync(
        RevitClient client,
        string category,
        string outputFormat,
        string? nameFilter,
        bool writableOnly,
        bool missingOnly,
        TextWriter output)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            await output.WriteLineAsync("Error: category is required.");
            return 1;
        }

        var result = await client.QueryElementsAsync(category, null);
        if (!result.Success)
        {
            await output.WriteLineAsync($"Error: {result.Error}");
            return 1;
        }

        var elements = result.Data ?? Array.Empty<ElementInfo>();
        var items = BuildParameterItems(category, elements)
            .Where(item => MatchesOptionalPattern(item.Name, nameFilter))
            .Where(item => !writableOnly || item.CanWrite == true)
            .Where(item => !missingOnly || item.ValueCoveragePercent < 100)
            .ToArray();

        if (IsJson(outputFormat))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(items, PrettyJson));
            return 0;
        }

        if (IsMarkdown(outputFormat))
        {
            await output.WriteLineAsync(RenderParamsMarkdown(category, elements.Length, items));
            return 0;
        }

        if (elements.Length == 0)
        {
            await output.WriteLineAsync($"No elements found for category '{category}'.");
            return 0;
        }

        if (items.Length == 0)
        {
            await output.WriteLineAsync($"No parameters matched the inspect filters on {elements.Length} {category} element(s).");
            return 0;
        }

        await output.WriteLineAsync($"{"Parameter",-28} {"Seen",-8} {"Values",-8} {"Write",-12} {"Type",-12} {"Samples",-30} Dry-run probe");
        await output.WriteLineAsync(new string('-', 154));
        foreach (var item in items)
        {
            var storageTypes = item.StorageTypes.Length == 0
                ? "unknown"
                : string.Join("/", item.StorageTypes);
            var dryRunProbe = string.IsNullOrWhiteSpace(item.DryRunProbeCommand)
                ? "-"
                : item.DryRunProbeCommand;
            await output.WriteLineAsync(
                $"{TrimForTable(item.Name, 28),-28} {item.SeenOn,-8} {item.ValueCoveragePercent,6:0.#}%  {item.WriteStatus,-12} {TrimForTable(storageTypes, 12),-12} {TrimForTable(string.Join(" | ", item.SampleValues), 30),-30} {dryRunProbe}");
        }

        return 0;
    }

    private static ParameterInspectItem[] BuildParameterItems(string category, ElementInfo[] elements)
    {
        var stats = new Dictionary<string, ParameterStats>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in elements)
        {
            if (element.ParameterMetadata.Count > 0)
            {
                foreach (var parameter in element.ParameterMetadata.Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name)))
                {
                    var stat = GetParameterStats(stats, parameter.Name);
                    stat.SeenOn++;
                    stat.HasWriteMetadata = true;
                    if (parameter.CanWrite)
                    {
                        stat.WritableOn++;
                        stat.WritableSampleElementId ??= element.Id;
                    }

                    if (parameter.IsReadOnly)
                        stat.ReadOnlyOn++;
                    if (!string.IsNullOrWhiteSpace(parameter.StorageType))
                        stat.StorageTypes.Add(parameter.StorageType);

                    if (!string.IsNullOrWhiteSpace(parameter.Value))
                    {
                        stat.ValueSeenOn++;
                        AddSampleValue(stat, parameter.Value);
                    }
                    else
                    {
                        stat.MissingSampleElementId ??= element.Id;
                        if (parameter.CanWrite)
                            stat.MissingWritableSampleElementId ??= element.Id;
                    }
                }

                continue;
            }

            foreach (var (name, value) in element.Parameters)
            {
                var stat = GetParameterStats(stats, name);
                stat.SeenOn++;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    stat.ValueSeenOn++;
                    AddSampleValue(stat, value);
                }
            }
        }

        var denominator = Math.Max(elements.Length, 1);
        return stats.Values
            .OrderByDescending(stat => stat.SeenOn)
            .ThenBy(stat => stat.Name, StringComparer.OrdinalIgnoreCase)
            .Select(stat =>
            {
                var writeStatus = BuildWriteStatus(stat);
                var canWrite = stat.HasWriteMetadata
                    ? stat.WritableOn > 0
                    : (bool?)null;
                var probeElementId = stat.MissingWritableSampleElementId ?? stat.WritableSampleElementId;
                return new ParameterInspectItem(
                    stat.Name,
                    stat.SeenOn,
                    Math.Round(stat.SeenOn * 100.0 / denominator, 1),
                    stat.ValueSeenOn,
                    Math.Round(stat.ValueSeenOn * 100.0 / denominator, 1),
                    stat.HasWriteMetadata ? stat.WritableOn : null,
                    stat.HasWriteMetadata ? Math.Round(stat.WritableOn * 100.0 / denominator, 1) : null,
                    canWrite,
                    writeStatus,
                    stat.StorageTypes.OrderBy(type => type, StringComparer.OrdinalIgnoreCase).ToArray(),
                    stat.SampleValues.ToArray(),
                    probeElementId,
                    stat.MissingSampleElementId,
                    canWrite == true ? BuildDryRunProbeCommand(category, stat.Name, probeElementId) : "");
            })
            .ToArray();
    }

    private static WorkflowInspectReport BuildWorkflowInspectReport(string projectRoot)
    {
        var workflowDirectory = Path.Combine(projectRoot, WorkflowLoader.DefaultDirectory);
        var files = Directory.Exists(workflowDirectory)
            ? Directory.EnumerateFiles(workflowDirectory, "*.yml")
                .Concat(Directory.EnumerateFiles(workflowDirectory, "*.yaml"))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();
        var workflows = files
            .Select(path => BuildWorkflowInspectItem(path, projectRoot))
            .ToArray();

        return new WorkflowInspectReport(
            "inspect-workflows.v1",
            NormalizePath(projectRoot),
            NormalizePath(workflowDirectory),
            workflows.Length,
            workflows.Count(workflow => workflow.CanRun),
            workflows.Sum(workflow => workflow.IssueCount),
            workflows);
    }

    private static WorkflowInspectItem BuildWorkflowInspectItem(string path, string projectRoot)
    {
        var relativePath = NormalizePath(Path.GetRelativePath(projectRoot, path));
        try
        {
            var loaded = WorkflowLoader.Load(path);
            var simulation = WorkflowValidator.Simulate(loaded);
            var dryRunCount = simulation.Steps.Count(step =>
                string.Equals(step.Mode, "dry-run", StringComparison.OrdinalIgnoreCase));
            var mutatingCount = simulation.Steps.Count(step =>
                string.Equals(step.Mode, "mutating", StringComparison.OrdinalIgnoreCase));
            var approvalCount = simulation.Steps.Count(step => step.RequiresApproval);

            return new WorkflowInspectItem(
                relativePath,
                simulation.Name,
                simulation.Description,
                simulation.CanRun,
                simulation.StepCount,
                dryRunCount,
                mutatingCount,
                approvalCount,
                simulation.Issues.Count,
                simulation.CanRun ? "ready" : "blocked",
                simulation.Issues.ToArray(),
                BuildWorkflowInspectCommands(relativePath, simulation.Name, mutatingCount));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlDotNet.Core.YamlException)
        {
            var issues = new[]
            {
                new WorkflowValidationIssue(
                    WorkflowValidationSeverity.Error,
                    relativePath,
                    ex.Message)
            };
            var name = Path.GetFileNameWithoutExtension(path);
            return new WorkflowInspectItem(
                relativePath,
                name,
                null,
                false,
                0,
                0,
                0,
                0,
                issues.Length,
                "blocked",
                issues,
                BuildWorkflowInspectCommands(relativePath, name, mutatingCount: 0));
        }
    }

    private static PlanInspectReport BuildPlanInspectReport(string projectRoot)
    {
        var planDirectory = Path.Combine(projectRoot, ".revitcli", "plans");
        var files = Directory.Exists(planDirectory)
            ? Directory.EnumerateFiles(planDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .Where(path => !path.EndsWith(".receipt.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();
        var plans = files.Select(path => BuildPlanInspectItem(path, projectRoot)).ToArray();

        return new PlanInspectReport(
            "inspect-plans.v1",
            NormalizePath(projectRoot),
            NormalizePath(planDirectory),
            plans.Length,
            plans.Count(plan => string.Equals(plan.Status, "ready", StringComparison.OrdinalIgnoreCase)),
            plans.Count(plan => string.Equals(plan.Status, "high-impact", StringComparison.OrdinalIgnoreCase)),
            plans.Count(plan => string.Equals(plan.Status, "invalid", StringComparison.OrdinalIgnoreCase)),
            plans.Sum(plan => plan.ActionCount),
            plans);
    }

    private static PlanInspectItem BuildPlanInspectItem(string path, string projectRoot)
    {
        var fullPath = Path.GetFullPath(path);
        var relativePath = NormalizePath(Path.GetRelativePath(projectRoot, fullPath));
        var commands = BuildPlanInspectCommands(fullPath);
        var receiptPath = fullPath + ".receipt.json";
        try
        {
            var type = SetPlanFileStore.ReadType(fullPath);
            return type.ToLowerInvariant() switch
            {
                "set" => BuildSetPlanInspectItem(fullPath, relativePath, receiptPath, commands),
                "import" => BuildImportPlanInspectItem(fullPath, relativePath, receiptPath, commands),
                "fix" => BuildFixPlanInspectItem(fullPath, relativePath, receiptPath, commands),
                _ => InvalidPlanInspectItem(relativePath, fullPath, commands, $"Unsupported plan type '{type}'.")
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return InvalidPlanInspectItem(relativePath, fullPath, commands, ex.Message);
        }
    }

    private static PlanInspectItem BuildSetPlanInspectItem(
        string fullPath,
        string relativePath,
        string receiptPath,
        PlanInspectCommands commands)
    {
        var plan = SetPlanFileStore.Load(fullPath);
        var actionCount = plan.Summary.FrozenElementIds.Count > 0
            ? plan.Summary.FrozenElementIds.Count
            : plan.Summary.Affected;
        return new PlanInspectItem(
            relativePath,
            "set",
            BuildPlanStatus(actionCount),
            actionCount,
            plan.CreatedAtUtc,
            plan.CreatedBy,
            plan.Summary.Param,
            plan.Summary.Value,
            plan.Summary.OriginalTarget,
            plan.Summary.ApplyTarget,
            Array.Empty<string>(),
            File.Exists(receiptPath),
            File.Exists(receiptPath) ? NormalizePath(Path.GetFileName(receiptPath)) : "",
            commands);
    }

    private static PlanInspectItem BuildImportPlanInspectItem(
        string fullPath,
        string relativePath,
        string receiptPath,
        PlanInspectCommands commands)
    {
        var plan = SetPlanFileStore.LoadImport(fullPath);
        var issues = plan.Warnings
            .Concat(plan.Misses.Select(miss => $"missing match: {miss.MatchByValue}"))
            .Concat(plan.Duplicates.Select(duplicate => $"duplicate match: {duplicate.MatchByValue}"))
            .Concat(plan.Skipped.Select(skip => $"skipped cell: row {skip.RowNumber} {skip.Param}: {skip.Reason}"))
            .ToArray();
        return new PlanInspectItem(
            relativePath,
            "import",
            BuildPlanStatus(plan.Summary.ElementWrites),
            plan.Summary.ElementWrites,
            plan.CreatedAtUtc,
            plan.CreatedBy,
            plan.Summary.MatchBy,
            $"{plan.Summary.GroupCount} group(s)",
            plan.Summary.SourceCsv,
            $"category={plan.Summary.Category}",
            issues,
            File.Exists(receiptPath),
            File.Exists(receiptPath) ? NormalizePath(Path.GetFileName(receiptPath)) : "",
            commands);
    }

    private static PlanInspectItem BuildFixPlanInspectItem(
        string fullPath,
        string relativePath,
        string receiptPath,
        PlanInspectCommands commands)
    {
        var plan = SetPlanFileStore.LoadFix(fullPath);
        var issues = plan.Warnings
            .Concat(plan.Skipped.Select(skipped => $"skipped {skipped.Rule}: {skipped.Reason}"))
            .ToList();
        if (plan.Summary.InferredCount > 0)
            issues.Add($"{plan.Summary.InferredCount} inferred action(s) require --allow-inferred before apply.");

        return new PlanInspectItem(
            relativePath,
            "fix",
            BuildPlanStatus(plan.Summary.ActionCount),
            plan.Summary.ActionCount,
            plan.CreatedAtUtc,
            plan.CreatedBy,
            plan.Summary.CheckName,
            $"{plan.Summary.SkippedCount} skipped",
            plan.Summary.ProfilePath ?? "",
            string.Join(",", plan.Summary.Rules),
            issues.ToArray(),
            File.Exists(receiptPath),
            File.Exists(receiptPath) ? NormalizePath(Path.GetFileName(receiptPath)) : "",
            commands);
    }

    private static PlanInspectItem InvalidPlanInspectItem(
        string relativePath,
        string fullPath,
        PlanInspectCommands commands,
        string issue)
    {
        return new PlanInspectItem(
            relativePath,
            "unknown",
            "invalid",
            0,
            "",
            "",
            "",
            "",
            fullPath,
            "",
            new[] { issue },
            HasReceipt: false,
            ReceiptPath: "",
            commands);
    }

    private static string BuildPlanStatus(int actionCount)
    {
        if (actionCount <= 0)
            return "empty";
        return actionCount > 50 ? "high-impact" : "ready";
    }

    private static PlanInspectCommands BuildPlanInspectCommands(string fullPath)
    {
        var quoted = QuoteArgument(fullPath);
        var receiptPath = fullPath + ".receipt.json";
        return new PlanInspectCommands(
            $"revitcli plan show {quoted} --output markdown",
            $"revitcli plan apply {quoted} --dry-run",
            $"revitcli plan apply {quoted} --yes",
            File.Exists(receiptPath)
                ? $"revitcli rollback {QuoteArgument(receiptPath)} --dry-run"
                : "");
    }

    private static WorkflowInspectCommands BuildWorkflowInspectCommands(
        string relativePath,
        string workflowName,
        int mutatingCount)
    {
        var workflowPath = QuoteArgument(relativePath);
        var receiptCommand = string.IsNullOrWhiteSpace(workflowName)
            ? "revitcli workflow receipts --output markdown"
            : $"revitcli workflow receipts --name {QuoteArgument(workflowName)} --output markdown";

        return new WorkflowInspectCommands(
            $"revitcli workflow validate {workflowPath} --output markdown",
            $"revitcli workflow simulate {workflowPath} --output markdown",
            $"revitcli workflow review {workflowPath} --output markdown",
            $"revitcli workflow run {workflowPath} --dry-run --output markdown",
            mutatingCount > 0
                ? $"revitcli workflow run {workflowPath} --yes --output markdown"
                : "",
            receiptCommand);
    }

    private static string RenderWorkflowsTable(WorkflowInspectReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine($"RevitCli inspect workflows ({report.SchemaVersion})");
        writer.WriteLine($"Project: {report.ProjectDirectory}");
        writer.WriteLine($"Workflows: {report.WorkflowCount}; runnable: {report.RunnableCount}; issues: {report.IssueCount}");
        if (report.Workflows.Count == 0)
        {
            writer.WriteLine("No workflow YAML files found. Create one with revitcli workflow init pre-issue.");
            return writer.ToString().TrimEnd();
        }

        writer.WriteLine($"{"Name",-24} {"Status",-8} {"Steps",-6} {"Mutating",-9} {"Approvals",-10} {"Issues",-7} Next review");
        writer.WriteLine(new string('-', 132));
        foreach (var workflow in report.Workflows)
        {
            writer.WriteLine(
                $"{TrimForTable(workflow.Name, 24),-24} {workflow.Status,-8} {workflow.StepCount,-6} {workflow.MutatingStepCount,-9} {workflow.ApprovalRequiredCount,-10} {workflow.IssueCount,-7} {workflow.Commands.ReviewCommand}");
        }

        writer.WriteLine("Receipt review:");
        foreach (var workflow in report.Workflows)
            writer.WriteLine($"  {workflow.Name}: {workflow.Commands.ReceiptsCommand}");

        return writer.ToString().TrimEnd();
    }

    private static string RenderWorkflowsMarkdown(WorkflowInspectReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Inspect Workflows");
        writer.WriteLine();
        writer.WriteLine($"Schema: `{report.SchemaVersion}`");
        writer.WriteLine($"Project: `{EscapeInlineCode(report.ProjectDirectory)}`");
        writer.WriteLine($"Workflow directory: `{EscapeInlineCode(report.WorkflowDirectory)}`");
        writer.WriteLine($"Workflows: `{report.WorkflowCount}`; runnable: `{report.RunnableCount}`; issues: `{report.IssueCount}`");
        writer.WriteLine();

        if (report.Workflows.Count == 0)
        {
            writer.WriteLine("No workflow YAML files found. Create one with `revitcli workflow init pre-issue`.");
            return writer.ToString().TrimEnd();
        }

        writer.WriteLine("| Workflow | Status | Steps | Dry-run | Mutating | Approvals | Issues | Review | Dry-run run | Receipts |");
        writer.WriteLine("|---|---|---:|---:|---:|---:|---:|---|---|---|");
        foreach (var workflow in report.Workflows)
        {
            writer.WriteLine(
                $"| {EscapeTableCell(workflow.Name)} | {EscapeTableCell(workflow.Status)} | {workflow.StepCount} | {workflow.DryRunStepCount} | {workflow.MutatingStepCount} | {workflow.ApprovalRequiredCount} | {workflow.IssueCount} | {InlineCodeCell(workflow.Commands.ReviewCommand)} | {InlineCodeCell(workflow.Commands.DryRunCommand)} | {InlineCodeCell(workflow.Commands.ReceiptsCommand)} |");
        }

        var workflowsWithIssues = report.Workflows.Where(workflow => workflow.Issues.Count > 0).ToArray();
        if (workflowsWithIssues.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Issues");
            foreach (var workflow in workflowsWithIssues)
            {
                foreach (var issue in workflow.Issues)
                {
                    writer.WriteLine(
                        $"- `{EscapeInlineCode(workflow.Name)}` `{issue.Severity.ToString().ToUpperInvariant()}` `{EscapeInlineCode(issue.Path)}`: {EscapeMarkdownText(issue.Message)}");
                }
            }
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderPlansTable(PlanInspectReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine($"RevitCli inspect plans ({report.SchemaVersion})");
        writer.WriteLine($"Project: {report.ProjectDirectory}");
        writer.WriteLine($"Plan directory: {report.PlanDirectory}");
        writer.WriteLine($"Plans: {report.PlanCount}; ready: {report.ReadyCount}; high-impact: {report.HighImpactCount}; invalid: {report.InvalidCount}; actions: {report.TotalActionCount}");
        if (report.Plans.Count == 0)
        {
            writer.WriteLine("No saved plan JSON files found. Create one with --plan-output before apply.");
            return writer.ToString().TrimEnd();
        }

        writer.WriteLine($"{"Type",-8} {"Status",-11} {"Actions",-8} {"Receipt",-7} {"Path",-36} Dry-run apply");
        writer.WriteLine(new string('-', 128));
        foreach (var plan in report.Plans)
        {
            writer.WriteLine(
                $"{plan.Type,-8} {plan.Status,-11} {plan.ActionCount,-8} {YesNo(plan.HasReceipt),-7} {TrimForTable(plan.Path, 36),-36} {plan.Commands.DryRunApplyCommand}");
        }

        writer.WriteLine("Review commands:");
        foreach (var plan in report.Plans)
            writer.WriteLine($"  {plan.Path}: {plan.Commands.ShowCommand}");

        return writer.ToString().TrimEnd();
    }

    private static string RenderPlansMarkdown(PlanInspectReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Inspect Plans");
        writer.WriteLine();
        writer.WriteLine($"Schema: `{report.SchemaVersion}`");
        writer.WriteLine($"Project: `{EscapeInlineCode(report.ProjectDirectory)}`");
        writer.WriteLine($"Plan directory: `{EscapeInlineCode(report.PlanDirectory)}`");
        writer.WriteLine($"Plans: `{report.PlanCount}`; ready: `{report.ReadyCount}`; high-impact: `{report.HighImpactCount}`; invalid: `{report.InvalidCount}`; actions: `{report.TotalActionCount}`");
        writer.WriteLine();

        if (report.Plans.Count == 0)
        {
            writer.WriteLine("No saved plan JSON files found. Create one with `--plan-output` before apply.");
            return writer.ToString().TrimEnd();
        }

        writer.WriteLine("| Plan | Type | Status | Actions | Receipt | Show | Dry-run apply | Apply | Rollback preview |");
        writer.WriteLine("|---|---|---|---:|---|---|---|---|---|");
        foreach (var plan in report.Plans)
        {
            writer.WriteLine(
                $"| `{EscapeInlineCode(plan.Path)}` | {EscapeTableCell(plan.Type)} | `{EscapeTableCell(plan.Status)}` | {plan.ActionCount} | {YesNo(plan.HasReceipt)} | {InlineCodeCell(plan.Commands.ShowCommand)} | {InlineCodeCell(plan.Commands.DryRunApplyCommand)} | {InlineCodeCell(plan.Commands.ApplyCommand)} | {InlineCodeCell(plan.Commands.RollbackPreviewCommand)} |");
        }

        var plansWithIssues = report.Plans.Where(plan => plan.Issues.Count > 0).ToArray();
        if (plansWithIssues.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Issues");
            foreach (var plan in plansWithIssues)
            {
                foreach (var issue in plan.Issues)
                    writer.WriteLine($"- `{EscapeInlineCode(plan.Path)}`: {EscapeMarkdownText(issue)}");
            }
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderCategoriesMarkdown(IReadOnlyList<CategoryInspectItem> items)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Inspect Categories");
        writer.WriteLine();
        writer.WriteLine($"- Categories: `{items.Count}`");
        writer.WriteLine();

        if (items.Count == 0)
        {
            writer.WriteLine("No elements found in common categories.");
            return writer.ToString().TrimEnd();
        }

        writer.WriteLine("| Alias | Label | Model category | Count | Sample element | Next command |");
        writer.WriteLine("|---|---|---|---:|---:|---|");
        foreach (var item in items.OrderByDescending(item => item.Count).ThenBy(item => item.Alias, StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteLine(
                $"| {EscapeTableCell(item.Alias)} | {EscapeTableCell(item.Label)} | {EscapeTableCell(item.ModelCategory ?? "-")} | {item.Count} | {EscapeTableCell(item.SampleElementId?.ToString() ?? "-")} | {InlineCodeCell(item.ParamsCommand)} |");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderParamsMarkdown(
        string category,
        int elementCount,
        IReadOnlyList<ParameterInspectItem> items)
    {
        var writer = new StringWriter();
        writer.WriteLine($"# Inspect Parameters: {EscapeMarkdownText(category)}");
        writer.WriteLine();
        writer.WriteLine($"- Elements: `{elementCount}`");
        writer.WriteLine($"- Parameters: `{items.Count}`");
        writer.WriteLine();

        if (elementCount == 0)
        {
            writer.WriteLine($"No elements found for category `{EscapeInlineCode(category)}`.");
            return writer.ToString().TrimEnd();
        }

        if (items.Count == 0)
        {
            writer.WriteLine("No parameters matched the inspect filters.");
            return writer.ToString().TrimEnd();
        }

        writer.WriteLine("| Parameter | Seen | Values | Write | Type | Samples | Dry-run probe |");
        writer.WriteLine("|---|---:|---:|---|---|---|---|");
        foreach (var item in items)
        {
            var storageTypes = item.StorageTypes.Length == 0
                ? "unknown"
                : string.Join("/", item.StorageTypes);
            var samples = item.SampleValues.Length == 0
                ? "-"
                : string.Join(" | ", item.SampleValues);
            var probe = string.IsNullOrWhiteSpace(item.DryRunProbeCommand)
                ? "-"
                : InlineCodeCell(item.DryRunProbeCommand);
            writer.WriteLine(
                $"| {EscapeTableCell(item.Name)} | {item.SeenOn} ({item.CoveragePercent:0.#}%) | {item.ValueSeenOn} ({item.ValueCoveragePercent:0.#}%) | {EscapeTableCell(item.WriteStatus)} | {EscapeTableCell(storageTypes)} | {EscapeTableCell(samples)} | {probe} |");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderSchedulesMarkdown(IReadOnlyList<ScheduleInspectItem> items)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Inspect Schedules");
        writer.WriteLine();
        writer.WriteLine($"- Schedules: `{items.Count}`");
        writer.WriteLine($"- Ready: `{items.Count(item => item.ExportReady)}`");
        writer.WriteLine($"- With issues: `{items.Count(item => item.HasIssues)}`");
        writer.WriteLine();

        if (items.Count == 0)
        {
            writer.WriteLine("No schedules matched the inspect filters.");
            return writer.ToString().TrimEnd();
        }

        writer.WriteLine("| Name | Category | Fields | Rows | Ready | Issues | CSV export | JSON export |");
        writer.WriteLine("|---|---|---:|---:|---|---|---|---|");
        foreach (var item in items)
        {
            writer.WriteLine(
                $"| {EscapeTableCell(item.Name)} | {EscapeTableCell(item.Category ?? "-")} | {item.FieldCount} | {item.RowCount} | {YesNo(item.ExportReady)} | {EscapeTableCell(item.IssueSummary)} | {InlineCodeCell(item.CsvExportCommand)} | {InlineCodeCell(item.JsonExportCommand)} |");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderSheetsMarkdown(IReadOnlyList<SheetInspectItem> items)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Inspect Sheets");
        writer.WriteLine();
        writer.WriteLine($"- Sheets: `{items.Count}`");
        writer.WriteLine($"- Ready: `{items.Count(item => item.ExportReady)}`");
        writer.WriteLine($"- With issues: `{items.Count(item => item.HasIssues)}`");
        writer.WriteLine();

        if (items.Count == 0)
        {
            writer.WriteLine("No sheets matched the inspect filters.");
            return writer.ToString().TrimEnd();
        }

        writer.WriteLine("| Number | Name | Views | Ready | Issues | Dry-run export |");
        writer.WriteLine("|---|---|---:|---|---|---|");
        foreach (var item in items)
        {
            writer.WriteLine(
                $"| {EscapeTableCell(item.Number)} | {EscapeTableCell(item.Name)} | {item.PlacedViewCount} | {YesNo(item.ExportReady)} | {EscapeTableCell(item.IssueSummary)} | {InlineCodeCell(item.DryRunExportCommand)} |");
        }

        return writer.ToString().TrimEnd();
    }

    private static ParameterStats GetParameterStats(Dictionary<string, ParameterStats> stats, string name)
    {
        if (!stats.TryGetValue(name, out var stat))
        {
            stat = new ParameterStats(name);
            stats[name] = stat;
        }

        return stat;
    }

    private static void AddSampleValue(ParameterStats stat, string value)
    {
        if (stat.SampleValues.Count < 3
            && !stat.SampleValues.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            stat.SampleValues.Add(value);
        }
    }

    private static string BuildWriteStatus(ParameterStats stat)
    {
        if (!stat.HasWriteMetadata)
            return "unknown";
        if (stat.WritableOn == stat.SeenOn)
            return "writable";
        if (stat.WritableOn > 0)
            return "mixed";
        if (stat.ReadOnlyOn == stat.SeenOn)
            return "read-only";
        return "not-writable";
    }

    private static string BuildExportCommand(string scheduleName)
    {
        return $"revitcli schedule export --name {QuoteArgument(scheduleName)} --output csv";
    }

    private static string BuildScheduleJsonExportCommand(string scheduleName)
    {
        return $"revitcli schedule export --name {QuoteArgument(scheduleName)} --output json";
    }

    private static string BuildScheduleTableExportCommand(string scheduleName)
    {
        return $"revitcli schedule export --name {QuoteArgument(scheduleName)} --output table";
    }

    private static ScheduleInspectItem BuildScheduleInspectItem(ScheduleInfo schedule)
    {
        var issues = BuildScheduleIssues(schedule).ToArray();
        var csvCommand = string.IsNullOrWhiteSpace(schedule.Name)
            ? ""
            : BuildExportCommand(schedule.Name);
        var jsonCommand = string.IsNullOrWhiteSpace(schedule.Name)
            ? ""
            : BuildScheduleJsonExportCommand(schedule.Name);
        var tableCommand = string.IsNullOrWhiteSpace(schedule.Name)
            ? ""
            : BuildScheduleTableExportCommand(schedule.Name);

        return new ScheduleInspectItem(
            schedule.Id,
            schedule.Name,
            schedule.Category,
            schedule.FieldCount,
            schedule.RowCount,
            schedule.FieldCount > 0,
            schedule.RowCount > 0,
            issues,
            csvCommand,
            csvCommand,
            jsonCommand,
            tableCommand);
    }

    private static List<ScheduleIssue> BuildScheduleIssues(ScheduleInfo schedule)
    {
        var issues = new List<ScheduleIssue>();
        if (string.IsNullOrWhiteSpace(schedule.Name))
        {
            issues.Add(new ScheduleIssue(
                "error",
                "missing-name",
                "Schedule name is blank."));
        }

        if (schedule.FieldCount <= 0)
        {
            issues.Add(new ScheduleIssue(
                "error",
                "no-fields",
                "Schedule has no fields to export."));
        }

        if (schedule.RowCount < 0)
        {
            issues.Add(new ScheduleIssue(
                "warning",
                "unknown-rows",
                "Schedule row count is unavailable."));
        }
        else if (schedule.RowCount == 0)
        {
            issues.Add(new ScheduleIssue(
                "warning",
                "empty-schedule",
                "Schedule has zero rows."));
        }

        return issues;
    }

    private static SheetInspectItem BuildSheetInspectItem(SnapshotSheet sheet)
    {
        var selector = BuildSheetSelector(sheet);
        var issues = BuildSheetIssues(sheet).ToArray();
        return new SheetInspectItem(
            sheet.ViewId,
            sheet.Number,
            sheet.Name,
            selector,
            BuildSheetMatchKey(sheet),
            sheet.PlacedViewIds.Count,
            !string.IsNullOrWhiteSpace(sheet.Number),
            sheet.PlacedViewIds.Count > 0,
            BuildSheetKeyParameters(sheet.Parameters),
            issues,
            BuildSheetExportCommand(selector, dryRun: false),
            BuildSheetExportCommand(selector, dryRun: true));
    }

    private static string BuildSheetSelector(SnapshotSheet sheet)
    {
        if (!string.IsNullOrWhiteSpace(sheet.Number))
            return sheet.Number;
        if (!string.IsNullOrWhiteSpace(sheet.Name))
            return sheet.Name;
        return "";
    }

    private static string BuildSheetMatchKey(SnapshotSheet sheet)
    {
        if (!string.IsNullOrWhiteSpace(sheet.Number) && !string.IsNullOrWhiteSpace(sheet.Name))
            return $"{sheet.Number} - {sheet.Name}";
        return BuildSheetSelector(sheet);
    }

    private static string BuildSheetExportCommand(string selector, bool dryRun)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return "";

        var command = $"revitcli export --format pdf --sheets {QuoteArgument(selector)}";
        return dryRun ? $"{command} --dry-run" : command;
    }

    private static List<SheetIssue> BuildSheetIssues(SnapshotSheet sheet)
    {
        var issues = new List<SheetIssue>();
        if (string.IsNullOrWhiteSpace(sheet.Number))
        {
            issues.Add(new SheetIssue(
                "error",
                "missing-number",
                "Sheet number is blank."));
        }

        if (string.IsNullOrWhiteSpace(sheet.Name))
        {
            issues.Add(new SheetIssue(
                "warning",
                "missing-name",
                "Sheet name is blank."));
        }

        if (sheet.PlacedViewIds.Count == 0)
        {
            issues.Add(new SheetIssue(
                "warning",
                "no-placed-views",
                "No placed views were found; run audit --rules sheets-missing-info to confirm schedule-only sheets."));
        }

        return issues;
    }

    private static Dictionary<string, string> BuildSheetKeyParameters(Dictionary<string, string> parameters)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in SheetKeyParameters)
        {
            var value = FindParameterValue(parameters, definition.Aliases);
            if (!string.IsNullOrWhiteSpace(value))
                result[definition.Key] = value;
        }

        return result;
    }

    private static string? FindParameterValue(Dictionary<string, string> parameters, string[] aliases)
    {
        if (parameters.Count == 0)
            return null;

        foreach (var alias in aliases)
        {
            if (parameters.TryGetValue(alias, out var exact))
                return exact;
        }

        var normalizedAliases = aliases
            .Select(NormalizeParameterName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in parameters)
        {
            if (normalizedAliases.Contains(NormalizeParameterName(parameter.Key)))
                return parameter.Value;
        }

        return null;
    }

    private static string NormalizeParameterName(string value)
    {
        return new string((value ?? "")
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static string[] NormalizePatterns(string[]? patterns)
    {
        return (patterns ?? Array.Empty<string>())
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Trim())
            .ToArray();
    }

    private static bool MatchesSheetPattern(SheetInspectItem item, string pattern)
    {
        if (string.Equals(pattern, "all", StringComparison.OrdinalIgnoreCase))
            return true;

        return MatchesPattern(item.Number, pattern)
               || MatchesPattern(item.Name, pattern)
               || MatchesPattern(item.MatchKey, pattern)
               || MatchesPattern(item.Selector, pattern);
    }

    private static bool MatchesOptionalPattern(string text, string? pattern)
    {
        return string.IsNullOrWhiteSpace(pattern)
            || MatchesPattern(text, pattern.Trim());
    }

    private static bool MatchesPattern(string text, string pattern)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
            return false;

        if (!pattern.Contains('*', StringComparison.Ordinal))
            return string.Equals(text, pattern, StringComparison.OrdinalIgnoreCase);

        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
    }

    private static string BuildQueryCommand(string category)
    {
        return $"revitcli query {category} --output table";
    }

    private static string BuildParamsCommand(string category)
    {
        return $"revitcli inspect params {category}";
    }

    private static string BuildDryRunProbeCommand(string category, string parameterName, long? sampleElementId)
    {
        if (sampleElementId is > 0)
            return $"revitcli set --id {sampleElementId.Value} --param {QuoteArgument(parameterName)} --value {QuoteArgument("<value>")} --dry-run";

        return $"revitcli set {category} --param {QuoteArgument(parameterName)} --value {QuoteArgument("<value>")} --dry-run";
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string NormalizePath(string value) =>
        value.Replace('\\', '/');

    private static string TrimForTable(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;
        return value[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static bool IsJson(string? outputFormat) =>
        string.Equals(outputFormat, "json", StringComparison.OrdinalIgnoreCase);

    private static bool IsMarkdown(string? outputFormat) =>
        string.Equals(outputFormat, "markdown", StringComparison.OrdinalIgnoreCase);

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string EscapeInlineCode(string? value)
    {
        return (value ?? string.Empty)
            .Replace("`", "'", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static string InlineCodeCell(string? value) =>
        $"`{EscapeTableCell(EscapeInlineCode(value))}`";

    private static string EscapeMarkdownText(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static string EscapeTableCell(string? value)
    {
        return EscapeMarkdownText(string.IsNullOrWhiteSpace(value) ? "-" : value)
            .Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static bool IsConnectionFailure(string? error)
    {
        return error != null
               && (error.Contains("not running", StringComparison.OrdinalIgnoreCase)
                   || error.Contains("Communication error", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record KnownCategory(string Alias, string Label);

    private sealed record SheetKeyParameter(string Key, string[] Aliases);

    private sealed record CategoryInspectItem(
        string Alias,
        string Label,
        string? ModelCategory,
        int Count,
        bool HasElements,
        long? SampleElementId,
        string QueryCommand,
        string ParamsCommand,
        string? Error);

    private sealed record ParameterInspectItem(
        string Name,
        int SeenOn,
        double CoveragePercent,
        int ValueSeenOn,
        double ValueCoveragePercent,
        int? WritableOn,
        double? WritableCoveragePercent,
        bool? CanWrite,
        string WriteStatus,
        string[] StorageTypes,
        string[] SampleValues,
        long? SampleElementId,
        long? MissingSampleElementId,
        string DryRunProbeCommand);

    private sealed record ScheduleInspectItem(
        long Id,
        string Name,
        string? Category,
        int FieldCount,
        int RowCount,
        bool HasFields,
        bool HasRows,
        ScheduleIssue[] Issues,
        string ExportCommand,
        string CsvExportCommand,
        string JsonExportCommand,
        string TableExportCommand)
    {
        public bool HasIssues => Issues.Length > 0;

        public bool HasBlockingIssues => Issues.Any(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));

        public bool ExportReady => !string.IsNullOrWhiteSpace(Name) && HasFields && HasRows && !HasBlockingIssues;

        public string IssueSummary => HasIssues ? string.Join(", ", Issues.Select(issue => issue.Code)) : "ok";
    }

    private sealed record ScheduleIssue(
        string Severity,
        string Code,
        string Message);

    private sealed record SheetInspectItem(
        long ViewId,
        string Number,
        string Name,
        string Selector,
        string MatchKey,
        int PlacedViewCount,
        bool HasNumber,
        bool HasPlacedViews,
        Dictionary<string, string> KeyParameters,
        SheetIssue[] Issues,
        string ExportCommand,
        string DryRunCommand)
    {
        public bool HasIssues => Issues.Length > 0;

        public bool HasBlockingIssues => Issues.Any(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));

        public bool ExportReady => HasNumber && HasPlacedViews && !HasBlockingIssues && !string.IsNullOrWhiteSpace(ExportCommand);

        public string IssueSummary => HasIssues ? string.Join(", ", Issues.Select(issue => issue.Code)) : "ok";

        public string DryRunExportCommand => DryRunCommand;
    }

    private sealed record SheetIssue(string Severity, string Code, string Message);

    private sealed record WorkflowInspectReport(
        string SchemaVersion,
        string ProjectDirectory,
        string WorkflowDirectory,
        int WorkflowCount,
        int RunnableCount,
        int IssueCount,
        IReadOnlyList<WorkflowInspectItem> Workflows);

    private sealed record WorkflowInspectItem(
        string Path,
        string Name,
        string? Description,
        bool CanRun,
        int StepCount,
        int DryRunStepCount,
        int MutatingStepCount,
        int ApprovalRequiredCount,
        int IssueCount,
        string Status,
        IReadOnlyList<WorkflowValidationIssue> Issues,
        WorkflowInspectCommands Commands);

    private sealed record WorkflowInspectCommands(
        string ValidateCommand,
        string SimulateCommand,
        string ReviewCommand,
        string DryRunCommand,
        string ApprovedRunCommand,
        string ReceiptsCommand);

    private sealed record PlanInspectReport(
        string SchemaVersion,
        string ProjectDirectory,
        string PlanDirectory,
        int PlanCount,
        int ReadyCount,
        int HighImpactCount,
        int InvalidCount,
        int TotalActionCount,
        IReadOnlyList<PlanInspectItem> Plans);

    private sealed record PlanInspectItem(
        string Path,
        string Type,
        string Status,
        int ActionCount,
        string CreatedAtUtc,
        string CreatedBy,
        string PrimaryField,
        string Summary,
        string Source,
        string Target,
        IReadOnlyList<string> Issues,
        bool HasReceipt,
        string ReceiptPath,
        PlanInspectCommands Commands);

    private sealed record PlanInspectCommands(
        string ShowCommand,
        string DryRunApplyCommand,
        string ApplyCommand,
        string RollbackPreviewCommand);

    private sealed class ParameterStats
    {
        public ParameterStats(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public int SeenOn { get; set; }
        public int ValueSeenOn { get; set; }
        public int WritableOn { get; set; }
        public int ReadOnlyOn { get; set; }
        public bool HasWriteMetadata { get; set; }
        public long? WritableSampleElementId { get; set; }
        public long? MissingWritableSampleElementId { get; set; }
        public long? MissingSampleElementId { get; set; }
        public List<string> SampleValues { get; } = new();
        public HashSet<string> StorageTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
