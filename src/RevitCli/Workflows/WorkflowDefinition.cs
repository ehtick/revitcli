using System.Collections.Generic;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace RevitCli.Workflows;

public sealed class WorkflowDefinition
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

    [YamlMember(Alias = "steps")]
    [JsonPropertyName("steps")]
    public List<WorkflowStep> Steps { get; set; } = new();
}

public sealed class WorkflowStep
{
    [YamlMember(Alias = "name")]
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "run")]
    [JsonPropertyName("run")]
    public string Run { get; set; } = "";

    [YamlMember(Alias = "mode")]
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "";

    [YamlMember(Alias = "requiresApproval")]
    [JsonPropertyName("requiresApproval")]
    public bool RequiresApproval { get; set; }
}

public sealed record LoadedWorkflow(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("workflow")] WorkflowDefinition Workflow);
