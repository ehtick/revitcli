using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RevitCli.Shared;

/// <summary>
/// Request body for <c>POST /api/families/purge</c>. The CLI selects the
/// family ids based on the operator's filters (--unused, --category,
/// --keep) so the addin only has to enumerate Family elements once and
/// drop the listed ids inside a single Revit transaction.
/// </summary>
public class FamilyPurgeRequest
{
    [JsonPropertyName("ids")]
    public List<long> Ids { get; set; } = new();

    /// <summary>
    /// Don't actually delete; return the same shape as a real purge would
    /// have, so the CLI can render the same preview.
    /// </summary>
    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }
}
