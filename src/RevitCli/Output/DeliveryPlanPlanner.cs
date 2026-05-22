using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using RevitCli.Profile;
using RevitCli.Shared;

namespace RevitCli.Output;

public static class DeliveryPlanPlanner
{
    private static readonly HashSet<string> KnownFormats = new(StringComparer.OrdinalIgnoreCase)
        { "dwg", "pdf", "ifc" };

    public static DeliveryPlanReport Plan(string? profilePath, string? sincePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
            throw new ArgumentException("--profile is required.", nameof(profilePath));

        var resolvedProfilePath = Path.GetFullPath(profilePath!);
        var profileDirectory = Path.GetDirectoryName(resolvedProfilePath) ?? Directory.GetCurrentDirectory();
        var profile = ProfileLoader.Load(resolvedProfilePath);

        var report = new DeliveryPlanReport
        {
            ProfilePath = resolvedProfilePath,
            ProfileHash = File.Exists(resolvedProfilePath) ? ComputeFileHash(resolvedProfilePath) : null,
            ProjectDirectory = profileDirectory,
            GeneratedAt = DateTime.UtcNow.ToString("o")
        };

        DeliveryPlanBaseline? explicitBaseline = null;
        if (!string.IsNullOrWhiteSpace(sincePath))
        {
            var resolvedSincePath = ResolvePath(Directory.GetCurrentDirectory(), sincePath!);
            report.SincePath = resolvedSincePath;
            explicitBaseline = ReadBaseline(resolvedSincePath, required: true, report.Risks);
            report.Baseline = explicitBaseline;
        }

        if (profile.Publish.Count == 0)
        {
            report.Risks.Add(new DeliveryPlanRisk(
                "error",
                "publish-pipelines-missing",
                null,
                null,
                "profile has no publish pipelines."));
        }

        foreach (var (name, pipeline) in profile.Publish.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            report.Pipelines.Add(BuildPipeline(
                name,
                pipeline,
                profile,
                report,
                explicitBaseline));
        }

        report.CommandPaths.AddRange(report.Pipelines
            .SelectMany(pipeline => pipeline.CommandPaths)
            .Concat(new[]
            {
                "revitcli deliverables verify --output markdown",
                "revitcli deliverables bundle --dry-run --output markdown"
            })
            .Distinct(StringComparer.OrdinalIgnoreCase));

        return report;
    }

    private static DeliveryPlanPipeline BuildPipeline(
        string name,
        PublishPipeline pipeline,
        ProjectProfile profile,
        DeliveryPlanReport report,
        DeliveryPlanBaseline? explicitBaseline)
    {
        var pipelineReport = new DeliveryPlanPipeline
        {
            Name = name,
            Precheck = string.IsNullOrWhiteSpace(pipeline.Precheck) ? null : pipeline.Precheck,
            Incremental = pipeline.Incremental,
            SinceMode = string.IsNullOrWhiteSpace(pipeline.SinceMode) ? "content" : pipeline.SinceMode
        };

        DeliveryPlanBaseline? effectiveBaseline = explicitBaseline;
        if (explicitBaseline == null && pipeline.Incremental)
        {
            var baselinePath = ResolvePath(
                report.ProjectDirectory,
                string.IsNullOrWhiteSpace(pipeline.BaselinePath)
                    ? Path.Combine(".revitcli", "last-publish.json")
                    : pipeline.BaselinePath!);
            pipelineReport.BaselinePath = baselinePath;
            effectiveBaseline = ReadBaseline(baselinePath, required: true, report.Risks);
        }
        else if (explicitBaseline != null)
        {
            pipelineReport.BaselinePath = explicitBaseline.Path;
        }

        if (pipeline.Presets.Count == 0)
        {
            AddRisk(report, pipelineReport, "warning", "pipeline-empty", null,
                $"publish pipeline '{name}' has no presets.");
        }

        if (!string.IsNullOrWhiteSpace(pipeline.Precheck))
        {
            if (!profile.Checks.TryGetValue(pipeline.Precheck!, out var check))
            {
                AddRisk(report, pipelineReport, "error", "precheck-missing", null,
                    $"publish pipeline '{name}' references missing precheck '{pipeline.Precheck}'.");
            }
            else
            {
                pipelineReport.PrecheckRules.AddRange(GetCheckRuleNames(check));
                if (pipelineReport.PrecheckRules.Count == 0)
                {
                    AddRisk(report, pipelineReport, "warning", "precheck-empty", null,
                        $"precheck '{pipeline.Precheck}' has no rules.");
                }
            }
        }

        foreach (var presetName in pipeline.Presets)
        {
            if (!profile.Exports.TryGetValue(presetName, out var preset))
            {
                AddRisk(report, pipelineReport, "error", "preset-missing", presetName,
                    $"publish pipeline '{name}' references missing export preset '{presetName}'.");
                continue;
            }

            var export = BuildExport(presetName, preset, profile, report.ProjectDirectory, effectiveBaseline?.Snapshot);
            pipelineReport.Exports.Add(export);
            AddExportRisks(report, pipelineReport, export);
        }

        var baseCommand = BuildPublishCommand(name, report.ProfilePath, explicitBaseline?.Path);
        pipelineReport.CommandPaths.Add(baseCommand + " --dry-run --output json");
        pipelineReport.CommandPaths.Add(baseCommand);
        pipelineReport.CommandPaths.Add("revitcli deliverables verify --output markdown");
        pipelineReport.CommandPaths.Add("revitcli deliverables bundle --dry-run --output markdown");

        return pipelineReport;
    }

    private static DeliveryPlanExport BuildExport(
        string presetName,
        ExportPreset preset,
        ProjectProfile profile,
        string profileDirectory,
        ModelSnapshot? baseline)
    {
        var sheets = NormalizeList(preset.Sheets);
        var views = NormalizeList(preset.Views);
        var outputDir = preset.OutputDir ?? profile.Defaults.OutputDir ?? "./exports";
        var resolvedOutputDir = ResolvePath(profileDirectory, outputDir);
        var allSheets = sheets.Any(sheet => string.Equals(sheet, "all", StringComparison.OrdinalIgnoreCase));

        return new DeliveryPlanExport
        {
            Preset = presetName,
            Format = preset.Format ?? "",
            Sheets = sheets,
            Views = views,
            OutputDir = resolvedOutputDir,
            Selector = FormatSelector(sheets, views),
            ExportsAllSheets = allSheets,
            EstimatedSheetCount = EstimateSheetCount(sheets, baseline)
        };
    }

    private static void AddExportRisks(
        DeliveryPlanReport report,
        DeliveryPlanPipeline pipeline,
        DeliveryPlanExport export)
    {
        if (string.IsNullOrWhiteSpace(export.Format))
        {
            AddRisk(report, pipeline, "error", "preset-format-missing", export.Preset,
                $"export preset '{export.Preset}' has no format.");
        }
        else if (!KnownFormats.Contains(export.Format))
        {
            AddRisk(report, pipeline, "warning", "preset-format-unknown", export.Preset,
                $"export preset '{export.Preset}' uses unknown format '{export.Format}'.");
        }

        if (export.ExportsAllSheets)
        {
            AddRisk(report, pipeline, "info", "preset-all-sheets", export.Preset,
                $"export preset '{export.Preset}' exports all sheets.");
        }

        if (export.Sheets.Count == 0 && export.Views.Count == 0)
        {
            AddRisk(report, pipeline, "warning", "preset-empty-selectors", export.Preset,
                $"export preset '{export.Preset}' has neither sheets nor views configured.");
        }
    }

    private static void AddRisk(
        DeliveryPlanReport report,
        DeliveryPlanPipeline pipeline,
        string severity,
        string code,
        string? preset,
        string message)
    {
        var risk = new DeliveryPlanRisk(severity, code, pipeline.Name, preset, message);
        pipeline.Risks.Add(risk);
        report.Risks.Add(risk);
    }

    private static DeliveryPlanBaseline ReadBaseline(
        string path,
        bool required,
        IList<DeliveryPlanRisk> risks)
    {
        var baseline = new DeliveryPlanBaseline
        {
            Path = path,
            Exists = File.Exists(path)
        };

        if (!baseline.Exists)
        {
            if (required)
            {
                risks.Add(new DeliveryPlanRisk(
                    "error",
                    "baseline-missing",
                    null,
                    null,
                    $"baseline not found: {path}"));
            }

            return baseline;
        }

        var snapshot = BaselineManager.Load(path);
        if (snapshot == null)
        {
            risks.Add(new DeliveryPlanRisk(
                "error",
                "baseline-unreadable",
                null,
                null,
                $"baseline is not readable snapshot JSON: {path}"));
            return baseline;
        }

        baseline.Readable = true;
        baseline.TakenAt = snapshot.TakenAt;
        baseline.Document = snapshot.Revit.Document;
        baseline.DocumentPath = snapshot.Revit.DocumentPath;
        baseline.SheetCount = snapshot.Sheets.Count;
        baseline.ScheduleCount = snapshot.Schedules.Count;
        baseline.Snapshot = snapshot;
        return baseline;
    }

    private static IReadOnlyList<string> NormalizeList(List<string>? raw)
    {
        return raw == null
            ? Array.Empty<string>()
            : raw
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToArray();
    }

    private static IReadOnlyList<string> GetCheckRuleNames(CheckDefinition check)
    {
        var rules = new List<string>();
        rules.AddRange(check.AuditRules
            .Select(rule => rule.Rule)
            .Where(rule => !string.IsNullOrWhiteSpace(rule)));
        rules.AddRange(check.RequiredParameters
            .Select(rule => $"required-parameter:{rule.Category}.{rule.Parameter}"));
        rules.AddRange(check.Naming
            .Select(rule => $"naming:{rule.Target}"));
        return rules;
    }

    private static int? EstimateSheetCount(IReadOnlyList<string> sheets, ModelSnapshot? baseline)
    {
        if (baseline == null || sheets.Count == 0)
            return null;

        if (sheets.Any(sheet => string.Equals(sheet, "all", StringComparison.OrdinalIgnoreCase)))
            return baseline.Sheets.Count;

        var selected = sheets.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return baseline.Sheets.Count(sheet => selected.Contains(sheet.Number));
    }

    private static string FormatSelector(IReadOnlyList<string> sheets, IReadOnlyList<string> views)
    {
        if (sheets.Count == 0 && views.Count == 0)
            return "(preset default)";

        var parts = new List<string>();
        if (sheets.Count > 0)
            parts.Add("sheets: " + string.Join(",", sheets));
        if (views.Count > 0)
            parts.Add("views: " + string.Join(",", views));
        return string.Join("; ", parts);
    }

    private static string ResolvePath(string baseDirectory, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static string BuildPublishCommand(string pipelineName, string profilePath, string? sincePath)
    {
        var parts = new List<string>
        {
            "revitcli",
            "publish",
            QuoteArgument(pipelineName),
            "--profile",
            QuoteArgument(profilePath)
        };

        if (!string.IsNullOrWhiteSpace(sincePath))
        {
            parts.Add("--since");
            parts.Add(QuoteArgument(sincePath!));
        }

        return string.Join(" ", parts);
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";
        return value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static string ComputeFileHash(string path)
    {
        var bytes = SHA256.HashData(File.ReadAllBytes(path));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }
}

public sealed class DeliveryPlanReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "delivery-plan.v1";

    [JsonPropertyName("success")]
    public bool Success => Valid;

    [JsonPropertyName("valid")]
    public bool Valid => Risks.All(risk => !string.Equals(risk.Severity, "error", StringComparison.OrdinalIgnoreCase));

    [JsonPropertyName("profilePath")]
    public string ProfilePath { get; set; } = "";

    [JsonPropertyName("profileHash")]
    public string? ProfileHash { get; set; }

    [JsonPropertyName("projectDirectory")]
    public string ProjectDirectory { get; set; } = "";

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = "";

    [JsonPropertyName("sincePath")]
    public string? SincePath { get; set; }

    [JsonPropertyName("baseline")]
    public DeliveryPlanBaseline? Baseline { get; set; }

    [JsonPropertyName("pipelineCount")]
    public int PipelineCount => Pipelines.Count;

    [JsonPropertyName("exportCount")]
    public int ExportCount => Pipelines.Sum(pipeline => pipeline.Exports.Count);

    [JsonPropertyName("riskCount")]
    public int RiskCount => Risks.Count;

    [JsonPropertyName("pipelines")]
    public List<DeliveryPlanPipeline> Pipelines { get; } = new();

    [JsonPropertyName("commandPaths")]
    public List<string> CommandPaths { get; } = new();

    [JsonPropertyName("risks")]
    public List<DeliveryPlanRisk> Risks { get; } = new();

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    public static DeliveryPlanReport Failure(string error) =>
        new()
        {
            Error = error,
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            Risks =
            {
                new DeliveryPlanRisk("error", "delivery-plan-failed", null, null, error)
            }
        };
}

public sealed class DeliveryPlanPipeline
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("precheck")]
    public string? Precheck { get; set; }

    [JsonPropertyName("precheckRules")]
    public List<string> PrecheckRules { get; } = new();

    [JsonPropertyName("incremental")]
    public bool Incremental { get; set; }

    [JsonPropertyName("baselinePath")]
    public string? BaselinePath { get; set; }

    [JsonPropertyName("sinceMode")]
    public string SinceMode { get; set; } = "content";

    [JsonPropertyName("exportCount")]
    public int ExportCount => Exports.Count;

    [JsonPropertyName("riskCount")]
    public int RiskCount => Risks.Count;

    [JsonPropertyName("exports")]
    public List<DeliveryPlanExport> Exports { get; } = new();

    [JsonPropertyName("commandPaths")]
    public List<string> CommandPaths { get; } = new();

    [JsonPropertyName("risks")]
    public List<DeliveryPlanRisk> Risks { get; } = new();
}

public sealed class DeliveryPlanExport
{
    [JsonPropertyName("preset")]
    public string Preset { get; set; } = "";

    [JsonPropertyName("format")]
    public string Format { get; set; } = "";

    [JsonPropertyName("sheets")]
    public IReadOnlyList<string> Sheets { get; set; } = Array.Empty<string>();

    [JsonPropertyName("views")]
    public IReadOnlyList<string> Views { get; set; } = Array.Empty<string>();

    [JsonPropertyName("outputDir")]
    public string OutputDir { get; set; } = "";

    [JsonPropertyName("selector")]
    public string Selector { get; set; } = "";

    [JsonPropertyName("exportsAllSheets")]
    public bool ExportsAllSheets { get; set; }

    [JsonPropertyName("estimatedSheetCount")]
    public int? EstimatedSheetCount { get; set; }
}

public sealed class DeliveryPlanBaseline
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("readable")]
    public bool Readable { get; set; }

    [JsonPropertyName("takenAt")]
    public string? TakenAt { get; set; }

    [JsonPropertyName("document")]
    public string? Document { get; set; }

    [JsonPropertyName("documentPath")]
    public string? DocumentPath { get; set; }

    [JsonPropertyName("sheetCount")]
    public int SheetCount { get; set; }

    [JsonPropertyName("scheduleCount")]
    public int ScheduleCount { get; set; }

    [JsonIgnore]
    public ModelSnapshot? Snapshot { get; set; }
}

public sealed record DeliveryPlanRisk(
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("pipeline")] string? Pipeline,
    [property: JsonPropertyName("preset")] string? Preset,
    [property: JsonPropertyName("message")] string Message);
