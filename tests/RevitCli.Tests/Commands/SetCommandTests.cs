using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Plans;
using RevitCli.Shared;
using RevitCli.Tests.Client;
using Xunit;

namespace RevitCli.Tests.Commands;

public class SetCommandTests
{
    [Fact]
    public async Task Execute_DryRun_PrintsPreview()
    {
        var setResult = new SetResult
        {
            Affected = 2,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 100, Name = "Door 1", OldValue = "30min", NewValue = "60min" },
                new() { Id = 200, Name = "Door 2", OldValue = "30min", NewValue = "60min" }
            }
        };
        var response = ApiResponse<SetResult>.Ok(setResult);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await SetCommand.ExecuteAsync(client, "doors", null, null, "Fire Rating", "60min", true, false, false, null, writer);

        var output = writer.ToString();
        Assert.Contains("2 element(s)", output);
        Assert.Contains("Door 1", output);
        Assert.Contains("30min", output);
        Assert.Contains("60min", output);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Execute_PlanOutput_WritesFrozenSetPlan()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"set_plan_{Guid.NewGuid():N}.json");
        try
        {
            var setResult = new SetResult
            {
                Affected = 2,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 200, Name = "Door 2", OldValue = "30min", NewValue = "60min" },
                    new() { Id = 100, Name = "Door 1", OldValue = "30min", NewValue = "60min" }
                }
            };
            var response = ApiResponse<SetResult>.Ok(setResult);
            var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
            var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
            var writer = new StringWriter();

            var exitCode = await SetCommand.ExecuteAsync(
                client,
                "doors",
                "name contains Fire",
                null,
                "Fire Rating",
                "60min",
                dryRun: false,
                yes: false,
                fromStdin: false,
                idsFromFile: null,
                writer,
                planOutputPath: tmpFile);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(tmpFile));
            Assert.Contains("\"dryRun\":true", handler.LastRequestBody);
            Assert.Contains("Plan written", writer.ToString());
            Assert.Contains("revitcli plan apply", writer.ToString());

            var plan = SetPlanFileStore.Load(tmpFile);
            Assert.Equal("set", plan.Type);
            Assert.Equal(2, plan.Summary.Affected);
            Assert.Equal("category=doors, filter=name contains Fire", plan.Summary.OriginalTarget);
            Assert.Equal(new[] { 100L, 200L }, plan.ApplyRequest.ElementIds);
            Assert.Null(plan.ApplyRequest.Category);
            Assert.Null(plan.ApplyRequest.Filter);
            Assert.Equal("Fire Rating", plan.ApplyRequest.Param);
            Assert.False(plan.ApplyRequest.DryRun);
        }
        finally
        {
            if (File.Exists(tmpFile))
                File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task Execute_MissingParam_PrintsError()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await SetCommand.ExecuteAsync(client, "doors", null, null, null!, "60min", false, false, false, null, writer);

        Assert.Contains("--param", writer.ToString().ToLower());
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Execute_MissingValueWithoutClear_PrintsError()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await SetCommand.ExecuteAsync(client, "doors", null, null, "Comments", null, false, false, false, null, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--value is required", writer.ToString());
    }

    [Fact]
    public async Task Execute_ApplyWithoutYes_PrintsErrorBeforeRequest()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await SetCommand.ExecuteAsync(client, "doors", null, null, "Comments", "Reviewed", false, false, false, null, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--yes", writer.ToString());
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Invoke_MissingValueWithoutClear_PrintsError()
    {
        var savedOut = Console.Out;
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        try
        {
            Environment.ExitCode = 0;
            Console.SetOut(writer);
            await SetCommand.Create(client).InvokeAsync(new[] { "doors", "--param", "Comments" });

            Assert.Equal(1, Environment.ExitCode);
            Assert.Contains("--value is required", writer.ToString());
            Assert.Null(handler.LastRequestBody);
        }
        finally
        {
            Console.SetOut(savedOut);
            Environment.ExitCode = 0;
        }
    }

    [Fact]
    public async Task Execute_ClearValue_SendsEmptyString()
    {
        var auditDir = Path.Combine(Path.GetTempPath(), $"set_audit_{Guid.NewGuid():N}");
        var setResult = new SetResult { Affected = 1 };
        var response = ApiResponse<SetResult>.Ok(setResult);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        try
        {
            var exitCode = await SetCommand.ExecuteAsync(
                client,
                category: null,
                filter: null,
                id: 337596,
                param: "Comments",
                value: null,
                dryRun: false,
                yes: true,
                fromStdin: false,
                idsFromFile: null,
                writer,
                clearValue: true,
                auditDirectory: auditDir);

            Assert.Equal(0, exitCode);
            Assert.Contains("\"value\":\"\"", handler.LastRequestBody);
            Assert.Contains("Modified 1 element", writer.ToString());

            var ledgerPath = Path.Combine(auditDir, ".revitcli", "ledger", "operations.jsonl");
            var line = Assert.Single(File.ReadAllLines(ledgerPath));
            using var json = JsonDocument.Parse(line);
            var record = json.RootElement;
            Assert.Contains(record.GetProperty("args").EnumerateArray(), arg => arg.GetString() == "--clear-value");
            Assert.Equal(new[] { 337596L }, record.GetProperty("affectedElementIds").EnumerateArray().Select(id => id.GetInt64()).ToArray());
        }
        finally
        {
            if (Directory.Exists(auditDir))
                Directory.Delete(auditDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_ApprovedApply_AppendsSetLedgerRecord()
    {
        var auditDir = Path.Combine(Path.GetTempPath(), $"set_ledger_{Guid.NewGuid():N}");
        var setResult = new SetResult
        {
            Affected = 2,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 200, Name = "Wall 2", OldValue = "", NewValue = "Reviewed" },
                new() { Id = 100, Name = "Wall 1", OldValue = "", NewValue = "Reviewed" }
            }
        };
        var response = ApiResponse<SetResult>.Ok(setResult);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        try
        {
            var exitCode = await SetCommand.ExecuteAsync(
                client,
                "walls",
                "标记 = TEST",
                null,
                "注释",
                "Reviewed",
                dryRun: false,
                yes: true,
                fromStdin: false,
                idsFromFile: null,
                writer,
                auditDirectory: auditDir);

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain("Ledger", writer.ToString());

            var ledgerPath = Path.Combine(auditDir, ".revitcli", "ledger", "operations.jsonl");
            var line = Assert.Single(File.ReadAllLines(ledgerPath));
            using var json = JsonDocument.Parse(line);
            var record = json.RootElement;
            Assert.Equal("ledger-operation.v1", record.GetProperty("schemaVersion").GetString());
            Assert.Equal("set", record.GetProperty("command").GetString());
            Assert.Equal("set", record.GetProperty("action").GetString());
            Assert.Equal("walls", record.GetProperty("category").GetString());
            Assert.Equal("succeeded", record.GetProperty("status").GetString());
            Assert.Equal(2, record.GetProperty("affectedElementCount").GetInt32());
            Assert.Equal(new[] { 100L, 200L }, record.GetProperty("affectedElementIds").EnumerateArray().Select(id => id.GetInt64()).ToArray());
            Assert.Contains(record.GetProperty("args").EnumerateArray(), arg => arg.GetString() == "--value");
            Assert.Contains(record.GetProperty("args").EnumerateArray(), arg => arg.GetString() == "Reviewed");
            Assert.Contains(record.GetProperty("args").EnumerateArray(), arg => arg.GetString() == "--yes");
        }
        finally
        {
            if (Directory.Exists(auditDir))
                Directory.Delete(auditDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_ApprovedApply_AppendsLiveModelIdentityWhenStatusIsAvailable()
    {
        var auditDir = Path.Combine(Path.GetTempPath(), $"set_ledger_model_{Guid.NewGuid():N}");
        var setResult = new SetResult
        {
            Affected = 1,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 337596, Name = "Wall 1", OldValue = "", NewValue = "Reviewed" }
            }
        };
        var documentPath = Path.Combine(auditDir, "model", "revit_cli.rvt");
        var status = new StatusInfo
        {
            RevitYear = 2026,
            RevitVersion = "2026",
            AddinVersion = "2.3.0",
            DocumentName = "revit_cli.rvt",
            DocumentPath = documentPath,
        };
        var handler = new QueuedHttpHandler(
            JsonSerializer.Serialize(ApiResponse<SetResult>.Ok(setResult)),
            JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(status)));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });

        try
        {
            var exitCode = await SetCommand.ExecuteAsync(
                client,
                "walls",
                "标记 = TEST",
                null,
                "注释",
                "Reviewed",
                dryRun: false,
                yes: true,
                fromStdin: false,
                idsFromFile: null,
                new StringWriter(),
                auditDirectory: auditDir);

            Assert.Equal(0, exitCode);
            Assert.Equal(new[] { "/api/elements/set", "/api/status" }, handler.RequestPaths);

            var ledgerPath = Path.Combine(auditDir, ".revitcli", "ledger", "operations.jsonl");
            var line = Assert.Single(File.ReadAllLines(ledgerPath));
            using var json = JsonDocument.Parse(line);
            var record = json.RootElement;
            Assert.Equal("revit_cli.rvt", record.GetProperty("modelIdentity").GetString());
            Assert.Equal(documentPath, record.GetProperty("modelPath").GetString());
            Assert.Equal("2026", record.GetProperty("revitVersion").GetString());
        }
        finally
        {
            if (Directory.Exists(auditDir))
                Directory.Delete(auditDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_ApprovedApply_AppendsLedgerRecordWhenStatusIsUnavailable()
    {
        var auditDir = Path.Combine(Path.GetTempPath(), $"set_ledger_no_model_{Guid.NewGuid():N}");
        var setResult = new SetResult
        {
            Affected = 1,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 337596, Name = "Wall 1", OldValue = "", NewValue = "Reviewed" }
            }
        };
        var handler = new QueuedHttpHandler(
            JsonSerializer.Serialize(ApiResponse<SetResult>.Ok(setResult)),
            JsonSerializer.Serialize(ApiResponse<StatusInfo>.Fail("status unavailable")));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });

        try
        {
            var exitCode = await SetCommand.ExecuteAsync(
                client,
                "walls",
                "标记 = TEST",
                null,
                "注释",
                "Reviewed",
                dryRun: false,
                yes: true,
                fromStdin: false,
                idsFromFile: null,
                new StringWriter(),
                auditDirectory: auditDir);

            Assert.Equal(0, exitCode);
            Assert.Equal(new[] { "/api/elements/set", "/api/status" }, handler.RequestPaths);

            var ledgerPath = Path.Combine(auditDir, ".revitcli", "ledger", "operations.jsonl");
            var line = Assert.Single(File.ReadAllLines(ledgerPath));
            using var json = JsonDocument.Parse(line);
            var record = json.RootElement;
            Assert.Equal("succeeded", record.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, record.GetProperty("modelIdentity").ValueKind);
            Assert.Equal(JsonValueKind.Null, record.GetProperty("modelPath").ValueKind);
            Assert.Equal(JsonValueKind.Null, record.GetProperty("revitVersion").ValueKind);
        }
        finally
        {
            if (Directory.Exists(auditDir))
                Directory.Delete(auditDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_DryRunAndPlanOutput_DoNotAppendSetLedgerRecord()
    {
        var auditDir = Path.Combine(Path.GetTempPath(), $"set_no_ledger_{Guid.NewGuid():N}");
        var planPath = Path.Combine(auditDir, "set-plan.json");
        var setResult = new SetResult
        {
            Affected = 1,
            Preview = new List<SetPreviewItem>
            {
                new() { Id = 100, Name = "Wall 1", OldValue = "", NewValue = "Reviewed" }
            }
        };

        try
        {
            Directory.CreateDirectory(auditDir);
            var dryRunHandler = new FakeHttpHandler(JsonSerializer.Serialize(ApiResponse<SetResult>.Ok(setResult)));
            var dryRunClient = new RevitClient(new HttpClient(dryRunHandler) { BaseAddress = new System.Uri("http://localhost:17839") });
            var dryRunExitCode = await SetCommand.ExecuteAsync(
                dryRunClient,
                "walls",
                null,
                null,
                "Comments",
                "Reviewed",
                dryRun: true,
                yes: false,
                fromStdin: false,
                idsFromFile: null,
                new StringWriter(),
                auditDirectory: auditDir);

            var planHandler = new FakeHttpHandler(JsonSerializer.Serialize(ApiResponse<SetResult>.Ok(setResult)));
            var planClient = new RevitClient(new HttpClient(planHandler) { BaseAddress = new System.Uri("http://localhost:17839") });
            var planExitCode = await SetCommand.ExecuteAsync(
                planClient,
                "walls",
                null,
                null,
                "Comments",
                "Reviewed",
                dryRun: false,
                yes: false,
                fromStdin: false,
                idsFromFile: null,
                new StringWriter(),
                planOutputPath: planPath,
                auditDirectory: auditDir);

            Assert.Equal(0, dryRunExitCode);
            Assert.Equal(0, planExitCode);
            Assert.False(File.Exists(Path.Combine(auditDir, ".revitcli", "ledger", "operations.jsonl")));
        }
        finally
        {
            if (Directory.Exists(auditDir))
                Directory.Delete(auditDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_ValueAndClearValue_PrintsError()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await SetCommand.ExecuteAsync(
            client,
            "doors",
            null,
            null,
            "Comments",
            "x",
            false,
            false,
            false,
            null,
            writer,
            clearValue: true);

        Assert.Equal(1, exitCode);
        Assert.Contains("cannot be combined", writer.ToString());
    }

    [Fact]
    public void SetRequest_ElementIds_SerializesCorrectly()
    {
        var request = new SetRequest
        {
            Param = "Mark",
            Value = "TEST",
            ElementIds = new List<long> { 337596, 337601 }
        };

        var json = JsonSerializer.Serialize(request);
        Assert.Contains("\"elementIds\":[337596,337601]", json);

        var deserialized = JsonSerializer.Deserialize<SetRequest>(json);
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized!.ElementIds);
        Assert.Equal(2, deserialized.ElementIds!.Count);
        Assert.Equal(337596, deserialized.ElementIds[0]);
        Assert.Equal(337601, deserialized.ElementIds[1]);

        // Category and ElementId should be null
        Assert.Null(deserialized.Category);
        Assert.Null(deserialized.ElementId);
    }

    [Fact]
    public async Task IdsFrom_JsonArray_WorksEndToEnd()
    {
        // Write query-style JSON to temp file
        var tmpFile = Path.Combine(Path.GetTempPath(), $"ids_test_{Guid.NewGuid():N}.json");
        File.WriteAllText(tmpFile, @"[{""id"": 337596, ""name"": ""Wall 1""}, {""id"": 337601, ""name"": ""Wall 2""}]");

        var setResult = new SetResult { Affected = 2, Preview = new List<SetPreviewItem>
        {
            new() { Id = 337596, Name = "Wall 1", OldValue = "", NewValue = "TEST" },
            new() { Id = 337601, Name = "Wall 2", OldValue = "", NewValue = "TEST" }
        }};
        var response = ApiResponse<SetResult>.Ok(setResult);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await SetCommand.ExecuteAsync(client, null, null, null, "Mark", "TEST", true, false, false, tmpFile, writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("2 element(s)", writer.ToString());
        // Verify the request body contains elementIds
        Assert.Contains("337596", handler.LastRequestBody);
        Assert.Contains("337601", handler.LastRequestBody);
        File.Delete(tmpFile);
    }

    [Fact]
    public async Task IdsFrom_PlainText_WorksEndToEnd()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"ids_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmpFile, "100\n200\n300\n");

        var setResult = new SetResult { Affected = 3 };
        var response = ApiResponse<SetResult>.Ok(setResult);
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var auditDir = Path.Combine(Path.GetTempPath(), $"set_audit_{Guid.NewGuid():N}");
        try
        {
            var exitCode = await SetCommand.ExecuteAsync(
                client,
                null,
                null,
                null,
                "Mark",
                "X",
                false,
                true,
                false,
                tmpFile,
                writer,
                auditDirectory: auditDir);
            Assert.Equal(0, exitCode);
            Assert.Contains("100", handler.LastRequestBody);
        }
        finally
        {
            File.Delete(tmpFile);
            if (Directory.Exists(auditDir))
                Directory.Delete(auditDir, recursive: true);
        }
    }

    [Fact]
    public async Task IdsFrom_MalformedItem_ReturnsError()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"ids_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmpFile, "100\nnot_a_number\n300\n");

        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await SetCommand.ExecuteAsync(client, null, null, null, "Mark", "X", false, true, false, tmpFile, writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("not_a_number", writer.ToString());
        File.Delete(tmpFile);
    }

    [Fact]
    public async Task IdsFrom_MixedWithCategory_ReturnsError()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"ids_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmpFile, "100\n");

        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        // --ids-from + category should be rejected
        var exitCode = await SetCommand.ExecuteAsync(client, "walls", null, null, "Mark", "X", false, false, false, tmpFile, writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("cannot be combined", writer.ToString().ToLower());
        File.Delete(tmpFile);
    }

    [Fact]
    public async Task Execute_NoTarget_PrintsError()
    {
        var handler = new FakeHttpHandler("{}");
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:17839") });
        var writer = new StringWriter();

        var exitCode = await SetCommand.ExecuteAsync(client, null, null, null, "Mark", "W-01", false, false, false, null, writer);

        Assert.Contains("category", writer.ToString().ToLower());
        Assert.Equal(1, exitCode);
    }
}

public sealed class QueuedHttpHandler : HttpMessageHandler
{
    private readonly Queue<string> _responses;

    public QueuedHttpHandler(params string[] responses)
    {
        _responses = new Queue<string>(responses);
    }

    public List<string> RequestPaths { get; } = new();

    public List<string> RequestBodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestPaths.Add(request.RequestUri?.AbsolutePath ?? "");
        if (request.Content != null)
            RequestBodies.Add(await request.Content.ReadAsStringAsync());

        var response = _responses.Count == 0 ? "{}" : _responses.Dequeue();
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(response)
        };
    }
}
