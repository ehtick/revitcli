using System.Collections.Generic;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace RevitCli.Sheets;

public sealed class SheetIndex
{
    [YamlMember(Alias = "name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = "project-sheet-frame";

    [YamlMember(Alias = "schemaVersion")]
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [YamlMember(Alias = "numbering")]
    [JsonPropertyName("numbering")]
    public SheetNumberingConfig Numbering { get; set; } = new();

    [YamlMember(Alias = "required")]
    [JsonPropertyName("required")]
    public List<RequiredSheetDeclaration> Required { get; set; } = new();

    [YamlMember(Alias = "linkage")]
    [JsonPropertyName("linkage")]
    public SheetLinkageConfig Linkage { get; set; } = new();

    [YamlMember(Alias = "severities")]
    [JsonPropertyName("severities")]
    public Dictionary<string, string> Severities { get; set; } = new();
}

public sealed class SheetNumberingConfig
{
    [YamlMember(Alias = "scheme")]
    [JsonPropertyName("scheme")]
    public string? Scheme { get; set; }

    [YamlMember(Alias = "ranges")]
    [JsonPropertyName("ranges")]
    public List<SheetNumberRange> Ranges { get; set; } = new();

    [YamlMember(Alias = "allowedPrefixes")]
    [JsonPropertyName("allowedPrefixes")]
    public List<string> AllowedPrefixes { get; set; } = new();
}

public sealed class SheetNumberRange
{
    [YamlMember(Alias = "floors")]
    [JsonPropertyName("floors")]
    public List<int> Floors { get; set; } = new();

    [YamlMember(Alias = "seqMin")]
    [JsonPropertyName("seqMin")]
    public int SeqMin { get; set; } = 1;

    [YamlMember(Alias = "seqMax")]
    [JsonPropertyName("seqMax")]
    public int SeqMax { get; set; } = 99;
}

public sealed class RequiredSheetDeclaration
{
    [YamlMember(Alias = "pattern")]
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = "";

    [YamlMember(Alias = "description")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "needsViews")]
    [JsonPropertyName("needsViews")]
    public List<RequiredViewDeclaration> NeedsViews { get; set; } = new();
}

public sealed class RequiredViewDeclaration
{
    [YamlMember(Alias = "viewType")]
    [JsonPropertyName("viewType")]
    public string? ViewType { get; set; }

    [YamlMember(Alias = "minCount")]
    [JsonPropertyName("minCount")]
    public int MinCount { get; set; } = 1;
}

public sealed class SheetLinkageConfig
{
    [YamlMember(Alias = "ignoreOrphanViews")]
    [JsonPropertyName("ignoreOrphanViews")]
    public List<string> IgnoreOrphanViews { get; set; } = new();

    [YamlMember(Alias = "overloadThreshold")]
    [JsonPropertyName("overloadThreshold")]
    public int? OverloadThreshold { get; set; }
}
