using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Shared;

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

    public static Command Create(RevitClient client)
    {
        var command = new Command("inspect", "Discover model data for safe terminal workflows");
        command.AddCommand(CreateCategoriesCommand(client));
        command.AddCommand(CreateParamsCommand(client));
        command.AddCommand(CreateSchedulesCommand(client));
        return command;
    }

    private static Command CreateCategoriesCommand(RevitClient client)
    {
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json");
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
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json");
        var command = new Command("params", "Inspect parameters found on elements in a category")
        {
            categoryArg,
            outputOpt
        };
        command.SetHandler(async (category, output) =>
        {
            Environment.ExitCode = await ExecuteParamsAsync(client, category, output, Console.Out);
        }, categoryArg, outputOpt);
        return command;
    }

    private static Command CreateSchedulesCommand(RevitClient client)
    {
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json");
        var command = new Command("schedules", "Inspect schedules and export commands") { outputOpt };
        command.SetHandler(async output =>
        {
            Environment.ExitCode = await ExecuteSchedulesAsync(client, output, Console.Out);
        }, outputOpt);
        return command;
    }

    public static async Task<int> ExecuteSchedulesAsync(
        RevitClient client,
        string outputFormat,
        TextWriter output)
    {
        var result = await client.ListSchedulesAsync();
        if (!result.Success)
        {
            await output.WriteLineAsync($"Error: {result.Error}");
            return 1;
        }

        var schedules = result.Data ?? Array.Empty<ScheduleInfo>();
        var items = schedules
            .OrderBy(schedule => schedule.Name, StringComparer.OrdinalIgnoreCase)
            .Select(schedule => new ScheduleInspectItem(
                schedule.Id,
                schedule.Name,
                schedule.Category,
                schedule.FieldCount,
                schedule.RowCount,
                schedule.RowCount >= 0,
                BuildExportCommand(schedule.Name)))
            .ToArray();

        if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(items, PrettyJson));
            return 0;
        }

        if (items.Length == 0)
        {
            await output.WriteLineAsync("No schedules found in the model.");
            return 0;
        }

        await output.WriteLineAsync($"{"Name",-30} {"Category",-15} {"Fields",-8} {"Rows",-8} Export command");
        await output.WriteLineAsync(new string('-', 112));

        foreach (var item in items)
        {
            await output.WriteLineAsync(
                $"{item.Name,-30} {item.Category,-15} {item.FieldCount,-8} {item.RowCount,-8} {item.ExportCommand}");
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

        if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(items, PrettyJson));
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

    public static async Task<int> ExecuteParamsAsync(
        RevitClient client,
        string category,
        string outputFormat,
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
        var items = BuildParameterItems(category, elements);

        if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(items, PrettyJson));
            return 0;
        }

        if (elements.Length == 0)
        {
            await output.WriteLineAsync($"No elements found for category '{category}'.");
            return 0;
        }

        if (items.Length == 0)
        {
            await output.WriteLineAsync($"No parameters found on {elements.Length} {category} element(s).");
            return 0;
        }

        await output.WriteLineAsync($"{"Parameter",-28} {"Seen",-8} {"Coverage",-10} {"Samples",-30} Dry-run probe");
        await output.WriteLineAsync(new string('-', 130));
        foreach (var item in items)
        {
            await output.WriteLineAsync(
                $"{TrimForTable(item.Name, 28),-28} {item.SeenOn,-8} {item.CoveragePercent,8:0.#}%  {TrimForTable(string.Join(" | ", item.SampleValues), 30),-30} {item.DryRunProbeCommand}");
        }

        return 0;
    }

    private static ParameterInspectItem[] BuildParameterItems(string category, ElementInfo[] elements)
    {
        var stats = new Dictionary<string, ParameterStats>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in elements)
        {
            foreach (var (name, value) in element.Parameters)
            {
                if (!stats.TryGetValue(name, out var stat))
                {
                    stat = new ParameterStats(name);
                    stats[name] = stat;
                }

                stat.SeenOn++;
                if (!string.IsNullOrWhiteSpace(value) && stat.SampleValues.Count < 3
                    && !stat.SampleValues.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    stat.SampleValues.Add(value);
                }
            }
        }

        var denominator = Math.Max(elements.Length, 1);
        return stats.Values
            .OrderByDescending(stat => stat.SeenOn)
            .ThenBy(stat => stat.Name, StringComparer.OrdinalIgnoreCase)
            .Select(stat => new ParameterInspectItem(
                stat.Name,
                stat.SeenOn,
                Math.Round(stat.SeenOn * 100.0 / denominator, 1),
                stat.SampleValues.ToArray(),
                BuildDryRunProbeCommand(category, stat.Name)))
            .ToArray();
    }

    private static string BuildExportCommand(string scheduleName)
    {
        return $"revitcli schedule export --name {QuoteArgument(scheduleName)} --output csv";
    }

    private static string BuildQueryCommand(string category)
    {
        return $"revitcli query {category} --output table";
    }

    private static string BuildParamsCommand(string category)
    {
        return $"revitcli inspect params {category}";
    }

    private static string BuildDryRunProbeCommand(string category, string parameterName)
    {
        return $"revitcli set {category} --param {QuoteArgument(parameterName)} --value {QuoteArgument("<value>")} --dry-run";
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string TrimForTable(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;
        return value[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static bool IsConnectionFailure(string? error)
    {
        return error != null
               && (error.Contains("not running", StringComparison.OrdinalIgnoreCase)
                   || error.Contains("Communication error", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record KnownCategory(string Alias, string Label);

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
        string[] SampleValues,
        string DryRunProbeCommand);

    private sealed record ScheduleInspectItem(
        long Id,
        string Name,
        string? Category,
        int FieldCount,
        int RowCount,
        bool ExportReady,
        string ExportCommand);

    private sealed class ParameterStats
    {
        public ParameterStats(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public int SeenOn { get; set; }
        public List<string> SampleValues { get; } = new();
    }
}
