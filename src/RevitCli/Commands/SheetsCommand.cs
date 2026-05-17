using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Shared;
using RevitCli.Sheets;
using YamlDotNet.Core;

namespace RevitCli.Commands;

public static class SheetsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static Command Create(RevitClient client)
    {
        var command = new Command("sheets", "Verify and manage local sheet-frame expectations");
        command.AddCommand(CreateVerifyCommand(client));
        command.AddCommand(CreateIndexCommand(client));
        return command;
    }

    private static Command CreateVerifyCommand(RevitClient client)
    {
        var againstOpt = new Option<string?>("--against", "Sheet index YAML path");
        var ruleOpt = new Option<string?>("--rule", "Run a single sheet rule");
        var issuesOnlyOpt = new Option<bool>("--issues-only", "Only render warning/error issues");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");

        var command = new Command("verify", "Verify sheet numbering and required sheet expectations")
        {
            againstOpt, ruleOpt, issuesOnlyOpt, outputOpt
        };
        command.SetHandler(async (against, rule, issuesOnly, output) =>
        {
            Environment.ExitCode = await ExecuteVerifyAsync(client, against, rule, issuesOnly, output, Console.Out);
        }, againstOpt, ruleOpt, issuesOnlyOpt, outputOpt);
        return command;
    }

    private static Command CreateIndexCommand(RevitClient client)
    {
        var command = new Command("index", "Create or inspect .revitcli/sheets/index.yml");
        command.AddCommand(CreateIndexInitCommand(client));
        command.AddCommand(CreateIndexShowCommand());
        return command;
    }

    private static Command CreateIndexInitCommand(RevitClient client)
    {
        var pathOpt = new Option<string?>("--path", "Output path (default: .revitcli/sheets/index.yml)");
        var forceOpt = new Option<bool>("--force", "Overwrite an existing index file");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, yaml");

        var command = new Command("init", "Bootstrap a local sheet index from the active model")
        {
            pathOpt, forceOpt, outputOpt
        };
        command.SetHandler(async (path, force, output) =>
        {
            Environment.ExitCode = await ExecuteIndexInitAsync(client, path, force, output, Console.Out);
        }, pathOpt, forceOpt, outputOpt);
        return command;
    }

    private static Command CreateIndexShowCommand()
    {
        var pathOpt = new Option<string?>("--path", "Sheet index path (default: .revitcli/sheets/index.yml)");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, yaml");

        var command = new Command("show", "Show the declared sheet index");
        command.SetHandler(async (path, output) =>
        {
            Environment.ExitCode = await ExecuteIndexShowAsync(path, output, Console.Out);
        }, pathOpt, outputOpt);
        command.AddOption(pathOpt);
        command.AddOption(outputOpt);
        return command;
    }

    public static async Task<int> ExecuteVerifyAsync(
        RevitClient client,
        string? againstPath,
        string? rule,
        bool issuesOnly,
        string outputFormat,
        TextWriter output)
    {
        var normalizedOutput = NormalizeOutput(outputFormat, "table", "json", "markdown");
        if (normalizedOutput is null)
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, json, markdown.");
            return 1;
        }

        LoadedSheetIndex? loadedIndex;
        try
        {
            loadedIndex = LoadIndex(againstPath);
            if (!string.IsNullOrWhiteSpace(rule))
                SheetVerifier.ValidateRuleName(rule);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var snapshotResult = await client.CaptureSnapshotAsync(new SnapshotRequest
        {
            IncludeCategories = Array.Empty<string>().ToList(),
            IncludeSheets = true,
            IncludeSchedules = false,
            SummaryOnly = false,
        });

        if (!snapshotResult.Success)
        {
            await output.WriteLineAsync($"Error: {snapshotResult.Error}");
            return 4;
        }

        var report = SheetVerifier.Verify(snapshotResult.Data ?? new ModelSnapshot(), loadedIndex, rule);
        var rendered = issuesOnly ? FilterInfoIssues(report) : report;

        switch (normalizedOutput)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(rendered, JsonOptions));
                break;
            case "markdown":
                await output.WriteLineAsync(RenderMarkdown(rendered));
                break;
            default:
                await output.WriteLineAsync(RenderTable(rendered));
                break;
        }

        return report.Summary.ExitCode;
    }

    public static async Task<int> ExecuteIndexInitAsync(
        RevitClient client,
        string? path,
        bool force,
        string outputFormat,
        TextWriter output)
    {
        var normalizedOutput = NormalizeOutput(outputFormat, "table", "yaml");
        if (normalizedOutput is null)
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, yaml.");
            return 1;
        }

        var target = SheetIndexStore.ResolvePath(path);
        if (File.Exists(target) && !force)
        {
            await output.WriteLineAsync($"Error: sheet index already exists: {target}. Use --force to overwrite.");
            return 1;
        }

        var snapshotResult = await client.CaptureSnapshotAsync(new SnapshotRequest
        {
            IncludeCategories = Array.Empty<string>().ToList(),
            IncludeSheets = true,
            IncludeSchedules = false,
            SummaryOnly = false,
        });

        if (!snapshotResult.Success)
        {
            await output.WriteLineAsync($"Error: {snapshotResult.Error}");
            return 4;
        }

        var index = SheetVerifier.CreateIndexFromSnapshot(snapshotResult.Data ?? new ModelSnapshot());
        var yaml = SheetIndexStore.ToYaml(index);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await output.WriteLineAsync($"Writing sheet index: {target}");
        await File.WriteAllTextAsync(target, yaml);

        if (normalizedOutput == "yaml")
        {
            await output.WriteLineAsync(yaml.TrimEnd());
        }
        else
        {
            await output.WriteLineAsync($"Created sheet index with {index.Required.Count} required sheet declaration(s).");
            await output.WriteLineAsync($"Review and edit before using: revitcli sheets verify --against {Quote(target)}");
        }

        return 0;
    }

    public static async Task<int> ExecuteIndexShowAsync(
        string? path,
        string outputFormat,
        TextWriter output)
    {
        var normalizedOutput = NormalizeOutput(outputFormat, "table", "json", "yaml");
        if (normalizedOutput is null)
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, json, yaml.");
            return 1;
        }

        LoadedSheetIndex loaded;
        try
        {
            loaded = SheetIndexStore.Load(SheetIndexStore.ResolvePath(path));
            SheetVerifier.ValidateIndex(loaded.Index);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        switch (normalizedOutput)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(new { path = loaded.Path, index = loaded.Index }, JsonOptions));
                break;
            case "yaml":
                await output.WriteLineAsync(SheetIndexStore.ToYaml(loaded.Index).TrimEnd());
                break;
            default:
                await output.WriteLineAsync(RenderIndexTable(loaded));
                break;
        }

        return 0;
    }

    private static LoadedSheetIndex? LoadIndex(string? againstPath)
    {
        if (!string.IsNullOrWhiteSpace(againstPath))
        {
            var full = Path.GetFullPath(againstPath);
            if (!File.Exists(full))
                throw new FileNotFoundException($"Sheet index not found: {full}", full);
            return SheetIndexStore.Load(full);
        }

        return SheetIndexStore.TryLoadDefault();
    }

    private static SheetVerifyReport FilterInfoIssues(SheetVerifyReport report)
    {
        return new SheetVerifyReport
        {
            Command = report.Command,
            SchemaVersion = report.SchemaVersion,
            ConfigSource = report.ConfigSource,
            Summary = report.Summary,
            Issues = report.Issues.Where(issue => issue.Severity != SheetIssueSeverity.Info).ToArray(),
        };
    }

    private static string RenderTable(SheetVerifyReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine("Sheet verification");
        writer.WriteLine($"Config: {report.ConfigSource}");
        writer.WriteLine($"Sheets: {report.Summary.TotalSheets}, placed views: {report.Summary.TotalPlacedViews}, exit: {report.Summary.ExitCode}");
        writer.WriteLine();

        if (report.Issues.Length == 0)
        {
            writer.WriteLine("No sheet issues found.");
            return writer.ToString().TrimEnd();
        }

        writer.WriteLine($"{"Severity",-8} {"Rule",-24} Message");
        writer.WriteLine(new string('-', 110));
        foreach (var issue in report.Issues)
        {
            writer.WriteLine($"{issue.Severity,-8} {Trim(issue.Rule, 24),-24} {issue.Message}");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderMarkdown(SheetVerifyReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Sheet Verification");
        writer.WriteLine();
        writer.WriteLine($"- Config: `{report.ConfigSource}`");
        writer.WriteLine($"- Sheets: {report.Summary.TotalSheets}");
        writer.WriteLine($"- Placed views: {report.Summary.TotalPlacedViews}");
        writer.WriteLine($"- Exit code: {report.Summary.ExitCode}");
        writer.WriteLine();

        if (report.Issues.Length == 0)
        {
            writer.WriteLine("No sheet issues found.");
            return writer.ToString().TrimEnd();
        }

        writer.WriteLine("| Severity | Rule | Message |");
        writer.WriteLine("| --- | --- | --- |");
        foreach (var issue in report.Issues)
        {
            writer.WriteLine($"| {issue.Severity} | `{issue.Rule}` | {issue.Message.Replace("|", "\\|", StringComparison.Ordinal)} |");
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderIndexTable(LoadedSheetIndex loaded)
    {
        var writer = new StringWriter();
        writer.WriteLine("Sheet index");
        writer.WriteLine($"Path: {loaded.Path}");
        writer.WriteLine($"Name: {loaded.Index.Name}");
        writer.WriteLine($"Required sheets: {loaded.Index.Required.Count}");
        if (!string.IsNullOrWhiteSpace(loaded.Index.Numbering.Scheme))
            writer.WriteLine($"Numbering scheme: {loaded.Index.Numbering.Scheme}");
        if (loaded.Index.Linkage.OverloadThreshold is > 0)
            writer.WriteLine($"Overload threshold: {loaded.Index.Linkage.OverloadThreshold}");

        if (loaded.Index.Required.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine($"{"Pattern",-18} {"MinViews",-9} Description");
            writer.WriteLine(new string('-', 80));
            foreach (var required in loaded.Index.Required)
            {
                var minViews = required.NeedsViews.Sum(view => Math.Max(0, view.MinCount));
                writer.WriteLine($"{Trim(required.Pattern, 18),-18} {minViews,-9} {required.Description ?? ""}");
            }
        }

        return writer.ToString().TrimEnd();
    }

    private static string? NormalizeOutput(string? output, params string[] allowed)
    {
        var normalized = (output ?? allowed[0]).Trim().ToLowerInvariant();
        return allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? normalized : null;
    }

    private static string Trim(string value, int max)
    {
        if (value.Length <= max)
            return value;

        return value[..Math.Max(0, max - 3)] + "...";
    }

    private static string Quote(string path) => $"\"{path.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
