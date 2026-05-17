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
        var outputDir = TempDir();
        var progress = new ExportProgress { TaskId = "task-001", Status = "completed", Progress = 100 };
        var response = ApiResponse<ExportProgress>.Ok(progress);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        try
        {
            var exitCode = await ExportCommand.ExecuteAsync(client, "dwg", new[] { "A1*" }, Array.Empty<string>(), outputDir, writer);

            var output = writer.ToString();
            Assert.Contains("task-001", output);
            Assert.Contains("Receipt saved", output);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            Directory.Delete(outputDir, recursive: true);
        }
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
    public async Task Execute_DryRunJson_PrintsMachineReadablePlan()
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
            client,
            "pdf",
            new[] { "A1*" },
            Array.Empty<string>(),
            "./exports",
            dryRun: true,
            writer,
            outputFormat: "json");

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        var root = json.RootElement;
        Assert.Equal("export.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("dryRun").GetBoolean());
        Assert.Equal("pdf", root.GetProperty("format").GetString());
        Assert.Equal("A1*", root.GetProperty("sheets")[0].GetString());
        Assert.Equal("completed", root.GetProperty("status").GetString());
        Assert.Equal("task-dry", root.GetProperty("taskId").GetString());
    }

    [Fact]
    public async Task Execute_DryRunJson_ServerDown_PrintsErrorObject()
    {
        var handler = new FakeHttpHandler(throwException: true);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ExportCommand.ExecuteAsync(
            client,
            "dwg",
            new[] { "all" },
            Array.Empty<string>(),
            "./exports",
            dryRun: true,
            writer,
            outputFormat: "json");

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("not running", json.RootElement.GetProperty("error").GetString()!.ToLowerInvariant());
    }

    [Fact]
    public async Task Execute_JsonWithoutDryRun_ReturnsFailureBeforeHttp()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ExportCommand.ExecuteAsync(
            client,
            "dwg",
            new[] { "all" },
            Array.Empty<string>(),
            "./exports",
            dryRun: false,
            writer,
            outputFormat: "json");

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(writer.ToString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("--dry-run", json.RootElement.GetProperty("error").GetString()!);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Execute_UnknownOutput_ReturnsFailureBeforeHttp()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await ExportCommand.ExecuteAsync(
            client,
            "dwg",
            new[] { "all" },
            Array.Empty<string>(),
            "./exports",
            dryRun: true,
            writer,
            outputFormat: "xml");

        Assert.Equal(1, exitCode);
        Assert.Contains("--output", writer.ToString());
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Execute_NotDryRun_OmitsDryRunMessage()
    {
        var outputDir = TempDir();
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

        try
        {
            var exitCode = await ExportCommand.ExecuteAsync(
                client, "dwg", new[] { "A1*" }, Array.Empty<string>(), outputDir, dryRun: false, writer);

            Assert.Equal(0, exitCode);
            Assert.NotNull(handler.LastRequestBody);
            // Default false should serialize as dryRun:false (System.Text.Json default).
            Assert.Contains("\"dryRun\":false", handler.LastRequestBody);
            Assert.DoesNotContain("Dry run:", writer.ToString());
        }
        finally
        {
            Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_NotDryRun_WritesExportReceipt()
    {
        var outputDir = TempDir();
        var progress = new ExportProgress
        {
            TaskId = "task-receipt",
            Status = "completed",
            Progress = 100,
            Message = "Exported 1 sheet"
        };
        var response = ApiResponse<ExportProgress>.Ok(progress);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        try
        {
            var exitCode = await ExportCommand.ExecuteAsync(
                client, "pdf", new[] { "A101" }, Array.Empty<string>(), outputDir, dryRun: false, writer);

            Assert.Equal(0, exitCode);
            var receiptDir = Path.Combine(outputDir, ".revitcli", "receipts");
            var receiptPath = Assert.Single(Directory.GetFiles(receiptDir, "export-*.json"));
            using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
            var root = receipt.RootElement;
            Assert.Equal("export-receipt.v1", root.GetProperty("schemaVersion").GetString());
            Assert.Equal("export", root.GetProperty("action").GetString());
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.False(root.GetProperty("dryRun").GetBoolean());
            Assert.Equal("pdf", root.GetProperty("format").GetString());
            Assert.Equal("A101", root.GetProperty("sheets")[0].GetString());
            Assert.Equal(outputDir, root.GetProperty("outputDir").GetString());
            Assert.Equal("task-receipt", root.GetProperty("taskId").GetString());
            Assert.Contains("revitcli export --format pdf", root.GetProperty("command").GetString()!);

            var manifestPath = Path.Combine(outputDir, ".revitcli", "deliveries", "manifest.jsonl");
            var manifestLine = Assert.Single(File.ReadAllLines(manifestPath));
            using var manifest = JsonDocument.Parse(manifestLine);
            var manifestRoot = manifest.RootElement;
            Assert.Equal("delivery-manifest.v1", manifestRoot.GetProperty("schemaVersion").GetString());
            Assert.Equal("export", manifestRoot.GetProperty("kind").GetString());
            Assert.Equal(Path.GetFullPath(receiptPath), manifestRoot.GetProperty("receiptPath").GetString());
            Assert.False(manifestRoot.GetProperty("dryRun").GetBoolean());
            Assert.Equal("pdf", manifestRoot.GetProperty("format").GetString());
            Assert.Equal("task-receipt", manifestRoot.GetProperty("taskId").GetString());
        }
        finally
        {
            Directory.Delete(outputDir, recursive: true);
        }
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

    private static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"revitcli_export_{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
