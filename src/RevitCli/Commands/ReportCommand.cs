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
using RevitCli.Standards;
using RevitCli.Workflows;

namespace RevitCli.Commands;

public static class ReportCommand
{
    public static Command Create()
    {
        var command = new Command("report", "Generate local project reports from history and journal data");
        command.AddCommand(CreateWeeklyCommand());
        command.AddCommand(CreateKnowledgeCommand());
        return command;
    }

    private static Command CreateKnowledgeCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory; defaults to current directory");
        var historyDirOpt = new Option<string?>("--history-dir", "Override .revitcli/history directory");
        var journalOpt = new Option<string?>("--journal", "Override .revitcli/journal.jsonl path");
        var standardsManifestOpt = new Option<string?>(
            "--standards-manifest",
            $"Override standards manifest path (default: {StandardsValidator.DefaultManifestPath})");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table | json | markdown");
        var reportOpt = new Option<string?>("--report", "Write report to file (.md and .json infer format)");

        var command = new Command("knowledge", "Summarize reusable local project knowledge from RevitCli artifacts")
        {
            dirOpt,
            historyDirOpt,
            journalOpt,
            standardsManifestOpt,
            outputOpt,
            reportOpt,
        };

        command.SetHandler(async (
            string? dir,
            string? historyDir,
            string? journal,
            string? standardsManifest,
            string outputFormat,
            string? reportPath) =>
        {
            Environment.ExitCode = await ExecuteKnowledgeAsync(
                dir,
                historyDir,
                journal,
                standardsManifest,
                outputFormat,
                reportPath,
                Console.Out);
        }, dirOpt, historyDirOpt, journalOpt, standardsManifestOpt, outputOpt, reportOpt);

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

        var effectiveFormat = InferFormat(outputFormat, reportPath);
        if (!IsKnownReportFormat(effectiveFormat))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
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

    public static async Task<int> ExecuteKnowledgeAsync(
        string? projectDirectory,
        string? historyDirectory,
        string? journalPath,
        string? standardsManifestPath,
        string outputFormat,
        string? reportPath,
        TextWriter output,
        DateTimeOffset? now = null)
    {
        var projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectDirectory!);
        var generatedAt = (now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var report = new KnowledgeReport
        {
            GeneratedAt = generatedAt.ToString("o", CultureInfo.InvariantCulture),
            ProjectDirectory = projectRoot,
        };

        report.History = await BuildKnowledgeHistoryAsync(projectRoot, historyDirectory, report.Issues);
        report.Journal = BuildKnowledgeJournal(projectRoot, journalPath, report.Issues);
        report.WorkflowReceipts = BuildKnowledgeWorkflowReceipts(projectRoot, report.Issues);
        report.Deliveries = BuildKnowledgeDeliveries(projectRoot, report.Issues);
        report.Standards = BuildKnowledgeStandards(projectRoot, standardsManifestPath, report.Issues);
        report.WeeklyReports = BuildKnowledgeWeeklyReports(projectRoot, report.Issues);
        AddKnowledgeHints(report);

        var effectiveFormat = InferFormat(outputFormat, reportPath);
        if (!IsKnownReportFormat(effectiveFormat))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        var rendered = RenderKnowledge(report, effectiveFormat);
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
            "json" => JsonSerializer.Serialize(report, TerminalJsonOptions.PrettyCamel),
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

    private static async Task<KnowledgeHistorySummary> BuildKnowledgeHistoryAsync(
        string projectRoot,
        string? historyDirectory,
        List<KnowledgeIssue> issues)
    {
        var store = string.IsNullOrWhiteSpace(historyDirectory)
            ? HistoryStore.ForProject(projectRoot)
            : new HistoryStore(ResolveProjectPath(projectRoot, historyDirectory!));
        var summary = new KnowledgeHistorySummary
        {
            HistoryDirectory = store.RootDirectory,
            Exists = Directory.Exists(store.RootDirectory),
        };

        if (!summary.Exists)
            return summary;

        try
        {
            var entries = await store.ListAsync(includeFixBaselines: false);
            var ordered = entries
                .Select(entry => new SnapshotPoint(entry, ParseTimestamp(entry.CapturedAt)))
                .OrderBy(point => point.CapturedAt)
                .ToList();
            summary.SnapshotCount = ordered.Count;

            var latest = ordered.LastOrDefault();
            if (latest != null)
            {
                summary.LatestSnapshotId = latest.Metadata.Id;
                summary.LatestCapturedAt = latest.Metadata.CapturedAt;
                summary.LatestSource = latest.Metadata.Source;
                summary.LatestDocumentPath = latest.Metadata.DocumentPath;
                summary.LatestElementCount = latest.Metadata.ElementCount;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            AddKnowledgeIssue(issues, "error", "history", $"failed to read history: {ex.Message}");
        }

        return summary;
    }

    private static KnowledgeJournalSummary BuildKnowledgeJournal(
        string projectRoot,
        string? journalPath,
        List<KnowledgeIssue> issues)
    {
        var resolvedJournal = ResolveJournalPath(projectRoot, journalPath);
        var summary = new KnowledgeJournalSummary
        {
            JournalPath = resolvedJournal,
            Exists = File.Exists(resolvedJournal),
        };

        if (!summary.Exists)
            return summary;

        try
        {
            var journal = JournalReader.Read(resolvedJournal);
            var stats = JournalReader.GetStats(journal);
            var suggestions = WorkflowSuggester.Suggest(journal, minCount: 2, maxSteps: 5, limit: 3);
            summary.EntryCount = stats.EntryCount;
            summary.AffectedElementCount = stats.AffectedElementCount;
            summary.DistinctAffectedElementCount = stats.DistinctAffectedElementCount;
            summary.FirstTimestamp = stats.FirstTimestamp;
            summary.LastTimestamp = stats.LastTimestamp;
            summary.CommandEntryCount = suggestions.CommandEntryCount;
            summary.RepeatedWorkflowSuggestionCount = suggestions.Suggestions.Count;
            summary.FirstSuggestedWorkflowName = suggestions.Suggestions.FirstOrDefault()?.Name;
            summary.SuggestedWorkflows = suggestions.Suggestions
                .Select(ToKnowledgeSuggestedWorkflow)
                .ToList();
            summary.Actions = stats.Actions
                .Take(5)
                .Select(item => new KnowledgeNamedCount(item.Action, item.Count, item.AffectedElementCount))
                .ToList();
            summary.Categories = stats.Categories
                .Take(5)
                .Select(item => new KnowledgeNamedCount(item.Name, item.Count, item.AffectedElementCount))
                .ToList();
            summary.Users = stats.Users
                .Take(5)
                .Select(item => new KnowledgeNamedCount(item.Name, item.Count, item.AffectedElementCount))
                .ToList();
            summary.Operators = stats.Operators
                .Take(5)
                .Select(item => new KnowledgeNamedCount(item.Name, item.Count, item.AffectedElementCount))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            AddKnowledgeIssue(issues, "error", "journal", $"failed to read journal: {ex.Message}");
        }

        return summary;
    }

    private static KnowledgeWorkflowReceiptSummary BuildKnowledgeWorkflowReceipts(
        string projectRoot,
        List<KnowledgeIssue> issues)
    {
        var summary = new KnowledgeWorkflowReceiptSummary
        {
            ReceiptDirectory = WorkflowReceiptReader.ResolveReceiptDirectory(projectRoot),
        };

        try
        {
            var receipts = WorkflowReceiptReader.Read(projectRoot, int.MaxValue, failedOnly: false);
            summary.Exists = receipts.Exists;
            summary.ReceiptCount = receipts.ReceiptCount;
            summary.FailedCount = receipts.Receipts.Count(receipt => !receipt.Success);
            summary.DryRunCount = receipts.Receipts.Count(receipt => receipt.DryRun);
            summary.IssueCount = receipts.Issues.Count;

            var latest = receipts.Receipts.FirstOrDefault();
            if (latest != null)
            {
                summary.LatestWorkflow = latest.Name;
                summary.LatestCompletedAtUtc = FormatReceiptTimestamp(latest);
                summary.LatestReceiptPath = latest.Path;
            }

            foreach (var issue in receipts.Issues)
            {
                AddKnowledgeIssue(issues, issue.Severity, "workflow receipts", $"{issue.Path}: {issue.Message}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AddKnowledgeIssue(issues, "error", "workflow receipts", $"failed to read workflow receipts: {ex.Message}");
        }

        return summary;
    }

    private static KnowledgeDeliverySummary BuildKnowledgeDeliveries(
        string projectRoot,
        List<KnowledgeIssue> issues)
    {
        var summary = new KnowledgeDeliverySummary
        {
            ManifestPath = DeliveryManifestReader.ResolveManifestPath(projectRoot),
        };

        try
        {
            var deliveries = DeliveryManifestReader.Read(projectRoot);
            var stats = deliveries.Stats;
            summary.ManifestPath = deliveries.ManifestPath;
            summary.Exists = deliveries.Exists;
            summary.Valid = deliveries.Exists ? deliveries.Valid : null;
            summary.EntryCount = deliveries.EntryCount;
            summary.ErrorCount = stats.ErrorCount;
            summary.FailedCount = CountDeliveryOutcome(stats, "failed");
            summary.MissingReceiptCount = stats.ReceiptMissingCount;
            summary.UnreadableReceiptCount = stats.ReceiptUnreadableCount;

            var latest = deliveries.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Timestamp))
                .OrderByDescending(entry => ParseTimestamp(entry.Timestamp!))
                .ThenByDescending(entry => entry.LineNumber)
                .FirstOrDefault();
            if (latest != null)
            {
                summary.LatestKind = latest.Kind;
                summary.LatestTimestamp = latest.Timestamp;
                summary.LatestReceiptPath = latest.ResolvedReceiptPath ?? latest.ReceiptPath;
            }

            foreach (var issue in deliveries.Issues)
            {
                AddKnowledgeIssue(issues, issue.Severity, "deliveries", issue.Message);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            AddKnowledgeIssue(issues, "error", "deliveries", $"failed to read delivery manifest: {ex.Message}");
        }

        return summary;
    }

    private static KnowledgeStandardsSummary BuildKnowledgeStandards(
        string projectRoot,
        string? standardsManifestPath,
        List<KnowledgeIssue> issues)
    {
        var manifestPath = string.IsNullOrWhiteSpace(standardsManifestPath)
            ? Path.Combine(projectRoot, StandardsValidator.DefaultManifestPath)
            : ResolveProjectPath(projectRoot, standardsManifestPath!);
        var summary = new KnowledgeStandardsSummary
        {
            ManifestPath = manifestPath,
            Exists = File.Exists(manifestPath),
        };

        if (!summary.Exists)
        {
            if (!string.IsNullOrWhiteSpace(standardsManifestPath))
            {
                AddKnowledgeIssue(issues, "warning", "standards", $"manifest not found: {manifestPath}");
            }

            return summary;
        }

        try
        {
            var validation = StandardsValidator.Validate(manifestPath, projectRoot);
            summary.Valid = validation.Valid;
            summary.Name = validation.Name;
            summary.PackVersion = validation.PackVersion;
            summary.CliVersion = validation.CliVersion;
            summary.ErrorCount = validation.Issues.Count(issue => issue.Severity == StandardsValidationSeverity.Error);
            summary.WarningCount = validation.Issues.Count(issue => issue.Severity == StandardsValidationSeverity.Warning);

            foreach (var issue in validation.Issues.Where(issue => issue.Severity == StandardsValidationSeverity.Error))
            {
                AddKnowledgeIssue(issues, "error", "standards", $"{issue.Path}: {issue.Message}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlDotNet.Core.YamlException)
        {
            AddKnowledgeIssue(issues, "error", "standards", $"failed to validate standards: {ex.Message}");
        }

        return summary;
    }

    private static KnowledgeWeeklyReportsSummary BuildKnowledgeWeeklyReports(
        string projectRoot,
        List<KnowledgeIssue> issues)
    {
        var reportsDir = Path.Combine(projectRoot, ".revitcli", "reports");
        var summary = new KnowledgeWeeklyReportsSummary
        {
            ReportsDirectory = reportsDir,
            Exists = Directory.Exists(reportsDir),
        };

        if (!summary.Exists)
            return summary;

        try
        {
            var weeklyReports = Directory.EnumerateFiles(reportsDir, "weekly*.*", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(file => string.Equals(file.Extension, ".md", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(file.Extension, ".json", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            summary.ReportCount = weeklyReports.Count;

            var latest = weeklyReports.FirstOrDefault();
            if (latest != null)
            {
                summary.LatestReportPath = latest.FullName;
                summary.LatestUpdatedAtUtc = latest.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture);
                summary.LatestBytes = latest.Length;
                summary.LatestTitle = ReadFirstNonEmptyLine(latest.FullName);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddKnowledgeIssue(issues, "error", "weekly reports", $"failed to read weekly reports: {ex.Message}");
        }

        return summary;
    }

    private static void AddKnowledgeHints(KnowledgeReport report)
    {
        if (!report.History.Exists)
        {
            AddKnowledgeHint(
                report,
                "history.init",
                "revitcli history init",
                "No local history store was found, so reusable model trend knowledge cannot be built yet.");
        }
        else if (report.History.SnapshotCount >= 2)
        {
            AddKnowledgeHint(
                report,
                "history.review",
                "revitcli history diff @-2 @-1 --review",
                "Recent snapshots are available for deterministic diff review.");
        }

        if (report.Journal.RepeatedWorkflowSuggestionCount > 0)
        {
            AddKnowledgeHint(
                report,
                "workflow.suggest",
                "revitcli workflow suggest --output yaml",
                "Repeated journal command sequences were found; review the suggested YAML before saving any workflow.");
        }

        if (report.WorkflowReceipts.FailedCount > 0)
        {
            AddKnowledgeHint(
                report,
                "workflow.failed",
                "revitcli workflow receipts --failed-only --output markdown",
                "Failed workflow receipts need triage before the same deadline workflow is reused.");
        }

        if (report.Deliveries.Exists && (report.Deliveries.Valid == false ||
                                         report.Deliveries.FailedCount > 0 ||
                                         report.Deliveries.MissingReceiptCount > 0 ||
                                         report.Deliveries.UnreadableReceiptCount > 0))
        {
            AddKnowledgeHint(
                report,
                "deliverables.verify",
                "revitcli deliverables verify --output markdown",
                "Delivery manifest or receipt issues should be reviewed before packaging handoff evidence.");
        }

        if (report.Standards.Exists && report.Standards.Valid == false)
        {
            AddKnowledgeHint(
                report,
                "standards.validate",
                "revitcli standards validate --output markdown",
                "The local standards pack has validation errors that should be fixed before reusing project rules.");
        }
        else if (!report.Standards.Exists)
        {
            AddKnowledgeHint(
                report,
                "standards.bootstrap",
                "revitcli standards install ../office-standards --dry-run --output markdown",
                "No local standards manifest was found, so project rules are not yet captured as a reusable pack.");
        }

        if (report.WeeklyReports.ReportCount == 0 && report.History.Exists)
        {
            AddKnowledgeHint(
                report,
                "report.weekly",
                "revitcli report weekly --report .revitcli/reports/weekly.md",
                "History exists but no weekly report artifact was found for handoff review.");
        }

        if (report.ReuseHints.Count == 0)
        {
            AddKnowledgeHint(
                report,
                "review.ready",
                "revitcli report knowledge --output markdown",
                "Local review artifacts are present and no immediate reuse blockers were detected.");
        }
    }

    private static string RenderKnowledge(KnowledgeReport report, string format) =>
        format.ToLowerInvariant() switch
        {
            "json" => JsonSerializer.Serialize(report, TerminalJsonOptions.PrettyCamel),
            "markdown" or "md" => RenderKnowledgeMarkdown(report),
            _ => RenderKnowledgeTable(report),
        };

    private static string RenderKnowledgeTable(KnowledgeReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Knowledge report");
        sb.AppendLine($"Generated: {report.GeneratedAt}");
        sb.AppendLine($"Project: {report.ProjectDirectory}");
        sb.AppendLine($"History: {FormatExists(report.History.Exists)}; snapshots={report.History.SnapshotCount}; latest={Format(report.History.LatestSnapshotId)}");
        sb.AppendLine($"Journal: {FormatExists(report.Journal.Exists)}; entries={report.Journal.EntryCount}; affected={report.Journal.AffectedElementCount}; repeated workflows={report.Journal.RepeatedWorkflowSuggestionCount}");
        sb.AppendLine($"Workflow receipts: {FormatExists(report.WorkflowReceipts.Exists)}; receipts={report.WorkflowReceipts.ReceiptCount}; failed={report.WorkflowReceipts.FailedCount}; issues={report.WorkflowReceipts.IssueCount}");
        sb.AppendLine($"Deliveries: {FormatExists(report.Deliveries.Exists)}; entries={report.Deliveries.EntryCount}; valid={FormatNullableBool(report.Deliveries.Valid)}; failed={report.Deliveries.FailedCount}; errors={report.Deliveries.ErrorCount}");
        sb.AppendLine($"Standards: {FormatExists(report.Standards.Exists)}; valid={FormatNullableBool(report.Standards.Valid)}; pack={Format(report.Standards.PackVersion)}; errors={report.Standards.ErrorCount}");
        sb.AppendLine($"Weekly reports: {FormatExists(report.WeeklyReports.Exists)}; reports={report.WeeklyReports.ReportCount}; latest={Format(report.WeeklyReports.LatestReportPath)}; title={Format(report.WeeklyReports.LatestTitle)}");

        AppendKnowledgeCounts(sb, "Top actions", report.Journal.Actions);
        AppendKnowledgeCounts(sb, "Top categories", report.Journal.Categories);
        AppendKnowledgeCounts(sb, "Top operators", report.Journal.Operators);

        if (report.Journal.SuggestedWorkflows.Count > 0)
        {
            sb.AppendLine("Suggested workflow drafts:");
            foreach (var workflow in report.Journal.SuggestedWorkflows)
            {
                sb.AppendLine(
                    $"  - {workflow.Name}: repeated={workflow.Count}; steps={workflow.StepCount}; mutating={workflow.MutatingStepCount}; approval={workflow.RequiresApproval.ToString().ToLowerInvariant()}");
            }
        }

        sb.AppendLine("Reuse hints:");
        foreach (var hint in report.ReuseHints)
        {
            sb.AppendLine($"  - {hint.Code}: {hint.Command}");
            sb.AppendLine($"    {hint.Reason}");
        }

        if (report.Issues.Count > 0)
        {
            sb.AppendLine("Issues:");
            foreach (var issue in report.Issues)
            {
                sb.AppendLine($"  - {issue.Severity.ToUpperInvariant()} {issue.Source}: {issue.Message}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderKnowledgeMarkdown(KnowledgeReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# RevitCli Knowledge Report");
        sb.AppendLine();
        sb.AppendLine($"- Generated: `{EscapeInlineCode(report.GeneratedAt)}`");
        sb.AppendLine($"- Project: `{EscapeInlineCode(report.ProjectDirectory)}`");
        sb.AppendLine();
        sb.AppendLine("## Evidence");
        sb.AppendLine();
        sb.AppendLine("| Source | Status | Key facts |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine($"| History | {KnowledgeStatus(report.History.Exists)} | {EscapeTableCell($"{report.History.SnapshotCount} snapshots; latest {Format(report.History.LatestSnapshotId)}")} |");
        sb.AppendLine($"| Journal | {KnowledgeStatus(report.Journal.Exists)} | {EscapeTableCell($"{report.Journal.EntryCount} entries; {report.Journal.RepeatedWorkflowSuggestionCount} repeated workflow suggestions")} |");
        sb.AppendLine($"| Workflow Receipts | {KnowledgeStatus(report.WorkflowReceipts.Exists)} | {EscapeTableCell($"{report.WorkflowReceipts.ReceiptCount} receipts; {report.WorkflowReceipts.FailedCount} failed")} |");
        sb.AppendLine($"| Delivery Receipts | {KnowledgeStatus(report.Deliveries.Exists)} | {EscapeTableCell($"{report.Deliveries.EntryCount} entries; valid {FormatNullableBool(report.Deliveries.Valid)}; {report.Deliveries.MissingReceiptCount} missing receipts")} |");
        sb.AppendLine($"| Standards Validation | {KnowledgeStatus(report.Standards.Exists)} | {EscapeTableCell($"valid {FormatNullableBool(report.Standards.Valid)}; pack {Format(report.Standards.PackVersion)}; {report.Standards.ErrorCount} errors")} |");
        sb.AppendLine($"| Weekly Reports | {KnowledgeStatus(report.WeeklyReports.Exists)} | {EscapeTableCell($"{report.WeeklyReports.ReportCount} reports; latest {Format(report.WeeklyReports.LatestReportPath)}; title {Format(report.WeeklyReports.LatestTitle)}")} |");

        if (report.Journal.Actions.Count > 0 || report.Journal.Categories.Count > 0 || report.Journal.Operators.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Journal Signals");
            AppendKnowledgeCountsMarkdown(sb, "Actions", report.Journal.Actions);
            AppendKnowledgeCountsMarkdown(sb, "Categories", report.Journal.Categories);
            AppendKnowledgeCountsMarkdown(sb, "Operators", report.Journal.Operators);
        }

        if (report.Journal.SuggestedWorkflows.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Suggested Workflow Drafts");
            foreach (var workflow in report.Journal.SuggestedWorkflows)
            {
                sb.AppendLine();
                sb.AppendLine($"### {EscapeMarkdownText(workflow.Name)}");
                sb.AppendLine();
                sb.AppendLine($"- Repeated: `{workflow.Count}`");
                sb.AppendLine($"- First journal line: `{workflow.FirstLine}`");
                sb.AppendLine($"- Steps: `{workflow.StepCount}`");
                sb.AppendLine($"- Mutating steps: `{workflow.MutatingStepCount}`");
                sb.AppendLine($"- Requires approval: `{workflow.RequiresApproval.ToString().ToLowerInvariant()}`");
                sb.AppendLine();
                sb.AppendLine("```yaml");
                sb.AppendLine(workflow.Yaml);
                sb.AppendLine("```");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Reuse Hints");
        foreach (var hint in report.ReuseHints)
        {
            sb.AppendLine($"- `{EscapeInlineCode(hint.Code)}` `{EscapeInlineCode(hint.Command)}`: {EscapeMarkdownText(hint.Reason)}");
        }

        sb.AppendLine();
        sb.AppendLine("## Issues");
        if (report.Issues.Count == 0)
        {
            sb.AppendLine("- None.");
        }
        else
        {
            foreach (var issue in report.Issues)
            {
                sb.AppendLine(
                    $"- `{EscapeInlineCode(issue.Severity.ToUpperInvariant())}` `{EscapeInlineCode(issue.Source)}`: {EscapeMarkdownText(issue.Message)}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static bool IsKnownReportFormat(string format) =>
        string.Equals(format, "table", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(format, "json", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(format, "md", StringComparison.OrdinalIgnoreCase);

    private static int CountDeliveryOutcome(DeliveryManifestStats stats, string outcome) =>
        stats.Outcomes.FirstOrDefault(item => string.Equals(item.Name, outcome, StringComparison.OrdinalIgnoreCase))?.Count ?? 0;

    private static KnowledgeSuggestedWorkflow ToKnowledgeSuggestedWorkflow(WorkflowSuggestion suggestion)
    {
        var mutatingStepCount = suggestion.Steps.Count(step =>
            string.Equals(step.Mode, "mutating", StringComparison.OrdinalIgnoreCase));
        return new KnowledgeSuggestedWorkflow(
            suggestion.Name,
            suggestion.Count,
            suggestion.FirstLine,
            suggestion.Steps.Count,
            mutatingStepCount,
            suggestion.Steps.Any(step => step.RequiresApproval),
            suggestion.Yaml);
    }

    private static void AddKnowledgeIssue(List<KnowledgeIssue> issues, string severity, string source, string message) =>
        issues.Add(new KnowledgeIssue(severity, source, message));

    private static void AddKnowledgeHint(KnowledgeReport report, string code, string command, string reason) =>
        report.ReuseHints.Add(new KnowledgeHint(code, command, reason));

    private static void AppendKnowledgeCounts(
        StringBuilder sb,
        string title,
        IReadOnlyList<KnowledgeNamedCount> counts)
    {
        if (counts.Count == 0)
            return;

        sb.AppendLine($"{title}:");
        foreach (var count in counts)
        {
            var affected = count.AffectedElementCount > 0 ? $"; affected={count.AffectedElementCount}" : "";
            sb.AppendLine($"  {count.Name}: {count.Count}{affected}");
        }
    }

    private static void AppendKnowledgeCountsMarkdown(
        StringBuilder sb,
        string title,
        IReadOnlyList<KnowledgeNamedCount> counts)
    {
        if (counts.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"### {title}");
        sb.AppendLine();
        sb.AppendLine("| Name | Count | Affected elements |");
        sb.AppendLine("|---|---:|---:|");
        foreach (var count in counts)
        {
            sb.AppendLine($"| {EscapeTableCell(count.Name)} | {count.Count} | {count.AffectedElementCount} |");
        }
    }

    private static string FormatReceiptTimestamp(WorkflowReceiptSummary receipt)
    {
        if (!string.IsNullOrWhiteSpace(receipt.CompletedAtUtc))
            return receipt.CompletedAtUtc;
        if (!string.IsNullOrWhiteSpace(receipt.StartedAtUtc))
            return receipt.StartedAtUtc;
        return "";
    }

    private static string? ReadFirstNonEmptyLine(string path)
    {
        foreach (var line in File.ReadLines(path).Take(20))
        {
            if (!string.IsNullOrWhiteSpace(line))
                return line.Trim();
        }

        return null;
    }

    private static string ResolveProjectPath(string projectRoot, string path) =>
        Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(projectRoot, path));

    private static string Format(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "n/a" : value!;

    private static string FormatExists(bool exists) =>
        exists ? "present" : "missing";

    private static string FormatNullableBool(bool? value) =>
        value.HasValue ? value.Value.ToString().ToLowerInvariant() : "n/a";

    private static string KnowledgeStatus(bool exists) =>
        exists ? "`present`" : "`missing`";

    private static string EscapeInlineCode(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        return value.Replace("`", "\\`", StringComparison.Ordinal);
    }

    private static string EscapeMarkdownText(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        return value
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static string EscapeTableCell(string? value) =>
        EscapeMarkdownText(value)
            .Replace("`", "\\`", StringComparison.Ordinal);

    private static string ResolveJournalPath(string projectRoot, string? journalPath) =>
        string.IsNullOrWhiteSpace(journalPath)
            ? Path.Combine(projectRoot, ".revitcli", "journal.jsonl")
            : ResolveProjectPath(projectRoot, journalPath!);

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

public sealed class KnowledgeReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "knowledge-report.v1";

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = "";

    [JsonPropertyName("projectDirectory")]
    public string ProjectDirectory { get; set; } = "";

    [JsonPropertyName("history")]
    public KnowledgeHistorySummary History { get; set; } = new();

    [JsonPropertyName("journal")]
    public KnowledgeJournalSummary Journal { get; set; } = new();

    [JsonPropertyName("workflowReceipts")]
    public KnowledgeWorkflowReceiptSummary WorkflowReceipts { get; set; } = new();

    [JsonPropertyName("deliveries")]
    public KnowledgeDeliverySummary Deliveries { get; set; } = new();

    [JsonPropertyName("standards")]
    public KnowledgeStandardsSummary Standards { get; set; } = new();

    [JsonPropertyName("weeklyReports")]
    public KnowledgeWeeklyReportsSummary WeeklyReports { get; set; } = new();

    [JsonPropertyName("reuseHints")]
    public List<KnowledgeHint> ReuseHints { get; set; } = new();

    [JsonPropertyName("issues")]
    public List<KnowledgeIssue> Issues { get; set; } = new();
}

public sealed class KnowledgeHistorySummary
{
    [JsonPropertyName("historyDirectory")]
    public string HistoryDirectory { get; set; } = "";

    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("snapshotCount")]
    public int SnapshotCount { get; set; }

    [JsonPropertyName("latestSnapshotId")]
    public string? LatestSnapshotId { get; set; }

    [JsonPropertyName("latestCapturedAt")]
    public string? LatestCapturedAt { get; set; }

    [JsonPropertyName("latestSource")]
    public string? LatestSource { get; set; }

    [JsonPropertyName("latestDocumentPath")]
    public string? LatestDocumentPath { get; set; }

    [JsonPropertyName("latestElementCount")]
    public int? LatestElementCount { get; set; }
}

public sealed class KnowledgeJournalSummary
{
    [JsonPropertyName("journalPath")]
    public string JournalPath { get; set; } = "";

    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("entryCount")]
    public int EntryCount { get; set; }

    [JsonPropertyName("affectedElementCount")]
    public int AffectedElementCount { get; set; }

    [JsonPropertyName("distinctAffectedElementCount")]
    public int DistinctAffectedElementCount { get; set; }

    [JsonPropertyName("firstTimestamp")]
    public string? FirstTimestamp { get; set; }

    [JsonPropertyName("lastTimestamp")]
    public string? LastTimestamp { get; set; }

    [JsonPropertyName("commandEntryCount")]
    public int CommandEntryCount { get; set; }

    [JsonPropertyName("repeatedWorkflowSuggestionCount")]
    public int RepeatedWorkflowSuggestionCount { get; set; }

    [JsonPropertyName("firstSuggestedWorkflowName")]
    public string? FirstSuggestedWorkflowName { get; set; }

    [JsonPropertyName("actions")]
    public List<KnowledgeNamedCount> Actions { get; set; } = new();

    [JsonPropertyName("categories")]
    public List<KnowledgeNamedCount> Categories { get; set; } = new();

    [JsonPropertyName("users")]
    public List<KnowledgeNamedCount> Users { get; set; } = new();

    [JsonPropertyName("operators")]
    public List<KnowledgeNamedCount> Operators { get; set; } = new();

    [JsonPropertyName("suggestedWorkflows")]
    public List<KnowledgeSuggestedWorkflow> SuggestedWorkflows { get; set; } = new();
}

public sealed class KnowledgeWorkflowReceiptSummary
{
    [JsonPropertyName("receiptDirectory")]
    public string ReceiptDirectory { get; set; } = "";

    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("receiptCount")]
    public int ReceiptCount { get; set; }

    [JsonPropertyName("failedCount")]
    public int FailedCount { get; set; }

    [JsonPropertyName("dryRunCount")]
    public int DryRunCount { get; set; }

    [JsonPropertyName("issueCount")]
    public int IssueCount { get; set; }

    [JsonPropertyName("latestWorkflow")]
    public string? LatestWorkflow { get; set; }

    [JsonPropertyName("latestCompletedAtUtc")]
    public string? LatestCompletedAtUtc { get; set; }

    [JsonPropertyName("latestReceiptPath")]
    public string? LatestReceiptPath { get; set; }
}

public sealed class KnowledgeDeliverySummary
{
    [JsonPropertyName("manifestPath")]
    public string ManifestPath { get; set; } = "";

    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("valid")]
    public bool? Valid { get; set; }

    [JsonPropertyName("entryCount")]
    public int EntryCount { get; set; }

    [JsonPropertyName("failedCount")]
    public int FailedCount { get; set; }

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("missingReceiptCount")]
    public int MissingReceiptCount { get; set; }

    [JsonPropertyName("unreadableReceiptCount")]
    public int UnreadableReceiptCount { get; set; }

    [JsonPropertyName("latestKind")]
    public string? LatestKind { get; set; }

    [JsonPropertyName("latestTimestamp")]
    public string? LatestTimestamp { get; set; }

    [JsonPropertyName("latestReceiptPath")]
    public string? LatestReceiptPath { get; set; }
}

public sealed class KnowledgeStandardsSummary
{
    [JsonPropertyName("manifestPath")]
    public string ManifestPath { get; set; } = "";

    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("valid")]
    public bool? Valid { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("packVersion")]
    public string? PackVersion { get; set; }

    [JsonPropertyName("cliVersion")]
    public string? CliVersion { get; set; }

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("warningCount")]
    public int WarningCount { get; set; }
}

public sealed class KnowledgeWeeklyReportsSummary
{
    [JsonPropertyName("reportsDirectory")]
    public string ReportsDirectory { get; set; } = "";

    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("reportCount")]
    public int ReportCount { get; set; }

    [JsonPropertyName("latestReportPath")]
    public string? LatestReportPath { get; set; }

    [JsonPropertyName("latestUpdatedAtUtc")]
    public string? LatestUpdatedAtUtc { get; set; }

    [JsonPropertyName("latestBytes")]
    public long? LatestBytes { get; set; }

    [JsonPropertyName("latestTitle")]
    public string? LatestTitle { get; set; }
}

public sealed record KnowledgeNamedCount(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("affectedElementCount")] int AffectedElementCount);

public sealed record KnowledgeSuggestedWorkflow(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("firstLine")] int FirstLine,
    [property: JsonPropertyName("stepCount")] int StepCount,
    [property: JsonPropertyName("mutatingStepCount")] int MutatingStepCount,
    [property: JsonPropertyName("requiresApproval")] bool RequiresApproval,
    [property: JsonPropertyName("yaml")] string Yaml);

public sealed record KnowledgeHint(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record KnowledgeIssue(
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("message")] string Message);
