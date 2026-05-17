using System;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Journal;

namespace RevitCli.Commands;

public static class JournalCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static Command Create()
    {
        var command = new Command("journal", "Inspect, sign, and verify the RevitCli operation journal");
        command.AddCommand(CreateShowCommand());
        command.AddCommand(CreateStatsCommand());
        command.AddCommand(CreateReviewCommand());
        command.AddCommand(CreateSignCommand());
        command.AddCommand(CreateVerifyCommand());
        return command;
    }

    private static Command CreateShowCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory containing .revitcli (default: current directory)");
        var journalOpt = new Option<string?>("--journal", "Override journal file path (default: <dir>/.revitcli/journal.jsonl)");
        var limitOpt = new Option<int>("--limit", () => 20, "Maximum number of entries to show");
        var actionOpt = new Option<string?>("--action", "Only show entries with this action");
        var categoryOpt = new Option<string?>("--category", "Only show entries with this category");
        var operatorOpt = new Option<string?>("--operator", "Only show entries from this operator");
        var userOpt = new Option<string?>("--user", "Only show entries from this user");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json");

        var cmd = new Command("show", "Show recent journal entries")
        {
            dirOpt,
            journalOpt,
            limitOpt,
            actionOpt,
            categoryOpt,
            operatorOpt,
            userOpt,
            outputOpt,
        };

        cmd.SetHandler(async (
            string? dir,
            string? journal,
            int limit,
            string? action,
            string? category,
            string? operatorFilter,
            string? user,
            string output) =>
        {
            Environment.ExitCode = await ExecuteShowAsync(
                dir,
                journal,
                limit,
                action,
                output,
                Console.Out,
                category,
                operatorFilter,
                user);
        }, dirOpt, journalOpt, limitOpt, actionOpt, categoryOpt, operatorOpt, userOpt, outputOpt);

        return cmd;
    }

    private static Command CreateReviewCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory containing .revitcli (default: current directory)");
        var journalOpt = new Option<string?>("--journal", "Override journal file path (default: <dir>/.revitcli/journal.jsonl)");
        var limitOpt = new Option<int>("--limit", () => 50, "Maximum recent entries to review");
        var highImpactOpt = new Option<int>("--high-impact-threshold", () => 50, "Affected element count that marks an entry high-impact");
        var actionOpt = new Option<string?>("--action", "Only review entries with this action");
        var categoryOpt = new Option<string?>("--category", "Only review entries with this category");
        var operatorOpt = new Option<string?>("--operator", "Only review entries from this operator");
        var userOpt = new Option<string?>("--user", "Only review entries from this user");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");

        var cmd = new Command("review", "Review recent journal activity by risk, operator, category, and affected elements")
        {
            dirOpt,
            journalOpt,
            limitOpt,
            highImpactOpt,
            actionOpt,
            categoryOpt,
            operatorOpt,
            userOpt,
            outputOpt,
        };

        cmd.SetHandler(async ctx =>
        {
            Environment.ExitCode = await ExecuteReviewAsync(
                ctx.ParseResult.GetValueForOption(dirOpt),
                ctx.ParseResult.GetValueForOption(journalOpt),
                ctx.ParseResult.GetValueForOption(limitOpt),
                ctx.ParseResult.GetValueForOption(highImpactOpt),
                ctx.ParseResult.GetValueForOption(actionOpt),
                ctx.ParseResult.GetValueForOption(categoryOpt),
                ctx.ParseResult.GetValueForOption(operatorOpt),
                ctx.ParseResult.GetValueForOption(userOpt),
                ctx.ParseResult.GetValueForOption(outputOpt)!,
                Console.Out);
        });

        return cmd;
    }

    private static Command CreateStatsCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory containing .revitcli (default: current directory)");
        var journalOpt = new Option<string?>("--journal", "Override journal file path (default: <dir>/.revitcli/journal.jsonl)");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json");

        var cmd = new Command("stats", "Summarize journal entry counts by action")
        {
            dirOpt,
            journalOpt,
            outputOpt,
        };

        cmd.SetHandler(async (string? dir, string? journal, string output) =>
        {
            Environment.ExitCode = await ExecuteStatsAsync(dir, journal, output, Console.Out);
        }, dirOpt, journalOpt, outputOpt);

        return cmd;
    }

    private static Command CreateSignCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory containing .revitcli (default: current directory)");
        var journalOpt = new Option<string?>("--journal", "Override journal file path (default: <dir>/.revitcli/journal.jsonl)");
        var signatureOpt = new Option<string?>("--signature", "Override signature file path (default: <journal>.sig)");
        var keyOpt = new Option<string?>("--key", "HMAC key path (default: <dir>/.revitcli/journal.key; created if missing)");
        var untilOpt = new Option<string?>("--until", "Sign entries at or before this timestamp (ISO 8601)");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json");

        var cmd = new Command("sign", "Create .revitcli/journal.jsonl.sig")
        {
            dirOpt,
            journalOpt,
            signatureOpt,
            keyOpt,
            untilOpt,
            outputOpt,
        };

        cmd.SetHandler(async (string? dir, string? journal, string? signature, string? key, string? until, string output) =>
        {
            Environment.ExitCode = await ExecuteSignAsync(dir, journal, signature, key, until, output, Console.Out);
        }, dirOpt, journalOpt, signatureOpt, keyOpt, untilOpt, outputOpt);

        return cmd;
    }

    private static Command CreateVerifyCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory containing .revitcli (default: current directory)");
        var journalOpt = new Option<string?>("--journal", "Override journal file path (default: <dir>/.revitcli/journal.jsonl)");
        var signatureOpt = new Option<string?>("--signature", "Override signature file path (default: <journal>.sig)");
        var keyOpt = new Option<string?>("--key", "HMAC key path (default: <dir>/.revitcli/journal.key)");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json");

        var cmd = new Command("verify", "Verify .revitcli/journal.jsonl.sig against the journal")
        {
            dirOpt,
            journalOpt,
            signatureOpt,
            keyOpt,
            outputOpt,
        };

        cmd.SetHandler(async (string? dir, string? journal, string? signature, string? key, string output) =>
        {
            Environment.ExitCode = await ExecuteVerifyAsync(dir, journal, signature, key, output, Console.Out);
        }, dirOpt, journalOpt, signatureOpt, keyOpt, outputOpt);

        return cmd;
    }

    internal static Task<int> ExecuteSignAsync(
        string? dir,
        string? journal,
        string? signature,
        string? key,
        string? until,
        string outputFormat,
        TextWriter output)
    {
        if (!TryParseOutput(outputFormat, out var normalizedOutput, out var outputError))
            return WriteError(output, outputError);

        if (!TryParseUntil(until, out var signedUntil, out var untilError))
            return WriteError(output, normalizedOutput, untilError);

        var paths = ResolvePaths(dir, journal, signature, key);
        try
        {
            var result = JournalSignatureService.Sign(
                paths.JournalPath,
                paths.SignaturePath,
                paths.KeyPath,
                signedUntil);

            if (normalizedOutput == "json")
                return WriteJson(output, result, 0);

            return WriteLines(
                output,
                0,
                $"OK: Journal signature written: {result.SignaturePath}",
                $"OK: Entries signed: {result.EntryCount}",
                $"OK: Root hash: {result.RootHash}",
                $"OK: HMAC key: {result.KeyPath}{(result.KeyCreated ? " (created)" : "")}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return WriteError(output, normalizedOutput, $"failed to sign journal: {ex.Message}");
        }
    }

    internal static Task<int> ExecuteShowAsync(
        string? dir,
        string? journal,
        int limit,
        string? action,
        string outputFormat,
        TextWriter output,
        string? category = null,
        string? operatorFilter = null,
        string? user = null)
    {
        if (!TryParseOutput(outputFormat, out var normalizedOutput, out var outputError))
            return WriteError(output, outputError);

        if (limit <= 0)
            return WriteError(output, normalizedOutput, "--limit must be greater than 0.");

        var paths = ResolvePaths(dir, journal, null, null);
        try
        {
            var result = JournalReader.Read(paths.JournalPath);
            var entries = result.Entries
                .Where(entry => MatchesFilter(entry.Action, action)
                    && MatchesFilter(entry.Category, category)
                    && MatchesFilter(entry.Operator, operatorFilter)
                    && MatchesFilter(entry.User, user))
                .TakeLast(limit)
                .ToList();
            var showResult = new JournalShowResult(result.JournalPath, result.Entries.Count, entries.Count, entries);

            if (normalizedOutput == "json")
                return WriteJson(output, showResult, 0);

            if (entries.Count == 0)
            {
                return WriteLines(
                    output,
                    0,
                    $"INFO: Journal entries: 0 of {result.Entries.Count}",
                    $"OK: Journal: {result.JournalPath}");
            }

            var lines = new List<string>
            {
                $"OK: Journal: {result.JournalPath}",
                $"OK: Showing {entries.Count} of {result.Entries.Count} entr{(result.Entries.Count == 1 ? "y" : "ies")}"
            };
            lines.AddRange(entries.Select(entry =>
                $"{entry.LineNumber,4}  {entry.Timestamp ?? "-",-27}  {entry.Action,-12}  {entry.Summary}"));
            return WriteLines(output, 0, lines.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return WriteError(output, normalizedOutput, $"failed to read journal: {ex.Message}");
        }
    }

    internal static Task<int> ExecuteStatsAsync(
        string? dir,
        string? journal,
        string outputFormat,
        TextWriter output)
    {
        if (!TryParseOutput(outputFormat, out var normalizedOutput, out var outputError))
            return WriteError(output, outputError);

        var paths = ResolvePaths(dir, journal, null, null);
        try
        {
            var result = JournalReader.GetStats(JournalReader.Read(paths.JournalPath));
            if (normalizedOutput == "json")
                return WriteJson(output, result, 0);

            var lines = new List<string>
            {
                $"OK: Journal: {result.JournalPath}",
                $"OK: Entries: {result.EntryCount}",
                $"OK: First timestamp: {result.FirstTimestamp ?? "-"}",
                $"OK: Last timestamp: {result.LastTimestamp ?? "-"}",
                $"OK: Affected elements: {result.AffectedElementCount}",
                $"OK: Distinct affected element IDs: {result.DistinctAffectedElementCount}"
            };
            if (result.AffectedElementIds.Count > 0)
            {
                lines.Add($"Affected IDs: {string.Join(", ", result.AffectedElementIds.Take(20))}" +
                    (result.AffectedElementIds.Count > 20 ? ", ..." : ""));
            }

            if (result.Actions.Count == 0)
            {
                lines.Add("INFO: No actions recorded.");
            }
            else
            {
                lines.Add("Actions:");
                lines.AddRange(result.Actions.Select(action =>
                    $"  {action.Action,-12} {action.Count} affected={action.AffectedElementCount}"));
            }

            if (result.Categories.Count > 0)
            {
                lines.Add("Categories:");
                lines.AddRange(result.Categories.Select(category =>
                    $"  {category.Name,-12} {category.Count} affected={category.AffectedElementCount}"));
            }

            if (result.Users.Count > 0)
            {
                lines.Add("Users:");
                lines.AddRange(result.Users.Select(user =>
                    $"  {user.Name,-12} {user.Count} affected={user.AffectedElementCount}"));
            }

            if (result.Operators.Count > 0)
            {
                lines.Add("Operators:");
                lines.AddRange(result.Operators.Select(item =>
                    $"  {item.Name,-12} {item.Count} affected={item.AffectedElementCount}"));
            }

            return WriteLines(output, 0, lines.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return WriteError(output, normalizedOutput, $"failed to read journal: {ex.Message}");
        }
    }

    internal static Task<int> ExecuteReviewAsync(
        string? dir,
        string? journal,
        int limit,
        int highImpactThreshold,
        string? action,
        string? category,
        string? operatorFilter,
        string? user,
        string outputFormat,
        TextWriter output)
    {
        if (!TryParseReviewOutput(outputFormat, out var normalizedOutput, out var outputError))
            return WriteError(output, outputError);

        if (limit <= 0)
            return WriteError(output, normalizedOutput, "--limit must be greater than 0.");
        if (highImpactThreshold <= 0)
            return WriteError(output, normalizedOutput, "--high-impact-threshold must be greater than 0.");

        var paths = ResolvePaths(dir, journal, null, null);
        try
        {
            var result = JournalReader.Read(paths.JournalPath);
            var entries = result.Entries
                .Where(entry => MatchesFilter(entry.Action, action)
                    && MatchesFilter(entry.Category, category)
                    && MatchesFilter(entry.Operator, operatorFilter)
                    && MatchesFilter(entry.User, user))
                .TakeLast(limit)
                .ToList();

            var review = CreateReviewResult(result.JournalPath, result.Entries.Count, entries, highImpactThreshold);
            return normalizedOutput switch
            {
                "json" => WriteJson(output, review, 0),
                "markdown" => WriteLines(output, 0, RenderReviewMarkdown(review).ToArray()),
                _ => WriteLines(output, 0, RenderReviewTable(review).ToArray())
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return WriteError(output, normalizedOutput, $"failed to read journal: {ex.Message}");
        }
    }

    internal static Task<int> ExecuteVerifyAsync(
        string? dir,
        string? journal,
        string? signature,
        string? key,
        string outputFormat,
        TextWriter output)
    {
        if (!TryParseOutput(outputFormat, out var normalizedOutput, out var outputError))
            return WriteError(output, outputError);

        var paths = ResolvePaths(dir, journal, signature, key);
        try
        {
            var result = JournalSignatureService.Verify(paths.JournalPath, paths.SignaturePath, paths.KeyPath);
            if (normalizedOutput == "json")
                return WriteJson(output, result, result.IsValid ? 0 : 1);

            if (result.IsValid)
            {
                return WriteLines(
                    output,
                    0,
                    $"OK: Journal signature valid: {result.SignaturePath}",
                    $"OK: Entries verified: {result.EntryCount}",
                    $"OK: Root hash: {result.RootHash}");
            }

            return WriteLines(output, 1, result.Errors.Select(error => $"FAIL: {error}").ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return WriteError(output, normalizedOutput, $"failed to verify journal: {ex.Message}");
        }
    }

    private static bool TryParseOutput(string? outputFormat, out string normalized, out string error)
    {
        normalized = string.IsNullOrWhiteSpace(outputFormat)
            ? "table"
            : outputFormat.Trim().ToLowerInvariant();
        if (normalized is "table" or "json")
        {
            error = "";
            return true;
        }

        error = $"unknown output format '{outputFormat}'. Use table or json.";
        return false;
    }

    private static bool TryParseReviewOutput(string? outputFormat, out string normalized, out string error)
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

    private static bool MatchesFilter(string? value, string? filter)
    {
        return string.IsNullOrWhiteSpace(filter)
            || string.Equals(value, filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseUntil(string? until, out DateTimeOffset? signedUntil, out string error)
    {
        signedUntil = null;
        error = "";
        if (string.IsNullOrWhiteSpace(until))
            return true;

        if (DateTimeOffset.TryParse(
                until,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            signedUntil = parsed;
            return true;
        }

        error = $"invalid --until timestamp '{until}'. Use ISO 8601, for example 2026-04-29T12:34:56Z.";
        return false;
    }

    private static JournalPaths ResolvePaths(string? dir, string? journal, string? signature, string? key)
    {
        var baseDir = string.IsNullOrWhiteSpace(dir)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(dir);
        var journalPath = string.IsNullOrWhiteSpace(journal)
            ? Path.Combine(baseDir, ".revitcli", "journal.jsonl")
            : Path.GetFullPath(journal);
        var signaturePath = string.IsNullOrWhiteSpace(signature)
            ? journalPath + ".sig"
            : Path.GetFullPath(signature);
        var keyPath = string.IsNullOrWhiteSpace(key)
            ? Path.Combine(baseDir, ".revitcli", "journal.key")
            : Path.GetFullPath(key);
        return new JournalPaths(journalPath, signaturePath, keyPath);
    }

    private static Task<int> WriteError(TextWriter output, string error)
    {
        return WriteLines(output, 1, $"Error: {error}");
    }

    private static Task<int> WriteError(TextWriter output, string outputFormat, string error)
    {
        return outputFormat == "json"
            ? WriteJson(output, new JournalErrorOutput(false, error), 1)
            : WriteError(output, error);
    }

    private static async Task<int> WriteLines(TextWriter output, int exitCode, params string[] lines)
    {
        foreach (var line in lines)
            await output.WriteLineAsync(line);
        return exitCode;
    }

    private static async Task<int> WriteJson(TextWriter output, object value, int exitCode)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(value, JsonOptions));
        return exitCode;
    }

    private sealed record JournalPaths(string JournalPath, string SignaturePath, string KeyPath);

    private sealed record JournalErrorOutput(bool Success, string Error);

    private sealed record JournalShowResult(
        string JournalPath,
        int EntryCount,
        int ShownCount,
        IReadOnlyList<JournalEntrySummary> Entries);

    private static JournalReviewResult CreateReviewResult(
        string journalPath,
        int totalEntryCount,
        IReadOnlyList<JournalEntrySummary> entries,
        int highImpactThreshold)
    {
        var items = entries
            .Select(entry => JournalReviewItem.From(entry, highImpactThreshold))
            .ToList();
        var affectedElementIds = entries
            .SelectMany(entry => entry.AffectedElementIds)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        var highlighted = items
            .Where(item => item.RequiresAttention)
            .OrderByDescending(item => item.AffectedElementCount)
            .ThenByDescending(item => item.LineNumber)
            .Take(10)
            .ToList();

        return new JournalReviewResult(
            "journal-review.v1",
            true,
            journalPath,
            totalEntryCount,
            entries.Count,
            highImpactThreshold,
            highlighted.Count > 0,
            entries.Sum(entry => entry.AffectedElementCount ?? 0),
            affectedElementIds.Count,
            affectedElementIds.Take(30).ToList(),
            CountByRisk(items),
            CountBy(entries, entry => entry.Action),
            CountBy(entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Category)), entry => entry.Category!),
            CountBy(entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Operator)), entry => entry.Operator!),
            CountBy(entries.Where(entry => !string.IsNullOrWhiteSpace(entry.User)), entry => entry.User!),
            highlighted,
            items);
    }

    private static IReadOnlyList<JournalReviewCount> CountByRisk(IReadOnlyList<JournalReviewItem> items) =>
        items
            .GroupBy(item => item.Risk, StringComparer.OrdinalIgnoreCase)
            .Select(group => new JournalReviewCount(
                group.Key,
                group.Count(),
                group.Sum(item => item.AffectedElementCount)))
            .OrderBy(item => RiskRank(item.Name))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<JournalReviewCount> CountBy(
        IEnumerable<JournalEntrySummary> entries,
        Func<JournalEntrySummary, string> keySelector) =>
        entries
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(group => new JournalReviewCount(
                group.Key,
                group.Count(),
                group.Sum(entry => entry.AffectedElementCount ?? 0)))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

    private static IReadOnlyList<string> RenderReviewTable(JournalReviewResult review)
    {
        var lines = new List<string>
        {
            $"OK: Journal: {review.JournalPath}",
            $"OK: Reviewed {review.ReviewedCount} of {review.EntryCount} entries",
            $"OK: Requires attention: {review.RequiresAttention.ToString().ToLowerInvariant()}",
            $"OK: Affected elements: {review.AffectedElementCount}",
            $"OK: Distinct affected IDs: {review.DistinctAffectedElementCount}"
        };

        AppendReviewCounts(lines, "Risk", review.Risks);
        AppendReviewCounts(lines, "Actions", review.Actions);
        AppendReviewCounts(lines, "Categories", review.Categories);
        AppendReviewCounts(lines, "Operators", review.Operators);

        if (review.AffectedElementIds.Count > 0)
        {
            lines.Add($"Affected IDs: {string.Join(", ", review.AffectedElementIds)}" +
                (review.DistinctAffectedElementCount > review.AffectedElementIds.Count ? ", ..." : ""));
        }

        if (review.HighlightedEntries.Count > 0)
        {
            lines.Add("Review:");
            foreach (var item in review.HighlightedEntries)
            {
                lines.Add(
                    $"  {item.Risk,-11} line {item.LineNumber,4} {item.Timestamp ?? "-",-27} {item.Action,-12} affected={item.AffectedElementCount} {item.Summary}");
            }
        }
        else
        {
            lines.Add("INFO: No high-impact or mutating journal entries need attention in this window.");
        }

        return lines;
    }

    private static IReadOnlyList<string> RenderReviewMarkdown(JournalReviewResult review)
    {
        var lines = new List<string>
        {
            "# Journal Review",
            "",
            $"- Journal: `{review.JournalPath}`",
            $"- Reviewed entries: {review.ReviewedCount} of {review.EntryCount}",
            $"- Requires attention: {review.RequiresAttention.ToString().ToLowerInvariant()}",
            $"- Affected elements: {review.AffectedElementCount}",
            $"- Distinct affected IDs: {review.DistinctAffectedElementCount}",
            ""
        };

        AppendMarkdownTable(lines, "Risk", review.Risks);
        AppendMarkdownTable(lines, "Actions", review.Actions);
        AppendMarkdownTable(lines, "Categories", review.Categories);
        AppendMarkdownTable(lines, "Operators", review.Operators);

        lines.Add("## Review");
        if (review.HighlightedEntries.Count == 0)
        {
            lines.Add("");
            lines.Add("No high-impact or mutating journal entries need attention in this window.");
            return lines;
        }

        lines.Add("");
        lines.Add("| Risk | Line | Timestamp | Action | Affected | Summary |");
        lines.Add("| --- | ---: | --- | --- | ---: | --- |");
        foreach (var item in review.HighlightedEntries)
        {
            lines.Add(
                $"| {EscapeMarkdown(item.Risk)} | {item.LineNumber} | {EscapeMarkdown(item.Timestamp ?? "-")} | {EscapeMarkdown(item.Action)} | {item.AffectedElementCount} | {EscapeMarkdown(item.Summary)} |");
        }

        return lines;
    }

    private static void AppendReviewCounts(IList<string> lines, string title, IReadOnlyList<JournalReviewCount> counts)
    {
        if (counts.Count == 0)
            return;

        lines.Add($"{title}:");
        foreach (var count in counts)
            lines.Add($"  {count.Name,-12} {count.Count} affected={count.AffectedElementCount}");
    }

    private static void AppendMarkdownTable(IList<string> lines, string title, IReadOnlyList<JournalReviewCount> counts)
    {
        if (counts.Count == 0)
            return;

        lines.Add($"## {title}");
        lines.Add("");
        lines.Add("| Name | Entries | Affected |");
        lines.Add("| --- | ---: | ---: |");
        foreach (var count in counts)
            lines.Add($"| {EscapeMarkdown(count.Name)} | {count.Count} | {count.AffectedElementCount} |");
        lines.Add("");
    }

    private static string ClassifyRisk(JournalEntrySummary entry, int highImpactThreshold)
    {
        var affected = entry.AffectedElementCount ?? 0;
        if (affected >= highImpactThreshold)
            return "high-impact";
        if (IsMutatingAction(entry.Action) || affected > 0)
            return "write";
        if (IsDeliveryAction(entry.Action))
            return "delivery";
        return "info";
    }

    private static bool IsMutatingAction(string action) =>
        action.Contains("set", StringComparison.OrdinalIgnoreCase)
        || action.Contains("import", StringComparison.OrdinalIgnoreCase)
        || action.Contains("fix", StringComparison.OrdinalIgnoreCase)
        || action.Contains("rollback", StringComparison.OrdinalIgnoreCase)
        || action.Contains("apply", StringComparison.OrdinalIgnoreCase)
        || action.Contains("purge", StringComparison.OrdinalIgnoreCase);

    private static bool IsDeliveryAction(string action) =>
        action.Contains("publish", StringComparison.OrdinalIgnoreCase)
        || action.Contains("export", StringComparison.OrdinalIgnoreCase)
        || action.Contains("deliver", StringComparison.OrdinalIgnoreCase);

    private static int RiskRank(string risk) => risk.ToLowerInvariant() switch
    {
        "high-impact" => 0,
        "write" => 1,
        "delivery" => 2,
        _ => 3
    };

    private static bool RiskRequiresAttention(string risk) => risk is "high-impact" or "write";

    private static string EscapeMarkdown(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);

    private sealed record JournalReviewResult(
        string SchemaVersion,
        bool Success,
        string JournalPath,
        int EntryCount,
        int ReviewedCount,
        int HighImpactThreshold,
        bool RequiresAttention,
        int AffectedElementCount,
        int DistinctAffectedElementCount,
        IReadOnlyList<long> AffectedElementIds,
        IReadOnlyList<JournalReviewCount> Risks,
        IReadOnlyList<JournalReviewCount> Actions,
        IReadOnlyList<JournalReviewCount> Categories,
        IReadOnlyList<JournalReviewCount> Operators,
        IReadOnlyList<JournalReviewCount> Users,
        IReadOnlyList<JournalReviewItem> HighlightedEntries,
        IReadOnlyList<JournalReviewItem> Entries);

    private sealed record JournalReviewCount(string Name, int Count, int AffectedElementCount);

    private sealed record JournalReviewItem(
        int LineNumber,
        string? Timestamp,
        string Action,
        string? Category,
        string? Operator,
        string? User,
        int AffectedElementCount,
        IReadOnlyList<long> AffectedElementIds,
        string Risk,
        bool RequiresAttention,
        string Summary)
    {
        public static JournalReviewItem From(JournalEntrySummary entry, int highImpactThreshold)
        {
            var risk = ClassifyRisk(entry, highImpactThreshold);
            return new JournalReviewItem(
                entry.LineNumber,
                entry.Timestamp,
                entry.Action,
                entry.Category,
                entry.Operator,
                entry.User,
                entry.AffectedElementCount ?? 0,
                entry.AffectedElementIds,
                risk,
                RiskRequiresAttention(risk),
                entry.Summary);
        }
    }
}
