using System.CommandLine;
using System.Net.Http;
using System.Text.Json;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Config;
using RevitCli.Shared;
using RevitCli.Tests.Client;
using Xunit;

namespace RevitCli.Tests.Commands;

/// <summary>
/// Tests that go through Create().InvokeAsync() to verify System.CommandLine parser behavior.
/// Verifies: required options, type binding, defaults, handler wiring, and that parser
/// rejection prevents handler execution (proven by CallCount==0 AND parser error text).
/// </summary>
[Collection("Sequential")] // uses Environment.ExitCode + Console.Out/Error (process-global)
public class ParserLevelTests : IDisposable
{
    private readonly int _savedExitCode;
    private readonly TextWriter _savedOut;
    private readonly TextWriter _savedError;

    public ParserLevelTests()
    {
        _savedExitCode = Environment.ExitCode;
        _savedOut = Console.Out;
        _savedError = Console.Error;
        Environment.ExitCode = 0;
    }

    public void Dispose()
    {
        Console.SetOut(_savedOut);
        Console.SetError(_savedError);
        Environment.ExitCode = _savedExitCode;
    }

    private static (RevitClient client, FakeHttpHandler handler) CreateClientWithHandler(
        string? response = null, bool throwException = false)
    {
        var handler = new FakeHttpHandler(response, throwException);
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        return (client, handler);
    }

    private (StringWriter stdout, StringWriter stderr) CaptureConsole()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        return (stdout, stderr);
    }

    // ==========================================================
    // Parser rejection: required options missing
    // Proof: exitCode != 0, CallCount == 0, parser error text present
    // ==========================================================

    [Fact]
    public async Task Export_MissingRequiredFormat_ParserRejects()
    {
        var (client, handler) = CreateClientWithHandler();
        var command = ExportCommand.Create(client, new CliConfig());
        var (_, stderr) = CaptureConsole();

        var exitCode = await command.InvokeAsync(Array.Empty<string>());

        Assert.NotEqual(0, exitCode);
        Assert.Equal(0, handler.CallCount); // handler never executed
        Assert.Contains("--format", stderr.ToString()); // parser's own error mentions the option
    }

    [Fact]
    public async Task Set_MissingRequiredOptions_ParserRejects()
    {
        var (client, handler) = CreateClientWithHandler();
        var command = SetCommand.Create(client);
        var (_, stderr) = CaptureConsole();

        var exitCode = await command.InvokeAsync(new[] { "doors" });

        Assert.NotEqual(0, exitCode);
        Assert.Equal(0, handler.CallCount);
        var errText = stderr.ToString();
        Assert.Contains("--param", errText);
    }

    // ==========================================================
    // Type binding errors → parser rejects
    // ==========================================================

    [Fact]
    public async Task Query_InvalidIdType_ParserRejects()
    {
        var (client, handler) = CreateClientWithHandler();
        var command = QueryCommand.Create(client, new CliConfig());

        var exitCode = await command.InvokeAsync(new[] { "--id", "not_a_number" });

        Assert.NotEqual(0, exitCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Set_InvalidIdType_ParserRejects()
    {
        var (client, handler) = CreateClientWithHandler();
        var command = SetCommand.Create(client);

        var exitCode = await command.InvokeAsync(new[] { "doors", "--param", "Mark", "--value", "X", "--id", "abc" });

        Assert.NotEqual(0, exitCode);
        Assert.Equal(0, handler.CallCount);
    }

    // ==========================================================
    // Missing positional args
    // ==========================================================

    [Fact]
    public async Task Completions_MissingShellArg_ParserRejects()
    {
        var command = CompletionsCommand.Create();
        var (_, stderr) = CaptureConsole();

        var exitCode = await command.InvokeAsync(Array.Empty<string>());

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task Batch_MissingFileArg_ParserRejects()
    {
        var (client, _) = CreateClientWithHandler();
        var command = BatchCommand.Create(client, new CliConfig());

        var exitCode = await command.InvokeAsync(Array.Empty<string>());

        Assert.NotEqual(0, exitCode);
    }

    // ==========================================================
    // Unknown option → parser rejects
    // ==========================================================

    [Fact]
    public async Task Query_UnknownOption_ParserRejects()
    {
        var (client, handler) = CreateClientWithHandler();
        var command = QueryCommand.Create(client, new CliConfig());

        var exitCode = await command.InvokeAsync(new[] { "walls", "--nonexistent-flag" });

        Assert.NotEqual(0, exitCode);
        Assert.Equal(0, handler.CallCount);
    }

    // ==========================================================
    // Default values correctly bound (verified via output content)
    // ==========================================================

    [Fact]
    public async Task Query_DefaultOutput_UsesConfigValue()
    {
        var elements = new[] { new ElementInfo { Id = 1, Name = "Wall 1", Category = "Walls" } };
        var response = ApiResponse<ElementInfo[]>.Ok(elements);
        var (client, handler) = CreateClientWithHandler(JsonSerializer.Serialize(response));
        var config = new CliConfig { DefaultOutput = "json" };
        var command = QueryCommand.Create(client, config);
        var (stdout, _) = CaptureConsole();

        await command.InvokeAsync(new[] { "walls" });

        Assert.Equal(0, Environment.ExitCode);
        Assert.Equal(1, handler.CallCount);
        // JSON output proves config.DefaultOutput = "json" was bound by parser
        Assert.Contains("\"id\":", stdout.ToString());
    }

    [Fact]
    public async Task Set_DryRunFlag_PassedInRequestBody()
    {
        var result = new SetResult { Affected = 0 };
        var response = ApiResponse<SetResult>.Ok(result);
        var (client, handler) = CreateClientWithHandler(JsonSerializer.Serialize(response));
        var command = SetCommand.Create(client);
        CaptureConsole();

        await command.InvokeAsync(new[] { "doors", "--param", "Mark", "--value", "D-01", "--dry-run" });

        Assert.Equal(0, Environment.ExitCode);
        Assert.Equal(1, handler.CallCount);
        // Verify the request body actually contains dryRun:true
        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"dryRun\":true", handler.LastRequestBody!.Replace(" ", ""));
    }

    // ==========================================================
    // Handler wiring: correct endpoints called
    // ==========================================================

    [Fact]
    public async Task Status_CallsStatusEndpoint()
    {
        var status = new StatusInfo { RevitVersion = "2025", DocumentName = "Test.rvt" };
        var response = ApiResponse<StatusInfo>.Ok(status);
        var (client, handler) = CreateClientWithHandler(JsonSerializer.Serialize(response));
        var command = StatusCommand.Create(client);
        CaptureConsole();

        await command.InvokeAsync(Array.Empty<string>());

        Assert.Equal(0, Environment.ExitCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains("/api/status", handler.LastRequestUri!);
    }

    [Fact]
    public async Task Query_WithId_CallsElementByIdEndpoint()
    {
        var element = new ElementInfo { Id = 42, Name = "Element 42" };
        var response = ApiResponse<ElementInfo>.Ok(element);
        var (client, handler) = CreateClientWithHandler(JsonSerializer.Serialize(response));
        var command = QueryCommand.Create(client, new CliConfig());
        CaptureConsole();

        await command.InvokeAsync(new[] { "--id", "42" });

        Assert.Equal(0, Environment.ExitCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains("/api/elements/42", handler.LastRequestUri!);
    }

    [Fact]
    public async Task Query_WithCategory_CallsElementsEndpoint()
    {
        var elements = Array.Empty<ElementInfo>();
        var response = ApiResponse<ElementInfo[]>.Ok(elements);
        var (client, handler) = CreateClientWithHandler(JsonSerializer.Serialize(response));
        var command = QueryCommand.Create(client, new CliConfig());
        CaptureConsole();

        await command.InvokeAsync(new[] { "walls" });

        Assert.Equal(0, Environment.ExitCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains("category=walls", handler.LastRequestUri!);
    }

    [Fact]
    public async Task Export_WithFormat_CallsExportEndpoint()
    {
        var exportDir = Path.Combine(Path.GetTempPath(), "revitcli-parser-export-" + Guid.NewGuid().ToString("N"));
        var progress = new ExportProgress { TaskId = "t1", Status = "completed", Progress = 100 };
        var response = ApiResponse<ExportProgress>.Ok(progress);
        var (client, handler) = CreateClientWithHandler(JsonSerializer.Serialize(response));
        var command = ExportCommand.Create(client, new CliConfig { ExportDir = exportDir });
        CaptureConsole();

        try
        {
            await command.InvokeAsync(new[] { "--format", "dwg" });
        }
        finally
        {
            if (Directory.Exists(exportDir))
                Directory.Delete(exportDir, recursive: true);
        }

        Assert.Equal(0, Environment.ExitCode);
        Assert.Equal(2, handler.CallCount);
        Assert.Contains(handler.RequestUris, uri => uri.Contains("/api/export", StringComparison.Ordinal));
        Assert.Contains(handler.RequestUris, uri => uri.Contains("/api/status", StringComparison.Ordinal));
    }

    // ==========================================================
    // Error propagation: server down → exit code 1
    // ==========================================================

    [Fact]
    public async Task Status_ServerDown_SetsExitCode1()
    {
        var (client, handler) = CreateClientWithHandler(throwException: true);
        var command = StatusCommand.Create(client);
        CaptureConsole();

        await command.InvokeAsync(Array.Empty<string>());

        Assert.Equal(1, Environment.ExitCode);
        Assert.Equal(1, handler.CallCount); // handler DID execute, just got connection error
    }
}
