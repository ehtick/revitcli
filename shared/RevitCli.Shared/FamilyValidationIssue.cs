using System.Text.Json.Serialization;

namespace RevitCli.Shared;

/// <summary>
/// One issue surfaced by <c>revitcli family validate</c>. Shape mirrors
/// <see cref="AuditIssue"/> so consumers (CI dashboards, the SARIF
/// renderer) can lift the same fields without conditioning on the
/// source.
/// </summary>
public class FamilyValidationIssue
{
    [JsonPropertyName("familyId")]
    public long FamilyId { get; set; }

    [JsonPropertyName("familyName")]
    public string FamilyName { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("rule")]
    public string Rule { get; set; } = "";

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "warning";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
