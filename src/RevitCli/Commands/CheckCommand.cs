using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RevitCli.Checks;
using RevitCli.Client;
using RevitCli.Output;
using RevitCli.Reports;
using Spectre.Console;

namespace RevitCli.Commands;

public static class CheckCommand
{
    public static Command Create(RevitClient client)
    {
        var nameArg = new Argument<string?>("name", () => null, "Check set name (default: 'default')");
        var profileOpt = new Option<string?>("--profile", "Path to .revitcli.yml profile");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table, json, html, sarif, pr-comment");
        var reportOpt = new Option<string?>("--report", "Save report to file (format inferred from extension, or uses --output)");
        var noSaveOpt = new Option<bool>("--no-save", "Don't save results for diff comparison");

        var command = new Command("check", "Run project checks from .revitcli.yml profile")
        {
            nameArg, profileOpt, outputOpt, reportOpt, noSaveOpt
        };

        command.SetHandler(async (name, profilePath, outputFormat, reportPath, noSave) =>
        {
            Environment.ExitCode = await ExecuteAsync(client, name, profilePath, outputFormat, reportPath, noSave, Console.Out);
        }, nameArg, profileOpt, outputOpt, reportOpt, noSaveOpt);

        return command;
    }

    public static Task<int> ExecuteAsync(RevitClient client, string? name, string? profilePath,
        string outputFormat, string? reportPath, bool noSave, TextWriter output)
        => ExecuteAsync(client, name, profilePath, outputFormat, reportPath, noSave, true, output);

    internal static async Task<int> ExecuteAsync(RevitClient client, string? name, string? profilePath,
        string outputFormat, string? reportPath, bool noSave, bool sendNotify, TextWriter output)
    {
        var run = await CheckRunner.RunAsync(client, name, profilePath);
        if (!run.Success)
        {
            await output.WriteLineAsync(run.Error);
            if (run.Error.Contains("not running", StringComparison.OrdinalIgnoreCase))
                await output.WriteLineAsync("  Run 'revitcli doctor' to diagnose connection issues.");
            return 1;
        }

        var checkName = run.Data!.CheckName;
        var profile = run.Data.Profile;
        var checkDef = run.Data.CheckDefinition;
        var allIssues = run.Data.Issues;
        var suppressedCount = run.Data.SuppressedCount;
        var displayPassed = run.Data.DisplayPassed;
        var displayFailed = run.Data.DisplayFailed;

        // Render output
        var format = outputFormat.ToLowerInvariant();

        // Infer format from report file extension if provided
        if (reportPath != null)
        {
            var ext = Path.GetExtension(reportPath).ToLowerInvariant();
            if (ext == ".html" || ext == ".htm")
                format = "html";
            else if (ext == ".json")
                format = "json";
            else
            {
                // Let the v1.7 report formats (sarif/pr-comment) infer from .sarif / .md
                var inferred = AuditReportFormats.InferFormatFromExtension(ext);
                if (inferred != null)
                    format = inferred;
            }
        }

        string rendered;
        if (AuditReportFormats.TryRender(format, allIssues, out var ciContent))
        {
            rendered = ciContent;
        }
        else
        {
            rendered = format switch
            {
                "json" => CheckReportRenderer.RenderJson(checkName, displayPassed, displayFailed, allIssues, suppressedCount),
                "html" => CheckReportRenderer.RenderHtml(checkName, displayPassed, displayFailed, allIssues, suppressedCount),
                _ => CheckReportRenderer.RenderTable(checkName, displayPassed, displayFailed, allIssues, suppressedCount)
            };
        }

        // Write to file if --report specified
        if (reportPath != null)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(reportPath, rendered);
            await output.WriteLineAsync($"Report saved to {reportPath}");

            // Also print summary to console
            await output.WriteLineAsync(
                CheckReportRenderer.RenderTable(checkName, displayPassed, displayFailed, allIssues, suppressedCount));
        }
        else
        {
            await output.WriteLineAsync(rendered);
        }

        // Diff + save (only for table output — don't pollute JSON/HTML)
        var profileDir = run.Data.ProfilePath != null
            ? Path.GetDirectoryName(run.Data.ProfilePath)
            : null;

        if (!noSave)
        {
            try
            {
                // Diff against latest (before rotation)
                var diff = CheckResultStore.ComputeDiffAgainstLatest(checkName, allIssues, profileDir);
                CheckResultStore.Save(checkName, displayPassed, displayFailed, suppressedCount, allIssues, profileDir);

                // Only print diff to console for table format (not JSON/HTML)
                if (diff != null && format == "table")
                {
                    await output.WriteLineAsync("");
                    await output.WriteLineAsync(
                        $"vs previous: {diff.New.Count} new, {diff.Resolved.Count} resolved, {diff.Unchanged} unchanged");

                    foreach (var r in diff.Resolved.Take(5))
                        await output.WriteLineAsync($"  [RESOLVED] {r.Rule}: {r.Message}");
                    if (diff.Resolved.Count > 5)
                        await output.WriteLineAsync($"  ... and {diff.Resolved.Count - 5} more resolved");

                    foreach (var n in diff.New.Take(5))
                        await output.WriteLineAsync($"  [NEW] {n.Rule}: {n.Message}");
                    if (diff.New.Count > 5)
                        await output.WriteLineAsync($"  ... and {diff.New.Count - 5} more new issues");
                }
            }
            catch (Exception ex)
            {
                // Don't let history I/O failures break the audit result
                await Console.Error.WriteLineAsync($"Warning: failed to save check results: {ex.Message}");
            }
        }

        // Determine exit code based on failOn
        var failOn = checkDef.FailOn.ToLowerInvariant();
        var hasErrors = allIssues.Any(i => i.Severity == "error");
        var hasWarnings = allIssues.Any(i => i.Severity == "warning");

        var exitCode = 0;
        if (failOn == "error" && hasErrors)
            exitCode = 1;
        else if (failOn == "warning" && (hasErrors || hasWarnings))
            exitCode = 1;

        // Webhook notification (suppressed when called from publish precheck)
        if (sendNotify && !string.IsNullOrWhiteSpace(profile.Defaults.Notify))
        {
            await WebhookNotifier.NotifyAsync(profile.Defaults.Notify, new
            {
                type = "check",
                check = checkName,
                passed = displayPassed,
                failed = displayFailed,
                suppressed = suppressedCount,
                issueCount = allIssues.Count,
                status = exitCode == 0 ? "passed" : "failed",
                timestamp = DateTime.UtcNow.ToString("o")
            });
        }

        return exitCode;
    }
}
