using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Config;
using RevitCli.Shared;
using RevitCli.Tests.Client;
using Xunit;

namespace RevitCli.Tests.Commands;

public class ExportCommandTests
{
    [Fact]
    public async Task Execute_ValidRequest_PrintsTaskId()
    {
        var progress = new ExportProgress { TaskId = "task-001", Status = "completed", Progress = 100 };
        var response = ApiResponse<ExportProgress>.Ok(progress);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ExportCommand.ExecuteAsync(client, "dwg", new[] { "A1*" }, Array.Empty<string>(), "./exports", writer);

        var output = writer.ToString();
        Assert.Contains("task-001", output);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Execute_ServerDown_PrintsError()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ExportCommand.ExecuteAsync(client, "dwg", new[] { "all" }, Array.Empty<string>(), "./exports", writer);

        Assert.Contains("not running", writer.ToString().ToLower());
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Execute_MissingFormat_PrintsError()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ExportCommand.ExecuteAsync(client, null!, new[] { "all" }, Array.Empty<string>(), "./exports", writer);

        Assert.Contains("--format", writer.ToString().ToLower());
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Parser_AcceptsDryRunFlag()
    {
        // Parser must recognize --dry-run on the export command (regression: this
        // option used to be undocumented even though README claimed it worked).
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var config = new CliConfig { ExportDir = Path.GetTempPath() };
        var command = ExportCommand.Create(client, config);

        var parser = new Parser(command);
        var parseResult = parser.Parse(new[] { "--format", "dwg", "--sheets", "all", "--dry-run" });

        Assert.Empty(parseResult.Errors);
        var dryRunOption = command.Options.FirstOrDefault(o => o.Name == "dry-run");
        Assert.NotNull(dryRunOption);
        Assert.True((bool)parseResult.GetValueForOption((Option<bool>)dryRunOption!));
    }

    [Fact]
    public async Task Execute_DryRun_RequestBodyIncludesDryRunTrue()
    {
        var progress = new ExportProgress
        {
            TaskId = "task-dry",
            Status = "completed",
            Progress = 100,
            Message = "Dry run: would export 3 file(s) to ./exports"
        };
        var response = ApiResponse<ExportProgress>.Ok(progress);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ExportCommand.ExecuteAsync(
            client, "dwg", new[] { "A1*" }, Array.Empty<string>(), "./exports", dryRun: true, writer);

        Assert.Equal(0, exitCode);
        Assert.NotNull(handler.LastRequestBody);
        // Request body must signal dry-run to the server.
        Assert.Contains("\"dryRun\":true", handler.LastRequestBody);
    }

    [Fact]
    public async Task Execute_DryRun_PrintsDryRunMessage_NotExportedMessage()
    {
        var progress = new ExportProgress
        {
            TaskId = "task-dry",
            Status = "completed",
            Progress = 100,
            Message = "Dry run: would export 2 file(s) to ./exports"
        };
        var response = ApiResponse<ExportProgress>.Ok(progress);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ExportCommand.ExecuteAsync(
            client, "dwg", new[] { "A1*" }, Array.Empty<string>(), "./exports", dryRun: true, writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Dry run:", output);
        Assert.Contains("would export 2 file(s)", output);
        Assert.DoesNotContain("Export completed", output);
    }

    [Fact]
    public async Task Execute_NotDryRun_OmitsDryRunMessage()
    {
        var progress = new ExportProgress
        {
            TaskId = "task-real",
            Status = "completed",
            Progress = 100,
            Message = "Exported 2 view(s) to ./exports"
        };
        var response = ApiResponse<ExportProgress>.Ok(progress);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ExportCommand.ExecuteAsync(
            client, "dwg", new[] { "A1*" }, Array.Empty<string>(), "./exports", dryRun: false, writer);

        Assert.Equal(0, exitCode);
        Assert.NotNull(handler.LastRequestBody);
        // Default false should serialize as dryRun:false (System.Text.Json default).
        Assert.Contains("\"dryRun\":false", handler.LastRequestBody);
        Assert.DoesNotContain("Dry run:", writer.ToString());
    }

    [Fact]
    public void ExportRequest_DryRun_RoundTripsThroughJson()
    {
        // Wire-format guarantee: the new property serializes to "dryRun" so the
        // server's deserializer (which reads JsonPropertyName attributes) finds it.
        var request = new ExportRequest
        {
            Format = "dwg",
            OutputDir = "./out",
            DryRun = true
        };
        var json = JsonSerializer.Serialize(request);
        Assert.Contains("\"dryRun\":true", json);

        var roundTripped = JsonSerializer.Deserialize<ExportRequest>(json);
        Assert.NotNull(roundTripped);
        Assert.True(roundTripped!.DryRun);
    }
}
