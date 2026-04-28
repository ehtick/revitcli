using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RevitCli.Shared;

public class ExportRequest
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = "";

    [JsonPropertyName("sheets")]
    public List<string> Sheets { get; set; } = new();

    [JsonPropertyName("views")]
    public List<string> Views { get; set; } = new();

    [JsonPropertyName("outputDir")]
    public string OutputDir { get; set; } = "";

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }
}
