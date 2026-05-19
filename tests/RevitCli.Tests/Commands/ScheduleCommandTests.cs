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
    public async Task Create_DryRunJson_PrintsPreviewWithoutCallingRevit()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteCreateAsync(
            client, "Doors", "Mark,Level", null, "Mark", false, "Door Review", null, null,
            dryRun: true, outputFormat: "json", receiptDir: null, writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("schedule-create.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("dryRun").GetBoolean());
        Assert.False(root.GetProperty("willWrite").GetBoolean());
        Assert.False(root.GetProperty("receiptRequired").GetBoolean());
        Assert.False(root.GetProperty("receiptSaved").GetBoolean());
        Assert.Equal("Doors", root.GetProperty("category").GetString());
        Assert.Equal("Door Review", root.GetProperty("name").GetString());
        Assert.Contains(root.GetProperty("fields").EnumerateArray(), field => field.GetString() == "Mark");
        Assert.Equal("revitcli schedule create --category Doors --name \"Door Review\" --fields Mark,Level --sort Mark --output json",
            root.GetProperty("approvalCommand").GetString());
    }

    [Fact]
    public async Task Create_JsonRealRun_WritesReceipt()
    {
        var result = new ScheduleCreateResult { ViewId = 100, Name = "Door Schedule", FieldCount = 2, RowCount = 10 };
        var response = ApiResponse<ScheduleCreateResult>.Ok(result);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var receiptDir = Path.Combine(Path.GetTempPath(), $"revitcli-schedule-receipts-{Guid.NewGuid():N}");
        var writer = new StringWriter();

        try
        {
            var exitCode = await ScheduleCommand.ExecuteCreateAsync(
                client, "Doors", "Mark,Level", null, "Mark", false, "Door Schedule", null, null,
                dryRun: false, outputFormat: "json", receiptDir, writer);

            Assert.Equal(0, exitCode);
            using var document = JsonDocument.Parse(writer.ToString());
            var root = document.RootElement;
            Assert.Equal("schedule-create.v1", root.GetProperty("schemaVersion").GetString());
            Assert.False(root.GetProperty("dryRun").GetBoolean());
            Assert.True(root.GetProperty("willWrite").GetBoolean());
            Assert.True(root.GetProperty("receiptRequired").GetBoolean());
            Assert.True(root.GetProperty("receiptSaved").GetBoolean());
            var receiptPath = root.GetProperty("receiptPath").GetString();
            Assert.NotNull(receiptPath);
            Assert.True(File.Exists(receiptPath));

            using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath!));
            Assert.Equal("schedule-create-receipt.v1", receipt.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("Door Schedule", receipt.RootElement.GetProperty("name").GetString());
        }
        finally
        {
            if (Directory.Exists(receiptDir))
                Directory.Delete(receiptDir, recursive: true);
        }
    }

    [Fact]
    public async Task Create_JsonRealRun_WhenReceiptCannotBeSaved_PrintsWarning()
    {
        var result = new ScheduleCreateResult { ViewId = 100, Name = "Door Schedule", FieldCount = 2, RowCount = 10 };
        var response = ApiResponse<ScheduleCreateResult>.Ok(result);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var blockedReceiptDir = Path.Combine(Path.GetTempPath(), $"revitcli-schedule-receipts-{Guid.NewGuid():N}");
        File.WriteAllText(blockedReceiptDir, "not a directory");
        var writer = new StringWriter();

        try
        {
            var exitCode = await ScheduleCommand.ExecuteCreateAsync(
                client, "Doors", "Mark,Level", null, "Mark", false, "Door Schedule", null, null,
                dryRun: false, outputFormat: "json", receiptDir: blockedReceiptDir, writer);

            Assert.Equal(0, exitCode);
            using var document = JsonDocument.Parse(writer.ToString());
            var root = document.RootElement;
            Assert.True(root.GetProperty("receiptRequired").GetBoolean());
            Assert.False(root.GetProperty("receiptSaved").GetBoolean());
            Assert.Null(root.GetProperty("receiptPath").GetString());
            Assert.Contains(
                root.GetProperty("warnings").EnumerateArray(),
                warning => warning.GetString()!.Contains("receipt could not be saved", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(blockedReceiptDir))
                File.Delete(blockedReceiptDir);
        }
    }

    [Fact]
    public async Task Create_UnknownOutput_ReturnsFailureBeforeCallingRevit()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteCreateAsync(
            client, "Doors", "Mark", null, null, false, "Door Review", null, null,
            dryRun: true, outputFormat: "yaml", receiptDir: null, writer);

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: --output must be 'table', 'json', or 'markdown'." + Environment.NewLine, writer.ToString());
    }

    [Fact]
    public async Task Create_Filter_ReturnsPortableValidationError()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteCreateAsync(
            client, "Doors", "Mark", "Mark = D-01", null, false, "Door Review", null, null,
            dryRun: true, outputFormat: "json", receiptDir: null, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--filter on schedule create is not supported", writer.ToString());
    }

    [Fact]
    public async Task Create_FilterJson_PrintsScheduleCreateErrorEnvelope()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteCreateAsync(
            client, "Doors", "Mark", "Mark = D-01", null, false, "Door Review", null, null,
            dryRun: true, outputFormat: "json", receiptDir: null, writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("schedule-create.v1", root.GetProperty("schemaVersion").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("dryRun").GetBoolean());
        Assert.False(root.GetProperty("willWrite").GetBoolean());
        Assert.Contains("--filter on schedule create is not supported", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Create_ServerFailureJson_PrintsScheduleCreateErrorEnvelope()
    {
        var response = ApiResponse<ScheduleCreateResult>.Fail("Revit rejected schedule create.");
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteCreateAsync(
            client, "Doors", "Mark", null, null, false, "Door Review", null, null,
            dryRun: false, outputFormat: "json", receiptDir: null, writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("schedule-create.v1", root.GetProperty("schemaVersion").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.False(root.GetProperty("dryRun").GetBoolean());
        Assert.True(root.GetProperty("willWrite").GetBoolean());
        Assert.Equal("Revit rejected schedule create.", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Create_MissingNameMarkdown_PrintsScheduleCreateError()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ScheduleCommand.ExecuteCreateAsync(
            client, "Doors", "Mark", null, null, false, null!, null, null,
            dryRun: true, outputFormat: "markdown", receiptDir: null, writer);

        var text = writer.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("# Schedule Create", text);
        Assert.Contains("- Schema: `schedule-create.v1`", text);
        Assert.Contains("- Status: `FAIL`", text);
        Assert.Contains("--name is required", text);
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
