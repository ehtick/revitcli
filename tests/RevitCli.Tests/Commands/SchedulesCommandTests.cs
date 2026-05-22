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
    public async Task BatchExport_WritesCsvAndManifest()
    {
        WriteIssueSpec();
        var outputDir = Path.Combine(_root, "exports");
        var manifestPath = Path.Combine(_root, "exports", "manifest.json");
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
        Assert.Equal(2, json.RootElement.GetProperty("entries").GetArrayLength());
        Assert.Contains(json.RootElement.GetProperty("entries").EnumerateArray(), entry =>
            entry.GetProperty("scheduleName").GetString() == "Door Schedule" &&
            entry.GetProperty("scheduleId").GetInt64() == 100 &&
            entry.GetProperty("success").GetBoolean());
        Assert.Contains("\"schemaVersion\": \"schedule-export-manifest.v1\"", output.ToString());
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

    private string WriteIssueSpec()
    {
        var directory = Path.Combine(_root, ".revitcli", "schedules");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "issue.yml");
        File.WriteAllText(path, """
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
        return path;
    }

    private static RevitClient MakeClient(ScheduleInfo[] schedules, ScheduleData? exportData)
    {
        return new RevitClient(new HttpClient(new SchedulesHandler(schedules, exportData)) { BaseAddress = new Uri("http://localhost:17839") });
    }

    private sealed class SchedulesHandler : HttpMessageHandler
    {
        private readonly ScheduleInfo[] _schedules;
        private readonly ScheduleData? _exportData;

        public SchedulesHandler(ScheduleInfo[] schedules, ScheduleData? exportData)
        {
            _schedules = schedules;
            _exportData = exportData;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/schedules" && request.Method == HttpMethod.Get)
                return Json(ApiResponse<ScheduleInfo[]>.Ok(_schedules));

            if (request.RequestUri!.AbsolutePath == "/api/schedules/export" && request.Method == HttpMethod.Post)
                return Json(ApiResponse<ScheduleData>.Ok(_exportData ?? new ScheduleData()));

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
