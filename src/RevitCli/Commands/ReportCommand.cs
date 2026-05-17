using System;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RevitCli.History;
using RevitCli.Journal;
using RevitCli.Output;
using RevitCli.Shared;

namespace RevitCli.Commands;

public static class ReportCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static Command Create()
    {
        var command = new Command("report", "Generate local project reports from history and journal data");
        command.AddCommand(CreateWeeklyCommand());
        return command;
    }

    private static Command CreateWeeklyCommand()
    {
        var windowOpt = new Option<string>("--window", () => "7d", "History window, e.g. 7d, 30d, 24h");
        var dirOpt = new Option<string?>("--dir", "Project directory; defaults to current directory");
        var historyDirOpt = new Option<string?>("--history-dir", "Override .revitcli/history directory");
        var journalOpt = new Option<string?>("--journal", "Override .revitcli/journal.jsonl path");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json | markdown");
        var reportOpt = new Option<string?>("--report", "Write report to file (.md and .json infer format)");

        var command = new Command("weekly", "Generate a weekly summary from history, diff review, score, and journal")
        {
            windowOpt,
            dirOpt,
            historyDirOpt,
            journalOpt,
            outputOpt,
            reportOpt,
        };

        command.SetHandler(async (
            string window,
            string? dir,
            string? historyDir,
            string? journal,
            string outputFormat,
            string? reportPath) =>
        {
            Environment.ExitCode = await ExecuteWeeklyAsync(
                window,
                dir,
                historyDir,
                journal,
                outputFormat,
                reportPath,
                Console.Out);
        }, windowOpt, dirOpt, historyDirOpt, journalOpt, outputOpt, reportOpt);

        return command;
    }

    public static async Task<int> ExecuteWeeklyAsync(
        string window,
        string? projectDirectory,
        string? historyDirectory,
        string? journalPath,
        string outputFormat,
        string? reportPath,
        TextWriter output,
        DateTimeOffset? now = null)
    {
        TimeSpan windowSpan;
        try
        {
            windowSpan = HistoryCommand.ParseWindow(window);
        }
        catch (FormatException ex)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        var projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectDirectory!);
        var store = string.IsNullOrWhiteSpace(historyDirectory)
            ? HistoryStore.ForProject(projectRoot)
            : new HistoryStore(historyDirectory!);

        if (!Directory.Exists(store.RootDirectory))
        {
            await output.WriteLineAsync(
                $"Error: history store not initialised. Run 'revitcli history init' (looked in {store.RootDirectory}).");
            return 1;
        }

        var generatedAt = (now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var cutoff = generatedAt - windowSpan;
        IReadOnlyList<SnapshotMetadata> entries;
        try
        {
            entries = await store.ListAsync(includeFixBaselines: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await output.WriteLineAsync($"Error: failed to read history: {ex.Message}");
            return 1;
        }

        var windowEntries = entries
            .Select(entry => new SnapshotPoint(entry, ParseTimestamp(entry.CapturedAt)))
            .Where(point => point.CapturedAt >= cutoff && point.CapturedAt <= generatedAt)
            .OrderBy(point => point.CapturedAt)
            .ToList();

        if (windowEntries.Count == 0)
        {
            await output.WriteLineAsync($"No snapshots in window ({window}).");
            return 0;
        }

        var report = new WeeklyReport
        {
            GeneratedAt = generatedAt.ToString("o", CultureInfo.InvariantCulture),
            Window = window,
            HistoryDirectory = store.RootDirectory,
            SnapshotCount = windowEntries.Count,
        };

        var firstPoint = windowEntries.First();
        var latestPoint = windowEntries.Last();
        ModelSnapshot firstSnapshot;
        ModelSnapshot latestSnapshot;
        try
        {
            firstSnapshot = await ReadSnapshotAsync(store, firstPoint.Metadata, "first", output);
            latestSnapshot = await ReadSnapshotAsync(store, latestPoint.Metadata, "latest", output);
        }
        catch (InvalidOperationException)
        {
            return 1;
        }

        report.FirstSnapshot = BuildSnapshotSummary(firstPoint.Metadata, firstSnapshot);
        report.LatestSnapshot = BuildSnapshotSummary(latestPoint.Metadata, latestSnapshot);
        report.ScoreDelta = Delta(report.FirstSnapshot.Score, report.LatestSnapshot.Score);
        report.ElementDelta = report.LatestSnapshot.ElementCount - report.FirstSnapshot.ElementCount;

        if (windowEntries.Count >= 2)
        {
            try
            {
                var diff = SnapshotDiffer.Diff(
                    firstSnapshot,
                    latestSnapshot,
                    firstPoint.Metadata.Id,
                    latestPoint.Metadata.Id);
                report.DiffReview = BuildDiffSummary(DiffReviewRenderer.Build(diff));
            }
            catch (InvalidOperationException ex)
            {
                report.Issues.Add($"Diff review unavailable: {ex.Message}");
            }
        }

        AddJournalSummary(report, ResolveJournalPath(projectRoot, journalPath), cutoff, generatedAt);

        var effectiveFormat = InferFormat(outputFormat, reportPath);
        var rendered = Render(report, effectiveFormat);
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            var target = Path.GetFullPath(reportPath!);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            await File.WriteAllTextAsync(target, rendered);
            await output.WriteLineAsync($"Report saved to {reportPath}");
        }
        else
        {
            await output.WriteLineAsync(rendered);
        }

        return 0;
    }

    private static async Task<ModelSnapshot> ReadSnapshotAsync(
        HistoryStore store,
        SnapshotMetadata metadata,
        string label,
        TextWriter output)
    {
        try
        {
            var snapshot = await store.ReadAsync(metadata);
            if (snapshot != null)
            {
                return snapshot;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            await output.WriteLineAsync($"Error: failed to read {label} snapshot {metadata.Id}: {ex.Message}");
            throw new InvalidOperationException("failed to read snapshot", ex);
        }

        await output.WriteLineAsync($"Error: snapshot file missing for {label} snapshot {metadata.Id}.");
        throw new InvalidOperationException("snapshot missing");
    }

    private static WeeklySnapshotSummary BuildSnapshotSummary(SnapshotMetadata metadata, ModelSnapshot snapshot)
    {
        var score = ScoreCommand.SnapshotScore(snapshot);
        return new WeeklySnapshotSummary
        {
            Id = metadata.Id,
            CapturedAt = metadata.CapturedAt,
            Source = metadata.Source,
            DocumentPath = metadata.DocumentPath,
            ElementCount = metadata.ElementCount,
            Score = score.HasValue ? Math.Round(score.Value, 1) : null,
            SheetCount = snapshot.Summary?.SheetCount > 0 ? snapshot.Summary.SheetCount : snapshot.Sheets.Count,
            ScheduleCount = snapshot.Summary?.ScheduleCount > 0 ? snapshot.Summary.ScheduleCount : snapshot.Schedules.Count,
        };
    }

    private static WeeklyDiffSummary BuildDiffSummary(DiffReviewReport review) =>
        new()
        {
            HighestSeverity = review.HighestSeverity,
            TotalChanges = review.TotalChanges,
            SeverityCounts = review.SeverityCounts.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase),
            RecommendedActions = review.RecommendedActions.Take(5).ToList(),
        };

    private static void AddJournalSummary(
        WeeklyReport report,
        string journalPath,
        DateTimeOffset cutoff,
        DateTimeOffset generatedAt)
    {
        report.JournalPath = journalPath;
        if (!File.Exists(journalPath))
        {
            report.Issues.Add($"Journal not found: {journalPath}");
            report.Journal = new WeeklyJournalSummary();
            return;
        }

        try
        {
            var journal = JournalReader.Read(journalPath);
            var windowEntries = journal.Entries
                .Where(entry => IsInWindow(entry.Timestamp, cutoff, generatedAt))
                .ToList();
            var stats = JournalReader.GetStats(new JournalReadResult(journal.JournalPath, windowEntries));
            report.Journal = new WeeklyJournalSummary
            {
                EntryCount = stats.EntryCount,
                AffectedElementCount = stats.AffectedElementCount,
                FirstTimestamp = stats.FirstTimestamp,
                LastTimestamp = stats.LastTimestamp,
                Actions = stats.Actions.Take(5).Select(item => new WeeklyNamedCount(item.Action, item.Count)).ToList(),
                Categories = stats.Categories.Take(5).Select(item => new WeeklyNamedCount(item.Name, item.Count)).ToList(),
                Users = stats.Users.Take(5).Select(item => new WeeklyNamedCount(item.Name, item.Count)).ToList(),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            report.Issues.Add($"Journal unavailable: {ex.Message}");
            report.Journal = new WeeklyJournalSummary();
        }
    }

    private static string Render(WeeklyReport report, string format) =>
        format.ToLowerInvariant() switch
        {
            "json" => JsonSerializer.Serialize(report, JsonOpts),
            "markdown" or "md" => RenderMarkdown(report),
            _ => RenderTable(report),
        };

    private static string RenderTable(WeeklyReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Weekly report");
        sb.AppendLine($"Generated: {report.GeneratedAt}");
        sb.AppendLine($"Window: {report.Window}");
        sb.AppendLine($"History snapshots: {report.SnapshotCount}");
        sb.AppendLine($"First: {report.FirstSnapshot.CapturedAt} {report.FirstSnapshot.Id} source={report.FirstSnapshot.Source}");
        sb.AppendLine($"Latest: {report.LatestSnapshot.CapturedAt} {report.LatestSnapshot.Id} source={report.LatestSnapshot.Source}");
        sb.AppendLine($"Score: {FormatNullable(report.FirstSnapshot.Score)} -> {FormatNullable(report.LatestSnapshot.Score)} ({FormatDelta(report.ScoreDelta)})");
        sb.AppendLine($"Elements: {report.FirstSnapshot.ElementCount} -> {report.LatestSnapshot.ElementCount} ({FormatSigned(report.ElementDelta)})");
        sb.AppendLine($"Sheets: {report.LatestSnapshot.SheetCount}; schedules: {report.LatestSnapshot.ScheduleCount}");

        if (report.DiffReview != null)
        {
            sb.AppendLine($"Diff review: highest={report.DiffReview.HighestSeverity}, changes={report.DiffReview.TotalChanges}");
            foreach (var pair in report.DiffReview.SeverityCounts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  {pair.Key}: {pair.Value}");
            }
        }

        sb.AppendLine($"Journal entries: {report.Journal.EntryCount}; affected elements: {report.Journal.AffectedElementCount}");
        AppendCounts(sb, "Actions", report.Journal.Actions);
        AppendCounts(sb, "Categories", report.Journal.Categories);
        AppendCounts(sb, "Users", report.Journal.Users);

        if (report.DiffReview?.RecommendedActions.Count > 0)
        {
            sb.AppendLine("Recommended actions:");
            foreach (var action in report.DiffReview.RecommendedActions)
            {
                sb.AppendLine($"  - {action}");
            }
        }

        if (report.Issues.Count > 0)
        {
            sb.AppendLine("Issues:");
            foreach (var issue in report.Issues)
            {
                sb.AppendLine($"  - {issue}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderMarkdown(WeeklyReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Weekly RevitCli Report");
        sb.AppendLine();
        sb.AppendLine($"- Generated: `{report.GeneratedAt}`");
        sb.AppendLine($"- Window: `{report.Window}`");
        sb.AppendLine($"- Snapshots: `{report.SnapshotCount}`");
        sb.AppendLine($"- Score: `{FormatNullable(report.FirstSnapshot.Score)} -> {FormatNullable(report.LatestSnapshot.Score)} ({FormatDelta(report.ScoreDelta)})`");
        sb.AppendLine($"- Elements: `{report.FirstSnapshot.ElementCount} -> {report.LatestSnapshot.ElementCount} ({FormatSigned(report.ElementDelta)})`");
        sb.AppendLine($"- Sheets: `{report.LatestSnapshot.SheetCount}`");
        sb.AppendLine($"- Schedules: `{report.LatestSnapshot.ScheduleCount}`");

        if (report.DiffReview != null)
        {
            sb.AppendLine();
            sb.AppendLine("## Diff Review");
            sb.AppendLine();
            sb.AppendLine($"- Highest severity: `{report.DiffReview.HighestSeverity}`");
            sb.AppendLine($"- Total changes: `{report.DiffReview.TotalChanges}`");
            foreach (var pair in report.DiffReview.SeverityCounts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- {pair.Key}: `{pair.Value}`");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Journal");
        sb.AppendLine();
        sb.AppendLine($"- Entries: `{report.Journal.EntryCount}`");
        sb.AppendLine($"- Affected elements: `{report.Journal.AffectedElementCount}`");
        AppendMarkdownCounts(sb, "Actions", report.Journal.Actions);
        AppendMarkdownCounts(sb, "Categories", report.Journal.Categories);
        AppendMarkdownCounts(sb, "Users", report.Journal.Users);

        if (report.DiffReview?.RecommendedActions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Recommended Actions");
            foreach (var action in report.DiffReview.RecommendedActions)
            {
                sb.AppendLine($"- {action}");
            }
        }

        if (report.Issues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Issues");
            foreach (var issue in report.Issues)
            {
                sb.AppendLine($"- {issue}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendCounts(StringBuilder sb, string title, IReadOnlyList<WeeklyNamedCount> counts)
    {
        if (counts.Count == 0)
        {
            return;
        }

        sb.AppendLine($"{title}:");
        foreach (var item in counts)
        {
            sb.AppendLine($"  {item.Name}: {item.Count}");
        }
    }

    private static void AppendMarkdownCounts(StringBuilder sb, string title, IReadOnlyList<WeeklyNamedCount> counts)
    {
        if (counts.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"### {title}");
        foreach (var item in counts)
        {
            sb.AppendLine($"- {item.Name}: `{item.Count}`");
        }
    }

    private static string ResolveJournalPath(string projectRoot, string? journalPath) =>
        string.IsNullOrWhiteSpace(journalPath)
            ? Path.Combine(projectRoot, ".revitcli", "journal.jsonl")
            : Path.GetFullPath(journalPath!);

    private static string InferFormat(string outputFormat, string? reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            return outputFormat;
        }

        return Path.GetExtension(reportPath).ToLowerInvariant() switch
        {
            ".md" => "markdown",
            ".json" => "json",
            _ => outputFormat,
        };
    }

    private static bool IsInWindow(string? timestampText, DateTimeOffset cutoff, DateTimeOffset generatedAt) =>
        TryParseTimestamp(timestampText, out var timestamp) &&
        timestamp >= cutoff &&
        timestamp <= generatedAt;

    private static DateTimeOffset ParseTimestamp(string value) =>
        TryParseTimestamp(value, out var timestamp) ? timestamp : DateTimeOffset.MinValue;

    private static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out timestamp);
    }

    private static double? Delta(double? first, double? latest) =>
        first.HasValue && latest.HasValue ? Math.Round(latest.Value - first.Value, 1) : null;

    private static string FormatNullable(double? value) =>
        value.HasValue ? value.Value.ToString("0.#", CultureInfo.InvariantCulture) : "n/a";

    private static string FormatDelta(double? value) =>
        value.HasValue ? FormatSigned(value.Value) : "n/a";

    private static string FormatSigned(double value) =>
        value >= 0
            ? "+" + value.ToString("0.#", CultureInfo.InvariantCulture)
            : value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string FormatSigned(int value) =>
        value >= 0
            ? "+" + value.ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);

    private sealed record SnapshotPoint(SnapshotMetadata Metadata, DateTimeOffset CapturedAt);
}

public sealed class WeeklyReport
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = "";

    [JsonPropertyName("window")]
    public string Window { get; set; } = "";

    [JsonPropertyName("historyDirectory")]
    public string HistoryDirectory { get; set; } = "";

    [JsonPropertyName("journalPath")]
    public string JournalPath { get; set; } = "";

    [JsonPropertyName("snapshotCount")]
    public int SnapshotCount { get; set; }

    [JsonPropertyName("firstSnapshot")]
    public WeeklySnapshotSummary FirstSnapshot { get; set; } = new();

    [JsonPropertyName("latestSnapshot")]
    public WeeklySnapshotSummary LatestSnapshot { get; set; } = new();

    [JsonPropertyName("scoreDelta")]
    public double? ScoreDelta { get; set; }

    [JsonPropertyName("elementDelta")]
    public int ElementDelta { get; set; }

    [JsonPropertyName("diffReview")]
    public WeeklyDiffSummary? DiffReview { get; set; }

    [JsonPropertyName("journal")]
    public WeeklyJournalSummary Journal { get; set; } = new();

    [JsonPropertyName("issues")]
    public List<string> Issues { get; set; } = new();
}

public sealed class WeeklySnapshotSummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("capturedAt")]
    public string CapturedAt { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("documentPath")]
    public string DocumentPath { get; set; } = "";

    [JsonPropertyName("elementCount")]
    public int ElementCount { get; set; }

    [JsonPropertyName("score")]
    public double? Score { get; set; }

    [JsonPropertyName("sheetCount")]
    public int SheetCount { get; set; }

    [JsonPropertyName("scheduleCount")]
    public int ScheduleCount { get; set; }
}

public sealed class WeeklyDiffSummary
{
    [JsonPropertyName("highestSeverity")]
    public string HighestSeverity { get; set; } = "none";

    [JsonPropertyName("totalChanges")]
    public int TotalChanges { get; set; }

    [JsonPropertyName("severityCounts")]
    public Dictionary<string, int> SeverityCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("recommendedActions")]
    public List<string> RecommendedActions { get; set; } = new();
}

public sealed class WeeklyJournalSummary
{
    [JsonPropertyName("entryCount")]
    public int EntryCount { get; set; }

    [JsonPropertyName("affectedElementCount")]
    public int AffectedElementCount { get; set; }

    [JsonPropertyName("firstTimestamp")]
    public string? FirstTimestamp { get; set; }

    [JsonPropertyName("lastTimestamp")]
    public string? LastTimestamp { get; set; }

    [JsonPropertyName("actions")]
    public List<WeeklyNamedCount> Actions { get; set; } = new();

    [JsonPropertyName("categories")]
    public List<WeeklyNamedCount> Categories { get; set; } = new();

    [JsonPropertyName("users")]
    public List<WeeklyNamedCount> Users { get; set; } = new();
}

public sealed record WeeklyNamedCount(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("count")] int Count);
