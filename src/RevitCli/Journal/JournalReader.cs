using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RevitCli.Journal;

internal static class JournalReader
{
    public static JournalReadResult Read(string journalPath)
    {
        if (!File.Exists(journalPath))
            throw new FileNotFoundException($"Journal file not found: {journalPath}", journalPath);

        var entries = new List<JournalEntrySummary>();
        var lines = File.ReadAllLines(journalPath);
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"Journal line {lineNumber} is not a JSON object.");

                var action = ReadString(doc.RootElement, "action") ?? "(unknown)";
                var timestamp = ReadString(doc.RootElement, "timestamp");
                var affectedIds = ReadAffectedElementIds(doc.RootElement);
                entries.Add(new JournalEntrySummary(
                    lineNumber,
                    timestamp,
                    action,
                    ReadString(doc.RootElement, "category"),
                    ReadString(doc.RootElement, "user"),
                    ReadOperator(doc.RootElement),
                    ReadAffectedElementCount(doc.RootElement, affectedIds),
                    affectedIds,
                    BuildSummary(doc.RootElement),
                    line));
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Journal line {lineNumber} is not valid JSON: {ex.Message}", ex);
            }
        }

        return new JournalReadResult(Path.GetFullPath(journalPath), entries);
    }

    public static JournalStatsResult GetStats(JournalReadResult journal)
    {
        var firstTimestamp = default(DateTimeOffset?);
        var lastTimestamp = default(DateTimeOffset?);
        foreach (var entry in journal.Entries)
        {
            if (!TryParseTimestamp(entry.Timestamp, out var timestamp))
                continue;

            if (!firstTimestamp.HasValue || timestamp < firstTimestamp.Value)
                firstTimestamp = timestamp;
            if (!lastTimestamp.HasValue || timestamp > lastTimestamp.Value)
                lastTimestamp = timestamp;
        }

        var actions = journal.Entries
            .GroupBy(entry => entry.Action, StringComparer.OrdinalIgnoreCase)
            .Select(group => new JournalActionCount(
                group.Key,
                group.Count(),
                group.Sum(entry => entry.AffectedElementCount ?? 0)))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Action, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var categories = journal.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Category))
            .GroupBy(entry => entry.Category!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new JournalNamedCount(
                group.Key,
                group.Count(),
                group.Sum(entry => entry.AffectedElementCount ?? 0)))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var users = journal.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.User))
            .GroupBy(entry => entry.User!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new JournalNamedCount(
                group.Key,
                group.Count(),
                group.Sum(entry => entry.AffectedElementCount ?? 0)))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var operators = journal.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Operator))
            .GroupBy(entry => entry.Operator!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new JournalNamedCount(
                group.Key,
                group.Count(),
                group.Sum(entry => entry.AffectedElementCount ?? 0)))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var affectedElementIds = journal.Entries
            .SelectMany(entry => entry.AffectedElementIds)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        return new JournalStatsResult(
            journal.JournalPath,
            journal.Entries.Count,
            firstTimestamp?.ToString("o", CultureInfo.InvariantCulture),
            lastTimestamp?.ToString("o", CultureInfo.InvariantCulture),
            journal.Entries.Sum(entry => entry.AffectedElementCount ?? 0),
            affectedElementIds.Count,
            affectedElementIds,
            actions,
            categories,
            users,
            operators);
    }

    private static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out timestamp);
    }

    private static string? ReadString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? ReadOperator(JsonElement root)
    {
        return ReadString(root, "operator")
            ?? ReadString(root, "user")
            ?? ReadString(root, "appliedBy");
    }

    private static int? ReadAffectedElementCount(JsonElement root, IReadOnlyList<long> affectedElementIds)
    {
        foreach (var name in new[] { "affectedElements", "affected", "purged", "exported" })
        {
            if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number)
                continue;

            if (property.TryGetInt32(out var count) && count >= 0)
                return count;
        }

        if (affectedElementIds.Count > 0)
            return affectedElementIds.Count;

        return null;
    }

    private static IReadOnlyList<long> ReadAffectedElementIds(JsonElement root)
    {
        var ids = new List<long>();
        foreach (var name in new[] { "affectedElementIds", "elementIds", "ids" })
        {
            if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in property.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var id))
                    ids.Add(id);
            }
        }

        if (root.TryGetProperty("elementId", out var elementId)
            && elementId.ValueKind == JsonValueKind.Number
            && elementId.TryGetInt64(out var singleId))
        {
            ids.Add(singleId);
        }

        return ids
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }

    private static string BuildSummary(JsonElement root)
    {
        var parts = new List<string>();
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("action") || property.NameEquals("timestamp"))
                continue;

            parts.Add($"{property.Name}={FormatValue(property.Value)}");
            if (parts.Count == 4)
                break;
        }

        return parts.Count == 0 ? "" : string.Join(", ", parts);
    }

    private static string FormatValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => value.GetRawText()
        };
    }
}

internal sealed record JournalReadResult(
    string JournalPath,
    IReadOnlyList<JournalEntrySummary> Entries);

internal sealed record JournalEntrySummary(
    int LineNumber,
    string? Timestamp,
    string Action,
    string? Category,
    string? User,
    string? Operator,
    int? AffectedElementCount,
    IReadOnlyList<long> AffectedElementIds,
    string Summary,
    string Raw);

internal sealed record JournalStatsResult(
    string JournalPath,
    int EntryCount,
    string? FirstTimestamp,
    string? LastTimestamp,
    int AffectedElementCount,
    int DistinctAffectedElementCount,
    IReadOnlyList<long> AffectedElementIds,
    IReadOnlyList<JournalActionCount> Actions,
    IReadOnlyList<JournalNamedCount> Categories,
    IReadOnlyList<JournalNamedCount> Users,
    IReadOnlyList<JournalNamedCount> Operators);

internal sealed record JournalActionCount(string Action, int Count, int AffectedElementCount);

internal sealed record JournalNamedCount(string Name, int Count, int AffectedElementCount);
