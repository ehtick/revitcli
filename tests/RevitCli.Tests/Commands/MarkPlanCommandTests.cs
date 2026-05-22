using System.Net;
using System.Text;
using System.Text.Json;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Numbering;
using RevitCli.Output;
using RevitCli.Plans;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

[Collection("Sequential")]
public sealed class MarkPlanCommandTests : IDisposable
{
    private readonly string _root;
    private readonly string _previousDirectory;

    public MarkPlanCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "revitcli-mark-plan-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task Show_MarkAssignmentPlan_PrintsActions()
    {
        var planPath = WriteSampleMarkPlan();
        var output = new StringWriter();

        var exitCode = await PlanCommand.ExecuteShowAsync(planPath, "json", output);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("mark-assignment-plan.v1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("mark-assignment", json.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Apply_MarkAssignmentPlan_DryRunPreviewsFrozenGroups()
    {
        var planPath = WriteSampleMarkPlan();
        var client = MakeClient(Door(10, "Door A", "D-OLD"), Door(11, "Door B", "D-OLDER"));
        var output = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client,
            planPath,
            yes: false,
            dryRun: true,
            maxChanges: 10,
            output);

        Assert.Equal(0, exitCode);
        Assert.Contains("Dry run: 2 Mark value(s) would be modified from plan.", output.ToString());
    }

    [Fact]
    public async Task Apply_MarkAssignmentPlan_WritesReceiptWithRollbackActions()
    {
        var planPath = WriteSampleMarkPlan();
        var client = MakeClient(Door(10, "Door A", "D-OLD"), Door(11, "Door B", "D-OLDER"));
        var output = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client,
            planPath,
            yes: true,
            dryRun: false,
            maxChanges: 10,
            output);

        Assert.Equal(0, exitCode);
        var receiptPath = planPath + ".receipt.json";
        Assert.True(File.Exists(receiptPath));
        using var json = JsonDocument.Parse(File.ReadAllText(receiptPath));
        var root = json.RootElement;
        Assert.Equal("mark-assignment", root.GetProperty("operation").GetString());
        Assert.Equal("Mark", root.GetProperty("param").GetString());
        Assert.Equal(2, root.GetProperty("rollbackActions").GetArrayLength());
    }

    [Fact]
    public async Task Apply_MarkAssignmentPlan_RejectsStaleOldMarksBeforeWrites()
    {
        var planPath = WriteSampleMarkPlan();
        var client = MakeClient(Door(10, "Door A", "CHANGED"), Door(11, "Door B", "D-OLDER"));
        var output = new StringWriter();

        var exitCode = await PlanCommand.ExecuteApplyAsync(
            client,
            planPath,
            yes: true,
            dryRun: false,
            maxChanges: 10,
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("mark assignment plan is stale", output.ToString());
        Assert.DoesNotContain("Receipt saved", output.ToString());
    }

    private string WriteSampleMarkPlan()
    {
        var planPath = Path.Combine(_root, "marks.plan.json");
        var plan = new MarkAssignmentPlan(
            "mark-assignment-plan.v1",
            "mark-assignment",
            "marks assign",
            DateTime.UtcNow.ToString("o"),
            Environment.UserName,
            true,
            "doors",
            Path.Combine(_root, "marks.yml"),
            "Mark",
            new[] { "level", "zone", "type", "location" },
            new MarkAssignmentPlanSummary(2, 2, 2, 0),
            new[]
            {
                new MarkAssignmentPlanAction(10, "Door A", "doors", "Mark", "D-OLD", "D-001", "L1|A|Single|101"),
                new MarkAssignmentPlanAction(11, "Door B", "doors", "Mark", "D-OLDER", "D-002", "L1|A|Single|102")
            },
            Array.Empty<MarkAssignmentPlanSkipped>(),
            new SetPlanCommands
            {
                Show = $"revitcli plan show \"{planPath}\" --output markdown",
                DryRunApply = $"revitcli plan apply \"{planPath}\" --dry-run",
                Apply = $"revitcli plan apply \"{planPath}\" --yes"
            });
        MarkAssignmentPlanStore.Save(planPath, plan);
        return planPath;
    }

    private static ElementInfo Door(long id, string name, string mark)
    {
        return new ElementInfo
        {
            Id = id,
            Name = name,
            Category = "doors",
            TypeName = "Single Door",
            Parameters =
            {
                ["Mark"] = mark
            }
        };
    }

    private static RevitClient MakeClient(params ElementInfo[] doors)
    {
        return new RevitClient(new HttpClient(new MarkPlanHandler(doors)) { BaseAddress = new Uri("http://localhost:17839") });
    }

    private sealed class MarkPlanHandler : HttpMessageHandler
    {
        private readonly Dictionary<long, ElementInfo> _doors;

        public MarkPlanHandler(ElementInfo[] doors)
        {
            _doors = doors.ToDictionary(door => door.Id);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/elements" && request.Method == HttpMethod.Get)
            {
                return Json(ApiResponse<ElementInfo[]>.Ok(_doors.Values.ToArray()));
            }

            if (request.RequestUri.AbsolutePath == "/api/elements/set" && request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                var set = JsonSerializer.Deserialize<SetRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                var previews = new List<SetPreviewItem>();
                foreach (var id in set.ElementIds ?? new List<long>())
                {
                    if (!_doors.TryGetValue(id, out var door))
                        continue;
                    var oldValue = door.Parameters.TryGetValue(set.Param, out var value) ? value : "";
                    previews.Add(new SetPreviewItem
                    {
                        Id = id,
                        Name = door.Name,
                        OldValue = oldValue,
                        NewValue = set.Value
                    });
                    if (!set.DryRun)
                        door.Parameters[set.Param] = set.Value;
                }

                return Json(ApiResponse<SetResult>.Ok(new SetResult
                {
                    Affected = previews.Count,
                    Preview = previews
                }));
            }

            if (request.RequestUri.AbsolutePath == "/api/status" && request.Method == HttpMethod.Get)
            {
                return Json(ApiResponse<StatusInfo>.Ok(new StatusInfo
                {
                    RevitVersion = "2026",
                    DocumentName = "test.rvt",
                    DocumentPath = "/tmp/test.rvt"
                }));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            };
        }
    }
}
