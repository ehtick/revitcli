using System.Collections.Generic;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace RevitCli.Standards;

public sealed class StandardsManifest
{
    [YamlMember(Alias = "version")]
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [YamlMember(Alias = "name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "description")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "packVersion")]
    [JsonPropertyName("packVersion")]
    public string? PackVersion { get; set; }

    [YamlMember(Alias = "compatibility")]
    [JsonPropertyName("compatibility")]
    public StandardsCompatibility Compatibility { get; set; } = new();

    [YamlMember(Alias = "required")]
    [JsonPropertyName("required")]
    public StandardsRequirements Required { get; set; } = new();
}

public sealed class StandardsCompatibility
{
    [YamlMember(Alias = "revitCli")]
    [JsonPropertyName("revitCli")]
    public string? RevitCli { get; set; }

    [YamlMember(Alias = "revitYears")]
    [JsonPropertyName("revitYears")]
    public List<int> RevitYears { get; set; } = new();

    [YamlMember(Alias = "notes")]
    [JsonPropertyName("notes")]
    public List<string> Notes { get; set; } = new();
}

public sealed class StandardsRequirements
{
    [YamlMember(Alias = "profiles")]
    [JsonPropertyName("profiles")]
    public List<string> Profiles { get; set; } = new();

    [YamlMember(Alias = "workflows")]
    [JsonPropertyName("workflows")]
    public List<string> Workflows { get; set; } = new();

    [YamlMember(Alias = "outputPaths")]
    [JsonPropertyName("outputPaths")]
    public List<string> OutputPaths { get; set; } = new();

    [YamlMember(Alias = "scheduleTemplates")]
    [JsonPropertyName("scheduleTemplates")]
    public List<string> ScheduleTemplates { get; set; } = new();

    [YamlMember(Alias = "sheetMaps")]
    [JsonPropertyName("sheetMaps")]
    public List<string> SheetMaps { get; set; } = new();

    [YamlMember(Alias = "numberingRules")]
    [JsonPropertyName("numberingRules")]
    public List<string> NumberingRules { get; set; } = new();

    [YamlMember(Alias = "familyRules")]
    [JsonPropertyName("familyRules")]
    public List<string> FamilyRules { get; set; } = new();
}

public enum StandardsValidationSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

public sealed record StandardsValidationIssue(
    [property: JsonPropertyName("severity")] StandardsValidationSeverity Severity,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("message")] string Message);

public sealed class StandardsValidationReport
{
    [JsonPropertyName("manifestPath")]
    public string ManifestPath { get; set; } = "";

    [JsonPropertyName("projectDirectory")]
    public string ProjectDirectory { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("packVersion")]
    public string? PackVersion { get; set; }

    [JsonPropertyName("compatibility")]
    public StandardsCompatibility Compatibility { get; set; } = new();

    [JsonPropertyName("cliVersion")]
    public string CliVersion { get; set; } = "";

    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("issues")]
    public List<StandardsValidationIssue> Issues { get; set; } = new();
}
