using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RevitCli.Shared;

/// <summary>
/// Response body for <c>POST /api/families/export</c>. Per-family
/// outcome lets the CLI surface partial successes (e.g. 7 of 10 saved,
/// 3 failed because the family was an in-place family that can't be
/// edited as a separate doc).
/// </summary>
public class FamilyExportResult
{
    [JsonPropertyName("exported")]
    public List<FamilyExportedItem> Exported { get; set; } = new();

    [JsonPropertyName("failed")]
    public List<FamilyExportFailure> Failed { get; set; } = new();

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }

    [JsonPropertyName("outputDir")]
    public string OutputDir { get; set; } = "";
}

public class FamilyExportedItem
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }
}

public class FamilyExportFailure
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}
