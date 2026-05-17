using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Output;
using RevitCli.Profile;
using RevitCli.Shared;

namespace RevitCli.Commands;

public static class ScheduleCommand
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    public static Command Create(RevitClient client)
    {
        var command = new Command("schedule", "Manage and export Revit schedules");
        command.AddCommand(CreateListCommand(client));
        command.AddCommand(CreateExportCommand(client));
        command.AddCommand(CreateCreateCommand(client));
        return command;
    }

    private static Command CreateListCommand(RevitClient client)
    {
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        var cmd = new Command("list", "List existing schedules in the Revit model") { outputOpt };
        cmd.SetHandler(async (output) =>
        {
            Environment.ExitCode = await ExecuteListAsync(client, output, Console.Out);
        }, outputOpt);
        return cmd;
    }

    private static Command CreateExportCommand(RevitClient client)
    {
        var categoryOpt = new Option<string?>("--category", "Element category (Doors, Walls, Rooms, etc.)");
        var nameOpt = new Option<string?>("--name", "Name of existing schedule to export");
        var fieldsOpt = new Option<string?>("--fields", "Comma-separated field names, or 'all'");
        var filterOpt = new Option<string?>("--filter", "Filter expression");
        var sortOpt = new Option<string?>("--sort", "Sort by field name");
        var sortDescOpt = new Option<bool>("--sort-desc", () => false, "Sort descending");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, csv, markdown");
        var templateOpt = new Option<string?>("--template", "Schedule template name from .revitcli.yml");

        var cmd = new Command("export", "Export schedule data from the Revit model")
        {
            categoryOpt, nameOpt, fieldsOpt, filterOpt, sortOpt, sortDescOpt, outputOpt, templateOpt
        };

        cmd.SetHandler(async (category, name, fields, filter, sort, sortDesc, output, template) =>
        {
            Environment.ExitCode = await ExecuteExportAsync(
                client, category, name, fields, filter, sort, sortDesc, output, template, Console.Out);
        }, categoryOpt, nameOpt, fieldsOpt, filterOpt, sortOpt, sortDescOpt, outputOpt, templateOpt);

        return cmd;
    }

    private static Command CreateCreateCommand(RevitClient client)
    {
        var categoryOpt = new Option<string?>("--category", "Element category (Doors, Walls, Rooms, etc.)");
        var fieldsOpt = new Option<string?>("--fields", "Comma-separated field names, or 'all'");
        var filterOpt = new Option<string?>("--filter", "Filter expression");
        var sortOpt = new Option<string?>("--sort", "Sort by field name");
        var sortDescOpt = new Option<bool>("--sort-desc", () => false, "Sort descending");
        var nameOpt = new Option<string?>("--name", "Name for the new ViewSchedule");
        var placeOpt = new Option<string?>("--place-on-sheet", "Sheet pattern to place schedule on");
        var templateOpt = new Option<string?>("--template", "Schedule template name from .revitcli.yml");

        var cmd = new Command("create", "Create a new ViewSchedule in the Revit model")
        {
            categoryOpt, fieldsOpt, filterOpt, sortOpt, sortDescOpt, nameOpt, placeOpt, templateOpt
        };

        cmd.SetHandler(async (category, fields, filter, sort, sortDesc, name, placeOnSheet, template) =>
        {
            Environment.ExitCode = await ExecuteCreateAsync(
                client, category, fields, filter, sort, sortDesc, name, placeOnSheet, template, Console.Out);
        }, categoryOpt, fieldsOpt, filterOpt, sortOpt, sortDescOpt, nameOpt, placeOpt, templateOpt);

        return cmd;
    }

    public static async Task<int> ExecuteListAsync(RevitClient client, string outputFormat, TextWriter output)
    {
        if (!TryNormalizeListOutput(outputFormat, out var normalizedOutput))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        var result = await client.ListSchedulesAsync();
        if (!result.Success)
        {
            if (normalizedOutput == "json")
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(
                    new ScheduleListErrorOutput(false, result.Error ?? "Unknown error"),
                    PrettyJson));
                return 1;
            }

            var message = result.Error ?? "Unknown error";
            if (normalizedOutput == "markdown")
                await output.WriteLineAsync(RenderScheduleListErrorMarkdown(message));
            else
                await output.WriteLineAsync($"Error: {message}");
            return 1;
        }

        var schedules = result.Data!;
        if (schedules.Length == 0)
        {
            if (normalizedOutput == "json")
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(Array.Empty<ScheduleInfo>(), PrettyJson));
                return 0;
            }

            if (normalizedOutput == "markdown")
                await output.WriteLineAsync(RenderScheduleListMarkdown(schedules));
            else
                await output.WriteLineAsync("No schedules found in the model.");
            return 0;
        }

        if (normalizedOutput == "json")
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(schedules, PrettyJson));
        }
        else if (normalizedOutput == "markdown")
        {
            await output.WriteLineAsync(RenderScheduleListMarkdown(schedules));
        }
        else
        {
            await output.WriteLineAsync($"{"Name",-30} {"Category",-15} {"Fields",-8} {"Rows",-8} {"Id",-10}");
            await output.WriteLineAsync(new string('-', 71));
            foreach (var s in schedules)
                await output.WriteLineAsync($"{s.Name,-30} {s.Category,-15} {s.FieldCount,-8} {s.RowCount,-8} {s.Id,-10}");
        }

        return 0;
    }

    private static bool TryNormalizeListOutput(string? outputFormat, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(outputFormat)
            ? "table"
            : outputFormat.Trim().ToLowerInvariant();

        return normalized is "table" or "json" or "markdown";
    }

    public static async Task<int> ExecuteExportAsync(
        RevitClient client, string? category, string? existingName,
        string? fields, string? filter, string? sort, bool sortDesc,
        string outputFormat, string? templateName, TextWriter output)
    {
        if (!TryNormalizeExportOutput(outputFormat, out var normalizedOutput))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', 'csv', or 'markdown'.");
            return 1;
        }

        var request = new ScheduleExportRequest { SortDescending = sortDesc };

        if (templateName != null)
        {
            var template = LoadTemplate(templateName);
            if (template == null)
            {
                await output.WriteLineAsync($"Error: schedule template '{templateName}' not found in .revitcli.yml.");
                return 1;
            }
            request.Category = existingName != null ? category : (category ?? template.Category);
            request.Fields = ParseFields(fields) ?? template.Fields;
            request.Filter = filter ?? template.Filter;
            request.Sort = sort ?? template.Sort;
            request.SortDescending = sortDesc || template.SortDescending;
            request.ExistingName = existingName;
        }
        else
        {
            request.Category = category;
            request.Fields = ParseFields(fields);
            request.Filter = filter;
            request.Sort = sort;
            request.ExistingName = existingName;
        }

        if (request.Category == null && request.ExistingName == null)
        {
            await output.WriteLineAsync("Error: provide --category or --name (or --template).");
            return 1;
        }

        if (request.Category != null && request.ExistingName != null)
        {
            await output.WriteLineAsync("Error: --category and --name are mutually exclusive.");
            return 1;
        }

        var result = await client.ExportScheduleAsync(request);
        if (!result.Success)
        {
            await output.WriteLineAsync($"Error: {result.Error}");
            return 1;
        }

        var data = result.Data!;
        await output.WriteLineAsync(FormatScheduleData(data, normalizedOutput));

        if (data.TotalRows > data.Rows.Count)
            await output.WriteLineAsync($"Warning: showing {data.Rows.Count} of {data.TotalRows} total rows (truncated).");

        return 0;
    }

    private static bool TryNormalizeExportOutput(string? outputFormat, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(outputFormat)
            ? "table"
            : outputFormat.Trim().ToLowerInvariant();

        return normalized is "table" or "json" or "csv" or "markdown";
    }

    public static async Task<int> ExecuteCreateAsync(
        RevitClient client, string? category, string? fields,
        string? filter, string? sort, bool sortDesc,
        string? name, string? placeOnSheet, string? templateName, TextWriter output)
    {
        var request = new ScheduleCreateRequest { SortDescending = sortDesc };

        if (templateName != null)
        {
            var template = LoadTemplate(templateName);
            if (template == null)
            {
                await output.WriteLineAsync($"Error: schedule template '{templateName}' not found in .revitcli.yml.");
                return 1;
            }
            request.Category = category ?? template.Category;
            request.Fields = ParseFields(fields) ?? template.Fields;
            request.Filter = filter ?? template.Filter;
            request.Sort = sort ?? template.Sort;
            request.SortDescending = sortDesc || template.SortDescending;
            request.Name = name ?? template.Name ?? templateName;
            request.PlaceOnSheet = placeOnSheet;
        }
        else
        {
            request.Category = category ?? "";
            request.Fields = ParseFields(fields);
            request.Filter = filter;
            request.Sort = sort;
            request.Name = name ?? "";
            request.PlaceOnSheet = placeOnSheet;
        }

        if (string.IsNullOrWhiteSpace(request.Category))
        {
            await output.WriteLineAsync("Error: --category is required (or use --template).");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            await output.WriteLineAsync("Error: --name is required (or use --template with a name defined).");
            return 1;
        }

        var result = await client.CreateScheduleAsync(request);
        if (!result.Success)
        {
            await output.WriteLineAsync($"Error: {result.Error}");
            return 1;
        }

        var r = result.Data!;
        await output.WriteLineAsync($"Schedule '{r.Name}' created (ViewId: {r.ViewId}, {r.FieldCount} fields, {r.RowCount} rows).");
        if (r.PlacedOnSheet != null)
            await output.WriteLineAsync($"Placed on sheet: {r.PlacedOnSheet}");

        return 0;
    }

    private static List<string>? ParseFields(string? fields)
    {
        if (string.IsNullOrWhiteSpace(fields))
            return null;
        if (fields.Equals("all", StringComparison.OrdinalIgnoreCase))
            return new List<string> { "all" };
        return fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static ScheduleTemplate? LoadTemplate(string name)
    {
        var profile = ProfileLoader.DiscoverAndLoad();
        if (profile == null)
            return null;
        return profile.Schedules.TryGetValue(name, out var template) ? template : null;
    }

    private static string FormatScheduleData(ScheduleData data, string format)
    {
        return format.ToLowerInvariant() switch
        {
            "json" => JsonSerializer.Serialize(data, PrettyJson),
            "csv" => FormatCsv(data),
            "markdown" => RenderScheduleExportMarkdown(data),
            _ => data.Rows.Count == 0 ? "No data." : FormatTable(data),
        };
    }

    private static string RenderScheduleListMarkdown(IReadOnlyList<ScheduleInfo> schedules)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Schedule List");
        writer.WriteLine();
        writer.WriteLine($"- Schedules: `{schedules.Count}`");
        writer.WriteLine($"- Export-ready: `{schedules.Count(schedule => schedule.FieldCount > 0 && schedule.RowCount > 0)}`");
        writer.WriteLine($"- Empty: `{schedules.Count(schedule => schedule.RowCount == 0)}`");
        writer.WriteLine();

        if (schedules.Count == 0)
        {
            writer.WriteLine("No schedules found in the model.");
            return writer.ToString().TrimEnd();
        }

        writer.WriteLine("| Name | Category | Fields | Rows | Id |");
        writer.WriteLine("|---|---|---:|---:|---:|");
        foreach (var schedule in schedules)
        {
            writer.WriteLine(
                $"| {EscapeTableCell(schedule.Name)} | {EscapeTableCell(schedule.Category)} | {schedule.FieldCount} | {schedule.RowCount} | {schedule.Id} |");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderScheduleListErrorMarkdown(string message)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Schedule List");
        writer.WriteLine();
        writer.WriteLine("- Status: `FAIL`");
        writer.WriteLine($"- Error: {EscapeMarkdownText(message)}");
        return writer.ToString().TrimEnd();
    }

    private static string RenderScheduleExportMarkdown(ScheduleData data)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Schedule Export");
        writer.WriteLine();
        writer.WriteLine($"- Columns: `{data.Columns.Count}`");
        writer.WriteLine($"- Rows shown: `{data.Rows.Count}`");
        writer.WriteLine($"- Total rows: `{data.TotalRows}`");
        writer.WriteLine();

        if (data.Rows.Count == 0)
        {
            writer.WriteLine("No data.");
            return writer.ToString().TrimEnd();
        }

        writer.WriteLine($"| {string.Join(" | ", data.Columns.Select(EscapeTableCell))} |");
        writer.WriteLine($"| {string.Join(" | ", data.Columns.Select(_ => "---"))} |");
        foreach (var row in data.Rows)
        {
            var values = data.Columns.Select(column => row.TryGetValue(column, out var value) ? value : "");
            writer.WriteLine($"| {string.Join(" | ", values.Select(EscapeTableCell))} |");
        }

        return writer.ToString().TrimEnd();
    }

    private static string FormatCsv(ScheduleData data)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(",", data.Columns.Select(EscapeCsvField)));
        foreach (var row in data.Rows)
        {
            var values = data.Columns.Select(c => row.TryGetValue(c, out var v) ? v : "");
            sb.AppendLine(string.Join(",", values.Select(EscapeCsvField)));
        }
        return sb.ToString().TrimEnd();
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }

    private static string FormatTable(ScheduleData data)
    {
        var sb = new System.Text.StringBuilder();
        var widths = data.Columns.Select(c =>
            Math.Max(c.Length, data.Rows.Count > 0 ? data.Rows.Max(r => r.TryGetValue(c, out var v) ? v.Length : 0) : 0)
        ).ToArray();

        for (int i = 0; i < data.Columns.Count; i++)
            sb.Append(data.Columns[i].PadRight(widths[i] + 2));
        sb.AppendLine();
        sb.AppendLine(new string('-', widths.Sum() + widths.Length * 2));

        foreach (var row in data.Rows)
        {
            for (int i = 0; i < data.Columns.Count; i++)
            {
                var val = row.TryGetValue(data.Columns[i], out var v) ? v : "";
                sb.Append(val.PadRight(widths[i] + 2));
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

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

    private sealed record ScheduleListErrorOutput(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error")] string Error);
}
