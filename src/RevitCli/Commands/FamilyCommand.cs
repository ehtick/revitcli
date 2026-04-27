using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Shared;

namespace RevitCli.Commands;

public static class FamilyCommand
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    public static Command Create(RevitClient client)
    {
        var command = new Command("family", "Manage Revit families (list)");
        command.AddCommand(CreateListCommand(client));
        return command;
    }

    private static Command CreateListCommand(RevitClient client)
    {
        var unusedOpt = new Option<bool>(
            "--unused",
            () => false,
            "Only list families with zero placed FamilyInstances");
        var categoryOpt = new Option<string?>(
            "--category",
            "Filter by Revit category name (e.g. Doors, Windows)");
        var outputOpt = new Option<string>(
            "--output",
            () => "table",
            "Output format: table, json, csv");

        var cmd = new Command("ls", "List families in the active Revit document")
        {
            unusedOpt, categoryOpt, outputOpt
        };

        cmd.SetHandler(async (unused, category, output) =>
        {
            Environment.ExitCode = await ExecuteListAsync(client, unused, category, output, Console.Out);
        }, unusedOpt, categoryOpt, outputOpt);

        return cmd;
    }

    public static async Task<int> ExecuteListAsync(
        RevitClient client,
        bool unused,
        string? category,
        string outputFormat,
        TextWriter output)
    {
        var request = new FamilyListRequest
        {
            IncludeUnplaced = unused,
            Category = string.IsNullOrWhiteSpace(category) ? null : category
        };

        var result = await client.ListFamiliesAsync(request);
        if (!result.Success)
        {
            await output.WriteLineAsync($"Error: {result.Error}");
            return 1;
        }

        var families = result.Data ?? Array.Empty<FamilyInfo>();

        switch ((outputFormat ?? "table").ToLowerInvariant())
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(families, PrettyJson));
                break;
            case "csv":
                await output.WriteLineAsync(FormatCsv(families));
                break;
            default:
                await output.WriteLineAsync(FormatTable(families));
                break;
        }

        return 0;
    }

    private static string FormatTable(FamilyInfo[] families)
    {
        if (families.Length == 0)
            return "No families found.";

        var sb = new StringBuilder();
        sb.AppendLine($"{"Id",-10} {"Name",-32} {"Category",-18} {"InPlace",-8} {"Placed",-7} FilePath");
        sb.AppendLine(new string('-', 90));
        foreach (var f in families)
        {
            sb.AppendLine(
                $"{f.Id,-10} {Truncate(f.Name, 32),-32} {Truncate(f.Category, 18),-18} " +
                $"{(f.IsInPlace ? "yes" : "no"),-8} {(f.IsPlaced ? "yes" : "no"),-7} {f.FilePath ?? ""}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatCsv(FamilyInfo[] families)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,Name,Category,IsInPlace,IsPlaced,FilePath");
        foreach (var f in families)
        {
            sb.AppendLine(string.Join(",",
                f.Id.ToString(),
                EscapeCsvField(f.Name),
                EscapeCsvField(f.Category),
                f.IsInPlace ? "true" : "false",
                f.IsPlaced ? "true" : "false",
                EscapeCsvField(f.FilePath ?? "")));
        }
        return sb.ToString().TrimEnd();
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s.Substring(0, Math.Max(0, max - 1)) + "…";
    }
}
