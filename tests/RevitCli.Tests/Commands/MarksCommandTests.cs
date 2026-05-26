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
    public async Task Assign_RejectsDuplicateTargetsGeneratedInsidePlan()
    {
        var rulePath = WriteRule("""
schemaVersion: 1
category: doors
scheme: "D-CONSTANT"
sort: [name]
""");
        var planPath = Path.Combine(_root, "door-marks-duplicates.json");
        var client = MakeClient(
            Door(10, "Alpha", "L1", "A", "101", "OLD-A"),
            Door(11, "Beta", "L1", "A", "102", "OLD-B"));
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
        Assert.Contains("target Mark D-CONSTANT appears more than once in the plan", output.ToString());
        Assert.False(File.Exists(planPath));
    }

    [Fact]
    public async Task Assign_ReservedAndHoldMarksProduceDeterministicGaps()
    {
        var rulePath = WriteRule("""
schemaVersion: 1
category: doors
scheme: "D-{seq:03}"
start: 1
reservedMarks:
  - D-002
holdMarks:
  - D-003
sort: [name]
""");
        var firstPlanPath = Path.Combine(_root, "door-marks-first.json");
        var secondPlanPath = Path.Combine(_root, "door-marks-second.json");
        var client = MakeClient(
            Door(10, "Alpha", "L1", "A", "101", "OLD-A"),
            Door(11, "Beta", "L1", "A", "102", "D-003"),
            Door(12, "Gamma", "L1", "A", "103", "OLD-C"));
        var output = new StringWriter();

        var firstExitCode = await MarksCommand.ExecuteAssignAsync(
            client,
            "doors",
            rulePath,
            firstPlanPath,
            "name",
            dryRun: true,
            maxChanges: null,
            outputFormat: "json",
            output);
        var secondExitCode = await MarksCommand.ExecuteAssignAsync(
            client,
            "doors",
            rulePath,
            secondPlanPath,
            "name",
            dryRun: true,
            maxChanges: null,
            outputFormat: "json",
            new StringWriter());

        Assert.Equal(0, firstExitCode);
        Assert.Equal(0, secondExitCode);
        using var first = JsonDocument.Parse(File.ReadAllText(firstPlanPath));
        using var second = JsonDocument.Parse(File.ReadAllText(secondPlanPath));
        var firstTargets = first.RootElement.GetProperty("actions").EnumerateArray()
            .Select(action => action.GetProperty("newMark").GetString())
            .ToArray();
        var secondTargets = second.RootElement.GetProperty("actions").EnumerateArray()
            .Select(action => action.GetProperty("newMark").GetString())
            .ToArray();

        Assert.Equal(new[] { "D-001", "D-004" }, firstTargets);
        Assert.Equal(firstTargets, secondTargets);
        Assert.Contains(first.RootElement.GetProperty("skipped").EnumerateArray(), skipped =>
            skipped.GetProperty("elementId").GetInt64() == 11 &&
            skipped.GetProperty("reason").GetString() == "hold-mark");
    }

    [Fact]
    public async Task Assign_MultiBuildingFixturesProduceDeterministicTargets()
    {
        await AssertMarkFixtureAsync(
            "residential",
            """
schemaVersion: 1
category: doors
scheme: "D-{building}-{level}-{seq:02}"
start: 1
sort: [building, level, unit, location]
reservedMarks:
  - D-A-L01-02
holdMarks:
  - D-A-L01-03
tokens:
  building: Building
  unit: Unit Type
  location: Room
""",
            new[]
            {
                Door(30, "A Studio Entry", "L01", "Residential", "101", "OLD-1", ("Building", "A"), ("Unit Type", "Studio")),
                Door(31, "A One Bed Entry", "L01", "Residential", "102", "D-A-L01-03", ("Building", "A"), ("Unit Type", "One Bed")),
                Door(32, "A Two Bed Entry", "L01", "Residential", "103", "OLD-3", ("Building", "A"), ("Unit Type", "Two Bed")),
                Door(33, "B Studio Entry", "L02", "Residential", "201", "OLD-4", ("Building", "B"), ("Unit Type", "Studio")),
            },
            "building,level,unit,location",
            new[] { "D-A-L01-01", "D-A-L01-04", "D-B-L02-05" },
            expectedSkippedReason: "hold-mark");

        await AssertMarkFixtureAsync(
            "office",
            """
schemaVersion: 1
category: doors
scheme: "D-{level}-{zone}-{seq:02}"
start: 1
sort: [level, zone, location]
reservedMarks:
  - D-05-FIN-02
holdMarks:
  - D-05-FIN-03
tokens:
  zone: Department
  location: Room
""",
            new[]
            {
                Door(40, "Finance Open Door", "05", "FIN", "501", "OLD-1"),
                Door(41, "Finance Office Door", "05", "FIN", "502", "D-05-FIN-03"),
                Door(42, "Finance Storage Door", "05", "FIN", "503", "OLD-3"),
                Door(43, "Legal Office Door", "05", "LEG", "504", "OLD-4"),
            },
            "level,zone,location",
            new[] { "D-05-FIN-01", "D-05-FIN-04", "D-05-LEG-05" },
            expectedSkippedReason: "hold-mark");

        await AssertMarkFixtureAsync(
            "healthcare",
            """
schemaVersion: 1
category: doors
scheme: "D-{dept}-{level}-{seq:03}"
start: 10
sort: [dept, level, acuity, location]
reservedMarks:
  - D-ED-02-011
holdMarks:
  - D-ED-02-012
tokens:
  dept: Department
  acuity: Acuity
  location: Room
""",
            new[]
            {
                Door(50, "ED Trauma Door", "02", "ED", "T-1", "OLD-1", ("Acuity", "1")),
                Door(51, "ED Exam Door", "02", "ED", "E-2", "D-ED-02-012", ("Acuity", "2")),
                Door(52, "ED Consult Door", "02", "ED", "C-3", "OLD-3", ("Acuity", "3")),
                Door(53, "Imaging CT Door", "02", "IMG", "CT", "OLD-4", ("Acuity", "2")),
            },
            "dept,level,acuity,location",
            new[] { "D-ED-02-010", "D-ED-02-013", "D-IMG-02-014" },
            expectedSkippedReason: "hold-mark");
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

    private async Task AssertMarkFixtureAsync(
        string name,
        string rule,
        ElementInfo[] doors,
        string sort,
        string[] expectedTargets,
        string expectedSkippedReason)
    {
        var rulePath = Path.Combine(_root, $"{name}-marks.yml");
        File.WriteAllText(rulePath, rule);
        var firstPlanPath = Path.Combine(_root, $"{name}-marks-first.json");
        var secondPlanPath = Path.Combine(_root, $"{name}-marks-second.json");
        var client = MakeClient(doors);

        var firstExitCode = await MarksCommand.ExecuteAssignAsync(
            client,
            "doors",
            rulePath,
            firstPlanPath,
            sort,
            dryRun: true,
            maxChanges: null,
            outputFormat: "json",
            new StringWriter());
        var secondExitCode = await MarksCommand.ExecuteAssignAsync(
            client,
            "doors",
            rulePath,
            secondPlanPath,
            sort,
            dryRun: true,
            maxChanges: null,
            outputFormat: "json",
            new StringWriter());

        Assert.Equal(0, firstExitCode);
        Assert.Equal(0, secondExitCode);
        using var first = JsonDocument.Parse(File.ReadAllText(firstPlanPath));
        using var second = JsonDocument.Parse(File.ReadAllText(secondPlanPath));
        Assert.Equal(expectedTargets, ReadMarkTargets(first));
        Assert.Equal(expectedTargets, ReadMarkTargets(second));
        Assert.Contains(first.RootElement.GetProperty("skipped").EnumerateArray(), skipped =>
            skipped.GetProperty("reason").GetString() == expectedSkippedReason);
    }

    private static string?[] ReadMarkTargets(JsonDocument document) =>
        document.RootElement.GetProperty("actions").EnumerateArray()
            .Select(action => action.GetProperty("newMark").GetString())
            .ToArray();

    private static ElementInfo Door(
        long id,
        string name,
        string level,
        string zone,
        string room,
        string mark,
        params (string Key, string Value)[] parameters)
    {
        var door = new ElementInfo
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
        foreach (var (key, value) in parameters)
            door.Parameters[key] = value;
        return door;
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
