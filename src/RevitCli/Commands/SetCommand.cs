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
        var valueOpt = new Option<string>("--value", "New parameter value") { IsRequired = true };
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without applying");
        var planOutputOpt = new Option<string?>("--plan-output", "Write a saved set plan JSON file without applying");
        var stdinOpt = new Option<bool>("--stdin", "Read element IDs from stdin (JSON array or query output)");
        var idsFromOpt = new Option<string?>("--ids-from", "Read element IDs from a JSON file");

        var command = new Command("set", "Modify element parameters in the Revit model")
        {
            categoryArg, filterOpt, idOpt, paramOpt, valueOpt, dryRunOpt, planOutputOpt, stdinOpt, idsFromOpt
        };

        command.SetHandler(async ctx =>
        {
            var category = ctx.ParseResult.GetValueForArgument(categoryArg);
            var filter = ctx.ParseResult.GetValueForOption(filterOpt);
            var id = ctx.ParseResult.GetValueForOption(idOpt);
            var param = ctx.ParseResult.GetValueForOption(paramOpt)!;
            var value = ctx.ParseResult.GetValueForOption(valueOpt)!;
            var dryRun = ctx.ParseResult.GetValueForOption(dryRunOpt);
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
                    fromStdin,
                    idsFromFile,
                    Console.Out,
                    planOutput);
                return;
            }

            if (string.IsNullOrEmpty(param))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] --param is required.");
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

            var request = new SetRequest
            {
                Category = category,
                ElementId = id,
                Filter = filter,
                Param = param,
                Value = value,
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
            LogSetOperation(param, value, category, filter, id, fromStdin, data.Affected);
        });

        return command;
    }

    public static async Task<int> ExecuteAsync(
        RevitClient client,
        string? category,
        string? filter,
        long? id,
        string param,
        string value,
        bool dryRun,
        bool fromStdin,
        string? idsFromFile,
        TextWriter output,
        string? planOutputPath = null)
    {
        if (string.IsNullOrEmpty(param))
        {
            await output.WriteLineAsync("Error: --param is required.");
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

        var request = new SetRequest
        {
            Category = category,
            ElementId = id,
            Filter = filter,
            Param = param,
            Value = value,
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

        LogSetOperation(param, value, category, filter, id, fromStdin, data.Affected);

        return 0;
    }

    private static void LogSetOperation(string param, string value, string? category,
        string? filter, long? id, bool fromStdin, int affected)
    {
        var profileDir = Profile.ProfileLoader.Discover() is { } p
            ? Path.GetDirectoryName(Path.GetFullPath(p))
            : null;

        JournalLogger.Log(profileDir, new
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
