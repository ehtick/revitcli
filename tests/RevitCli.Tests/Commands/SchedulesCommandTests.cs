using System.Net;
using System.Text;
using System.Text.Json;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

[Collection("Sequential")]
public sealed class SchedulesCommandTests : IDisposable
{
    private readonly string _root;
    private readonly string _previousDirectory;

    public SchedulesCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-schedules-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _previousDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_root);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_previousDirectory);
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Ensure_DryRun_WritesScheduleEnsurePlan()
    {
        var specPath = WriteIssueSpec();
        var planPath = Path.Combine(_root, ".revitcli", "plans", "schedule-ensure.json");
        var client = MakeClient(
            schedules: new[]
            {
                new ScheduleInfo { Id = 100, Name = "Door Schedule", Category = "Doors", FieldCount = 2, RowCount = 4 }
            },
            exportData: null);
        var output = new StringWriter();

        var exitCode = await SchedulesCommand.ExecuteEnsureAsync(
            client,
            specPath,
            planPath,
            dryRun: true,
            mode: "sync-fields",
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(planPath));
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        Assert.Equal("schedule-ensure-plan.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("sync-fields", json.RootElement.GetProperty("mode").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("summary").GetProperty("actionCount").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("baselineCount").GetInt32());
        var baseline = Assert.Single(json.RootElement.GetProperty("baselines").EnumerateArray());
        Assert.True(baseline.TryGetProperty("fields", out _));
        Assert.True(baseline.TryGetProperty("filter", out _));
        Assert.True(baseline.TryGetProperty("sort", out _));
        Assert.Contains(json.RootElement.GetProperty("actions").EnumerateArray(), action =>
            action.GetProperty("action").GetString() == "sync-fields" &&
            action.GetProperty("name").GetString() == "Door Schedule");
        Assert.Contains(json.RootElement.GetProperty("actions").EnumerateArray(), action =>
            action.GetProperty("action").GetString() == "create" &&
            action.GetProperty("name").GetString() == "Window Schedule");
        Assert.Contains("\"schemaVersion\": \"schedule-ensure-plan.v1\"", output.ToString());

        var showOutput = new StringWriter();
        var showExitCode = await PlanCommand.ExecuteShowAsync(planPath, "json", showOutput);
        Assert.Equal(0, showExitCode);
        using var showJson = JsonDocument.Parse(showOutput.ToString());
        Assert.Equal("plan-summary.v1", showJson.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("schedule-ensure", showJson.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Ensure_RejectsRealWritePath()
    {
        var output = new StringWriter();

        var exitCode = await SchedulesCommand.ExecuteEnsureAsync(
            MakeClient(Array.Empty<ScheduleInfo>(), null),
            "issue.yml",
            "plan.json",
            dryRun: false,
            mode: "create-only",
            outputFormat: "table",
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("only creates reviewed plans", output.ToString());
    }

    [Fact]
    public async Task Ensure_RejectsSpecWithMissingFields()
    {
        var specPath = WriteScheduleSpec("""
schemaVersion: schedule-spec.v1
set: issue
schedules:
  - name: Door Schedule
    category: Doors
""");
        var output = new StringWriter();

        var exitCode = await SchedulesCommand.ExecuteEnsureAsync(
            MakeClient(Array.Empty<ScheduleInfo>(), null),
            specPath,
            Path.Combine(_root, "plan.json"),
            dryRun: true,
            mode: "create-only",
            outputFormat: "table",
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires fields", output.ToString());
    }

    [Fact]
    public async Task Ensure_AllowsDuplicateExportFileNames()
    {
        var specPath = WriteScheduleSpec("""
schemaVersion: schedule-spec.v1
set: issue
schedules:
  - name: Door/Schedule
    category: Doors
    fields: [Mark]
  - name: Door_Schedule
    category: Doors
    fields: [Level]
""");
        var planPath = Path.Combine(_root, "schedule-ensure.json");
        var output = new StringWriter();

        var exitCode = await SchedulesCommand.ExecuteEnsureAsync(
            MakeClient(Array.Empty<ScheduleInfo>(), null),
            specPath,
            planPath,
            dryRun: true,
            mode: "create-only",
            outputFormat: "table",
            output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(planPath));
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        Assert.Equal(2, json.RootElement.GetProperty("summary").GetProperty("actionCount").GetInt32());
        Assert.DoesNotContain("duplicate export file name", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BatchExport_RejectsDuplicateExportFileNames()
    {
        WriteScheduleSpec("""
schemaVersion: schedule-spec.v1
set: issue
schedules:
  - name: Door Schedule
    category: Doors
    fields: [Mark]
  - name: "Door Schedule"
    category: Doors
    fields: [Level]
""");
        var output = new StringWriter();

        var exitCode = await SchedulesCommand.ExecuteBatchExportAsync(
            MakeClient(Array.Empty<ScheduleInfo>(), null),
            "issue",
            Path.Combine(_root, "exports"),
            "csv",
            null,
            "table",
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("duplicate export file name", output.ToString());
    }

    [Fact]
    public async Task BatchExport_WritesCsvAndManifest()
    {
        WriteIssueSpec();
        var outputDir = Path.Combine(_root, "exports");
        var manifestPath = Path.Combine(_root, "exports", "manifest.json");
        var documentPath = Path.GetFullPath(Path.Combine(
            Path.GetPathRoot(outputDir) ?? Path.DirectorySeparatorChar.ToString(),
            "models",
            "Demo.rvt"));
        var client = MakeClient(
            schedules: new[]
            {
                new ScheduleInfo { Id = 100, Name = "Door Schedule", Category = "Doors", FieldCount = 2, RowCount = 1 }
            },
            exportData: new ScheduleData
            {
                Columns = new List<string> { "Mark", "Level" },
                Rows = new List<Dictionary<string, string>>
                {
                    new() { ["Mark"] = "D-001", ["Level"] = "L1" }
                },
                TotalRows = 1
            },
            status: new StatusInfo
            {
                RevitVersion = "2026",
                DocumentName = "Demo.rvt",
                DocumentPath = documentPath
            });
        var output = new StringWriter();

        var exitCode = await SchedulesCommand.ExecuteBatchExportAsync(
            client,
            "issue",
            outputDir,
            "csv",
            manifestPath,
            "json",
            output);

        Assert.Equal(0, exitCode);
        var csvPath = Path.Combine(outputDir, "Door Schedule.csv");
        Assert.True(File.Exists(csvPath));
        Assert.Contains("Mark,Level", File.ReadAllText(csvPath));
        using var json = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.Equal("schedule-export-manifest.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("issue", json.RootElement.GetProperty("profile").GetString());
        Assert.Equal(Path.GetFullPath(manifestPath), json.RootElement.GetProperty("manifestPath").GetString());
        Assert.Equal("csv", json.RootElement.GetProperty("format").GetString());
        Assert.Equal(documentPath, json.RootElement.GetProperty("modelPath").GetString());
        Assert.Equal("Demo.rvt", json.RootElement.GetProperty("documentName").GetString());
        Assert.Equal("2026", json.RootElement.GetProperty("documentVersion").GetString());
        Assert.Contains("schedules batch-export", json.RootElement.GetProperty("command").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--manifest", json.RootElement.GetProperty("command").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, json.RootElement.GetProperty("entries").GetArrayLength());
        Assert.Contains(json.RootElement.GetProperty("entries").EnumerateArray(), entry =>
            entry.GetProperty("scheduleName").GetString() == "Door Schedule" &&
            entry.GetProperty("scheduleId").GetInt64() == 100 &&
            entry.GetProperty("success").GetBoolean() &&
            entry.GetProperty("bytes").GetInt64() > 0 &&
            entry.GetProperty("sha256").GetString()!.Length == 64);
        Assert.Contains("\"schemaVersion\": \"schedule-export-manifest.v1\"", output.ToString());

        var ledgerPath = Path.Combine(outputDir, ".revitcli", "ledger", "operations.jsonl");
        var ledgerLine = Assert.Single(File.ReadAllLines(ledgerPath));
        using var ledger = JsonDocument.Parse(ledgerLine);
        var operation = ledger.RootElement;
        Assert.Equal("ledger-operation.v1", operation.GetProperty("schemaVersion").GetString());
        Assert.Equal("schedules", operation.GetProperty("command").GetString());
        Assert.Equal("schedules.batch-export", operation.GetProperty("action").GetString());
        Assert.Equal("csv", operation.GetProperty("category").GetString());
        Assert.Equal("succeeded", operation.GetProperty("status").GetString());
        Assert.Equal("Demo.rvt", operation.GetProperty("modelIdentity").GetString());
        Assert.Equal(documentPath, operation.GetProperty("modelPath").GetString());
        Assert.Equal("2026", operation.GetProperty("revitVersion").GetString());
        Assert.Equal(Path.GetFullPath(manifestPath), operation.GetProperty("artifactPath").GetString());
        Assert.Contains(operation.GetProperty("args").EnumerateArray(), arg => arg.GetString() == "batch-export");
        Assert.Contains(operation.GetProperty("artifacts").EnumerateArray(), artifact =>
            artifact.GetProperty("role").GetString() == "artifact" &&
            artifact.GetProperty("path").GetString() == Path.GetFullPath(manifestPath) &&
            artifact.GetProperty("sha256").GetString()!.Length == 64);
    }

    [Fact]
    public async Task BatchExport_WritesShellSafeManifestCommandForDangerousArguments()
    {
        var set = "issue $(touch hacked)'";
        WriteScheduleSpec($$"""
schemaVersion: schedule-spec.v1
set: "{{set}}"
schedules:
  - name: Door Schedule
    category: Doors
    fields: [Mark]
""", set);
        var outputDir = Path.Combine(_root, "exports $(touch hacked)' dir");
        var manifestPath = Path.Combine(outputDir, "manifest $(touch hacked)'.json");
        var output = new StringWriter();

        var exitCode = await SchedulesCommand.ExecuteBatchExportAsync(
            MakeClient(
                schedules: new[]
                {
                    new ScheduleInfo { Id = 100, Name = "Door Schedule", Category = "Doors", FieldCount = 1, RowCount = 1 }
                },
                exportData: new ScheduleData
                {
                    Columns = new List<string> { "Mark" },
                    Rows = new List<Dictionary<string, string>> { new() { ["Mark"] = "D-001" } },
                    TotalRows = 1
                }),
            set,
            outputDir,
            "csv",
            manifestPath,
            "json",
            output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var command = json.RootElement.GetProperty("command").GetString() ?? "";
        Assert.Contains("schedules batch-export", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'\"'\"'", command);
        Assert.Contains("$(touch hacked)", command);
        Assert.Contains($"'{Path.GetFullPath(outputDir).Replace("'", "'\"'\"'", StringComparison.Ordinal)}'", command);
        Assert.Contains($"'{Path.GetFullPath(manifestPath).Replace("'", "'\"'\"'", StringComparison.Ordinal)}'", command);
    }

    [Fact]
    public async Task BatchExport_StatusUnavailable_WritesManifestWithoutModelIdentity()
    {
        WriteIssueSpec();
        var outputDir = Path.Combine(_root, "exports-no-status");
        var manifestPath = Path.Combine(outputDir, "manifest.json");
        var output = new StringWriter();

        var exitCode = await SchedulesCommand.ExecuteBatchExportAsync(
            MakeClient(
                schedules: new[]
                {
                    new ScheduleInfo { Id = 100, Name = "Door Schedule", Category = "Doors", FieldCount = 2, RowCount = 1 }
                },
                exportData: new ScheduleData
                {
                    Columns = new List<string> { "Mark", "Level" },
                    Rows = new List<Dictionary<string, string>> { new() { ["Mark"] = "D-001", ["Level"] = "L1" } },
                    TotalRows = 1
                }),
            "issue",
            outputDir,
            "csv",
            manifestPath,
            "json",
            output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("modelPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("documentName").ValueKind);
        Assert.Contains(json.RootElement.GetProperty("entries").EnumerateArray(), entry =>
            entry.GetProperty("success").GetBoolean() &&
            entry.GetProperty("sha256").GetString()!.Length == 64);

        var ledgerPath = Path.Combine(outputDir, ".revitcli", "ledger", "operations.jsonl");
        var ledgerLine = Assert.Single(File.ReadAllLines(ledgerPath));
        using var ledger = JsonDocument.Parse(ledgerLine);
        Assert.Equal(JsonValueKind.Null, ledger.RootElement.GetProperty("modelIdentity").ValueKind);
        Assert.Equal(JsonValueKind.Null, ledger.RootElement.GetProperty("modelPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, ledger.RootElement.GetProperty("revitVersion").ValueKind);
    }

    [Fact]
    public async Task BatchExport_RecordsExportFailureForMissingSchedule()
    {
        WriteScheduleSpec("""
schemaVersion: schedule-spec.v1
set: issue
schedules:
  - name: Missing Schedule
    category: Doors
    fields: [Mark]
""");
        var outputDir = Path.Combine(_root, "exports-missing");
        var manifestPath = Path.Combine(outputDir, "manifest.json");
        var output = new StringWriter();

        var exitCode = await SchedulesCommand.ExecuteBatchExportAsync(
            MakeClient(
                schedules: Array.Empty<ScheduleInfo>(),
                exportData: null,
                exportResponse: ApiResponse<ScheduleData>.Fail("schedule not found")),
            "issue",
            outputDir,
            "csv",
            manifestPath,
            "json",
            output);

        Assert.Equal(2, exitCode);
        using var json = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "export-failed" &&
            issue.GetProperty("message").GetString()!.Contains("schedule not found", StringComparison.OrdinalIgnoreCase));
        var entry = Assert.Single(json.RootElement.GetProperty("entries").EnumerateArray());
        Assert.False(entry.GetProperty("success").GetBoolean());
        Assert.Equal("Missing Schedule", entry.GetProperty("scheduleName").GetString());
        Assert.Equal("schedule not found", entry.GetProperty("error").GetString());
        var ledgerPath = Path.Combine(outputDir, ".revitcli", "ledger", "operations.jsonl");
        var ledgerLine = Assert.Single(File.ReadAllLines(ledgerPath));
        using var ledger = JsonDocument.Parse(ledgerLine);
        var operation = ledger.RootElement;
        Assert.Equal("schedules.batch-export", operation.GetProperty("action").GetString());
        Assert.Equal("csv", operation.GetProperty("category").GetString());
        Assert.Equal("failed", operation.GetProperty("status").GetString());
        Assert.Equal(Path.GetFullPath(manifestPath), operation.GetProperty("artifactPath").GetString());
    }

    [Fact]
    public async Task Compare_ReportsChangedAddedAndRemovedRows()
    {
        var baseline = Path.Combine(_root, "baseline");
        var current = Path.Combine(_root, "current");
        Directory.CreateDirectory(baseline);
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(baseline, "Door Schedule.csv"), "Mark,Level,Width\nD-001,L1,900\nD-002,L1,800\n");
        File.WriteAllText(Path.Combine(current, "Door Schedule.csv"), "Mark,Level,Width\nD-001,L1,1000\nD-003,L2,700\n");
        var output = new StringWriter();

        var exitCode = await SchedulesCommand.ExecuteCompareAsync(
            baseline,
            current,
            "Mark",
            "json",
            output);

        Assert.Equal(2, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("schedule-diff-report.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        var summary = json.RootElement.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("totalChangedRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("addedRows").GetInt32());
        Assert.Equal(1, summary.GetProperty("removedRows").GetInt32());
        var file = Assert.Single(json.RootElement.GetProperty("files").EnumerateArray());
        Assert.EndsWith("Door Schedule.csv", file.GetProperty("beforePath").GetString()!, StringComparison.Ordinal);
        Assert.EndsWith("Door Schedule.csv", file.GetProperty("afterPath").GetString()!, StringComparison.Ordinal);
        Assert.True(file.GetProperty("beforeBytes").GetInt64() > 0);
        Assert.True(file.GetProperty("afterBytes").GetInt64() > 0);
        Assert.Equal(64, file.GetProperty("beforeSha256").GetString()!.Length);
        Assert.Equal(64, file.GetProperty("afterSha256").GetString()!.Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Compare_RejectsDuplicateRowKeysInEitherFile(bool duplicateBaseline)
    {
        var baseline = Path.Combine(_root, "baseline");
        var current = Path.Combine(_root, "current");
        Directory.CreateDirectory(baseline);
        Directory.CreateDirectory(current);
        var duplicateRows = "Mark,Level,Width\nD-001,L1,900\nD-001,L2,800\n";
        var singleRows = "Mark,Level,Width\nD-001,L1,900\n";
        File.WriteAllText(Path.Combine(baseline, "Door Schedule.csv"), duplicateBaseline ? duplicateRows : singleRows);
        File.WriteAllText(Path.Combine(current, "Door Schedule.csv"), duplicateBaseline ? singleRows : duplicateRows);
        var output = new StringWriter();

        var exitCode = await SchedulesCommand.ExecuteCompareAsync(
            baseline,
            current,
            "Mark",
            "json",
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Door Schedule.csv contains duplicate schedule diff key 'D-001'", output.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Compare_StaleBaselineOrCurrentPath_ReturnsFailure(bool missingBaseline)
    {
        var baseline = Path.Combine(_root, "missing-baseline");
        var current = Path.Combine(_root, "missing-current");
        if (missingBaseline)
            Directory.CreateDirectory(current);
        else
            Directory.CreateDirectory(baseline);
        var output = new StringWriter();

        var exitCode = await SchedulesCommand.ExecuteCompareAsync(
            baseline,
            current,
            "Mark",
            "table",
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            missingBaseline ? "Baseline directory not found" : "Current directory not found",
            output.ToString());
    }

    private string WriteIssueSpec()
    {
        return WriteScheduleSpec("""
schemaVersion: schedule-spec.v1
set: issue
schedules:
  - name: Door Schedule
    category: Doors
    fields: [Mark, Level, Fire Rating]
    sort: Mark
    keyColumns: [Mark]
  - name: Window Schedule
    category: Windows
    fields: [Mark, Level]
    keyColumns: [Mark]
""");
    }

    private string WriteScheduleSpec(string yaml)
    {
        return WriteScheduleSpec(yaml, "issue");
    }

    private string WriteScheduleSpec(string yaml, string set)
    {
        var directory = Path.Combine(_root, ".revitcli", "schedules");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{set}.yml");
        File.WriteAllText(path, yaml);
        return path;
    }

    private static RevitClient MakeClient(
        ScheduleInfo[] schedules,
        ScheduleData? exportData,
        StatusInfo? status = null,
        ApiResponse<ScheduleData>? exportResponse = null)
    {
        return new RevitClient(new HttpClient(new SchedulesHandler(schedules, exportData, status, exportResponse)) { BaseAddress = new Uri("http://localhost:17839") });
    }

    private sealed class SchedulesHandler : HttpMessageHandler
    {
        private readonly ScheduleInfo[] _schedules;
        private readonly ScheduleData? _exportData;
        private readonly StatusInfo? _status;
        private readonly ApiResponse<ScheduleData>? _exportResponse;

        public SchedulesHandler(
            ScheduleInfo[] schedules,
            ScheduleData? exportData,
            StatusInfo? status,
            ApiResponse<ScheduleData>? exportResponse)
        {
            _schedules = schedules;
            _exportData = exportData;
            _status = status;
            _exportResponse = exportResponse;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/status" && request.Method == HttpMethod.Get && _status != null)
                return Json(ApiResponse<StatusInfo>.Ok(_status));

            if (request.RequestUri!.AbsolutePath == "/api/schedules" && request.Method == HttpMethod.Get)
                return Json(ApiResponse<ScheduleInfo[]>.Ok(_schedules));

            if (request.RequestUri!.AbsolutePath == "/api/schedules/export" && request.Method == HttpMethod.Post)
                return Json(_exportResponse ?? ApiResponse<ScheduleData>.Ok(_exportData ?? new ScheduleData()));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json<T>(T value)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            });
        }
    }
}
