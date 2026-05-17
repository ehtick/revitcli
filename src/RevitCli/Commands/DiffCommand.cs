using System;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Output;
using RevitCli.Shared;

namespace RevitCli.Commands;

public static class DiffCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static Command Create()
    {
        var fromArg = new Argument<string>("from", "Baseline snapshot JSON file");
        var toArg = new Argument<string>("to", "Current snapshot JSON file");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        var reportOpt = new Option<string?>("--report", "Write to file (format inferred from .md/.json extension)");
        var categoriesOpt = new Option<string?>("--categories", "Comma-separated category filter");
        var maxRowsOpt = new Option<int>("--max-rows", () => 20, "Rows shown per section in table/markdown");
        var reviewOpt = new Option<bool>("--review", "Render a rule-based review grouped by anomaly/notable/routine changes");

        var command = new Command("diff", "Diff two snapshot JSON files")
        {
            fromArg, toArg, outputOpt, reportOpt, categoriesOpt, maxRowsOpt, reviewOpt
        };

        command.SetHandler(async (string from, string to, string output,
                                  string? report, string? categories, int maxRows, bool review) =>
        {
            Environment.ExitCode = await ExecuteAsync(from, to, output, report, categories, maxRows, Console.Out, review);
        }, fromArg, toArg, outputOpt, reportOpt, categoriesOpt, maxRowsOpt, reviewOpt);

        return command;
    }

    public static async Task<int> ExecuteAsync(
        string fromPath, string toPath,
        string outputFormat, string? reportPath, string? categoriesFilter, int maxRows,
        TextWriter output,
        bool review = false)
    {
        if (!File.Exists(fromPath))
        {
            await output.WriteLineAsync($"Error: snapshot not found: {fromPath}");
            return 1;
        }
        if (!File.Exists(toPath))
        {
            await output.WriteLineAsync($"Error: snapshot not found: {toPath}");
            return 1;
        }

        ModelSnapshot fromSnap, toSnap;
        try
        {
            fromSnap = JsonSerializer.Deserialize<ModelSnapshot>(
                await File.ReadAllTextAsync(fromPath), JsonOpts)
                ?? throw new JsonException("Snapshot deserializes to null — not a valid snapshot file.");
            toSnap = JsonSerializer.Deserialize<ModelSnapshot>(
                await File.ReadAllTextAsync(toPath), JsonOpts)
                ?? throw new JsonException("Snapshot deserializes to null — not a valid snapshot file.");
        }
        catch (JsonException ex)
        {
            await output.WriteLineAsync($"Error: invalid snapshot JSON: {ex.Message}");
            return 1;
        }

        SnapshotDiff diff;
        try
        {
            diff = SnapshotDiffer.Diff(fromSnap, toSnap, Path.GetFileName(fromPath), Path.GetFileName(toPath));
        }
        catch (InvalidOperationException ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(categoriesFilter))
        {
            var allow = new System.Collections.Generic.HashSet<string>(
                categoriesFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
            foreach (var key in new System.Collections.Generic.List<string>(diff.Categories.Keys))
                if (!allow.Contains(key)) diff.Categories.Remove(key);
            // Keep Summary.PerCategory in sync so the header totals match the visible sections.
            foreach (var key in new System.Collections.Generic.List<string>(diff.Summary.PerCategory.Keys))
                if (!allow.Contains(key)) diff.Summary.PerCategory.Remove(key);
        }

        var effectiveFormat = outputFormat;
        if (reportPath != null)
        {
            var ext = Path.GetExtension(reportPath).ToLowerInvariant();
            if (ext == ".md") effectiveFormat = "markdown";
            else if (ext == ".json") effectiveFormat = "json";
        }

        var rendered = review
            ? DiffReviewRenderer.Render(DiffReviewRenderer.Build(diff), effectiveFormat, maxRows)
            : DiffRenderer.Render(diff, effectiveFormat, maxRows);

        if (reportPath != null)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(reportPath, rendered);
            await output.WriteLineAsync($"Diff saved to {reportPath}");
        }
        else
        {
            await output.WriteLineAsync(rendered);
        }

        return 0;
    }
}
