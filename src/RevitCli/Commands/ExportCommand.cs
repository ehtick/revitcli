using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Config;
using RevitCli.Output;
using RevitCli.Shared;
using Spectre.Console;

namespace RevitCli.Commands;

public static class ExportCommand
{
    internal static readonly string[] ValidFormats = { "dwg", "pdf", "ifc" };

    public static Command Create(RevitClient client, CliConfig config)
    {
        var formatOpt = new Option<string>("--format", "Export format: dwg, pdf, ifc") { IsRequired = true };
        var sheetsOpt = new Option<string[]>("--sheets", () => System.Array.Empty<string>(), "Sheet name patterns (e.g. \"A1*\", \"all\")");
        var viewsOpt = new Option<string[]>("--views", () => System.Array.Empty<string>(), "View name patterns (e.g. \"Level 1\", \"all\")");
        var outputDirOpt = new Option<string>("--output-dir", () => config.ExportDir, "Output directory for exported files");
        var dryRunOpt = new Option<bool>("--dry-run", "Validate inputs and resolve targets without writing files");
        var outputOpt = new Option<string>("--output", () => "table", "Output format for dry-runs: table | json");

        var command = new Command("export", "Export sheets or views from the Revit model")
        {
            formatOpt, sheetsOpt, viewsOpt, outputDirOpt, dryRunOpt, outputOpt
        };

        command.SetHandler(async (format, sheets, views, outputDir, dryRun, outputFormat) =>
        {
            if (!ConsoleHelper.IsInteractive)
            {
                Environment.ExitCode = await ExecuteAsync(client, format, sheets, views, outputDir, dryRun, Console.Out, outputFormat);
                return;
            }

            if (string.IsNullOrEmpty(format) || !ValidFormats.Contains(format.ToLower()))
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] --format must be one of: {string.Join(", ", ValidFormats)}");
                Environment.ExitCode = 1;
                return;
            }

            var request = new ExportRequest
            {
                Format = format.ToLower(),
                Sheets = sheets.ToList(),
                Views = views.ToList(),
                OutputDir = ResolveOutputDir(outputDir, dryRun),
                DryRun = dryRun
            };

            var result = await client.ExportAsync(request);
            if (!result.Success)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(result.Error ?? "Unknown error")}");
                Environment.ExitCode = 1;
                return;
            }

            var progress = result.Data!;

            if (dryRun && progress.Status == "completed")
            {
                AnsiConsole.MarkupLine($"[yellow]Dry run:[/] {Markup.Escape(progress.Message ?? $"would export to {request.OutputDir}")}");
                return;
            }

            if (progress.Status == "completed")
            {
                AnsiConsole.MarkupLine($"[green]Export completed.[/] Task ID: {progress.TaskId}");
                return;
            }

            await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn(),
                })
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask($"[cyan]Exporting {format.ToUpper()}[/]", maxValue: 100);
                    task.Value = progress.Progress;

                    var pollFailed = false;
                    var pollDeadline = DateTime.UtcNow.AddMinutes(10);
                    while (progress.Status != "completed" && progress.Status != "failed" && DateTime.UtcNow < pollDeadline)
                    {
                        await Task.Delay(1000);
                        var pollResult = await client.GetExportProgressAsync(progress.TaskId);
                        if (!pollResult.Success) { pollFailed = true; break; }
                        progress = pollResult.Data!;
                        task.Value = progress.Progress;
                    }

                    if (!pollFailed)
                        task.Value = 100;
                });

            if (progress.Status == "completed")
            {
                AnsiConsole.MarkupLine("[green]Export completed.[/]");
            }
            else if (progress.Status == "failed")
            {
                AnsiConsole.MarkupLine($"[red]Export failed:[/] {Markup.Escape(progress.Message ?? "Unknown error")}");
                Environment.ExitCode = 1;
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Export timed out or lost connection to Revit.[/]");
                Environment.ExitCode = 1;
            }
        }, formatOpt, sheetsOpt, viewsOpt, outputDirOpt, dryRunOpt, outputOpt);

        return command;
    }

    public static async Task<int> ExecuteAsync(RevitClient client, string format, string[] sheets, string[] views, string outputDir, TextWriter output)
        => await ExecuteAsync(client, format, sheets, views, outputDir, false, output);

    public static async Task<int> ExecuteAsync(
        RevitClient client,
        string format,
        string[] sheets,
        string[] views,
        string outputDir,
        bool dryRun,
        TextWriter output,
        string outputFormat = "table")
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json"))
        {
            await output.WriteLineAsync("Error: --output must be 'table' or 'json'.");
            return 1;
        }

        if (string.IsNullOrEmpty(format) || !ValidFormats.Contains(format.ToLower()))
        {
            return await WriteJsonAwareError(
                output,
                normalizedOutput,
                dryRun,
                format ?? "",
                sheets,
                views,
                outputDir,
                $"--format must be one of: {string.Join(", ", ValidFormats)}");
        }

        if (normalizedOutput == "json" && !dryRun)
        {
            return await WriteJsonAwareError(
                output,
                normalizedOutput,
                dryRun,
                format,
                sheets,
                views,
                outputDir,
                "--output json is supported for export dry-runs only. Add --dry-run or use table output.");
        }

        var request = new ExportRequest
        {
            Format = format.ToLower(),
            Sheets = sheets.ToList(),
            Views = views.ToList(),
            OutputDir = ResolveOutputDir(outputDir, dryRun),
            DryRun = dryRun
        };

        var result = await client.ExportAsync(request);

        if (!result.Success)
        {
            return await WriteJsonAwareError(
                output,
                normalizedOutput,
                dryRun,
                request.Format,
                request.Sheets,
                request.Views,
                request.OutputDir,
                result.Error ?? "Unknown error");
        }

        var progress = result.Data!;

        if (normalizedOutput == "json")
        {
            if (dryRun && progress.Status == "completed")
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(
                    ExportJsonReport.FromSuccess(request, progress),
                    TerminalJsonOptions.PrettyIgnoreNull));
                return 0;
            }

            await output.WriteLineAsync(JsonSerializer.Serialize(
                ExportJsonReport.Failure(
                    request.Format,
                    dryRun,
                    request.Sheets,
                    request.Views,
                    request.OutputDir,
                    progress.Message ?? $"Export dry-run returned status '{progress.Status}'.",
                    progress.Status,
                    progress.TaskId),
                TerminalJsonOptions.PrettyIgnoreNull));
            return 1;
        }

        if (dryRun && progress.Status == "completed")
        {
            var msg = progress.Message ?? $"would export to {request.OutputDir}";
            await output.WriteLineAsync($"Dry run: {msg}");
            return 0;
        }

        if (progress.Status == "completed")
        {
            await output.WriteLineAsync($"Export completed. Task ID: {progress.TaskId}");
            var receiptPath = TrySaveExportReceipt(request, progress);
            if (receiptPath != null)
                await output.WriteLineAsync($"Receipt saved to {receiptPath}");
            return 0;
        }

        await output.WriteLineAsync($"Export started. Task ID: {progress.TaskId}");
        await output.WriteLineAsync($"Status: {progress.Status}, Progress: {progress.Progress}%");

        var deadline = DateTime.UtcNow.AddMinutes(10);
        while (progress.Status != "completed" && progress.Status != "failed" && DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000);
            var pollResult = await client.GetExportProgressAsync(progress.TaskId);
            if (!pollResult.Success) break;
            progress = pollResult.Data!;
            await output.WriteLineAsync($"Progress: {progress.Progress}%");
        }

        if (progress.Status == "completed")
        {
            await output.WriteLineAsync("Export completed.");
            var receiptPath = TrySaveExportReceipt(request, progress);
            if (receiptPath != null)
                await output.WriteLineAsync($"Receipt saved to {receiptPath}");
            return 0;
        }

        if (progress.Status == "failed")
            await output.WriteLineAsync($"Export failed: {progress.Message}");
        else if (DateTime.UtcNow >= deadline)
            await output.WriteLineAsync("Error: export timed out after 10 minutes.");
        return 1;
    }

    // Dry-run never writes files, so the server doesn't need a resolved path.
    // Calling Path.GetFullPath("") would silently substitute the current
    // working directory, which is not what the user asked for when they
    // omit --output-dir alongside --dry-run.
    private static string ResolveOutputDir(string outputDir, bool dryRun)
    {
        if (dryRun && string.IsNullOrWhiteSpace(outputDir))
            return string.Empty;
        return Path.GetFullPath(outputDir);
    }

    private static string? TrySaveExportReceipt(ExportRequest request, ExportProgress progress)
    {
        if (request.DryRun || string.IsNullOrWhiteSpace(request.OutputDir))
            return null;

        try
        {
            var receiptDir = Path.Combine(request.OutputDir, ".revitcli", "receipts");
            Directory.CreateDirectory(receiptDir);
            var receiptPath = Path.Combine(receiptDir, $"export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            var receipt = ExportReceipt.From(request, progress);
            File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, TerminalJsonOptions.PrettyIgnoreNull));
            DeliveryManifestWriter.Append(request.OutputDir, new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "export",
                receiptPath = Path.GetFullPath(receiptPath),
                success = receipt.Success,
                dryRun = receipt.DryRun,
                format = receipt.Format,
                outputDir = receipt.OutputDir,
                taskId = receipt.TaskId,
                command = receipt.Command,
                timestamp = receipt.Timestamp
            });
            return receiptPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"[RevitCli] Export receipt write failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<int> WriteJsonAwareError(
        TextWriter output,
        string outputFormat,
        bool dryRun,
        string format,
        IReadOnlyList<string> sheets,
        IReadOnlyList<string> views,
        string outputDir,
        string error)
    {
        if (outputFormat == "json")
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(
                ExportJsonReport.Failure(format, dryRun, sheets, views, outputDir, error),
                TerminalJsonOptions.PrettyIgnoreNull));
            return 1;
        }

        await output.WriteLineAsync($"Error: {error}");
        return 1;
    }

    private sealed record ExportJsonReport(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("dryRun")] bool DryRun,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("sheets")] IReadOnlyList<string> Sheets,
        [property: JsonPropertyName("views")] IReadOnlyList<string> Views,
        [property: JsonPropertyName("outputDir")] string OutputDir,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("taskId")] string? TaskId,
        [property: JsonPropertyName("error")] string? Error)
    {
        public static ExportJsonReport FromSuccess(ExportRequest request, ExportProgress progress) =>
            new(
                "export.v1",
                true,
                request.DryRun,
                request.Format,
                request.Sheets,
                request.Views,
                request.OutputDir,
                progress.Status,
                progress.Message ?? $"would export to {request.OutputDir}",
                progress.TaskId,
                null);

        public static ExportJsonReport Failure(
            string format,
            bool dryRun,
            IReadOnlyList<string> sheets,
            IReadOnlyList<string> views,
            string outputDir,
            string error,
            string? status = null,
            string? taskId = null) =>
            new("export.v1", false, dryRun, format, sheets, views, outputDir, status, null, taskId, error);
    }

    private sealed record ExportReceipt(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("action")] string Action,
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("dryRun")] bool DryRun,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("sheets")] IReadOnlyList<string> Sheets,
        [property: JsonPropertyName("views")] IReadOnlyList<string> Views,
        [property: JsonPropertyName("outputDir")] string OutputDir,
        [property: JsonPropertyName("taskId")] string TaskId,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("timestamp")] string Timestamp,
        [property: JsonPropertyName("user")] string User,
        [property: JsonPropertyName("machine")] string Machine)
    {
        public static ExportReceipt From(ExportRequest request, ExportProgress progress) =>
            new(
                "export-receipt.v1",
                "export",
                true,
                request.DryRun,
                request.Format,
                request.Sheets,
                request.Views,
                request.OutputDir,
                progress.TaskId,
                progress.Status,
                progress.Message,
                BuildCommand(request),
                DateTime.UtcNow.ToString("o"),
                Environment.UserName,
                Environment.MachineName);
    }

    private static string BuildCommand(ExportRequest request)
    {
        var parts = new List<string>
        {
            "revitcli",
            "export",
            "--format",
            request.Format
        };

        foreach (var sheet in request.Sheets)
        {
            parts.Add("--sheets");
            parts.Add(QuoteArgument(sheet));
        }

        foreach (var view in request.Views)
        {
            parts.Add("--views");
            parts.Add(QuoteArgument(view));
        }

        if (!string.IsNullOrWhiteSpace(request.OutputDir))
        {
            parts.Add("--output-dir");
            parts.Add(QuoteArgument(request.OutputDir));
        }

        if (request.DryRun)
            parts.Add("--dry-run");

        return string.Join(" ", parts);
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";
        return value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }
}
