using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using RevitCli.Output;
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
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "plan-receipt.v1";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "plan.apply";

    [JsonPropertyName("planPath")]
    public string PlanPath { get; set; } = "";

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("appliedAtUtc")]
    public string AppliedAtUtc { get; set; } = "";

    [JsonPropertyName("operator")]
    public string Operator { get; set; } = "";

    [JsonPropertyName("user")]
    public string User { get; set; } = "";

    [JsonPropertyName("appliedBy")]
    public string AppliedBy { get; set; } = "";

    [JsonPropertyName("machine")]
    public string Machine { get; set; } = "";

    [JsonPropertyName("modelPath")]
    public string? ModelPath { get; set; }

    [JsonPropertyName("documentName")]
    public string? DocumentName { get; set; }

    [JsonPropertyName("documentVersion")]
    public string? DocumentVersion { get; set; }

    [JsonPropertyName("affected")]
    public int Affected { get; set; }

    [JsonPropertyName("affectedElementIds")]
    public List<long> AffectedElementIds { get; set; } = new();

    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("requiresRollback")]
    public bool RequiresRollback { get; set; }

    [JsonPropertyName("operation")]
    public string Operation { get; set; } = "";

    [JsonPropertyName("param")]
    public string Param { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("preview")]
    public List<SetPreviewItem> Preview { get; set; } = new();

    [JsonPropertyName("rollbackActions")]
    public List<PlanReceiptRollbackAction> RollbackActions { get; set; } = new();

    [JsonPropertyName("linkRepairActions")]
    public List<PlanReceiptLinkRepairAction> LinkRepairActions { get; set; } = new();

    [JsonPropertyName("modelMapActions")]
    public List<PlanReceiptModelMapAction> ModelMapActions { get; set; } = new();

    [JsonPropertyName("groupCount")]
    public int GroupCount { get; set; }

    [JsonPropertyName("elementWrites")]
    public int ElementWrites { get; set; }

    [JsonPropertyName("baselinePath")]
    public string? BaselinePath { get; set; }

    [JsonPropertyName("journalPath")]
    public string? JournalPath { get; set; }

    [JsonPropertyName("actionCount")]
    public int ActionCount { get; set; }

    [JsonPropertyName("groups")]
    public List<ImportGroup> Groups { get; set; } = new();

    [JsonPropertyName("failures")]
    public List<PlanApplyFailure> Failures { get; set; } = new();
}

public sealed class PlanReceiptRollbackAction
{
    [JsonPropertyName("elementId")]
    public long ElementId { get; set; }

    [JsonPropertyName("param")]
    public string Param { get; set; } = "";

    [JsonPropertyName("oldValue")]
    public string? OldValue { get; set; }

    [JsonPropertyName("newValue")]
    public string? NewValue { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";
}

public sealed class PlanReceiptLinkRepairAction
{
    [JsonPropertyName("linkId")]
    public long LinkId { get; set; }

    [JsonPropertyName("linkTypeId")]
    public long? LinkTypeId { get; set; }

    [JsonPropertyName("linkName")]
    public string LinkName { get; set; } = "";

    [JsonPropertyName("typeName")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("oldPath")]
    public string OldPath { get; set; } = "";

    [JsonPropertyName("newPath")]
    public string NewPath { get; set; } = "";

    [JsonPropertyName("oldLoaded")]
    public bool OldLoaded { get; set; }

    [JsonPropertyName("newLoaded")]
    public bool NewLoaded { get; set; }

    [JsonPropertyName("oldPathExists")]
    public bool OldPathExists { get; set; }

    [JsonPropertyName("newPathExists")]
    public bool NewPathExists { get; set; }

    [JsonPropertyName("oldPathLastWriteTimeUtc")]
    public string? OldPathLastWriteTimeUtc { get; set; }

    [JsonPropertyName("newPathLastWriteTimeUtc")]
    public string? NewPathLastWriteTimeUtc { get; set; }

    [JsonPropertyName("oldPathSizeBytes")]
    public long? OldPathSizeBytes { get; set; }

    [JsonPropertyName("newPathSizeBytes")]
    public long? NewPathSizeBytes { get; set; }
}

public sealed class PlanReceiptModelMapAction
{
    [JsonPropertyName("elementId")]
    public long ElementId { get; set; }

    [JsonPropertyName("elementName")]
    public string ElementName { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    [JsonPropertyName("oldValue")]
    public string? OldValue { get; set; }

    [JsonPropertyName("newValue")]
    public string NewValue { get; set; } = "";
}

public sealed class ImportPlanFile
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "import";

    [JsonPropertyName("createdAtUtc")]
    public string CreatedAtUtc { get; set; } = "";

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; set; } = "";

    [JsonPropertyName("summary")]
    public ImportPlanSummary Summary { get; set; } = new();

    [JsonPropertyName("groups")]
    public List<ImportGroup> Groups { get; set; } = new();

    [JsonPropertyName("previewGroups")]
    public List<ImportPlanPreviewGroup> PreviewGroups { get; set; } = new();

    [JsonPropertyName("misses")]
    public List<ImportMiss> Misses { get; set; } = new();

    [JsonPropertyName("duplicates")]
    public List<ImportDuplicate> Duplicates { get; set; } = new();

    [JsonPropertyName("skipped")]
    public List<ImportSkip> Skipped { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    [JsonPropertyName("commands")]
    public SetPlanCommands Commands { get; set; } = new();

    public static ImportPlanFile Create(
        string sourceCsv,
        CsvData csv,
        IReadOnlyDictionary<string, string> mapping,
        string category,
        string matchBy,
        string onMissing,
        string onDuplicate,
        int batchSize,
        ImportPlan plan,
        List<ImportPlanPreviewGroup> previewGroups,
        string planPath)
    {
        var normalizedPlanPath = Path.GetFullPath(planPath);
        return new ImportPlanFile
        {
            CreatedAtUtc = DateTime.UtcNow.ToString("o"),
            CreatedBy = Environment.UserName,
            Summary = new ImportPlanSummary
            {
                Operation = "import",
                SourceCsv = Path.GetFullPath(sourceCsv),
                Category = category,
                MatchBy = matchBy,
                Encoding = csv.EncodingName,
                CsvRows = csv.Rows.Count,
                MappedColumns = new Dictionary<string, string>(mapping, StringComparer.OrdinalIgnoreCase),
                OnMissing = onMissing,
                OnDuplicate = onDuplicate,
                BatchSize = batchSize,
                GroupCount = plan.Groups.Count,
                ElementWrites = plan.Groups.Sum(group => group.ElementIds.Count)
            },
            Groups = plan.Groups,
            PreviewGroups = previewGroups,
            Misses = plan.Misses,
            Duplicates = plan.Duplicates,
            Skipped = plan.Skipped,
            Warnings = plan.Warnings,
            Commands = new SetPlanCommands
            {
                Show = $"revitcli plan show {QuoteArgument(normalizedPlanPath)}",
                Apply = $"revitcli plan apply {QuoteArgument(normalizedPlanPath)} --yes",
                DryRunApply = $"revitcli plan apply {QuoteArgument(normalizedPlanPath)} --dry-run"
            }
        };
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}

public sealed class ImportPlanSummary
{
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = "";

    [JsonPropertyName("sourceCsv")]
    public string SourceCsv { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("matchBy")]
    public string MatchBy { get; set; } = "";

    [JsonPropertyName("encoding")]
    public string Encoding { get; set; } = "";

    [JsonPropertyName("csvRows")]
    public int CsvRows { get; set; }

    [JsonPropertyName("mappedColumns")]
    public Dictionary<string, string> MappedColumns { get; set; } = new();

    [JsonPropertyName("onMissing")]
    public string OnMissing { get; set; } = "";

    [JsonPropertyName("onDuplicate")]
    public string OnDuplicate { get; set; } = "";

    [JsonPropertyName("batchSize")]
    public int BatchSize { get; set; } = 100;

    [JsonPropertyName("groupCount")]
    public int GroupCount { get; set; }

    [JsonPropertyName("elementWrites")]
    public int ElementWrites { get; set; }
}

public sealed class ImportPlanPreviewGroup
{
    [JsonPropertyName("param")]
    public string Param { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("elementIds")]
    public List<long> ElementIds { get; set; } = new();

    [JsonPropertyName("preview")]
    public List<SetPreviewItem> Preview { get; set; } = new();
}

public sealed class PlanApplyFailure
{
    [JsonPropertyName("param")]
    public string Param { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("elementIds")]
    public List<long> ElementIds { get; set; } = new();

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
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

    public static string ReadType(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("type", out var type)
            ? type.GetString() ?? ""
            : "";
    }

    public static ImportPlanFile LoadImport(string path)
    {
        var json = File.ReadAllText(path);
        var plan = JsonSerializer.Deserialize<ImportPlanFile>(json, JsonOptions)
            ?? throw new InvalidOperationException("Plan file is empty or invalid JSON.");

        if (plan.SchemaVersion != 1)
            throw new InvalidOperationException($"Unsupported plan schemaVersion '{plan.SchemaVersion}'.");

        if (!string.Equals(plan.Type, "import", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported plan type '{plan.Type}'.");

        if (plan.Groups == null)
            throw new InvalidOperationException("Plan file is missing import groups.");

        return plan;
    }

    internal static FixPlanFile LoadFix(string path)
    {
        var json = File.ReadAllText(path);
        var plan = JsonSerializer.Deserialize<FixPlanFile>(json, JsonOptions)
            ?? throw new InvalidOperationException("Plan file is empty or invalid JSON.");

        if (plan.SchemaVersion != 1)
            throw new InvalidOperationException($"Unsupported plan schemaVersion '{plan.SchemaVersion}'.");

        if (!string.Equals(plan.Type, "fix", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported plan type '{plan.Type}'.");

        if (plan.Actions == null)
            throw new InvalidOperationException("Plan file is missing fix actions.");

        return plan;
    }

    public static void SaveImport(string path, ImportPlanFile plan)
    {
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, JsonSerializer.Serialize(plan, JsonOptions));
    }

    internal static void SaveFix(string path, FixPlanFile plan)
    {
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, JsonSerializer.Serialize(plan, JsonOptions));
    }

    public static string SaveReceipt(string planPath, PlanReceipt receipt)
    {
        var receiptPath = Path.GetFullPath(planPath) + ".receipt.json";
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, JsonOptions));
        return receiptPath;
    }
}
