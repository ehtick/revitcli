using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RevitCli.Release;

namespace RevitCli.Commands;

public static class ReleaseCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static Command Create()
    {
        var command = new Command("release", "Release preparation helpers");
        command.AddCommand(CreateVerifyCommand());
        return command;
    }

    private static Command CreateVerifyCommand()
    {
        var rootOpt = new Option<string>(
            "--root",
            () => Directory.GetCurrentDirectory(),
            "Repository root to verify");
        var outputOpt = new Option<string>(
            "--output",
            () => "table",
            "Output format: table, json, markdown");
        var tagOpt = new Option<string?>(
            "--tag",
            "Release tag to compare with RevitCliVersion (for example v2.2.0)");
        var strictOpt = new Option<bool>(
            "--strict",
            "Treat warnings as release-blocking failures");

        var command = new Command("verify", "Verify local release-readiness files and CI guardrails")
        {
            rootOpt, outputOpt, tagOpt, strictOpt
        };

        command.SetHandler(async (root, output, tag, strict) =>
        {
            Environment.ExitCode = await ExecuteVerifyAsync(root, output, tag, strict, Console.Out);
        }, rootOpt, outputOpt, tagOpt, strictOpt);

        return command;
    }

    public static async Task<int> ExecuteVerifyAsync(
        string root,
        string outputFormat,
        string? tag,
        bool strict,
        TextWriter output)
    {
        var normalizedOutput = (outputFormat ?? "table").Trim().ToLowerInvariant();
        if (normalizedOutput is not ("table" or "json" or "markdown"))
        {
            await output.WriteLineAsync("Error: unknown output format. Use one of: table, json, markdown.");
            return 1;
        }

        var report = ReleaseVerifier.Verify(new ReleaseVerifyOptions
        {
            Root = root,
            Tag = tag,
            Strict = strict,
        });

        if (normalizedOutput == "json")
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonOptions));
        }
        else if (normalizedOutput == "markdown")
        {
            await output.WriteLineAsync(RenderMarkdown(report));
        }
        else
        {
            await output.WriteLineAsync(RenderTable(report));
        }

        return report.Success ? 0 : 1;
    }

    private static string RenderTable(ReleaseVerifyReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine("Release verification");
        writer.WriteLine($"Root:    {report.Root}");
        writer.WriteLine($"Version: {report.Version ?? "(missing)"}");
        if (!string.IsNullOrWhiteSpace(report.Tag))
        {
            writer.WriteLine($"Tag:     {report.Tag}");
        }

        writer.WriteLine($"Result:  {(report.Success ? "PASS" : "FAIL")} ({report.ErrorCount} error, {report.WarningCount} warning)");
        writer.WriteLine();
        writer.WriteLine($"{"Status",-7} {"Check",-34} Message");
        writer.WriteLine(new string('-', 100));

        foreach (var check in report.Checks
            .OrderBy(check => check.Status switch
            {
                ReleaseVerifyStatus.Error => 0,
                ReleaseVerifyStatus.Warning => 1,
                _ => 2,
            })
            .ThenBy(check => check.Id, StringComparer.Ordinal))
        {
            writer.WriteLine($"{FormatStatus(check.Status),-7} {Truncate(check.Id, 34),-34} {check.Message}");
        }

        writer.WriteLine();
        writer.WriteLine("Note: release verify checks local release files and CI guardrails only; real Revit smoke remains a Windows/Revit checklist gate.");
        return writer.ToString().TrimEnd();
    }

    private static string RenderMarkdown(ReleaseVerifyReport report)
    {
        var writer = new StringWriter();
        writer.WriteLine("# Release Verification");
        writer.WriteLine();
        writer.WriteLine($"- Status: `{(report.Success ? "PASS" : "FAIL")}`");
        writer.WriteLine($"- Root: `{EscapeInlineCode(report.Root)}`");
        writer.WriteLine($"- Version: `{EscapeInlineCode(report.Version ?? "(missing)")}`");
        if (!string.IsNullOrWhiteSpace(report.Tag))
            writer.WriteLine($"- Tag: `{EscapeInlineCode(report.Tag)}`");
        writer.WriteLine($"- Strict: `{report.Strict.ToString().ToLowerInvariant()}`");
        writer.WriteLine($"- Errors: `{report.ErrorCount}`");
        writer.WriteLine($"- Warnings: `{report.WarningCount}`");
        writer.WriteLine();

        AppendChecksMarkdown(writer, "Errors", report.Checks
            .Where(check => check.Status == ReleaseVerifyStatus.Error));
        AppendChecksMarkdown(writer, "Warnings", report.Checks
            .Where(check => check.Status == ReleaseVerifyStatus.Warning));
        AppendChecksMarkdown(writer, "Passing Checks", report.Checks
            .Where(check => check.Status == ReleaseVerifyStatus.Ok));

        writer.WriteLine();
        writer.WriteLine("## Gate Scope");
        writer.WriteLine();
        writer.WriteLine("- `release verify` checks local release files and CI guardrails only.");
        writer.WriteLine("- Real Revit smoke remains a separate Windows/Revit checklist gate.");
        return writer.ToString().TrimEnd();
    }

    private static void AppendChecksMarkdown(
        TextWriter writer,
        string title,
        IEnumerable<ReleaseVerifyCheck> checks)
    {
        writer.WriteLine($"## {title}");
        var ordered = checks
            .OrderBy(check => check.Id, StringComparer.Ordinal)
            .ToList();
        if (ordered.Count == 0)
        {
            writer.WriteLine("- None.");
            writer.WriteLine();
            return;
        }

        writer.WriteLine();
        writer.WriteLine("| Status | Check | Path | Message |");
        writer.WriteLine("|---|---|---|---|");
        foreach (var check in ordered)
        {
            writer.WriteLine(
                $"| {EscapeTableCell(FormatStatus(check.Status))} | {EscapeTableCell(check.Id)} | {EscapeTableCell(check.Path ?? "-")} | {EscapeTableCell(check.Message)} |");
        }

        writer.WriteLine();
    }

    private static string FormatStatus(ReleaseVerifyStatus status) => status switch
    {
        ReleaseVerifyStatus.Ok => "OK",
        ReleaseVerifyStatus.Warning => "WARN",
        ReleaseVerifyStatus.Error => "ERROR",
        _ => status.ToString().ToUpperInvariant(),
    };

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
            return value;

        return value[..Math.Max(0, max - 1)] + "…";
    }

    private static string EscapeInlineCode(string value)
    {
        return value
            .Replace("`", "'", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static string EscapeTableCell(string value)
    {
        return value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);
    }
}
