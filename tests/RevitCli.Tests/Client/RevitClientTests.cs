using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Client;

public class RevitClientTests
{
    [Fact]
    public async Task GetStatusAsync_ReturnsStatusInfo()
    {
        var statusInfo = new StatusInfo
        {
            RevitVersion = "2025",
            DocumentName = "Project1.rvt"
        };
        var response = ApiResponse<StatusInfo>.Ok(statusInfo);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });

        var result = await client.GetStatusAsync();

        Assert.True(result.Success);
        Assert.Equal("2025", result.Data!.RevitVersion);
        Assert.Equal("Project1.rvt", result.Data.DocumentName);
    }

    [Fact]
    public async Task GetStatusAsync_ServerDown_ReturnsFail()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });

        var result = await client.GetStatusAsync();

        Assert.False(result.Success);
        Assert.Contains("not running", result.Error!.ToLower());
    }

    [Fact]
    public async Task QueryElementsAsync_WithCategory_SendsCorrectRequest()
    {
        var elements = new[] { new ElementInfo { Id = 1, Name = "Wall 1", Category = "Walls" } };
        var response = ApiResponse<ElementInfo[]>.Ok(elements);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });

        var result = await client.QueryElementsAsync("walls", null);

        Assert.True(result.Success);
        var data = result.Data!;
        Assert.Single(data);
        Assert.Equal("Wall 1", data[0].Name);
        Assert.Contains("category=walls", handler.LastRequestUri!);
    }

    [Fact]
    public async Task ListSchedulesAsync_ReturnsSchedules()
    {
        var schedules = new[] { new ScheduleInfo { Id = 1, Name = "Door Schedule", Category = "Doors" } };
        var response = ApiResponse<ScheduleInfo[]>.Ok(schedules);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });

        var result = await client.ListSchedulesAsync();

        Assert.True(result.Success);
        Assert.Single(result.Data!);
        Assert.Equal("Door Schedule", result.Data![0].Name);
    }

    [Fact]
    public async Task ExportScheduleAsync_ReturnsData()
    {
        var data = new ScheduleData
        {
            Columns = new List<string> { "Name", "Level" },
            Rows = new List<Dictionary<string, string>>
            {
                new() { ["Name"] = "Door-01", ["Level"] = "Level 1" }
            },
            TotalRows = 1
        };
        var response = ApiResponse<ScheduleData>.Ok(data);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });

        var result = await client.ExportScheduleAsync(new ScheduleExportRequest { Category = "Doors" });

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Columns.Count);
        Assert.Single(result.Data.Rows);
    }

    [Fact]
    public async Task CreateScheduleAsync_ReturnsResult()
    {
        var createResult = new ScheduleCreateResult { ViewId = 100, Name = "Test", FieldCount = 3, RowCount = 5 };
        var response = ApiResponse<ScheduleCreateResult>.Ok(createResult);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });

        var result = await client.CreateScheduleAsync(new ScheduleCreateRequest { Category = "Doors", Name = "Test" });

        Assert.True(result.Success);
        Assert.Equal(100, result.Data!.ViewId);
    }

    [Fact]
    public async Task ListViewsAsync_ReturnsViewInventory()
    {
        var views = new[] { new ViewInfo { Id = 10, Name = "Level 1", ViewType = "FloorPlan" } };
        var response = ApiResponse<ViewInfo[]>.Ok(views);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });

        var result = await client.ListViewsAsync();

        Assert.True(result.Success);
        Assert.Equal("/api/views", new Uri(handler.LastRequestUri!).AbsolutePath);
        Assert.Equal("Level 1", Assert.Single(result.Data!).Name);
    }

    [Fact]
    public async Task ListLinksAsync_ReturnsLinkInventory()
    {
        var links = new[] { new LinkInfo { Id = 10, Name = "Structural Model", Path = @"D:\coordination\struct.rvt" } };
        var response = ApiResponse<LinkInfo[]>.Ok(links);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });

        var result = await client.ListLinksAsync();

        Assert.True(result.Success);
        Assert.Equal("/api/links", new Uri(handler.LastRequestUri!).AbsolutePath);
        Assert.Equal("Structural Model", Assert.Single(result.Data!).Name);
    }

    [Fact]
    public async Task ApplyLinkRepairAsync_PostsRepairRequest()
    {
        var response = ApiResponse<LinkRepairResult>.Ok(new LinkRepairResult
        {
            Affected = 1,
            Preview = { new LinkRepairOperation { LinkId = 10, LinkTypeId = 20, LinkName = "Structural Model" } }
        });
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });

        var result = await client.ApplyLinkRepairAsync(new LinkRepairRequest
        {
            DryRun = true,
            Actions = { new LinkRepairOperation { LinkId = 10, LinkTypeId = 20, LinkName = "Structural Model" } }
        });

        Assert.True(result.Success);
        Assert.Equal("/api/links/repair", new Uri(handler.LastRequestUri!).AbsolutePath);
        Assert.Contains("Structural Model", handler.LastRequestBody);
        Assert.Equal(1, result.Data!.Affected);
    }

    [Fact]
    public async Task ListModelMapElementsAsync_ReturnsModelMapInventory()
    {
        var elements = new[] { new ModelMapElementInfo { Id = 20, Name = "Room 101", Category = "Rooms", WorksetName = "Architecture" } };
        var response = ApiResponse<ModelMapElementInfo[]>.Ok(elements);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });

        var result = await client.ListModelMapElementsAsync();

        Assert.True(result.Success);
        Assert.Equal("/api/model/map", new Uri(handler.LastRequestUri!).AbsolutePath);
        Assert.Equal("Room 101", Assert.Single(result.Data!).Name);
    }

    [Fact]
    public async Task ApplyModelMapFixAsync_PostsFixRequest()
    {
        var response = ApiResponse<ModelMapFixResult>.Ok(new ModelMapFixResult
        {
            Affected = 1,
            Preview = { new ModelMapFixOperation { ElementId = 20, ElementName = "Room 101", Field = "workset", NewValue = "Architecture" } }
        });
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });

        var result = await client.ApplyModelMapFixAsync(new ModelMapFixRequest
        {
            DryRun = true,
            Actions =
            {
                new ModelMapFixOperation
                {
                    ElementId = 20,
                    ElementName = "Room 101",
                    Field = "workset",
                    OldValue = "Interior",
                    NewValue = "Architecture"
                }
            }
        });

        Assert.True(result.Success);
        Assert.Equal("/api/model/map/fix", new Uri(handler.LastRequestUri!).AbsolutePath);
        Assert.Contains("Architecture", handler.LastRequestBody);
        Assert.Equal(1, result.Data!.Affected);
    }

    [Fact]
    public async Task CaptureSnapshotAsync_PostsRequestAndParsesResponse()
    {
        var snapshot = new ModelSnapshot { SchemaVersion = 1, TakenAt = "2026-04-23T00:00:00Z" };
        var response = ApiResponse<ModelSnapshot>.Ok(snapshot);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });

        var result = await client.CaptureSnapshotAsync(new SnapshotRequest());

        Assert.True(result.Success);
        Assert.Equal("2026-04-23T00:00:00Z", result.Data!.TakenAt);
        Assert.Equal("http://localhost:17839/api/snapshot", handler.LastRequestUri);
        Assert.Contains("IncludeSheets", handler.LastRequestBody ?? "", System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CaptureSnapshotAsync_ConnectionFailed_ReturnsFail()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });

        var result = await client.CaptureSnapshotAsync(new SnapshotRequest());

        Assert.False(result.Success);
        Assert.Contains("not running", result.Error, System.StringComparison.OrdinalIgnoreCase);
    }
}

public class FakeHttpHandler : HttpMessageHandler
{
    private readonly string? _response;
    private readonly bool _throwException;
    public string? LastRequestUri { get; private set; }
    public int CallCount { get; private set; }
    public string? LastRequestBody { get; private set; }

    public FakeHttpHandler(string? response = null, bool throwException = false)
    {
        _response = response;
        _throwException = throwException;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequestUri = request.RequestUri?.ToString();
        if (request.Content != null)
            LastRequestBody = await request.Content.ReadAsStringAsync();

        if (_throwException)
            throw new HttpRequestException("Connection refused");

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_response ?? "{}")
        };
    }
}
