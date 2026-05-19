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
        var dryRunOpt = new Option<bool>("--dry-run", "Preview the schedule create request without writing to Revit");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        var receiptDirOpt = new Option<string?>(
            "--receipt-dir",
            () => Path.Combine(".revitcli", "receipts"),
            "Directory for schedule-create receipts after real writes");

        var cmd = new Command("create", "Create a new ViewSchedule in the Revit model")
        {
            categoryOpt, fieldsOpt, filterOpt, sortOpt, sortDescOpt, nameOpt, placeOpt,
            templateOpt, dryRunOpt, outputOpt, receiptDirOpt
        };

        cmd.SetHandler(async ctx =>
        {
            var category = ctx.ParseResult.GetValueForOption(categoryOpt);
            var fields = ctx.ParseResult.GetValueForOption(fieldsOpt);
            var filter = ctx.ParseResult.GetValueForOption(filterOpt);
            var sort = ctx.ParseResult.GetValueForOption(sortOpt);
            var sortDesc = ctx.ParseResult.GetValueForOption(sortDescOpt);
            var name = ctx.ParseResult.GetValueForOption(nameOpt);
            var placeOnSheet = ctx.ParseResult.GetValueForOption(placeOpt);
            var template = ctx.ParseResult.GetValueForOption(templateOpt);
            var dryRun = ctx.ParseResult.GetValueForOption(dryRunOpt);
            var outputFormat = ctx.ParseResult.GetValueForOption(outputOpt)!;
            var receiptDir = ctx.ParseResult.GetValueForOption(receiptDirOpt);
            Environment.ExitCode = await ExecuteCreateAsync(
                client, category, fields, filter, sort, sortDesc, name, placeOnSheet, template,
                dryRun, outputFormat, receiptDir, Console.Out);
        });

        return cmd;
    }

    public static async Task<int> ExecuteListAsync(RevitClient client, string outputFormat, TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "markdown"))
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
                    TerminalJsonOptions.Pretty));
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
                await output.WriteLineAsync(JsonSerializer.Serialize(Array.Empty<ScheduleInfo>(), TerminalJsonOptions.Pretty));
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
            await output.WriteLineAsync(JsonSerializer.Serialize(schedules, TerminalJsonOptions.Pretty));
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

    public static async Task<int> ExecuteExportAsync(
        RevitClient client, string? category, string? existingName,
        string? fields, string? filter, string? sort, bool sortDesc,
        string outputFormat, string? templateName, TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "csv", "markdown"))
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

    public static async Task<int> ExecuteCreateAsync(
        RevitClient client, string? category, string? fields,
        string? filter, string? sort, bool sortDesc,
        string? name, string? placeOnSheet, string? templateName, TextWriter output)
    {
        return await ExecuteCreateAsync(
            client, category, fields, filter, sort, sortDesc, name, placeOnSheet, templateName,
            dryRun: false, outputFormat: "table", receiptDir: null, output);
    }

    public static async Task<int> ExecuteCreateAsync(
        RevitClient client, string? category, string? fields,
        string? filter, string? sort, bool sortDesc,
        string? name, string? placeOnSheet, string? templateName,
        bool dryRun, string outputFormat, string? receiptDir, TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        var request = new ScheduleCreateRequest { SortDescending = sortDesc };

        if (templateName != null)
        {
            var template = LoadTemplate(templateName);
            if (template == null)
            {
                await WriteScheduleCreateErrorAsync(
                    output,
                    normalizedOutput,
                    $"schedule template '{templateName}' not found in .revitcli.yml.",
                    dryRun,
                    willWrite: !dryRun);
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
            await WriteScheduleCreateErrorAsync(
                output,
                normalizedOutput,
                "--category is required (or use --template).",
                dryRun,
                willWrite: !dryRun);
            return 1;
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            await WriteScheduleCreateErrorAsync(
                output,
                normalizedOutput,
                "--name is required (or use --template with a name defined).",
                dryRun,
                willWrite: !dryRun);
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(request.Filter))
        {
            await WriteScheduleCreateErrorAsync(
                output,
                normalizedOutput,
                "--filter on schedule create is not supported. Use schedule export --filter instead.",
                dryRun,
                willWrite: !dryRun);
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(request.Sort) && !CreateFieldsIncludeSort(request.Fields, request.Sort))
        {
            await WriteScheduleCreateErrorAsync(
                output,
                normalizedOutput,
                "--sort field must be included in --fields, or use --fields all.",
                dryRun,
                willWrite: !dryRun);
            return 1;
        }

        if (dryRun)
        {
            var preview = CreateScheduleCreateOutput(
                request,
                dryRun: true,
                receiptRequired: false,
                receiptPath: null,
                result: null);
            await output.WriteLineAsync(FormatScheduleCreateOutput(preview, normalizedOutput));
            return 0;
        }

        var result = await client.CreateScheduleAsync(request);
        if (!result.Success)
        {
            await WriteScheduleCreateErrorAsync(
                output,
                normalizedOutput,
                result.Error ?? "Unknown error",
                dryRun: false,
                willWrite: true);
            return 1;
        }

        var r = result.Data!;
        var savedReceiptPath = string.IsNullOrWhiteSpace(receiptDir)
            ? null
            : TrySaveScheduleCreateReceipt(request, r, receiptDir);
        var createOutput = CreateScheduleCreateOutput(
            request,
            dryRun: false,
            receiptRequired: !string.IsNullOrWhiteSpace(receiptDir),
            savedReceiptPath,
            r);
        await output.WriteLineAsync(FormatScheduleCreateOutput(createOutput, normalizedOutput));

        return 0;
    }


    private static bool CreateFieldsIncludeSort(IReadOnlyList<string>? fields, string sort)
    {
        if (fields is not { Count: > 0 })
            return false;
        if (fields.Count == 1 && string.Equals(fields[0], "all", StringComparison.OrdinalIgnoreCase))
            return true;
        return fields.Any(field => string.Equals(field, sort, StringComparison.OrdinalIgnoreCase));
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
            "json" => JsonSerializer.Serialize(data, TerminalJsonOptions.Pretty),
            "csv" => FormatCsv(data),
            "markdown" => RenderScheduleExportMarkdown(data),
            _ => data.Rows.Count == 0 ? "No data." : FormatTable(data),
        };
    }

    private static ScheduleCreateOutput CreateScheduleCreateOutput(
        ScheduleCreateRequest request,
        bool dryRun,
        bool receiptRequired,
        string? receiptPath,
        ScheduleCreateResult? result)
    {
        var fields = request.Fields?.ToArray() ?? Array.Empty<string>();
        var warnings = new List<string>();
        if (fields.Length == 0)
            warnings.Add("No fields were specified; Revit will create the schedule with zero requested fields.");
        if (!string.IsNullOrWhiteSpace(request.PlaceOnSheet))
            warnings.Add("Sheet placement is resolved during the real create after the schedule view is created.");
        if (receiptRequired && receiptPath == null)
            warnings.Add("Schedule was created but the receipt could not be saved; review stderr for the receipt write error.");

        return new ScheduleCreateOutput(
            "schedule-create.v1",
            Success: true,
            DryRun: dryRun,
            WillWrite: !dryRun,
            ReceiptRequired: receiptRequired,
            ReceiptSaved: receiptPath != null,
            request.Category,
            request.Name,
            fields,
            request.Filter,
            request.Sort,
            request.SortDescending,
            request.PlaceOnSheet,
            BuildScheduleCreateCommand(request, includeDryRun: false),
            receiptPath,
            result,
            warnings);
    }

    private static string FormatScheduleCreateOutput(ScheduleCreateOutput output, string format)
    {
        return format.ToLowerInvariant() switch
        {
            "json" => JsonSerializer.Serialize(output, TerminalJsonOptions.Pretty),
            "markdown" => RenderScheduleCreateMarkdown(output),
            _ => RenderScheduleCreateTable(output)
        };
    }

    private static async Task WriteScheduleCreateErrorAsync(
        TextWriter output,
        string format,
        string message,
        bool dryRun,
        bool willWrite)
    {
        if (format == "json")
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(
                new ScheduleCreateErrorOutput(
                    "schedule-create.v1",
                    Success: false,
                    DryRun: dryRun,
                    WillWrite: willWrite,
                    message),
                TerminalJsonOptions.Pretty));
            return;
        }

        if (format == "markdown")
        {
            await output.WriteLineAsync(RenderScheduleCreateErrorMarkdown(message, dryRun, willWrite));
            return;
        }

        await output.WriteLineAsync($"Error: {message}");
    }

    private static string RenderScheduleCreateTable(ScheduleCreateOutput output)
    {
        var writer = new StringWriter();
        if (output.DryRun)
        {
            writer.WriteLine($"Schedule create dry-run ({output.SchemaVersion})");
            writer.WriteLine($"Name: {output.Name}");
            writer.WriteLine($"Category: {output.Category}");
            writer.WriteLine($"Fields: {FormatFieldList(output.Fields)}");
            writer.WriteLine($"Sort: {FormatNullable(output.Sort)}{(output.SortDescending ? " desc" : "")}");
            writer.WriteLine($"Place on sheet: {FormatNullable(output.PlaceOnSheet)}");
            writer.WriteLine("Writes: no");
            writer.WriteLine($"Approval command: {output.ApprovalCommand}");
        }
        else
        {
            var result = output.Result!;
            writer.WriteLine($"Schedule '{result.Name}' created (ViewId: {result.ViewId}, {result.FieldCount} fields, {result.RowCount} rows).");
            if (result.PlacedOnSheet != null)
                writer.WriteLine($"Placed on sheet: {result.PlacedOnSheet}");
            if (output.ReceiptPath != null)
                writer.WriteLine($"Receipt saved to {output.ReceiptPath}");
        }

        foreach (var warning in output.Warnings)
            writer.WriteLine($"Warning: {warning}");

        return writer.ToString().TrimEnd();
    }

    private static string RenderScheduleCreateMarkdown(ScheduleCreateOutput output)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Schedule Create");
        writer.WriteLine();
        writer.WriteLine($"- Schema: `{output.SchemaVersion}`");
        writer.WriteLine($"- Dry-run: `{output.DryRun.ToString().ToLowerInvariant()}`");
        writer.WriteLine($"- Will write: `{output.WillWrite.ToString().ToLowerInvariant()}`");
        writer.WriteLine($"- Receipt required: `{output.ReceiptRequired.ToString().ToLowerInvariant()}`");
        writer.WriteLine($"- Receipt saved: `{output.ReceiptSaved.ToString().ToLowerInvariant()}`");
        writer.WriteLine($"- Name: `{EscapeMarkdownText(output.Name)}`");
        writer.WriteLine($"- Category: `{EscapeMarkdownText(output.Category)}`");
        writer.WriteLine($"- Fields: `{EscapeMarkdownText(FormatFieldList(output.Fields))}`");
        writer.WriteLine($"- Sort: `{EscapeMarkdownText(FormatNullable(output.Sort))}{(output.SortDescending ? " desc" : "")}`");
        writer.WriteLine($"- Place on sheet: `{EscapeMarkdownText(FormatNullable(output.PlaceOnSheet))}`");
        if (output.ReceiptPath != null)
            writer.WriteLine($"- Receipt: `{EscapeMarkdownText(output.ReceiptPath)}`");
        writer.WriteLine($"- Approval command: `{EscapeMarkdownText(output.ApprovalCommand)}`");

        if (output.Result != null)
        {
            writer.WriteLine();
            writer.WriteLine("| ViewId | Field count | Row count | Placed on sheet |");
            writer.WriteLine("|---:|---:|---:|---|");
            writer.WriteLine(
                $"| {output.Result.ViewId} | {output.Result.FieldCount} | {output.Result.RowCount} | {EscapeTableCell(output.Result.PlacedOnSheet)} |");
        }

        if (output.Warnings.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Warnings");
            foreach (var warning in output.Warnings)
                writer.WriteLine($"- {EscapeMarkdownText(warning)}");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderScheduleCreateErrorMarkdown(string message, bool dryRun, bool willWrite)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Schedule Create");
        writer.WriteLine();
        writer.WriteLine("- Schema: `schedule-create.v1`");
        writer.WriteLine("- Status: `FAIL`");
        writer.WriteLine($"- Dry-run: `{dryRun.ToString().ToLowerInvariant()}`");
        writer.WriteLine($"- Will write: `{willWrite.ToString().ToLowerInvariant()}`");
        writer.WriteLine($"- Error: {EscapeMarkdownText(message)}");
        return writer.ToString().TrimEnd();
    }

    private static string? TrySaveScheduleCreateReceipt(
        ScheduleCreateRequest request,
        ScheduleCreateResult result,
        string receiptDir)
    {
        try
        {
            var fullDir = Path.GetFullPath(receiptDir);
            Directory.CreateDirectory(fullDir);
            var path = Path.Combine(fullDir, $"schedule-create-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            var receipt = new ScheduleCreateReceipt(
                "schedule-create-receipt.v1",
                DateTimeOffset.UtcNow,
                BuildScheduleCreateCommand(request, includeDryRun: false),
                request.Category,
                request.Name,
                request.Fields?.ToArray() ?? Array.Empty<string>(),
                request.Sort,
                request.SortDescending,
                request.PlaceOnSheet,
                result);
            File.WriteAllText(path, JsonSerializer.Serialize(receipt, TerminalJsonOptions.Pretty));
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Console.Error.WriteLine($"[RevitCli] Schedule create receipt write failed: {ex.Message}");
            return null;
        }
    }

    private static string BuildScheduleCreateCommand(ScheduleCreateRequest request, bool includeDryRun)
    {
        var parts = new List<string>
        {
            "revitcli",
            "schedule",
            "create",
            "--category",
            QuoteArg(request.Category),
            "--name",
            QuoteArg(request.Name)
        };
        if (request.Fields is { Count: > 0 })
        {
            parts.Add("--fields");
            parts.Add(QuoteArg(string.Join(",", request.Fields)));
        }

        if (!string.IsNullOrWhiteSpace(request.Sort))
        {
            parts.Add("--sort");
            parts.Add(QuoteArg(request.Sort));
        }

        if (request.SortDescending)
            parts.Add("--sort-desc");

        if (!string.IsNullOrWhiteSpace(request.PlaceOnSheet))
        {
            parts.Add("--place-on-sheet");
            parts.Add(QuoteArg(request.PlaceOnSheet));
        }

        if (includeDryRun)
            parts.Add("--dry-run");

        parts.Add("--output");
        parts.Add("json");
        return string.Join(" ", parts);
    }

    private static string QuoteArg(string value) =>
        value.Any(char.IsWhiteSpace) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;

    private static string FormatFieldList(IReadOnlyList<string> fields) =>
        fields.Count == 0 ? "(none)" : string.Join(", ", fields);

    private static string FormatNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(none)" : value;

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

    private sealed record ScheduleCreateOutput(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("dryRun")] bool DryRun,
        [property: JsonPropertyName("willWrite")] bool WillWrite,
        [property: JsonPropertyName("receiptRequired")] bool ReceiptRequired,
        [property: JsonPropertyName("receiptSaved")] bool ReceiptSaved,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("fields")] IReadOnlyList<string> Fields,
        [property: JsonPropertyName("filter")] string? Filter,
        [property: JsonPropertyName("sort")] string? Sort,
        [property: JsonPropertyName("sortDescending")] bool SortDescending,
        [property: JsonPropertyName("placeOnSheet")] string? PlaceOnSheet,
        [property: JsonPropertyName("approvalCommand")] string ApprovalCommand,
        [property: JsonPropertyName("receiptPath")] string? ReceiptPath,
        [property: JsonPropertyName("result")] ScheduleCreateResult? Result,
        [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);

    private sealed record ScheduleCreateReceipt(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("timestampUtc")] DateTimeOffset TimestampUtc,
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("fields")] IReadOnlyList<string> Fields,
        [property: JsonPropertyName("sort")] string? Sort,
        [property: JsonPropertyName("sortDescending")] bool SortDescending,
        [property: JsonPropertyName("placeOnSheet")] string? PlaceOnSheet,
        [property: JsonPropertyName("result")] ScheduleCreateResult Result);

    private sealed record ScheduleCreateErrorOutput(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("dryRun")] bool DryRun,
        [property: JsonPropertyName("willWrite")] bool WillWrite,
        [property: JsonPropertyName("error")] string Error);

    private sealed record ScheduleListErrorOutput(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error")] string Error);
}
