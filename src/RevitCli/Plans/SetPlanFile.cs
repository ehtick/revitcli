using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using RevitCli.Shared;

namespace RevitCli.Plans;

public sealed class SetPlanFile
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "set";

    [JsonPropertyName("createdAtUtc")]
    public string CreatedAtUtc { get; set; } = "";

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; set; } = "";

    [JsonPropertyName("summary")]
    public SetPlanSummary Summary { get; set; } = new();

    [JsonPropertyName("originalRequest")]
    public SetRequest OriginalRequest { get; set; } = new();

    [JsonPropertyName("applyRequest")]
    public SetRequest ApplyRequest { get; set; } = new();

    [JsonPropertyName("preview")]
    public List<SetPreviewItem> Preview { get; set; } = new();

    [JsonPropertyName("commands")]
    public SetPlanCommands Commands { get; set; } = new();

    public static SetPlanFile Create(SetRequest originalRequest, SetResult dryRunResult, string planPath)
    {
        var frozenIds = dryRunResult.Preview
            .Select(item => item.Id)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        var applyRequest = new SetRequest
        {
            ElementIds = frozenIds,
            Param = originalRequest.Param,
            Value = originalRequest.Value,
            DryRun = false
        };

        var normalizedPlanPath = NormalizePathForCommand(planPath);
        return new SetPlanFile
        {
            CreatedAtUtc = DateTime.UtcNow.ToString("o"),
            CreatedBy = Environment.UserName,
            Summary = new SetPlanSummary
            {
                Operation = "set",
                Param = originalRequest.Param,
                Value = originalRequest.Value,
                Affected = dryRunResult.Affected,
                FrozenElementIds = frozenIds,
                OriginalTarget = DescribeTarget(originalRequest),
                ApplyTarget = "frozen elementIds"
            },
            OriginalRequest = originalRequest,
            ApplyRequest = applyRequest,
            Preview = dryRunResult.Preview,
            Commands = new SetPlanCommands
            {
                Show = $"revitcli plan show {QuoteArgument(normalizedPlanPath)}",
                Apply = $"revitcli plan apply {QuoteArgument(normalizedPlanPath)} --yes",
                DryRunApply = $"revitcli plan apply {QuoteArgument(normalizedPlanPath)} --dry-run"
            }
        };
    }

    private static string DescribeTarget(SetRequest request)
    {
        if (request.ElementId.HasValue)
            return $"elementId={request.ElementId.Value}";

        if (request.ElementIds is { Count: > 0 })
            return $"elementIds={request.ElementIds.Count}";

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            return string.IsNullOrWhiteSpace(request.Filter)
                ? $"category={request.Category}"
                : $"category={request.Category}, filter={request.Filter}";
        }

        return "unknown";
    }

    private static string NormalizePathForCommand(string planPath)
    {
        if (string.IsNullOrWhiteSpace(planPath))
            return planPath;

        return Path.GetFullPath(planPath);
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}

public sealed class SetPlanSummary
{
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = "";

    [JsonPropertyName("param")]
    public string Param { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("affected")]
    public int Affected { get; set; }

    [JsonPropertyName("frozenElementIds")]
    public List<long> FrozenElementIds { get; set; } = new();

    [JsonPropertyName("originalTarget")]
    public string OriginalTarget { get; set; } = "";

    [JsonPropertyName("applyTarget")]
    public string ApplyTarget { get; set; } = "";
}

public sealed class SetPlanCommands
{
    [JsonPropertyName("show")]
    public string Show { get; set; } = "";

    [JsonPropertyName("apply")]
    public string Apply { get; set; } = "";

    [JsonPropertyName("dryRunApply")]
    public string DryRunApply { get; set; } = "";
}

public sealed class PlanReceipt
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "plan.apply";

    [JsonPropertyName("planPath")]
    public string PlanPath { get; set; } = "";

    [JsonPropertyName("appliedAtUtc")]
    public string AppliedAtUtc { get; set; } = "";

    [JsonPropertyName("appliedBy")]
    public string AppliedBy { get; set; } = "";

    [JsonPropertyName("affected")]
    public int Affected { get; set; }

    [JsonPropertyName("param")]
    public string Param { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("preview")]
    public List<SetPreviewItem> Preview { get; set; } = new();
}

public static class SetPlanFileStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Save(string path, SetPlanFile plan)
    {
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, JsonSerializer.Serialize(plan, JsonOptions));
    }

    public static SetPlanFile Load(string path)
    {
        var json = File.ReadAllText(path);
        var plan = JsonSerializer.Deserialize<SetPlanFile>(json, JsonOptions)
            ?? throw new InvalidOperationException("Plan file is empty or invalid JSON.");

        if (plan.SchemaVersion != 1)
            throw new InvalidOperationException($"Unsupported plan schemaVersion '{plan.SchemaVersion}'.");

        if (!string.Equals(plan.Type, "set", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported plan type '{plan.Type}'.");

        if (plan.ApplyRequest == null || string.IsNullOrWhiteSpace(plan.ApplyRequest.Param))
            throw new InvalidOperationException("Plan file is missing a set apply request.");

        return plan;
    }

    public static string SaveReceipt(string planPath, PlanReceipt receipt)
    {
        var receiptPath = Path.GetFullPath(planPath) + ".receipt.json";
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, JsonOptions));
        return receiptPath;
    }
}
