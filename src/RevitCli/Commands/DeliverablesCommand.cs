using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RevitCli.Output;

namespace RevitCli.Commands;

public static class DeliverablesCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Command Create()
    {
        var command = new Command("deliverables", "Review local delivery plans, manifests, and receipts");
        command.AddCommand(CreateListCommand());
        command.AddCommand(CreateStatsCommand());
        command.AddCommand(CreateVerifyCommand());
        command.AddCommand(CreatePlanCommand());
        command.AddCommand(CreateBundleCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory containing .revitcli (default: current directory)");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");
        var command = new Command("list", "List delivery manifest entries")
        {
            dirOpt,
            outputOpt,
        };

        command.SetHandler(async (string? dir, string output) =>
        {
            Environment.ExitCode = await ExecuteListAsync(dir, output, Console.Out);
        }, dirOpt, outputOpt);

        return command;
    }

    private static Command CreatePlanCommand()
    {
        var profileOpt = new Option<string?>("--profile", "Path to .revitcli.yml profile")
        {
            IsRequired = true
        };
        var sinceOpt = new Option<string?>("--since", "Baseline snapshot JSON file for incremental delivery evidence");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");
        var command = new Command("plan", "Plan deliverable exports from a profile without contacting Revit")
        {
            profileOpt,
            sinceOpt,
            outputOpt,
        };

        command.SetHandler(async (string? profile, string? since, string output) =>
        {
            Environment.ExitCode = await ExecutePlanAsync(profile, since, output, Console.Out);
        }, profileOpt, sinceOpt, outputOpt);

        return command;
    }

    private static Command CreateBundleCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory containing .revitcli (default: current directory)");
        var bundlePathOpt = new Option<string?>("--bundle-path", "Zip path to write (default: .revitcli/deliveries/bundles/deliverables-<timestamp>.zip)");
        var dryRunOpt = new Option<bool>("--dry-run", "Plan the bundle without writing the zip or receipt");
        var forceOpt = new Option<bool>("--force", "Overwrite an existing --bundle-path");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");
        var command = new Command("bundle", "Create a zip audit package from delivery manifest receipts and output files")
        {
            dirOpt,
            bundlePathOpt,
            dryRunOpt,
            forceOpt,
            outputOpt,
        };

        command.SetHandler(async (
            string? dir,
            string? bundlePath,
            bool dryRun,
            bool force,
            string output) =>
        {
            Environment.ExitCode = await ExecuteBundleAsync(dir, bundlePath, dryRun, force, output, Console.Out);
        }, dirOpt, bundlePathOpt, dryRunOpt, forceOpt, outputOpt);

        return command;
    }

    private static Command CreateStatsCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory containing .revitcli (default: current directory)");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");
        var command = new Command("stats", "Summarize delivery manifest entries")
        {
            dirOpt,
            outputOpt,
        };

        command.SetHandler(async (string? dir, string output) =>
        {
            Environment.ExitCode = await ExecuteStatsAsync(dir, output, Console.Out);
        }, dirOpt, outputOpt);

        return command;
    }

    private static Command CreateVerifyCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory containing .revitcli (default: current directory)");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");
        var command = new Command("verify", "Verify delivery manifest entries point to readable receipts")
        {
            dirOpt,
            outputOpt,
        };

        command.SetHandler(async (string? dir, string output) =>
        {
            Environment.ExitCode = await ExecuteVerifyAsync(dir, output, Console.Out);
        }, dirOpt, outputOpt);

        return command;
    }

    internal static async Task<int> ExecuteListAsync(string? dir, string outputFormat, TextWriter output)
    {
        if (!TryParseOutput(outputFormat, out var normalizedOutput, out var outputError))
            return await WriteError(output, outputError);

        var report = ReadReport(dir, out var readError);
        if (report == null)
            return await WriteError(output, normalizedOutput, readError!);

        return normalizedOutput switch
        {
            "json" => await WriteJson(output, ToOutput(report), ExitCode(report)),
            "markdown" => await WriteLines(output, ExitCode(report), RenderListMarkdown(report)),
            _ => await WriteLines(output, ExitCode(report), RenderList(report))
        };
    }

    internal static async Task<int> ExecuteStatsAsync(string? dir, string outputFormat, TextWriter output)
    {
        if (!TryParseOutput(outputFormat, out var normalizedOutput, out var outputError))
            return await WriteError(output, outputError);

        var report = ReadReport(dir, out var readError);
        if (report == null)
            return await WriteError(output, normalizedOutput, readError!);

        return normalizedOutput switch
        {
            "json" => await WriteJson(output, ToOutput(report), ExitCode(report)),
            "markdown" => await WriteLines(output, ExitCode(report), RenderStatsMarkdown(report)),
            _ => await WriteLines(output, ExitCode(report), RenderStats(report))
        };
    }

    internal static async Task<int> ExecuteVerifyAsync(string? dir, string outputFormat, TextWriter output)
    {
        if (!TryParseOutput(outputFormat, out var normalizedOutput, out var outputError))
            return await WriteError(output, outputError);

        var report = ReadReport(dir, out var readError);
        if (report == null)
            return await WriteError(output, normalizedOutput, readError!);

        if (!report.Exists)
        {
            report.Issues.Add(new DeliveryManifestIssue(
                null,
                "error",
                "manifest-missing",
                $"delivery manifest not found: {report.ManifestPath}"));
        }

        return normalizedOutput switch
        {
            "json" => await WriteJson(output, ToOutput(report), ExitCode(report)),
            "markdown" => await WriteLines(output, ExitCode(report), RenderVerifyMarkdown(report)),
            _ => await WriteLines(output, ExitCode(report), RenderVerify(report))
        };
    }

    internal static async Task<int> ExecutePlanAsync(
        string? profilePath,
        string? sincePath,
        string outputFormat,
        TextWriter output)
    {
        if (!TryParseOutput(outputFormat, out var normalizedOutput, out var outputError))
            return await WriteError(output, outputError);

        DeliveryPlanReport report;
        try
        {
            report = DeliveryPlanPlanner.Plan(profilePath, sincePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return normalizedOutput == "json"
                ? await WriteJson(output, DeliveryPlanReport.Failure(ex.Message), 1)
                : await WriteError(output, ex.Message);
        }

        var exitCode = report.Success ? 0 : 1;
        return normalizedOutput switch
        {
            "json" => await WriteJson(output, report, exitCode),
            "markdown" => await WriteLines(output, exitCode, RenderPlanMarkdown(report)),
            _ => await WriteLines(output, exitCode, RenderPlan(report))
        };
    }

    internal static async Task<int> ExecuteBundleAsync(
        string? dir,
        string? bundlePath,
        bool dryRun,
        bool force,
        string outputFormat,
        TextWriter output)
    {
        if (!TryParseOutput(outputFormat, out var normalizedOutput, out var outputError))
            return await WriteError(output, outputError);

        DeliveryBundleReport report;
        try
        {
            report = DeliveryBundlePlanner.Plan(dir, bundlePath);
            report.DryRun = dryRun;
            report.RequiresWrite = !dryRun;
            if (File.Exists(report.BundlePath) && !force)
            {
                report.Issues.Add(new DeliveryManifestIssue(
                    null,
                    "error",
                    "bundle-exists",
                    $"bundle already exists: {report.BundlePath}"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return await WriteError(output, normalizedOutput, $"failed to plan delivery bundle: {ex.Message}");
        }

        if (!dryRun && report.Valid)
            DeliveryBundlePlanner.WriteBundle(report, force);

        var exitCode = report.Success ? 0 : 1;
        return normalizedOutput switch
        {
            "json" => await WriteJson(output, report, exitCode),
            "markdown" => await WriteLines(output, exitCode, RenderBundleMarkdown(report)),
            _ => await WriteLines(output, exitCode, RenderBundle(report))
        };
    }

    private static DeliveryManifestReport? ReadReport(string? dir, out string? error)
    {
        try
        {
            error = null;
            return DeliveryManifestReader.Read(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = $"failed to read delivery manifest: {ex.Message}";
            return null;
        }
    }

    private static IReadOnlyList<string> RenderList(DeliveryManifestReport report)
    {
        var lines = new List<string>();
        if (!report.Exists)
        {
            lines.Add($"INFO: No delivery manifest found: {report.ManifestPath}");
            return lines;
        }

        lines.Add($"OK: Delivery manifest: {report.ManifestPath}");
        lines.Add($"OK: Entries: {report.EntryCount}");
        if (report.EntryCount == 0)
        {
            lines.Add("INFO: No deliverables recorded.");
            AppendIssues(lines, report);
            return lines;
        }

        lines.Add("Entries:");
        foreach (var entry in report.Entries.OrderBy(entry => entry.LineNumber))
        {
            lines.Add(
                $"  {entry.LineNumber,4}  {Format(entry.Kind),-8} {FormatOutcome(entry),-8} {FormatReceipt(entry),-18} {Format(entry.Timestamp),-27} {Format(entry.ReceiptPath)}");
        }

        AppendIssues(lines, report);
        return lines;
    }

    private static IReadOnlyList<string> RenderListMarkdown(DeliveryManifestReport report)
    {
        var lines = new List<string>
        {
            "# Delivery Manifest",
            "",
            $"- Status: `{StatusText(report.Valid)}`",
            $"- Manifest: `{EscapeInlineCode(report.ManifestPath)}`",
            $"- Exists: `{BoolText(report.Exists)}`",
            $"- Entries: `{report.EntryCount}`",
            ""
        };

        if (!report.Exists)
        {
            lines.Add("No delivery manifest found.");
            AppendIssuesMarkdown(lines, report.Issues);
            return lines;
        }

        if (report.EntryCount == 0)
        {
            lines.Add("No deliverables recorded.");
            AppendIssuesMarkdown(lines, report.Issues);
            return lines;
        }

        lines.Add("## Entries");
        lines.Add("");
        lines.Add("| Line | Kind | Outcome | Receipt | Timestamp | Receipt path |");
        lines.Add("|---:|---|---|---|---|---|");
        foreach (var entry in report.Entries.OrderBy(entry => entry.LineNumber))
        {
            lines.Add(
                $"| {entry.LineNumber} | {EscapeTableCell(Format(entry.Kind))} | {EscapeTableCell(FormatOutcome(entry))} | {EscapeTableCell(FormatReceipt(entry))} | {EscapeTableCell(Format(entry.Timestamp))} | {EscapeTableCell(Format(entry.ReceiptPath))} |");
        }

        AppendIssuesMarkdown(lines, report.Issues);
        return lines;
    }

    private static IReadOnlyList<string> RenderPlan(DeliveryPlanReport report)
    {
        var lines = new List<string>
        {
            $"{StatusText(report.Success)}: Delivery plan: {report.ProfilePath}",
            $"OK: Schema: {report.SchemaVersion}",
            $"OK: Pipelines: {report.PipelineCount}",
            $"OK: Exports: {report.ExportCount}",
            $"OK: Risks: {report.RiskCount}"
        };

        if (!string.IsNullOrWhiteSpace(report.SincePath))
            lines.Add($"OK: Since baseline: {report.SincePath}");
        if (report.Baseline is { Readable: true } baseline)
            lines.Add($"OK: Baseline sheets: {baseline.SheetCount}");

        foreach (var pipeline in report.Pipelines)
        {
            lines.Add($"Pipeline: {pipeline.Name} exports={pipeline.ExportCount} risks={pipeline.RiskCount}");
            foreach (var export in pipeline.Exports)
            {
                var estimate = export.EstimatedSheetCount.HasValue
                    ? $" estimatedSheets={export.EstimatedSheetCount.Value}"
                    : "";
                lines.Add(
                    $"  {export.Preset,-18} {export.Format,-5} {export.Selector}{estimate} outputDir={export.OutputDir}");
            }
        }

        AppendPlanRisks(lines, report.Risks);
        return lines;
    }

    private static IReadOnlyList<string> RenderPlanMarkdown(DeliveryPlanReport report)
    {
        var lines = new List<string>
        {
            "# Delivery Plan",
            "",
            $"- Schema: `{report.SchemaVersion}`",
            $"- Status: `{StatusText(report.Success)}`",
            $"- Profile: `{EscapeInlineCode(report.ProfilePath)}`",
            $"- Profile hash: `{EscapeInlineCode(report.ProfileHash ?? "-")}`",
            $"- Since: `{EscapeInlineCode(report.SincePath ?? "-")}`",
            $"- Pipelines: `{report.PipelineCount}`",
            $"- Exports: `{report.ExportCount}`",
            $"- Risks: `{report.RiskCount}`",
            ""
        };

        if (report.Baseline != null)
        {
            lines.Add("## Baseline");
            lines.Add("");
            lines.Add($"- Path: `{EscapeInlineCode(report.Baseline.Path)}`");
            lines.Add($"- Exists: `{BoolText(report.Baseline.Exists)}`");
            lines.Add($"- Readable: `{BoolText(report.Baseline.Readable)}`");
            if (report.Baseline.Readable)
            {
                lines.Add($"- Document: `{EscapeInlineCode(report.Baseline.Document ?? "-")}`");
                lines.Add($"- Taken at: `{EscapeInlineCode(report.Baseline.TakenAt ?? "-")}`");
                lines.Add($"- Sheets: `{report.Baseline.SheetCount}`");
                lines.Add($"- Schedules: `{report.Baseline.ScheduleCount}`");
            }

            lines.Add("");
        }

        if (report.Pipelines.Count > 0)
        {
            lines.Add("## Pipelines");
            lines.Add("");
            lines.Add("| Pipeline | Precheck | Incremental | Since mode | Exports | Risks |");
            lines.Add("|---|---|---|---|---:|---:|");
            foreach (var pipeline in report.Pipelines)
            {
                lines.Add(
                    $"| {EscapeTableCell(pipeline.Name)} | {EscapeTableCell(pipeline.Precheck ?? "-")} | {BoolText(pipeline.Incremental)} | {EscapeTableCell(pipeline.SinceMode)} | {pipeline.ExportCount} | {pipeline.RiskCount} |");
            }

            lines.Add("");
            lines.Add("## Exports");
            lines.Add("");
            lines.Add("| Pipeline | Preset | Format | Selector | Estimated sheets | Output dir |");
            lines.Add("|---|---|---|---|---:|---|");
            foreach (var pipeline in report.Pipelines)
            {
                foreach (var export in pipeline.Exports)
                {
                    lines.Add(
                        $"| {EscapeTableCell(pipeline.Name)} | {EscapeTableCell(export.Preset)} | {EscapeTableCell(export.Format)} | {EscapeTableCell(export.Selector)} | {EscapeTableCell(export.EstimatedSheetCount?.ToString() ?? "-")} | {EscapeTableCell(export.OutputDir)} |");
                }
            }

            lines.Add("");
        }

        if (report.CommandPaths.Count > 0)
        {
            lines.Add("## Command Paths");
            lines.Add("");
            foreach (var command in report.CommandPaths)
                lines.Add($"- `{EscapeInlineCode(command)}`");
            lines.Add("");
        }

        AppendPlanRisksMarkdown(lines, report.Risks);
        return lines;
    }

    private static IReadOnlyList<string> RenderBundle(DeliveryBundleReport report)
    {
        var lines = new List<string>();
        var status = report.Success ? "OK" : "FAIL";
        if (report.DryRun)
        {
            lines.Add($"{status}: Delivery bundle dry-run: {report.BundlePath}");
        }
        else if (report.BundleWritten)
        {
            lines.Add($"{status}: Delivery bundle saved: {report.BundlePath}");
            lines.Add($"OK: Bundle receipt saved: {report.ReceiptPath}");
        }
        else
        {
            lines.Add($"{status}: Delivery bundle not written: {report.BundlePath}");
        }

        lines.Add($"OK: Manifest: {report.ManifestPath}");
        lines.Add($"OK: Entries: {report.EntryCount}");
        lines.Add($"OK: Files: {report.FileCount} ({report.DeliverableCount} deliverables, {report.ReceiptCount} receipts)");
        lines.Add($"OK: Bytes: {report.TotalBytes}");

        if (report.Files.Count > 0)
        {
            lines.Add("Files:");
            foreach (var file in report.Files.OrderBy(file => file.ArchivePath, StringComparer.OrdinalIgnoreCase))
                lines.Add($"  {file.Kind,-11} {file.Bytes,8} {file.ArchivePath}");
        }

        AppendIssues(lines, report.Issues);
        return lines;
    }

    private static IReadOnlyList<string> RenderBundleMarkdown(DeliveryBundleReport report)
    {
        var mode = report.DryRun ? "dry-run" : "write";
        var lines = new List<string>
        {
            "# Delivery Bundle",
            "",
            $"- Status: `{StatusText(report.Success)}`",
            $"- Mode: `{mode}`",
            $"- Bundle: `{EscapeInlineCode(report.BundlePath)}`",
            $"- Receipt: `{EscapeInlineCode(report.ReceiptPath)}`",
            $"- Manifest: `{EscapeInlineCode(report.ManifestPath)}`",
            $"- Entries: `{report.EntryCount}`",
            $"- Files: `{report.FileCount}`",
            $"- Deliverables: `{report.DeliverableCount}`",
            $"- Receipts: `{report.ReceiptCount}`",
            $"- Bytes: `{report.TotalBytes}`",
            $"- Bundle written: `{BoolText(report.BundleWritten)}`",
            $"- Receipt written: `{BoolText(report.ReceiptWritten)}`",
            ""
        };

        if (report.Files.Count > 0)
        {
            lines.Add("## Files");
            lines.Add("");
            lines.Add("| Kind | Bytes | Archive path | Source path | Manifest line |");
            lines.Add("|---|---:|---|---|---:|");
            foreach (var file in report.Files.OrderBy(file => file.ArchivePath, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add(
                    $"| {EscapeTableCell(file.Kind)} | {file.Bytes} | {EscapeTableCell(file.ArchivePath)} | {EscapeTableCell(file.SourcePath)} | {EscapeTableCell(file.LineNumber?.ToString() ?? "-")} |");
            }

            lines.Add("");
        }

        AppendIssuesMarkdown(lines, report.Issues);
        return lines;
    }

    private static IReadOnlyList<string> RenderStats(DeliveryManifestReport report)
    {
        var lines = new List<string>();
        if (!report.Exists)
        {
            lines.Add($"INFO: No delivery manifest found: {report.ManifestPath}");
            return lines;
        }

        var stats = report.Stats;
        lines.Add($"OK: Delivery manifest: {report.ManifestPath}");
        lines.Add($"OK: Entries: {stats.EntryCount}");
        lines.Add($"OK: Valid: {report.Valid.ToString().ToLowerInvariant()}");
        lines.Add($"OK: Errors: {stats.ErrorCount}");
        lines.Add($"OK: Missing receipts: {stats.ReceiptMissingCount}");
        lines.Add($"OK: Unreadable receipts: {stats.ReceiptUnreadableCount}");

        AppendCounts(lines, "Kinds", stats.Kinds);
        AppendCounts(lines, "Outcomes", stats.Outcomes);
        AppendIssues(lines, report);
        return lines;
    }

    private static IReadOnlyList<string> RenderStatsMarkdown(DeliveryManifestReport report)
    {
        var lines = new List<string>
        {
            "# Delivery Manifest Stats",
            "",
            $"- Status: `{StatusText(report.Valid)}`",
            $"- Manifest: `{EscapeInlineCode(report.ManifestPath)}`",
            $"- Exists: `{BoolText(report.Exists)}`",
            ""
        };

        if (!report.Exists)
        {
            lines.Add("No delivery manifest found.");
            AppendIssuesMarkdown(lines, report.Issues);
            return lines;
        }

        var stats = report.Stats;
        lines.Add($"- Entries: `{stats.EntryCount}`");
        lines.Add($"- Errors: `{stats.ErrorCount}`");
        lines.Add($"- Missing receipts: `{stats.ReceiptMissingCount}`");
        lines.Add($"- Unreadable receipts: `{stats.ReceiptUnreadableCount}`");
        lines.Add("");

        AppendCountsMarkdown(lines, "Kinds", stats.Kinds);
        AppendCountsMarkdown(lines, "Outcomes", stats.Outcomes);
        AppendIssuesMarkdown(lines, report.Issues);
        return lines;
    }

    private static IReadOnlyList<string> RenderVerify(DeliveryManifestReport report)
    {
        if (report.Valid)
        {
            return new[]
            {
                $"OK: Delivery manifest valid: {report.ManifestPath}",
                $"OK: Entries verified: {report.EntryCount}"
            };
        }

        var lines = new List<string>
        {
            $"FAIL: Delivery manifest invalid: {report.ManifestPath}"
        };
        AppendIssues(lines, report);
        return lines;
    }

    private static IReadOnlyList<string> RenderVerifyMarkdown(DeliveryManifestReport report)
    {
        var lines = new List<string>
        {
            "# Delivery Manifest Verification",
            "",
            $"- Status: `{StatusText(report.Valid)}`",
            $"- Manifest: `{EscapeInlineCode(report.ManifestPath)}`",
            $"- Entries verified: `{report.EntryCount}`",
            ""
        };

        AppendIssuesMarkdown(lines, report.Issues);
        return lines;
    }

    private static void AppendCounts(
        IList<string> lines,
        string title,
        IReadOnlyList<DeliveryManifestCount> counts)
    {
        if (counts.Count == 0)
            return;

        lines.Add($"{title}:");
        foreach (var count in counts)
            lines.Add($"  {count.Name,-12} {count.Count}");
    }

    private static void AppendCountsMarkdown(
        IList<string> lines,
        string title,
        IReadOnlyList<DeliveryManifestCount> counts)
    {
        if (counts.Count == 0)
            return;

        lines.Add($"## {title}");
        lines.Add("");
        lines.Add("| Name | Count |");
        lines.Add("|---|---:|");
        foreach (var count in counts)
            lines.Add($"| {EscapeTableCell(count.Name)} | {count.Count} |");
        lines.Add("");
    }

    private static void AppendPlanRisks(IList<string> lines, IReadOnlyList<DeliveryPlanRisk> risks)
    {
        if (risks.Count == 0)
            return;

        lines.Add("Risks:");
        foreach (var risk in risks)
        {
            var prefix = string.Equals(risk.Severity, "error", StringComparison.OrdinalIgnoreCase)
                ? "FAIL"
                : string.Equals(risk.Severity, "warning", StringComparison.OrdinalIgnoreCase)
                    ? "WARN"
                    : "INFO";
            var scope = string.IsNullOrWhiteSpace(risk.Pipeline)
                ? "profile"
                : string.IsNullOrWhiteSpace(risk.Preset)
                    ? risk.Pipeline!
                    : $"{risk.Pipeline}/{risk.Preset}";
            lines.Add($"  {prefix}: {scope}: {risk.Code}: {risk.Message}");
        }
    }

    private static void AppendPlanRisksMarkdown(IList<string> lines, IReadOnlyList<DeliveryPlanRisk> risks)
    {
        lines.Add("## Risks");
        lines.Add("");
        if (risks.Count == 0)
        {
            lines.Add("- None.");
            return;
        }

        lines.Add("| Severity | Scope | Code | Message |");
        lines.Add("|---|---|---|---|");
        foreach (var risk in risks)
        {
            var scope = string.IsNullOrWhiteSpace(risk.Pipeline)
                ? "profile"
                : string.IsNullOrWhiteSpace(risk.Preset)
                    ? risk.Pipeline!
                    : $"{risk.Pipeline}/{risk.Preset}";
            lines.Add(
                $"| {EscapeTableCell(risk.Severity.ToUpperInvariant())} | {EscapeTableCell(scope)} | {EscapeTableCell(risk.Code)} | {EscapeTableCell(risk.Message)} |");
        }
    }

    private static void AppendIssues(IList<string> lines, DeliveryManifestReport report)
    {
        AppendIssues(lines, report.Issues);
    }

    private static void AppendIssues(IList<string> lines, IReadOnlyList<DeliveryManifestIssue> issues)
    {
        if (issues.Count == 0)
            return;

        lines.Add("Issues:");
        foreach (var issue in issues)
        {
            var prefix = string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)
                ? "FAIL"
                : "WARN";
            var location = issue.LineNumber.HasValue ? $"line {issue.LineNumber.Value}" : "manifest";
            lines.Add($"  {prefix}: {location}: {issue.Code}: {issue.Message}");
        }
    }

    private static void AppendIssuesMarkdown(IList<string> lines, IReadOnlyList<DeliveryManifestIssue> issues)
    {
        lines.Add("## Issues");
        lines.Add("");
        if (issues.Count == 0)
        {
            lines.Add("- None.");
            return;
        }

        foreach (var issue in issues)
        {
            var location = issue.LineNumber.HasValue ? $"line {issue.LineNumber.Value}" : "manifest";
            lines.Add(
                $"- `{EscapeInlineCode(issue.Severity.ToUpperInvariant())}` `{EscapeInlineCode(location)}` `{EscapeInlineCode(issue.Code)}`: {EscapeMarkdownText(issue.Message)}");
        }
    }

    private static bool TryParseOutput(string? outputFormat, out string normalized, out string error)
    {
        normalized = string.IsNullOrWhiteSpace(outputFormat)
            ? "table"
            : outputFormat.Trim().ToLowerInvariant();
        if (normalized is "table" or "json" or "markdown")
        {
            error = "";
            return true;
        }

        error = $"unknown output format '{outputFormat}'. Use table, json, or markdown.";
        return false;
    }

    private static int ExitCode(DeliveryManifestReport report) => report.Valid ? 0 : 1;

    private static string Format(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static string StatusText(bool success) => success ? "OK" : "FAIL";

    private static string BoolText(bool value) => value.ToString().ToLowerInvariant();

    private static string EscapeInlineCode(string? value)
    {
        return (value ?? string.Empty)
            .Replace("`", "'", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static string EscapeMarkdownText(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static string EscapeTableCell(string? value)
    {
        return EscapeMarkdownText(value)
            .Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string FormatOutcome(DeliveryManifestEntry entry) => entry.Success switch
    {
        true => "success",
        false => "failed",
        _ => "unknown"
    };

    private static string FormatReceipt(DeliveryManifestEntry entry)
    {
        if (!entry.ReceiptExists)
            return "receipt-missing";
        return entry.ReceiptReadable ? "receipt-ok" : "receipt-unreadable";
    }

    private static async Task<int> WriteLines(TextWriter output, int exitCode, IEnumerable<string> lines)
    {
        foreach (var line in lines)
            await output.WriteLineAsync(line);
        return exitCode;
    }

    private static Task<int> WriteError(TextWriter output, string error)
    {
        return WriteLines(output, 1, new[] { $"Error: {error}" });
    }

    private static Task<int> WriteError(TextWriter output, string outputFormat, string error)
    {
        return outputFormat == "json"
            ? WriteJson(output, DeliverablesCommandOutput.Failure(error), 1)
            : WriteError(output, error);
    }

    private static async Task<int> WriteJson<T>(TextWriter output, T value, int exitCode)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(value, JsonOpts));
        return exitCode;
    }

    private static DeliverablesCommandOutput ToOutput(DeliveryManifestReport report)
    {
        return new DeliverablesCommandOutput(
            "deliverables.v1",
            report.Valid,
            report.Valid,
            report.ManifestPath,
            report.Exists,
            report.EntryCount,
            report.Entries,
            report.Stats,
            report.Issues,
            null);
    }

    private sealed record DeliverablesCommandOutput(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("valid")] bool Valid,
        [property: JsonPropertyName("manifestPath")] string? ManifestPath,
        [property: JsonPropertyName("exists")] bool Exists,
        [property: JsonPropertyName("entryCount")] int EntryCount,
        [property: JsonPropertyName("entries")] IReadOnlyList<DeliveryManifestEntry> Entries,
        [property: JsonPropertyName("stats")] DeliveryManifestStats? Stats,
        [property: JsonPropertyName("issues")] IReadOnlyList<DeliveryManifestIssue> Issues,
        [property: JsonPropertyName("error")] string? Error)
    {
        public static DeliverablesCommandOutput Failure(string error) =>
            new("deliverables.v1", false, false, null, false, 0,
                Array.Empty<DeliveryManifestEntry>(),
                null,
                Array.Empty<DeliveryManifestIssue>(),
                error);
    }
}
