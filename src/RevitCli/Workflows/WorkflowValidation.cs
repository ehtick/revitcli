using System.Text.Json.Serialization;

namespace RevitCli.Workflows;

public enum WorkflowValidationSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

public sealed record WorkflowValidationIssue(
    [property: JsonPropertyName("severity")] WorkflowValidationSeverity Severity,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("message")] string Message);

public sealed record WorkflowStepSimulation(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("run")] string Run,
    [property: JsonPropertyName("requiresApproval")] bool RequiresApproval);

public sealed class WorkflowSimulationReport
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("canRun")]
    public bool CanRun { get; set; }

    [JsonPropertyName("stepCount")]
    public int StepCount { get; set; }

    [JsonPropertyName("modeCounts")]
    public Dictionary<string, int> ModeCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("steps")]
    public List<WorkflowStepSimulation> Steps { get; set; } = new();

    [JsonPropertyName("issues")]
    public List<WorkflowValidationIssue> Issues { get; set; } = new();
}

public sealed class WorkflowRunReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "workflow-run-receipt.v1";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "workflow.run";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("startedAtUtc")]
    public string StartedAtUtc { get; set; } = "";

    [JsonPropertyName("completedAtUtc")]
    public string CompletedAtUtc { get; set; } = "";

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("timeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long TimeoutMs { get; set; }

    [JsonPropertyName("operator")]
    public string Operator { get; set; } = "";

    [JsonPropertyName("machine")]
    public string Machine { get; set; } = "";

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("canRun")]
    public bool CanRun { get; set; }

    [JsonPropertyName("exitCode")]
    public int ExitCode { get; set; }

    [JsonPropertyName("receiptPath")]
    public string? ReceiptPath { get; set; }

    [JsonPropertyName("issues")]
    public List<WorkflowValidationIssue> Issues { get; set; } = new();

    [JsonPropertyName("steps")]
    public List<WorkflowRunStepResult> Steps { get; set; } = new();
}

public sealed class WorkflowRunStepResult
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "";

    [JsonPropertyName("run")]
    public string Run { get; set; } = "";

    [JsonPropertyName("requiresApproval")]
    public bool RequiresApproval { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; set; }

    [JsonPropertyName("startedAtUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StartedAtUtc { get; set; }

    [JsonPropertyName("completedAtUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CompletedAtUtc { get; set; }

    [JsonPropertyName("durationMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DurationMs { get; set; }

    [JsonPropertyName("timedOut")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TimedOut { get; set; }
}
