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

public class StatusCommandTests
{
    [Fact]
    public async Task Execute_ServerOnline_PrintsStatus()
    {
        var status = new StatusInfo { RevitVersion = "2025", DocumentName = "Test.rvt" };
        var response = ApiResponse<StatusInfo>.Ok(status);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await StatusCommand.ExecuteAsync(client, writer);

        var output = writer.ToString();
        Assert.Contains("2025", output);
        Assert.Contains("Test.rvt", output);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Execute_JsonOutput_PrintsStatusInfo()
    {
        var status = new StatusInfo
        {
            RevitVersion = "2026",
            RevitYear = 2026,
            AddinVersion = "2.1.0",
            DocumentName = "Model.rvt",
            DocumentPath = "C:/Models/Model.rvt",
            Capabilities = { "snapshot", "schedule" },
        };
        var response = ApiResponse<StatusInfo>.Ok(status);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await StatusCommand.ExecuteAsync(client, writer, "json");

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        Assert.Equal("2026", json.RootElement.GetProperty("revitVersion").GetString());
        Assert.Equal("Model.rvt", json.RootElement.GetProperty("documentName").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("capabilities").GetArrayLength());
    }

    [Fact]
    public async Task Execute_ServerDown_PrintsError()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await StatusCommand.ExecuteAsync(client, writer);

        var output = writer.ToString();
        Assert.Contains("not running", output.ToLower());
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Execute_ServerDownJson_PrintsErrorObject()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await StatusCommand.ExecuteAsync(client, writer, "json");

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("not running", json.RootElement.GetProperty("error").GetString()!.ToLowerInvariant());
    }

    [Fact]
    public async Task Execute_UnknownOutput_ReturnsFailureBeforeHttp()
    {
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(new StatusInfo())));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await StatusCommand.ExecuteAsync(client, writer, "xml");

        Assert.Equal(1, exitCode);
        Assert.Contains("--output", writer.ToString());
        Assert.Equal(0, handler.CallCount);
    }
}
