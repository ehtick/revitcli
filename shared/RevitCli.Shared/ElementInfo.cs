using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RevitCli.Shared;

public class ElementInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("typeName")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("parameters")]
    public Dictionary<string, string> Parameters { get; set; } = new();

    [JsonPropertyName("parameterMetadata")]
    public List<ElementParameterInfo> ParameterMetadata { get; set; } = new();
}

public class ElementParameterInfo
{
    /// <summary>
    /// Command-ready parameter name. Duplicate Revit definitions use "Name [N]".
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("definitionName")]
    public string DefinitionName { get; set; } = "";

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("storageType")]
    public string StorageType { get; set; } = "";

    [JsonPropertyName("hasValue")]
    public bool HasValue { get; set; }

    [JsonPropertyName("isReadOnly")]
    public bool IsReadOnly { get; set; }

    [JsonPropertyName("canWrite")]
    public bool CanWrite { get; set; }
}
