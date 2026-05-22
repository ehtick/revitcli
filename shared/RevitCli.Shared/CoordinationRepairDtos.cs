using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RevitCli.Shared;

public class LinkRepairRequest
{
    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }

    [JsonPropertyName("actions")]
    public List<LinkRepairOperation> Actions { get; set; } = new();
}

public class LinkRepairOperation
{
    [JsonPropertyName("linkId")]
    public long LinkId { get; set; }

    [JsonPropertyName("linkTypeId")]
    public long? LinkTypeId { get; set; }

    [JsonPropertyName("linkName")]
    public string LinkName { get; set; } = "";

    [JsonPropertyName("typeName")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("oldPath")]
    public string OldPath { get; set; } = "";

    [JsonPropertyName("newPath")]
    public string NewPath { get; set; } = "";

    [JsonPropertyName("oldLoaded")]
    public bool OldLoaded { get; set; }

    [JsonPropertyName("newLoaded")]
    public bool NewLoaded { get; set; }

    [JsonPropertyName("oldPathExists")]
    public bool OldPathExists { get; set; }

    [JsonPropertyName("newPathExists")]
    public bool NewPathExists { get; set; }

    [JsonPropertyName("oldPathLastWriteTimeUtc")]
    public string? OldPathLastWriteTimeUtc { get; set; }

    [JsonPropertyName("newPathLastWriteTimeUtc")]
    public string? NewPathLastWriteTimeUtc { get; set; }

    [JsonPropertyName("oldPathSizeBytes")]
    public long? OldPathSizeBytes { get; set; }

    [JsonPropertyName("newPathSizeBytes")]
    public long? NewPathSizeBytes { get; set; }
}

public class LinkRepairResult
{
    [JsonPropertyName("affected")]
    public int Affected { get; set; }

    [JsonPropertyName("preview")]
    public List<LinkRepairOperation> Preview { get; set; } = new();

    [JsonPropertyName("failures")]
    public List<CoordinationRepairFailure> Failures { get; set; } = new();
}

public class ModelMapFixRequest
{
    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }

    [JsonPropertyName("actions")]
    public List<ModelMapFixOperation> Actions { get; set; } = new();
}

public class ModelMapFixOperation
{
    [JsonPropertyName("elementId")]
    public long ElementId { get; set; }

    [JsonPropertyName("elementName")]
    public string ElementName { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    [JsonPropertyName("oldValue")]
    public string? OldValue { get; set; }

    [JsonPropertyName("newValue")]
    public string NewValue { get; set; } = "";
}

public class ModelMapFixResult
{
    [JsonPropertyName("affected")]
    public int Affected { get; set; }

    [JsonPropertyName("preview")]
    public List<ModelMapFixOperation> Preview { get; set; } = new();

    [JsonPropertyName("failures")]
    public List<CoordinationRepairFailure> Failures { get; set; } = new();
}

public class CoordinationRepairFailure
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
