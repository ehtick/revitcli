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
}
