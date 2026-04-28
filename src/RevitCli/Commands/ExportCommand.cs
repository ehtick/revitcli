using System;
using System.CommandLine;
using System.IO;
using System.Linq;
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

        var command = new Command("export", "Export sheets or views from the Revit model")
        {
            formatOpt, sheetsOpt, viewsOpt, outputDirOpt, dryRunOpt
        };

        command.SetHandler(async (format, sheets, views, outputDir, dryRun) =>
        {
            if (!ConsoleHelper.IsInteractive)
            {
                Environment.ExitCode = await ExecuteAsync(client, format, sheets, views, outputDir, dryRun, Console.Out);
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
        }, formatOpt, sheetsOpt, viewsOpt, outputDirOpt, dryRunOpt);

        return command;
    }

    public static async Task<int> ExecuteAsync(RevitClient client, string format, string[] sheets, string[] views, string outputDir, TextWriter output)
        => await ExecuteAsync(client, format, sheets, views, outputDir, false, output);

    public static async Task<int> ExecuteAsync(RevitClient client, string format, string[] sheets, string[] views, string outputDir, bool dryRun, TextWriter output)
    {
        if (string.IsNullOrEmpty(format) || !ValidFormats.Contains(format.ToLower()))
        {
            await output.WriteLineAsync($"Error: --format must be one of: {string.Join(", ", ValidFormats)}");
            return 1;
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
            await output.WriteLineAsync($"Error: {result.Error}");
            return 1;
        }

        var progress = result.Data!;

        if (dryRun && progress.Status == "completed")
        {
            var msg = progress.Message ?? $"would export to {request.OutputDir}";
            await output.WriteLineAsync($"Dry run: {msg}");
            return 0;
        }

        if (progress.Status == "completed")
        {
            await output.WriteLineAsync($"Export completed. Task ID: {progress.TaskId}");
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
}
