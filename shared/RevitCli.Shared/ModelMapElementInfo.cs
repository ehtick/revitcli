using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RevitCli.Shared;

public class ModelMapElementInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("typeName")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("worksetName")]
    public string? WorksetName { get; set; }

    [JsonPropertyName("phaseCreated")]
    public string? PhaseCreated { get; set; }

    [JsonPropertyName("phaseDemolished")]
    public string? PhaseDemolished { get; set; }

    [JsonPropertyName("canWriteWorkset")]
    public bool CanWriteWorkset { get; set; }

    [JsonPropertyName("canWritePhaseCreated")]
    public bool CanWritePhaseCreated { get; set; }

    [JsonPropertyName("canWritePhaseDemolished")]
    public bool CanWritePhaseDemolished { get; set; }

    [JsonPropertyName("availableWorksets")]
    public List<string> AvailableWorksets { get; set; } = new();

    [JsonPropertyName("availablePhases")]
    public List<string> AvailablePhases { get; set; } = new();
}
