using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Globalization;
using System.CommandLine.Parsing;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.History;
using RevitCli.Output;
using RevitCli.Shared;
using RevitCli.Tests.Client;

namespace RevitCli.Tests.Commands;

[Collection("Sequential")]
public sealed class LedgerCommandTests : IDisposable
{
    private readonly string _root;
    private readonly DateTimeOffset _now = new(2026, 5, 23, 12, 0, 0, TimeSpan.Zero);

    public LedgerCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-ledger-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string GetOperationsLedgerPath() => Path.Combine(_root, ".revitcli", "ledger", "operations.jsonl");

    private async Task SeedReplayableSetOperationAsync()
    {
        var exitCode = await LedgerCommand.ExecuteAppendAsync(
            _root,
            action: "set",
            category: "walls",
            operatorName: "alice",
            status: "succeeded",
            summary: "Set 注释 on 2 element(s)",
            timestamp: _now.ToString("o"),
            model: null,
            modelPath: null,
            planHash: null,
            artifactPath: null,
            receiptPath: null,
            receiptHash: null,
            rollbackPointer: null,
            evidenceLinks: Array.Empty<string>(),
            yes: true,
            outputFormat: "json",
            new StringWriter(),
            _now,
            commandName: "set",
            commandArgs: new[] { "set", "walls", "--filter", "标记 = TEST", "--param", "注释", "--value", "Reviewed", "--yes" },
            affectedElementCount: 2,
            affectedElementIds: new[] { 200L, 100L });
        Assert.Equal(0, exitCode);
    }

    private async Task SeedReplayableExportOperationAsync(IReadOnlyList<string>? commandArgs = null, bool receiptBacked = true)
    {
        var outputDir = Path.Combine(_root, "exports");
        var receiptPath = receiptBacked ? Path.Combine(outputDir, ".revitcli", "receipts", "export.json") : null;
        if (receiptPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            File.WriteAllText(receiptPath, "{\"schemaVersion\":\"export-receipt.v1\"}");
        }
        var exitCode = await LedgerCommand.ExecuteAppendAsync(
            _root,
            action: "export",
            category: "pdf",
            operatorName: "alice",
            status: "succeeded",
            summary: "Export pdf completed",
            timestamp: _now.ToString("o"),
            model: null,
            modelPath: null,
            planHash: null,
            artifactPath: outputDir,
            receiptPath: receiptPath,
            receiptHash: null,
            rollbackPointer: null,
            evidenceLinks: Array.Empty<string>(),
            yes: true,
            outputFormat: "json",
            new StringWriter(),
            _now,
            commandName: "export",
            commandArgs: commandArgs ?? new[] { "export", "--format", "pdf", "--sheets", "A101", "--output-dir", outputDir },
            affectedElementCount: null,
            affectedElementIds: Array.Empty<long>());
        Assert.Equal(0, exitCode);
    }

    private async Task<string> SeedReplayableScheduleBatchExportOperationAsync(
        IReadOnlyList<string>? commandArgs = null,
        bool writeManifest = true,
        string? entryOutputPath = null)
    {
        var outputDir = Path.Combine(_root, "schedule-exports");
        var manifestPath = Path.Combine(outputDir, "manifest.json");
        Directory.CreateDirectory(outputDir);
        if (writeManifest)
        {
            var csvPath = entryOutputPath ?? Path.Combine(outputDir, "Door Schedule.csv");
            var manifest = new
            {
                schemaVersion = "schedule-export-manifest.v1",
                entries = new[]
                {
                    new
                    {
                        scheduleName = "Door Schedule",
                        category = "Doors",
                        outputPath = csvPath,
                        success = true,
                    },
                },
            };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
        }

        var exitCode = await LedgerCommand.ExecuteAppendAsync(
            _root,
            action: "schedules.batch-export",
            category: "csv",
            operatorName: "alice",
            status: "succeeded",
            summary: "Batch-exported 1 schedule file(s) for set issue",
            timestamp: _now.ToString("o"),
            model: null,
            modelPath: null,
            planHash: null,
            artifactPath: manifestPath,
            receiptPath: null,
            receiptHash: null,
            rollbackPointer: null,
            evidenceLinks: writeManifest ? new[] { manifestPath } : Array.Empty<string>(),
            yes: true,
            outputFormat: "json",
            new StringWriter(),
            _now,
            commandName: "schedules",
            commandArgs: commandArgs ?? new[] { "schedules", "batch-export", "--set", "issue", "--output-dir", outputDir, "--format", "csv", "--manifest", manifestPath },
            affectedElementCount: null,
            affectedElementIds: Array.Empty<long>());
        Assert.Equal(0, exitCode);
        return manifestPath;
    }

    [Fact]
    public async Task Append_DryRun_DoesNotWriteRecord()
    {
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteAppendAsync(
            _root,
            action: "issue.package",
            category: "issue",
            operatorName: "alice",
            status: "succeeded",
            summary: "Package issue deliverables",
            timestamp: _now.ToString("o"),
            model: "sample.rvt",
            modelPath: null,
            planHash: "plan-123",
            artifactPath: "out/package.zip",
            receiptPath: null,
            receiptHash: null,
            rollbackPointer: "revitcli rollback receipt.json",
            evidenceLinks: Array.Empty<string>(),
            yes: false,
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(GetOperationsLedgerPath()));
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("ledger-append.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("dryRun").GetBoolean());
        Assert.False(root.GetProperty("written").GetBoolean());

        var operation = root.GetProperty("operation");
        Assert.Equal("ledger", operation.GetProperty("source").GetString());
        Assert.Equal("issue.package", operation.GetProperty("action").GetString());
        Assert.Equal("succeeded", operation.GetProperty("status").GetString());
        Assert.Equal("sample.rvt", operation.GetProperty("modelIdentity").GetString());
        Assert.Equal("revitcli rollback receipt.json", operation.GetProperty("rollbackPointer").GetString());
    }

    [Fact]
    public async Task Append_WithYes_WritesRecord_And_QueryCanRead()
    {
        var receiptDir = Path.Combine(_root, ".revitcli", "receipts");
        Directory.CreateDirectory(receiptDir);
        var receiptPath = Path.Combine(receiptDir, "issue-package.json");
        File.WriteAllText(receiptPath, "{\"schemaVersion\":\"publish-receipt.v1\",\"success\":true}");
        var receiptHash = DeliveryManifestWriter.ComputeSha256Hex(receiptPath);
        var output = new StringWriter();

        var appendExitCode = await LedgerCommand.ExecuteAppendAsync(
            _root,
            action: "issue.package",
            category: "issue",
            operatorName: "alice",
            status: "succeeded",
            summary: "Package issue deliverables",
            timestamp: _now.ToString("o"),
            model: "sample.rvt",
            modelPath: "models/sample.rvt",
            planHash: "plan-123",
            artifactPath: "out/package.zip",
            receiptPath: receiptPath,
            receiptHash: receiptHash,
            rollbackPointer: "revitcli rollback issue-package.json",
            evidenceLinks: new[] { receiptPath, "out/package.zip", receiptPath },
            yes: true,
            outputFormat: "json",
            output,
            _now,
            revitVersion: "2026");

        Assert.Equal(0, appendExitCode);
        Assert.True(File.Exists(GetOperationsLedgerPath()));
        var ledgerLines = File.ReadAllLines(GetOperationsLedgerPath());
        var ledgerLine = Assert.Single(ledgerLines);
        using var recordJson = JsonDocument.Parse(ledgerLine);
        var record = recordJson.RootElement;
        Assert.Equal("ledger-operation.v1", record.GetProperty("schemaVersion").GetString());
        Assert.StartsWith("ledger-", record.GetProperty("operationId").GetString());
        Assert.Equal("ledger append", record.GetProperty("command").GetString());
        Assert.Contains(record.GetProperty("args").EnumerateArray(), arg => arg.GetString() == "--yes");
        Assert.Equal(_root, record.GetProperty("workingDirectory").GetString());
        Assert.True(record.TryGetProperty("profile", out _));
        Assert.Equal("sample.rvt", record.GetProperty("modelIdentity").GetString());
        Assert.Equal("2026", record.GetProperty("revitVersion").GetString());
        Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("machine").GetString()));
        Assert.Equal(_now.ToString("o"), record.GetProperty("startedAtUtc").GetString());
        Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("endedAtUtc").GetString()));
        Assert.Equal("local-write", record.GetProperty("riskLevel").GetString());
        Assert.True(record.GetProperty("dryRunRequired").GetBoolean());
        Assert.True(record.GetProperty("approvalRequired").GetBoolean());
        Assert.True(record.TryGetProperty("planPath", out _));
        Assert.Equal("plan-123", record.GetProperty("planHash").GetString());
        Assert.Equal(receiptPath, record.GetProperty("receiptPath").GetString());
        Assert.Equal(receiptHash, record.GetProperty("receiptHash").GetString());
        Assert.EndsWith(Path.Combine(".revitcli", "journal.jsonl"), record.GetProperty("journalPath").GetString());
        Assert.Equal("revitcli rollback issue-package.json", record.GetProperty("rollbackPointer").GetString());
        Assert.Contains(record.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "approval-required" &&
            check.GetProperty("status").GetString() == "pass");
        Assert.Contains(record.GetProperty("artifacts").EnumerateArray(), artifact =>
            artifact.GetProperty("role").GetString() == "receipt" &&
            artifact.GetProperty("sha256").GetString() == receiptHash);

        var queryOutput = new StringWriter();
        var queryExitCode = await LedgerCommand.ExecuteQueryAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 100,
            outputFormat: "json",
            queryOutput,
            _now);

        Assert.Equal(0, queryExitCode);
        using var json = JsonDocument.Parse(queryOutput.ToString());
        var operation = Assert.Single(json.RootElement.GetProperty("operations").EnumerateArray());
        Assert.Equal("ledger", operation.GetProperty("source").GetString());
        Assert.Equal(1, operation.GetProperty("line").GetInt32());
        Assert.Equal("issue.package", operation.GetProperty("action").GetString());
        Assert.Equal("2026", operation.GetProperty("revitVersion").GetString());
        Assert.Equal("valid", operation.GetProperty("receiptStatus").GetString());
        Assert.Equal(receiptHash, operation.GetProperty("receiptHash").GetString());
        Assert.Equal("revitcli rollback issue-package.json", operation.GetProperty("rollbackPointer").GetString());

        var evidenceLinks = operation.GetProperty("evidenceLinks").EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Equal(evidenceLinks.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase), evidenceLinks);
        Assert.Contains(GetOperationsLedgerPath(), evidenceLinks);
    }

    [Fact]
    public async Task Append_SetOperation_ReadsAffectedElementEvidence()
    {
        var output = new StringWriter();
        var exitCode = await LedgerCommand.ExecuteAppendAsync(
            _root,
            action: "set",
            category: "walls",
            operatorName: "alice",
            status: "succeeded",
            summary: "Set 注释 on 2 element(s)",
            timestamp: _now.ToString("o"),
            model: null,
            modelPath: null,
            planHash: null,
            artifactPath: null,
            receiptPath: null,
            receiptHash: null,
            rollbackPointer: null,
            evidenceLinks: Array.Empty<string>(),
            yes: true,
            outputFormat: "json",
            output,
            _now,
            commandName: "set",
            commandArgs: new[] { "set", "walls", "--filter", "标记 = TEST", "--param", "注释", "--value", "Reviewed", "--yes" },
            affectedElementCount: 2,
            affectedElementIds: new[] { 200L, 100L });

        Assert.Equal(0, exitCode);

        var queryOutput = new StringWriter();
        var queryExitCode = await LedgerCommand.ExecuteQueryAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: "set",
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 100,
            outputFormat: "json",
            queryOutput,
            _now);

        Assert.Equal(0, queryExitCode);
        using var json = JsonDocument.Parse(queryOutput.ToString());
        var operation = Assert.Single(json.RootElement.GetProperty("operations").EnumerateArray());
        Assert.Equal("set", operation.GetProperty("command").GetString());
        Assert.Equal("set", operation.GetProperty("action").GetString());
        Assert.Equal("walls", operation.GetProperty("category").GetString());
        Assert.Equal(2, operation.GetProperty("affectedElementCount").GetInt32());
        Assert.Equal(new[] { 100L, 200L }, operation.GetProperty("affectedElementIds").EnumerateArray().Select(id => id.GetInt64()).ToArray());
        Assert.Contains(operation.GetProperty("args").EnumerateArray(), arg => arg.GetString() == "标记 = TEST");
    }

    [Fact]
    public async Task ReplayApply_SetOperation_AppliesFrozenAffectedIds()
    {
        await LedgerCommand.ExecuteAppendAsync(
            _root,
            action: "set",
            category: "walls",
            operatorName: "alice",
            status: "succeeded",
            summary: "Set 注释 on 2 element(s)",
            timestamp: _now.ToString("o"),
            model: null,
            modelPath: null,
            planHash: null,
            artifactPath: null,
            receiptPath: null,
            receiptHash: null,
            rollbackPointer: null,
            evidenceLinks: Array.Empty<string>(),
            yes: true,
            outputFormat: "json",
            new StringWriter(),
            _now,
            commandName: "set",
            commandArgs: new[] { "set", "walls", "--filter", "标记 = TEST", "--param", "注释", "--value", "Reviewed", "--yes" },
            affectedElementCount: 2,
            affectedElementIds: new[] { 200L, 100L });
        Assert.Single(File.ReadAllLines(GetOperationsLedgerPath()));
        var response = ApiResponse<SetResult>.Ok(new SetResult { Affected = 2 });
        var status = new StatusInfo
        {
            RevitVersion = "2026",
            RevitYear = 2026,
            AddinVersion = "2.3.0",
            DocumentName = "revit_cli.rvt",
            DocumentPath = Path.Combine(_root, "model", "revit_cli.rvt"),
        };
        var handler = new QueuedHttpHandler(
            JsonSerializer.Serialize(response),
            JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(status)));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteReplayAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: "set",
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 10,
            outputFormat: "json",
            output,
            _now,
            client,
            apply: true,
            yes: true);

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "/api/elements/set", "/api/status" }, handler.RequestPaths);
        using var requestJson = JsonDocument.Parse(handler.RequestBodies[0]);
        var request = requestJson.RootElement;
        Assert.Equal(new[] { 100L, 200L }, request.GetProperty("elementIds").EnumerateArray().Select(id => id.GetInt64()).ToArray());
        Assert.Equal("注释", request.GetProperty("param").GetString());
        Assert.Equal("Reviewed", request.GetProperty("value").GetString());
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("dryRun").GetBoolean());
        Assert.True(root.GetProperty("applySupported").GetBoolean());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("appliedStepCount").GetInt32());
        var step = Assert.Single(root.GetProperty("steps").EnumerateArray());
        Assert.True(step.GetProperty("canApply").GetBoolean());
        Assert.Equal("applied", step.GetProperty("applyStatus").GetString());

        var ledgerLines = File.ReadAllLines(GetOperationsLedgerPath());
        Assert.Equal(2, ledgerLines.Length);
        using var auditJson = JsonDocument.Parse(ledgerLines[1]);
        var audit = auditJson.RootElement;
        Assert.Equal("ledger-operation.v1", audit.GetProperty("schemaVersion").GetString());
        Assert.Equal("ledger", audit.GetProperty("command").GetString());
        Assert.Equal("ledger.replay.apply", audit.GetProperty("action").GetString());
        Assert.Equal("walls", audit.GetProperty("category").GetString());
        Assert.Equal("succeeded", audit.GetProperty("status").GetString());
        Assert.Equal("revit_cli.rvt", audit.GetProperty("modelIdentity").GetString());
        Assert.Equal(Path.Combine(_root, "model", "revit_cli.rvt"), audit.GetProperty("modelPath").GetString());
        Assert.Equal("2026", audit.GetProperty("revitVersion").GetString());
        Assert.Equal(2, audit.GetProperty("affectedElementCount").GetInt32());
        Assert.Equal(new[] { 100L, 200L }, audit.GetProperty("affectedElementIds").EnumerateArray().Select(id => id.GetInt64()).ToArray());
        var args = audit.GetProperty("args").EnumerateArray().Select(arg => arg.GetString()).ToArray();
        Assert.Contains("replay", args);
        Assert.Contains("--apply", args);
        Assert.Contains("--yes", args);

        var replayOutput = new StringWriter();
        var replayExitCode = await LedgerCommand.ExecuteReplayAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 10,
            outputFormat: "json",
            replayOutput,
            _now);

        Assert.Equal(0, replayExitCode);
        using var replayJson = JsonDocument.Parse(replayOutput.ToString());
        var replaySteps = replayJson.RootElement.GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal(2, replaySteps.Length);
        var replayAuditStep = Assert.Single(replaySteps, step => step.GetProperty("action").GetString() == "ledger.replay.apply");
        Assert.Equal("ledger", replayAuditStep.GetProperty("command").GetString());
        Assert.False(replayAuditStep.GetProperty("canApply").GetBoolean());
    }

    [Fact]
    public async Task ReplayApply_ExportOperation_AppliesReplayableExport()
    {
        var outputDir = Path.Combine(_root, "exports");
        await SeedReplayableExportOperationAsync(new[] { "export", "--format", "pdf", "--sheets", "A101", "--output-dir", outputDir });
        Assert.Single(File.ReadAllLines(GetOperationsLedgerPath()));
        var progress = ApiResponse<ExportProgress>.Ok(new ExportProgress
        {
            TaskId = "export-replay",
            Status = "completed",
            Progress = 100,
        });
        var status = new StatusInfo
        {
            RevitVersion = "2026",
            RevitYear = 2026,
            AddinVersion = "2.3.0",
            DocumentName = "revit_cli.rvt",
            DocumentPath = Path.Combine(_root, "model", "revit_cli.rvt"),
        };
        var handler = new QueuedHttpHandler(
            JsonSerializer.Serialize(progress),
            JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(status)));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteReplayAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: "export",
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 10,
            outputFormat: "json",
            output,
            _now,
            client,
            apply: true,
            yes: true);

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "/api/export", "/api/status" }, handler.RequestPaths);
        using var requestJson = JsonDocument.Parse(handler.RequestBodies[0]);
        var request = requestJson.RootElement;
        Assert.Equal("pdf", request.GetProperty("format").GetString());
        Assert.Equal(new[] { "A101" }, request.GetProperty("sheets").EnumerateArray().Select(sheet => sheet.GetString()).ToArray());
        Assert.Empty(request.GetProperty("views").EnumerateArray());
        Assert.Equal(outputDir, request.GetProperty("outputDir").GetString());
        Assert.False(request.GetProperty("dryRun").GetBoolean());
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("dryRun").GetBoolean());
        Assert.True(root.GetProperty("applySupported").GetBoolean());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("appliedStepCount").GetInt32());
        var step = Assert.Single(root.GetProperty("steps").EnumerateArray());
        Assert.True(step.GetProperty("canApply").GetBoolean());
        Assert.Equal("applied", step.GetProperty("applyStatus").GetString());

        var ledgerLines = File.ReadAllLines(GetOperationsLedgerPath());
        Assert.Equal(2, ledgerLines.Length);
        using var auditJson = JsonDocument.Parse(ledgerLines[1]);
        var audit = auditJson.RootElement;
        Assert.Equal("ledger", audit.GetProperty("command").GetString());
        Assert.Equal("ledger.replay.apply", audit.GetProperty("action").GetString());
        Assert.Equal("pdf", audit.GetProperty("category").GetString());
        Assert.Equal("succeeded", audit.GetProperty("status").GetString());
        Assert.Equal("revit_cli.rvt", audit.GetProperty("modelIdentity").GetString());
        Assert.Equal(Path.Combine(_root, "model", "revit_cli.rvt"), audit.GetProperty("modelPath").GetString());
        Assert.Equal("2026", audit.GetProperty("revitVersion").GetString());
        Assert.Equal(0, audit.GetProperty("affectedElementCount").GetInt32());
        var args = audit.GetProperty("args").EnumerateArray().Select(arg => arg.GetString()).ToArray();
        Assert.Contains("--action", args);
        Assert.Contains("export", args);
        Assert.Contains("--apply", args);
        Assert.Contains("--yes", args);
    }

    [Fact]
    public async Task ReplayApply_ExportOperation_MissingOutputDir_NotReplayable()
    {
        await SeedReplayableExportOperationAsync(new[] { "export", "--format", "pdf", "--sheets", "A101" });
        var handler = new FakeHttpHandler();
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteReplayAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: "export",
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 10,
            outputFormat: "json",
            output,
            _now,
            client,
            apply: true,
            yes: true);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, handler.CallCount);
        Assert.Single(File.ReadAllLines(GetOperationsLedgerPath()));
        using var json = JsonDocument.Parse(output.ToString());
        var step = Assert.Single(json.RootElement.GetProperty("steps").EnumerateArray());
        Assert.False(step.GetProperty("canApply").GetBoolean());
        Assert.Equal("recorded export command is missing --output-dir", step.GetProperty("blockReason").GetString());
        Assert.DoesNotContain("ledger.replay.apply", output.ToString());
    }

    [Fact]
    public async Task ReplayApply_ExportOperation_RequiresValidReceipt()
    {
        await SeedReplayableExportOperationAsync(receiptBacked: false);
        var handler = new FakeHttpHandler();
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteReplayAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: "export",
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 10,
            outputFormat: "json",
            output,
            _now,
            client,
            apply: true,
            yes: true);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, handler.CallCount);
        Assert.Single(File.ReadAllLines(GetOperationsLedgerPath()));
        using var json = JsonDocument.Parse(output.ToString());
        var step = Assert.Single(json.RootElement.GetProperty("steps").EnumerateArray());
        Assert.False(step.GetProperty("canApply").GetBoolean());
        Assert.Equal("only valid receipt-backed export ledger records are eligible for replay apply", step.GetProperty("blockReason").GetString());
    }

    [Fact]
    public async Task ReplayApply_ScheduleBatchExportOperation_AppliesReplayableManifestEntries()
    {
        var manifestPath = await SeedReplayableScheduleBatchExportOperationAsync();
        Assert.Single(File.ReadAllLines(GetOperationsLedgerPath()));
        var status = new StatusInfo
        {
            RevitVersion = "2026",
            RevitYear = 2026,
            AddinVersion = "2.3.0",
            DocumentName = "revit_cli.rvt",
            DocumentPath = Path.Combine(_root, "model", "revit_cli.rvt"),
        };
        var handler = new QueuedHttpHandler(
            JsonSerializer.Serialize(ApiResponse<ScheduleInfo[]>.Ok(new[]
            {
                new ScheduleInfo { Id = 100, Name = "Door Schedule", Category = "Doors", FieldCount = 2, RowCount = 1 },
            })),
            JsonSerializer.Serialize(ApiResponse<ScheduleData>.Ok(new ScheduleData
            {
                Columns = new List<string> { "Mark", "Level" },
                Rows = new List<Dictionary<string, string>>
                {
                    new() { ["Mark"] = "D-001", ["Level"] = "L1" },
                },
                TotalRows = 1,
            })),
            JsonSerializer.Serialize(ApiResponse<StatusInfo>.Ok(status)));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteReplayAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: "schedules.batch-export",
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 10,
            outputFormat: "json",
            output,
            _now,
            client,
            apply: true,
            yes: true);

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "/api/schedules", "/api/schedules/export", "/api/status" }, handler.RequestPaths);
        using var requestJson = JsonDocument.Parse(handler.RequestBodies[0]);
        var request = requestJson.RootElement;
        Assert.Equal("Door Schedule", request.GetProperty("existingName").GetString());
        Assert.Equal(JsonValueKind.Null, request.GetProperty("category").ValueKind);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.True(root.GetProperty("applySupported").GetBoolean());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("appliedStepCount").GetInt32());
        var step = Assert.Single(root.GetProperty("steps").EnumerateArray());
        Assert.True(step.GetProperty("canApply").GetBoolean());
        Assert.Equal("applied", step.GetProperty("applyStatus").GetString());
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var csvPath = manifest.RootElement.GetProperty("entries")[0].GetProperty("outputPath").GetString()!;
        Assert.Contains("Mark,Level", File.ReadAllText(csvPath));
        Assert.Contains("D-001,L1", File.ReadAllText(csvPath));

        var ledgerLines = File.ReadAllLines(GetOperationsLedgerPath());
        Assert.Equal(2, ledgerLines.Length);
        using var originalJson = JsonDocument.Parse(ledgerLines[0]);
        Assert.Equal("schedules.batch-export", originalJson.RootElement.GetProperty("action").GetString());
        using var auditJson = JsonDocument.Parse(ledgerLines[1]);
        var audit = auditJson.RootElement;
        Assert.Equal("ledger", audit.GetProperty("command").GetString());
        Assert.Equal("ledger.replay.apply", audit.GetProperty("action").GetString());
        Assert.Equal("csv", audit.GetProperty("category").GetString());
        Assert.Equal("succeeded", audit.GetProperty("status").GetString());
        Assert.Equal("revit_cli.rvt", audit.GetProperty("modelIdentity").GetString());
        Assert.Equal(Path.Combine(_root, "model", "revit_cli.rvt"), audit.GetProperty("modelPath").GetString());
        Assert.Equal("2026", audit.GetProperty("revitVersion").GetString());
        var args = audit.GetProperty("args").EnumerateArray().Select(arg => arg.GetString()).ToArray();
        Assert.Contains("--action", args);
        Assert.Contains("schedules.batch-export", args);
        Assert.Contains("--apply", args);
        Assert.Contains("--yes", args);
    }

    [Fact]
    public async Task ReplayApply_ScheduleBatchExportOperation_MissingManifest_NotReplayable()
    {
        await SeedReplayableScheduleBatchExportOperationAsync(writeManifest: false);
        var handler = new FakeHttpHandler();
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteReplayAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: "schedules.batch-export",
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 10,
            outputFormat: "json",
            output,
            _now,
            client,
            apply: true,
            yes: true);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, handler.CallCount);
        Assert.Single(File.ReadAllLines(GetOperationsLedgerPath()));
        using var json = JsonDocument.Parse(output.ToString());
        var step = Assert.Single(json.RootElement.GetProperty("steps").EnumerateArray());
        Assert.False(step.GetProperty("canApply").GetBoolean());
        Assert.Equal("recorded schedules batch-export manifest is missing", step.GetProperty("blockReason").GetString());
        Assert.DoesNotContain("ledger.replay.apply", output.ToString());
    }

    [Fact]
    public async Task ReplayApply_ScheduleBatchExportOperation_OutputOutsideRecordedDirectory_NotReplayable()
    {
        var outsidePath = Path.Combine(_root, "outside", "Door Schedule.csv");
        await SeedReplayableScheduleBatchExportOperationAsync(entryOutputPath: outsidePath);
        var handler = new FakeHttpHandler();
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteReplayAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: "schedules.batch-export",
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 10,
            outputFormat: "json",
            output,
            _now,
            client,
            apply: true,
            yes: true);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, handler.CallCount);
        Assert.Single(File.ReadAllLines(GetOperationsLedgerPath()));
        Assert.False(File.Exists(outsidePath));
        using var json = JsonDocument.Parse(output.ToString());
        var step = Assert.Single(json.RootElement.GetProperty("steps").EnumerateArray());
        Assert.False(step.GetProperty("canApply").GetBoolean());
        Assert.Equal("recorded schedules batch-export manifest output is outside --output-dir", step.GetProperty("blockReason").GetString());
    }

    [Fact]
    public async Task ReplayApply_StatusUnavailable_AppendsReplayAuditWithoutModelFields()
    {
        await SeedReplayableSetOperationAsync();
        var handler = new QueuedHttpHandler(
            JsonSerializer.Serialize(ApiResponse<SetResult>.Ok(new SetResult { Affected = 2 })),
            JsonSerializer.Serialize(ApiResponse<StatusInfo>.Fail("status unavailable")));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteReplayAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: "set",
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 10,
            outputFormat: "json",
            output,
            _now,
            client,
            apply: true,
            yes: true);

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "/api/elements/set", "/api/status" }, handler.RequestPaths);
        var ledgerLines = File.ReadAllLines(GetOperationsLedgerPath());
        Assert.Equal(2, ledgerLines.Length);
        using var auditJson = JsonDocument.Parse(ledgerLines[1]);
        var audit = auditJson.RootElement;
        Assert.Equal("ledger.replay.apply", audit.GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Null, audit.GetProperty("modelIdentity").ValueKind);
        Assert.Equal(JsonValueKind.Null, audit.GetProperty("modelPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, audit.GetProperty("revitVersion").ValueKind);
    }

    [Fact]
    public async Task ReplayApply_RequiresYes()
    {
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteReplayAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: "set",
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 10,
            outputFormat: "json",
            output,
            _now,
            new RevitClient(new HttpClient(new FakeHttpHandler()) { BaseAddress = new Uri("http://localhost:17839") }),
            apply: true,
            yes: false);

        Assert.Equal(1, exitCode);
        Assert.Contains("--yes", output.ToString());
    }

    [Fact]
    public async Task ReplayApply_FailedSet_DoesNotAppendReplayAudit()
    {
        await SeedReplayableSetOperationAsync();
        var response = ApiResponse<SetResult>.Fail("write failed");
        var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteReplayAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: "set",
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 10,
            outputFormat: "json",
            output,
            _now,
            client,
            apply: true,
            yes: true);

        Assert.Equal(1, exitCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Single(File.ReadAllLines(GetOperationsLedgerPath()));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(0, json.RootElement.GetProperty("summary").GetProperty("appliedStepCount").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("failedStepCount").GetInt32());
        Assert.DoesNotContain("ledger.replay.apply", output.ToString());
    }

    [Fact]
    public async Task ReplayApply_AuditAppendFailure_ReturnsFailure()
    {
        await SeedReplayableSetOperationAsync();
        var ledgerPath = GetOperationsLedgerPath();
        File.SetAttributes(ledgerPath, File.GetAttributes(ledgerPath) | FileAttributes.ReadOnly);
        try
        {
            var response = ApiResponse<SetResult>.Ok(new SetResult { Affected = 2 });
            var handler = new FakeHttpHandler(JsonSerializer.Serialize(response));
            var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
            var output = new StringWriter();

            var exitCode = await LedgerCommand.ExecuteReplayAsync(
                _root,
                source: "ledger",
                since: null,
                until: null,
                window: null,
                action: "set",
                category: null,
                operatorFilter: null,
                receiptStatus: "all",
                limit: 10,
                outputFormat: "json",
                output,
                _now,
                client,
                apply: true,
                yes: true);

            Assert.Equal(1, exitCode);
            Assert.Equal(2, handler.CallCount);
            Assert.Single(File.ReadAllLines(ledgerPath));
            Assert.Contains("operation ledger append failed", output.ToString());
        }
        finally
        {
            File.SetAttributes(ledgerPath, File.GetAttributes(ledgerPath) & ~FileAttributes.ReadOnly);
        }
    }

    [Fact]
    public async Task ReplayApply_BlockedStep_DoesNotAppendReplayAudit()
    {
        await LedgerCommand.ExecuteAppendAsync(
            _root,
            action: "issue.preflight",
            category: "issue",
            operatorName: "alice",
            status: "succeeded",
            summary: "Preflight issue",
            timestamp: _now.ToString("o"),
            model: null,
            modelPath: null,
            planHash: null,
            artifactPath: null,
            receiptPath: null,
            receiptHash: null,
            rollbackPointer: null,
            evidenceLinks: Array.Empty<string>(),
            yes: true,
            outputFormat: "json",
            new StringWriter(),
            _now);
        var handler = new FakeHttpHandler();
        var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteReplayAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 10,
            outputFormat: "json",
            output,
            _now,
            client,
            apply: true,
            yes: true);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, handler.CallCount);
        Assert.Single(File.ReadAllLines(GetOperationsLedgerPath()));
        Assert.DoesNotContain("ledger.replay.apply", output.ToString());
    }

    [Theory]
    [InlineData("invalid", null, null, "Error: --status must be 'planned', 'succeeded', 'failed', or 'blocked'.")]
    [InlineData("succeeded", "2026-05-23T12:00:00", null, "Error: --timestamp must be ISO 8601 with an explicit UTC offset.")]
    [InlineData("succeeded", null, "abc", "Error: --receipt-hash must be a 64-character SHA256 hex digest.")]
    public async Task Append_InvalidOptions_AreRejected(string status, string? timestamp, string? receiptHash, string expected)
    {
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteAppendAsync(
            _root,
            action: "issue.package",
            category: null,
            operatorName: null,
            status: status,
            summary: null,
            timestamp: timestamp,
            model: null,
            modelPath: null,
            planHash: null,
            artifactPath: null,
            receiptPath: null,
            receiptHash: receiptHash,
            rollbackPointer: null,
            evidenceLinks: Array.Empty<string>(),
            yes: true,
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(1, exitCode);
        Assert.Contains(expected, output.ToString());
        Assert.False(File.Exists(GetOperationsLedgerPath()));
    }

    [Fact]
    public async Task Replay_Json_PreviewsLedgerOperationsWithoutApply()
    {
        await LedgerCommand.ExecuteAppendAsync(
            _root,
            action: "issue.preflight",
            category: "issue",
            operatorName: "alice",
            status: "succeeded",
            summary: "Preflight issue",
            timestamp: _now.AddMinutes(-10).ToString("o"),
            model: "sample.rvt",
            modelPath: null,
            planHash: "plan-a",
            artifactPath: null,
            receiptPath: null,
            receiptHash: null,
            rollbackPointer: null,
            evidenceLinks: Array.Empty<string>(),
            yes: true,
            outputFormat: "json",
            new StringWriter(),
            _now);
        await LedgerCommand.ExecuteAppendAsync(
            _root,
            action: "issue.package",
            category: "issue",
            operatorName: "alice",
            status: "succeeded",
            summary: "Package issue",
            timestamp: _now.ToString("o"),
            model: "sample.rvt",
            modelPath: null,
            planHash: "plan-b",
            artifactPath: "out/package.zip",
            receiptPath: null,
            receiptHash: null,
            rollbackPointer: "revitcli rollback issue-package.json",
            evidenceLinks: Array.Empty<string>(),
            yes: true,
            outputFormat: "json",
            new StringWriter(),
            _now);
        var before = SnapshotLocalFiles(_root);
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteReplayAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 10,
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        Assert.Equal(before, SnapshotLocalFiles(_root));
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("ledger-replay.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("dryRun").GetBoolean());
        Assert.False(root.GetProperty("applySupported").GetBoolean());
        Assert.Equal(2, root.GetProperty("summary").GetProperty("stepCount").GetInt32());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("applicableStepCount").GetInt32());
        Assert.Equal(2, root.GetProperty("summary").GetProperty("blockedStepCount").GetInt32());

        var steps = root.GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal(new[] { "issue.preflight", "issue.package" }, steps.Select(step => step.GetProperty("action").GetString()).ToArray());
        Assert.All(steps, step =>
        {
            Assert.Equal("preview", step.GetProperty("replayMode").GetString());
            Assert.False(step.GetProperty("canApply").GetBoolean());
            Assert.Contains("preview-only", step.GetProperty("blockReason").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("ledger append", step.GetProperty("command").GetString());
            Assert.Contains(step.GetProperty("args").EnumerateArray(), arg => arg.GetString() == "--yes");
        });
    }

    [Fact]
    public async Task Replay_Json_PreviewKeepsReplayableRowsNonApplicable()
    {
        await SeedReplayableSetOperationAsync();
        await SeedReplayableExportOperationAsync();
        await SeedReplayableScheduleBatchExportOperationAsync();
        var before = SnapshotLocalFiles(_root);
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteReplayAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 10,
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        Assert.Equal(before, SnapshotLocalFiles(_root));
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.True(root.GetProperty("dryRun").GetBoolean());
        Assert.False(root.GetProperty("applySupported").GetBoolean());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("applicableStepCount").GetInt32());
        Assert.Equal(3, root.GetProperty("summary").GetProperty("blockedStepCount").GetInt32());
        var steps = root.GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal(new[] { "set", "export", "schedules.batch-export" }, steps.Select(step => step.GetProperty("action").GetString()).ToArray());
        Assert.All(steps, step =>
        {
            Assert.Equal("preview", step.GetProperty("replayMode").GetString());
            Assert.False(step.GetProperty("canApply").GetBoolean());
            Assert.Contains("preview-only", step.GetProperty("blockReason").GetString(), StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Replay_InvalidLimit_IsRejected()
    {
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteReplayAsync(
            _root,
            source: "ledger",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 0,
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(1, exitCode);
        Assert.Contains("Error: --limit must be greater than 0.", output.ToString());
    }

    [Fact]
    public async Task Query_Json_MergesLocalArtifactsDeterministically()
    {
        await SeedHistoryAsync();
        SeedJournal(("set", "sheets", "alice", _now.AddHours(-3), 2));
        SeedWorkflowReceipt("pre-issue", "alice", _now.AddHours(-2), success: true);
        SeedDeliveryManifest(_now.AddHours(-1));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteQueryAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 100,
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("ledger-query.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(_root, root.GetProperty("projectDirectory").GetString());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("issueCount").GetInt32());

        var operations = root.GetProperty("operations").EnumerateArray().ToArray();
        Assert.Equal(4, operations.Length);
        Assert.Equal(
            new[] { "history", "journal", "workflows", "deliveries" },
            operations.Select(operation => operation.GetProperty("source").GetString()).ToArray());
        Assert.Contains(operations, operation =>
            operation.GetProperty("action").GetString() == "workflow.run" &&
            operation.GetProperty("receiptStatus").GetString() == "valid");
        Assert.Contains(operations, operation =>
            operation.GetProperty("action").GetString() == "deliverables.publish" &&
            operation.GetProperty("receiptStatus").GetString() == "valid");

        var sourceCounts = root.GetProperty("summary").GetProperty("bySource").EnumerateArray().ToArray();
        Assert.Contains(sourceCounts, item =>
            item.GetProperty("source").GetString() == "journal" &&
            item.GetProperty("count").GetInt32() == 1);
    }

    [Fact]
    public async Task Query_Json_FiltersSourceWindowAndOperator()
    {
        SeedJournal(
            ("set", "sheets", "alice", _now.AddHours(-3), 2),
            ("publish", "issue", "bob", _now.AddHours(-1), 1),
            ("set", "old", "alice", _now.AddDays(-10), 9));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteQueryAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: "2d",
            action: "set",
            category: null,
            operatorFilter: "alice",
            receiptStatus: "all",
            limit: 10,
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var operation = Assert.Single(json.RootElement.GetProperty("operations").EnumerateArray());
        Assert.Equal("journal", operation.GetProperty("source").GetString());
        Assert.Equal("set", operation.GetProperty("action").GetString());
        Assert.Equal("alice", operation.GetProperty("operator").GetString());
        Assert.Equal("2d", json.RootElement.GetProperty("query").GetProperty("window").GetString());
    }

    [Fact]
    public async Task Query_Json_TimeBoundsDropInvalidTimestampOperations()
    {
        SeedJournalRaw(
            JsonSerializer.Serialize(new
            {
                timestamp = _now.AddHours(-1).ToString("o"),
                action = "set",
                category = "sheets",
                user = "alice",
                @operator = "alice",
                affected = 1,
            }),
            JsonSerializer.Serialize(new
            {
                timestamp = "2026-05-23T12:00:00",
                action = "set",
                category = "sheets",
                user = "bob",
                @operator = "bob",
                affected = 1,
            }));

        (string Label, string? Since, string? Until)[] cases =
        [
            ("since", _now.AddDays(-1).ToString("o"), null),
            ("until", null, _now.AddDays(1).ToString("o")),
        ];

        foreach (var filter in cases)
        {
            var output = new StringWriter();
            var exitCode = await LedgerCommand.ExecuteQueryAsync(
                _root,
                source: "journal",
                since: filter.Since,
                until: filter.Until,
                window: null,
                action: null,
                category: null,
                operatorFilter: null,
                receiptStatus: "all",
                limit: 100,
                outputFormat: "json",
                output,
                _now);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(output.ToString());
            var operation = Assert.Single(json.RootElement.GetProperty("operations").EnumerateArray());
            Assert.Equal("alice", operation.GetProperty("operator").GetString());
            Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("totalOperations").GetInt32());
        }
    }

    [Fact]
    public async Task Query_Json_OrdersSameSourceSameTimestampByLine()
    {
        var timestamp = _now.AddHours(-2).ToString("o");
        SeedJournalRaw(
            JsonSerializer.Serialize(new { timestamp, action = "zeta", category = "sheets", user = "alice", @operator = "alice", affected = 1 }),
            JsonSerializer.Serialize(new { timestamp, action = "alpha", category = "sheets", user = "alice", @operator = "alice", affected = 2 }),
            JsonSerializer.Serialize(new { timestamp, action = "beta", category = "sheets", user = "alice", @operator = "alice", affected = 3 }));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteQueryAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 100,
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var operations = json.RootElement.GetProperty("operations").EnumerateArray().ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, operations.Select(operation => operation.GetProperty("line").GetInt32()).ToArray());
        Assert.Equal(new[] { "zeta", "alpha", "beta" }, operations.Select(operation => operation.GetProperty("action").GetString()).ToArray());
    }

    [Fact]
    public async Task Query_Json_MalformedArtifactsReturnIssuesWithoutFailing()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".revitcli"));
        File.WriteAllText(Path.Combine(_root, ".revitcli", "journal.jsonl"), "{not json");

        var workflowDir = Path.Combine(_root, ".revitcli", "workflows", "receipts");
        Directory.CreateDirectory(workflowDir);
        var workflowBPath = Path.Combine(workflowDir, "bad-b.json");
        var workflowAPath = Path.Combine(workflowDir, "bad-a.json");
        File.WriteAllText(workflowBPath, "{not json");
        File.WriteAllText(workflowAPath, "{not json");

        var deliveryDir = Path.Combine(_root, ".revitcli", "deliveries");
        Directory.CreateDirectory(deliveryDir);
        File.WriteAllText(Path.Combine(deliveryDir, "manifest.jsonl"), "{not json");
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteQueryAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 50,
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var issues = json.RootElement.GetProperty("issues").EnumerateArray().ToArray();
        Assert.Contains(issues, issue =>
            issue.GetProperty("source").GetString() == "journal" &&
            issue.GetProperty("message").GetString()!.Contains("failed to read journal", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.GetProperty("source").GetString() == "workflows");
        Assert.Contains(issues, issue => issue.GetProperty("source").GetString() == "deliveries");
        var workflowIssuePaths = issues
            .Where(issue => issue.GetProperty("source").GetString() == "workflows")
            .Select(issue => issue.GetProperty("path").GetString())
            .ToArray();
        Assert.Equal(new[] { workflowAPath, workflowBPath }, workflowIssuePaths);

        var operations = json.RootElement.GetProperty("operations").EnumerateArray().ToArray();
        Assert.Contains(operations, operation =>
            operation.GetProperty("action").GetString() == "workflow.receipt-issue" &&
            operation.GetProperty("issues").GetArrayLength() == 0);
        Assert.Contains(operations, operation =>
            operation.GetProperty("action").GetString() == "deliverables.manifest-issue" &&
            operation.GetProperty("issues").GetArrayLength() == 0);
        Assert.Equal(issues.Length, json.RootElement.GetProperty("summary").GetProperty("issueCount").GetInt32());
    }

    [Fact]
    public async Task Query_MarkdownAndTable_RenderHeadings()
    {
        var markdownOutput = new StringWriter();
        var markdownExitCode = await LedgerCommand.ExecuteQueryAsync(
            _root,
            "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 100,
            outputFormat: "markdown",
            markdownOutput,
            _now);

        var tableOutput = new StringWriter();
        var tableExitCode = await LedgerCommand.ExecuteQueryAsync(
            _root,
            "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 100,
            outputFormat: "table",
            tableOutput,
            _now);

        Assert.Equal(0, markdownExitCode);
        Assert.Equal(0, tableExitCode);
        Assert.Contains("# RevitCli Ledger Query", markdownOutput.ToString());
        Assert.Contains("| Timestamp | Source | Action | Receipt | Artifact |", markdownOutput.ToString());
        Assert.Contains("Ledger query", tableOutput.ToString());
        Assert.Contains("Operations:", tableOutput.ToString());
    }

    [Fact]
    public async Task Query_OutputFormats_DoNotWriteFiles()
    {
        await SeedHistoryAsync();
        SeedJournal(("set", "sheets", "alice", _now.AddHours(-3), 2));
        SeedWorkflowReceipt("pre-issue", "alice", _now.AddHours(-2), success: true);
        SeedDeliveryManifest(_now.AddHours(-1));
        var before = SnapshotFiles(_root);

        foreach (var outputFormat in new[] { "json", "markdown", "table" })
        {
            var output = new StringWriter();
            var exitCode = await LedgerCommand.ExecuteQueryAsync(
                _root,
                source: "all",
                since: null,
                until: null,
                window: null,
                action: null,
                category: null,
                operatorFilter: null,
                receiptStatus: "all",
                limit: 100,
                outputFormat,
                output,
                _now);

            Assert.Equal(0, exitCode);
            Assert.Equal(before, SnapshotFiles(_root));
        }
    }

    [Fact]
    public async Task Query_OutputFormats_PreserveOperationSemanticOrder()
    {
        await SeedHistoryAsync();
        SeedJournal(
            ("set", "sheets", "alice", _now.AddHours(-3), 2),
            ("publish", "issue", "bob", _now.AddHours(-1), 1));
        SeedWorkflowReceipt("pre-issue", "alice", _now.AddHours(-2), success: true);
        SeedDeliveryManifest(_now.AddMinutes(-30));
        var jsonOutput = new StringWriter();
        var markdownOutput = new StringWriter();
        var tableOutput = new StringWriter();

        var jsonExitCode = await LedgerCommand.ExecuteQueryAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 100,
            outputFormat: "json",
            jsonOutput,
            _now);
        var markdownExitCode = await LedgerCommand.ExecuteQueryAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 100,
            outputFormat: "markdown",
            markdownOutput,
            _now);
        var tableExitCode = await LedgerCommand.ExecuteQueryAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 100,
            outputFormat: "table",
            tableOutput,
            _now);

        Assert.Equal(0, jsonExitCode);
        Assert.Equal(0, markdownExitCode);
        Assert.Equal(0, tableExitCode);
        using var json = JsonDocument.Parse(jsonOutput.ToString());
        var expected = json.RootElement.GetProperty("operations")
            .EnumerateArray()
            .Select(QueryProjection.FromJson)
            .ToArray();

        Assert.Equal(5, expected.Length);
        Assert.Equal(expected, ParseQueryMarkdownRows(markdownOutput.ToString()));
        Assert.Equal(expected, ParseQueryTableRows(tableOutput.ToString(), expected.Length));
    }

    [Fact]
    public async Task Query_InvalidOptions_ReturnFailure()
    {
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteQueryAsync(
            _root,
            source: "database",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            limit: 100,
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(1, exitCode);
        Assert.Contains("--source must be", output.ToString());
    }

    [Fact]
    public async Task Validate_Json_ReturnsValidForConsistentArtifacts()
    {
        await SeedHistoryAsync();
        SeedJournal(("set", "sheets", "alice", _now.AddHours(-3), 2));
        SeedWorkflowReceipt("pre-issue", "alice", _now.AddHours(-2), success: true);
        SeedDeliveryManifest(_now.AddHours(-1));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "all",
            failOn: "error",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("ledger-validate.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("valid").GetBoolean());
        var summary = root.GetProperty("summary");
        Assert.Equal(4, summary.GetProperty("operationCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("errorCount").GetInt32());
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "artifact-links" &&
            check.GetProperty("status").GetString() == "pass");
    }

    [Fact]
    public async Task Validate_Json_ReturnsFailureForMissingDeliveryReceipt()
    {
        SeedDeliveryManifestWithMissingReceipt(_now.AddHours(-1));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "deliveries",
            failOn: "error",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("valid").GetBoolean());
        Assert.True(root.GetProperty("summary").GetProperty("errorCount").GetInt32() > 0);
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("source").GetString() == "deliveries" &&
            issue.GetProperty("message").GetString()!.Contains("receipt file not found", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "receipt.missing" &&
            issue.GetProperty("source").GetString() == "deliveries");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "receipt-status" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Validate_Json_ReturnsFailureForMismatchedDeliveryReceiptHash()
    {
        SeedDeliveryManifestWithMismatchedReceiptHash(_now.AddHours(-1));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "deliveries",
            failOn: "error",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("valid").GetBoolean());
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "receipt.hash" &&
            issue.GetProperty("source").GetString() == "deliveries");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "receipt-hashes" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Validate_Json_FailsExplicitMissingSource()
    {
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "journal",
            failOn: "error",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("valid").GetBoolean());
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "source.missing" &&
            issue.GetProperty("severity").GetString() == "error" &&
            issue.GetProperty("source").GetString() == "journal");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "sources-readable" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Validate_Json_ReusesQueryFilters()
    {
        SeedJournal(
            ("set", "sheets", "alice", _now.AddHours(-3), 2),
            ("publish", "issue", "bob", _now.AddHours(-1), 1),
            ("set", "old", "alice", _now.AddDays(-10), 9));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: "2d",
            action: "set",
            category: "sheets",
            operatorFilter: "alice",
            receiptStatus: "all",
            failOn: "error",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.True(root.GetProperty("valid").GetBoolean());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal("2d", root.GetProperty("query").GetProperty("window").GetString());
        Assert.Equal("set", root.GetProperty("query").GetProperty("action").GetString());
        Assert.Equal("sheets", root.GetProperty("query").GetProperty("category").GetString());
        Assert.Equal("alice", root.GetProperty("query").GetProperty("operator").GetString());
    }

    [Fact]
    public async Task Validate_Json_ReusesReceiptStatusFilter()
    {
        SeedDeliveryManifestWithMissingReceipt(_now.AddHours(-1));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "deliveries",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "missing",
            failOn: "error",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("valid").GetBoolean());
        Assert.Equal("missing", root.GetProperty("query").GetProperty("receiptStatus").GetString());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("missingReceiptCount").GetInt32());
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "receipt.missing" &&
            issue.GetProperty("source").GetString() == "deliveries");
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "receipt-status" &&
            check.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task Validate_Json_InvalidTimestampReturnsWarningAndFailOnWarningFailure()
    {
        SeedJournalRaw(JsonSerializer.Serialize(new
        {
            timestamp = "not-a-date",
            action = "set",
            category = "sheets",
            user = "alice",
            @operator = "alice",
            affected = 1,
        }));

        var errorThresholdOutput = new StringWriter();
        var errorThresholdExitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            failOn: "error",
            outputFormat: "json",
            errorThresholdOutput,
            _now);

        Assert.Equal(0, errorThresholdExitCode);
        using var errorThresholdJson = JsonDocument.Parse(errorThresholdOutput.ToString());
        var root = errorThresholdJson.RootElement;
        Assert.True(root.GetProperty("valid").GetBoolean());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("warningCount").GetInt32());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("errorCount").GetInt32());
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "timestamp.invalid" &&
            issue.GetProperty("severity").GetString() == "warning" &&
            issue.GetProperty("source").GetString() == "journal" &&
            issue.GetProperty("line").GetInt32() == 1 &&
            issue.GetProperty("path").GetString()!.EndsWith(Path.Combine(".revitcli", "journal.jsonl"), StringComparison.Ordinal) &&
            issue.GetProperty("message").GetString()!.Contains("explicit UTC offset", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(root.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("id").GetString() == "timestamp-format" &&
            check.GetProperty("status").GetString() == "warning");

        var warningThresholdOutput = new StringWriter();
        var warningThresholdExitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            failOn: "warning",
            outputFormat: "json",
            warningThresholdOutput,
            _now);

        Assert.Equal(1, warningThresholdExitCode);
        using var warningThresholdJson = JsonDocument.Parse(warningThresholdOutput.ToString());
        Assert.False(warningThresholdJson.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("warning", warningThresholdJson.RootElement.GetProperty("failOn").GetString());
    }

    [Fact]
    public async Task Validate_Json_TimestampWithoutOffsetReturnsWarning()
    {
        SeedJournalRaw(JsonSerializer.Serialize(new
        {
            timestamp = "2026-05-23T12:00:00",
            action = "set",
            category = "sheets",
            user = "alice",
            @operator = "alice",
            affected = 1,
        }));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            failOn: "error",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.True(root.GetProperty("valid").GetBoolean());
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "timestamp.invalid" &&
            issue.GetProperty("severity").GetString() == "warning" &&
            issue.GetProperty("message").GetString()!.Contains("explicit UTC offset", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validate_Json_NonIsoTimestampShapeReturnsWarning()
    {
        SeedJournalRaw(
            JsonSerializer.Serialize(new
            {
                timestamp = "2026-05-23 12:00:00Z",
                action = "set",
                category = "sheets",
                user = "alice",
                @operator = "alice",
                affected = 1,
            }),
            JsonSerializer.Serialize(new
            {
                timestamp = "2026/05/23T12:00:00Z",
                action = "set",
                category = "sheets",
                user = "alice",
                @operator = "alice",
                affected = 1,
            }));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            failOn: "error",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal(2, root.GetProperty("summary").GetProperty("warningCount").GetInt32());
        Assert.All(root.GetProperty("issues").EnumerateArray(), issue =>
        {
            Assert.Equal("timestamp.invalid", issue.GetProperty("code").GetString());
            Assert.Contains("explicit UTC offset", issue.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Validate_Json_WindowKeepsInvalidTimestampWarnings()
    {
        SeedJournalRaw(JsonSerializer.Serialize(new
        {
            timestamp = "2026-05-23T12:00:00",
            action = "set",
            category = "sheets",
            user = "alice",
            @operator = "alice",
            affected = 1,
        }));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: "1d",
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            failOn: "error",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "timestamp.invalid");
    }

    [Fact]
    public async Task Validate_Json_TimeBoundsKeepInvalidTimestampWarnings()
    {
        SeedJournalRaw(JsonSerializer.Serialize(new
        {
            timestamp = "2026-05-23T12:00:00",
            action = "set",
            category = "sheets",
            user = "alice",
            @operator = "alice",
            affected = 1,
        }));

        (string Label, string? Since, string? Until)[] cases =
        [
            ("since", _now.AddDays(-1).ToString("o"), null),
            ("until", null, _now.AddDays(1).ToString("o")),
        ];

        foreach (var filter in cases)
        {
            var output = new StringWriter();
            var exitCode = await LedgerCommand.ExecuteValidateAsync(
                _root,
                source: "journal",
                since: filter.Since,
                until: filter.Until,
                window: null,
                action: null,
                category: null,
                operatorFilter: null,
                receiptStatus: "all",
                failOn: "error",
                outputFormat: "json",
                output,
                _now);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(output.ToString());
            var root = json.RootElement;
            Assert.Equal(1, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
            Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
                issue.GetProperty("code").GetString() == "timestamp.invalid" &&
                issue.GetProperty("message").GetString()!.Contains("explicit UTC offset", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Validate_InvalidSinceWithoutOffsetReturnsExplicitOffsetError()
    {
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "journal",
            since: "2026-05-23T12:00:00",
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            failOn: "error",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(1, exitCode);
        Assert.Contains("explicit UTC offset", output.ToString());
    }

    [Fact]
    public async Task Validate_MarkdownAndTable_RenderHeadings()
    {
        var markdownOutput = new StringWriter();
        var markdownExitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "all",
            failOn: "error",
            outputFormat: "markdown",
            markdownOutput,
            _now);

        var tableOutput = new StringWriter();
        var tableExitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "all",
            failOn: "error",
            outputFormat: "table",
            tableOutput,
            _now);

        Assert.Equal(0, markdownExitCode);
        Assert.Equal(0, tableExitCode);
        Assert.Contains("# RevitCli Ledger Validation", markdownOutput.ToString());
        Assert.Contains("| Status | Check | Evidence |", markdownOutput.ToString());
        Assert.Contains("Ledger validation", tableOutput.ToString());
        Assert.Contains("Valid:", tableOutput.ToString());
    }

    [Fact]
    public async Task Validate_OutputFormats_PreserveValidationSemantics()
    {
        SeedJournalRaw(JsonSerializer.Serialize(new
        {
            timestamp = "2026-05-23T12:00:00",
            action = "set",
            category = "sheets",
            user = "alice",
            @operator = "alice",
            affected = 1,
        }));
        SeedDeliveryManifestWithMissingReceipt(_now.AddHours(-1));
        var jsonOutput = new StringWriter();
        var markdownOutput = new StringWriter();
        var tableOutput = new StringWriter();

        var jsonExitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            failOn: "error",
            outputFormat: "json",
            jsonOutput,
            _now);
        var markdownExitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            failOn: "error",
            outputFormat: "markdown",
            markdownOutput,
            _now);
        var tableExitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            failOn: "error",
            outputFormat: "table",
            tableOutput,
            _now);

        Assert.Equal(1, jsonExitCode);
        Assert.Equal(jsonExitCode, markdownExitCode);
        Assert.Equal(jsonExitCode, tableExitCode);
        using var json = JsonDocument.Parse(jsonOutput.ToString());
        var expected = ValidationProjection.FromJson(json.RootElement);

        AssertValidationEqual(expected, ParseValidationMarkdown(markdownOutput.ToString()));
        AssertValidationEqual(expected, ParseValidationTable(tableOutput.ToString()));
    }

    [Fact]
    public async Task Validate_OutputFormats_DoNotWriteFilesEvenWhenValidationFails()
    {
        await SeedHistoryAsync();
        SeedJournal(("set", "sheets", "alice", _now.AddHours(-3), 2));
        SeedDeliveryManifestWithMissingReceipt(_now.AddHours(-1));
        var before = SnapshotFiles(_root);

        foreach (var outputFormat in new[] { "json", "markdown", "table" })
        {
            var output = new StringWriter();
            var exitCode = await LedgerCommand.ExecuteValidateAsync(
                _root,
                source: "all",
                since: null,
                until: null,
                window: null,
                action: null,
                category: null,
                operatorFilter: null,
                receiptStatus: "all",
                failOn: "error",
                outputFormat,
                output,
                _now);

            Assert.Equal(1, exitCode);
            Assert.Equal(before, SnapshotFiles(_root));
        }
    }

    [Fact]
    public async Task Validate_InvalidOptions_ReturnFailure()
    {
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteValidateAsync(
            _root,
            source: "all",
            failOn: "info",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(1, exitCode);
        Assert.Contains("--fail-on must be", output.ToString());
    }

    [Fact]
    public async Task Stats_Json_SummarizesLocalArtifactsDeterministically()
    {
        await SeedHistoryAsync();
        SeedJournal(
            ("set", "sheets", "alice", _now.AddHours(-3), 2),
            ("publish", "issue", "bob", _now.AddHours(-2), 1));
        SeedWorkflowReceipt("pre-issue", "alice", _now.AddHours(-1), success: true);
        SeedDeliveryManifest(_now);
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteStatsAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("ledger-stats.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(5, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("issueCount").GetInt32());
        Assert.Equal(_now.AddHours(-4).ToUniversalTime().ToString("o"), root.GetProperty("summary").GetProperty("firstTimestamp").GetString());
        Assert.Equal(_now.ToUniversalTime().ToString("o"), root.GetProperty("summary").GetProperty("lastTimestamp").GetString());

        var bySource = root.GetProperty("bySource").EnumerateArray().ToArray();
        Assert.Equal("journal", bySource[0].GetProperty("name").GetString());
        Assert.Equal(2, bySource[0].GetProperty("count").GetInt32());
        Assert.Contains(root.GetProperty("byReceiptStatus").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "valid" &&
            item.GetProperty("count").GetInt32() == 2);
        Assert.Contains(root.GetProperty("byCategory").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "issue" &&
            item.GetProperty("count").GetInt32() == 2);
        Assert.Contains(root.GetProperty("byOperator").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "alice" &&
            item.GetProperty("count").GetInt32() == 2);
        Assert.Empty(root.GetProperty("issuesBySeverity").EnumerateArray());
    }

    [Fact]
    public async Task Stats_Json_CanSummarizeMultipleProjectsWithoutWriting()
    {
        var otherRoot = Path.Combine(_root, "other-project");
        SeedJournalIn(
            _root,
            ("set", "sheets", "alice", _now.AddHours(-3), 2));
        SeedJournalIn(
            otherRoot,
            ("publish", "issue", "bob", _now.AddHours(-2), 1),
            ("set", "views", "bob", _now.AddHours(-1), 3));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteStatsAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            outputFormat: "json",
            output,
            _now,
            projectDirectories: new[] { otherRoot });

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("ledger-stats.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(3, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Contains(root.GetProperty("projectDirectories").EnumerateArray(), item =>
            item.GetString() == Path.GetFullPath(_root));
        Assert.Contains(root.GetProperty("projectDirectories").EnumerateArray(), item =>
            item.GetString() == Path.GetFullPath(otherRoot));
        Assert.Contains(root.GetProperty("byProject").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == Path.GetFullPath(_root) &&
            item.GetProperty("count").GetInt32() == 1);
        Assert.Contains(root.GetProperty("byProject").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == Path.GetFullPath(otherRoot) &&
            item.GetProperty("count").GetInt32() == 2);
        Assert.Contains(root.GetProperty("byOperator").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "bob" &&
            item.GetProperty("count").GetInt32() == 2);
    }

    [Fact]
    public void Stats_Parser_AcceptsRepeatedProjectOptions()
    {
        var command = LedgerCommand.Create();
        var parser = new Parser(command);

        var parseResult = parser.Parse(new[]
        {
            "stats",
            "--dir", _root,
            "--project", Path.Combine(_root, "a"),
            "--project", Path.Combine(_root, "b"),
            "--output", "json",
        });

        Assert.Empty(parseResult.Errors);
    }

    [Fact]
    public void Timeline_Parser_AcceptsRepeatedProjectOptions()
    {
        var command = LedgerCommand.Create();
        var parser = new Parser(command);

        var parseResult = parser.Parse(new[]
        {
            "timeline",
            "--dir", _root,
            "--project", Path.Combine(_root, "a"),
            "--project", Path.Combine(_root, "b"),
            "--bucket", "day",
            "--output", "json",
        });

        Assert.Empty(parseResult.Errors);
    }

    [Fact]
    public void StatsAndTimeline_Parser_AcceptsAnalyticsSnapshotOptions()
    {
        var command = LedgerCommand.Create();
        var parser = new Parser(command);

        Assert.Empty(parser.Parse(new[]
        {
            "stats",
            "--analytics-snapshot", ".revitcli/analytics/ledger-stats.json",
            "--output", "json",
        }).Errors);
        Assert.Empty(parser.Parse(new[]
        {
            "timeline",
            "--from-analytics-snapshot", ".revitcli/analytics/ledger-timeline.json",
            "--bucket", "day",
            "--output", "json",
        }).Errors);
    }

    [Fact]
    public async Task Stats_AnalyticsSnapshot_WritesAndReadsLocalJson()
    {
        SeedJournal(("set", "sheets", "alice", _now.AddHours(-3), 2));
        var snapshotPath = Path.Combine(".revitcli", "analytics", "ledger-stats.json");
        var writeOutput = new StringWriter();

        var writeExitCode = await LedgerCommand.ExecuteStatsAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            outputFormat: "json",
            writeOutput,
            _now,
            analyticsSnapshotPath: snapshotPath);

        Assert.Equal(0, writeExitCode);
        var fullSnapshotPath = Path.Combine(_root, snapshotPath);
        Assert.True(File.Exists(fullSnapshotPath));
        using var writtenJson = JsonDocument.Parse(File.ReadAllText(fullSnapshotPath));
        Assert.Equal("ledger-stats.v1", writtenJson.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(1, writtenJson.RootElement.GetProperty("summary").GetProperty("operationCount").GetInt32());

        var readOutput = new StringWriter();
        var readExitCode = await LedgerCommand.ExecuteStatsAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            outputFormat: "json",
            readOutput,
            _now,
            fromAnalyticsSnapshotPath: snapshotPath);

        Assert.Equal(0, readExitCode);
        using var readJson = JsonDocument.Parse(readOutput.ToString());
        Assert.Equal("ledger-stats.v1", readJson.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(1, readJson.RootElement.GetProperty("summary").GetProperty("operationCount").GetInt32());
    }

    [Fact]
    public async Task Timeline_AnalyticsSnapshot_WritesAndReadsLocalJson()
    {
        SeedJournal(("set", "sheets", "alice", _now.AddHours(-3), 2));
        var snapshotPath = Path.Combine(".revitcli", "analytics", "ledger-timeline.json");
        var writeOutput = new StringWriter();

        var writeExitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "day",
            outputFormat: "json",
            writeOutput,
            _now,
            analyticsSnapshotPath: snapshotPath);

        Assert.Equal(0, writeExitCode);
        var fullSnapshotPath = Path.Combine(_root, snapshotPath);
        Assert.True(File.Exists(fullSnapshotPath));
        using var writtenJson = JsonDocument.Parse(File.ReadAllText(fullSnapshotPath));
        Assert.Equal("ledger-timeline.v1", writtenJson.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(1, writtenJson.RootElement.GetProperty("summary").GetProperty("operationCount").GetInt32());

        var readOutput = new StringWriter();
        var readExitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "day",
            outputFormat: "json",
            readOutput,
            _now,
            fromAnalyticsSnapshotPath: snapshotPath);

        Assert.Equal(0, readExitCode);
        using var readJson = JsonDocument.Parse(readOutput.ToString());
        Assert.Equal("ledger-timeline.v1", readJson.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(1, readJson.RootElement.GetProperty("summary").GetProperty("operationCount").GetInt32());
    }

    [Fact]
    public async Task Stats_Json_CountsIssueSeverityForMissingReceipts()
    {
        SeedDeliveryManifestWithMissingReceipt(_now);
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteStatsAsync(
            _root,
            source: "deliveries",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.True(root.GetProperty("summary").GetProperty("errorIssueCount").GetInt32() > 0);
        Assert.Contains(root.GetProperty("issuesBySeverity").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "error" &&
            item.GetProperty("count").GetInt32() > 0);
        Assert.Contains(root.GetProperty("issuesBySource").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "deliveries" &&
            item.GetProperty("count").GetInt32() > 0);
        Assert.Contains(root.GetProperty("byReceiptStatus").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "missing" &&
            item.GetProperty("count").GetInt32() == 1);
    }

    [Fact]
    public async Task Stats_Json_ReusesReceiptStatusFilter()
    {
        SeedDeliveryManifestWithValidAndMissingReceipts(_now.AddHours(-1), _now);
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteStatsAsync(
            _root,
            source: "deliveries",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "missing",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("missing", root.GetProperty("query").GetProperty("receiptStatus").GetString());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("missingReceiptCount").GetInt32());
        Assert.Contains(root.GetProperty("byReceiptStatus").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "missing" &&
            item.GetProperty("count").GetInt32() == 1);
        Assert.DoesNotContain(root.GetProperty("byReceiptStatus").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "valid");
        Assert.Contains(root.GetProperty("issuesBySeverity").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "error" &&
            item.GetProperty("count").GetInt32() > 0);
        Assert.Contains(root.GetProperty("issuesBySource").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "deliveries" &&
            item.GetProperty("count").GetInt32() > 0);
    }

    [Fact]
    public async Task Stats_Json_MalformedArtifactsReturnIssueSeverityCountsWithoutFailing()
    {
        await SeedHistoryAsync();
        Directory.CreateDirectory(Path.Combine(_root, ".revitcli"));
        File.WriteAllText(Path.Combine(_root, ".revitcli", "journal.jsonl"), "{not json");

        var workflowDir = Path.Combine(_root, ".revitcli", "workflows", "receipts");
        Directory.CreateDirectory(workflowDir);
        File.WriteAllText(Path.Combine(workflowDir, "bad-workflow.json"), "{not json");

        var deliveryDir = Path.Combine(_root, ".revitcli", "deliveries");
        Directory.CreateDirectory(deliveryDir);
        File.WriteAllText(Path.Combine(deliveryDir, "manifest.jsonl"), "{not json");
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteStatsAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("ledger-stats.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("summary").GetProperty("issueCount").GetInt32() >= 3);
        Assert.True(root.GetProperty("summary").GetProperty("errorIssueCount").GetInt32() >= 3);
        Assert.Contains(root.GetProperty("issuesBySeverity").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "error" &&
            item.GetProperty("count").GetInt32() >= 3);
        var issuesBySource = root.GetProperty("issuesBySource").EnumerateArray().ToArray();
        Assert.Contains(issuesBySource, item =>
            item.GetProperty("name").GetString() == "journal" &&
            item.GetProperty("count").GetInt32() >= 1);
        Assert.Contains(issuesBySource, item =>
            item.GetProperty("name").GetString() == "deliveries" &&
            item.GetProperty("count").GetInt32() >= 1);
        Assert.Contains(issuesBySource, item =>
            item.GetProperty("name").GetString() == "workflows" &&
            item.GetProperty("count").GetInt32() >= 1);
        Assert.Contains(root.GetProperty("bySource").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "history" &&
            item.GetProperty("count").GetInt32() == 1);
        Assert.Contains(root.GetProperty("bySource").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "deliveries" &&
            item.GetProperty("count").GetInt32() == 1);
        Assert.Contains(root.GetProperty("bySource").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "workflows" &&
            item.GetProperty("count").GetInt32() == 1);
        Assert.Contains(root.GetProperty("byAction").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "deliverables.manifest-issue");
        Assert.Contains(root.GetProperty("byAction").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "workflow.receipt-issue");
    }

    [Fact]
    public async Task Stats_Json_ReusesQueryFilters()
    {
        SeedJournal(
            ("set", "sheets", "alice", _now.AddHours(-3), 2),
            ("publish", "issue", "bob", _now.AddHours(-1), 1),
            ("set", "old", "alice", _now.AddDays(-10), 9));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteStatsAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: "2d",
            action: "set",
            category: "sheets",
            operatorFilter: "alice",
            receiptStatus: "all",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal("2d", root.GetProperty("query").GetProperty("window").GetString());
        Assert.Equal("set", root.GetProperty("query").GetProperty("action").GetString());
        Assert.Equal("sheets", root.GetProperty("query").GetProperty("category").GetString());
        Assert.Equal("alice", root.GetProperty("query").GetProperty("operator").GetString());
    }

    [Fact]
    public async Task Stats_Json_TimeBoundsDropInvalidTimestampOperations()
    {
        SeedJournalRaw(
            JsonSerializer.Serialize(new
            {
                timestamp = _now.AddHours(-1).ToString("o"),
                action = "set",
                category = "sheets",
                user = "alice",
                @operator = "alice",
                affected = 1,
            }),
            JsonSerializer.Serialize(new
            {
                timestamp = "2026-05-23T12:00:00",
                action = "set",
                category = "sheets",
                user = "bob",
                @operator = "bob",
                affected = 1,
            }));

        (string Label, string? Since, string? Until)[] cases =
        [
            ("since", _now.AddDays(-1).ToString("o"), null),
            ("until", null, _now.AddDays(1).ToString("o")),
        ];

        foreach (var filter in cases)
        {
            var output = new StringWriter();
            var exitCode = await LedgerCommand.ExecuteStatsAsync(
                _root,
                source: "journal",
                since: filter.Since,
                until: filter.Until,
                window: null,
                action: null,
                category: null,
                operatorFilter: null,
                receiptStatus: "all",
                outputFormat: "json",
                output,
                _now);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(output.ToString());
            var root = json.RootElement;
            Assert.Equal(1, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
            Assert.Contains(root.GetProperty("byOperator").EnumerateArray(), item =>
                item.GetProperty("name").GetString() == "alice" &&
                item.GetProperty("count").GetInt32() == 1);
            Assert.DoesNotContain(root.GetProperty("byOperator").EnumerateArray(), item =>
                item.GetProperty("name").GetString() == "bob");
        }
    }

    [Fact]
    public async Task Stats_MarkdownAndTable_RenderHeadings()
    {
        var markdownOutput = new StringWriter();
        var markdownExitCode = await LedgerCommand.ExecuteStatsAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            outputFormat: "markdown",
            markdownOutput,
            _now);

        var tableOutput = new StringWriter();
        var tableExitCode = await LedgerCommand.ExecuteStatsAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            outputFormat: "table",
            tableOutput,
            _now);

        Assert.Equal(0, markdownExitCode);
        Assert.Equal(0, tableExitCode);
        Assert.Contains("# RevitCli Ledger Stats", markdownOutput.ToString());
        Assert.Contains("## By Source", markdownOutput.ToString());
        Assert.Contains("## By Category", markdownOutput.ToString());
        Assert.Contains("## By Operator", markdownOutput.ToString());
        Assert.Contains("## Issues By Source", markdownOutput.ToString());
        Assert.Contains("## Issues By Severity", markdownOutput.ToString());
        Assert.Contains("Ledger stats", tableOutput.ToString());
        Assert.Contains("By source:", tableOutput.ToString());
        Assert.Contains("By category:", tableOutput.ToString());
        Assert.Contains("By operator:", tableOutput.ToString());
        Assert.Contains("Issues by source:", tableOutput.ToString());
        Assert.Contains("Issues by severity:", tableOutput.ToString());
    }

    [Fact]
    public async Task Stats_OutputFormats_PreserveCounterSemantics()
    {
        await SeedHistoryAsync();
        SeedJournal(
            ("set", "sheets", "alice", _now.AddHours(-3), 2),
            ("publish", "issue", "bob", _now.AddHours(-2), 1));
        SeedWorkflowReceipt("pre-issue", "alice", _now.AddHours(-1), success: true);
        SeedDeliveryManifestWithMissingReceipt(_now);
        var jsonOutput = new StringWriter();
        var markdownOutput = new StringWriter();
        var tableOutput = new StringWriter();

        var jsonExitCode = await LedgerCommand.ExecuteStatsAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            outputFormat: "json",
            jsonOutput,
            _now);
        var markdownExitCode = await LedgerCommand.ExecuteStatsAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            outputFormat: "markdown",
            markdownOutput,
            _now);
        var tableExitCode = await LedgerCommand.ExecuteStatsAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            outputFormat: "table",
            tableOutput,
            _now);

        Assert.Equal(0, jsonExitCode);
        Assert.Equal(jsonExitCode, markdownExitCode);
        Assert.Equal(jsonExitCode, tableExitCode);
        using var json = JsonDocument.Parse(jsonOutput.ToString());
        var expected = StatsProjection.FromJson(json.RootElement);

        AssertStatsEqual(expected, ParseStatsMarkdown(markdownOutput.ToString()));
        AssertStatsEqual(expected, ParseStatsTable(tableOutput.ToString()));
    }

    [Fact]
    public async Task Stats_OutputFormats_DoNotWriteFiles()
    {
        await SeedHistoryAsync();
        SeedJournal(("set", "sheets", "alice", _now.AddHours(-3), 2));
        SeedWorkflowReceipt("pre-issue", "alice", _now.AddHours(-2), success: true);
        SeedDeliveryManifest(_now.AddHours(-1));
        var before = SnapshotFiles(_root);

        foreach (var outputFormat in new[] { "json", "markdown", "table" })
        {
            var output = new StringWriter();
            var exitCode = await LedgerCommand.ExecuteStatsAsync(
                _root,
                source: "all",
                since: null,
                until: null,
                window: null,
                action: null,
                category: null,
                operatorFilter: null,
                receiptStatus: "all",
                outputFormat,
                output,
                _now);

            Assert.Equal(0, exitCode);
            Assert.Equal(before, SnapshotFiles(_root));
        }
    }

    [Fact]
    public async Task Stats_InvalidOptions_ReturnFailure()
    {
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteStatsAsync(
            _root,
            source: "timeline",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(1, exitCode);
        Assert.Contains("--source must be", output.ToString());
    }

    [Fact]
    public async Task Timeline_Json_BucketsOperationsByDay()
    {
        SeedJournal(
            ("set", "sheets", "alice", _now.AddHours(-3), 2),
            ("publish", "issue", "bob", _now.AddDays(-1).AddHours(-1), 1),
            ("set", "sheets", "alice", _now.AddDays(-10), 4));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: "2d",
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "day",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("ledger-timeline.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("day", root.GetProperty("query").GetProperty("bucket").GetString());
        Assert.Equal("2d", root.GetProperty("query").GetProperty("window").GetString());
        Assert.Equal(2, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal(2, root.GetProperty("summary").GetProperty("bucketCount").GetInt32());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("unbucketedOperationCount").GetInt32());

        var buckets = root.GetProperty("buckets").EnumerateArray().ToArray();
        var expectedFirstBucket = new DateTimeOffset(
            _now.AddDays(-1).Year,
            _now.AddDays(-1).Month,
            _now.AddDays(-1).Day,
            0,
            0,
            0,
            TimeSpan.Zero);
        Assert.Equal(expectedFirstBucket.ToString("o"), buckets[0].GetProperty("bucketStartUtc").GetString());
        Assert.Equal(1, buckets[0].GetProperty("operationCount").GetInt32());
        Assert.Contains(buckets[0].GetProperty("byAction").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "publish" &&
            item.GetProperty("count").GetInt32() == 1);
        Assert.Contains(buckets[1].GetProperty("bySource").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "journal" &&
            item.GetProperty("count").GetInt32() == 1);
    }

    [Fact]
    public async Task Timeline_Json_CanBucketMultipleProjectsWithoutWriting()
    {
        var otherRoot = Path.Combine(_root, "other-project");
        SeedJournalIn(
            _root,
            ("set", "sheets", "alice", _now.AddHours(-3), 2));
        SeedJournalIn(
            otherRoot,
            ("publish", "issue", "bob", _now.AddHours(-2), 1),
            ("set", "views", "bob", _now.AddHours(-1), 3));
        var beforeRoot = SnapshotLocalFiles(_root);
        var beforeOther = SnapshotLocalFiles(otherRoot);
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "day",
            outputFormat: "json",
            output,
            _now,
            projectDirectories: new[] { otherRoot });

        Assert.Equal(0, exitCode);
        Assert.Equal(beforeRoot, SnapshotLocalFiles(_root));
        Assert.Equal(beforeOther, SnapshotLocalFiles(otherRoot));
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("ledger-timeline.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(3, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("bucketCount").GetInt32());
        Assert.Contains(root.GetProperty("projectDirectories").EnumerateArray(), item =>
            item.GetString() == Path.GetFullPath(_root));
        Assert.Contains(root.GetProperty("projectDirectories").EnumerateArray(), item =>
            item.GetString() == Path.GetFullPath(otherRoot));
        Assert.Contains(root.GetProperty("byProject").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == Path.GetFullPath(_root) &&
            item.GetProperty("count").GetInt32() == 1);
        Assert.Contains(root.GetProperty("byProject").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == Path.GetFullPath(otherRoot) &&
            item.GetProperty("count").GetInt32() == 2);
        var bucket = Assert.Single(root.GetProperty("buckets").EnumerateArray());
        Assert.Contains(bucket.GetProperty("byOperator").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "bob" &&
            item.GetProperty("count").GetInt32() == 2);

        var tableOutput = new StringWriter();
        var tableExitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "day",
            outputFormat: "table",
            tableOutput,
            _now,
            projectDirectories: new[] { otherRoot });
        var markdownOutput = new StringWriter();
        var markdownExitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "day",
            outputFormat: "markdown",
            markdownOutput,
            _now,
            projectDirectories: new[] { otherRoot });

        Assert.Equal(0, tableExitCode);
        Assert.Equal(0, markdownExitCode);
        Assert.Contains("Projects: 2", tableOutput.ToString());
        Assert.Contains("By project", tableOutput.ToString());
        Assert.Contains("By Project", markdownOutput.ToString());
    }

    [Fact]
    public async Task Timeline_Json_MergesAllSourcesAndExposesIssueSeverity()
    {
        await SeedHistoryAsync();
        SeedJournal(("set", "sheets", "alice", _now.AddHours(-3), 2));
        SeedWorkflowReceipt("pre-issue", "alice", _now.AddHours(-2), success: true);
        SeedDeliveryManifestWithMissingReceipt(_now.AddHours(-1));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "day",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("ledger-timeline.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(4, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("missingReceiptCount").GetInt32());

        var bucket = Assert.Single(root.GetProperty("buckets").EnumerateArray());
        var sources = bucket.GetProperty("bySource").EnumerateArray().ToArray();
        Assert.Contains(sources, item => item.GetProperty("name").GetString() == "history");
        Assert.Contains(sources, item => item.GetProperty("name").GetString() == "journal");
        Assert.Contains(sources, item => item.GetProperty("name").GetString() == "workflows");
        Assert.Contains(sources, item => item.GetProperty("name").GetString() == "deliveries");
        Assert.Contains(bucket.GetProperty("byReceiptStatus").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "missing" &&
            item.GetProperty("count").GetInt32() == 1);
        Assert.Contains(bucket.GetProperty("issuesBySeverity").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "error" &&
            item.GetProperty("count").GetInt32() > 0);
        Assert.Contains(root.GetProperty("issuesBySeverity").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "error" &&
            item.GetProperty("count").GetInt32() > 0);
    }

    [Fact]
    public async Task Timeline_Json_ReusesReceiptStatusFilter()
    {
        SeedDeliveryManifestWithValidAndMissingReceipts(_now.AddHours(-2), _now.AddHours(-1));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "deliveries",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "missing",
            bucket: "day",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("missing", root.GetProperty("query").GetProperty("receiptStatus").GetString());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("missingReceiptCount").GetInt32());
        Assert.Contains(root.GetProperty("issuesBySeverity").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "error" &&
            item.GetProperty("count").GetInt32() > 0);

        var bucket = Assert.Single(root.GetProperty("buckets").EnumerateArray());
        Assert.Equal(1, bucket.GetProperty("operationCount").GetInt32());
        Assert.Equal(1, bucket.GetProperty("missingReceiptCount").GetInt32());
        Assert.Contains(bucket.GetProperty("byReceiptStatus").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "missing" &&
            item.GetProperty("count").GetInt32() == 1);
        Assert.DoesNotContain(bucket.GetProperty("byReceiptStatus").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "valid");
        Assert.Contains(bucket.GetProperty("issuesBySeverity").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "error" &&
            item.GetProperty("count").GetInt32() > 0);
    }

    [Fact]
    public async Task Timeline_Json_FiltersBeforeBucketingByHour()
    {
        SeedJournal(
            ("set", "sheets", "alice", _now.AddHours(-2), 2),
            ("set", "sheets", "alice", _now.AddHours(-1), 3),
            ("publish", "issue", "bob", _now.AddHours(-1), 1));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: null,
            action: "set",
            category: "sheets",
            operatorFilter: "alice",
            receiptStatus: "all",
            bucket: "hour",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("hour", root.GetProperty("query").GetProperty("bucket").GetString());
        Assert.Equal("set", root.GetProperty("query").GetProperty("action").GetString());
        Assert.Equal("sheets", root.GetProperty("query").GetProperty("category").GetString());
        Assert.Equal("alice", root.GetProperty("query").GetProperty("operator").GetString());
        Assert.Equal(2, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal(2, root.GetProperty("summary").GetProperty("bucketCount").GetInt32());
        Assert.All(root.GetProperty("buckets").EnumerateArray(), bucket =>
        {
            Assert.Contains(bucket.GetProperty("byAction").EnumerateArray(), item =>
                item.GetProperty("name").GetString() == "set" &&
                item.GetProperty("count").GetInt32() == 1);
            Assert.Contains(bucket.GetProperty("byCategory").EnumerateArray(), item =>
                item.GetProperty("name").GetString() == "sheets" &&
                item.GetProperty("count").GetInt32() == 1);
            Assert.Contains(bucket.GetProperty("byOperator").EnumerateArray(), item =>
                item.GetProperty("name").GetString() == "alice" &&
                item.GetProperty("count").GetInt32() == 1);
        });
    }

    [Fact]
    public async Task Timeline_Json_MissingAndInvalidTimestampsAreUnbucketedWarnings()
    {
        SeedJournalRaw(JsonSerializer.Serialize(new
        {
            timestamp = "not-a-date",
            action = "set",
            category = "sheets",
            user = "alice",
            @operator = "alice",
        }));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "day",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("bucketCount").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("unbucketedOperationCount").GetInt32());
        Assert.Contains(root.GetProperty("issuesBySeverity").EnumerateArray(), issue =>
            issue.GetProperty("name").GetString() == "warning" &&
            issue.GetProperty("count").GetInt32() == 1);
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("severity").GetString() == "warning" &&
            issue.GetProperty("message").GetString()!.Contains("timeline bucket", StringComparison.OrdinalIgnoreCase) &&
            issue.GetProperty("message").GetString()!.Contains("explicit UTC offset", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Timeline_Json_TimestampWithoutOffsetIsUnbucketedWarning()
    {
        SeedJournalRaw(JsonSerializer.Serialize(new
        {
            timestamp = "2026-05-23T12:00:00",
            action = "set",
            category = "sheets",
            user = "alice",
            @operator = "alice",
        }));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "day",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("bucketCount").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("unbucketedOperationCount").GetInt32());
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("severity").GetString() == "warning" &&
            issue.GetProperty("message").GetString()!.Contains("explicit UTC offset", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Timeline_Json_WindowKeepsInvalidTimestampWarnings()
    {
        SeedJournalRaw(JsonSerializer.Serialize(new
        {
            timestamp = "2026-05-23T12:00:00",
            action = "set",
            category = "sheets",
            user = "alice",
            @operator = "alice",
        }));
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: "1d",
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "day",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("unbucketedOperationCount").GetInt32());
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("message").GetString()!.Contains("explicit UTC offset", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Timeline_Json_TimeBoundsKeepInvalidTimestampWarnings()
    {
        SeedJournalRaw(JsonSerializer.Serialize(new
        {
            timestamp = "2026-05-23T12:00:00",
            action = "set",
            category = "sheets",
            user = "alice",
            @operator = "alice",
        }));

        (string Label, string? Since, string? Until)[] cases =
        [
            ("since", _now.AddDays(-1).ToString("o"), null),
            ("until", null, _now.AddDays(1).ToString("o")),
        ];

        foreach (var filter in cases)
        {
            var output = new StringWriter();
            var exitCode = await LedgerCommand.ExecuteTimelineAsync(
                _root,
                source: "journal",
                since: filter.Since,
                until: filter.Until,
                window: null,
                action: null,
                category: null,
                operatorFilter: null,
                receiptStatus: "all",
                bucket: "day",
                outputFormat: "json",
                output,
                _now);

            Assert.Equal(0, exitCode);
            using var json = JsonDocument.Parse(output.ToString());
            var root = json.RootElement;
            Assert.Equal(1, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("unbucketedOperationCount").GetInt32());
            Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
                issue.GetProperty("message").GetString()!.Contains("explicit UTC offset", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Timeline_Json_AllMissingSourcesWarnsWithoutFailing()
    {
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "day",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("ledger-timeline.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("operationCount").GetInt32());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("bucketCount").GetInt32());
        Assert.True(root.GetProperty("summary").GetProperty("warningIssueCount").GetInt32() > 0);
        Assert.Contains(root.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("source").GetString() == "journal" &&
            issue.GetProperty("message").GetString()!.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Timeline_MarkdownAndTable_RenderHeadings()
    {
        SeedJournal(("set", "sheets", "alice", _now.AddHours(-1), 2));
        var markdownOutput = new StringWriter();
        var markdownExitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "day",
            outputFormat: "markdown",
            markdownOutput,
            _now);

        var tableOutput = new StringWriter();
        var tableExitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "journal",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "day",
            outputFormat: "table",
            tableOutput,
            _now);

        Assert.Equal(0, markdownExitCode);
        Assert.Equal(0, tableExitCode);
        var markdown = markdownOutput.ToString();
        var table = tableOutput.ToString();
        Assert.Contains("# RevitCli Ledger Timeline", markdown);
        Assert.Contains("| Bucket start UTC | Bucket end UTC | Operations | Sources | Actions | Categories | Operators | Receipt status | Issues |", markdown);
        Assert.Contains("| journal=1 | set=1 | sheets=1 | alice=1 | none=1 |", markdown);
        Assert.Contains("Ledger timeline", table);
        Assert.Contains("Bucket: day", table);
        Assert.Contains("bucketEnd=", table);
        Assert.Contains("categories=sheets=1", table);
        Assert.Contains("operators=alice=1", table);
        Assert.Contains("issues=0", table);
        Assert.Contains("issueSeverity=none", table);
    }

    [Fact]
    public async Task Timeline_OutputFormats_PreserveBucketSemantics()
    {
        await SeedHistoryAsync();
        SeedJournal(
            ("set", "sheets", "alice", _now.AddHours(-26), 2),
            ("publish", "issue", "bob", _now.AddHours(-1), 1));
        SeedWorkflowReceipt("pre-issue", "alice", _now.AddMinutes(-45), success: true);
        SeedDeliveryManifestWithMissingReceipt(_now.AddMinutes(-30));
        var jsonOutput = new StringWriter();
        var markdownOutput = new StringWriter();
        var tableOutput = new StringWriter();

        var jsonExitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "day",
            outputFormat: "json",
            jsonOutput,
            _now);
        var markdownExitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "day",
            outputFormat: "markdown",
            markdownOutput,
            _now);
        var tableExitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "day",
            outputFormat: "table",
            tableOutput,
            _now);

        Assert.Equal(0, jsonExitCode);
        Assert.Equal(jsonExitCode, markdownExitCode);
        Assert.Equal(jsonExitCode, tableExitCode);
        using var json = JsonDocument.Parse(jsonOutput.ToString());
        var expected = TimelineProjection.FromJson(json.RootElement);

        AssertTimelineEqual(expected, ParseTimelineMarkdown(markdownOutput.ToString()));
        AssertTimelineEqual(expected, ParseTimelineTable(tableOutput.ToString()));
    }

    [Fact]
    public async Task Timeline_OutputFormats_DoNotWriteFiles()
    {
        await SeedHistoryAsync();
        SeedJournal(("set", "sheets", "alice", _now.AddHours(-3), 2));
        SeedWorkflowReceipt("pre-issue", "alice", _now.AddHours(-2), success: true);
        SeedDeliveryManifest(_now.AddHours(-1));
        var before = SnapshotFiles(_root);

        foreach (var outputFormat in new[] { "json", "markdown", "table" })
        {
            var output = new StringWriter();
            var exitCode = await LedgerCommand.ExecuteTimelineAsync(
                _root,
                source: "all",
                since: null,
                until: null,
                window: null,
                action: null,
                category: null,
                operatorFilter: null,
                receiptStatus: "all",
                bucket: "day",
                outputFormat,
                output,
                _now);

            Assert.Equal(0, exitCode);
            Assert.Equal(before, SnapshotFiles(_root));
        }
    }

    [Fact]
    public async Task Timeline_InvalidOptions_ReturnFailure()
    {
        var output = new StringWriter();

        var exitCode = await LedgerCommand.ExecuteTimelineAsync(
            _root,
            source: "all",
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            bucket: "week",
            outputFormat: "json",
            output,
            _now);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bucket must be", output.ToString());
    }

    private async Task SeedHistoryAsync()
    {
        var store = HistoryStore.ForProject(_root);
        await store.InitAsync();
        await store.AppendAsync(
            Snapshot((1, "A", "h1")),
            "issue-baseline",
            _now.AddHours(-4));
    }

    private void SeedJournal(params (string Action, string Category, string Operator, DateTimeOffset Timestamp, int Affected)[] entries)
    {
        SeedJournalIn(_root, entries);
    }

    private static void SeedJournalIn(string root, params (string Action, string Category, string Operator, DateTimeOffset Timestamp, int Affected)[] entries)
    {
        var dir = Path.Combine(root, ".revitcli");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(
            Path.Combine(dir, "journal.jsonl"),
            entries.Select(entry =>
                JsonSerializer.Serialize(new
                {
                    timestamp = entry.Timestamp.ToString("o"),
                    action = entry.Action,
                    category = entry.Category,
                    user = entry.Operator,
                    @operator = entry.Operator,
                    affected = entry.Affected,
                })));
    }

    private void SeedJournalRaw(params string[] lines)
    {
        var dir = Path.Combine(_root, ".revitcli");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "journal.jsonl"), lines);
    }

    private void SeedWorkflowReceipt(string name, string operatorName, DateTimeOffset completedAt, bool success)
    {
        var receiptDir = Path.Combine(_root, ".revitcli", "workflows", "receipts");
        Directory.CreateDirectory(receiptDir);
        File.WriteAllText(
            Path.Combine(receiptDir, $"{name}-20260523T120000Z.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = "workflow-run-receipt.v1",
                action = "workflow.run",
                path = Path.Combine(_root, ".revitcli", "workflows", $"{name}.yml"),
                name,
                command = $"revitcli workflow run .revitcli/workflows/{name}.yml --yes",
                startedAtUtc = completedAt.AddMinutes(-5).ToString("o"),
                completedAtUtc = completedAt.ToString("o"),
                @operator = operatorName,
                machine = "workstation",
                dryRun = false,
                success,
                canRun = true,
                exitCode = success ? 0 : 7,
                issues = Array.Empty<object>(),
                steps = new[]
                {
                    new
                    {
                        index = 1,
                        name = "preflight",
                        mode = "read-only",
                        run = "revitcli issue preflight --output json",
                        requiresApproval = false,
                        status = success ? "ok" : "failed",
                        exitCode = success ? 0 : 7,
                    }
                }
            }));
    }

    private void SeedDeliveryManifest(DateTimeOffset timestamp)
    {
        var receiptDir = Path.Combine(_root, ".revitcli", "receipts");
        Directory.CreateDirectory(receiptDir);
        var receiptPath = Path.Combine(receiptDir, "publish-issue.json");
        File.WriteAllText(
            receiptPath,
            JsonSerializer.Serialize(new
            {
                schemaVersion = "publish-receipt.v1",
                action = "publish",
                success = true,
                dryRun = false,
                command = "revitcli publish issue",
            }));

        var manifestDir = Path.Combine(_root, ".revitcli", "deliveries");
        Directory.CreateDirectory(manifestDir);
        File.WriteAllLines(
            Path.Combine(manifestDir, "manifest.jsonl"),
            new[]
            {
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "delivery-manifest.v1",
                    kind = "publish",
                    success = true,
                    dryRun = false,
                    pipeline = "issue",
                    receiptPath,
                    timestamp = timestamp.ToString("o"),
                })
            });
    }

    private void SeedDeliveryManifestWithMismatchedReceiptHash(DateTimeOffset timestamp)
    {
        var receiptDir = Path.Combine(_root, ".revitcli", "receipts");
        Directory.CreateDirectory(receiptDir);
        var receiptPath = Path.Combine(receiptDir, "publish-issue.json");
        File.WriteAllText(
            receiptPath,
            JsonSerializer.Serialize(new
            {
                schemaVersion = "publish-receipt.v1",
                action = "publish",
                success = true,
                dryRun = false,
                command = "revitcli publish issue",
            }));

        var manifestDir = Path.Combine(_root, ".revitcli", "deliveries");
        Directory.CreateDirectory(manifestDir);
        File.WriteAllLines(
            Path.Combine(manifestDir, "manifest.jsonl"),
            new[]
            {
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "delivery-manifest.v1",
                    kind = "publish",
                    success = true,
                    dryRun = false,
                    pipeline = "issue",
                    receiptPath,
                    receiptHash = new string('0', 64),
                    timestamp = timestamp.ToString("o"),
                })
            });

        Assert.NotEqual(new string('0', 64), DeliveryManifestWriter.ComputeSha256Hex(receiptPath));
    }

    private void SeedDeliveryManifestWithMissingReceipt(DateTimeOffset timestamp)
    {
        var manifestDir = Path.Combine(_root, ".revitcli", "deliveries");
        Directory.CreateDirectory(manifestDir);
        File.WriteAllLines(
            Path.Combine(manifestDir, "manifest.jsonl"),
            new[]
            {
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "delivery-manifest.v1",
                    kind = "publish",
                    success = true,
                    dryRun = false,
                    pipeline = "issue",
                    receiptPath = Path.Combine(_root, ".revitcli", "receipts", "missing-publish.json"),
                    timestamp = timestamp.ToString("o"),
                })
            });
    }

    private void SeedDeliveryManifestWithValidAndMissingReceipts(DateTimeOffset validTimestamp, DateTimeOffset missingTimestamp)
    {
        var receiptDir = Path.Combine(_root, ".revitcli", "receipts");
        Directory.CreateDirectory(receiptDir);
        var receiptPath = Path.Combine(receiptDir, "publish-issue.json");
        File.WriteAllText(
            receiptPath,
            JsonSerializer.Serialize(new
            {
                schemaVersion = "publish-receipt.v1",
                action = "publish",
                success = true,
                dryRun = false,
                command = "revitcli publish issue",
            }));

        var manifestDir = Path.Combine(_root, ".revitcli", "deliveries");
        Directory.CreateDirectory(manifestDir);
        File.WriteAllLines(
            Path.Combine(manifestDir, "manifest.jsonl"),
            new[]
            {
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "delivery-manifest.v1",
                    kind = "publish",
                    success = true,
                    dryRun = false,
                    pipeline = "issue",
                    receiptPath,
                    timestamp = validTimestamp.ToString("o"),
                }),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "delivery-manifest.v1",
                    kind = "publish",
                    success = true,
                    dryRun = false,
                    pipeline = "issue",
                    receiptPath = Path.Combine(_root, ".revitcli", "receipts", "missing-publish.json"),
                    timestamp = missingTimestamp.ToString("o"),
                })
            });
    }

    private static ModelSnapshot Snapshot(params (long Id, string Mark, string Hash)[] walls)
    {
        var snapshot = new ModelSnapshot
        {
            SchemaVersion = 1,
            TakenAt = "2026-05-23T00:00:00Z",
            Revit = new SnapshotRevit
            {
                Version = "2026",
                Document = "Sample.rvt",
                DocumentPath = "C:/models/Sample.rvt",
            },
            Summary = new SnapshotSummary
            {
                ElementCounts = new Dictionary<string, int> { ["walls"] = walls.Length },
                SheetCount = 1,
                ScheduleCount = 1,
            },
        };

        snapshot.Categories["walls"] = walls
            .Select(item => new SnapshotElement
            {
                Id = item.Id,
                Name = $"W{item.Id}",
                Parameters = new Dictionary<string, string> { ["Mark"] = item.Mark },
                Hash = item.Hash,
            })
            .ToList();
        return snapshot;
    }

    private static SortedDictionary<string, string> SnapshotFiles(string root)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/') + "/";
            result[relative] = "<dir>";
        }

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            result[relative] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }

        return result;
    }

    private static IReadOnlyList<QueryProjection> ParseQueryMarkdownRows(string markdown)
    {
        var rows = new List<QueryProjection>();
        var lines = SplitLines(markdown);
        var headerIndex = Array.FindIndex(lines, line =>
            string.Equals(line.Trim(), "| Timestamp | Source | Action | Receipt | Artifact |", StringComparison.Ordinal));
        if (headerIndex < 0 || headerIndex + 1 >= lines.Length)
            return rows;

        foreach (var line in lines.Skip(headerIndex + 2))
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("|", StringComparison.Ordinal))
                break;

            var cells = SplitMarkdownTableRow(line);
            if (cells.Length == 5)
            {
                rows.Add(new QueryProjection(cells[0], cells[1], cells[2], cells[3], cells[4]));
            }
        }

        return rows;
    }

    private static IReadOnlyList<QueryProjection> ParseQueryTableRows(string table, int expectedCount)
    {
        var rows = new List<QueryProjection>();
        foreach (var line in SplitLines(table).Skip(3).Take(expectedCount))
        {
            if (!TryParseQueryTableRow(line, out var row))
                return Array.Empty<QueryProjection>();

            rows.Add(row);
        }

        return rows;
    }

    private static bool TryParseQueryTableRow(string line, out QueryProjection row)
    {
        row = new QueryProjection("", "", "", "", "");
        var columns = line.Split(new[] { ' ' }, 5, StringSplitOptions.RemoveEmptyEntries);
        if (columns.Length != 5)
        {
            return false;
        }

        row = new QueryProjection(
            columns[0],
            columns[1],
            columns[2],
            columns[3],
            columns[4]);
        return true;
    }

    private static string[] SplitMarkdownTableRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("|", StringComparison.Ordinal))
            trimmed = trimmed[1..];
        if (trimmed.EndsWith("|", StringComparison.Ordinal))
            trimmed = trimmed[..^1];

        var cells = new List<string>();
        var cell = new StringBuilder();
        for (var i = 0; i < trimmed.Length; i++)
        {
            var current = trimmed[i];
            if (current == '\\' && i + 1 < trimmed.Length && trimmed[i + 1] == '|')
            {
                cell.Append('|');
                i++;
                continue;
            }

            if (current == '|')
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
                continue;
            }

            cell.Append(current);
        }

        cells.Add(cell.ToString().Trim());
        return cells.ToArray();
    }

    private static string[] SplitLines(string value) =>
        value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

    private static ValidationProjection ParseValidationMarkdown(string markdown)
    {
        var lines = SplitLines(markdown);
        var valid = ParseMarkdownBulletBool(lines, "Valid");
        var operations = ParseMarkdownBulletInt(lines, "Operations");
        var errors = ParseMarkdownBulletInt(lines, "Errors");
        var warnings = ParseMarkdownBulletInt(lines, "Warnings");
        var checks = ParseMarkdownRows(lines, "| Status | Check | Evidence |")
            .Select(cells => new ValidationCheckProjection(
                StripInlineCode(cells[0]).ToLowerInvariant(),
                StripInlineCode(cells[1]),
                cells[2]))
            .ToArray();
        var issues = lines
            .Where(line => line.StartsWith("- `", StringComparison.Ordinal) &&
                           !line.Contains("None.", StringComparison.Ordinal))
            .Select(ParseValidationMarkdownIssue)
            .Where(issue => issue is not null)
            .Select(issue => issue!)
            .ToArray();

        return new ValidationProjection(valid, operations, errors, warnings, checks, issues);
    }

    private static ValidationProjection ParseValidationTable(string table)
    {
        var lines = SplitLines(table);
        var summaryLine = lines.First(line => line.StartsWith("Valid:", StringComparison.Ordinal));
        var summaryParts = summaryLine.Split(';', StringSplitOptions.TrimEntries);
        var valid = bool.Parse(summaryParts[0]["Valid:".Length..].Trim());
        var operations = ParseNamedInt(summaryParts[1], "operations");
        var errors = ParseNamedInt(summaryParts[2], "errors");
        var warnings = ParseNamedInt(summaryParts[3], "warnings");
        var issueIndex = Array.FindIndex(lines, line => string.Equals(line.Trim(), "Issues:", StringComparison.Ordinal));
        var checkLines = lines
            .Skip(3)
            .Take(issueIndex < 0 ? int.MaxValue : issueIndex - 3)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        var checks = checkLines
            .Select(line =>
            {
                var columns = line.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                return new ValidationCheckProjection(columns[0].ToLowerInvariant(), columns[1], columns[2]);
            })
            .ToArray();
        var issues = issueIndex < 0
            ? Array.Empty<ValidationIssueProjection>()
            : lines.Skip(issueIndex + 1)
                .Where(line => line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
                .Select(ParseValidationTableIssue)
                .ToArray();

        return new ValidationProjection(valid, operations, errors, warnings, checks, issues);
    }

    private static StatsProjection ParseStatsMarkdown(string markdown)
    {
        var lines = SplitLines(markdown);
        return new StatsProjection(
            ParseMarkdownBulletInt(lines, "Operations"),
            ParseMarkdownBulletInt(lines, "Issues"),
            ParseMarkdownBulletInt(lines, "Missing receipts"),
            ParseMarkdownBulletInt(lines, "Unreadable receipts"),
            ParseMarkdownCountSection(lines, "By Source"),
            ParseMarkdownCountSection(lines, "By Action"),
            ParseMarkdownCountSection(lines, "By Category"),
            ParseMarkdownCountSection(lines, "By Operator"),
            ParseMarkdownCountSection(lines, "By Receipt Status"),
            ParseMarkdownCountSection(lines, "Issues By Source"),
            ParseMarkdownCountSection(lines, "Issues By Severity"));
    }

    private static StatsProjection ParseStatsTable(string table)
    {
        var lines = SplitLines(table);
        var summaryLine = lines.First(line => line.StartsWith("Operations:", StringComparison.Ordinal));
        var summaryParts = summaryLine.Split(';', StringSplitOptions.TrimEntries);
        return new StatsProjection(
            ParseLeadingInt(summaryParts[0], "Operations:"),
            ParseNamedInt(summaryParts[1], "issues"),
            ParseNamedInt(summaryParts[2], "missingReceipts"),
            ParseNamedInt(summaryParts[3], "unreadableReceipts"),
            ParseTableCountSection(lines, "By source:"),
            ParseTableCountSection(lines, "By action:"),
            ParseTableCountSection(lines, "By category:"),
            ParseTableCountSection(lines, "By operator:"),
            ParseTableCountSection(lines, "By receipt:"),
            ParseTableCountSection(lines, "Issues by source:"),
            ParseTableCountSection(lines, "Issues by severity:"));
    }

    private static TimelineProjection ParseTimelineMarkdown(string markdown)
    {
        var lines = SplitLines(markdown);
        var rows = ParseMarkdownRows(lines, "| Bucket start UTC | Bucket end UTC | Operations | Sources | Actions | Categories | Operators | Receipt status | Issues |")
            .Where(cells => !string.Equals(cells[0], "none", StringComparison.OrdinalIgnoreCase))
            .Select(cells => new TimelineBucketProjection(
                cells[0],
                cells[1],
                int.Parse(cells[2], CultureInfo.InvariantCulture),
                ParseInlineCounts(cells[3]),
                ParseInlineCounts(cells[4]),
                ParseInlineCounts(cells[5]),
                ParseInlineCounts(cells[6]),
                ParseInlineCounts(cells[7]),
                int.Parse(cells[8], CultureInfo.InvariantCulture)))
            .ToArray();
        return new TimelineProjection(
            ParseMarkdownBullet("Bucket"),
            ParseMarkdownBulletInt(lines, "Operations"),
            ParseMarkdownBulletInt(lines, "Buckets"),
            ParseMarkdownBulletInt(lines, "Issues"),
            ParseMarkdownBulletInt(lines, "Unbucketed operations"),
            rows,
            ParseMarkdownCountSection(lines, "Issues By Severity"));

        string ParseMarkdownBullet(string label)
        {
            var prefix = $"- {label}: `";
            var line = lines.First(line => line.StartsWith(prefix, StringComparison.Ordinal));
            return line[prefix.Length..^1];
        }
    }

    private static TimelineProjection ParseTimelineTable(string table)
    {
        var lines = SplitLines(table);
        var summaryLine = lines.First(line => line.StartsWith("Bucket:", StringComparison.Ordinal));
        var summaryParts = summaryLine.Split(';', StringSplitOptions.TrimEntries);
        var buckets = new List<TimelineBucketProjection>();
        foreach (var line in lines.Skip(3))
        {
            if (string.IsNullOrWhiteSpace(line) ||
                line.EndsWith(":", StringComparison.Ordinal) ||
                line.TrimStart().StartsWith("- ", StringComparison.Ordinal) ||
                !line.Contains(" operations=", StringComparison.Ordinal) ||
                !line.Contains(" sources=", StringComparison.Ordinal))
                continue;

            var firstSpace = line.IndexOf(' ');
            if (firstSpace < 0)
                continue;

            var bucketEnd = ReadLabeledSegment(line, "bucketEnd", "operations");
            buckets.Add(new TimelineBucketProjection(
                line[..firstSpace],
                bucketEnd,
                int.Parse(ReadLabeledSegment(line, "operations", "missingReceipts"), CultureInfo.InvariantCulture),
                ParseInlineCounts(ReadLabeledSegment(line, "sources", "actions")),
                ParseInlineCounts(ReadLabeledSegment(line, "actions", "categories")),
                ParseInlineCounts(ReadLabeledSegment(line, "categories", "operators")),
                ParseInlineCounts(ReadLabeledSegment(line, "operators", "receipts")),
                ParseInlineCounts(ReadLabeledSegment(line, "receipts", "issues")),
                int.Parse(ReadLabeledSegment(line, "issues", "issueSeverity"), CultureInfo.InvariantCulture)));
        }

        return new TimelineProjection(
            summaryParts[0]["Bucket:".Length..].Trim(),
            ParseNamedInt(summaryParts[1], "operations"),
            ParseNamedInt(summaryParts[2], "buckets"),
            ParseNamedInt(summaryParts[3], "issues"),
            ParseNamedInt(summaryParts[4], "unbucketed"),
            buckets.ToArray(),
            ParseTableCountSection(lines, "Issues by severity:"));
    }

    private static IReadOnlyList<string[]> ParseMarkdownRows(string[] lines, string header)
    {
        var headerIndex = Array.FindIndex(lines, line =>
            string.Equals(line.Trim(), header, StringComparison.Ordinal));
        if (headerIndex < 0 || headerIndex + 1 >= lines.Length)
            return Array.Empty<string[]>();

        var rows = new List<string[]>();
        foreach (var line in lines.Skip(headerIndex + 2))
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("|", StringComparison.Ordinal))
                break;

            rows.Add(SplitMarkdownTableRow(line));
        }

        return rows;
    }

    private static SortedDictionary<string, int> ParseMarkdownCountSection(string[] lines, string title)
    {
        var rows = ParseMarkdownRows(lines, $"| Name | Count |");
        var titleIndex = Array.FindIndex(lines, line => string.Equals(line.Trim(), $"## {title}", StringComparison.Ordinal));
        if (titleIndex < 0)
            return new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var headerIndex = Array.FindIndex(lines, titleIndex, line => string.Equals(line.Trim(), "| Name | Count |", StringComparison.Ordinal));
        if (headerIndex < 0)
            return new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(headerIndex + 2))
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("|", StringComparison.Ordinal))
                break;

            var cells = SplitMarkdownTableRow(line);
            if (cells.Length == 2 &&
                (!string.Equals(cells[0], "none", StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(cells[1], "0", StringComparison.OrdinalIgnoreCase)))
            {
                counts[cells[0]] = int.Parse(cells[1], CultureInfo.InvariantCulture);
            }
        }

        _ = rows;
        return counts;
    }

    private static SortedDictionary<string, int> ParseTableCountSection(string[] lines, string title)
    {
        var index = Array.FindIndex(lines, line => string.Equals(line.Trim(), title, StringComparison.Ordinal));
        var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (index < 0)
            return counts;

        foreach (var line in lines.Skip(index + 1))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) ||
                trimmed.EndsWith(":", StringComparison.Ordinal) ||
                trimmed.Equals("Issues:", StringComparison.Ordinal))
            {
                break;
            }

            if (!trimmed.StartsWith("- ", StringComparison.Ordinal) ||
                trimmed.Equals("- none", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = trimmed[2..].Split(':', 2, StringSplitOptions.TrimEntries);
            counts[parts[0]] = int.Parse(parts[1], CultureInfo.InvariantCulture);
        }

        return counts;
    }

    private static SortedDictionary<string, int> ParseInlineCounts(string value)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
            return counts;

        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            counts[pair[0]] = int.Parse(pair[1], CultureInfo.InvariantCulture);
        }

        return counts;
    }

    private static SortedDictionary<string, int> ParsePrefixedInlineCounts(string value, string prefix)
    {
        var expectedPrefix = prefix + "=";
        if (!value.StartsWith(expectedPrefix, StringComparison.Ordinal))
            return new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        return ParseInlineCounts(value[expectedPrefix.Length..]);
    }

    private static string ReadLabeledSegment(string line, string label, string? nextLabel)
    {
        var prefix = label + "=";
        var start = line.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            return "";

        start += prefix.Length;
        if (string.IsNullOrWhiteSpace(nextLabel))
            return line[start..].Trim();

        var end = line.IndexOf(" " + nextLabel + "=", start, StringComparison.Ordinal);
        return end < 0
            ? line[start..].Trim()
            : line[start..end].Trim();
    }

    private static int ParseMarkdownBulletInt(string[] lines, string label) =>
        int.Parse(ParseMarkdownBulletCode(lines, label), CultureInfo.InvariantCulture);

    private static bool ParseMarkdownBulletBool(string[] lines, string label) =>
        bool.Parse(ParseMarkdownBulletCode(lines, label));

    private static string ParseMarkdownBulletCode(string[] lines, string label)
    {
        var prefix = $"- {label}: `";
        var line = lines.First(line => line.StartsWith(prefix, StringComparison.Ordinal));
        return line[prefix.Length..^1];
    }

    private static int ParseLeadingInt(string value, string prefix) =>
        int.Parse(value[prefix.Length..].Trim(), CultureInfo.InvariantCulture);

    private static int ParseNamedInt(string value, string name)
    {
        if (string.IsNullOrEmpty(name))
            return int.Parse(value, CultureInfo.InvariantCulture);

        var prefix = name + "=";
        return int.Parse(value[prefix.Length..].Trim(), CultureInfo.InvariantCulture);
    }

    private static string StripInlineCode(string value) =>
        value.Length >= 2 && value[0] == '`' && value[^1] == '`'
            ? value[1..^1]
            : value;

    private static ValidationIssueProjection? ParseValidationMarkdownIssue(string line)
    {
        var parts = line.Split('`', StringSplitOptions.None);
        if (parts.Length < 6)
            return null;

        var sourceAndMessage = line.Split(": ", 2, StringSplitOptions.None);
        if (sourceAndMessage.Length != 2)
            return null;

        return new ValidationIssueProjection(
            parts[1].ToLowerInvariant(),
            parts[3],
            parts[5],
            sourceAndMessage[1]);
    }

    private static ValidationIssueProjection ParseValidationTableIssue(string line)
    {
        var trimmed = line.Trim();
        var body = trimmed[2..];
        var sourceAndMessage = body.Split(": ", 2, StringSplitOptions.None);
        var columns = sourceAndMessage[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        return new ValidationIssueProjection(
            columns[0].ToLowerInvariant(),
            columns[1],
            columns[2],
            sourceAndMessage[1]);
    }

    private static void AssertValidationEqual(ValidationProjection expected, ValidationProjection actual)
    {
        Assert.Equal(expected.Valid, actual.Valid);
        Assert.Equal(expected.OperationCount, actual.OperationCount);
        Assert.Equal(expected.ErrorCount, actual.ErrorCount);
        Assert.Equal(expected.WarningCount, actual.WarningCount);
        Assert.Equal(expected.Checks, actual.Checks);
        Assert.Equal(expected.Issues, actual.Issues);
    }

    private static void AssertStatsEqual(StatsProjection expected, StatsProjection actual)
    {
        Assert.Equal(expected.OperationCount, actual.OperationCount);
        Assert.Equal(expected.IssueCount, actual.IssueCount);
        Assert.Equal(expected.MissingReceiptCount, actual.MissingReceiptCount);
        Assert.Equal(expected.UnreadableReceiptCount, actual.UnreadableReceiptCount);
        Assert.Equal(expected.BySource.ToArray(), actual.BySource.ToArray());
        Assert.Equal(expected.ByAction.ToArray(), actual.ByAction.ToArray());
        Assert.Equal(expected.ByCategory.ToArray(), actual.ByCategory.ToArray());
        Assert.Equal(expected.ByOperator.ToArray(), actual.ByOperator.ToArray());
        Assert.Equal(expected.ByReceiptStatus.ToArray(), actual.ByReceiptStatus.ToArray());
        Assert.Equal(expected.IssuesBySource.ToArray(), actual.IssuesBySource.ToArray());
        Assert.Equal(expected.IssuesBySeverity.ToArray(), actual.IssuesBySeverity.ToArray());
    }

    private static void AssertTimelineEqual(TimelineProjection expected, TimelineProjection actual)
    {
        Assert.Equal(expected.Bucket, actual.Bucket);
        Assert.Equal(expected.OperationCount, actual.OperationCount);
        Assert.Equal(expected.BucketCount, actual.BucketCount);
        Assert.Equal(expected.IssueCount, actual.IssueCount);
        Assert.Equal(expected.UnbucketedOperationCount, actual.UnbucketedOperationCount);
        Assert.Equal(expected.IssuesBySeverity.ToArray(), actual.IssuesBySeverity.ToArray());
        Assert.Equal(expected.Buckets.Count, actual.Buckets.Count);
        for (var i = 0; i < expected.Buckets.Count; i++)
            AssertTimelineBucketEqual(expected.Buckets[i], actual.Buckets[i]);
    }

    private static void AssertTimelineBucketEqual(TimelineBucketProjection expected, TimelineBucketProjection actual)
    {
        Assert.Equal(expected.BucketStartUtc, actual.BucketStartUtc);
        if (!string.IsNullOrWhiteSpace(actual.BucketEndUtc))
            Assert.Equal(expected.BucketEndUtc, actual.BucketEndUtc);
        Assert.Equal(expected.OperationCount, actual.OperationCount);
        Assert.Equal(expected.BySource.ToArray(), actual.BySource.ToArray());
        Assert.Equal(expected.ByAction.ToArray(), actual.ByAction.ToArray());
        Assert.Equal(expected.ByCategory.ToArray(), actual.ByCategory.ToArray());
        Assert.Equal(expected.ByOperator.ToArray(), actual.ByOperator.ToArray());
        Assert.Equal(expected.ByReceiptStatus.ToArray(), actual.ByReceiptStatus.ToArray());
        Assert.Equal(expected.IssueCount, actual.IssueCount);
    }

    private sealed record QueryProjection(
        string Timestamp,
        string Source,
        string Action,
        string Receipt,
        string Artifact)
    {
        public static QueryProjection FromJson(JsonElement operation) =>
            new(
                ReadString(operation, "timestamp"),
                ReadString(operation, "source"),
                ReadString(operation, "action"),
                ReadString(operation, "receiptStatus"),
                ReadString(operation, "artifactPath"));

        private static string ReadString(JsonElement element, string propertyName)
        {
            var value = element.GetProperty(propertyName).GetString();
            return string.IsNullOrWhiteSpace(value) ? "n/a" : value;
        }
    }

    private sealed record ValidationProjection(
        bool Valid,
        int OperationCount,
        int ErrorCount,
        int WarningCount,
        IReadOnlyList<ValidationCheckProjection> Checks,
        IReadOnlyList<ValidationIssueProjection> Issues)
    {
        public static ValidationProjection FromJson(JsonElement root) =>
            new(
                root.GetProperty("valid").GetBoolean(),
                root.GetProperty("summary").GetProperty("operationCount").GetInt32(),
                root.GetProperty("summary").GetProperty("errorCount").GetInt32(),
                root.GetProperty("summary").GetProperty("warningCount").GetInt32(),
                root.GetProperty("checks").EnumerateArray()
                    .Select(check => new ValidationCheckProjection(
                        check.GetProperty("status").GetString()!,
                        check.GetProperty("id").GetString()!,
                        check.GetProperty("evidence").GetString()!))
                    .ToArray(),
                root.GetProperty("issues").EnumerateArray()
                    .Select(issue => new ValidationIssueProjection(
                        issue.GetProperty("severity").GetString()!,
                        issue.GetProperty("code").GetString()!,
                        issue.GetProperty("source").GetString()!,
                        issue.GetProperty("message").GetString()!))
                    .ToArray());
    }

    private sealed record ValidationCheckProjection(string Status, string Id, string Evidence);

    private sealed record ValidationIssueProjection(string Severity, string Code, string Source, string Message);

    private sealed record StatsProjection(
        int OperationCount,
        int IssueCount,
        int MissingReceiptCount,
        int UnreadableReceiptCount,
        SortedDictionary<string, int> BySource,
        SortedDictionary<string, int> ByAction,
        SortedDictionary<string, int> ByCategory,
        SortedDictionary<string, int> ByOperator,
        SortedDictionary<string, int> ByReceiptStatus,
        SortedDictionary<string, int> IssuesBySource,
        SortedDictionary<string, int> IssuesBySeverity)
    {
        public static StatsProjection FromJson(JsonElement root) =>
            new(
                root.GetProperty("summary").GetProperty("operationCount").GetInt32(),
                root.GetProperty("summary").GetProperty("issueCount").GetInt32(),
                root.GetProperty("summary").GetProperty("missingReceiptCount").GetInt32(),
                root.GetProperty("summary").GetProperty("unreadableReceiptCount").GetInt32(),
                CountDictionary(root.GetProperty("bySource")),
                CountDictionary(root.GetProperty("byAction")),
                CountDictionary(root.GetProperty("byCategory")),
                CountDictionary(root.GetProperty("byOperator")),
                CountDictionary(root.GetProperty("byReceiptStatus")),
                CountDictionary(root.GetProperty("issuesBySource")),
                CountDictionary(root.GetProperty("issuesBySeverity")));
    }

    private sealed record TimelineProjection(
        string Bucket,
        int OperationCount,
        int BucketCount,
        int IssueCount,
        int UnbucketedOperationCount,
        IReadOnlyList<TimelineBucketProjection> Buckets,
        SortedDictionary<string, int> IssuesBySeverity)
    {
        public static TimelineProjection FromJson(JsonElement root) =>
            new(
                root.GetProperty("query").GetProperty("bucket").GetString()!,
                root.GetProperty("summary").GetProperty("operationCount").GetInt32(),
                root.GetProperty("summary").GetProperty("bucketCount").GetInt32(),
                root.GetProperty("summary").GetProperty("issueCount").GetInt32(),
                root.GetProperty("summary").GetProperty("unbucketedOperationCount").GetInt32(),
                root.GetProperty("buckets").EnumerateArray()
                    .Select(bucket => new TimelineBucketProjection(
                        bucket.GetProperty("bucketStartUtc").GetString()!,
                        bucket.GetProperty("bucketEndUtc").GetString()!,
                        bucket.GetProperty("operationCount").GetInt32(),
                        CountDictionary(bucket.GetProperty("bySource")),
                        CountDictionary(bucket.GetProperty("byAction")),
                        CountDictionary(bucket.GetProperty("byCategory")),
                        CountDictionary(bucket.GetProperty("byOperator")),
                        CountDictionary(bucket.GetProperty("byReceiptStatus")),
                        bucket.GetProperty("issueCount").GetInt32()))
                    .ToArray(),
                CountDictionary(root.GetProperty("issuesBySeverity")));
    }

    private sealed record TimelineBucketProjection(
        string BucketStartUtc,
        string BucketEndUtc,
        int OperationCount,
        SortedDictionary<string, int> BySource,
        SortedDictionary<string, int> ByAction,
        SortedDictionary<string, int> ByCategory,
        SortedDictionary<string, int> ByOperator,
        SortedDictionary<string, int> ByReceiptStatus,
        int IssueCount);

    private static SortedDictionary<string, int> CountDictionary(JsonElement array)
    {
        var result = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in array.EnumerateArray())
            result[item.GetProperty("name").GetString()!] = item.GetProperty("count").GetInt32();

        return result;
    }

    private static SortedDictionary<string, string> SnapshotLocalFiles(string root)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(root))
            return result;

        foreach (var path in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            result[Path.GetRelativePath(root, path).Replace('\\', '/') + "/"] = "<dir>";
        }

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            result[relative] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }

        return result;
    }
}
