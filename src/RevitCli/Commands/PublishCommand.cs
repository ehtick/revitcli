using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Output;
using RevitCli.Profile;
using RevitCli.Shared;
using Spectre.Console;

namespace RevitCli.Commands;

public static class PublishCommand
{
    private static readonly Regex DryRunExportLine = new(
        @"^\[dry-run\] Would export '([^']+)': format=([^,]+), sheets=\[(.*)\], outputDir=(.*)$",
        RegexOptions.Compiled);

    public static Command Create(RevitClient client)
    {
        var nameArg = new Argument<string?>("name", () => null, "Publish pipeline name (default: 'default')");
        var profileOpt = new Option<string?>("--profile", "Path to .revitcli.yml profile");
        var dryRunOpt = new Option<bool>("--dry-run", "Show what would be exported without exporting");
        var outputOpt = new Option<string>("--output", () => "table", "Output format for dry-runs: table | json");
        var sinceOpt = new Option<string?>("--since", "Baseline snapshot JSON file; only re-export sheets whose content changed since");
        var sinceModeOpt = new Option<string?>("--since-mode", "content | meta (default: content, or from profile)");
        var updateBaselineOpt = new Option<bool>("--update-baseline", "After successful publish, write the current snapshot back to the --since path");

        var command = new Command("publish", "Run export pipeline from .revitcli.yml profile")
        {
            nameArg, profileOpt, dryRunOpt, outputOpt, sinceOpt, sinceModeOpt, updateBaselineOpt
        };

        command.SetHandler(async (name, profilePath, dryRun, outputFormat, since, sinceMode, updateBaseline) =>
        {
            Environment.ExitCode = await ExecuteAsync(
                client, name, profilePath, dryRun, since, sinceMode, updateBaseline, Console.Out, outputFormat);
        }, nameArg, profileOpt, dryRunOpt, outputOpt, sinceOpt, sinceModeOpt, updateBaselineOpt);

        return command;
    }

    public static async Task<int> ExecuteAsync(
        RevitClient client,
        string? name,
        string? profilePath,
        bool dryRun,
        string? since,
        string? sinceMode,
        bool updateBaseline,
        TextWriter output,
        string outputFormat = "table")
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json"))
        {
            await output.WriteLineAsync("Error: --output must be 'table' or 'json'.");
            return 1;
        }

        var pipelineName = name ?? "default";
        if (normalizedOutput == "json")
        {
            if (!dryRun)
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(
                    PublishJsonReport.Failure(
                        pipelineName,
                        dryRun,
                        "--output json is supported for publish dry-runs only. Add --dry-run or use table output."),
                    TerminalJsonOptions.PrettyIgnoreNull));
                return 1;
            }

            var capture = new StringWriter();
            var exitCode = await ExecuteTableAsync(
                client,
                name,
                profilePath,
                dryRun,
                since,
                sinceMode,
                updateBaseline,
                capture);
            var report = CreateJsonReport(pipelineName, dryRun, exitCode, capture.ToString());
            await output.WriteLineAsync(JsonSerializer.Serialize(report, TerminalJsonOptions.PrettyIgnoreNull));
            return exitCode;
        }

        return await ExecuteTableAsync(
            client,
            name,
            profilePath,
            dryRun,
            since,
            sinceMode,
            updateBaseline,
            output);
    }

    private static async Task<int> ExecuteTableAsync(
        RevitClient client,
        string? name,
        string? profilePath,
        bool dryRun,
        string? since,
        string? sinceMode,
        bool updateBaseline,
        TextWriter output)
    {
        // Load profile
        ProjectProfile? profile;
        string? profileDir = null;
        string? resolvedProfilePath = null;
        try
        {
            if (profilePath != null)
            {
                resolvedProfilePath = Path.GetFullPath(profilePath);
                profile = ProfileLoader.Load(resolvedProfilePath);
                profileDir = Path.GetDirectoryName(resolvedProfilePath);
            }
            else
            {
                var discovered = ProfileLoader.Discover();
                if (discovered != null)
                {
                    resolvedProfilePath = Path.GetFullPath(discovered);
                    profile = ProfileLoader.Load(resolvedProfilePath);
                    profileDir = Path.GetDirectoryName(resolvedProfilePath);
                }
                else
                {
                    profile = null;
                }
            }
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error loading profile: {ex.Message}");
            return 1;
        }

        if (profile == null)
        {
            await output.WriteLineAsync($"Error: no {ProfileLoader.FileName} found.");
            await output.WriteLineAsync($"  Create one in your project root, or copy from .revitcli.example.yml");
            return 1;
        }

        var pipelineName = name ?? "default";
        if (!profile.Publish.TryGetValue(pipelineName, out var pipeline))
        {
            await output.WriteLineAsync($"Error: publish pipeline '{pipelineName}' not found in profile.");
            if (profile.Publish.Count > 0)
                await output.WriteLineAsync($"  Available pipelines: {string.Join(", ", profile.Publish.Keys)}");
            else
                await output.WriteLineAsync($"  Your profile has no publish pipelines. Add a 'publish:' section.");
            return 1;
        }

        // ── Incremental resolution ──────────────────────────────────────────
        // Effective baseline path: CLI --since wins; else profile.incremental → default
        string? effectiveBaselinePath = since;
        if (effectiveBaselinePath == null && pipeline.Incremental)
        {
            effectiveBaselinePath = pipeline.BaselinePath ?? ".revitcli/last-publish.json";
            // Resolve relative to profile dir
            if (profileDir != null && !Path.IsPathRooted(effectiveBaselinePath))
                effectiveBaselinePath = Path.GetFullPath(Path.Combine(profileDir, effectiveBaselinePath));
        }
        var effectiveSinceMode = SinceModeParser.Parse(sinceMode ?? pipeline.SinceMode);
        // --update-baseline is an explicit user opt-in and always applies, regardless of
        // where the baseline path came from.  Auto-update ADDITIONALLY applies when the
        // profile declares incremental: true AND no CLI --since was given — the "default
        // incremental workflow": the user expects the baseline to track the latest good
        // publish without passing a flag every time.  The combination means:
        //   * --update-baseline given            → always rewrite
        //   * no flag, no --since, incremental   → auto-rewrite .revitcli/last-publish.json
        //   * --since path only, no flag         → read path, do not rewrite
        var shouldUpdateBaseline = updateBaseline || (pipeline.Incremental && since == null);

        HashSet<string>? changedSheetNumbers = null;
        ModelSnapshot? currentSnapshot = null;
        if (effectiveBaselinePath != null)
        {
            var baseline = BaselineManager.Load(effectiveBaselinePath);
            if (baseline == null)
            {
                await output.WriteLineAsync($"Error: baseline not found or unreadable: {effectiveBaselinePath}");
                await output.WriteLineAsync($"  First time? Run: revitcli snapshot --output {effectiveBaselinePath}");
                return 1;
            }

            await output.WriteLineAsync($"Capturing current snapshot for diff against baseline ...");
            var snapResult = await client.CaptureSnapshotAsync(new SnapshotRequest());
            if (!snapResult.Success)
            {
                await output.WriteLineAsync($"Error: {snapResult.Error}");
                if (snapResult.Error?.Contains("not running") == true)
                    await output.WriteLineAsync("  Run 'revitcli doctor' to diagnose connection issues.");
                return 1;
            }
            currentSnapshot = snapResult.Data!;

            var diff = SnapshotDiffer.Diff(
                baseline, currentSnapshot,
                Path.GetFileName(effectiveBaselinePath), "current",
                effectiveSinceMode);

            changedSheetNumbers = new HashSet<string>();
            foreach (var m in diff.Sheets.Modified)
            {
                if (m.Key.StartsWith("sheet:"))
                    changedSheetNumbers.Add(m.Key.Substring("sheet:".Length));
            }
            foreach (var a in diff.Sheets.Added)
            {
                if (a.Key.StartsWith("sheet:"))
                    changedSheetNumbers.Add(a.Key.Substring("sheet:".Length));
            }

            if (changedSheetNumbers.Count == 0)
            {
                await output.WriteLineAsync(
                    $"Publish '{pipelineName}': no sheets changed since baseline ({effectiveSinceMode.ToString().ToLower()} mode). Nothing to export.");
                // Still refresh baseline if incremental — so schedule-only or element-only
                // changes that didn't surface at the sheet level don't accumulate silently.
                if (shouldUpdateBaseline && !dryRun)
                {
                    try
                    {
                        BaselineManager.Save(effectiveBaselinePath, currentSnapshot);
                        await output.WriteLineAsync($"Baseline refreshed: {effectiveBaselinePath}");
                    }
                    catch (Exception ex)
                    {
                        await output.WriteLineAsync($"Warning: failed to update baseline: {ex.Message}");
                    }
                }
                return 0;
            }

            await output.WriteLineAsync(
                $"Incremental publish: {changedSheetNumbers.Count} sheet(s) changed ({effectiveSinceMode.ToString().ToLower()} mode).");
        }

        // Run precheck if defined
        if (!string.IsNullOrWhiteSpace(pipeline.Precheck))
        {
            await output.WriteLineAsync($"Running precheck '{pipeline.Precheck}' ...");
            var checkResult = await CheckCommand.ExecuteAsync(client, pipeline.Precheck, profilePath, "table", null, true, false, output);
            if (checkResult != 0)
            {
                await output.WriteLineAsync("Precheck failed. Aborting publish.");
                await output.WriteLineAsync("  Fix the issues above, or use suppressions to waive known problems.");
                return 1;
            }
            await output.WriteLineAsync("");
        }

        // Run each export preset
        var succeeded = 0;
        var failed = 0;
        var receiptExports = new List<PublishReceiptExport>();

        foreach (var presetName in pipeline.Presets)
        {
            if (!profile.Exports.TryGetValue(presetName, out var preset))
            {
                await output.WriteLineAsync($"Error: export preset '{presetName}' not found in profile.");
                if (profile.Exports.Count > 0)
                    await output.WriteLineAsync($"  Available presets: {string.Join(", ", profile.Exports.Keys)}");
                failed++;
                continue;
            }

            // Incremental: narrow sheet selector to changed set
            var effectiveSheets = preset.Sheets;
            if (changedSheetNumbers != null)
            {
                if (preset.Sheets == null || preset.Sheets.Contains("all", StringComparer.OrdinalIgnoreCase))
                {
                    effectiveSheets = new List<string>(changedSheetNumbers);
                }
                else
                {
                    effectiveSheets = preset.Sheets
                        .Where(s => changedSheetNumbers.Contains(s))
                        .ToList();
                }

                if (effectiveSheets.Count == 0)
                {
                    await output.WriteLineAsync($"  Skipping '{presetName}': no matching changed sheets.");
                    succeeded++;
                    continue;
                }
            }

            // Resolve output dir relative to profile file
            var outputDir = preset.OutputDir ?? profile.Defaults.OutputDir ?? "./exports";
            if (profileDir != null && !Path.IsPathRooted(outputDir))
                outputDir = Path.GetFullPath(Path.Combine(profileDir, outputDir));

            if (dryRun)
            {
                var sheetSummary = effectiveSheets != null && effectiveSheets.Count > 0
                    ? string.Join(",", effectiveSheets)
                    : "(preset default)";
                await output.WriteLineAsync(
                    $"[dry-run] Would export '{presetName}': format={preset.Format}, sheets=[{sheetSummary}], outputDir={outputDir}");
                succeeded++;
                continue;
            }

            await output.WriteLineAsync($"Exporting '{presetName}' ({preset.Format.ToUpper()}) ...");

            var request = new ExportRequest
            {
                Format = preset.Format,
                Sheets = effectiveSheets ?? new List<string>(),
                Views = preset.Views ?? new List<string>(),
                OutputDir = outputDir
            };

            var result = await client.ExportAsync(request);
            var exportSuccess = result.Success && result.Data?.Status == "completed";
            var exportMessage = result.Error ?? result.Data?.Message;
            receiptExports.Add(new PublishReceiptExport(
                presetName,
                preset.Format,
                preset.Sheets ?? new List<string>(),
                request.Sheets,
                request.Views,
                outputDir,
                exportSuccess,
                result.Data?.Status,
                result.Data?.TaskId,
                exportMessage));

            if (exportSuccess)
            {
                await output.WriteLineAsync($"  Completed: {result.Data?.Message ?? "OK"}");
                succeeded++;
            }
            else
            {
                var errMsg = exportMessage ?? "Unknown error";
                await output.WriteLineAsync($"  Failed: {errMsg}");
                if (errMsg.Contains("not running"))
                    await output.WriteLineAsync("  Run 'revitcli doctor' to diagnose connection issues.");
                failed++;
            }
        }

        await output.WriteLineAsync("");
        await output.WriteLineAsync($"Publish '{pipelineName}': {succeeded} succeeded, {failed} failed");

        var exitCode = failed > 0 ? 1 : 0;

        // Update baseline if all presets succeeded and caller asked for it
        if (exitCode == 0 && shouldUpdateBaseline && currentSnapshot != null && effectiveBaselinePath != null && !dryRun)
        {
            try
            {
                BaselineManager.Save(effectiveBaselinePath, currentSnapshot);
                await output.WriteLineAsync($"Baseline updated: {effectiveBaselinePath}");
            }
            catch (Exception ex)
            {
                await output.WriteLineAsync($"Warning: failed to update baseline: {ex.Message}");
            }
        }
        else if (exitCode != 0 && shouldUpdateBaseline)
        {
            await output.WriteLineAsync($"Baseline NOT updated (publish had failures; baseline retained at {effectiveBaselinePath}).");
        }

        // Journal log + receipt
        if (!dryRun)
        {
            var command = BuildPublishCommand(pipelineName, profilePath, since, sinceMode, updateBaseline);
            var timestamp = DateTime.UtcNow.ToString("o");
            var receipt = new
            {
                schemaVersion = "publish-receipt.v1",
                action = "publish",
                success = failed == 0,
                dryRun = false,
                pipeline = pipelineName,
                succeeded,
                failed,
                presets = pipeline.Presets,
                exports = receiptExports,
                incremental = changedSheetNumbers != null,
                changedSheets = changedSheetNumbers?.Count ?? 0,
                command,
                timestamp,
                user = Environment.UserName,
                profileHash = resolvedProfilePath != null && File.Exists(resolvedProfilePath)
                    ? ComputeFileHash(resolvedProfilePath) : null,
                machine = Environment.MachineName
            };

            Output.JournalLogger.Log(profileDir, receipt);

            try
            {
                var receiptDir = profileDir ?? Directory.GetCurrentDirectory();
                var receiptPath = Path.Combine(receiptDir, ".revitcli", "receipts",
                    $"{pipelineName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
                var dir = Path.GetDirectoryName(receiptPath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(receiptPath, System.Text.Json.JsonSerializer.Serialize(receipt,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                DeliveryManifestWriter.Append(receiptDir, new
                {
                    schemaVersion = "delivery-manifest.v1",
                    kind = "publish",
                    receiptPath = Path.GetFullPath(receiptPath),
                    receiptHash = DeliveryManifestWriter.ComputeSha256Hex(receiptPath),
                    success = failed == 0,
                    dryRun = false,
                    pipeline = pipelineName,
                    succeeded,
                    failed,
                    command,
                    timestamp
                });
                await output.WriteLineAsync($"Receipt saved to {receiptPath}");
            }
            catch { /* best effort */ }
        }

        // Webhook notification
        if (!string.IsNullOrWhiteSpace(profile.Defaults.Notify) && !dryRun)
        {
            await Output.WebhookNotifier.NotifyAsync(profile.Defaults.Notify, new
            {
                type = "publish",
                pipeline = pipelineName,
                succeeded,
                failed,
                presets = pipeline.Presets,
                incremental = changedSheetNumbers != null,
                status = exitCode == 0 ? "passed" : "failed",
                timestamp = DateTime.UtcNow.ToString("o")
            });
        }

        return exitCode;
    }

    private static string ComputeFileHash(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()[..16];
    }

    private static string BuildPublishCommand(
        string pipelineName,
        string? profilePath,
        string? since,
        string? sinceMode,
        bool updateBaseline)
    {
        var parts = new List<string> { "revitcli", "publish" };
        if (!string.Equals(pipelineName, "default", StringComparison.OrdinalIgnoreCase))
            parts.Add(QuoteArgument(pipelineName));
        if (!string.IsNullOrWhiteSpace(profilePath))
        {
            parts.Add("--profile");
            parts.Add(QuoteArgument(profilePath));
        }
        if (!string.IsNullOrWhiteSpace(since))
        {
            parts.Add("--since");
            parts.Add(QuoteArgument(since));
        }
        if (!string.IsNullOrWhiteSpace(sinceMode))
        {
            parts.Add("--since-mode");
            parts.Add(sinceMode);
        }
        if (updateBaseline)
            parts.Add("--update-baseline");

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

    private static PublishJsonReport CreateJsonReport(
        string pipelineName,
        bool dryRun,
        int exitCode,
        string textOutput)
    {
        var messages = new List<PublishJsonMessage>();
        var exports = new List<PublishJsonExport>();
        foreach (var rawLine in textOutput.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            messages.Add(new PublishJsonMessage(ClassifyLine(line), line));
            if (TryParseDryRunExport(line, out var plannedExport))
                exports.Add(plannedExport);
        }

        var error = exitCode == 0
            ? null
            : messages.FirstOrDefault(message => message.Level == "error")?.Message ?? "Publish failed.";

        return new PublishJsonReport(
            "publish.v1",
            exitCode == 0,
            dryRun,
            pipelineName,
            exitCode,
            exports,
            messages,
            error);
    }

    private static string ClassifyLine(string line)
    {
        if (line.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Failed:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Precheck failed", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Failed ", StringComparison.OrdinalIgnoreCase))
        {
            return "error";
        }

        if (line.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Baseline NOT updated", StringComparison.OrdinalIgnoreCase))
        {
            return "warn";
        }

        return "info";
    }

    private static bool TryParseDryRunExport(string line, out PublishJsonExport plannedExport)
    {
        var match = DryRunExportLine.Match(line);
        if (!match.Success)
        {
            plannedExport = default!;
            return false;
        }

        var sheetText = match.Groups[3].Value;
        var usesPresetDefaultSheets = sheetText.Equals("(preset default)", StringComparison.OrdinalIgnoreCase);
        var sheets = usesPresetDefaultSheets
            ? Array.Empty<string>()
            : sheetText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        plannedExport = new PublishJsonExport(
            match.Groups[1].Value,
            match.Groups[2].Value,
            sheets,
            usesPresetDefaultSheets,
            match.Groups[4].Value);
        return true;
    }

    private sealed record PublishJsonReport(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("dryRun")] bool DryRun,
        [property: JsonPropertyName("pipeline")] string Pipeline,
        [property: JsonPropertyName("exitCode")] int ExitCode,
        [property: JsonPropertyName("plannedExports")] IReadOnlyList<PublishJsonExport> PlannedExports,
        [property: JsonPropertyName("messages")] IReadOnlyList<PublishJsonMessage> Messages,
        [property: JsonPropertyName("error")] string? Error)
    {
        public static PublishJsonReport Failure(string pipeline, bool dryRun, string error) =>
            new("publish.v1", false, dryRun, pipeline, 1, Array.Empty<PublishJsonExport>(),
                new[] { new PublishJsonMessage("error", error) }, error);
    }

    private sealed record PublishJsonExport(
        [property: JsonPropertyName("preset")] string Preset,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("sheets")] IReadOnlyList<string> Sheets,
        [property: JsonPropertyName("usesPresetDefaultSheets")] bool UsesPresetDefaultSheets,
        [property: JsonPropertyName("outputDir")] string OutputDir);

    private sealed record PublishReceiptExport(
        [property: JsonPropertyName("preset")] string Preset,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("configuredSheets")] IReadOnlyList<string> ConfiguredSheets,
        [property: JsonPropertyName("sheets")] IReadOnlyList<string> Sheets,
        [property: JsonPropertyName("views")] IReadOnlyList<string> Views,
        [property: JsonPropertyName("outputDir")] string OutputDir,
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("taskId")] string? TaskId,
        [property: JsonPropertyName("message")] string? Message);

    private sealed record PublishJsonMessage(
        [property: JsonPropertyName("level")] string Level,
        [property: JsonPropertyName("message")] string Message);
}
