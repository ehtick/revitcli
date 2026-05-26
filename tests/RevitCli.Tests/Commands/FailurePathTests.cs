using System;
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

public class FailurePathTests
{
    private static RevitClient CreateClient(string? response = null, bool throwException = false)
    {
        var handler = new FakeHttpHandler(response, throwException);
        return new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
    }

    // === Query Command ===

    [Fact]
    public async Task Query_NoCategoryNoId_ReturnsError()
    {
        var client = CreateClient();
        var writer = new StringWriter();
        var exitCode = await QueryCommand.ExecuteAsync(client, null, null, null, "table", writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("category", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Query_ServerDown_ReturnsError()
    {
        var client = CreateClient(throwException: true);
        var writer = new StringWriter();
        var exitCode = await QueryCommand.ExecuteAsync(client, "walls", null, null, "table", writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("not running", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Query_EmptyStringCategory_ReturnsError()
    {
        var client = CreateClient();
        var writer = new StringWriter();
        var exitCode = await QueryCommand.ExecuteAsync(client, "", null, null, "table", writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("category", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Query_InvalidOutputFormat_FallsBackToTable()
    {
        var elements = new[] { new ElementInfo { Id = 1, Name = "Wall 1", Category = "Walls" } };
        var response = ApiResponse<ElementInfo[]>.Ok(elements);
        var client = CreateClient(JsonSerializer.Serialize(response));
        var writer = new StringWriter();
        var exitCode = await QueryCommand.ExecuteAsync(client, "walls", null, null, "invalid_format", writer);
        // Should succeed — invalid format falls back to table
        Assert.Equal(0, exitCode);
        Assert.Contains("Wall 1", writer.ToString());
    }

    // === Set Command ===

    [Fact]
    public async Task Set_NullParam_ReturnsError()
    {
        var client = CreateClient();
        var writer = new StringWriter();
        var exitCode = await SetCommand.ExecuteAsync(client, "doors", null, null, null!, "60min", false, false, false, null, writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("--param", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Set_EmptyParam_ReturnsError()
    {
        var client = CreateClient();
        var writer = new StringWriter();
        var exitCode = await SetCommand.ExecuteAsync(client, "doors", null, null, "", "60min", false, false, false, null, writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("--param", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Set_NoCategoryNoId_ReturnsError()
    {
        var client = CreateClient();
        var writer = new StringWriter();
        var exitCode = await SetCommand.ExecuteAsync(client, null, null, null, "Mark", "W-01", false, false, false, null, writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("category", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Set_ServerDown_ReturnsError()
    {
        var client = CreateClient(throwException: true);
        var writer = new StringWriter();
        var exitCode = await SetCommand.ExecuteAsync(client, "doors", null, null, "Mark", "W-01", false, true, false, null, writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("not running", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Set_WithIdOnly_Succeeds()
    {
        var result = new SetResult { Affected = 1 };
        var response = ApiResponse<SetResult>.Ok(result);
        var client = CreateClient(JsonSerializer.Serialize(response));
        var writer = new StringWriter();
        var exitCode = await SetCommand.ExecuteAsync(client, null, null, 123, "Mark", "W-01", false, true, false, null, writer);
        Assert.Equal(0, exitCode);
        Assert.Contains("Modified 1", writer.ToString());
    }

    // === Export Command ===

    [Fact]
    public async Task Export_NullFormat_ReturnsError()
    {
        var client = CreateClient();
        var writer = new StringWriter();
        var exitCode = await ExportCommand.ExecuteAsync(client, null!, Array.Empty<string>(), Array.Empty<string>(), ".", writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("--format", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Export_InvalidFormat_ReturnsError()
    {
        var client = CreateClient();
        var writer = new StringWriter();
        var exitCode = await ExportCommand.ExecuteAsync(client, "xlsx", Array.Empty<string>(), Array.Empty<string>(), ".", writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("dwg", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Export_ServerDown_ReturnsError()
    {
        var client = CreateClient(throwException: true);
        var writer = new StringWriter();
        var exitCode = await ExportCommand.ExecuteAsync(client, "dwg", Array.Empty<string>(), Array.Empty<string>(), ".", writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("not running", writer.ToString().ToLower());
    }

    // === Status Command ===

    [Fact]
    public async Task Status_ServerDown_ReturnsExitCode1()
    {
        var client = CreateClient(throwException: true);
        var writer = new StringWriter();
        var exitCode = await StatusCommand.ExecuteAsync(client, writer);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Status_NoDocument_ShowsNoneOpen()
    {
        var status = new StatusInfo { RevitVersion = "2025", DocumentName = null };
        var response = ApiResponse<StatusInfo>.Ok(status);
        var client = CreateClient(JsonSerializer.Serialize(response));
        var writer = new StringWriter();
        var exitCode = await StatusCommand.ExecuteAsync(client, writer);
        Assert.Equal(0, exitCode);
        Assert.Contains("none open", writer.ToString().ToLower());
    }

    // === Audit Command ===

    [Fact]
    public async Task Audit_UnknownRule_ReturnsError()
    {
        var client = CreateClient();
        var writer = new StringWriter();
        var exitCode = await AuditCommand.ExecuteAsync(client, "nonexistent_rule", writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("unknown rule", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Audit_ServerDown_ReturnsError()
    {
        var client = CreateClient(throwException: true);
        var writer = new StringWriter();
        var exitCode = await AuditCommand.ExecuteAsync(client, "naming", writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("not running", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Audit_MultipleRulesWithOneInvalid_ReturnsError()
    {
        var client = CreateClient();
        var writer = new StringWriter();
        var exitCode = await AuditCommand.ExecuteAsync(client, "naming,bogus", writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("bogus", writer.ToString().ToLower());
    }

    // === Doctor Command ===

    [Fact]
    public async Task Doctor_ServerDown_ReturnsExitCode1()
    {
        var client = CreateClient(throwException: true);
        var config = new CliConfig();
        var writer = new StringWriter();
        var exitCode = await DoctorCommand.ExecuteAsync(client, config, writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("FAIL", writer.ToString());
    }

    // === ElementFilter edge cases ===

    [Fact]
    public void Filter_OperatorOnly_ReturnsNull()
    {
        var filter = RevitCli.Shared.ElementFilter.Parse("=");
        Assert.Null(filter);
    }

    [Fact]
    public void Filter_ValueOnly_ReturnsNull()
    {
        var filter = RevitCli.Shared.ElementFilter.Parse("3000");
        Assert.Null(filter);
    }

    [Fact]
    public void Filter_WhitespaceOnly_ReturnsNull()
    {
        var filter = RevitCli.Shared.ElementFilter.Parse("   ");
        Assert.Null(filter);
    }
}
