using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitCli.Output;

public static class DeliveryManifestReader
{
    public static string ResolveManifestPath(string? projectDirectory)
    {
        var projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectDirectory!);
        return Path.Combine(projectRoot, ".revitcli", "deliveries", "manifest.jsonl");
    }

    public static DeliveryManifestReport Read(string? projectDirectory)
    {
        var projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectDirectory!);
        var manifestPath = ResolveManifestPath(projectRoot);
        var report = new DeliveryManifestReport
        {
            ManifestPath = manifestPath,
            Exists = File.Exists(manifestPath)
        };

        if (!report.Exists)
            return report;

        var lineNumber = 0;
        foreach (var rawLine in File.ReadLines(manifestPath))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(rawLine))
                continue;

            try
            {
                using var document = JsonDocument.Parse(rawLine);
                var entry = ReadEntry(document.RootElement, projectRoot, lineNumber, report.Issues);
                report.Entries.Add(entry);
            }
            catch (JsonException ex)
            {
                report.Issues.Add(new DeliveryManifestIssue(
                    lineNumber,
                    "error",
                    "manifest-json-invalid",
                    $"manifest line is not valid JSON: {ex.Message}"));
            }
        }

        return report;
    }

    private static DeliveryManifestEntry ReadEntry(
        JsonElement root,
        string projectRoot,
        int lineNumber,
        IList<DeliveryManifestIssue> issues)
    {
        var schemaVersion = GetString(root, "schemaVersion");
        var kind = GetString(root, "kind");
        var receiptPath = GetString(root, "receiptPath");
        var entry = new DeliveryManifestEntry
        {
            LineNumber = lineNumber,
            SchemaVersion = schemaVersion,
            Kind = kind,
            ReceiptPath = receiptPath,
            Success = GetBool(root, "success"),
            DryRun = GetBool(root, "dryRun"),
            Format = GetString(root, "format"),
            Pipeline = GetString(root, "pipeline"),
            OutputDir = GetString(root, "outputDir"),
            TaskId = GetString(root, "taskId"),
            Command = GetString(root, "command"),
            Timestamp = GetString(root, "timestamp")
        };

        if (!string.Equals(schemaVersion, "delivery-manifest.v1", StringComparison.Ordinal))
        {
            issues.Add(new DeliveryManifestIssue(
                lineNumber,
                "error",
                "manifest-schema-invalid",
                $"schemaVersion must be delivery-manifest.v1, got {schemaVersion ?? "(missing)"}."));
        }

        if (kind is not ("export" or "publish"))
        {
            issues.Add(new DeliveryManifestIssue(
                lineNumber,
                "error",
                "manifest-kind-invalid",
                $"kind must be export or publish, got {kind ?? "(missing)"}."));
        }

        if (string.IsNullOrWhiteSpace(receiptPath))
        {
            issues.Add(new DeliveryManifestIssue(
                lineNumber,
                "error",
                "receipt-path-missing",
                "receiptPath is required."));
            return entry;
        }

        try
        {
            entry.ResolvedReceiptPath = ResolveReceiptPath(projectRoot, receiptPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            issues.Add(new DeliveryManifestIssue(
                lineNumber,
                "error",
                "receipt-path-invalid",
                $"receiptPath is invalid: {ex.Message}"));
            return entry;
        }

        if (!File.Exists(entry.ResolvedReceiptPath))
        {
            issues.Add(new DeliveryManifestIssue(
                lineNumber,
                "error",
                "receipt-missing",
                $"receipt file not found: {entry.ResolvedReceiptPath}"));
            return entry;
        }

        entry.ReceiptExists = true;
        ReadReceipt(entry, issues);
        return entry;
    }

    private static void ReadReceipt(DeliveryManifestEntry entry, IList<DeliveryManifestIssue> issues)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(entry.ResolvedReceiptPath!));
            var root = document.RootElement;
            entry.ReceiptReadable = true;
            entry.ReceiptSchemaVersion = GetString(root, "schemaVersion");
            entry.ReceiptAction = GetString(root, "action");

            var expectedSchema = entry.Kind switch
            {
                "export" => "export-receipt.v1",
                "publish" => "publish-receipt.v1",
                _ => null
            };
            if (expectedSchema != null &&
                !string.Equals(entry.ReceiptSchemaVersion, expectedSchema, StringComparison.Ordinal))
            {
                issues.Add(new DeliveryManifestIssue(
                    entry.LineNumber,
                    "error",
                    "receipt-schema-invalid",
                    $"receipt schemaVersion must be {expectedSchema}, got {entry.ReceiptSchemaVersion ?? "(missing)"}."));
            }

            if (!string.IsNullOrWhiteSpace(entry.Kind) &&
                !string.Equals(entry.ReceiptAction, entry.Kind, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new DeliveryManifestIssue(
                    entry.LineNumber,
                    "error",
                    "receipt-action-mismatch",
                    $"receipt action must match manifest kind {entry.Kind}, got {entry.ReceiptAction ?? "(missing)"}."));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            issues.Add(new DeliveryManifestIssue(
                entry.LineNumber,
                "error",
                "receipt-json-invalid",
                $"receipt file is not readable JSON: {ex.Message}"));
        }
    }

    private static string ResolveReceiptPath(string projectRoot, string receiptPath)
    {
        return Path.IsPathRooted(receiptPath)
            ? Path.GetFullPath(receiptPath)
            : Path.GetFullPath(Path.Combine(projectRoot, receiptPath));
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool? GetBool(JsonElement root, string propertyName)
    {
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }
}

public sealed class DeliveryManifestReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "deliverables.v1";

    [JsonPropertyName("manifestPath")]
    public string ManifestPath { get; set; } = "";

    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("valid")]
    public bool Valid => Issues.All(issue => !string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));

    [JsonPropertyName("entryCount")]
    public int EntryCount => Entries.Count;

    [JsonPropertyName("entries")]
    public List<DeliveryManifestEntry> Entries { get; } = new();

    [JsonPropertyName("issues")]
    public List<DeliveryManifestIssue> Issues { get; } = new();

    [JsonPropertyName("stats")]
    public DeliveryManifestStats Stats => DeliveryManifestStats.From(Entries, Issues);
}

public sealed class DeliveryManifestEntry
{
    [JsonPropertyName("lineNumber")]
    public int LineNumber { get; set; }

    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("success")]
    public bool? Success { get; set; }

    [JsonPropertyName("dryRun")]
    public bool? DryRun { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("pipeline")]
    public string? Pipeline { get; set; }

    [JsonPropertyName("outputDir")]
    public string? OutputDir { get; set; }

    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("receiptPath")]
    public string? ReceiptPath { get; set; }

    [JsonPropertyName("resolvedReceiptPath")]
    public string? ResolvedReceiptPath { get; set; }

    [JsonPropertyName("receiptExists")]
    public bool ReceiptExists { get; set; }

    [JsonPropertyName("receiptReadable")]
    public bool ReceiptReadable { get; set; }

    [JsonPropertyName("receiptSchemaVersion")]
    public string? ReceiptSchemaVersion { get; set; }

    [JsonPropertyName("receiptAction")]
    public string? ReceiptAction { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}

public sealed record DeliveryManifestIssue(
    [property: JsonPropertyName("lineNumber")] int? LineNumber,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

public sealed record DeliveryManifestCount(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("count")] int Count);

public sealed class DeliveryManifestStats
{
    [JsonPropertyName("entryCount")]
    public int EntryCount { get; set; }

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("receiptMissingCount")]
    public int ReceiptMissingCount { get; set; }

    [JsonPropertyName("receiptUnreadableCount")]
    public int ReceiptUnreadableCount { get; set; }

    [JsonPropertyName("kinds")]
    public List<DeliveryManifestCount> Kinds { get; set; } = new();

    [JsonPropertyName("outcomes")]
    public List<DeliveryManifestCount> Outcomes { get; set; } = new();

    public static DeliveryManifestStats From(
        IReadOnlyList<DeliveryManifestEntry> entries,
        IReadOnlyList<DeliveryManifestIssue> issues)
    {
        return new DeliveryManifestStats
        {
            EntryCount = entries.Count,
            ErrorCount = issues.Count(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)),
            ReceiptMissingCount = entries.Count(entry => !entry.ReceiptExists),
            ReceiptUnreadableCount = entries.Count(entry => entry.ReceiptExists && !entry.ReceiptReadable),
            Kinds = entries
                .GroupBy(entry => string.IsNullOrWhiteSpace(entry.Kind) ? "unknown" : entry.Kind!)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new DeliveryManifestCount(group.Key, group.Count()))
                .ToList(),
            Outcomes = entries
                .GroupBy(entry => entry.Success switch
                {
                    true => "success",
                    false => "failed",
                    _ => "unknown"
                })
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new DeliveryManifestCount(group.Key, group.Count()))
                .ToList()
        };
    }
}
