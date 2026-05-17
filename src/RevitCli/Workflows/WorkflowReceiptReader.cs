using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitCli.Workflows;

public static class WorkflowReceiptReader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static WorkflowReceiptListReport Read(
        string? projectDirectory,
        int limit,
        bool failedOnly)
    {
        if (limit < 1)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be at least 1.");

        var projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectDirectory!);
        var receiptDir = ResolveReceiptDirectory(projectRoot);
        var report = new WorkflowReceiptListReport
        {
            ProjectDirectory = projectRoot,
            ReceiptDirectory = receiptDir,
            Exists = Directory.Exists(receiptDir),
            Limit = limit,
            FailedOnly = failedOnly,
        };

        if (!report.Exists)
            return report;

        var summaries = new List<WorkflowReceiptSummary>();
        foreach (var path in Directory.EnumerateFiles(receiptDir, "*.json"))
        {
            var summary = ReadReceipt(path, report.Issues);
            if (summary != null)
                summaries.Add(summary);
        }

        var filtered = failedOnly
            ? summaries.Where(summary => !summary.Success)
            : summaries;

        var ordered = filtered
            .OrderByDescending(summary => ParseTimestamp(summary.CompletedAtUtc) ??
                                          ParseTimestamp(summary.StartedAtUtc) ??
                                          DateTimeOffset.MinValue)
            .ThenByDescending(summary => summary.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        report.ReceiptCount = ordered.Count;
        report.Receipts.AddRange(ordered.Take(limit));
        return report;
    }

    public static string ResolveReceiptDirectory(string projectRoot) =>
        Path.Combine(projectRoot, ".revitcli", "workflows", "receipts");

    private static WorkflowReceiptSummary? ReadReceipt(
        string path,
        IList<WorkflowReceiptIssue> issues)
    {
        WorkflowRunReport? receipt;
        string? schemaVersion;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            schemaVersion = TryGetString(document.RootElement, "schemaVersion");
            receipt = document.RootElement.Deserialize<WorkflowRunReport>(JsonOpts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            issues.Add(new WorkflowReceiptIssue(
                "error",
                path,
                $"workflow receipt is not readable JSON: {ex.Message}"));
            return null;
        }

        if (receipt == null)
        {
            issues.Add(new WorkflowReceiptIssue(
                "error",
                path,
                "workflow receipt is empty."));
            return null;
        }

        if (!string.Equals(schemaVersion, "workflow-run-receipt.v1", StringComparison.Ordinal))
        {
            issues.Add(new WorkflowReceiptIssue(
                "error",
                path,
                $"schemaVersion must be workflow-run-receipt.v1, got {schemaVersion ?? "(missing)"}."));
            return null;
        }

        return new WorkflowReceiptSummary
        {
            Path = Path.GetFullPath(path),
            Name = receipt.Name,
            Success = receipt.Success,
            DryRun = receipt.DryRun,
            ExitCode = receipt.ExitCode,
            StartedAtUtc = receipt.StartedAtUtc,
            CompletedAtUtc = receipt.CompletedAtUtc,
            StepCount = receipt.Steps.Count,
            FailedStepCount = receipt.Steps.Count(step =>
                string.Equals(step.Status, "failed", StringComparison.OrdinalIgnoreCase)),
            IssueCount = receipt.Issues.Count,
            Operator = receipt.Operator,
            Machine = receipt.Machine,
            Command = receipt.Command,
        };
    }

    private static DateTimeOffset? ParseTimestamp(string? timestamp)
    {
        return DateTimeOffset.TryParse(timestamp, out var value)
            ? value
            : null;
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}

public sealed class WorkflowReceiptListReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "workflow-receipts.v1";

    [JsonPropertyName("success")]
    public bool Success => ErrorCount == 0;

    [JsonPropertyName("projectDirectory")]
    public string ProjectDirectory { get; set; } = "";

    [JsonPropertyName("receiptDirectory")]
    public string ReceiptDirectory { get; set; } = "";

    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("failedOnly")]
    public bool FailedOnly { get; set; }

    [JsonPropertyName("receiptCount")]
    public int ReceiptCount { get; set; }

    [JsonPropertyName("returnedCount")]
    public int ReturnedCount => Receipts.Count;

    [JsonPropertyName("errorCount")]
    public int ErrorCount => Issues.Count(issue =>
        string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));

    [JsonPropertyName("receipts")]
    public List<WorkflowReceiptSummary> Receipts { get; } = new();

    [JsonPropertyName("issues")]
    public List<WorkflowReceiptIssue> Issues { get; } = new();
}

public sealed class WorkflowReceiptSummary
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }

    [JsonPropertyName("exitCode")]
    public int ExitCode { get; set; }

    [JsonPropertyName("startedAtUtc")]
    public string StartedAtUtc { get; set; } = "";

    [JsonPropertyName("completedAtUtc")]
    public string CompletedAtUtc { get; set; } = "";

    [JsonPropertyName("stepCount")]
    public int StepCount { get; set; }

    [JsonPropertyName("failedStepCount")]
    public int FailedStepCount { get; set; }

    [JsonPropertyName("issueCount")]
    public int IssueCount { get; set; }

    [JsonPropertyName("operator")]
    public string Operator { get; set; } = "";

    [JsonPropertyName("machine")]
    public string Machine { get; set; } = "";

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";
}

public sealed record WorkflowReceiptIssue(
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("message")] string Message);
