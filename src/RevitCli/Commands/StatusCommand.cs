using System;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Output;
using Spectre.Console;

namespace RevitCli.Commands;

public static class StatusCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Command Create(RevitClient client)
    {
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json");
        var command = new Command("status", "Check if Revit plugin is online")
        {
            outputOpt,
        };
        command.SetHandler(async (string outputFormat) =>
        {
            if (!ConsoleHelper.IsInteractive || IsJson(outputFormat) || !IsTable(outputFormat))
            {
                Environment.ExitCode = await ExecuteAsync(client, Console.Out, outputFormat);
                return;
            }

            var result = await client.GetStatusAsync();
            if (!result.Success)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(result.Error ?? "Unknown error")}");
                Environment.ExitCode = 1;
                return;
            }

            var status = result.Data!;
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Property");
            table.AddColumn("Value");
            table.AddRow("Revit Version", $"[green]{Markup.Escape(status.RevitVersion)}[/]");
            if (!string.IsNullOrEmpty(status.AddinVersion))
                table.AddRow("Add-in Version", Markup.Escape(status.AddinVersion));
            table.AddRow("Document", status.DocumentName != null
                ? $"[cyan]{Markup.Escape(status.DocumentName)}[/]"
                : "[grey](none open)[/]");
            if (status.DocumentName != null && status.DocumentPath != null)
                table.AddRow("Path", Markup.Escape(status.DocumentPath));
            if (status.Capabilities.Count > 0)
                table.AddRow("Capabilities", Markup.Escape(string.Join(", ", status.Capabilities)));
            AnsiConsole.Write(table);
        }, outputOpt);
        return command;
    }

    public static async Task<int> ExecuteAsync(RevitClient client, TextWriter output)
        => await ExecuteAsync(client, output, "table");

    public static async Task<int> ExecuteAsync(RevitClient client, TextWriter output, string outputFormat)
    {
        if (!IsTable(outputFormat) && !IsJson(outputFormat))
        {
            await output.WriteLineAsync("Error: --output must be 'table' or 'json'.");
            return 1;
        }

        var result = await client.GetStatusAsync();

        if (!result.Success)
        {
            if (IsJson(outputFormat))
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(
                    new StatusOutput(false, result.Error ?? "Unknown error", null),
                    JsonOpts));
                return 1;
            }

            await output.WriteLineAsync($"Error: {result.Error}");
            return 1;
        }

        var status = result.Data!;
        if (IsJson(outputFormat))
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(status, JsonOpts));
            return 0;
        }

        await output.WriteLineAsync($"Revit version: {status.RevitVersion}");
        if (!string.IsNullOrEmpty(status.AddinVersion))
            await output.WriteLineAsync($"Add-in:        v{status.AddinVersion}");
        if (status.DocumentName != null)
        {
            await output.WriteLineAsync($"Document:      {status.DocumentName}");
            if (status.DocumentPath != null)
                await output.WriteLineAsync($"Path:          {status.DocumentPath}");
        }
        else
        {
            await output.WriteLineAsync("Document:      (none open)");
        }
        return 0;
    }

    private static bool IsJson(string outputFormat) =>
        string.Equals(outputFormat, "json", StringComparison.OrdinalIgnoreCase);

    private static bool IsTable(string outputFormat) =>
        string.Equals(outputFormat, "table", StringComparison.OrdinalIgnoreCase);

    private sealed record StatusOutput(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("status")] object? Status);
}
