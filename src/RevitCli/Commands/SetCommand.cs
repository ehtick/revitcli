using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Output;
using RevitCli.Plans;
using RevitCli.Shared;
using Spectre.Console;

namespace RevitCli.Commands;

public static class SetCommand
{
    public static Command Create(RevitClient client)
    {
        var categoryArg = new Argument<string?>("category", () => null, "Element category (e.g. doors, walls)");
        var filterOpt = new Option<string?>("--filter", "Filter expression (e.g. \"height > 3000\")");
        var idOpt = new Option<long?>("--id", "Target a specific element by ID");
        var paramOpt = new Option<string>("--param", "Parameter name to modify") { IsRequired = true };
        var valueOpt = new Option<string?>("--value", "New parameter value");
        var clearValueOpt = new Option<bool>("--clear-value", "Set the parameter value to an empty string");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without applying");
        var yesOpt = new Option<bool>("--yes", "Approve and apply the parameter change. Required for non-dry-run writes.");
        var planOutputOpt = new Option<string?>("--plan-output", "Write a saved set plan JSON file without applying");
        var stdinOpt = new Option<bool>("--stdin", "Read element IDs from stdin (JSON array or query output)");
        var idsFromOpt = new Option<string?>("--ids-from", "Read element IDs from a JSON file");

        var command = new Command("set", "Modify element parameters in the Revit model")
        {
            categoryArg, filterOpt, idOpt, paramOpt, valueOpt, clearValueOpt, dryRunOpt, yesOpt, planOutputOpt, stdinOpt, idsFromOpt
        };

        command.SetHandler(async ctx =>
        {
            var category = ctx.ParseResult.GetValueForArgument(categoryArg);
            var filter = ctx.ParseResult.GetValueForOption(filterOpt);
            var id = ctx.ParseResult.GetValueForOption(idOpt);
            var param = ctx.ParseResult.GetValueForOption(paramOpt)!;
            var value = ctx.ParseResult.GetValueForOption(valueOpt);
            var clearValue = ctx.ParseResult.GetValueForOption(clearValueOpt);
            var dryRun = ctx.ParseResult.GetValueForOption(dryRunOpt);
            var yes = ctx.ParseResult.GetValueForOption(yesOpt);
            var planOutput = ctx.ParseResult.GetValueForOption(planOutputOpt);
            var fromStdin = ctx.ParseResult.GetValueForOption(stdinOpt);
            var idsFromFile = ctx.ParseResult.GetValueForOption(idsFromOpt);

            if (!ConsoleHelper.IsInteractive || !string.IsNullOrWhiteSpace(planOutput))
            {
                Environment.ExitCode = await ExecuteAsync(
                    client,
                    category,
                    filter,
                    id,
                    param,
                    value,
                    dryRun,
                    yes,
                    fromStdin,
                    idsFromFile,
                    Console.Out,
                    planOutput,
                    clearValue);
                return;
            }

            if (string.IsNullOrEmpty(param))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] --param is required.");
                Environment.ExitCode = 1;
                return;
            }

            if (!TryResolveValue(value, clearValue, out var resolvedValue, out var valueError))
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(valueError)}");
                Environment.ExitCode = 1;
                return;
            }

            var hasIdSource = fromStdin || !string.IsNullOrEmpty(idsFromFile);
            if (category == null && !id.HasValue && !hasIdSource)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] provide a category, --id, --stdin, or --ids-from to target elements.");
                Environment.ExitCode = 1;
                return;
            }

            if (!dryRun && !yes)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] use --yes to apply a set operation. Use --dry-run or --plan-output to preview first.");
                Environment.ExitCode = 1;
                return;
            }

            var request = new SetRequest
            {
                Category = category,
                ElementId = id,
                Filter = filter,
                Param = param,
                Value = resolvedValue,
                DryRun = dryRun
            };

            if (!string.IsNullOrEmpty(idsFromFile))
                request.ElementIds = ReadIdsFromFile(idsFromFile);
            else if (fromStdin)
                request.ElementIds = ReadIdsFromStdin();

            var result = await client.SetParameterAsync(request);

            if (!result.Success)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(result.Error ?? "Unknown error")}");
                Environment.ExitCode = 1;
                return;
            }

            var data = result.Data!;

            if (dryRun)
            {
                AnsiConsole.MarkupLine($"[yellow]Dry run:[/] {data.Affected} element(s) would be modified.");
                if (data.Preview.Count > 0)
                {
                    var previewTable = new Table().Border(TableBorder.Rounded);
                    previewTable.AddColumn("[bold]Id[/]");
                    previewTable.AddColumn("[bold]Name[/]");
                    previewTable.AddColumn("[bold]Old Value[/]");
                    previewTable.AddColumn("[bold]New Value[/]");
                    foreach (var item in data.Preview)
                        previewTable.AddRow(
                            item.Id.ToString(),
                            Markup.Escape(item.Name),
                            $"[red]{Markup.Escape(item.OldValue ?? "")}[/]",
                            $"[green]{Markup.Escape(item.NewValue)}[/]");
                    AnsiConsole.Write(previewTable);
                }
                return;
            }

            AnsiConsole.MarkupLine($"Modified [green]{data.Affected}[/] element(s).");

            // Journal log (interactive path)
            var auditDirectory = ResolveAuditDirectory(null);
            LogSetOperation(auditDirectory, param, resolvedValue, category, filter, id, fromStdin, data.Affected);
            var status = await TryGetStatusForLedgerAsync(client);
            await TryLogSetLedgerOperationAsync(auditDirectory, request, data, clearValue, fromStdin, idsFromFile, status);
        });

        return command;
    }

    public static async Task<int> ExecuteAsync(
        RevitClient client,
        string? category,
        string? filter,
        long? id,
        string param,
        string? value,
        bool dryRun,
        bool yes,
        bool fromStdin,
        string? idsFromFile,
        TextWriter output,
        string? planOutputPath = null,
        bool clearValue = false,
        string? auditDirectory = null)
    {
        if (string.IsNullOrEmpty(param))
        {
            await output.WriteLineAsync("Error: --param is required.");
            return 1;
        }

        if (!TryResolveValue(value, clearValue, out var resolvedValue, out var valueError))
        {
            await output.WriteLineAsync($"Error: {valueError}");
            return 1;
        }

        var hasIdSource = fromStdin || !string.IsNullOrEmpty(idsFromFile);
        if (category == null && !id.HasValue && !hasIdSource)
        {
            await output.WriteLineAsync("Error: provide a category, --id, --stdin, or --ids-from to target elements.");
            return 1;
        }

        // ID source options are mutually exclusive with category/filter/id
        if (hasIdSource && (category != null || !string.IsNullOrEmpty(filter) || id.HasValue))
        {
            await output.WriteLineAsync("Error: --stdin/--ids-from cannot be combined with category, --filter, or --id.");
            return 1;
        }

        if (!dryRun && string.IsNullOrWhiteSpace(planOutputPath) && !yes)
        {
            await output.WriteLineAsync("Error: use --yes to apply a set operation. Use --dry-run or --plan-output to preview first.");
            return 1;
        }

        var request = new SetRequest
        {
            Category = category,
            ElementId = id,
            Filter = filter,
            Param = param,
            Value = resolvedValue,
            DryRun = dryRun || !string.IsNullOrWhiteSpace(planOutputPath)
        };

        try
        {
            if (!string.IsNullOrEmpty(idsFromFile))
                request.ElementIds = ReadIdsFromFile(idsFromFile);
            else if (fromStdin)
                request.ElementIds = ReadIdsFromStdin();
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var result = await client.SetParameterAsync(request);

        if (!result.Success)
        {
            await output.WriteLineAsync($"Error: {result.Error}");
            return 1;
        }

        var data = result.Data!;

        if (!string.IsNullOrWhiteSpace(planOutputPath))
        {
            if (data.Preview.Count != data.Affected)
            {
                await output.WriteLineAsync(
                    $"Error: dry-run returned {data.Affected} affected element(s) but only {data.Preview.Count} preview row(s); refusing to write an unstable plan.");
                return 1;
            }

            var plan = SetPlanFile.Create(request, data, planOutputPath);
            SetPlanFileStore.Save(planOutputPath, plan);
            await output.WriteLineAsync($"Plan written to {Path.GetFullPath(planOutputPath)}");
            await output.WriteLineAsync($"Review: {plan.Commands.Show}");
            await output.WriteLineAsync($"Dry-run apply: {plan.Commands.DryRunApply}");
            await output.WriteLineAsync($"Apply: {plan.Commands.Apply}");
            return 0;
        }

        if (dryRun)
        {
            await output.WriteLineAsync($"Dry run: {data.Affected} element(s) would be modified.");
            foreach (var item in data.Preview)
                await output.WriteLineAsync($"  [{item.Id}] {item.Name}: \"{item.OldValue}\" -> \"{item.NewValue}\"");
            return 0;
        }

        await output.WriteLineAsync($"Modified {data.Affected} element(s).");

        var resolvedAuditDirectory = ResolveAuditDirectory(auditDirectory);
        LogSetOperation(resolvedAuditDirectory, param, resolvedValue, category, filter, id, fromStdin, data.Affected);
        var status = await TryGetStatusForLedgerAsync(client);
        await TryLogSetLedgerOperationAsync(resolvedAuditDirectory, request, data, clearValue, fromStdin, idsFromFile, status);

        return 0;
    }

    private static bool TryResolveValue(string? value, bool clearValue, out string resolvedValue, out string error)
    {
        if (clearValue && value is not null)
        {
            resolvedValue = "";
            error = "--value cannot be combined with --clear-value.";
            return false;
        }

        if (clearValue)
        {
            resolvedValue = "";
            error = "";
            return true;
        }

        if (value is null)
        {
            resolvedValue = "";
            error = "--value is required unless --clear-value is used.";
            return false;
        }

        resolvedValue = value;
        error = "";
        return true;
    }

    private static void LogSetOperation(string? auditDirectory, string param, string value, string? category,
        string? filter, long? id, bool fromStdin, int affected)
    {
        JournalLogger.Log(auditDirectory, new
        {
            action = "set",
            param,
            value,
            category,
            filter,
            elementId = id,
            fromStdin,
            affected,
            timestamp = DateTime.UtcNow.ToString("o"),
            user = Environment.UserName
        });
    }

    private static async Task TryLogSetLedgerOperationAsync(
        string auditDirectory,
        SetRequest request,
        SetResult result,
        bool clearValue,
        bool fromStdin,
        string? idsFromFile,
        StatusInfo? status)
    {
        try
        {
            var output = new StringWriter();
            var exitCode = await LedgerCommand.ExecuteAppendAsync(
                auditDirectory,
                action: "set",
                category: request.Category ?? "elements",
                operatorName: null,
                status: "succeeded",
                summary: $"Set {request.Param} on {result.Affected} element(s)",
                timestamp: null,
                model: NormalizeLedgerText(status?.DocumentName),
                modelPath: NormalizeLedgerText(status?.DocumentPath),
                planHash: null,
                artifactPath: null,
                receiptPath: null,
                receiptHash: null,
                rollbackPointer: null,
                evidenceLinks: Array.Empty<string>(),
                yes: true,
                outputFormat: "json",
                output,
                commandName: "set",
                commandArgs: BuildSetLedgerArgs(request, clearValue, fromStdin, idsFromFile),
                affectedElementCount: result.Affected,
                affectedElementIds: GetAffectedElementIds(request, result),
                revitVersion: NormalizeLedgerText(status?.RevitVersion));
            if (exitCode != 0)
                Console.Error.WriteLine($"[RevitCli] Ledger write failed (operation ledger incomplete): {output.ToString().Trim()}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException)
        {
            Console.Error.WriteLine($"[RevitCli] Ledger write failed (operation ledger incomplete): {ex.Message}");
        }
    }

    private static async Task<StatusInfo?> TryGetStatusForLedgerAsync(RevitClient client)
    {
        try
        {
            var response = await client.GetStatusAsync();
            return response.Success ? response.Data : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Console.Error.WriteLine($"[RevitCli] Status read failed (operation ledger model identity incomplete): {ex.Message}");
            return null;
        }
    }

    private static string? NormalizeLedgerText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ResolveAuditDirectory(string? auditDirectory)
    {
        if (!string.IsNullOrWhiteSpace(auditDirectory))
            return Path.GetFullPath(auditDirectory);

        return Profile.ProfileLoader.Discover() is { } profilePath
            ? Path.GetDirectoryName(Path.GetFullPath(profilePath))!
            : Directory.GetCurrentDirectory();
    }

    private static List<string> BuildSetLedgerArgs(SetRequest request, bool clearValue, bool fromStdin, string? idsFromFile)
    {
        var args = new List<string> { "set" };
        if (!string.IsNullOrWhiteSpace(request.Category))
            args.Add(request.Category!);
        if (request.ElementId.HasValue)
        {
            args.Add("--id");
            args.Add(request.ElementId.Value.ToString());
        }
        if (!string.IsNullOrWhiteSpace(request.Filter))
        {
            args.Add("--filter");
            args.Add(request.Filter!);
        }
        if (fromStdin)
        {
            args.Add("--stdin");
        }
        if (!string.IsNullOrWhiteSpace(idsFromFile))
        {
            args.Add("--ids-from");
            args.Add(idsFromFile!);
        }
        args.Add("--param");
        args.Add(request.Param);
        if (clearValue)
        {
            args.Add("--clear-value");
        }
        else
        {
            args.Add("--value");
            args.Add(request.Value);
        }
        args.Add("--yes");
        return args;
    }

    private static List<long> GetAffectedElementIds(SetRequest request, SetResult result)
    {
        var ids = new List<long>();
        if (request.ElementId.HasValue)
            ids.Add(request.ElementId.Value);
        if (request.ElementIds is { Count: > 0 })
            ids.AddRange(request.ElementIds);
        if (result.Preview.Count > 0)
            ids.AddRange(result.Preview.Select(item => item.Id));
        return ids
            .Distinct()
            .OrderBy(elementId => elementId)
            .ToList();
    }

    /// <summary>
    /// Read element IDs from stdin. Supports:
    /// - JSON array of objects with "id" field (query --output json)
    /// - JSON array of numbers
    /// - One ID per line (plain text)
    /// Fails explicitly if stdin is non-empty but yields no valid IDs.
    /// </summary>
    private static List<long> ReadIdsFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"--ids-from: file not found: {filePath}");

        var input = File.ReadAllText(filePath);
        return ParseIds(input, "--ids-from");
    }

    private static List<long> ReadIdsFromStdin()
    {
        var input = Console.In.ReadToEnd();
        if (string.IsNullOrWhiteSpace(input))
            throw new InvalidOperationException("--stdin: no input received. Pipe element data to stdin.");

        return ParseIds(input, "--stdin");
    }

    private static List<long> ParseIds(string input, string source)
    {
        input = input.Trim();
        List<long>? ids = null;

        // Try JSON array
        if (input.StartsWith("["))
        {
            var elements = JsonSerializer.Deserialize<List<JsonElement>>(input);
            if (elements != null)
            {
                ids = new List<long>();
                foreach (var elem in elements)
                {
                    if (elem.ValueKind == JsonValueKind.Number)
                        ids.Add(elem.GetInt64());
                    else if (elem.ValueKind == JsonValueKind.Object &&
                             elem.TryGetProperty("id", out var idProp))
                        ids.Add(idProp.GetInt64());
                    else
                        throw new InvalidOperationException(
                            $"{source}: array item is not a number or object with 'id' field.");
                }
            }
        }
        // Try JSON wrapper with "data" field (API response format)
        else if (input.StartsWith("{"))
        {
            using var doc = JsonDocument.Parse(input);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Array)
            {
                ids = data.EnumerateArray()
                    .Select(e =>
                    {
                        if (!e.TryGetProperty("id", out var idProp))
                            throw new InvalidOperationException($"{source}: object in data array missing 'id' field.");
                        return idProp.GetInt64();
                    })
                    .ToList();
            }
        }
        else
        {
            // Fallback: one ID per line
            var lines = input.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            ids = new List<long>();
            foreach (var line in lines)
            {
                if (!long.TryParse(line, out var parsed))
                    throw new InvalidOperationException($"{source}: '{line}' is not a valid element ID.");
                ids.Add(parsed);
            }
        }

        if (ids == null || ids.Count == 0)
            throw new InvalidOperationException($"{source}: no element IDs found in input.");

        return ids;
    }
}
