using System.Net;
using System.Text;
using System.Text.Json;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

[Collection("Sequential")]
public sealed class RoomsCommandTests : IDisposable
{
    private readonly string _root;
    private readonly string _previousDirectory;

    public RoomsCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-rooms-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task Renumber_DryRun_WritesFrozenRoomNumberingPlan()
    {
        var rulePath = WriteRule("""
schemaVersion: 1
parameter: Number
scheme: "{level}-{seq:03}"
groupBy: [level]
sort: [level, zone, name]
tokens:
  level: Level
  zone: Department
""");
        var planPath = Path.Combine(_root, ".revitcli", "plans", "rooms.json");
        var client = MakeClient(
            Room(10, "Office", "L1", "A", "101"),
            Room(11, "Lobby", "L1", "A", "102"),
            Room(12, "Store", "L2", "B", "201"));
        var output = new StringWriter();

        var exitCode = await RoomsCommand.ExecuteRenumberAsync(
            client,
            rulePath,
            planPath,
            scope: "all",
            dryRun: true,
            maxChanges: null,
            outputFormat: "json",
            output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(planPath));
        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        Assert.Equal("room-numbering-plan.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("room-numbering", json.RootElement.GetProperty("type").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("summary").GetProperty("actionCount").GetInt32());
        Assert.Contains(json.RootElement.GetProperty("actions").EnumerateArray(), action =>
            action.GetProperty("roomId").GetInt64() == 10 &&
            action.GetProperty("oldNumber").GetString() == "101" &&
            action.GetProperty("newNumber").GetString() == "L1-002");
        Assert.Contains("\"schemaVersion\": \"room-numbering-plan.v1\"", output.ToString());
    }

    [Fact]
    public async Task Renumber_RejectsDuplicateTargetAlreadyUsedByOtherRoom()
    {
        var rulePath = WriteRule("""
schemaVersion: 1
parameter: number
scheme: "R-{seq:03}"
sort: [name]
""");
        var planPath = Path.Combine(_root, "rooms.json");
        var client = MakeClient(
            Room(10, "Alpha", "L1", "A", "001"),
            Room(11, "Beta", "L1", "A", "R-001"));
        var output = new StringWriter();

        var exitCode = await RoomsCommand.ExecuteRenumberAsync(
            client,
            rulePath,
            planPath,
            scope: "Alpha",
            dryRun: true,
            maxChanges: null,
            outputFormat: "table",
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("would overwrite existing room numbers", output.ToString());
        Assert.False(File.Exists(planPath));
    }

    [Fact]
    public async Task Renumber_RejectsDuplicateTargetsGeneratedInsidePlan()
    {
        var rulePath = WriteRule("""
schemaVersion: 1
scheme: "R-CONSTANT"
sort: [name]
""");
        var planPath = Path.Combine(_root, "rooms-duplicates.json");
        var client = MakeClient(
            Room(10, "Alpha", "L1", "A", "101"),
            Room(11, "Beta", "L1", "A", "102"));
        var output = new StringWriter();

        var exitCode = await RoomsCommand.ExecuteRenumberAsync(
            client,
            rulePath,
            planPath,
            scope: "all",
            dryRun: true,
            maxChanges: null,
            outputFormat: "table",
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("target room number R-CONSTANT appears more than once in the plan", output.ToString());
        Assert.False(File.Exists(planPath));
    }

    [Fact]
    public async Task Renumber_ReservedAndHoldNumbersProduceDeterministicGaps()
    {
        var rulePath = WriteRule("""
schemaVersion: 1
scheme: "R-{seq:03}"
start: 1
reservedNumbers:
  - R-002
holdNumbers:
  - R-003
sort: [name]
""");
        var firstPlanPath = Path.Combine(_root, "rooms-first.json");
        var secondPlanPath = Path.Combine(_root, "rooms-second.json");
        var client = MakeClient(
            Room(10, "Alpha", "L1", "A", "OLD-A"),
            Room(11, "Beta", "L1", "A", "R-003"),
            Room(12, "Gamma", "L1", "A", "OLD-C"));
        var output = new StringWriter();

        var firstExitCode = await RoomsCommand.ExecuteRenumberAsync(
            client,
            rulePath,
            firstPlanPath,
            scope: "all",
            dryRun: true,
            maxChanges: null,
            outputFormat: "json",
            output);
        var secondExitCode = await RoomsCommand.ExecuteRenumberAsync(
            client,
            rulePath,
            secondPlanPath,
            scope: "all",
            dryRun: true,
            maxChanges: null,
            outputFormat: "json",
            new StringWriter());

        Assert.Equal(0, firstExitCode);
        Assert.Equal(0, secondExitCode);
        using var first = JsonDocument.Parse(File.ReadAllText(firstPlanPath));
        using var second = JsonDocument.Parse(File.ReadAllText(secondPlanPath));
        var firstTargets = first.RootElement.GetProperty("actions").EnumerateArray()
            .Select(action => action.GetProperty("newNumber").GetString())
            .ToArray();
        var secondTargets = second.RootElement.GetProperty("actions").EnumerateArray()
            .Select(action => action.GetProperty("newNumber").GetString())
            .ToArray();

        Assert.Equal(new[] { "R-001", "R-004" }, firstTargets);
        Assert.Equal(firstTargets, secondTargets);
        Assert.Contains(first.RootElement.GetProperty("skipped").EnumerateArray(), skipped =>
            skipped.GetProperty("roomId").GetInt64() == 11 &&
            skipped.GetProperty("reason").GetString() == "hold-number");
    }

    [Fact]
    public async Task Renumber_MultiBuildingFixturesProduceDeterministicTargets()
    {
        await AssertRoomFixtureAsync(
            "residential",
            """
schemaVersion: 1
scheme: "{building}-{level}{seq:02}"
start: 1
groupBy: [building, level]
sort: [building, level, unit, name]
reservedNumbers:
  - A-L0102
holdNumbers:
  - A-L0103
tokens:
  building: Building
  unit: Unit Type
""",
            new[]
            {
                Room(30, "A-101 Studio", "L01", "Residential", "OLD-1", ("Building", "A"), ("Unit Type", "Studio")),
                Room(31, "A-102 One Bed", "L01", "Residential", "A-L0103", ("Building", "A"), ("Unit Type", "One Bed")),
                Room(32, "A-103 Two Bed", "L01", "Residential", "OLD-3", ("Building", "A"), ("Unit Type", "Two Bed")),
                Room(33, "B-201 Studio", "L02", "Residential", "OLD-4", ("Building", "B"), ("Unit Type", "Studio")),
            },
            new[] { "A-L0101", "A-L0104", "B-L0201" },
            expectedSkippedReason: "hold-number");

        await AssertRoomFixtureAsync(
            "office",
            """
schemaVersion: 1
scheme: "OF-{level}-{zone}-{seq:02}"
start: 1
groupBy: [level, zone]
sort: [level, zone, name]
reservedNumbers:
  - OF-05-FIN-02
holdNumbers:
  - OF-05-FIN-03
tokens:
  zone: Department
""",
            new[]
            {
                Room(40, "Finance Open Office", "05", "FIN", "OLD-1"),
                Room(41, "Finance Office", "05", "FIN", "OF-05-FIN-03"),
                Room(42, "Finance Storage", "05", "FIN", "OLD-3"),
                Room(43, "Legal Office", "05", "LEG", "OLD-4"),
            },
            new[] { "OF-05-FIN-01", "OF-05-FIN-04", "OF-05-LEG-01" },
            expectedSkippedReason: "hold-number");

        await AssertRoomFixtureAsync(
            "healthcare",
            """
schemaVersion: 1
scheme: "HC-{dept}-{level}-{seq:03}"
start: 10
groupBy: [dept, level]
sort: [dept, level, acuity, name]
reservedNumbers:
  - HC-ED-02-011
holdNumbers:
  - HC-ED-02-012
tokens:
  dept: Department
  acuity: Acuity
""",
            new[]
            {
                Room(50, "ED Trauma", "02", "ED", "OLD-1", ("Acuity", "1")),
                Room(51, "ED Exam", "02", "ED", "HC-ED-02-012", ("Acuity", "2")),
                Room(52, "ED Consult", "02", "ED", "OLD-3", ("Acuity", "3")),
                Room(53, "Imaging CT", "02", "IMG", "OLD-4", ("Acuity", "2")),
            },
            new[] { "HC-ED-02-010", "HC-ED-02-013", "HC-IMG-02-010" },
            expectedSkippedReason: "hold-number");
    }

    [Fact]
    public async Task Renumber_RejectsRealWritePath()
    {
        var output = new StringWriter();
        var exitCode = await RoomsCommand.ExecuteRenumberAsync(
            MakeClient(),
            "rooms.yml",
            "rooms.json",
            "all",
            dryRun: false,
            maxChanges: null,
            outputFormat: "table",
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("only creates reviewed plans", output.ToString());
    }

    private string WriteRule(string body)
    {
        var path = Path.Combine(_root, "rooms.yml");
        File.WriteAllText(path, body);
        return path;
    }

    private async Task AssertRoomFixtureAsync(
        string name,
        string rule,
        ElementInfo[] rooms,
        string[] expectedTargets,
        string expectedSkippedReason)
    {
        var rulePath = Path.Combine(_root, $"{name}-rooms.yml");
        File.WriteAllText(rulePath, rule);
        var firstPlanPath = Path.Combine(_root, $"{name}-rooms-first.json");
        var secondPlanPath = Path.Combine(_root, $"{name}-rooms-second.json");
        var client = MakeClient(rooms);

        var firstExitCode = await RoomsCommand.ExecuteRenumberAsync(
            client,
            rulePath,
            firstPlanPath,
            scope: "all",
            dryRun: true,
            maxChanges: null,
            outputFormat: "json",
            new StringWriter());
        var secondExitCode = await RoomsCommand.ExecuteRenumberAsync(
            client,
            rulePath,
            secondPlanPath,
            scope: "all",
            dryRun: true,
            maxChanges: null,
            outputFormat: "json",
            new StringWriter());

        Assert.Equal(0, firstExitCode);
        Assert.Equal(0, secondExitCode);
        using var first = JsonDocument.Parse(File.ReadAllText(firstPlanPath));
        using var second = JsonDocument.Parse(File.ReadAllText(secondPlanPath));
        Assert.Equal(expectedTargets, ReadRoomTargets(first));
        Assert.Equal(expectedTargets, ReadRoomTargets(second));
        Assert.Contains(first.RootElement.GetProperty("skipped").EnumerateArray(), skipped =>
            skipped.GetProperty("reason").GetString() == expectedSkippedReason);
    }

    private static string?[] ReadRoomTargets(JsonDocument document) =>
        document.RootElement.GetProperty("actions").EnumerateArray()
            .Select(action => action.GetProperty("newNumber").GetString())
            .ToArray();

    private static ElementInfo Room(
        long id,
        string name,
        string level,
        string zone,
        string number,
        params (string Key, string Value)[] parameters)
    {
        var room = new ElementInfo
        {
            Id = id,
            Name = name,
            Category = "rooms",
            TypeName = "Room",
            Parameters =
            {
                ["Level"] = level,
                ["Department"] = zone,
                ["Number"] = number
            }
        };
        foreach (var (key, value) in parameters)
            room.Parameters[key] = value;
        return room;
    }

    private static RevitClient MakeClient(params ElementInfo[] rooms)
    {
        return new RevitClient(new HttpClient(new RoomsHandler(rooms)) { BaseAddress = new Uri("http://localhost:17839") });
    }

    private sealed class RoomsHandler : HttpMessageHandler
    {
        private readonly ElementInfo[] _rooms;

        public RoomsHandler(ElementInfo[] rooms)
        {
            _rooms = rooms;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/elements" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(ApiResponse<ElementInfo[]>.Ok(_rooms)),
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
