using System.Text.Json.Serialization;

namespace RevitCli.Shared;

public class FamilyInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("isInPlace")]
    public bool IsInPlace { get; set; }

    [JsonPropertyName("isLoadable")]
    public bool IsLoadable { get; set; }

    [JsonPropertyName("filePath")]
    public string? FilePath { get; set; }

    /// <summary>
    /// True when at least one FamilyInstance of any FamilySymbol of this family
    /// exists (and is not an element type) in the active document.
    /// </summary>
    [JsonPropertyName("isPlaced")]
    public bool IsPlaced { get; set; }
}
