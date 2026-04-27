using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RevitCli.Reports;

/// <summary>
/// Minimal SARIF 2.1.0 record types used by <see cref="SarifWriter"/>.
/// Only the subset needed for RevitCli's audit output is modelled here — we do
/// not attempt to cover the entire SARIF 2.1.0 schema. See
/// https://docs.oasis-open.org/sarif/sarif/v2.1.0/sarif-v2.1.0.html for the full spec.
/// </summary>
public sealed record SarifLog
{
    [JsonPropertyName("$schema")]
    public string Schema { get; init; } =
        "https://json.schemastore.org/sarif-2.1.0.json";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "2.1.0";

    [JsonPropertyName("runs")]
    public IReadOnlyList<SarifRun> Runs { get; init; } = new List<SarifRun>();
}

public sealed record SarifRun
{
    [JsonPropertyName("tool")]
    public SarifTool Tool { get; init; } = new();

    [JsonPropertyName("results")]
    public IReadOnlyList<SarifResult> Results { get; init; } = new List<SarifResult>();
}

public sealed record SarifTool
{
    [JsonPropertyName("driver")]
    public SarifToolComponent Driver { get; init; } = new();
}

public sealed record SarifToolComponent
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("informationUri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InformationUri { get; init; }
}

public sealed record SarifResult
{
    [JsonPropertyName("ruleId")]
    public string RuleId { get; init; } = "";

    [JsonPropertyName("level")]
    public string Level { get; init; } = "note";

    [JsonPropertyName("message")]
    public SarifMessage Message { get; init; } = new();

    [JsonPropertyName("locations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SarifLocation>? Locations { get; init; }

    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, object>? Properties { get; init; }
}

public sealed record SarifMessage
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";
}

public sealed record SarifLocation
{
    [JsonPropertyName("logicalLocations")]
    public IReadOnlyList<SarifLogicalLocation> LogicalLocations { get; init; } =
        new List<SarifLogicalLocation>();
}

public sealed record SarifLogicalLocation
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "element";
}
