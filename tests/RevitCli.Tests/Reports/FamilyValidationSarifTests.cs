using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RevitCli.Reports;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Reports;

/// <summary>
/// Unit tests for <see cref="SarifWriter.RenderFamilyValidation"/>.
/// We assert the SARIF 2.1.0 envelope fields that Code Scanning
/// ingestion depends on (version, schema, run.tool.driver), the
/// per-result projection (ruleId, level, message), and the
/// family-flavored locations + properties bag.
/// </summary>
public class FamilyValidationSarifTests
{
    private static List<FamilyValidationIssue> SampleIssues() => new()
    {
        new()
        {
            FamilyId = 5001, FamilyName = "Bad/Name", Category = "Walls",
            Rule = "name-no-path-chars", Severity = "error",
            Message = "Family Name 'Bad/Name' contains a forbidden character."
        },
        new()
        {
            FamilyId = 5002, FamilyName = "Foo", Category = "Unknown",
            Rule = "category-known", Severity = "warning",
            Message = "Family 'Foo' has placeholder Category 'Unknown'."
        },
    };

    [Fact]
    public void Render_ProducesSarif21Envelope()
    {
        var sarif = SarifWriter.RenderFamilyValidation(SampleIssues());
        using var doc = JsonDocument.Parse(sarif);
        var root = doc.RootElement;

        // SARIF Code Scanning treats both "version" and "$schema" as
        // required for ingestion — pin them so a future serializer
        // change doesn't silently break uploads.
        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        Assert.Contains("sarif-2.1.0", root.GetProperty("$schema").GetString());
    }

    [Fact]
    public void Render_ToolDriverIsRevitCli()
    {
        var sarif = SarifWriter.RenderFamilyValidation(SampleIssues());
        using var doc = JsonDocument.Parse(sarif);
        var driver = doc.RootElement.GetProperty("runs")[0]
            .GetProperty("tool").GetProperty("driver");
        Assert.Equal("RevitCli", driver.GetProperty("name").GetString());
        Assert.True(driver.TryGetProperty("version", out _));
    }

    [Fact]
    public void Render_ResultsCarryRuleIdLevelAndMessage()
    {
        var sarif = SarifWriter.RenderFamilyValidation(SampleIssues());
        using var doc = JsonDocument.Parse(sarif);
        var results = doc.RootElement.GetProperty("runs")[0].GetProperty("results");
        Assert.Equal(2, results.GetArrayLength());

        var first = results[0];
        Assert.Equal("name-no-path-chars", first.GetProperty("ruleId").GetString());
        Assert.Equal("error", first.GetProperty("level").GetString());
        Assert.Contains("Bad/Name", first.GetProperty("message").GetProperty("text").GetString());

        // SARIF "warning" maps from FamilyValidationIssue.Severity="warning".
        Assert.Equal("warning", results[1].GetProperty("level").GetString());
    }

    [Fact]
    public void Render_LocationsUseFamilyKindAndQualifiedName()
    {
        var sarif = SarifWriter.RenderFamilyValidation(SampleIssues());
        using var doc = JsonDocument.Parse(sarif);
        var loc = doc.RootElement.GetProperty("runs")[0].GetProperty("results")[0]
            .GetProperty("locations")[0]
            .GetProperty("logicalLocations")[0];
        Assert.Equal("family", loc.GetProperty("kind").GetString());
        // Name format: "family:<name>#<id>" — lets a SARIF reader parse
        // out the id without going through properties.
        Assert.Equal("family:Bad/Name#5001", loc.GetProperty("name").GetString());
    }

    [Fact]
    public void Render_PropertiesCarryFamilyIdNameAndCategory()
    {
        var sarif = SarifWriter.RenderFamilyValidation(SampleIssues());
        using var doc = JsonDocument.Parse(sarif);
        var props = doc.RootElement.GetProperty("runs")[0].GetProperty("results")[0]
            .GetProperty("properties");
        Assert.Equal(5001, props.GetProperty("revitFamilyId").GetInt64());
        Assert.Equal("Bad/Name", props.GetProperty("revitFamilyName").GetString());
        Assert.Equal("Walls", props.GetProperty("revitCategory").GetString());
    }

    [Fact]
    public void Render_DocumentPathOption_LandsInProperties()
    {
        var sarif = SarifWriter.RenderFamilyValidation(SampleIssues(),
            new SarifWriterOptions { DocumentPath = "/projects/Demo.rvt" });
        using var doc = JsonDocument.Parse(sarif);
        var props = doc.RootElement.GetProperty("runs")[0]
            .GetProperty("results")[0].GetProperty("properties");
        Assert.Equal("/projects/Demo.rvt", props.GetProperty("documentPath").GetString());
    }

    [Fact]
    public void Render_FamilyWithEmptyName_FallsBackToIdOnlyLocation()
    {
        // Defensive: shouldn't happen in practice (validator catches
        // empty names) but the renderer must not crash with a "" name.
        var sarif = SarifWriter.RenderFamilyValidation(new[]
        {
            new FamilyValidationIssue
            {
                FamilyId = 1, FamilyName = "", Category = "Walls",
                Rule = "name-non-empty", Severity = "error", Message = "x"
            }
        });
        using var doc = JsonDocument.Parse(sarif);
        var name = doc.RootElement.GetProperty("runs")[0].GetProperty("results")[0]
            .GetProperty("locations")[0]
            .GetProperty("logicalLocations")[0]
            .GetProperty("name").GetString();
        Assert.Equal("family:#1", name);
    }

    [Fact]
    public void Render_EmptyIssueList_ProducesValidEnvelope()
    {
        var sarif = SarifWriter.RenderFamilyValidation(System.Linq.Enumerable.Empty<FamilyValidationIssue>());
        using var doc = JsonDocument.Parse(sarif);
        Assert.Equal("2.1.0", doc.RootElement.GetProperty("version").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("runs")[0].GetProperty("results").GetArrayLength());
    }

    [Fact]
    public void Render_CompactOption_OmitsIndentation()
    {
        var compact = SarifWriter.RenderFamilyValidation(SampleIssues(),
            new SarifWriterOptions { Indented = false });
        // Compact output has no embedded newline (apart from any inside
        // the JSON content itself, which there shouldn't be).
        Assert.DoesNotContain("\n  ", compact);
        // But it's still valid JSON.
        using var doc = JsonDocument.Parse(compact);
        Assert.Equal("2.1.0", doc.RootElement.GetProperty("version").GetString());
    }
}
