using System.Text.Json.Serialization;

namespace RevitCli.Shared;

public class LinkInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("linkTypeId")]
    public long? LinkTypeId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("typeName")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("linkedFileStatus")]
    public string LinkedFileStatus { get; set; } = "";

    [JsonPropertyName("isLoaded")]
    public bool IsLoaded { get; set; }

    [JsonPropertyName("pathExists")]
    public bool PathExists { get; set; }

    [JsonPropertyName("isCloud")]
    public bool IsCloud { get; set; }

    [JsonPropertyName("worksetName")]
    public string? WorksetName { get; set; }

    [JsonPropertyName("transformOrigin")]
    public string TransformOrigin { get; set; } = "";

    [JsonPropertyName("transformFingerprint")]
    public string TransformFingerprint { get; set; } = "";

    [JsonPropertyName("lastWriteTimeUtc")]
    public string? LastWriteTimeUtc { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; set; }
}
