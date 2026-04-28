using System.Text.Json;
using RevitCli.Addin.Services;
using RevitCli.Shared;

namespace RevitCli.Addin.Tests.Handlers;

/// <summary>
/// Tests PlaceholderRevitOperations directly. Does NOT test HTTP handlers or controller delegation.
/// Controller-level tests are covered by ProtocolTests which hit real HTTP endpoints.
/// </summary>
public class PlaceholderOperationsTests
{
    private readonly PlaceholderRevitOperations _operations = new();

    [Fact]
    public async Task GetStatusAsync_ReturnsStatusInfo()
    {
        var status = await _operations.GetStatusAsync();

        Assert.Equal("2025", status.RevitVersion);
        Assert.Equal("Placeholder.rvt", status.DocumentName);
    }

    [Fact]
    public async Task QueryElementsAsync_ReturnsEmptyArray()
    {
        var elements = await _operations.QueryElementsAsync("Walls", null);

        Assert.Empty(elements);
    }

    [Fact]
    public async Task GetElementByIdAsync_ReturnsElementWithId()
    {
        var element = await _operations.GetElementByIdAsync(42);

        Assert.NotNull(element);
        Assert.Equal(42, element.Id);
        Assert.Equal("Element 42", element.Name);
    }

    [Fact]
    public async Task ExportAsync_ReturnsCompletedProgress()
    {
        var request = new ExportRequest
        {
            Format = "dwg",
            Sheets = new() { "A1" },
            OutputDir = "/tmp"
        };

        var progress = await _operations.ExportAsync(request);

        Assert.Equal("completed", progress.Status);
        Assert.Equal(100, progress.Progress);
        Assert.False(string.IsNullOrEmpty(progress.TaskId));
    }

    [Fact]
    public async Task ExportAsync_DryRun_ReturnsDryRunProgressMessage()
    {
        // Server-side contract: when ExportRequest.DryRun is true, the operation
        // must return Status="completed" with a "Dry run: ..." message that names
        // the count of would-be files and the output directory.
        var request = new ExportRequest
        {
            Format = "dwg",
            Sheets = new() { "A1*", "A2*" },
            OutputDir = "/tmp/exports",
            DryRun = true
        };

        var progress = await _operations.ExportAsync(request);

        Assert.Equal("completed", progress.Status);
        Assert.Equal(100, progress.Progress);
        Assert.NotNull(progress.Message);
        Assert.StartsWith("Dry run:", progress.Message);
        Assert.Contains("file(s)", progress.Message);
        Assert.Contains("/tmp/exports", progress.Message);
    }

    [Fact]
    public async Task GetExportProgressAsync_ReturnsProgressForTaskId()
    {
        var progress = await _operations.GetExportProgressAsync("test-123");

        Assert.Equal("test-123", progress.TaskId);
        Assert.Equal("completed", progress.Status);
    }

    [Fact]
    public async Task SetParametersAsync_ReturnsZeroAffected()
    {
        var request = new SetRequest
        {
            Category = "doors",
            Param = "Fire Rating",
            Value = "60min",
            DryRun = true
        };

        var result = await _operations.SetParametersAsync(request);

        Assert.Equal(0, result.Affected);
    }

    [Fact]
    public async Task RunAuditAsync_ReturnsPlaceholderResult()
    {
        var request = new AuditRequest
        {
            RequiredParameters = new()
            {
                new RequiredParameterSpec
                {
                    Category = "doors",
                    Parameter = "Mark",
                    RequireNonEmpty = true,
                    Severity = "warning"
                }
            }
        };

        var result = await _operations.RunAuditAsync(request);

        Assert.Equal(4, result.Passed);
        Assert.Equal(1, result.Failed);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("doors", issue.Category);
        Assert.Equal("Mark", issue.Parameter);
        Assert.Equal("doors", issue.Target);
        Assert.Equal("", issue.CurrentValue);
        Assert.Equal("D-100", issue.ExpectedValue);
        Assert.Equal("structured", issue.Source);
    }

    [Fact]
    public async Task RunAuditAsync_RetainsStructuredMetadataAfterJsonRoundTrip()
    {
        var request = new AuditRequest
        {
            RequiredParameters = new()
            {
                new RequiredParameterSpec
                {
                    Category = "doors",
                    Parameter = "Mark",
                    RequireNonEmpty = true,
                    Severity = "warning"
                }
            }
        };

        var result = await _operations.RunAuditAsync(request);
        var json = JsonSerializer.Serialize(result);
        var roundTripped = JsonSerializer.Deserialize<AuditResult>(json);

        Assert.NotNull(roundTripped);
        var issue = Assert.Single(roundTripped!.Issues);
        Assert.Equal("doors", issue.Category);
        Assert.Equal("Mark", issue.Parameter);
        Assert.Equal("doors", issue.Target);
        Assert.Equal("", issue.CurrentValue);
        Assert.Equal("D-100", issue.ExpectedValue);
        Assert.Equal("structured", issue.Source);
    }

    [Fact]
    public async Task RunAuditAsync_NamingRequestRemainsPassingAndEmpty()
    {
        var request = new AuditRequest { Rules = new() { "naming" } };

        var result = await _operations.RunAuditAsync(request);

        Assert.Equal(5, result.Passed);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task ListFamiliesAsync_DefaultRequest_ReturnsThreeFixedFamilies()
    {
        var result = await _operations.ListFamiliesAsync(new FamilyListRequest());

        Assert.Equal(3, result.Length);
        Assert.Contains(result, f => f.Name == "M_Single-Flush" && f.Category == "Doors");
        Assert.Contains(result, f => f.Name == "M_Fixed" && f.Category == "Windows");
        Assert.Contains(result, f => f.IsInPlace);
    }

    [Fact]
    public async Task ListFamiliesAsync_UnusedFilter_DropsPlacedFamilies()
    {
        var result = await _operations.ListFamiliesAsync(
            new FamilyListRequest { IncludeUnplaced = true });

        Assert.NotEmpty(result);
        Assert.All(result, f => Assert.False(f.IsPlaced));
    }

    [Fact]
    public async Task ListFamiliesAsync_CategoryFilter_KeepsOnlyMatching()
    {
        var result = await _operations.ListFamiliesAsync(
            new FamilyListRequest { Category = "Doors" });

        Assert.Single(result);
        Assert.Equal("Doors", result[0].Category);
    }
}
