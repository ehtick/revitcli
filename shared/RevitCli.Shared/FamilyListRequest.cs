using System.Text.Json.Serialization;

namespace RevitCli.Shared;

/// <summary>
/// Query options for <see cref="IRevitOperations.ListFamiliesAsync"/>.
/// Wire format mirrors the GET /api/families query string:
///   <c>unused=true|false</c> (default false), <c>category=&lt;name&gt;</c> (optional).
/// </summary>
public class FamilyListRequest
{
    /// <summary>
    /// When true, returns only families whose FamilySymbols have zero placed
    /// FamilyInstances in the active document. When false (default), returns
    /// every family regardless of placement.
    /// </summary>
    [JsonPropertyName("includeUnplaced")]
    public bool IncludeUnplaced { get; set; }

    /// <summary>
    /// Optional Revit category filter (raw name, no alias resolution applied
    /// here — match the spelling that appears on <c>Family.FamilyCategory.Name</c>).
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }
}
