using System.Collections.Generic;
using System.Text.Json;
using RevitCli.Reports;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Reports;

public class AuditReportFormatsTests
{
    [Theory]
    [InlineData("sarif")]
    [InlineData("SARIF")]
    [InlineData("Sarif")]
    public void TryRender_Sarif_ReturnsTrueAndValidJson(string format)
    {
        var issues = new[]
        {
            new AuditIssue { Rule = "r", Severity = "warning", Message = "m" }
        };

        var ok = AuditReportFormats.TryRender(format, issues, out var content);

        Assert.True(ok);
        // Validates JSON parses.
        using var _ = JsonDocument.Parse(content);
        Assert.Contains("\"version\": \"2.1.0\"", content);
    }

    [Theory]
    [InlineData("pr-comment")]
    [InlineData("PR-COMMENT")]
    [InlineData("Pr-Comment")]
    public void TryRender_PrComment_ReturnsTrueAndMarkdown(string format)
    {
        var issues = new[]
        {
            new AuditIssue
            {
                Rule = "r", Severity = "error", Message = "m",
                ElementId = 1, Category = "Doors"
            }
        };

        var ok = AuditReportFormats.TryRender(format, issues, out var content);

        Assert.True(ok);
        Assert.Contains("## RevitCli Check Report", content);
        Assert.Contains("| r | error | Doors:1 | m |", content);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("html")]
    [InlineData("table")]
    [InlineData("csv")]
    [InlineData("")]
    [InlineData(null)]
    public void TryRender_LegacyOrUnknownFormats_ReturnsFalse(string? format)
    {
        var ok = AuditReportFormats.TryRender(format, new List<AuditIssue>(), out var content);

        Assert.False(ok);
        Assert.Equal("", content);
    }

    [Fact]
    public void SupportedFormats_ContainsExactlyKnownFormats()
    {
        Assert.Equal(new[] { "sarif", "pr-comment" }, AuditReportFormats.SupportedFormats);
    }

    [Fact]
    public void TryRender_PassesDocumentPathThroughToSarif()
    {
        var issue = new AuditIssue
        {
            Rule = "r", Severity = "warning", Message = "m",
            ElementId = 1, Category = "Doors"
        };

        var ok = AuditReportFormats.TryRender(
            "sarif",
            new[] { issue },
            documentPath: "/tmp/example.rvt",
            out var content);

        Assert.True(ok);
        using var doc = JsonDocument.Parse(content);
        var props = doc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results")[0]
            .GetProperty("properties");
        Assert.Equal("/tmp/example.rvt", props.GetProperty("documentPath").GetString());
    }

    [Theory]
    [InlineData("sarif", ".sarif")]
    [InlineData("pr-comment", ".md")]
    [InlineData("json", null)]
    [InlineData("html", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void GetDefaultExtension_ReturnsExpectedHints(string? format, string? expected)
    {
        Assert.Equal(expected, AuditReportFormats.GetDefaultExtension(format));
    }

    [Theory]
    [InlineData(".sarif", "sarif")]
    [InlineData("sarif", "sarif")]
    [InlineData(".SARIF", "sarif")]
    [InlineData(".json", null)]
    [InlineData(".md", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void InferFormatFromExtension_ReturnsExpected(string? extension, string? expected)
    {
        Assert.Equal(expected, AuditReportFormats.InferFormatFromExtension(extension));
    }
}
