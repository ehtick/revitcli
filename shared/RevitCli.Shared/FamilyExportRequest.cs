using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RevitCli.Shared;

/// <summary>
/// Request body for <c>POST /api/families/export</c>. The addin saves
/// each requested family as a standalone .rfa under <see cref="OutputDir"/>
/// using <c>Document.EditFamily(...)</c> + <c>Document.SaveAs(...)</c>.
/// </summary>
public class FamilyExportRequest
{
    [JsonPropertyName("ids")]
    public List<long> Ids { get; set; } = new();

    [JsonPropertyName("outputDir")]
    public string OutputDir { get; set; } = "";

    [JsonPropertyName("overwrite")]
    public bool Overwrite { get; set; }

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }
}
