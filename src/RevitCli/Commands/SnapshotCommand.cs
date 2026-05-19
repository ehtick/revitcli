using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Output;
using RevitCli.Shared;

namespace RevitCli.Commands;

public static class SnapshotCommand
{
    public static Command Create(RevitClient client)
    {
        var outputOpt = new Option<string?>("--output", "Write JSON to file (default: stdout)");
        var categoriesOpt = new Option<string?>("--categories", "Comma-separated category list (default: built-in set)");
        var noSheetsOpt = new Option<bool>("--no-sheets", "Skip sheets section");
        var noSchedulesOpt = new Option<bool>("--no-schedules", "Skip schedules section");
        var summaryOnlyOpt = new Option<bool>("--summary-only", "Only output Summary section (fast)");

        var command = new Command("snapshot", "Capture model's semantic state as JSON")
        {
            outputOpt, categoriesOpt, noSheetsOpt, noSchedulesOpt, summaryOnlyOpt
        };

        command.SetHandler(async (string? output, string? categories,
                                  bool noSheets, bool noSchedules, bool summaryOnly) =>
        {
            Environment.ExitCode = await ExecuteAsync(
                client, output, categories, !noSheets, !noSchedules, summaryOnly, Console.Out);
        }, outputOpt, categoriesOpt, noSheetsOpt, noSchedulesOpt, summaryOnlyOpt);

        return command;
    }

    public static async Task<int> ExecuteAsync(
        RevitClient client,
        string? outputPath,
        string? categories,
        bool includeSheets,
        bool includeSchedules,
        bool summaryOnly,
        TextWriter output)
    {
        var request = new SnapshotRequest
        {
            IncludeSheets = includeSheets,
            IncludeSchedules = includeSchedules,
            SummaryOnly = summaryOnly
        };
        if (!string.IsNullOrWhiteSpace(categories))
        {
            request.IncludeCategories = new List<string>(
                categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var result = await client.CaptureSnapshotAsync(request);
        if (!result.Success)
        {
            await output.WriteLineAsync($"Error: {result.Error}");
            return 1;
        }

        var json = JsonSerializer.Serialize(result.Data, TerminalJsonOptions.Pretty);
        if (outputPath != null)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(outputPath, json);
            await output.WriteLineAsync($"Snapshot written to {outputPath}");
        }
        else
        {
            await output.WriteLineAsync(json);
        }
        return 0;
    }
}
