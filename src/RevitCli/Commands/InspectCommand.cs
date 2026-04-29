using System;
using System.CommandLine;
using System.IO;
using System.Linq;
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
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create(RevitClient client)
    {
        var command = new Command("inspect", "Discover model data for safe terminal workflows");
        command.AddCommand(CreateSchedulesCommand(client));
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

    private static string BuildExportCommand(string scheduleName)
    {
        return $"revitcli schedule export --name {QuoteArgument(scheduleName)} --output csv";
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private sealed record ScheduleInspectItem(
        long Id,
        string Name,
        string? Category,
        int FieldCount,
        int RowCount,
        bool ExportReady,
        string ExportCommand);
}
