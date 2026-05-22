using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RevitCli.Shared;

public class ViewInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("viewType")]
    public string ViewType { get; set; } = "";

    [JsonPropertyName("isTemplate")]
    public bool IsTemplate { get; set; }

    [JsonPropertyName("templateId")]
    public long? TemplateId { get; set; }

    [JsonPropertyName("templateName")]
    public string? TemplateName { get; set; }

    [JsonPropertyName("canBePrinted")]
    public bool CanBePrinted { get; set; }

    [JsonPropertyName("isPlacedOnSheet")]
    public bool IsPlacedOnSheet { get; set; }

    [JsonPropertyName("isLocked")]
    public bool IsLocked { get; set; }

    [JsonPropertyName("parameters")]
    public Dictionary<string, string> Parameters { get; set; } = new();
}
