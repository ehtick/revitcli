using System.Net;
using System.Text;
using System.Text.Json;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Numbering;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

[Collection("Sequential")]
public sealed class MarksCommandTests : IDisposable
{
    private readonly string _root;
    private readonly string _previousDirectory;

    public MarksCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-marks-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task Assign_DryRun_WritesFrozenMarkAssignmentPlan()
    {
        var rulePath = WriteRule("""
schemaVersion: 1
category: doors
parameter: Mark
scheme: "D-{level}-{seq:03}"
sort: [level, zone, type, location]
tokens:
  level: Level
  zone: Department
  location: Room
""");
        var planPath = Path.Combine(_root, ".revitcli", "plans", "door-marks.json");
        var client = MakeClient(
            Door(10, "Door B", "L1", "A", "Office", "D-OLD"),
            Door(11, "Door A", "L1", "A", "Lobby", "D-OLDER"));
        var output = new StringWriter();

        var exitCode = await MarksCommand.ExecuteAssignAsync(
            client,
            "doors",
            rulePath,
            planPath,
            "level,zone,type,location",
            dryRun: true,
            maxChanges: null,
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(planPath));
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        Assert.Equal("mark-assignment-plan.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("mark-assignment", json.RootElement.GetProperty("type").GetString());
        Assert.Equal("doors", json.RootElement.GetProperty("category").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("summary").GetProperty("actionCount").GetInt32());
        Assert.Contains(json.RootElement.GetProperty("actions").EnumerateArray(), action =>
            action.GetProperty("elementId").GetInt64() == 11 &&
            action.GetProperty("newMark").GetString() == "D-L1-001");
        Assert.Contains("\"schemaVersion\": \"mark-assignment-plan.v1\"", output.ToString());
    }

    [Fact]
    public async Task Assign_RejectsDuplicateTargetAlreadyUsedByOtherElement()
    {
        var rulePath = WriteRule("""
schemaVersion: 1
scheme: "D-{seq:03}"
sort: [name]
""");
        var planPath = Path.Combine(_root, "door-marks.json");
        var client = MakeClient(
            Door(10, "Alpha", "L1", "A", "101", "D-OLD"),
            Door(11, "Beta", "L1", "A", "102", "D-001"));
        var output = new StringWriter();

        var exitCode = await MarksCommand.ExecuteAssignAsync(
            client,
            "doors",
            rulePath,
            planPath,
            "name",
            dryRun: true,
            maxChanges: null,
            outputFormat: "table",
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("would overwrite existing Marks", output.ToString());
        Assert.False(File.Exists(planPath));
    }

    [Fact]
    public async Task Verify_ReportsDuplicateAndMissingMarks()
    {
        var client = MakeClient(
            Door(10, "Alpha", "L1", "A", "101", "D-001"),
            Door(11, "Beta", "L1", "A", "102", "D-001"),
            Door(12, "Gamma", "L1", "A", "103", ""));
        var output = new StringWriter();

        var exitCode = await MarksCommand.ExecuteVerifyAsync(
            client,
            "doors",
            against: null,
            outputFormat: "json",
            output);

        Assert.Equal(2, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("mark-verify-report.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("errorCount").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("warningCount").GetInt32());
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "duplicate-mark");
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("code").GetString() == "missing-mark");
    }

    [Fact]
    public async Task Assign_RejectsRealWritePath()
    {
        var output = new StringWriter();
        var exitCode = await MarksCommand.ExecuteAssignAsync(
            MakeClient(),
            "doors",
            "marks.yml",
            "marks.json",
            "level,zone,type,location",
            dryRun: false,
            maxChanges: null,
            outputFormat: "table",
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("only creates reviewed plans", output.ToString());
    }

    private string WriteRule(string body)
    {
        var path = Path.Combine(_root, "marks.yml");
        File.WriteAllText(path, body);
        return path;
    }

    private static ElementInfo Door(long id, string name, string level, string zone, string room, string mark)
    {
        return new ElementInfo
        {
            Id = id,
            Name = name,
            Category = "doors",
            TypeName = "Single Door",
            Parameters =
            {
                ["Level"] = level,
                ["Department"] = zone,
                ["Room"] = room,
                ["Mark"] = mark
            }
        };
    }

    private static RevitClient MakeClient(params ElementInfo[] doors)
    {
        return new RevitClient(new HttpClient(new MarksHandler(doors)) { BaseAddress = new Uri("http://localhost:17839") });
    }

    private sealed class MarksHandler : HttpMessageHandler
    {
        private readonly ElementInfo[] _doors;

        public MarksHandler(ElementInfo[] doors)
        {
            _doors = doors;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/elements" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(ApiResponse<ElementInfo[]>.Ok(_doors)),
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
