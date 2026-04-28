using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RevitCli.Shared;

/// <summary>
/// Response body for <c>POST /api/families/purge</c>. Per-family outcome
/// is returned so the CLI can show what was dropped vs what the addin
/// refused (e.g. instances reappeared between list and purge, or Revit
/// rejected the delete).
/// </summary>
public class FamilyPurgeResult
{
    [JsonPropertyName("purged")]
    public List<FamilyPurgedItem> Purged { get; set; } = new();

    [JsonPropertyName("skipped")]
    public List<FamilyPurgeSkipped> Skipped { get; set; } = new();

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }
}

public class FamilyPurgedItem
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";
}

public class FamilyPurgeSkipped
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}
