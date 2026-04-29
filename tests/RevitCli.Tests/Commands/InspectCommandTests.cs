using System;
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

public class InspectCommandTests
{
    [Fact]
    public async Task Categories_Table_PrintsNextCommands()
    {
        var elements = new[]
        {
            new ElementInfo { Id = 10, Name = "Door 1", Category = "Doors", TypeName = "Single" }
        };
        var response = ApiResponse<ElementInfo[]>.Ok(elements);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteCategoriesAsync(client, "table", includeEmpty: false, writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("doors", output);
        Assert.Contains("revitcli inspect params doors", output);
        Assert.True(handler.CallCount >= 3);
    }

    [Fact]
    public async Task Categories_ServerDown_PrintsError()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteCategoriesAsync(client, "table", includeEmpty: false, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not running", writer.ToString().ToLowerInvariant());
    }

    [Fact]
    public async Task Params_Table_AggregatesParameters()
    {
        var elements = new[]
        {
            new ElementInfo
            {
                Id = 1,
                Name = "Door 1",
                Category = "Doors",
                Parameters =
                {
                    ["Mark"] = "D-01",
                    ["Fire Rating"] = "60min"
                }
            },
            new ElementInfo
            {
                Id = 2,
                Name = "Door 2",
                Category = "Doors",
                Parameters =
                {
                    ["Mark"] = "D-02"
                }
            }
        };
        var response = ApiResponse<ElementInfo[]>.Ok(elements);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteParamsAsync(client, "doors", "table", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Mark", output);
        Assert.Contains("100", output);
        Assert.Contains("Fire Rating", output);
        Assert.Contains("revitcli set doors --param \"Fire Rating\" --value \"<value>\" --dry-run", output);
        Assert.Contains("Dry-run probe", output);
    }

    [Fact]
    public async Task Params_Json_PrintsCodexFriendlyFields()
    {
        var elements = new[]
        {
            new ElementInfo
            {
                Id = 1,
                Name = "Door 1",
                Category = "Doors",
                Parameters =
                {
                    ["Mark"] = "D-01"
                }
            }
        };
        var response = ApiResponse<ElementInfo[]>.Ok(elements);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteParamsAsync(client, "doors", "json", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("\"name\": \"Mark\"", output);
        Assert.Contains("\"coveragePercent\": 100", output);
        Assert.Contains("\"dryRunProbeCommand\"", output);
    }

    [Fact]
    public async Task Sheets_Table_PrintsDryRunExportCommands()
    {
        var snapshot = new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "A101",
                    Name = "Floor Plan",
                    PlacedViewIds = { 100, 101 }
                },
                new SnapshotSheet
                {
                    ViewId = 11,
                    Number = "G001",
                    Name = "Cover",
                    PlacedViewIds = { 102 }
                }
            }
        };
        var response = ApiResponse<ModelSnapshot>.Ok(snapshot);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSheetsAsync(client, "table", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains("/api/snapshot", handler.LastRequestUri);
        Assert.Contains("A101", output);
        Assert.Contains("Floor Plan", output);
        Assert.Contains("revitcli export --format pdf --sheets \"A101\"", output);
        Assert.Contains("revitcli export --format pdf --sheets \"A101\" --dry-run", output);
        Assert.True(
            CountOccurrences(output, "revitcli export --format pdf --sheets \"A101\"") >= 2,
            "Table output should include separate export and dry-run commands for each sheet.");
        AssertSnapshotRequestCapturesOnlySheets(handler.LastRequestBody);
    }

    [Fact]
    public async Task Sheets_Json_PrintsCodexFriendlyFields()
    {
        var snapshot = new ModelSnapshot
        {
            Sheets =
            {
                new SnapshotSheet
                {
                    ViewId = 10,
                    Number = "A101",
                    Name = "Floor Plan",
                    PlacedViewIds = { 100, 101 }
                }
            }
        };
        var response = ApiResponse<ModelSnapshot>.Ok(snapshot);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSheetsAsync(client, "json", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        AssertSnapshotRequestCapturesOnlySheets(handler.LastRequestBody);
        Assert.Contains("\"number\": \"A101\"", output);
        Assert.Contains("\"placedViewCount\": 2", output);
        Assert.Contains("\"exportReady\": true", output);
        Assert.Contains("\"exportCommand\"", output);
        Assert.Contains("\"dryRunCommand\"", output);
        Assert.Contains("\"dryRunExportCommand\"", output);

        using var document = JsonDocument.Parse(output);
        var item = document.RootElement[0];
        Assert.Equal("revitcli export --format pdf --sheets \"A101\"", item.GetProperty("exportCommand").GetString());
        Assert.Equal(
            "revitcli export --format pdf --sheets \"A101\" --dry-run",
            item.GetProperty("dryRunCommand").GetString());
        Assert.Equal(
            item.GetProperty("dryRunCommand").GetString(),
            item.GetProperty("dryRunExportCommand").GetString());
    }

    [Fact]
    public async Task Sheets_ServerDown_PrintsError()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSheetsAsync(client, "table", writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not running", writer.ToString().ToLowerInvariant());
    }

    [Fact]
    public async Task Schedules_Table_PrintsExportCommands()
    {
        var schedules = new[]
        {
            new ScheduleInfo { Id = 2, Name = "Room Schedule", Category = "Rooms", FieldCount = 4, RowCount = 9 },
            new ScheduleInfo { Id = 1, Name = "Door Schedule", Category = "Doors", FieldCount = 5, RowCount = 12 }
        };
        var response = ApiResponse<ScheduleInfo[]>.Ok(schedules);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSchedulesAsync(client, "table", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Door Schedule", output);
        Assert.Contains("revitcli schedule export --name \"Door Schedule\" --output csv", output);
    }

    [Fact]
    public async Task Schedules_Json_PrintsCodexFriendlyFields()
    {
        var schedules = new[]
        {
            new ScheduleInfo { Id = 1, Name = "Door Schedule", Category = "Doors", FieldCount = 5, RowCount = 12 }
        };
        var response = ApiResponse<ScheduleInfo[]>.Ok(schedules);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSchedulesAsync(client, "json", writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("\"exportReady\": true", output);
        Assert.Contains("\"exportCommand\"", output);
        Assert.Contains("revitcli schedule export --name", output);
        Assert.Contains("Door Schedule", output);
    }

    [Fact]
    public async Task Schedules_ServerDown_PrintsError()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await InspectCommand.ExecuteSchedulesAsync(client, "table", writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not running", writer.ToString().ToLowerInvariant());
    }

    private static void AssertSnapshotRequestCapturesOnlySheets(string? requestBody)
    {
        Assert.False(string.IsNullOrWhiteSpace(requestBody));

        using var document = JsonDocument.Parse(requestBody);
        var root = document.RootElement;

        Assert.True(root.GetProperty("includeSheets").GetBoolean());
        Assert.False(root.GetProperty("includeSchedules").GetBoolean());

        var includeCategories = root.GetProperty("includeCategories");
        Assert.Equal(JsonValueKind.Array, includeCategories.ValueKind);
        Assert.Equal(0, includeCategories.GetArrayLength());
    }

    private static int CountOccurrences(string value, string substring)
    {
        var count = 0;
        var index = 0;

        while ((index = value.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }
}
