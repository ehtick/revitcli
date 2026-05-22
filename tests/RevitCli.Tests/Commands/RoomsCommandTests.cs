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

    private static ElementInfo Room(long id, string name, string level, string zone, string number)
    {
        return new ElementInfo
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
