using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Shared;
using RevitCli.Tests.Client;
using Xunit;

namespace RevitCli.Tests.Commands;

public class ScheduleCommandTests
{
    [Fact]
    public async Task List_ReturnsSchedules_PrintsTable()
    {
        var schedules = new[]
        {
            new ScheduleInfo { Id = 1, Name = "Door Schedule", Category = "Doors", FieldCount = 5, RowCount = 12 }
        };
        var response = ApiResponse<ScheduleInfo[]>.Ok(schedules);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteListAsync(client, "table", writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Door Schedule", writer.ToString());
    }

    [Fact]
    public async Task List_OutputJson_PrintsJson()
    {
        var schedules = new[] { new ScheduleInfo { Id = 1, Name = "Test", Category = "Walls" } };
        var response = ApiResponse<ScheduleInfo[]>.Ok(schedules);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteListAsync(client, "json", writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("\"name\"", output.ToLower());
    }

    [Fact]
    public async Task List_OutputMarkdown_PrintsReviewTable()
    {
        var schedules = new[]
        {
            new ScheduleInfo { Id = 1, Name = "Door Schedule", Category = "Doors", FieldCount = 5, RowCount = 12 }
        };
        var response = ApiResponse<ScheduleInfo[]>.Ok(schedules);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteListAsync(client, "markdown", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# Schedule List", output);
        Assert.Contains("- Schedules: `1`", output);
        Assert.Contains("| Name | Category | Fields | Rows | Id |", output);
        Assert.Contains("| Door Schedule | Doors | 5 | 12 | 1 |", output);
    }

    [Fact]
    public async Task List_OutputJson_EmptyModel_PrintsEmptyArray()
    {
        var response = ApiResponse<ScheduleInfo[]>.Ok(Array.Empty<ScheduleInfo>());
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteListAsync(client, "json", writer);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(0, json.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task List_OutputMarkdown_EmptyModel_PrintsNoSchedules()
    {
        var response = ApiResponse<ScheduleInfo[]>.Ok(Array.Empty<ScheduleInfo>());
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteListAsync(client, "markdown", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# Schedule List", output);
        Assert.Contains("- Schedules: `0`", output);
        Assert.Contains("No schedules found in the model.", output);
    }

    [Fact]
    public async Task List_OutputJson_ServerDown_PrintsErrorObject()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteListAsync(client, "json", writer);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("not running", json.RootElement.GetProperty("error").GetString()!.ToLowerInvariant());
    }

    [Fact]
    public async Task List_UnknownOutput_ReturnsFailureBeforeHttp()
    {
        var schedules = new[] { new ScheduleInfo { Id = 1, Name = "Test", Category = "Walls" } };
        var response = ApiResponse<ScheduleInfo[]>.Ok(schedules);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteListAsync(client, "xml", writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--output", writer.ToString());
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Export_UnknownOutput_ReturnsFailureBeforeHttp()
    {
        var data = new ScheduleData
        {
            Columns = new List<string> { "Name" },
            Rows = new List<Dictionary<string, string>> { new() { ["Name"] = "Room A" } },
            TotalRows = 1
        };
        var response = ApiResponse<ScheduleData>.Ok(data);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteExportAsync(
            client, "Rooms", null, "Name", null, null, false, "xml", null, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--output", writer.ToString());
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Export_ByCategoryAndFields_ReturnsData()
    {
        var data = new ScheduleData
        {
            Columns = new List<string> { "Fire Rating", "Width" },
            Rows = new List<Dictionary<string, string>>
            {
                new() { ["Fire Rating"] = "60min", ["Width"] = "900" }
            },
            TotalRows = 1
        };
        var response = ApiResponse<ScheduleData>.Ok(data);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteExportAsync(
            client, "Doors", null, "Fire Rating,Width", null, null, false, "csv", null, writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Fire Rating", writer.ToString());
    }

    [Fact]
    public async Task Export_ByExistingName_ReturnsData()
    {
        var data = new ScheduleData
        {
            Columns = new List<string> { "Name" },
            Rows = new List<Dictionary<string, string>> { new() { ["Name"] = "Room A" } },
            TotalRows = 1
        };
        var response = ApiResponse<ScheduleData>.Ok(data);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteExportAsync(
            client, null, "Room Schedule", null, null, null, false, "json", null, writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Room A", writer.ToString());
    }

    [Fact]
    public async Task Export_OutputJson_EmptyRows_PrintsScheduleDataJson()
    {
        var data = new ScheduleData
        {
            Columns = new List<string> { "Name", "Width" },
            Rows = new List<Dictionary<string, string>>(),
            TotalRows = 0
        };
        var response = ApiResponse<ScheduleData>.Ok(data);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteExportAsync(
            client, null, "Door Schedule", null, null, null, false, "json", null, writer);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        Assert.Equal(2, json.RootElement.GetProperty("columns").GetArrayLength());
        Assert.Empty(json.RootElement.GetProperty("rows").EnumerateArray());
        Assert.Equal(0, json.RootElement.GetProperty("totalRows").GetInt32());
    }

    [Fact]
    public async Task Export_OutputCsv_EmptyRows_PrintsHeader()
    {
        var data = new ScheduleData
        {
            Columns = new List<string> { "Name", "Width" },
            Rows = new List<Dictionary<string, string>>(),
            TotalRows = 0
        };
        var response = ApiResponse<ScheduleData>.Ok(data);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteExportAsync(
            client, null, "Door Schedule", null, null, null, false, "csv", null, writer);

        Assert.Equal(0, exitCode);
        Assert.Equal("Name,Width", writer.ToString().Trim());
    }

    [Fact]
    public async Task Export_OutputMarkdown_PrintsRows()
    {
        var data = new ScheduleData
        {
            Columns = new List<string> { "Name", "Comments" },
            Rows = new List<Dictionary<string, string>>
            {
                new() { ["Name"] = "Room A", ["Comments"] = "East | Wing" }
            },
            TotalRows = 1
        };
        var response = ApiResponse<ScheduleData>.Ok(data);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteExportAsync(
            client, null, "Room Schedule", null, null, null, false, "markdown", null, writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# Schedule Export", output);
        Assert.Contains("- Columns: `2`", output);
        Assert.Contains("| Name | Comments |", output);
        Assert.Contains("| Room A | East \\| Wing |", output);
    }

    [Fact]
    public async Task Export_NoCategoryOrName_PrintsError()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteExportAsync(
            client, null, null, null, null, null, false, "table", null, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--category", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Export_BothCategoryAndName_PrintsError()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteExportAsync(
            client, "Doors", "Existing Schedule", null, null, null, false, "table", null, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("mutually exclusive", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Create_ValidRequest_ReturnsResult()
    {
        var result = new ScheduleCreateResult { ViewId = 100, Name = "Door Schedule", FieldCount = 3, RowCount = 10 };
        var response = ApiResponse<ScheduleCreateResult>.Ok(result);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteCreateAsync(
            client, "Doors", "Fire Rating,Width,Height", null, null, false, "Door Schedule", null, null, writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Door Schedule", writer.ToString());
    }

    [Fact]
    public async Task Create_MissingName_PrintsError()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteCreateAsync(
            client, "Doors", "Width", null, null, false, null!, null, null, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--name", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Create_MissingCategory_PrintsError()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteCreateAsync(
            client, null!, "Width", null, null, false, "Test", null, null, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--category", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Export_ServerDown_PrintsError()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteExportAsync(
            client, "Doors", null, "Width", null, null, false, "csv", null, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not running", writer.ToString().ToLower());
    }
}
