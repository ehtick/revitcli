using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Output;
using RevitCli.Shared;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RevitCli.Commands;

public static class SchedulesCommand
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static Command Create(RevitClient client)
    {
        var command = new Command("schedules", "Ensure, export, and compare versioned schedule specs");
        command.AddCommand(CreateEnsureCommand(client));
        command.AddCommand(CreateBatchExportCommand(client));
        command.AddCommand(CreateCompareCommand());
        return command;
    }

    private static Command CreateEnsureCommand(RevitClient client)
    {
        var specOpt = new Option<string>("--spec", "Schedule spec YAML file or glob") { IsRequired = true };
        var planOutputOpt = new Option<string>("--plan-output", "Write schedule ensure plan JSON") { IsRequired = true };
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview only; no model writes are performed");
        var modeOpt = new Option<string>("--mode", () => "create-only", "Mode: create-only or sync-fields");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");
        var command = new Command("ensure", "Create a reviewed plan for missing or drifted schedules")
        {
            specOpt,
            planOutputOpt,
            dryRunOpt,
            modeOpt,
            outputOpt
        };

        command.SetHandler(async (string spec, string planOutput, bool dryRun, string mode, string output) =>
        {
            Environment.ExitCode = await ExecuteEnsureAsync(client, spec, planOutput, dryRun, mode, output, Console.Out);
        }, specOpt, planOutputOpt, dryRunOpt, modeOpt, outputOpt);
        return command;
    }

    private static Command CreateBatchExportCommand(RevitClient client)
    {
        var setOpt = new Option<string>("--set", "Schedule set name; loads .revitcli/schedules/<set>.yml") { IsRequired = true };
        var outputDirOpt = new Option<string>("--output-dir", "Directory for exported schedule files") { IsRequired = true };
        var formatOpt = new Option<string>("--format", () => "csv", "Export format: csv");
        var manifestOpt = new Option<string?>("--manifest", "Manifest JSON path");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");
        var command = new Command("batch-export", "Export all schedules in a named schedule spec set")
        {
            setOpt,
            outputDirOpt,
            formatOpt,
            manifestOpt,
            outputOpt
        };

        command.SetHandler(async (string set, string outputDir, string format, string? manifest, string output) =>
        {
            Environment.ExitCode = await ExecuteBatchExportAsync(client, set, outputDir, format, manifest, output, Console.Out);
        }, setOpt, outputDirOpt, formatOpt, manifestOpt, outputOpt);
        return command;
    }

    private static Command CreateCompareCommand()
    {
        var fromOpt = new Option<string>("--from", "Baseline schedule export directory") { IsRequired = true };
        var toOpt = new Option<string>("--to", "Current schedule export directory") { IsRequired = true };
        var keysOpt = new Option<string>("--keys", () => "Number,Mark", "Comma-separated key columns");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");
        var command = new Command("compare", "Compare two directories of exported schedule CSV files")
        {
            fromOpt,
            toOpt,
            keysOpt,
            outputOpt
        };

        command.SetHandler(async (string from, string to, string keys, string output) =>
        {
            Environment.ExitCode = await ExecuteCompareAsync(from, to, keys, output, Console.Out);
        }, fromOpt, toOpt, keysOpt, outputOpt);
        return command;
    }

    public static async Task<int> ExecuteEnsureAsync(
        RevitClient client,
        string specPattern,
        string planOutputPath,
        bool dryRun,
        string mode,
        string outputFormat,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        if (!dryRun)
        {
            await output.WriteLineAsync("Error: schedules ensure only creates reviewed plans. Use --dry-run, then apply reviewed schedule changes manually or through a future apply path.");
            return 1;
        }

        var normalizedMode = NormalizeMode(mode);
        if (normalizedMode == null)
        {
            await output.WriteLineAsync("Error: --mode must be create-only or sync-fields.");
            return 1;
        }

        ScheduleSpecSet spec;
        try
        {
            spec = LoadSpecSet(specPattern);
            ValidateSpecSet(spec);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var list = await client.ListSchedulesAsync();
        if (!list.Success)
        {
            await output.WriteLineAsync($"Error: {list.Error}");
            return 4;
        }

        var plan = CreateEnsurePlan(spec, list.Data ?? Array.Empty<ScheduleInfo>(), normalizedMode, specPattern, planOutputPath);
        SaveJson(planOutputPath, plan);
        await output.WriteLineAsync(Render(plan, normalizedOutput));
        return plan.Summary.ActionCount == 0 ? 2 : 0;
    }

    public static async Task<int> ExecuteBatchExportAsync(
        RevitClient client,
        string set,
        string outputDirectory,
        string format,
        string? manifestPath,
        string outputFormat,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        if (!string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync("Error: --format currently supports csv only.");
            return 1;
        }

        ScheduleSpecSet spec;
        var specPath = Path.Combine(".revitcli", "schedules", $"{set}.yml");
        try
        {
            spec = LoadSpecSet(specPath);
            ValidateSpecSet(spec);
            ValidateUniqueExportNames(spec);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var list = await client.ListSchedulesAsync();
        if (!list.Success)
        {
            await output.WriteLineAsync($"Error: {list.Error}");
            return 4;
        }

        var fullOutputDir = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDir);
        var finalManifestPath = Path.GetFullPath(manifestPath ?? Path.Combine(fullOutputDir, "schedule-export-manifest.json"));
        var status = await TryGetStatusAsync(client);
        var manifest = new ScheduleExportManifest(
            "schedule-export-manifest.v1",
            DateTime.UtcNow.ToString("o"),
            set,
            set,
            Path.GetFullPath(specPath),
            fullOutputDir,
            finalManifestPath,
            format.ToLowerInvariant(),
            BuildBatchExportCommand(set, outputDirectory, format, finalManifestPath),
            status?.DocumentPath,
            status?.DocumentName,
            status?.RevitVersion,
            new List<ScheduleExportManifestEntry>(),
            new List<ScheduleExportManifestIssue>());

        foreach (var schedule in spec.Schedules)
        {
            var request = new ScheduleExportRequest
            {
                ExistingName = schedule.Name,
                Category = string.IsNullOrWhiteSpace(schedule.Name) ? schedule.Category : null,
                Fields = schedule.Fields.Count == 0 ? null : schedule.Fields,
                Filter = schedule.Filter,
                Sort = schedule.Sort,
                SortDescending = schedule.SortDescending
            };
            var export = await client.ExportScheduleAsync(request);
            var fileName = SafeFileName(schedule.NameOrKey()) + ".csv";
            var fullPath = Path.Combine(fullOutputDir, fileName);
            var match = (list.Data ?? Array.Empty<ScheduleInfo>())
                .FirstOrDefault(item => string.Equals(item.Name, schedule.Name, StringComparison.OrdinalIgnoreCase));

            if (!export.Success)
            {
                manifest.Issues.Add(new ScheduleExportManifestIssue("error", "export-failed", schedule.NameOrKey(), export.Error ?? "unknown"));
                manifest.Entries.Add(new ScheduleExportManifestEntry(schedule.NameOrKey(), match?.Id, schedule.Category, fullPath, false, 0, 0, null, export.Error ?? "unknown"));
                continue;
            }

            var data = export.Data ?? new ScheduleData();
            File.WriteAllText(fullPath, FormatCsv(data));
            var fileInfo = new FileInfo(fullPath);
            manifest.Entries.Add(new ScheduleExportManifestEntry(
                schedule.NameOrKey(),
                match?.Id,
                schedule.Category,
                fullPath,
                true,
                data.TotalRows,
                fileInfo.Length,
                ComputeSha256Hex(fullPath),
                null));
        }

        SaveJson(finalManifestPath, manifest);
        await TryLogBatchExportLedgerOperationAsync(fullOutputDir, finalManifestPath, manifest);
        await output.WriteLineAsync(Render(manifest, normalizedOutput));
        return manifest.Issues.Any(issue => issue.Severity == "error") ? 2 : 0;
    }

    private static async Task TryLogBatchExportLedgerOperationAsync(
        string outputDirectory,
        string manifestPath,
        ScheduleExportManifest manifest)
    {
        try
        {
            var ledgerOutput = new StringWriter();
            var evidenceLinks = new List<string> { manifestPath };
            evidenceLinks.AddRange(manifest.Entries
                .Where(entry => entry.Success)
                .Select(entry => entry.OutputPath));

            var exitCode = await LedgerCommand.ExecuteAppendAsync(
                outputDirectory,
                action: "schedules.batch-export",
                category: manifest.Format,
                operatorName: null,
                status: manifest.Issues.Any(issue => issue.Severity == "error") ? "failed" : "succeeded",
                summary: $"Batch-exported {manifest.Entries.Count(entry => entry.Success)} schedule file(s) for set {manifest.Set}",
                timestamp: null,
                model: NormalizeLedgerText(manifest.DocumentName),
                modelPath: NormalizeLedgerText(manifest.ModelPath),
                planHash: null,
                artifactPath: manifestPath,
                receiptPath: null,
                receiptHash: null,
                rollbackPointer: null,
                evidenceLinks: evidenceLinks,
                yes: true,
                outputFormat: "json",
                output: ledgerOutput,
                commandName: "schedules",
                commandArgs: BuildBatchExportLedgerArgs(manifest.Set, manifest.OutputDirectory, manifest.Format, manifestPath),
                affectedElementCount: null,
                affectedElementIds: Array.Empty<long>(),
                revitVersion: NormalizeLedgerText(manifest.DocumentVersion));
            if (exitCode != 0)
                Console.Error.WriteLine($"[RevitCli] Schedule export ledger write failed (operation ledger incomplete): {ledgerOutput.ToString().Trim()}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException)
        {
            Console.Error.WriteLine($"[RevitCli] Schedule export ledger write failed (operation ledger incomplete): {ex.Message}");
        }
    }

    private static async Task<StatusInfo?> TryGetStatusAsync(RevitClient client)
    {
        try
        {
            var status = await client.GetStatusAsync();
            return status.Success ? status.Data : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    public static async Task<int> ExecuteCompareAsync(
        string fromDirectory,
        string toDirectory,
        string keys,
        string outputFormat,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalizedOutput, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        var keyColumns = ParseCsv(keys);
        if (keyColumns.Count == 0)
        {
            await output.WriteLineAsync("Error: --keys must include at least one column.");
            return 1;
        }

        ScheduleDiffReport report;
        try
        {
            report = CompareDirectories(fromDirectory, toDirectory, keyColumns);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        await output.WriteLineAsync(Render(report, normalizedOutput));
        return report.Summary.TotalChangedRows > 0 || report.Summary.AddedRows > 0 || report.Summary.RemovedRows > 0 ? 2 : 0;
    }

    private static ScheduleSpecSet LoadSpecSet(string pattern)
    {
        var files = ExpandFiles(pattern);
        if (files.Count == 0)
            throw new FileNotFoundException($"Schedule spec not found: {Path.GetFullPath(pattern)}");

        var schedules = new List<ScheduleSpec>();
        var setName = Path.GetFileNameWithoutExtension(files[0]);
        foreach (var file in files)
        {
            var loaded = Yaml.Deserialize<ScheduleSpecSet>(File.ReadAllText(file))
                ?? throw new InvalidOperationException($"Failed to parse schedule spec: {file}");
            if (!string.Equals(loaded.SchemaVersion, "schedule-spec.v1", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(loaded.SchemaVersion, "1", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported schedule spec schemaVersion '{loaded.SchemaVersion}' in {file}.");
            }

            if (!string.IsNullOrWhiteSpace(loaded.Set))
                setName = loaded.Set;
            schedules.AddRange(loaded.Schedules);
        }

        return new ScheduleSpecSet
        {
            SchemaVersion = "schedule-spec.v1",
            Set = setName,
            Schedules = schedules
        };
    }

    private static void ValidateSpecSet(ScheduleSpecSet spec)
    {
        if (!string.Equals(spec.SchemaVersion, "schedule-spec.v1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(spec.SchemaVersion, "1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported schedule spec schemaVersion '{spec.SchemaVersion}'.");
        }

        if (spec.Schedules.Count == 0)
            throw new InvalidOperationException("Schedule spec must contain at least one schedule.");

        foreach (var schedule in spec.Schedules)
        {
            if (string.IsNullOrWhiteSpace(schedule.Name))
                throw new InvalidOperationException("Each schedule spec requires name.");
            if (string.IsNullOrWhiteSpace(schedule.Category))
                throw new InvalidOperationException($"Schedule spec {schedule.Name} requires category.");
            if (schedule.Fields.Count == 0)
                throw new InvalidOperationException($"Schedule spec {schedule.Name} requires fields.");
            if (schedule.KeyColumns.Count == 0)
                schedule.KeyColumns.AddRange(schedule.Fields.Take(1));
        }
    }

    private static void ValidateUniqueExportNames(ScheduleSpecSet spec)
    {
        var exportNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var schedule in spec.Schedules)
        {
            var exportName = SafeFileName(schedule.NameOrKey()) + ".csv";
            if (!exportNames.Add(exportName))
                throw new InvalidOperationException($"Schedule specs generate duplicate export file name '{exportName}'. Rename one schedule before batch export.");
        }
    }

    private static ScheduleEnsurePlan CreateEnsurePlan(
        ScheduleSpecSet spec,
        IReadOnlyList<ScheduleInfo> existing,
        string mode,
        string specPattern,
        string planPath)
    {
        var actions = new List<ScheduleEnsureAction>();
        var baselines = new List<ScheduleEnsureBaseline>();
        foreach (var schedule in spec.Schedules.OrderBy(schedule => schedule.Name, StringComparer.OrdinalIgnoreCase))
        {
            var match = existing.FirstOrDefault(item => string.Equals(item.Name, schedule.Name, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                actions.Add(new ScheduleEnsureAction("create", schedule.Name, schedule.Category, schedule.Fields, schedule.Filter, schedule.Sort, schedule.SortDescending, "Schedule does not exist."));
                continue;
            }

            baselines.Add(new ScheduleEnsureBaseline(
                match.Id,
                match.Name,
                match.Category,
                match.FieldCount,
                match.RowCount,
                Array.Empty<string>(),
                null,
                null,
                false));
            if (mode == "sync-fields" && match.FieldCount != schedule.Fields.Count)
            {
                actions.Add(new ScheduleEnsureAction("sync-fields", schedule.Name, schedule.Category, schedule.Fields, schedule.Filter, schedule.Sort, schedule.SortDescending, $"Field count differs: model={match.FieldCount}, spec={schedule.Fields.Count}."));
            }
        }

        return new ScheduleEnsurePlan(
            "schedule-ensure-plan.v1",
            DateTime.UtcNow.ToString("o"),
            Path.GetFullPath(specPattern),
            Path.GetFullPath(planPath),
            mode,
            true,
            new ScheduleEnsureSummary(spec.Schedules.Count, existing.Count, actions.Count, baselines.Count),
            actions,
            baselines,
            new[]
            {
                $"revitcli plan show \"{Path.GetFullPath(planPath)}\" --output markdown",
                "Review schedule creation/sync actions before applying in Revit."
            });
    }

    private static ScheduleDiffReport CompareDirectories(string fromDirectory, string toDirectory, IReadOnlyList<string> keys)
    {
        var from = Path.GetFullPath(fromDirectory);
        var to = Path.GetFullPath(toDirectory);
        if (!Directory.Exists(from))
            throw new DirectoryNotFoundException($"Baseline directory not found: {from}");
        if (!Directory.Exists(to))
            throw new DirectoryNotFoundException($"Current directory not found: {to}");

        var fromFiles = Directory.GetFiles(from, "*.csv")
            .ToDictionary(path => Path.GetFileName(path) ?? path, StringComparer.OrdinalIgnoreCase);
        var toFiles = Directory.GetFiles(to, "*.csv")
            .ToDictionary(path => Path.GetFileName(path) ?? path, StringComparer.OrdinalIgnoreCase);
        var files = fromFiles.Keys.Concat(toFiles.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
        var diffs = new List<ScheduleFileDiff>();

        foreach (var file in files)
        {
            if (!fromFiles.TryGetValue(file, out var beforePath))
            {
                var current = ReadCsvRows(toFiles[file], keys);
                var currentEvidence = CreateFileEvidence(toFiles[file]);
                diffs.Add(new ScheduleFileDiff(
                    file,
                    null,
                    currentEvidence.Path,
                    null,
                    currentEvidence.Sha256,
                    null,
                    currentEvidence.Bytes,
                    0,
                    current.Count,
                    0,
                    current.Count,
                    0));
                continue;
            }

            if (!toFiles.TryGetValue(file, out var afterPath))
            {
                var before = ReadCsvRows(beforePath, keys);
                var beforeEvidence = CreateFileEvidence(beforePath);
                diffs.Add(new ScheduleFileDiff(
                    file,
                    beforeEvidence.Path,
                    null,
                    beforeEvidence.Sha256,
                    null,
                    beforeEvidence.Bytes,
                    null,
                    before.Count,
                    0,
                    0,
                    0,
                    before.Count));
                continue;
            }

            var beforeRows = ReadCsvRows(beforePath, keys);
            var afterRows = ReadCsvRows(afterPath, keys);
            var changed = beforeRows.Keys.Intersect(afterRows.Keys, StringComparer.OrdinalIgnoreCase)
                .Count(key => !RowsEqual(beforeRows[key], afterRows[key]));
            var added = afterRows.Keys.Except(beforeRows.Keys, StringComparer.OrdinalIgnoreCase).Count();
            var removed = beforeRows.Keys.Except(afterRows.Keys, StringComparer.OrdinalIgnoreCase).Count();
            var fromEvidence = CreateFileEvidence(beforePath);
            var toEvidence = CreateFileEvidence(afterPath);
            diffs.Add(new ScheduleFileDiff(
                file,
                fromEvidence.Path,
                toEvidence.Path,
                fromEvidence.Sha256,
                toEvidence.Sha256,
                fromEvidence.Bytes,
                toEvidence.Bytes,
                beforeRows.Count,
                afterRows.Count,
                changed,
                added,
                removed));
        }

        return new ScheduleDiffReport(
            "schedule-diff-report.v1",
            DateTime.UtcNow.ToString("o"),
            from,
            to,
            keys,
            new ScheduleDiffSummary(diffs.Count, diffs.Sum(diff => diff.ChangedRows), diffs.Sum(diff => diff.AddedRows), diffs.Sum(diff => diff.RemovedRows)),
            diffs);
    }

    private static Dictionary<string, Dictionary<string, string>> ReadCsvRows(string path, IReadOnlyList<string> keys)
    {
        var csv = CsvParser.ParseFile(path);
        var missing = keys.Where(key => !csv.Headers.Contains(key, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"{Path.GetFileName(path)} missing key column(s): {string.Join(", ", missing)}");
        var rows = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in csv.Rows)
        {
            var dict = csv.Headers
                .Select((header, index) => new { header, value = index < row.Count ? row[index] : "" })
                .ToDictionary(item => item.header, item => item.value, StringComparer.OrdinalIgnoreCase);
            var key = string.Join("|", keys.Select(column => dict.TryGetValue(column, out var value) ? value : ""));
            if (!rows.TryAdd(key, dict))
                throw new InvalidOperationException($"{Path.GetFileName(path)} contains duplicate schedule diff key '{key}' for --keys {string.Join(", ", keys)}.");
        }

        return rows;
    }

    private static bool RowsEqual(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        var keys = left.Keys.Concat(right.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
        return keys.All(key =>
            string.Equals(
                left.TryGetValue(key, out var leftValue) ? leftValue : "",
                right.TryGetValue(key, out var rightValue) ? rightValue : "",
                StringComparison.Ordinal));
    }

    private static string Render(object value, string outputFormat) =>
        outputFormat switch
        {
            "json" => JsonSerializer.Serialize(value, TerminalJsonOptions.PrettyCamel),
            "markdown" => RenderMarkdown(value),
            _ => RenderTable(value)
        };

    private static string RenderTable(object value)
    {
        return value switch
        {
            ScheduleEnsurePlan plan => $"Schedule ensure plan ({plan.SchemaVersion}): specs={plan.Summary.SpecCount}, actions={plan.Summary.ActionCount}, baselines={plan.Summary.BaselineCount}",
            ScheduleExportManifest manifest => $"Schedule export manifest ({manifest.SchemaVersion}): entries={manifest.Entries.Count}, issues={manifest.Issues.Count}, output={manifest.OutputDirectory}",
            ScheduleDiffReport report => $"Schedule diff report ({report.SchemaVersion}): files={report.Summary.FileCount}, changed={report.Summary.TotalChangedRows}, added={report.Summary.AddedRows}, removed={report.Summary.RemovedRows}",
            _ => value.ToString() ?? ""
        };
    }

    private static string RenderMarkdown(object value)
    {
        var writer = new StringWriter();
        switch (value)
        {
            case ScheduleEnsurePlan plan:
                writer.WriteLine("# Schedule Ensure Plan");
                writer.WriteLine();
                writer.WriteLine($"- Schema: `{plan.SchemaVersion}`");
                writer.WriteLine($"- Mode: `{plan.Mode}`");
                writer.WriteLine($"- Actions: `{plan.Summary.ActionCount}`");
                writer.WriteLine($"- Baselines: `{plan.Summary.BaselineCount}`");
                writer.WriteLine();
                writer.WriteLine("| Action | Name | Category | Fields | Reason |");
                writer.WriteLine("| --- | --- | --- | --- | --- |");
                foreach (var action in plan.Actions)
                    writer.WriteLine($"| `{action.Action}` | {EscapeTable(action.Name)} | {EscapeTable(action.Category)} | {EscapeTable(string.Join(", ", action.Fields))} | {EscapeTable(action.Reason)} |");
                break;
            case ScheduleExportManifest manifest:
                writer.WriteLine("# Schedule Export Manifest");
                writer.WriteLine();
                writer.WriteLine($"- Schema: `{manifest.SchemaVersion}`");
                writer.WriteLine($"- Entries: `{manifest.Entries.Count}`");
                writer.WriteLine($"- Issues: `{manifest.Issues.Count}`");
                writer.WriteLine();
                writer.WriteLine("| Schedule | Rows | Bytes | SHA256 | Success | Path |");
                writer.WriteLine("| --- | ---: | ---: | --- | --- | --- |");
                foreach (var entry in manifest.Entries)
                    writer.WriteLine($"| {EscapeTable(entry.ScheduleName)} | {entry.RowCount} | {entry.Bytes} | `{EscapeInline(ShortHash(entry.Sha256))}` | `{entry.Success.ToString().ToLowerInvariant()}` | `{EscapeInline(entry.OutputPath)}` |");
                break;
            case ScheduleDiffReport report:
                writer.WriteLine("# Schedule Diff Report");
                writer.WriteLine();
                writer.WriteLine($"- Schema: `{report.SchemaVersion}`");
                writer.WriteLine($"- Changed rows: `{report.Summary.TotalChangedRows}`");
                writer.WriteLine($"- Added rows: `{report.Summary.AddedRows}`");
                writer.WriteLine($"- Removed rows: `{report.Summary.RemovedRows}`");
                writer.WriteLine();
                writer.WriteLine("| File | Before | After | Changed | Added | Removed | Before SHA256 | After SHA256 |");
                writer.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: | --- | --- |");
                foreach (var diff in report.Files)
                    writer.WriteLine($"| {EscapeTable(diff.File)} | {diff.BeforeRows} | {diff.AfterRows} | {diff.ChangedRows} | {diff.AddedRows} | {diff.RemovedRows} | `{EscapeInline(ShortHash(diff.BeforeSha256))}` | `{EscapeInline(ShortHash(diff.AfterSha256))}` |");
                break;
        }

        return writer.ToString().TrimEnd();
    }

    private static IReadOnlyList<string> ExpandFiles(string pattern)
    {
        var full = Path.GetFullPath(pattern);
        if (!pattern.Contains('*', StringComparison.Ordinal) && !pattern.Contains('?', StringComparison.Ordinal))
            return File.Exists(full) ? new[] { full } : Array.Empty<string>();
        var directory = Path.GetDirectoryName(full);
        var filePattern = Path.GetFileName(full);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return Array.Empty<string>();
        return Directory.GetFiles(directory, filePattern).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? NormalizeMode(string mode)
    {
        var normalized = string.IsNullOrWhiteSpace(mode) ? "create-only" : mode.Trim().ToLowerInvariant();
        return normalized is "create-only" or "sync-fields" ? normalized : null;
    }

    private static List<string> ParseCsv(string? value) =>
        (value ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

    private static string FormatCsv(ScheduleData data)
    {
        var lines = new List<string> { string.Join(",", data.Columns.Select(EscapeCsvField)) };
        lines.AddRange(data.Rows.Select(row => string.Join(",", data.Columns.Select(column => EscapeCsvField(row.TryGetValue(column, out var value) ? value : "")))));
        return string.Join(Environment.NewLine, lines);
    }

    private static string EscapeCsvField(string value) =>
        value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static void SaveJson<T>(string path, T value)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(full, JsonSerializer.Serialize(value, TerminalJsonOptions.PrettyCamel));
    }

    private static ScheduleFileEvidence CreateFileEvidence(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        return new ScheduleFileEvidence(fullPath, info.Length, ComputeSha256Hex(fullPath));
    }

    private static string BuildBatchExportCommand(string set, string outputDirectory, string format, string manifestPath)
    {
        return string.Join(
            " ",
            "revitcli",
            "schedules",
            "batch-export",
            "--set",
            QuoteArgument(set),
            "--output-dir",
            QuoteArgument(Path.GetFullPath(outputDirectory)),
            "--format",
            QuoteArgument(format.ToLowerInvariant()),
            "--manifest",
            QuoteArgument(Path.GetFullPath(manifestPath)));
    }

    private static string[] BuildBatchExportLedgerArgs(string set, string outputDirectory, string format, string manifestPath) =>
        new[]
        {
            "schedules",
            "batch-export",
            "--set",
            set,
            "--output-dir",
            outputDirectory,
            "--format",
            format.ToLowerInvariant(),
            "--manifest",
            Path.GetFullPath(manifestPath)
        };

    private static string? NormalizeLedgerText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string QuoteArgument(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
    }

    private static string ComputeSha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim('_');
        return string.IsNullOrWhiteSpace(safe) ? "schedule" : safe;
    }

    private static string EscapeInline(string? value) =>
        (value ?? "").Replace("`", "'", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string EscapeTable(string? value) =>
        EscapeInline(value).Replace("|", "\\|", StringComparison.Ordinal);

    private static string ShortHash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Length <= 12 ? value : value[..12];

    private sealed record ScheduleFileEvidence(string Path, long Bytes, string Sha256);

    public sealed class ScheduleSpecSet
    {
        public string SchemaVersion { get; set; } = "schedule-spec.v1";
        public string Set { get; set; } = "";
        public List<ScheduleSpec> Schedules { get; set; } = new();
    }

    public sealed class ScheduleSpec
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public List<string> Fields { get; set; } = new();
        public string? Filter { get; set; }
        public string? Sort { get; set; }
        public bool SortDescending { get; set; }
        public List<string> KeyColumns { get; set; } = new();

        public string NameOrKey() => string.IsNullOrWhiteSpace(Name) ? Category : Name;
    }

    public sealed record ScheduleEnsurePlan(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
        [property: JsonPropertyName("specPath")] string SpecPath,
        [property: JsonPropertyName("planPath")] string PlanPath,
        [property: JsonPropertyName("mode")] string Mode,
        [property: JsonPropertyName("dryRun")] bool DryRun,
        [property: JsonPropertyName("summary")] ScheduleEnsureSummary Summary,
        [property: JsonPropertyName("actions")] IReadOnlyList<ScheduleEnsureAction> Actions,
        [property: JsonPropertyName("baselines")] IReadOnlyList<ScheduleEnsureBaseline> Baselines,
        [property: JsonPropertyName("commands")] IReadOnlyList<string> Commands);

    public sealed record ScheduleEnsureSummary(
        [property: JsonPropertyName("specCount")] int SpecCount,
        [property: JsonPropertyName("existingCount")] int ExistingCount,
        [property: JsonPropertyName("actionCount")] int ActionCount,
        [property: JsonPropertyName("baselineCount")] int BaselineCount);

    public sealed record ScheduleEnsureAction(
        [property: JsonPropertyName("action")] string Action,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("fields")] IReadOnlyList<string> Fields,
        [property: JsonPropertyName("filter")] string? Filter,
        [property: JsonPropertyName("sort")] string? Sort,
        [property: JsonPropertyName("sortDescending")] bool SortDescending,
        [property: JsonPropertyName("reason")] string Reason);

    public sealed record ScheduleEnsureBaseline(
        [property: JsonPropertyName("scheduleId")] long ScheduleId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("fieldCount")] int FieldCount,
        [property: JsonPropertyName("rowCount")] int RowCount,
        [property: JsonPropertyName("fields")] IReadOnlyList<string> Fields,
        [property: JsonPropertyName("filter")] string? Filter,
        [property: JsonPropertyName("sort")] string? Sort,
        [property: JsonPropertyName("sortDescending")] bool SortDescending);

    public sealed record ScheduleExportManifest(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
        [property: JsonPropertyName("set")] string Set,
        [property: JsonPropertyName("profile")] string Profile,
        [property: JsonPropertyName("specPath")] string SpecPath,
        [property: JsonPropertyName("outputDirectory")] string OutputDirectory,
        [property: JsonPropertyName("manifestPath")] string ManifestPath,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("modelPath")] string? ModelPath,
        [property: JsonPropertyName("documentName")] string? DocumentName,
        [property: JsonPropertyName("documentVersion")] string? DocumentVersion,
        [property: JsonPropertyName("entries")] List<ScheduleExportManifestEntry> Entries,
        [property: JsonPropertyName("issues")] List<ScheduleExportManifestIssue> Issues);

    public sealed record ScheduleExportManifestEntry(
        [property: JsonPropertyName("scheduleName")] string ScheduleName,
        [property: JsonPropertyName("scheduleId")] long? ScheduleId,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("outputPath")] string OutputPath,
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("rowCount")] int RowCount,
        [property: JsonPropertyName("bytes")] long Bytes,
        [property: JsonPropertyName("sha256")] string? Sha256,
        [property: JsonPropertyName("error")] string? Error);

    public sealed record ScheduleExportManifestIssue(
        [property: JsonPropertyName("severity")] string Severity,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("scheduleName")] string ScheduleName,
        [property: JsonPropertyName("message")] string Message);

    public sealed record ScheduleDiffReport(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("createdAtUtc")] string CreatedAtUtc,
        [property: JsonPropertyName("fromDirectory")] string FromDirectory,
        [property: JsonPropertyName("toDirectory")] string ToDirectory,
        [property: JsonPropertyName("keys")] IReadOnlyList<string> Keys,
        [property: JsonPropertyName("summary")] ScheduleDiffSummary Summary,
        [property: JsonPropertyName("files")] IReadOnlyList<ScheduleFileDiff> Files);

    public sealed record ScheduleDiffSummary(
        [property: JsonPropertyName("fileCount")] int FileCount,
        [property: JsonPropertyName("totalChangedRows")] int TotalChangedRows,
        [property: JsonPropertyName("addedRows")] int AddedRows,
        [property: JsonPropertyName("removedRows")] int RemovedRows);

    public sealed record ScheduleFileDiff(
        [property: JsonPropertyName("file")] string File,
        [property: JsonPropertyName("beforePath")] string? BeforePath,
        [property: JsonPropertyName("afterPath")] string? AfterPath,
        [property: JsonPropertyName("beforeSha256")] string? BeforeSha256,
        [property: JsonPropertyName("afterSha256")] string? AfterSha256,
        [property: JsonPropertyName("beforeBytes")] long? BeforeBytes,
        [property: JsonPropertyName("afterBytes")] long? AfterBytes,
        [property: JsonPropertyName("beforeRows")] int BeforeRows,
        [property: JsonPropertyName("afterRows")] int AfterRows,
        [property: JsonPropertyName("changedRows")] int ChangedRows,
        [property: JsonPropertyName("addedRows")] int AddedRows,
        [property: JsonPropertyName("removedRows")] int RemovedRows);
}
