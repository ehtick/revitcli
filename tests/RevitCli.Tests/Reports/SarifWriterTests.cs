using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RevitCli.Reports;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Reports;

public class SarifWriterTests
{
    [Fact]
    public void Render_EmptyIssues_ProducesValidSarifShell()
    {
        var json = SarifWriter.Render(new List<AuditIssue>());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        var runs = root.GetProperty("runs");
        Assert.Equal(1, runs.GetArrayLength());

        var run = runs[0];
        var driver = run.GetProperty("tool").GetProperty("driver");
        Assert.Equal("RevitCli", driver.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(driver.GetProperty("version").GetString()));

        Assert.Equal(0, run.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public void Render_SingleErrorIssue_ProducesExpectedShape()
    {
        var issue = new AuditIssue
        {
            Rule = "naming-mark",
            Severity = "error",
            Message = "Door is missing Mark",
            ElementId = 12345,
            Category = "Doors"
        };

        var json = SarifWriter.Render(new[] { issue });

        using var doc = JsonDocument.Parse(json);
        var result = doc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results")[0];

        Assert.Equal("naming-mark", result.GetProperty("ruleId").GetString());
        Assert.Equal("error", result.GetProperty("level").GetString());
        Assert.Equal("Door is missing Mark", result.GetProperty("message").GetProperty("text").GetString());

        var location = result.GetProperty("locations")[0]
            .GetProperty("logicalLocations")[0];
        Assert.Equal("Doors:12345", location.GetProperty("name").GetString());
        Assert.Equal("element", location.GetProperty("kind").GetString());
    }

    [Fact]
    public void Render_NoPhysicalLocationField_PerRoadmapDecision()
    {
        var issue = new AuditIssue
        {
            Rule = "naming",
            Severity = "warning",
            Message = "msg",
            ElementId = 1,
            Category = "Walls"
        };

        var json = SarifWriter.Render(new[] { issue });
        Assert.DoesNotContain("physicalLocation", json);
    }

    [Fact]
    public void Render_PopulatesPropertyBagWhenMetadataPresent()
    {
        var issue = new AuditIssue
        {
            Rule = "param-mark",
            Severity = "warning",
            Message = "Mark blank",
            ElementId = 999,
            Category = "Doors",
            Parameter = "Mark",
            CurrentValue = ""
        };

        var json = SarifWriter.Render(
            new[] { issue },
            new SarifWriterOptions { DocumentPath = @"C:\models\demo.rvt" });

        using var doc = JsonDocument.Parse(json);
        var props = doc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results")[0]
            .GetProperty("properties");

        Assert.Equal(999, props.GetProperty("revitElementId").GetInt64());
        Assert.Equal("Doors", props.GetProperty("revitCategory").GetString());
        Assert.Equal("Mark", props.GetProperty("revitParameter").GetString());
        Assert.Equal(@"C:\models\demo.rvt", props.GetProperty("documentPath").GetString());
        // CurrentValue is empty/whitespace -> omitted.
        Assert.False(props.TryGetProperty("revitCurrentValue", out _));
    }

    [Theory]
    [InlineData("error", "error")]
    [InlineData("ERROR", "error")]
    [InlineData("warning", "warning")]
    [InlineData("Warning", "warning")]
    [InlineData("info", "note")]
    [InlineData("hint", "note")]
    [InlineData("", "note")]
    [InlineData(null, "note")]
    public void MapSeverity_NormalisesToSarifLevel(string? severity, string expected)
    {
        Assert.Equal(expected, SarifWriter.MapSeverity(severity));
    }

    [Fact]
    public void Render_LargeIssueList_EmitsAllResults()
    {
        var issues = Enumerable.Range(0, 75)
            .Select(i => new AuditIssue
            {
                Rule = $"rule-{i % 3}",
                Severity = i % 2 == 0 ? "warning" : "error",
                Message = $"issue {i}",
                ElementId = i,
                Category = "Doors"
            })
            .ToList();

        var json = SarifWriter.Render(issues);
        using var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("runs")[0].GetProperty("results");

        Assert.Equal(75, results.GetArrayLength());
    }

    [Fact]
    public void Render_IssueWithoutElementOrCategory_OmitsLocations()
    {
        var issue = new AuditIssue
        {
            Rule = "global",
            Severity = "warning",
            Message = "Project-wide warning"
        };

        var json = SarifWriter.Render(new[] { issue });
        using var doc = JsonDocument.Parse(json);
        var result = doc.RootElement.GetProperty("runs")[0].GetProperty("results")[0];

        Assert.False(result.TryGetProperty("locations", out _));
        Assert.False(result.TryGetProperty("properties", out _));
    }

    [Fact]
    public void Render_RespectsCustomToolNameAndVersion()
    {
        var json = SarifWriter.Render(
            new List<AuditIssue>(),
            new SarifWriterOptions { ToolName = "Custom", ToolVersion = "9.9.9" });

        using var doc = JsonDocument.Parse(json);
        var driver = doc.RootElement.GetProperty("runs")[0]
            .GetProperty("tool").GetProperty("driver");

        Assert.Equal("Custom", driver.GetProperty("name").GetString());
        Assert.Equal("9.9.9", driver.GetProperty("version").GetString());
    }
}
