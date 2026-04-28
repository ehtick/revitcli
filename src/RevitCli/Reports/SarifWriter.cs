using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using RevitCli.Diagnostics;
using RevitCli.Shared;

namespace RevitCli.Reports;

/// <summary>
/// Options accepted by <see cref="SarifWriter.Render"/>.
/// </summary>
public sealed class SarifWriterOptions
{
    /// <summary>
    /// Tool driver name. Defaults to "RevitCli".
    /// </summary>
    public string ToolName { get; init; } = "RevitCli";

    /// <summary>
    /// Tool driver version. When null the writer falls back to
    /// <see cref="AssemblyVersionReader.CurrentCliVersion"/>.
    /// </summary>
    public string? ToolVersion { get; init; }

    /// <summary>
    /// Optional informational URI for the tool driver.
    /// </summary>
    public string? InformationUri { get; init; } =
        "https://github.com/xiaodream551-a11y/revitcli";

    /// <summary>
    /// Optional document path captured into per-result properties.
    /// </summary>
    public string? DocumentPath { get; init; }

    /// <summary>
    /// When true (default) the resulting JSON is pretty-printed.
    /// </summary>
    public bool Indented { get; init; } = true;
}

/// <summary>
/// Renders a sequence of <see cref="AuditIssue"/> values into SARIF 2.1.0 JSON.
/// </summary>
/// <remarks>
/// Per roadmap §4 design decision 1 (candidate A) we deliberately omit
/// <c>physicalLocation</c> — Revit elements do not map onto file paths. Instead
/// we surface element identity through <see cref="SarifLogicalLocation"/> and
/// the per-result <c>properties</c> bag.
/// </remarks>
public static class SarifWriter
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Render(IEnumerable<AuditIssue> issues, SarifWriterOptions? options = null)
    {
        options ??= new SarifWriterOptions();

        var version = options.ToolVersion;
        if (string.IsNullOrWhiteSpace(version))
            version = AssemblyVersionReader.CurrentCliVersion();

        var results = new List<SarifResult>();
        foreach (var issue in issues)
            results.Add(BuildResult(issue, options));

        var log = new SarifLog
        {
            Runs = new List<SarifRun>
            {
                new()
                {
                    Tool = new SarifTool
                    {
                        Driver = new SarifToolComponent
                        {
                            Name = options.ToolName,
                            Version = version!,
                            InformationUri = options.InformationUri
                        }
                    },
                    Results = results
                }
            }
        };

        var serializerOptions = options.Indented ? IndentedOptions : CompactOptions;
        return JsonSerializer.Serialize(log, serializerOptions);
    }

    internal static string MapSeverity(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
            return "note";

        return severity.Trim().ToLowerInvariant() switch
        {
            "error" => "error",
            "warning" => "warning",
            _ => "note"
        };
    }

    private static SarifResult BuildResult(AuditIssue issue, SarifWriterOptions options)
    {
        var locations = BuildLocations(issue);
        var properties = BuildProperties(issue, options);

        return new SarifResult
        {
            RuleId = issue.Rule ?? "",
            Level = MapSeverity(issue.Severity),
            Message = new SarifMessage { Text = issue.Message ?? "" },
            Locations = locations,
            Properties = properties
        };
    }

    private static IReadOnlyList<SarifLocation>? BuildLocations(AuditIssue issue)
    {
        var hasElement = issue.ElementId.HasValue || !string.IsNullOrWhiteSpace(issue.Category);
        if (!hasElement)
            return null;

        var category = string.IsNullOrWhiteSpace(issue.Category) ? "element" : issue.Category!;
        var idPart = issue.ElementId.HasValue ? issue.ElementId.Value.ToString() : "?";
        var name = $"{category}:{idPart}";

        return new List<SarifLocation>
        {
            new()
            {
                LogicalLocations = new List<SarifLogicalLocation>
                {
                    new() { Name = name, Kind = "element" }
                }
            }
        };
    }

    private static IReadOnlyDictionary<string, object>? BuildProperties(
        AuditIssue issue,
        SarifWriterOptions options)
    {
        var bag = new Dictionary<string, object>();

        if (issue.ElementId.HasValue)
            bag["revitElementId"] = issue.ElementId.Value;
        if (!string.IsNullOrWhiteSpace(issue.Category))
            bag["revitCategory"] = issue.Category!;
        if (!string.IsNullOrWhiteSpace(issue.Parameter))
            bag["revitParameter"] = issue.Parameter!;
        if (!string.IsNullOrWhiteSpace(issue.CurrentValue))
            bag["revitCurrentValue"] = issue.CurrentValue!;
        if (!string.IsNullOrWhiteSpace(options.DocumentPath))
            bag["documentPath"] = options.DocumentPath!;

        return bag.Count == 0 ? null : bag;
    }

    /// <summary>
    /// Render a sequence of <see cref="FamilyValidationIssue"/> values
    /// into SARIF 2.1.0 JSON. Same envelope as <see cref="Render"/>
    /// so a CI consumer (GitHub Code Scanning) can ingest family
    /// validation runs and audit runs uniformly. The locations and
    /// per-result properties carry family-flavored fields
    /// (<c>revitFamilyId</c>, <c>revitFamilyName</c>) instead of the
    /// element-flavored ones audit uses, but the shape is otherwise
    /// identical.
    /// </summary>
    public static string RenderFamilyValidation(
        IEnumerable<FamilyValidationIssue> issues, SarifWriterOptions? options = null)
    {
        options ??= new SarifWriterOptions();

        var version = options.ToolVersion;
        if (string.IsNullOrWhiteSpace(version))
            version = AssemblyVersionReader.CurrentCliVersion();

        var results = new List<SarifResult>();
        foreach (var issue in issues)
            results.Add(BuildFamilyResult(issue, options));

        var log = new SarifLog
        {
            Runs = new List<SarifRun>
            {
                new()
                {
                    Tool = new SarifTool
                    {
                        Driver = new SarifToolComponent
                        {
                            Name = options.ToolName,
                            Version = version,
                            InformationUri = options.InformationUri,
                        }
                    },
                    Results = results,
                }
            }
        };

        return JsonSerializer.Serialize(log, options.Indented ? IndentedOptions : CompactOptions);
    }

    private static SarifResult BuildFamilyResult(FamilyValidationIssue issue, SarifWriterOptions options)
    {
        return new SarifResult
        {
            RuleId = issue.Rule ?? "",
            Level = MapSeverity(issue.Severity),
            Message = new SarifMessage { Text = issue.Message ?? "" },
            Locations = BuildFamilyLocations(issue),
            Properties = BuildFamilyProperties(issue, options),
        };
    }

    private static IReadOnlyList<SarifLocation>? BuildFamilyLocations(FamilyValidationIssue issue)
    {
        // A family always has both an id and a name in our model — no
        // "no-location" case. We still keep the shape parallel to
        // BuildLocations(AuditIssue) so a SARIF reader can apply the
        // same logical-location handling to both runs.
        var name = string.IsNullOrEmpty(issue.FamilyName)
            ? $"family:#{issue.FamilyId}"
            : $"family:{issue.FamilyName}#{issue.FamilyId}";

        return new List<SarifLocation>
        {
            new()
            {
                LogicalLocations = new List<SarifLogicalLocation>
                {
                    new() { Name = name, Kind = "family" }
                }
            }
        };
    }

    private static IReadOnlyDictionary<string, object>? BuildFamilyProperties(
        FamilyValidationIssue issue, SarifWriterOptions options)
    {
        var bag = new Dictionary<string, object>
        {
            ["revitFamilyId"] = issue.FamilyId,
        };
        if (!string.IsNullOrWhiteSpace(issue.FamilyName))
            bag["revitFamilyName"] = issue.FamilyName;
        if (!string.IsNullOrWhiteSpace(issue.Category))
            bag["revitCategory"] = issue.Category;
        if (!string.IsNullOrWhiteSpace(options.DocumentPath))
            bag["documentPath"] = options.DocumentPath!;
        return bag;
    }
}
